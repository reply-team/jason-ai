import { batch, effect, signal } from '@preact/signals-react';
import { Unpaired } from '../api/client';
import type { Approval, ApprovalSummary, DecisionSummary, WorkItem } from '../api/types';
import { attentionStore, repeated } from '../screens/attention/store';
import { reasonLabel } from '../screens/attention/labels';
import {
    TextMoved,
    editable,
    hasSubject,
    heldArgs,
    readItem,
    saveEdit,
    sentence,
    wordsOf,
    type Words,
} from '../screens/attention/message-edit';
import { asking, changedSince, edits, noteEdit, withdrawn } from '../screens/attention/reasking';
import {
    ASKING_MARK,
    DIFFERS_SENTENCE,
    askedAfter,
    stateSentence,
    withWords,
} from '../screens/attention/reasking-model';
import { doubtWords } from '../screens/send-modes/model';
import { act, argsOf, define } from './actions';
import { html, type Html } from './html';
import { svg } from './icons';
import { offUnless } from './off';
import { ageOf, searchLines, toApprovalDetail, toApprovalRow, toQuestionRow } from './live';
import { chatWaits } from './needs-chats';
import {
    agentsOf,
    countWords,
    filtersOf,
    moreUnder,
    pick,
    waitingOf,
    type Chosen,
    type NeedsFilter,
    type NeedsInput,
    type WaitingItem,
} from './needs-model';
import { clock, here } from './state';
import { lookupFor, readSender, senderMark, senderReads } from './senders';
import { SENDER_WHEN, senderAsked, senderLine, senderWords } from './senders-model';
import { approveWhole, fillApprovals, keepWhole, wholeReads } from './waiting';
import { approvalGate, readingSentence, unreadSentence, type ApprovalGate } from './waiting-model';

// Needs you: what waits on a person, in two panes. On the left every approval and question the runtime lists (and,
// once read, every chat whose AI waits on a choice), under a filter and an agent; on the right the one chosen, whole,
// with the person's own acts on it. Approvals and questions are read and decided the way the browser's Attention screen
// reads and decides them, in a holder of the panel's own: the console's area keeps that screen as it was left while
// this panel is shown, and neither replaces the other's list. Every count is a length of what the runtime listed.

/** The panel's own approvals and questions. */
export const attention = attentionStore({ keepPages: true });

/**
 * The panel's approvals read whole, as an agent's chat reads its cards': while the panel is drawn, each listed approval
 * is read once, a few at a time, and its Approve is live only once it has been read (`approvalGate`).
 */
export const reads = wholeReads('needs', attention);

/** How many drawings of the panel there are: its rows are read whole only while it is drawn. */
let drawn = 0;

/**
 * The approvals whose message is being checked against its work item, at the press of their Approve or their Edit: a
 * check the runtime has not answered holds its own approval and no other.
 */
const checking = signal<ReadonlySet<string>>(new Set());

/** What the pane says while its message is being checked, before it is approved. */
const checkingSentence = 'This message is being checked against its work item before it is approved.';

const checked = (id: string, on: boolean): void => {
    const rows = new Set(checking.value);
    if (on) rows.add(id);
    else rows.delete(id);
    checking.value = rows;
};

/** Why an approval's last Approve or Edit did nothing, by its id: until its next press, or until it leaves. */
const unapproved = signal<Readonly<Record<string, string>>>({});

const said = (id: string, why: string | null): void => {
    const rest = Object.fromEntries(Object.entries(unapproved.value).filter(([each]) => each !== id));
    unapproved.value = why === null ? rest : { ...rest, [id]: why };
};

/** Which of what waits the panel shows. Kept to the panel. */
export const filter = signal<NeedsFilter>('all');

/** The agent the list is narrowed to, by its campaign's id; null for every agent. Kept to the panel. */
export const agent = signal<string | null>(null);

/** The item the person chose; the pane shows it while it is listed under the filter, else the first one listed. */
export const chosen = signal<Chosen | null>(null);

/** The approval whose Reject asks to be confirmed, by its id. */
const rejecting = signal<string | null>(null);

/** An edit of a message under way: the work item read for it, the words it opened with, and what it says. */
interface Editing {
    id: string;
    item: WorkItem;
    opened: Words;
    /** The words the item holds now, when they moved while the person edited. */
    moved: Words | null;
    problem: string | null;
    saving: boolean;
}
const editing = signal<Editing | null>(null);

/** What Needs you holds, as the pure model reads it: the runtime's two lists and, once read, the chats that wait. */
export function needsInput(): NeedsInput {
    return {
        approvals: attention.approvals.value,
        questions: attention.questions.value,
        chats: chatWaits.value,
        more: {
            approvals: attention.approvalsCursor.value !== null,
            questions: attention.questionsCursor.value !== null,
        },
    };
}

// When nothing waits at all the panel draws its card and no filter row, so the filter and the agent go back to
// everything: what arrives next is drawn whatever was chosen before.
effect(() => {
    if (!attention.loaded.value) return;
    const input = needsInput();
    if (input.approvals.length === 0 && input.questions.length === 0 && (input.chats ?? []).length === 0) {
        filter.value = 'all';
        agent.value = null;
    }
});

/** The questions and approvals the panel itself asked for whole: their answers are no person's choice of what to show. */
const ownOpens = new Set<string>();

/** Opens an item whole for the panel's own sake, not as a choice made elsewhere. */
function openOwn(kind: 'approvals' | 'questions', id: string): Promise<void> {
    ownOpens.add(id);
    return attention.open(here().api, kind, id).finally(() => ownOpens.delete(id));
}

// An approval or a question opened from elsewhere (an agent's chat's Details, the dashboard) is the one the pane shows,
// in view. One the panel opened for itself changes nothing, however late its answer comes.
effect(() => {
    const shown = attention.detail.value;
    if (shown === null) return;
    if (ownOpens.has(shown.item.id)) return;
    const opened: Chosen = { kind: shown.kind === 'approvals' ? 'approval' : 'question', id: shown.item.id };
    const now = chosen.peek();
    if (now?.kind === opened.kind && now.id === opened.id) return;
    chosen.value = opened;
    const items = waitingOf(needsInput(), filter.peek(), agent.peek());
    if (!items.some((item) => item.kind === opened.kind && item.id === opened.id)) {
        filter.value = 'all';
        agent.value = null;
    }
});

/** The items listed under the filter and the agent, and the one the pane shows. */
function listed(): { items: WaitingItem[]; shown: WaitingItem | null } {
    const items = waitingOf(needsInput(), filter.value, agent.value);
    return { items, shown: placeOf(items, chosen.value) };
}

/** The order the list was last drawn in, so that the item after one that left is known. */
let lastOrder: readonly Chosen[] = [];

/**
 * Where the pane stands: the item chosen while it is listed; when it left, the one after it in the order last drawn that
 * is still listed; with nothing chosen yet, the first. Nothing else moves it.
 */
function placeOf(items: readonly WaitingItem[], now: Chosen | null): WaitingItem | null {
    const kept = pick(items, now);
    if (now === null || (kept !== null && kept.kind === now.kind && kept.id === now.id)) return kept;
    const at = lastOrder.findIndex((each) => each.kind === now.kind && each.id === now.id);
    if (at < 0) return kept;
    for (const each of [...lastOrder.slice(at + 1), ...lastOrder.slice(0, at)]) {
        const found = items.find((item) => item.kind === each.kind && item.id === each.id);
        if (found !== undefined) return found;
    }
    return kept;
}

/** What the person typed into a field of the panel, kept while the panel is painted again around it. */
const typed = new Map<string, string>();

const field = (key: string, label: string): Html =>
    html`<input
        class="inp"
        type="text"
        aria-label="${label}"
        placeholder="${label}"
        value="${typed.get(key) ?? ''}"
        data-input="needs.type"
        ${argsOf(key)}
    />`;

const optional = (key: string): string | undefined => typed.get(key)?.trim() || undefined;

/** The question the pane shows, read whole for its options when it has not been. */
function readShown(): void {
    if (drawn === 0) return;
    const { shown } = listed();
    const open = attention.detail.peek();
    if (shown?.kind === 'question' && !(open?.kind === 'questions' && open.item.id === shown.id))
        void openOwn('questions', shown.id);
}

/**
 * Puts the keyboard on a line of the list, once the panel has been painted with it: only when the keyboard is nowhere
 * (on the page's body, where the button pressed left it), never taken from a field the person has moved to.
 */
function focusRow(id: string): void {
    const active = document.activeElement;
    if (active !== null && active !== document.body) return;
    for (const row of document.querySelectorAll<HTMLElement>('.needs-row'))
        if (row.dataset['row'] === id) {
            row.focus();
            return;
        }
}

/** Puts the keyboard on the first element of the pane that matches, if there is one. */
function focusPane(selector: string): void {
    document.querySelector<HTMLElement>(`.needs-pane ${selector}`)?.focus();
}

/**
 * How many of the person's acts are under way, from the press until the lists read after it have answered, counted by
 * the holder (`attention.acting`): the pane's acts are drawn disabled meanwhile, so a second click never lands on the
 * next item's, and an item that leaves meanwhile left by the person's hand.
 */
const acting = attention.acting;

/**
 * The reads behind the sending name under an approval's From (`senders.ts`), and the approval the pane draws it for with
 * the moment it was drawn: the pane reads when it comes to show an approval, and not again while it keeps showing it.
 */
const senders = senderReads();
let sending: { id: string; drawn: number } | null = null;

/** Whether the item the pane showed left while the person was not acting on it, until they choose or act again. */
const left = signal(false);

/** Holds the pane where it stands (`placeOf`) and remembers the order drawn; true when the item chosen had left. */
function keepPlace(): boolean {
    const items = waitingOf(needsInput(), filter.value, agent.value);
    const now = chosen.value;
    const shown = placeOf(items, now);
    // Only an item that was in the lists read can be said to have left them: one opened by a link to a page not read
    // yet was never there.
    const wasListed = now !== null && lastOrder.some((each) => each.kind === now.kind && each.id === now.id);
    const moved = wasListed && (shown === null || shown.kind !== now.kind || shown.id !== now.id);
    chosen.value = shown === null ? null : { kind: shown.kind, id: shown.id };
    lastOrder = items.map((item) => ({ kind: item.kind, id: item.id }));
    return moved;
}

/**
 * Lets go of what was said of items that left, keeps the pane where the person is, and reads whole the approvals not
 * read yet while the panel is drawn.
 */
async function fill(): Promise<void> {
    take();
    if (drawn > 0) await fillApprovals(here().api, reads);
}

/** What `fill` does at once, before any read: what was said of items that left, and where the pane stands. */
function take(): void {
    const listedIds = new Set(attention.approvals.value.map((row) => row.id));
    batch(() => {
        if (Object.keys(unapproved.value).some((id) => !listedIds.has(id)))
            unapproved.value = Object.fromEntries(Object.entries(unapproved.value).filter(([id]) => listedIds.has(id)));
        if (rejecting.value !== null && !listedIds.has(rejecting.value)) rejecting.value = null;
        if (editing.value !== null && !listedIds.has(editing.value.id)) editing.value = null;
        // A message edited here that the runtime asked about again is the new approval, still the one chosen.
        const now = chosen.value;
        const asked = now?.kind === 'approval' ? askedAfter(edits.value, now.id) : null;
        if (asked !== null && listedIds.has(asked)) chosen.value = { kind: 'approval', id: asked };
        if (agent.value !== null && !agentsOf(needsInput()).some((each) => each.id === agent.value)) agent.value = null;
        // An item that leaves while no act of the person's is under way left by someone else's hand: the pane says so.
        if (keepPlace() && acting.value === 0) left.value = true;
    });
    readShown();
}

/** An act of the person's, after which the keyboard is on the line the pane then shows, never on a button. */
async function acted(work: () => Promise<unknown>): Promise<void> {
    left.value = false;
    await attention.hold(async () => {
        await work();
        take();
    });
    const shown = chosen.value;
    if (shown !== null) focusRow(shown.id);
    if (drawn > 0) await fillApprovals(here().api, reads);
}

/**
 * Reads what waits again, every page the person had read, then each approval not read whole yet. `followed` says the
 * journal's word asked for it, so a row edited here is read again only when a line named its work item.
 */
export async function reload(followed = false): Promise<void> {
    await attention.load(here().api, false, followed);
    await fill();
}

/** Forgets what the panel holds of its own: the filter, the agent, the choice, and every act under way; for a test. */
export function forgetNeeds(): void {
    filter.value = 'all';
    agent.value = null;
    chosen.value = null;
    rejecting.value = null;
    editing.value = null;
    checking.value = new Set();
    unapproved.value = {};
    typed.clear();
    attention.forgetPages();
    lastOrder = [];
    ownOpens.clear();
    left.value = false;
    acting.value = 0;
    senders.lookups.value = {};
    sending = null;
}

/** The panel is drawn: its approvals are read whole now, and at every reload while it stays drawn. */
export function drawNeeds(): () => void {
    drawn += 1;
    // A pane drawn again is a new drawing: the approval it shows reads its sending name again.
    sending = null;
    void fill();
    return () => {
        drawn = Math.max(0, drawn - 1);
    };
}

define('needs.type', (args, element) => {
    if (typeof args[0] === 'string' && (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement))
        typed.set(args[0], element.value);
});

/** After the filter or the agent changed: the pane keeps its item if it is still listed, else shows the first. */
function narrowed(): void {
    left.value = false;
    const { items } = listed();
    const now = chosen.value;
    if (now === null || !items.some((item) => item.kind === now.kind && item.id === now.id))
        chosen.value = items[0] === undefined ? null : { kind: items[0].kind, id: items[0].id };
    lastOrder = items.map((item) => ({ kind: item.kind, id: item.id }));
    readShown();
}

define('needs.filter', ([value]) => {
    const found = filtersOf(needsInput(), agent.value).find(([each]) => each === value);
    if (!found) return;
    filter.value = found[0];
    narrowed();
});

define('needs.agent', (_, element) => {
    if (!(element instanceof HTMLSelectElement)) return;
    const value = element.value;
    agent.value = agentsOf(needsInput()).some((each) => each.id === value) ? value : null;
    narrowed();
});

/** The width under which the panes stand one above the other: the media query the panel's sheet narrows at. */
const NARROW = '(max-width: 900px)';

define('needs.pick', ([kind, id]) => {
    if ((kind === 'approval' || kind === 'question' || kind === 'chat') && typeof id === 'string') {
        batch(() => {
            chosen.value = { kind, id };
            rejecting.value = null;
            left.value = false;
        });
        readShown();
        // Where the list and the pane stand one above the other, the pane chosen is brought into view.
        if (window.matchMedia?.(NARROW).matches)
            document.querySelector('.needs-pane')?.scrollIntoView({ block: 'start' });
    }
});

// Show more reads one more page of each list that has one, and every read after it reads that page again.
define('needs.more', () => {
    const api = here().api;
    void Promise.all([attention.more(api, 'approvals', false), attention.more(api, 'questions', false)]).then(fill);
});

define('needs.campaigns', () => here().dock.show('campaigns'));

define('needs.chat', ([id, name]) => {
    if (typeof id === 'string' && typeof name === 'string') here().dock.chat({ id, name });
});

// A read of an approval that failed is asked for again by the person: once it answers, its Approve is live.
define('needs.read', ([id]) => {
    if (typeof id === 'string')
        void openOwn('approvals', id).then(() => {
            const shown = attention.detail.value;
            if (shown?.kind === 'approvals' && shown.item.id === id) keepWhole(reads, shown.item);
        });
});

// An item opened from elsewhere (a chat's card's Open): Needs you is shown first, then the item, chosen in the pane and
// read whole, so an approval whose own read failed can be approved once this has read it.
define('needs.open', ([kind, id]) => {
    if ((kind !== 'approvals' && kind !== 'questions') || typeof id !== 'string') return;
    here().dock.show('needs');
    void attention.open(here().api, kind, id).then(() => {
        const shown = attention.detail.value;
        if (shown?.kind === 'approvals' && shown.item.id === id) keepWhole(reads, shown.item);
    });
});

// Approve acts only on an approval read whole, a message checked against its work item first: the chat's cards' rule.
define('needs.approve', ([id], _, event) => {
    if (repeated(event)) return;
    if (typeof id !== 'string' || checking.value.has(id)) return;
    const api = here().api;
    said(id, null);
    void acted(() =>
        approveWhole(
            api,
            reads.approvals.value[id],
            () => attention.approve(api, id),
            (on) => checked(id, on),
        ).then((why) => {
            if (why !== null) said(id, why);
        }),
    );
});

// Reject asks first, in a dialog that takes the keyboard and gives it back to Reject when it closes without rejecting.
// It takes the keyboard on Cancel: the reason is optional, and Enter as it opens rejects nothing.
define('needs.reject', ([id]) => {
    if (typeof id !== 'string') return;
    typed.delete(`${id}:reason`);
    rejecting.value = id;
    focusPane('.needs-reject [data-action="needs.unreject"]');
});

define('needs.unreject', () => {
    rejecting.value = null;
    focusPane('[data-action="needs.reject"]');
});

define('needs.rejected', ([id], _, event) => {
    if (repeated(event)) return;
    if (typeof id !== 'string' || rejecting.value !== id) return;
    rejecting.value = null;
    // Only an approval still listed is rejected: one that left while the question stood open is no longer the person's.
    if (!attention.approvals.value.some((row) => row.id === id)) return;
    void acted(() => attention.reject(here().api, id, optional(`${id}:reason`)));
});

// Edit reads the message's work item, and the boxes open with the words it holds now: a person never replaces words
// they have not seen. Save writes them through the work item's update and approves nothing.
define('needs.edit', ([id]) => {
    const whole = typeof id === 'string' ? reads.approvals.value[id] : undefined;
    if (whole === undefined || checking.value.has(whole.id) || withdrawn(whole.id) !== undefined) return;
    said(whole.id, null);
    checked(whole.id, true);
    readItem(here().api, whole.work_item_id)
        .then((item) => {
            const opened = wordsOf(heldArgs(item), whole.operation);
            typed.set(`${whole.id}:subject`, opened.subject ?? '');
            typed.set(`${whole.id}:body`, opened.body ?? '');
            rejecting.value = null;
            editing.value = { id: whole.id, item, opened, moved: null, problem: null, saving: false };
        })
        .catch((error: unknown) => {
            if (!(error instanceof Unpaired)) said(whole.id, sentence(error));
        })
        .finally(() => {
            checked(whole.id, false);
            focusPane('.needs-edit textarea');
        });
});

define('needs.unedit', () => {
    editing.value = null;
    focusPane('[data-action="needs.edit"]');
});

/** The reason the journal keeps for an edit made here: what was done, in plain words. */
export const NEEDS_EDIT_REASON = "Changed the message's words before deciding on it.";

define('needs.save', ([id], _, event) => {
    if (repeated(event)) return;
    const now = editing.value;
    const whole = typeof id === 'string' ? reads.approvals.value[id] : undefined;
    if (now === null || whole === undefined || now.id !== whole.id || now.saving) return;
    const subject = typed.get(`${whole.id}:subject`) ?? '';
    const body = typed.get(`${whole.id}:body`) ?? '';
    // Words that did not change are not written: the runtime would leave the approval as it is, and nothing is asked again.
    if (subject === (now.opened.subject ?? '') && body === (now.opened.body ?? '')) {
        editing.value = null;
        focusRow(whole.id);
        return;
    }
    const api = here().api;
    editing.value = { ...now, saving: true, problem: null };
    saveEdit(api, whole.operation, whole.work_item_id, subject, body, now.opened, NEEDS_EDIT_REASON)
        .then((item) => {
            editing.value = null;
            // The row stays where it is, chosen, with the words saved, until the runtime has asked again (`reasking.ts`).
            noteEdit(whole, item);
            return acted(() => attention.load(api, false));
        })
        .catch((error: unknown) => {
            const still = editing.value;
            if (still === null || still.id !== whole.id) return;
            if (error instanceof TextMoved) {
                // What the person typed stays in the boxes; the new text is shown beside them and is what Save checks now.
                const fresh = wordsOf(heldArgs(error.fresh), whole.operation);
                editing.value = {
                    ...still,
                    item: error.fresh,
                    opened: fresh,
                    moved: fresh,
                    problem: error.message,
                    saving: false,
                };
                return;
            }
            editing.value = {
                ...still,
                saving: false,
                problem: error instanceof Unpaired ? null : sentence(error),
            };
        });
});

define('needs.option', ([id, option], _, event) => {
    if (repeated(event)) return;
    if (typeof id === 'string' && typeof option === 'string')
        void acted(() => attention.answer(here().api, id, { option }, optional(`${id}:reason`)));
});

define('needs.answer', ([id], _, event) => {
    if (repeated(event)) return;
    if (typeof id !== 'string') return;
    const text = optional(`${id}:text`);
    if (text) void acted(() => attention.answer(here().api, id, { text }, optional(`${id}:reason`)));
});

/** Who doubted a send and why, from the approval read whole, in the words the chat's cards and the browser use. */
function doubts(preview: Record<string, unknown>): Html | null {
    const doubted = doubtWords(preview);
    if (doubted === null) return null;
    return html`<div class="section send-doubts">
        <h3>${doubted.heading}</h3>
        <ul>
            ${doubted.doubts.map(
                (doubt) =>
                    html`<li>
                        <p>${doubt.said}</p>
                        ${doubt.note === null ? null : html`<p class="message-text">${doubt.note}</p>`}
                    </li>`,
            )}
        </ul>
    </div>`;
}

/** Why Approve is held: the approval is being read, or could not be read, with what the runtime said. */
function heldNote(gate: ApprovalGate, id: string): Html | null {
    if (gate.live) return null;
    if ('reading' in gate) return html`<div class="note am" role="status">${readingSentence('needs')}</div>`;
    const { code, message } = gate.unread;
    return html`<div class="note am" role="status">
        ${unreadSentence('needs', gate.unread)} ${code === null ? null : html`<code>${code}</code>`} ${message}
        <button class="btn sm" type="button" ${act('needs.read', id)}>Read again</button>
    </div>`;
}

/** The approval read whole: by the panel's own read, or by one opened from elsewhere. */
function wholeOf(id: string): Approval | undefined {
    const whole = reads.approvals.value[id];
    if (whole !== undefined) return whole;
    const shown = attention.detail.value;
    return shown?.kind === 'approvals' && shown.item.id === id ? shown.item : undefined;
}

/** The person an approval is about, with "to" before the name when the operation reaches them by a message. */
function person(summary: ApprovalSummary, now: number): Html | null {
    const row = toApprovalRow(summary, now, wholeOf(summary.id)?.preview);
    return row.who ? html`<span class="needs-who">${row.byMessage ? 'to ' : null}<span>${row.who}</span></span>` : null;
}

/** One line of the list: what it is, whose it is, and how long it has waited. Choosing it shows it in the pane. */
function listRow(item: WaitingItem, shown: WaitingItem | null, now: number): Html {
    const current = shown !== null && shown.kind === item.kind && shown.id === item.id;
    const choose = html`${current ? html`aria-current="true"` : null} ${act('needs.pick', item.kind, item.id)}`;
    if (item.kind === 'approval') {
        const row = toApprovalRow(item.row, now);
        return html`<button class="needs-row" type="button" data-row="${item.id}" ${choose}>
            <span class="needs-ic amber" aria-hidden="true">!</span>
            <span class="needs-row-b">
                <span class="needs-row-t">${row.what} ${person(item.row, now)}</span>
                <span class="needs-row-m"
                    >${asking(item.id) === undefined ? 'Approval' : ASKING_MARK} · ${row.campaign}</span
                >
            </span>
            <span class="needs-row-a">${row.age}</span>
        </button>`;
    }
    if (item.kind === 'question') {
        const row = toQuestionRow(item.row, now);
        return html`<button class="needs-row" type="button" data-row="${item.id}" ${choose}>
            <span class="needs-ic violet" aria-hidden="true">?</span>
            <span class="needs-row-b">
                <span class="needs-row-t">${row.question}</span>
                <span class="needs-row-m">Question · ${row.campaign}</span>
            </span>
            <span class="needs-row-a">${row.age}</span>
        </button>`;
    }
    return html`<button class="needs-row" type="button" data-row="${item.id}" ${choose}>
        <span class="needs-ic blue" aria-hidden="true">?</span>
        <span class="needs-row-b">
            <span class="needs-row-t">Asked you in its chat</span>
            <span class="needs-row-m">Chat · ${item.chat.agent ?? item.chat.title}</span>
        </span>
        <span class="needs-row-a">${ageOf(item.chat.at, now)}</span>
    </button>`;
}

/** "Open the chat": the agent's own conversation, shown; a way there, which decides nothing. */
const openChat = (campaignId: string, name: string): Html =>
    html`<button class="btn sm ghost" type="button" ${act('needs.chat', campaignId, name)}>Open the chat</button>`;

/** What the message says, whole (the pane never clamps it), and below it what the act is: its reach, reversibility and cost. */
function words(whole: Approval | undefined): Html | null {
    if (whole === undefined) return null;
    // A message edited here shows the words saved, which the runtime asks again with, never the withdrawn ones.
    const edit = withdrawn(whole.id);
    const read = toApprovalDetail(whole.preview);
    const detail = edit === undefined ? read : withWords(read, edit.words);
    return html`<div class="needs-words">
            ${detail.to ? html`<div class="meta">To ${detail.to}</div>` : null}
            ${
                detail.from
                    ? html`<div class="meta needs-from">From <span>${detail.from}</span></div>
                          ${senderOf(whole)}`
                    : null
            }
            ${detail.channel ? html`<div class="meta needs-channel">Channel <span>${detail.channel}</span></div>` : null}
            ${detail.subject ? html`<div class="ask-s">${detail.subject}</div>` : null}
            ${detail.body ? html`<div class="said needs-body">${detail.body}</div>` : null}
            ${
                detail.subject === null && detail.body === null && detail.intent
                    ? html`<div class="needs-intent">${detail.intent}</div>`
                    : null
            }
            ${
                detail.search === null
                    ? null
                    : html`<div class="needs-search">
                          ${searchLines(detail.search).map((line) => html`<div>${line}</div>`)}
                      </div>`
            }
        </div>
        ${detail.facts.length > 0 ? html`<div class="needs-facts">${detail.facts.map((fact) => html`<div class="meta">${withCode(fact)}</div>`)}</div>` : null}`;
}

/** A no-break space: the one character a held line holds, so it has the height of a line of words. */
const NBSP = ' ';

/**
 * The sending name the provider holds now for the mailbox the send names, in the words of an agent's chat's cards: one
 * read per approval the pane shows, its place held at the line's height while it is read, nothing where the send names
 * no mailbox. It decides nothing; Approve and its gate are the pane's.
 */
function senderOf(whole: Approval): Html | null {
    const asked = senderAsked(whole);
    if (asked === null) return null;
    if (sending?.id !== whole.id) {
        sending = { id: whole.id, drawn: senderMark() };
        void readSender(here().api, senders, asked, sending.drawn);
    }
    const line = senderLine(lookupFor(senders, asked, sending.drawn), asked);
    // While the read is out the line's place keeps its height, so the acts below do not move when it arrives.
    if (line === null)
        return html`<div class="sender-line sender-hold" aria-hidden="true">
            <div class="sender-now">${NBSP}</div>
            <div class="sender-when">${NBSP}</div>
        </div>`;
    const said = senderWords(line);
    if (said === null) return null;
    return html`<div class="sender-line">
        <div class="sender-now">
            ${said.text}${said.code === null ? null : html` <code>${said.code}</code>`}${said.message === null ? null : html` ${said.message}`}
        </div>
        ${said.when ? html`<div class="sender-when">${SENDER_WHEN}</div>` : null}
    </div>`;
}

/** A fact as the runtime words it, its quoted names set as code rather than shown between backticks. */
function withCode(fact: string): Html[] {
    return fact.split('`').map((part, at) => (at % 2 === 1 ? html`<code>${part}</code>` : html`${part}`));
}

/** The box of a message's body. Its words follow its tag at once: a line break there would be read as part of them. */
// prettier-ignore
const bodyBox = (id: string, disabled: Html | null): Html =>
    html`<textarea class="inp" rows="8" data-input="needs.type" ${argsOf(`${id}:body`)} ${disabled}>${typed.get(`${id}:body`) ?? ''}</textarea>`;

/** The boxes that edit a message's words, the words that moved meanwhile, and Save and Cancel. */
function editBoxes(edit: Editing, operation: string, busy: boolean): Html {
    const id = edit.id;
    const disabled = edit.saving ? html`disabled` : null;
    return html`<div class="needs-edit message-editor">
        ${
            hasSubject(heldArgs(edit.item), operation)
                ? html`<label class="field"
                      >Subject
                      <input
                          class="inp"
                          type="text"
                          value="${typed.get(`${id}:subject`) ?? ''}"
                          data-input="needs.type"
                          ${argsOf(`${id}:subject`)}
                          ${disabled}
                  /></label>`
                : null
        }
        <label class="field">Body ${bodyBox(id, disabled)}</label>
        ${edit.problem === null ? null : html`<div class="note am" role="alert">${edit.problem}</div>`}
        ${
            edit.moved === null
                ? null
                : html`<div class="message-card needs-moved">
                      ${edit.moved.subject ? html`<div class="message-text">${edit.moved.subject}</div>` : null}
                      ${edit.moved.body ? html`<div class="message-text">${edit.moved.body}</div>` : null}
                  </div>`
        }
        <div class="ask-w needs-save-says">
            Saving changes the words only and approves nothing: Jason asks you again.
        </div>
        <div class="ask-a">
            <button
                class="btn sm p"
                type="button"
                ${edit.saving || busy ? html`disabled` : null}
                ${act('needs.save', id)}
            >
                Save
            </button>
            <button class="btn sm" type="button" ${disabled} ${act('needs.unedit')}>Cancel</button>
        </div>
    </div>`;
}

function approvalPane(summary: ApprovalSummary, now: number, busy: boolean): Html {
    const row = toApprovalRow(summary, now, wholeOf(summary.id)?.preview);
    const whole = wholeOf(summary.id);
    // Approve is live only once the approval has been read whole, whatever it does: the rule is the approval's.
    const gate = approvalGate(reads.approvals.value[summary.id], reads.unread.value[summary.id]);
    const inCheck = checking.value.has(summary.id);
    const why = unapproved.value[summary.id];
    const edit = editing.value?.id === summary.id ? editing.value : null;
    const asked = rejecting.value === summary.id;
    // An approval an edit withdrew is acted on in no way: what it says is its state, and the agent's chat stays a way there.
    const kept = withdrawn(summary.id);
    const reasking = kept !== undefined;
    const acts = reasking
        ? html`<div class="ask-a">${openChat(summary.campaign_id, summary.campaign_name)}</div>`
        : edit
          ? editBoxes(edit, summary.operation, busy)
          : asked
            ? html`<div
                  class="note am needs-reject"
                  role="alertdialog"
                  aria-label="Reject this?"
                  data-escape="needs.unreject"
              >
                  <div>Reject this? It ends the work, and a rejection cannot be taken back.</div>
                  ${field(`${summary.id}:reason`, 'Reason (optional, kept in the journal)')}
                  <div class="ask-a">
                      <button class="btn sm" type="button" ${act('needs.unreject')}>Cancel</button>
                      <button
                          class="btn sm no"
                          type="button"
                          ${busy ? html`disabled` : null}
                          ${act('needs.rejected', summary.id)}
                      >
                          Reject
                      </button>
                  </div>
              </div>`
            : html`<div class="ask-a">
                  <button
                      class="btn sm p"
                      type="button"
                      ${busy || inCheck || !gate.live ? html`disabled` : null}
                      ${act('needs.approve', summary.id)}
                  >
                      Approve
                  </button>
                  ${
                      whole !== undefined && gate.live && editable(whole)
                          ? html`<button
                                class="btn sm"
                                type="button"
                                ${busy || inCheck ? html`disabled` : null}
                                ${act('needs.edit', summary.id)}
                            >
                                Edit
                            </button>`
                          : null
                  }
                  <button
                      class="btn sm"
                      type="button"
                      ${busy || inCheck ? html`disabled` : null}
                      ${act('needs.reject', summary.id)}
                  >
                      Reject
                  </button>
                  ${openChat(summary.campaign_id, summary.campaign_name)}
              </div>`;
    return html`<section class="needs-pane ask" data-id="${summary.id}" aria-label="${row.what}">
        <div class="ask-h">
            <span class="b amber">Waiting for you</span>
            <span class="meta">${row.campaign} · ${row.age}</span>
        </div>
        <h2 class="needs-title">${row.what} ${person(summary, now)}</h2>
        <div class="ask-w">${reasonLabel(whole?.reason ?? summary.reason)}</div>
        ${kept === undefined ? null : html`<div class="note needs-reasking" role="status">${stateSentence(kept)}</div>`}
        ${
            whole !== undefined && !reasking && changedSince(whole)
                ? html`<div class="note am needs-reasking" role="alert">${DIFFERS_SENTENCE}</div>`
                : null
        }
        ${whole === undefined || reasking ? null : doubts(whole.preview)} ${words(whole)}
        ${reasking ? null : heldNote(gate, summary.id)}
        ${inCheck ? html`<div class="note" role="status">${checkingSentence}</div>` : null}
        ${why === undefined ? null : html`<div class="note am" role="status">${why}</div>`} ${acts}
    </section>`;
}

function questionPane(summary: DecisionSummary, now: number, busy: boolean): Html {
    const row = toQuestionRow(summary, now);
    const shown = attention.detail.value;
    const whole = shown?.kind === 'questions' && shown.item.id === summary.id ? shown.item : null;
    const disabled = busy ? html`disabled` : null;
    return html`<section class="needs-pane ask q" data-id="${summary.id}" aria-label="Question">
        <div class="ask-h">
            <span class="b violet">Question</span><span class="meta">${row.campaign} · ${row.age}</span>
        </div>
        <h2 class="needs-title">${row.question}</h2>
        ${
            (whole?.options ?? []).length > 0
                ? html`<div class="ui-opts">
                      ${(whole?.options ?? []).map(
                          (option) =>
                              html`<button
                                  class="ui-opt"
                                  type="button"
                                  title="${option.detail ?? ''}"
                                  ${disabled}
                                  ${act('needs.option', summary.id, option.label)}
                              >
                                  ${option.label}
                              </button>`,
                      )}
                  </div>`
                : null
        }
        <div class="ask-a">
            ${field(`${summary.id}:text`, 'Your answer')}
            <button class="btn sm p" type="button" ${disabled} ${act('needs.answer', summary.id)}>Answer</button>
            ${field(`${summary.id}:reason`, 'Reason (optional)')}
        </div>
        <div class="ask-a">${openChat(summary.campaign_id, summary.campaign_name)}</div>
    </section>`;
}

/** A chat whose AI waits on a choice: its question and options in the AI's words, answered in the chat itself. */
function chatPane(item: Extract<WaitingItem, { kind: 'chat' }>, now: number): Html {
    const chat = item.chat;
    return html`<section class="needs-pane ask" data-id="${item.id}" aria-label="Asked you in its chat">
        <div class="ask-h">
            <span class="b amber">Waiting for you</span>
            <span class="meta">${chat.agent ?? chat.title} · ${ageOf(chat.at, now)}</span>
        </div>
        <h2 class="needs-title">${chat.title}</h2>
        <div class="ask-w">Its AI asked you something in its chat. Answer there when you are ready.</div>
        <div class="needs-said">
            <div class="lab">Its question</div>
            <div class="ask-s">${chat.question}</div>
            <ul>
                ${chat.options.map((option) => html`<li>${option}</li>`)}
            </ul>
        </div>
        ${
            chat.campaignId === null || chat.agent === null
                ? null
                : html`<div class="ask-a">${openChat(chat.campaignId, chat.agent)}</div>`
        }
    </section>`;
}

function pane(shown: WaitingItem, now: number, busy: boolean): Html {
    switch (shown.kind) {
        case 'approval':
            return approvalPane(shown.row, now, busy);
        case 'question':
            return questionPane(shown.row, now, busy);
        case 'chat':
            return chatPane(shown, now);
    }
}

/** What a filter says when it holds nothing. */
const nothing: Readonly<Record<NeedsFilter, string>> = {
    all: 'Nothing waits for this agent.',
    approvals: 'No approvals waiting.',
    questions: 'No questions for you.',
    chats: 'No chat waits for you.',
    inbox: 'Nothing about a reply waits for you.',
};

function bar(input: NeedsInput): Html {
    const agents = agentsOf(input);
    return html`<div class="needs-bar">
        <div class="needs-filter" role="group" aria-label="Show">
            ${filtersOf(input, agent.value).map(
                ([value, label, count]) =>
                    html`<button
                        class="btn sm"
                        type="button"
                        aria-pressed="${filter.value === value ? 'true' : 'false'}"
                        ${act('needs.filter', value)}
                    >
                        ${label} <span class="needs-n">${countWords(count)}</span>
                    </button>`,
            )}
        </div>
        <select class="inp needs-agent" aria-label="Agent" data-change="needs.agent">
            <option value="" ${agent.value === null ? html`selected` : null}>All agents</option>
            ${agents.map(
                (each) =>
                    html`<option value="${each.id}" ${agent.value === each.id ? html`selected` : null}>
                        ${each.name}
                    </option>`,
            )}
        </select>
    </div>`;
}

function panes(now: number, busy: boolean): Html {
    const input = needsInput();
    const { items, shown } = listed();
    const more = moreUnder(input, filter.value);
    return html`${bar(input)}
        <div class="needs-panes">
            <div class="needs-list" role="group" aria-label="Waiting">
                ${
                    items.length === 0
                        ? html`<div class="empty needs-none">${nothing[filter.value]}</div>`
                        : items.map((item) => listRow(item, shown, now))
                }
                ${
                    more
                        ? html`<button class="btn sm needs-more" type="button" ${act('needs.more')}>Show more</button>`
                        : null
                }
            </div>
            ${shown === null ? null : pane(shown, now, busy)}
        </div>`;
}

export function needsView(): Html {
    const now = clock.value;
    const busy = attention.busy.value || acting.value > 0;
    const failure = attention.failure.value;
    const input = needsInput();
    const none = input.approvals.length === 0 && input.questions.length === 0 && (input.chats ?? []).length === 0;
    return html`<div class="panel-body needs-panel">
        <div class="ph">
            <div>
                <h1>Needs you</h1>
                <p>Approvals and questions waiting on a person, in one list.</p>
            </div>
        </div>
        ${failure ? html`<div class="note am" role="alert">${failure}</div>` : null}
        ${
            left.value
                ? html`<div class="note needs-left" role="status">
                      What you had open is no longer waiting: it was decided or withdrawn elsewhere. This is the next
                      one.
                  </div>`
                : null
        }
        ${
            !attention.loaded.value
                ? html`<div class="empty">Reading what waits…</div>`
                : none
                  ? html`<div class="card needs-empty">
                        <span class="needs-disc">${svg('check', 28)}</span>
                        <div class="bd">You're all caught up</div>
                        <div class="needs-none-says">Nothing is waiting for your decision.</div>
                        <div class="ask-a">
                            <button class="btn sm p" type="button" ${act('side.start')} ${offUnless('chat.create')}>
                                New GTM agent
                            </button>
                            <button class="btn sm" type="button" ${act('needs.campaigns')}>Open your agents</button>
                        </div>
                    </div>`
                  : panes(now, busy)
        }
    </div>`;
}
