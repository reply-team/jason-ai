import type { MembershipWhy } from '../../api/types';

/**
 * What a count's why says happened, in the few words a row has room for: "replied by email", "opted out of email",
 * "added", "removed", or the operation or role of the work. Under People the why's text names which count it was.
 */
export function happened(why: MembershipWhy, count: string | undefined): string | null {
    const what = count === undefined || count === 'people' ? (why.text ?? count) : count;
    if (what === 'replied') return `replied by ${why.code ?? 'a message'}`;
    if (what === 'opted_out') return `opted out of ${why.code ?? 'a channel'}`;
    return why.code;
}
