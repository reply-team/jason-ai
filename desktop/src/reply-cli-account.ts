/**
 * Pure: the Reply CLI's account verbs as the desktop uses them, and nothing of the desktop's own: the arguments each verb
 * is started with, the shapes it prints under `--json`, the codes its refusals carry and the warning it gives with the
 * shared mailbox page. This file is the desktop's whole knowledge of that contract, so a change to it at the CLI is a
 * change here; `test/reply-cli-account-fixture.ts` holds what the CLI prints, taken from the CLI's own tests.
 *
 * The CLI prints a verb's result as one JSON object on stdout and exits 0; it ends a refusal with one JSON line
 * `{"error":{"code",...}}` on stderr and exits 1, a usage error (a `usage.*` code) exits 2, and a write whose outcome it
 * does not know exits 3 with `outcome.unknown`. A link it prints in the field `link` once, and never on stderr.
 */

export type ReplyCliChannel = 'email' | 'linkedin';
export type ReplyCliProvider = 'gmail' | 'office365';

/** The codes the desktop does something with; every other code is shown by the code alone. */
export const replyCliCodes = {
    emailLinkNotAvailable: 'email_link.not_available',
    emailLinkProviderRequired: 'email_link.provider_required',
    sharedPageNotEnabled: 'shared_page.not_enabled',
    emailAccountNotFound: 'email_account.not_found',
    emailAccountLimitReached: 'email_account.limit_reached',
    emailAccountNoFreeMailboxSlots: 'email_account.no_free_mailbox_slots',
    emailAccountReconnectNotSupported: 'email_account.reconnect_not_supported',
    linkedInAccountNotFound: 'linkedin.account_not_found',
    linkedInInvalidCookie: 'linkedin.invalid_cookie',
    linkedInAccountLimitReached: 'linkedin.account_limit_reached',
    linkedInAccountInUse: 'linkedin.account_in_use',
    accountStatusUnknown: 'account.status_unknown',
    accountStatusChanged: 'account.status_changed',
    accountPauseUnavailable: 'account.pause_unavailable',
    accessFeatureUnavailable: 'access.feature_unavailable',
    outcomeUnknown: 'outcome.unknown',
} as const;

/** The prefix of the codes of a usage error, which the CLI ends with exit 2 before it asks Reply anything. */
export const replyCliUsagePrefix = 'usage.';

/**
 * The CLI's own words for the shared mailbox page's address, which it prints beside one as `warning` and in the hint of
 * `email_link.not_available`.
 */
export const replyCliSharedPageWarning = "this address does not expire and lets its holder manage all of this user's mailboxes, deletion included";

const json = '--json';

/**
 * `account connect`: LinkedIn by a name, email by a provider. The name is one argument with its flag, so no name can be
 * read as another option. `--allow-shared-page` is added only where the caller says so.
 */
export function connectArguments(
    request: { channel: 'linkedin'; name: string } | { channel: 'email'; provider: ReplyCliProvider; allowSharedPage: boolean },
): string[] {
    if (request.channel === 'linkedin') return ['account', 'connect', '--channel', 'linkedin', `--name=${request.name}`, json];
    return ['account', 'connect', '--channel', 'email', '--provider', request.provider, ...(request.allowSharedPage ? ['--allow-shared-page'] : []), json];
}

/** `account reconnect <id>`: by the account's id; `--allow-shared-page` for email only, and only where the caller says so. */
export function reconnectArguments(channel: ReplyCliChannel, id: string, allowSharedPage: boolean): string[] {
    return ['account', 'reconnect', id, '--channel', channel, ...(channel === 'email' && allowSharedPage ? ['--allow-shared-page'] : []), json];
}

/** `account pending`, one channel at a time, as the accounts are listed. */
export const pendingArguments = (channel: ReplyCliChannel): string[] => ['account', 'pending', '--channel', channel, json];

/** `account limits <id> --channel email`: read without `--daily`, set with it. */
export const limitsArguments = (id: string, daily?: number): string[] =>
    ['account', 'limits', id, '--channel', 'email', ...(daily === undefined ? [] : ['--daily', String(daily)]), json];

/** `account pause|resume <id> --channel linkedin`: Reply has neither for a mailbox. */
export const stateArguments = (verb: 'pause' | 'resume', id: string): string[] => ['account', verb, id, '--channel', 'linkedin', json];

/** `account unblock <id>`: a mailbox only, so it takes no channel. */
export const unblockArguments = (id: string): string[] => ['account', 'unblock', id, json];

/** `account delete <id>`: always `--yes`, which the CLI requires; `--force` for LinkedIn only, where the caller says so. */
export const deleteArguments = (channel: ReplyCliChannel, id: string, force: boolean): string[] =>
    ['account', 'delete', id, '--channel', channel, '--yes', ...(channel === 'linkedin' && force ? ['--force'] : []), json];

/** How one run of a verb ended, read from its exit code and its own print, never from its words. */
export type ReplyCliEnding =
    | { kind: 'done'; stdout: string }
    /** A refusal with its code: exit 1 or 2 and the code of the last JSON line on stderr; null where there was none. */
    | { kind: 'refused'; code: string | null }
    /** Exit 3: a write whose outcome the CLI does not know. */
    | { kind: 'unknown' }
    /** Any other ending: the program was not started, was stopped at its time limit, or ended as the CLI never does. */
    | { kind: 'unreached' };

/** A code as the CLI writes one, `rate_limited` or `email_link.not_available`: lower-case words, at most 64 characters. */
const codeShape = /^[a-z][a-z0-9_]*(?:\.[a-z0-9_]+)*$/;

export function replyCliEnding(result: { exit: number; stdout: string; stderr: string }): ReplyCliEnding {
    if (result.exit === 0) return { kind: 'done', stdout: result.stdout };
    if (result.exit === 3) return { kind: 'unknown' };
    if (result.exit !== 1 && result.exit !== 2) return { kind: 'unreached' };
    const last = result.stderr.split(/\r?\n/).map((line) => line.trim()).filter((line) => line !== '').pop();
    try {
        const code = (JSON.parse(last ?? '') as { error?: { code?: unknown } }).error?.code;
        return { kind: 'refused', code: typeof code === 'string' && code.length <= 64 && codeShape.test(code) ? code : null };
    } catch {
        return { kind: 'refused', code: null };
    }
}

function objectOf(stdout: string): Record<string, unknown> | null {
    try {
        const raw: unknown = JSON.parse(stdout.trim());
        return typeof raw === 'object' && raw !== null && !Array.isArray(raw) ? (raw as Record<string, unknown>) : null;
    } catch {
        return null;
    }
}

const positive = (value: unknown): number | null => (typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : null);
const text = (value: unknown): string | null => (typeof value === 'string' && value !== '' ? value : null);

/** What `account connect` and `account reconnect` print. `link` is the person's secret: it goes to the opener and nowhere else. */
export interface ReplyCliLink {
    channel: ReplyCliChannel;
    link: string;
    expiresAt: string | null;
    pendingId: number | null;
    kind: 'connect' | 'reconnect';
    linkKind: 'connection_link' | 'shared_page';
}

/**
 * `{"channel", "id"?, "link", "expires_at", "pending_id", "kind", "link_kind", "warning"?}`, or null for a print of
 * any other shape.
 */
export function readLink(stdout: string): ReplyCliLink | null {
    const raw = objectOf(stdout);
    if (raw === null) return null;
    const { channel, link, kind } = raw;
    const linkKind = raw['link_kind'];
    if ((channel !== 'email' && channel !== 'linkedin') || typeof link !== 'string' || link === '') return null;
    if ((kind !== 'connect' && kind !== 'reconnect') || (linkKind !== 'connection_link' && linkKind !== 'shared_page')) return null;
    return { channel, link, expiresAt: text(raw['expires_at']), pendingId: positive(raw['pending_id']), kind, linkKind };
}

/** One entry of `account pending`: a link made and not used yet, never with its link, which the CLI does not print. */
export interface ReplyCliPending {
    channel: ReplyCliChannel;
    id: string;
    name: string;
    kind: 'connect' | 'reconnect' | null;
    expiresAt: string | null;
    expired: boolean;
}

/** `{"items":[{"channel","id","name","kind","account_id","expires_at","expired"}],"supported","unsupported_channels"}`. */
export function readPending(stdout: string): ReplyCliPending[] | null {
    const items = objectOf(stdout)?.['items'];
    if (!Array.isArray(items)) return null;
    const rows: ReplyCliPending[] = [];
    for (const item of items) {
        if (typeof item !== 'object' || item === null) continue;
        const row = item as Record<string, unknown>;
        const id = positive(row['id']);
        const channel = row['channel'];
        if (id === null || (channel !== 'email' && channel !== 'linkedin')) continue;
        const kind = row['kind'];
        rows.push({
            channel,
            id: String(id),
            name: text(row['name']) ?? '',
            kind: kind === 'connect' || kind === 'reconnect' ? kind : null,
            expiresAt: text(row['expires_at']),
            expired: row['expired'] === true,
        });
    }
    return rows;
}

/** `account limits --channel email`: `{"channel","id","limits"}`, the mailbox's daily limit read from `limits.dailyLimit`. */
export function readDailyLimit(stdout: string): { daily: number | null } | null {
    const raw = objectOf(stdout);
    if (raw === null || !('limits' in raw)) return null;
    const limits = raw['limits'];
    const daily = typeof limits === 'object' && limits !== null ? (limits as Record<string, unknown>)['dailyLimit'] : undefined;
    return { daily: typeof daily === 'number' && Number.isSafeInteger(daily) && daily >= 0 ? daily : null };
}

/** `account pause|resume`: `{"channel","id","status","reply_status","changed"}`, status `active` or `inactive`. */
export function readState(stdout: string): { status: 'active' | 'inactive'; changed: boolean } | null {
    const raw = objectOf(stdout);
    const status = raw?.['status'];
    if (raw === null || (status !== 'active' && status !== 'inactive')) return null;
    return { status, changed: raw['changed'] === true };
}

/** `account unblock`: `{"channel":"email","id","unblocked":true}`; `account delete`: `{"channel","id","deleted":true}`. */
export function readDone(stdout: string, field: 'unblocked' | 'deleted'): boolean {
    return objectOf(stdout)?.[field] === true;
}
