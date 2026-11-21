import { computed, effect, signal, untracked } from '@preact/signals-react';
import type { Api } from '../api/client';
import type { Campaign, CampaignSummary } from '../api/types';
import { act, define, parseArgs } from './actions';
import { agentFacts } from './agent-facts';
import { shownAgent } from './agent-place';
import { campaigns, read } from './campaigns';
import { agentMaking } from './new-agent';
import { offUnless } from './off';
import { html, type Html } from './html';
import { t } from '../platform/i18n';
import { svg } from './icons';
import { needsInput } from './needs';
import {
    agentRows,
    chatRows,
    pageSize,
    recentFilters,
    shownRows,
    type RecentFilter,
    type RecentInput,
    type RecentRow,
} from './recent-rows';
import { chats, clock, conversations, currentChat } from './state';

// The sidebar's two sections, AGENTS above CHATS, each a header with a plus at its right. AGENTS: every agent that is
// not archived, each with a dot, its title, what waits on the person and its age, a filter in the header, and All agents
// under the rows. CHATS: the conversations about no agent, the newest first, ten at a time, each its title and age.
// Everything here is read from what the console already holds, but the state of the agent of a conversation the list of
// agents does not hold, which is read on its own, so that a conversation whose agent is archived leaves AGENTS; the
// state below is the page's own and is kept nowhere.

/**
 * The agents of the conversations that the list of agents does not hold, as one key: an archived agent, which that list
 * leaves out, or one past its first page. Empty until the list is read.
 */
const unlisted = computed(() => {
    if (!read.value) return '';
    const listed = new Set(campaigns.value.map((each) => each.id));
    const ids = new Set<string>();
    for (const conversation of conversations.value)
        if (conversation.campaignId !== null && conversation.main !== true && !listed.has(conversation.campaignId))
            ids.add(conversation.campaignId);
    return [...ids].sort().join(' ');
});

/**
 * The agents read here on their own, by id: their state alone, by `campaign.get`, never the count of people or the work
 * the agent's header reads (`agent-facts.ts`). Emptied each time the console binds the reads below.
 */
const readAlone = signal<Readonly<Record<string, CampaignSummary>>>({});

/** Reads one agent's state; an agent the runtime does not know, or a read that failed, leaves nothing said of it. */
async function readState(api: Api, id: string): Promise<void> {
    try {
        const { name, status } = await api.call<Campaign>('campaign.get', { campaign_id: id });
        readAlone.value = { ...readAlone.value, [id]: { id, name, status } };
    } catch {
        // Its conversation stays, as one whose agent is not held.
    }
}

/**
 * An agent the list of agents does not hold, as it was read last, here or by its header: archived where either read
 * says so, since an archive is not undone.
 */
function stateOf(id: string): CampaignSummary | undefined {
    const alone = readAlone.value[id];
    const facts = agentFacts.value[id]?.campaign;
    return alone?.status === 'archived' ? alone : (facts ?? alone);
}

/**
 * Reads the state of each agent that comes to be among the agents of the conversations the list of agents does not
 * hold, and of no other: so an agent the list stops holding, archived anywhere, is read once the journal has the list
 * read again, and one already among them is not read again. One known as archived is not read, since an archive is not
 * undone. Bound while the console is; the function it returns stops it.
 */
export function readUnlisted(api: Api): () => void {
    readAlone.value = {};
    let before = new Set<string>();
    return effect(() => {
        const key = unlisted.value;
        const now = new Set(key === '' ? [] : key.split(' '));
        const fresh = [...now].filter((id) => !before.has(id));
        before = now;
        untracked(() => {
            for (const id of fresh) if (stateOf(id)?.status !== 'archived') void readState(api, id);
        });
    });
}

/** AGENTS' filter entry in force, whether its menu is open, and how many rows of CHATS "Load more" has uncovered. */
const filter = signal<RecentFilter>('all');
const menuOpen = signal(false);
const limit = signal(pageSize);

/** Puts the filter and the paging back as a fresh window has them. */
export function resetRecent(): void {
    filter.value = 'all';
    menuOpen.value = false;
    limit.value = pageSize;
}

/** Closes the filter's menu. */
export function closeFilterMenu(): void {
    menuOpen.value = false;
}

/**
 * The menu open on a row (`row-menu.tsx`): the row's key (its conversation, or an agent with none yet), its title, and
 * where the menu stands in the window.
 */
export interface RowMenuAt {
    id: string;
    title: string;
    x: number;
    y: number;
}

export const rowMenu = signal<RowMenuAt | null>(null);

/** Closes the menu on a row. */
export function closeRowMenu(): void {
    rowMenu.value = null;
}

/** The row of the conversation, where it is drawn. */
export function rowElement(id: string): HTMLElement | undefined {
    const rows = document.querySelectorAll<HTMLElement>('.side .recent-row');
    return [...rows].find((each) => parseArgs(each.dataset['args'])[0] === id);
}

/** Gives the focus to the row of the conversation, where it is drawn. */
export function focusRow(id: string): void {
    rowElement(id)?.focus();
}

const labels: Readonly<Record<RecentFilter, string>> = {
    all: t('recent.filter.all'),
    needs: t('recent.filter.needs'),
    live: t('recent.filter.live'),
    paused: t('recent.filter.paused'),
    draft: t('recent.filter.draft'),
};

const dotTitles = {
    waiting: t('recent.dot.waiting'),
    active: t('recent.dot.active'),
    paused: t('recent.dot.paused'),
    draft: t('recent.dot.draft'),
    none: t('recent.dot.none'),
} as const;

define('recent.menu', () => {
    menuOpen.value = !menuOpen.value;
});
define('recent.filter', ([value]) => {
    const chosen = recentFilters.find((each) => each === value);
    if (!chosen) return;
    filter.value = chosen;
    limit.value = pageSize;
    closeFilterMenu();
});
define('recent.more', () => {
    limit.value += pageSize;
});

/** The dot's title: its state when the page holds one, "No agent" only for a chat about no campaign, else none. */
function dotTitle(row: RecentRow): Html | null {
    if (row.dot === 'none' && row.aboutAgent) return null;
    return html`title="${dotTitles[row.dot]}"`;
}

/** Whether the row is the agent the area shows, on whichever of its tabs, or the chat it shows. */
function inFront(row: RecentRow): boolean {
    if (row.conversationId !== null && row.conversationId === currentChat.value) return true;
    const agent = shownAgent.value;
    return agent !== null && row.campaignId === agent.id;
}

function rowView(row: RecentRow): Html {
    const menu = rowMenu.value?.id === row.id;
    const agent = row.campaignId !== null;
    const said = [
        agent && row.waiting > 0 ? t('recent.waiting', { count: row.waitingWords }) : null,
        row.age === '' ? null : row.age,
        row.agent === null ? null : dotTitles[row.dot],
    ]
        .filter((each) => each !== null)
        .join(' · ');
    const more = t('side.rowMenu', { name: row.title });
    return html`<div class="rrow${menu ? ' menu-open' : ''}">
        <button
            class="item recent-row${menu ? ' menu-open' : ''}"
            type="button"
            aria-label="${row.title}"
            aria-description="${said}"
            ${inFront(row) ? html`aria-current="true"` : null}
            aria-haspopup="menu"
            aria-expanded="${menu ? 'true' : 'false'}"
            ${row.conversationId === null ? act('side.agent', row.id) : act('side.chat', row.id)}
            data-menu="side.rowmenu"
        >
            ${
                agent
                    ? html`<span
                          class="rdot ${row.dot}${row.running ? ' running' : ''}"
                          ${dotTitle(row)}
                          aria-hidden="true"
                      ></span>`
                    : null
            }
            <span class="rt">${row.title}</span>
            ${agent && row.waiting > 0 ? html`<span class="count" aria-hidden="true">${row.waitingWords}</span>` : null}
            <span class="t" aria-hidden="true">${row.age}</span>
        </button>
        <button
            class="rdots"
            type="button"
            title="${more}"
            aria-label="${more}"
            aria-haspopup="menu"
            aria-expanded="${menu ? 'true' : 'false'}"
            ${act('side.rowdots', row.id)}
        >
            ${svg('more', 14)}
        </button>
    </div>`;
}

function filterMenu(): Html {
    return html`<button class="menu-away" type="button" tabindex="-1" aria-hidden="true" ${act('recent.menu')}></button>
        <div class="menu" role="menu" aria-label="${t('recent.show')}">
            <div class="mhead">${t('recent.show')}</div>
            ${recentFilters.map(
                (each) =>
                    html`<button
                        class="menuitem"
                        type="button"
                        role="menuitemradio"
                        aria-checked="${filter.value === each ? 'true' : 'false'}"
                        ${act('recent.filter', each)}
                    >
                        ${filter.value === each ? svg('check', 15) : html`<span></span>`}<span class="mt"
                            >${labels[each]}</span
                        >
                    </button>`,
            )}
        </div>`;
}

/** The two sections' rows, and the ones drawn now: AGENTS under its filter, CHATS by the paging. */
const listed = computed(() => {
    const input: RecentInput = {
        conversations: conversations.value,
        titles: (id) => chats.value[id]?.title,
        running: (id) => chats.value[id]?.running,
        campaigns: campaigns.value,
        agentRead: stateOf,
        ...needsInput(),
        now: clock.value,
    };
    const agents = agentRows(input);
    const talks = chatRows(input);
    const drawn = shownRows(talks, 'all', limit.value);
    return {
        agents,
        // AGENTS shows its first ten under All, and every agent a filter keeps; All agents opens the rest on their page.
        agentsShown: shownRows(agents, filter.value, pageSize).rows,
        chats: talks,
        chatsShown: drawn.rows,
        more: drawn.more,
    };
});

/** The row of a key as it is drawn now, in either section. */
export function drawnRow(id: string): RecentRow | undefined {
    const { agentsShown, chatsShown } = listed.value;
    return agentsShown.find((each) => each.id === id) ?? chatsShown.find((each) => each.id === id);
}

/** Whether the row of the key is drawn now. */
export function rowDrawn(id: string): boolean {
    return drawnRow(id) !== undefined;
}

/** A section's header: its name, then its tools and the plus at its right. */
function header(name: string, tools: Html | null, plus: Html): Html {
    return html`<div class="recent">
        <span class="rl">${name}</span>
        <span class="rtools">${tools}${plus}</span>
        ${tools !== null && menuOpen.value ? filterMenu() : null}
    </div>`;
}

/** AGENTS above CHATS. */
export function recentView(): Html {
    const { agents, agentsShown, chats: talks, chatsShown, more } = listed.value;
    const filterButton = html`<button
        class="rbtn"
        type="button"
        title="${t('recent.filter')}"
        aria-label="${t('recent.filter')}"
        aria-haspopup="menu"
        aria-expanded="${menuOpen.value ? 'true' : 'false'}"
        data-active="${filter.value === 'all' ? 'false' : 'true'}"
        ${act('recent.menu')}
    >
        ${svg('filter', 14)}
    </button>`;
    const newAgent = html`<button
        class="rbtn plus"
        type="button"
        title="${t('side.newAgent')}"
        aria-label="${t('side.newAgent')}"
        ${act('side.start')}
        ${offUnless('chat.createAgent') ?? (agentMaking.value ? html`disabled aria-busy="true"` : null)}
    >
        ${svg('plus', 14)}
    </button>`;
    const newChat = html`<button
        class="rbtn plus"
        type="button"
        title="${t('side.newChat')}"
        aria-label="${t('side.newChat')}"
        ${act('side.new')}
        ${offUnless('chat.create')}
    >
        ${svg('plus', 14)}
    </button>`;
    // Said once the list of agents is read: before that, no agent is not yet known.
    const agentsEmpty =
        agentsShown.length > 0 || !read.value
            ? null
            : html`<div class="rnone">${agents.length === 0 ? t('side.none.agents') : t('recent.none.match')}</div>`;
    // One landmark for both sections, each a named group of its own under its header.
    return html`<nav class="sections" aria-label="Conversations">
        ${header(t('side.agents'), filterButton, newAgent)}
        <div class="list" role="group" aria-label="${t('side.agents')}">
            ${agentsShown.map(rowView)}
            <button class="item rmore" type="button" ${act('side.show', 'campaigns')}>
                <span>${t('side.allAgents')}</span>
            </button>
        </div>
        ${agentsEmpty} ${header(t('side.chats'), null, newChat)}
        <div class="list" role="group" aria-label="${t('side.chats')}">
            ${chatsShown.map(rowView)}
            ${
                more
                    ? html`<button class="item rmore" type="button" ${act('recent.more')}>
                          <span>${t('recent.more')}</span>
                      </button>`
                    : null
            }
        </div>
        ${talks.length > 0 ? null : html`<div class="rnone">${t('side.none.chats')}</div>`}
    </nav>`;
}
