import { execFile } from 'node:child_process';
import { accessSync, constants, readFileSync, statSync } from 'node:fs';
import { endProgram } from './chat-process.ts';
import { findOnPath, launchOf, type Launch } from './program-path.ts';
import { withReplyClient } from './reply-client.ts';
import { withoutSessionMarkers } from './session-markers.ts';

/**
 * The programs a Connect card may start, and nothing else: the agent hosts the chat or a role can run on, the Reply CLI,
 * Node and npm. Each is found on the PATH the desktop was given and started without a shell.
 */
export const programNames = ['claude', 'codex', 'gemini', 'agy', 'cursor-agent', 'copilot', 'reply', 'node', 'npm'] as const;
export type ProgramName = (typeof programNames)[number];

export function isProgramName(value: unknown): value is ProgramName {
    return typeof value === 'string' && (programNames as readonly string[]).includes(value);
}

export interface RunResult {
    /** The exit code; -1 when the program could not be started or was stopped at its time limit. */
    exit: number;
    stdout: string;
    stderr: string;
}

export interface RunOptions {
    /** Written to the program's standard input, which is then closed. A key travels here, never on the command line. */
    stdin?: string;
    timeoutMs?: number;
    cwd?: string;
}

export interface Programs {
    /** Where the PATH finds the program, or null. */
    find(name: ProgramName): string | null;
    run(name: ProgramName, args: readonly string[], options?: RunOptions): Promise<RunResult>;
}

/** The value of PATH in an environment, whatever case Windows wrote its name in. */
export function pathOf(env: Readonly<Record<string, string | undefined>>): string {
    for (const [name, value] of Object.entries(env)) if (name.toUpperCase() === 'PATH' && value !== undefined) return value;
    return '';
}

/** The environment with its PATH replaced: one entry, whatever case the old one was in, so a child never sees two. */
export function withPath(env: Readonly<Record<string, string | undefined>>, path: string): Record<string, string> {
    const next: Record<string, string> = {};
    for (const [name, value] of Object.entries(env)) if (value !== undefined && name.toUpperCase() !== 'PATH') next[name] = value;
    next.PATH = path;
    return next;
}

function isProgram(file: string): boolean {
    try {
        if (!statSync(file).isFile()) return false;
        if (process.platform !== 'win32') accessSync(file, constants.X_OK);
        return true;
    } catch {
        return false;
    }
}

function isFile(file: string): boolean {
    try {
        return statSync(file).isFile();
    } catch {
        return false;
    }
}

function readText(file: string): string | null {
    try {
        return readFileSync(file, 'utf8');
    } catch {
        return null;
    }
}

const defaultLimit = 30_000;

/**
 * Runs a file with arguments and no shell, and reads what it said. Never rejects: a failure to start is an answer too.
 * At its time limit the answer is given at once, as stopped there, whether or not the program has gone, and the program
 * is ended as a stop ends a chat turn's host (`endProgram`), so one that ignores the first signal cannot hold a caller.
 */
export function runFile(file: string, args: readonly string[], env: Record<string, string>, options: RunOptions = {}): Promise<RunResult> {
    return new Promise((resolve) => {
        const limit = options.timeoutMs ?? defaultLimit;
        let bound: ReturnType<typeof setTimeout> | undefined;
        const answer = (result: RunResult) => {
            clearTimeout(bound);
            resolve(result);
        };
        const child = execFile(
            file,
            [...args],
            { env, cwd: options.cwd, windowsHide: true, maxBuffer: 8 * 1024 * 1024, encoding: 'utf8' },
            (error, stdout, stderr) => {
                const code = error && typeof (error as { code?: unknown }).code === 'number' ? ((error as { code: number }).code) : error ? -1 : 0;
                answer({ exit: code, stdout: String(stdout ?? ''), stderr: String(stderr ?? '') || (error && code === -1 ? error.message : '') });
            },
        );
        bound = setTimeout(() => {
            answer({ exit: -1, stdout: '', stderr: `It was stopped at its time limit of ${limit} ms.` });
            if (child.pid !== undefined && child.exitCode === null && child.signalCode === null) endProgram(child.pid);
        }, limit);
        child.stdin?.on('error', () => undefined);
        child.stdin?.end(options.stdin ?? '');
    });
}

/**
 * How to start a program the PATH finds with no shell, as the file and the arguments before the program's own (the
 * script an npm batch file hands to Node, on Windows), or why it cannot be. The chat's turn starts a host from this.
 */
export function programLaunch(env: Readonly<Record<string, string | undefined>>, name: ProgramName): Launch {
    if (!isProgramName(name)) return { refused: 'That is not a program the desktop starts.' };
    const lookup = { platform: process.platform, path: pathOf(env), pathext: env.PATHEXT ?? env.PathExt, isProgram };
    const found = findOnPath(name, lookup);
    if (found === null) return { refused: `${name} is not on the PATH.` };
    return launchOf(found, process.platform, readText, findOnPath('node', lookup), isFile);
}

/**
 * The programs on a PATH. The environment is the desktop's own with that PATH, less a parent Claude Code session's
 * markers (`sessionMarkers`); nothing is added to it, except that `reply` is given the harness's label built from the
 * bundle's version (`withReplyClient`) in place of any `REPLY_CLIENT` the desktop inherited.
 */
export function programsOn(env: Readonly<Record<string, string | undefined>>, bundleVersion: string | null): Programs {
    const path = pathOf(env);
    const childEnv = withPath(withoutSessionMarkers(env), path);
    const replyEnv = withReplyClient(childEnv, bundleVersion);
    const lookup = { platform: process.platform, path, pathext: env.PATHEXT ?? env.PathExt, isProgram };
    const find = (name: ProgramName) => findOnPath(name, lookup);
    return {
        find,
        async run(name, args, options) {
            if (!isProgramName(name)) return { exit: -1, stdout: '', stderr: 'That is not a program the desktop starts.' };
            const found = find(name);
            if (found === null) return { exit: -1, stdout: '', stderr: `${name} is not on the PATH.` };
            const launch = launchOf(found, process.platform, readText, findOnPath('node', lookup), isFile);
            if ('refused' in launch) return { exit: -1, stdout: '', stderr: launch.refused };
            return runFile(launch.file, [...launch.prefix, ...args], name === 'reply' ? replyEnv : childEnv, options);
        },
    };
}
