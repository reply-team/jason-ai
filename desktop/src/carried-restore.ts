/**
 * A missing carried `jason.exe` put back at a start, on Windows (`docs/release-and-update.md` §7). A carried
 * `jason update apply` moves its own image out of the application's folder to
 * `%LOCALAPPDATA%\jason-desktop-updater\jason-carried\<request>\jason.exe` before the installer runs, writing beside it
 * `jason.version`, the version it came from alone, as the application's `version.json` names it. Where the installer
 * then installs nothing, a kill or a power cut in the one gap leaves the application's folder the old version with no
 * `jason.exe` in it. A start of the application finds that: `resources\bundle\jason.exe` missing, the bundle's
 * `version.json` naming a version, and a stored image whose `jason.version` names the same one; it copies that image back,
 * through a file beside the target renamed over it, so a half-written copy is never the carried file. An image of
 * another version is never put back: the folder is no longer the build it came from.
 */

import { copyFileSync, existsSync, readdirSync, readFileSync, renameSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import type { Logger } from './log.ts';

/** The name of the file beside a stored image that names the version it came from (`CarriedUpdate.MovedVersionName`). */
export const movedVersionName = 'jason.version';

/** `%LOCALAPPDATA%\jason-desktop-updater\jason-carried`, the store `CarriedUpdate.Store` names. */
export function carriedStore(updaterCache: string): string {
    return join(updaterCache, 'jason-carried');
}

export interface RestoreFiles {
    exists(path: string): boolean;
    /** The folders directly in a folder, by name; none where it cannot be read. */
    folders(path: string): string[];
    /** A file's text, or null where it cannot be read. */
    read(path: string): string | null;
    /** The copy through a file beside the target, renamed over it. */
    copy(from: string, to: string): void;
}

export type Restored = { act: 'present' } | { act: 'restored'; from: string } | { act: 'none' } | { act: 'failed'; from: string };

/**
 * Pure but for what it is given: where the carried file is missing and the application names a version, the first
 * stored image whose `jason.version` names that version, copied back. Anything else changes nothing.
 */
export function restoreCarried(input: { executable: string; version: string | null; store: string; files: RestoreFiles }): Restored {
    const { executable, version, store, files } = input;
    if (files.exists(executable)) return { act: 'present' };
    if (version === null) return { act: 'none' };
    for (const folder of files.folders(store).sort()) {
        const image = join(store, folder, 'jason.exe');
        if (files.read(join(store, folder, movedVersionName))?.trim() !== version || !files.exists(image)) continue;
        try {
            files.copy(image, executable);
            return { act: 'restored', from: image };
        } catch {
            return { act: 'failed', from: image };
        }
    }
    return { act: 'none' };
}

/** The files as they are on disk. */
export const diskFiles: RestoreFiles = {
    exists: (path) => existsSync(path),
    folders(path) {
        try {
            return readdirSync(path, { withFileTypes: true }).flatMap((entry) => (entry.isDirectory() ? [entry.name] : []));
        } catch {
            return [];
        }
    },
    read(path) {
        try {
            return readFileSync(path, 'utf8');
        } catch {
            return null;
        }
    },
    copy(from, to) {
        const beside = `${to}.restoring`;
        try {
            copyFileSync(from, beside);
            renameSync(beside, to);
        } catch (error) {
            rmSync(beside, { force: true });
            throw error;
        }
    },
};

/** The start's look, logged where it put one back or could not; a carried file that is there is said nowhere. */
export function restoreAtStart(input: { executable: string; version: string | null; updaterCache: string; log: Logger; files?: RestoreFiles }): Restored {
    const restored = restoreCarried({ executable: input.executable, version: input.version, store: carriedStore(input.updaterCache), files: input.files ?? diskFiles });
    if (restored.act === 'restored' || restored.act === 'failed') input.log.info('carried_restored', { Restored: restored.act === 'restored' });
    return restored;
}
