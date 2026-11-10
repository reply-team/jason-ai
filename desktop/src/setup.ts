/**
 * The Connect cards: what `window.jasonDesktop.setup` answers and does. Every change to the installation goes through
 * the CLI (`jason plugin install`, `jason plugin reload`) or through the settings file a person would edit; a Reply key
 * goes to the Reply CLI on its standard input and is kept by it alone; a provider's key goes to encrypted storage.
 */

import { cp, mkdtemp, readdir, rm } from 'node:fs/promises';
import { mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import type { Cli } from './cli.ts';
import { feedArguments } from './installation.ts';
import type { Logger } from './log.ts';
import type { DesktopPaths } from './paths.ts';
import { isProgramName, type ProgramName, type Programs, type RunResult } from './programs.ts';
import { hosts, keyProvider, keyProviders } from './providers-model.ts';
import type { ProviderKeys } from './providers.ts';
import type { DesktopSettings } from './settings.ts';
import {
    bundledPlugin,
    bundledPlugins,
    hasBinding,
    isConfigured,
    parseInstallationSettings,
    parseReplyAccounts,
    parseReplyAuth,
    pluginRuntimes,
    programInstalls,
    Refused,
    versionIn,
    withPluginConfigured,
    type PluginRuntime,
    type PluginValues,
    type ReplyAccount,
} from './setup-model.ts';

export interface HostState {
    id: string;
    label: string;
    kind: 'program' | 'provider';
    found: boolean;
    version: string | null;
    drivesChat: boolean;
    /** The `installProgram` id when the program is missing and npm is there to install it. */
    install: string | null;
}

export interface ReplyState {
    cli: { found: boolean; version: string | null; install: string | null };
    signedIn: boolean;
    user: string | null;
    wired: boolean;
    accounts: ReplyAccount[];
}

export interface PluginState {
    id: string;
    label: string;
    description: string;
    target: { key: 'channel' | 'chat' | 'topic' | 'url'; label: string };
    tokenLabel: string | null;
    serverLabel: string | null;
    note: string | null;
    configured: boolean;
    runtime: PluginRuntime | null;
    binding: boolean | null;
}

export interface SetupState {
    hosts: HostState[];
    reply: ReplyState;
    runtimeVersion: string | null;
    bundleVersion: string;
    /**
     * When the runtime is another version than the one the desktop carries, the sentence that says so and names
     * `jason update`; null when they are one version or no runtime answers.
     */
    versionsDiffer: string | null;
    plugins: PluginState[];
    /**
     * Why the settings file could not be read, in the words of the desktop's parse or of the runtime's last reload
     * (`settings_file_unreadable`); null when it could.
     */
    settingsRefused: string | null;
}

export interface SetupDeps {
    paths: DesktopPaths;
    cli: Cli;
    programs: Programs;
    keys: ProviderKeys;
    log: Logger;
    bundleVersion: string;
    runtimeVersion(): string | null;
    settings(): DesktopSettings;
    clock?: () => number;
}

const signInLimit = 5 * 60_000;
const installLimit = 5 * 60_000;

function readText(file: string): string | null {
    try {
        return readFileSync(file, 'utf8');
    } catch (error) {
        if ((error as NodeJS.ErrnoException).code === 'ENOENT') return null;
        throw new Refused('The settings file could not be read, so it was left as it is.');
    }
}

/** Writes a file through one beside it renamed over it, so a reader sees the old text or the new and never half. */
function writeAtomically(file: string, text: string): void {
    mkdirSync(dirname(file), { recursive: true });
    const temporary = `${file}.${process.pid}.tmp`;
    writeFileSync(temporary, text, { mode: 0o600 });
    renameSync(temporary, file);
}

function words(result: RunResult, otherwise: string): string {
    return (result.stderr.trim() || result.stdout.trim() || otherwise).slice(-1000);
}

/** The words of a reload refused because the settings file could not be read, or null for any other answer. */
export function settingsUnreadable(stdout: string): string | null {
    try {
        const raw = JSON.parse(stdout) as { error?: { details?: { code?: unknown; message?: unknown }[] } };
        const details = Array.isArray(raw.error?.details) ? raw.error.details : [];
        const detail = details.find((entry) => entry?.code === 'settings_file_unreadable');
        return typeof detail?.message === 'string' ? detail.message : null;
    } catch {
        return null;
    }
}

/** Pure: what the system screen says when the runtime and the desktop are two versions. */
export function versionsDifferSentence(runtimeVersion: string | null, bundleVersion: string): string | null {
    if (runtimeVersion === null || runtimeVersion === bundleVersion) return null;
    return `The runtime is ${runtimeVersion} and this desktop carries ${bundleVersion}, so the UI the runtime serves is shown as it is. Run \`jason update\` to bring them to one version.`;
}

export class Setup {
    #deps: SetupDeps;
    #listeners = new Set<() => void>();
    #versions = new Map<ProgramName, string | null>();
    #queue: Promise<unknown> = Promise.resolve();
    /** What the last reload said about the settings file, when it could not read it. */
    #reloadRefusal: string | null = null;

    constructor(deps: SetupDeps) {
        this.#deps = deps;
    }

    onChanged(listener: () => void): () => void {
        this.#listeners.add(listener);
        return () => this.#listeners.delete(listener);
    }

    #changed(): void {
        for (const listener of this.#listeners) listener();
    }

    /** One change at a time: two cards clicked together never write the settings file over each other. */
    #serial<T>(work: () => Promise<T>): Promise<T> {
        const next = this.#queue.then(work, work);
        this.#queue = next.catch(() => undefined);
        return next;
    }

    async #run(program: ProgramName, verb: string, args: readonly string[], options?: { stdin?: string; timeoutMs?: number; cwd?: string }): Promise<RunResult> {
        const clock = this.#deps.clock ?? Date.now;
        const started = clock();
        const result = await this.#deps.programs.run(program, args, options);
        this.#deps.log.info('program_exited', { Program: program, Verb: verb, Exit: result.exit, ElapsedMs: clock() - started });
        return result;
    }

    async #version(program: ProgramName): Promise<string | null> {
        if (this.#versions.has(program)) return this.#versions.get(program) ?? null;
        const version = versionIn((await this.#run(program, '--version', ['--version'], { timeoutMs: 15_000 })).stdout);
        this.#versions.set(program, version);
        return version;
    }

    /** The id `installProgram` takes for a program missing here, when the desktop installs it and npm is there to. */
    #installable(program: ProgramName): string | null {
        return programInstalls[program]?.npmPackage && this.#deps.programs.find('npm') !== null ? program : null;
    }

    async status(): Promise<SetupState> {
        const { programs, keys } = this.#deps;
        const stored = new Set(keys.stored());
        const programHosts: HostState[] = [];
        for (const host of hosts) {
            const found = programs.find(host.program) !== null;
            programHosts.push({
                id: host.id,
                label: host.label,
                kind: 'program',
                found,
                version: found ? await this.#version(host.program) : null,
                drivesChat: host.drivesChat,
                install: found ? null : this.#installable(host.program),
            });
        }
        const providerHosts: HostState[] = keyProviders.map((provider) => ({
            id: provider.id,
            label: provider.label,
            kind: 'provider',
            found: stored.has(provider.id),
            version: null,
            drivesChat: hosts.find((host) => host.program === provider.program)?.drivesChat ?? false,
            install: null,
        }));

        let settings: unknown = {};
        let settingsRefused = this.#reloadRefusal;
        try {
            settings = parseInstallationSettings(readText(this.#deps.paths.installationSettings));
        } catch (error) {
            settings = {};
            settingsRefused = (error as Error).message;
        }

        // One question to the runtime answers both the Reply card and the plugins' cards.
        const listed = await this.#deps.cli.run(['plugin', 'list']);
        const answered = listed.exit === 0 ? pluginRuntimes(listed.stdout) : new Map<string, PluginRuntime>();

        const runtimeVersion = this.#deps.runtimeVersion();
        return {
            hosts: [...programHosts, ...providerHosts],
            reply: await this.#reply(answered.get('reply')?.valid ?? false),
            runtimeVersion,
            bundleVersion: this.#deps.bundleVersion,
            versionsDiffer: versionsDifferSentence(runtimeVersion, this.#deps.bundleVersion),
            plugins: bundledPlugins.map((plugin) => ({
                id: plugin.id,
                label: plugin.label,
                description: plugin.description,
                target: { key: plugin.target.key, label: plugin.target.label },
                tokenLabel: plugin.tokenLabel,
                serverLabel: plugin.serverLabel,
                note: plugin.note,
                configured: isConfigured(settings, plugin),
                runtime: answered.get(plugin.id) ?? null,
                binding: hasBinding(settings, plugin),
            })),
            settingsRefused,
        };
    }

    async #reply(wired: boolean): Promise<ReplyState> {
        const found = this.#deps.programs.find('reply') !== null;
        if (!found) {
            return { cli: { found, version: null, install: this.#installable('reply') }, signedIn: false, user: null, wired: false, accounts: [] };
        }
        const version = await this.#version('reply');
        const auth = parseReplyAuth((await this.#run('reply', 'auth status', ['auth', 'status', '--json'])).stdout);
        const accounts = auth.signedIn ? parseReplyAccounts((await this.#run('reply', 'account list', ['account', 'list'])).stdout) : [];
        return { cli: { found, version, install: null }, signedIn: auth.signedIn, user: auth.user, wired, accounts };
    }

    connectReply(): Promise<void> {
        return this.#serial(async () => {
            const result = await this.#run('reply', 'auth login', ['auth', 'login'], { timeoutMs: signInLimit });
            this.#deps.log.info('reply_connected', { Method: 'browser', Exit: result.exit });
            if (result.exit !== 0) throw new Refused(words(result, 'Reply did not sign in.'));
            await this.#deployReplySkills();
            this.#changed();
        });
    }

    connectReplyWithKey(key: unknown): Promise<void> {
        return this.#serial(async () => {
            if (typeof key !== 'string' || key.trim() === '') throw new Refused('Enter the key.');
            const result = await this.#run('reply', 'auth login --with-token', ['auth', 'login', '--with-token'], { stdin: key.trim(), timeoutMs: signInLimit });
            this.#deps.log.info('reply_connected', { Method: 'key', Exit: result.exit });
            if (result.exit !== 0) throw new Refused(words(result, 'Reply did not accept that key.'));
            await this.#deployReplySkills();
            this.#changed();
        });
    }

    /**
     * The Reply skills beside Jason's, in the chat folder. The Reply CLI writes them as plain folders only for hosts other
     * than Claude Code, so they are written to a folder of ours for one of those and the Reply ones copied across; the
     * sales skills it adds with them are an older copy of Jason's own and are left out.
     */
    async #deployReplySkills(): Promise<void> {
        const target = join(this.#deps.paths.chat, '.claude', 'skills');
        const place = await mkdtemp(join(tmpdir(), 'jason-reply-skills-'));
        try {
            const answer = await this.#run('reply', 'skills install', ['skills', 'install', 'adapter', '--project', '--agent', 'codex'], { cwd: place, timeoutMs: installLimit });
            if (answer.exit !== 0) return;
            const source = join(place, '.agents', 'skills');
            for (const name of await readdir(source).catch(() => [] as string[])) {
                if (!name.startsWith('reply-')) continue;
                await rm(join(target, name), { recursive: true, force: true });
                await cp(join(source, name), join(target, name), { recursive: true });
            }
        } finally {
            await rm(place, { recursive: true, force: true });
        }
    }

    setProviderKey(providerId: unknown, key: unknown): Promise<void> {
        return this.#serial(async () => {
            if (typeof providerId !== 'string' || keyProvider(providerId) === undefined) throw new Refused('That is not a provider the desktop keeps a key for.');
            if (typeof key !== 'string') throw new Refused('Enter the key.');
            const done = this.#deps.keys.set(providerId, key);
            this.#deps.log.info(done === 'stored' ? 'provider_key_stored' : 'provider_key_removed', { ProviderId: providerId });
            this.#changed();
        });
    }

    /** Runs the npm install of a missing program, on a person's click only, and only where npm is there. */
    installProgram(programId: unknown): Promise<void> {
        return this.#serial(async () => {
            const install = isProgramName(programId) ? programInstalls[programId] : undefined;
            if (!isProgramName(programId) || install === undefined || install.npmPackage === null) throw new Refused('The desktop does not install that.');
            if (this.#deps.programs.find('npm') === null) {
                throw new Refused(`Installing this needs Node.js and npm, which are not on this computer. Install Node.js from ${programInstalls.node?.link}, then try again.`);
            }
            const result = await this.#run('npm', 'install', ['install', '-g', install.npmPackage], { timeoutMs: installLimit });
            this.#deps.log.info('program_installed', { Program: programId, Exit: result.exit });
            this.#versions.delete(programId);
            if (result.exit !== 0) throw new Refused(words(result, `npm ended with code ${result.exit}.`));
            this.#changed();
        });
    }

    /**
     * Places one of the four bundled plugins with the CLI, writes its token, its target, ntfy's server and its grant into
     * the installation's settings as a person would, and asks the runtime to load its plugins again. With no values it
     * only asks for the reload, which is what Reload is. A settings file the desktop cannot read is refused and left as
     * it is. The token travels in that file only: never in an argument, an environment or a log.
     */
    configurePlugin(pluginId: unknown, values: unknown): Promise<void> {
        return this.#serial(async () => {
            const plugin = bundledPlugin(pluginId);
            if (plugin === undefined) throw new Refused('That is not a plugin the desktop configures.');
            const asked = (typeof values === 'object' && values !== null ? values : {}) as Record<string, unknown>;
            const wanted: PluginValues = {
                token: typeof asked.token === 'string' ? asked.token : undefined,
                target: typeof asked.target === 'string' ? asked.target : undefined,
                server: typeof asked.server === 'string' ? asked.server : undefined,
            };
            if (wanted.token === undefined && wanted.target === undefined && wanted.server === undefined) {
                await this.#reload(plugin.id, wanted);
                return;
            }

            const file = this.#deps.paths.installationSettings;
            let current: Record<string, unknown>;
            try {
                current = parseInstallationSettings(readText(file));
            } catch (error) {
                this.#deps.log.warn('settings_refused', { Path: file });
                throw error;
            }
            const next = withPluginConfigured(current, plugin, wanted);

            const placed = await this.#deps.cli.run(['plugin', 'install', plugin.id, ...feedArguments(this.#deps.settings())], { timeoutMs: installLimit });
            if (placed.exit !== 0) throw new Refused(words(placed, `jason plugin install ${plugin.id} did not place the plugin.`));

            // Read again just before writing, so an edit made while the package was fetched is not lost.
            const latest = parseInstallationSettings(readText(file));
            const merged = JSON.stringify(latest) === JSON.stringify(current) ? next : withPluginConfigured(latest, plugin, wanted);
            writeAtomically(file, `${JSON.stringify(merged, null, 2)}\n`);
            await this.#reload(plugin.id, wanted);
        });
    }

    /** Asks the runtime to read its plugins again, and keeps what it said about the settings file for `status()`. */
    async #reload(pluginId: string, wanted: PluginValues): Promise<void> {
        const reload = await this.#deps.cli.run(['plugin', 'reload']);
        this.#reloadRefusal = reload.exit === 0 ? null : settingsUnreadable(reload.stdout);
        this.#deps.log.info('plugin_configured', {
            PluginId: pluginId,
            TokenChanged: wanted.token !== undefined,
            TargetChanged: wanted.target !== undefined,
            ServerChanged: wanted.server !== undefined,
            Exit: reload.exit,
        });
        this.#changed();
        if (reload.exit !== 0) throw new Refused(this.#reloadRefusal ?? words(reload, 'The runtime did not load the plugins again.'));
    }
}
