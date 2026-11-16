import { createBody } from '../screens/campaigns/kind';
import { archivedLeftOut } from '../screens/campaigns/store';
import { matches, type FakeApi } from './fake-api';
import type { FixtureName } from './fixtures';

/** The agents as the header over an agent's chat or page reads them, by id; any other id is one the runtime lacks. */
const agents: Readonly<Record<string, FixtureName>> = {
    cmp_1: 'campaign.get/details',
    cmp_2: 'campaign.get/unlinked',
    cmp_3: 'campaign.get/summer',
    cmp_4: 'campaign.get/archived',
    cmp_5: 'campaign.get/card-plan-draft',
};

const campaignOf = (body: object): string => {
    const id = (body as { campaign_id?: unknown }).campaign_id;
    return typeof id === 'string' ? id : '';
};

const onlyOf = (body: object): string => {
    const ids = (body as { campaign_ids?: unknown }).campaign_ids;
    return Array.isArray(ids) && ids.length === 1 && typeof ids[0] === 'string' ? ids[0] : '';
};

/**
 * The agent's header in the console reads the agent the area shows (`campaign.get`), for a started agent with no
 * provider campaign pinned whether one is being made (`workitem.list` of its unfinished provider operations), and for an
 * agent the console's list does not hold its people (`campaign.summary` of it alone). A test of something else is given
 * those reads, from recordings, where nothing the test said answers them, and only in the very shapes the header asks
 * them: any other read of the same operations is still the test's to answer, and unanswered fails it. What a test offers
 * stays its own, and no queued case is taken by these.
 */
export function agentHeads(fake: FakeApi): FakeApi {
    return fake
        .otherwise('campaign.get', (body) =>
            matches({ campaign_id: campaignOf(body) }, body)
                ? (agents[campaignOf(body)] ?? 'campaign.get/gone')
                : undefined,
        )
        .otherwise('campaign.summary', (body) =>
            onlyOf(body) && matches({ campaign_ids: [onlyOf(body)], ...archivedLeftOut }, body)
                ? 'campaign.summary/archived'
                : undefined,
        )
        .otherwise('workitem.list', (body) =>
            campaignOf(body) && matches(createBody(campaignOf(body)), body) ? 'workitem.list/creating-none' : undefined,
        );
}
