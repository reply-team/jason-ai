import { describe, expect, it } from 'vitest';
import type { JournalEntry } from '../api/types';
import type { DesktopFeedKind, DesktopLineEntry } from '../platform/desktop';
import { feedKinds, toFeedLine, toFeedRow } from './feed';

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
            'An approval was given',
            'An approval was declined',
            'A question was answered',
            'Agent archived',
        ]);
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
