import { t } from '../platform/i18n';
import type { UiPlugin } from '../platform/plugin';
import { href } from '../router';
import { WorkItemsScreen } from '../screens/workitems/screen';

export const workItemsPlugin: UiPlugin = {
    id: 'workitems',
    setup({ register }) {
        register('screen', {
            id: 'workitems',
            label: () => t('nav.workitems'),
            order: 40,
            href: () => href({ screen: 'workitems', tab: 'now', filters: {} }),
            component: WorkItemsScreen,
        });
    },
};
