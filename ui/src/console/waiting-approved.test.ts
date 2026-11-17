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

    it('says a message whose work expired was not sent', () => {
        expect(approvedSentence({ ...message, state: 'expired' })).toBe(
            'You approved: Message to Ines Duarte. It was not sent: the work expired.',
        );
    });

    it('says Jason is sending it only while the work is queued or running, and otherwise that it is not sent yet', () => {
        expect(approvedSentence({ ...message, state: 'held', why: 'it is waiting' })).toBe(
            'You approved: Message to Ines Duarte. It is not sent yet: it is waiting.',
        );
        expect(approvedSentence({ ...enrol, state: 'held', why: 'it is waiting' })).toBe(
            'You approved: Add to sequence. It is not done yet: it is waiting.',
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

    it('maps every state of the work item: queued or running, parked again, expired, and the three that end it', async () => {
        const states = {
            created: 'approved',
            scheduled: 'approved',
            processing: 'approved',
            awaiting_approval: 'held',
            waiting: 'held',
            expired: 'expired',
            succeeded: 'done',
            failed: 'failed',
            cancelled: 'cancelled',
        };
        for (const [status, state] of Object.entries(states)) {
            const waiting = waitingOf(`cmp_map_${status}`);
            waiting.approved.value = [message];
            await followApproved(reading({ wi_1: status }), waiting);
            expect(waiting.approved.value[0]!.state, status).toBe(state);
        }
    });

    it('keeps following an act that parked or expired, since it may be queued again, and drops the why when it moves', async () => {
        const waiting = waitingOf('cmp_follow_back');
        waiting.approved.value = [message];
        await followApproved(reading({ wi_1: 'waiting' }), waiting);
        expect(waiting.approved.value[0]).toMatchObject({ state: 'held', why: 'it is waiting' });
        await followApproved(reading({ wi_1: 'expired' }), waiting);
        expect(waiting.approved.value[0]!.state).toBe('expired');
        await followApproved(reading({ wi_1: 'succeeded' }), waiting);
        expect(waiting.approved.value[0]).toMatchObject({ state: 'done', why: undefined });
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
