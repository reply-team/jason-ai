import { t } from '../platform/i18n';
import type { UiPlugin } from '../platform/plugin';
import { href } from '../router';
import { SystemScreen } from '../screens/system/screen';

export const systemPlugin: UiPlugin = {
    id: 'system',
    setup({ register }) {
        register('screen', {
            id: 'system',
            label: () => t('nav.system'),
            order: 60,
            href: () => href({ screen: 'system' }),
            component: SystemScreen,
        });
    },
};
