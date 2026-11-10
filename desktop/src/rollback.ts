/**
 * The record an update leaves, and what a launch makes of it. Before an update installs, the application's own folder
 * as it is installed (the `.app` on macOS, the installation folder on Windows) is copied under
 * `<data>/desktop/rollback/<version>/` and the update is recorded as pending. The first launch of the new version settles
 * it: a start that loads the window removes the copy; one that does not keeps it, says where it is, and refuses that
 * version from then on. Only the application is copied: nothing of the runtime's data, and nothing outside the folder
 * the application is installed in.
 */

import { mkdirSync, readdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { cp } from 'node:fs/promises';
import { basename, dirname, join, posix, win32 } from 'node:path';
import type { Logger } from './log.ts';

export interface PendingUpdate {
    fromVersion: string;
    toVersion: string;
    /** The folder holding the copy of the application as it was installed. */
    directory: string;
}

export interface RollbackState {
    pending?: PendingUpdate | undefined;
    /** Versions whose first start failed here; the updater does not offer them again. */
    rejected: string[];
}

export const emptyRollbackState: RollbackState = { rejected: [] };

export function parseRollbackState(text: string | null): RollbackState {
    if (text === null) return emptyRollbackState;
    try {
        const raw = JSON.parse(text) as Partial<RollbackState>;
        const rejected = Array.isArray(raw.rejected) ? raw.rejected.filter((v) => typeof v === 'string') : [];
        const p = raw.pending;
        const pending =
            p && typeof p.fromVersion === 'string' && typeof p.toVersion === 'string' && typeof p.directory === 'string'
                ? { fromVersion: p.fromVersion, toVersion: p.toVersion, directory: p.directory }
                : undefined;
        return { pending, rejected };
    } catch {
        return emptyRollbackState;
    }
}

/** True on the first launch of the version a pending update installed: only then is the record settled. */
export function isFirstLaunchOfUpdate(state: RollbackState, currentVersion: string): boolean {
    return state.pending?.toVersion === currentVersion;
}

export function afterSuccessfulStart(state: RollbackState): RollbackState {
    return { rejected: state.rejected };
}

export function afterFailedStart(state: RollbackState): RollbackState {
    const failed = state.pending?.toVersion;
    if (!failed) return state;
    return { pending: state.pending, rejected: [...new Set([...state.rejected, failed])] };
}

export function isRejected(state: RollbackState, version: string): boolean {
    return state.rejected.includes(version);
}

/** The folder the application is installed as: the `.app` around the executable on macOS, its folder elsewhere. */
export function applicationFolder(platform: NodeJS.Platform, executable: string): string {
    if (platform === 'darwin') return posix.dirname(posix.dirname(posix.dirname(executable)));
    return platform === 'win32' ? win32.dirname(executable) : posix.dirname(executable);
}

/** `<rollback>/state.json`: the record. */
export function rollbackStateFile(rollback: string): string {
    return join(rollback, 'state.json');
}

export function readRollback(rollback: string): RollbackState {
    try {
        return parseRollbackState(readFileSync(rollbackStateFile(rollback), 'utf8'));
    } catch {
        return emptyRollbackState;
    }
}

export function writeRollback(rollback: string, state: RollbackState): void {
    mkdirSync(rollback, { recursive: true });
    const file = rollbackStateFile(rollback);
    const temporary = `${file}.tmp`;
    writeFileSync(temporary, `${JSON.stringify(state, null, 2)}\n`);
    renameSync(temporary, file);
}

/**
 * Copies the installed application under `<rollback>/<fromVersion>/` and records the update as pending; whatever copy an
 * earlier update left is removed first, so one previous version is kept at most. Returns the folder of the copy.
 */
export async function keepPrevious(rollback: string, application: string, fromVersion: string, toVersion: string): Promise<string> {
    const state = readRollback(rollback);
    let kept: string[] = [];
    try {
        kept = readdirSync(rollback);
    } catch {
        // No copy was kept yet.
    }
    for (const name of kept) if (name !== basename(rollbackStateFile(rollback))) rmSync(join(rollback, name), { recursive: true, force: true });
    const directory = join(rollback, fromVersion, application.split(/[\\/]/).filter(Boolean).at(-1) ?? 'Jason');
    mkdirSync(directory, { recursive: true });
    await cp(application, directory, { recursive: true, verbatimSymlinks: true });
    writeRollback(rollback, { rejected: state.rejected, pending: { fromVersion, toVersion, directory } });
    return directory;
}

/**
 * Settles an update's first launch with whether the window loaded. Returns the folder of the previous version when that
 * start failed and the copy is kept, null otherwise.
 */
export function settleStart(rollback: string, currentVersion: string, started: boolean, log: Logger): string | null {
    const state = readRollback(rollback);
    if (!isFirstLaunchOfUpdate(state, currentVersion) || !state.pending) return null;
    const { directory } = state.pending;
    writeRollback(rollback, started ? afterSuccessfulStart(state) : afterFailedStart(state));
    log.info('update_settled', { Version: currentVersion, Started: started });
    if (!started) return directory;
    rmSync(dirname(directory), { recursive: true, force: true });
    return null;
}
