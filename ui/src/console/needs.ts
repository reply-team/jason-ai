import { effect, signal } from '@preact/signals-react';
import { Unpaired } from '../api/client';
import type { Approval, ApprovalSummary, DecisionSummary, WorkItem } from '../api/types';
import { attentionStore } from '../screens/attention/store';
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
import { doubtWords } from '../screens/send-modes/model';
import { act, argsOf, define } from './actions';
import { html, type Html } from './html';
import { ageOf, toApprovalDetail, toApprovalRow, toQuestionRow } from './live';
import { chatWaits } from './needs-chats';
import {
    agentsOf,
    countWords,
    filtersOf,
    pick,
    waitingOf,
    type Chosen,
    type NeedsFilter,
    type NeedsInput,
    type WaitingItem,
} from './needs-model';
import { clock, here } from './state';
import { approveWhole, fillApprovals, keepWhole, wholeReads } from './waiting';
import {
    approvalGate,
    readingSentence,
    stillAsking,
    unreadSentence,
    type ApprovalGate,
    type Reasked,
} from './waiting-model';

// Needs you: what waits on a person, in two panes. On the left every approval and question the runtime lists (and,
// once read, every chat whose AI waits on a choice), under a filter and an agent; on the right the one chosen, whole,
// with the person's own acts on it. Approvals and questions are read and decided the way the browser's Attention screen
// reads and decides them, in a holder of the panel's own: the console's area keeps that screen as it was left while
// this panel is shown, and neither replaces the other's list. Every count is a length of what the runtime listed.

/** The panel's own approvals and questions. */
export const attention = attentionStore();

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

/** The messages edited here, until the runtime has asked about each again. */
export const reasked = signal<readonly Reasked[]>([]);

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
// everything: what arrives next is drawn whatever was chosen before. An agent that no longer has anything waiting is
// let go too, since the picker no longer names it.
effect(() => {
    if (!attention.loaded.value) return;
    const input = needsInput();
    if (input.approvals.length === 0 && input.questions.length === 0 && (input.chats ?? []).length === 0) {
        filter.value = 'all';
        agent.value = null;
    } else if (agent.value !== null && !agentsOf(input).some((each) => each.id === agent.value)) {
        agent.value = null;
    }
});

// An approval or a question opened from elsewhere (an agent's chat's Details) is the one the pane shows, in view.
effect(() => {
    const shown = attention.detail.value;
    if (shown === null) return;
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
    return { items, shown: pick(items, chosen.value) };
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
        void attention.open(here().api, 'questions', shown.id);
}

/** Reads whole the approvals not read yet while the panel is drawn, and lets go of what was said of items that left. */
async function fill(): Promise<void> {
    const listedIds = new Set(attention.approvals.value.map((row) => row.id));
    if (Object.keys(unapproved.value).some((id) => !listedIds.has(id)))
        unapproved.value = Object.fromEntries(Object.entries(unapproved.value).filter(([id]) => listedIds.has(id)));
    if (rejecting.value !== null && !listedIds.has(rejecting.value)) rejecting.value = null;
    if (editing.value !== null && !listedIds.has(editing.value.id)) editing.value = null;
    reasked.value = stillAsking(reasked.value, attention.approvals.value);
    readShown();
    if (drawn > 0) await fillApprovals(here().api, reads);
}

/** Reads what waits again, then each approval not read whole yet and not being read. */
export async function reload(): Promise<void> {
    await attention.load(here().api, false);
    await fill();
}

/** Forgets what the panel holds of its own: the filter, the agent, the choice, and every act under way; for a test. */
export function forgetNeeds(): void {
    filter.value = 'all';
    agent.value = null;
    chosen.value = null;
    rejecting.value = null;
    editing.value = null;
    reasked.value = [];
    checking.value = new Set();
    unapproved.value = {};
    typed.clear();
}

/** The panel is drawn: its approvals are read whole now, and at every reload while it stays drawn. */
export function drawNeeds(): () => void {
    drawn += 1;
    void fill();
    return () => {
        drawn = Math.max(0, drawn - 1);
    };
}

define('needs.type', (args, element) => {
    if (typeof args[0] === 'string' && (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement))
        typed.set(args[0], element.value);
});

define('needs.filter', ([value]) => {
    const found = filtersOf(needsInput(), agent.value).find(([each]) => each === value);
    if (!found) return;
    filter.value = found[0];
    readShown();
});

define('needs.agent', (_, element) => {
    if (!(element instanceof HTMLSelectElement)) return;
    const value = element.value;
    agent.value = agentsOf(needsInput()).some((each) => each.id === value) ? value : null;
    readShown();
});

define('needs.pick', ([kind, id]) => {
    if ((kind === 'approval' || kind === 'question' || kind === 'chat') && typeof id === 'string') {
        chosen.value = { kind, id };
        rejecting.value = null;
        readShown();
    }
});

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
        void attention.open(here().api, 'approvals', id).then(() => {
            const shown = attention.detail.value;
            if (shown?.kind === 'approvals' && shown.item.id === id) keepWhole(reads, shown.item);
        });
});

// Approve acts only on an approval read whole, a message checked against its work item first: the chat's cards' rule.
define('needs.approve', ([id]) => {
    if (typeof id !== 'string' || checking.value.has(id)) return;
    const api = here().api;
    said(id, null);
    void approveWhole(
        api,
        reads.approvals.value[id],
        () => attention.approve(api, id),
        (on) => checked(id, on),
    )
        .then((why) => {
            if (why !== null) said(id, why);
        })
        .then(fill);
});

define('needs.reject', ([id]) => {
    if (typeof id !== 'string') return;
    typed.delete(`${id}:reason`);
    rejecting.value = id;
});

define('needs.unreject', () => {
    rejecting.value = null;
});

define('needs.rejected', ([id]) => {
    if (typeof id !== 'string') return;
    rejecting.value = null;
    void attention.reject(here().api, id, optional(`${id}:reason`)).then(fill);
});

// Edit reads the message's work item, and the boxes open with the words it holds now: a person never replaces words
// they have not seen. Save writes them through the work item's update and approves nothing.
define('needs.edit', ([id]) => {
    const whole = typeof id === 'string' ? reads.approvals.value[id] : undefined;
    if (whole === undefined || checking.value.has(whole.id)) return;
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
        .finally(() => checked(whole.id, false));
});

define('needs.unedit', () => {
    editing.value = null;
});

define('needs.save', ([id]) => {
    const now = editing.value;
    const whole = typeof id === 'string' ? reads.approvals.value[id] : undefined;
    if (now === null || whole === undefined || now.id !== whole.id || now.saving) return;
    const api = here().api;
    editing.value = { ...now, saving: true, problem: null };
    saveEdit(
        api,
        whole.operation,
        whole.work_item_id,
        typed.get(`${whole.id}:subject`) ?? '',
        typed.get(`${whole.id}:body`) ?? '',
        now.opened,
    )
        .then(() => {
            editing.value = null;
            reasked.value = [
                ...reasked.value.filter((each) => each.workItemId !== whole.work_item_id),
                { workItemId: whole.work_item_id, approvalId: whole.id },
            ];
            return reload();
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

define('needs.option', ([id, option]) => {
    if (typeof id === 'string' && typeof option === 'string')
        void attention.answer(here().api, id, { option }, optional(`${id}:reason`)).then(fill);
});

define('needs.answer', ([id]) => {
    if (typeof id !== 'string') return;
    const text = optional(`${id}:text`);
    if (text) void attention.answer(here().api, id, { text }, optional(`${id}:reason`)).then(fill);
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
                <span class="needs-row-m">Approval · ${row.campaign}</span>
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
    const detail = toApprovalDetail(whole.preview);
    return html`<div class="needs-words">
            ${detail.to ? html`<div class="meta">To ${detail.to}</div>` : null}
            ${detail.from ? html`<div class="meta needs-from">From <span>${detail.from}</span></div>` : null}
            ${detail.channel ? html`<div class="meta needs-channel">Channel <span>${detail.channel}</span></div>` : null}
            ${detail.subject ? html`<div class="ask-s">${detail.subject}</div>` : null}
            ${detail.body ? html`<div class="said needs-body">${detail.body}</div>` : null}
            ${
                detail.subject === null && detail.body === null && detail.intent
                    ? html`<div class="needs-intent">${detail.intent}</div>`
                    : null
            }
        </div>
        ${detail.facts.length > 0 ? html`<div class="needs-facts">${detail.facts.map((fact) => html`<div class="meta">${fact}</div>`)}</div>` : null}`;
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
    const acts = edit
        ? editBoxes(edit, summary.operation, busy)
        : asked
          ? html`<div class="note am needs-reject" role="alertdialog" aria-label="Reject this?">
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
            <span class="meta">${row.campaign} · ${row.via} · ${row.age}</span>
        </div>
        <h2 class="needs-title">${row.what} ${person(summary, now)}</h2>
        <div class="ask-w">${reasonLabel(whole?.reason ?? summary.reason)}</div>
        ${whole === undefined ? null : doubts(whole.preview)} ${words(whole)} ${heldNote(gate, summary.id)}
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
    inbox: 'No answer to a reply waits for you.',
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
    const more =
        (filter.value !== 'questions' && filter.value !== 'chats' && input.more.approvals) ||
        (filter.value !== 'approvals' && filter.value !== 'inbox' && filter.value !== 'chats' && input.more.questions);
    return html`${bar(input)}
        ${
            reasked.value.length > 0
                ? html`<div class="note needs-reasked" role="status">
                      Jason asks again with the new text when it next takes this work.
                  </div>`
                : null
        }
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
    const busy = attention.busy.value;
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
            !attention.loaded.value
                ? html`<div class="empty">Reading what waits…</div>`
                : none
                  ? html`<div class="card needs-empty">
                        <div class="bd">You're all caught up</div>
                        <div class="ask-a">
                            <button class="btn sm" type="button" ${act('needs.campaigns')}>Open your agents</button>
                        </div>
                    </div>`
                  : panes(now, busy)
        }
    </div>`;
}
