import { describe, expect, it } from 'vitest';
import type { Approval, ApprovalSummary, Decision, DecisionSummary } from '../api/types';
import { fixtures } from '../test/fixtures';
import { keptAndMissing, stillAsking, toApprovalCard, toQuestionCard } from './waiting-model';

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
