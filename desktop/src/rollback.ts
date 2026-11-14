/**
 * The record an update leaves, and what a launch makes of it. Before an update installs, the application's own folder
 * as it is installed (the `.app` on macOS, the installation folder on Windows), or on Linux the AppImage file itself, is
 * copied under `<data>/desktop/rollback/<version>/` and the update is recorded as pending. The deb keeps no copy: putting
 * `/opt/Jason` back by hand would need an administrator and leave dpkg's record on the newer version, so its record names
 * no copy and the starting page names the previous release's deb to install again instead. The first launch of the new version settles
 * it: a start that loads the window records the copy as kept; one that does not keeps it too, says where it is, and
 * refuses that version from then on. Either way the copy stays until the next update replaces it, and the Jason menu
 * names it while it is there (`keptLine`). Only the application is copied: nothing of the runtime's data, and nothing
 * outside the folder the application is installed in.
 */

import { mkdirSync, readdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { cp } from 'node:fs/promises';
import { basename, dirname, isAbsolute, join, posix, relative, win32 } from 'node:path';
import type { Logger } from './log.ts';

export interface PendingUpdate {
    fromVersion: string;
    toVersion: string;
    /** The copy of the application as it was installed, or null where none is kept (the deb). */
    directory: string | null;
}

/** The copy of the version an update that started replaced, kept until the next update replaces it. */
export interface KeptCopy {
    version: string;
    directory: string;
}

export interface RollbackState {
    pending?: PendingUpdate | undefined;
    kept?: KeptCopy | undefined;
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
            p && typeof p.fromVersion === 'string' && typeof p.toVersion === 'string' && (typeof p.directory === 'string' || p.directory === null)
                ? { fromVersion: p.fromVersion, toVersion: p.toVersion, directory: p.directory }
                : undefined;
        const k = raw.kept;
        const kept = k && typeof k.version === 'string' && typeof k.directory === 'string' ? { version: k.version, directory: k.directory } : undefined;
        return kept === undefined ? { pending, rejected } : { pending, kept, rejected };
    } catch {
        return emptyRollbackState;
    }
}

/** True on the first launch of the version a pending update installed: only then is the record settled. */
export function isFirstLaunchOfUpdate(state: RollbackState, currentVersion: string): boolean {
    return state.pending?.toVersion === currentVersion;
}

/** The update started: its copy of the previous version is kept, and what was refused stays refused. */
export function afterSuccessfulStart(state: RollbackState): RollbackState {
    if (!state.pending || state.pending.directory === null) return { rejected: state.rejected };
    return { kept: { version: state.pending.fromVersion, directory: state.pending.directory }, rejected: state.rejected };
}

export function afterFailedStart(state: RollbackState): RollbackState {
    const failed = state.pending?.toVersion;
    if (!failed) return state;
    return { pending: state.pending, rejected: [...new Set([...state.rejected, failed])] };
}

/** The versions the record names as previous: the copy kept, or the version a pending update came from. */
export function rollbackVersions(state: RollbackState): string[] {
    return [...new Set([state.kept?.version, state.pending?.fromVersion].filter((version): version is string => version !== undefined))];
}

export function isRejected(state: RollbackState, version: string): boolean {
    return state.rejected.includes(version);
}

/**
 * The Jason menu's line while a copy of the previous version is there: the copy a start that worked recorded as kept, or
 * the one a first start that failed left pending. Null when no copy is there, or when it is of the version that runs.
 */
export function keptLine(state: RollbackState, currentVersion: string, exists: (path: string) => boolean): string | null {
    const pending = state.pending?.toVersion === currentVersion ? { version: state.pending.fromVersion, directory: state.pending.directory } : undefined;
    const copy = state.kept ?? pending;
    if (copy === undefined || copy.directory === null || copy.version === currentVersion || !exists(copy.directory)) return null;
    return `The previous version ${copy.version} is kept at ${copy.directory}; put it back by hand.`;
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

/** Whether `path` is `folder` or lies inside it. */
function within(folder: string, path: string): boolean {
    const rest = relative(folder, path);
    return rest === '' || (!rest.startsWith('..') && !isAbsolute(rest));
}

/** Copies `source` into `target`, leaving out `data` wherever it lies inside `source`. */
async function copyOutside(source: string, target: string, data: string): Promise<void> {
    if (!within(source, data)) {
        await cp(source, target, { recursive: true, verbatimSymlinks: true });
        return;
    }
    mkdirSync(target, { recursive: true });
    for (const name of readdirSync(source)) {
        const path = join(source, name);
        if (within(data, path)) continue;
        await copyOutside(path, join(target, name), data);
    }
}

/**
 * Copies the installed application under `<rollback>/<fromVersion>/` and records the update as pending; whatever copy an
 * earlier update left, kept or pending, is removed first, so one previous version is kept at most. Returns the copy. An
 * application that is one file, the AppImage, is copied as that file; a null one, the deb, is recorded with no copy.
 */
export async function keepPrevious(rollback: string, application: string | null, fromVersion: string, toVersion: string): Promise<string | null> {
    const state = readRollback(rollback);
    let kept: string[] = [];
    try {
        kept = readdirSync(rollback);
    } catch {
        // No copy was kept yet.
    }
    for (const name of kept) if (name !== basename(rollbackStateFile(rollback))) rmSync(join(rollback, name), { recursive: true, force: true });
    if (application === null) {
        writeRollback(rollback, { rejected: state.rejected, pending: { fromVersion, toVersion, directory: null } });
        return null;
    }
    const directory = join(rollback, fromVersion, application.split(/[\\/]/).filter(Boolean).at(-1) ?? 'Jason');
    if (statSync(application).isFile()) {
        mkdirSync(dirname(directory), { recursive: true });
        await cp(application, directory);
        writeRollback(rollback, { rejected: state.rejected, pending: { fromVersion, toVersion, directory } });
        return directory;
    }
    mkdirSync(directory, { recursive: true });
    // `<rollback>` is `<data>/desktop/rollback`: a data directory placed inside the application's folder stays out of the
    // copy, so the copy never holds the run token, a provider key or anything else of the installation's state.
    await copyOutside(application, directory, dirname(dirname(rollback)));
    writeRollback(rollback, { rejected: state.rejected, pending: { fromVersion, toVersion, directory } });
    return directory;
}

/** Where the version an update replaced is: a copy kept here, or the release's package to install again (the deb). */
export type Previous = { copy: string } | { release: string; file: string };

/**
 * What the starting page says when an update's first start failed. Nothing puts the previous version back by itself: the
 * runtime has no guard against a data directory a newer version migrated, so going back is the person's act alone.
 */
export function keptNotice(said: string, previous: Previous, installed: string, platform: NodeJS.Platform): string {
    const back =
        'release' in previous
            ? [
                  `If this version is the cause, the version of Jason it was updated from is published at ${previous.release}.`,
                  `To go back, quit Jason, download ${previous.file} from that page, and in the folder it is in run sudo apt install ./${previous.file}: nothing does it for you.`,
              ]
            : [
                  `If this version is the cause, the version of Jason it was updated from is kept at ${previous.copy}.`,
                  // On Linux the copy is the AppImage file.
                  `To go back, quit Jason and put that ${platform === 'linux' ? 'file' : 'folder'} back in the place of the installed one, ${installed}, by hand: nothing does it for you.`,
              ];
    return [
        said,
        ...back,
        'This puts back the previous application and the Jason it carried; the data directory stays as the newer version left it.',
        // The installer registered the newer version with Windows; a folder put back by hand does not change that entry.
        ...(platform === 'win32' ? ['The system’s list of installed programs keeps showing the newer version’s number after that.'] : []),
    ].join(' ');
}

/**
 * What the starting page says when a start failed: what failed, and after an update's first start, where the version it
 * replaced is. A data directory whose layout `jason runtime start` refused is said in those words alone: the version an
 * update replaced knows that directory no better, so going back is not offered.
 */
export function failedStartNotice(said: string, layoutRefused: boolean, previous: Previous | null, installed: string, platform: NodeJS.Platform): string {
    return previous === null || layoutRefused ? said : keptNotice(said, previous, installed, platform);
}

/**
 * Settles an update's first launch with whether the window loaded; the copy is kept either way. Returns the pending
 * record, the version it came from and its copy, when that start failed, null otherwise.
 */
export function settleStart(rollback: string, currentVersion: string, started: boolean, log: Logger): PendingUpdate | null {
    const state = readRollback(rollback);
    if (!isFirstLaunchOfUpdate(state, currentVersion) || !state.pending) return null;
    const { pending } = state;
    writeRollback(rollback, started ? afterSuccessfulStart(state) : afterFailedStart(state));
    log.info('update_settled', { Version: currentVersion, Started: started });
    return started ? null : pending;
}
