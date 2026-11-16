import type { FakeApi } from './fake-api';
import type { FixtureName } from './fixtures';

/** The agents as the header over an agent's chat or page reads them, by id; any other id is one the runtime lacks. */
const agents: Readonly<Record<string, FixtureName>> = {
    cmp_1: 'campaign.get/details',
    cmp_2: 'campaign.get/unlinked',
    cmp_3: 'campaign.get/summer',
    cmp_4: 'campaign.get/archived',
    cmp_5: 'campaign.get/card-plan-draft',
};

/**
 * The agent's header in the console reads the agent the area shows (`campaign.get`), for a started agent with no
 * provider campaign pinned whether one is being made (`workitem.list` of its unfinished provider operations), and for an
 * agent the console's list does not hold its people (`campaign.summary` of it alone). A test of
 * something else is given those reads, from recordings, where nothing the test said answers them; what a test offers
 * stays its own.
 */
export function agentHeads(fake: FakeApi): FakeApi {
    return (
        fake
            .otherwise(
                'campaign.get',
                (body) => agents[(body as { campaign_id?: string }).campaign_id ?? ''] ?? 'campaign.get/gone',
            )
            // An agent the console's list does not hold, an archived one, has its people counted for it alone.
            .otherwise('campaign.summary', (body) =>
                (body as { campaign_ids?: string[] }).campaign_ids?.length === 1
                    ? 'campaign.summary/archived'
                    : undefined,
            )
            .otherwise('workitem.list', (body) =>
                (body as { kind?: string }).kind === 'provider_op' ? 'workitem.list/creating-none' : undefined,
            )
    );
}
