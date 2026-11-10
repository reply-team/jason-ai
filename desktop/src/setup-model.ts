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
    /** What the card calls the target. */
    targetLabel: string;
    /**
     * Where the target is written: as the notification binding's field (`Notifications:Plugins:<id>:Binding:<field>`)
     * or as the plugin's own configuration (`Plugins:Config:<id>:<field>`).
     */
    target: { binding: string } | { config: string };
    /** The hosts granted under `Plugins:Grants:<id>:Http`; null when the target names the host. */
    http: readonly string[] | null;
}

export const bundledPlugins: readonly BundledPlugin[] = [
    {
        id: 'slack',
        label: 'Slack',
        description: 'Posts a pointer to one channel when Jason starts waiting for you.',
        tokenLabel: 'Bot token',
        targetLabel: 'Channel id',
        target: { binding: 'channel' },
        http: ['slack.com'],
    },
    {
        id: 'telegram',
        label: 'Telegram',
        description: 'Sends a pointer to one chat when Jason starts waiting for you.',
        tokenLabel: 'Bot token',
        targetLabel: 'Chat id or @name',
        target: { binding: 'chat' },
        http: ['api.telegram.org'],
    },
    {
        id: 'ntfy',
        label: 'ntfy',
        description: 'Sends a pointer to one ntfy topic when Jason starts waiting for you. ntfy takes no token here: a token for a server of your own is read from the runtime environment only.',
        tokenLabel: null,
        targetLabel: 'Topic',
        target: { binding: 'topic' },
        http: ['ntfy.sh'],
    },
    {
        id: 'journal-forwarder',
        label: 'Journal forwarder',
        description: 'Posts the journal lines it is granted to one receiver of yours.',
        tokenLabel: 'Bearer token (optional)',
        targetLabel: 'Receiver URL',
        target: { config: 'url' },
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

function setOrRemove(object: Json, name: string, value: string | undefined): void {
    if (value === undefined) return;
    const key = keyOf(object, name);
    if (value === '') delete object[key];
    else object[key] = value;
}

/** A target the card may write: one line, no spaces, and for the forwarder an https URL or http on this machine. */
export function targetProblem(plugin: BundledPlugin, target: string): string | null {
    if (target === '') return null;
    if (target.length > 400 || /\s/.test(target)) return `That is not a ${plugin.targetLabel.toLowerCase()}.`;
    if ('config' in plugin.target) {
        let url: URL;
        try {
            url = new URL(target);
        } catch {
            return 'That is not a web address.';
        }
        const here = url.hostname === '127.0.0.1' || url.hostname === 'localhost';
        if (url.protocol !== 'https:' && !(url.protocol === 'http:' && here)) return 'The receiver is reached over https, or over http on this machine.';
    }
    return null;
}

/**
 * The installation's settings with one plugin configured, as a person would write it: the token under
 * `Plugins:Config:<id>:token`, the hosts under `Plugins:Grants:<id>:Http`, the target as the notification binding or the
 * plugin's own setting. Every other key stays as it was, `Routes` is never touched, and the input is not changed.
 */
export function withPluginConfigured(settings: Json, plugin: BundledPlugin, values: PluginValues): Json {
    if (plugin.tokenLabel === null && values.token !== undefined && values.token !== '') {
        throw new Refused(`${plugin.label} takes no token from the settings file.`);
    }
    const target = values.target?.trim();
    const problem = target === undefined ? null : targetProblem(plugin, target);
    if (problem !== null) throw new Refused(problem);

    const next = structuredClone(settings);
    const config = section(next, ['Plugins', 'Config', plugin.id]);
    setOrRemove(config, 'token', values.token?.trim());

    if ('binding' in plugin.target) {
        if (target !== undefined) {
            const binding = section(next, ['Notifications', 'Plugins', plugin.id, 'Binding']);
            setOrRemove(binding, plugin.target.binding, target);
        }
    } else {
        setOrRemove(config, plugin.target.config, target);
    }

    const hosts = plugin.http ?? (target ? [new URL(target).host] : null);
    if (hosts !== null) {
        const grant = section(next, ['Plugins', 'Grants', plugin.id]);
        grant[keyOf(grant, 'Http')] = [...hosts];
    }
    return next;
}

function at(root: unknown, path: readonly string[]): unknown {
    let here = root;
    for (const name of path) {
        if (typeof here !== 'object' || here === null || Array.isArray(here)) return undefined;
        here = (here as Json)[keyOf(here as Json, name)];
    }
    return here;
}

const present = (value: unknown) => typeof value === 'string' && value !== '';

/** Whether the settings carry what the plugin needs to be invoked: its target, and its token where it takes one. */
export function isConfigured(settings: unknown, plugin: BundledPlugin): boolean {
    const target = 'binding' in plugin.target
        ? at(settings, ['Notifications', 'Plugins', plugin.id, 'Binding', plugin.target.binding])
        : at(settings, ['Plugins', 'Config', plugin.id, plugin.target.config]);
    if (!present(target)) return false;
    if (plugin.id === 'slack' || plugin.id === 'telegram') return present(at(settings, ['Plugins', 'Config', plugin.id, 'token']));
    return true;
}

/** The settings file's text as an object, an absent file as an empty one; anything else is refused, never replaced. */
export function parseInstallationSettings(text: string | null): Json {
    if (text === null) return {};
    let raw: unknown;
    try {
        raw = JSON.parse(text);
    } catch {
        throw new Refused('The settings file is not JSON the desktop can read, so it was left as it is. Fix it, or remove it, and try again.');
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

export function parseReplyAuth(stdout: string): { signedIn: boolean; user: string | null } {
    try {
        const raw = JSON.parse(stdout) as { authenticated?: unknown; user?: { username?: unknown } };
        return { signedIn: raw.authenticated === true, user: typeof raw.user?.username === 'string' ? raw.user.username : null };
    } catch {
        return { signedIn: false, user: null };
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
}

/** What `jason status` printed, read by its checks' names and states; anything unreadable says no runtime. */
export function statusFacts(stdout: string): StatusFacts {
    try {
        const raw = JSON.parse(stdout) as { checks?: { name?: unknown; state?: unknown }[] };
        const state = (name: string) => raw.checks?.find((check) => check.name === name)?.state;
        return { runtimeUp: state('runtime') === 'ok', sameVersion: state('runtime_version') === 'ok' };
    } catch {
        return { runtimeUp: false, sameVersion: false };
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
