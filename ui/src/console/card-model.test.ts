import { describe, expect, it } from 'vitest';
import type { CampaignActivityRow, CampaignSummaryRow, Contact } from '../api/types';
import {
    asOf,
    capped,
    drawsAs,
    lastReading,
    modesInForce,
    monthCounts,
    needsLine,
    nowCounts,
    peopleEmpty,
    peopleFooter,
    peoplePlace,
    personRow,
    statusSentence,
} from './card-model';

// The chat's data cards, read the way the page reads them: which card of a conversation reads itself and which stands
// as a stub, the sentence a campaign's status gives, the counts a status card shows, the footer and the empty words of
// a people list, and the count line of what waits for a decision.

const summary = (fields: Partial<CampaignSummaryRow> = {}): CampaignSummaryRow => ({
    campaign_id: 'cmp_1',
    people: 12,
    waiting: 2,
    failed: 0,
    working: 2,
    new: 8,
    idle: 1,
    sequence: 0,
    out: 1,
    removed: 0,
    sequence_linked: true,
    ...fields,
});
const month: CampaignActivityRow = {
    campaign_id: 'cmp_1',
    people: 12,
    added: 12,
    worked_on: 1,
    contacted: 3,
    replied: 1,
    bounced: 4,
    opted_out: 5,
    removed: 0,
};

describe('which card of a conversation reads', () => {
    it('is the last card that reads; a choice and a card that did not read are passed over', () => {
        expect(
            lastReading([
                { index: 1, kind: 'people_list' },
                { index: 3, kind: 'agent_status' },
                { index: 5, kind: 'choice' },
                { index: 7, kind: null },
            ]),
        ).toBe(3);
        expect(lastReading([{ index: 2, kind: 'choice' }])).toBeNull();
        expect(lastReading([])).toBeNull();
        // A kind this build draws only as a line that it is not drawn reads nothing, so the card before it still reads.
        expect(
            lastReading([
                { index: 1, kind: 'needs' },
                { index: 4, kind: 'accounts' },
            ]),
        ).toBe(1);
        // A plan reads what is on record, so a plan after a data card is the one that reads.
        expect(
            lastReading([
                { index: 1, kind: 'needs' },
                { index: 4, kind: 'plan' },
            ]),
        ).toBe(4);
    });

    it('draws a card already read as it was read, the last as a read, and every earlier one as a stub', () => {
        expect(drawsAs(3, 3, false)).toBe('read');
        expect(drawsAs(1, 3, false)).toBe('stub');
        expect(drawsAs(1, 3, true)).toBe('held');
        expect(drawsAs(3, 3, true)).toBe('held');
        expect(drawsAs(1, null, false)).toBe('stub');
    });

    it('says when it was read in the person’s own clock, to the minute', () => {
        expect(asOf(Date.parse('2026-10-16T14:02:59.000Z'), 'UTC')).toBe('As of 14:02');
        expect(asOf(Date.parse('2026-10-16T14:02:00.000Z'), 'Europe/Berlin')).toBe('As of 16:02');
    });
});

describe('the status card', () => {
    it('gives one sentence for each status, chosen from the status alone', () => {
        expect(statusSentence('draft')).toBe('It is a draft: none of its work starts until it is started.');
        expect(statusSentence('active')).toBe('It is active.');
        expect(statusSentence('paused')).toBe('It is paused: none of its work starts but what stops it.');
        expect(statusSentence('archived')).toBe('It is archived.');
        // A status a newer runtime names is said as it is, and nothing is inferred from it.
        expect(statusSentence('frozen')).toBe('Its status is frozen.');
    });

    it('shows the counts of now, In the sequence only when a sequence is linked', () => {
        expect(nowCounts(summary())).toEqual([
            { key: 'people', label: 'People', value: 12 },
            { key: 'waiting', label: 'People waiting on you', value: 2 },
            { key: 'working', label: 'Working', value: 2 },
            { key: 'failed', label: 'Failed', value: 0 },
            { key: 'sequence', label: 'In the sequence', value: 0 },
            { key: 'out', label: 'Out', value: 1 },
        ]);
        expect(nowCounts(summary({ sequence_linked: false })).map((tile) => tile.key)).toEqual([
            'people',
            'waiting',
            'working',
            'failed',
            'out',
        ]);
    });

    it('shows the last thirty days’ four counts and nothing the runtime does not count', () => {
        expect(monthCounts(month)).toEqual([
            { key: 'contacted', label: 'Contacted', value: 3 },
            { key: 'replied', label: 'Replied', value: 1 },
            { key: 'bounced', label: 'Bounced', value: 4 },
            { key: 'opted_out', label: 'Opted out', value: 5 },
        ]);
        const words = [...nowCounts(summary()), ...monthCounts(month)].map((tile) => tile.label).join(' ');
        expect(words).not.toMatch(/Meeting|%|rate|Interested/i);
    });

    it('says how sends are decided now in the send-modes screen’s words', () => {
        expect(
            modesInForce({
                cold: { mode: 'every_message', source: 'default' },
                warm: { mode: 'automatic', source: 'campaign' },
            }),
        ).toEqual([
            { kind: 'People who have not replied yet', mode: 'Every message waits for you' },
            { kind: 'People who have replied', mode: 'Goes automatically' },
        ]);
        expect(modesInForce(undefined)).toEqual([]);
        expect(
            modesInForce({ cold: { mode: 'later', source: 'default' }, warm: { mode: 'later', source: 'default' } }),
        ).toEqual([
            { kind: 'People who have not replied yet', mode: 'later' },
            { kind: 'People who have replied', mode: 'later' },
        ]);
    });
});

describe('the people list', () => {
    it('counts the agent’s people in its footer only with a campaign and no search', () => {
        expect(peopleFooter({ campaign: true, search: false, count: 12, more: true })).toBe('12 people in this agent');
        expect(peopleFooter({ campaign: true, search: false, count: 1, more: false })).toBe('1 person in this agent');
        expect(peopleFooter({ campaign: true, search: false, standing: 'waiting', count: 2, more: false })).toBe(
            '2 people in this agent are counted under Waiting',
        );
        expect(peopleFooter({ campaign: true, search: true, count: null, more: true })).toBe('More on the People page');
        expect(peopleFooter({ campaign: true, search: true, count: null, more: false })).toBeNull();
        expect(peopleFooter({ campaign: false, search: false, count: null, more: true })).toBe(
            'More on the People page',
        );
        expect(peopleFooter({ campaign: false, search: true, count: null, more: false })).toBeNull();
    });

    it('says why it is empty', () => {
        expect(peopleEmpty({ campaign: true, search: true })).toBe('Nobody matches.');
        expect(peopleEmpty({ campaign: true, search: false, standing: 'failed' })).toBe('Nobody matches.');
        expect(peopleEmpty({ campaign: true, search: false })).toBe('Nobody is in this agent yet.');
        expect(peopleEmpty({ campaign: false, search: true })).toBe('Nobody matches.');
        expect(peopleEmpty({ campaign: false, search: false })).toBe('No people yet.');
    });

    it('shows a person by name, company, title and the kind of their primary channel, never its value', () => {
        const ines: Contact = {
            id: 'cnt_1',
            first_name: 'Ines',
            last_name: 'Duarte',
            company: 'Kavali',
            title: null,
            channels: [
                { channel: 'linkedin', value: 'https://example.test/in/ines', primary: false },
                { channel: 'email', value: 'ines@example.test', primary: true },
            ],
            campaigns: [
                {
                    campaign_id: 'cmp_1',
                    campaign_name: 'Spring launch',
                    state: 'enrolled',
                    added_at: '2026-10-16T09:00:00.000Z',
                    updated_at: '2026-10-16T09:00:00.000Z',
                    standing: ['waiting', 'sequence'],
                },
            ],
        };
        expect(personRow(ines, 'cmp_1')).toEqual({
            id: 'cnt_1',
            name: 'Ines Duarte',
            company: 'Kavali',
            title: null,
            channel: 'Email',
            standing: ['Waiting', 'Sequence'],
        });
        expect(personRow(ines).standing).toEqual([]);
        const nameless = personRow({ ...ines, first_name: null, last_name: null, channels: [] });
        expect(nameless.name).toBe('No name on record');
        expect(nameless.channel).toBeNull();
        expect(JSON.stringify(personRow(ines, 'cmp_1'))).not.toContain('example.test');
    });
});

describe('where a people list’s footer leads', () => {
    it('opens the agent’s People tab under the count it counts, else the People page with the card’s own filters', () => {
        expect(peoplePlace({}, 'cmp_1')).toBe('#/campaigns/cmp_1/people');
        expect(peoplePlace({ standing: 'waiting' }, 'cmp_1')).toBe('#/campaigns/cmp_1/people?filter=waiting');
        expect(peoplePlace({ search: 'ines' }, null)).toBe('#/people?q=ines');
        expect(peoplePlace({}, null)).toBe('#/people');
        // A search with a standing keeps the standing, as the People page's stage.
        expect(peoplePlace({ search: 'ines', standing: 'waiting' }, 'cmp_1')).toBe(
            '#/people?q=ines&campaign=cmp_1&stage=waiting',
        );
        // Removed is no stage the People page offers: the agent and the search stay.
        expect(peoplePlace({ search: 'ines', standing: 'removed' }, 'cmp_1')).toBe('#/people?q=ines&campaign=cmp_1');
    });
});

describe('what waits for a decision', () => {
    it('lists ten rows at most and counts the rest', () => {
        const twelve = Array.from({ length: 12 }, (_, at) => at);
        expect(capped(twelve)).toEqual({ shown: twelve.slice(0, 10), rest: 2 });
        expect(capped([1, 2])).toEqual({ shown: [1, 2], rest: 0 });
    });

    it('counts items, never people, and says "50 or more" where a list has a further page', () => {
        expect(needsLine({ count: 2, more: false }, { count: 2, more: false })).toBe(
            '2 approvals and 2 questions waiting for your decision',
        );
        expect(needsLine({ count: 1, more: false }, { count: 1, more: false })).toBe(
            '1 approval and 1 question waiting for your decision',
        );
        expect(needsLine({ count: 50, more: true }, { count: 0, more: false })).toBe(
            '50 or more approvals and 0 questions waiting for your decision',
        );
        expect(needsLine({ count: 3, more: false }, { count: 50, more: true })).toBe(
            '3 approvals and 50 or more questions waiting for your decision',
        );
    });
});
