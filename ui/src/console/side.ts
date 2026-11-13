import { signal } from '@preact/signals-react';
import { parseRoute, route } from '../router';
import { registry } from '../platform/slots';
import { act, define } from './actions';
import { html, type Html } from './html';
import { svg, type IconName } from './icons';
import { openConversation } from './chat';
import { attention } from './needs';
import { openPalette, paletteHint, paletteShortcut } from './palette';
import { t } from '../platform/i18n';
import { activePanel, chats, conversations, currentChat, feedId, feedUnseen, here, type PanelName } from './state';

// The console's sidebar, top to bottom: the brand, the button that makes an agent (and the caret's menu), Search, the Feed,
// Needs you with the number of what waits, People, Agents, Accounts, More (the screens with no row of their own), the
// conversations under "Recent", and Settings in the foot in the foot.

/** Which of the sidebar's two menus is open. */
const open = signal<'create' | 'more' | null>(null);

/** Closes whichever menu is open. */
export function closeMenus(): void {
    open.value = null;
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

/** What More holds: each opens a panel of the console's own or a registered screen in the page panel. */
const more: readonly { label: string; hint: string; icon: IconName; panel?: PanelName; screen?: string }[] = [
    { label: 'Performance', hint: "Replies and what's working", icon: 'perf', screen: 'performance' },
    { label: 'Activity', hint: 'What happened, line by line', icon: 'pulse', panel: 'activity' },
    { label: 'Work items', hint: 'Work waiting, running and finished', icon: 'work', screen: 'workitems' },
    { label: 'Connect', hint: 'The AI and Reply', icon: 'plug', panel: 'connect' },
    { label: 'System', hint: 'The runtime and this installation', icon: 'ops', screen: 'system' },
];

/** More is active while one of its screens is in front. */
function moreActive(): boolean {
    const front = activePanel.value;
    if (front === 'activity' || front === 'connect') return true;
    return front === 'page' && ['performance', 'workitems', 'system'].includes(route.value.screen);
}

/** How many approvals and questions wait on a person: the number the Needs you panel shows. */
export function waitingCount(): number {
    return attention.approvals.value.length + attention.questions.value.length;
}

define('side.menu', ([which]) => {
    if (which !== 'create' && which !== 'more') return;
    open.value = open.value === which ? null : which;
});
define('side.close', () => closeMenus());
define('side.search', () => {
    closeMenus();
    openPalette();
});
// Start is made or brought forward as the window does it; no chat is made until the person sends one.
define('side.start', () => {
    closeMenus();
    here().dock.show('start');
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
define('side.chat', ([id]) => {
    if (typeof id !== 'string') return;
    void openConversation(id).then((held) => {
        if (held !== null) here().dock.showChat(held);
    });
});
define('side.show', ([panel]) => {
    closeMenus();
    if (panel === 'needs' || panel === 'campaigns' || panel === 'activity' || panel === 'connect')
        here().dock.show(panel);
});
define('side.page', ([hash]) => {
    closeMenus();
    if (typeof hash === 'string') here().dock.page(parseRoute(hash));
});

/** Where the build's own files are served from: the runtime serves the build under /ui/. */
const assets = import.meta.env.BASE_URL;

const entry = (title: string, hint: string, action: Html, icon: IconName): Html =>
    html`<button class="menuitem" type="button" role="menuitem" ${action}>
        ${svg(icon, 15)}<span class="mt">${title}</span><span class="ms">${hint}</span>
    </button>`;

export function sideView(): Html {
    const count = waitingCount();
    const feed = feedId.value;
    const menu = open.value;
    const pageRow = (id: string, label: string, icon: IconName): Html | null => {
        const href = pageOf(id);
        return href === null
            ? null
            : html`<button class="item" type="button" ${act('side.page', href)}>
                  ${svg(icon, 15)}<span>${label}</span>
              </button>`;
    };
    const accounts = pageOf('accounts');
    const settings = pageOf('settings');
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
        <div class="menuwrap create">
            <div class="newrow">
                <button class="newbtn" type="button" ${act('side.start')}>${svg('plusc', 15)}New GTM agent</button>
                <button
                    class="caret"
                    type="button"
                    aria-label="More to create"
                    title="Create an agent or a chat, or connect an account"
                    aria-haspopup="menu"
                    aria-expanded="${menu === 'create' ? 'true' : 'false'}"
                    ${act('side.menu', 'create')}
                >
                    ${svg('chev', 15)}
                </button>
            </div>
            ${
                menu === 'create'
                    ? html`<div class="menu" role="menu" aria-label="Create">
                          ${entry('New GTM agent', 'Plan and run outreach', act('side.start'), 'plus')}
                          ${entry('New chat', 'Talk with Jason, with no agent yet', act('side.new'), 'chat')}
                          ${
                              accounts === null
                                  ? null
                                  : entry(
                                        'Connect an account',
                                        'Link the accounts Jason works through',
                                        act('side.page', accounts),
                                        'plug',
                                    )
                          }
                      </div>`
                    : null
            }
        </div>
        <nav class="list" aria-label="Menu">
            <button class="item" type="button" aria-keyshortcuts="${paletteShortcut()}" ${act('side.search')}>
                ${svg('search', 15)}<span>${t('palette.search')}</span><span class="kbd">${paletteHint()}</span>
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
            <button class="item" type="button" ${act('side.show', 'needs')}>
                ${svg('att', 15)}<span>Needs you</span>
                ${count > 0 ? html`<span class="count" aria-label="${count} waiting">${count}</span>` : null}
            </button>
            ${pageRow('people', 'People', 'people')}
            <button class="item" type="button" ${act('side.show', 'campaigns')}>
                ${svg('camp', 15)}<span>Agents</span>
            </button>
            ${pageRow('accounts', 'Accounts', 'plug')}
            <div class="menuwrap">
                <button
                    class="item"
                    type="button"
                    aria-haspopup="menu"
                    aria-expanded="${menu === 'more' ? 'true' : 'false'}"
                    ${moreActive() ? html`aria-current="true"` : null}
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
            </div>
        </nav>
        <div class="recent">Recent</div>
        <nav class="list" aria-label="Conversations">
            ${conversations.value
                .filter((conversation) => conversation.main !== true)
                .map(
                    // A chat the console holds says its title as it is now; the list says it as the desktop kept it.
                    (conversation) =>
                        html`<button
                            class="item"
                            type="button"
                            ${conversation.id === currentChat.value ? html`aria-current="true"` : null}
                            ${act('side.chat', conversation.id)}
                        >
                            ${svg(conversation.campaignId === null ? 'chat' : 'camp', 15)}<span
                                >${chats.value[conversation.id]?.title ?? conversation.title}</span
                            >
                        </button>`,
                )}
        </nav>
        ${
            settings === null
                ? null
                : html`<div class="sfoot">
                      <button class="item" type="button" ${act('side.page', settings)}>
                          ${svg('gear', 15)}<span>Settings</span>
                      </button>
                  </div>`
        }`;
}
