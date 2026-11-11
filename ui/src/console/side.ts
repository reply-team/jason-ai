import { parseRoute } from '../router';
import { registry, textOf } from '../platform/slots';
import { pendingMark } from '../shell/attention-mark';
import { info } from '../shell/shell';
import { act, define } from './actions';
import { html, type Html } from './html';
import { svg, type IconName } from './icons';
import { openConversation } from './chat';
import { chats, conversations, conversationsFailure, here, type PanelName } from './state';

// The console's sidebar: a new chat, the conversations the desktop keeps, newest first, the console's own panels, and
// the browser shell's screens that the console has no panel of its own for, shown in the page panel.

const panels: readonly { panel: PanelName; label: string; icon: IconName }[] = [
    { panel: 'needs', label: 'Needs you', icon: 'att' },
    { panel: 'campaigns', label: 'Campaigns', icon: 'camp' },
    { panel: 'activity', label: 'Activity', icon: 'pulse' },
    { panel: 'connect', label: 'Connect', icon: 'plug' },
];

/** The browser screens the console's own panels stand in for. */
const ownPanels = new Set(['chat', 'attention', 'campaigns']);

const icons: Readonly<Record<string, IconName>> = {
    people: 'people',
    performance: 'perf',
    workitems: 'work',
    system: 'ops',
    settings: 'gear',
};

define('side.new', () => here().dock.chat());
define('side.chat', ([id]) => {
    if (typeof id !== 'string') return;
    void openConversation(id).then((held) => {
        if (held !== null) here().dock.showChat(held);
    });
});
define('side.show', ([panel]) => {
    if (panel === 'needs' || panel === 'campaigns' || panel === 'activity' || panel === 'connect')
        here().dock.show(panel);
});
define('side.page', ([hash]) => {
    if (typeof hash === 'string') here().dock.page(parseRoute(hash));
});

export function sideView(): Html {
    const marked = pendingMark.value;
    const screens = registry.list('screen').filter((each) => !ownPanels.has(each.id));
    // A long version is cut with an ellipsis at the sidebar's width; the title keeps all of it.
    const version = `Jason ${info.value?.runtime_version ?? '…'}`;
    return html`<div class="brand"><span class="glyph"></span>Jason</div>
        <button class="newbtn" type="button" ${act('side.new')}>${svg('plus', 15)}New chat</button>
        <nav class="list" aria-label="Conversations">
            ${conversations.value.map(
                // A chat the console holds says its title as it is now; the list says it as the desktop kept it.
                (conversation) =>
                    html`<button class="item" type="button" ${act('side.chat', conversation.id)}>
                        ${svg(conversation.campaignId === null ? 'chat' : 'camp', 15)}<span
                            >${chats.value[conversation.id]?.title ?? conversation.title}</span
                        >
                    </button>`,
            )}
        </nav>
        ${conversationsFailure.value !== null ? html`<div class="note am side-note" role="alert">${conversationsFailure.value}</div>` : null}
        <nav class="list" aria-label="Console">
            ${panels.map(
                (each) =>
                    html`<button class="item" type="button" ${act('side.show', each.panel)}>
                        ${svg(each.icon, 15)}<span>${each.label}</span>
                        ${each.panel === 'needs' && marked ? html`<span class="dot w" aria-label="Something waits for you"></span>` : null}
                    </button>`,
            )}
        </nav>
        <nav class="list" aria-label="Pages">
            ${screens.map(
                (each) =>
                    html`<button class="item" type="button" ${act('side.page', each.href())}>
                        ${svg(icons[each.id] ?? 'work', 15)}<span>${textOf(each.label)}</span>
                    </button>`,
            )}
        </nav>
        <div class="sfoot meta" title="${version}">${version}</div>`;
}
