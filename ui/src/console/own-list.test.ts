import { describe, expect, it } from 'vitest';
import type { OwnListCard } from './cards';
import { addressesIn, fieldWord, listReason, listRequest, mappingLine, rowOutcome } from './own-list';

// A list card's two pieces of logic of its own: the request a press sends, built from the card alone, and the comparison
// of its addresses with what the person wrote in the chat.

const card = (fields: Partial<OwnListCard> = {}): OwnListCard => ({
    kind: 'own_list',
    match_by: 'email',
    columns: [
        { from: 'Name', field: 'first_name' },
        { from: 'Surname', field: 'last_name' },
        { from: 'E-mail', field: 'email' },
        { from: 'Org', field: 'company' },
        { from: 'Phone', field: 'phone' },
        { from: 'Profile', field: 'linkedin' },
        { from: 'Region', field: 'custom:region' },
        { field: 'time_zone' },
    ],
    left_out: ['Notes'],
    rows: [
        ['Ann', 'Lee', 'ann@example.com', 'Example Co', '+1 555 0100', 'linkedin.com/in/ann', 'North', 'Europe/Lisbon'],
        ['Bo', '', '', '', '', '', '', ''],
    ],
    ...fields,
});

describe('the request a press sends', () => {
    it('is one add of every row, in order, matched by the card’s key and given the fixed reason', () => {
        expect(listRequest(card(), 'cmp_1')).toEqual({
            campaign_id: 'cmp_1',
            match_by: 'email',
            contacts: [
                {
                    first_name: 'Ann',
                    last_name: 'Lee',
                    company: 'Example Co',
                    time_zone: 'Europe/Lisbon',
                    channels: [
                        { channel: 'email', value: 'ann@example.com' },
                        { channel: 'phone', value: '+1 555 0100' },
                        { channel: 'linkedin', value: 'linkedin.com/in/ann' },
                    ],
                    custom: { region: 'North' },
                },
                // An empty cell is no value, so it is left out, and a row of nothing but a name sends a name.
                { first_name: 'Bo' },
            ],
            reason: 'From a list given in the chat.',
        });
        expect(listReason).toBe('From a list given in the chat.');
    });

    it('sends a row with no cell at all as an empty person, which the runtime refuses for its missing key', () => {
        expect(listRequest(card({ rows: [['', '', '', '', '', '', '', '']] }), 'cmp_1').contacts).toEqual([{}]);
    });

    it('names a column by the field it fills, a custom one by its own name', () => {
        expect(fieldWord('first_name')).toBe('First name');
        expect(fieldWord('linkedin')).toBe('LinkedIn');
        expect(fieldWord('custom:deal_size')).toBe('deal_size');
        expect(mappingLine(card())).toBe(
            'Name → First name, Surname → Last name, E-mail → Email, Org → Company, Phone → Phone, Profile → LinkedIn, Region → region, Time zone; left out: Notes',
        );
        expect(mappingLine(card({ left_out: [] }))).not.toContain('left out');
    });
});

describe('what the person wrote, compared', () => {
    const rows = [
        ['Ann', '', '  ANN@Example.com ', '', '', '', '', ''],
        ['Bo', '', 'bo@example.com', '', '', '', '', ''],
        ['Cy', '', '', '', '', '', '', ''],
        ['Di', '', 'di@example.com', '', '', '', '', ''],
    ];

    it('looks for each address, trimmed and without regard to case, in the person’s own messages', () => {
        expect(addressesIn(card({ rows }), ['Here: ann@example.COM, and', 'one more di@example.com.'])).toEqual({
            total: 3,
            found: 2,
            missing: [1],
        });
    });

    it('finds an address only whole inside one message, never across two', () => {
        expect(addressesIn(card({ rows }), ['bo@exam', 'ple.com']).missing).toEqual([0, 1, 3]);
    });

    it('counts no address for a row without one, and none for a list without an email column', () => {
        expect(addressesIn(card({ rows }), []).total).toBe(3);
        expect(
            addressesIn(card({ match_by: 'linkedin', columns: [{ field: 'linkedin' }], rows: [['in/ann']] }), [
                'in/ann',
            ]),
        ).toEqual({ total: 0, found: 0, missing: [] });
    });
});

describe('what the runtime answered of a row', () => {
    it('says each row in the runtime’s words for it', () => {
        const item = (fields: object) => ({
            index: 0,
            status: 'added',
            contact_id: 'cnt_1',
            contact_created: true,
            state: 'enrolled',
            error: null,
            ...fields,
        });
        expect(rowOutcome(item({}))).toEqual({ added: true, words: 'Added, as a new person' });
        expect(rowOutcome(item({ contact_created: false }))).toEqual({
            added: true,
            words: 'Added, as the person Jason already held',
        });
        expect(rowOutcome(item({ status: 'already_member', contact_created: false }))).toEqual({
            added: false,
            words: 'Already in this agent',
        });
        expect(rowOutcome(item({ status: 'already_member', state: 'excluded' }))).toEqual({
            added: false,
            words: 'Taken out of this agent before; not added back',
        });
        expect(
            rowOutcome(
                item({
                    status: 'rejected',
                    contact_id: null,
                    state: null,
                    error: {
                        code: 'no_match_key',
                        message: 'the item carries no value for the requested match_by key.',
                    },
                }),
            ),
        ).toEqual({ added: false, words: 'Not added: the item carries no value for the requested match_by key.' });
        expect(rowOutcome(item({ status: 'later' }))).toEqual({ added: false, words: 'later' });
        expect(rowOutcome(undefined)).toEqual({ added: false, words: 'The runtime said nothing of this row.' });
    });
});
