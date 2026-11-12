/**
 * The standing choice of Full access, `<data>/desktop/full-access.json`: one entry per host program, written by the main
 * process alone when the person answers the confirmation with Always, and removed when they take it back. No chat's
 * command or edit can change it, and a chat on Claude Code cannot read it, in any mode: it is in the data directory,
 * outside the chat folder, which the permission model refuses in every mode and the host's own rules name
 * (`knownDesktopEntries`). Where Codex's sandbox reads Jason's data folder a Codex command can read it, and it holds no
 * secret. A program the AI writes and that you let run, in any mode, can change it, as it can anything you can; the
 * confirmation says so.
 *
 * A write goes to a new file beside it, made only if no file has that name, and renamed over it, so the file is never
 * half-written; a write that fails removes that file again, and a read never takes it.
 *
 * A choice is read only when it was given under this build's consent number (`fullAccessConsent`): a lower or a higher
 * one, a file that does not parse, is larger than any the desktop writes, names a key twice or a key it does not know,
 * or says anything wrongly, is no choice, and the confirmation is shown again. The page's storage never holds it.
 *
 * A take-back the file would not take (`takeBackOwed`) leaves a mark beside it, `full-access-taken-back`: while the mark
 * is there the main process holds the choice as taken back, and the next start removes the file before any chat is made.
 * Where the folder takes no new file at a start (`folderTakesFiles`), the choice is held as taken back for that run.
 */

import { randomBytes } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fullAccessConsent } from './chat-modes.ts';

/** The host programs a standing choice is kept for, each its own. */
export type StandingHost = 'claude' | 'codex';

const standingHosts: readonly StandingHost[] = ['claude', 'codex'];
const entryKeys = ['consent', 'given_at', 'new_chats'];

/** Far more than the desktop ever writes; a larger file is no choice and is not read. */
const largest = 4096;

export interface Standing {
    consent: number;
    /** When the person chose Always, ISO-8601 UTC. */
    givenAt: string;
    /** Whether new chats start in Full access. */
    newChats: boolean;
}

const isoShape = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,3})?Z$/;

/** A moment of the ISO shape that is also a real one: `2026-99-99T99:99:99Z` has the shape and is no date. */
function isMoment(text: string): boolean {
    if (!isoShape.test(text)) return false;
    const moment = new Date(text);
    if (Number.isNaN(moment.getTime())) return false;
    return moment.toISOString().slice(0, 19) === text.slice(0, 19);
}

const isRecord = (raw: unknown): raw is Record<string, unknown> => raw !== null && typeof raw === 'object' && !Array.isArray(raw);

function entryOf(raw: unknown): Standing | null {
    if (!isRecord(raw) || Object.keys(raw).some((key) => !entryKeys.includes(key))) return null;
    const { consent, given_at: givenAt, new_chats: newChats } = raw;
    if (typeof consent !== 'number' || typeof givenAt !== 'string' || !isMoment(givenAt) || typeof newChats !== 'boolean') return null;
    return { consent, givenAt, newChats };
}

/** How many keys the text names, a key named twice counted twice: a string followed by a colon is a key in JSON. */
const keysNamed = (text: string): number => text.match(/"(?:[^"\\]|\\.)*"\s*:/g)?.length ?? 0;

/**
 * Every entry the text says rightly, by host, whatever its consent number; none at all when any part of it is wrong: a
 * key it does not know, a key named twice, an entry that says anything wrongly.
 */
function entriesOf(text: string | null): Map<StandingHost, Standing> {
    const entries = new Map<StandingHost, Standing>();
    const none = new Map<StandingHost, Standing>();
    if (text === null || text.length > largest) return none;
    let raw: unknown;
    try {
        raw = JSON.parse(text);
    } catch {
        return none;
    }
    if (!isRecord(raw)) return none;
    let keys = 0;
    for (const [key, value] of Object.entries(raw)) {
        if (!(standingHosts as readonly string[]).includes(key)) return none;
        const entry = entryOf(value);
        if (entry === null) return none;
        entries.set(key as StandingHost, entry);
        keys += 1 + entryKeys.length;
    }
    if (keysNamed(text) !== keys) return none;
    return entries;
}

/** Pure: the choice the file's text gives a host, or null when it gives none under this build's consent number. */
export function parseStanding(text: string | null, host: StandingHost): Standing | null {
    const entry = entriesOf(text).get(host);
    return entry !== undefined && entry.consent === fullAccessConsent ? entry : null;
}

function readText(file: string): string | null {
    try {
        if (statSync(file).size > largest) return null;
        return readFileSync(file, 'utf8');
    } catch {
        return null;
    }
}

export function readStanding(file: string, host: StandingHost): Standing | null {
    return parseStanding(readText(file), host);
}

/**
 * Writes a host's choice, or takes it back with null, keeping the other host's entry, through a new file beside it
 * renamed over it; a file left with no entry is removed. It throws what the file system threw.
 */
export function writeStanding(file: string, host: StandingHost, standing: Standing | null): void {
    const entries = entriesOf(readText(file));
    if (standing === null) entries.delete(host);
    else entries.set(host, standing);
    if (entries.size === 0) {
        if (existsSync(file)) rmSync(file, { force: true });
        return;
    }
    const body: Record<string, { consent: number; given_at: string; new_chats: boolean }> = {};
    for (const each of standingHosts) {
        const entry = entries.get(each);
        if (entry !== undefined) body[each] = { consent: entry.consent, given_at: entry.givenAt, new_chats: entry.newChats };
    }
    mkdirSync(dirname(file), { recursive: true });
    // A name no one can foretell, made only where nothing has it: a link put there beforehand is never followed.
    const temporary = `${file}.${randomBytes(8).toString('hex')}.tmp`;
    try {
        writeFileSync(temporary, `${JSON.stringify(body, null, 2)}\n`, { mode: 0o600, flag: 'wx' });
        renameSync(temporary, file);
    } catch (error) {
        try {
            rmSync(temporary, { force: true });
        } catch {
            // What could not be removed is never read: a read takes the file alone.
        }
        throw error;
    }
}

/**
 * Whether the file's folder takes a new file: an empty one under a name no one can foretell, made only where nothing is,
 * and removed again. Where it does not, a take-back could not be recorded there either.
 */
export function folderTakesFiles(file: string): boolean {
    const probe = join(dirname(file), `full-access.${randomBytes(8).toString('hex')}.probe`);
    try {
        writeFileSync(probe, '', { mode: 0o600, flag: 'wx' });
        rmSync(probe, { force: true });
        return true;
    } catch {
        try {
            rmSync(probe, { force: true });
        } catch {
            // Nothing more to do: the choice is held as taken back.
        }
        return false;
    }
}

/** The mark a take-back the file would not take leaves beside it. */
export const takenBackMark = (file: string): string => join(dirname(file), 'full-access-taken-back');

/** Whether a take-back is owed: the mark is there. */
export const takeBackOwed = (file: string): boolean => existsSync(takenBackMark(file));

/** Leaves the mark; false when it could not be made either. */
export function oweTakeBack(file: string): boolean {
    try {
        mkdirSync(dirname(file), { recursive: true });
        writeFileSync(takenBackMark(file), '', { mode: 0o600 });
        return true;
    } catch {
        return false;
    }
}

/** Removes the mark once the take-back is done; it throws what the file system threw. */
export function settleTakeBack(file: string): void {
    rmSync(takenBackMark(file), { force: true });
}
