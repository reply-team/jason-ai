/**
 * The desktop's note of who each Reply account was last seen signed in as, and in which team:
 * `<data>/desktop/reply-account-notes.json`, the desktop's alone, never in the installation's settings or the Reply CLI's
 * files. It is written where a look finds an account signed in and bound to a team with nothing noted for it, and at Keep
 * it; an account's note goes when the account is removed. It says who an account was, and nothing else: it is never read
 * to decide which accounts there are or what a card lists.
 */

import { mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import type { AccountRef, ReplyTeam } from './accounts.ts';

/** A sign-in as the note keeps it: the login, and the team it is bound to. */
export interface SignIn {
    user: string;
    team: ReplyTeam;
}

/** One account's note. */
export interface AccountNote extends AccountRef, SignIn {}

/** Whether two sign-ins are one: the same login in the same team, whatever the team's name is now. */
export const sameSignIn = (a: SignIn, b: SignIn): boolean => a.user === b.user && a.team.id === b.team.id;

/** The largest note read, in bytes: a file past it is read as no note. */
const noteFileLimit = 1_000_000;

const objectOf = (value: unknown): Record<string, unknown> | null =>
    typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : null;

/** The notes a file's text holds, each account once, the first kept; an entry of another shape is left out. */
export function parseAccountNotes(text: string): AccountNote[] {
    let raw: unknown;
    try {
        raw = JSON.parse(text);
    } catch {
        return [];
    }
    const listed = objectOf(raw)?.['accounts'];
    const notes: AccountNote[] = [];
    for (const each of Array.isArray(listed) ? listed : []) {
        const note = objectOf(each);
        const team = objectOf(note?.['team']);
        const [type, name, user, id, teamName] = [note?.['type'], note?.['name'], note?.['user'], team?.['id'], team?.['name']];
        if (type !== 'reply' || typeof name !== 'string' || name === '' || typeof user !== 'string' || user === '') continue;
        if (typeof id !== 'string' || !/^[1-9][0-9]{0,15}$/.test(id) || typeof teamName !== 'string') continue;
        if (notes.some((kept) => kept.name === name)) continue;
        notes.push({ type, name, user, team: { id, name: teamName } });
    }
    return notes;
}

/** The notes as the file holds them. */
export const accountNotesText = (notes: readonly AccountNote[]): string =>
    `${JSON.stringify({ accounts: notes.map(({ type, name, user, team }) => ({ type, name, user, team: { id: team.id, name: team.name } })) }, null, 2)}\n`;

/** The notes the file holds: none where it is missing, cannot be read, or is past the limit. */
export function readAccountNotes(file: string): AccountNote[] {
    try {
        if (statSync(file).size > noteFileLimit) return [];
        return parseAccountNotes(readFileSync(file, 'utf8'));
    } catch {
        return [];
    }
}

/** Writes the notes beside the file and renames them over it, readable by this account alone; throws where it could not. */
export function writeAccountNotes(file: string, notes: readonly AccountNote[]): void {
    const beside = `${file}.${process.pid}.tmp`;
    try {
        mkdirSync(dirname(file), { recursive: true });
        writeFileSync(beside, accountNotesText(notes), { mode: 0o600 });
        renameSync(beside, file);
    } catch {
        rmSync(beside, { force: true });
        throw new Error('reply-account-notes.json could not be written');
    }
}
