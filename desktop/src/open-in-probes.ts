import { accessSync, constants, statSync } from 'node:fs';
import { homedir } from 'node:os';
import { pathOf } from './programs.ts';
import { findOnPath } from './program-path.ts';
import type { Probes } from './open-in.ts';

function isProgram(file: string): boolean {
    try {
        if (!statSync(file).isFile()) return false;
        if (process.platform !== 'win32') accessSync(file, constants.X_OK);
        return true;
    } catch {
        return false;
    }
}

/** Whether a path is a folder: a link is followed, as the system's own folder-opening follows it. */
export function isFolder(path: string): boolean {
    try {
        return statSync(path).isDirectory();
    } catch {
        return false;
    }
}

/** This computer as "Open in" reads it: the PATH the desktop gave every program, and the disk. */
export function systemProbes(env: Readonly<Record<string, string | undefined>>): Probes {
    const lookup = { platform: process.platform, path: pathOf(env), pathext: env.PATHEXT ?? env.PathExt, isProgram };
    return {
        platform: process.platform,
        env,
        home: homedir(),
        onPath: (command) => findOnPath(command, lookup),
        exists: (path) => {
            try {
                statSync(path);
                return true;
            } catch {
                return false;
            }
        },
    };
}
