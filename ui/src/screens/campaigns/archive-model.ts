import type { ApprovalSummary, DecisionSummary, WorkItemSummary } from '../../api/types';

/**
 * The operations whose open work an archive leaves to finish: those whose contract says `dispatch.in_archived_campaign:
 * runs`. A search's stop is the one. `archive-model.test.ts` holds this list to the contracts under
 * `docs/contracts/operations/`, so it is the documents' rule read once more, never a rule of its own.
 */
export const runsPastArchive: readonly string[] = ['audience.search_cancel'];

/**
 * What an archive cancels, counted from the runtime's answers as the confirmation opens: the open work it cancels and
 * how many of those items are processing now (stopped once the archive is made), the approvals on that work still asked
 * and already given, the pending questions, and the open searches' stops it leaves to finish.
 */
export type Cancels = {
    work: number;
    processing: number;
    asked: number;
    given: number;
    questions: number;
    finishing: number;
};

export function countCancels(
    work: WorkItemSummary[],
    approvals: ApprovalSummary[],
    questions: DecisionSummary[],
): Cancels {
    const left = work.filter((item) => item.operation !== null && runsPastArchive.includes(item.operation));
    const cancelled = work.filter((item) => !left.includes(item));
    // An approval is cancelled with its work; one given to work that has run, or to work the archive leaves, stays.
    const ids = new Set(cancelled.map((item) => item.id));
    const onCancelled = approvals.filter((approval) => ids.has(approval.work_item_id));
    return {
        work: cancelled.length,
        processing: cancelled.filter((item) => item.status === 'processing').length,
        asked: onCancelled.filter((approval) => approval.status === 'pending').length,
        given: onCancelled.filter((approval) => approval.status === 'approved').length,
        questions: questions.length,
        finishing: left.length,
    };
}

/** Whether nothing at all is open: no work, cancelled or left to finish, no approval and no question. */
export const nothingOpen = (cancels: Cancels): boolean =>
    cancels.work + cancels.asked + cancels.given + cancels.questions + cancels.finishing === 0;

/**
 * Whether Jason itself can pause the provider's campaign before the archive: only for an agent that is live or paused,
 * since a draft's work waits, and only where its route for the provider's pause is usable. A route that was not read
 * (null) is no way.
 */
export function jasonMayPauseFirst(status: string, usable: boolean | null): boolean {
    return (status === 'active' || status === 'paused') && usable === true;
}

const number = new Intl.NumberFormat('en-US');

/** "3 open work items": a count and its noun, one or many. */
export function many(count: number, one: string, more: string): string {
    return `${number.format(count)} ${count === 1 ? one : more}`;
}

/**
 * The page's word after the archive: the agent named, and what was cancelled as the confirmation counted it, only what
 * there was; where nothing was open, or the counts were not read, the archive alone.
 */
export function saidArchived(name: string, cancels: Cancels | null): string {
    const approvals = cancels ? cancels.asked + cancels.given : 0;
    const parts = cancels
        ? [
              cancels.work > 0 && many(cancels.work, 'open work item', 'open work items'),
              approvals > 0 && many(approvals, 'approval', 'approvals'),
              cancels.questions > 0 && many(cancels.questions, 'question', 'questions'),
          ].filter((each): each is string => Boolean(each))
        : [];
    if (parts.length === 0) return `${name} is archived.`;
    const list = parts.length === 1 ? parts[0] : `${parts.slice(0, -1).join(', ')} and ${parts[parts.length - 1]}`;
    const total = (cancels?.work ?? 0) + approvals + (cancels?.questions ?? 0);
    return `${name} is archived: ${list} ${total === 1 ? 'is' : 'are'} cancelled.`;
}
