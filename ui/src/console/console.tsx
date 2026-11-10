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
import type { DesktopCommand, JasonDesktop } from '../platform/desktop';
import { pageStream, type JournalStream } from '../platform/journal-stream';
import { resolvedTheme } from '../platform/theme';
import { navigate, parseRoute, type Route } from '../router';
import { PairAgain } from '../shell/pair-again';
import * as shell from '../shell/shell';
import { activityView } from './activity';
import { listen } from './actions';
import { ChatPanel, openChat } from './chat';
import { received } from './chat-model';
import * as campaigns from './campaigns';
import * as connect from './connect';
import * as needs from './needs';
import { Painted } from './painted';
import { sideView } from './side';
import { bind, changeChat, chats, clock, keepLine, streamState, type Dock, type PanelName } from './state';

// The desktop's shell: a sidebar and a dock. Home is the Live chat; beside it the panels the console draws itself from
// the Runtime API, and a page panel that shows the browser shell's screens. The dock's layout is not kept: every
// window opens on the same one.

const titles: Readonly<Record<PanelName, string>> = {
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

function PagePanel() {
    return (
        <div className="harness">
            <div className="app chromeless">
                <div className="content">
                    <CurrentPage />
                </div>
            </div>
        </div>
    );
}

const components = {
    chat: ChatTab,
    needs: () => <Painted className="painted" view={needs.needsView} />,
    campaigns: () => <Painted className="painted" view={campaigns.campaignsView} />,
    activity: () => <Painted className="painted" view={activityView} />,
    connect: () => <Painted className="painted" view={connect.connectView} />,
    page: PagePanel,
};

const commands: Partial<Record<DesktopCommand, PanelName | Route>> = {
    'navigate.attention': 'needs',
    'navigate.campaigns': 'campaigns',
    'navigate.activity': 'activity',
    'navigate.connect': 'connect',
    'navigate.performance': { screen: 'performance' } as Route,
    'navigate.workitems': parseRoute('#/workitems'),
    'navigate.people': parseRoute('#/people'),
    'navigate.system': { screen: 'system' },
    'navigate.settings': { screen: 'settings' },
};

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
        showChat,
        chat: (campaign) => showChat(openChat(campaign)),
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
        const unlisten = root.current ? listen(root.current) : () => undefined;
        void shell.load(api);
        void needs.reload();
        void campaigns.reload();
        void connect.reload();
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
        const stops = [
            desktop.chat.onEvent((id, event) => changeChat(id, (chat) => received(chat, event))),
            desktop.setup.onChanged(() => void connect.reload()),
            desktop.onCommand((command) => {
                if (command === 'chat.new' || command === 'navigate.chat') return dock.chat();
                const to = commands[command];
                if (typeof to === 'string') dock.show(to);
                else if (to) dock.page(to);
            }),
            journal.onState((state) => {
                streamState.value = state;
            }),
            journal.onLine((line) => {
                keepLine(line);
                if (pending !== null) return;
                pending = setTimeout(() => {
                    pending = null;
                    void needs.reload();
                    void campaigns.reload();
                }, reloadDelay);
            }),
        ];
        journal.start();
        const tick = setInterval(() => {
            clock.value = Date.now();
        }, 30_000);
        return () => {
            journal.stop();
            for (const stop of stops) stop();
            clearInterval(tick);
            if (pending !== null) clearTimeout(pending);
            unlisten();
            unbind();
        };
    }, [api, desktop, dock, stream]);

    const ready = (event: DockviewReadyEvent) => {
        dockApi.current = event.api;
        const first = openChat();
        event.api.addPanel({ id: `chat:${first}`, component: 'chat', title: 'Chat', params: { chatId: first } });
        event.api.addPanel({
            id: 'needs',
            component: 'needs',
            title: titles.needs,
            position: { referencePanel: `chat:${first}`, direction: 'right' },
        });
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
        event.api.getPanel(`chat:${first}`)?.api.setActive();
    };

    return (
        <div ref={root} className={`jc-console${desktop.platform === 'darwin' ? ' desktop-mac' : ''}`}>
            <div className="shell">
                <aside className="side">
                    <Painted className="side-body" view={sideView} />
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
