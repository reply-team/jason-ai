// Pure: the Feed's lines. The Feed is the person's main chat, a conversation of its own kind, into which the desktop puts
// a line for each journal line of a closed table of kinds: a reply observed, an approval or a question waiting, a
// campaign created, started or paused, and work that stopped. A line keeps identifiers, the kind, the moment and a count,
// and never a person's text: no reason, no old or new value, no campaign or contact name. Lines of the same kind and
// campaign that follow one another fold into one, at most `feedLimit` are kept, and a turn of the Feed is given the
// lines no turn was given yet, set apart as records (`feedBlock` in `chat-header.ts`): each kept line is marked once
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
] as const;

export type FeedKind = (typeof feedKinds)[number];

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
    /** How many journal lines it stands for: more than one when lines of its kind and campaign followed one another. */
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
const kinds: ReadonlySet<string> = new Set(feedKinds);

const isKind = (value: unknown): value is FeedKind => typeof value === 'string' && kinds.has(value);
const shaped = (value: unknown, shape: RegExp): value is string => typeof value === 'string' && shape.test(value);

function made(kind: FeedKind, id: string, at: string, campaign: string, workItem: unknown, count: number): LineEntry {
    const line: LineEntry = { kind: 'line', journal_kind: kind, journal_id: id, at, campaign_id: campaign, count };
    if (typeof workItem === 'string') line.work_item_id = workItem;
    return line;
}

/**
 * A journal line made a Feed line, as the page hands it or as `jason journal list` answers it: its kind from the table,
 * its id, moment, campaign and work item held to their shapes, and nothing else of it. Null for a line of another kind,
 * one about no campaign, and anything out of shape.
 */
export function lineOf(value: unknown): LineEntry | null {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return null;
    const { id, ts, kind, campaign_id: campaign, work_item_id: workItem } = value as Record<string, unknown>;
    if (!isKind(kind) || !shaped(id, journalIdShape) || !shaped(ts, atShape) || !shaped(campaign, campaignIdShape)) return null;
    if (workItem !== null && workItem !== undefined && !shaped(workItem, workItemIdShape)) return null;
    return made(kind, id, ts, campaign, workItem, 1);
}

/** A line read back from a record: every field in shape and a count of at least one; null otherwise. */
export function keptLineOf(value: unknown): LineEntry | null {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return null;
    const entry = value as Record<string, unknown>;
    const { journal_kind: kind, journal_id: id, at, campaign_id: campaign, work_item_id: workItem, count } = entry;
    if (entry.kind !== 'line' || !isKind(kind) || !shaped(id, journalIdShape) || !shaped(at, atShape) || !shaped(campaign, campaignIdShape)) return null;
    if (workItem !== undefined && !shaped(workItem, workItemIdShape)) return null;
    if (typeof count !== 'number' || !Number.isSafeInteger(count) || count < 1) return null;
    const line = made(kind, id, at, campaign, workItem, count);
    // Anything but true is a line not given yet: given again at worst, never left out.
    if (entry.given === true) line.given = true;
    return line;
}

/**
 * Puts a line at the end of the entries: folded into the last entry when that is a line of the same kind and campaign,
 * which then takes the newest id, moment and work item and counts one more; else as an entry of its own.
 */
export function take(entries: KeptEntry[], line: LineEntry): { line: LineEntry; folded: boolean } {
    const last = entries.at(-1);
    if (last?.kind === 'line' && last.journal_kind === line.journal_kind && last.campaign_id === line.campaign_id) {
        const next = made(line.journal_kind, line.journal_id, line.at, line.campaign_id, line.work_item_id, last.count + line.count);
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
