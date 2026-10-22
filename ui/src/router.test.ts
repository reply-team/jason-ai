import { describe, expect, it } from 'vitest';
import { href, parseRoute } from './router';

describe('parseRoute', () => {
    it.each([
        ['', { screen: 'attention' }],
        ['#/', { screen: 'attention' }],
        ['#/attention', { screen: 'attention' }],
        ['#/attention/approvals/apr_1', { screen: 'attention', kind: 'approvals', id: 'apr_1' }],
        ['#/attention/questions/dec_1?t=abc', { screen: 'attention', kind: 'questions', id: 'dec_1' }],
        ['#/attention?decided=1', { screen: 'attention', decided: true }],
        [
            '#/attention/approvals/apr_1?decided=1&t=abc',
            { screen: 'attention', kind: 'approvals', id: 'apr_1', decided: true },
        ],
        ['#/attention?decided=yes', { screen: 'attention' }],
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
        ['#/people', { screen: 'people', tab: 'timeline', filters: {} }],
        ['#/people/cnt_7', { screen: 'people', id: 'cnt_7', tab: 'timeline', filters: {} }],
        [
            '#/people/cnt_7/details?q=ada&campaign=cmp_1&archived=1&t=abc',
            { screen: 'people', id: 'cnt_7', tab: 'details', filters: { q: 'ada', campaign: 'cmp_1', archived: true } },
        ],
        ['#/people/cnt_7/threads', { screen: 'people', id: 'cnt_7', tab: 'threads', filters: {} }],
        ['#/people/cnt_7/elsewhere?archived=yes&x=1', { screen: 'people', id: 'cnt_7', tab: 'timeline', filters: {} }],
        ['#/people/%E0%A4%A/details', { screen: 'people', tab: 'timeline', filters: {} }],
    ])('%s is %o', (hash, route) => {
        expect(parseRoute(hash)).toEqual(route);
    });
});

describe('href', () => {
    it.each([
        [{ screen: 'attention' as const }, '#/attention'],
        [{ screen: 'attention' as const, kind: 'approvals' as const, id: 'apr 1' }, '#/attention/approvals/apr%201'],
        [{ screen: 'attention' as const, decided: true as const }, '#/attention?decided=1'],
        [
            { screen: 'attention' as const, kind: 'approvals' as const, id: 'apr_1', decided: true as const },
            '#/attention/approvals/apr_1?decided=1',
        ],
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
        [{ screen: 'people' as const, tab: 'timeline' as const, filters: {} }, '#/people'],
        [
            { screen: 'people' as const, id: 'cnt 7', tab: 'details' as const, filters: { archived: true, q: 'a b' } },
            '#/people/cnt%207/details?q=a%20b&archived=1',
        ],
        [{ screen: 'people' as const, id: 'cnt_7', tab: 'threads' as const, filters: {} }, '#/people/cnt_7/threads'],
        [
            { screen: 'people' as const, id: 'cnt_7', tab: 'timeline' as const, filters: { campaign: 'cmp_1' } },
            '#/people/cnt_7?campaign=cmp_1',
        ],
    ])('%o is %s', (route, hash) => {
        expect(href(route)).toBe(hash);
    });
});
