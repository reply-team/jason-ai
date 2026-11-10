// The one impure part of the chat's permission model: where an existing path really is on this machine.

import { realpathSync } from 'node:fs';

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
