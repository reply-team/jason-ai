/**
 * The models Codex offers, as the desktop keeps them in `<data>/desktop/models.json`: one list per host id, `codex` on
 * the person's own sign-in and `codex-api-key` on the OpenAI key, since OpenAI says the two differ
 * (https://developers.openai.com/codex/models, read 2026-11-11: "With API-key authentication, model availability
 * follows your API organization and project."). A list is shown at once from what is kept, and asked again of the host
 * in the background at most once a day per host id, one ask at a time across the desktop; an ask that fails, is not
 * read or meets its bound keeps the last list, and is said by its kind alone.
 *
 * An ask obeys what a turn obeys: the host found on the PATH and started with no shell by the module that starts a
 * turn's host (`startListing`), in the chat folder, with the environment a turn of that host id gets (`hostEnvironment`):
 * on the OpenAI key, the key and the desktop's own Codex folder, made private, and none of the credentials the desktop
 * inherited. It sends no message: the exchange starts no thread and no turn (`ListingExchange`).
 */

import { mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { hostEnvironment } from './chat-hosts.ts';
import { keptListsText, ListingExchange, listedHosts, modelLimit, parseKeptLists, type KeptList, type KeptLists, type ModelOffer } from './chat-models.ts';
import type { Platform } from './chat-permission-model.ts';
import { startListing, type ListingProcess } from './chat-process.ts';
import { makePrivate } from './chat.ts';
import type { DesktopPaths } from './paths.ts';
import { programLaunch } from './programs.ts';
import { keyedFolder, keyProvider, providerEnvironment } from './providers-model.ts';

/** How long a kept list stands before its host is asked again. */
export const askEveryMs = 24 * 60 * 60 * 1000;

/** How long one ask may take before its host is ended and the last list kept. */
export const askLimitMs = 30_000;

/** How long after a failed ask its host is asked again while the desktop runs; a start asks again at once. */
export const askAgainMs = 60 * 60 * 1000;

/** The longest line of a listing read, in bytes; a longer one ends the ask as unreadable. */
export const lineLimit = 1024 * 1024;

/**
 * The largest kept file read: more than the two lists of `modelLimit` models each could take, each model's name, words
 * and levels at their longest; a larger file was not written by the desktop and is not read.
 */
export const keptFileLimit = 2 * modelLimit * 1024 + 4096;

/**
 * Why an ask kept the last list, as a word for the log: the host refused a request, answered what cannot be read, ended
 * before it answered, met the bound, could not be started, is not on the PATH, has no key stored, its folder could not
 * be made, or the host id is not one whose list is asked.
 */
export type AskFailure = 'refused' | 'unreadable' | 'ended' | 'bound' | 'start' | 'not_found' | 'no_key' | 'folder' | 'not_listed' | 'closed';

export type AskResult = { models: ModelOffer[] } | { failed: AskFailure };

/** How long a host whose exchange has ended is given to leave by itself once its standard input is closed. */
const leaveMs = 2000;

/**
 * One ask over a listing process the caller starts: the exchange's lines written one at a time, each after the answer
 * before it. Once the exchange has ended, the host's standard input is closed and the host given a moment to leave
 * before it is ended. At the bound, on a line past its limit, and when `signal` ends the ask (the desktop quits), it is
 * ended at once, as a turn's host is. Resolves once the host is gone or ended; never rejects.
 */
export function askOver(
    start: (onLine: (line: string) => void, tooLong: () => void) => ListingProcess,
    version: string,
    limitMs = askLimitMs,
    signal?: AbortSignal,
): Promise<AskResult> {
    return new Promise((resolve) => {
        if (signal?.aborted) {
            resolve({ failed: 'closed' });
            return;
        }
        const exchange = new ListingExchange(version);
        let host: ListingProcess | null = null;
        let settled = false;
        let bound: ReturnType<typeof setTimeout> | undefined;
        const end = (result: AskResult) => {
            if (settled) return;
            settled = true;
            clearTimeout(bound);
            const started = host;
            if (started === null) {
                resolve(result);
                return;
            }
            const atOnce = 'failed' in result && (result.failed === 'bound' || result.failed === 'closed' || result.failed === 'unreadable');
            if (atOnce) started.stop();
            else started.endInput();
            let grace: ReturnType<typeof setTimeout> | undefined;
            const left = new Promise<void>((done) => {
                grace = setTimeout(done, leaveMs);
                void started.exited.then(() => done());
            });
            void left.then(() => {
                clearTimeout(grace);
                started.stop();
                resolve(result);
            });
        };
        const heard = (line: string) => {
            for (const next of exchange.receive(line)) host?.write(next);
            const outcome = exchange.outcome;
            if (outcome !== undefined) end(outcome);
        };
        try {
            host = start(heard, () => end({ failed: 'unreadable' }));
        } catch {
            end({ failed: 'start' });
            return;
        }
        bound = setTimeout(() => end({ failed: 'bound' }), limitMs);
        signal?.addEventListener('abort', () => end({ failed: 'closed' }), { once: true });
        void host.exited.then((exit) => end({ failed: exit.startError !== null ? 'start' : 'ended' }));
        for (const line of exchange.start()) host.write(line);
    });
}

export interface CodexAskerOptions {
    paths: Pick<DesktopPaths, 'chat' | 'data' | 'claudeKeyed' | 'codexKeyed'>;
    /** The desktop's environment, PATH included, as a turn's is built from. */
    env: Readonly<Record<string, string | undefined>>;
    bundle: string;
    dataNamed: boolean;
    bundleVersion: string | null;
    keyFor(providerId: string): string | undefined;
    platform?: Platform;
    limitMs?: number;
}

function currentPlatform(): Platform {
    return process.platform === 'win32' || process.platform === 'darwin' ? process.platform : 'linux';
}

/** Asks Codex for its models under a host id: `codex`, or `codex-api-key` with the OpenAI key stored. */
export function codexAsker(options: CodexAskerOptions): (hostId: string, signal?: AbortSignal) => Promise<AskResult> {
    return async (hostId, signal) => {
        if (!(listedHosts as readonly string[]).includes(hostId)) return { failed: 'not_listed' };
        const provider = keyProvider(hostId);
        let provided: Record<string, string> = {};
        if (provider !== undefined) {
            const key = options.keyFor(provider.id);
            if (!key) return { failed: 'no_key' };
            const folder = keyedFolder(options.paths, provider);
            try {
                makePrivate(provider.label, options.paths.codexKeyed, folder);
            } catch {
                return { failed: 'folder' };
            }
            provided = providerEnvironment(provider, key, '', folder);
        }
        const launch = programLaunch(options.env, 'codex');
        if ('refused' in launch) return { failed: 'not_found' };
        try {
            mkdirSync(options.paths.chat, { recursive: true });
        } catch {
            return { failed: 'folder' };
        }
        const env = hostEnvironment(options.env, {
            platform: options.platform ?? currentPlatform(),
            bundle: options.bundle,
            data: options.dataNamed ? options.paths.data : null,
            program: 'codex',
            keyed: provider !== undefined,
            provider: provided,
            bundleVersion: options.bundleVersion,
        });
        return askOver(
            (onLine, onTooLong) => startListing(launch, { cwd: options.paths.chat, env, onLine, lineLimit, onTooLong }),
            options.bundleVersion ?? '0.0.0',
            options.limitMs,
            signal,
        );
    };
}

export interface ModelListsOptions {
    /** `<data>/desktop/models.json`. */
    file: string;
    /** Asks a host for its list; `signal` ends the ask when the desktop quits. */
    ask(hostId: string, signal: AbortSignal): Promise<AskResult>;
    now?(): number;
    /** Said when an ask ended, read or failed, so the page reads the hosts again. */
    changed?(): void;
    /** Said when an ask brought a list: the host id and how many models it kept. */
    listed?(hostId: string, count: number): void;
    /** Said when an ask kept the last list: the host id and why, never what the host said. */
    failed?(hostId: string, kind: AskFailure): void;
}

function readKept(file: string): Map<string, KeptList> {
    try {
        if (statSync(file).size > keptFileLimit) return new Map();
        return parseKeptLists(readFileSync(file, 'utf8'));
    } catch {
        return new Map();
    }
}

/** Writes the lists beside the file and renames them over it, readable by this account alone. */
function writeKept(file: string, lists: KeptLists): void {
    const beside = `${file}.${process.pid}.tmp`;
    try {
        mkdirSync(dirname(file), { recursive: true });
        writeFileSync(beside, keptListsText(lists), { mode: 0o600 });
        renameSync(beside, file);
    } catch {
        rmSync(beside, { force: true });
        throw new Error('models.json could not be written');
    }
}

/**
 * The lists, and their asks. Only a list read counts for the day: a failed ask leaves the last list read as it was, with
 * its moment, and is kept in memory alone, so its host is asked again at the next start, or after an hour while the
 * desktop runs. A host whose list was never read says so (`unread`), and its turns go on its own default model.
 */
export class ModelLists {
    #options: ModelListsOptions;
    #lists: Map<string, KeptList>;
    /** The host ids waiting to be asked, and the one being asked. */
    #queue: string[] = [];
    #asking: string | null = null;
    #drained: Promise<void> = Promise.resolve();
    /** By host id, when its last ask failed, since this start. */
    #failedAt = new Map<string, number>();
    /** Ends the ask that runs; set while one does. */
    #running: AbortController | null = null;
    #closed = false;

    constructor(options: ModelListsOptions) {
        this.#options = options;
        this.#lists = readKept(options.file);
    }

    /** The lists as kept now. */
    lists(): KeptLists {
        return this.#lists;
    }

    /**
     * Whether a host's models are unread: `waiting` before its first ask of this start has ended, `failed` once it has
     * failed, and null when a list is kept, or for a host whose list is not asked.
     */
    unread(hostId: string): 'waiting' | 'failed' | null {
        if (!(listedHosts as readonly string[]).includes(hostId) || this.#lists.has(hostId)) return null;
        return this.#failedAt.has(hostId) ? 'failed' : 'waiting';
    }

    /**
     * Asks again, in the background, each host id given whose list is asked: one never read or read a day ago, unless
     * its last ask of this start failed less than an hour ago.
     */
    refresh(hostIds: readonly string[]): void {
        if (this.#closed) return;
        const now = this.#now();
        const within = (since: number, span: number) => !Number.isNaN(since) && now - since >= 0 && now - since < span;
        for (const hostId of hostIds) {
            if (!(listedHosts as readonly string[]).includes(hostId) || this.#queue.includes(hostId) || this.#asking === hostId) continue;
            if (within(Date.parse(this.#lists.get(hostId)?.askedAt ?? ''), askEveryMs)) continue;
            if (within(this.#failedAt.get(hostId) ?? Number.NaN, askAgainMs)) continue;
            this.#queue.push(hostId);
        }
        if (this.#asking === null && this.#queue.length > 0) this.#drained = this.#drain();
    }

    /**
     * The desktop quits, or its last window closes: the ask that runs is ended as a turn's host is, the ones that wait are
     * dropped, and nothing is asked after.
     */
    close(): void {
        this.#closed = true;
        this.#queue = [];
        this.#running?.abort();
    }

    /** Resolves once no ask waits or runs. */
    async settled(): Promise<void> {
        while (this.#asking !== null || this.#queue.length > 0) await this.#drained;
    }

    async #drain(): Promise<void> {
        for (let hostId = this.#queue.shift(); hostId !== undefined; hostId = this.#queue.shift()) {
            this.#asking = hostId;
            const running = new AbortController();
            this.#running = running;
            let result: AskResult;
            try {
                result = await this.#options.ask(hostId, running.signal);
            } catch {
                result = { failed: 'start' };
            }
            this.#running = null;
            // A close is no failure of the host's: nothing is kept of it.
            if (running.signal.aborted || ('failed' in result && result.failed === 'closed')) continue;
            if ('models' in result) {
                this.#lists.set(hostId, { askedAt: new Date(this.#now()).toISOString(), models: result.models });
                this.#failedAt.delete(hostId);
                try {
                    writeKept(this.#options.file, this.#lists);
                } catch {
                    // The lists stand in memory; the next start asks again.
                }
                this.#options.listed?.(hostId, result.models.length);
            } else {
                this.#failedAt.set(hostId, this.#now());
                this.#options.failed?.(hostId, result.failed);
            }
            this.#options.changed?.();
        }
        this.#asking = null;
    }

    #now(): number {
        return (this.#options.now ?? Date.now)();
    }
}
