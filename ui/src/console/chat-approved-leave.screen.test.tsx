import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Api } from '../api/client';
import { ApiProvider } from '../api/context';
import { FakeApi } from '../test/fake-api';
import { forgetWaiting, waitingOf } from './waiting';
import { WaitingCards } from './waiting-cards';
import { APPROVED_LINE_LEAVES_MS, type Approved } from './waiting-model';

// An approval decided on its card stays under the conversation as a line. One that reached its happy end leaves five
// seconds after it first showed that; one still on its way waits until it gets there; a failure stays.

const message: Approved = {
    approvalId: 'apr_1',
    workItemId: 'wi_1',
    operation: 'message.send',
    what: 'Message',
    to: 'Ines Duarte',
    state: 'approved',
};

function draw(campaign: string, lines: readonly Approved[]) {
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/empty')
        .answer('decision.list', 'decision.list/empty');
    render(
        <ApiProvider api={fake as unknown as Api}>
            <WaitingCards campaignId={campaign} />
        </ApiProvider>,
    );
    const waiting = waitingOf(campaign);
    act(() => {
        waiting.approved.value = lines;
    });
    return waiting;
}

const line = (re: RegExp) => screen.queryByText(re);
const tick = (ms: number) => act(() => vi.advanceTimersByTimeAsync(ms));

describe('the lines of what a chat approved', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        cleanup();
        forgetWaiting();
        vi.useRealTimers();
    });

    it('takes a line away five seconds after it first said "It was sent."', async () => {
        draw('cmp_leave_sent', [{ ...message, state: 'done' }]);
        await tick(0);
        expect(line(/It was sent\./)).not.toBeNull();
        await tick(APPROVED_LINE_LEAVES_MS - 1);
        expect(line(/It was sent\./)).not.toBeNull();
        await tick(1);
        expect(line(/It was sent\./)).toBeNull();
    });

    it('takes an act that is no message away the same way, on "It is done."', async () => {
        draw('cmp_leave_done', [
            { ...message, operation: 'campaign.enroll', what: 'Add to sequence', to: null, state: 'done' },
        ]);
        await tick(APPROVED_LINE_LEAVES_MS);
        expect(line(/It is done\./)).toBeNull();
    });

    it('keeps a line that is still on its way, and starts the five seconds when it turns final', async () => {
        const waiting = draw('cmp_leave_late', [message]);
        await tick(60_000);
        expect(line(/Jason will send it\./)).not.toBeNull();
        act(() => {
            waiting.approved.value = [{ ...message, state: 'running' }];
        });
        await tick(60_000);
        expect(line(/Jason is sending it\./)).not.toBeNull();
        act(() => {
            waiting.approved.value = [{ ...message, state: 'done' }];
        });
        await tick(APPROVED_LINE_LEAVES_MS - 1);
        expect(line(/It was sent\./)).not.toBeNull();
        await tick(1);
        expect(line(/It was sent\./)).toBeNull();
    });

    it('keeps the lines that are the only sign of a failure', async () => {
        draw('cmp_leave_failed', [
            { ...message, state: 'held', why: 'it is waiting' },
            { ...message, approvalId: 'apr_2', state: 'failed' },
        ]);
        await tick(10 * APPROVED_LINE_LEAVES_MS);
        expect(line(/It is not sent yet: it is waiting\./)).not.toBeNull();
        expect(line(/It was not sent: the work failed\./)).not.toBeNull();
    });

    it('takes one line away without touching another that is still on its way', async () => {
        draw('cmp_leave_two', [
            { ...message, state: 'done' },
            { ...message, approvalId: 'apr_2', state: 'running' },
        ]);
        await tick(APPROVED_LINE_LEAVES_MS);
        expect(line(/It was sent\./)).toBeNull();
        expect(line(/Jason is sending it\./)).not.toBeNull();
    });
});
