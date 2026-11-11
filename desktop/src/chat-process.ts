import { execFile, spawn } from 'node:child_process';
import { join } from 'node:path';
import { createInterface } from 'node:readline';
import type { Writable } from 'node:stream';
import type { Launch } from './program-path.ts';

/**
 * One chat turn's host process: the agent host the programs module found, started with a file and arguments and no
 * shell, the person's words written to its standard input and closed, its output read line by line as it comes. A stop
 * ends the whole tree the host started, its tools' processes with it: on Windows by `taskkill /T` from the system folder,
 * elsewhere by a signal to the process group the host leads. Beside it one more start, made the same way: Codex's model
 * listing, `codex app-server` and nothing else, whose standard input stays open for the lines of its exchange
 * (`startListing`).
 *
 * This is a module of its own, beside the programs module, because a turn streams for as long as the host runs and is
 * stopped from outside, where the programs module runs a program to its end and buffers what it said.
 */

export interface HostExit {
    /** The exit code; null when the host was ended by a signal or could not be started. */
    code: number | null;
    /** Why it could not be started, in the system's words; null when it started. */
    startError: string | null;
}

export interface HostProcess {
    /** Ends the host and everything it started. Calling it again, or after the host exited, does nothing. */
    stop(): void;
    /** Resolves once the host has exited and its output has been read to the end. */
    exited: Promise<HostExit>;
    /** The last part of what the host wrote to its standard error, for the words of a failure. */
    stderrTail(): string;
}

const stderrKept = 2000;
const graceMs = 2000;

/**
 * How a stop ends a POSIX host's process group: SIGTERM to the group, then after the grace period SIGKILL to the group
 * whether or not the host itself has exited by then, since a process it started may ignore SIGTERM and outlive it. A
 * group already empty throws, and the throw is caught. `kill` and `later` are the system's, given here so a row can
 * record them.
 */
export function signalGroup(
    pid: number,
    kill: (pid: number, signal: NodeJS.Signals) => void,
    later: (run: () => void, ms: number) => void,
): void {
    signalThenKill(-pid, kill, later);
}

function signalThenKill(
    target: number,
    kill: (pid: number, signal: NodeJS.Signals) => void,
    later: (run: () => void, ms: number) => void,
): void {
    try {
        kill(target, 'SIGTERM');
    } catch {
        return;
    }
    later(() => {
        try {
            kill(target, 'SIGKILL');
        } catch {
            // The group, or the process, is gone.
        }
    }, graceMs);
}

/**
 * Ends a process and what it started the way a stop ends a turn's host: on Windows `taskkill /T` from the system folder,
 * elsewhere SIGTERM and then, after the grace period, SIGKILL, to the group the host leads or to a lone process.
 */
function endTree(pid: number, group: boolean): void {
    if (process.platform === 'win32') {
        const taskkill = join(process.env.SystemRoot ?? process.env.windir ?? 'C:\\Windows', 'System32', 'taskkill.exe');
        execFile(taskkill, ['/PID', String(pid), '/T', '/F'], { windowsHide: true }, () => undefined);
        return;
    }
    signalThenKill(
        group ? -pid : pid,
        (target, signal) => process.kill(target, signal),
        (run, ms) => setTimeout(run, ms).unref(),
    );
}

/** Ends a program the programs module started that ran past its time limit, as a stop ends a turn's host. */
export function endProgram(pid: number): void {
    endTree(pid, false);
}

/** A listing's host process: a turn's, with its standard input kept open for the exchange's lines. */
export interface ListingProcess extends HostProcess {
    /** Writes one line, and its line end, to the host's standard input. */
    write(line: string): void;
    /** Closes the host's standard input. */
    endInput(): void;
}

/** The arguments of the one listing a host is started for: `codex app-server`, over standard input and output. */
export const listingArguments: readonly string[] = ['app-server'];

export function startHost(
    launch: Exclude<Launch, { refused: string }>,
    args: readonly string[],
    options: { cwd: string; env: Record<string, string>; stdin: string; onLine(line: string): void },
): HostProcess {
    const { input, process: host } = spawnHost(launch, args, options);
    input.end(options.stdin, 'utf8');
    return host;
}

/**
 * Starts Codex's model listing, `codex app-server` and no other arguments, as a turn's host is started: the file and its
 * prefix the programs module found, no shell, no window, its own process group elsewhere than Windows, and the same stop.
 */
export function startListing(
    launch: Exclude<Launch, { refused: string }>,
    options: { cwd: string; env: Record<string, string>; onLine(line: string): void },
): ListingProcess {
    const { input, process: host } = spawnHost(launch, listingArguments, options);
    return {
        ...host,
        write(line) {
            if (!input.writableEnded) input.write(`${line}\n`, 'utf8');
        },
        endInput() {
            if (!input.writableEnded) input.end();
        },
    };
}

function spawnHost(
    launch: Exclude<Launch, { refused: string }>,
    args: readonly string[],
    options: { cwd: string; env: Record<string, string>; onLine(line: string): void },
): { input: Writable; process: HostProcess } {
    const windows = process.platform === 'win32';
    const child = spawn(launch.file, [...launch.prefix, ...args], {
        cwd: options.cwd,
        env: options.env,
        stdio: ['pipe', 'pipe', 'pipe'],
        windowsHide: true,
        // Elsewhere the host leads a process group of its own, so a stop reaches what it started.
        detached: !windows,
    });
    let tail = '';
    let stopped = false;
    let done = false;
    child.stderr.setEncoding('utf8');
    child.stderr.on('data', (chunk: string) => {
        tail = (tail + chunk).slice(-stderrKept);
    });
    const lines = createInterface({ input: child.stdout, crlfDelay: Infinity });
    lines.on('line', (line) => options.onLine(line));
    child.stdin.on('error', () => undefined);

    const exited = new Promise<HostExit>((resolve) => {
        child.once('error', (error) => {
            done = true;
            resolve({ code: null, startError: error.message });
        });
        // 'close' comes after the output has ended, and so after its last line was read.
        child.once('close', (code) => {
            done = true;
            resolve({ code, startError: null });
        });
    });

    function stopTree(): void {
        const pid = child.pid;
        if (pid === undefined) return;
        endTree(pid, true);
    }

    return {
        input: child.stdin,
        process: {
            stop() {
                if (stopped || done) return;
                stopped = true;
                stopTree();
            },
            exited,
            stderrTail: () => tail,
        },
    };
}
