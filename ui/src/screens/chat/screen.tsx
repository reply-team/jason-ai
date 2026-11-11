import { t } from '../../platform/i18n';

export const releasesPage = 'https://github.com/reply-team/jason-ai/releases';

/** The browser's Chat screen: where the chat is. It starts nothing and asks nothing of the runtime or a desktop. */
export function ChatScreen() {
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
