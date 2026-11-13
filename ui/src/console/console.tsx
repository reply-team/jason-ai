import { useEffect, useMemo, useRef, useState } from 'react';
import {
    DockviewReact,
    themeDark,
    themeLight,
    type DockviewApi,
    type DockviewReadyEvent,
    type IDockviewPanelProps,
} from 'dockview-react';
import { HttpApi, type Api } from '../api/client';
import { ApiProvider } from '../api/context';
import { session } from '../api/session';
import { CurrentPage } from '../app';
import type { DesktopCommand, DesktopLink, JasonDesktop } from '../platform/desktop';
import { pageStream, type JournalStream } from '../platform/journal-stream';
import { registry } from '../platform/slots';
import { resolvedTheme } from '../platform/theme';
import { navigate, parseRoute, type Route } from '../router';
import { CampaignChat } from '../screens/campaigns/campaign-chat';
import { ProviderAccountsContext } from '../screens/accounts/provider-card';
import { PairAgain } from '../shell/pair-again';
import * as shell from '../shell/shell';
import { activityView } from './activity';
import { listen } from './actions';
import { askFeed, catchUpFeed, ChatPanel, handLine, openConversation, reloadConversations, startChat } from './chat';
import { received } from './chat-model';
import * as campaigns from './campaigns';
import * as connect from './connect';
import { linkPlace } from './links';
import * as needs from './needs';
import { followStanding } from '../platform/full-access';
import { Painted } from './painted';
import { sideView } from './side';
import { StartPanel, startMaking, startWaiting } from './start';
import {
    bind,
    changeChat,
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

// The desktop's shell: a sidebar and a dock. Home is the Live chat; beside it the panels the console draws itself from
// the Runtime API, and a page panel that shows the browser shell's screens. The dock's layout is not kept: every
// window opens on the same one.

const titles: Readonly<Record<PanelName, string>> = {
    start: 'Start',
    needs: 'Needs you',
    campaigns: 'Campaigns',
    activity: 'Activity',
    connect: 'Connect',
    page: 'Page',
};

function ChatTab({ params, api }: IDockviewPanelProps<{ chatId: string }>) {
    const title = chats.value[params.chatId]?.title;
    useEffect(() => {
        if (title) api.setTitle(title);
    }, [api, title]);
    return <ChatPanel chatId={params.chatId} />;
}

/** What a campaign's Chat in the page panel does: shows the campaign's one conversation, made on first use; with no
 *  campaign, a new set-up chat. One function for every render, so the pages it is handed to keep it. */
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

const components = {
    chat: ChatTab,
    start: StartPanel,
    needs: () => <Painted className="painted" view={needs.needsView} />,
    campaigns: () => <Painted className="painted" view={campaigns.campaignsView} />,
    activity: () => <Painted className="painted" view={activityView} />,
    connect: () => (
        <Painted className="painted" view={connect.connectView} onGone={() => connect.dropDialogs('connect')} />
    ),
    page: PagePanel,
};

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

function makeDock(get: () => DockviewApi | null): Dock {
    const chatPanel = (id: string) => `chat:${id}`;
    const show = (panel: PanelName) => {
        const dock = get();
        if (!dock) return;
        const open = dock.getPanel(panel);
        if (open) open.api.setActive();
        else dock.addPanel({ id: panel, component: panel, title: titles[panel] });
    };
    const showChat = (id: string) => {
        const dock = get();
        const chat = chats.value[id];
        if (!dock || !chat) return;
        const open = dock.getPanel(chatPanel(id));
        if (open) open.api.setActive();
        else dock.addPanel({ id: chatPanel(id), component: 'chat', title: chat.title, params: { chatId: id } });
    };
    return {
        show,
        close: (panel) => get()?.getPanel(panel)?.api.close(),
        showChat,
        chat: (campaign) =>
            void startChat(campaign).then((id) => {
                if (id !== null) showChat(id);
            }),
        closeChat: (id) => {
            get()?.getPanel(chatPanel(id))?.api.close();
            if (currentChat.value === id) currentChat.value = null;
        },
        page: (to) => {
            navigate(to);
            show('page');
        },
    };
}

function Shell({ api, desktop, stream }: { api: Api; desktop: JasonDesktop; stream: JournalStream | undefined }) {
    const root = useRef<HTMLDivElement>(null);
    const dockApi = useRef<DockviewApi | null>(null);
    const dock = useMemo(() => makeDock(() => dockApi.current), []);
    useEffect(() => {
        const unbind = bind({ api, desktop, dock });
        const onFocus = () => connect.reloadOnFocus();
        window.addEventListener('focus', onFocus);
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
            desktop.links.onOpen(opened),
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
            desktop.onCommand((command) => {
                if (command === 'chat.new' || command === 'navigate.chat') return dock.chat();
                const to = commandTarget(command);
                if (typeof to === 'string') dock.show(to);
                else if (to) dock.page(to);
            }),
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
                    void campaigns.reload();
                }, reloadDelay);
            }),
        ];
        journal.start();
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
            // A dialog the Reply card was showing is not kept past the console that showed it.
            connect.resetDialogs();
            unbind();
        };
    }, [api, desktop, dock, stream]);

    const ready = (event: DockviewReadyEvent) => {
        dockApi.current = event.api;
        // The sidebar marks the conversation whose chat came to the front last.
        event.api.onDidActivePanelChange(({ panel }) => {
            if (panel?.id.startsWith('chat:')) currentChat.value = panel.id.slice('chat:'.length);
            if (currentChat.value !== null && currentChat.value === feedId.value) feedUnseen.value = false;
        });
        event.api.addPanel({ id: 'needs', component: 'needs', title: titles.needs });
        event.api.addPanel({
            id: 'campaigns',
            component: 'campaigns',
            title: titles.campaigns,
            position: { referencePanel: 'needs' },
        });
        event.api.addPanel({
            id: 'activity',
            component: 'activity',
            title: titles.activity,
            position: { referencePanel: 'needs' },
        });
        event.api.getPanel('needs')?.api.setActive();
        // The window opens on the start screen, beside what needs the person, the campaigns and the activity.
        const placeStart = () => {
            event.api.addPanel({
                id: 'start',
                component: 'start',
                title: titles.start,
                position: { referencePanel: 'needs', direction: 'left' },
            });
            event.api.getPanel('start')?.api.setActive();
        };
        placeStart();
        // A message that waits is never out of sight: the tab cannot be closed while one waits or a chat is being made.
        event.api.onDidRemovePanel((panel) => {
            if (panel.id === 'start' && (startWaiting.value !== null || startMaking.value !== null)) placeStart();
        });
    };

    return (
        <div ref={root} className={`jc-console${desktop.platform === 'darwin' ? ' desktop-mac' : ''}`}>
            <div className="shell">
                <aside className="side">
                    <Painted className="side-body" view={sideView} />
                    <SideFailure />
                </aside>
                <main className="dock">
                    <DockviewReact
                        components={components}
                        onReady={ready}
                        theme={resolvedTheme.value === 'dark' ? themeDark : themeLight}
                        disableFloatingGroups
                    />
                </main>
            </div>
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
export function Console({ api, desktop, stream }: { api?: Api; desktop: JasonDesktop; stream?: JournalStream }) {
    const client = useMemo(() => api ?? new HttpApi(session), [api]);
    const [ready, setReady] = useState(false);
    useEffect(() => {
        void session.bootstrap().finally(() => setReady(true));
    }, []);
    if (!ready) return null;
    if (!session.paired.value) return <PairAgain />;
    return (
        <ApiProvider api={client}>
            <Shell api={client} desktop={desktop} stream={stream} />
        </ApiProvider>
    );
}
