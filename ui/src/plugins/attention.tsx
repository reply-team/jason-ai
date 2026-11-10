import { t } from '../platform/i18n';
import type { UiPlugin } from '../platform/plugin';
import { href } from '../router';
import { AttentionScreen } from '../screens/attention/screen';
import { pendingMark } from '../shell/attention-mark';

export const attentionPlugin: UiPlugin = {
    id: 'attention',
    setup({ register }) {
        register('screen', {
            id: 'attention',
            label: () => t('nav.attention'),
            order: 10,
            href: () => href({ screen: 'attention' }),
            marked: () => pendingMark.value,
            component: AttentionScreen,
        });
    },
};
