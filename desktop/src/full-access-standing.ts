/**
 * The standing choice of Full access, `<data>/desktop/full-access.json`: one entry per host program, written by the main
 * process alone when the person answers the confirmation with Always, and removed when they take it back. A chat's turn
 * can neither read nor write it: it is in the data directory, outside the chat folder, which the permission model
 * refuses in every mode and the host's own rules name (`knownDesktopEntries`). A program the agent writes and runs in
 * Full access could write it, as it could anything the person can; the confirmation says so.
 *
 * A choice is read only when it was given under this build's consent number (`fullAccessConsent`): a lower or a higher
 * one, a file that does not parse, or an entry that says anything wrongly is no choice, and the confirmation is shown
 * again. The page's storage never holds it.
 */

import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { fullAccessConsent } from './chat-modes.ts';

/** The host programs a standing choice is kept for, each its own. */
export type StandingHost = 'claude' | 'codex';

const standingHosts: readonly StandingHost[] = ['claude', 'codex'];

export interface Standing {
    consent: number;
    /** When the person chose Always, ISO-8601 UTC. */
    givenAt: string;
    /** Whether new chats start in Full access. */
    newChats: boolean;
}

const isoShape = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,3})?Z$/;

function entryOf(raw: unknown): Standing | null {
    if (raw === null || typeof raw !== 'object' || Array.isArray(raw)) return null;
    const value = raw as { consent?: unknown; given_at?: unknown; new_chats?: unknown };
    if (typeof value.consent !== 'number' || typeof value.given_at !== 'string' || !isoShape.test(value.given_at) || typeof value.new_chats !== 'boolean') return null;
    return { consent: value.consent, givenAt: value.given_at, newChats: value.new_chats };
}

/** Every entry the text says rightly, by host, whatever its consent number. */
function entriesOf(text: string | null): Map<StandingHost, Standing> {
    const entries = new Map<StandingHost, Standing>();
    if (text === null) return entries;
    let raw: unknown;
    try {
        raw = JSON.parse(text);
    } catch {
        return entries;
    }
    if (raw === null || typeof raw !== 'object' || Array.isArray(raw)) return entries;
    for (const host of standingHosts) {
        const entry = entryOf((raw as Record<string, unknown>)[host]);
        if (entry !== null) entries.set(host, entry);
    }
    return entries;
}

/** Pure: the choice the file's text gives a host, or null when it gives none under this build's consent number. */
export function parseStanding(text: string | null, host: StandingHost): Standing | null {
    const entry = entriesOf(text).get(host);
    return entry !== undefined && entry.consent === fullAccessConsent ? entry : null;
}

function readText(file: string): string | null {
    try {
        return readFileSync(file, 'utf8');
    } catch {
        return null;
    }
}

export function readStanding(file: string, host: StandingHost): Standing | null {
    return parseStanding(readText(file), host);
}

/**
 * Writes a host's choice, or takes it back with null, keeping the other host's entry, through a file beside it renamed
 * over it; a file left with no entry is removed. It throws what the file system threw.
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
    const temporary = `${file}.${process.pid}.tmp`;
    writeFileSync(temporary, `${JSON.stringify(body, null, 2)}\n`, { mode: 0o600 });
    renameSync(temporary, file);
}
