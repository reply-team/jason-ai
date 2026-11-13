import { describe, expect, it } from 'vitest';
import {
    categoryLabel,
    cut,
    dashboardWindow,
    greetingFor,
    needsSubline,
    progressOf,
    rateOf,
    startingRange,
    threadsIn,
    tilesOf,
    type ActivityRead,
    type ReplyCountRead,
} from './dashboard-model';

describe('the greeting by the hour', () => {
    it.each([
        [0, 'Good evening'],
        [4, 'Good evening'],
        [5, 'Good morning'],
        [11, 'Good morning'],
        [12, 'Good afternoon'],
        [17, 'Good afternoon'],
        [18, 'Good evening'],
        [23, 'Good evening'],
    ])('at hour %i says %s', (hour, words) => {
        expect(greetingFor(hour)).toBe(words);
    });
});

describe('the Needs you subline', () => {
    it('names a part only above zero, singular and plural by count', () => {
        expect(needsSubline(2, 1)).toBe('2 approvals · 1 question');
        expect(needsSubline(1, 3)).toBe('1 approval · 3 questions');
        expect(needsSubline(0, 2)).toBe('2 questions');
        expect(needsSubline(4, 0)).toBe('4 approvals');
        expect(needsSubline(0, 0)).toBeNull();
    });
});

describe('the reply rate', () => {
    it('is replied over contacted, to one decimal that is dropped when whole', () => {
        expect(rateOf(5, 40)).toBe('12.5%');
        expect(rateOf(2, 4)).toBe('50%');
        expect(rateOf(1, 3)).toBe('33.3%');
    });
    it('is nothing when nobody was contacted', () => {
        expect(rateOf(0, 0)).toBeNull();
        expect(rateOf(3, 0)).toBeNull();
    });
});

describe('the window of the tiles', () => {
    const now = new Date('2026-10-16T09:00:00Z');
    it('runs from the local midnight twenty-nine days back to now for thirty days, and six back for seven', () => {
        expect(dashboardWindow('30d', now, 'UTC')).toEqual({
            since: '2026-09-17T00:00:00.000Z',
            until: '2026-10-16T09:00:00.000Z',
        });
        expect(dashboardWindow('7d', now, 'UTC')?.since).toBe('2026-10-10T00:00:00.000Z');
    });
    it('begins three and six months on the same day of the month, and this year on 1 January', () => {
        expect(dashboardWindow('3m', now, 'UTC')?.since).toBe('2026-07-16T00:00:00.000Z');
        expect(dashboardWindow('6m', now, 'UTC')?.since).toBe('2026-04-16T00:00:00.000Z');
        expect(dashboardWindow('year', now, 'UTC')).toEqual({
            since: '2026-01-01T00:00:00.000Z',
            until: '2026-10-16T09:00:00.000Z',
        });
    });
    it('lands a day the earlier month lacks on that month’s last day', () => {
        const end = new Date('2026-05-31T09:00:00Z');
        expect(dashboardWindow('3m', end, 'UTC')?.since).toBe('2026-02-28T00:00:00.000Z');
        expect(dashboardWindow('6m', end, 'UTC')?.since).toBe('2025-11-30T00:00:00.000Z');
    });
    it('counts days in the person’s zone, so a window begins at that zone’s midnight', () => {
        expect(dashboardWindow('year', new Date('2026-01-01T03:00:00Z'), 'America/Los_Angeles')?.since).toBe(
            '2025-01-01T08:00:00.000Z',
        );
        expect(dashboardWindow('3m', now, 'America/Los_Angeles')?.since).toBe('2026-07-16T07:00:00.000Z');
    });
    it('takes a custom range whole days, both included, and ends at now when the last day is today', () => {
        const range = { from: '2026-09-01', to: '2026-09-30' };
        expect(dashboardWindow('custom', now, 'UTC', range)).toEqual({
            since: '2026-09-01T00:00:00.000Z',
            until: '2026-10-01T00:00:00.000Z',
        });
        expect(dashboardWindow('custom', now, 'UTC', { from: '2026-10-01', to: '2026-10-16' })?.until).toBe(
            '2026-10-16T09:00:00.000Z',
        );
    });
    it('asks for nothing from a custom range that is empty, backwards or not yet begun', () => {
        expect(dashboardWindow('custom', now, 'UTC', null)).toBeNull();
        expect(dashboardWindow('custom', now, 'UTC', { from: '', to: '2026-09-30' })).toBeNull();
        expect(dashboardWindow('custom', now, 'UTC', { from: '2026-09-30', to: '2026-09-01' })).toBeNull();
        expect(dashboardWindow('custom', now, 'UTC', { from: '2026-11-01', to: '2026-11-30' })).toBeNull();
    });
    it('starts a custom choice on the thirty days up to today', () => {
        expect(startingRange(now, 'UTC')).toEqual({ from: '2026-09-17', to: '2026-10-16' });
    });
});

describe('the categories', () => {
    const read: ReplyCountRead = {
        total: {
            replies: 6,
            threads: 4,
            categories: [
                { category: 'interested', replies: 3, threads: 2 },
                { category: 'meeting_booked', replies: 2, threads: 1 },
                { category: 'very_interested', replies: 1, threads: 1 },
            ],
        },
    };
    it('reads the threads of one word, compared exactly, and nothing where the word is absent', () => {
        expect(threadsIn(read, 'interested')).toBe(2);
        expect(threadsIn(read, 'meeting_booked')).toBe(1);
        expect(threadsIn(read, 'Interested')).toBeNull();
        expect(threadsIn(null, 'interested')).toBeNull();
    });
    it('shows a word with its underscores as spaces and a capital first', () => {
        expect(categoryLabel('meeting_booked')).toBe('Meeting booked');
        expect(categoryLabel('other')).toBe('Other');
    });
});

describe('the tiles', () => {
    const read: ActivityRead = { contacted: 40, replied: 5 };
    it('gives six tiles in order, the three with no fact read holding no value', () => {
        const tiles = tilesOf({ approvals: 2, questions: 1, waitingKnown: true, activity: read });
        expect(tiles.map((each) => each.label)).toEqual([
            'Needs you',
            'Contacted',
            'Replied',
            'Interested',
            'Meetings booked',
            'Bounce rate',
        ]);
        expect(tiles.map((each) => each.value)).toEqual([3, 40, 5, null, null, null]);
        expect(tiles[0]!.sub).toBe('2 approvals · 1 question');
        expect(tiles[2]!.sub).toBe('12.5% reply rate');
    });
    it('fills Interested and Meetings booked from the threads of their words, each saying on hover what it counts', () => {
        const replies: ReplyCountRead = {
            total: {
                replies: 5,
                threads: 4,
                categories: [
                    { category: 'interested', replies: 3, threads: 2 },
                    { category: 'meeting_booked', replies: 1, threads: 1 },
                    { category: 'meeting_requested', replies: 1, threads: 1 },
                ],
            },
        };
        const tiles = tilesOf({ approvals: 0, questions: 0, waitingKnown: true, activity: read, replies });
        expect(tiles.map((each) => each.value)).toEqual([0, 40, 5, 2, 1, null]);
        expect(tiles[3]!.hint).toBe('Conversations the reply-reading role judged interested.');
        expect(tiles[4]!.hint).toBe('Conversations the reply-reading role judged to have a meeting booked.');
        expect(tiles[5]!.hint).toBeUndefined();
    });
    it('holds a dash for a word the answer does not use, and never takes meeting_requested for a booking', () => {
        const replies: ReplyCountRead = {
            total: { replies: 1, threads: 1, categories: [{ category: 'meeting_requested', replies: 1, threads: 1 }] },
        };
        const tiles = tilesOf({ approvals: 0, questions: 0, waitingKnown: true, activity: read, replies });
        expect(tiles[3]!.value).toBeNull();
        expect(tiles[4]!.value).toBeNull();
    });
    it('shows a computed zero as zero, and no rate when nobody was contacted', () => {
        const tiles = tilesOf({
            approvals: 0,
            questions: 0,
            waitingKnown: true,
            activity: { contacted: 0, replied: 0 },
        });
        expect(tiles.slice(0, 3).map((each) => each.value)).toEqual([0, 0, 0]);
        expect(tiles[2]!.sub).toBeNull();
    });
    it('holds no value for what was not read', () => {
        const tiles = tilesOf({ approvals: 0, questions: 0, waitingKnown: false, activity: null });
        expect(tiles.map((each) => each.value)).toEqual([null, null, null, null, null, null]);
    });
});

describe('progress and cutting', () => {
    it('is contacted over people, capped, and nothing without people', () => {
        expect(progressOf(5, 10)).toBe(0.5);
        expect(progressOf(12, 10)).toBe(1);
        expect(progressOf(3, 0)).toBeNull();
    });
    it('cuts a long question to ninety characters with an ellipsis', () => {
        expect(cut('short', 90)).toBe('short');
        const long = 'x'.repeat(120);
        expect(cut(long, 90)).toBe(`${'x'.repeat(89)}…`);
        expect(cut(long, 90)).toHaveLength(90);
    });
});
