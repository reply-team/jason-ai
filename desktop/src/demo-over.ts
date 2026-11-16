/**
 * What a start over of an earlier version's demo left on disk, and how the removal takes it off. Up to 0.44 a start
 * over took each marked folder of the demo out of its name by one rename, `<folder>.removing-<time>`, and removed the
 * copy as far as it could; a copy it could not remove was left beside the folder, still marked. Remove What Jason
 * Placed and the uninstall take such copies off with the folders themselves (`uninstall.ts`), each with its marker last.
 */

import { readdirSync, rmSync, rmdirSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { demoMarkerName } from './demo-folders.ts';

/** What an aside copy's name carries after the folder's own: `<folder>.removing-<time>`. */
export const asideMarker = '.removing-';

/**
 * Removes a marked folder with its marker last: every other entry first, then the marker, then the folder. A removal that
 * stops part-way (a file held open on Windows) leaves the marker, so the copy is still known as the demo's and a later
 * Remove What Jason Placed or uninstall can finish it. Throws at the first entry it cannot remove.
 */
export function removeMarkedLast(folder: string, remove: (path: string) => void = (path) => rmSync(path, { recursive: true, force: true }), marker: string = demoMarkerName): void {
    for (const name of readdirSync(folder)) {
        if (name !== marker) remove(join(folder, name));
    }
    remove(join(folder, marker));
    rmdirSync(folder);
}

/** The aside copies (`<folder>.removing-*`) that lie beside a folder, as paths; none when the parent cannot be read. */
export function asidesOf(folder: string): string[] {
    try {
        return readdirSync(dirname(folder))
            .filter((name) => name.startsWith(`${basename(folder)}${asideMarker}`))
            .map((name) => join(dirname(folder), name));
    } catch {
        return [];
    }
}
