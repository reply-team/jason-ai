/**
 * The Connect cards: what `window.jasonDesktop.setup` answers and does. Every change to the installation goes through
 * the CLI (`jason plugin install`, `jason plugin reload`) or through the settings file a person would edit; a Reply key
 * goes to the Reply CLI on its standard input and is kept by it alone; a provider's key goes to encrypted storage.
 */

import { cp, mkdtemp, readdir, rm } from 'node:fs/promises';
import { mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { deployReplySkills } from './reply-skills.ts';
import { offeredModels, type KeptLists, type ModelOffer } from './chat-models.ts';
import type { Cli, CliResult } from './cli.ts';
import { feedArguments } from './installation.ts';
import type { Logger } from './log.ts';
import { floorSentence, printedVersion, versionLimitMs } from './host-version.ts';
import { codexFullAccess, fullAccessNotYet, fullAccessOffered, fullAccessSentence } from './chat-modes.ts';
import type { DesktopPaths } from './paths.ts';
import { isProgramName, type ProgramName, type Programs, type RunResult } from './programs.ts';
import { hosts, keyProvider, keyProviders } from './providers-model.ts';
import type { ProviderKeys } from './providers.ts';
import {
    answeredWell,
    checkedLinkedInLink,
    isMailboxPage,
    isReplyId,
    linkedInFloorVersion,
    linkedInNameProblem,
    linkedInRefusal,
    mailboxAddress,
    mailboxAnswer,
    mailboxRefusal,
    mailboxToken,
    pendingRows,
    readReplyApi,
    replyApiArguments,
    replyApiLimitMs,
    replyLinkSentences,
    withoutLink,
    type ReplyApiAct,
    type ReplyApiAnswer,
    type ReplyPending,
} from './reply-links.ts';
import type { DesktopSettings } from './settings.ts';
import {
    bundledPlugin,
    bundledPlugins,
    defaultRoutePlugin,
    hasBinding,
    isConfigured,
    parseInstallationSettings,
    parseReplyAccounts,
    readReplyChannel,
    replyChannels,
    type ReplyChannel,
    parseReplyAuth,
    pluginRuntimes,
    programInstalls,
    replyMinimumVersion,
    Refused,
    versionAtLeast,
    versionIn,
    withPluginConfigured,
    withPluginDisconnected,
    withReplyWired,
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
    /**
     * When the Claude Code the host runs on is older than the oldest version the chat runs on, or its version could not
     * be read, the sentence that says so, names the floor and the cure: the chat starts no turn on the host. Null
     * otherwise, and for a host that is not Claude Code or a key provider on it.
     */
    belowFloor: string | null;
    /**
     * Why Full access cannot be chosen for a chat on this host, in the desktop's words: Codex has none, and a Claude Code
     * older than the oldest version a Full access turn runs on, or whose version could not be read, says so
     * (`fullAccessSentence`). Null where it can be chosen, and for a host that does not drive the chat.
     */
    fullAccess: string | null;
    /**
     * The models the chat's box offers on this host, each with the effort levels it takes: Claude Code's aliases at the
     * version read now, the list kept of what Codex offered, or a key provider's table (`offeredModels`).
     */
    models: ModelOffer[];
    /** Whether the box also takes a model's full name typed by the person: on Claude Code and on Anthropic's key. */
    typedModel: boolean;
    /**
     * Where the host's models are asked of it and no list was ever read, the desktop's one line saying so and that the
     * turn uses the host's own default model; null otherwise.
     */
    modelsUnread: string | null;
}

export interface ReplyState {
    cli: { found: boolean; version: string | null; install: string | null };
    signedIn: boolean;
    user: string | null;
    wired: boolean;
    accounts: ReplyAccount[];
    /** The channels the CLI says are not in this account's plan or role (`email`, `linkedin`). */
    absent: ReplyChannel[];
    /** The channels whose list could not be read, for any other reason. The other channel's rows stay in both cases. */
    unread: ReplyChannel[];
    /**
     * The LinkedIn connections started from a link and not finished yet, read where LinkedIn was listed; never with their
     * link, which `replyLinkedInPendingLink` asks again on the person's click.
     */
    linkedinPending: ReplyPending[];
    /**
     * Why the LinkedIn link acts are off, where the Reply CLI is signed in but older than the one that reads a body from
     * standard input; null otherwise.
     */
    linkedinBelowFloor: string | null;
}

/**
 * A link Reply answered for connecting or reconnecting an account, checked: what the person opens or sends to whoever
 * owns the account, the host it leads to, and when it stops working where Reply said.
 */
export interface ReplyLink {
    kind: 'linkedin' | 'mailbox';
    link: string;
    host: string;
    expiresAtUtc: string | null;
}

/** A link's ask that ended without one, in one sentence of the desktop's. */
export interface LinkRefusal {
    refused: string;
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
     * When the runtime is another version than the one the desktop carries, the sentence that says so and names the way
     * to one version: Check for Updates when the runtime runs from the jason the desktop carries, which `jason update`
     * refuses to change, and `jason update` when it is another installation's; null when they are one version or no
     * runtime answers.
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
    /** Whether the runtime runs from the jason the desktop carries (`own-runtime.ts`), as the start found it; absent, it does not. */
    runtimeIsOurs?(): boolean;
    settings(): DesktopSettings;
    clock?: () => number;
    /** Whether Full access is offered; absent, this version's switch (`fullAccessOffered`). */
    fullAccessOffered?: boolean;
    /**
     * The person's yes, asked by the main process in a native box before the mailbox page is asked of Reply; absent, the
     * answer is no.
     */
    confirmMailboxLink?(): Promise<boolean>;
    /** The models Codex offered when last asked, and the background ask of the ones a day old (`model-lists.ts`). */
    models?: { lists(): KeptLists; refresh(hostIds: readonly string[]): void; unread(hostId: string): 'waiting' | 'failed' | null };
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

/**
 * Writes a file through one beside it renamed over it, so a reader sees the old text or the new and never half; a rename
 * that fails takes the file beside it away again.
 */
export function writeAtomically(file: string, text: string): void {
    mkdirSync(dirname(file), { recursive: true });
    const temporary = `${file}.${process.pid}.tmp`;
    try {
        writeFileSync(temporary, text, { mode: 0o600 });
        renameSync(temporary, file);
    } catch (error) {
        rmSync(temporary, { force: true });
        throw error;
    }
}

/** What the page adds to `settingsRefused`, and what a refusal thrown for the same file ends with. */
const editByHand = 'Edit it by hand, then press Reload.';

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

/**
 * Pure: what the system screen says when the runtime and the desktop are two versions. A runtime that runs from the jason
 * the desktop carries is brought to one version by the desktop's own update, since `jason update` refuses to change a
 * carried file; another installation's runtime by `jason update`.
 */
export function versionsDifferSentence(runtimeVersion: string | null, bundleVersion: string, ours: boolean): string | null {
    // A build after a plus names the commit, not another version.
    const bare = (version: string) => version.split('+')[0];
    if (runtimeVersion === null || bare(runtimeVersion) === bare(bundleVersion)) return null;
    // A Windows or Linux window shows no menu bar, so the tray's menu is named too, in the CLI's own words.
    const way = ours
        ? "Choose Check for Updates… in the desktop's Jason menu, or in the menu of its icon in the tray,"
        : 'Run `jason update`';
    return `The runtime is ${runtimeVersion} and this desktop carries ${bundleVersion}, so the UI the runtime serves is shown as it is. ${way} to bring them to one version.`;
}

/** The line a host whose models were never read shows instead of its list. */
export function unreadSentence(label: string, unread: 'waiting' | 'failed' | null): string | null {
    if (unread === 'failed') return `${label}'s models could not be read, so the turn uses its own default model.`;
    if (unread === 'waiting') return `${label}'s models are being read; until then the turn uses its own default model.`;
    return null;
}

function field(data: unknown, name: string): unknown {
    return typeof data === 'object' && data !== null && !Array.isArray(data) ? (data as Record<string, unknown>)[name] : undefined;
}

/** A LinkedIn link answered, checked; refused in one sentence where it is not of Reply's forms. */
function linkedIn(value: unknown, expiresAtUtc: string | null): ReplyLink | LinkRefusal {
    const checked = checkedLinkedInLink(value);
    return checked === null ? { refused: replyLinkSentences.badLink } : { kind: 'linkedin', link: checked.link, host: checked.host, expiresAtUtc };
}

export class Setup {
    #deps: SetupDeps;
    #listeners = new Set<() => void>();
    #versions = new Map<ProgramName, string | null>();
    #queue: Promise<unknown> = Promise.resolve();
    /** What the last reload said about the settings file, when it could not read it. */
    #reloadRefusal: string | null = null;
    /** Whether a link is being asked of Reply: one at a time, and a second click meanwhile is refused. */
    #linking = false;

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

    /**
     * Claude Code's version, read at each listing and never remembered, and the sentences when it is below the floor and
     * below the floor of Full access.
     */
    async #claude(): Promise<{ version: string | null; belowFloor: string | null; fullAccess: string | null }> {
        const version = printedVersion(await this.#run('claude', '--version', ['--version'], { timeoutMs: versionLimitMs }));
        return { version, belowFloor: floorSentence(version), fullAccess: fullAccessSentence(version) };
    }

    async status(): Promise<SetupState> {
        const { programs, keys } = this.#deps;
        const stored = new Set(keys.stored());
        const claude = programs.find('claude') !== null ? await this.#claude() : null;
        const kept = this.#deps.models?.lists() ?? new Map();
        const offered = (id: string, label: string) => {
            const { models, typed } = offeredModels(id, kept, claude?.version ?? null);
            return { models, typedModel: typed, modelsUnread: unreadSentence(label, this.#deps.models?.unread(id) ?? null) };
        };
        // Codex has no Full access; Claude Code has it from its floor of Full access. A Claude Code not found offers nothing yet.
        // While this version does not offer it, every host that drives the chat says so.
        const offeredHere = this.#deps.fullAccessOffered ?? fullAccessOffered;
        const fullAccessOn = (program: string, drives: boolean) =>
            !drives ? null : !offeredHere ? fullAccessNotYet : program === 'codex' ? codexFullAccess : program === 'claude' ? (claude === null ? null : claude.fullAccess) : null;
        const programHosts: HostState[] = [];
        for (const host of hosts) {
            const found = programs.find(host.program) !== null;
            const own = host.program === 'claude' ? claude : null;
            programHosts.push({
                id: host.id,
                label: host.label,
                kind: 'program',
                found,
                version: !found ? null : own !== null ? own.version : await this.#version(host.program),
                drivesChat: host.drivesChat,
                install: found ? null : this.#installable(host.program),
                belowFloor: own?.belowFloor ?? null,
                fullAccess: fullAccessOn(host.program, host.drivesChat),
                ...offered(host.id, host.label),
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
            belowFloor: provider.program === 'claude' ? (claude?.belowFloor ?? null) : null,
            fullAccess: fullAccessOn(provider.program, hosts.find((host) => host.program === provider.program)?.drivesChat ?? false),
            ...offered(provider.id, provider.label),
        }));
        // Shown from what is kept; Codex is asked again in the background where it is on the PATH or its key is stored.
        this.#deps.models?.refresh([
            ...(programs.find('codex') !== null ? ['codex'] : []),
            ...(stored.has('codex-api-key') ? ['codex-api-key'] : []),
        ]);

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
            versionsDiffer: versionsDifferSentence(runtimeVersion, this.#deps.bundleVersion, this.#deps.runtimeIsOurs?.() ?? false),
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

    /** Wired: Jason's Reply plugin is valid, and the global default the runtime froze is Reply. */
    async #reply(valid: boolean): Promise<ReplyState> {
        const found = this.#deps.programs.find('reply') !== null;
        if (!found) {
            return { cli: { found, version: null, install: this.#installable('reply') }, signedIn: false, user: null, wired: false, accounts: [], absent: [], unread: [], linkedinPending: [], linkedinBelowFloor: null };
        }
        const routes = valid ? await this.#deps.cli.run(['route', 'list']) : null;
        const wired = routes !== null && routes.exit === 0 && defaultRoutePlugin(routes.stdout) === 'reply';
        const version = await this.#version('reply');
        // Older than Jason's Reply plugin requires, the CLI is offered the update and is not asked for its accounts,
        // which it has no command for.
        const current = version !== null && versionAtLeast(version, replyMinimumVersion);
        const auth = parseReplyAuth((await this.#run('reply', 'auth status', ['auth', 'status', '--json'])).stdout);
        const accounts: ReplyAccount[] = [];
        const absent: ReplyChannel[] = [];
        const unread: ReplyChannel[] = [];
        let linkedinPending: ReplyPending[] = [];
        if (auth.signedIn && current) {
            // One read per channel: without --channel a channel the plan or role lacks fails the whole command.
            for (const channel of replyChannels) {
                const read = readReplyChannel(await this.#run('reply', 'account list', ['account', 'list', '--channel', channel, '--json']));
                if (read.kind === 'listed') accounts.push(...read.accounts);
                else (read.kind === 'absent' ? absent : unread).push(channel);
            }
            // The connections under way, read where LinkedIn was listed; their links are dropped here, in main.
            if (!absent.includes('linkedin') && !unread.includes('linkedin')) {
                const answer = await this.#replyApi('linkedin_pending');
                linkedinPending = answeredWell(answer) ? (pendingRows(answer.data) ?? []).map(withoutLink) : [];
            }
        }
        const linkedinBelowFloor = auth.signedIn && current && !versionAtLeast(version, linkedInFloorVersion) ? replyLinkSentences.linkedInFloor : null;
        return { cli: { found, version, install: current ? null : this.#installable('reply') }, signedIn: auth.signedIn, user: auth.user, wired, accounts, absent, unread, linkedinPending, linkedinBelowFloor };
    }

    /**
     * One call of the closed table through `reply api`, with the body on standard input for the one call that has one.
     * Logged by the call's name, Reply's status and the exit code only: never the path's id, the body or what was printed.
     */
    async #replyApi(act: ReplyApiAct, options: { id?: string; body?: string } = {}): Promise<ReplyApiAnswer | null> {
        const args = replyApiArguments(act, options.id);
        const result = await this.#run('reply', `api ${act}`, args, { stdin: options.body, timeoutMs: replyApiLimitMs });
        const answer = readReplyApi(result);
        this.#deps.log.info('reply_api_answered', { Act: act, Status: answer?.status ?? 0, Exit: result.exit });
        return answer;
    }

    /** Whether the Reply CLI reads a body from standard input, which the LinkedIn acts need. */
    async #linkedInReady(): Promise<boolean> {
        const version = await this.#version('reply');
        return version !== null && versionAtLeast(version, linkedInFloorVersion);
    }

    /** One link asked at a time; the answer is the link, or the sentence it ended with. */
    async #asking(work: () => Promise<ReplyLink | LinkRefusal>): Promise<ReplyLink | LinkRefusal> {
        if (this.#linking) return { refused: replyLinkSentences.busy };
        this.#linking = true;
        try {
            return await work();
        } finally {
            this.#linking = false;
        }
    }

    /**
     * Adds a LinkedIn account by a link Reply makes for it (`connection-link`), which also lists it as waiting until it
     * is finished. The name the person typed goes to Reply as the JSON body on standard input, never on a command line.
     */
    replyLinkedInConnect(name: unknown): Promise<ReplyLink | LinkRefusal> {
        const problem = linkedInNameProblem(name);
        if (problem !== null) return Promise.resolve({ refused: problem });
        return this.#asking(async () => {
            if (!(await this.#linkedInReady())) return { refused: replyLinkSentences.linkedInFloor };
            const answer = await this.#replyApi('linkedin_connect', { body: JSON.stringify({ name: (name as string).trim() }) });
            if (!answeredWell(answer)) return { refused: linkedInRefusal(answer, 'linkedin_connect') };
            return linkedIn(field(answer.data, 'connectionLink'), null);
        });
    }

    /** The link of a LinkedIn connection that waits, asked again from the pending list by its id, never kept. */
    replyLinkedInPendingLink(pendingId: unknown): Promise<ReplyLink | LinkRefusal> {
        if (!isReplyId(pendingId)) return Promise.resolve({ refused: replyLinkSentences.badId });
        return this.#asking(async () => {
            if (!(await this.#linkedInReady())) return { refused: replyLinkSentences.linkedInFloor };
            const answer = await this.#replyApi('linkedin_pending');
            if (!answeredWell(answer)) return { refused: linkedInRefusal(answer, 'linkedin_pending') };
            const rows = pendingRows(answer.data);
            if (rows === null) return { refused: replyLinkSentences.unreached };
            const row = rows.find((each) => each.id === pendingId);
            if (row === undefined) return { refused: replyLinkSentences.pendingGone };
            return linkedIn(row.connectionLink, row.expiresAtUtc);
        });
    }

    /** A link that signs a LinkedIn account Reply holds in again, by the account's id. */
    replyLinkedInReconnect(accountId: unknown): Promise<ReplyLink | LinkRefusal> {
        if (!isReplyId(accountId)) return Promise.resolve({ refused: replyLinkSentences.badId });
        return this.#asking(async () => {
            if (!(await this.#linkedInReady())) return { refused: replyLinkSentences.linkedInFloor };
            const answer = await this.#replyApi('linkedin_reconnect', { id: accountId });
            if (!answeredWell(answer)) return { refused: linkedInRefusal(answer, 'linkedin_reconnect') };
            return linkedIn(field(answer.data, 'connectionLink'), null);
        });
    }

    /**
     * One of the account's mailbox pages: Gmail's or Outlook's, which connect a mailbox or reconnect the one with the
     * same address, or the hosted page itself, where any other mailbox is added or put right. Each is built from the
     * checked token of the address Reply answers, and nothing else of the answer.
     */
    replyMailboxLink(page: unknown): Promise<ReplyLink | LinkRefusal> {
        if (!isMailboxPage(page)) return Promise.resolve({ refused: replyLinkSentences.badPage });
        return this.#asking(async () => {
            // The one link that never expires is asked only on the person's yes in the main process's own box.
            const confirmed = (await this.#deps.confirmMailboxLink?.()) ?? false;
            if (!confirmed) return { refused: replyLinkSentences.mailboxNotConfirmed };
            const answer = await this.#replyApi('mailbox_page');
            if (!answeredWell(answer)) return { refused: mailboxRefusal(answer) };
            const checked = mailboxToken(mailboxAnswer(answer.data));
            if (checked === null) return { refused: replyLinkSentences.badLink };
            return { kind: 'mailbox', link: mailboxAddress(checked.host, checked.token, page), host: checked.host, expiresAtUtc: null };
        });
    }

    connectReply(): Promise<void> {
        return this.#serial(async () => {
            const result = await this.#run('reply', 'auth login', ['auth', 'login'], { timeoutMs: signInLimit });
            this.#deps.log.info('reply_connected', { Method: 'browser', Exit: result.exit });
            if (result.exit !== 0) throw new Refused(words(result, 'Reply did not sign in.'));
            await this.#signedIn();
        });
    }

    connectReplyWithKey(key: unknown): Promise<void> {
        return this.#serial(async () => {
            if (typeof key !== 'string' || key.trim() === '') throw new Refused('Enter the key.');
            const result = await this.#run('reply', 'auth login --with-token', ['auth', 'login', '--with-token'], { stdin: key.trim(), timeoutMs: signInLimit });
            this.#deps.log.info('reply_connected', { Method: 'key', Exit: result.exit });
            if (result.exit !== 0) throw new Refused(words(result, 'Reply did not accept that key.'));
            await this.#signedIn();
        });
    }

    /** After a sign-in: Jason's Reply plugin wired to the profile signed in, and the Reply skills deployed either way. */
    async #signedIn(): Promise<void> {
        try {
            const auth = parseReplyAuth((await this.#run('reply', 'auth status', ['auth', 'status', '--json'])).stdout);
            await this.#wireReply(auth.profile ?? 'default');
        } finally {
            await this.#deployReplySkills();
            this.#changed();
        }
    }

    /**
     * Places Jason's Reply plugin with the CLI, grants it the Reply CLI and, where the settings file names no global
     * default, names it that default for the profile signed in, as the golden path does by hand; then the reload.
     */
    async #wireReply(profile: string): Promise<void> {
        let routeWritten = false;
        await this.#placeAndEdit('reply', (settings) => {
            const wired = withReplyWired(settings, profile);
            routeWritten = wired.routeWritten;
            return wired.settings;
        });
        const reload = await this.#reloadPlugins();
        this.#deps.log.info('reply_wired', { RouteWritten: routeWritten, Exit: reload.exit });
        if (reload.exit !== 0) throw this.#reloadRefused(reload);
    }

    /** The settings file as an object for an edit; one the desktop cannot parse is logged by its path and refused. */
    #readForEdit(): Record<string, unknown> {
        const file = this.#deps.paths.installationSettings;
        try {
            return parseInstallationSettings(readText(file));
        } catch (error) {
            this.#deps.log.warn('settings_refused', { Path: file });
            throw new Refused(`${(error as Error).message} ${editByHand}`);
        }
    }

    /**
     * One plugin placed with `jason plugin install` and one edit of the settings file: the edit is tried before the
     * install, so a file or a value it refuses is refused before anything is fetched, and made again on the file as it
     * is just before the write, so an edit made while the package was fetched is not lost.
     */
    async #placeAndEdit(pluginId: string, edit: (settings: Record<string, unknown>) => Record<string, unknown>): Promise<void> {
        edit(this.#readForEdit());
        const placed = await this.#deps.cli.run(['plugin', 'install', pluginId, ...feedArguments(this.#deps.settings())], { timeoutMs: installLimit });
        if (placed.exit !== 0) throw new Refused(words(placed, `jason plugin install ${pluginId} did not place the plugin.`));
        writeAtomically(this.#deps.paths.installationSettings, `${JSON.stringify(edit(this.#readForEdit()), null, 2)}\n`);
    }

    /**
     * The Reply skills beside Jason's, in the chat folder's two skills folders, Claude Code's and Codex's. The Reply CLI
     * writes them as plain folders only for hosts other than Claude Code, so they are written to a folder of ours for one
     * of those and the Reply ones copied across; the sales skills it adds with them are an older copy of Jason's own and
     * are left out.
     */
    async #deployReplySkills(): Promise<void> {
        // The routine the start uses too (`reply-skills.ts`); what it copies is recorded outside the chat folder.
        await deployReplySkills((verb, args, options) => this.#run('reply', verb, args, options), this.#deps.paths);
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
     * the installation's settings as a person would, asks the runtime to load its plugins again and reads them back. With
     * no values it only asks for the reload and the read-back, which is what Reload is. A settings file the desktop
     * cannot read is refused and left as it is. The token travels in that file only: never in an argument, an
     * environment or a log.
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
            if (wanted.token !== undefined || wanted.target !== undefined || wanted.server !== undefined) {
                await this.#placeAndEdit(plugin.id, (settings) => withPluginConfigured(settings, plugin, wanted));
            }
            await this.#reload(plugin.id, wanted);
        });
    }

    /**
     * Disconnects one of the three notification plugins: its configuration and its notification binding are taken out
     * of the installation's settings, with the Http grant derived from a server of its own; the rest of its grant and
     * its package stay, and the runtime is asked to load its plugins again and, when that reload exits 0, the plugins
     * are read back. The id is checked against the desktop's own table before the settings file is read, and the
     * journal forwarder is refused there too. A settings file the desktop cannot read or keep is refused and left as it
     * is; one the edit would not change is not written.
     */
    disconnectPlugin(pluginId: unknown): Promise<void> {
        return this.#serial(async () => {
            const plugin = bundledPlugin(pluginId);
            if (plugin === undefined || plugin.target.where !== 'binding') throw new Refused('That is not a plugin the desktop disconnects.');
            const settings = this.#readForEdit();
            let next: Record<string, unknown>;
            try {
                next = withPluginDisconnected(settings, plugin);
            } catch (error) {
                throw error instanceof Refused ? new Refused(`${error.message} ${editByHand}`) : error;
            }
            const written = JSON.stringify(next) !== JSON.stringify(settings);
            if (written) writeAtomically(this.#deps.paths.installationSettings, `${JSON.stringify(next, null, 2)}\n`);
            const reload = await this.#reloadPlugins();
            const listed = reload.exit === 0 ? await this.#deps.cli.run(['plugin', 'list']) : null;
            this.#deps.log.info('plugin_disconnected', {
                PluginId: plugin.id,
                Written: written,
                Exit: reload.exit,
                Valid: listed !== null && listed.exit === 0 && (pluginRuntimes(listed.stdout).get(plugin.id)?.valid ?? false),
            });
            this.#changed();
            if (reload.exit !== 0) throw this.#reloadRefused(reload);
        });
    }

    /** Asks the runtime to read its plugins again, and keeps what it said about the settings file for `status()`. */
    async #reloadPlugins(): Promise<CliResult> {
        const reload = await this.#deps.cli.run(['plugin', 'reload']);
        this.#reloadRefusal = reload.exit === 0 ? null : settingsUnreadable(reload.stdout);
        return reload;
    }

    #reloadRefused(reload: CliResult): Refused {
        return new Refused(this.#reloadRefusal ?? words(reload, 'The runtime did not load the plugins again.'));
    }

    /** The reload, then the plugin list read back, so what is logged says whether the plugin is now valid. */
    async #reload(pluginId: string, wanted: PluginValues): Promise<void> {
        const reload = await this.#reloadPlugins();
        const listed = reload.exit === 0 ? await this.#deps.cli.run(['plugin', 'list']) : null;
        this.#deps.log.info('plugin_configured', {
            PluginId: pluginId,
            TokenChanged: wanted.token !== undefined,
            TargetChanged: wanted.target !== undefined,
            ServerChanged: wanted.server !== undefined,
            Exit: reload.exit,
            Valid: listed !== null && listed.exit === 0 && (pluginRuntimes(listed.stdout).get(pluginId)?.valid ?? false),
        });
        this.#changed();
        if (reload.exit !== 0) throw this.#reloadRefused(reload);
    }
}
