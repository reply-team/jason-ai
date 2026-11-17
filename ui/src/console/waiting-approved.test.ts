import { describe, expect, it } from 'vitest';
import type { Api } from '../api/client';
import { followApproved, waitingOf } from './waiting';
import { approvedSentence, type Approved } from './waiting-model';

const message: Approved = {
    approvalId: 'apr_1',
    workItemId: 'wi_1',
    operation: 'message.send',
    what: 'Message',
    to: 'Ines Duarte',
    state: 'approved',
};
const enrol: Approved = {
    ...message,
    approvalId: 'apr_2',
    workItemId: 'wi_2',
    operation: 'campaign.enroll',
    what: 'Add to sequence',
    to: null,
};

describe('what a chat says of an approval its card approved', () => {
    it('says what was approved and to whom, and that a message was sent only once its work succeeded', () => {
        expect(approvedSentence(message)).toBe('You approved: Message to Ines Duarte. Jason is sending it.');
        expect(approvedSentence({ ...message, state: 'done' })).toBe(
            'You approved: Message to Ines Duarte. It was sent.',
        );
    });

    it('never says a message was sent when its work failed or was cancelled', () => {
        expect(approvedSentence({ ...message, state: 'failed' })).toBe(
            'You approved: Message to Ines Duarte. It was not sent: the work failed.',
        );
        expect(approvedSentence({ ...message, state: 'cancelled' })).toBe(
            'You approved: Message to Ines Duarte. It was not sent: the work was cancelled.',
        );
    });

    it('says an act that is no message is done, not sent', () => {
        expect(approvedSentence({ ...enrol, state: 'approved' })).toBe(
            'You approved: Add to sequence. Jason is doing it.',
        );
        expect(approvedSentence({ ...enrol, state: 'done' })).toBe('You approved: Add to sequence. It is done.');
    });
});

/** An API that answers each work item read with the status the test gave that item; one it has none for fails. */
const reading = (status: Record<string, string>): Api & { reads: string[] } => {
    const reads: string[] = [];
    return {
        reads,
        call: (_operation: string, body: object) => {
            const id = (body as { work_item_id: string }).work_item_id;
            reads.push(id);
            const known = status[id];
            return known === undefined
                ? Promise.reject(new Error('unreachable'))
                : Promise.resolve({ id, status: known });
        },
    } as unknown as Api & { reads: string[] };
};

describe('following an approved act to the end of its work', () => {
    it('moves each approval to the state its work item ended in, and leaves one still running as approved', async () => {
        const waiting = waitingOf('cmp_follow');
        waiting.approved.value = [message, enrol, { ...message, approvalId: 'apr_3', workItemId: 'wi_3' }];
        const api = reading({ wi_1: 'succeeded', wi_2: 'processing', wi_3: 'failed' });
        await followApproved(api, waiting);
        expect(waiting.approved.value.map((each) => [each.approvalId, each.state])).toEqual([
            ['apr_1', 'done'],
            ['apr_2', 'approved'],
            ['apr_3', 'failed'],
        ]);
    });

    it('reads an ended one no more, and keeps what it knew when a read fails', async () => {
        const waiting = waitingOf('cmp_follow_two');
        waiting.approved.value = [{ ...message, state: 'done' }, enrol];
        const api = reading({});
        await followApproved(api, waiting);
        expect(api.reads).toEqual(['wi_2']);
        expect(waiting.approved.value.map((each) => each.state)).toEqual(['done', 'approved']);
    });
});
