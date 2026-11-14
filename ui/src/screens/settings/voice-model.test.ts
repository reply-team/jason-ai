import { describe, expect, it } from 'vitest';
import {
    Unreadable,
    emptySender,
    emptyTone,
    mergeSender,
    mergeTone,
    noneStated,
    senderCount,
    senderForm,
    toneForm,
} from './voice-model';

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
        const value = mergeSender(
            kept,
            { name: ' Marta L. ', title: '', company: 'Example', gender: 'neutral' },
            senderForm(kept),
        );
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
        expect(
            mergeSender(
                { senders: [{ name: 'Ada', gender_forms: 'neutral' }] },
                { name: '  ', title: '', company: '', gender: 'neutral' },
                emptySender,
            ),
        ).toBeNull();
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
        expect(mergeTone(stored, { summary: ' Warm ', formality: 'informal' }, toneForm(stored))).toEqual({
            ...stored,
            summary: 'Warm',
            formality: 'informal',
        });
    });

    it('takes the formality away when none is stated', () => {
        const rest: Record<string, unknown> = { ...stored };
        delete rest['formality'];
        expect(mergeTone(stored, { summary: 'Plain', formality: '' }, toneForm(stored))).toEqual(rest);
    });

    it('never makes up a summary', () => {
        expect(mergeTone(stored, { summary: '', formality: 'formal' })).toBeNull();
        expect(mergeTone(null, { summary: ' ', formality: '' })).toBeNull();
        expect(toneForm(null)).toEqual({ summary: '', formality: '' });
    });
});

describe('a save writes only what the person changed', () => {
    const painted = { name: 'Ada', title: '', company: '', gender: 'neutral' as const };
    const set = { senders: [{ name: 'Ada', gender_forms: 'neutral', title: 'CEO set in the terminal' }] };

    it('keeps a title set elsewhere since the form was painted, when the person left it alone', () => {
        const value = mergeSender(set, { ...painted, name: 'Ada L.' }, painted) as typeof set;
        expect(value.senders[0]).toEqual({ name: 'Ada L.', gender_forms: 'neutral', title: 'CEO set in the terminal' });
    });

    it('takes a title away only when the person emptied it', () => {
        const shown = { ...painted, title: 'CEO' };
        const stored = { senders: [{ name: 'Ada', gender_forms: 'neutral', title: 'CEO' }] };
        expect(mergeSender(stored, { ...shown, title: '' }, shown)).toEqual({
            senders: [{ name: 'Ada', gender_forms: 'neutral' }],
        });
        expect(mergeSender(stored, { ...shown, title: 'COO' }, shown)).toEqual({
            senders: [{ name: 'Ada', gender_forms: 'neutral', title: 'COO' }],
        });
    });

    it('writes what the schema requires and the value lacks, left alone or not', () => {
        expect(mergeSender(null, { name: 'Ada', title: '', company: '', gender: 'neutral' }, emptySender)).toEqual({
            senders: [{ name: 'Ada', gender_forms: 'neutral' }],
        });
        expect(mergeSender({ senders: [{ name: 'Ada' }] }, painted, painted)).toEqual({
            senders: [{ name: 'Ada', gender_forms: 'neutral' }],
        });
    });

    it('keeps a formality set elsewhere when it was left alone, and removes it only when put back to not stated', () => {
        const stored = { summary: 'Plain', formality: 'formal' };
        const shown = { summary: 'Plain', formality: '' as const };
        expect(mergeTone(stored, { ...shown, summary: 'Warm' }, shown)).toEqual({
            summary: 'Warm',
            formality: 'formal',
        });
        const was = { summary: 'Plain', formality: 'formal' as const };
        expect(mergeTone(stored, { ...was, formality: '' }, was)).toEqual({ summary: 'Plain' });
        expect(mergeTone(stored, { ...was, formality: 'informal' }, was)).toEqual({
            summary: 'Plain',
            formality: 'informal',
        });
    });

    it('has no summary to write when none is stored and none is given', () => {
        expect(mergeTone(null, emptyTone, emptyTone)).toBeNull();
    });
});

describe('a stored word this build does not know', () => {
    it('is shown as it is and left as read when the person did not touch it, for the grammar forms', () => {
        const stored = { senders: [{ name: 'Ada', gender_forms: 'epicene' }] };
        const shown = senderForm(stored);
        expect(shown.gender).toBe('epicene');
        expect(mergeSender(stored, { ...shown, name: 'Ada L.' }, shown)).toEqual({
            senders: [{ name: 'Ada L.', gender_forms: 'epicene' }],
        });
        expect(mergeSender(stored, { ...shown, gender: 'neutral' }, shown)).toEqual({
            senders: [{ name: 'Ada', gender_forms: 'neutral' }],
        });
    });

    it('is shown as it is and left as read when the person did not touch it, for the formality', () => {
        const stored = { summary: 'Plain', formality: 'ceremonial' };
        const shown = toneForm(stored);
        expect(shown.formality).toBe('ceremonial');
        expect(mergeTone(stored, { ...shown, summary: 'Warm' }, shown)).toEqual({
            summary: 'Warm',
            formality: 'ceremonial',
        });
        expect(mergeTone(stored, { ...shown, formality: '' }, shown)).toEqual({ summary: 'Plain' });
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
