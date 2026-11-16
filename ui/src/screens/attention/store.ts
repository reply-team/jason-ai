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
 * A holder of one campaign's asks for that campaign's pending ones, as the campaign's own page does.
 */
function statusBody(kind: Kind, decided: boolean, campaignId?: string): object {
    if (campaignId !== undefined && !decided) return { status: 'pending', campaign_id: campaignId, limit: 50 };
    const scope = campaignId === undefined ? {} : { campaign_id: campaignId };
    if (!decided) return scope;
    return kind === 'approvals'
        ? { ...scope, statuses: ['approved', 'rejected', 'superseded', 'cancelled'] }
        : { ...scope, statuses: ['answered'] };
}

/**
 * One holder of the approvals and questions a person reads: its lists, the item open, and the acts on them. The
 * Attention screen has the default one below; a view that lists them beside that screen, such as the desktop's own
 * panel, holds one of its own, so neither replaces what the other shows. A pending list read by either sets the mark.
 * A holder given a campaign reads that campaign's alone, as an agent's chat does, and leaves the mark to the others: one
 * campaign's lists say nothing of what waits elsewhere. A holder that keeps its pages reads again, at every load, as many
 * pages of each list as the person had read with `more`, and replaces each list only once they are all in: its list never
 * shrinks to the first page under a person's hand.
 */
export function attentionStore(scope?: { campaignId?: string; keepPages?: boolean }) {
    const campaignId = scope?.campaignId;
    const keepPages = scope?.keepPages === true;
    /** How many pages of each list the person has read: one, and one more at each `more` that read one. */
    const pages: Record<Kind, number> = { approvals: 1, questions: 1 };
    const approvals = signal<ApprovalSummary[]>([]);
    const approvalsCursor = signal<string | null>(null);
    const questions = signal<DecisionSummary[]>([]);
    const questionsCursor = signal<string | null>(null);
    const detail = signal<{ kind: 'approvals'; item: Approval } | { kind: 'questions'; item: Decision } | null>(null);
    const failure = signal<string | null>(null);
    /** The runtime's code for the failure shown, when the runtime refused; null for a failure it gave no code for. */
    const failureCode = signal<string | null>(null);
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
        failureCode.value = error instanceof ApiError ? error.code : null;
    }

    /** Which list is shown, so the reload after an action reads the same one. */
    let shownDecided = false;

    /** A list's first page, then, for a holder that keeps its pages, the pages after it the person had read. */
    async function readList<T extends { id: string }>(
        api: Api,
        kind: Kind,
        decided: boolean,
        operation: 'approval.list' | 'decision.list',
    ): Promise<Page<T>> {
        const status = statusBody(kind, decided, campaignId);
        const first = await api.call<Page<T>>(operation, status);
        let items = first.items;
        let cursor = first.next_cursor;
        let read = 1;
        while (keepPages && cursor && read < pages[kind]) {
            const page = await api.call<Page<T>>(operation, { ...status, cursor });
            items = mergePage(items, page.items, (i) => i.id);
            cursor = page.next_cursor;
            read += 1;
        }
        if (cursor === null) pages[kind] = Math.max(1, Math.min(pages[kind], read));
        return { items, next_cursor: cursor };
    }

    async function load(api: Api, decided: boolean): Promise<void> {
        shownDecided = decided;
        failure.value = null;
        failureCode.value = null;
        try {
            const [a, q] = await Promise.all([
                readList<ApprovalSummary>(api, 'approvals', decided, 'approval.list'),
                readList<DecisionSummary>(api, 'questions', decided, 'decision.list'),
            ]);
            approvals.value = a.items;
            approvalsCursor.value = a.next_cursor;
            questions.value = q.items;
            questionsCursor.value = q.next_cursor;
            if (!decided && campaignId === undefined) pendingMark.value = a.items.length + q.items.length > 0;
            loaded.value = true;
        } catch (error) {
            fail(error);
        }
    }

    async function more(api: Api, kind: Kind, decided: boolean): Promise<void> {
        const cursor = kind === 'approvals' ? approvalsCursor.value : questionsCursor.value;
        if (!cursor) return;
        try {
            const status = statusBody(kind, decided, campaignId);
            if (kind === 'approvals') {
                const page = await api.call<Page<ApprovalSummary>>('approval.list', { ...status, cursor });
                approvals.value = mergePage(approvals.value, page.items, (i) => i.id);
                approvalsCursor.value = page.next_cursor;
                pages.approvals += 1;
            } else {
                const page = await api.call<Page<DecisionSummary>>('decision.list', { ...status, cursor });
                questions.value = mergePage(questions.value, page.items, (i) => i.id);
                questionsCursor.value = page.next_cursor;
                pages.questions += 1;
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
        failureCode.value = null;
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

    /** Forgets the pages read: the next load reads the first page alone. */
    function forgetPages(): void {
        pages.approvals = 1;
        pages.questions = 1;
    }

    return {
        approvals,
        approvalsCursor,
        questions,
        questionsCursor,
        detail,
        failure,
        failureCode,
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
        forgetPages,
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
