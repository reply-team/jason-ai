// The chat's conversations as the desktop keeps them: one file per conversation under `<data>/desktop/conversations/`,
// written by the desktop alone. A record holds what the page drew, as text and never as markup, the campaign the
// conversation is about, and the session each agent host named for it. Never the runtime's (the runtime never knows a
// screen exists), never the harness root's, and never a host's own transcript, which stays the host's.
//
// What stands at a record's name is never followed: a record is read only from a regular file, and written beside its
// name and renamed over it, so a link at the name is replaced and what it named is left as it was. The folder is never
// followed either. A file that cannot be read as a record is left where it is, untouched, and counted.

import { randomBytes } from 'node:crypto';
import { closeSync, fsyncSync, lstatSync, mkdirSync, openSync, readdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { isFullAccessWhy, type FullAccessWhy } from './chat-full-access-why.ts';
import { currentModel, isEffort, modelShape, type Effort } from './chat-models.ts';
import { isChatMode, type ChatMode } from './chat-modes.ts';
import { givenUpTo, isJournalId, keptLineOf, type LineEntry } from './feed.ts';
import { digestShape, memoryLevels, type GivenDigests } from './chat-memory.ts';

export type KeptAnswer = 'allow' | 'always' | 'deny' | 'gone';

/**
 * One thing the page drew, kept as text: the person's words, the agent's, a step, a card and its answer, an error, and
 * the campaigns a set-up turn made for the person to pick from. The Feed's record alone also keeps its lines
 * (`feed.ts`) and the gap a catch-up left, where the lines written while the window was closed were more than one read
 * brings back.
 */
export type KeptEntry =
    | { kind: 'user'; text: string }
    /**
     * `plan` present, and true, on the last words of a Plan only turn that ended well while they are its live plan. `at`
     * the moment the desktop kept the words, ISO-8601 UTC: the moment a card they hold was asked, after which a person's
     * act on the campaign makes it a question of the past. A record an earlier desktop wrote has none.
     */
    | { kind: 'text'; text: string; plan?: true; at?: string }
    | { kind: 'step'; text: string; detail?: string }
    /** `why`, on a card of a Full access turn: why Full access asked, as a code the page has words for. */
    | { kind: 'permission'; title: string; detail: string; answer: KeptAnswer | null; why?: FullAccessWhy }
    | { kind: 'error'; text: string }
    | { kind: 'pick'; campaignIds: string[] }
    | LineEntry
    | { kind: 'gap' };

/** What the person chose for the conversation's last turn: the host, the model (null: the host's own) and the effort. */
export interface Chosen {
    host: string;
    model: string | null;
    effort: Effort | null;
}

export interface Conversation {
    /** `conv_` and 26 Crockford base-32 characters, made by the desktop. */
    id: string;
    /** The campaign it is about, or null for a set-up chat and for the Feed. */
    campaign_id: string | null;
    /** Present, and true, for the Feed: the person's main chat, one per desktop, which keeps lines and is never handed over. */
    main?: true;
    /** The Feed's alone: the journal ids it took, oldest first, at most `takenLimit`, so no line is taken twice. */
    taken?: string[];
    title: string;
    /**
     * Present, and true, when a person gave the title: nothing replaces it afterwards, not a hand-over to a campaign, not
     * the campaign's name, not a name the desktop asks a model for. Absent when the desktop named the conversation.
     */
    named_by_person?: true;
    created_at: string;
    updated_at: string;
    /** By host id, the session or thread that host named for this conversation. */
    sessions: Record<string, string>;
    /**
     * The host ids of `sessions` whose session the last turn to name it, or to answer with it, was a plain one: on the
     * person's own login of Claude Code or Codex, in any mode (`nameSession`). Absent when there is none, and in a record
     * an earlier desktop wrote, so a session no mark covers is never one to continue in a terminal.
     */
    plain_sessions?: string[];
    /**
     * By host id, the session the person's memory files were last given to and, by level, the digest of each file it was
     * given (`chat-memory.ts`): never the words. Absent until a session has been given one, and in a record an earlier
     * desktop wrote, so that session is given them again.
     */
    memory_given?: Record<string, MemoryGiven>;
    entries: KeptEntry[];
    /** How many of the oldest entries were dropped to keep the record within its bound. */
    dropped: number;
    /** What the last turn was sent with; absent until a turn is, and in a record another version wrote. */
    chosen?: Chosen;
    /**
     * The mode the person chose from the menu, or the one the Feed was made in; absent, Full access, which is never
     * written: choosing it takes the mode off the record. A record an earlier desktop wrote names Plan only or nothing.
     */
    mode?: ChatMode;
    /**
     * Present, `own`, for a conversation made by a desktop that gives each conversation a folder of its own,
     * `<data>/desktop/chats/<id>/`, where every turn of it runs. Absent, the shared chat folder, `<data>/desktop/chat/`, as
     * for every conversation an earlier desktop made, which so keeps resuming its host's session where it was made.
     */
    folder?: 'own';
}

/** What one host session was given of the memory files: the session's id, and by level the digest of each file. */
export interface MemoryGiven {
    session: string;
    files: GivenDigests;
}

/** The most a record takes on disk; the oldest entries go first to keep within it. */
export const recordLimit = 2 * 1024 * 1024;
/** The most one text of an entry takes in a record, written as JSON; a longer one keeps its end after `cutMark`. */
export const keptTextLimit = 128 * 1024;
/** What a text cut to fit its record begins with: the desktop's words, never the person's or the agent's. */
export const cutMark = '[The desktop kept only the end of this; its start was cut to keep the conversation within its size.] ';

export const conversationIdShape = /^conv_[0-9A-HJKMNP-TV-Z]{26}$/;
/** How many journal ids the Feed's record keeps of the lines it took. */
export const takenLimit = 1000;
const campaignIdShape = /^cmp_[0-9A-Za-z]{1,64}$/;
const hostIdShape = /^[a-z][a-z0-9-]{0,63}$/;
/**
 * A session or thread id a host named: it begins with a letter or a digit, so it is never read as an option where it
 * follows `--resume` or `resume` on a host's command line, whether it came from the host or from a record.
 */
export const sessionShape = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,199}$/;
const crockford = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';

/** A new conversation id: the moment in its first ten characters, then sixteen random ones. */
export function newConversationId(now = Date.now(), random: Buffer = randomBytes(10)): string {
    let time = '';
    let rest = now;
    for (let i = 0; i < 10; i++) {
        time = crockford[rest % 32] + time;
        rest = Math.floor(rest / 32);
    }
    let bits = 0n;
    for (const byte of random.subarray(0, 10)) bits = (bits << 8n) | BigInt(byte);
    let tail = '';
    for (let i = 0; i < 16; i++) {
        tail = crockford[Number(bits & 31n)] + tail;
        bits >>= 5n;
    }
    return `conv_${time}${tail}`;
}

const isText = (value: unknown): value is string => typeof value === 'string';
/** A moment as the desktop writes one, `Date.toISOString`'s: UTC to the millisecond. */
const isMoment = (value: unknown): value is string =>
    typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/.test(value) && Number.isFinite(Date.parse(value));
const knownKinds: ReadonlySet<string> = new Set<KeptEntry['kind']>(['user', 'text', 'step', 'permission', 'error', 'pick', 'line', 'gap']);

function entryOf(value: unknown): KeptEntry | null {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return null;
    const entry = value as Record<string, unknown>;
    switch (entry.kind) {
        case 'user':
        case 'error':
            return isText(entry.text) ? { kind: entry.kind, text: entry.text } : null;
        // A plan's mark is read only as true, and the moment only as one of the desktop's own; any other value is none, and
        // the words stand.
        case 'text':
            if (!isText(entry.text)) return null;
            return {
                kind: 'text',
                text: entry.text,
                ...(entry.plan === true ? { plan: true as const } : {}),
                ...(isMoment(entry.at) ? { at: entry.at } : {}),
            };
        case 'step':
            if (!isText(entry.text)) return null;
            if (entry.detail === undefined) return { kind: 'step', text: entry.text };
            return isText(entry.detail) ? { kind: 'step', text: entry.text, detail: entry.detail } : null;
        case 'permission': {
            const answer = entry.answer;
            const known = answer === null || answer === 'allow' || answer === 'always' || answer === 'deny' || answer === 'gone';
            if (!isText(entry.title) || !isText(entry.detail) || !known) return null;
            // A why that is not one the page has words for is dropped, never kept as words: the card stays without it.
            const why = isFullAccessWhy(entry.why) ? { why: { ...entry.why } } : {};
            return { kind: 'permission', title: entry.title, detail: entry.detail, answer, ...why };
        }
        case 'pick': {
            const ids = entry.campaignIds;
            if (!Array.isArray(ids) || ids.length === 0 || !ids.every((each) => isText(each) && campaignIdShape.test(each))) return null;
            return { kind: 'pick', campaignIds: [...ids] as string[] };
        }
        case 'line':
            return keptLineOf(entry);
        case 'gap':
            return { kind: 'gap' };
        default:
            return null;
    }
}

/**
 * What a record says was chosen, or undefined when it says nothing that passes the shapes: a host id, a model name the
 * desktop passes to a host or null, and one of the five efforts or null. A model a vendor retired is read as the one it
 * gives now (`currentModel`). A choice out of shape is no reason to refuse the record.
 */
function chosenOf(value: unknown): Chosen | undefined {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return undefined;
    const { host, model, effort } = value as Record<string, unknown>;
    if (!isText(host) || !hostIdShape.test(host)) return undefined;
    if (model !== null && !(isText(model) && modelShape.test(model))) return undefined;
    if (effort !== null && !isEffort(effort)) return undefined;
    return { host, model: model === null ? null : currentModel(host, model), effort };
}

/**
 * What a record says each host session was given of the memory files: only a host whose session the record keeps, under
 * that very session, with digests of their shape; anything else is nothing given, never a reason to refuse the record.
 */
function memoryGivenOf(value: unknown, sessions: Readonly<Record<string, string>>): Record<string, MemoryGiven> {
    const out: Record<string, MemoryGiven> = {};
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return out;
    for (const [host, given] of Object.entries(value as Record<string, unknown>)) {
        if (!hostIdShape.test(host) || given === null || typeof given !== 'object' || Array.isArray(given)) continue;
        const { session, files } = given as Record<string, unknown>;
        if (!isText(session) || sessions[host] !== session || files === null || typeof files !== 'object' || Array.isArray(files)) continue;
        const digests: GivenDigests = {};
        for (const level of memoryLevels) {
            const digest = (files as Record<string, unknown>)[level];
            if (isText(digest) && digestShape.test(digest)) digests[level] = digest;
        }
        out[host] = { session, files: digests };
    }
    return out;
}

/** A record read from its file, or null when the text is not one, or not the one its name says. */
export function parseConversation(text: string, id: string): Conversation | null {
    return readRecord(text, id)?.conversation ?? null;
}

/** A record read from its file, and whether it carries a shape (`recordShape`); null as `parseConversation` says. */
function readRecord(text: string, id: string): { conversation: Conversation; stamped: boolean } | null {
    let value: unknown;
    try {
        value = JSON.parse(text);
    } catch {
        return null;
    }
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return null;
    const raw = value as Record<string, unknown>;
    if (raw.id !== id || !conversationIdShape.test(id)) return null;
    if (raw.campaign_id !== null && !(isText(raw.campaign_id) && campaignIdShape.test(raw.campaign_id))) return null;
    if (!isText(raw.title) || !isText(raw.created_at) || !isText(raw.updated_at)) return null;
    if (raw.sessions === null || typeof raw.sessions !== 'object' || Array.isArray(raw.sessions)) return null;
    const sessions: Record<string, string> = {};
    for (const [host, session] of Object.entries(raw.sessions as Record<string, unknown>)) {
        if (!hostIdShape.test(host) || !isText(session)) return null;
        // A session out of shape is no session for that host: its next turn starts a fresh one, given the recap.
        if (sessionShape.test(session)) sessions[host] = session;
    }
    // The mark is read only for a host whose session is kept; anything else there is no mark, never a reason to refuse.
    const plain = Array.isArray(raw.plain_sessions) ? Object.keys(sessions).filter((host) => (raw.plain_sessions as unknown[]).includes(host)).sort() : [];
    const memory = memoryGivenOf(raw.memory_given, sessions);
    if (!Array.isArray(raw.entries)) return null;
    const main = raw.main === true && raw.campaign_id === null;
    const entries: KeptEntry[] = [];
    let skipped = 0;
    for (const each of raw.entries) {
        const entry = entryOf(each);
        // A line or a gap belongs to the Feed's record alone: anywhere else it is skipped and counted.
        if (entry !== null && (entry.kind === 'line' || entry.kind === 'gap') && !main) skipped++;
        else if (entry !== null) entries.push(entry);
        // A line out of shape, a kind a later desktop takes among them, is skipped and counted: the rest of the record stands.
        else if ((each as { kind?: unknown } | null)?.kind === 'line') skipped++;
        // An entry of a kind another version of the desktop wrote is skipped and counted, as an entry dropped is.
        else if (isText((each as { kind?: unknown } | null)?.kind) && !knownKinds.has((each as { kind: string }).kind)) skipped++;
        else return null;
    }
    const given = typeof raw.dropped === 'number' && Number.isSafeInteger(raw.dropped) && raw.dropped >= 0 ? raw.dropped : 0;
    const dropped = given + skipped;
    const chosen = chosenOf(raw.chosen);
    // A Feed record an earlier desktop wrote keeps the newest journal id a turn was given, and no mark on its lines: the
    // lines up to it are read as given. It is written back with the marks alone.
    if (main && isJournalId(raw.delivered) && !entries.some((entry) => entry.kind === 'line' && entry.given === true)) givenUpTo(entries, raw.delivered);
    const taken = main && Array.isArray(raw.taken) ? { taken: (raw.taken as unknown[]).filter(isJournalId).slice(-takenLimit) } : {};
    const read: Conversation = {
        id,
        campaign_id: raw.campaign_id as string | null,
        ...(main ? { main: true as const } : {}),
        ...taken,
        title: raw.title,
        ...(raw.named_by_person === true ? { named_by_person: true as const } : {}),
        created_at: raw.created_at,
        updated_at: raw.updated_at,
        sessions,
        ...(plain.length === 0 ? {} : { plain_sessions: plain }),
        ...(Object.keys(memory).length === 0 ? {} : { memory_given: memory }),
        entries,
        dropped,
    };
    // The mode chosen is read back; none, or Full access written by hand, is Full access. A mode this desktop does not know,
    // written by another version, is read as Ask first rather than widened to Full access.
    const moded = raw.mode === undefined || raw.mode === 'full_access' ? {} : { mode: isChatMode(raw.mode) ? raw.mode : ('ask_first' as const) };
    // A folder of its own only by the one word this desktop writes; anything else is the shared chat folder.
    const owned = raw.folder === 'own' ? { folder: 'own' as const } : {};
    // A shape of a later desktop's is a shape all the same: that desktop keeps a chat's mode too.
    const stamped = typeof raw.shape === 'number' && Number.isSafeInteger(raw.shape) && raw.shape >= recordShape;
    return { conversation: chosen === undefined ? { ...read, ...moded, ...owned } : { ...read, chosen, ...moded, ...owned }, stamped };
}

/**
 * The record as it is written: its fields in a fixed order, the Feed's mark and the person's only where they stand, the
 * mode only when one was chosen, the folder only when the conversation has one of its own, and last the record's shape.
 */
export function serialize(conversation: Conversation): string {
    const { id, campaign_id, title, created_at, updated_at, sessions, plain_sessions, memory_given, entries, dropped, chosen } = conversation;
    const main = conversation.main === true ? { main: true, ...(conversation.taken === undefined ? {} : { taken: conversation.taken }) } : {};
    const mark = conversation.named_by_person === true ? { named_by_person: true } : {};
    const moded = conversation.mode === undefined || conversation.mode === 'full_access' ? {} : { mode: conversation.mode };
    const owned = conversation.folder === 'own' ? { folder: 'own' } : {};
    const plain = plain_sessions === undefined || plain_sessions.length === 0 ? {} : { plain_sessions };
    const memory = memory_given === undefined || Object.keys(memory_given).length === 0 ? {} : { memory_given };
    return JSON.stringify({ id, campaign_id, ...main, title, ...mark, created_at, updated_at, sessions, ...plain, ...memory, entries, dropped, ...(chosen === undefined ? {} : { chosen }), ...moded, ...owned, shape: recordShape });
}

/**
 * Keeps the session a host named, or answered a resume with, and whether that turn was plain: on the person's own login
 * of Claude Code or Codex, in any mode. A turn that is not plain takes the mark away even from the same id, since it
 * continued that session where a terminal would not. Answers whether the record changed.
 */
export function nameSession(conversation: Conversation, host: string, session: string, plain: boolean): boolean {
    const marked = conversation.plain_sessions?.includes(host) === true;
    if (conversation.sessions[host] === session && marked === plain) return false;
    conversation.sessions[host] = session;
    const others = (conversation.plain_sessions ?? []).filter((each) => each !== host);
    const next = plain ? [...others, host].sort() : others;
    if (next.length === 0) delete conversation.plain_sessions;
    else conversation.plain_sessions = next;
    return true;
}

/**
 * Keeps what a host session was given of the memory files: the session's id, and the digest of each file it now holds.
 * Answers whether the record changed.
 */
export function noteMemoryGiven(conversation: Conversation, host: string, session: string, files: GivenDigests): boolean {
    const kept = conversation.memory_given?.[host];
    const same = kept !== undefined && kept.session === session && memoryLevels.every((level) => kept.files[level] === files[level]);
    if (same) return false;
    conversation.memory_given = { ...(conversation.memory_given ?? {}), [host]: { session, files: { ...files } } };
    return true;
}

/** What a host session was given of the memory files, or null when the record holds nothing for that very session. */
export function memoryGivenTo(conversation: Conversation, host: string, session: string | undefined): GivenDigests | null {
    const kept = conversation.memory_given?.[host];
    return session === undefined || kept === undefined || kept.session !== session ? null : kept.files;
}

/** The hosts whose own conversation a terminal can continue, by the id the chat keeps for each. */
export type ContinuableHost = 'claude' | 'codex';

/**
 * The session a terminal may continue this conversation by, or null: the session of the host its last turn ran on, when
 * that host is Claude Code or Codex (a key provider's host id is neither) and a plain turn named it.
 */
export function continuableOf(conversation: Conversation): { host: ContinuableHost; session: string } | null {
    const host = conversation.chosen?.host;
    if (host !== 'claude' && host !== 'codex') return null;
    const session = conversation.sessions[host];
    if (session === undefined || conversation.plain_sessions?.includes(host) !== true) return null;
    return { host, session };
}

const bytes = (value: string) => Buffer.byteLength(value, 'utf8');

/** A text within `keptTextLimit` as JSON: as it is, or its longest end that fits after `cutMark`. */
function fitted(text: string): string {
    if (bytes(JSON.stringify(text)) <= keptTextLimit) return text;
    const characters = Array.from(text);
    const fits = (count: number) => bytes(JSON.stringify(cutMark + characters.slice(characters.length - count).join(''))) <= keptTextLimit;
    // The most characters of the end that fit: none always does, all never does.
    let low = 0;
    let high = characters.length;
    while (high - low > 1) {
        const middle = Math.floor((low + high) / 2);
        if (fits(middle)) low = middle;
        else high = middle;
    }
    return cutMark + characters.slice(characters.length - low).join('');
}

/** An entry whose every text is within `keptTextLimit`; the same entry when none was over. */
function fittedEntry(entry: KeptEntry): KeptEntry {
    switch (entry.kind) {
        case 'user':
        case 'error': {
            const text = fitted(entry.text);
            return text === entry.text ? entry : { kind: entry.kind, text };
        }
        case 'text': {
            // A plan cut to fit is no plan: the person would approve words they cannot read whole. The words keep their
            // moment: a card at their end, which a cut keeps, was asked then all the same.
            const text = fitted(entry.text);
            if (text === entry.text) return entry;
            return entry.at === undefined ? { kind: 'text', text } : { kind: 'text', text, at: entry.at };
        }
        case 'step': {
            const text = fitted(entry.text);
            const detail = entry.detail === undefined ? undefined : fitted(entry.detail);
            if (text === entry.text && detail === entry.detail) return entry;
            return detail === undefined ? { kind: 'step', text } : { kind: 'step', text, detail };
        }
        case 'permission': {
            const title = fitted(entry.title);
            const detail = fitted(entry.detail);
            return title === entry.title && detail === entry.detail ? entry : { kind: 'permission', title, detail, answer: entry.answer, ...(entry.why === undefined ? {} : { why: entry.why }) };
        }
        case 'pick':
        case 'line':
        case 'gap':
            return entry;
    }
}

/**
 * The record within its bound. Every text of an entry is first cut to `keptTextLimit`, keeping its end, so no one entry
 * can take the record; then the oldest entries are dropped, one at a time, until what is written is at most `limit`
 * bytes, and counted in `dropped`. The newest entry always stays.
 */
export function bounded(conversation: Conversation, limit = recordLimit): Conversation {
    const entries = conversation.entries.map(fittedEntry);
    let next = entries.every((entry, i) => entry === conversation.entries[i]) ? conversation : { ...conversation, entries };
    let over = bytes(serialize(next)) - limit;
    while (over > 0 && next.entries.length > 1) {
        // Each entry takes its own bytes and a comma; the count's digits may grow by a few, which the next measure catches.
        let count = 0;
        let freed = 0;
        while (count < next.entries.length - 1 && freed < over) freed += bytes(JSON.stringify(next.entries[count++])) + 1;
        count = Math.max(count, 1);
        next = { ...next, entries: next.entries.slice(count), dropped: next.dropped + count };
        over = bytes(serialize(next)) - limit;
    }
    return next;
}

/** A record that could not be written: the system's error code, never the error's text or a path. */
export class ConversationUnwritten extends Error {
    readonly code: string;

    constructor(code: unknown) {
        super('The conversation could not be kept on this computer.');
        this.name = 'ConversationUnwritten';
        this.code = typeof code === 'string' && /^E[A-Z]+$/.test(code) ? code : 'unknown';
    }
}

/** Makes the folder when it is not there, and refuses a name that is not a folder itself, a link to one included. */
function ownFolder(folder: string): void {
    let isFolder: boolean;
    try {
        mkdirSync(folder, { recursive: true, mode: 0o700 });
        isFolder = lstatSync(folder).isDirectory();
    } catch (error) {
        throw new ConversationUnwritten((error as NodeJS.ErrnoException).code);
    }
    if (!isFolder) throw new ConversationUnwritten('ENOTDIR');
}

/**
 * The shape of the records this desktop writes, on every one it writes. An earlier desktop writes a record again with only
 * the fields it knows, and drops this one with the rest, the mode but Plan only and the folder among them: once every
 * record was stamped (`DesktopPaths.conversationsStamped`), a record without it is one an earlier desktop wrote last.
 */
export const recordShape = 1;

/**
 * The records in the folder, how many files there could not be read as one, whether the folder could be listed at all
 * (one that is not there, is no folder or is a link is not, and then nothing is known of what it would hold), and the ids
 * of the records read that carry no shape (`recordShape`). Nothing there is changed.
 */
export function readConversations(folder: string): { kept: Conversation[]; unreadable: number; listed: boolean; unstamped: string[] } {
    let names: string[];
    try {
        if (!lstatSync(folder).isDirectory()) return { kept: [], unreadable: 0, listed: false, unstamped: [] };
        names = readdirSync(folder);
    } catch {
        return { kept: [], unreadable: 0, listed: false, unstamped: [] };
    }
    const kept: Conversation[] = [];
    const unstamped: string[] = [];
    let unreadable = 0;
    for (const name of names) {
        if (!name.endsWith('.json')) continue;
        const id = name.slice(0, -'.json'.length);
        const file = join(folder, name);
        let record: { conversation: Conversation; stamped: boolean } | null = null;
        try {
            const stat = lstatSync(file);
            if (stat.isFile() && stat.size <= recordLimit * 2) record = readRecord(readFileSync(file, 'utf8'), id);
        } catch {
            record = null;
        }
        if (record === null) unreadable++;
        else {
            kept.push(record.conversation);
            if (!record.stamped) unstamped.push(id);
        }
    }
    return { kept, unreadable, listed: true, unstamped };
}

/** Writes the record to a new file beside its name, readable by this account alone, and renames it over the name. */
export function writeConversation(folder: string, conversation: Conversation): void {
    if (!conversationIdShape.test(conversation.id)) throw new ConversationUnwritten('EINVAL');
    ownFolder(folder);
    const temporary = join(folder, `.${conversation.id}.${randomBytes(6).toString('hex')}.tmp`);
    try {
        const handle = openSync(temporary, 'wx', 0o600);
        try {
            writeFileSync(handle, serialize(conversation), 'utf8');
            fsyncSync(handle);
        } finally {
            closeSync(handle);
        }
        renameSync(temporary, join(folder, `${conversation.id}.json`));
    } catch (error) {
        try {
            rmSync(temporary, { force: true });
        } catch {
            // A file that cannot be removed now is never read: its name is no record's.
        }
        throw new ConversationUnwritten((error as NodeJS.ErrnoException).code);
    }
}

/** Removes the record's file, itself and never what a link there names; one already gone is nothing to remove. */
export function removeConversation(folder: string, id: string): void {
    if (!conversationIdShape.test(id)) return;
    try {
        if (!lstatSync(folder).isDirectory()) return;
        rmSync(join(folder, `${id}.json`), { force: true });
    } catch (error) {
        throw new ConversationUnwritten((error as NodeJS.ErrnoException).code);
    }
}
