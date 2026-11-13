import { useEffect, useRef, useState } from 'react';
import { useApi } from '../../api/context';
import { listen } from '../../console/actions';
import { ChatPanel, openConversation, reloadConversations, startChat } from '../../console/chat';
import { received } from '../../console/chat-model';
import * as campaigns from '../../console/campaigns';
import * as connect from '../../console/connect';
import { bind, changeChat, chats, conversations, conversationsFailure, type Dock } from '../../console/state';
import { asDesktop, type Bridge } from '../../platform/bridge';
import { followStanding } from '../../platform/full-access';
import { t } from '../../platform/i18n';
import '../../console/console.css';
import '../../console/harness.css';
import './tab-chat.css';

// The console's chat, in a browser tab, over the bridge the desktop opened for it. The same chat state and the same
// panel the desktop's window draws; the console is bound with the bridge as its seam and with `tab` set, which hides
// every control the desktop's own window alone may use. Nothing here asks the runtime for the chat.

const noop = () => undefined;

/** There is no dock in a tab: the chat is the whole of it. */
const tabDock: Dock = { show: noop, close: noop, chat: noop, showChat: noop, closeChat: noop, page: noop };

/** The kinds of event after which the desktop's list of conversations has changed. */
const listChanged = new Set(['attached', 'renamed', 'done']);

export function TabChat({ bridge }: { bridge: Bridge }) {
    const api = useApi();
    const root = useRef<HTMLDivElement>(null);
    const [ready, setReady] = useState(false);
    const [shown, setShown] = useState<string | null>(null);

    useEffect(() => {
        const desktop = asDesktop(bridge.seam());
        const unbind = bind({ api, desktop, dock: tabDock, tab: true });
        const unlisten = root.current ? listen(root.current) : noop;
        const stops = [
            followStanding(desktop),
            desktop.chat.onEvent((id, event) => {
                changeChat(id, (chat) => received(chat, event));
                if (listChanged.has(event.kind)) void reloadConversations();
            }),
            desktop.setup.onChanged(() => void connect.reload()),
            // The event stream was cut and opened again: what the tab drew may be behind, so every chat is read anew.
            bridge.onReopen(() => {
                const held = Object.keys(chats.value);
                chats.value = {};
                void reloadConversations();
                void connect.reload();
                for (const id of held) void openConversation(id);
            }),
        ];
        void campaigns.reload();
        void connect.reload();
        void reloadConversations();
        setReady(true);
        return () => {
            for (const stop of stops) stop();
            unlisten();
            connect.resetDialogs();
            unbind();
            bridge.close();
        };
    }, [api, bridge]);

    const open = (id: string) => void openConversation(id).then((held) => setShown(held));
    const fresh = () => void startChat().then((id) => setShown(id));

    return (
        <div ref={root} className="jc-console tab-chat">
            {ready && (
                <>
                    <nav className="tab-list" aria-label={t('chat.tab.list')}>
                        <button className="btn sm p" type="button" onClick={fresh}>
                            {t('chat.tab.new')}
                        </button>
                        {conversationsFailure.value !== null && (
                            <div className="note am">{conversationsFailure.value}</div>
                        )}
                        {conversations.value.length === 0 && conversationsFailure.value === null && (
                            <p className="muted">{t('chat.tab.empty')}</p>
                        )}
                        <ul>
                            {conversations.value.map((each) => (
                                <li key={each.id}>
                                    <button
                                        className="btn sm"
                                        type="button"
                                        aria-current={each.id === shown ? 'true' : undefined}
                                        onClick={() => open(each.id)}
                                    >
                                        {each.title}
                                        {each.running ? ` (${t('chat.tab.running')})` : ''}
                                    </button>
                                </li>
                            ))}
                        </ul>
                    </nav>
                    <div className="tab-panel">
                        {shown === null ? <p className="muted">{t('chat.tab.pick')}</p> : <ChatPanel chatId={shown} />}
                    </div>
                </>
            )}
        </div>
    );
}
