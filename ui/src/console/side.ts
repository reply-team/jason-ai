import { signal } from '@preact/signals-react';
import { shownAgent } from './agent-place';
import { parseRoute, route } from '../router';
import { registry } from '../platform/slots';
import { t } from '../platform/i18n';
import { mode } from '../platform/mode';
import { act, define, menuPointer, systemMenuAfterKey } from './actions';
import { html, type Html } from './html';
import { newAgent } from './new-agent';
import { svg, type IconName } from './icons';
import { openConversation } from './chat';
import { supersede } from './conversation-acts';
import { needsInput } from './needs';
import { countWords, needsCount, type Count } from './needs-model';
import { openPalette, paletteHint, paletteShortcut } from './palette';
import { closeFilterMenu, closeRowMenu, drawnRow, recentView, rowElement, rowMenu } from './recent';
import { activePanel, currentChat, feedId, feedUnseen, here, type PanelName } from './state';
import { updateMark } from '../screens/settings/update-state';

// The console's sidebar, top to bottom: the brand, Search, Dashboard, Needs you with the number of what waits, the Feed,
// People, Agents, Accounts, More (the screens with no row of their own), the two sections AGENTS and CHATS, each with a
// plus that makes one (`recent.ts`), and Settings in the foot. The menu and the sections scroll between the brand and
// the foot, so Settings stays at the foot of the window. Live draws the essentials alone, in the same order: the brand,
// Search, Needs you, the Feed, Accounts, AGENTS, CHATS and Settings. The rows left out are only not drawn: each page
// keeps its route and its code, and Search finds every page in both modes. Demo draws the whole list.

/** Whether the sidebar's menu, More, is open. */
const open = signal<'more' | null>(null);

/** Closes whichever menu is open. */
export function closeMenus(): void {
    open.value = null;
    closeFilterMenu();
    closeRowMenu();
}

/** The address of a screen the browser shell registers, or null when this build has none. */
function pageOf(id: string): string | null {
    return (
        registry
            .list('screen')
            .find((each) => each.id === id)
            ?.href() ?? null
    );
}

/** What More holds: each opens a panel of the console's own or a registered screen in the area. */
const more: readonly { label: string; hint: string; icon: IconName; panel?: PanelName; screen?: string }[] = [
    { label: 'Performance', hint: "Replies and what's working", icon: 'perf', screen: 'performance' },
    { label: 'Activity', hint: 'What happened, line by line', icon: 'pulse', panel: 'activity' },
    { label: 'Work items', hint: 'Work waiting, running and finished', icon: 'work', screen: 'workitems' },
    { label: 'Connect', hint: 'The AI and Reply', icon: 'plug', panel: 'connect' },
    { label: 'System', hint: 'The runtime and this installation', icon: 'ops', screen: 'system' },
];

/** The rows of the sidebar that mark what the area shows; a chat is marked in the sections, or on the Feed's row. */
type Row = 'start' | 'dashboard' | 'needs' | 'people' | 'campaigns' | 'accounts' | 'more' | 'settings';

/** The screens of the browser shell each row stands for; a screen of none of them marks no row. */
const screenRows: Readonly<Record<string, Row>> = {
    attention: 'needs',
    people: 'people',
    campaigns: 'campaigns',
    accounts: 'accounts',
    performance: 'more',
    workitems: 'more',
    system: 'more',
    settings: 'settings',
};

/** The row whose page the area shows, or null while a chat is shown. */
function shownRow(): Row | null {
    const front = activePanel.value;
    switch (front) {
        case 'start':
        case 'dashboard':
        case 'needs':
        case 'campaigns':
            return front;
        case 'activity':
        case 'connect':
            return 'more';
        case 'page':
            // An agent's page is marked in Agents, by its row, as its chat is.
            return shownAgent.value !== null ? null : (screenRows[route.value.screen] ?? null);
        default:
            return null;
    }
}

const marked = (row: Row): Html | null => (shownRow() === row ? html`aria-current="true"` : null);

/** What waits on a person, by the one count: the number Needs you's All shows. */
export function waitingCount(): Count {
    return needsCount(needsInput());
}

define('side.menu', ([which]) => {
    if (which !== 'more') return;
    open.value = open.value === which ? null : which;
});
define('side.close', () => closeMenus());
define('side.search', () => {
    closeMenus();
    openPalette();
});
// The plus over AGENTS makes the agent at the click, and its place opens on its Chat, which asks Start's question
// (`new-agent.ts`); the plus over CHATS makes a chat about no agent.
define('side.start', () => {
    closeMenus();
    void newAgent();
});
define('side.new', () => {
    closeMenus();
    here().dock.chat();
});
define('side.feed', () => {
    const id = feedId.value;
    if (id === null) return;
    void openConversation(id).then((held) => {
        if (held === null) return;
        feedUnseen.value = false;
        here().dock.showChat(held);
    });
});
/** Shows a conversation of either section, as a click on its row does. */
export function openRow(id: string): void {
    supersede();
    void openConversation(id).then((held) => {
        if (held !== null) here().dock.showChat(held);
    });
}

define('side.chat', ([id]) => {
    if (typeof id === 'string') openRow(id);
});

/** Opens an agent with no conversation yet, as the Agents page does: its Chat makes the conversation at the first act. */
export function openAgentRow(id: string): void {
    const row = drawnRow(id);
    supersede();
    here().dock.chat({ id, name: row?.title ?? id });
}

define('side.agent', ([id]) => {
    if (typeof id === 'string') openAgentRow(id);
});

/**
 * The three dots on a row: its menu, under the dots, as the keys open it under the row, and the focus comes back to the
 * dots when it closes. A click on the dots while their menu is open closes it.
 */
define('side.rowdots', ([id], element) => {
    if (typeof id !== 'string') return;
    if (rowMenu.value?.id === id && rowMenu.value.dots === true) {
        closeRowMenu();
        return;
    }
    const row = rowElement(id);
    const box = element.getBoundingClientRect();
    closeMenus();
    rowMenu.value = { id, title: row?.getAttribute('aria-label') ?? '', x: box.left, y: box.bottom, dots: true };
});

/**
 * Opens the menu on a row of either section, and nothing else: the row is neither opened nor shown. A right-click places it at
 * the pointer; the keyboard, Shift+F10 or the menu key, under the row. A menu the keyboard opened on a row already is not
 * opened again by the system's own event that follows the key.
 */
define('side.rowmenu', ([id], element, event) => {
    if (typeof id !== 'string') return;
    if (systemMenuAfterKey(event) && rowMenu.value?.id === id) return;
    const pointer = menuPointer(event);
    const box = element.getBoundingClientRect();
    closeMenus();
    rowMenu.value = {
        id,
        title: element.getAttribute('aria-label') ?? '',
        x: pointer?.x ?? box.left + 12,
        y: pointer?.y ?? box.bottom,
    };
});
/** All agents, under AGENTS' rows: the Agents page, as its own control, so a repaint keeps the focus on it. */
define('side.allAgents', () => {
    closeMenus();
    here().dock.show('campaigns');
});
define('side.show', ([panel]) => {
    closeMenus();
    if (
        panel === 'dashboard' ||
        panel === 'needs' ||
        panel === 'campaigns' ||
        panel === 'activity' ||
        panel === 'connect'
    )
        here().dock.show(panel);
});
define('side.page', ([hash]) => {
    closeMenus();
    if (typeof hash === 'string') here().dock.page(parseRoute(hash));
});

/** Where the build's own files are served from: the runtime serves the build under /ui/. */
const assets = import.meta.env.BASE_URL;

const entry = (title: string, hint: string, action: Html, icon: IconName, off: Html | null = null): Html =>
    html`<button class="menuitem" type="button" role="menuitem" ${action} ${off}>
        ${svg(icon, 15)}<span class="mt">${title}</span><span class="ms">${hint}</span>
    </button>`;

export function sideView(): Html {
    const count = waitingCount();
    const feed = feedId.value;
    const menu = open.value;
    const pageRow = (id: Row, label: string, icon: IconName): Html | null => {
        const href = pageOf(id);
        return href === null
            ? null
            : html`<button class="item" type="button" ${marked(id)} ${act('side.page', href)}>
                  ${svg(icon, 15)}<span>${label}</span>
              </button>`;
    };
    const full = mode.value === 'demo';
    const settings = pageOf('settings');
    // One quiet mark on Settings while the desktop knows of a version it has not installed; no count.
    const newer = updateMark();
    return html`${
            menu === null
                ? null
                : html`<button
                      class="menu-away"
                      type="button"
                      tabindex="-1"
                      aria-hidden="true"
                      ${act('side.close')}
                  ></button>`
        }
        <div class="brand">
            <img class="mark" src="${assets}favicon.svg" alt="" />
            <img class="wordmark light" src="${assets}brand-wordmark.svg" alt="Jason AI, GTM Harness" />
            <img class="wordmark dark" src="${assets}brand-wordmark-dark.svg" alt="Jason AI, GTM Harness" />
            <span class="alpha">Alpha</span>
        </div>
        <div class="sscroll">
            <nav class="list" aria-label="Menu">
                <button class="item find" type="button" aria-keyshortcuts="${paletteShortcut()}" ${act('side.search')}>
                    ${svg('search', 15)}<span>${t('palette.search')}</span><span class="kbd">${paletteHint()}</span>
                </button>
                ${
                    full
                        ? html`<button
                              class="item"
                              type="button"
                              ${marked('dashboard')}
                              ${act('side.show', 'dashboard')}
                          >
                              ${svg('home', 15)}<span>${t('dash.title')}</span>
                          </button>`
                        : null
                }
                <button class="item" type="button" ${marked('needs')} ${act('side.show', 'needs')}>
                    ${svg('needs', 15)}<span>Needs you</span>
                    ${count.n > 0 ? html`<span class="count" aria-label="${countWords(count)} waiting">${countWords(count)}</span>` : null}
                </button>
                ${
                    feed === null
                        ? null
                        : html`<button
                              class="item"
                              type="button"
                              ${feed === currentChat.value ? html`aria-current="true"` : null}
                              ${act('side.feed')}
                          >
                              ${svg('pulse', 15)}<span>Feed</span>
                              ${feedUnseen.value ? html`<span class="dot w" aria-label="New lines in the Feed"></span>` : null}
                          </button>`
                }
                ${full ? pageRow('people', 'People', 'people') : null}
                ${
                    full
                        ? html`<button
                              class="item"
                              type="button"
                              ${marked('campaigns')}
                              ${act('side.show', 'campaigns')}
                          >
                              ${svg('camp', 15)}<span>Agents</span>
                          </button>`
                        : null
                }
                ${pageRow('accounts', 'Accounts', 'plug')}
                ${
                    full
                        ? html`<div class="menuwrap">
                              <button
                                  class="item"
                                  type="button"
                                  aria-haspopup="menu"
                                  aria-expanded="${menu === 'more' ? 'true' : 'false'}"
                                  ${marked('more')}
                                  ${act('side.menu', 'more')}
                              >
                                  ${svg('more', 15)}<span>More</span>
                              </button>
                              ${
                                  menu === 'more'
                                      ? html`<div class="menu" role="menu" aria-label="More">
                                            ${more.map((each) => {
                                                const href = each.screen === undefined ? null : pageOf(each.screen);
                                                if (each.screen !== undefined && href === null) return null;
                                                return entry(
                                                    each.label,
                                                    each.hint,
                                                    each.panel !== undefined
                                                        ? act('side.show', each.panel)
                                                        : act('side.page', href ?? ''),
                                                    each.icon,
                                                );
                                            })}
                                        </div>`
                                      : null
                              }
                          </div>`
                        : null
                }
            </nav>
            ${recentView()}
        </div>
        ${
            settings === null
                ? null
                : html`<div class="sfoot">
                      <button class="item" type="button" ${marked('settings')} ${act('side.page', settings)}>
                          ${svg('gear', 15)}<span>Settings</span>
                          ${newer === null ? null : html`<span class="dot w" role="img" aria-label="${t('settings.update.newer', { version: newer })}" title="${t('settings.update.newer', { version: newer })}"></span>`}
                      </button>
                  </div>`
        }`;
}
