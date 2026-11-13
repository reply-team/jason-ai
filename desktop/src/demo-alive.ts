/**
 * Whether a demo runtime is alive on a data directory: its run file (`<data>/run/runtime.json`) names a process that is
 * running. One helper for every act that must not change the demo's files under a live runtime: preparing the demo at a
 * start (`prepareDemo`) and carrying out a start over (`carryOutStartOver`), both of which run before the single-instance
 * lock, so a second launch can reach them while a demo instance runs. A run file that is there and cannot be read counts as
 * alive: when it is not known to be dead, nothing is touched.
 */

import { existsSync, readFileSync } from 'node:fs';
import { processIsRunning } from './own-runtime.ts';

export interface AliveDeps {
    exists(file: string): boolean;
    read(file: string): string;
    running(pid: number): boolean;
}

const onDisk: AliveDeps = { exists: existsSync, read: (file) => readFileSync(file, 'utf8'), running: processIsRunning };

/** True when the run file at `runFile` names a running process, or is there and unreadable. */
export function demoRuntimeAlive(runFile: string, deps: AliveDeps = onDisk): boolean {
    if (!deps.exists(runFile)) return false;
    try {
        const pid: unknown = (JSON.parse(deps.read(runFile)) as { pid?: unknown } | null)?.pid;
        if (typeof pid !== 'number' || !Number.isSafeInteger(pid) || pid <= 0) return true;
        return deps.running(pid);
    } catch {
        return true;
    }
}
