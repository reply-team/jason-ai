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
    /** Set when the runtime said nothing, its time having run out: `message` is then the UI's own words saying so. */
    unanswered?: true;
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
 * the approval whole once read; Needs you's pane shows it whole once read.
 */
export const readingSentence = (where: 'chat' | 'needs'): string =>
    `This approval is being read; it cannot be approved until it ${
        where === 'chat' ? 'is shown here whole' : 'has been read whole'
    }.`;

/**
 * What a held Approve says when its approval could not be read, before what the runtime said, or before the words that
 * say it did not answer in time; and where the person can still open it: in a chat, Details opens it in Needs you; in
 * Needs you's pane, Read again asks for it once more.
 */
export const unreadSentence = (where: 'chat' | 'needs', said?: Said): string =>
    `This approval could not be read, so it cannot be approved here; ${
        where === 'chat' ? 'Details opens it in Needs you' : 'Read again asks for it once more'
    }.${said?.unanswered === true ? '' : ' The runtime said:'}`;

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

/**
 * An approval this chat's card approved: what it was, who it reaches, and how its work stands as the runtime last said:
 * `approved` while the work has not ended, then `done` (the work item succeeded), `failed` or `cancelled`.
 */
export interface Approved {
    readonly approvalId: string;
    readonly workItemId: string;
    readonly operation: string;
    readonly what: string;
    readonly to: string | null;
    /**
     * How the work stands as the runtime last said: `approved` while it is queued (it may still be held at a limit),
     * `running` once a handler has it, `held` when it parked again (`why` says how), and `done`, `failed`, `cancelled`
     * or `expired` when it ended.
     */
    readonly state: 'approved' | 'running' | 'held' | 'done' | 'failed' | 'cancelled' | 'expired';
    /** Why a held one is not sent yet, in plain words. */
    readonly why?: string | undefined;
}

/**
 * What the chat says of an approval its card approved: what it was and to whom, then how its work stands. Only a
 * message is said to be sent, and only once its work succeeded; any other act is said done. "Jason is sending it" is said
 * only while a handler runs the work; while it is queued, which a limit may still hold, it "will" send it.
 */
export function approvedSentence(each: Approved): string {
    const what = `${each.what}${each.to === null ? '' : ` to ${each.to}`}`;
    const sends = isMessage(each.operation);
    const not = sends ? 'It was not sent' : 'It did not complete';
    switch (each.state) {
        case 'approved':
            return `You approved: ${what}. ${sends ? 'Jason will send it.' : 'Jason will do it.'}`;
        case 'running':
            return `You approved: ${what}. ${sends ? 'Jason is sending it.' : 'Jason is doing it.'}`;
        case 'held':
            return `You approved: ${what}. ${sends ? 'It is not sent yet' : 'It is not done yet'}: ${each.why ?? 'it is held'}.`;
        case 'done':
            return `You approved: ${what}. ${sends ? 'It was sent.' : 'It is done.'}`;
        case 'failed':
            return `You approved: ${what}. ${not}: the work failed.`;
        case 'cancelled':
            return `You approved: ${what}. ${not}: the work was cancelled.`;
        case 'expired':
            return `You approved: ${what}. ${not}: the work expired.`;
    }
}
