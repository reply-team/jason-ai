import { describe, expect, it } from 'vitest';
import { bucketLabel, bucketPhrase, labelEvery, labelled } from './buckets';

const now = new Date('2026-10-16T09:00:00.000Z');
const day = (d: number) => `2026-10-${String(d).padStart(2, '0')}T00:00:00.000Z`;

describe('bucketLabel', () => {
    it('names a day by its weekday, by its date in a chart of more than a week, and the day still running today', () => {
        const days = [10, 11, 12, 13, 14, 15, 16].map((d) => ({
            start: day(d),
            end: d === 16 ? '2026-10-16T09:00:00.000Z' : day(d + 1),
            value: 0,
        }));
        const labels = days.map((bucket) => bucketLabel(bucket, 'day', 'UTC', now, days.length));
        expect(labels.map((l) => l.short)).toEqual(['Sat', 'Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'today']);
        expect(labels[2]!.long).toBe('Mon 12 Oct');
        expect(labels.map((l) => l.current)).toEqual([false, false, false, false, false, false, true]);
        // Over a month a weekday says nothing: the same Monday is named by its date.
        expect(bucketLabel(days[2]!, 'day', 'UTC', now, 30).short).toBe('12 Oct');
    });

    it('names an hour by its clock in the zone, and the hour running now', () => {
        const hour = (h: number) => `2026-10-16T${String(h).padStart(2, '0')}:00:00.000Z`;
        const labels = [7, 8].map((h) =>
            bucketLabel({ start: hour(h), end: hour(h + 1), value: 0 }, 'hour', 'Europe/Kyiv', now, 2),
        );
        expect(labels.map((l) => l.short)).toEqual(['10', 'now']);
        expect(labels[0]!.long).toBe('16 Oct, 10:00');
    });

    it('names a week by the day it starts, a week cut short by its days, and the week running now', () => {
        const weeks = [
            { start: '2026-07-19T00:00:00.000Z', end: '2026-07-20T00:00:00.000Z', value: 0 },
            { start: '2026-07-20T00:00:00.000Z', end: '2026-07-27T00:00:00.000Z', value: 0 },
            { start: '2026-10-12T00:00:00.000Z', end: '2026-10-16T09:00:00.000Z', value: 0 },
        ];
        const labels = weeks.map((bucket) => bucketLabel(bucket, 'week', 'UTC', now, 3));
        expect(labels.map((l) => l.short)).toEqual(['19 Jul', '20 Jul', 'this week']);
        // The window begins on a Sunday: its first bar is that day, not a week beginning on it.
        expect(labels.map((l) => l.long)).toEqual(['19 Jul', 'week of 20 Jul', 'week of 12 Oct']);
        expect(weeks.map((bucket) => bucketPhrase(bucket, 'week', 'UTC', now))).toEqual([
            'on 19 Jul',
            'in the week of 20 Jul',
            'in the week of 12 Oct',
        ]);
    });
});

describe('labelEvery', () => {
    it('labels every hour of a day on a wide chart and every third on a narrow one', () => {
        expect(labelEvery('hour', 24)).toEqual({ wide: 1, narrow: 3 });
        expect(labelEvery('hour', 9)).toEqual({ wide: 1, narrow: 2 });
    });

    it('keeps a week of days whole, and thins a month of days and a quarter of weeks', () => {
        expect(labelEvery('day', 7)).toEqual({ wide: 1, narrow: 1 });
        expect(labelEvery('day', 30)).toEqual({ wide: 3, narrow: 5 });
        expect(labelEvery('week', 14)).toEqual({ wide: 2, narrow: 2 });
    });
});

describe('labelled', () => {
    it('counts back from the last bar, so the bar running now keeps its label', () => {
        const bars = (n: number) => [...Array(n).keys()];
        expect(bars(7).filter((i) => labelled(i, 7, 3))).toEqual([0, 3, 6]);
        expect(bars(8).filter((i) => labelled(i, 8, 3))).toEqual([1, 4, 7]);
        expect(bars(3).filter((i) => labelled(i, 3, 1))).toEqual([0, 1, 2]);
    });
});
