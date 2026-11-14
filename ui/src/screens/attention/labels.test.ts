import { describe, expect, it } from 'vitest';
import { reasonLabel } from './labels';

describe('why an approval is here', () => {
    it('says the daily limit in its own sentence, before any recording carries it', () => {
        expect(reasonLabel('daily_ceiling')).toBe(
            'This campaign has reached its limit of automatic sends for today, so this one waits for you to approve it.',
        );
    });

    it('says a send that went without asking, and shows a reason this build does not know as it is', () => {
        expect(reasonLabel('automatic_mode')).toBe(
            'The send mode let it go without asking you, after a review passed its text.',
        );
        expect(reasonLabel('a_newer_reason')).toBe('a_newer_reason');
    });
});
