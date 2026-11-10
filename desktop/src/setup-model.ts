/** Pure: what the Connect cards know, and what writing a card into the installation's settings changes. */

import type { ProgramName } from './programs.ts';

// ---- The bundled plugins -------------------------------------------------------------------------------------------

/** One of the four plugins the repository ships for notifications and forwarding, as its card configures it. */
export interface BundledPlugin {
    id: 'slack' | 'telegram' | 'ntfy' | 'journal-forwarder';
    label: string;
    description: string;
    /** What the card calls the token, or null for a plugin that takes none from the settings file. */
    tokenLabel: string | null;
    /** What the card calls the server, for the one plugin whose binding takes a server of its own; null for the others. */
    serverLabel: string | null;
    /** What the desktop does not set for this plugin, and where that is set instead. */
    note: string | null;
    /**
     * The one value a card's `target` sets, by the key the plugin reads it under: in the notification binding
     * (`Notifications:Plugins:<id>:Binding:<key>`) or in the plugin's own configuration (`Plugins:Config:<id>:<key>`).
     * `pattern` and `maxLength` are the binding schema's own, from the plugin's manifest; the forwarder's URL is
     * checked by the host rule instead.
     */
    target: { key: 'channel' | 'chat' | 'topic' | 'url'; label: string; where: 'binding' | 'config'; pattern: string | null; maxLength: number };
    /** The hosts granted under `Plugins:Grants:<id>:Http` by default; null when the target names the host. */
    http: readonly string[] | null;
}

/** The pattern ntfy's binding declares for `server`. */
export const serverPattern = '^https?://[^/?#]+$';

export const bundledPlugins: readonly BundledPlugin[] = [
    {
        id: 'slack',
        label: 'Slack',
        description: 'Posts a pointer to one channel when Jason starts waiting for you.',
        tokenLabel: 'Bot token',
        serverLabel: null,
        note: null,
        target: { key: 'channel', label: 'Channel id or name', where: 'binding', pattern: '^(#?[a-z0-9._-]{1,80}|[CDGU][A-Z0-9]{8,})$', maxLength: 400 },
        http: ['slack.com'],
    },
    {
        id: 'telegram',
        label: 'Telegram',
        description: 'Sends a pointer to one chat when Jason starts waiting for you.',
        tokenLabel: 'Bot token',
        serverLabel: null,
        note: null,
        target: { key: 'chat', label: 'Chat id or @name', where: 'binding', pattern: '^(-?[0-9]{1,20}|@[A-Za-z][A-Za-z0-9_]{4,31})$', maxLength: 400 },
        http: ['api.telegram.org'],
    },
    {
        id: 'ntfy',
        label: 'ntfy',
        description: 'Sends a pointer to one ntfy topic when Jason starts waiting for you.',
        tokenLabel: null,
        serverLabel: 'Server (optional)',
        note: 'A server that needs a token reads it from NTFY_TOKEN in the runtime’s environment, granted under Plugins:Grants:ntfy:Env, as the plugin’s README says; the desktop sets neither.',
        target: { key: 'topic', label: 'Topic', where: 'binding', pattern: '^[A-Za-z0-9_-]+$', maxLength: 64 },
        // The public server, by the name the manifest declares; a server of the person's own replaces it.
        http: ['ntfy.sh'],
    },
    {
        id: 'journal-forwarder',
        label: 'Journal forwarder',
        description: 'Posts the journal lines it is granted to one receiver of yours.',
        tokenLabel: 'Bearer token (optional)',
        serverLabel: null,
        note: 'Which journal kinds it forwards is granted under Plugins:Grants:journal-forwarder:Subscriptions, which the desktop does not set; until that grant names a kind, nothing is forwarded.',
        target: { key: 'url', label: 'Receiver URL', where: 'config', pattern: null, maxLength: 400 },
        http: null,
    },
];

export function bundledPlugin(id: unknown): BundledPlugin | undefined {
    return bundledPlugins.find((plugin) => plugin.id === id);
}

/** What a card asks to change: absent leaves a value as it is, an empty string removes it. */
export interface PluginValues {
    token?: string | undefined;
    target?: string | undefined;
    server?: string | undefined;
}

export class Refused extends Error {}

type Json = Record<string, unknown>;

/** The key of an object that equals `name` ignoring case, as the runtime's configuration reads keys. */
function keyOf(object: Json, name: string): string {
    return Object.keys(object).find((key) => key.toLowerCase() === name.toLowerCase()) ?? name;
}

/** The object at a path, made where it is missing; refused when something there is not an object. */
function section(root: Json, path: readonly string[]): Json {
    let here = root;
    for (const name of path) {
        const key = keyOf(here, name);
        const next = here[key];
        if (next === undefined || next === null) {
            const made: Json = {};
            here[key] = made;
            here = made;
        } else if (typeof next === 'object' && !Array.isArray(next)) {
            here = next as Json;
        } else {
            throw new Refused(`The settings file has ${path.join(':')} as something other than an object, so it was left as it is.`);
        }
    }
    return here;
}

/** The object at a path when it is there, made nowhere; refused when something there is not an object. */
function existing(root: Json, path: readonly string[]): Json | undefined {
    let here = root;
    for (const name of path) {
        const next = here[keyOf(here, name)];
        if (next === undefined || next === null) return undefined;
        if (typeof next !== 'object' || Array.isArray(next)) {
            throw new Refused(`The settings file has ${path.join(':')} as something other than an object, so it was left as it is.`);
        }
        here = next as Json;
    }
    return here;
}

/**
 * One value at a path: absent leaves it, empty removes it from a section already there, anything else writes it, making
 * the section only then.
 */
function setOrRemove(root: Json, path: readonly string[], name: string, value: string | undefined): void {
    if (value === undefined) return;
    if (value === '') {
        const object = existing(root, path);
        if (object !== undefined) delete object[keyOf(object, name)];
        return;
    }
    const object = section(root, path);
    object[keyOf(object, name)] = value;
}

/** An address this machine answers on, the one place plain http may go. */
function onThisMachine(url: URL): boolean {
    return url.hostname === 'localhost' || url.hostname === '127.0.0.1' || url.hostname === '[::1]';
}

/** A web address a plugin may be pointed at: https, or http on this machine; the problem with it otherwise. */
function addressProblem(text: string, what: string): string | null {
    let url: URL;
    try {
        url = new URL(text);
    } catch {
        return `That is not a web address for the ${what}.`;
    }
    if (url.protocol !== 'https:' && !(url.protocol === 'http:' && onThisMachine(url))) {
        return `The ${what} is reached over https, or over http on this machine.`;
    }
    return null;
}

/** A target the card may write: present, one line, and the binding's own pattern, or for the forwarder the host rule. */
export function targetProblem(plugin: BundledPlugin, target: string): string | null {
    const named = plugin.target.label.toLowerCase();
    if (target === '') return `Enter the ${named}: ${plugin.label} needs one.`;
    if (target.length > plugin.target.maxLength || /\s/.test(target)) return `That is not a ${named}.`;
    if (plugin.target.pattern !== null && !new RegExp(plugin.target.pattern).test(target)) return `That is not a ${named} ${plugin.label} takes.`;
    if (plugin.target.where === 'config') return addressProblem(target, 'receiver');
    return null;
}

/** A server the card may write for ntfy: the binding's own pattern, then the host rule. */
export function serverProblem(server: string): string | null {
    if (server === '') return null;
    if (/\s/.test(server) || !new RegExp(serverPattern).test(server)) return 'That is not a server address: give its scheme and host, and nothing after.';
    return addressProblem(server, 'server');
}

function at(root: unknown, path: readonly string[]): unknown {
    let here = root;
    for (const name of path) {
        if (typeof here !== 'object' || here === null || Array.isArray(here)) return undefined;
        here = (here as Json)[keyOf(here as Json, name)];
    }
    return here;
}

/** What a plugin's Http grant derives from: the forwarder's receiver, ntfy's own server; undefined for the others. */
function grantSource(settings: Json, plugin: BundledPlugin): unknown {
    if (plugin.target.where === 'config') return at(settings, ['Plugins', 'Config', plugin.id, plugin.target.key]);
    if (plugin.serverLabel !== null) return at(settings, ['Notifications', 'Plugins', plugin.id, 'Binding', 'server']);
    return undefined;
}

/**
 * The installation's settings with one plugin configured, as a person would write it: the token under
 * `Plugins:Config:<id>:token`, the target as the notification binding or the plugin's own setting, for ntfy its server
 * beside the topic, and the hosts under `Plugins:Grants:<id>:Http`. Everything is checked before anything is changed; a
 * section is made only when a value is written into it; the grant is written only where there is none, or where the
 * server or receiver it derives from changed, so a grant a person wrote outlives a new token or topic; every other key
 * stays as it was, `Routes` is never touched, and the input is not changed.
 */
export function withPluginConfigured(settings: Json, plugin: BundledPlugin, values: PluginValues): Json {
    if (plugin.tokenLabel === null && values.token !== undefined && values.token !== '') {
        throw new Refused(`${plugin.label} takes no token from the settings file.`);
    }
    if (plugin.serverLabel === null && values.server !== undefined) throw new Refused(`${plugin.label} takes no server.`);
    const token = values.token?.trim();
    const target = values.target?.trim();
    const server = values.server?.trim();
    const problem = (target === undefined ? null : targetProblem(plugin, target)) ?? (server === undefined ? null : serverProblem(server));
    if (problem !== null) throw new Refused(problem);

    const next = structuredClone(settings);
    const config = ['Plugins', 'Config', plugin.id];
    setOrRemove(next, config, 'token', token);
    if (plugin.target.where === 'binding') {
        const binding = ['Notifications', 'Plugins', plugin.id, 'Binding'];
        setOrRemove(next, binding, plugin.target.key, target);
        setOrRemove(next, binding, 'server', server);
    } else {
        setOrRemove(next, config, plugin.target.key, target);
    }

    const before = grantSource(settings, plugin);
    const after = grantSource(next, plugin);
    const hosts = typeof after === 'string' && after !== '' ? [new URL(after).host] : plugin.http;
    const grants = existing(next, ['Plugins', 'Grants', plugin.id]);
    const granted = grants !== undefined && grants[keyOf(grants, 'Http')] !== undefined;
    const given = [token, target, server].some((value) => value !== undefined && value !== '');
    if (hosts !== null && ((!granted && given) || before !== after)) {
        const grant = section(next, ['Plugins', 'Grants', plugin.id]);
        grant[keyOf(grant, 'Http')] = [...hosts];
    }
    return next;
}

/** The Reply plugin's binding as the golden path writes it: the reply-cli profile the route acts as. */
export interface ReplyWiring {
    settings: Json;
    /** Whether `Routes:Default` was written, which it is only where the file holds none. */
    routeWritten: boolean;
}

/**
 * The installation's settings with Jason's Reply plugin granted its one executable (`Plugins:Grants:reply:Exec`) and,
 * where the file names no global default at all, named the global default with the signed-in profile
 * (`Routes:Default`), as the golden path writes both by hand. A default a person set is never replaced; every other key
 * stays as it was, and the input is not changed.
 */
export function withReplyWired(settings: Json, profile: string): ReplyWiring {
    const next = structuredClone(settings);
    const grant = section(next, ['Plugins', 'Grants', 'reply']);
    grant[keyOf(grant, 'Exec')] = ['reply'];
    const routes = existing(next, ['Routes']);
    const routeWritten = routes === undefined || routes[keyOf(routes, 'Default')] === undefined;
    if (routeWritten) section(next, ['Routes'])[keyOf(routes ?? {}, 'Default')] = { Plugin: 'reply', Binding: { profile } };
    return { settings: next, routeWritten };
}

const present = (value: unknown) => typeof value === 'string' && value !== '';

/** Whether the settings carry what the plugin needs to be invoked: its target, and its token where it requires one. */
export function isConfigured(settings: unknown, plugin: BundledPlugin): boolean {
    const target = plugin.target.where === 'binding'
        ? at(settings, ['Notifications', 'Plugins', plugin.id, 'Binding', plugin.target.key])
        : at(settings, ['Plugins', 'Config', plugin.id, plugin.target.key]);
    if (!present(target)) return false;
    if (plugin.id === 'slack' || plugin.id === 'telegram') return present(at(settings, ['Plugins', 'Config', plugin.id, 'token']));
    return true;
}

/** Whether the settings hold a notification binding for the plugin; null for one that takes none. */
export function hasBinding(settings: unknown, plugin: BundledPlugin): boolean | null {
    if (plugin.target.where !== 'binding') return null;
    const binding = at(settings, ['Notifications', 'Plugins', plugin.id, 'Binding']);
    return typeof binding === 'object' && binding !== null && !Array.isArray(binding);
}

/** The settings file's text as an object, an absent file as an empty one; anything else is refused, never replaced. */
export function parseInstallationSettings(text: string | null): Json {
    if (text === null) return {};
    let raw: unknown;
    try {
        raw = JSON.parse(text);
    } catch {
        throw new Refused(
            'The settings file is not plain JSON, so the desktop left it as it is: comments and trailing commas, which the runtime reads, are among what the desktop cannot keep when it writes the file.',
        );
    }
    if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) {
        throw new Refused('The settings file does not hold a JSON object, so it was left as it is.');
    }
    return raw as Json;
}

// ---- Programs ------------------------------------------------------------------------------------------------------

export interface ProgramInstall {
    /** What a person types to install it, shown on the card. */
    command: string;
    /** Where it is explained. */
    link: string;
    /** The npm package the desktop installs on a click, or null for a program it never installs. */
    npmPackage: string | null;
}

export const programInstalls: Readonly<Partial<Record<ProgramName, ProgramInstall>>> = {
    claude: { command: 'npm install -g @anthropic-ai/claude-code', link: 'https://docs.anthropic.com/en/docs/claude-code/setup', npmPackage: '@anthropic-ai/claude-code' },
    codex: { command: 'npm install -g @openai/codex', link: 'https://github.com/openai/codex', npmPackage: '@openai/codex' },
    reply: { command: 'npm install -g reply-cli', link: 'https://www.npmjs.com/package/reply-cli', npmPackage: 'reply-cli' },
    node: { command: 'Install Node.js from nodejs.org', link: 'https://nodejs.org/en/download', npmPackage: null },
};

/** The first version number in what a program printed about itself. */
export function versionIn(text: string): string | null {
    return /\d+\.\d+\.\d+/.exec(text)?.[0] ?? null;
}

export function versionAtLeast(found: string, wanted: string): boolean {
    const parts = (version: string) => version.split('.').map(Number);
    const [a, b] = [parts(found), parts(wanted)];
    for (let index = 0; index < 3; index++) {
        const difference = (a[index] ?? 0) - (b[index] ?? 0);
        if (difference !== 0) return difference > 0;
    }
    return true;
}

// ---- Reply ---------------------------------------------------------------------------------------------------------

export const replyMinimumVersion = '0.18.0';

export interface ReplyAccount {
    id: string;
    channel: string;
    name: string;
    address: string;
    status: string;
}

// The two answers of the Reply CLI read here, in the shapes reply-cli 0.18.0 and 0.19.0 print (unchanged between them):
//
// `reply auth status --json` (src/commands/auth.ts, the object built in src/auth/status.ts) prints
//   {"authenticated": bool, "profile": string, "source"?, "method"?, "user"?: {"id"?, "username"?, "team_id"?},
//    "expires_at"?, "expired"?, "team_id"?, "acting_user_id"?, "acting_email"?}
// and exits 1 when the profile holds no credential.
//
// `reply account list --json` (src/operations/commands.ts, src/operations/accounts.ts) prints
//   {"items": [{"id", "channel", "name", "address", "owner_user_id", "status", "reply_status"}]}
//
// `--json` is the CLI's global flag for compact output (src/index.ts, src/utils/output.ts); without it the same object is
// printed indented, so it is passed to both.

/** The profile a route's binding may name: the Reply plugin's binding schema takes one to sixty-four characters. */
const profileLimit = 64;

export function parseReplyAuth(stdout: string): { signedIn: boolean; user: string | null; profile: string | null } {
    try {
        const raw = JSON.parse(stdout) as { authenticated?: unknown; profile?: unknown; user?: { username?: unknown } };
        const profile = typeof raw.profile === 'string' && raw.profile.length > 0 && raw.profile.length <= profileLimit ? raw.profile : null;
        return { signedIn: raw.authenticated === true, user: typeof raw.user?.username === 'string' ? raw.user.username : null, profile };
    } catch {
        return { signedIn: false, user: null, profile: null };
    }
}

/** The plugin `jason route list` names as the global default, or null for none or an answer it cannot read. */
export function defaultRoutePlugin(stdout: string): string | null {
    try {
        const raw = JSON.parse(stdout) as { global?: { default?: { plugin_id?: unknown } | null } };
        const plugin = raw.global?.default?.plugin_id;
        return typeof plugin === 'string' ? plugin : null;
    } catch {
        return null;
    }
}

export function parseReplyAccounts(stdout: string): ReplyAccount[] {
    try {
        const raw = JSON.parse(stdout) as { items?: Record<string, unknown>[] };
        return (raw.items ?? []).map((item) => ({
            id: String(item['id'] ?? ''),
            channel: String(item['channel'] ?? ''),
            name: String(item['name'] ?? ''),
            address: String(item['address'] ?? ''),
            status: String(item['status'] ?? ''),
        }));
    } catch {
        return [];
    }
}

// ---- What the CLI said ---------------------------------------------------------------------------------------------

export interface StatusFacts {
    /** Whether `jason status` found a runtime answering. */
    runtimeUp: boolean;
    /** Whether the runtime answering is the build of the CLI that asked, which is the bundle's. */
    sameVersion: boolean;
    /** Whether `ui_module` is anything but ok: no UI, one with a problem, one for another API, or a stale default. */
    uiNeeded: boolean;
    /** Whether a UI is served all the same: `ui_module` is failed only while one is, and absent when none is. */
    uiServed: boolean;
}

/** What `jason status` printed, read by its checks' names and states; anything unreadable says no runtime. */
export function statusFacts(stdout: string): StatusFacts {
    try {
        const raw = JSON.parse(stdout) as { checks?: { name?: unknown; state?: unknown }[] };
        const state = (name: string) => raw.checks?.find((check) => check.name === name)?.state;
        const ui = state('ui_module');
        return { runtimeUp: state('runtime') === 'ok', sameVersion: state('runtime_version') === 'ok', uiNeeded: ui !== 'ok', uiServed: ui === 'ok' || ui === 'failed' };
    } catch {
        return { runtimeUp: false, sameVersion: false, uiNeeded: true, uiServed: false };
    }
}

/** The runtime's version from what `jason runtime status` printed, or null. */
export function runtimeVersionIn(stdout: string): string | null {
    try {
        const raw = JSON.parse(stdout) as { runtime_version?: unknown };
        return typeof raw.runtime_version === 'string' ? raw.runtime_version : null;
    } catch {
        return null;
    }
}

/** Whether `jason plugin list` shows a plugin loaded and valid. */
export function pluginValid(stdout: string, id: string): boolean {
    try {
        const raw = JSON.parse(stdout) as { plugins?: { id?: unknown; status?: unknown }[] };
        return (raw.plugins ?? []).some((plugin) => plugin.id === id && plugin.status === 'valid');
    } catch {
        return false;
    }
}

/** What the runtime answered for one plugin: whether it is valid, and how many grants it holds across its capabilities. */
export interface PluginRuntime {
    valid: boolean;
    grants: number;
}

/** Each plugin `jason plugin list` printed, by id; an empty map when it printed nothing readable. */
export function pluginRuntimes(stdout: string): Map<string, PluginRuntime> {
    const answered = new Map<string, PluginRuntime>();
    let raw: { plugins?: { id?: unknown; status?: unknown; capabilities?: unknown }[] };
    try {
        raw = JSON.parse(stdout);
    } catch {
        return answered;
    }
    for (const plugin of Array.isArray(raw?.plugins) ? raw.plugins : []) {
        if (typeof plugin?.id !== 'string') continue;
        let grants = 0;
        const capabilities = plugin.capabilities;
        if (typeof capabilities === 'object' && capabilities !== null) {
            for (const capability of Object.values(capabilities)) {
                const granted = (capability as { granted?: unknown } | null)?.granted;
                if (Array.isArray(granted)) grants += granted.length;
            }
        }
        answered.set(plugin.id, { valid: plugin.status === 'valid', grants });
    }
    return answered;
}

