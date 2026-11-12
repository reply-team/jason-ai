// What the Reply CLI prints for its account verbs under --json, as its own tests pin it at the release tag v0.21.0
// (commit f8a6a28c) of the public reply-cli repository: src/__tests__/operations/account-links.test.ts and account-manage.test.ts (the results, each by
// `toStrictEqual`), src/utils/errors.ts (an error's line on stderr) and src/operations/shared-page.ts (the warning). The
// desktop reads these through src/reply-cli-account.ts alone, so the published CLI is checked against this one file.
// Every link, token, id and name here is made up.

const app = 'https://app.example.test';
const token = '00made0up0token0for0tests0only0000000000000000';

/** The links the stand-in prints; no test holds a real one. */
export const fixtureLinks = {
    emailConnect: `${app}/email-connect/${token}`,
    linkedinConnect: `${app}/linkedin-connect/${token}`,
    sharedGmail: `${app}/hosted/eac/ConnectWithGMail?uidToken=${token}`,
    sharedIndex: `${app}/hosted/eac/index/${token}`,
    token,
};

export const sharedPageWarning = "this address does not expire and lets its holder manage all of this user's mailboxes, deletion included";

/** The verbs' results, in the CLI's field order. */
export const printed = {
    connectEmail: { channel: 'email', link: fixtureLinks.emailConnect, expires_at: '2026-11-18T12:00:00Z', pending_id: 50, kind: 'connect', link_kind: 'connection_link' },
    connectEmailShared: {
        channel: 'email',
        link: fixtureLinks.sharedGmail,
        expires_at: null,
        pending_id: null,
        kind: 'connect',
        link_kind: 'shared_page',
        warning: sharedPageWarning,
    },
    connectLinkedIn: { channel: 'linkedin', link: fixtureLinks.linkedinConnect, expires_at: null, pending_id: 51, kind: 'connect', link_kind: 'connection_link' },
    reconnectEmail: {
        channel: 'email',
        id: 4294967296,
        link: fixtureLinks.emailConnect,
        expires_at: '2026-11-18T12:00:00Z',
        pending_id: 52,
        kind: 'reconnect',
        link_kind: 'connection_link',
    },
    reconnectEmailShared: {
        channel: 'email',
        id: 12,
        link: fixtureLinks.sharedIndex,
        expires_at: null,
        pending_id: null,
        kind: 'reconnect',
        link_kind: 'shared_page',
        warning: sharedPageWarning,
    },
    reconnectLinkedIn: { channel: 'linkedin', id: 34, link: fixtureLinks.linkedinConnect, expires_at: null, pending_id: null, kind: 'reconnect', link_kind: 'connection_link' },
    pending: {
        items: [
            { channel: 'email', id: 50, name: 'Made-up mailbox', kind: 'connect', account_id: null, expires_at: '2026-11-18T12:00:00Z', expired: false },
            { channel: 'email', id: 52, name: null, kind: 'reconnect', account_id: 4294967296, expires_at: '2026-11-01T12:00:00Z', expired: true },
            { channel: 'linkedin', id: 7, name: 'Made-up LinkedIn', kind: 'connect', account_id: null, expires_at: '2026-11-19T00:00:00Z', expired: false },
        ],
        supported: true,
        unsupported_channels: [],
    },
    pendingUnsupported: { items: [], supported: false, unsupported_channels: ['email'] },
    limitsEmail: {
        channel: 'email',
        id: 12,
        limits: {
            dailyLimit: 500,
            isEmailsThrottlingEnabled: false,
            emailsPerInterval: null,
            emailsThrottlingSecondsInterval: null,
            isSendingDelayEnabled: true,
            maxSendingDelaySeconds: 120,
            minSendingDelaySeconds: 60,
        },
    },
    pause: { channel: 'linkedin', id: 34, status: 'inactive', reply_status: 'disabled', changed: true },
    pauseUnchanged: { channel: 'linkedin', id: 34, status: 'inactive', reply_status: 'disabled', changed: false },
    resume: { channel: 'linkedin', id: 34, status: 'active', reply_status: 'enabled', changed: true },
    unblock: { channel: 'email', id: 4294967296, unblocked: true },
    deleteEmail: { channel: 'email', id: 12, deleted: true },
    deleteLinkedIn: { channel: 'linkedin', id: 34, deleted: true },
} as const;

/**
 * The line a refusal ends with on stderr, `{"error":{...}}` as `CliError.to_json` builds it (status, code, title, detail,
 * hint, reply_code, ...), with the exit code the CLI gives it.
 */
export function refusalLine(code: string, extra: Record<string, unknown> = {}): string {
    return JSON.stringify({ error: { status: 409, code, title: 'Words of the CLI that the desktop never shows.', ...extra } });
}

/** The codes the CLI's account verbs can end with at that tag, by where they come from. */
export const cliCodes = {
    account: [
        'email_link.not_available',
        'email_link.provider_required',
        'shared_page.not_enabled',
        'email_account.not_found',
        'email_account.limit_reached',
        'email_account.no_free_mailbox_slots',
        'email_account.reconnect_not_supported',
        'linkedin.account_not_found',
        'linkedin.invalid_cookie',
        'linkedin.account_limit_reached',
        'linkedin.account_in_use',
        'account.status_unknown',
        'account.status_changed',
        'account.pause_unavailable',
    ],
    common: ['access.denied', 'access.feature_unavailable', 'rate_limited', 'reply.unavailable', 'reply.refused', 'reply.invalid_request', 'outcome.unknown'],
    usage: ['usage.channel', 'usage.name', 'usage.provider', 'usage.choice', 'usage.id', 'usage.limits', 'usage.range', 'usage.unsupported', 'usage.force', 'usage.confirm'],
} as const;
