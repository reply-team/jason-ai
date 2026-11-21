import type { ApprovalSummary, CampaignSummary, DecisionSummary } from '../api/types';
import type { DesktopConversation } from '../platform/desktop';
import { ageOf } from './live';
import { countWords, needsCount, type ChatWait } from './needs-model';

// Pure: the rows of the sidebar's two sections, AGENTS and CHATS, from what the console already holds. Nothing here
// reads the runtime or draws markup. A conversation is in exactly one section: an agent's in AGENTS, as that agent's
// row, and one about no agent in CHATS.

/** The entries of AGENTS' filter, in the order its menu lists them. */
export const recentFilters = ['all', 'needs', 'live', 'paused', 'draft'] as const;
export type RecentFilter = (typeof recentFilters)[number];

/** How many rows of CHATS are shown at first, and how many each "Load more" adds. */
export const pageSize = 10;

/** What a row's dot says: something waits on the person, else the agent's state; `none` for a chat with no agent. */
export type RecentDot = 'waiting' | 'active' | 'paused' | 'draft' | 'none';

export interface RecentRow {
    /** The row's key: its conversation's id, or the agent's own for an agent with no conversation yet. */
    id: string;
    /** The agent the row is about; null for a chat with no agent. */
    campaignId: string | null;
    /** The row's conversation; null for an agent with no conversation yet. */
    conversationId: string | null;
    title: string;
    /** The name of the agent the chat is about; null for a chat with no agent. */
    agent: string | null;
    /** Whether the conversation is about a campaign at all, whether or not the page holds that campaign. */
    aboutAgent: boolean;
    /** What waits on the person for the row's agent, by Needs you's one count; for a chat of no agent, its own wait. */
    waiting: number;
    /** That count in words: with a plus when the runtime holds more than was read. */
    waitingWords: string;
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
    /**
     * An agent `campaigns` does not hold, as it was read on its own (`recent.ts`): an archived one, which that list leaves
     * out, or one past its first page. Undefined while it has not been read, and for one the runtime does not know;
     * absent for none read.
     */
    agentRead?: (id: string) => CampaignSummary | undefined;
    approvals: readonly ApprovalSummary[];
    questions: readonly DecisionSummary[];
    /** The chats that wait on a choice, once read; absent or null while they have not been. */
    chats?: readonly ChatWait[] | null;
    /** Whether the runtime holds more of each list than was read; absent for neither. */
    more?: { approvals: boolean; questions: boolean };
    now: number;
}

const states = ['active', 'paused', 'draft'] as const;

/**
 * Every conversation except the Feed and those whose agent is archived, newest first as the desktop listed them, with its
 * agent's state and what waits. An archived agent's conversation is reached from that agent's own place; one whose agent
 * is not read yet stays, as a conversation whose agent is not held.
 */
export function recentRows(input: RecentInput): RecentRow[] {
    const waits = {
        approvals: input.approvals,
        questions: input.questions,
        chats: input.chats ?? null,
        more: input.more ?? { approvals: false, questions: false },
    };
    return input.conversations
        .filter((conversation) => conversation.main !== true)
        .flatMap((conversation) => {
            const agent =
                conversation.campaignId === null
                    ? undefined
                    : (input.campaigns.find((each) => each.id === conversation.campaignId) ??
                      input.agentRead?.(conversation.campaignId));
            if (agent?.status === 'archived') return [];
            const count = agent
                ? needsCount(waits, agent.id)
                : {
                      n:
                          conversation.campaignId === null
                              ? (waits.chats ?? []).filter((each) => each.conversationId === conversation.id).length
                              : 0,
                      more: false,
                  };
            const waiting = count.n;
            const state = states.find((each) => each === agent?.status);
            return [
                {
                    id: conversation.id,
                    campaignId: conversation.campaignId,
                    conversationId: conversation.id,
                    title: input.titles(conversation.id) ?? conversation.title,
                    agent: agent?.name ?? null,
                    aboutAgent: conversation.campaignId !== null,
                    waiting,
                    waitingWords: countWords(count),
                    state: state ?? null,
                    running: input.running?.(conversation.id) ?? conversation.running,
                    dot: waiting > 0 ? 'waiting' : (state ?? 'none'),
                    age: ageOf(conversation.updatedAt, input.now),
                } satisfies RecentRow,
            ];
        });
}

/**
 * AGENTS: each agent once, by its campaign's name, by the row of its conversation, the newest first as the desktop listed them, then each agent
 * of the list of agents that has no conversation yet, in that list's order, by its name. An agent's later conversations
 * are its own, never a chat of CHATS: its row opens the first, as its Chat does.
 */
export function agentRows(input: RecentInput): RecentRow[] {
    const seen = new Set<string>();
    const rows: RecentRow[] = [];
    for (const row of recentRows(input)) {
        if (row.campaignId === null || seen.has(row.campaignId)) continue;
        seen.add(row.campaignId);
        // An agent is named by its campaign's name wherever the console holds it: a title its conversation carries never
        // masks a rename. One whose agent is not read yet keeps its conversation's title.
        rows.push(row.agent === null ? row : { ...row, title: row.agent });
    }
    const waits = {
        approvals: input.approvals,
        questions: input.questions,
        chats: input.chats ?? null,
        more: input.more ?? { approvals: false, questions: false },
    };
    for (const agent of input.campaigns) {
        if (agent.status === 'archived' || seen.has(agent.id)) continue;
        seen.add(agent.id);
        const count = needsCount(waits, agent.id);
        const state = states.find((each) => each === agent.status) ?? null;
        rows.push({
            id: agent.id,
            campaignId: agent.id,
            conversationId: null,
            title: agent.name,
            agent: agent.name,
            aboutAgent: true,
            waiting: count.n,
            waitingWords: countWords(count),
            state,
            running: false,
            dot: count.n > 0 ? 'waiting' : (state ?? 'none'),
            age: '',
        });
    }
    return rows;
}

/** CHATS: the conversations about no agent, the newest first as the desktop listed them. */
export function chatRows(input: RecentInput): RecentRow[] {
    return recentRows(input).filter((row) => row.campaignId === null);
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

/** The rows to draw: all that the filter keeps while it is not All, else the first `limit`. */
export function shownRows(
    rows: readonly RecentRow[],
    filter: RecentFilter,
    limit: number,
): { rows: RecentRow[]; more: boolean } {
    const found = rows.filter((row) => inFilter(row, filter));
    return filter !== 'all'
        ? { rows: found, more: false }
        : { rows: found.slice(0, limit), more: found.length > limit };
}
