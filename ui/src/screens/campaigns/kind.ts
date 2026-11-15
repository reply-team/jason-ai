import type { Campaign } from '../../api/types';
import { t } from '../../platform/i18n';

/** What kind of agent this is, said by the facts the page holds: a provider's Sequence, Jason's own Adaptive, or nothing. */
export type AgentKind = 'sequence' | 'adaptive';

type Facts = Pick<Campaign, 'status' | 'external_ids'>;

const started = (status: string): boolean => status === 'active' || status === 'paused';
const pinned = (campaign: Facts): boolean => campaign.external_ids.some((id) => id.kind === 'campaign');

/**
 * The agent's kind, a pure function of the facts the page has.
 *
 * - **Sequence**: the campaign holds a pin of kind `campaign`, whatever its status. A pin the provider has since lost
 *   still reads Sequence: nothing reports that, and nothing here guesses it.
 * - **Adaptive**: no such pin, the agent has been started (active or paused), and no `campaign.create` work item of it is
 *   unfinished. With no provider campaign pinned or being made, every message of a started agent is Jason's own send.
 *   `creating` is whether one is unfinished: true, or null while that is not known, holds the word back.
 * - **Nothing**: a draft with no pin, and any state the two rules do not cover.
 */
export function agentKind(campaign: Facts, creating: boolean | null): AgentKind | null {
    if (pinned(campaign)) return 'sequence';
    if (started(campaign.status) && creating === false) return 'adaptive';
    return null;
}

/** The label's word, from the locale; nothing where the agent has no kind. */
export function kindWord(kind: AgentKind | null): string | null {
    return kind === null ? null : t(kind === 'sequence' ? 'agent.kind.sequence' : 'agent.kind.adaptive');
}

/** The first tab's name: Sequence where the label says Sequence, and Paths otherwise. */
export function firstTabName(kind: AgentKind | null): string {
    return t(kind === 'sequence' ? 'agent.kind.sequence' : 'agent.kind.paths');
}

/** Whether the header must ask if a `campaign.create` is unfinished: only a started agent with no pin leaves it open. */
export function asksForCreate(campaign: Facts): boolean {
    return started(campaign.status) && !pinned(campaign);
}

/** What the one read asks: the agent's provider operations that are not finished, oldest first, one page. */
export const unfinishedStatuses = ['created', 'awaiting_approval', 'scheduled', 'processing', 'waiting'] as const;
export const createBody = (id: string) => ({
    campaign_id: id,
    kind: 'provider_op',
    status: [...unfinishedStatuses],
    order: 'created',
    limit: 50,
});

/** Whether the page the read returned holds a `campaign.create`. */
export function unfinishedCreate(items: readonly { operation: string | null }[]): boolean {
    return items.some((item) => item.operation === 'campaign.create');
}
