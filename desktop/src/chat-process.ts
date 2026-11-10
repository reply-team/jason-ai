import { execFile, spawn } from 'node:child_process';
import { join } from 'node:path';
import { createInterface } from 'node:readline';
import type { Launch } from './program-path.ts';

/**
 * One chat turn's host process: the agent host the programs module found, started with a file and arguments and no
 * shell, the person's words written to its standard input and closed, its output read line by line as it comes. A stop
 * ends the whole tree the host started, its tools' processes with it: on Windows by `taskkill /T` from the system folder,
 * elsewhere by a signal to the process group the host leads.
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

export function startHost(
    launch: Exclude<Launch, { refused: string }>,
    args: readonly string[],
    options: { cwd: string; env: Record<string, string>; stdin: string; onLine(line: string): void },
): HostProcess {
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
    child.stdin.end(options.stdin, 'utf8');

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
        if (windows) {
            const taskkill = join(process.env.SystemRoot ?? process.env.windir ?? 'C:\\Windows', 'System32', 'taskkill.exe');
            execFile(taskkill, ['/PID', String(pid), '/T', '/F'], { windowsHide: true }, () => undefined);
            return;
        }
        try {
            process.kill(-pid, 'SIGTERM');
        } catch {
            return;
        }
        setTimeout(() => {
            if (done) return;
            try {
                process.kill(-pid, 'SIGKILL');
            } catch {
                // Gone already.
            }
        }, graceMs).unref();
    }

    return {
        stop() {
            if (stopped || done) return;
            stopped = true;
            stopTree();
        },
        exited,
        stderrTail: () => tail,
    };
}
