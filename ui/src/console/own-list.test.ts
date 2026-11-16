import { describe, expect, it } from 'vitest';
import { cardLayout, optionsOf, readCard, type OwnListCard } from './cards';
import { choiceState, locksChoice, newChat, type Chat, type Entry } from './chat-model';
import { addressesIn, fieldWord, listReason, listRequest, mappingLine, ownMessages, rowOutcome } from './own-list';

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

    it('finds an address only where it stands whole, never inside a longer address', () => {
        const one = (address: string) => card({ rows: [['A', '', address, '', '', '', '', '']] });
        // A domain cut short, and a mailbox that ends another one, are not what the person wrote.
        expect(addressesIn(one('bo@example.co'), ['bo@example.com']).missing).toEqual([0]);
        expect(addressesIn(one('ann@example.test'), ['joann@example.test']).missing).toEqual([0]);
        expect(addressesIn(one('ann@example.test'), ['ann@example.test.uk']).missing).toEqual([0]);
        expect(addressesIn(one('ann@example.test'), ['ann@example.test-mail.org']).missing).toEqual([0]);
        expect(addressesIn(one('ann@example.test'), ['x.ann@example.test']).missing).toEqual([0]);
        // Stops, brackets and commas around it are not part of it.
        for (const said of [
            'ann@example.test.',
            '<ann@example.test>',
            'ann@example.test,',
            '(ann@example.test)',
            'Ann: ann@example.test',
            'ann@example.test',
        ])
            expect(addressesIn(one('ann@example.test'), [said]).missing, said).toEqual([]);
        // A later whole one counts where an earlier hit stood inside another address.
        expect(addressesIn(one('ann@example.test'), ['joann@example.test, ann@example.test']).missing).toEqual([]);
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
        // A refusal of a value says it in the runtime's words for the value, not by the request's zero-based index.
        expect(
            rowOutcome(
                item({
                    status: 'rejected',
                    error: {
                        code: 'validation_failed',
                        message: 'Validation failed: contacts[4].channels[0].value: invalid.',
                        details: [
                            {
                                field: 'contacts[4].channels[0].value',
                                code: 'invalid',
                                message: 'an email value is a bare mailbox address with a dotted domain.',
                            },
                        ],
                    },
                }),
            ),
        ).toEqual({ added: false, words: 'Not added: an email value is a bare mailbox address with a dotted domain.' });
        expect(rowOutcome(item({ status: 'later' }))).toEqual({ added: false, words: 'later' });
        expect(rowOutcome(undefined)).toEqual({ added: false, words: 'The runtime said nothing of this row.' });
    });
});

describe('the person’s own messages', () => {
    const fenced = (value: unknown) => `\`\`\`card\n${JSON.stringify(value)}\n\`\`\``;
    const choice = {
        kind: 'choice',
        question: 'Which one?',
        options: [{ label: 'ann@example.test' }, { label: 'Skip' }],
    };
    const chat = (entries: Entry[], fields: Partial<Chat> = {}): Chat => ({
        ...newChat('chat_1', null, { id: 'cmp_1', name: 'Agent' }),
        entries,
        ...fields,
    });

    it('are the person’s entries, but an answer a click on a choice sent, whose words the AI wrote', () => {
        expect(
            ownMessages(
                chat([
                    { kind: 'user', text: 'My list: bo@example.test' },
                    { kind: 'text', text: `Which one?\n\n${fenced(choice)}` },
                    { kind: 'user', text: 'ann@example.test' },
                    { kind: 'text', text: 'Noted.' },
                    { kind: 'user', text: 'and cy@example.test' },
                ]),
            ),
        ).toEqual(['My list: bo@example.test', 'and cy@example.test']);
    });

    it('keep words the person wrote after a choice that are none of its labels', () => {
        expect(
            ownMessages(
                chat([
                    { kind: 'text', text: fenced(choice) },
                    { kind: 'user', text: 'Neither: dee@example.test' },
                ]),
            ),
        ).toEqual(['Neither: dee@example.test']);
        // A label typed again later, with the choice no longer last, is the person's own words.
        expect(
            ownMessages(
                chat([
                    { kind: 'text', text: fenced(choice) },
                    { kind: 'user', text: 'Skip' },
                    { kind: 'text', text: 'Fine.' },
                    { kind: 'user', text: 'ann@example.test' },
                ]),
            ),
        ).toEqual(['ann@example.test']);
    });

    it('leave out exactly the answers the chat itself draws as chosen, so the two rules never part', () => {
        const spaced = { ...choice, options: [{ label: ' Skip ' }, { label: 'ann@example.test' }] };
        const cases: { entries: Entry[]; left: string[] }[] = [
            // Chosen: a label as sent, a card's label with spaces round it, a gap between that locks nothing.
            {
                entries: [
                    { kind: 'text', text: fenced(choice) },
                    { kind: 'user', text: ' Skip' },
                ],
                left: [' Skip'],
            },
            {
                entries: [
                    { kind: 'text', text: fenced(spaced) },
                    { kind: 'user', text: 'Skip' },
                ],
                left: ['Skip'],
            },
            {
                entries: [
                    { kind: 'text', text: fenced(choice) },
                    { kind: 'gap' },
                    { kind: 'user', text: 'ann@example.test' },
                ],
                left: ['ann@example.test'],
            },
            // Not chosen: another case of a label, an entry that locks the choice first.
            {
                entries: [
                    { kind: 'text', text: fenced(choice) },
                    { kind: 'user', text: 'skip' },
                ],
                left: [],
            },
            {
                entries: [
                    { kind: 'text', text: fenced(choice) },
                    { kind: 'error', text: 'The turn failed.' },
                    { kind: 'user', text: 'Skip' },
                ],
                left: [],
            },
        ];
        for (const each of cases) {
            const opened = chat(each.entries);
            const kept = ownMessages(opened);
            const users = opened.entries.flatMap((entry) => (entry.kind === 'user' ? [entry.text] : []));
            const left = users.filter((text) => !kept.includes(text));
            expect(left, JSON.stringify(each.entries)).toEqual(each.left);
            // The chat's own rule, read the way the chat reads it, names the very same entries.
            const chosen: string[] = [];
            for (const [index, placed] of cardLayout(opened.entries).cards) {
                const reading = readCard(placed, { place: 'agent', campaignId: 'cmp_1', ended: true });
                if (reading.state !== 'card') continue;
                const labels = optionsOf(reading.card).map((option) => option.label);
                if (choiceState(opened, index, labels, null, false).state !== 'chosen') continue;
                const next = opened.entries.find((entry, at) => at > index && locksChoice(entry.kind));
                if (next?.kind === 'user') chosen.push(next.text);
            }
            expect(left, JSON.stringify(each.entries)).toEqual(chosen);
        }
    });
});
