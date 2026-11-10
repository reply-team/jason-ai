/**
 * Pure: finding a program on the PATH the way the operating system would, and how to start what was found without a
 * shell. No `which`, no `where`: both platforms are read the same way, from the PATH itself.
 */

import { posix, win32 } from 'node:path';

const pathsOf = (platform: NodeJS.Platform) => (platform === 'win32' ? win32 : posix);

export interface PathLookup {
    platform: NodeJS.Platform;
    /** The PATH, as the desktop gives it to every program it starts. */
    path: string;
    /** Windows' PATHEXT; the system's default when unset. */
    pathext?: string | undefined;
    /** Whether a file is there and, on macOS and Linux, may be executed. */
    isProgram(file: string): boolean;
}

const defaultPathext = '.COM;.EXE;.BAT;.CMD';

/** The first file the PATH resolves the name to, or null. */
export function findOnPath(name: string, lookup: PathLookup): string | null {
    const windows = lookup.platform === 'win32';
    const separator = windows ? ';' : ':';
    const extensions = windows
        ? (lookup.pathext || defaultPathext).split(';').filter(Boolean).map((extension) => extension.toLowerCase())
        : [''];
    for (const raw of lookup.path.split(separator)) {
        const directory = raw.trim().replace(/^"(.*)"$/, '$1');
        if (directory === '') continue;
        for (const extension of extensions) {
            const candidate = pathsOf(lookup.platform).join(directory, name + extension);
            if (lookup.isProgram(candidate)) return candidate;
        }
    }
    return null;
}

/** How to start a program that was found: a file and the arguments before the program's own. */
export type Launch = { file: string; prefix: string[] } | { refused: string };

/**
 * Starting what the PATH found. An executable is started as it is. On Windows a program installed by npm is a batch
 * file, which Node starts only through the command interpreter; the desktop starts no shell, so the batch file is read instead and the
 * script it hands to Node is started with Node directly, as the batch file itself would. A batch file of any other shape
 * is refused, never run.
 */
export function launchOf(found: string, platform: NodeJS.Platform, readText: (file: string) => string | null, nodeOnPath: string | null, isFile: (file: string) => boolean): Launch {
    const { basename, dirname, extname, join } = pathsOf(platform);
    const extension = extname(found).toLowerCase();
    if (platform !== 'win32' || (extension !== '.cmd' && extension !== '.bat')) return { file: found, prefix: [] };

    const text = readText(found);
    const here = dirname(found);
    const script = text === null ? null : npmScript(text);
    if (script === null) {
        return { refused: `${basename(found)} is a batch file that is not one npm wrote, and Jason starts no batch file through a shell.` };
    }
    const besideIt = join(here, 'node.exe');
    const node = isFile(besideIt) ? besideIt : nodeOnPath;
    if (node === null) return { refused: `${basename(found)} needs Node.js, which is not on the PATH.` };
    return { file: node, prefix: [join(here, script)] };
}

/**
 * The script, relative to the batch file's folder, that a batch file npm wrote hands to Node: either the shape npm writes
 * for every installed package (`"%dp0%\node_modules\…\cli.js" %*`) or the one npm itself is installed with
 * (`SET "NPM_CLI_JS=%~dp0\node_modules\npm\bin\npm-cli.js"`). Null for anything else, and for a script outside the folder.
 */
export function npmScript(text: string): string | null {
    if (!/node(?:\.exe)?/i.test(text)) return null;
    const shim = /"%dp0%\\([^"%]+)"\s*%\*/.exec(text) ?? /SET\s+"NPM_CLI_JS=%~dp0\\([^"%]+)"/i.exec(text);
    const relative = shim?.[1];
    if (relative === undefined) return null;
    if (relative.split(/[\\/]/).some((part) => part === '..') || /^[a-z]:/i.test(relative)) return null;
    return relative;
}
