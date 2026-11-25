import { en, type MessageKey } from '../locale/en';
import type { WorkItem } from '../api/types';
import { t } from '../platform/i18n';
import { when } from '../time/format';

/** What an email send's root `follows` says: the letter it goes under, or why it is a letter of its own. */
export interface FollowsWords {
    /** The earlier letter's work item; null for a letter of its own. */
    workItem: string | null;
    because: string | null;
}

const record = (value: unknown): Record<string, unknown> | null =>
    value !== null && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : null;
const text = (value: unknown): string | null => (typeof value === 'string' && value ? value : null);

/** The preview's `/follows` highlight as words' inputs; null where the preview carries none. */
export function followsOf(value: unknown): FollowsWords | null {
    const follows = record(value);
    if (follows === null) return null;
    return { workItem: text(follows['work_item']), because: text(follows['because']) };
}

/** What the letter followed was, as a read of its work item says: its subject and its own moment, already in words. */
export interface Followed {
    subject: string | null;
    moment: string | null;
}

/**
 * The Follows row's words. `followed` is null while the letter is unread or could not be read; a read that succeeded but
 * found an empty subject or no moment shows the read-failed sentence too, since a reply to "" says nothing. A followed
 * work item whose `because` is neither `named` nor `line_root` is worded as `line_root`: the runtime says only those
 * two for a letter it follows.
 */
export function followsSentence(follows: FollowsWords, followed: Followed | null): string {
    if (follows.workItem === null) {
        const key = `follows.new.${follows.because ?? ''}`;
        return t((Object.hasOwn(en, key) ? key : 'follows.new.other') as MessageKey);
    }
    if (followed?.subject && followed.moment) {
        const key = follows.because === 'named' ? 'follows.reply.named' : 'follows.reply.line_root';
        return t(key, { subject: followed.subject, moment: followed.moment });
    }
    return t('follows.reply.unread');
}

/**
 * What the work item of a letter that was followed says of it, in words: its subject, and its moment, which is its
 * send_after where it has one, else when the provider accepted it. A work item's context.input is the operation's
 * arguments themselves.
 */
export function followedOf(item: WorkItem): Followed {
    const input = (item.context as { input?: { message?: { subject?: unknown }; send_after?: unknown } }).input;
    const subject = input?.message?.subject;
    const sendAfter = input?.send_after;
    const at = typeof sendAfter === 'string' && sendAfter ? sendAfter : item.finished_at;
    return {
        subject: typeof subject === 'string' && subject ? subject : null,
        moment: at ? when(new Date(at), new Date(), Intl.DateTimeFormat().resolvedOptions().timeZone) : null,
    };
}

/** What an approval of an email send shows about where it lands: the Follows row, and whether the subject is not sent. */
export interface FollowsRow {
    /** The row's words: where the letter goes, or why it is a letter of its own. */
    words: string;
    /** Whether the letter's own subject is not sent, because it goes under the followed letter's. */
    subjectNotSent: boolean;
}

/**
 * The Follows row, for both shells so they cannot drift apart: shown for an email send whose preview says where it
 * goes, with the words of `followsSentence`; the subject is marked as not sent exactly where the send follows a letter.
 * `followed` is null while that letter is unread or could not be read.
 */
export function followsRow(follows: FollowsWords | null, email: boolean, followed: Followed | null): FollowsRow | null {
    if (!email || follows === null) return null;
    return { words: followsSentence(follows, followed), subjectNotSent: follows.workItem !== null };
}

/** Whether an email send's own subject is not sent: it goes under the subject of the letter it follows. */
export const subjectNotSent = (follows: FollowsWords | null, email: boolean): boolean =>
    followsRow(follows, email, null)?.subjectNotSent ?? false;
