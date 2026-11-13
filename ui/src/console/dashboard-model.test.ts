import { describe, expect, it } from 'vitest';
import {
    cut,
    dashboardWindow,
    greetingFor,
    needsSubline,
    progressOf,
    rateOf,
    tilesOf,
    type ActivityRead,
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
    it('runs from the local midnight twenty-nine days back to now', () => {
        expect(dashboardWindow(new Date('2026-10-16T09:00:00Z'), 'UTC')).toEqual({
            since: '2026-09-17T00:00:00.000Z',
            until: '2026-10-16T09:00:00.000Z',
        });
    });
});

describe('the tiles', () => {
    const read: ActivityRead = { contacted: 40, replied: 5 };
    it('gives six tiles in order, the three with no fact in the runtime holding no value', () => {
        const tiles = tilesOf({ approvals: 2, questions: 1, waitingKnown: true, activity: read });
        expect(tiles.map((each) => each.label)).toEqual([
            'Needs you',
            'Contacted · 30 days',
            'Replied · 30 days',
            'Interested',
            'Meetings booked',
            'Bounce rate',
        ]);
        expect(tiles.map((each) => each.value)).toEqual([3, 40, 5, null, null, null]);
        expect(tiles[0]!.sub).toBe('2 approvals · 1 question');
        expect(tiles[2]!.sub).toBe('12.5% reply rate');
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
