import { t } from '../platform/i18n';
import type { UiPlugin } from '../platform/plugin';
import { href } from '../router';
import { AccountsScreen } from '../screens/accounts/screen';

export const accountsPlugin: UiPlugin = {
    id: 'accounts',
    setup({ register }) {
        register('screen', {
            id: 'accounts',
            label: () => t('nav.accounts'),
            order: 70,
            href: () => href({ screen: 'accounts' }),
            component: AccountsScreen,
        });
    },
};
