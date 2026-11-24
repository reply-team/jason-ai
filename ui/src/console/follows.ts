import { en, type MessageKey } from '../locale/en';
import { t } from '../platform/i18n';

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

/** The Follows row's words. `followed` is null while the letter is unread or could not be read. */
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
