import { describe, expect, it } from 'vitest';
import { groupByDay } from './grouping';

const rows = (...isos: string[]) => isos.map((iso) => ({ at: iso }));

describe('groupByDay', () => {
    it.each([
        [rows('2026-10-16T09:00:00Z', '2026-10-16T17:00:00Z'), ['2026-10-16']],
        [rows('2026-10-16T23:30:00Z', '2026-10-17T00:30:00Z'), ['2026-10-17', '2026-10-16']],
        [rows('2026-10-10T09:00:00Z', '2026-10-16T09:00:00Z', '2026-10-10T10:00:00Z'), ['2026-10-16', '2026-10-10']],
        [[], []],
    ])('groups into days newest first', (items, days) => {
        expect(groupByDay(items, (r) => new Date(r.at), 'UTC').map((g) => g.day)).toEqual(days);
    });

    it('keeps the days in the order the items came in when asked, soonest first for planned work', () => {
        const grouped = groupByDay(
            rows('2026-10-16T10:00:00Z', '2026-10-17T09:00:00Z'),
            (r) => new Date(r.at),
            'UTC',
            false,
        );
        expect(grouped.map((g) => g.day)).toEqual(['2026-10-16', '2026-10-17']);
    });

    it('keeps the input order inside a day and puts a zone boundary where the zone says', () => {
        // Kyiv is UTC+3 in October: 20:00Z is still the 16th there, 23:30Z and 00:30Z are the 17th.
        const grouped = groupByDay(
            rows('2026-10-16T23:30:00Z', '2026-10-16T20:00:00Z', '2026-10-17T00:30:00Z'),
            (r) => new Date(r.at),
            'Europe/Kyiv',
        );
        expect(grouped).toEqual([
            { day: '2026-10-17', items: rows('2026-10-16T23:30:00Z', '2026-10-17T00:30:00Z') },
            { day: '2026-10-16', items: rows('2026-10-16T20:00:00Z') },
        ]);
    });
});
