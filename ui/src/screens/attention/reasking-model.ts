// Pure: a message a person edited on this page, from the save until the runtime has asked about it again. A save is a
// `workitem.update`, which withdraws the pending approval; the runtime asks again with the new text when it next takes
// the work. Until then the row the person edited stays where it was in every list that held it, says so, and offers
// no act on the approval the edit withdrew; once a pending approval of the same work item is listed, the row becomes it
// in place. The work item's id is the one thing an edit is matched by. Nothing here is markup or a signal.
import type { ApprovalSummary } from '../../api/types';
import type { Words } from './message-edit';

/** A message edited on this page: its work item, the approval the edit withdrew, the words saved, and the approval asked again. */
export interface Edited {
    readonly workItemId: string;
    /** The approval the save withdrew: its row stands for the message until the runtime asks again. */
    readonly withdrawn: string;
    readonly operation: string;
    /** The words the save wrote, which the row shows in place of the ones the withdrawn approval was asked about. */
    readonly words: Words;
    /** The pending approval of the same work item the runtime asked with, once a list or a read has held it. */
    readonly asked: string | null;
}

/** What the row and its panel say while the runtime has not asked again: the state in words, nothing to press. */
export const ASKING_SENTENCE =
    'Your new text is saved. Jason is asking again with it; Approve and Reject come back when it has asked.';

/** The row's own few words for the same state, beside its campaign. */
export const ASKING_MARK = 'Edited · asking again';

/** The work item states in which the work ended: an edit whose item reached one is never asked about again. */
export const ENDED: ReadonlySet<string> = new Set(['succeeded', 'failed', 'cancelled', 'expired']);

/** The pending approval of the edit's work item among these rows, other than the one the edit withdrew. */
export function askedIn(edit: Edited, rows: readonly ApprovalSummary[]): ApprovalSummary | undefined {
    return rows.find(
        (row) => row.work_item_id === edit.workItemId && row.id !== edit.withdrawn && row.status === 'pending',
    );
}

/** The edit still waiting for the runtime to ask again whose withdrawn approval is this one. */
export function askingOf(edits: readonly Edited[], approvalId: string): Edited | undefined {
    return edits.find((edit) => edit.withdrawn === approvalId && edit.asked === null);
}

/** The approval asked again after the one an edit withdrew; null while it has not been, or for an approval no edit withdrew. */
export function askedAfter(edits: readonly Edited[], approvalId: string): string | null {
    return edits.find((edit) => edit.withdrawn === approvalId && edit.asked !== null)?.asked ?? null;
}

/** The edit a list's row stands for: its withdrawn approval, or the approval asked again with its words. */
export function editOf(edits: readonly Edited[], rowId: string): Edited | undefined {
    return edits.find((edit) => edit.withdrawn === rowId || edit.asked === rowId);
}

/** The message's words as the approval was read, with the words an edit saved in their place. */
export function withWords<T extends { subject: string | null; body: string | null }>(detail: T, words: Words): T {
    return { ...detail, subject: words.subject ?? detail.subject, body: words.body ?? detail.body };
}

/**
 * What a row of the list as last drawn becomes in the list read now, for the edit it stands for: the approval asked
 * again when the list holds it; gone when the approval asked again had been drawn here and has left (it was decided);
 * else only a read of its work item can say (`afterRead`).
 */
export function rowNow(
    edit: Edited,
    before: ApprovalSummary,
    fresh: readonly ApprovalSummary[],
): { asked: ApprovalSummary } | 'gone' | 'read' {
    const asked = askedIn(edit, fresh);
    if (asked !== undefined) return { asked };
    return edit.asked !== null && before.id === edit.asked ? 'gone' : 'read';
}

/**
 * Where an edit stands by its work item's status and, when the item waits for an approval again, its pending
 * approvals: ended when the work ended another way, asked when one of them is the approval asked again, else asking.
 */
export function afterRead(
    edit: Edited,
    status: string,
    pending: readonly ApprovalSummary[] = [],
): { asked: ApprovalSummary } | 'ended' | 'asking' {
    if (ENDED.has(status)) return 'ended';
    const asked = status === 'awaiting_approval' ? askedIn(edit, pending) : undefined;
    return asked === undefined ? 'asking' : { asked };
}

/** One row a list keeps where it stood: the id it had in the list as last drawn, and the row it is now. */
export interface Kept {
    readonly from: string;
    readonly row: ApprovalSummary;
}

/**
 * The list as the runtime gave it, with each kept row where it stood in the list as last drawn: after the nearest row
 * before it there that is still listed, else first. Every other row keeps the runtime's order, and a kept row the
 * runtime also lists is drawn once, in its kept place.
 */
export function placeRows(
    previous: readonly string[],
    fresh: readonly ApprovalSummary[],
    kept: readonly Kept[],
): ApprovalSummary[] {
    const keptIds = new Set(kept.map((each) => each.row.id));
    const placed = fresh.filter((row) => !keptIds.has(row.id));
    const now = new Map(kept.map((each) => [each.from, each.row.id]));
    const at = (id: string) => previous.indexOf(id);
    for (const each of [...kept].sort((a, b) => at(a.from) - at(b.from))) {
        let after = -1;
        for (let i = at(each.from) - 1; i >= 0 && after < 0; i--) {
            const before = now.get(previous[i]!) ?? previous[i]!;
            after = placed.findIndex((row) => row.id === before);
        }
        placed.splice(after + 1, 0, each.row);
    }
    return placed;
}
