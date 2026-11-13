// The one impure part of the chat's permission model: where an existing path really is on this machine, and what is
// there.

import { lstatSync, readFileSync, realpathSync, statSync } from 'node:fs';

/** The largest file whose text an edit is judged by as it would read after the edit (`textOf`). */
export const textLimit = 4 * 1024 * 1024;

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
 * The text of an existing file as UTF-8, for judging an edit by the file as it would read after it; null when nothing is
 * there, it is no file, it cannot be read, or it is larger than `textLimit`.
 */
export function textOf(path: string): string | null {
    try {
        const stat = statSync(path);
        if (!stat.isFile() || stat.size > textLimit) return null;
        return readFileSync(path, 'utf8');
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
