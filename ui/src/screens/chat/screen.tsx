import { lazy, Suspense } from 'react';
import { session } from '../../api/session';
import { t } from '../../platform/i18n';

export const releasesPage = 'https://github.com/reply-team/jason-ai/releases';

// The chat's code and its stylesheet load only for a tab the desktop opened.
const TabChat = lazy(() => import('./tab-chat').then((module) => ({ default: module.TabChat })));

/**
 * The browser's Chat screen. A tab the desktop opened holds a bridge to the desktop's chat, and the screen shows that
 * chat; any other tab says where the chat is. It starts nothing and asks nothing of the runtime or a desktop.
 */
export function ChatScreen() {
    const bridge = session.bridge.value;
    if (bridge !== null) {
        return (
            <div className="chat-in-tab">
                <h1>{t('chat.title')}</h1>
                <Suspense fallback={null}>
                    <TabChat bridge={bridge} />
                </Suspense>
            </div>
        );
    }
    return (
        <div className="chat-elsewhere">
            <h1>{t('chat.title')}</h1>
            <p>{t('chat.desktop')}</p>
            <p>
                <a href={releasesPage} target="_blank" rel="noopener noreferrer">
                    {t('chat.get')}
                </a>
            </p>
            <p className="muted">{t('chat.agent')}</p>
        </div>
    );
}
