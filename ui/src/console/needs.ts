import { signal } from '@preact/signals-react';
import { attentionStore } from '../screens/attention/store';
import { act, argsOf, define } from './actions';
import { html, type Html } from './html';
import { toApprovalDetail, toApprovalRow, toQuestionRow } from './live';
import { clock, here } from './state';

// Needs you: the approvals and the questions waiting on a person, read and decided the way the browser's Attention
// screen reads and decides them, in a holder of the panel's own: the page panel can show that screen beside this one,
// and neither replaces the other's list.

/** The panel's own approvals and questions. */
export const attention = attentionStore();

/** Which of what waits the panel shows: everything, the approvals only or the questions only. Kept to the panel. */
export const filter = signal<'all' | 'approvals' | 'questions'>('all');

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

export function reload(): Promise<void> {
    return attention.load(here().api, false);
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

define('needs.open', ([kind, id]) => {
    if ((kind === 'approvals' || kind === 'questions') && typeof id === 'string')
        void attention.open(here().api, kind, id);
});

define('needs.approve', ([id]) => {
    if (typeof id === 'string') void attention.approve(here().api, id);
});

define('needs.reject', ([id]) => {
    if (typeof id === 'string') void attention.reject(here().api, id, optional(`${id}:reason`));
});

define('needs.option', ([id, option]) => {
    if (typeof id === 'string' && typeof option === 'string')
        void attention.answer(here().api, id, { option }, optional(`${id}:reason`));
});

define('needs.answer', ([id]) => {
    if (typeof id !== 'string') return;
    const text = optional(`${id}:text`);
    if (text) void attention.answer(here().api, id, { text }, optional(`${id}:reason`));
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

function approvals(now: number, busy: boolean): Html {
    const shown = attention.detail.value;
    const rows = attention.approvals.value.map((row) =>
        toApprovalRow(
            row,
            now,
            shown?.kind === 'approvals' && shown.item.id === row.id ? shown.item.preview : undefined,
        ),
    );
    return html`${rows.map(
        (row) =>
            html`<div class="ask" data-id="${row.id}">
                <div class="ask-h">
                    <span class="b amber">${row.what}</span>
                    ${row.who ? html`<span class="needs-to">to <span>${row.who}</span></span>` : null}
                    <span class="meta">${row.campaign} · ${row.via} · ${row.age}</span>
                </div>
                ${approvalDetail(row.id)}
                <div class="ask-a">
                    <button class="btn sm" type="button" ${act('needs.open', 'approvals', row.id)}>Details</button>
                    <button
                        class="btn sm p"
                        type="button"
                        ${busy ? html`disabled` : null}
                        ${act('needs.approve', row.id)}
                    >
                        Approve
                    </button>
                    <button class="btn sm" type="button" ${busy ? html`disabled` : null} ${act('needs.reject', row.id)}>
                        Reject
                    </button>
                    ${field(`${row.id}:reason`, 'Reason (optional)')}
                </div>
            </div>`,
    )}`;
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
                  ? html`<div class="card needs-empty"><div class="bd">You're all caught up</div></div>`
                  : html`${filterRow()} ${waiting(now, busy)}`
        }
    </div>`;
}
