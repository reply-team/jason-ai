/**
 * Pure: the links the Reply card asks Reply for, to connect or reconnect an account with no sign-in to Reply. The main
 * process asks through `reply api`, which holds the sign-in in its profile, and only the calls of the closed table below:
 * nothing of the page's input goes into one but an account id held to its shape and, for a new LinkedIn account, the
 * name typed for it, as the JSON body on standard input. Every address Reply answers is checked before it is shown.
 *
 * Whoever holds an address can connect an account to this Reply account, so it is a secret of the person's: it goes to
 * the page on the person's click and nowhere else, never into a log line, a refusal or a file. Nothing of Reply's own
 * error text is read but a closed code; each refusal is one sentence of the desktop's.
 */

import type { RunResult } from './programs.ts';

/** The four calls, by the name the log knows each by. `{id}` is an account id that `isReplyId` holds. */
export const replyApiCalls = {
    linkedin_connect: { method: 'POST', path: '/v3/linkedin-accounts/connection-link', body: 'name' },
    linkedin_pending: { method: 'GET', path: '/v3/linkedin-accounts/pending', body: null },
    linkedin_reconnect: { method: 'POST', path: '/v3/linkedin-accounts/{id}/reconnect', body: null },
    mailbox_page: { method: 'GET', path: '/v1/SharedPageUrl', body: null },
} as const;

export type ReplyApiAct = keyof typeof replyApiCalls;

/** An account id as Reply gives one: digits with no leading zero, at most sixteen, a safe integer. */
export function isReplyId(value: unknown): value is string {
    return typeof value === 'string' && /^[1-9][0-9]{0,15}$/.test(value) && Number.isSafeInteger(Number(value));
}

/**
 * What `reply` is started with for one call: `reply api <path>`, the method where it is not GET, the body read from
 * standard input for the one call that has one, and the CLI's compact print. Throws for anything outside the table.
 */
export function replyApiArguments(act: ReplyApiAct, id?: string): string[] {
    if (!Object.hasOwn(replyApiCalls, act)) throw new Error('That is not a call of the table.');
    const call = replyApiCalls[act];
    let path: string = call.path;
    if (path.includes('{id}')) {
        if (!isReplyId(id)) throw new Error('That is not an account id.');
        path = path.replace('{id}', id);
    }
    return ['api', path, ...(call.method === 'GET' ? [] : ['--method', call.method]), ...(call.body === null ? [] : ['--body', '-']), '--json'];
}

/** The desktop's sentences: the only words a link's ask ends with besides the link. */
export const replyLinkSentences = {
    badName: 'Name the LinkedIn account in one line of up to 100 characters.',
    badId: 'That is not an account Reply listed.',
    badPage: 'That is not a mailbox page the desktop offers.',
    busy: 'A link is already being asked for; wait for it.',
    limitReached: 'This Reply account has reached the number of LinkedIn accounts its plan allows.',
    paymentFailed: 'Reply could not take the payment for another LinkedIn account; settle it in Reply, then try again.',
    accountGone: 'Reply no longer has that LinkedIn account; Check now reads the list again.',
    pendingGone: 'That connection is no longer waiting in Reply; Check now reads the list again.',
    notInPlan: 'LinkedIn is not in this Reply account’s plan.',
    notPermitted: 'This Reply user is not permitted to add LinkedIn accounts.',
    notMade: 'Reply did not make the link.',
    mailboxPageOff: 'Connecting mailboxes by a shared page is not on for this Reply account’s plan.',
    signInAgain: 'Reply says the sign-in is no longer valid; sign in to Reply again on this card.',
    wait: 'Reply asked to wait; try again in a minute.',
    unreached: 'Reply could not be reached; try again.',
    badLink: 'Reply answered a link the desktop does not show.',
} as const;

const nameLimit = 100;

/** Why a typed LinkedIn account name is refused, or null: one plain line of one to a hundred characters, trimmed. */
export function linkedInNameProblem(value: unknown): string | null {
    if (typeof value !== 'string') return replyLinkSentences.badName;
    const name = value.trim();
    const length = [...name].length;
    if (length === 0 || length > nameLimit || /[\p{Cc}\p{Cf}]/u.test(name)) return replyLinkSentences.badName;
    return null;
}

/** What Reply answered, as `reply api` prints it under `--json`: `{"code": <status>, "data": <body>}`. */
export interface ReplyApiAnswer {
    status: number;
    data: unknown;
}

/**
 * Reads `reply api`'s print: an answer only where the CLI ended as it does for one, exit 0 with a status below 400 or
 * exit 1 with one at 400 or above; null for every other ending, a broken connection (exit 3), a time limit, a print that
 * is not that object.
 */
export function readReplyApi(result: RunResult): ReplyApiAnswer | null {
    if (result.exit !== 0 && result.exit !== 1) return null;
    let raw: unknown;
    try {
        raw = JSON.parse(result.stdout.trim());
    } catch {
        return null;
    }
    if (typeof raw !== 'object' || raw === null || Array.isArray(raw) || !('data' in raw)) return null;
    const { code: status, data } = raw as { code?: unknown; data: unknown };
    if (typeof status !== 'number' || !Number.isInteger(status) || status < 100 || status > 599) return null;
    if ((status >= 400) !== (result.exit === 1)) return null;
    return { status, data };
}

/** Whether Reply answered with a status of 2xx. */
export const answeredWell = (answer: ReplyApiAnswer | null): answer is ReplyApiAnswer => answer !== null && answer.status >= 200 && answer.status < 300;

function field(data: unknown, name: string): unknown {
    return typeof data === 'object' && data !== null && !Array.isArray(data) ? (data as Record<string, unknown>)[name] : undefined;
}

/** A problem document's top-level `code`, in lower case, or null where it has none. */
function codeOf(data: unknown): string | null {
    const code = field(data, 'code');
    return typeof code === 'string' && code !== '' ? code.toLowerCase() : null;
}

/** Whether a problem document's `reasons` holds a number, given as a number or as its digits. */
function hasReason(data: unknown, reason: number): boolean {
    const reasons = field(data, 'reasons');
    return Array.isArray(reasons) && reasons.some((each) => each === reason || each === String(reason));
}

/** The sentence for a LinkedIn call that did not answer a link, keyed on Reply's status and code and nothing else. */
export function linkedInRefusal(answer: ReplyApiAnswer | null, act: ReplyApiAct): string {
    if (answer === null) return replyLinkSentences.unreached;
    const { status, data } = answer;
    const code = codeOf(data);
    if (status === 409 && code === 'linkedinaccount.limitreached') return replyLinkSentences.limitReached;
    if (status === 409 && code === 'linkedinaccount.paymentfailed') return replyLinkSentences.paymentFailed;
    if (status === 404 && act === 'linkedin_reconnect' && code === 'linkedinaccount.notfound') return replyLinkSentences.accountGone;
    if (status === 403 && code === null && hasReason(data, 1)) return replyLinkSentences.notInPlan;
    if (status === 403 && code === null && hasReason(data, 2)) return replyLinkSentences.notPermitted;
    if (status === 400 && (code === null || code === 'linkedinaccount.invalidinput')) return replyLinkSentences.notMade;
    return common(status);
}

/** The sentence for the mailbox page's call that did not answer one: a 403 is the shared page being off for the plan. */
export function mailboxRefusal(answer: ReplyApiAnswer | null): string {
    if (answer === null) return replyLinkSentences.unreached;
    if (answer.status === 403) return replyLinkSentences.mailboxPageOff;
    return common(answer.status);
}

function common(status: number): string {
    if (status === 401) return replyLinkSentences.signInAgain;
    if (status === 429) return replyLinkSentences.wait;
    return replyLinkSentences.unreached;
}

/** A host name of plain letters, digits, dots and hyphens: labels that neither start nor end with a hyphen, two or more. */
const hostPattern = '(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\\.)+[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?';
/** The connect page's two forms: `/linkedin-connect/<id>` and `/linkedin-custom-connect/<id>`, the id 32 lowercase hex. */
const connectForm = new RegExp(`^https://(${hostPattern})/linkedin-(?:custom-)?connect/[0-9a-f]{32}$`);
/** The sign-in service a reconnect may answer directly: this host exactly, whatever its path and query. */
const signInHost = 'auth.linkedin.reply.io';
/** Printable ASCII with no space: what any address shown here is made of. */
const plain = /^[\x21-\x7e]+$/;

/** A link a person is shown, with the host it leads to drawn beside it. */
export interface CheckedLink {
    link: string;
    host: string;
}

/**
 * A LinkedIn link as Reply answers one, or null: https, no user, no port, no fragment, and either the connect page's
 * exact path with no query on any plain host (Reply's app domain for most accounts, another product domain by plan, a
 * white-label team's own), or anything on the sign-in service's exact host.
 */
export function checkedLinkedInLink(value: unknown): CheckedLink | null {
    if (typeof value !== 'string' || !plain.test(value)) return null;
    const form = connectForm.exec(value);
    if (form) return { link: value, host: form[1]!.toLowerCase() };
    if (value.startsWith(`https://${signInHost}/`) && !value.includes('#')) return { link: value, host: signInHost };
    return null;
}

/** The hosted page Reply answers for the account's mailboxes: `/hosted/eac/index/<token>`, no query, no fragment. */
const mailboxForm = new RegExp(`^https://(${hostPattern})/hosted/eac/index/([A-Za-z0-9_-]{8,200})$`);

/** The host and the token of the account's hosted mailbox page, or null for an address of any other shape. */
export function mailboxToken(value: unknown): { host: string; token: string } | null {
    if (typeof value !== 'string') return null;
    const form = mailboxForm.exec(value);
    return form ? { host: form[1]!.toLowerCase(), token: form[2]! } : null;
}

/** The three mailbox pages the card offers: Gmail's, Outlook's, and the hosted page itself for any other mailbox. */
export const mailboxPages = ['gmail', 'outlook', 'hosted'] as const;
export type MailboxPage = (typeof mailboxPages)[number];

export function isMailboxPage(value: unknown): value is MailboxPage {
    return typeof value === 'string' && (mailboxPages as readonly string[]).includes(value);
}

/** One mailbox page's address, built from a checked host and token and nothing else of the answer. */
export function mailboxAddress(host: string, token: string, page: MailboxPage): string {
    const encoded = encodeURIComponent(token);
    if (page === 'gmail') return `https://${host}/hosted/eac/ConnectWithGMail?uidToken=${encoded}`;
    if (page === 'outlook') return `https://${host}/hosted/eac/ConnectWithOffice?uidToken=${encoded}&isExchange=false`;
    return `https://${host}/hosted/eac/index/${encoded}`;
}

/** The address in the mailbox page's answer: the body itself as a string, or its `url`. */
export function mailboxAnswer(data: unknown): unknown {
    return typeof data === 'string' ? data : field(data, 'url');
}

/** A LinkedIn connection waiting to be finished, as the card lists it: never with its link. */
export interface ReplyPending {
    id: string;
    name: string;
    /** When its link stops working, as Reply wrote it; null where Reply gave none, or not as a moment. */
    expiresAtUtc: string | null;
    isExpired: boolean;
}

const moment = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?(?:Z|[+-]\d{2}:\d{2})?$/;

/**
 * The pending list's rows that carry an id of Reply's shape, each with its link beside it for the one caller that asks
 * for it; `pendingRows(...).map(withoutLink)` is what the card is given.
 */
export function pendingRows(data: unknown): (ReplyPending & { connectionLink: unknown })[] | null {
    if (!Array.isArray(data)) return null;
    const rows: (ReplyPending & { connectionLink: unknown })[] = [];
    for (const row of data) {
        const id = field(row, 'id');
        const text = typeof id === 'number' || typeof id === 'string' ? String(id) : '';
        if (!isReplyId(text)) continue;
        const name = field(row, 'name');
        const expires = field(row, 'expiresAtUtc');
        rows.push({
            id: text,
            name: typeof name === 'string' ? name : '',
            expiresAtUtc: typeof expires === 'string' && moment.test(expires) ? expires : null,
            isExpired: field(row, 'isExpired') === true,
            connectionLink: field(row, 'connectionLink'),
        });
    }
    return rows;
}

export const withoutLink = ({ id, name, expiresAtUtc, isExpired }: ReplyPending): ReplyPending => ({ id, name, expiresAtUtc, isExpired });
