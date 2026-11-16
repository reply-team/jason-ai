/**
 * An earlier version's demo's two folders, its data directory and its harness root (the person's paths with `-demo`
 * after them), are known by a marker file the desktop of 0.44 and earlier wrote into each when it made it, because their
 * names alone can belong to the person. The removal deletes such a folder only when it carries the marker; a folder
 * without it is never written into or removed. The name of the marker and the check for it live here, once.
 */

import { lstatSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

/** The marker file's name, in the folder's top level. */
export const demoMarkerName = '.jason-demo-folder';

/** The marker file's one line, exactly as the desktop that made the folder wrote it. */
export const demoMarkerText = 'This folder is Jason’s demo, made by Jason Desktop. Remove What Jason Placed removes it.\n';

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
