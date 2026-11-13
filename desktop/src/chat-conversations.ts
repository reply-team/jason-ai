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
import { currentModel, isEffort, modelShape, type Effort } from './chat-models.ts';
import { givenUpTo, isJournalId, keptLineOf, type LineEntry } from './feed.ts';

export type KeptAnswer = 'allow' | 'always' | 'deny' | 'gone';

/**
 * One thing the page drew, kept as text: the person's words, the agent's, a step, a card and its answer, an error, and
 * the campaigns a set-up turn made for the person to pick from. The Feed's record alone also keeps its lines
 * (`feed.ts`) and the gap a catch-up left, where the lines written while the window was closed were more than one read
 * brings back.
 */
export type KeptEntry =
    | { kind: 'user'; text: string }
    /** `plan` present, and true, on the last words of a Plan only turn that ended well while they are its live plan. */
    | { kind: 'text'; text: string; plan?: true }
    | { kind: 'step'; text: string; detail?: string }
    | { kind: 'permission'; title: string; detail: string; answer: KeptAnswer | null }
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
    entries: KeptEntry[];
    /** How many of the oldest entries were dropped to keep the record within its bound. */
    dropped: number;
    /** What the last turn was sent with; absent until a turn is, and in a record another version wrote. */
    chosen?: Chosen;
    /**
     * Present when the person chose Plan only for the conversation; absent, Ask first. Full access is never written: it is
     * held in memory alone, so a record never opens a chat in it.
     */
    mode?: 'plan_only';
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
const knownKinds: ReadonlySet<string> = new Set<KeptEntry['kind']>(['user', 'text', 'step', 'permission', 'error', 'pick', 'line', 'gap']);

function entryOf(value: unknown): KeptEntry | null {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return null;
    const entry = value as Record<string, unknown>;
    switch (entry.kind) {
        case 'user':
        case 'error':
            return isText(entry.text) ? { kind: entry.kind, text: entry.text } : null;
        // A plan's mark is read only as true; any other value is no mark, and the words stand.
        case 'text':
            if (!isText(entry.text)) return null;
            return entry.plan === true ? { kind: 'text', text: entry.text, plan: true } : { kind: 'text', text: entry.text };
        case 'step':
            if (!isText(entry.text)) return null;
            if (entry.detail === undefined) return { kind: 'step', text: entry.text };
            return isText(entry.detail) ? { kind: 'step', text: entry.text, detail: entry.detail } : null;
        case 'permission': {
            const answer = entry.answer;
            const known = answer === null || answer === 'allow' || answer === 'always' || answer === 'deny' || answer === 'gone';
            return isText(entry.title) && isText(entry.detail) && known ? { kind: 'permission', title: entry.title, detail: entry.detail, answer } : null;
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

/** A record read from its file, or null when the text is not one, or not the one its name says. */
export function parseConversation(text: string, id: string): Conversation | null {
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
        entries,
        dropped,
    };
    // Only Plan only is read from a record; anything else there, Full access included, is Ask first.
    const planned = raw.mode === 'plan_only' ? { mode: 'plan_only' as const } : {};
    return chosen === undefined ? { ...read, ...planned } : { ...read, chosen, ...planned };
}

/**
 * The record as it is written: its fields in a fixed order, the Feed's mark and the person's only where they stand, and
 * the mode only when it is Plan only.
 */
export function serialize(conversation: Conversation): string {
    const { id, campaign_id, title, created_at, updated_at, sessions, entries, dropped, chosen } = conversation;
    const main = conversation.main === true ? { main: true, ...(conversation.taken === undefined ? {} : { taken: conversation.taken }) } : {};
    const mark = conversation.named_by_person === true ? { named_by_person: true } : {};
    const planned = conversation.mode === 'plan_only' ? { mode: 'plan_only' } : {};
    return JSON.stringify({ id, campaign_id, ...main, title, ...mark, created_at, updated_at, sessions, entries, dropped, ...(chosen === undefined ? {} : { chosen }), ...planned });
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
            const text = fitted(entry.text);
            if (text === entry.text) return entry;
            return entry.plan === true ? { kind: 'text', text, plan: true } : { kind: 'text', text };
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
            return title === entry.title && detail === entry.detail ? entry : { kind: 'permission', title, detail, answer: entry.answer };
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

/** The records in the folder, and how many files there could not be read as one; nothing there is changed. */
export function readConversations(folder: string): { kept: Conversation[]; unreadable: number } {
    let names: string[];
    try {
        if (!lstatSync(folder).isDirectory()) return { kept: [], unreadable: 0 };
        names = readdirSync(folder);
    } catch {
        return { kept: [], unreadable: 0 };
    }
    const kept: Conversation[] = [];
    let unreadable = 0;
    for (const name of names) {
        if (!name.endsWith('.json')) continue;
        const id = name.slice(0, -'.json'.length);
        const file = join(folder, name);
        let record: Conversation | null = null;
        try {
            const stat = lstatSync(file);
            if (stat.isFile() && stat.size <= recordLimit * 2) record = parseConversation(readFileSync(file, 'utf8'), id);
        } catch {
            record = null;
        }
        if (record === null) unreadable++;
        else kept.push(record);
    }
    return { kept, unreadable };
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
