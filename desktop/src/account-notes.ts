/**
 * The desktop's note of each Reply account: `<data>/desktop/reply-account-notes.json`, the desktop's alone, never in the
 * installation's settings or the Reply CLI's files. For an account it holds who it was last seen signed in as, with Reply's
 * id of that user where it was known, and in which team, with the team's name and organization as Reply last named them,
 * and beside it the Reply CLI's own record of the sign-in as it stood then (`CliRecord`); or, after Keep it on a sign-in
 * that was bound to no team, that its team is picked afresh. A look writes it for an account signed in and bound with
 * nothing noted, or where it read of the noted sign-in what the note does not hold (`signInRead`): the address Reply names
 * the user by now, the user's id, the team's name or organization, the Reply CLI's record. Choose and Keep it write it;
 * removing the account takes it away. It is never used to tell which Reply accounts there are.
 *
 * Like is compared with like. The Reply CLI keeps, for a sign-in, the user Reply gave it then: the seat of the team pinned
 * then, under that day's address, for good. Reply, asked now, names the seat of the team pinned now under today's address.
 * So the Reply CLI's record is held only against the record noted, and Reply's answer only against who is noted.
 */

import { mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import type { AccountRef, ReplyOrg, ReplyTeam, ReplyWhoami } from './accounts.ts';

/**
 * A team an account is bound to: its id, and its name as Reply gives it, empty where Reply gives it none, null where it was
 * not read; and the organization it belongs to, as Reply's whoami named it after a sign-in, null for a team outside one,
 * absent where Reply has not said. The organization decides nothing of the binding.
 */
export interface BoundTeam {
    id: string;
    name: string | null;
    org?: ReplyOrg | null;
}

/** A sign-in as the card shows it: the login, and the team it is bound to. */
export interface SignIn {
    user: string;
    team: BoundTeam;
}

/** A login: the address Reply names the user by, and Reply's id of the user's seat where it is known, one in each team. */
export interface Login {
    user: string;
    id?: string | null;
}

/**
 * The Reply CLI's own record of a sign-in (`auth status`'s `user`): the address and Reply's id of the user it was given at
 * the sign-in, each null where it holds none. Only a sign-in writes it.
 */
export interface CliRecord {
    user: string | null;
    id: string | null;
}

/**
 * A sign-in as the note keeps it: the card's, Reply's id of the user's seat in its team where it was known, and the Reply
 * CLI's record as it stood when noted, absent from a note written before it was kept, whose one user then stands for it.
 */
export interface NotedSignIn extends SignIn {
    id?: string;
    record?: CliRecord;
}

/** What the note holds of one account: the sign-in it last saw, `fresh` where its team is picked afresh, null for nothing. */
export type Noted = NotedSignIn | 'fresh' | null;

/** One account's entry in the note. */
export interface AccountNote extends AccountRef {
    noted: NotedSignIn | 'fresh';
}

/**
 * Whether two sign-ins are one login, a person's Reply sign-in: the same address, whatever the team and the id, since Reply
 * gives a login one user id in each of its teams; or, under another address, the same id of Reply's, the same seat whose
 * address the person changed at Reply. Another address and another id is another login.
 */
export const sameUser = (a: Login, b: Login): boolean => a.user === b.user || (a.id != null && b.id != null && a.id === b.id);

/** Whether two sign-ins are one: the same user in the same team, whatever the address and the team's name are now. */
export const sameSignIn = (a: SignIn & Login, b: SignIn & Login): boolean => sameUser(a, b) && a.team.id === b.team.id;

/** Whether two teams are named alike: the same name, and the same organization or none said of either. */
const sameWords = (a: BoundTeam, b: BoundTeam): boolean =>
    a.name === b.name && (a.org === undefined || b.org === undefined || a.org === null || b.org === null ? a.org === b.org : a.org.id === b.org.id && a.org.name === b.org.name);

const sameRecord = (a: CliRecord | undefined, b: CliRecord | undefined): boolean =>
    a === undefined || b === undefined ? a === b : a.user === b.user && a.id === b.id;

/**
 * Whether two notes of an account hold the same, the address, the user's id, the team's name and organization, and the
 * Reply CLI's record included.
 */
const sameNoted = (a: Noted, b: Noted): boolean =>
    a === null || b === null || a === 'fresh' || b === 'fresh'
        ? a === b
        : a.user === b.user && a.id === b.id && a.team.id === b.team.id && sameWords(a.team, b.team) && sameRecord(a.record, b.record);

/**
 * A bound team as Reply's whoami names it besides, where it answered in that team (`said`, null where it was not asked or
 * not answered): its organization where the answer carries one or says there is none, and its name where `team list`
 * gave none or could not be read. Where both give a name, `team list`'s stands: the teams to choose from and the binding
 * are named by it, so the card names a team as it offered it.
 */
export function namedBy(team: BoundTeam, said: ReplyWhoami | null): BoundTeam {
    if (said === null || said.team !== team.id) return team;
    const name = (team.name === null || team.name === '') && said.teamName !== undefined ? (said.teamName ?? '') : team.name;
    return { ...team, name, ...(said.org === undefined ? {} : { org: said.org }) };
}

/**
 * The team the note keeps of a noted sign-in after a look that read `now` of the same team: what this look read, and the
 * note's own where it read nothing (a name not read, or none where the note holds one; an organization Reply did not say).
 */
export function teamKept(noted: BoundTeam, now: BoundTeam): BoundTeam {
    const name = now.name === null || (now.name === '' && noted.name !== null) ? noted.name : now.name;
    const org = now.org !== undefined ? now.org : noted.org;
    return { id: now.id, name, ...(org === undefined ? {} : { org }) };
}

/**
 * The sign-in the note keeps of a noted one after a look that read `now` of the same user in the same team: the address
 * and the user's id as the look took them, the note's id where it took none, the team as `teamKept` keeps it, and the Reply
 * CLI's record as the look read it.
 */
export function signInKept(noted: NotedSignIn, now: NotedSignIn): NotedSignIn {
    const id = now.id ?? noted.id;
    const record = now.record ?? noted.record;
    return { user: now.user, ...(id === undefined ? {} : { id }), team: teamKept(noted.team, now.team), ...(record === undefined ? {} : { record }) };
}

/** Whether a look read anything of a noted sign-in that its note does not hold. */
export const signInRead = (noted: NotedSignIn, now: NotedSignIn): boolean => !sameNoted(signInKept(noted, now), noted);

/** What a look read of a signed-in account. */
export interface SignInSeen {
    /** The team its profile pins, which every run of the account asks in. */
    pin: string | null;
    /** The address of the Reply CLI's record (`CliRecord`), as `auth status` gives it; null where it holds none. */
    login: string | null;
    /** Reply's id of that user, as the record holds it. */
    userId: string | null;
    /** Who Reply said it is at this look, `auth whoami` in the team its runs act in; null where it was not asked or did not say. */
    said: Login | null;
    /** The team a key acts in, whatever team is asked (`ReplyAuth.keyTeam`); null for a browser's sign-in. */
    keyTeam: string | null;
    /** Whether Reply refused the account's own read in its pin at this look, the sign-in not acting in that team. */
    refused: boolean;
    /** The teams `team list` gave; null where they could not be read. */
    teams: readonly ReplyTeam[] | null;
}

/** What the Reply CLI's own files say of a sign-in, read with no call to Reply. */
export type SignInFiles = Pick<SignInSeen, 'pin' | 'login' | 'userId' | 'keyTeam'>;

/** The Reply CLI's record as its files hold it now. */
export const recordOf = (files: Pick<SignInFiles, 'login' | 'userId'>): CliRecord => ({ user: files.login, id: files.userId });

/**
 * Whether the Reply CLI's files hold the record noted, and nothing else: the same address and id, or none where it named
 * none. A note written before it kept the record stands for it by its one user, by `sameUser`, or by none named.
 */
const recordHeld = (files: Pick<SignInFiles, 'login' | 'userId'>, noted: NotedSignIn): boolean =>
    noted.record === undefined
        ? files.login === null || sameUser({ user: files.login, id: files.userId }, noted)
        : files.login === noted.record.user && files.userId === noted.record.id;

/**
 * Whether a signed-in account is its noted login, each user held only against its own (`sameUser`): who Reply said it is
 * at this look against who is noted; where Reply did not say, the Reply CLI's record against the record noted, where both
 * name a user. Null where neither tells.
 */
export function isNotedLogin(seen: Pick<SignInSeen, 'login' | 'userId' | 'said'>, noted: NotedSignIn): boolean | null {
    if (seen.said !== null) return sameUser(seen.said, noted);
    const kept = noted.record ?? { user: noted.user, id: noted.id ?? null };
    return seen.login === null || kept.user === null ? null : sameUser({ user: seen.login, id: seen.userId }, { user: kept.user, id: kept.id });
}

/**
 * Who a signed-in account is where Reply did not say. With nothing noted, the Reply CLI's record. Noted: the noted login,
 * as noted, where the record is its record (`isNotedLogin`), so the CLI's older address or another team's seat stands for
 * nothing; the record where it is another login's; none where the record cannot tell. Null for none.
 */
export function recordedAs(files: Pick<SignInFiles, 'login' | 'userId'>, noted: Noted): Login | null {
    const record = files.login === null ? null : { user: files.login, id: files.userId };
    if (noted === null || noted === 'fresh') return record;
    const same = isNotedLogin({ ...files, said: null }, noted);
    return same === null ? null : same ? { user: noted.user, id: noted.id ?? null } : record;
}

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
 * The team Reply answers who a sign-in is in at this moment, the one its runs act in: a key's own; else its pin, where that
 * is one of its teams, or may be (its teams not read) and Reply did not just refuse its read there; else, nothing pinned,
 * its one team. Null where Reply would name it in no team.
 */
export function asksIn(seen: Pick<SignInSeen, 'pin' | 'keyTeam' | 'teams' | 'refused'>): string | null {
    if (seen.keyTeam !== null) return seen.keyTeam;
    const own = actsIn(seen);
    if (seen.pin === null) return own?.length === 1 ? own[0]!.id : null;
    return !seen.refused && (own === null || own.some((each) => each.id === seen.pin)) ? seen.pin : null;
}

/**
 * Whether no sign-in is seen since the note, before anything is asked of Reply: the account is not marked to be checked (a
 * sign-in on its card, or a change not answered yet), and the Reply CLI's own files hold the record noted (`recordHeld`),
 * whatever Reply would say of the address or the seat, and the noted team both pinned and as the team its runs act in
 * (`runsIn`: a key acts in its own, whatever is pinned). Another record is a sign-in made in the files. Then the look takes
 * the account as its note says; only Reply, refusing its lists there or naming another user, gives a sign-in away.
 */
export const noSignInSince = (files: SignInFiles, noted: Noted, marked: boolean): noted is NotedSignIn =>
    noted !== null && noted !== 'fresh' && !marked && files.pin === noted.team.id && runsIn(files) === noted.team.id && recordHeld(files, noted);

/**
 * Whether a look with no sign-in since asks Reply who a signed-in account is, and with it its team's organization: once in
 * a run (`asked`: whether it was asked in this run in that team), for a team whose organization the note does not hold, or
 * for a sign-in the Reply CLI does not name (`unnamed`), which nothing else could give away. After a sign-in every look
 * asks it, at every look until the sign-in is answered, where Reply can be asked (`asksIn`).
 */
export const whoIsAsked = (noted: NotedSignIn, unnamed: boolean, asked: (team: string) => boolean): boolean =>
    !asked(noted.team.id) && (noted.team.org === undefined || unnamed);

/**
 * Whether what is decided of a signed-in account rests on its pin alone: a browser's sign-in whose teams could not be
 * read, and a pin, with nothing noted, or noted where the Reply CLI does not name the login, which Reply can name only in
 * a team it works in. Its own read in that pin, made in any case then, is made before it is decided, so a refusal there
 * is part of what is decided (`SignInSeen.refused`).
 */
export const restsOnPin = (seen: Pick<SignInSeen, 'pin' | 'login' | 'keyTeam' | 'teams'>, noted: Noted): boolean =>
    (noted === null || (noted !== 'fresh' && seen.login === null)) && actsIn(seen) === null && seen.pin !== null;

/**
 * What a look makes of a signed-in account's team, unless it takes the account as noted (`noSignInSince`), decided here and
 * nowhere else. Case by case:
 * - nothing noted, or its team picked afresh after Keep it: it is bound as a first sign-in is. A pinned team it acts in is
 *   its team; a pin of a team it does not act in, one Reply has just refused, or the pin Keep it set aside, is none, and
 *   then a sign-in in one team is bound to it now, and one in several is offered them. Where its teams could not be read
 *   a pin stands and nothing is bound; with its team picked afresh, nothing is read in the pin set aside;
 * - noted, and who it is cannot be known: Reply did not say, the Reply CLI's record cannot tell (`isNotedLogin`), and Reply
 *   would name it in no team (`asksIn`). That is a change, and until it is answered it has no team and nothing is listed.
 *   Where it could be asked and the answer did not come, nothing is decided and nothing listed;
 * - another login (`isNotedLogin`): a change; no team, and nothing listed, until it is answered;
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
    const same = isNotedLogin(seen, noted);
    if (same === null) return unbound(asksIn(seen) !== null ? null : 'unknown');
    if (!same) return unbound('login');
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

const idShape = /^[1-9][0-9]{0,15}$/;

/** A team's organization as an entry holds it: null for none; undefined where it holds none, or one of another shape. */
function orgOf(value: unknown): ReplyOrg | null | undefined {
    if (value === null) return null;
    const org = objectOf(value);
    const [id, name] = [org?.['id'], org?.['name']];
    return typeof id === 'string' && idShape.test(id) && typeof name === 'string' ? { id, name } : undefined;
}

/** The Reply CLI's record as an entry holds it; undefined where it holds none, or one of another shape. */
function recordIn(value: unknown): CliRecord | undefined {
    const record = objectOf(value);
    const [user, id] = [record?.['user'], record?.['id']];
    if (record === null || !((typeof user === 'string' && user !== '') || user === null) || !((typeof id === 'string' && idShape.test(id)) || id === null)) return undefined;
    return { user, id };
}

/**
 * One entry's sign-in, or its team picked afresh; null for an entry of another shape. A user's id of another shape is none,
 * and a record of another shape is none, as in a note written before it was kept.
 */
function entryOf(note: Record<string, unknown>): NotedSignIn | 'fresh' | null {
    if (note['pick'] === 'fresh') return 'fresh';
    const team = objectOf(note['team']);
    const [user, userId, id, teamName] = [note['user'], note['id'], team?.['id'], team?.['name']];
    if (typeof user !== 'string' || user === '' || typeof id !== 'string' || !idShape.test(id)) return null;
    if (typeof teamName !== 'string' && teamName !== null) return null;
    const org = orgOf(team?.['org']);
    const record = recordIn(note['record']);
    return {
        user,
        ...(typeof userId === 'string' && idShape.test(userId) ? { id: userId } : {}),
        team: { id, name: teamName, ...(org === undefined ? {} : { org }) },
        ...(record === undefined ? {} : { record }),
    };
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
                noted === 'fresh'
                    ? { type, name, pick: 'fresh' }
                    : {
                          type,
                          name,
                          user: noted.user,
                          ...(noted.id === undefined ? {} : { id: noted.id }),
                          team: { id: noted.team.id, name: noted.team.name, ...(noted.team.org === undefined ? {} : { org: noted.team.org }) },
                          ...(noted.record === undefined ? {} : { record: { user: noted.record.user, id: noted.record.id } }),
                      },
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
export function withNote(notes: readonly AccountNote[], account: AccountRef, entry: NotedSignIn | 'fresh' | null, over?: Noted): AccountNote[] | null {
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
