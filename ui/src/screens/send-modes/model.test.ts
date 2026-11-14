import { describe, expect, it } from 'vitest';
import { askedKinds, doubtsOf, keyOf, remindsOfVoice, storedMode } from './model';

describe('the send modes the card asks about', () => {
    it('asks the kinds whose preference is unset, cold first, whatever order the runtime lists them in', () => {
        expect(askedKinds(['send_mode.warm', 'send_mode.cold'])).toEqual(['cold', 'warm']);
        expect(askedKinds(['send_mode.warm'])).toEqual(['warm']);
        expect(askedKinds([])).toEqual([]);
        expect(askedKinds(undefined)).toEqual([]);
        expect(askedKinds(['send_mode.other'])).toEqual([]);
    });

    it('names each kind by its preference', () => {
        expect([keyOf('cold'), keyOf('warm')]).toEqual(['send_mode.cold', 'send_mode.warm']);
    });
});

describe('the doubts a preview carries', () => {
    it('reads each entry in order, a person by name and a role by its role', () => {
        expect(
            doubtsOf({
                doubts: [
                    {
                        source: 'ask_person',
                        by: { type: 'human', id: 'Marta' },
                        role: null,
                        code: 'off_brief',
                        note: 'Too pushy.',
                    },
                    { source: 'review', by: { type: 'attempt', id: 'att_1' }, role: 'critic', code: 'other', note: '' },
                ],
            }),
        ).toEqual([
            { source: 'ask_person', who: 'Marta', role: null, code: 'off_brief', note: 'Too pushy.' },
            { source: 'review', who: null, role: 'critic', code: 'other', note: null },
        ]);
    });

    it('drops an entry that is not the runtime shape and yields none for a preview without doubts', () => {
        expect(doubtsOf({ doubts: [{ code: 'other' }, 'x', null] })).toEqual([]);
        expect(doubtsOf({ doubts: 'x' })).toEqual([]);
        expect(doubtsOf({})).toEqual([]);
    });
});

describe('the reminder about name and tone', () => {
    const item = (key: string, value: unknown) => ({
        key,
        value,
        value_hash: null,
        value_bytes: null,
        created_at: null,
        updated_at: null,
        updated_by: null,
    });
    const stored = (cold: unknown, warm: unknown, sender: unknown, tone: unknown) => [
        item('send_mode.cold', cold),
        item('send_mode.warm', warm),
        item('sender.identity', sender),
        item('tone_of_voice', tone),
    ];
    const automatic = { mode: 'automatic' };
    const waits = { mode: 'every_message' };
    const name = { senders: [{ name: 'Ada', gender_forms: 'neutral' }] };
    const tone = { summary: 'Plain.' };
    const agent = (cold: string, warm: string) => ({
        cold: { mode: cold, source: 'campaign' },
        warm: { mode: warm, source: 'default' },
    });

    it('reads a stored mode, and an unset one as none', () => {
        expect(storedMode(stored(automatic, null, null, null), 'cold')).toBe('automatic');
        expect(storedMode(stored(automatic, null, null, null), 'warm')).toBeNull();
        expect(storedMode(stored({ mode: 7 }, null, null, null), 'cold')).toBeNull();
    });

    it('never reminds while every mode in force waits for the person', () => {
        expect(remindsOfVoice(stored(waits, waits, null, null), agent('every_message', 'every_message'))).toBe(false);
        expect(remindsOfVoice(stored(null, null, null, null), agent('every_message', 'every_message'))).toBe(false);
        expect(remindsOfVoice(stored(null, null, null, null), undefined)).toBe(false);
    });

    it('reminds while the installation or the agent goes automatically and the name or the tone is unset', () => {
        expect(remindsOfVoice(stored(automatic, waits, null, null), undefined)).toBe(true);
        expect(remindsOfVoice(stored(null, null, name, null), agent('automatic', 'every_message'))).toBe(true);
        expect(remindsOfVoice(stored(waits, automatic, null, tone), undefined)).toBe(true);
    });

    it('goes away once both are set, whatever goes automatically', () => {
        expect(remindsOfVoice(stored(automatic, automatic, name, tone), agent('automatic', 'automatic'))).toBe(false);
    });
});
