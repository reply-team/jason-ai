/**
 * Whether the runtime that is running is the one this application carries, and stopping it only then. An update
 * replaces the application's folder and an uninstall removes it, the jason executable inside it included, so a runtime
 * started from that executable is stopped first, the way a person would stop it: `jason runtime stop`, through the
 * carried CLI, waited out. A runtime started from any other executable, another installation's, is never stopped.
 *
 * What a runtime runs as is the `installed_image` of the run file it wrote (`docs/runtime-api.md`), believed only when
 * `jason runtime status` confirms that the runtime answering is that run file's instance, and only when it is an
 * absolute path that resolves to the same file as the carried executable: on Windows without regard to case. A
 * confirmed runtime that names no executable, or a path that resolves to another file, is another runtime.
 *
 * A run file whose runtime does not answer as itself (nothing answers in time, the token is refused, another instance
 * answers, the CLI could not ask) is taken for no runtime only when the process it names is not running. While that
 * process runs, or when the run file names none, it may be a runtime of this application that hangs or is starting,
 * so the answer is that nothing answered, and an update or an uninstall goes no further until a person has looked: a
 * reused process id can only err towards that answer. So is a run file that is there and cannot be read: only one that
 * is not there is no runtime. Nothing here is logged but what the CLI's own lines say.
 */

import { readFileSync, realpathSync } from 'node:fs';
import { posix, win32 } from 'node:path';
import type { Cli, CliResult } from './cli.ts';
import { sameResolved } from './resolved-path.ts';

export interface OwnRuntimeDeps {
    platform: NodeJS.Platform;
    /** The jason executable the application carries. */
    executable: string;
    /** `<data>/run/runtime.json`, which a person is told about when it is left behind. */
    runFile: string;
    /** The run file's text, or null when there is none; throws when it is there and cannot be read. */
    readRunFile(): string | null;
    cli: Cli;
    /** The file a path names, links followed; throws when there is none. */
    realpath(path: string): string;
    /** Whether a process of that id is running. */
    isRunning(pid: number): boolean;
}

/**
 * Whose the runtime a run file names is: none running, this application's, another's (one that answered and does not
 * run from the carried executable), or one that did not answer while the process it names may still be running.
 */
export type Whose = 'none' | 'ours' | 'another' | 'unanswered';

export interface RuntimeLook {
    whose: Whose;
    /** The process the run file names, or null when there is no run file or it names none. */
    pid: number | null;
    /** Set when the run file is there and could not be read. */
    unreadable?: true;
}

/** What the person is told when it is the demo's runtime, the sample installation's own and not theirs, that would not stop. */
export const demoRuntimeWords = 'the demo’s runtime, the sample installation’s own and not yours, is running and did not stop';

/** What a person does about it: quit the demo from its own window, or stop its runtime on the demo’s data directory. */
export const demoRuntimeNext = 'Open Jason in the demo and quit it, or stop that runtime with jason runtime stop run with JASON_DATA_DIR set to the demo’s data directory.';

/** What came of stopping this application's runtime: only `none`, `another` and `stopped` let an update or an uninstall go on. */
export type StopOutcome =
    | { kind: 'none' }
    | { kind: 'another' }
    | { kind: 'stopped' }
    | { kind: 'failed'; exit: number; demo?: true }
    | Unanswered;

/** A runtime that may be running and did not answer, with what a person is told to look at. */
export interface Unanswered {
    kind: 'unanswered';
    pid: number | null;
    runFile: string;
    unreadable?: true;
    /** Set when the runtime that did not answer is the demo's, not the person's (`stopWithDemo`). */
    demo?: true;
}

/** `jason runtime status` answers at once or not at all; this bounds a runtime that takes the request and never answers. */
const statusLimitMs = 30_000;
/** `jason runtime stop` waits thirty seconds for the process to exit before it says it did not; this is longer. */
const stopLimitMs = 60_000;

/** Whether a process of that id is running: one this account may not signal is running too. */
export function processIsRunning(pid: number): boolean {
    try {
        process.kill(pid, 0);
        return true;
    } catch (error) {
        return (error as NodeJS.ErrnoException).code !== 'ESRCH';
    }
}

/** A run file's text, or null only when it is not there; any other error, a folder in its place say, is thrown. */
export function readRunFileText(file: string): string | null {
    try {
        return readFileSync(file, 'utf8');
    } catch (error) {
        const code = (error as NodeJS.ErrnoException).code;
        if (code === 'ENOENT' || code === 'ENOTDIR') return null;
        throw error;
    }
}

/**
 * The dependencies the application looks with: the run file as it is on disk, the system's own resolver, which on
 * Windows expands a short name and gives a path's true case, and the process table by id.
 */
export function ownRuntimeDeps(platform: NodeJS.Platform, executable: string, runFile: string, cli: Cli): OwnRuntimeDeps {
    return { platform, executable, runFile, readRunFile: () => readRunFileText(runFile), cli, realpath: realpathSync.native, isRunning: processIsRunning };
}

/**
 * Why a runtime that did not answer stops what was asked, and what the person does next: ask the runtime first; then
 * learn from `jason runtime start` whether the process is the runtime (it refuses beside a runtime that is still running
 * and names its process, and starts in place of one whose process has gone or now belongs to another program); end the
 * process only once it is known to be a runtime that never answers; and delete the run file only when no runtime runs.
 */
export function unansweredWords(outcome: { pid: number | null; runFile: string; unreadable?: true }, jason = 'jason'): { why: string; next: string } {
    const deleting = `If no Jason runtime is running, deleting ${outcome.runFile} lets this go on.`;
    if (outcome.unreadable) {
        return {
            why: `a runtime may be running: its run file, ${outcome.runFile}, is there and could not be read`,
            next: `${jason} runtime status answers once the runtime does. ${deleting}`,
        };
    }
    const pid = outcome.pid;
    if (pid === null) {
        return {
            why: 'a runtime may be running that did not answer, and its run file names no process',
            next: `${jason} runtime status answers once the runtime does, and ${jason} runtime start starts one in place of a run file left behind. ${deleting}`,
        };
    }
    return {
        why: `a runtime may be running that did not answer: its run file names process ${pid}, which is running`,
        next: `${jason} runtime status answers once the runtime does. ${jason} runtime start says whether process ${pid} is the runtime: it names it if it is, and starts one in place of the run file if it is not. If it is the runtime and never answers, end process ${pid}. ${deleting}`,
    };
}

function parse(text: string): Record<string, unknown> | null {
    try {
        const raw: unknown = JSON.parse(text);
        return typeof raw === 'object' && raw !== null && !Array.isArray(raw) ? (raw as Record<string, unknown>) : null;
    } catch {
        return null;
    }
}

function errorCode(result: CliResult): string | null {
    const error = parse(result.stdout.trim())?.['error'];
    const code = typeof error === 'object' && error !== null ? (error as { code?: unknown }).code : null;
    return typeof code === 'string' ? code : null;
}

/** Whether a field names the carried executable: an absolute path that resolves to it, on Windows without regard to case. */
function namesCarried(deps: OwnRuntimeDeps, image: unknown): boolean {
    if (typeof image !== 'string' || image === '') return false;
    if (!(deps.platform === 'win32' ? win32 : posix).isAbsolute(image)) return false;
    return sameResolved(deps.platform, image, deps.executable, deps.realpath);
}

/** Whose the runtime the run file names is, and the process it names. */
export async function whoseRuntime(deps: OwnRuntimeDeps): Promise<RuntimeLook> {
    let text: string | null;
    try {
        text = deps.readRunFile();
    } catch {
        return { whose: 'unanswered', pid: null, unreadable: true };
    }
    if (text === null) return { whose: 'none', pid: null };
    const file = parse(text);
    const named = file?.['pid'];
    const pid = typeof named === 'number' && Number.isSafeInteger(named) && named > 0 ? named : null;

    const instance = file?.['instance_id'];
    if (typeof instance === 'string' && instance !== '') {
        const status = await deps.cli.run(['runtime', 'status'], { timeoutMs: statusLimitMs });
        if (status.exit === 0 && parse(status.stdout.trim())?.['instance_id'] === instance) {
            return { whose: namesCarried(deps, file?.['installed_image']) ? 'ours' : 'another', pid };
        }
    }
    // Not confirmed: the run file is left behind only when the process it names has gone.
    if (pid !== null && !deps.isRunning(pid)) return { whose: 'none', pid };
    return { whose: 'unanswered', pid };
}

/**
 * Stops the running runtime when it is this application's, and only then. A stop that says the runtime is gone (exit 0,
 * or exit 3 because there is no run file or nothing answers) is checked by a second look, which decides. A stop that
 * fails or does not finish in time leaves the runtime as it was, still running, and the caller goes no further.
 */
export async function stopOwnRuntime(deps: OwnRuntimeDeps): Promise<StopOutcome> {
    const look = await whoseRuntime(deps);
    if (look.whose === 'none' || look.whose === 'another') return { kind: look.whose };
    if (look.whose === 'unanswered') return unanswered(deps, look);

    const stop = await deps.cli.run(['runtime', 'stop'], { timeoutMs: stopLimitMs });
    const code = errorCode(stop);
    const gone = stop.exit === 0 || (stop.exit === 3 && (code === 'no_descriptor' || code === 'runtime_unreachable'));
    if (!gone) return { kind: 'failed', exit: stop.exit };
    const again = await whoseRuntime(deps);
    if (again.whose === 'ours') return { kind: 'failed', exit: stop.exit };
    if (again.whose === 'unanswered') return unanswered(deps, again);
    return { kind: 'stopped' };
}

function unanswered(deps: OwnRuntimeDeps, look: RuntimeLook): Unanswered {
    return { kind: 'unanswered', pid: look.pid, runFile: deps.runFile, ...(look.unreadable ? { unreadable: true as const } : {}) };
}
