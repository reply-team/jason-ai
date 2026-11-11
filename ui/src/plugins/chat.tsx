import { t } from '../platform/i18n';
import type { UiPlugin } from '../platform/plugin';
import { href } from '../router';
import { ChatScreen } from '../screens/chat/screen';

/** The browser's Chat row: a page that says where the chat is. The desktop draws a chat of its own instead. */
export const chatPlugin: UiPlugin = {
    id: 'chat',
    setup({ register }) {
        register('screen', {
            id: 'chat',
            label: () => t('nav.chat'),
            order: 5,
            href: () => href({ screen: 'chat' }),
            component: ChatScreen,
        });
    },
};
