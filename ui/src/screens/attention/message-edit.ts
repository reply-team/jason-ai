import { ApiError, type Api } from '../../api/client';
import type { Approval, ApprovalSummary, Page, WorkItem } from '../../api/types';

/** The two operations that put a person text in front of an approver: a first message, and a reply in a thread. */
export const SENDS = 'message.send';
export const REPLIES = 'conversation.reply';
export const isMessage = (operation: string): boolean => operation === SENDS || operation === REPLIES;

const record = (value: unknown): Record<string, unknown> | null =>
    value !== null && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : null;
const text = (value: unknown): string | null => (typeof value === 'string' ? value : null);

/** The words of a message as the arguments carry them. An absent field and a null one are the same: no text. */
export type Words = { subject: string | null; body: string | null; compose: string | null };

/**
 * A first message keeps its words in args.message (a subject, a body, how it is composed); a reply keeps its text in
 * args.body and is always final text, with no subject of its own.
 */
export function wordsOf(args: unknown, operation: string = SENDS): Words {
    if (operation === REPLIES) return { subject: null, body: text(record(args)?.['body']), compose: null };
    const message = record(record(args)?.['message']);
    return { subject: text(message?.['subject']), body: text(message?.['body']), compose: text(message?.['compose']) };
}

/** What the runtime approved: the arguments inside the approval subject. */
export const approvedArgs = (approval: Approval): unknown => record(record(approval.subject['input'])?.['args']);

/** What would be sent now: the arguments the work item holds. */
export const heldArgs = (item: WorkItem): unknown => item.context['input'];

/**
 * Whether the work item message no longer says what the approval was asked about. The approval subject carries the
 * arguments exactly as the item held them when it was parked, and the item holds them as they are now; the fields
 * that make the text (subject, body, compose for a first message, the body for a reply) are compared as strings,
 * character for character, with no trimming or other reading, and an absent field equals a null one. Two equal texts
 * are the same string in both documents, so they cannot compare as different; any other difference is a change of
 * what a person would send.
 */
export function isStale(approval: Approval, item: WorkItem): boolean {
    if (!isMessage(approval.operation)) return false;
    const asked = wordsOf(approvedArgs(approval), approval.operation);
    const now = wordsOf(heldArgs(item), approval.operation);
    return asked.subject !== now.subject || asked.body !== now.body || asked.compose !== now.compose;
}

/** A pending message written as text can be edited; a template cannot, and neither can an approval already decided. */
export function editable(approval: Approval): boolean {
    if (approval.status !== 'pending' || !isMessage(approval.operation)) return false;
    const words = wordsOf(approvedArgs(approval), approval.operation);
    return words.body !== null && words.compose !== 'template';
}

/** Whether the message has a subject to edit: an email does, a LinkedIn message and a reply do not. */
export function hasSubject(args: unknown, operation: string = SENDS): boolean {
    if (operation === REPLIES) return false;
    const message = record(record(args)?.['message']);
    return message !== null && ('subject' in message || message['format'] === 'email');
}

export const EDIT_REASON = 'Message text edited on the approval card.';

type Patch = { work_item_id: string; set: { input: Record<string, unknown> }; reason: string };

/**
 * The patch that saves an edit: the item arguments exactly as they are now, with the text replaced and nothing else
 * (a first message: its subject and body; a reply: its body). The caller passes the arguments just read, so no other
 * argument goes back changed.
 */
export function editPatch(operation: string, workItemId: string, held: unknown, subject: string, body: string): Patch {
    const args = record(held);
    if (!args) throw new Error('The work item holds no message to edit.');
    if (operation === REPLIES)
        return { work_item_id: workItemId, set: { input: { ...args, body } }, reason: EDIT_REASON };
    const message = record(args['message']);
    if (!message) throw new Error('The work item holds no message to edit.');
    const next = { ...message, body, ...(hasSubject(held, operation) ? { subject } : {}) };
    return { work_item_id: workItemId, set: { input: { ...args, message: next } }, reason: EDIT_REASON };
}

/** A sentence for what the runtime said, or for what went wrong; the runtime own words when it has them. */
export const sentence = (error: unknown): string => (error instanceof ApiError ? error.message : String(error));

export const readItem = (api: Api, id: string): Promise<WorkItem> =>
    api.call<WorkItem>('workitem.get', { work_item_id: id });

/** Reads the item again, then writes the edit over the arguments just read. */
export async function saveEdit(
    api: Api,
    operation: string,
    id: string,
    subject: string,
    body: string,
): Promise<WorkItem> {
    const item = await readItem(api, id);
    return api.call<WorkItem>('workitem.update', editPatch(operation, id, heldArgs(item), subject, body), 'write');
}

/**
 * After a save the runtime supersedes the pending approval and asks again under the new text. Asks for the item
 * pending approvals until one other than the old one shows, up to `tries` times, `delayMs` apart; null when none came.
 */
export async function awaitNewApproval(
    api: Api,
    workItemId: string,
    oldId: string,
    {
        tries = 30,
        delayMs = 1000,
        stopped = () => false,
    }: { tries?: number; delayMs?: number; stopped?: () => boolean } = {},
): Promise<string | null> {
    for (let attempt = 0; attempt < tries && !stopped(); attempt++) {
        const page = await api.call<Page<ApprovalSummary>>('approval.list', {
            work_item_id: workItemId,
            statuses: ['pending'],
        });
        const fresh = page.items.find((a) => a.id !== oldId);
        if (fresh) return fresh.id;
        await new Promise((resolve) => setTimeout(resolve, delayMs));
    }
    return null;
}
