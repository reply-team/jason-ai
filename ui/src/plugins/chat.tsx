import { t } from '../platform/i18n';
import type { UiPlugin } from '../platform/plugin';
import { href } from '../router';
import { ChatScreen } from '../screens/chat/screen';

/** The Chat row: its page draws nothing of its own, since the console draws the chat in its area. */
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
