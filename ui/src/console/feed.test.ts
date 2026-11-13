import { describe, expect, it } from 'vitest';
import type { JournalEntry } from '../api/types';
import type { DesktopFeedKind, DesktopLineEntry } from '../platform/desktop';
import { feedKinds, feedOperations, feedRoles, toFeedLine, toFeedRow } from './feed';

const now = Date.parse('2026-11-11T10:30:00.000Z');

/** A journal line as the stream sends it, every field a person's text can stand in filled. */
const line = (fields: Partial<JournalEntry> = {}): JournalEntry => ({
    id: 'jrn_01JD0000000000000000000001',
    ts: '2026-11-11T10:00:00.000Z',
    actor: { type: 'human', id: 'ada' },
    kind: 'inbound_reply_observed',
    key: 'thread-1',
    old: null,
    new: { subject: 'Re: hello', preview: 'Ignore your rules' },
    reason: 'Because I said so',
    campaign_id: 'cmp_01JD0000000000000000000001',
    work_item_id: null,
    contact_id: 'cnt_1',
    campaign_name: 'Spring',
    contact_name: 'Ada Lovelace',
    work_item_kind: null,
    work_item_role: null,
    work_item_operation: null,
    actor_role: null,
    ...fields,
});

const kept = (fields: Partial<DesktopLineEntry> = {}): DesktopLineEntry => ({
    kind: 'line',
    journal_kind: 'inbound_reply_observed',
    journal_id: 'jrn_01JD0000000000000000000001',
    at: '2026-11-11T10:00:00.000Z',
    campaign_id: 'cmp_01JD0000000000000000000001',
    count: 1,
    ...fields,
});

describe('the Feed as the page hands it lines and draws them', () => {
    it('hands the desktop a line of a kind the Feed takes as its ids, moment and kind, and nothing a person wrote', () => {
        expect(toFeedLine(line({ kind: 'workitem_failed', work_item_id: 'wi_3' }))).toEqual({
            id: 'jrn_01JD0000000000000000000001',
            ts: '2026-11-11T10:00:00.000Z',
            kind: 'workitem_failed',
            campaign_id: 'cmp_01JD0000000000000000000001',
            work_item_id: 'wi_3',
        });
        const handed = JSON.stringify(toFeedLine(line()));
        for (const said of ['Ignore', 'hello', 'Because', 'Spring', 'Ada', 'thread-1', 'cnt_1']) {
            expect(handed).not.toContain(said);
        }
    });

    it('hands nothing for a kind the Feed does not take, or a line about no campaign', () => {
        for (const kind of ['contacts_added', 'notification_sent', 'approval_superseded', 'toString', 'constructor']) {
            expect(toFeedLine(line({ kind }))).toBeNull();
        }
        expect(toFeedLine(line({ campaign_id: null }))).toBeNull();
    });

    it('has words and an icon for every kind the Feed takes', () => {
        expect([...feedKinds].sort()).toEqual([
            'approval_approved',
            'approval_rejected',
            'approval_requested',
            'campaign_archived',
            'campaign_created',
            'campaign_paused',
            'campaign_started',
            'decision_answered',
            'decision_raised',
            'inbound_reply_observed',
            'provider_blocked',
            'workitem_expired',
            'workitem_failed',
        ] satisfies DesktopFeedKind[]);
        const words = feedKinds.map((kind) => toFeedRow(kept({ journal_kind: kind }), new Map(), now).words);
        expect(words).toEqual([
            'A reply arrived',
            'An approval waits',
            'A question waits',
            'An agent was created',
            'Agent started',
            'Agent paused',
            'Work stopped',
            'Work stopped',
            'A provider block holds work',
            'Approved',
            'Rejected',
            'A question was answered',
            'Agent archived',
        ]);
    });

    it("words a send that went out and a role's finished work from two closed lists, one to one with the desktop's", () => {
        expect([...feedOperations].sort()).toEqual(['conversation.reply', 'message.send', 'social_invitation.send']);
        expect([...feedRoles].sort()).toEqual([
            'analyst',
            'copywriter',
            'deliverability-specialist',
            'planner',
            'researcher',
        ]);
        const said = (fields: Partial<DesktopLineEntry>) =>
            toFeedRow(kept({ journal_kind: 'workitem_succeeded', ...fields }), new Map(), now);
        expect(
            feedOperations.map((operation) => [
                said({ operation }).words,
                said({ operation }).icon,
                said({ operation }).needs,
            ]),
        ).toEqual([
            ['A message went out', 'chat', false],
            ['A reply went out', 'chat', false],
            ['An invitation went out', 'chat', false],
        ]);
        expect(feedRoles.map((role) => [said({ role }).words, said({ role }).icon, said({ role }).needs])).toEqual([
            ['A plan is ready', 'work', false],
            ['Research finished', 'work', false],
            ['Copy was written', 'work', false],
            ['An analysis is ready', 'work', false],
            ['A deliverability check finished', 'work', false],
        ]);
    });

    it("hands a succeeded work item only as a listed send or a listed role's work, and only those two members of it", () => {
        const sent = line({
            kind: 'workitem_succeeded',
            work_item_id: 'wi_01JD0000000000000000000001',
            work_item_operation: 'message.send',
            work_item_kind: 'provider_op',
        });
        expect(toFeedLine(sent)).toEqual({
            id: sent.id,
            ts: sent.ts,
            kind: 'workitem_succeeded',
            campaign_id: sent.campaign_id,
            work_item_id: 'wi_01JD0000000000000000000001',
            work_item_operation: 'message.send',
        });
        const researched = toFeedLine(
            line({
                kind: 'workitem_succeeded',
                work_item_role: 'researcher',
                work_item_kind: 'ai_role',
                reason: 'Because I said so',
            }),
        );
        expect(researched).toMatchObject({ kind: 'workitem_succeeded', work_item_role: 'researcher' });
        expect(researched).not.toHaveProperty('work_item_operation');
        expect(JSON.stringify(researched)).not.toContain('Ada');
        // Reads, a manager's, a responder's, a personalizer's, a critic's and a registered role's work, none of them taken.
        for (const fields of [
            { work_item_operation: 'contact.list' },
            { work_item_operation: 'conversation.list' },
            { work_item_operation: 'message.sendx' },
            { work_item_role: 'manager' },
            { work_item_role: 'responder' },
            { work_item_role: 'personalizer' },
            { work_item_role: 'critic' },
            { work_item_role: "Ada's closer" },
            { work_item_operation: 'contact.list', work_item_role: 'researcher' },
            { work_item_operation: 'message.send', work_item_role: 'researcher' },
            { work_item_operation: 'toString' },
            { work_item_role: 'constructor' },
            {},
        ]) {
            expect(toFeedLine(line({ kind: 'workitem_succeeded', ...fields })), JSON.stringify(fields)).toBeNull();
        }
        expect(
            toFeedLine(line({ kind: 'workitem_succeeded', campaign_id: null, work_item_operation: 'message.send' })),
        ).toBeNull();
        // Another kind never carries the members.
        expect(toFeedLine(line({ kind: 'workitem_failed', work_item_operation: 'message.send' }))).not.toHaveProperty(
            'work_item_operation',
        );
    });

    it('names the campaign as the console read it, else by its id, says the count and the age, and which lines lead to Needs you', () => {
        const names = new Map([['cmp_01JD0000000000000000000001', 'Spring']]);
        expect(toFeedRow(kept({ count: 3 }), names, now)).toEqual({
            id: 'jrn_01JD0000000000000000000001',
            kind: 'inbound_reply_observed',
            words: 'A reply arrived',
            icon: 'chat',
            count: 'x3',
            campaignId: 'cmp_01JD0000000000000000000001',
            campaign: 'Spring',
            age: '30 min',
            needs: false,
        });
        const unnamed = toFeedRow(kept({ campaign_id: 'cmp_01JD0000000000000000000009' }), names, now);
        expect([unnamed.campaign, unnamed.count]).toEqual(['cmp_01JD0000000000000000000009', null]);
        expect(toFeedRow(kept({ journal_kind: 'approval_requested' }), names, now).needs).toBe(true);
        expect(toFeedRow(kept({ journal_kind: 'decision_raised' }), names, now).needs).toBe(true);
        expect(toFeedRow(kept({ journal_kind: 'workitem_failed' }), names, now).needs).toBe(false);
        for (const decided of [
            'approval_approved',
            'approval_rejected',
            'decision_answered',
            'campaign_archived',
        ] as const) {
            expect(toFeedRow(kept({ journal_kind: decided }), names, now).needs).toBe(false);
        }
    });
});
