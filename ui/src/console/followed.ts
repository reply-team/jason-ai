import { signal } from '@preact/signals-react';
import type { Api } from '../api/client';
import type { Approval, WorkItem } from '../api/types';
import { channelWords, toApprovalDetail } from './live';
import { followedOf, followsRow, type Followed, type FollowsRow, type FollowsWords } from './follows';

// The letter a follow-up goes under, read for its subject and its moment with `workitem.get`, once for each letter the
// console draws an approval about. The letter is a finished fact, so a read that succeeded is kept; a read that failed
// is dropped, so the next drawing asks again, and until a read has succeeded the card says the unread sentence.

const reads = signal<Readonly<Record<string, Followed>>>({});
const asked = new Set<string>();

/** What was read of a followed letter; null while it is unread or the read failed. */
export const followedRead = (workItem: string): Followed | null => reads.value[workItem] ?? null;

/** Reads a followed letter unless it is read or being read. */
export async function readFollowed(api: Api, workItem: string): Promise<void> {
    if (asked.has(workItem)) return;
    asked.add(workItem);
    try {
        const item = await api.call<WorkItem>('workitem.get', { work_item_id: workItem });
        reads.value = { ...reads.value, [workItem]: followedOf(item) };
    } catch {
        asked.delete(workItem);
    }
}

/** The letter an approval's send follows that is to be read: null for a send that follows none. */
export const followedLetter = (follows: FollowsWords | null): string | null => follows?.workItem ?? null;

/**
 * The Follows row an approval's card draws, from the send's `follows` and the channel in words, with what is read of
 * the letter followed so far. It starts no read: the drawing does (`readFollowed`).
 */
export function followsRowOf(follows: FollowsWords | null, channel: string | null): FollowsRow | null {
    const letter = followedLetter(follows);
    return followsRow(follows, channel === channelWords('email'), letter === null ? null : followedRead(letter));
}

/** Forgets every read: a pane or chat drawn anew reads again, and a test starts with none. */
export function forgetFollowed(): void {
    asked.clear();
    reads.value = {};
}

/** Whether an approval's email send has its own subject not sent, which the edit form's label says too. */
export function subjectIsNotSent(approval: Approval | undefined): boolean {
    if (approval === undefined) return false;
    const detail = toApprovalDetail(approval.preview, approval.operation);
    return followsRowOf(detail.follows, detail.channel)?.subjectNotSent ?? false;
}
