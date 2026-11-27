/**
 * Pure: the Reply accounts Jason Desktop shows and acts on. They are read every time from two answers and kept nowhere:
 * the Reply CLI's profiles (`reply profile list --json`) and the routes (`jason route list`). An account is a type, the
 * provider, and a name, the Reply CLI profile's exactly, and nothing else: Jason knows nothing of teams, and a profile
 * acts in the team the Reply CLI holds for it. The accounts are every profile the CLI holds and every profile a route to
 * the plugin `reply` names, at any level, global or a campaign's; a team a binding holds is ignored. A route whose binding
 * names no profile is no account, since Jason's Reply plugin acts on none: it is counted, to be said. The default is the
 * account the global default route names, where that route is the plugin `reply`'s and names a profile, and no account
 * otherwise. The profile the CLI is set to decides nothing; it is read only to say which one a global default that names
 * no profile has been sending through.
 */

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
 * What `reply profile list --json` answered: every profile the CLI holds, in its order, and the profile it is set to, kept
 * only to name it where a global default names no profile.
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

/** A team a sign-in can act in, as the Reply CLI lists it: its id, digits only, and its name, held to a length. */
export interface ReplyTeam {
    id: string;
    name: string;
}

/** The longest team name the page is handed, in characters. */
const teamNameMost = 100;

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
        const name = typeof team?.['team_name'] === 'string' ? [...team['team_name'].replace(/[\p{Cc}\p{Cf}]/gu, ' ').trim()].slice(0, teamNameMost).join('') : '';
        teams.push({ id: String(id), name });
    }
    return teams.length > 0 ? teams : null;
}

/** The global default route as `jason route list` answered it: its plugin and its whole binding. */
export interface GlobalDefault {
    plugin: string;
    binding: Record<string, unknown> | null;
}

/**
 * What `jason route list` answered: the global default, or none; the profile every route to `reply` there is names, the
 * global ones first; the routes to `reply` that name none: how many campaigns have one of their own, each once, and how
 * many global routes but the default are one, a channel's counting as one; and how many campaigns have a route of their
 * own that names a Reply team beside its profile, each once.
 */
export interface Routes {
    globalDefault: GlobalDefault | null;
    reply: string[];
    campaignsWithoutProfile: number;
    globalWithoutProfile: number;
    campaignsWithTeam: number;
}

const objectOf = (value: unknown): Record<string, unknown> | null =>
    typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : null;

const text = (value: unknown): string | null => (typeof value === 'string' && value !== '' ? value : null);

/** The profile a binding of a route to `reply` names, a team beside it ignored; null where it names none. */
const profileOf = (binding: Record<string, unknown> | null): string | null => text(binding?.['profile']);

/** Whether a route is one to `reply` whose binding names a Reply team beside its profile. */
const namesTeam = (route: Record<string, unknown>): boolean => {
    const binding = objectOf(route['binding']);
    const team = binding?.['team_id'];
    return route['plugin_id'] === 'reply' && profileOf(binding) !== null && team !== undefined && team !== null;
};

/** The profile each route to `reply` of these names, null for one that names none. */
const replyProfiles = (routes: Record<string, unknown>[]): (string | null)[] =>
    routes.filter((route) => route['plugin_id'] === 'reply').map((route) => profileOf(objectOf(route['binding'])));

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
    const globals = replyProfiles(routesOf(global));
    const own = campaigns.map((campaign) => replyProfiles(routesOf(campaign)));
    const named = (profiles: (string | null)[]) => profiles.filter((profile): profile is string => profile !== null);
    return {
        globalDefault: defaulted === null ? null : { plugin: text(defaulted['plugin_id'])!, binding: objectOf(defaulted['binding']) },
        reply: [...named(globals), ...own.flatMap(named)],
        campaignsWithoutProfile: own.filter((profiles) => profiles.includes(null)).length,
        // The global default's own is said apart (`defaultNamesNoProfile`).
        globalWithoutProfile: replyProfiles(routesOf({ ...global, default: null })).filter((profile) => profile === null).length,
        campaignsWithTeam: campaigns.filter((campaign) => routesOf(campaign).some(namesTeam)).length,
    };
}

/** The account the global default names, where it is the plugin `reply`'s and names a profile; null otherwise. */
export function defaultAccount(routes: Routes): AccountRef | null {
    const fallback = routes.globalDefault;
    const profile = fallback === null || fallback.plugin !== 'reply' ? null : profileOf(fallback.binding);
    return profile === null ? null : { type: 'reply', name: profile };
}

/** Whether the global default is the plugin `reply`'s and names no profile, so campaigns that use it cannot send. */
export const defaultNamesNoProfile = (routes: Routes): boolean =>
    routes.globalDefault !== null && routes.globalDefault.plugin === 'reply' && profileOf(routes.globalDefault.binding) === null;

/**
 * The accounts in the order the page shows them, one for each profile: the default first, then the CLI's own order, then
 * the profiles only a route names, in the order the routes name them.
 */
export function accountsOf(profiles: Profiles, routes: Routes | null): { accounts: AccountRef[]; default: AccountRef | null } {
    const names = [...new Set([...profiles.available, ...(routes?.reply ?? [])])];
    const fallback = routes === null ? null : defaultAccount(routes);
    const first = fallback === null ? -1 : names.indexOf(fallback.name);
    const ordered = first < 0 ? names : [names[first]!, ...names.filter((_, at) => at !== first)];
    return { accounts: ordered.map((name): AccountRef => ({ type: 'reply', name })), default: first < 0 ? null : fallback };
}

/**
 * The arguments that make one run of the Reply CLI act as the account, before the command's own, as Jason's Reply plugin
 * starts it: its profile, and never a team.
 */
export const accountArguments = (account: AccountRef): string[] => ['--profile', account.name];

/**
 * The binding Make default names the account by: its profile, and `derive_first_name` where the old default was a Reply
 * binding that held it; nothing else of the old route, a team included.
 */
export function defaultBinding(account: AccountRef, was: GlobalDefault | null): Record<string, unknown> {
    const derive = was?.plugin === 'reply' ? was.binding?.['derive_first_name'] : undefined;
    return { profile: account.name, ...(typeof derive === 'boolean' ? { derive_first_name: derive } : {}) };
}

const shown = (value: string): string => {
    const characters = [...value.replace(/[\p{Cc}\p{Cf}]/gu, ' ').trim()];
    return characters.length > 64 ? `${characters.slice(0, 64).join('')}…` : characters.join('');
};

/**
 * `the Reply account “outbound”`: an account in the desktop's sentences, its name set off, since a profile is as often named
 * `default` as anything.
 */
export const accountNamed = (account: AccountRef): string => `the Reply account “${shown(account.name)}”`;

/**
 * The global default as the box of Make default names it: a Reply account, or another plugin by its id; null for a Reply
 * default that names no profile, which the box says in a sentence of its own.
 */
export function defaultWords(fallback: GlobalDefault): string | null {
    if (fallback.plugin !== 'reply') return `the plugin ${shown(fallback.plugin)}`;
    const profile = profileOf(fallback.binding);
    return profile === null ? null : accountNamed({ type: 'reply', name: profile });
}

/** The reasons the runtime journals with a default Jason Desktop names, by a click or at a sign-in; fixed text, never a person's. */
export const defaultReason = 'Made the default with Jason Desktop';
export const signInReason = 'Made the default at a sign-in in Jason Desktop';

/** The desktop's sentences for the accounts: the only words their acts end with. */
export const accountSentences = {
    badAccount: 'That is not a Reply account the desktop listed.',
    profilesUnread: 'The Reply accounts could not be read, so nobody was signed in.',
    profileNotMade: 'The Reply CLI did not make the profile, so nobody was signed in.',
    cliMissing: 'The Reply CLI was not found, so nobody was signed in.',
    routesUnread: 'Which account is the default could not be read, so nothing was changed.',
    changedMeanwhile: 'The default changed meanwhile, so nothing was changed: look at the cards again, then try again.',
    routesUnreadAtSignIn: 'Signed in, but which account is the default could not be read, so none was made the default.',
    noActor: 'Jason Desktop could not read the name of this computer’s account, which a change of the default is recorded under, so nothing was changed.',
    notRunning: 'Jason is not running, so nothing was changed. Start it, then try again.',
    notPlaced: 'Jason’s Reply plugin could not be placed or loaded, so nothing was changed.',
    orphansSearch: 'A search still followed runs on the account that is the default now, so nothing was changed: stop it, or let it end, then try again.',
    orphansSend: 'A send Reply holds for later runs on the account that is the default now, so nothing was changed: cancel it, or wait until it is sent, then try again.',
    notDefaultAtSignIn: 'Signed in, but this account was not made the default: Make default on its card says why.',
    notRepresentable: 'The settings file cannot hold this account’s name as written, so nothing was changed: route a campaign to it with `jason route set`, which keeps it as written.',
    teamNotOffered: 'That is not a team this account was offered.',
    teamNotChosen: 'The team was not chosen.',
} as const;

/**
 * A sign-in that signed in, then could not place or load Jason's Reply plugin: said as that first, then the program's own
 * words without the "Nothing was changed." `jason plugin install` ends a refusal with, which the sign-in belies.
 */
export function notPlacedAtSignIn(said: string): string {
    const why = said.replace(/\s*Nothing was changed\.\s*$/, '');
    return why === '' ? 'Signed in, but Jason’s Reply plugin could not be placed.' : `Signed in, but Jason’s Reply plugin could not be placed: ${why}`;
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
    if (code === 'route_orphans_search' || details.includes('route_orphans_search')) return accountSentences.orphansSearch;
    if (code === 'route_orphans_send' || details.includes('route_orphans_send')) return accountSentences.orphansSend;
    if (details.includes('not_representable_globally')) return accountSentences.notRepresentable;
    return otherDefaultRefusal(code);
}
