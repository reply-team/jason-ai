import { signal } from '@preact/signals';
import { ApiError, Unpaired, type Api } from '../../api/client';
import { session } from '../../api/session';
import type { Approval, ApprovalSummary, Decision, DecisionSummary, Page } from '../../api/types';
import { mergePage } from '../../lists/paging';
import { pendingMark } from '../../shell/attention-mark';

export type Filter = 'pending' | 'decided';
export type Kind = 'approvals' | 'questions';

export const filter = signal<Filter>('pending');
export const approvals = signal<ApprovalSummary[]>([]);
export const approvalsCursor = signal<string | null>(null);
export const questions = signal<DecisionSummary[]>([]);
export const questionsCursor = signal<string | null>(null);
export const detail = signal<{ kind: 'approvals'; item: Approval } | { kind: 'questions'; item: Decision } | null>(
    null,
);
export const failure = signal<string | null>(null);
export const busy = signal(false);
export const loaded = signal(false);
/** True while a refresh the person asked for is under way: long enough to be seen, even when the answer is instant. */
export const refreshing = signal(false);

/**
 * What each list is asked for under the filter: nothing (pending), or everything that no longer needs a person, several
 * statuses at once: decided by one, or ended without one (superseded by a changed input, cancelled with its work item).
 */
function statusBody(kind: Kind): object {
    if (filter.value === 'pending') return {};
    return kind === 'approvals'
        ? { statuses: ['approved', 'rejected', 'superseded', 'cancelled'] }
        : { statuses: ['answered'] };
}

function fail(error: unknown): void {
    if (error instanceof Unpaired) {
        session.clear();
        return;
    }
    failure.value = error instanceof ApiError ? error.message : String(error);
}

export async function load(api: Api): Promise<void> {
    failure.value = null;
    try {
        const [a, q] = await Promise.all([
            api.call<Page<ApprovalSummary>>('approval.list', statusBody('approvals')),
            api.call<Page<DecisionSummary>>('decision.list', statusBody('questions')),
        ]);
        approvals.value = a.items;
        approvalsCursor.value = a.next_cursor;
        questions.value = q.items;
        questionsCursor.value = q.next_cursor;
        if (filter.value === 'pending') pendingMark.value = a.items.length + q.items.length > 0;
        loaded.value = true;
    } catch (error) {
        fail(error);
    }
}

export async function more(api: Api, kind: Kind): Promise<void> {
    const cursor = kind === 'approvals' ? approvalsCursor.value : questionsCursor.value;
    if (!cursor) return;
    try {
        const status = statusBody(kind);
        if (kind === 'approvals') {
            const page = await api.call<Page<ApprovalSummary>>('approval.list', { ...status, cursor });
            approvals.value = mergePage(approvals.value, page.items, (i) => i.id);
            approvalsCursor.value = page.next_cursor;
        } else {
            const page = await api.call<Page<DecisionSummary>>('decision.list', { ...status, cursor });
            questions.value = mergePage(questions.value, page.items, (i) => i.id);
            questionsCursor.value = page.next_cursor;
        }
    } catch (error) {
        fail(error);
    }
}

export async function open(api: Api, kind: Kind, id: string): Promise<void> {
    try {
        detail.value =
            kind === 'approvals'
                ? { kind, item: await api.call<Approval>('approval.get', { approval_id: id }) }
                : { kind, item: await api.call<Decision>('decision.get', { decision_id: id }) };
    } catch (error) {
        detail.value = null;
        fail(error);
    }
}

/** Every pending item in the list's order: approvals, then questions. */
function pendingInOrder(): { kind: Kind; id: string }[] {
    return [
        ...approvals.value.map((a) => ({ kind: 'approvals' as const, id: a.id })),
        ...questions.value.map((q) => ({ kind: 'questions' as const, id: q.id })),
    ];
}

/**
 * The item after this one in the list's order, wrapping round to the first; null when it is the only one. Computed
 * before an action, so that after the reload the person lands where the list goes on.
 */
export function nextPending(after: { kind: Kind; id: string }): { kind: Kind; id: string } | null {
    const all = pendingInOrder();
    const at = all.findIndex((x) => x.kind === after.kind && x.id === after.id);
    const rest = at < 0 ? all : [...all.slice(at + 1), ...all.slice(0, at)];
    return rest[0] ?? null;
}

/** The planned item if the reloaded list still holds it, else the first one left, else null. */
export function stillPending(planned: { kind: Kind; id: string } | null): { kind: Kind; id: string } | null {
    const all = pendingInOrder();
    if (planned && all.some((x) => x.kind === planned.kind && x.id === planned.id)) return planned;
    return all[0] ?? null;
}

/** A refresh the person asked for, shown for at least a moment so the button is seen to do something. */
export async function refresh(api: Api): Promise<void> {
    refreshing.value = true;
    try {
        await Promise.all([load(api), new Promise((resolve) => setTimeout(resolve, 500))]);
    } finally {
        refreshing.value = false;
    }
}

/** The call, then a reload either way, then the failure if there was one: a refusal must outlive the reload that follows it. */
async function act(api: Api, call: () => Promise<unknown>): Promise<boolean> {
    busy.value = true;
    failure.value = null;
    let problem: unknown = null;
    try {
        await call();
    } catch (error) {
        problem = error;
    } finally {
        busy.value = false;
    }
    await load(api);
    if (problem !== null) {
        fail(problem);
        return false;
    }
    return true;
}

export function approve(api: Api, id: string): Promise<boolean> {
    return act(api, () => api.call('approval.approve', { approval_id: id }, 'write'));
}

export function reject(api: Api, id: string, reason?: string): Promise<boolean> {
    return act(api, () => api.call('approval.reject', { approval_id: id, ...(reason ? { reason } : {}) }, 'write'));
}

/** An option chosen on the page travels as the answer and as the option; a free text as the answer alone. */
export function answer(
    api: Api,
    id: string,
    choice: { option?: string; text?: string },
    reason?: string,
): Promise<boolean> {
    const body = choice.option
        ? { decision_id: id, answer: choice.option, option: choice.option }
        : { decision_id: id, answer: choice.text ?? '' };
    return act(api, () => api.call('decision.answer', { ...body, ...(reason ? { reason } : {}) }, 'write'));
}
