import { effect, signal, untracked } from '@preact/signals-react';
import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { HttpApi, type Api } from '../api/client';
import { ApiProvider } from '../api/context';
import { session } from '../api/session';
import { CurrentPage } from '../app';
import { Notice } from '../layout/notice';
import { DemoView } from '../platform/demo-frame';
import { can, earlier, placeKind } from '../platform/places';
import type { DesktopCommand, DesktopLink, JasonDesktop } from '../platform/desktop';
import { pageStream, type JournalStream } from '../platform/journal-stream';
import { consoleShell } from '../platform/console-shell';
import { registry } from '../platform/slots';
import { LiveSettingsSections } from './settings-sections';
import { href, navigate, parseRoute, replace, route, type Route } from '../router';
import { AgentHeaded, CampaignChat } from '../screens/campaigns/campaign-chat';
import { ProviderAccountsContext } from '../screens/accounts/provider-card';
import { ModeStrip } from './mode-strip';
import { OpenIn } from './open-in';
import { PairAgain } from '../shell/pair-again';
import * as shell from '../shell/shell';
import { activityView } from './activity';
import { agentFacts, goneAgents, readAgent } from './agent-facts';
import { AgentHead } from './agent-head';
import { agentChatOf, agentChatRoute, agentOfChat, namesPlace, shownAgent } from './agent-place';
import { listen } from './actions';
import {
    askFeed,
    catchUpFeed,
    ChatPanel,
    handLine,
    isUnmade,
    onChatMade,
    openConversation,
    reloadConversations,
    retireShadowed,
    startChat,
    unmadeChat,
} from './chat';
import { received } from './chat-model';
import * as campaigns from './campaigns';
import * as connect from './connect';
import * as dashboard from './dashboard';
import { linkPlace } from './links';
import * as needs from './needs';
import { reloadWaiting } from './waiting';
import { heardLine, readActsAgain } from './card-acts';
import { followStanding } from '../platform/full-access';
import { Painted } from './painted';
import { Palette } from './palette';
import { closeMenus, sideView } from './side';
import { StartPanel } from './start';
import {
    activePanel,
    bind,
    changeChat,
    chatOf,
    chats,
    clock,
    conversations,
    conversationsFailure,
    currentChat,
    feedId,
    feedUnseen,
    here,
    keepLine,
    streamState,
    type Dock,
    type PanelName,
} from './state';

// Loading the console is what makes this page the console: the classic shell never loads this module, so the parts that
// are the console's alone (Live's essentials, the extra Settings sections) are drawn nowhere else.
consoleShell.loaded = true;
consoleShell.settingsSections = LiveSettingsSections;

// The desktop's shell: a sidebar and one area. The sidebar's menu, its Recent and the palette are the only way about, and
// the area shows exactly one thing: Start, one chat, one of the panels the console draws itself from the Runtime API, or
// one of the browser shell's screens. Nothing is closed, only chosen; every window opens on Start.

/** What a campaign's Chat on the browser shell's screens does: shows the campaign's one conversation, made on first
 *  use; with no campaign, a new set-up chat. One function for every render, so the pages it is handed to keep it. */
function chatAbout(campaign?: { id: string; name: string }): void {
    here().dock.chat(campaign);
}

/** What the Accounts page is handed to draw the provider's accounts: the Reply card, as the Connect panel acts on it. */
const providerAccounts = { view: connect.accountsReplyView, gone: connect.accountsReplyGone };

/**
 * What an agent's Chat tab says in place of a conversation: that the desktop could not open it, and why, until it opens;
 * or that the agent is archived and has none, where nothing can be made.
 */
const chatNote = signal<{ agent: string; why: string } | { agent: string; archived: true } | null>(null);

/** An agent's Chat tab whose conversation could not be opened: what the desktop said, and a way to try again. */
function ChatUnopened({ agent, why }: { agent: string; why: string }) {
    return (
        <Notice danger>
            This agent’s conversation could not be opened. {why}{' '}
            <button type="button" className="btn sm" onClick={() => here().dock.page(agentChatRoute(agent))}>
                Try again
            </button>
        </Notice>
    );
}

function PagePanel() {
    // An agent's Chat is the console's own pane: while the address names it, the page draws nothing behind it but the
    // word that its conversation could not be opened, or that the agent is archived and has none.
    const chatOf = can('chat.create') ? agentChatOf(route.value) : null;
    const note = chatNote.value;
    return (
        <div className="harness">
            <div className="app chromeless">
                <div className="content">
                    {chatOf === null ? (
                        <CampaignChat.Provider value={chatAbout}>
                            <AgentHeaded.Provider value={true}>
                                <ProviderAccountsContext.Provider value={providerAccounts}>
                                    <CurrentPage />
                                </ProviderAccountsContext.Provider>
                            </AgentHeaded.Provider>
                        </CampaignChat.Provider>
                    ) : (
                        note !== null &&
                        note.agent === chatOf &&
                        ('why' in note ? (
                            <ChatUnopened agent={note.agent} why={note.why} />
                        ) : (
                            <Notice status>This agent is archived.</Notice>
                        ))
                    )}
                </div>
            </div>
        </div>
    );
}

function DashboardPanel({ inSight }: { inSight: boolean }) {
    useEffect(() => {
        dashboard.watching(true);
        return () => dashboard.watching(false);
    }, []);
    // Out of sight it reads nothing on the journal's word; shown again, it reads what it missed.
    useEffect(() => {
        dashboard.sighted(inSight);
        return () => dashboard.sighted(false);
    }, [inSight]);
    return <Painted className="painted" view={dashboard.dashboardView} />;
}

/** Needs you, whose rows are read whole while it is drawn, so that an approval is approved only once it has been. */
function NeedsPanel() {
    useEffect(() => needs.drawNeeds(), []);
    return <Painted className="painted" view={needs.needsView} />;
}

const panels: Readonly<Record<PanelName, (inSight: boolean) => ReactNode>> = {
    start: () => <StartPanel />,
    dashboard: (inSight) => <DashboardPanel inSight={inSight} />,
    needs: () => <NeedsPanel />,
    campaigns: () => <Painted className="painted" view={campaigns.campaignsView} />,
    activity: () => <Painted className="painted" view={activityView} />,
    connect: () => (
        <Painted className="painted" view={connect.connectView} onGone={() => connect.dropDialogs('connect')} />
    ),
    page: () => <PagePanel />,
};

/** The panels once drawn for a narrow column, which the wide area gives a measure of their own. */
const measured = new Set<string>(['needs', 'campaigns', 'activity']);

/**
 * What the area holds, in the order each was first shown: a panel's name or `chat:<id>`. Only the one shown is in sight;
 * the others are drawn out of sight. The panels are a fixed few; of the chats only the ones shown last are kept drawn.
 * A chat's record holds what it had (what was typed and not sent, what arrived meanwhile, a message that waits), so a
 * chat drawn afresh when it is chosen again has all of it.
 */
const inArea = signal<readonly string[]>([]);

/** How many chats stay drawn: the one shown and the two shown before it. */
const chatsDrawn = 3;

/** The chats' panes, the one shown last first. */
let lastChats: string[] = [];

const chatPane = (id: string) => `chat:${id}`;

/** Shows one thing in the area, and marks it for the sidebar. */
function reveal(pane: string): void {
    let held = inArea.value.includes(pane) ? inArea.value : [...inArea.value, pane];
    if (pane.startsWith('chat:')) {
        lastChats = [pane, ...lastChats.filter((each) => each !== pane)].slice(0, chatsDrawn);
        held = held.filter((each) => !each.startsWith('chat:') || lastChats.includes(each));
    }
    if (held.length !== inArea.value.length || held.some((each, at) => each !== inArea.value[at])) inArea.value = held;
    activePanel.value = pane;
    currentChat.value = pane.startsWith('chat:') ? pane.slice('chat:'.length) : null;
    if (currentChat.value !== null && currentChat.value === feedId.value) feedUnseen.value = false;
}

/** One thing the area holds: in sight while it is the one shown, else hidden as it was left. */
function Pane({ pane }: { pane: string }) {
    const chatId = pane.startsWith('chat:') ? pane.slice('chat:'.length) : null;
    // Its own chat alone is read here, so a word typed in the chat shown draws no other.
    const kept = chatId === null || chatOf(chatId).value !== undefined;
    const inSight = pane === activePanel.value;
    return (
        <section className={measured.has(pane) ? 'pane measure' : 'pane'} data-pane={pane} hidden={!inSight}>
            {chatId === null ? (
                <PanelBody name={pane as PanelName} inSight={inSight} />
            ) : (
                kept && <ChatPanel chatId={chatId} headed />
            )}
        </section>
    );
}

function PanelBody({ name, inSight }: { name: PanelName; inSight: boolean }) {
    return panels[name](inSight);
}

/** The one area: every thing it holds is drawn, and all but the one shown are hidden. */
function Area() {
    const agent = shownAgent.value;
    return (
        <main className={agent === null ? 'area' : 'area agent-shown'}>
            {/* One header for every place of the agent shown, kept while the person moves between its tabs. */}
            {agent !== null && <AgentHead key={agent.id} id={agent.id} tab={agent.tab} />}
            {inArea.value.map((pane) => (
                <Pane key={pane} pane={pane} />
            ))}
        </main>
    );
}

// A command names a panel of the console's own, or a screen of the browser shell by its id. A screen's address is the
// one its registered entry gives, read when the command comes, so each screen's route is defined in one place.
const commands: Partial<Record<DesktopCommand, { panel: PanelName } | { screen: Route['screen'] }>> = {
    'navigate.attention': { panel: 'needs' },
    'navigate.campaigns': { panel: 'campaigns' },
    'navigate.activity': { panel: 'activity' },
    'navigate.connect': { panel: 'connect' },
    'navigate.performance': { screen: 'performance' },
    'navigate.workitems': { screen: 'workitems' },
    'navigate.people': { screen: 'people' },
    'navigate.system': { screen: 'system' },
    'navigate.settings': { screen: 'settings' },
};

/** Where a desktop command goes: a panel's name, the registered screen's own route, or nothing. */
export function commandTarget(command: DesktopCommand): PanelName | Route | null {
    const to = commands[command];
    if (!to) return null;
    if ('panel' in to) return to.panel;
    const entry = registry.list('screen').find((each) => each.id === to.screen);
    return entry ? parseRoute(entry.href()) : null;
}

/** How long the console waits after a journal line before it reads what waits and the campaigns again. */
export const reloadDelay = 400;

/**
 * The last pane shown that has no address of its own (a set-up chat, the Feed, Start, a panel of the console's own): the
 * chat's address shows it. An agent's chat and a page are addressed; everything else stands at the chat's address.
 */
let unaddressed = 'start';
/** The address a move of the dock's own put in the bar: the route's change that follows it is the dock's own. */
let ownAddress: string | null = null;
/** Counts the moves: an agent's chat opened on its way is shown only if nothing moved the area meanwhile. */
let moves = 0;

/** Puts `to` in the bar as the dock's own move, a history entry or, `replacing`, none; nothing when it is there. */
function moveBar(to: Route, replacing = false): void {
    const address = href(to);
    if (address === href(route.value)) return;
    ownAddress = address;
    if (replacing) replace(to);
    else navigate(to);
}

/**
 * Shows a pane with no address of its own. Leaving a place that has one puts the chat's address in the bar as a history
 * entry, so Back comes back to that place; two such panes in a row are one entry.
 */
function showUnaddressed(pane: string): void {
    unaddressed = pane;
    if (namesPlace(route.value)) moveBar({ screen: 'chat' });
    reveal(pane);
}

/** Shows again what the chat's address shows: the pane last shown that has no address, while it is still there. */
function restore(): void {
    if (unaddressed.startsWith('chat:')) {
        const chat = chats.value[unaddressed.slice('chat:'.length)];
        // A chat that went, or became an agent's and so has an address of its own, gives way to Start.
        if (chat === undefined || agentOfChat(chat) !== null) unaddressed = 'start';
    }
    reveal(unaddressed);
}

/** What `chatOfAgent` answers for an agent the runtime does not know: it has no Chat to draw. */
const agentGone = Symbol('gone');
/** What `chatOfAgent` answers for an archived agent with no conversation: nothing can be made for it. */
const agentArchived = Symbol('archived');

/**
 * The conversation of an agent: the one held open, else the one the desktop keeps, else its unmade chat. Null where the
 * desktop could not open the one it keeps; `agentGone` where the runtime does not know the agent; `agentArchived` for an
 * archived agent with no conversation.
 */
async function chatOfAgent(id: string): Promise<string | null | typeof agentGone | typeof agentArchived> {
    // A conversation made for it wins over its unmade chat, which gives way to it.
    const held = Object.values(chats.value).find((each) => !each.main && each.campaignId === id && !isUnmade(each.id));
    if (held) return held.id;
    const kept = conversations.value.find((each) => !each.main && each.campaignId === id);
    if (kept) return openConversation(kept.id);
    // The desktop's list read afresh, which may not have been read yet when an address opens the agent.
    await reloadConversations();
    const listed = conversations.value.find((each) => !each.main && each.campaignId === id);
    if (listed) return openConversation(listed.id);
    const meanwhile = Object.values(chats.value).find(
        (each) => !each.main && each.campaignId === id && !isUnmade(each.id),
    );
    if (meanwhile) return meanwhile.id;
    // None yet: its Chat is drawn, and its conversation made at the person's first act that needs one; an agent the
    // console does not list is read first, and one the runtime does not know has no Chat at all.
    if (!campaigns.campaigns.value.some((each) => each.id === id)) {
        if (agentFacts.value[id] === undefined) await readAgent(here().api, id);
        if (goneAgents.value.has(id)) return agentGone;
    }
    const status =
        campaigns.campaigns.value.find((each) => each.id === id)?.status ?? agentFacts.value[id]?.campaign.status;
    if (status === 'archived') return agentArchived;
    const name =
        campaigns.campaigns.value.find((each) => each.id === id)?.name ?? agentFacts.value[id]?.campaign.name ?? id;
    return unmadeChat({ id, name });
}

/** An unmade chat became its conversation: the conversation takes its place in the area, shown if it was. */
function chatMade(from: string, to: string): void {
    const was = chatPane(from);
    const now = chatPane(to);
    lastChats = lastChats
        .map((each) => (each === was ? now : each))
        .filter((each, at, all) => all.indexOf(each) === at);
    if (unaddressed === was) unaddressed = now;
    const shown = activePanel.value === was;
    inArea.value = inArea.value
        .map((each) => (each === was ? now : each))
        .filter((each, at, all) => all.indexOf(each) === at);
    if (shown) reveal(now);
}

/** Shows an agent's Chat tab, at its own address, once its conversation is open; a move made meanwhile wins. */
function showAgentChat(id: string): void {
    const mine = ++moves;
    void chatOfAgent(id).then((chatId) => {
        if (mine !== moves) return;
        if (chatId === agentGone) {
            // The page's own way with an agent the runtime does not know: it says so, and the address leaves it.
            moveBar({ ...agentChatRoute(id), tab: 'people' }, true);
            reveal('page');
            return;
        }
        moveBar(agentChatRoute(id));
        if (chatId === null || chatId === agentArchived) {
            // Under the agent's own header, never another's pane: its Chat tab says why the desktop could not open its
            // conversation and offers to try again, or that it is archived, with nothing that could make one.
            chatNote.value =
                chatId === agentArchived
                    ? { agent: id, archived: true }
                    : { agent: id, why: conversationsFailure.value ?? 'The desktop did not say why.' };
            reveal('page');
            return;
        }
        if (chatNote.value?.agent === id) chatNote.value = null;
        reveal(chatPane(chatId));
    });
}

/**
 * The address changed by the browser's own history (Back, Forward) or by a link: the area shows what it names. The
 * chat's address shows the pane last shown that has no address; an agent's Chat tab its conversation; an approval's or a
 * question's address Needs you at it; any other address the page. A move the dock made itself is skipped.
 */
function followHistory(to: Route): void {
    const address = href(to);
    if (address === ownAddress) {
        ownAddress = null;
        return;
    }
    moves++;
    if (!namesPlace(to)) return restore();
    const agent = agentChatOf(to);
    // Where no chat can be made the page draws the agent and gives its Chat's address up for its first tab.
    if (agent !== null && can('chat.create')) return showAgentChat(agent);
    if (to.screen === 'attention' && to.kind && to.id) {
        reveal('needs');
        void needs.attention.open(here().api, to.kind, to.id);
        return;
    }
    if (activePanel.value !== 'page') reveal('page');
}

/** What an address names at the shell's start: a panel (with the item it opens), an agent's chat, a page, or nothing. */
function addressPlace(
    hash: string,
): { panel: PanelName; open?: { kind: 'approvals' | 'questions'; id: string } } | { agent: string } | null {
    if (hash === '' || hash === '#') return null;
    const to = parseRoute(hash);
    if (to.screen === 'chat') return null;
    // The demo's address names nothing of the console: where it is drawn at all, it is drawn instead of the console.
    if (to.screen === 'demo') return null;
    // The bare attention address is the default one (the window opens at it, and pairing leaves it): no address.
    if (to.screen === 'attention')
        return to.kind && to.id ? { panel: 'needs', open: { kind: to.kind, id: to.id } } : null;
    if (to.screen === 'campaigns' && to.id === undefined) return { panel: 'campaigns' };
    const agent = agentChatOf(to);
    if (agent !== null && can('chat.create')) return { agent };
    return { panel: 'page' };
}

function makeDock(): Dock {
    const show = (panel: PanelName) => {
        moves++;
        showUnaddressed(panel);
    };
    const showChat = (id: string) => {
        const chat = chats.value[id];
        if (!chat) return;
        moves++;
        const agent = agentOfChat(chat);
        if (agent === null) return showUnaddressed(chatPane(id));
        moveBar(agentChatRoute(agent));
        reveal(chatPane(id));
    };
    return {
        show,
        showChat,
        chat: (campaign) => {
            // A place that cannot make a chat shows where the chat is, in the place the window opens on.
            if (!can('chat.create')) return show('start');
            if (campaign) return showAgentChat(campaign.id);
            const mine = ++moves;
            void startChat().then((id) => {
                if (id !== null && mine === moves) showChat(id);
            });
        },
        dropChat: (id) => {
            inArea.value = inArea.value.filter((each) => each !== chatPane(id));
            lastChats = lastChats.filter((each) => each !== chatPane(id));
            if (unaddressed === chatPane(id)) unaddressed = 'start';
            // The chat shown was taken away: the area shows what the window opens on, at the chat's address.
            if (activePanel.value === chatPane(id)) {
                moves++;
                unaddressed = 'start';
                if (namesPlace(route.value)) moveBar({ screen: 'chat' }, true);
                reveal('start');
            }
        },
        page: (to) => {
            const agent = agentChatOf(to);
            if (agent !== null && can('chat.create')) return showAgentChat(agent);
            moves++;
            // The bar names what is shown: an agent's chat by its own address, anything else by the chat's. A panel a
            // start's address named gives that address up for the chat's, so Back from the page shows the panel.
            if (activePanel.value !== 'page' && shownAgent.value === null && route.value.screen !== 'chat')
                moveBar({ screen: 'chat' }, true);
            moveBar(to);
            reveal('page');
        },
    };
}

function Shell({ api, desktop, stream }: { api: Api; desktop: JasonDesktop; stream: JournalStream | undefined }) {
    const root = useRef<HTMLDivElement>(null);
    const dock = useMemo(() => makeDock(), []);
    // What the shell draws asks the console it is in, so nothing in it is drawn until the console is bound. It is bound
    // as the shell mounts, before the page is painted, and again whenever React mounts the shell once more (StrictMode
    // does, at once); the window opens on Start, holding nothing else.
    const [bound, setBound] = useState(false);
    // Where the window is narrow the sidebar is off the screen; the strip's button brings it over the area, and a choice in it (or a tap beside it) puts it away.
    const [sideOpen, setSideOpen] = useState(false);
    // While it is over the area the page behind is out of reach, the sidebar takes the focus, Escape puts it away and gives
    // the focus back to its button, and a window that grows past the narrow edge leaves nothing of it standing.
    useEffect(() => {
        if (!sideOpen) return;
        const area = document.querySelector('.jc-console .mainc > .area');
        area?.setAttribute('inert', '');
        document.querySelector<HTMLElement>('.jc-console .side button, .jc-console .side a')?.focus();
        const away = (event: KeyboardEvent) => {
            if (event.key !== 'Escape') return;
            setSideOpen(false);
            document.querySelector<HTMLElement>('.jc-console .mode-nav')?.focus();
        };
        window.addEventListener('keydown', away);
        const wide = typeof matchMedia === 'function' ? matchMedia('(min-width: 821px)') : null;
        const grown = () => {
            if (wide?.matches) setSideOpen(false);
        };
        wide?.addEventListener('change', grown);
        return () => {
            area?.removeAttribute('inert');
            window.removeEventListener('keydown', away);
            wide?.removeEventListener('change', grown);
        };
    }, [sideOpen]);
    useLayoutEffect(() => {
        const unbound = bind({ api, desktop, dock });
        inArea.value = [];
        lastChats = [];
        unaddressed = 'start';
        ownAddress = null;
        moves = 0;
        reveal('start');
        // The address is the truth: one that names something draws it (the window opens with none, so on Start).
        const named = addressPlace(location.hash);
        // An agent's address an older link gave, or one naming no tab, is written as the tab it opens, with no entry.
        const at = route.value;
        if (at.screen === 'campaigns' && at.id !== undefined && location.hash !== href(at))
            history.replaceState(null, '', href(at));
        if (named !== null && 'agent' in named) showAgentChat(named.agent);
        else if (named !== null) {
            if (named.panel !== 'page') unaddressed = named.panel;
            reveal(named.panel);
            if (named.open) void needs.attention.open(api, named.open.kind, named.open.id);
        }
        setBound(true);
        // The browser's Back and Forward move the address; the area follows it .
        let seen = href(route.value);
        const followed = effect(() => {
            const to = route.value;
            // The address read again from the bar, as it was, moves nothing.
            if (href(to) === seen) return;
            seen = href(to);
            followHistory(to);
        });
        const made = onChatMade(chatMade);
        // An unmade chat gives way to its agent's conversation as soon as one is held or listed.
        const shadowed = effect(() => {
            void chats.value;
            void conversations.value;
            untracked(retireShadowed);
        });
        return () => {
            shadowed();
            made();
            followed();
            unbound();
        };
    }, [api, desktop, dock]);
    useEffect(() => {
        const onFocus = () => connect.reloadOnFocus();
        window.addEventListener('focus', onFocus);
        const onKey = (event: KeyboardEvent) => {
            if (event.key === 'Escape') closeMenus();
        };
        window.addEventListener('keydown', onKey);
        const unlisten = root.current ? listen(root.current) : () => undefined;
        void shell.load(api);
        void needs.reload();
        void campaigns.reload();
        void connect.reload();
        void reloadConversations();
        // The Feed's id comes with the desktop's first answer, which a link to the Feed waits for.
        const feedAsked = askFeed();
        let pending: ReturnType<typeof setTimeout> | null = null;
        const journal =
            stream ??
            pageStream(
                () => session.token.value,
                async () => {
                    try {
                        await api.call('system.info');
                        return true;
                    } catch {
                        return false;
                    }
                },
            );
        // A link opens the panel or the screen it names, and does nothing else (`links.ts`).
        const opened = (link: DesktopLink) => {
            const place = linkPlace(link);
            if (place === null) return;
            if ('feed' in place) {
                void feedAsked
                    .then(() => (feedId.value === null ? null : openConversation(feedId.value)))
                    .then((held) => {
                        if (held === null) return;
                        feedUnseen.value = false;
                        dock.showChat(held);
                    });
                return;
            }
            if ('route' in place) return dock.page(place.route);
            dock.show(place.panel);
            if (place.panel === 'needs' && place.open) void needs.attention.open(api, place.open.kind, place.open.id);
        };
        const stops = [
            // The standing choice of Full access, for the mode menu's take-back and the System screen.
            followStanding(desktop),
            can('links.onOpen') ? desktop.links.onOpen(opened) : () => undefined,
            desktop.chat.onEvent((id, event) => {
                changeChat(id, (chat) => received(chat, event));
                // A line the Feed took while it is not in front marks it in the sidebar; nothing else happens.
                if ((event.kind === 'line' || event.kind === 'gap') && id === feedId.value && currentChat.value !== id)
                    feedUnseen.value = true;
                // The desktop renamed or reordered its conversations: a hand-over, a new title, or a turn's end.
                if (event.kind === 'attached' || event.kind === 'renamed' || event.kind === 'done')
                    void reloadConversations();
                // A hand-over makes a draft campaign the chat is about; the Reply card reads its status from the list.
                if (event.kind === 'attached') {
                    void campaigns.reload();
                    // The chat shown is that agent's now: it stands under the agent's header, at the agent's address.
                    if (activePanel.value === chatPane(id)) {
                        if (unaddressed === chatPane(id)) unaddressed = 'start';
                        moveBar(agentChatRoute(event.campaignId), true);
                    }
                }
            }),
            desktop.setup.onChanged(() => void connect.reload()),
            can('onCommand')
                ? desktop.onCommand((command) => {
                      if (command === 'chat.new' || command === 'navigate.chat') return dock.chat();
                      const to = commandTarget(command);
                      if (typeof to === 'string') dock.show(to);
                      else if (to) dock.page(to);
                  })
                : () => undefined,
            journal.onState((state) => {
                // A resume the runtime refused starts the stream afresh: what was written meanwhile is read back for the
                // Feed, and a person's acts on an agent since its chat's open choice are read again.
                const before = streamState.value;
                if (state.fresh && state.last === null && (!before.fresh || before.last !== null)) {
                    catchUpFeed();
                    readActsAgain(api);
                }
                streamState.value = state;
            }),
            journal.onLine((line) => {
                keepLine(line);
                handLine(line);
                // A person's act on an agent locks its chat's open choice at once, before any list is read again.
                heardLine(line);
                if (pending !== null) return;
                pending = setTimeout(() => {
                    pending = null;
                    void needs.reload();
                    reloadWaiting(api);
                    void campaigns.reload();
                    dashboard.reloadIfOpen();
                    // The agent shown follows the journal: started, paused or archived anywhere, its header says so.
                    const agent = shownAgent.value;
                    if (agent !== null) void readAgent(api, agent.id);
                }, reloadDelay);
            }),
        ];
        journal.start();
        if (can('links.take'))
            void desktop.links
                .take()
                .then((link) => {
                    if (link !== null) opened(link);
                })
                .catch(() => undefined);
        const tick = setInterval(() => {
            clock.value = Date.now();
        }, 30_000);
        return () => {
            journal.stop();
            for (const stop of stops) stop();
            clearInterval(tick);
            if (pending !== null) clearTimeout(pending);
            unlisten();
            window.removeEventListener('focus', onFocus);
            window.removeEventListener('keydown', onKey);
            // A dialog the Reply card was showing is not kept past the console that showed it.
            connect.resetDialogs();
        };
    }, [api, desktop, dock, stream]);

    // The Mac desktop's own window has no title bar: its buttons sit over the sidebar's top, and the window is dragged by
    // a band there and by the strip. The Windows desktop's window has none either: the caption buttons stand over the
    // strip's right end, which leaves them free. A browser tab, and the desktop's window on Linux, keep their own frame.
    const mac = desktop.platform === 'darwin' && placeKind() === 'window';
    const win = desktop.platform === 'win32' && placeKind() === 'window';
    return (
        <div ref={root} className={`jc-console${mac ? ' desktop-mac' : ''}${win ? ' desktop-win' : ''}`}>
            {mac && <div className="title-drag" aria-hidden="true" />}
            {bound && (
                <>
                    {/* The sidebar runs the window's height; the strip heads the column right of it, over the area. */}
                    <div className="shell">
                        <aside
                            className={sideOpen ? 'side open' : 'side'}
                            onClickCapture={(event) => {
                                if ((event.target as HTMLElement).closest('[data-action], a, button'))
                                    setSideOpen(false);
                            }}
                        >
                            <Painted className="side-body" view={sideView} />
                            <SideFailure />
                        </aside>
                        {sideOpen && (
                            <button
                                type="button"
                                className="side-away"
                                tabIndex={-1}
                                aria-hidden="true"
                                onClick={() => setSideOpen(false)}
                            />
                        )}
                        <div className="mainc">
                            <ModeStrip
                                nav={{ open: sideOpen, toggle: () => setSideOpen((was) => !was) }}
                                openIn={
                                    can('openIn.targets') && earlier(desktop).openIn !== undefined ? (
                                        <OpenIn desktop={desktop} />
                                    ) : undefined
                                }
                            />
                            <Area />
                        </div>
                    </div>
                    <Palette system={desktop.platform} />
                </>
            )}
        </div>
    );
}

/**
 * Why the conversations could not be listed, opened or made, in the desktop's words. Drawn by React beside the painted
 * sidebar, not in it: the sidebar is painted afresh as the chats change, and an alert inserted afresh is said afresh.
 */
function SideFailure() {
    const failure = conversationsFailure.value;
    return failure === null ? null : (
        <div className="note am side-note" role="alert">
            {failure}
        </div>
    );
}

/** The console, paired first like the browser shell; `stream` stands in for the page's own stream in tests. */
export function Console({
    api,
    desktop,
    stream,
    bootstrapped = false,
}: {
    api?: Api;
    desktop: JasonDesktop;
    stream?: JournalStream;
    /** The page read the pairing before it started the console, as a tab's start does to learn its bridge. */
    bootstrapped?: boolean;
}) {
    const client = useMemo(() => api ?? new HttpApi(session), [api]);
    const [ready, setReady] = useState(bootstrapped);
    useEffect(() => {
        if (!bootstrapped) void session.bootstrap().finally(() => setReady(true));
    }, [bootstrapped]);
    if (!ready) return null;
    // A browser tab shows the demo as a view of its own, the console put away; the window's demo is the desktop's.
    if (route.value.screen === 'demo' && placeKind() !== 'window') return <DemoView />;
    if (!session.paired.value) return <PairAgain />;
    return (
        <ApiProvider api={client}>
            <Shell api={client} desktop={desktop} stream={stream} />
        </ApiProvider>
    );
}
