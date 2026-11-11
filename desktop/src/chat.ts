/**
 * The Live chat. One turn is one host process: Claude Code, or Codex, started in the chat folder with the person's words
 * on its standard input, its output mapped to the page's events in the order the host says them, and its end always
 * said with a `done`. Claude Code asks what its rules leave open through the permission endpoint the turn opens; the
 * broker decides what the permission model settles and puts the rest on the page as a card. A host that cannot drive
 * the chat is never swapped for one that can, and the desktop never signs in to a host.
 */

import { mkdirSync, readdirSync, statSync } from 'node:fs';
import { homedir } from 'node:os';
import { isAbsolute } from 'node:path';
import { claudeArguments, claudeLine, codexArguments, codexLine, dataRules, hostEnvironment, type HostLine, type Step } from './chat-hosts.ts';
import { PermissionBroker, type BrokerEvent, type Decided } from './chat-permission-broker.ts';
import { openEndpoint, type Endpoint } from './chat-permission-endpoint.ts';
import type { ChatScope, Platform } from './chat-permission-model.ts';
import { startHost, type HostProcess } from './chat-process.ts';
import { realPathOf } from './chat-real-path.ts';
import { newSecret, newTurnId, removeTurnFiles, sweepTurnFiles, writeTurnConfig } from './chat-turn-files.ts';
import type { ChatFence } from './chat-deny.ts';
import { InstructionsUnwritten, placeInstructions } from './chat-instructions.ts';
import type { Cli } from './cli.ts';
import { floorSentence, printedVersion, versionLimitMs } from './host-version.ts';
import type { DesktopPaths } from './paths.ts';
import { programLaunch, type Programs } from './programs.ts';
import { hosts, keyProvider, providerEnvironment } from './providers-model.ts';

/** What the page is told, by the chat it belongs to: U2's `DesktopChatEvent`. */
export type ChatEvent =
    | ({ kind: 'step' } & Step)
    | { kind: 'text'; text: string }
    | BrokerEvent
    | { kind: 'done'; error: string | null };

export interface ChatSendOptions {
    host: string;
    model?: string;
    campaignId?: string;
}

/** What one turn did, for the log: counts and a duration, never its words. */
export interface TurnEnded {
    host: string;
    exit: number;
    durationMs: number;
    steps: number;
    requests: number;
}

export interface ChatOptions {
    paths: DesktopPaths;
    /** The desktop's environment, PATH included: where hosts are found, and the base of a host's environment. */
    env: Readonly<Record<string, string | undefined>>;
    /** The folder the bundled `jason` is in, first on the host's PATH. */
    bundle: string;
    /** Whether the desktop runs with a data directory `JASON_DATA_DIR` names, which the host is then given too. */
    dataNamed: boolean;
    /** The bundle's version, from which a host's label for reply-cli is built (`hostEnvironment`); null when there is none. */
    bundleVersion: string | null;
    cli: Cli;
    /** The program runner, which reads Claude Code's version before each turn on it. */
    programs: Pick<Programs, 'run'>;
    keyFor(providerId: string): string | undefined;
    /** The deny list's commands and operations, the allow list's verbs and the instruction text, read at each turn. */
    fence(): ChatFence;
    emit(chatId: string, event: ChatEvent): void;
    started?(host: string, resumed: boolean): void;
    ended?(turn: TurnEnded): void;
    decided?(decided: Decided): void;
    /** Said once, the first time the application is found to carry no instruction text; the turns run without it. */
    instructionsMissing?(): void;
    /** Said each time a memory file cannot be written, at the start or before a turn, which is then refused. */
    instructionsUnwritten?(file: string, code: string): void;
    /** How long a card waits for the person; thirty minutes unless a test shortens it. */
    answerWindowMs?: number;
    platform?: Platform;
}

interface Running {
    stopped: boolean;
    process: HostProcess | null;
    /** The turn's id while it has a file under `<data>/desktop`; a Codex turn has none. */
    turnId: string | null;
}

interface Resolved {
    host: string;
    label: string;
    program: 'claude' | 'codex';
    provider: Record<string, string>;
    /** Whether the turn runs on a key provider, which the inherited provider and credentials never reach. */
    keyed: boolean;
    /** The model named on the command line; an Anthropic-compatible endpoint is given its model by the environment instead. */
    model: string | undefined;
}

const chatIdShape = /^[A-Za-z0-9_.:-]{1,200}$/;
const campaignIdShape = /^cmp_[0-9A-Za-z]{1,64}$/;
const modelShape = /^[A-Za-z0-9][A-Za-z0-9._:[\]/-]{0,99}$/;
const textLimit = 1_000_000;
const wordsKept = 600;

const busy = 'This chat is still answering. Wait for it to finish, or stop it, then send again.';
const notAHost = 'That is not an agent host this desktop knows. Choose Claude Code or Codex for the chat.';

function currentPlatform(): Platform {
    return process.platform === 'win32' || process.platform === 'darwin' ? process.platform : 'linux';
}

/** The last words a host wrote to its standard error, whole lines, short enough for the page. */
function lastWords(tail: string): string {
    const lines = tail.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
    let words = '';
    for (let i = lines.length - 1; i >= 0; i--) {
        const next = words === '' ? (lines[i] ?? '') : `${lines[i]} ${words}`;
        if (next.length > wordsKept) break;
        words = next;
    }
    return words === '' ? (lines.at(-1) ?? '').slice(-wordsKept) : words;
}

function folderThere(path: unknown): string | null {
    if (typeof path !== 'string' || !isAbsolute(path)) return null;
    try {
        return statSync(path).isDirectory() ? path : null;
    } catch {
        return null;
    }
}

/** Removes a turn's file; one that cannot be removed now is left for the next start's sweep. */
function removeQuietly(turns: string, turnId: string): void {
    try {
        removeTurnFiles(turns, turnId);
    } catch {
        // Left for the next start.
    }
}

function entriesOf(folder: string): string[] {
    try {
        return readdirSync(folder);
    } catch {
        return [];
    }
}

export class Chat {
    #options: ChatOptions;
    #platform: Platform;
    #broker: PermissionBroker;
    #running = new Map<string, Running>();
    /** Per chat id, the host it last ran on and the session that host named; in memory only. */
    #sessions = new Map<string, { host: string; session: string }>();
    /** Whether the missing instruction text was said already. */
    #missingSaid = false;

    constructor(options: ChatOptions) {
        this.#options = options;
        this.#platform = options.platform ?? currentPlatform();
        sweepTurnFiles(options.paths.turns);
        // The chat folder carries the instruction text from the start; a fence that cannot be read now is the first
        // turn's to refuse, and a file that cannot be written now is said and written again before that turn. The start
        // never stops for either.
        try {
            this.#placeInstructions(options.fence());
        } catch {
            // Nothing more to do until a turn starts.
        }
        this.#broker = new PermissionBroker({
            emit: (chatId, event) => options.emit(chatId, event),
            ...(options.decided ? { decided: options.decided } : {}),
            ...(options.answerWindowMs !== undefined ? { timeoutMs: options.answerWindowMs } : {}),
        });
    }

    /** Whether a turn of this chat is running. */
    isRunning(chatId: string): boolean {
        return this.#running.has(chatId);
    }

    /**
     * Starts a turn. It resolves once the host has started, or once the send was refused; a refusal, like every end, is
     * a `done` with an error in plain words, and no host was started: on Claude Code, one below the floor
     * (`claudeFloor`) or whose version cannot be read is refused after `claude --version` and before anything else. A chat id the desktop does not take has no chat to
     * say `done` to, so that send rejects with an Error the page shows.
     */
    async send(chatId: unknown, text: unknown, options: unknown): Promise<void> {
        if (typeof chatId !== 'string' || !chatIdShape.test(chatId)) throw new Error('That is not a chat id the desktop takes.');
        const refuse = (error: string) => this.#options.emit(chatId, { kind: 'done', error });
        if (this.#running.has(chatId)) return refuse(busy);
        if (typeof text !== 'string' || text.trim() === '') return refuse('There is nothing to send.');
        if (text.length > textLimit) return refuse('That message is too long to send in one turn.');
        const given = (options ?? {}) as Partial<Record<keyof ChatSendOptions, unknown>>;
        if (given.model !== undefined && (typeof given.model !== 'string' || !modelShape.test(given.model))) return refuse('That model name is not one the desktop passes to a host.');
        if (given.campaignId !== undefined && (typeof given.campaignId !== 'string' || !campaignIdShape.test(given.campaignId))) return refuse('That is not a campaign id.');
        const resolved = this.#resolve(given.host, given.model as string | undefined);
        if (typeof resolved === 'string') return refuse(resolved);
        const launch = programLaunch(this.#options.env, resolved.program);
        if ('refused' in launch) return refuse(`${resolved.label} cannot be started: ${launch.refused}`);

        const running: Running = { stopped: false, process: null, turnId: null };
        this.#running.set(chatId, running);
        try {
            // The page marks a host below the floor; this is the guard, whatever the page asked. The chat counts as
            // answering while the version is read, so a second send is refused as busy.
            const held = resolved.program === 'claude' ? await this.#belowFloor() : null;
            if (held !== null) {
                this.#running.delete(chatId);
                return refuse(held);
            }
            await this.#turn(chatId, text, resolved, launch, given.campaignId as string | undefined, running);
        } catch (error) {
            this.#running.delete(chatId);
            if (error instanceof InstructionsUnwritten) refuse(error.message);
            else refuse(`The turn could not be started: ${error instanceof Error ? error.message : String(error)}`);
        }
    }

    /** Ends the chat's running turn and everything its host started; its `done` follows. */
    stop(chatId: unknown): void {
        if (typeof chatId !== 'string') return;
        const running = this.#running.get(chatId);
        if (running === undefined) return;
        running.stopped = true;
        running.process?.stop();
    }

    /** A person's answer to a card; anything but the asking chat's first answer to a request it holds changes nothing. */
    answer(chatId: unknown, requestId: unknown, decision: unknown): void {
        if (typeof chatId !== 'string' || typeof requestId !== 'string') return;
        const given = (decision ?? {}) as { allow?: unknown; always?: unknown };
        this.#broker.answer(chatId, requestId, { allow: given.allow === true, always: given.always === true });
    }

    /**
     * The window closed: every turn is stopped, every waiting request refused, every "always" forgotten, and every
     * running turn's file removed before this returns, since the desktop may quit before a host's exit is seen.
     */
    closeAll(): void {
        for (const [chatId, running] of [...this.#running.entries()]) {
            this.stop(chatId);
            if (running.turnId !== null) removeQuietly(this.#options.paths.turns, running.turnId);
        }
        this.#broker.endAll();
    }

    /** Claude Code's version, read before each turn on it and never remembered, and the sentence when it is below the floor. */
    async #belowFloor(): Promise<string | null> {
        return floorSentence(printedVersion(await this.#options.programs.run('claude', ['--version'], { timeoutMs: versionLimitMs })));
    }

    #resolve(host: unknown, model: string | undefined): Resolved | string {
        const provider = keyProvider(host);
        if (provider !== undefined) {
            const key = this.#options.keyFor(provider.id);
            if (!key) return `No key is stored for ${provider.label}. Add it on the Connect card, then send again.`;
            const provided = providerEnvironment(provider, key, model ?? '');
            return { host: provider.id, label: provider.label, program: provider.program, provider: provided, keyed: true, model: provider.baseUrl ? undefined : model };
        }
        const known = hosts.find((each) => each.id === host);
        if (known === undefined) return notAHost;
        if (!known.drivesChat || (known.program !== 'claude' && known.program !== 'codex')) {
            return `${known.label} runs campaign work, not the chat yet. Choose Claude Code or Codex for the chat.`;
        }
        return { host: known.id, label: known.label, program: known.program, provider: {}, keyed: false, model };
    }

    /** The campaign's folder and its project's, as the runtime recorded them and only when they are on this disk. */
    async #addedFolders(campaignId: string | undefined): Promise<string[]> {
        if (campaignId === undefined) return [];
        const read = async (args: string[]): Promise<Record<string, unknown> | null> => {
            const answer = await this.#options.cli.run(args);
            if (answer.exit !== 0) return null;
            try {
                const value: unknown = JSON.parse(answer.stdout);
                return value !== null && typeof value === 'object' ? (value as Record<string, unknown>) : null;
            } catch {
                return null;
            }
        };
        const campaign = await read(['campaign', 'get', campaignId]);
        if (campaign === null) return [];
        const folders: string[] = [];
        const own = folderThere(campaign.folder_path);
        if (own !== null) folders.push(own);
        if (typeof campaign.project_id === 'string' && /^prj_[0-9A-Za-z]{1,64}$/.test(campaign.project_id)) {
            const project = await read(['project', 'get', campaign.project_id]);
            const theirs = folderThere(project?.folder_path);
            if (theirs !== null && !folders.includes(theirs)) folders.push(theirs);
        }
        return folders;
    }

    /**
     * Writes the instruction text into the chat folder where a memory file does not hold it. With no text in the
     * application, nothing is written and the gap is said once; a file that cannot be written is said and thrown, and
     * the turn it was written for is refused in the desktop's words.
     */
    #placeInstructions(fence: ChatFence): void {
        if (fence.instructions === null) {
            if (!this.#missingSaid) this.#options.instructionsMissing?.();
            this.#missingSaid = true;
            return;
        }
        try {
            placeInstructions(this.#options.paths.chat, fence.instructions);
        } catch (error) {
            if (error instanceof InstructionsUnwritten) this.#options.instructionsUnwritten?.(error.file, error.code);
            throw error;
        }
    }

    async #turn(
        chatId: string,
        text: string,
        resolved: Resolved,
        launch: { file: string; prefix: string[] },
        campaignId: string | undefined,
        running: Running,
    ): Promise<void> {
        const { paths } = this.#options;
        const added = resolved.program === 'claude' ? await this.#addedFolders(campaignId) : [];
        const previous = this.#sessions.get(chatId);
        const resume = previous !== undefined && previous.host === resolved.host ? previous.session : undefined;
        mkdirSync(paths.chat, { recursive: true });

        let turnId: string | null = null;
        let endpoint: Endpoint | null = null;
        let args: string[];
        let requests = 0;
        // Closes what the turn opened. The file's removal runs whatever came before it, and a removal that fails (a scanner
        // holding the file on Windows) leaves the file for the next start to remove.
        const finishFiles = async () => {
            try {
                if (turnId !== null) this.#broker.endTurn(turnId);
                await endpoint?.close();
            } finally {
                if (turnId !== null) removeQuietly(paths.turns, turnId);
            }
        };
        try {
            const fence = this.#options.fence();
            // First of all a turn does: the chat folder is found to be the desktop's own, and its memory files written,
            // before the turn's file, its endpoint or the host that starts in that folder.
            this.#placeInstructions(fence);
            if (resolved.program === 'claude') {
                const id = newTurnId();
                turnId = id;
                running.turnId = id;
                const scope: ChatScope = {
                    platform: this.#platform,
                    cwd: paths.chat,
                    chat: paths.chat,
                    added,
                    data: paths.data,
                    home: homedir(),
                    protectedFiles: [paths.installationSettings, paths.runFile],
                    denied: fence.commands,
                    deniedOperations: fence.operations,
                    real: realPathOf,
                };
                this.#broker.openTurn(chatId, id, scope);
                const secret = newSecret();
                endpoint = await openEndpoint({
                    secret,
                    decide: (tool, input, signal) => {
                        requests++;
                        return this.#broker.ask(id, tool, input, signal);
                    },
                });
                const mcpConfig = writeTurnConfig(paths.turns, id, endpoint.url, secret);
                const rules = dataRules(this.#platform, paths.data, entriesOf(paths.data), entriesOf(paths.desktop));
                args = claudeArguments({ mcpConfig, addDirs: added, denied: fence.commands, allowed: fence.allowed, dataRules: rules, model: resolved.model, resume });
            } else {
                args = codexArguments({ model: resolved.model, resume });
            }
        } catch (error) {
            // Whatever the turn had opened is closed again: its endpoint, its broker turn and its file.
            await finishFiles();
            throw error;
        }

        if (running.stopped) {
            await finishFiles();
            this.#running.delete(chatId);
            this.#options.emit(chatId, { kind: 'done', error: null });
            return;
        }

        const env = hostEnvironment(this.#options.env, {
            platform: this.#platform,
            bundle: this.#options.bundle,
            data: this.#options.dataNamed ? paths.data : null,
            program: resolved.program,
            keyed: resolved.keyed,
            provider: resolved.provider,
            bundleVersion: this.#options.bundleVersion,
        });
        let steps = 0;
        let result: string | null | undefined;
        const seen = new Set<string>();
        const said = (line: HostLine) => {
            if (line.kind === 'session') this.#sessions.set(chatId, { host: resolved.host, session: line.id });
            else if (line.kind === 'result') result = line.error;
            else {
                if (line.kind === 'step') steps++;
                this.#options.emit(chatId, line);
            }
        };
        const begun = Date.now();
        const host = startHost(launch, args, {
            cwd: paths.chat,
            env,
            stdin: text,
            onLine: (line) => {
                for (const each of resolved.program === 'claude' ? claudeLine(line) : codexLine(line, seen)) said(each);
            },
        });
        running.process = host;
        this.#options.started?.(resolved.host, resume !== undefined);
        if (running.stopped) host.stop();

        void host.exited.then(async (exit) => {
            try {
                await finishFiles();
            } finally {
                // The turn ends whatever closing its files did: it is no longer running, its end is told, and its done sent.
                this.#running.delete(chatId);
                this.#options.ended?.({ host: resolved.host, exit: exit.code ?? -1, durationMs: Date.now() - begun, steps, requests });
                this.#options.emit(chatId, { kind: 'done', error: this.#ending(resolved.label, exit, result, running.stopped, host) });
            }
        }).catch(() => undefined);
    }

    #ending(label: string, exit: { code: number | null; startError: string | null }, result: string | null | undefined, stopped: boolean, host: HostProcess): string | null {
        if (stopped) return null;
        if (exit.startError !== null) return `${label} could not be started: ${exit.startError}`;
        if (typeof result === 'string') return result;
        const words = lastWords(host.stderrTail());
        if (result === null && exit.code === 0) return null;
        if (result === null) return words || `${label} stopped with exit code ${exit.code ?? 'none'}.`;
        if (exit.code === 0) return words || `${label} ended without an answer.`;
        return words || `${label} stopped with exit code ${exit.code ?? 'none'} before it answered.`;
    }
}
