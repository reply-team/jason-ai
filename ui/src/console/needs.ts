import { effect, signal } from '@preact/signals-react';
import type { ApprovalSummary } from '../api/types';
import { attentionStore } from '../screens/attention/store';
import { doubtWords } from '../screens/send-modes/model';
import { act, argsOf, define } from './actions';
import { html, type Html } from './html';
import { toApprovalDetail, toApprovalRow, toQuestionRow, type ApprovalRow } from './live';
import { clock, here } from './state';
import { approveWhole, fillApprovals, keepWhole, wholeReads } from './waiting';
import { approvalGate, readingSentence, unreadSentence, type ApprovalGate } from './waiting-model';

// Needs you: the approvals and the questions waiting on a person, read and decided the way the browser's Attention
// screen reads and decides them, in a holder of the panel's own: the console's area keeps that screen as it was left
// while this panel is shown, and neither replaces the other's list.

/** The panel's own approvals and questions. */
export const attention = attentionStore();

/**
 * The panel's approvals read whole, as an agent's chat reads its cards': while the panel is drawn, each listed approval
 * is read once, a few at a time, and its row's Approve is live only once it has been read (`approvalGate`).
 */
export const reads = wholeReads('needs', attention);

/** How many drawings of the panel there are: its rows are read whole only while it is drawn. */
let drawn = 0;

/**
 * The rows whose message is being checked against its work item, at the press of their Approve: a check the runtime
 * has not answered holds its own row and no other.
 */
const checking = signal<ReadonlySet<string>>(new Set());

/** What a row says while its message is being checked, before it is approved. */
const checkingSentence = 'This message is being checked against its work item before it is approved.';

const checked = (id: string, on: boolean): void => {
    const rows = new Set(checking.value);
    if (on) rows.add(id);
    else rows.delete(id);
    checking.value = rows;
};

/** Why a row's last Approve approved nothing, by the row's id: until its next press, or until the row leaves. */
const unapproved = signal<Readonly<Record<string, string>>>({});

/** Which of what waits the panel shows: everything, the approvals only or the questions only. Kept to the panel. */
export const filter = signal<'all' | 'approvals' | 'questions'>('all');

// When nothing waits at all the panel draws its card and no filter row, so the filter goes back to everything: what
// arrives next is drawn whatever was chosen before.
effect(() => {
    if (attention.loaded.value && attention.approvals.value.length === 0 && attention.questions.value.length === 0) {
        filter.value = 'all';
    }
});

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

/** Reads whole the rows not read yet while the panel is drawn, and lets go of what was said of rows that left. */
async function fill(): Promise<void> {
    const listed = new Set(attention.approvals.value.map((row) => row.id));
    if (Object.keys(unapproved.value).some((id) => !listed.has(id)))
        unapproved.value = Object.fromEntries(Object.entries(unapproved.value).filter(([id]) => listed.has(id)));
    if (drawn > 0) await fillApprovals(here().api, reads);
}

/** Reads what waits again, then each row not read whole yet and not being read. */
export async function reload(): Promise<void> {
    await attention.load(here().api, false);
    await fill();
}

/** The panel is drawn: its rows are read whole now, and at every reload while it stays drawn. */
export function drawNeeds(): () => void {
    drawn += 1;
    void fill();
    return () => {
        drawn = Math.max(0, drawn - 1);
    };
}

define('needs.type', (args, element) => {
    if (typeof args[0] === 'string' && element instanceof HTMLInputElement) typed.set(args[0], element.value);
});

const filters: readonly [typeof filter.value, string][] = [
    ['all', 'All'],
    ['approvals', 'Approvals'],
    ['questions', 'Questions'],
];

define('needs.filter', ([value]) => {
    const chosen = filters.find(([each]) => each === value);
    if (chosen) filter.value = chosen[0];
});

define('needs.campaigns', () => here().dock.show('campaigns'));

// An approval Details opens is read whole by it, so a row whose own read failed can be approved once Details has read it.
define('needs.open', ([kind, id]) => {
    if ((kind === 'approvals' || kind === 'questions') && typeof id === 'string')
        void attention.open(here().api, kind, id).then(() => {
            const shown = attention.detail.value;
            if (shown?.kind === 'approvals' && shown.item.id === id) keepWhole(reads, shown.item);
        });
});

// Approve acts only on an approval read whole, a message checked against its work item first: the chat's cards' rule.
define('needs.approve', ([id]) => {
    if (typeof id !== 'string' || checking.value.has(id)) return;
    const api = here().api;
    if (Object.hasOwn(unapproved.value, id))
        unapproved.value = Object.fromEntries(Object.entries(unapproved.value).filter(([each]) => each !== id));
    void approveWhole(
        api,
        reads.approvals.value[id],
        () => attention.approve(api, id),
        (on) => checked(id, on),
    )
        .then((why) => {
            if (why !== null) unapproved.value = { ...unapproved.value, [id]: why };
        })
        .then(fill);
});

define('needs.reject', ([id]) => {
    if (typeof id === 'string') void attention.reject(here().api, id, optional(`${id}:reason`)).then(fill);
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

function approvalDetail(id: string): Html {
    const shown = attention.detail.value;
    if (shown?.kind !== 'approvals' || shown.item.id !== id) return html``;
    const detail = toApprovalDetail(shown.item.preview);
    return html`<div class="ask-w">
        ${detail.intent ? html`<div>${detail.intent}</div>` : null}
        ${detail.to ? html`<div class="meta">To ${detail.to}</div>` : null}
        ${detail.from ? html`<div class="meta needs-from">From <span>${detail.from}</span></div>` : null}
        ${detail.channel ? html`<div class="meta needs-channel">Channel <span>${detail.channel}</span></div>` : null}
        ${detail.subject ? html`<div><b>${detail.subject}</b></div>` : null}
        ${detail.body ? html`<div class="said">${detail.body}</div>` : null}
        ${detail.facts.map((fact) => html`<div class="meta">${fact}</div>`)}
    </div>`;
}

/** Who doubted a send and why, from the approval read whole, in the words the chat's cards and the browser use. */
function doubts(preview: Record<string, unknown>): Html | null {
    const said = doubtWords(preview);
    if (said === null) return null;
    return html`<div class="section send-doubts">
        <h3>${said.heading}</h3>
        <ul>
            ${said.doubts.map(
                (doubt) =>
                    html`<li>
                        <p>${doubt.said}</p>
                        ${doubt.note === null ? null : html`<p class="message-text">${doubt.note}</p>`}
                    </li>`,
            )}
        </ul>
    </div>`;
}

/** Why a row's Approve is held: its approval is being read, or could not be read, with what the runtime said. */
function heldNote(gate: ApprovalGate): Html | null {
    if (gate.live) return null;
    if ('reading' in gate) return html`<div class="note am" role="status">${readingSentence('needs')}</div>`;
    const { code, message } = gate.unread;
    return html`<div class="note am" role="status">
        ${unreadSentence('needs')} ${code === null ? null : html`<code>${code}</code>`} ${message}
    </div>`;
}

/** The person a row is about, with "to" before the name when the operation reaches them by a message. */
const person = (row: ApprovalRow): Html | null =>
    row.who ? html`<span class="needs-who">${row.byMessage ? 'to ' : null}<span>${row.who}</span></span>` : null;

function approvals(now: number, busy: boolean): Html {
    const shown = attention.detail.value;
    const wholes = reads.approvals.value;
    const unread = reads.unread.value;
    const said = unapproved.value;
    const pressing = checking.value;
    const rowOf = (summary: ApprovalSummary): ApprovalRow =>
        toApprovalRow(
            summary,
            now,
            shown?.kind === 'approvals' && shown.item.id === summary.id ? shown.item.preview : undefined,
        );
    return html`${attention.approvals.value.map((summary) => {
        const row = rowOf(summary);
        const whole = wholes[row.id];
        // Approve is live only once the approval has been read whole, whatever it does: the rule is the row's.
        const gate = approvalGate(whole, unread[row.id]);
        const why = said[row.id];
        const inCheck = pressing.has(row.id);
        return html`<div class="ask" data-id="${row.id}">
            <div class="ask-h">
                <span class="b amber">${row.what}</span>
                ${person(row)}
                <span class="meta">${row.campaign} · ${row.via} · ${row.age}</span>
            </div>
            ${whole === undefined ? null : doubts(whole.preview)} ${approvalDetail(row.id)} ${heldNote(gate)}
            ${inCheck ? html`<div class="note" role="status">${checkingSentence}</div>` : null}
            ${why === undefined ? null : html`<div class="note am" role="status">${why}</div>`}
            <div class="ask-a">
                <button class="btn sm" type="button" ${act('needs.open', 'approvals', row.id)}>Details</button>
                <button
                    class="btn sm p"
                    type="button"
                    ${busy || inCheck || !gate.live ? html`disabled` : null}
                    ${act('needs.approve', row.id)}
                >
                    Approve
                </button>
                <button class="btn sm" type="button" ${busy ? html`disabled` : null} ${act('needs.reject', row.id)}>
                    Reject
                </button>
                ${field(`${row.id}:reason`, 'Reason (optional)')}
            </div>
        </div>`;
    })}`;
}

function questionDetail(id: string, busy: boolean): Html {
    const shown = attention.detail.value;
    if (shown?.kind !== 'questions' || shown.item.id !== id)
        return html`<div class="ask-a">
            <button class="btn sm" type="button" ${act('needs.open', 'questions', id)}>Answer</button>
        </div>`;
    const disabled = busy ? html`disabled` : null;
    return html`<div class="ask-a">
            ${(shown.item.options ?? []).map(
                (option) =>
                    html`<button
                        class="btn sm"
                        type="button"
                        title="${option.detail ?? ''}"
                        ${disabled}
                        ${act('needs.option', id, option.label)}
                    >
                        ${option.label}
                    </button>`,
            )}
        </div>
        <div class="ask-a">
            ${field(`${id}:text`, 'Your answer')}
            <button class="btn sm p" type="button" ${disabled} ${act('needs.answer', id)}>Answer</button>
            ${field(`${id}:reason`, 'Reason (optional)')}
        </div>`;
}

function questions(now: number, busy: boolean): Html {
    return html`${attention.questions.value.map((summary) => {
        const row = toQuestionRow(summary, now);
        return html`<div class="ask q" data-id="${row.id}">
            <div class="ask-h">
                <span class="b violet">Question</span><span class="meta">${row.campaign} · ${row.age}</span>
            </div>
            <div class="ask-s">${row.question}</div>
            ${questionDetail(row.id, busy)}
        </div>`;
    })}`;
}

function filterRow(): Html {
    return html`<div class="needs-filter" role="group" aria-label="Show">
        ${filters.map(
            ([value, label]) =>
                html`<button
                    class="btn sm"
                    type="button"
                    aria-pressed="${filter.value === value ? 'true' : 'false'}"
                    ${act('needs.filter', value)}
                >
                    ${label}
                </button>`,
        )}
    </div>`;
}

/** What waits, under the filter: a group with its rows, or under its own filter a line that says it holds nothing. */
function waiting(now: number, busy: boolean): Html {
    const shown = filter.value;
    const approvalCount = attention.approvals.value.length;
    const questionCount = attention.questions.value.length;
    return html`${
        shown === 'questions'
            ? null
            : approvalCount
              ? html`<div class="lab">Approvals</div>
                    ${approvals(now, busy)}`
              : shown === 'approvals'
                ? html`<div class="empty needs-none">No approvals waiting.</div>`
                : null
    }
    ${
        shown === 'approvals'
            ? null
            : questionCount
              ? html`<div class="lab">Questions</div>
                    ${questions(now, busy)}`
              : shown === 'questions'
                ? html`<div class="empty needs-none">No questions for you.</div>`
                : null
    }`;
}

export function needsView(): Html {
    const now = clock.value;
    const busy = attention.busy.value;
    const failure = attention.failure.value;
    const none = attention.approvals.value.length === 0 && attention.questions.value.length === 0;
    return html`<div class="panel-body needs-panel">
        <div class="ph">
            <div>
                <h1>Needs you</h1>
                <p>Approvals and questions waiting on a person.</p>
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
                  : html`${filterRow()} ${waiting(now, busy)}`
        }
    </div>`;
}
