/**
 * Whether a demo runtime is alive on a data directory: its run file (`<data>/run/runtime.json`) names a process that is
 * running. One helper for every act that must not change the demo's files under a live runtime: preparing the demo at a
 * start (`prepareDemo`) and carrying out a start over (`carryOutStartOver`), both of which run before the single-instance
 * lock, so a second launch can reach them while a demo instance runs. A run file that is older than the machine's boot is stale
 * (the lock on Windows is not tested: the file's age says the same thing everywhere). One that is there and cannot be read counts as
 * alive: when it is not known to be dead, nothing is touched.
 */

import { existsSync, readFileSync, statSync } from 'node:fs';
import { uptime } from 'node:os';
import { processIsRunning } from './own-runtime.ts';

export interface AliveDeps {
    exists(file: string): boolean;
    read(file: string): string;
    running(pid: number): boolean;
    /** When the file was last written, in milliseconds since the epoch. */
    modifiedMs(file: string): number;
    /** When the machine started, in milliseconds since the epoch. */
    bootMs(): number;
}

const onDisk: AliveDeps = { exists: existsSync, read: (file) => readFileSync(file, 'utf8'), running: processIsRunning,
    modifiedMs: (file) => statSync(file).mtimeMs,
    bootMs: () => Date.now() - uptime() * 1000,
};

/** True when the run file at `runFile` names a running process, or is there and unreadable. */
export function demoRuntimeAlive(runFile: string, deps: AliveDeps = onDisk): boolean {
    if (!deps.exists(runFile)) return false;
    try {
        // A run file written before the machine started is a leftover of a runtime that did not end cleanly, and its process id
        // may belong to anything since: it names no runtime.
        if (deps.modifiedMs(runFile) < deps.bootMs()) return false;
        const pid: unknown = (JSON.parse(deps.read(runFile)) as { pid?: unknown } | null)?.pid;
        if (typeof pid !== 'number' || !Number.isSafeInteger(pid) || pid <= 0) return true;
        return deps.running(pid);
    } catch {
        return true;
    }
}
