import { signal, type Signal } from '@preact/signals-react';
import { ApiError, Unpaired, type Api } from '../api/client';
import type { Approval, Decision } from '../api/types';
import { t } from '../platform/i18n';
import {
    STALE_SENTENCE,
    checkMessage,
    isMessage,
    readItem,
    uncheckedSentence,
} from '../screens/attention/message-edit';
import { attentionStore, type AttentionStore } from '../screens/attention/store';
import { keptAndMissing, stillAsking, type Approved, type Reasked, type Said } from './waiting-model';

// What waits on a person in one agent's chat: the campaign's pending approvals and questions, read with
// `approval.list` and `decision.list` of that campaign and decided through the holder Needs you is built on, so an
// approval is approved, rejected or edited and a question answered by the same calls, with the same actor and reason.
// Nothing the AI writes in the chat reaches it. Each item is read whole once, for what its card shows; a card leaves when
// the next read no longer lists its item, whether this chat decided it or anything else did.
//
// The whole reads of approvals are what Needs you's rows share with the cards: a holder's listed approvals, each read
// whole once, a few at a time, and approved only once read and, for a message, checked against its work item at the
// press.

export type { Approved, Said } from './waiting-model';

/** One holder's approvals read whole: an agent's chat's, or Needs you's. */
export interface WholeReads {
    /** Which holder the reads are of, so that two holders' reads of one approval are kept apart. */
    readonly key: string;
    /** The holder of what waits: its lists, and the acts on them. */
    readonly store: AttentionStore;
    /** Each pending approval read whole, by its id. */
    readonly approvals: Signal<Readonly<Record<string, Approval>>>;
    /** Why a listed item could not be read whole, by its id: what the runtime said, until a read of it succeeds. */
    readonly unread: Signal<Readonly<Record<string, Said>>>;
}

export interface Waiting extends WholeReads {
    readonly campaignId: string;
    /** Each pending question read whole, by its id. */
    readonly questions: Signal<Readonly<Record<string, Decision>>>;
    /**
     * What the runtime said when it refused the last act, and the item the act named: kept until the next act, and let
     * go by the first reload after it that no longer lists that item, so it leaves with the item's card.
     */
    readonly refused: Signal<(Said & { id: string }) | null>;
    /** The messages edited in this drawing of the chat, until the runtime has asked about each again. */
    readonly reasked: Signal<readonly Reasked[]>;
    /** The approvals this drawing of the chat approved, each followed to the end of its work, so the chat can say so. */
    readonly approved: Signal<readonly Approved[]>;
}

/** The whole reads of a holder's approvals, none read yet. */
export function wholeReads(key: string, store: AttentionStore): WholeReads {
    return { key, store, approvals: signal({}), unread: signal({}) };
}

const held = new Map<string, Waiting>();
/** How many chats of each campaign are drawn: a journal line reads again only the campaigns drawn. */
const drawn = new Map<string, number>();
/** The whole reads under way, by holder, kind and id: one the runtime has not answered is never sent beside itself. */
const reading = new Map<string, object>();

/** How many approvals one fill reads whole at once: a long list is read a few rows at a time, in turn. */
const READS_AT_ONCE = 8;

/** How long a whole read may go unanswered before it counts as not answered: the UI's API client has no limit of its own. */
export const READ_LIMIT_MS = 30_000;

/** What is kept beside an item whose read was not answered within the limit: the runtime said nothing. */
const unanswered = (): Said => ({
    code: null,
    message: t('waiting.unanswered', { seconds: String(READ_LIMIT_MS / 1000) }),
    unanswered: true,
});

export function waitingOf(campaignId: string): Waiting {
    let waiting = held.get(campaignId);
    if (waiting === undefined) {
        waiting = {
            ...wholeReads(campaignId, attentionStore({ campaignId })),
            campaignId,
            questions: signal({}),
            refused: signal(null),
            reasked: signal([]),
            approved: signal([]),
        };
        held.set(campaignId, waiting);
    }
    return waiting;
}

const said = (error: unknown): Said =>
    error instanceof ApiError ? { code: error.code, message: error.message } : { code: null, message: String(error) };

const listedIds = (store: AttentionStore): Set<string> =>
    new Set([...store.approvals.value, ...store.questions.value].map((row) => row.id));

const without = <T>(record: Readonly<Record<string, T>>, id: string): Record<string, T> =>
    Object.fromEntries(Object.entries(record).filter(([each]) => each !== id));

/**
 * One whole read of a listed item, never sent while another read of it is under way: what it read is kept while the
 * lists still hold the item, and what the runtime said when it failed is kept beside the item until a read of it
 * succeeds. A read the runtime has not answered holds its item until it is answered or `READ_LIMIT_MS` has passed;
 * then it counts as not answered, and like a failed one it is sent again by the next fill. Only the read under way
 * writes: one whose time ran out keeps nothing it later answers, so a late answer never overwrites a newer read, and
 * never makes an Approve live.
 */
function readOnce<T>(
    reads: WholeReads,
    kind: 'apr' | 'dec',
    id: string,
    read: () => Promise<T>,
    keep: (item: T) => void,
): Promise<void> {
    const key = `${reads.key}/${kind}/${id}`;
    if (reading.has(key)) return Promise.resolve();
    const mark = {};
    reading.set(key, mark);
    return new Promise<void>((done) => {
        /** Whether this read still writes: it is the one under way, and its time has not run out. */
        const current = (): boolean => {
            if (reading.get(key) !== mark) return false;
            reading.delete(key);
            clearTimeout(limit);
            done();
            return true;
        };
        const limit = setTimeout(() => {
            if (current() && listedIds(reads.store).has(id))
                reads.unread.value = { ...reads.unread.value, [id]: unanswered() };
        }, READ_LIMIT_MS);
        read().then(
            (item) => {
                if (!current()) return;
                if (Object.hasOwn(reads.unread.value, id)) reads.unread.value = without(reads.unread.value, id);
                keep(item);
            },
            (error: unknown) => {
                if (current() && !(error instanceof Unpaired) && listedIds(reads.store).has(id))
                    reads.unread.value = { ...reads.unread.value, [id]: said(error) };
            },
        );
    });
}

/** Does each id's work, at most `READS_AT_ONCE` of them at a time, in the order given. */
async function inTurn(ids: readonly string[], each: (id: string) => Promise<void>): Promise<void> {
    let next = 0;
    const lane = async (): Promise<void> => {
        while (next < ids.length) await each(ids[next++]!);
    };
    await Promise.all(Array.from({ length: Math.min(READS_AT_ONCE, ids.length) }, lane));
}

/**
 * Reads whole each approval the holder lists and has not read yet, a few at a time, and lets go of the reads, and of
 * what the runtime said, of the items the lists no longer hold. A read that fails leaves its approval drawn from the
 * list's row with what the runtime said, and is sent again by the next fill.
 */
export async function fillApprovals(api: Api, reads: WholeReads): Promise<void> {
    const approvals = keptAndMissing(
        reads.approvals.value,
        reads.store.approvals.value.map((row) => row.id),
    );
    const listed = listedIds(reads.store);
    reads.approvals.value = approvals.kept;
    reads.unread.value = Object.fromEntries(Object.entries(reads.unread.value).filter(([id]) => listed.has(id)));
    // A row read whole, or gone from the lists, while it waited its turn is not read again.
    await inTurn(approvals.missing, (id) =>
        Object.hasOwn(reads.approvals.value, id) || !reads.store.approvals.value.some((row) => row.id === id)
            ? Promise.resolve()
            : readOnce(
                  reads,
                  'apr',
                  id,
                  () => api.call<Approval>('approval.get', { approval_id: id }),
                  (item) => keepWhole(reads, item),
              ),
    );
}

/** An approval read whole, by its holder or by Details: kept as its whole read while the holder still lists it. */
export function keepWhole(reads: WholeReads, approval: Approval): void {
    if (!reads.store.approvals.value.some((row) => row.id === approval.id)) return;
    if (Object.hasOwn(reads.unread.value, approval.id)) reads.unread.value = without(reads.unread.value, approval.id);
    reads.approvals.value = { ...reads.approvals.value, [approval.id]: approval };
}

/**
 * Approves an approval only as it was read whole: nothing when it has not been; a message is first checked against its
 * work item, as the browser's approval panel checks it, and is not approved when its text moved since it was asked or
 * it could not be checked. Returns the sentence that says why it was not approved, or null; a pairing that has ended
 * approves nothing, and the session answers for it. `checking` is told while the check is under way.
 */
export async function approveWhole(
    api: Api,
    whole: Approval | undefined,
    approve: () => Promise<boolean>,
    checking: (on: boolean) => void = () => undefined,
): Promise<string | null> {
    if (whole === undefined) return null;
    if (isMessage(whole.operation)) {
        checking(true);
        const checked = await checkMessage(api, whole).finally(() => checking(false));
        if (checked === null) return null;
        if ('unchecked' in checked) return uncheckedSentence(checked.unchecked);
        if (checked.stale) return STALE_SENTENCE;
    }
    await approve();
    return null;
}

/**
 * Reads whole each row the lists hold and this chat has not read yet, and lets go of the rows they no longer hold. A
 * read that fails leaves its card drawn from the list's row with what the runtime said, and is sent again by the next
 * fill; one the runtime has not answered is not sent again while it is under way, until its time runs out.
 */
async function fill(api: Api, waiting: Waiting): Promise<void> {
    const questions = keptAndMissing(
        waiting.questions.value,
        waiting.store.questions.value.map((row) => row.id),
    );
    waiting.questions.value = questions.kept;
    waiting.reasked.value = stillAsking(waiting.reasked.value, waiting.store.approvals.value);
    await Promise.all([
        fillApprovals(api, waiting),
        ...questions.missing.map((id) =>
            readOnce(
                waiting,
                'dec',
                id,
                () => api.call<Decision>('decision.get', { decision_id: id }),
                (item) => {
                    if (waiting.store.questions.value.some((row) => row.id === id))
                        waiting.questions.value = { ...waiting.questions.value, [id]: item };
                },
            ),
        ),
    ]);
}

/**
 * Reads what waits in the campaign again: its two lists, then each new row whole. A refusal whose item the lists no
 * longer hold goes, as that item's card did.
 */
export async function readWaiting(api: Api, waiting: Waiting): Promise<void> {
    await listWaiting(api, waiting);
    await fill(api, waiting);
    await followApproved(api, waiting);
}

/** The work item states that end an approved act, and what each says of it. */
const ended: Readonly<Record<string, Approved['state']>> = {
    succeeded: 'done',
    failed: 'failed',
    cancelled: 'cancelled',
};

/**
 * Reads again the work of each approval this chat approved that has not ended, and keeps how it stands. A read that
 * fails keeps what was known; the next read tries again.
 */
export async function followApproved(api: Api, waiting: Waiting): Promise<void> {
    const open = waiting.approved.value.filter((each) => each.state === 'approved');
    await Promise.all(
        open.map(async (each) => {
            try {
                const state = ended[(await readItem(api, each.workItemId)).status];
                if (state === undefined) return;
                waiting.approved.value = waiting.approved.value.map((now) =>
                    now.approvalId === each.approvalId ? { ...now, state } : now,
                );
            } catch {
                // Kept as it was: a later read says it.
            }
        }),
    );
}

/** The campaign's two lists read again; a refusal whose item they do not hold now goes. */
async function listWaiting(api: Api, waiting: Waiting): Promise<void> {
    await waiting.store.load(api, false);
    const refused = waiting.refused.value;
    if (refused !== null && !listedIds(waiting.store).has(refused.id)) waiting.refused.value = null;
}

/**
 * A chat of the campaign is drawn: what waits is read now and on every journal line while it stays drawn. A drawing
 * starts from what the runtime lists; what an earlier drawing said of its own acts went with it.
 */
export function drawWaiting(api: Api, campaignId: string): () => void {
    const waiting = waitingOf(campaignId);
    if (!drawn.has(campaignId)) {
        waiting.refused.value = null;
        waiting.reasked.value = [];
        waiting.approved.value = [];
    }
    drawn.set(campaignId, (drawn.get(campaignId) ?? 0) + 1);
    void readWaiting(api, waiting);
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
 * code and the item it named, so a card that stays is never a silent revert, and one that left says why.
 */
async function decide(api: Api, waiting: Waiting, id: string, act: () => Promise<boolean>): Promise<boolean> {
    waiting.refused.value = null;
    const done = await act();
    const failure = waiting.store.failure.value;
    if (!done && failure !== null)
        waiting.refused.value = { id, code: waiting.store.failureCode.value, message: failure };
    await fill(api, waiting);
    return done;
}

/**
 * Approves one approval through the holder. When it was approved, `act` (what it was, as the card showed it) is kept so
 * the chat says what was approved and, once its work ends, whether it was sent.
 */
export async function approveWaiting(
    api: Api,
    waiting: Waiting,
    id: string,
    act?: Omit<Approved, 'approvalId' | 'state'>,
): Promise<boolean> {
    const done = await decide(api, waiting, id, () => waiting.store.approve(api, id));
    if (done && act !== undefined) {
        waiting.approved.value = [
            ...waiting.approved.value.filter((each) => each.approvalId !== id),
            { ...act, approvalId: id, state: 'approved' },
        ];
        await followApproved(api, waiting);
    }
    return done;
}

export const rejectWaiting = (api: Api, waiting: Waiting, id: string, reason?: string): Promise<boolean> =>
    decide(api, waiting, id, () => waiting.store.reject(api, id, reason));

export const answerWaiting = (
    api: Api,
    waiting: Waiting,
    id: string,
    choice: { option?: string; text?: string },
    reason?: string,
): Promise<boolean> => decide(api, waiting, id, () => waiting.store.answer(api, id, choice, reason));

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
    // The save is an act of the person's: the cards' acts stay held until the lists read after it have answered.
    void waiting.store.hold(() => listWaiting(api, waiting)).then(() => fill(api, waiting));
}
