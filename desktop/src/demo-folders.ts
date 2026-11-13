/**
 * The demo's two folders, the data directory and the harness root, are known by a marker file the desktop writes into
 * each when it makes it, because their names alone (the person's path with `-demo` after it) can already belong to the
 * person. A start uses a folder of either name only when it is absent (then it is made and marked) or carries the marker;
 * a folder without it is never adopted, written into or removed. The removal deletes a demo folder only when it carries
 * the marker. The name of the marker and the check for it live here, once, for the start, the removal and any later
 * act on the demo's folders.
 */

import { lstatSync, mkdirSync, readFileSync, rmdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/** The marker file's name, in the folder's top level. */
export const demoMarkerName = '.jason-demo-folder';

/** The marker file's one line: what the folder is and who made it. */
export const demoMarkerText = 'This folder is Jason’s demo, made by Jason Desktop. Remove What Jason Placed removes it.\n';

/** Why a folder of a demo's name is refused, as the start logs it (never the path). */
export const demoFolderForeign = 'a folder of the demo’s name exists that this application did not make';

/** Whether a folder carries the marker: a regular file of that name, holding exactly the marker's line. */
export function hasDemoMarker(folder: string): boolean {
    try {
        const file = join(folder, demoMarkerName);
        const found = lstatSync(file);
        return found.isFile() && !found.isSymbolicLink() && readFileSync(file, 'utf8') === demoMarkerText;
    } catch {
        return false;
    }
}

/**
 * Makes the folder and marks it when it is not there; uses it when it carries the marker; throws, writing nothing into
 * it, when it is there without the marker, or is not a folder. A folder this call made and could not mark is taken away
 * again, so a failed start leaves no unmarked folder of the demo's name behind.
 */
export function claimDemoFolder(folder: string): void {
    let there: ReturnType<typeof lstatSync> | null = null;
    try {
        there = lstatSync(folder);
    } catch (error) {
        if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
    }
    if (there !== null) {
        if (!there.isDirectory() || there.isSymbolicLink() || !hasDemoMarker(folder)) throw new Error(demoFolderForeign);
        return;
    }
    mkdirSync(folder, { recursive: true });
    try {
        writeFileSync(join(folder, demoMarkerName), demoMarkerText);
    } catch (error) {
        try {
            rmdirSync(folder);
        } catch {
            // Left as it is.
        }
        throw error;
    }
}

/**
 * Marks a folder this start found absent and `jason import` then made: the prepared demo's harness root (`demo-archive.ts`),
 * which an import placing into a folder that holds anything, the marker included, would first move aside, so it is marked
 * only once the import has ended. The archive's root was the demo's where it was made, so it usually carries the marker
 * already, and is then kept as it is. Throws, writing nothing, when the path is not a folder of its own (a link, a file).
 */
export function markPlacedFolder(folder: string): void {
    const there = lstatSync(folder);
    if (!there.isDirectory() || there.isSymbolicLink()) throw new Error(demoFolderForeign);
    if (hasDemoMarker(folder)) return;
    writeFileSync(join(folder, demoMarkerName), demoMarkerText);
}
