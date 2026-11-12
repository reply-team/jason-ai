import { signal } from '@preact/signals-react';
import { ApiError, Unpaired, type Api } from '../../api/client';
import { session } from '../../api/session';
import type { Approval, ApprovalSummary, Decision, DecisionSummary, Page } from '../../api/types';
import { mergePage } from '../../lists/paging';
import { pendingMark } from '../../shell/attention-mark';

export type Kind = 'approvals' | 'questions';

/**
 * What each list is asked for under the filter: nothing (pending), or everything that no longer needs a person, several
 * statuses at once: decided by one, or ended without one (superseded by a changed input, cancelled with its work item).
 */
function statusBody(kind: Kind, decided: boolean): object {
    if (!decided) return {};
    return kind === 'approvals'
        ? { statuses: ['approved', 'rejected', 'superseded', 'cancelled'] }
        : { statuses: ['answered'] };
}

/**
 * One holder of the approvals and questions a person reads: its lists, the item open, and the acts on them. The
 * Attention screen has the default one below; a view that lists them beside that screen, such as the desktop's own
 * panel, holds one of its own, so neither replaces what the other shows. A pending list read by either sets the mark.
 */
export function attentionStore() {
    const approvals = signal<ApprovalSummary[]>([]);
    const approvalsCursor = signal<string | null>(null);
    const questions = signal<DecisionSummary[]>([]);
    const questionsCursor = signal<string | null>(null);
    const detail = signal<{ kind: 'approvals'; item: Approval } | { kind: 'questions'; item: Decision } | null>(null);
    const failure = signal<string | null>(null);
    const busy = signal(false);
    const loaded = signal(false);
    /** True while a refresh the person asked for is under way: long enough to be seen, even when the answer is instant. */
    const refreshing = signal(false);

    function fail(error: unknown): void {
        if (error instanceof Unpaired) {
            session.clear();
            return;
        }
        failure.value = error instanceof ApiError ? error.message : String(error);
    }

    /** Which list is shown, so the reload after an action reads the same one. */
    let shownDecided = false;

    async function load(api: Api, decided: boolean): Promise<void> {
        shownDecided = decided;
        failure.value = null;
        try {
            const [a, q] = await Promise.all([
                api.call<Page<ApprovalSummary>>('approval.list', statusBody('approvals', decided)),
                api.call<Page<DecisionSummary>>('decision.list', statusBody('questions', decided)),
            ]);
            approvals.value = a.items;
            approvalsCursor.value = a.next_cursor;
            questions.value = q.items;
            questionsCursor.value = q.next_cursor;
            if (!decided) pendingMark.value = a.items.length + q.items.length > 0;
            loaded.value = true;
        } catch (error) {
            fail(error);
        }
    }

    async function more(api: Api, kind: Kind, decided: boolean): Promise<void> {
        const cursor = kind === 'approvals' ? approvalsCursor.value : questionsCursor.value;
        if (!cursor) return;
        try {
            const status = statusBody(kind, decided);
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

    async function open(api: Api, kind: Kind, id: string): Promise<void> {
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

    /** Every pending item in the list's order, approvals then questions, or of the one kind a filter shows. */
    function pendingInOrder(only?: Kind): { kind: Kind; id: string }[] {
        return [
            ...(only === 'questions' ? [] : approvals.value.map((a) => ({ kind: 'approvals' as const, id: a.id }))),
            ...(only === 'approvals' ? [] : questions.value.map((q) => ({ kind: 'questions' as const, id: q.id }))),
        ];
    }

    /**
     * The item after this one in the list's order, wrapping round to the first; null when it is the only one. Computed
     * before an action, so that after the reload the person lands where the list goes on; under a filter, among the
     * items it shows.
     */
    function nextPending(after: { kind: Kind; id: string }, only?: Kind): { kind: Kind; id: string } | null {
        const all = pendingInOrder(only);
        const at = all.findIndex((x) => x.kind === after.kind && x.id === after.id);
        const rest = at < 0 ? all : [...all.slice(at + 1), ...all.slice(0, at)];
        return rest[0] ?? null;
    }

    /** The planned item if the reloaded list still holds it, else the first one left, else null. */
    function stillPending(planned: { kind: Kind; id: string } | null, only?: Kind): { kind: Kind; id: string } | null {
        const all = pendingInOrder(only);
        if (planned && all.some((x) => x.kind === planned.kind && x.id === planned.id)) return planned;
        return all[0] ?? null;
    }

    /** A refresh the person asked for, shown for at least a moment so the button is seen to do something. */
    async function refresh(api: Api, decided: boolean): Promise<void> {
        refreshing.value = true;
        try {
            await Promise.all([load(api, decided), new Promise((resolve) => setTimeout(resolve, 500))]);
        } finally {
            refreshing.value = false;
        }
    }

    /** The call, then a reload either way, then the failure if there was one: a refusal must outlive the reload. */
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
        await load(api, shownDecided);
        if (problem !== null) {
            fail(problem);
            return false;
        }
        return true;
    }

    function approve(api: Api, id: string): Promise<boolean> {
        return act(api, () => api.call('approval.approve', { approval_id: id }, 'write'));
    }

    function reject(api: Api, id: string, reason?: string): Promise<boolean> {
        return act(api, () => api.call('approval.reject', { approval_id: id, ...(reason ? { reason } : {}) }, 'write'));
    }

    /** An option chosen on the page travels as the answer and as the option; a free text as the answer alone. */
    function answer(
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

    return {
        approvals,
        approvalsCursor,
        questions,
        questionsCursor,
        detail,
        failure,
        busy,
        loaded,
        refreshing,
        load,
        more,
        open,
        nextPending,
        stillPending,
        refresh,
        approve,
        reject,
        answer,
    };
}

export type AttentionStore = ReturnType<typeof attentionStore>;

/** The Attention screen's own holder, under the names that screen has always read. */
export const {
    approvals,
    approvalsCursor,
    questions,
    questionsCursor,
    detail,
    failure,
    busy,
    loaded,
    refreshing,
    load,
    more,
    open,
    nextPending,
    stillPending,
    refresh,
    approve,
    reject,
    answer,
} = attentionStore();
