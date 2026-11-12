import { ApiError, type Api } from '../../api/client';
import type { Approval, WorkItem } from '../../api/types';

/** The only operation whose text a person may edit on the card, and whose card is checked against its item. */
export const SENDS = 'message.send';

const record = (value: unknown): Record<string, unknown> | null =>
    value !== null && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : null;
const text = (value: unknown): string | null => (typeof value === 'string' ? value : null);

/** The words of a message as the arguments carry them. An absent field and a null one are the same: no text. */
export type Words = { subject: string | null; body: string | null; compose: string | null };

export function wordsOf(args: unknown): Words {
    const message = record(record(args)?.['message']);
    return { subject: text(message?.['subject']), body: text(message?.['body']), compose: text(message?.['compose']) };
}

/** What the runtime approved: the arguments inside the approval subject. */
export const approvedArgs = (approval: Approval): unknown => record(record(approval.subject['input'])?.['args']);

/** What would be sent now: the arguments the work item holds. */
export const heldArgs = (item: WorkItem): unknown => item.context['input'];

/**
 * Whether the work item message no longer says what the approval was asked about. The approval subject carries the
 * arguments exactly as the item held them when it was parked, and the item holds them as they are now; the three fields
 * that make the text (subject, body, compose) are compared as strings, character for character, with no trimming or
 * other reading, and an absent field equals a null one. Two equal texts are the same string in both documents, so they
 * cannot compare as different; any other difference is a change of what a person would send.
 */
export function isStale(approval: Approval, item: WorkItem): boolean {
    if (approval.operation !== SENDS) return false;
    const asked = wordsOf(approvedArgs(approval));
    const now = wordsOf(heldArgs(item));
    return asked.subject !== now.subject || asked.body !== now.body || asked.compose !== now.compose;
}

/** A pending text message can be edited; a template cannot, and neither can an approval already decided. */
export function editable(approval: Approval): boolean {
    if (approval.status !== 'pending' || approval.operation !== SENDS) return false;
    const words = wordsOf(approvedArgs(approval));
    return words.body !== null && words.compose !== 'template';
}

/** Whether the message has a subject to edit: an email does, a LinkedIn message does not. */
export function hasSubject(args: unknown): boolean {
    const message = record(record(args)?.['message']);
    return message !== null && ('subject' in message || message['format'] === 'email');
}

export const EDIT_REASON = 'Message text edited on the approval card.';

/**
 * The patch that saves an edit: the item arguments exactly as they are now, with the message subject and body
 * replaced and nothing else. The caller passes the arguments just read, so no other argument goes back changed.
 */
export function editPatch(
    workItemId: string,
    held: unknown,
    subject: string,
    body: string,
): { work_item_id: string; set: { input: Record<string, unknown> }; reason: string } {
    const args = record(held);
    const message = record(args?.['message']);
    if (!args || !message) throw new Error('The work item holds no message to edit.');
    const next = { ...message, body, ...(hasSubject(held) ? { subject } : {}) };
    return { work_item_id: workItemId, set: { input: { ...args, message: next } }, reason: EDIT_REASON };
}

/** A sentence for what the runtime said, or for what went wrong; the runtime own words when it has them. */
export const sentence = (error: unknown): string => (error instanceof ApiError ? error.message : String(error));

export const readItem = (api: Api, id: string): Promise<WorkItem> =>
    api.call<WorkItem>('workitem.get', { work_item_id: id });

/** Reads the item again, then writes the edit over the arguments just read. */
export async function saveEdit(api: Api, id: string, subject: string, body: string): Promise<WorkItem> {
    const item = await readItem(api, id);
    return api.call<WorkItem>('workitem.update', editPatch(id, heldArgs(item), subject, body), 'write');
}
