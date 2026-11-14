import { describe, expect, it } from 'vitest';
import { hiddenUntil, isHidden } from './reminder';

describe('the reminder about name and tone hides for seven days', () => {
    const now = new Date('2026-11-14T10:00:00Z');

    it('hides until exactly seven days after the moment it was hidden', () => {
        expect(new Date(hiddenUntil(now)).toISOString()).toBe('2026-11-21T10:00:00.000Z');
    });

    it('is hidden before that moment and shown from it', () => {
        const stored = String(hiddenUntil(now));
        expect(isHidden(stored, new Date('2026-11-21T09:59:59Z'))).toBe(true);
        expect(isHidden(stored, new Date('2026-11-21T10:00:00Z'))).toBe(false);
    });

    it('hides nothing when nothing is kept or what is kept is not a moment', () => {
        expect(isHidden(null, now)).toBe(false);
        expect(isHidden('soon', now)).toBe(false);
    });
});
