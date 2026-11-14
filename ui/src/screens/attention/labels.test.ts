import { describe, expect, it } from 'vitest';
import { reasonLabel } from './labels';

describe('why an approval is here', () => {
    it('says the daily limit in its own sentence, before any recording carries it', () => {
        expect(reasonLabel('daily_ceiling')).toBe(
            "This agent has reached today's limit of automatic messages, so this one waits for you to approve it.",
        );
    });

    it('says a send that went without asking, and shows a reason this build does not know as it is', () => {
        expect(reasonLabel('automatic_mode')).toBe(
            'It went without asking you, as these messages are set to go, after a review passed its text.',
        );
        expect(reasonLabel('a_newer_reason')).toBe('a_newer_reason');
    });
});
