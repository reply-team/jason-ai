/**
 * The restart re-pair. The window was paired with one runtime, the one the run file named when it loaded. When the run
 * file names another (the runtime restarted on another port, or as another process on the same one), the page's pairing
 * is the old runtime's, so the window asks `jason ui open --no-open` for a new link and loads it. While no run file
 * names a runtime the window waits; nothing here starts one. The IPC check needs nothing from here: it reads the run
 * file on every call, so a page at an origin the run file no longer names is refused at once.
 */

import type { Cli } from './cli.ts';
import { pairingLink, runtimeOrigin } from './descriptor.ts';
import type { Logger } from './log.ts';

export interface RunIdentity {
    origin: string;
    pid: number | null;
}

/** Which runtime the run file names: its loopback origin and its process id. The token is never read. */
export function runIdentity(text: string | null): RunIdentity | null {
    if (text === null) return null;
    const origin = runtimeOrigin(text);
    if (origin === null) return null;
    const pid = (JSON.parse(text) as { pid?: unknown }).pid;
    return { origin, pid: typeof pid === 'number' && Number.isInteger(pid) ? pid : null };
}

const sameRuntime = (a: RunIdentity, b: RunIdentity) => a.origin === b.origin && a.pid === b.pid;

/** How often the run file is looked at, and how long a failed pairing waits before it is tried again. */
export const repairCadenceMs = 2_000;
export const retryAfterFailureMs = 30_000;

export type Look = 'same' | 'gone' | 'repaired' | 'failed' | 'waiting' | 'busy';

export interface RepairDeps {
    readRunFile(): string | null;
    cli: Cli;
    /** Loads the new link into the window. */
    load(link: string): Promise<void>;
    log: Logger;
    clock?: () => number;
}

export class Repairer {
    private readonly deps: RepairDeps;
    private readonly clock: () => number;
    private paired: RunIdentity | null = null;
    private gone = false;
    private busy = false;
    private failedAt: number | null = null;
    private handle: NodeJS.Timeout | null = null;

    constructor(deps: RepairDeps) {
        this.deps = deps;
        this.clock = deps.clock ?? Date.now;
    }

    /** The runtime the window was paired with when it first loaded, from the run file read then. */
    pairedWith(runFile: string | null): void {
        this.paired = runIdentity(runFile);
    }

    /** One look at the run file, pairing again when it names another runtime than the window's. */
    async check(): Promise<Look> {
        if (this.busy) return 'busy';
        const now = runIdentity(this.deps.readRunFile());
        if (now === null) {
            if (!this.gone) this.deps.log.info('runtime_gone', {});
            this.gone = true;
            return 'gone';
        }
        this.gone = false;
        if (this.paired !== null && sameRuntime(this.paired, now)) {
            this.failedAt = null;
            return 'same';
        }
        if (this.failedAt !== null && this.clock() - this.failedAt < retryAfterFailureMs) return 'waiting';
        return this.pair(now);
    }

    /** Pairs again with the runtime the run file names now, whether or not it changed: the menu's Reload. */
    async reload(): Promise<Look> {
        if (this.busy) return 'busy';
        const now = runIdentity(this.deps.readRunFile());
        return now === null ? 'gone' : this.pair(now);
    }

    private async pair(now: RunIdentity): Promise<Look> {
        this.busy = true;
        try {
            const open = await this.deps.cli.run(['ui', 'open', '--no-open']);
            const link = open.exit === 0 ? pairingLink(open.stdout, now.origin) : null;
            if (link === null) return this.failed(now, open.exit);
            try {
                await this.deps.load(link);
            } catch {
                // The navigation failed (the runtime went between the run file and the load): the window keeps what it
                // shows, and the pairing waits half a minute like one the CLI refused. -1 is the start's own word for
                // a load that failed.
                return this.failed(now, -1);
            }
            this.deps.log.info('window_repaired', { From: this.paired?.origin ?? 'none', Origin: now.origin });
            this.paired = now;
            this.failedAt = null;
            return 'repaired';
        } finally {
            this.busy = false;
        }
    }

    private failed(now: RunIdentity, exit: number): Look {
        this.failedAt = this.clock();
        this.deps.log.warn('repair_failed', { Origin: now.origin, Exit: exit });
        return 'failed';
    }

    /** Looks at the run file on the cadence until stopped. */
    start(): void {
        if (this.handle !== null) return;
        const next = () => {
            this.handle = setTimeout(() => {
                void this.check().finally(() => {
                    if (this.handle !== null) next();
                });
            }, repairCadenceMs);
        };
        next();
    }

    stop(): void {
        if (this.handle !== null) clearTimeout(this.handle);
        this.handle = null;
    }
}
