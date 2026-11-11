/**
 * Whether a file is inside a folder, and whether two paths name one file, decided on resolved paths. A path the system
 * or the command line reports is resolved already: the `exe` link of a process under `/proc`, the executable a start
 * at logon names, the image a runtime reports. A path the desktop holds is as it was given: the data directory from
 * `JASON_DATA_DIR` or the home folder, either of which may pass through a link. Comparing the two as written would say
 * that a file is outside a folder it is in, so every such decision resolves the folder, and the file where it can.
 *
 * Lexically inside the folder as given, or as resolved, is inside. Otherwise the file is resolved, or, when it is not
 * there, the nearest folder of it that is; where the folder or the file cannot be resolved at all the answer is that it
 * cannot be told, and a caller acts on that as it would on inside: it removes nothing, re-points nothing and disables
 * nothing it cannot tell is its own.
 */

import { realpathSync } from 'node:fs';
import { posix, win32 } from 'node:path';

export type Containment = 'inside' | 'outside' | 'unknown';

/** Resolves a path to the file it names, links followed; throws when there is none. */
export type Resolve = (path: string) => string;

/** The system's own resolver, which on Windows also expands a short name and gives a path's true case. */
export const resolveReal: Resolve = (path) => realpathSync.native(path);

function pathsOf(platform: NodeJS.Platform): typeof posix {
    return platform === 'win32' ? win32 : posix;
}

/** Whether `file` is inside `folder` as written, by the platform's path rules: on Windows without regard to case. */
export function lexicallyInside(platform: NodeJS.Platform, folder: string, file: string): boolean {
    const paths = pathsOf(platform);
    const relative = paths.relative(folder, file);
    return relative !== '' && !relative.startsWith('..') && !paths.isAbsolute(relative);
}

/**
 * The file a path names, resolved. One that is not there is named by its nearest folder that is, resolved, with the rest
 * of the path after it, since a link can only be in the part that is there; null when not even that resolves.
 */
export function resolvedFile(platform: NodeJS.Platform, file: string, resolve: Resolve = resolveReal): string | null {
    const paths = pathsOf(platform);
    const rest: string[] = [];
    let at = file;
    for (;;) {
        try {
            return paths.join(resolve(at), ...rest);
        } catch {
            // Not there: try the folder it would be in.
        }
        const up = paths.dirname(at);
        if (up === at) return null;
        rest.unshift(paths.basename(at));
        at = up;
    }
}

/** Whether `file` is inside `folder`, on resolved paths. */
export function containedIn(platform: NodeJS.Platform, folder: string, file: string, resolve: Resolve = resolveReal): Containment {
    if (lexicallyInside(platform, folder, file)) return 'inside';
    const real = resolvedFile(platform, folder, resolve);
    if (real === null) return 'unknown';
    if (lexicallyInside(platform, real, file)) return 'inside';
    const named = resolvedFile(platform, file, resolve);
    if (named === null) return 'unknown';
    return lexicallyInside(platform, real, named) ? 'inside' : 'outside';
}

/** Whether two paths name one file, on resolved paths: on Windows without regard to case. A path that does not resolve names no file. */
export function sameResolved(platform: NodeJS.Platform, a: string, b: string, resolve: Resolve = resolveReal): boolean {
    let first: string;
    let second: string;
    try {
        first = resolve(a);
        second = resolve(b);
    } catch {
        return false;
    }
    return platform === 'win32' ? first.toLowerCase() === second.toLowerCase() : first === second;
}
