import { describe, expect, it } from 'vitest';
import { atFoot, footSlack } from './follow';

describe('a conversation at its foot', () => {
    it('is within 120 px of it, the Demo’s measure, and no further', () => {
        expect(footSlack).toBe(120);
        expect(atFoot({ scrollHeight: 2000, clientHeight: 500, scrollTop: 1500 })).toBe(true);
        expect(atFoot({ scrollHeight: 2000, clientHeight: 500, scrollTop: 1380 })).toBe(true);
        expect(atFoot({ scrollHeight: 2000, clientHeight: 500, scrollTop: 1379 })).toBe(false);
        expect(atFoot({ scrollHeight: 2000, clientHeight: 500, scrollTop: 0 })).toBe(false);
    });

    it('is where a thread shorter than its view always stands', () => {
        expect(atFoot({ scrollHeight: 300, clientHeight: 500, scrollTop: 0 })).toBe(true);
    });
});
