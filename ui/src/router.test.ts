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
    ])('%s is %o', (hash, route) => {
        expect(parseRoute(hash)).toEqual(route);
    });
});

describe('href', () => {
    it.each([
        [{ screen: 'attention' as const }, '#/attention'],
        [{ screen: 'attention' as const, kind: 'approvals' as const, id: 'apr 1' }, '#/attention/approvals/apr%201'],
        [{ screen: 'system' as const }, '#/system'],
    ])('%o is %s', (route, hash) => {
        expect(href(route)).toBe(hash);
    });
});
