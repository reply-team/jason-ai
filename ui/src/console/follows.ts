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

const NEW_LETTER: Readonly<Record<string, string>> = {
    asked_new_thread: 'A new letter: it was asked to go as one of its own.',
    not_offered: 'A new letter: this provider cannot place a reply in a thread on this channel.',
    no_earlier_letter: 'A new letter: no earlier letter of this campaign to this person can be followed.',
    other_account: 'A new letter: the earlier letter went through another account of the provider.',
    other_mailbox: "A new letter: the earlier letter went from another of the provider's mailboxes.",
};

/** What the letter followed was, as a read of its work item says: its subject and the moment it went. */
export interface Followed {
    subject: string | null;
    sent: string | null;
}

const FOLLOWING_TAIL = 'It goes under that subject; its own subject below is not sent.';

/** The Follows row's words. `followed` is null while the letter is unread or could not be read. */
export function followsSentence(follows: FollowsWords, followed: Followed | null): string {
    if (follows.workItem === null) {
        return NEW_LETTER[follows.because ?? ''] ?? 'A new letter.';
    }
    if (followed?.subject) {
        return `A reply to "${followed.subject}"${followed.sent ? `, sent ${followed.sent}` : ''}. ${FOLLOWING_TAIL}`;
    }
    return `A reply to an earlier letter of this campaign. It goes under that letter's subject; its own subject below is not sent.`;
}
