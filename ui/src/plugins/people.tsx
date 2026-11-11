import { t } from '../platform/i18n';
import type { UiPlugin } from '../platform/plugin';
import { href } from '../router';
import { PeopleScreen } from '../screens/people/screen';

export const peoplePlugin: UiPlugin = {
    id: 'people',
    setup({ register }) {
        register('screen', {
            id: 'people',
            label: () => t('nav.people'),
            order: 50,
            href: () => href({ screen: 'people', tab: 'timeline', filters: {} }),
            component: PeopleScreen,
        });
    },
};
