/**
 * Pure: the Reply accounts Jason Desktop shows and acts on. They are read every time from two answers and kept nowhere:
 * the Reply CLI's profiles (`reply profile list --json`) and the routes (`jason route list`). An account is a type, the
 * provider, and a name, the Reply CLI profile's exactly, and nothing else. It has no team of its own in Jason: the team is
 * the profile's, pinned in the Reply CLI, and the desktop names the profile alone, which is how Jason's Reply plugin runs a
 * route that names no team. A signed-in account is bound to one team, its profile's pin; the teams of its sign-in
 * (`readTeams`) name that team, or are offered where it can act in several and pins none.
 *
 * The accounts are every profile the CLI holds and every profile a route to the plugin `reply` names, at any level, global
 * or a campaign's; a team a binding holds is no part of its account. A route's profile is the one its binding names, else
 * the one the Reply CLI is set to at that read, as Jason's Reply plugin runs it. The default is the account of the global
 * default route's profile, where that route is the plugin `reply`'s, and no account otherwise.
 */

import { box, capped, type ReplyBox } from './reply-senders.ts';

/** The profile a route's binding may name: the Reply plugin's binding schema takes one to sixty-four characters. */
const profileLimit = 64;

/** An account as a member of the seam names it: the provider, and the profile's name exactly. */
export interface AccountRef {
    type: 'reply';
    name: string;
}

export const sameAccount = (a: AccountRef, b: AccountRef): boolean => a.type === b.type && a.name === b.name;

/** Whether a value the page sent names an account in the seam's shape, and nothing more. */
export function isAccountRef(value: unknown): value is AccountRef {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) return false;
    const { type, name, ...rest } = value as Record<string, unknown>;
    return Object.keys(rest).length === 0 && type === 'reply' && typeof name === 'string' && name !== '';
}

/**
 * What `reply profile list --json` answered: every profile the CLI holds, in its order, and the profile it is set to, which
 * a route that names none acts as.
 */
export interface Profiles {
    current: string;
    available: string[];
}

/**
 * reply-cli 0.23.2 prints exactly `{"current", "available"}` and ends 0, `default` always there and first
 * (src/profile.ts `list_profiles`); a configuration it cannot read or parse ends 1 with its error line on stderr alone.
 * Anything else is a list that was not read.
 */
export function readProfiles(result: { exit: number; stdout: string }): Profiles | null {
    if (result.exit !== 0) return null;
    try {
        const raw = JSON.parse(result.stdout) as { current?: unknown; available?: unknown } | null;
        if (typeof raw?.current !== 'string' || raw.current === '' || !Array.isArray(raw.available)) return null;
        return { current: raw.current, available: [...new Set(raw.available.filter((name): name is string => typeof name === 'string' && name !== ''))] };
    } catch {
        return null;
    }
}

/**
 * Where a profile points, as `reply profile show --json -- <name>` prints it with no call to Reply (reply-cli 0.23.2,
 * src/commands/profile.ts `show_json`): `backend.api_base`, the API address its runs use, and whether the profile inherits
 * it (`backend.inherited.api_base`), which it can only from the Reply CLI's own built-in address (src/profile.ts
 * `EMBEDDED`, `resolve_profile`), production.
 */
export interface ProfileBackend {
    api: string;
    inherited: boolean;
}

/** The longest address the page is handed, in characters. */
const addressMost = 200;

/**
 * A profile's backend as `profile show --json` printed it, its address without blanks around it, which a configuration
 * edited by hand may hold; null for any other ending, or an address that is no http(s) URL in any letter case.
 */
export function readBackend(result: { exit: number; stdout: string }): ProfileBackend | null {
    if (result.exit !== 0) return null;
    try {
        const backend = objectOf(objectOf(JSON.parse(result.stdout))?.['backend']);
        const api = typeof backend?.['api_base'] === 'string' ? backend['api_base'].trim() : null;
        const inherited = objectOf(backend?.['inherited'])?.['api_base'];
        if (api === null || api.length > addressMost || !/^https?:\/\/\S+$/i.test(api) || typeof inherited !== 'boolean') return null;
        return { api, inherited };
    } catch {
        return null;
    }
}

/**
 * Reply's production API: the Reply CLI's built-in address, which a profile that sets none inherits (reply-cli 0.23.2,
 * src/profile.ts `EMBEDDED.api_base`).
 */
export const replyProductionApi = 'https://api.reply.io';

/**
 * An address as two are compared: the scheme and the host in any letter case, a default port and a trailing slash
 * dropped; everything else as written, a query and a fragment among it.
 */
function comparable(address: string): string | null {
    try {
        const url = new URL(address);
        return `${url.protocol}//${url.host}${url.pathname.replace(/\/+$/, '')}${url.search}${url.hash}`;
    } catch {
        return null;
    }
}

/**
 * The API address an account's profile points at where that is not Reply's production: set on the profile, and not
 * `replyProductionApi` however it is written. An address the profile inherits is production. Null on production, or
 * where the backend was not read.
 */
export function nonProductionApi(backend: ProfileBackend | null): string | null {
    if (backend === null || backend.inherited) return null;
    return comparable(backend.api) === comparable(replyProductionApi) ? null : backend.api;
}

/** A team a sign-in can act in, as the Reply CLI lists it: its id, digits only, and its name, held to a length. */
export interface ReplyTeam {
    id: string;
    name: string;
}

/** The longest team or organization name the page is handed, in characters. */
const teamNameMost = 100;

/** A name Reply gives a team or an organization, as the page is handed it: control characters taken out, and cut. */
const replyName = (text: string): string => [...text.replace(/[\p{Cc}\p{Cf}]/gu, ' ').trim()].slice(0, teamNameMost).join('');

/** A positive whole number Reply gives as an id, by its digits; null for anything else. */
const replyId = (value: unknown): string | null => (typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? String(value) : null);

/**
 * The teams `reply --profile <name> team list --json` answered, as reply-cli 0.23.2 prints them (src/commands/team.ts):
 * `{"current_team_id", "teams": [{"team_id", "team_name"}]}`. Each team once, by a positive id, its name with control
 * characters taken out and cut; null for any other ending, or a list with no team.
 */
export function readTeams(result: { exit: number; stdout: string }): ReplyTeam[] | null {
    if (result.exit !== 0) return null;
    let raw: unknown;
    try {
        raw = JSON.parse(result.stdout);
    } catch {
        return null;
    }
    const listed = objectOf(raw)?.['teams'];
    if (!Array.isArray(listed)) return null;
    const teams: ReplyTeam[] = [];
    for (const each of listed) {
        const team = objectOf(each);
        const id = team?.['team_id'];
        if (typeof id !== 'number' || !Number.isSafeInteger(id) || id <= 0 || teams.some((kept) => kept.id === String(id))) continue;
        const name = typeof team?.['team_name'] === 'string' ? replyName(team['team_name']) : '';
        teams.push({ id: String(id), name });
    }
    return teams.length > 0 ? teams : null;
}

/** The Reply organization a team belongs to: its id, digits only, and its name as Reply gives it, held as a team's is. */
export interface ReplyOrg {
    id: string;
    name: string;
}

/**
 * Who a sign-in is, as Reply answered `auth whoami`: the login, the team it answered in, and, where Reply's answer carries
 * them, the team's name and its organization. A member absent here is one the answer does not carry, as from a Reply
 * that has none of them; null is Reply saying there is none.
 */
export interface ReplyWhoami {
    login: string;
    /** The team Reply answered in, the one the sign-in's runs act in; null where it named none. */
    team: string | null;
    /** The team's name; null where Reply gives it none. */
    teamName?: string | null;
    /** The organization the team belongs to; null for a team outside one. */
    org?: ReplyOrg | null;
}

/**
 * Who a sign-in is, as `reply auth whoami --json` answered it, or null where it did not. reply-cli 0.23.2 prints the body
 * of Reply's /v3/whoami as it came and ends 0 (src/commands/auth.ts `handle_whoami`): `{userId, username, teamId}`, and
 * from a Reply that carries them `teamName`, `orgId` and `orgName` too, null where there is none. It asks in the
 * profile's pinned team, so Reply refuses it for a login in several teams with none pinned, or in a team it is not in,
 * and every failure ends with its error line on stderr alone. An organization is read only whole, both members said or
 * both null; nothing is stored.
 */
export function readReplyWhoami(result: { exit: number; stdout: string }): ReplyWhoami | null {
    if (result.exit !== 0) return null;
    let raw: Record<string, unknown> | null;
    try {
        raw = objectOf(JSON.parse(result.stdout));
    } catch {
        return null;
    }
    const login = raw?.['username'];
    if (raw === null || typeof login !== 'string' || login === '') return null;
    const said: ReplyWhoami = { login, team: replyId(raw['teamId']) };
    const teamName = raw['teamName'];
    if (teamName === null || typeof teamName === 'string') said.teamName = teamName === null || replyName(teamName) === '' ? null : replyName(teamName);
    const [orgId, orgName] = [raw['orgId'], raw['orgName']];
    if (orgId === null && orgName === null) said.org = null;
    else if (replyId(orgId) !== null && typeof orgName === 'string') said.org = { id: replyId(orgId)!, name: replyName(orgName) };
    return said;
}

/** The global default route as `jason route list` answered it: its plugin and its whole binding. */
export interface GlobalDefault {
    plugin: string;
    binding: Record<string, unknown> | null;
}

/**
 * A global route to `reply` other than the default, as the mark of the account it names: the kind of work it routes, by
 * Jason's operation, and the channel where it is one channel's route of that operation.
 */
export type DefaultFor = { kind: 'operation'; operation: string } | { kind: 'channel'; operation: string; channel: string };

/**
 * What `jason route list` answered: the global default, or none; the profile every route to `reply` there is names, the
 * global ones first, null for one that names none; and each global route to `reply` but the default, with the profile
 * its binding names.
 */
export interface Routes {
    globalDefault: GlobalDefault | null;
    reply: (string | null)[];
    globalReply: { mark: DefaultFor; profile: string | null }[];
}

const objectOf = (value: unknown): Record<string, unknown> | null =>
    typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : null;

const text = (value: unknown): string | null => (typeof value === 'string' && value !== '' ? value : null);

/** The profile a binding of a route to `reply` names, a team beside it no part of it; null where it names none. */
const profileOf = (binding: Record<string, unknown> | null): string | null => text(binding?.['profile']);

/** The profile each route to `reply` of these names, null for one that names none. */
const replyProfiles = (routes: Record<string, unknown>[]): (string | null)[] =>
    routes.filter((route) => route['plugin_id'] === 'reply').map((route) => profileOf(objectOf(route['binding'])));

/** The global routes to `reply` other than the default, in the order the runtime answered them: the operations', then the channels'. */
function globalReplyRoutes(global: Record<string, unknown>): Routes['globalReply'] {
    const found: Routes['globalReply'] = [];
    const add = (mark: DefaultFor, value: unknown) => {
        const route = objectOf(value);
        if (route?.['plugin_id'] === 'reply') found.push({ mark, profile: profileOf(objectOf(route['binding'])) });
    };
    for (const [operation, route] of Object.entries(objectOf(global['operations']) ?? {})) add({ kind: 'operation', operation }, route);
    for (const [operation, channels] of Object.entries(objectOf(global['channels']) ?? {})) {
        for (const [channel, route] of Object.entries(objectOf(channels) ?? {})) add({ kind: 'channel', operation, channel }, route);
    }
    return found;
}

/** Every route of one scope, in the order the runtime answered them: the default, the operations' own, then the channels'. */
function routesOf(scope: Record<string, unknown>): Record<string, unknown>[] {
    const found: Record<string, unknown>[] = [];
    const add = (value: unknown) => {
        const route = objectOf(value);
        if (route !== null) found.push(route);
    };
    add(scope['default']);
    for (const route of Object.values(objectOf(scope['operations']) ?? {})) add(route);
    for (const channels of Object.values(objectOf(scope['channels']) ?? {})) for (const route of Object.values(objectOf(channels) ?? {})) add(route);
    return found;
}

/**
 * The routes as the runtime answers `route.list` (`RoutesDto`): the global set, whose `default` is null where none is set,
 * and every campaign that has a route of its own. An exit other than 0 (3 where no runtime answered, 1 where it refused,
 * -1 at the time limit), or an answer without `global.default`, is a list that was not read.
 */
export function readRoutes(result: { exit: number; stdout: string }): Routes | null {
    if (result.exit !== 0) return null;
    let raw: unknown;
    try {
        raw = JSON.parse(result.stdout);
    } catch {
        return null;
    }
    const global = objectOf(objectOf(raw)?.['global']);
    if (global === null || !('default' in global)) return null;
    const fallback = global['default'];
    const defaulted = objectOf(fallback);
    if (fallback !== null && (defaulted === null || text(defaulted['plugin_id']) === null)) return null;
    const campaigns = (Array.isArray(objectOf(raw)?.['campaigns']) ? (objectOf(raw)!['campaigns'] as unknown[]) : []).map(objectOf).filter((each): each is Record<string, unknown> => each !== null);
    return {
        globalDefault: defaulted === null ? null : { plugin: text(defaulted['plugin_id'])!, binding: objectOf(defaulted['binding']) },
        reply: [global, ...campaigns].flatMap((scope) => replyProfiles(routesOf(scope))),
        globalReply: globalReplyRoutes(global),
    };
}

/**
 * The account the global default names, where it is the plugin `reply`'s: its binding's profile, else `unnamed`, the
 * profile a route that names none acts as. Null otherwise.
 */
function defaultAccount(routes: Routes, unnamed: string): AccountRef | null {
    const fallback = routes.globalDefault;
    return fallback === null || fallback.plugin !== 'reply' ? null : { type: 'reply', name: profileOf(fallback.binding) ?? unnamed };
}

/** The accounts as one read of the profiles and the routes gives them. */
export interface Listing {
    /** In the order the page shows them: the default first, then the CLI's own order, then the profiles only a route names. */
    accounts: AccountRef[];
    default: AccountRef | null;
    /** The profiles a route names, at any level; null where the routes were not read. */
    routed: Set<string> | null;
    /** The marks of each account a global route to `reply` other than the default names, by its profile. */
    defaultFor: Map<string, DefaultFor[]>;
    /** The profile a route that names none follows (`followedProfile`), or null. */
    followed: string | null;
}

/**
 * The profile a route to `reply` that names none follows: the one the Reply CLI is set to, where any such route is, or
 * where the routes were not read and so might be; null where none is. Removing that profile would reset the CLI's current
 * profile to `default`, and move every such route to `default`'s login.
 */
export const followedProfile = (profiles: Profiles, routes: Routes | null): string | null =>
    routes === null || routes.reply.includes(null) ? profiles.current : null;

/**
 * The accounts, one for each profile, the profiles only a route names in the order the routes name them, and the marks of
 * the global routes. A route that names no profile acts as the one the Reply CLI is set to at this read.
 */
export function accountsOf(profiles: Profiles, routes: Routes | null): Listing {
    const unnamed = profiles.current;
    const routed = routes === null ? null : new Set(routes.reply.map((profile) => profile ?? unnamed));
    const names = [...new Set([...profiles.available, ...(routed ?? [])])];
    const fallback = routes === null ? null : defaultAccount(routes, unnamed);
    const first = fallback === null ? -1 : names.indexOf(fallback.name);
    const ordered = first < 0 ? names : [names[first]!, ...names.filter((_, at) => at !== first)];
    const defaultFor = new Map<string, DefaultFor[]>();
    for (const { mark, profile } of routes?.globalReply ?? []) {
        const name = profile ?? unnamed;
        defaultFor.set(name, [...(defaultFor.get(name) ?? []), mark]);
    }
    return { accounts: ordered.map((name): AccountRef => ({ type: 'reply', name })), default: first < 0 ? null : fallback, routed, defaultFor, followed: followedProfile(profiles, routes) };
}

/**
 * The arguments that make one run of the Reply CLI act as the account, before the command's own: its profile alone, which
 * is how Jason's Reply plugin runs a route that names no team.
 */
export const accountArguments = (account: AccountRef): string[] => ['--profile', account.name];

/** The binding a default Jason Desktop names is written with: the account's profile, and nothing else. */
export const defaultBinding = (account: AccountRef): Record<string, unknown> => ({ profile: account.name });

/** The reasons the runtime journals with a default Jason Desktop names, by a click or at a sign-in; fixed text, never a person's. */
export const defaultReason = 'Made the default with Jason Desktop';
export const signInReason = 'Made the default at a sign-in in Jason Desktop';

/** The desktop's sentences for the accounts: the only words their acts end with. */
export const accountSentences = {
    badAccount: 'That is not a Reply account the desktop listed.',
    profilesUnread: 'The Reply accounts could not be read, so nobody was signed in.',
    profileNotMade: 'The Reply CLI did not make the profile, so nobody was signed in.',
    signedInAlready: 'This Reply account is signed in already, so nobody was signed in: sign it out first, or add another Reply account.',
    cliMissing: 'The Reply CLI was not found, so nobody was signed in.',
    routesUnread: 'Which Reply account is the default could not be read, so nothing was changed.',
    defaultExists: 'Jason has a default already, and Jason Desktop does not move it, so nothing was changed.',
    routesUnreadAtSignIn: 'Signed in, but which Reply account is the default could not be read, so none was made the default.',
    noActor: 'Jason Desktop could not read the name you are signed in to this computer as, which a change of the default is recorded under, so nothing was changed.',
    notRunning: 'Jason is not running, so nothing was changed. Start it, then try again.',
    notPlaced: 'Jason’s Reply plugin could not be placed or loaded, so nothing was changed.',
    notPlacedAtSignIn: 'Signed in, but Jason’s Reply plugin could not be placed or loaded.',
    notDefaultAtSignIn: 'Signed in, but this Reply account was not made the default: Make default on its card says why.',
    notRepresentable: 'The settings file cannot hold this Reply account’s name as written, so nothing was changed: route a campaign to it with `jason route set`, which keeps it as written.',
    teamNotOffered: 'That is not a team this Reply account was offered.',
    teamNotChosen: 'The team was not chosen.',
    teamGone: 'The team was not chosen: this sign-in can no longer work in it.',
    teamPinned: 'This Reply account is bound to its team already, and Jason Desktop does not change it, so nothing was changed.',
    badName: `Name the Reply account in one line of up to ${profileLimit} characters that does not begin with a hyphen.`,
    nameTaken: 'The Reply CLI holds a Reply account of that name already; choose another name.',
    profilesUnreadAtAdd: 'The Reply accounts could not be read, so none was added.',
    notAdded: 'The Reply CLI did not make the profile.',
    notSignedOut: 'The Reply CLI did not sign it out.',
    notRemoved: 'The Reply CLI did not remove the profile.',
    defaultKept: 'The Reply CLI’s own profile default is always there, so nothing was removed.',
    notHeld: 'The Reply CLI holds no profile for this Reply account, so nothing was removed.',
    followed: 'A route of Jason’s follows this Reply account as the Reply CLI’s current profile, and removing it would move that route to another Reply account, so nothing was removed.',
    followedUnread: 'Jason’s routes could not be read, so Jason Desktop cannot tell whether one follows this Reply account as the Reply CLI’s current profile, and nothing was removed.',
    nothingToKeep: 'That Reply account shows no change to keep.',
    notKept: 'Jason Desktop could not write its note of this Reply account, so nothing was changed.',
    unanswered: 'The sign-in of this Reply account changed: keep it or sign it out on its card first, so nothing was changed.',
} as const;

/**
 * Why a name asked for a new Reply account is refused before anything runs, or null: one line of text, not empty after
 * trimming, of no more characters than a route's binding holds, not beginning with a hyphen, which the Reply CLI would read
 * as an option, and not `default`, which every Reply CLI holds. A name the Reply CLI holds is refused after it is asked.
 */
export function accountNameProblem(value: unknown): string | null {
    if (typeof value !== 'string' || value.trim() === '' || value.length > profileLimit || value.startsWith('-') || /[\p{Cc}\p{Zl}\p{Zp}]/u.test(value)) return accountSentences.badName;
    return value === 'default' ? accountSentences.nameTaken : null;
}

/**
 * The box Sign out and Remove ask in, naming the Reply account in typographic quotes: that the act is made in the Reply CLI
 * itself, so `reply` in a terminal loses the sign-in or the profile too, and, where a route names the account, that
 * campaigns sending through it stop.
 */
export function accountBox(act: 'signOut' | 'remove', name: string, routed: boolean): ReplyBox {
    const named = `the Reply account “${capped(name)}”`;
    return act === 'signOut'
        ? box('question', `Sign out of ${named}?`, `It is signed out in the Reply CLI itself, so reply in a terminal loses this sign-in too.${routed ? ' Campaigns sending through it stop until it is signed in again.' : ''}`, 'Sign out')
        : box('warning', `Remove ${named}?`, `Its profile and its sign-in are deleted in the Reply CLI itself, so reply in a terminal loses them too.${routed ? ' Campaigns sending through it stop, and its card stays, not signed in.' : ''}`, 'Remove');
}

/**
 * A refusal in one shape: the desktop's sentence, then the reason of the program that refused as a sentence of its own,
 * cut to 1000 characters and without the "Nothing was changed." `jason` ends a refusal with, which the desktop's sentence
 * says or belies. A reason with no words leaves the sentence alone.
 */
export function withReason(sentence: string, reason: string): string {
    const said = reason.replace(/\s+/g, ' ').trim().replace(/\s*Nothing was changed\.$/, '').slice(0, 1000);
    if (said === '') return sentence;
    return `${sentence} ${said[0]!.toUpperCase()}${said.slice(1)}${/[.!?]$/.test(said) ? '' : '.'}`;
}

/** Every other refusal of the runtime's, in one sentence that carries its code. */
export const otherDefaultRefusal = (code: string | null): string =>
    code === null ? 'The runtime did not change the default, and gave no code, so nothing was changed.' : `The runtime did not change the default: it answered ${code}, so nothing was changed.`;

/** A code as the runtime writes one: snake_case words, at most 64 characters. */
const codeShape = /^[a-z][a-z0-9_]{0,63}$/;

/**
 * What a `jason route set --global` that did not end 0 comes to, in the desktop's sentence, read from the exit and the
 * codes of the runtime's error (`{"error":{"code","details":[{"code"}]}}` on stdout), never from its words.
 */
export function defaultRefusal(result: { exit: number; stdout: string }): string {
    if (result.exit === 3) return accountSentences.notRunning;
    let code: string | null = null;
    let details: string[] = [];
    try {
        const error = objectOf(objectOf(JSON.parse(result.stdout))?.['error']);
        code = typeof error?.['code'] === 'string' && codeShape.test(error['code']) ? error['code'] : null;
        details = (Array.isArray(error?.['details']) ? error['details'] : []).map((detail) => objectOf(detail)?.['code']).filter((each): each is string => typeof each === 'string');
    } catch {
        // No error the runtime printed: said by no code.
    }
    if (details.includes('not_representable_globally')) return accountSentences.notRepresentable;
    return otherDefaultRefusal(code);
}
