import { describe, expect, it } from 'vitest';
import { ApiError, type Api } from '../api/client';
import type { SenderAccount, SenderListRequest, SenderListResponse } from '../api/types';
import { lookupFor, readSender, senderMark, senderReads } from './senders';
import { senderLine, type SenderAsked } from './senders-model';

const account = (externalId: string, name: string): SenderAccount => ({
    external_id: externalId,
    channel: 'email',
    name,
    address: `${externalId}@example.test`,
    status: 'active',
    owner: 'own',
});

const of = (externalId: string): SenderAsked => ({
    campaignId: 'cmp_1',
    channel: 'email',
    externalId,
    pluginId: 'reply',
});

/** The provider's accounts in pages of one: the first page holds `s_b`, the second `s_a`. */
const pages: SenderListResponse[] = [
    { plugin_id: 'reply', items: [account('s_b', 'Second')], has_more: true },
    { plugin_id: 'reply', items: [account('s_a', 'First')], has_more: false },
];

/** A runtime answering `sender.list` by offset, or failing every read once `failing` is set. */
function provider(): { api: Api; calls: SenderListRequest[]; fail: () => void } {
    const calls: SenderListRequest[] = [];
    let failing = false;
    const api = {
        call: (_operation: string, body: SenderListRequest) => {
            calls.push(body);
            if (failing) return Promise.reject(new ApiError('no_route', 'No route for sender.list.', false, 409));
            return Promise.resolve(pages[body.offset ?? 0]);
        },
    } as unknown as Api;
    return { api, calls, fail: () => (failing = true) };
}

describe('the reads behind the sender lines of cards drawn at different moments', () => {
    it('keeps an earlier card’s own answer when a later card’s read stops before the earlier one’s account', async () => {
        const { api, calls } = provider();
        const reads = senderReads();
        const first = senderMark();
        await readSender(api, reads, of('s_a'), first);
        const later = senderMark();
        await readSender(api, reads, of('s_b'), later);

        expect(calls.map((call) => call.offset ?? 0)).toEqual([0, 1, 0]);
        expect(senderLine(lookupFor(reads, of('s_a'), first), of('s_a'))).toEqual({
            kind: 'named',
            provider: 'Reply',
            name: 'First',
        });
        expect(senderLine(lookupFor(reads, of('s_b'), later), of('s_b'))).toEqual({
            kind: 'named',
            provider: 'Reply',
            name: 'Second',
        });
    });

    it('never shows an earlier card the failure of a later card’s read', async () => {
        const { api, fail } = provider();
        const reads = senderReads();
        const first = senderMark();
        await readSender(api, reads, of('s_a'), first);
        fail();
        const later = senderMark();
        await readSender(api, reads, of('s_b'), later);

        expect(senderLine(lookupFor(reads, of('s_a'), first), of('s_a'))).toMatchObject({ kind: 'named' });
        expect(senderLine(lookupFor(reads, of('s_b'), later), of('s_b'))).toMatchObject({ kind: 'unread' });
    });

    it('shares one read between cards drawn before it began', async () => {
        const { api, calls } = provider();
        const reads = senderReads();
        const one = senderMark();
        const two = senderMark();
        await Promise.all([readSender(api, reads, of('s_a'), one), readSender(api, reads, of('s_b'), two)]);

        expect(calls.map((call) => call.offset ?? 0)).toEqual([0, 1]);
        expect(senderLine(lookupFor(reads, of('s_b'), two), of('s_b'))).toMatchObject({ name: 'Second' });
    });
});
