import { describe, expect, it } from 'vitest';
import { href, parseRoute } from './router';

describe('parseRoute', () => {
    it.each([
        ['', { screen: 'attention' }],
        ['#/', { screen: 'attention' }],
        ['#/attention', { screen: 'attention' }],
        ['#/attention/approvals/apr_1', { screen: 'attention', kind: 'approvals', id: 'apr_1' }],
        ['#/attention/questions/dec_1?t=abc', { screen: 'attention', kind: 'questions', id: 'dec_1' }],
        ['#/system', { screen: 'system' }],
        ['#/nowhere/else', { screen: 'attention' }],
        ['#/attention/approvals/', { screen: 'attention' }],
        ['#/attention/approvals/%E0%A4%A', { screen: 'attention' }],
        ['#/workitems', { screen: 'workitems', tab: 'now', filters: {} }],
        [
            '#/workitems/planned?campaign=cmp_1&kind=ai_role',
            { screen: 'workitems', tab: 'planned', filters: { campaign: 'cmp_1', kind: 'ai_role' } },
        ],
        [
            '#/workitems/done/wi_7?failed=1&t=abc',
            { screen: 'workitems', tab: 'done', id: 'wi_7', filters: { failed: true } },
        ],
        ['#/workitems/done?kind=everything&x=1', { screen: 'workitems', tab: 'done', filters: {} }],
        ['#/workitems/soon', { screen: 'workitems', tab: 'now', filters: {} }],
        [
            '#/workitems/done?on=2026-10-16&failed=1',
            { screen: 'workitems', tab: 'done', filters: { on: '2026-10-16', failed: true } },
        ],
        ['#/workitems/planned?on=yesterday', { screen: 'workitems', tab: 'planned', filters: {} }],
        ['#/workitems/planned?on=2026-13-01', { screen: 'workitems', tab: 'planned', filters: {} }],
    ])('%s is %o', (hash, route) => {
        expect(parseRoute(hash)).toEqual(route);
    });
});

describe('href', () => {
    it.each([
        [{ screen: 'attention' as const }, '#/attention'],
        [{ screen: 'attention' as const, kind: 'approvals' as const, id: 'apr 1' }, '#/attention/approvals/apr%201'],
        [{ screen: 'system' as const }, '#/system'],
        [{ screen: 'workitems' as const, tab: 'now' as const, filters: {} }, '#/workitems'],
        [
            {
                screen: 'workitems' as const,
                tab: 'planned' as const,
                filters: { role: 'manager', contact: 'cnt_2', campaign: 'cmp_1' },
            },
            '#/workitems/planned?campaign=cmp_1&contact=cnt_2&role=manager',
        ],
        [
            { screen: 'workitems' as const, tab: 'done' as const, filters: { failed: true, on: '2026-10-16' } },
            '#/workitems/done?on=2026-10-16&failed=1',
        ],
        [
            { screen: 'workitems' as const, tab: 'done' as const, id: 'wi 7', filters: { failed: true } },
            '#/workitems/done/wi%207?failed=1',
        ],
    ])('%o is %s', (route, hash) => {
        expect(href(route)).toBe(hash);
    });
});
