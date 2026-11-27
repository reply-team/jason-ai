/**
 * Pure: the Reply accounts Jason Desktop shows and acts on. They are read every time from two answers and kept nowhere:
 * the Reply CLI's profiles (`reply profile list --json`) and the routes (`jason route list`). An account is a type, the
 * provider, and a name, the Reply CLI profile's exactly, with the team where a route names one for that profile. The
 * accounts are every profile the CLI holds and every profile a route to the plugin `reply` names, at any level, global
 * or a campaign's; a route whose binding names no profile counts as the profile the CLI answers as current. The default
 * is the account the global default route names, where that route is the plugin `reply`'s, and no account otherwise.
 */

/** An account as a member of the seam names it: the provider, the profile's name exactly, and the team or none. */
export interface AccountRef {
    type: 'reply';
    name: string;
    team: string | null;
}

export const sameAccount = (a: AccountRef, b: AccountRef): boolean => a.type === b.type && a.name === b.name && a.team === b.team;

/** Whether a value the page sent names an account in the seam's shape, and nothing more. */
export function isAccountRef(value: unknown): value is AccountRef {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) return false;
    const { type, name, team, ...rest } = value as Record<string, unknown>;
    return Object.keys(rest).length === 0 && type === 'reply' && typeof name === 'string' && name !== '' && (team === null || (typeof team === 'string' && team !== ''));
}

/** What `reply profile list --json` answered: the profile the CLI is set to, and every profile it holds, in its order. */
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
        const available = [...new Set(raw.available.filter((name): name is string => typeof name === 'string' && name !== ''))];
        return { current: raw.current, available };
    } catch {
        return null;
    }
}

/** A binding of a route to the plugin `reply`, by the account it names; null where it names none. */
interface ReplyBinding {
    profile: string | null;
    team: string | null;
}

/** The global default route as `jason route list` answered it: its plugin and its whole binding. */
export interface GlobalDefault {
    plugin: string;
    binding: Record<string, unknown> | null;
}

/** What `jason route list` answered: the global default, or none, and every route to `reply` there is, the global ones first. */
export interface Routes {
    globalDefault: GlobalDefault | null;
    reply: ReplyBinding[];
}

const objectOf = (value: unknown): Record<string, unknown> | null =>
    typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : null;

const text = (value: unknown): string | null => (typeof value === 'string' && value !== '' ? value : null);

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
    const campaigns = objectOf(raw)?.['campaigns'];
    const scopes = [global, ...(Array.isArray(campaigns) ? campaigns.map(objectOf).filter((each): each is Record<string, unknown> => each !== null) : [])];
    const reply = scopes
        .flatMap(routesOf)
        .filter((route) => route['plugin_id'] === 'reply')
        .map((route) => {
            const binding = objectOf(route['binding']);
            return { profile: text(binding?.['profile']), team: text(binding?.['team_id']) };
        });
    return {
        globalDefault: defaulted === null ? null : { plugin: text(defaulted['plugin_id'])!, binding: objectOf(defaulted['binding']) },
        reply,
    };
}

/** The account a binding names: its profile, or the one the CLI is set to where it names none, and its team. */
export const boundAccount = (binding: ReplyBinding, current: string): AccountRef => ({ type: 'reply', name: binding.profile ?? current, team: binding.team });

/** The account the global default names, where it is the plugin `reply`'s; null otherwise. */
export function defaultAccount(routes: Routes, current: string): AccountRef | null {
    const fallback = routes.globalDefault;
    if (fallback === null || fallback.plugin !== 'reply') return null;
    return boundAccount({ profile: text(fallback.binding?.['profile']), team: text(fallback.binding?.['team_id']) }, current);
}

/**
 * The accounts in the order the page shows them: the default first, then the CLI's own order, then the profiles only a
 * route names, in the order the routes name them. A profile is one account with no team where no route names a team for
 * it, and one account for each team a route names for it otherwise, its route with no team among them.
 */
export function accountsOf(profiles: Profiles, routes: Routes | null): { accounts: AccountRef[]; default: AccountRef | null } {
    const bound = (routes?.reply ?? []).map((binding) => boundAccount(binding, profiles.current));
    const teams = new Map<string, (string | null)[]>();
    for (const account of bound) {
        const named = teams.get(account.name) ?? [];
        if (!named.includes(account.team)) named.push(account.team);
        teams.set(account.name, named);
    }
    const names = [...profiles.available, ...bound.map((account) => account.name).filter((name) => !profiles.available.includes(name))];
    const accounts = [...new Set(names)].flatMap((name) => (teams.get(name) ?? [null]).map((team): AccountRef => ({ type: 'reply', name, team })));
    const fallback = routes === null ? null : defaultAccount(routes, profiles.current);
    const first = fallback === null ? -1 : accounts.findIndex((account) => sameAccount(account, fallback));
    return { accounts: first < 0 ? accounts : [accounts[first]!, ...accounts.filter((_, at) => at !== first)], default: first < 0 ? null : fallback };
}

/**
 * The arguments that make one run of the Reply CLI act as the account, before the command's own, as Jason's Reply plugin
 * starts it: the profile always, the team where the account has one.
 */
export const accountArguments = (account: AccountRef): string[] => ['--profile', account.name, ...(account.team === null ? [] : ['--team-id', account.team])];

/**
 * The binding Make default names the account by: its profile and its team, and `derive_first_name` where the old default
 * was a Reply binding that held it; nothing else of the old route.
 */
export function defaultBinding(account: AccountRef, was: GlobalDefault | null): Record<string, unknown> {
    const derive = was?.plugin === 'reply' ? was.binding?.['derive_first_name'] : undefined;
    return { profile: account.name, ...(account.team === null ? {} : { team_id: account.team }), ...(typeof derive === 'boolean' ? { derive_first_name: derive } : {}) };
}

const shown = (value: string): string => {
    const characters = [...value.replace(/[\p{Cc}\p{Cf}]/gu, ' ').trim()];
    return characters.length > 64 ? `${characters.slice(0, 64).join('')}…` : characters.join('');
};

/** `the Reply account outbound`, `the Reply account outbound, team 3`: an account in the desktop's sentences. */
export const accountNamed = (account: AccountRef): string => `the Reply account ${shown(account.name)}${account.team === null ? '' : `, team ${shown(account.team)}`}`;

/** The global default as the box of Make default names it: a Reply account, or another plugin by its id. */
export function defaultWords(fallback: GlobalDefault, current: string): string {
    if (fallback.plugin !== 'reply') return `the plugin ${shown(fallback.plugin)}`;
    return accountNamed(boundAccount({ profile: text(fallback.binding?.['profile']), team: text(fallback.binding?.['team_id']) }, current));
}

/** The reason the runtime journals with a default Jason Desktop names; fixed text, never a person's. */
export const defaultReason = 'Made the default with Jason Desktop';

/** The desktop's sentences for the accounts: the only words their acts end with. */
export const accountSentences = {
    badAccount: 'That is not a Reply account the desktop listed.',
    profilesUnread: 'The Reply accounts could not be read, so nobody was signed in.',
    profileNotMade: 'The Reply CLI did not make the profile, so nobody was signed in.',
    signInRefused: 'Reply did not sign in.',
    keyRefused: 'Reply did not accept that key.',
    routesUnread: 'Which account is the default could not be read, so nothing was changed.',
    noActor: 'Jason Desktop could not read the name of this computer’s account, which a change of the default is recorded under, so nothing was changed.',
    notRunning: 'Jason is not running, so nothing was changed. Start it, then try again.',
    notPlaced: 'Jason’s Reply plugin could not be placed or loaded, so nothing was changed.',
    orphansSearch: 'A search still followed runs on the account that is the default now, so nothing was changed: stop it, or let it end, then try again.',
    orphansSend: 'A send Reply holds for later runs on the account that is the default now, so nothing was changed: cancel it, or wait until it is sent, then try again.',
    notDefaultAtSignIn: 'Signed in, but this account was not made the default: Make default on its card says why.',
    notRepresentable: 'The settings file cannot hold this account’s name or team as written, so nothing was changed: route a campaign to it with `jason route set`, which keeps it as written.',
} as const;

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
