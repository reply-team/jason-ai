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

export type KeptAnswer = 'allow' | 'always' | 'deny' | 'gone';

/**
 * One thing the page drew, kept as text: the person's words, the agent's, a step, a card and its answer, an error, and
 * the campaigns a set-up turn made for the person to pick from.
 */
export type KeptEntry =
    | { kind: 'user'; text: string }
    | { kind: 'text'; text: string }
    | { kind: 'step'; text: string; detail?: string }
    | { kind: 'permission'; title: string; detail: string; answer: KeptAnswer | null }
    | { kind: 'error'; text: string }
    | { kind: 'pick'; campaignIds: string[] };

export interface Conversation {
    /** `conv_` and 26 Crockford base-32 characters, made by the desktop. */
    id: string;
    /** The campaign it is about, or null for a set-up chat. */
    campaign_id: string | null;
    title: string;
    created_at: string;
    updated_at: string;
    /** By host id, the session or thread that host named for this conversation. */
    sessions: Record<string, string>;
    entries: KeptEntry[];
    /** How many of the oldest entries were dropped to keep the record within its bound. */
    dropped: number;
}

/** The most a record takes on disk; the oldest entries go first to keep within it. */
export const recordLimit = 2 * 1024 * 1024;

export const conversationIdShape = /^conv_[0-9A-HJKMNP-TV-Z]{26}$/;
const campaignIdShape = /^cmp_[0-9A-Za-z]{1,64}$/;
const hostIdShape = /^[a-z][a-z0-9-]{0,63}$/;
const sessionShape = /^[A-Za-z0-9._:-]{1,200}$/;
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

function entryOf(value: unknown): KeptEntry | null {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return null;
    const entry = value as Record<string, unknown>;
    switch (entry.kind) {
        case 'user':
        case 'text':
        case 'error':
            return isText(entry.text) ? { kind: entry.kind, text: entry.text } : null;
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
        default:
            return null;
    }
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
        if (!hostIdShape.test(host) || !isText(session) || !sessionShape.test(session)) return null;
        sessions[host] = session;
    }
    if (!Array.isArray(raw.entries)) return null;
    const entries: KeptEntry[] = [];
    for (const each of raw.entries) {
        const entry = entryOf(each);
        if (entry === null) return null;
        entries.push(entry);
    }
    const dropped = typeof raw.dropped === 'number' && Number.isSafeInteger(raw.dropped) && raw.dropped >= 0 ? raw.dropped : 0;
    return { id, campaign_id: raw.campaign_id as string | null, title: raw.title, created_at: raw.created_at, updated_at: raw.updated_at, sessions, entries, dropped };
}

/** The record as it is written: its fields in a fixed order. */
export function serialize(conversation: Conversation): string {
    const { id, campaign_id, title, created_at, updated_at, sessions, entries, dropped } = conversation;
    return JSON.stringify({ id, campaign_id, title, created_at, updated_at, sessions, entries, dropped });
}

/**
 * The record within its bound: the oldest entries dropped, one at a time, until what is written is at most `limit`
 * bytes, and counted in `dropped`. An entry larger than the bound by itself is dropped like any other.
 */
export function bounded(conversation: Conversation, limit = recordLimit): Conversation {
    const bytes = (value: string) => Buffer.byteLength(value, 'utf8');
    let next = conversation;
    let over = bytes(serialize(next)) - limit;
    while (over > 0 && next.entries.length > 0) {
        // Each entry takes its own bytes and a comma; the count's digits may grow by a few, which the next measure catches.
        let count = 0;
        let freed = 0;
        while (count < next.entries.length && freed < over) freed += bytes(JSON.stringify(next.entries[count++])) + 1;
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
