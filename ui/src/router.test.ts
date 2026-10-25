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
        ['#/campaigns', { screen: 'campaigns', tab: 'timeline', view: 'summary', period: '7d', filters: {} }],
        [
            '#/campaigns?view=activity&period=30d&q=spr&archived=1&t=abc',
            {
                screen: 'campaigns',
                tab: 'timeline',
                view: 'activity',
                period: '30d',
                filters: { q: 'spr', archived: true },
            },
        ],
        [
            '#/campaigns?view=summary&period=today',
            { screen: 'campaigns', tab: 'timeline', view: 'summary', period: '7d', filters: {} },
        ],
        [
            '#/campaigns?view=weekly&period=1y',
            { screen: 'campaigns', tab: 'timeline', view: 'summary', period: '7d', filters: {} },
        ],
        [
            '#/campaigns/cmp_1',
            { screen: 'campaigns', id: 'cmp_1', tab: 'timeline', view: 'summary', period: '7d', filters: {} },
        ],
        [
            '#/campaigns/cmp_1/people?filter=failed',
            {
                screen: 'campaigns',
                id: 'cmp_1',
                tab: 'people',
                view: 'summary',
                period: '7d',
                filters: {},
                filter: 'failed',
            },
        ],
        [
            '#/campaigns/cmp_1/people?view=activity&period=today&filter=worked_on',
            {
                screen: 'campaigns',
                id: 'cmp_1',
                tab: 'people',
                view: 'activity',
                period: 'today',
                filters: {},
                filter: 'worked_on',
            },
        ],
        [
            '#/campaigns/cmp_1/people?filter=replied',
            { screen: 'campaigns', id: 'cmp_1', tab: 'people', view: 'summary', period: '7d', filters: {} },
        ],
        [
            '#/campaigns/cmp_1/people?view=activity&filter=removed',
            {
                screen: 'campaigns',
                id: 'cmp_1',
                tab: 'people',
                view: 'activity',
                period: '7d',
                filters: {},
                filter: 'removed',
            },
        ],
        [
            '#/campaigns/cmp_1/details?filter=failed',
            { screen: 'campaigns', id: 'cmp_1', tab: 'details', view: 'summary', period: '7d', filters: {} },
        ],
        [
            '#/campaigns/cmp_1/elsewhere',
            { screen: 'campaigns', id: 'cmp_1', tab: 'timeline', view: 'summary', period: '7d', filters: {} },
        ],
        [
            '#/campaigns/%E0%A4%A/people',
            { screen: 'campaigns', tab: 'timeline', view: 'summary', period: '7d', filters: {} },
        ],
        [
            '#/campaigns?view=activity&period=90d',
            { screen: 'campaigns', tab: 'timeline', view: 'activity', period: '7d', filters: {} },
        ],
        ['#/performance', { screen: 'performance', tab: 'timeline', period: '7d', campaigns: [] }],
        [
            '#/performance?period=90d&campaign=cmp_1&campaign=cmp_2&campaign=cmp_1&count=replied',
            { screen: 'performance', tab: 'timeline', period: '90d', campaigns: ['cmp_1', 'cmp_2'], count: 'replied' },
        ],
        [
            '#/performance/cnt_1/details?count=people&from=2026-10-13T00%3A00%3A00.000Z&to=2026-10-14T00%3A00%3A00.000Z',
            {
                screen: 'performance',
                id: 'cnt_1',
                tab: 'details',
                period: '7d',
                campaigns: [],
                count: 'people',
                from: '2026-10-13T00:00:00.000Z',
                to: '2026-10-14T00:00:00.000Z',
            },
        ],
        [
            '#/performance?count=nope&from=2026-10-13T00:00:00.000Z',
            { screen: 'performance', tab: 'timeline', period: '7d', campaigns: [] },
        ],
        [
            '#/performance?count=replied&from=2026-10-13T00:00:00.000Z',
            { screen: 'performance', tab: 'timeline', period: '7d', campaigns: [], count: 'replied' },
        ],
        [
            '#/performance?period=today&t=abc',
            { screen: 'performance', tab: 'timeline', period: 'today', campaigns: [] },
        ],
        // A bar's window is two real instants, the first before the second.
        [
            '#/performance?count=replied&from=2026-13-45T00:00:00.000Z&to=2026-10-14T00:00:00.000Z',
            { screen: 'performance', tab: 'timeline', period: '7d', campaigns: [], count: 'replied' },
        ],
        [
            '#/performance?count=replied&from=2026-10-14T00:00:00.000Z&to=2026-10-13T00:00:00.000Z',
            { screen: 'performance', tab: 'timeline', period: '7d', campaigns: [], count: 'replied' },
        ],
    ])('%s is %o', (hash, route) => {
        expect(parseRoute(hash)).toEqual(route);
    });

    it('keeps the first hundred campaigns of a Performance address, as many as one read may name', () => {
        const ids = (n: number) => Array.from({ length: n }, (_, i) => `cmp_${i}`);
        const address = (n: number) =>
            `#/performance?${ids(n)
                .map((id) => `campaign=${id}`)
                .join('&')}`;
        expect(parseRoute(address(100))).toMatchObject({ campaigns: ids(100) });
        expect(parseRoute(address(101))).toMatchObject({ campaigns: ids(100) });
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
        [
            {
                screen: 'campaigns' as const,
                tab: 'timeline' as const,
                view: 'summary' as const,
                period: '7d' as const,
                filters: {},
            },
            '#/campaigns',
        ],
        [
            {
                screen: 'campaigns' as const,
                tab: 'timeline' as const,
                view: 'activity' as const,
                period: '30d' as const,
                filters: { archived: true, q: 'a b' },
            },
            '#/campaigns?view=activity&period=30d&q=a%20b&archived=1',
        ],
        [
            {
                screen: 'campaigns' as const,
                id: 'cmp 1',
                tab: 'people' as const,
                view: 'activity' as const,
                period: '7d' as const,
                filters: {},
                filter: 'worked_on',
            },
            '#/campaigns/cmp%201/people?view=activity&filter=worked_on',
        ],
        [
            {
                screen: 'campaigns' as const,
                id: 'cmp_1',
                tab: 'people' as const,
                view: 'summary' as const,
                period: '7d' as const,
                filters: { q: 'spr' },
                filter: 'removed',
            },
            '#/campaigns/cmp_1/people?q=spr&filter=removed',
        ],
        [
            {
                screen: 'campaigns' as const,
                id: 'cmp_1',
                tab: 'details' as const,
                view: 'summary' as const,
                period: '7d' as const,
                filters: {},
            },
            '#/campaigns/cmp_1/details',
        ],
        [
            { screen: 'performance' as const, tab: 'timeline' as const, period: '7d' as const, campaigns: [] },
            '#/performance',
        ],
        [
            {
                screen: 'performance' as const,
                id: 'cnt 1',
                tab: 'threads' as const,
                period: '30d' as const,
                campaigns: ['cmp_1', 'cmp_2'],
                count: 'opted_out' as const,
                from: '2026-10-13T00:00:00.000Z',
                to: '2026-10-14T00:00:00.000Z',
            },
            '#/performance/cnt%201/threads?period=30d&campaign=cmp_1&campaign=cmp_2&count=opted_out&from=2026-10-13T00%3A00%3A00.000Z&to=2026-10-14T00%3A00%3A00.000Z',
        ],
    ])('%o is %s', (route, hash) => {
        expect(href(route)).toBe(hash);
    });
});
