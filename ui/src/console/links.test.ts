import { describe, expect, it } from 'vitest';
import type { DesktopLink } from '../platform/desktop';
import { parseRoute } from '../router';
import { linkPlace } from './links';

const id = (prefix: string) => `${prefix}_01JD0000000000000000000001`;

describe('where a link the desktop parsed takes the console', () => {
    it('opens a panel, or a screen at the entity, and nothing else', () => {
        const table: [DesktopLink, unknown][] = [
            [{ page: 'needs' }, { panel: 'needs' }],
            [{ page: 'campaigns' }, { panel: 'campaigns' }],
            [
                { page: 'approval', id: id('apr') },
                { panel: 'needs', open: { kind: 'approvals', id: id('apr') } },
            ],
            [
                { page: 'question', id: id('dec') },
                { panel: 'needs', open: { kind: 'questions', id: id('dec') } },
            ],
            // A screen at an entity is where the router's own address of it goes.
            [{ page: 'campaign', id: id('cmp') }, { route: parseRoute(`#/campaigns/${id('cmp')}`) }],
            [{ page: 'workitem', id: id('wi') }, { route: parseRoute(`#/workitems/now/${id('wi')}`) }],
            [{ page: 'person', id: id('cnt') }, { route: parseRoute(`#/people/${id('cnt')}`) }],
        ];
        for (const [link, place] of table) expect(linkPlace(link)).toEqual(place);
    });

    it('takes nothing from a value that is no page of the table', () => {
        for (const value of [
            { page: 'settings' },
            { page: 'chat', id: 'x' },
            { page: 'toString' },
            null,
            'jason-desktop://needs',
            42,
        ]) {
            expect(linkPlace(value as DesktopLink)).toBeNull();
        }
    });
});
