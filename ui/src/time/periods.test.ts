import { describe, expect, it } from 'vitest';
import { periodWindow } from './periods';

describe('periodWindow', () => {
    it.each([
        ['today', '2026-10-16T00:00:00.000Z'],
        ['7d', '2026-10-10T00:00:00.000Z'],
        ['30d', '2026-09-17T00:00:00.000Z'],
        ['90d', '2026-07-19T00:00:00.000Z'],
    ] as const)('%s starts at a local midnight and ends now', (period, since) => {
        const now = new Date('2026-10-16T09:00:00.000Z');
        expect(periodWindow(period, now, 'UTC')).toEqual({ since, until: '2026-10-16T09:00:00.000Z' });
    });

    it('starts today where the person is, not where UTC is', () => {
        // 22:30 UTC is already 01:30 on the 17th in Kyiv, three hours ahead in October.
        const now = new Date('2026-10-16T22:30:00.000Z');
        expect(periodWindow('today', now, 'Europe/Kyiv').since).toBe('2026-10-16T21:00:00.000Z');
    });

    it('keeps local midnights across a change of clocks', () => {
        // Kyiv leaves summer time at 04:00 on 25 October 2026: midnight that day is still three hours ahead, the next two.
        const after = new Date('2026-10-26T10:00:00.000Z');
        expect(periodWindow('today', after, 'Europe/Kyiv').since).toBe('2026-10-25T22:00:00.000Z');
        expect(periodWindow('7d', after, 'Europe/Kyiv').since).toBe('2026-10-19T21:00:00.000Z');
        const onTheDay = new Date('2026-10-25T12:00:00.000Z');
        expect(periodWindow('today', onTheDay, 'Europe/Kyiv').since).toBe('2026-10-24T21:00:00.000Z');
        // Santiago's clocks go forward at midnight on 6 September 2026: the day has no 00:00 and begins at 01:00.
        const skipped = new Date('2026-09-06T12:00:00.000Z');
        expect(periodWindow('today', skipped, 'America/Santiago').since).toBe('2026-09-06T04:00:00.000Z');
    });
});
