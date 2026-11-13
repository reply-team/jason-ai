import type { ApprovalSummary, CampaignSummary, DecisionSummary } from '../api/types';
import type { DesktopConversation } from '../platform/desktop';
import { ageOf } from './live';

// Pure: the rows under "Recent" from what the console already holds. Nothing here reads the runtime or draws markup.

/** The entries of the filter, in the order its menu lists them. */
export const recentFilters = ['all', 'needs', 'live', 'paused', 'draft'] as const;
export type RecentFilter = (typeof recentFilters)[number];

/** How many rows are shown at first, and how many each "Load more" adds. */
export const pageSize = 10;

/** What a row's dot says: something waits on the person, else the agent's state; `none` for a chat with no agent. */
export type RecentDot = 'waiting' | 'active' | 'paused' | 'draft' | 'none';

export interface RecentRow {
    id: string;
    title: string;
    /** The name of the agent the chat is about; null for a chat with no agent. */
    agent: string | null;
    /** Pending approvals plus pending questions of the row's agent. */
    waiting: number;
    /** The agent's state, whether or not something waits; null for a chat with no agent or one not read yet. */
    state: 'active' | 'paused' | 'draft' | null;
    dot: RecentDot;
    /** Whether a turn of the chat runs now. */
    running: boolean;
    age: string;
}

export interface RecentInput {
    conversations: readonly DesktopConversation[];
    /** The title a chat the console holds says now, by conversation id. */
    titles: (id: string) => string | undefined;
    /** Whether a chat the console holds has a turn running now; undefined for one it does not hold. */
    running?: (id: string) => boolean | undefined;
    campaigns: readonly CampaignSummary[];
    approvals: readonly ApprovalSummary[];
    questions: readonly DecisionSummary[];
    now: number;
}

const states = ['active', 'paused', 'draft'] as const;

/** Every conversation except the Feed, newest first as the desktop listed them, with its agent's state and what waits. */
export function recentRows(input: RecentInput): RecentRow[] {
    const waitingOf = new Map<string, number>();
    for (const each of [...input.approvals, ...input.questions])
        waitingOf.set(each.campaign_id, (waitingOf.get(each.campaign_id) ?? 0) + 1);
    return input.conversations
        .filter((conversation) => conversation.main !== true)
        .map((conversation) => {
            const agent =
                conversation.campaignId === null
                    ? undefined
                    : input.campaigns.find((each) => each.id === conversation.campaignId);
            const waiting = agent ? (waitingOf.get(agent.id) ?? 0) : 0;
            const state = states.find((each) => each === agent?.status);
            return {
                id: conversation.id,
                title: input.titles(conversation.id) ?? conversation.title,
                agent: agent?.name ?? null,
                waiting,
                state: state ?? null,
                running: input.running?.(conversation.id) ?? conversation.running,
                dot: waiting > 0 ? 'waiting' : (state ?? 'none'),
                age: ageOf(conversation.updatedAt, input.now),
            } satisfies RecentRow;
        });
}

/** Whether a row belongs under a filter entry. A chat with no agent belongs under All alone. */
export function inFilter(row: RecentRow, filter: RecentFilter): boolean {
    switch (filter) {
        case 'all':
            return true;
        case 'needs':
            return row.waiting > 0;
        case 'live':
            return row.state === 'active';
        case 'paused':
            return row.state === 'paused';
        case 'draft':
            return row.state === 'draft';
    }
}

/** Whether a row's title holds the text, whatever the case; no text matches every row. */
export function matches(row: RecentRow, text: string): boolean {
    const wanted = text.trim().toLowerCase();
    if (wanted === '') return true;
    return row.title.toLowerCase().includes(wanted);
}

/** The rows to draw: all that match while a search text or a filter is active, else the first `limit`. */
export function shownRows(
    rows: readonly RecentRow[],
    filter: RecentFilter,
    text: string,
    limit: number,
): { rows: RecentRow[]; more: boolean } {
    const narrowed = filter !== 'all' || text.trim() !== '';
    const found = rows.filter((row) => inFilter(row, filter) && matches(row, text));
    return narrowed ? { rows: found, more: false } : { rows: found.slice(0, limit), more: found.length > limit };
}
