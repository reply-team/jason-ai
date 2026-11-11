import { t } from '../platform/i18n';
import type { UiPlugin } from '../platform/plugin';
import { href } from '../router';
import { SettingsScreen } from '../screens/settings/screen';

export const settingsPlugin: UiPlugin = {
    id: 'settings',
    setup({ register }) {
        register('screen', {
            id: 'settings',
            label: () => t('nav.settings'),
            order: 90,
            href: () => href({ screen: 'settings' }),
            component: SettingsScreen,
        });
    },
};
