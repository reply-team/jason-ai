import { describe, expect, it } from 'vitest';
import { Unreadable, mergeSender, mergeTone, noneStated, senderCount, senderForm, toneForm } from './voice-model';

const kept = {
    senders: [
        {
            name: 'Marta Lind',
            addressed_as: 'Marta',
            gender_forms: 'feminine',
            title: 'Head',
            details: 'Prefers short notes',
        },
        { name: 'Ola Berg', gender_forms: 'masculine', aliases: ['O. Berg'] },
    ],
};

describe('the sender part', () => {
    it('changes the first sender name, title, company and grammar forms and keeps every other member', () => {
        const value = mergeSender(kept, { name: ' Marta L. ', title: '', company: 'Example', gender: 'neutral' });
        expect(value).toEqual({
            senders: [
                {
                    name: 'Marta L.',
                    addressed_as: 'Marta',
                    gender_forms: 'neutral',
                    company: 'Example',
                    details: 'Prefers short notes',
                },
                kept.senders[1],
            ],
        });
    });

    it('makes one sender from nothing, with no member it was not given', () => {
        expect(mergeSender(null, { name: 'Ada', title: '', company: '', gender: 'neutral' })).toEqual({
            senders: [{ name: 'Ada', gender_forms: 'neutral' }],
        });
    });

    it('has nothing to save for an empty name, and refuses a stored value it cannot write back whole', () => {
        expect(mergeSender(kept, { name: '  ', title: '', company: '', gender: 'neutral' })).toBeNull();
        expect(() => mergeSender({ senders: 'x' }, { name: 'A', title: '', company: '', gender: 'neutral' })).toThrow(
            Unreadable,
        );
    });

    it('reads the first sender into the form, with neutral where no grammar forms were set', () => {
        expect(senderForm(kept)).toEqual({ name: 'Marta Lind', title: 'Head', company: '', gender: 'feminine' });
        expect(senderForm(null).gender).toBe('neutral');
        expect(senderCount(kept)).toBe(2);
    });
});

describe('the tone part', () => {
    const stored = { summary: 'Plain', formality: 'formal', greeting: 'Hi', sign_off: 'Best', examples: ['One'] };

    it('changes the summary and the formality and keeps the greeting, the sign-off and the examples', () => {
        expect(mergeTone(stored, { summary: ' Warm ', formality: 'informal' })).toEqual({
            ...stored,
            summary: 'Warm',
            formality: 'informal',
        });
    });

    it('takes the formality away when none is stated', () => {
        const rest: Record<string, unknown> = { ...stored };
        delete rest['formality'];
        expect(mergeTone(stored, { summary: 'Plain', formality: '' })).toEqual(rest);
    });

    it('never makes up a summary', () => {
        expect(mergeTone(stored, { summary: '', formality: 'formal' })).toBeNull();
        expect(mergeTone(null, { summary: ' ', formality: '' })).toBeNull();
        expect(toneForm(null)).toEqual({ summary: '', formality: '' });
    });
});

describe('what is stated', () => {
    const item = (key: string, value: unknown) => ({
        key,
        value,
        value_hash: null,
        value_bytes: null,
        created_at: null,
        updated_at: null,
        updated_by: null,
    });

    it('is none only when neither preference is set; the host preference does not count', () => {
        expect(
            noneStated([
                item('agent.interactive_host', { host: 'codex' }),
                item('sender.identity', null),
                item('tone_of_voice', null),
            ]),
        ).toBe(true);
        expect(noneStated([item('sender.identity', null), item('tone_of_voice', { summary: 'x' })])).toBe(false);
    });
});
