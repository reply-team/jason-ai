import { signal } from '@preact/signals-react';
import { Unpaired, type Api } from '../../api/client';
import type { Approval, ApprovalSummary, Page, WorkItem } from '../../api/types';
import { heldArgs, readItem, wordsOf } from './message-edit';
import { afterRead, askingOf, editOf, placeRows, rowNow, type Edited, type Kept } from './reasking-model';

// The messages edited on this page, until the runtime has asked about each again: the page's own memory, kept in no
// storage and asked of the runtime nowhere but where these screens already read. Every list of approvals reads it (the
// browser's Attention, Needs you, an agent's chat), so a row edited in one stays in each that held it; a reload of the
// page forgets it, and the row comes back when the runtime has asked again.

export type { Edited } from './reasking-model';

/** The edits made on this page. */
export const edits = signal<readonly Edited[]>([]);

/**
 * A save of a message's words withdrew its approval: the edit is kept until the runtime asks again. A save that left
 * the work waiting on the same approval (the runtime asks again only when the edit took the work out of waiting) keeps
 * nothing.
 */
export function noteEdit(approval: Approval, saved: WorkItem): void {
    if (saved.status === 'awaiting_approval') return;
    const edit: Edited = {
        workItemId: approval.work_item_id,
        withdrawn: approval.id,
        operation: approval.operation,
        words: wordsOf(heldArgs(saved), approval.operation),
        asked: null,
    };
    edits.value = [...edits.value.filter((each) => each.workItemId !== edit.workItemId), edit];
}

/** The runtime asked again about an edited message: the row is that approval from now on. */
export function noteAsked(workItemId: string, approvalId: string): void {
    edits.value = edits.value.map((each) =>
        each.workItemId === workItemId && each.asked === null ? { ...each, asked: approvalId } : each,
    );
}

/** The work of an edited message ended another way: its row goes, as a row decided elsewhere does. */
function noteEnded(workItemId: string): void {
    edits.value = edits.value.filter((each) => each.workItemId !== workItemId);
}

/** The edit waiting for the runtime to ask again whose withdrawn approval this is, if there is one. */
export const asking = (approvalId: string): Edited | undefined => askingOf(edits.value, approvalId);

/** Forgets every edit; for a test that starts afresh. */
export function forgetEdits(): void {
    edits.value = [];
}

/**
 * Where an edit not asked about yet stands, from the reads these screens already make: its work item, and when that
 * waits for an approval again, the item's pending approvals. Asking while a read fails; an ended pairing is the
 * session's to answer.
 */
async function standing(api: Api, edit: Edited): Promise<ReturnType<typeof afterRead>> {
    try {
        const item = await readItem(api, edit.workItemId);
        if (item.status !== 'awaiting_approval') return afterRead(edit, item.status);
        const page = await api.call<Page<ApprovalSummary>>('approval.list', {
            work_item_id: edit.workItemId,
            statuses: ['pending'],
        });
        return afterRead(edit, item.status, page.items);
    } catch (error) {
        if (error instanceof Unpaired) throw error;
        return 'asking';
    }
}

/**
 * A list's pending approvals as the runtime gave them, with every edited row that list held kept where it stood: the
 * withdrawn approval's row while the runtime has not asked again, and the approval it asked with once it has. A row
 * whose work ended another way, or whose approval asked again left the list, goes as any row decided elsewhere goes.
 */
export async function keepEdited(
    api: Api,
    previous: readonly ApprovalSummary[],
    fresh: readonly ApprovalSummary[],
): Promise<ApprovalSummary[]> {
    const kept: Kept[] = [];
    for (const before of previous) {
        const edit = editOf(edits.value, before.id);
        if (edit === undefined) continue;
        const listed = rowNow(edit, before, fresh);
        if (listed === 'gone') continue;
        const now = listed === 'read' ? await standing(api, edit) : listed;
        if (now === 'ended') noteEnded(edit.workItemId);
        else if (now === 'asking') kept.push({ from: before.id, row: before });
        else {
            if (edit.asked === null) noteAsked(edit.workItemId, now.asked.id);
            kept.push({ from: before.id, row: now.asked });
        }
    }
    return placeRows(
        previous.map((row) => row.id),
        fresh,
        kept,
    );
}
