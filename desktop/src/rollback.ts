/**
 * The record an update leaves, and what a launch makes of it. Before an update installs, the application's own folder
 * as it is installed (the `.app` on macOS, the installation folder on Windows), or on Linux the AppImage file itself, is
 * copied under `<data>/desktop/rollback/<version>/` and the update is recorded as pending. The deb keeps no copy: putting
 * `/opt/Jason` back by hand would need an administrator and leave dpkg's record on the newer version, so its record names
 * no copy and the starting page names the previous release's deb to install again instead. The first launch of the new version settles
 * it: a start that loads the window records the copy as kept; one that does not keeps it too, says where it is, and
 * refuses that version from then on. Either way the copy stays until the next update replaces it, and the menus name
 * its version while it is there (`keptLine`). Only the application is copied: nothing of the runtime's data, and nothing
 * outside the folder the application is installed in.
 */

import * as nodeFiles from 'node:fs';
import { basename, dirname, isAbsolute, join, posix, relative, win32 } from 'node:path';
import { compareVersions } from './host-version.ts';
import type { Logger } from './log.ts';

/**
 * The file system this module reads and writes with: Electron's `original-fs` where it runs under Electron, else Node's
 * own, which outside Electron is the same thing. In Electron's main process `node:fs` reads an `.asar` as an archive, so a
 * copy of the application's folder through it takes `resources/app.asar` for a folder and fails; `original-fs` reads it
 * as the file it is. Only this module's calls use it: everything else of the main process still reads its own archive.
 */
const files: typeof nodeFiles = (process.getBuiltinModule('original-fs') as typeof nodeFiles | undefined) ?? nodeFiles;
const { mkdirSync, readdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } = files;

/** How a file or a folder is copied: `cp` of the file system above, unless a test says otherwise. */
export type Copy = (source: string, target: string, options: nodeFiles.CopyOptions) => Promise<void>;

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
 * The menus' line while a copy of the previous version is there, the tray's and the window's alike: the copy a start
 * that worked recorded as kept, or the one a first start that failed left pending. It names the version and never the
 * folder, whose path would make a menu as wide as itself; a start that failed and an update that was not installed name
 * the folder, where a person needs it. Null when no copy is there, or when it is of the version that runs.
 */
export function keptLine(state: RollbackState, currentVersion: string, exists: (path: string) => boolean): string | null {
    const pending = state.pending?.toVersion === currentVersion ? { version: state.pending.fromVersion, directory: state.pending.directory } : undefined;
    const copy = state.kept ?? pending;
    if (copy === undefined || copy.directory === null || copy.version === currentVersion || !exists(copy.directory)) return null;
    return `The previous version ${copy.version} is kept.`;
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
async function copyOutside(source: string, target: string, data: string, copy: Copy): Promise<void> {
    if (!within(source, data)) {
        await copy(source, target, { recursive: true, verbatimSymlinks: true });
        return;
    }
    mkdirSync(target, { recursive: true });
    for (const name of readdirSync(source)) {
        const path = join(source, name);
        if (within(data, path)) continue;
        await copyOutside(path, join(target, name), data, copy);
    }
}

/**
 * Copies the installed application under `<rollback>/<fromVersion>/` and records the update as pending; whatever copy an
 * earlier update left, kept or pending, is removed first, so one previous version is kept at most. Returns the copy. An
 * application that is one file, the AppImage, is copied as that file; a null one, the deb, is recorded with no copy. A
 * copy that fails leaves nothing of itself and records no copy: what it copied is removed, the record no longer names the
 * earlier copy, which was removed before it, and its error is thrown.
 */
export async function keepPrevious(
    rollback: string,
    application: string | null,
    fromVersion: string,
    toVersion: string,
    copy: Copy = files.promises.cp,
): Promise<string | null> {
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
    try {
        if (statSync(application).isFile()) {
            mkdirSync(dirname(directory), { recursive: true });
            await copy(application, directory, {});
        } else {
            mkdirSync(directory, { recursive: true });
            // `<rollback>` is `<data>/desktop/rollback`: a data directory placed inside the application's folder stays out
            // of the copy, so the copy never holds the run token, a provider key or anything else of the installation's state.
            await copyOutside(application, directory, dirname(dirname(rollback)), copy);
        }
    } catch (error) {
        try {
            rmSync(join(rollback, fromVersion), { recursive: true, force: true });
        } catch {
            // The copy's own error is the one thrown; a folder that cannot be removed either is the next update's to remove.
        }
        // The earlier copy went before this one was made, so the record names none: kept, or pending from an earlier update.
        if (state.kept !== undefined || (state.pending !== undefined && state.pending.directory !== null)) {
            try {
                writeRollback(rollback, { rejected: state.rejected });
            } catch {
                // A record that cannot be written is read as it was; the menu names a copy only while its folder is there.
            }
        }
        throw error;
    }
    writeRollback(rollback, { rejected: state.rejected, pending: { fromVersion, toVersion, directory } });
    return directory;
}

/** Where the version an update replaced is: a copy kept here, or the release's package to install again (the deb), of its version. */
export type Previous = { copy: string } | { release: string; file: string; version: string };

/** The deb's first release whose package carries the key of Jason's apt source, and the source it writes. */
const keyedSince = '0.49.1';
const aptSource = '/etc/apt/sources.list.d/jason-desktop.sources';

/**
 * What the starting page says when an update's first start failed. Nothing puts the previous version back by itself: the
 * runtime has no guard against a data directory a newer version migrated, so going back is the person's act alone.
 */
export function keptNotice(said: string, previous: Previous, installed: string, platform: NodeJS.Platform): string {
    const back =
        'release' in previous
            ? [
                  `If this version is the cause, the version of Jason it was updated from is published at ${previous.release}.`,
                  // An earlier package carries no key for the source, which its install keeps: every apt update would report it.
                  compareVersions(previous.version.split('+')[0] ?? previous.version, keyedSince) < 0
                      ? `To go back, quit Jason, download ${previous.file} from that page, and in the folder it is in run sudo rm ${aptSource}, since that release carries no key for Jason's apt source, then sudo apt install ./${previous.file}: nothing does it for you.`
                      : `To go back, quit Jason, download ${previous.file} from that page, and in the folder it is in run sudo apt install ./${previous.file}: nothing does it for you.`,
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
 * replaced is. A data directory whose layout `jason runtime start` refused, or whose database a newer Jason migrated, is
 * said in those words alone: the version an update replaced knows that directory no better, so going back is not offered.
 */
export function failedStartNotice(said: string, layoutRefused: boolean, previous: Previous | null, installed: string, platform: NodeJS.Platform): string {
    return previous === null || layoutRefused ? said : keptNotice(said, previous, installed, platform);
}

/**
 * Settles an update's first launch that did not load the window, as `settleStart` does, but for a start `jason runtime
 * start` refused for the data directory's layout, or for a database a newer Jason migrated: then the record stays as it
 * is, since the cause is the directory and
 * not the version, so that version is not refused, and the next start settles the same record. Returns the pending
 * record where the person is to be told where the previous version is, null otherwise.
 */
export function settleFailedStart(rollback: string, currentVersion: string, layoutRefused: boolean, log: Logger): PendingUpdate | null {
    return layoutRefused ? null : settleStart(rollback, currentVersion, false, log);
}

/**
 * Settles an update a terminal asked for and the installer made with no window (`update-request.ts`): the `--update-settle`
 * start of the new version, made once that version's runtime answered, records it as a start that worked records it,
 * the copy kept and the version not refused. Only the update that installed this version is settled, and once: a record
 * that names no such update is left as it is, so a second settle or one after a window's changes nothing. An update whose
 * runtime did not come up is not settled here: it stays pending, and the next start of a Live window settles it and
 * says where the copy is. Answers whether it settled.
 */
export function settleByRequest(rollback: string, currentVersion: string, log: Logger): boolean {
    const state = readRollback(rollback);
    if (!isFirstLaunchOfUpdate(state, currentVersion)) return false;
    writeRollback(rollback, afterSuccessfulStart(state));
    log.info('update_settled_by_request', { Version: currentVersion });
    return true;
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
