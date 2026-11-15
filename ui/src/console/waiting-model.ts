// Pure: what one card of an agent's chat says about an approval or a question waiting on a person. Every word comes
// from the runtime's lists and reads of that campaign's approvals and questions, never from what the AI wrote in the
// chat; nothing here is markup.
import type { Approval, ApprovalSummary, Decision, DecisionOption, DecisionSummary } from '../api/types';
import { reasonLabel, title } from '../screens/attention/labels';
import { isMessage } from '../screens/attention/message-edit';
import { ageOf, operationWords, toApprovalDetail } from './live';

/** What the runtime said: its code, when it gave one, and its words. */
export interface Said {
    code: string | null;
    message: string;
}

/**
 * Whether an approval may be approved where it is drawn, in an agent's chat or on a row of Needs you: only once it has
 * been read whole, whatever it does, since its doubts, its words or what will happen, and the person it reaches all
 * come from that read. The rule is the approval's, never a list of kinds. Until then it is being read, or its read
 * failed with what the runtime said.
 */
export type ApprovalGate = { live: true } | { live: false; reading: true } | { live: false; unread: Said };

export function approvalGate(whole: Approval | undefined, unread: Said | undefined): ApprovalGate {
    if (whole !== undefined) return { live: true };
    return unread === undefined ? { live: false, reading: true } : { live: false, unread };
}

/**
 * What a held Approve says while its approval is being read, in the words true where it is drawn: a chat's card shows
 * the approval whole once read; Needs you's row reads it whole and keeps its words for Details.
 */
export const readingSentence = (where: 'chat' | 'needs'): string =>
    `This approval is being read; it cannot be approved until it ${
        where === 'chat' ? 'is shown here whole' : 'has been read whole'
    }.`;

/**
 * What a held Approve says when its approval could not be read, before what the runtime said; and where the person can
 * still open it: in a chat, Details opens it in Needs you; on Needs you's row, Details reads it again.
 */
export const unreadSentence = (where: 'chat' | 'needs'): string =>
    `This approval could not be read, so it cannot be approved here; ${
        where === 'chat' ? 'Details opens it in Needs you' : 'Details reads it again'
    }. The runtime said:`;

export interface ApprovalCard {
    id: string;
    /** What the approval does, in words: Message, LinkedIn invite, Add to sequence. */
    what: string;
    /** What a confirmation names: the operation and its person. */
    title: string;
    /** Who it reaches: the person's name and address as the preview gives them, else the name the list carries. */
    to: string | null;
    /** The mailbox or account it goes from. */
    from: string | null;
    channel: string | null;
    subject: string | null;
    body: string | null;
    /** What will happen, said when there is no message to show. */
    intent: string | null;
    /** Why it waits for a person, in a sentence. */
    why: string;
    age: string;
    /** A message, which is checked against its work item before it is approved; every approval waits for its whole read. */
    message: boolean;
}

/**
 * One approval's card: the list's row, and the approval read whole once it has been. Until it is, the card says what
 * the row says; a message's words are never guessed.
 */
export function toApprovalCard(row: ApprovalSummary, whole: Approval | undefined, now: number): ApprovalCard {
    const operation = whole?.operation ?? row.operation;
    const detail = whole ? toApprovalDetail(whole.preview) : null;
    const name = row.contact_name;
    return {
        id: row.id,
        what: operationWords(operation),
        title: title(operation, name),
        to: detail?.to ?? name,
        from: detail?.from ?? null,
        channel: detail?.channel ?? null,
        subject: detail?.subject ?? null,
        body: detail?.body ?? null,
        intent: detail && detail.subject === null && detail.body === null ? detail.intent : null,
        why: reasonLabel(whole?.reason ?? row.reason),
        age: ageOf(row.requested_at, now),
        message: isMessage(operation),
    };
}

export interface QuestionCard {
    id: string;
    question: string;
    age: string;
    /** The options the question names, once it has been read whole; none before, and none for a question with none. */
    options: DecisionOption[];
}

export function toQuestionCard(row: DecisionSummary, whole: Decision | undefined, now: number): QuestionCard {
    return { id: row.id, question: row.question, age: ageOf(row.raised_at, now), options: whole?.options ?? [] };
}

/** A message edited from a card: the approval the edit withdrew, and the work item the runtime asks about again. */
export interface Reasked {
    workItemId: string;
    approvalId: string;
}

/** The edits still waiting for the runtime to ask again: one is let go once another approval of its work item is listed. */
export function stillAsking(reasked: readonly Reasked[], pending: readonly ApprovalSummary[]): Reasked[] {
    return reasked.filter(
        (each) => !pending.some((row) => row.work_item_id === each.workItemId && row.id !== each.approvalId),
    );
}

/** The whole reads kept for the rows a list still holds, and the ids of the rows not read yet. */
export function keptAndMissing<T>(
    read: Readonly<Record<string, T>>,
    listed: readonly string[],
): { kept: Record<string, T>; missing: string[] } {
    const kept: Record<string, T> = {};
    const missing: string[] = [];
    for (const id of listed) {
        if (Object.hasOwn(read, id)) kept[id] = read[id]!;
        else missing.push(id);
    }
    return { kept, missing };
}
