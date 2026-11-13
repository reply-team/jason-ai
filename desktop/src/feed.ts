// Pure: the Feed's lines. The Feed is the person's main chat, a conversation of its own kind, into which the desktop puts
// a line for each journal line of a closed table of kinds: a reply observed, an approval or a question waiting, a
// campaign created, started, paused or archived, an approval given or declined, a question answered, and work that
// stopped; and, of the work that succeeded, a send that went out and the work of five roles. A line keeps identifiers,
// the kind, the moment and a count, and for a succeeded work item the operation or the role from a closed list,
// and never a person's text: no reason, no old or new value, no campaign or contact name. Lines of the same kind,
// campaign, operation and role that follow one another fold into one (sends that alternate with other lines are shown one
// by one), at most `feedLimit` are kept, and a turn of the Feed is given the lines no turn was given yet, set apart as records (`feedBlock` in `chat-header.ts`): each kept line is marked once
// it was given, on the record, so the order the runtime commits lines in decides nothing. A line never starts a turn.

import type { KeptEntry } from './chat-conversations.ts';

/** The kinds the Feed takes, each one the runtime writes itself (a test reads them from the runtime's source). */
export const feedKinds = [
    'inbound_reply_observed',
    'approval_requested',
    'decision_raised',
    'campaign_created',
    'campaign_started',
    'campaign_paused',
    'workitem_failed',
    'workitem_expired',
    'provider_blocked',
    'approval_approved',
    'approval_rejected',
    'decision_answered',
    'campaign_archived',
] as const;

/**
 * The kind of a work item that succeeded. It is not one of `feedKinds`: a campaign's reads succeed every cadence, so
 * "every succeeded item" is never taken. A line of it is kept only for an operation or a role of the lists below, and
 * its catch-up is a request of its own (`chat.ts`).
 */
export const succeededKind = 'workitem_succeeded';

export type FeedKind = (typeof feedKinds)[number] | typeof succeededKind;

/** The operations of the catalog whose success is a send that went out. */
export const feedOperations = ['message.send', 'conversation.reply', 'social_invitation.send'] as const;
export type FeedOperation = (typeof feedOperations)[number];

/** The roles Jason ships, a copy of `ShippedRoles.Names` (a test reads the runtime's source). A registered role is none of them. */
export const shippedRoles = [
    'manager',
    'planner',
    'researcher',
    'copywriter',
    'personalizer',
    'critic',
    'responder',
    'analyst',
    'deliverability-specialist',
] as const;
export type ShippedRole = (typeof shippedRoles)[number];

/**
 * The roles whose finished work the Feed shows: a second closed list beside the shipped roles. Left out, on purpose:
 * the manager and the responder, whose results already surface as approvals, questions and sends; and the personalizer
 * and the critic, which work message by message, so that their lines, with the role in the folding key, would alternate
 * and never fold and would fill the kept lines of a campaign of some hundreds of people. A role a person registered
 * is in no list, so its work is never kept and its name never reaches a line or a turn.
 */
export const feedRoles = ['planner', 'researcher', 'copywriter', 'analyst', 'deliverability-specialist'] as const satisfies readonly ShippedRole[];
export type FeedRole = (typeof feedRoles)[number];

/** One line of the Feed, as the record keeps it and the page draws it. */
export interface LineEntry {
    kind: 'line';
    journal_kind: FeedKind;
    /** The newest journal line it stands for: `jrn_` and its ULID. */
    journal_id: string;
    /** That line's moment, ISO-8601 UTC. */
    at: string;
    campaign_id: string;
    /** The work item the line names, where it names one. */
    work_item_id?: string;
    /** On a `workitem_succeeded` line, the operation of a send that went out; else absent. Exactly one of it and `role`. */
    operation?: FeedOperation;
    /** On a `workitem_succeeded` line, the shipped role whose work finished; else absent. */
    role?: FeedRole;
    /** How many journal lines it stands for: more than one when lines of its kind, campaign, operation and role followed one another. */
    count: number;
    /**
     * Present, and true, once a turn whose host started was given the line with this count. A line is kept without it,
     * and a fold that raises the count makes a line without it, so the raised count is given too.
     */
    given?: true;
}

/** The Feed's title, which nothing replaces. */
export const feedTitle = 'Feed';
/** The most lines (and gaps) the Feed keeps; the oldest go first. */
export const feedLimit = 500;
/** The most lines a turn of the Feed is given; the older ones are counted. */
export const headerLimit = 50;

const crockford = '[0-9A-HJKMNP-TV-Z]{26}';
const journalIdShape = new RegExp(`^jrn_${crockford}$`);
const campaignIdShape = new RegExp(`^cmp_${crockford}$`);
const workItemIdShape = new RegExp(`^wi_${crockford}$`);
const atShape = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$/;
const kinds: ReadonlySet<string> = new Set<string>([...feedKinds, succeededKind]);
const operations: ReadonlySet<string> = new Set(feedOperations);
const roles: ReadonlySet<string> = new Set(feedRoles);

const isKind = (value: unknown): value is FeedKind => typeof value === 'string' && kinds.has(value);
const isOperation = (value: unknown): value is FeedOperation => typeof value === 'string' && operations.has(value);
const isRole = (value: unknown): value is FeedRole => typeof value === 'string' && roles.has(value);
const absent = (value: unknown) => value === null || value === undefined;

/**
 * What a succeeded work item says of itself that the Feed keeps: its operation if it names one and that is on the list,
 * or its role if that is on the list, else nothing (null: the line is none of the Feed's). A name outside the lists is
 * never kept, and one naming both is refused.
 */
function whoOf(operation: unknown, role: unknown): { operation: FeedOperation } | { role: FeedRole } | null {
    // The runtime makes no item with both; a line naming both is out of shape, and neither name is kept.
    if (!absent(operation) && !absent(role)) return null;
    if (!absent(operation)) return isOperation(operation) ? { operation } : null;
    return isRole(role) ? { role } : null;
}
const shaped = (value: unknown, shape: RegExp): value is string => typeof value === 'string' && shape.test(value);

function made(
    kind: FeedKind,
    id: string,
    at: string,
    campaign: string,
    workItem: unknown,
    count: number,
    who: { operation?: FeedOperation; role?: FeedRole } = {},
): LineEntry {
    const line: LineEntry = { kind: 'line', journal_kind: kind, journal_id: id, at, campaign_id: campaign, count };
    if (typeof workItem === 'string') line.work_item_id = workItem;
    if (who.operation !== undefined) line.operation = who.operation;
    if (who.role !== undefined) line.role = who.role;
    return line;
}

/**
 * A journal line made a Feed line, as the page hands it or as `jason journal list` answers it: its kind from the table,
 * its id, moment, campaign and work item held to their shapes, and nothing else of it. Null for a line of another kind,
 * one about no campaign, and anything out of shape.
 */
export function lineOf(value: unknown): LineEntry | null {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return null;
    const { id, ts, kind, campaign_id: campaign, work_item_id: workItem, work_item_operation: operation, work_item_role: role } = value as Record<string, unknown>;
    if (!isKind(kind) || !shaped(id, journalIdShape) || !shaped(ts, atShape) || !shaped(campaign, campaignIdShape)) return null;
    if (workItem !== null && workItem !== undefined && !shaped(workItem, workItemIdShape)) return null;
    // Only a succeeded work item carries who it was; for any other kind those members are not read at all.
    if (kind !== succeededKind) return made(kind, id, ts, campaign, workItem, 1);
    const who = whoOf(operation, role);
    return who === null ? null : made(kind, id, ts, campaign, workItem, 1, who);
}

/** A line read back from a record: every field in shape and a count of at least one; null otherwise. */
export function keptLineOf(value: unknown): LineEntry | null {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return null;
    const entry = value as Record<string, unknown>;
    const { journal_kind: kind, journal_id: id, at, campaign_id: campaign, work_item_id: workItem, count, operation, role } = entry;
    if (entry.kind !== 'line' || !isKind(kind) || !shaped(id, journalIdShape) || !shaped(at, atShape) || !shaped(campaign, campaignIdShape)) return null;
    if (workItem !== undefined && !shaped(workItem, workItemIdShape)) return null;
    if (typeof count !== 'number' || !Number.isSafeInteger(count) || count < 1) return null;
    let who: { operation?: FeedOperation; role?: FeedRole } = {};
    if (kind === succeededKind) {
        // A record of this kind names exactly one of the two, from the lists; a hand-edited one naming both, or a name
        // outside them, is refused. Of any other kind the members are not read.
        const named = whoOf(operation, role);
        if (named === null) return null;
        who = named;
    }
    const line = made(kind, id, at, campaign, workItem, count, who);
    // Anything but true is a line not given yet: given again at worst, never left out.
    if (entry.given === true) line.given = true;
    return line;
}

/**
 * Puts a line at the end of the entries: folded into the last entry when that is a line of the same kind, campaign,
 * operation and role, which then takes the newest id, moment and work item and counts one more; else as an entry of its
 * own.
 */
export function take(entries: KeptEntry[], line: LineEntry): { line: LineEntry; folded: boolean } {
    const last = entries.at(-1);
    // The operation or the role is part of the key: a send and a finished piece of research never fold into one.
    if (last?.kind === 'line' && last.journal_kind === line.journal_kind && last.campaign_id === line.campaign_id && last.operation === line.operation && last.role === line.role) {
        const next = made(line.journal_kind, line.journal_id, line.at, line.campaign_id, line.work_item_id, last.count + line.count, line);
        entries[entries.length - 1] = next;
        return { line: next, folded: true };
    }
    entries.push(line);
    return { line, folded: false };
}

/** Drops the oldest lines and gaps past `limit`, every message kept; answers how many went. */
export function trimmed(entries: KeptEntry[], limit = feedLimit): number {
    let over = entries.filter((each) => each.kind === 'line' || each.kind === 'gap').length - limit;
    if (over <= 0) return 0;
    const gone = over;
    for (let i = 0; i < entries.length && over > 0; ) {
        const kind = entries[i]?.kind;
        if (kind === 'line' || kind === 'gap') {
            entries.splice(i, 1);
            over--;
        } else i++;
    }
    return gone;
}

/**
 * The lines no turn was given yet, in the order they stand, wherever they stand and whatever their journal ids: the
 * newest `limit` of them, how many older ones are left out, and all of them, which the turn marks given once its host
 * started (`markGiven`), those left out included, as they are counted to it.
 */
export function linesWaiting(entries: readonly KeptEntry[], limit = headerLimit): { lines: LineEntry[]; more: number; waiting: LineEntry[] } {
    const waiting = entries.filter((entry): entry is LineEntry => entry.kind === 'line' && entry.given !== true);
    const more = Math.max(0, waiting.length - limit);
    return { lines: waiting.slice(more), more, waiting };
}

/**
 * Marks given each line still kept as it was handed: the same journal id and count. A line folded since it was handed
 * stands for more lines under another id and count, and waits for the next turn; one dropped since is gone.
 */
export function markGiven(entries: readonly KeptEntry[], handed: readonly LineEntry[]): void {
    const keys = new Set(handed.map((line) => `${line.journal_id}/${line.count}`));
    for (const entry of entries) if (entry.kind === 'line' && keys.has(`${entry.journal_id}/${entry.count}`)) entry.given = true;
}

/**
 * The lines of a Feed record an earlier desktop wrote, which kept the newest journal id a turn was given instead of a mark
 * on each line: those whose id is not newer than it are marked given, the rest wait.
 */
export function givenUpTo(entries: readonly KeptEntry[], delivered: string): void {
    for (const entry of entries) if (entry.kind === 'line' && entry.journal_id <= delivered) entry.given = true;
}

/** Whether a value is a journal id, `jrn_` and its ULID. */
export const isJournalId = (value: unknown): value is string => shaped(value, journalIdShape);
