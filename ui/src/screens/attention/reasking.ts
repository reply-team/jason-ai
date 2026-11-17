import { signal } from '@preact/signals-react';
import { ApiError, Unpaired, type Api } from '../../api/client';
import type { Approval, ApprovalSummary, Page, WorkItem } from '../../api/types';
import { approvedArgs, heldArgs, readItem, wordsOf } from './message-edit';
import {
    APPROVAL_STATUSES,
    afterRead,
    askingOf,
    differs,
    editOf,
    placeRows,
    rowNow,
    withdrawnOf,
    type Edited,
    type Kept,
    type Read,
} from './reasking-model';

// The messages edited on this page: the page's own memory, kept in no storage and asked of the runtime nowhere but where
// these screens already read. Every list of approvals reads it (the browser's Attention, a campaign's, Needs you, an
// agent's chat), so a row edited in one stays in each that held it; a reload of the page forgets it, and the row comes
// back when the runtime has asked again. An approval an edit withdrew stays withdrawn here for the page's life: no
// surface acts on it again, whatever became of the edit.

export type { Edited } from './reasking-model';

/** The edits made on this page. */
export const edits = signal<readonly Edited[]>([]);

/**
 * A save of a message's words withdrew its approval: the edit is kept for the page's life. A save that left the work
 * waiting on the same approval (the runtime asks again only when the edit took the work out of waiting) keeps nothing.
 */
export function noteEdit(approval: Approval, saved: WorkItem): void {
    if (saved.status === 'awaiting_approval') return;
    const edit: Edited = {
        workItemId: approval.work_item_id,
        withdrawn: approval.id,
        operation: approval.operation,
        words: wordsOf(heldArgs(saved), approval.operation),
        asked: null,
        ended: null,
        read: 'queued',
    };
    edits.value = [...edits.value.filter((each) => each.withdrawn !== edit.withdrawn), edit];
}

function change(workItemId: string, next: (edit: Edited) => Edited): void {
    edits.value = edits.value.map((each) =>
        each.workItemId === workItemId && each.asked === null && each.ended === null ? next(each) : each,
    );
}

/** The runtime asked again about an edited message: the row is that approval from now on. */
export function noteAsked(workItemId: string, approvalId: string): void {
    change(workItemId, (edit) => ({ ...edit, asked: approvalId }));
}

/** The work of an edited message ended another way: its row goes, as a row decided elsewhere does. */
export function noteEnded(workItemId: string, how: string): void {
    change(workItemId, (edit) => ({ ...edit, ended: how }));
}

function noteRead(workItemId: string, read: Read): void {
    change(workItemId, (edit) => (edit.read === read ? edit : { ...edit, read }));
}

/** The edit that withdrew this approval, whatever became of it: no surface acts on that approval again. */
export const withdrawn = (approvalId: string): Edited | undefined => withdrawnOf(edits.value, approvalId);

/** The edit waiting for the runtime to ask again whose withdrawn approval this is, if there is one. */
export const asking = (approvalId: string): Edited | undefined => askingOf(edits.value, approvalId);

/**
 * Whether this approval is the one the runtime asked again with after an edit on this page, carrying words other than
 * the ones the person saved: its row says so before anything on it can be approved.
 */
export function changedSince(approval: Approval): boolean {
    const edit = edits.value.find((each) => each.asked === approval.id);
    return edit !== undefined && differs(edit, wordsOf(approvedArgs(approval), approval.operation));
}

/** How many journal lines named each work item, so a list reads a kept row's item again only when one did. */
const heard = new Map<string, number>();

/** A journal line named this work item: a list holding its edited row reads where it stands at its next read. */
export function heardOf(workItemId: string | null): void {
    if (workItemId !== null && edits.value.some((edit) => edit.workItemId === workItemId))
        heard.set(workItemId, (heard.get(workItemId) ?? 0) + 1);
}

/** Forgets every edit; for a test that starts afresh. */
export function forgetEdits(): void {
    edits.value = [];
    heard.clear();
}

/**
 * Where an edit not asked about yet stands, from the reads these screens already make: its work item, and every
 * approval of that item. A read that fails says the page could not read it; an item the runtime does not have is gone.
 */
async function standing(api: Api, edit: Edited): Promise<ReturnType<typeof afterRead> | 'unread'> {
    let status: string | null;
    try {
        status = (await readItem(api, edit.workItemId)).status;
    } catch (error) {
        if (error instanceof Unpaired) throw error;
        if (!(error instanceof ApiError) || error.code !== 'work_item_not_found') return 'unread';
        status = null;
    }
    try {
        const page = await api.call<Page<ApprovalSummary>>('approval.list', {
            work_item_id: edit.workItemId,
            statuses: [...APPROVAL_STATUSES],
        });
        return afterRead(edit, status, page.items);
    } catch (error) {
        if (error instanceof Unpaired) throw error;
        return 'unread';
    }
}

/**
 * One list's memory of which journal lines it has read each kept row's work item after: a list read on the journal's
 * word reads a kept row's item again only when a line named it since; any other read of the list reads every one.
 */
export function keptReads(): (workItemId: string, followed: boolean) => boolean {
    const seen = new Map<string, number>();
    return (workItemId, followed) => {
        const now = heard.get(workItemId) ?? 0;
        if (followed && seen.has(workItemId) && seen.get(workItemId) === now) return false;
        seen.set(workItemId, now);
        return true;
    };
}

/**
 * A list's pending approvals as the runtime gave them, with every edited row that list held kept where it stood: the
 * withdrawn approval's row while the runtime has not asked again, and the approval it asked with once it has. A row
 * whose work ended another way, or whose approval asked again was decided, goes as any row decided elsewhere goes.
 * `due` says whether this read of the list reads a kept row's work item again.
 */
export async function keepEdited(
    api: Api,
    previous: readonly ApprovalSummary[],
    fresh: readonly ApprovalSummary[],
    due: (workItemId: string) => boolean = () => true,
): Promise<ApprovalSummary[]> {
    const kept: Kept[] = [];
    for (const before of previous) {
        const edit = editOf(edits.value, before.id);
        if (edit === undefined) continue;
        const listed = rowNow(edit, fresh);
        if (listed === 'gone') continue;
        if (listed !== 'read') {
            if (edit.asked === null) noteAsked(edit.workItemId, listed.asked.id);
            kept.push({ from: before.id, row: listed.asked });
            continue;
        }
        if (!due(edit.workItemId)) {
            kept.push({ from: before.id, row: before });
            continue;
        }
        const now = await standing(api, edit);
        if (now === 'queued' || now === 'unread') {
            noteRead(edit.workItemId, now);
            kept.push({ from: before.id, row: before });
        } else if ('ended' in now) noteEnded(edit.workItemId, now.ended);
        else if ('decided' in now) noteAsked(edit.workItemId, now.decided);
        else {
            noteAsked(edit.workItemId, now.asked.id);
            kept.push({ from: before.id, row: now.asked });
        }
    }
    return placeRows(
        previous.map((row) => row.id),
        fresh,
        kept,
    );
}
