// The one impure part of the chat's permission model: where an existing path really is on this machine, and what is
// there.

import { lstatSync, realpathSync, statSync } from 'node:fs';

/**
 * The real path of an existing path, with links, junctions and Windows' short names followed, by the operating
 * system's own resolution; null when nothing is there or it cannot be read.
 */
export function realPathOf(path: string): string | null {
    try {
        return realpathSync.native(path);
    } catch {
        return null;
    }
}

/**
 * What is at a path, its last component not followed: null when nothing is there; `unresolved` when something is there
 * that cannot be followed to an existing file or folder, a link or a junction that leads nowhere, or an entry that cannot
 * be read; `present` otherwise.
 */
export function entryOf(path: string): 'present' | 'unresolved' | null {
    try {
        lstatSync(path);
    } catch (error) {
        const code = (error as NodeJS.ErrnoException).code;
        return code === 'ENOENT' || code === 'ENOTDIR' ? null : 'unresolved';
    }
    try {
        statSync(path);
        return 'present';
    } catch {
        return 'unresolved';
    }
}
