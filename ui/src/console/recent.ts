import { computed, effect, signal, untracked } from '@preact/signals-react';
import type { Api } from '../api/client';
import type { Campaign, CampaignSummary } from '../api/types';
import { act, argsOf, define, parseArgs } from './actions';
import { agentFacts } from './agent-facts';
import { shownAgent } from './agent-place';
import { campaigns, read } from './campaigns';
import { html, type Html } from './html';
import { t } from '../platform/i18n';
import { svg } from './icons';
import { needsInput } from './needs';
import { pageSize, recentFilters, recentRows, shownRows, type RecentFilter, type RecentRow } from './recent-rows';
import { chats, clock, conversations, currentChat } from './state';

// "Recent" in the sidebar: the conversations, each with a dot, its title, what waits on the person and its age, a search
// box and a filter in the header, ten rows at a time. Everything here is read from what the console already holds, but
// the state of the agent of a conversation the list of agents does not hold, which is read on its own, so that a
// conversation whose agent is archived leaves Recent; the state below is the page's own and is kept nowhere.

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

/** Whether the search box is drawn, and what is typed in it. */
const searching = signal(false);
const text = signal('');
/** The filter entry in force, whether its menu is open, and how many rows "Load more" has uncovered. */
const filter = signal<RecentFilter>('all');
const menuOpen = signal(false);
const limit = signal(pageSize);

/** Puts the search, the filter and the paging back as a fresh window has them. */
export function resetRecent(): void {
    searching.value = false;
    text.value = '';
    filter.value = 'all';
    menuOpen.value = false;
    limit.value = pageSize;
}

/** Closes the filter's menu. */
export function closeFilterMenu(): void {
    menuOpen.value = false;
}

/** The menu open on a row (`row-menu.tsx`): the row's conversation, its title, and where the menu stands in the window. */
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

define('recent.search', (_args, element) => {
    searching.value = !searching.value;
    if (!searching.value) {
        text.value = '';
        return;
    }
    closeFilterMenu();
    element.closest('.side')?.querySelector<HTMLInputElement>('[data-input="recent.type"]')?.focus();
});
define('recent.type', (_args, element) => {
    if (element instanceof HTMLInputElement) text.value = element.value;
});
define('recent.close', () => {
    searching.value = false;
    text.value = '';
});
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

/** Whether the row is the conversation of the agent the area shows, on whichever of its tabs. */
function agentsRow(id: string): boolean {
    const agent = shownAgent.value;
    return (
        agent !== null &&
        conversations.value.some((each) => each.id === id && !each.main && each.campaignId === agent.id)
    );
}

function rowView(row: RecentRow): Html {
    const menu = rowMenu.value?.id === row.id;
    const said = [
        row.waiting > 0 ? t('recent.waiting', { count: row.waitingWords }) : null,
        row.age === '' ? null : row.age,
        row.agent === null ? null : dotTitles[row.dot],
    ]
        .filter((each) => each !== null)
        .join(' · ');
    return html`<button
        class="item recent-row${menu ? ' menu-open' : ''}"
        type="button"
        aria-label="${row.title}"
        aria-description="${said}"
        ${row.id === currentChat.value || agentsRow(row.id) ? html`aria-current="true"` : null}
        aria-haspopup="menu"
        aria-expanded="${menu ? 'true' : 'false'}"
        ${act('side.chat', row.id)}
        data-menu="side.rowmenu"
    >
        <span class="rdot ${row.dot}${row.running ? ' running' : ''}" ${dotTitle(row)} aria-hidden="true"></span>
        <span class="rt">${row.title}</span>
        ${row.waiting > 0 ? html`<span class="count" aria-hidden="true">${row.waitingWords}</span>` : null}
        <span class="t" aria-hidden="true">${row.age}</span>
    </button>`;
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

/** Every row of Recent, and the ones drawn now under the filter, the search and the paging. */
const listed = computed(() => {
    const rows = recentRows({
        conversations: conversations.value,
        titles: (id) => chats.value[id]?.title,
        running: (id) => chats.value[id]?.running,
        campaigns: campaigns.value,
        agentRead: stateOf,
        ...needsInput(),
        now: clock.value,
    });
    const drawn = shownRows(rows, filter.value, text.value, limit.value);
    return { rows, shown: drawn.rows, more: drawn.more };
});

/** Whether the conversation's row is drawn now. */
export function rowDrawn(id: string): boolean {
    return listed.value.shown.some((each) => each.id === id);
}

/** The header, the search box when it is open, and the list of conversations. */
export function recentView(): Html {
    const { rows, shown, more } = listed.value;
    return html`<div class="recent">
            <span class="rl">${t('recent.title')}</span>
            <span class="rtools">
                <button
                    class="rbtn"
                    type="button"
                    title="${t('recent.search')}"
                    aria-label="${t('recent.search')}"
                    aria-pressed="${searching.value ? 'true' : 'false'}"
                    ${act('recent.search')}
                >
                    ${svg('search', 14)}
                </button>
                <button
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
                </button>
            </span>
            ${menuOpen.value ? filterMenu() : null}
        </div>
        ${
            searching.value
                ? html`<div class="rsearch">
                      <input
                          class="inp"
                          type="text"
                          aria-label="${t('recent.search')}"
                          placeholder="${t('recent.search')}"
                          autocomplete="off"
                          value="${text.value}"
                          data-input="recent.type"
                          data-escape="recent.close"
                          ${argsOf()}
                      />
                  </div>`
                : null
        }
        <nav class="list" aria-label="Conversations">
            ${shown.map(rowView)}
            ${
                more
                    ? html`<button class="item rmore" type="button" ${act('recent.more')}>
                          <span>${t('recent.more')}</span>
                      </button>`
                    : null
            }
        </nav>
        ${
            shown.length > 0
                ? null
                : html`<div class="rnone">${rows.length === 0 ? t('recent.none.chats') : t('recent.none.match')}</div>`
        }`;
}
