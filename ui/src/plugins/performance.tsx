import { t } from '../platform/i18n';
import type { UiPlugin } from '../platform/plugin';
import { href } from '../router';
import { PerformanceScreen } from '../screens/performance/screen';

export const performancePlugin: UiPlugin = {
    id: 'performance',
    setup({ register }) {
        register('screen', {
            id: 'performance',
            label: () => t('nav.performance'),
            order: 20,
            href: () => href({ screen: 'performance', tab: 'timeline', period: '7d', campaigns: [] }),
            component: PerformanceScreen,
        });
    },
};
