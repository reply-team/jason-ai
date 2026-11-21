import { describe, expect, it } from 'vitest';
import { startHeld, transitions } from './moves-model';

// A campaign's one move by its status, and the one draft that offers none yet: no brief and no one in it.

describe('a campaign’s move', () => {
    it('is Pause for an active one, Resume for a paused one, Start for a draft and none for an archived one', () => {
        expect(transitions({ status: 'active' })).toEqual(['pause']);
        expect(transitions({ status: 'paused' })).toEqual(['resume']);
        expect(transitions({ status: 'draft' })).toEqual(['start']);
        expect(transitions({ status: 'archived' })).toEqual([]);
    });

    it('holds Start back only while a draft has no brief and no one in it, both read', () => {
        const empty = { status: 'draft', context: {} };
        expect(startHeld(empty, 0)).toBe(true);
        expect(transitions(empty, 0)).toEqual([]);
        // A brief keeps Start, with people or without: finding the audience is the manager's to plan.
        expect(transitions({ status: 'draft', context: { offer: 'A pilot' } }, 0)).toEqual(['start']);
        expect(transitions(empty, 3)).toEqual(['start']);
        // What was not read holds nothing back.
        expect(transitions(empty, null)).toEqual(['start']);
        expect(transitions(empty)).toEqual(['start']);
        expect(transitions({ status: 'draft' }, 0)).toEqual(['start']);
        // Another status is not a draft's.
        expect(startHeld({ status: 'active', context: {} }, 0)).toBe(false);
    });
});
