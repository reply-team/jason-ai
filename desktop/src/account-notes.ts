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

/** What a look found of a signed-in account: the team its profile pins, its sign-in's own teams, and its login. */
export interface SignInSeen {
    pin: string | null;
    /** Null where they could not be read: then nothing is rebound. */
    teams: readonly ReplyTeam[] | null;
    /** Null where the Reply CLI did not say. */
    login: string | null;
}

/** What a look makes of a signed-in account's team and of its note (`bindingOf`). */
export interface Binding {
    team: ReplyTeam | null;
    /** The team to pin now, with no question. */
    bind: ReplyTeam | null;
    choose: ReplyTeam[] | null;
    /** Whether its mailboxes and LinkedIn accounts are read. */
    lists: boolean;
    change: 'login' | 'left' | 'team' | null;
}

/**
 * What a look makes of a signed-in account's team, decided here and nowhere else. The binding is one login's:
 * - another login than the note's (`login`), or a sign-in that is no longer a member of the noted team (`left`), is bound
 *   to no team and lists nothing until Keep it, which forgets the note so the account is bound as a first sign-in is;
 * - a pin the sign-in is a member of is its team, another of its teams than the note's being a change (`team`);
 * - otherwise, a pin it is not a member of counting as none, a sign-in in one team is bound to it now, and one in several
 *   is offered them.
 * Membership is read from the sign-in's own teams alone: where they could not be read, the pin stands.
 */
export function bindingOf({ pin, teams, login }: SignInSeen, noted: SignIn | null): Binding {
    const unbound = { team: null, bind: null, choose: null, lists: false };
    if (noted !== null && login !== null && login !== noted.user) return { ...unbound, change: 'login' };
    if (noted !== null && teams !== null && !teams.some((each) => each.id === noted.team.id)) return { ...unbound, change: 'left' };
    if (pin !== null && (teams === null || teams.some((each) => each.id === pin))) {
        const team = teams?.find((each) => each.id === pin) ?? (noted?.team.id === pin ? noted.team : { id: pin, name: '' });
        return { team, bind: null, choose: null, lists: true, change: noted !== null && noted.team.id !== pin ? 'team' : null };
    }
    if (teams !== null && teams.length > 1) return { ...unbound, choose: [...teams], change: null };
    return { team: null, bind: teams?.length === 1 ? teams[0]! : null, choose: null, lists: true, change: null };
}

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
