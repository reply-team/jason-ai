// Pure: a message a person edited on this page, from the save until the runtime has asked about it again, and the
// withdrawn approval for as long as the page lives. A save is a `workitem.update`, which withdraws the pending approval;
// the runtime asks again with the new text when it next takes the work. Until then the row the person edited stays
// where it was in every list that held it, says so, and offers no act on the approval the edit withdrew; once a pending
// approval of the same work item is listed, the row becomes it in place. The work item's id is the one thing an edit is
// matched by. The approval an edit withdrew is never acted on again on this page, whatever became of the edit. Nothing
// here is markup or a signal.
import type { ApprovalSummary } from '../../api/types';
import type { Words } from './message-edit';

/** What the last read of an edited message's work item said, while no approval asked again is known. */
export type Read = 'queued' | 'unread';

/** A message edited on this page: its work item, the approval the edit withdrew, the words saved, and what became of it. */
export interface Edited {
    readonly workItemId: string;
    /** The approval the save withdrew: its row stands for the message until the runtime asks again. */
    readonly withdrawn: string;
    readonly operation: string;
    /** The words the save wrote, which the row shows in place of the ones the withdrawn approval was asked about. */
    readonly words: Words;
    /** The approval of the same work item the runtime asked with, once a list or a read has held it. */
    readonly asked: string | null;
    /**
     * How the work ended before the runtime asked again (its status, or `gone` when the item is not there): the row goes
     * as a row decided elsewhere does. Null while it has not.
     */
    readonly ended: string | null;
    /** What the last read of the work item said; `queued` until a read says otherwise. */
    readonly read: Read;
}

/** The row's own few words while the runtime has not asked again, beside its campaign. */
export const ASKING_MARK = 'Edited · not asked again yet';

/** What a row of an approval asked again says when its words are not the ones the person saved. */
export const DIFFERS_SENTENCE =
    'This is not the text you saved: it was changed after your edit. Read it before you approve it.';

/** What the row says while the work is queued: the text saved, and Jason asking again when it next takes the work. */
export const QUEUED_SENTENCE =
    'Your new text is saved. Jason asks again with it when it next takes this work; Approve and Reject come back then.';

const endedWords: Readonly<Record<string, string>> = {
    succeeded: 'it was done',
    failed: 'it failed',
    cancelled: 'it was cancelled',
    expired: 'it expired',
    gone: 'it is no longer there',
};

/**
 * What the row and its panel say of a message edited here, in words that promise nothing the page does not know: the
 * text is saved and Jason asks again with it when it next takes the work; that a read could not say where it stands;
 * that the work ended another way; or that Jason asked again under another approval, which is where Approve is.
 */
export function stateSentence(edit: Edited): string {
    if (edit.ended !== null)
        return `Your new text was saved, but this work ended before Jason asked again: ${endedWords[edit.ended] ?? edit.ended}. Nothing here can be approved.`;
    if (edit.asked !== null)
        return 'Jason asked again with your text under a new approval; this one was withdrawn by your edit and cannot be approved.';
    if (edit.read === 'unread')
        return 'Your new text is saved. Whether Jason has asked again could not be read just now; Approve and Reject come back once it has.';
    return QUEUED_SENTENCE;
}

/** The work item states in which the work ended: an edit whose item reached one is never asked about again. */
export const ENDED: ReadonlySet<string> = new Set(['succeeded', 'failed', 'cancelled', 'expired']);

/** Every status an approval can have, for a read of all of one work item's approvals. */
export const APPROVAL_STATUSES = ['pending', 'approved', 'rejected', 'superseded', 'cancelled'] as const;

/** The pending approval of the edit's work item among these rows, other than the one the edit withdrew. */
export function askedIn(edit: Edited, rows: readonly ApprovalSummary[]): ApprovalSummary | undefined {
    return rows.find(
        (row) => row.work_item_id === edit.workItemId && row.id !== edit.withdrawn && row.status === 'pending',
    );
}

/** The edit that withdrew this approval, whatever became of it since: its approval is never acted on again here. */
export function withdrawnOf(edits: readonly Edited[], approvalId: string): Edited | undefined {
    return edits.find((edit) => edit.withdrawn === approvalId);
}

/** The edit still waiting for the runtime to ask again whose withdrawn approval is this one: the mark and the poll's. */
export function askingOf(edits: readonly Edited[], approvalId: string): Edited | undefined {
    const edit = withdrawnOf(edits, approvalId);
    return edit !== undefined && edit.asked === null && edit.ended === null ? edit : undefined;
}

/** The approval asked again after the one an edit withdrew; null while it has not been, or for an approval no edit withdrew. */
export function askedAfter(edits: readonly Edited[], approvalId: string): string | null {
    return withdrawnOf(edits, approvalId)?.asked ?? null;
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
 * Whether the approval asked again after an edit carries words other than the ones the person saved: something wrote
 * the item between the save and the new asking. Only the text is compared, as the edit wrote only the text.
 */
export function differs(edit: Edited, askedWords: Words): boolean {
    return (
        (edit.words.subject !== null && askedWords.subject !== edit.words.subject) ||
        askedWords.body !== edit.words.body
    );
}

/**
 * What a row of the list as last drawn becomes in the list read now, for the edit it stands for: the approval asked
 * again when the list holds it; gone once the edit was asked or ended and the list does not hold the approval asked
 * (it was decided, here or elsewhere); else only a read of its work item can say (`afterRead`).
 */
export function rowNow(edit: Edited, fresh: readonly ApprovalSummary[]): { asked: ApprovalSummary } | 'gone' | 'read' {
    const asked = askedIn(edit, fresh);
    if (asked !== undefined) return { asked };
    return edit.asked !== null || edit.ended !== null ? 'gone' : 'read';
}

/**
 * Where an edit stands by a read of its work item (null when the runtime has no such item) and of every approval of
 * that item: asked when one other than the withdrawn one is pending; decided when one other than it was already
 * decided, here or elsewhere, so the row goes; ended when the work ended another way; else queued.
 */
export function afterRead(
    edit: Edited,
    status: string | null,
    approvals: readonly ApprovalSummary[],
): { asked: ApprovalSummary } | { decided: string } | { ended: string } | 'queued' {
    const asked = askedIn(edit, approvals);
    if (asked !== undefined) return { asked };
    const later = approvals.find(
        (row) => row.work_item_id === edit.workItemId && row.id !== edit.withdrawn && row.status !== 'superseded',
    );
    if (later !== undefined) return { decided: later.id };
    if (status === null) return { ended: 'gone' };
    if (ENDED.has(status)) return { ended: status };
    return 'queued';
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
