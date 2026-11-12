/**
 * Pure: what the desktop does with the Reply card's account acts, each of which is one verb of the Reply CLI
 * (`reply-cli-account.ts` holds that contract): its own sentences, what each refusal's code makes the card do, the main
 * process's confirmation boxes, and the check a link passes before the system opener is handed it.
 *
 * A connection link, a consent address and the shared page's address are the person's secrets: the main process hands
 * the link to the system opener and keeps nothing, and the page learns only that something was opened, when it expires
 * and its pending entry. Nothing of the CLI's words is read but its code, and every code the desktop does not act on is
 * shown by one sentence that carries it, so a code the CLI adds never breaks the card.
 */

import { replyCliCodes as codes, replyCliSharedPageWarning, replyCliUsagePrefix, type ReplyCliChannel, type ReplyCliEnding } from './reply-cli-account.ts';

/** How long one verb may take before it is stopped. */
export const replyActLimitMs = 30_000;

/** An account id as Reply gives one: digits with no leading zero, at most sixteen, a safe integer. */
export function isReplyId(value: unknown): value is string {
    return typeof value === 'string' && /^[1-9][0-9]{0,15}$/.test(value) && Number.isSafeInteger(Number(value));
}

export const isReplyChannel = (value: unknown): value is ReplyCliChannel => value === 'email' || value === 'linkedin';

/** The largest daily limit the CLI takes. */
export const dailyLimitMost = 2_147_483_647;

/** The desktop's sentences: the only words an act ends with. */
export const replyAccountSentences = {
    badName: 'Name the LinkedIn account in one line of up to 100 characters.',
    badId: 'That is not an account Reply listed.',
    badRequest: 'That is not an act the card offers.',
    badLimit: `A daily limit is a whole number from 0 to ${dailyLimitMost}.`,
    busy: 'Another act on a Reply account is going; wait for it.',
    notConfirmed: 'Nothing was done.',
    notOffered: 'That is offered only once Reply has said it is needed.',
    sharedPageOffer: 'This Reply does not make a link for one mailbox yet. Its shared mailbox page can be opened instead.',
    signedOut: 'The LinkedIn account is signed out of LinkedIn, so Reply did not do it: reconnect the account first.',
    inUse: 'Sequences use this LinkedIn account, so it was not deleted.',
    outcomeUnknown: 'Reply did not say how that ended; the list is read again to show where it stands.',
    stateChanged: 'The account’s state changed meanwhile, so nothing was changed; the list is read again.',
    stateUnknown: 'Reply gave the account a state the desktop does not know, so nothing was changed; the list is read again.',
    accountGone: 'Reply no longer has that account; the list is read again.',
    mailboxLimit: 'This Reply account has reached the number of mailboxes its plan allows.',
    mailboxSeat: 'One more mailbox would need another paid seat in Reply.',
    linkedInLimit: 'This Reply account has reached the number of LinkedIn accounts its plan allows.',
    notInPlan: 'This Reply account’s plan or role does not include that.',
    sharedPageOff: 'This Reply account’s plan does not include the shared mailbox page.',
    unreached: 'The Reply CLI did not answer in time, or could not be run; try again.',
    unreachedWrite: 'The Reply CLI did not answer in time, or could not be run; the list is read again to show whether it was done.',
    badLink: 'The Reply CLI answered a link the desktop does not open; the list is read again.',
    notOpened: 'The browser could not be asked to open the link; the list is read again.',
} as const;

/** Every code the desktop does not act on, known or not, in one sentence that carries it. */
export const otherRefusal = (code: string | null): string =>
    code === null ? 'Reply did not do it, and the Reply CLI gave no code.' : `Reply did not do it: the Reply CLI answered ${code}.`;

/** What the card says, and offers nothing else, below the Reply CLI the acts need. */
export const belowMinimumSentence = (minimum: string): string =>
    `Managing Reply accounts here needs the Reply CLI ${minimum} or later: update the Reply CLI.`;

const nameLimit = 100;

/** Why a typed LinkedIn account name is refused, or null: one plain line of one to a hundred characters, trimmed. */
export function linkedInNameProblem(value: unknown): string | null {
    if (typeof value !== 'string') return replyAccountSentences.badName;
    const name = value.trim();
    const length = [...name].length;
    if (length === 0 || length > nameLimit || /[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]/u.test(name)) return replyAccountSentences.badName;
    return null;
}

/** The acts of the card, by the verb each is. */
export type ReplyAct = 'connect' | 'reconnect' | 'pending' | 'limits' | 'pause' | 'resume' | 'unblock' | 'delete';

/** What the card offers after a refusal: the shared page, a forced delete, or a reconnect. */
export type ReplyOffer = 'shared_page' | 'force' | 'reconnect';

/**
 * An act that ended without its effect: the desktop's sentence, the CLI's code where it gave one, what the card may
 * offer next, and whether it reads the list again to show what is.
 */
export interface ReplyRefusal {
    refused: string;
    code: string | null;
    offer: ReplyOffer | null;
    reread: boolean;
}

const refusal = (refused: string, code: string | null, offer: ReplyOffer | null = null, reread = false): ReplyRefusal => ({ refused, code, offer, reread });

/** The plan's limits: a sentence, nothing offered. */
const planLimits: Readonly<Record<string, string>> = {
    [codes.emailAccountLimitReached]: replyAccountSentences.mailboxLimit,
    [codes.emailAccountNoFreeMailboxSlots]: replyAccountSentences.mailboxSeat,
    [codes.linkedInAccountLimitReached]: replyAccountSentences.linkedInLimit,
    [codes.accessFeatureUnavailable]: replyAccountSentences.notInPlan,
    [codes.sharedPageNotEnabled]: replyAccountSentences.sharedPageOff,
};

/** The codes after which the card reads the list again and shows what is; it never repeats the act by itself. */
const rereads: Readonly<Record<string, string>> = {
    [codes.outcomeUnknown]: replyAccountSentences.outcomeUnknown,
    [codes.accountStatusChanged]: replyAccountSentences.stateChanged,
    [codes.accountStatusUnknown]: replyAccountSentences.stateUnknown,
    [codes.emailAccountNotFound]: replyAccountSentences.accountGone,
    [codes.linkedInAccountNotFound]: replyAccountSentences.accountGone,
};

/** The acts that change something at Reply, whose ending without an answer may still have done it. */
const writes: ReadonlySet<ReplyAct> = new Set(['connect', 'reconnect', 'limits', 'pause', 'resume', 'unblock', 'delete']);

/**
 * What an act that did not end done comes to. The shared page is offered only after `email_link.not_available` on an
 * email link asked without it, and a forced delete only after `linkedin.account_in_use` on a LinkedIn delete asked
 * without it; neither is ever the first try.
 */
export function refusalOf(
    ending: Exclude<ReplyCliEnding, { kind: 'done' }>,
    act: { verb: ReplyAct; channel: ReplyCliChannel; write: boolean; sharedPage?: boolean; force?: boolean },
): ReplyRefusal {
    const write = act.write && writes.has(act.verb);
    if (ending.kind === 'unknown') return refusal(replyAccountSentences.outcomeUnknown, codes.outcomeUnknown, null, true);
    if (ending.kind === 'unreached') return write ? refusal(replyAccountSentences.unreachedWrite, null, null, true) : refusal(replyAccountSentences.unreached, null);
    const code = ending.code;
    if (code === null) return refusal(otherRefusal(null), null);
    if (code === codes.emailLinkNotAvailable && act.channel === 'email' && (act.verb === 'connect' || act.verb === 'reconnect') && act.sharedPage !== true) {
        return refusal(replyAccountSentences.sharedPageOffer, code, 'shared_page');
    }
    // A reconnect's own refusal offers no second reconnect.
    if (code === codes.linkedInInvalidCookie) return refusal(replyAccountSentences.signedOut, code, act.verb === 'reconnect' ? null : 'reconnect');
    if (code === codes.linkedInAccountInUse && act.verb === 'delete' && act.channel === 'linkedin' && act.force !== true) {
        return refusal(replyAccountSentences.inUse, code, 'force');
    }
    if (Object.hasOwn(rereads, code)) return refusal(rereads[code]!, code, null, true);
    if (Object.hasOwn(planLimits, code)) return refusal(planLimits[code]!, code);
    // A usage error is the desktop's own defect; it is shown like any other code, and the code is logged.
    return refusal(otherRefusal(code), code);
}

/** Whether a code is a usage error's, the desktop's own defect. */
export const isUsageCode = (code: string | null): boolean => code !== null && code.startsWith(replyCliUsagePrefix);

/**
 * Whether a host is a host name and not an address written as numbers: a browser reads a host whose last label is a
 * number (all digits, or `0x` and hex) as IPv4, and one in brackets as IPv6.
 */
function isHostName(host: string): boolean {
    if (host === '' || host.startsWith('[') || !host.includes('.')) return false;
    const last = host.slice(host.lastIndexOf('.') + 1);
    return last !== '' && !/^[0-9]+$/.test(last) && !/^0x/i.test(last);
}

/**
 * Whether a link the CLI printed is one the system opener is handed: https, on a host name, with no user or password,
 * no port and nothing but printable ASCII. Anything else is opened nowhere.
 */
export function isOpenableLink(value: unknown): value is string {
    if (typeof value !== 'string' || !/^[\x21-\x7e]+$/.test(value)) return false;
    let url: URL;
    try {
        url = new URL(value);
    } catch {
        return false;
    }
    return url.protocol === 'https:' && url.username === '' && url.password === '' && url.port === '' && isHostName(url.hostname);
}

/** An account as the main process names it in a box: by the list's name and address, as the card shows it. */
export interface NamedAccount {
    channel: string;
    name: string;
    address: string;
}

const capped = (text: string): string => {
    const characters = [...text.replace(/[\p{Cc}\p{Cf}]/gu, ' ').trim()];
    return characters.length > 80 ? `${characters.slice(0, 80).join('')}…` : characters.join('');
};

/** `the mailbox Sales (sales@example.test)`, `the LinkedIn account Ada`: the account as the list names it. */
export function accountWords(account: NamedAccount): string {
    const name = capped(account.name);
    const address = capped(account.address);
    const named = name && address && name !== address ? `${name} (${address})` : name || address || 'with no name';
    return `${account.channel === 'linkedin' ? 'the LinkedIn account' : 'the mailbox'} ${named}`;
}

/** A native box of the main process: its words, Cancel focused, and only the first button a yes. */
export interface ReplyBox {
    type: 'question' | 'warning';
    title: string;
    message: string;
    detail: string;
    buttons: readonly [string, 'Cancel'];
    defaultId: 1;
    cancelId: 1;
    noLink: true;
}

const box = (type: ReplyBox['type'], message: string, detail: string, yes: string): ReplyBox => ({
    type,
    title: message,
    message,
    detail,
    buttons: [yes, 'Cancel'],
    defaultId: 1,
    cancelId: 1,
    noLink: true,
});

/** The box before the shared page is asked for: it carries the CLI's own warning. */
export function sharedPageBox(): ReplyBox {
    return box(
        'warning',
        'Open Reply’s shared mailbox page?',
        `The Reply CLI warns that ${replyCliSharedPageWarning}. It opens in your browser, and the desktop keeps nothing of it.`,
        'Open the page',
    );
}

/** The box of an act on one account, naming it as the list does. */
export function actBox(act: 'pause' | 'resume' | 'unblock' | 'delete' | 'force' | 'limits', account: NamedAccount, daily?: number): ReplyBox {
    const named = accountWords(account);
    switch (act) {
        case 'pause':
            return box('question', `Pause ${named}?`, 'Reply stops its automation until it is resumed.', 'Pause');
        case 'resume':
            return box('question', `Resume ${named}?`, 'Reply starts its automation again.', 'Resume');
        case 'unblock':
            return box('question', `Unblock ${named}?`, 'Reply lets its sequences send again after its provider stopped it from sending.', 'Unblock');
        case 'delete':
            return box('warning', `Delete ${named} from Reply?`, 'Reply removes it, and this cannot be undone.', 'Delete');
        case 'force':
            return box('warning', `Delete ${named} although sequences use it?`, 'Sequences use this LinkedIn account. Reply deletes it anyway, and this cannot be undone.', 'Delete anyway');
        case 'limits':
            return box('question', `Set the daily limit of ${named} to ${daily ?? 0}?`, 'Reply sends at most that many emails from it in a day, and caps a value above what the plan allows.', 'Set the limit');
    }
}

/**
 * Asks the person on the window with a box: yes only for its first button, and no where there is no window to ask on.
 * A box is closed when its window is hidden or closed (`gone` says when, and answers how to stop listening), and a box
 * closed so is a no whatever it answered, so no box waits unseen while the card's acts are held for it.
 */
export async function askBox<Window>(
    window: Window | null,
    show: (window: Window, options: ReplyBox, signal: AbortSignal) => Promise<{ response: number }>,
    options: ReplyBox,
    gone: (window: Window, abort: () => void) => () => void = () => () => undefined,
): Promise<boolean> {
    if (window === null) return false;
    const closing = new AbortController();
    const stop = gone(window, () => closing.abort());
    try {
        const { response } = await show(window, options, closing.signal);
        return !closing.signal.aborted && response === 0;
    } finally {
        stop();
    }
}
