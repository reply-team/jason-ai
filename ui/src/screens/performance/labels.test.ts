import { describe, expect, it } from 'vitest';
import { shareOfContacted } from './labels';

describe('shareOfContacted', () => {
    it('is the period’s share, nothing when nobody was contacted, and no percentage past all of them', () => {
        expect(shareOfContacted(2, 4)).toBe('50.0% of the period’s contacted');
        expect(shareOfContacted(4, 4)).toBe('100.0% of the period’s contacted');
        expect(shareOfContacted(1, 0)).toBeNull();
        // The counts cover different people: today's replies may answer last week's messages.
        expect(shareOfContacted(3, 1)).toBe('more than the period’s contacted');
    });
});
