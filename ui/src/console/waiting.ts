import { signal, type Signal } from '@preact/signals-react';
import type { Api } from '../api/client';
import type { Approval, Decision } from '../api/types';
import { attentionStore, type AttentionStore } from '../screens/attention/store';
import { keptAndMissing, stillAsking, type Reasked } from './waiting-model';

// What waits on a person in one agent's chat: the campaign's pending approvals and questions, read with
// `approval.list` and `decision.list` of that campaign and decided through the holder Needs you is built on, so an
// approval is approved, rejected or edited and a question answered by the same calls, with the same actor and reason.
// Nothing the AI writes in the chat reaches it. Each item is read whole once, for what its card shows; a card leaves when
// the next read no longer lists its item, whether this chat decided it or anything else did.

export interface Waiting {
    readonly campaignId: string;
    /** The campaign's own holder of what waits: its lists, and the acts on them. */
    readonly store: AttentionStore;
    /** Each pending approval read whole, by its id. */
    readonly approvals: Signal<Readonly<Record<string, Approval>>>;
    /** Each pending question read whole, by its id. */
    readonly questions: Signal<Readonly<Record<string, Decision>>>;
    /** What the runtime said when it refused the last act: its code and its words, kept until the next act. */
    readonly refused: Signal<{ code: string | null; message: string } | null>;
    /** The messages edited here, until the runtime has asked about each again. */
    readonly reasked: Signal<readonly Reasked[]>;
}

const held = new Map<string, Waiting>();
/** How many chats of each campaign are drawn: a journal line reads again only the campaigns drawn. */
const drawn = new Map<string, number>();
/** The reads under way, so a row is never read twice at once. */
const reading = new Set<string>();

export function waitingOf(campaignId: string): Waiting {
    let waiting = held.get(campaignId);
    if (waiting === undefined) {
        waiting = {
            campaignId,
            store: attentionStore({ campaignId }),
            approvals: signal({}),
            questions: signal({}),
            refused: signal(null),
            reasked: signal([]),
        };
        held.set(campaignId, waiting);
    }
    return waiting;
}

/**
 * Reads whole each row the lists hold and this chat has not read yet, and lets go of the rows they no longer hold. A
 * read that fails leaves its card drawn from the list's row.
 */
async function fill(api: Api, waiting: Waiting): Promise<void> {
    const approvals = keptAndMissing(
        waiting.approvals.value,
        waiting.store.approvals.value.map((row) => row.id),
    );
    const questions = keptAndMissing(
        waiting.questions.value,
        waiting.store.questions.value.map((row) => row.id),
    );
    waiting.approvals.value = approvals.kept;
    waiting.questions.value = questions.kept;
    waiting.reasked.value = stillAsking(waiting.reasked.value, waiting.store.approvals.value);
    const once = <T>(key: string, read: () => Promise<T>, keep: (item: T) => void): Promise<void> => {
        if (reading.has(key)) return Promise.resolve();
        reading.add(key);
        return read()
            .then(keep, () => undefined)
            .finally(() => reading.delete(key));
    };
    await Promise.all([
        ...approvals.missing.map((id) =>
            once(
                `${waiting.campaignId}/apr/${id}`,
                () => api.call<Approval>('approval.get', { approval_id: id }),
                (item) => {
                    if (waiting.store.approvals.value.some((row) => row.id === id))
                        waiting.approvals.value = { ...waiting.approvals.value, [id]: item };
                },
            ),
        ),
        ...questions.missing.map((id) =>
            once(
                `${waiting.campaignId}/dec/${id}`,
                () => api.call<Decision>('decision.get', { decision_id: id }),
                (item) => {
                    if (waiting.store.questions.value.some((row) => row.id === id))
                        waiting.questions.value = { ...waiting.questions.value, [id]: item };
                },
            ),
        ),
    ]);
}

/** Reads what waits in the campaign again: its two lists, then each new row whole. */
export async function readWaiting(api: Api, waiting: Waiting): Promise<void> {
    await waiting.store.load(api, false);
    await fill(api, waiting);
}

/** A chat of the campaign is drawn: what waits is read now and on every journal line while it stays drawn. */
export function drawWaiting(api: Api, campaignId: string): () => void {
    drawn.set(campaignId, (drawn.get(campaignId) ?? 0) + 1);
    void readWaiting(api, waitingOf(campaignId));
    return () => {
        const left = (drawn.get(campaignId) ?? 1) - 1;
        if (left > 0) drawn.set(campaignId, left);
        else drawn.delete(campaignId);
    };
}

/** Reads again what waits in every campaign whose chat is drawn: the console calls it on the journal's word. */
export function reloadWaiting(api: Api): void {
    for (const campaignId of drawn.keys()) void readWaiting(api, waitingOf(campaignId));
}

/** Forgets every campaign's lists and reads; for a test that starts afresh. */
export function forgetWaiting(): void {
    held.clear();
    drawn.clear();
    reading.clear();
}

/**
 * One act through the campaign's holder, which reads the lists again either way; a refusal is kept with the runtime's
 * code until the next act, so a card that stays is never a silent revert, and one that left says why.
 */
async function decide(api: Api, waiting: Waiting, act: () => Promise<boolean>): Promise<boolean> {
    waiting.refused.value = null;
    const done = await act();
    const failure = waiting.store.failure.value;
    if (!done && failure !== null) waiting.refused.value = { code: waiting.store.failureCode.value, message: failure };
    await fill(api, waiting);
    return done;
}

export const approveWaiting = (api: Api, waiting: Waiting, id: string): Promise<boolean> =>
    decide(api, waiting, () => waiting.store.approve(api, id));

export const rejectWaiting = (api: Api, waiting: Waiting, id: string, reason?: string): Promise<boolean> =>
    decide(api, waiting, () => waiting.store.reject(api, id, reason));

export const answerWaiting = (
    api: Api,
    waiting: Waiting,
    id: string,
    choice: { option?: string; text?: string },
    reason?: string,
): Promise<boolean> => decide(api, waiting, () => waiting.store.answer(api, id, choice, reason));

/** The next page of each list that has one. */
export async function moreWaiting(api: Api, waiting: Waiting): Promise<void> {
    await Promise.all([waiting.store.more(api, 'approvals', false), waiting.store.more(api, 'questions', false)]);
    await fill(api, waiting);
}

/**
 * A message was edited from its card: the runtime withdrew the approval and asks again with the new text when it next
 * takes the work, which the chat says until the new approval is listed.
 */
export function editedWaiting(api: Api, waiting: Waiting, approval: Approval): void {
    waiting.refused.value = null;
    waiting.reasked.value = [
        ...waiting.reasked.value.filter((each) => each.workItemId !== approval.work_item_id),
        { workItemId: approval.work_item_id, approvalId: approval.id },
    ];
    void readWaiting(api, waiting);
}
