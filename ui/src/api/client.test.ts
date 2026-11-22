import { afterEach, describe, expect, it, vi } from 'vitest';
import tooLong from '../../../docs/api/fixtures/campaign.update/new-agent-name-too-long.json';
import { ApiError, HttpApi } from './client';
import type { Session } from './session';

// The client against a stand-in fetch: a refusal becomes an ApiError that keeps what the runtime said, its details too.

/** A paired session, as far as the client reads one. */
const paired = { token: { value: 'tok' }, actorId: { value: 'ada' }, clear: () => undefined } as unknown as Session;

/** Answers every request with the recorded refusal of a name longer than the runtime keeps. */
function refusing(): void {
    vi.stubGlobal(
        'fetch',
        async () =>
            new Response(JSON.stringify(tooLong.response), {
                status: tooLong.status,
                headers: { 'content-type': 'application/json' },
            }),
    );
}

afterEach(() => {
    vi.unstubAllGlobals();
});

describe('a refusal the runtime answers', () => {
    it('keeps its code, message, retryable, status and the details it names each refused field by', async () => {
        refusing();
        const failure = await new HttpApi(paired)
            .call('campaign.update', { campaign_id: 'cmp_1', name: 'n'.repeat(201) }, 'write')
            .then(
                () => null,
                (error: unknown) => error,
            );
        expect(failure).toBeInstanceOf(ApiError);
        const { code, message, retryable, status, details } = failure as ApiError;
        expect({ code, message, retryable, status, details }).toEqual({
            code: 'validation_failed',
            message: 'Validation failed: name: too_long.',
            retryable: false,
            status: 400,
            details: [{ field: 'name', code: 'too_long', message: 'name must be at most 200 characters.' }],
        });
    });

    it('keeps no details where the runtime named none', async () => {
        vi.stubGlobal(
            'fetch',
            async () =>
                new Response(
                    JSON.stringify({ error: { code: 'campaign_not_found', message: 'No.', retryable: false } }),
                    {
                        status: 404,
                    },
                ),
        );
        const failure = (await new HttpApi(paired)
            .call('campaign.get', { campaign_id: 'cmp_9' })
            .catch((error) => error)) as ApiError;
        expect([failure.code, failure.details]).toEqual(['campaign_not_found', undefined]);
    });
});
