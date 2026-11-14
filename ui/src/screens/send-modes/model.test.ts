import { describe, expect, it } from 'vitest';
import { askedKinds, doubtsOf, keyOf } from './model';

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
