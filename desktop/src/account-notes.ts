/**
 * The desktop's note of each Reply account: `<data>/desktop/reply-account-notes.json`, the desktop's alone, never in the
 * installation's settings or the Reply CLI's files. For an account it holds who it was last seen signed in as, and in which
 * team; or, after Keep it on a sign-in that was bound to no team, that its team is picked afresh. A look writes it for an
 * account signed in and bound with nothing noted, or whose noted team's name had not been read; Choose and Keep it write
 * it; removing the account takes it away. It is never used to tell which Reply accounts there are.
 */

import { mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import type { AccountRef, ReplyTeam } from './accounts.ts';

/**
 * A team an account is bound to: its id, and its name as Reply gives it, empty where Reply gives it none, as for every
 * Reply account outside an organization; null where it was not read.
 */
export interface BoundTeam {
    id: string;
    name: string | null;
}

/** A sign-in as the note keeps it: the login, and the team it is bound to. */
export interface SignIn {
    user: string;
    team: BoundTeam;
}

/** What the note holds of one account: the sign-in it last saw, `fresh` where its team is picked afresh, null for nothing. */
export type Noted = SignIn | 'fresh' | null;

/** One account's entry in the note. */
export interface AccountNote extends AccountRef {
    noted: SignIn | 'fresh';
}

/** Whether two sign-ins are one: the same login in the same team, whatever the team's name is now. */
export const sameSignIn = (a: SignIn, b: SignIn): boolean => a.user === b.user && a.team.id === b.team.id;

/** Whether two notes of an account hold the same, the team's name included. */
const sameNoted = (a: Noted, b: Noted): boolean =>
    a === null || b === null || a === 'fresh' || b === 'fresh' ? a === b : sameSignIn(a, b) && a.team.name === b.team.name;

/** What a look read of a signed-in account. */
export interface SignInSeen {
    /** The team its profile pins, which every run of the account asks in. */
    pin: string | null;
    /** Who it is, as `auth status` names it or `auth whoami` answered; null where neither said. */
    login: string | null;
    /** The team a key acts in, whatever team is asked (`ReplyAuth.keyTeam`); null for a browser's sign-in. */
    keyTeam: string | null;
    /** Whether Reply refused the account's own read in its pin at this look, the sign-in not acting in that team. */
    refused: boolean;
    /** The teams `team list` gave; null where they could not be read. */
    teams: readonly ReplyTeam[] | null;
}

/** What the Reply CLI's own files say of a sign-in, read with no call to Reply. */
export type SignInFiles = Pick<SignInSeen, 'pin' | 'login' | 'keyTeam'>;

/**
 * How a sign-in differs from the note: another login; the same login, no longer in the noted team; the same login,
 * pinned to another of its teams; or a sign-in whose login cannot be known before it has a team it acts in.
 */
export type Change = 'login' | 'left' | 'team' | 'unknown';

/** What a look makes of a signed-in account (`bindingOf`). */
export interface Binding {
    team: BoundTeam | null;
    /** The team to pin now, with no question. */
    bind: BoundTeam | null;
    /** The teams offered on the card, where the account is to be bound and its sign-in acts in several. */
    choose: ReplyTeam[] | null;
    /** Whether its mailboxes and LinkedIn accounts are read. */
    lists: boolean;
    change: Change | null;
}

/** The team a sign-in's runs act in: a key's own, whatever team is asked; a browser's, its pin. */
export const runsIn = ({ pin, keyTeam }: Pick<SignInSeen, 'pin' | 'keyTeam'>): string | null => keyTeam ?? pin;

/**
 * The teams a sign-in acts in: a key's own team alone, which Reply acts in whatever team is asked, named where `team list`
 * names it; a browser's, as `team list` gave them. Null where a browser's could not be read.
 */
export function actsIn({ keyTeam, teams }: Pick<SignInSeen, 'keyTeam' | 'teams'>): BoundTeam[] | null {
    if (keyTeam !== null) return [teams?.find((each) => each.id === keyTeam) ?? { id: keyTeam, name: teams === null ? null : '' }];
    return teams === null ? null : [...teams];
}

/**
 * Whether who a sign-in is can be asked where the Reply CLI does not name it: Reply names it only in a team it acts in, so
 * only where its pin is one of its teams, or may be, its teams not read.
 */
export function whoCanBeAsked(seen: Omit<SignInSeen, 'refused'>): boolean {
    const own = actsIn(seen);
    return seen.login === null && seen.pin !== null && (own === null || own.some((each) => each.id === seen.pin));
}

/**
 * The rule's first case, `bindingOf`'s with no sign-in since, decided before anything is asked of Reply: a look takes a
 * signed-in account as its note says where it is not marked to be checked (a sign-in on its card, or a change not
 * answered yet), and the Reply CLI's own files hold the noted login, or none, and the noted team both pinned and as the
 * team its runs act in (`runsIn`: a key acts in its own, whatever is pinned), the team's name read. Then no teams are asked
 * of Reply; only Reply refusing the account's own read in that team gives a sign-in away.
 */
export const unchangedSince = (files: SignInFiles, noted: Noted, marked: boolean): noted is SignIn =>
    noted !== null &&
    noted !== 'fresh' &&
    !marked &&
    noted.team.name !== null &&
    files.pin === noted.team.id &&
    runsIn(files) === noted.team.id &&
    (files.login ?? noted.user) === noted.user;

/**
 * Whether what is decided of a signed-in account rests on its pin alone: nothing noted, a browser's sign-in whose teams
 * could not be read, and a pin. Its own read in that pin, made in any case then, is made before it is decided, so a
 * refusal there is part of what is decided (`SignInSeen.refused`).
 */
export const restsOnPin = (seen: Omit<SignInSeen, 'refused'>, noted: Noted): boolean => noted === null && actsIn(seen) === null && seen.pin !== null;

/**
 * What a look makes of a signed-in account's team, after a sign-in (with none since, `unchangedSince`), decided here and
 * nowhere else. Case by case:
 * - nothing noted, or its team picked afresh after Keep it: it is bound as a first sign-in is. A pinned team it acts in is
 *   its team; a pin of a team it does not act in, one Reply has just refused, or the pin Keep it set aside, is none, and
 *   then a sign-in in one team is bound to it now, and one in several is offered them. Where its teams could not be read
 *   a pin stands and nothing is bound; with its team picked afresh, nothing is read in the pin set aside;
 * - noted, and who it is cannot be known: the Reply CLI does not name it, and it has no pin it acts in to be asked in. That
 *   is a change, and until it is answered it has no team and nothing is listed. Where it could be asked and the answer did
 *   not come, nothing is decided and nothing listed;
 * - another login: a change; no team, and nothing listed, until it is answered;
 * - the same login, its teams not read: its pin stands, nothing is rebound and nothing said;
 * - the same login, no longer in the noted team: a change; no team, and nothing listed, until it is answered;
 * - the same login pinned to another of its teams: a change, and that team;
 * - the same login still in the noted team: that team, named as it is now; a pin cleared, or of a team it does not act
 *   in, is put back, and nothing is said.
 */
export function bindingOf(seen: SignInSeen, noted: Noted): Binding {
    const own = actsIn(seen);
    const ownTeam = (id: string | null) => (id === null ? undefined : own?.find((each) => each.id === id));
    const bound = (team: BoundTeam | null, change: Change | null = null): Binding => ({ team, bind: null, choose: null, lists: true, change });
    const unbound = (change: Change | null): Binding => ({ team: null, bind: null, choose: null, lists: false, change });

    if (noted === null || noted === 'fresh') {
        const pin = noted === 'fresh' || seen.refused ? null : seen.pin;
        if (own === null) return pin !== null ? bound({ id: pin, name: null }) : noted === null ? bound(null) : unbound(null);
        const pinned = ownTeam(pin);
        if (pinned !== undefined) return bound(pinned);
        if (own.length === 1) return { ...bound(null), bind: own[0]! };
        return { ...unbound(null), choose: [...(seen.teams ?? [])] };
    }
    if (seen.login === null) return unbound(whoCanBeAsked(seen) ? null : 'unknown');
    if (seen.login !== noted.user) return unbound('login');
    if (own === null) return bound(seen.pin === null ? null : seen.pin === noted.team.id ? noted.team : { id: seen.pin, name: null });
    const kept = ownTeam(noted.team.id);
    if (kept === undefined) return unbound('left');
    const pinned = ownTeam(seen.pin);
    if (pinned === undefined) return { ...bound(null), bind: kept };
    return pinned.id === kept.id ? bound(kept) : bound(pinned, 'team');
}

/** The largest note read, in bytes: a file past it is read as no note. */
const noteFileLimit = 1_000_000;

const objectOf = (value: unknown): Record<string, unknown> | null =>
    typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : null;

/** One entry's sign-in, or its team picked afresh; null for an entry of another shape. */
function entryOf(note: Record<string, unknown>): SignIn | 'fresh' | null {
    if (note['pick'] === 'fresh') return 'fresh';
    const team = objectOf(note['team']);
    const [user, id, teamName] = [note['user'], team?.['id'], team?.['name']];
    if (typeof user !== 'string' || user === '' || typeof id !== 'string' || !/^[1-9][0-9]{0,15}$/.test(id)) return null;
    if (typeof teamName !== 'string' && teamName !== null) return null;
    return { user, team: { id, name: teamName } };
}

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
        const [type, name] = [note?.['type'], note?.['name']];
        if (note === null || type !== 'reply' || typeof name !== 'string' || name === '' || notes.some((kept) => kept.name === name)) continue;
        const noted = entryOf(note);
        if (noted !== null) notes.push({ type, name, noted });
    }
    return notes;
}

/** The notes as the file holds them. */
export const accountNotesText = (notes: readonly AccountNote[]): string =>
    `${JSON.stringify(
        {
            accounts: notes.map(({ type, name, noted }) =>
                noted === 'fresh' ? { type, name, pick: 'fresh' } : { type, name, user: noted.user, team: { id: noted.team.id, name: noted.team.name } },
            ),
        },
        null,
        2,
    )}\n`;

/** The notes the file holds: none where it is missing, cannot be read, or is past the limit. */
export function readAccountNotes(file: string): AccountNote[] {
    try {
        if (statSync(file).size > noteFileLimit) return [];
        return parseAccountNotes(readFileSync(file, 'utf8'));
    } catch {
        return [];
    }
}

/** What the notes hold of one account. */
export const notedOf = (notes: readonly AccountNote[], account: AccountRef): Noted =>
    notes.find((each) => each.type === account.type && each.name === account.name)?.noted ?? null;

/**
 * The notes with one account's entry written, or taken away (null), where they hold `over` for it; a look passes what it
 * read, so it writes over nothing another wrote meanwhile. Null where they hold something else.
 */
export function withNote(notes: readonly AccountNote[], account: AccountRef, entry: SignIn | 'fresh' | null, over?: Noted): AccountNote[] | null {
    if (over !== undefined && !sameNoted(notedOf(notes, account), over)) return null;
    const others = notes.filter((each) => !(each.type === account.type && each.name === account.name));
    return entry === null ? others : [...others, { type: account.type, name: account.name, noted: entry }];
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
