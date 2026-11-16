import { describe, expect, it } from 'vitest';
import type { Approval, ApprovalSummary, Decision, DecisionSummary } from '../api/types';
import { fixtures } from '../test/fixtures';
import {
    approvalGate,
    keptAndMissing,
    readingSentence,
    stillAsking,
    toApprovalCard,
    toQuestionCard,
    unreadSentence,
} from './waiting-model';

const now = Date.parse('2026-10-01T10:09:00.000Z');
const rows = fixtures['approval.list/by-campaign-pending'].response.items as ApprovalSummary[];
const email = fixtures['approval.get/email-message'].response as unknown as Approval;
const enroll = fixtures['approval.get/other-pending'].response as unknown as Approval;

describe('an approval’s card in a chat', () => {
    it('says what the row says until the approval is read whole, and never guesses a message’s words', () => {
        const card = toApprovalCard(rows[0]!, undefined, now);
        expect(card).toMatchObject({ id: 'apr_1', what: 'Add to sequence', to: 'Ada Example', age: '9 min' });
        expect([card.from, card.channel, card.subject, card.body, card.intent]).toEqual([null, null, null, null, null]);
        expect(card.message).toBe(false);
    });

    it('draws a message from its whole read: to whom, from which mailbox, the channel, the subject and the body', () => {
        const card = toApprovalCard({ ...rows[0]!, operation: 'message.send' }, email, now);
        expect(card).toMatchObject({
            what: 'Message',
            to: 'Ines Duarte, ines@example.test',
            from: 'owner@example.test',
            channel: 'Email',
            subject: 'Notes from the meetup',
            body: 'Hi Ines, thanks for the talk at the meetup.',
            intent: null,
            message: true,
        });
    });

    it('says what will happen for an approval that carries no message', () => {
        const card = toApprovalCard(rows[1]!, enroll, now);
        expect(card.intent).toBe('Put Grace Hopper into the campaign at the provider.');
        expect(card.title).toBe('Enroll Oli Example');
    });
});

describe('a question’s card in a chat', () => {
    it('has its options once read whole, and none before', () => {
        const row = fixtures['decision.list/by-campaign-pending'].response.items[0] as DecisionSummary;
        const whole = fixtures['decision.get/pending-with-options'].response as unknown as Decision;
        expect(toQuestionCard(row, undefined, now).options).toEqual([]);
        expect(toQuestionCard(row, whole, now).options.map((each) => each.label)).toEqual([
            'Pause until the news',
            'Keep going',
        ]);
    });
});

describe('what the chat keeps between reads', () => {
    it('keeps the whole reads of the rows still listed, and names the rows not read yet', () => {
        expect(keptAndMissing({ a: 1, b: 2 }, ['b', 'c'])).toEqual({ kept: { b: 2 }, missing: ['c'] });
    });

    it('says an edited message is asked again until another approval of its work item is listed', () => {
        const reasked = [{ workItemId: 'wi_1', approvalId: 'apr_1' }];
        expect(stillAsking(reasked, [rows[0]!])).toEqual(reasked);
        expect(stillAsking(reasked, [])).toEqual(reasked);
        expect(stillAsking(reasked, [{ ...rows[0]!, id: 'apr_7' }])).toEqual([]);
    });
});

describe('when an approval may be approved where it is drawn', () => {
    const failed = { code: null, message: 'Failed to fetch' };

    it('is live only once the approval has been read whole, whatever it does', () => {
        for (const operation of ['message.send', 'social_invitation.send', 'campaign.enroll', 'x.y']) {
            expect(approvalGate({ ...email, operation }, undefined)).toEqual({ live: true });
            expect(approvalGate(undefined, undefined)).toEqual({ live: false, reading: true });
        }
    });

    it('says what the runtime said while the read failed, and a whole read outweighs an earlier failure', () => {
        expect(approvalGate(undefined, failed)).toEqual({ live: false, unread: failed });
        expect(approvalGate(enroll, failed)).toEqual({ live: true });
    });

    it('says an approval is being read in the words of where it is drawn: a card shows it whole, a row reads it', () => {
        expect(readingSentence('chat')).toBe(
            'This approval is being read; it cannot be approved until it is shown here whole.',
        );
        expect(readingSentence('needs')).toBe(
            'This approval is being read; it cannot be approved until it has been read whole.',
        );
    });

    it('says where the person can still open an approval it could not read', () => {
        expect(unreadSentence('chat')).toContain('Details opens it in Needs you');
        expect(unreadSentence('needs')).toContain('Read again asks for it once more');
        for (const where of ['chat', 'needs'] as const)
            expect(unreadSentence(where)).toMatch(/^This approval could not be read, so it cannot be approved here; /);
    });

    it('says nothing of what the runtime said when its time ran out with no answer', () => {
        const unanswered = {
            code: null,
            message: 'The runtime did not answer within 30 seconds.',
            unanswered: true,
        } as const;
        for (const where of ['chat', 'needs'] as const) {
            expect(unreadSentence(where)).toMatch(/\. The runtime said:$/);
            expect(unreadSentence(where, { code: 'not_found', message: 'gone' })).toMatch(/\. The runtime said:$/);
            expect(unreadSentence(where, unanswered)).toMatch(/^This approval could not be read, .*\.$/);
            expect(unreadSentence(where, unanswered)).not.toContain('The runtime said');
        }
    });
});
