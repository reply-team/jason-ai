import { describe, expect, it } from 'vitest';
import { chipWords, openingChips } from './chips';

// The chips a chat offers: the opening three of an empty chat, words a person would ask and never a claim.

describe('the chips’ words', () => {
    it('are the opening three, hold no number, and are shown once each', () => {
        expect(openingChips.map((chip) => chip.shown)).toEqual([
            'What is waiting for me?',
            'How are my agents doing?',
            'Help me start a new agent.',
        ]);
        for (const word of chipWords) expect(word, word).not.toMatch(/\p{N}/u);
        expect(new Set(chipWords).size).toBe(chipWords.length);
        expect(chipWords).toEqual(openingChips.map((chip) => chip.shown));
    });
});
