/**
 * Whether the runtime that is running is the one this application carries, and stopping it only then. An update
 * replaces the application's folder and an uninstall removes it, the jason executable inside it included, so a runtime
 * started from that executable is stopped first, the way a person would stop it: `jason runtime stop`, through the
 * carried CLI, waited out. A runtime started from any other executable, another installation's, is never stopped.
 *
 * What a runtime runs as is the `installed_image` of the run file it wrote (`docs/runtime-api.md`), believed only when
 * `jason runtime status` confirms that the runtime answering is that run file's instance, and only when it resolves to
 * the same file as the carried executable: on Windows without regard to case. A run file that cannot be read, an
 * instance that is not confirmed, a runtime that names no executable and a path that resolves to another file all mean
 * another runtime. Nothing here is logged but what the CLI's own lines say: no path, no instance.
 */

import type { Cli, CliResult } from './cli.ts';

export interface OwnRuntimeDeps {
    platform: NodeJS.Platform;
    /** The jason executable the application carries. */
    executable: string;
    /** `<data>/run/runtime.json`, or null when there is none. */
    readRunFile(): string | null;
    cli: Cli;
    /** The file a path names, links followed; throws when there is none. */
    realpath(path: string): string;
}

export type Whose = 'none' | 'ours' | 'another';

/** `jason runtime status` answers at once or not at all; this bounds a runtime that takes the request and never answers. */
const statusLimitMs = 30_000;
/** `jason runtime stop` waits thirty seconds for the process to exit before it says it did not; this is longer. */
const stopLimitMs = 60_000;

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

/** Whether two paths name one file once both are resolved: on Windows without regard to case. */
function sameFile(deps: OwnRuntimeDeps, image: string): boolean {
    let named: string;
    let carried: string;
    try {
        named = deps.realpath(image);
        carried = deps.realpath(deps.executable);
    } catch {
        return false;
    }
    return deps.platform === 'win32' ? named.toLowerCase() === carried.toLowerCase() : named === carried;
}

/** Whose the running runtime is: none running, this application's, or another's. */
export async function whoseRuntime(deps: OwnRuntimeDeps): Promise<Whose> {
    const text = deps.readRunFile();
    if (text === null) return 'none';
    const file = parse(text);
    const instance = file?.['instance_id'];
    if (typeof instance !== 'string' || instance === '') return 'another';

    const status = await deps.cli.run(['runtime', 'status'], { timeoutMs: statusLimitMs });
    if (status.exit !== 0) {
        // Nothing answers at the run file's address, or there is no run file any more: no runtime is running from it.
        const code = errorCode(status);
        return status.exit === 3 && (code === 'no_descriptor' || code === 'runtime_unreachable') ? 'none' : 'another';
    }
    if (parse(status.stdout.trim())?.['instance_id'] !== instance) return 'another';

    const image = file?.['installed_image'];
    return typeof image === 'string' && image !== '' && sameFile(deps, image) ? 'ours' : 'another';
}

/**
 * Stops the running runtime when it is this application's, and only then. Null when none of this application's is
 * running any more: there was none, it was another's, or it stopped. Otherwise the exit of `jason runtime stop`, -1 when
 * it could not be started or did not finish in time; the runtime is then left as it was, still running, and the caller
 * goes no further.
 */
export async function stopOwnRuntime(deps: OwnRuntimeDeps): Promise<{ exit: number } | null> {
    if ((await whoseRuntime(deps)) !== 'ours') return null;
    const stop = await deps.cli.run(['runtime', 'stop'], { timeoutMs: stopLimitMs });
    const gone = stop.exit === 0 || (stop.exit === 3 && errorCode(stop) === 'no_descriptor');
    if (gone && (await whoseRuntime(deps)) !== 'ours') return null;
    return { exit: stop.exit };
}
