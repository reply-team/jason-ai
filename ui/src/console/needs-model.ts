// Pure: what Needs you lists, under which filter and agent, and the one count the sidebar, the dashboard and Recent
// show. Every approval and question comes from the runtime's two lists as they were read, filtered only on fields the
// runtime wrote; a chat that waits comes from the conversations the desktop keeps, once they have been read. Nothing
// here reads, keeps or draws anything.
import type { ApprovalSummary, DecisionSummary } from '../api/types';

/**
 * The operation of a drafted answer to a reply a person sent. Inbox holds those approvals and the questions the runtime
 * names a thread for: what waits on a person about a reply.
 */
export const REPLY_OPERATION = 'conversation.reply';

/**
 * A chat whose newest turn of the AI ends in a choice nobody has made: the conversation, the agent it is about (none
 * for a chat of no agent), its title, the choice's question and its options in the AI's own words, and when it was
 * asked.
 */
export interface ChatWait {
    conversationId: string;
    campaignId: string | null;
    agent: string | null;
    title: string;
    question: string;
    options: readonly string[];
    at: string;
}

export interface NeedsInput {
    approvals: readonly ApprovalSummary[];
    questions: readonly DecisionSummary[];
    /** The chats that wait; null while they have not been read, and then nothing is said of them. */
    chats: readonly ChatWait[] | null;
    /** Whether the runtime has another page of each list. */
    more: { approvals: boolean; questions: boolean };
}

export type NeedsFilter = 'all' | 'approvals' | 'questions' | 'chats' | 'inbox';

export type WaitingItem =
    | { kind: 'approval'; id: string; campaignId: string; row: ApprovalSummary }
    | { kind: 'question'; id: string; campaignId: string; row: DecisionSummary }
    | { kind: 'chat'; id: string; campaignId: string | null; chat: ChatWait };

export type Chosen = { kind: WaitingItem['kind']; id: string };

/** A count, and whether the runtime holds more than it counts: a list it has not given the next page of. */
export interface Count {
    n: number;
    more: boolean;
}

const inFilter = (item: WaitingItem, filter: NeedsFilter): boolean => {
    switch (filter) {
        case 'all':
            return true;
        case 'approvals':
            return item.kind === 'approval';
        case 'questions':
            return item.kind === 'question';
        case 'chats':
            return item.kind === 'chat';
        case 'inbox':
            return (
                (item.kind === 'approval' && item.row.operation === REPLY_OPERATION) ||
                (item.kind === 'question' && item.row.thread != null)
            );
    }
};

/** Everything that waits, approvals then questions then chats, each in the order its list holds it. */
function everything(input: NeedsInput): WaitingItem[] {
    return [
        ...input.approvals.map((row) => ({ kind: 'approval' as const, id: row.id, campaignId: row.campaign_id, row })),
        ...input.questions.map((row) => ({ kind: 'question' as const, id: row.id, campaignId: row.campaign_id, row })),
        ...(input.chats ?? []).map((chat) => ({
            kind: 'chat' as const,
            id: chat.conversationId,
            campaignId: chat.campaignId,
            chat,
        })),
    ];
}

/** What waits under a filter and, when one is chosen, for one agent. */
export function waitingOf(input: NeedsInput, filter: NeedsFilter, agent: string | null): WaitingItem[] {
    return everything(input).filter((item) => inFilter(item, filter) && (agent === null || item.campaignId === agent));
}

/** The agents something waits for, each once, in the order they first appear. */
export function agentsOf(input: NeedsInput): { id: string; name: string }[] {
    const agents = new Map<string, string>();
    for (const item of everything(input)) {
        if (item.campaignId === null || agents.has(item.campaignId)) continue;
        const name = item.kind === 'chat' ? item.chat.agent : item.row.campaign_name;
        agents.set(item.campaignId, name ?? item.campaignId);
    }
    return [...agents].map(([id, name]) => ({ id, name }));
}

/** Whether a filter's list may be short of what the runtime holds: Inbox holds rows of both lists. */
export const moreUnder = (input: NeedsInput, filter: NeedsFilter): boolean => {
    switch (filter) {
        case 'all':
        case 'inbox':
            return input.more.approvals || input.more.questions;
        case 'approvals':
            return input.more.approvals;
        case 'questions':
            return input.more.questions;
        case 'chats':
            return false;
    }
};

/** What one filter holds, counted, for every agent or for one. */
export const countOf = (input: NeedsInput, filter: NeedsFilter, agent: string | null = null): Count => ({
    n: waitingOf(input, filter, agent).length,
    more: moreUnder(input, filter),
});

/**
 * The one count of what waits on a person: what Needs you's All holds, for every agent or for one. The sidebar, the
 * dashboard and each row of Recent show it; nothing else counts what waits.
 */
export const needsCount = (input: NeedsInput, agent: string | null = null): Count => countOf(input, 'all', agent);

/** A count in words: the number, and a plus when the runtime holds more than were read. */
export const countWords = (count: Count): string => `${count.n}${count.more ? '+' : ''}`;

const filters: readonly [NeedsFilter, string][] = [
    ['all', 'All'],
    ['approvals', 'Approvals'],
    ['questions', 'Questions'],
    ['chats', 'Chats'],
    ['inbox', 'Inbox'],
];

/** The filters drawn, with their words and counts under the agent chosen: Chats only once the chats have been read. */
export function filtersOf(input: NeedsInput, agent: string | null): [NeedsFilter, string, Count][] {
    return filters
        .filter(([filter]) => filter !== 'chats' || input.chats !== null)
        .map(([filter, label]) => [filter, label, countOf(input, filter, agent)]);
}

/** The item the pane shows: the one chosen while it is listed, else the first listed, else none. */
export function pick(items: readonly WaitingItem[], chosen: Chosen | null): WaitingItem | null {
    const found =
        chosen === null ? undefined : items.find((item) => item.kind === chosen.kind && item.id === chosen.id);
    return found ?? items[0] ?? null;
}
