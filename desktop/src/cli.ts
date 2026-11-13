import { execFile } from 'node:child_process';
import { withoutSessionMarkers } from './session-markers.ts';

/**
 * The jason executable the application carries, and the only way the desktop changes the installation: every verb it
 * runs is one a person could type. Started with a file and arguments, never through a shell, with the desktop's own
 * environment and nothing added to it, so the CLI finds the same data directory and harness root a terminal would; only
 * a parent Claude Code session's markers are taken out (`sessionMarkers`), which would otherwise ride down through the
 * runtime to its plugins.
 */

export interface CliResult {
    /** The verb's exit code (0 success, 1 refused, 2 usage, 3 runtime unreachable); -1 when it could not be started. */
    exit: number;
    stdout: string;
    stderr: string;
}

export interface Cli {
    /** `spawned` is told the started process's id, once it has one. */
    run(args: readonly string[], options?: { timeoutMs?: number; cwd?: string; spawned?: (pid: number) => void }): Promise<CliResult>;
}

const defaultLimit = 120_000;

/**
 * The CLI at `file`. `prefix` comes before the verb's arguments; a test names Node and a stub script there, so the same
 * code that runs the real executable runs the stub.
 */
export function cliAt(file: string, env: Record<string, string>, prefix: readonly string[] = []): Cli {
    return {
        run(args, options) {
            return new Promise((resolve) => {
                const child = execFile(
                    file,
                    [...prefix, ...args],
                    { env: withoutSessionMarkers(env), cwd: options?.cwd, timeout: options?.timeoutMs ?? defaultLimit, windowsHide: true, maxBuffer: 16 * 1024 * 1024, encoding: 'utf8' },
                    (error, stdout, stderr) => {
                        const code = (error as { code?: unknown } | null)?.code;
                        const exit = typeof code === 'number' ? code : error ? -1 : 0;
                        resolve({ exit, stdout: String(stdout ?? ''), stderr: String(stderr ?? '') || (exit === -1 && error ? error.message : '') });
                    },
                );
                if (child.pid !== undefined) options?.spawned?.(child.pid);
                child.stdin?.on('error', () => undefined);
                child.stdin?.end();
            });
        },
    };
}
