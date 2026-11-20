import { describe, expect, it } from 'vitest';
import { atFoot, followedTop, footSlack } from './follow';

describe('the place a conversation is followed to', () => {
    it('is the box’s own foot while nothing stands under the conversation', () => {
        expect(followedTop({ scrollHeight: 2000, clientHeight: 500, foot: 2000, keep: 250 })).toBe(1500);
    });

    it('is the box’s own foot while what stands under the conversation is shorter than half the view', () => {
        expect(followedTop({ scrollHeight: 2100, clientHeight: 500, foot: 2000, keep: 250 })).toBe(1600);
        expect(followedTop({ scrollHeight: 2250, clientHeight: 500, foot: 2000, keep: 250 })).toBe(1750);
    });

    it('keeps the conversation’s last half view at the top while something taller stands under it', () => {
        expect(followedTop({ scrollHeight: 4000, clientHeight: 500, foot: 2000, keep: 250 })).toBe(1750);
        expect(followedTop({ scrollHeight: 2251, clientHeight: 500, foot: 2000, keep: 250 })).toBe(1750);
    });

    it('is never above the top of the box', () => {
        expect(followedTop({ scrollHeight: 300, clientHeight: 500, foot: 300, keep: 250 })).toBe(0);
        expect(followedTop({ scrollHeight: 3000, clientHeight: 500, foot: 100, keep: 250 })).toBe(0);
    });
});

describe('a conversation at its foot', () => {
    it('is within 120 px above the followed place, the Demo’s measure, and no further', () => {
        expect(footSlack).toBe(120);
        expect(atFoot(1500, 1500)).toBe(true);
        expect(atFoot(1380, 1500)).toBe(true);
        expect(atFoot(1379, 1500)).toBe(false);
        expect(atFoot(0, 1500)).toBe(false);
    });

    it('is where a box below the followed place stands, reading what waits under the conversation', () => {
        expect(atFoot(3500, 1750)).toBe(true);
    });

    it('is where a conversation shorter than its view always stands', () => {
        expect(atFoot(0, 0)).toBe(true);
    });
});
