import { t } from '../platform/i18n';
import type { UiPlugin } from '../platform/plugin';
import { href } from '../router';
import { CampaignsScreen } from '../screens/campaigns/screen';

export const campaignsPlugin: UiPlugin = {
    id: 'campaigns',
    setup({ register }) {
        register('screen', {
            id: 'campaigns',
            label: () => t('nav.campaigns'),
            order: 30,
            href: () => href({ screen: 'campaigns', tab: 'timeline', view: 'summary', period: '7d', filters: {} }),
            component: CampaignsScreen,
        });
    },
};
