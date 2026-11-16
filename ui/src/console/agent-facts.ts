import { signal } from '@preact/signals-react';
import { ApiError, type Api } from '../api/client';
import type { Campaign, CampaignSummaryResponse, Page, WorkItemSummary } from '../api/types';
import { asksForCreate, createBody, unfinishedCreate } from '../screens/campaigns/kind';
import { archivedLeftOut } from '../screens/campaigns/store';
import { read, summary } from './campaigns';

// What the agent's header says, read for the agent the area shows: the campaign as the runtime holds it, and whether a
// provider campaign is being made for it, which the type's pill needs. How many people it holds is the count the
// console's list of agents already reads (its Summary, archived people left out, as the agent's People tab counts them);
// for an agent that list does not hold, the same read is made for it alone. Read when the header is drawn, again on the
// journal's lines and after a move; a read that fails leaves what was read before, and an agent never read has nothing
// said of it.

export interface AgentFacts {
    campaign: Campaign;
    /** Whether a provider campaign is being made for it: null where that was not asked or could not be read. */
    creating: boolean | null;
    /** The people it holds, read for it alone because the list of agents does not hold it; undefined where it does. */
    people?: number | null;
}

/** By agent id, what was read last. */
export const agentFacts = signal<Readonly<Record<string, AgentFacts>>>({});

/** The agents the runtime answered it does not know, as read last: an old link, or one deleted since. */
export const goneAgents = signal<ReadonlySet<string>>(new Set());

function markGone(id: string, gone: boolean): void {
    if (goneAgents.value.has(id) === gone) return;
    const next = new Set(goneAgents.value);
    if (gone) next.add(id);
    else next.delete(id);
    goneAgents.value = next;
}

const generations = new Map<string, number>();

/** How many people the agent holds now, as the runtime counted them last; null where no count came. */
export function peopleOf(id: string): number | null {
    const listed = summary.value[id];
    if (listed !== undefined) return typeof listed.people === 'number' ? listed.people : null;
    return agentFacts.value[id]?.people ?? null;
}

/** Reads one agent's facts; an answer that a later read overtook is dropped. */
export async function readAgent(api: Api, id: string): Promise<void> {
    const mine = (generations.get(id) ?? 0) + 1;
    generations.set(id, mine);
    try {
        const campaign = await api.call<Campaign>('campaign.get', { campaign_id: id });
        let creating: boolean | null = false;
        if (asksForCreate(campaign)) {
            try {
                const page = await api.call<Page<WorkItemSummary>>('workitem.list', createBody(id));
                creating = unfinishedCreate(page.items);
            } catch {
                creating = null;
            }
        }
        let people: number | null | undefined;
        // Asked only once the list of agents was read without it: until then the list's own count is awaited.
        if (read.value && summary.value[id] === undefined) {
            try {
                const counts = await api.call<CampaignSummaryResponse>('campaign.summary', {
                    campaign_ids: [id],
                    ...archivedLeftOut,
                });
                const row = counts.campaigns.find((each) => each.campaign_id === id);
                people = typeof row?.people === 'number' ? row.people : null;
            } catch {
                people = null;
            }
        }
        if (generations.get(id) !== mine) return;
        markGone(id, false);
        agentFacts.value = {
            ...agentFacts.value,
            [id]: { campaign, creating, ...(people === undefined ? {} : { people }) },
        };
    } catch (error) {
        // What was read before stays; what was never read stays unsaid. An agent the runtime does not know is said gone.
        if (generations.get(id) === mine && error instanceof ApiError && error.status === 404) markGone(id, true);
    }
}
