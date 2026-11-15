import { signal } from '@preact/signals-react';
import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { HttpApi, type Api } from '../api/client';
import { ApiProvider } from '../api/context';
import { session } from '../api/session';
import { CurrentPage } from '../app';
import { can, earlier } from '../platform/places';
import type { DesktopCommand, DesktopLink, JasonDesktop } from '../platform/desktop';
import { pageStream, type JournalStream } from '../platform/journal-stream';
import { registry } from '../platform/slots';
import { navigate, parseRoute, type Route } from '../router';
import { CampaignChat } from '../screens/campaigns/campaign-chat';
import { ProviderAccountsContext } from '../screens/accounts/provider-card';
import { ModeStrip } from './mode-strip';
import { OpenIn } from './open-in';
import { PairAgain } from '../shell/pair-again';
import * as shell from '../shell/shell';
import { activityView } from './activity';
import { listen } from './actions';
import { askFeed, catchUpFeed, ChatPanel, handLine, openConversation, reloadConversations, startChat } from './chat';
import { received } from './chat-model';
import * as campaigns from './campaigns';
import * as connect from './connect';
import * as dashboard from './dashboard';
import { linkPlace } from './links';
import * as needs from './needs';
import { reloadWaiting } from './waiting';
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

function PagePanel() {
    return (
        <div className="harness">
            <div className="app chromeless">
                <div className="content">
                    <CampaignChat.Provider value={chatAbout}>
                        <ProviderAccountsContext.Provider value={providerAccounts}>
                            <CurrentPage />
                        </ProviderAccountsContext.Provider>
                    </CampaignChat.Provider>
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

const panels: Readonly<Record<PanelName, (inSight: boolean) => ReactNode>> = {
    start: () => <StartPanel />,
    dashboard: (inSight) => <DashboardPanel inSight={inSight} />,
    needs: () => <Painted className="painted" view={needs.needsView} />,
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
                kept && <ChatPanel chatId={chatId} />
            )}
        </section>
    );
}

function PanelBody({ name, inSight }: { name: PanelName; inSight: boolean }) {
    return panels[name](inSight);
}

/** The one area: every thing it holds is drawn, and all but the one shown are hidden. */
function Area() {
    return (
        <main className="area">
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

function makeDock(): Dock {
    const show = (panel: PanelName) => reveal(panel);
    const showChat = (id: string) => {
        if (chats.value[id]) reveal(chatPane(id));
    };
    return {
        show,
        showChat,
        chat: (campaign) => {
            // A place that cannot make a chat shows where the chat is, in the place the window opens on.
            if (!can('chat.create')) return show('start');
            void startChat(campaign).then((id) => {
                if (id !== null) showChat(id);
            });
        },
        dropChat: (id) => {
            inArea.value = inArea.value.filter((each) => each !== chatPane(id));
            lastChats = lastChats.filter((each) => each !== chatPane(id));
            // The chat shown was taken away: the area shows what the window opens on.
            if (activePanel.value === chatPane(id)) reveal('start');
        },
        page: (to) => {
            navigate(to);
            show('page');
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
    useLayoutEffect(() => {
        const unbound = bind({ api, desktop, dock });
        inArea.value = [];
        lastChats = [];
        reveal('start');
        setBound(true);
        return unbound;
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
                if (event.kind === 'attached') void campaigns.reload();
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
                // Feed.
                const before = streamState.value;
                if (state.fresh && state.last === null && (!before.fresh || before.last !== null)) catchUpFeed();
                streamState.value = state;
            }),
            journal.onLine((line) => {
                keepLine(line);
                handLine(line);
                if (pending !== null) return;
                pending = setTimeout(() => {
                    pending = null;
                    void needs.reload();
                    reloadWaiting(api);
                    void campaigns.reload();
                    dashboard.reloadIfOpen();
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

    return (
        <div ref={root} className={`jc-console${desktop.platform === 'darwin' ? ' desktop-mac' : ''}`}>
            {bound && (
                <>
                    <ModeStrip
                        openIn={
                            can('openIn.targets') && earlier(desktop).openIn !== undefined ? (
                                <OpenIn desktop={desktop} />
                            ) : undefined
                        }
                    />
                    <div className="shell">
                        <aside className="side">
                            <Painted className="side-body" view={sideView} />
                            <SideFailure />
                        </aside>
                        <Area />
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
    if (!session.paired.value) return <PairAgain />;
    return (
        <ApiProvider api={client}>
            <Shell api={client} desktop={desktop} stream={stream} />
        </ApiProvider>
    );
}
