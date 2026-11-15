import type { FakeApi } from './fake-api';

/**
 * An agent's chat reads what waits in its campaign (`approval.list` and `decision.list` of that campaign). A test of
 * something else is told that nothing waits there, with the recorded empty page of a campaign's pending ones; every other
 * request to the two lists takes the cases the test offers.
 */
export function nothingWaitsInChats(fake: FakeApi): FakeApi {
    const scoped = (body: object) => 'campaign_id' in body && (body as { status?: string }).status === 'pending';
    return fake
        .answerAnyRequest('approval.list', (body) => (scoped(body) ? 'approval.list/summer-pending' : undefined))
        .answerAnyRequest('decision.list', (body) => (scoped(body) ? 'decision.list/summer-pending' : undefined));
}
