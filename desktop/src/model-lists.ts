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

import { mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { hostEnvironment } from './chat-hosts.ts';
import { keptListsText, ListingExchange, listedHosts, parseKeptLists, type KeptList, type KeptLists, type ModelOffer } from './chat-models.ts';
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

/**
 * Why an ask kept the last list, as a word for the log: the host refused a request, answered what cannot be read, ended
 * before it answered, met the bound, could not be started, is not on the PATH, has no key stored, its folder could not
 * be made, or the host id is not one whose list is asked.
 */
export type AskFailure = 'refused' | 'unreadable' | 'ended' | 'bound' | 'start' | 'not_found' | 'no_key' | 'folder' | 'not_listed';

export type AskResult = { models: ModelOffer[] } | { failed: AskFailure };

/** How long a host whose exchange has ended is given to leave by itself once its standard input is closed. */
const leaveMs = 2000;

/**
 * One ask over a listing process the caller starts: the exchange's lines written one at a time, each after the answer
 * before it. Once the exchange has ended, the host's standard input is closed and the host given a moment to leave
 * before it is ended; at the bound it is ended at once. Resolves once the host is gone or ended; never rejects.
 */
export function askOver(start: (onLine: (line: string) => void) => ListingProcess, version: string, limitMs = askLimitMs): Promise<AskResult> {
    return new Promise((resolve) => {
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
            const atBound = 'failed' in result && result.failed === 'bound';
            if (atBound) started.stop();
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
            host = start(heard);
        } catch {
            end({ failed: 'start' });
            return;
        }
        bound = setTimeout(() => end({ failed: 'bound' }), limitMs);
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
export function codexAsker(options: CodexAskerOptions): (hostId: string) => Promise<AskResult> {
    return async (hostId) => {
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
        return askOver((onLine) => startListing(launch, { cwd: options.paths.chat, env, onLine }), options.bundleVersion ?? '0.0.0', options.limitMs);
    };
}

export interface ModelListsOptions {
    /** `<data>/desktop/models.json`. */
    file: string;
    ask(hostId: string): Promise<AskResult>;
    now?(): number;
    /** Said when an ask brought a list, so the page reads the hosts again. */
    changed?(): void;
    /** Said when an ask brought a list: the host id and how many models it kept. */
    listed?(hostId: string, count: number): void;
    /** Said when an ask kept the last list: the host id and why, never what the host said. */
    failed?(hostId: string, kind: AskFailure): void;
}

function readKept(file: string): Map<string, KeptList> {
    try {
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

export class ModelLists {
    #options: ModelListsOptions;
    #lists: Map<string, KeptList>;
    /** The host ids waiting to be asked, and the one being asked. */
    #queue: string[] = [];
    #asking: string | null = null;
    #drained: Promise<void> = Promise.resolve();

    constructor(options: ModelListsOptions) {
        this.#options = options;
        this.#lists = readKept(options.file);
    }

    /** The lists as kept now. */
    lists(): KeptLists {
        return this.#lists;
    }

    /** Asks again, in the background, each host id given whose list is asked and is a day old or was never asked. */
    refresh(hostIds: readonly string[]): void {
        const now = this.#now();
        for (const hostId of hostIds) {
            if (!(listedHosts as readonly string[]).includes(hostId) || this.#queue.includes(hostId) || this.#asking === hostId) continue;
            const askedAt = Date.parse(this.#lists.get(hostId)?.askedAt ?? '');
            const elapsed = now - askedAt;
            if (!Number.isNaN(askedAt) && elapsed >= 0 && elapsed < askEveryMs) continue;
            this.#queue.push(hostId);
        }
        if (this.#asking === null && this.#queue.length > 0) this.#drained = this.#drain();
    }

    /** Resolves once no ask waits or runs. */
    async settled(): Promise<void> {
        while (this.#asking !== null || this.#queue.length > 0) await this.#drained;
    }

    async #drain(): Promise<void> {
        for (let hostId = this.#queue.shift(); hostId !== undefined; hostId = this.#queue.shift()) {
            this.#asking = hostId;
            let result: AskResult;
            try {
                result = await this.#options.ask(hostId);
            } catch {
                result = { failed: 'start' };
            }
            const askedAt = new Date(this.#now()).toISOString();
            if ('models' in result) this.#lists.set(hostId, { askedAt, models: result.models });
            else this.#lists.set(hostId, { askedAt, models: this.#lists.get(hostId)?.models ?? [] });
            try {
                writeKept(this.#options.file, this.#lists);
            } catch {
                // The lists stand in memory; the next start asks again.
            }
            if ('models' in result) {
                this.#options.listed?.(hostId, result.models.length);
                this.#options.changed?.();
            } else {
                this.#options.failed?.(hostId, result.failed);
            }
        }
        this.#asking = null;
    }

    #now(): number {
        return (this.#options.now ?? Date.now)();
    }
}
