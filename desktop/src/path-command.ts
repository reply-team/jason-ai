/**
 * The PATH command: a `jason` the person's terminal finds, which runs the executable the application carries.
 *
 * - On Windows it is a one-line `jason.cmd` shim in `%LOCALAPPDATA%\Microsoft\WindowsApps\`. That folder is the one
 *   user-writable folder every Windows 10 and 11 user PATH holds by default; the app-alias machinery there manages its
 *   own zero-byte alias files and ignores a foreign `.cmd`. The shim is placed only when that folder is on the PATH,
 *   so no PATH entry is ever written, and it is removed by deleting the file.
 * - On macOS it is a wrapper script in `~/.local/bin/`.
 *
 * Both are placed and removed only on the person's word, read back on every look (a shim of ours that runs another
 * executable is shown as pointing elsewhere and never rewritten silently), and never put in the place of a `jason` that
 * is not ours: the command is then named `jason-desktop` (`planCommand`). Nothing here starts a process.
 */

import { accessSync, chmodSync, constants, mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { join, posix, win32 } from 'node:path';
import { planCommand, shimTarget, windowsShim, wrapperScript, wrapperTarget } from './command-line.ts';
import type { Logger } from './log.ts';
import { findOnPath } from './program-path.ts';

type Name = 'jason' | 'jason-desktop';

export type CommandStatus =
    | { state: 'unsupported'; executable: string }
    /** Not placed: `name` and `file` are what placing it would write; `shadows` the other `jason` that made it jason-desktop. */
    | { state: 'absent'; name: Name; file: string; folderOnPath: boolean; shadows: string | null; executable: string }
    | { state: 'placed'; name: Name; file: string; executable: string }
    /** A shim of ours that runs another executable: an older installation folder, or another installation. */
    | { state: 'elsewhere'; name: Name; file: string; target: string; executable: string }
    /** Both names are taken in the folder by files the desktop did not write. */
    | { state: 'taken'; file: string; executable: string };

/** The folder the command goes in, or null where none is offered. */
export function commandFolder(platform: NodeJS.Platform, env: Readonly<Record<string, string | undefined>>, home: string): string | null {
    if (platform === 'win32') return env['LOCALAPPDATA'] ? win32.join(env['LOCALAPPDATA'], 'Microsoft', 'WindowsApps') : null;
    if (platform === 'darwin') return posix.join(home, '.local', 'bin');
    return null;
}

/** Whether a file the PATH names is a program: a file and, on macOS, one that may be executed. */
export function isProgramFile(file: string, platform: NodeJS.Platform): boolean {
    try {
        if (!statSync(file).isFile()) return false;
        if (platform !== 'win32') accessSync(file, constants.X_OK);
        return true;
    } catch {
        return false;
    }
}

export interface PathCommandDeps {
    platform: NodeJS.Platform;
    /** `commandFolder`'s answer; injected, so a test never names the person's own folder. */
    folder: string | null;
    /** The bundled jason executable's full path. */
    executable: string;
    /** `JASON_DATA_DIR` when the desktop runs with one, otherwise null. */
    dataDirectory: string | null;
    /** The PATH as the desktop gives it to every program it starts. */
    path(): string;
    pathext?: string | undefined;
    isProgram(file: string): boolean;
    log: Logger;
}

export class PathCommand {
    private readonly deps: PathCommandDeps;

    constructor(deps: PathCommandDeps) {
        this.deps = deps;
    }

    private fileOf(name: Name): string {
        // The folder is a path of the machine the desktop runs on, so it is joined as that machine joins it.
        return join(this.deps.folder ?? '', this.deps.platform === 'win32' ? `${name}.cmd` : name);
    }

    private read(file: string): { exists: boolean; target: string | null } {
        let text: string;
        try {
            text = readFileSync(file, 'utf8');
        } catch {
            return { exists: false, target: null };
        }
        const target = this.deps.platform === 'win32' ? shimTarget(text) : wrapperTarget(text);
        return { exists: true, target: target?.executable ?? null };
    }

    private folderOnPath(): boolean {
        const { platform, folder } = this.deps;
        if (folder === null) return false;
        const windows = platform === 'win32';
        const normal = (directory: string) => {
            const bare = directory.trim().replace(/^"(.*)"$/, '$1').replace(/[\\/]+$/, '');
            return windows ? bare.toLowerCase() : bare;
        };
        return this.deps.path().split(windows ? ';' : ':').some((entry) => entry.trim() !== '' && normal(entry) === normal(folder));
    }

    status(): CommandStatus {
        const { executable } = this.deps;
        if (this.deps.folder === null || (this.deps.platform !== 'win32' && this.deps.platform !== 'darwin')) return { state: 'unsupported', executable };
        const names: Name[] = ['jason', 'jason-desktop'];
        for (const name of names) {
            const file = this.fileOf(name);
            const { target } = this.read(file);
            if (target === null) continue;
            return target === executable ? { state: 'placed', name, file, executable } : { state: 'elsewhere', name, file, target, executable };
        }
        // No command of ours is placed, so whatever `jason` the PATH or the folder holds is another program's.
        const jasonFile = this.fileOf('jason');
        const found = this.read(jasonFile).exists ? jasonFile : findOnPath('jason', { platform: this.deps.platform, path: this.deps.path(), pathext: this.deps.pathext, isProgram: (file) => this.deps.isProgram(file) });
        const plan = planCommand(found, null);
        const file = this.fileOf(plan.name);
        if (this.read(file).exists) return { state: 'taken', file, executable };
        return { state: 'absent', name: plan.name, file, folderOnPath: this.folderOnPath(), shadows: plan.shadows ?? null, executable };
    }

    /** Places the command, or places it again over a shim of ours that points elsewhere: only on the person's word. */
    place(): CommandStatus {
        const status = this.status();
        if (status.state === 'unsupported') throw new Error('The PATH command is offered on Windows and macOS only.');
        if (status.state === 'taken') throw new Error(`${status.file} is there and is not the desktop’s; nothing was placed.`);
        if (status.state === 'placed') return status;
        if (status.state === 'absent' && this.deps.platform === 'win32' && !status.folderOnPath) {
            throw new Error(`${this.deps.folder} is not on the PATH, so a command placed there would not be found; nothing was placed.`);
        }
        const text = this.deps.platform === 'win32' ? windowsShim(this.deps.executable, this.deps.dataDirectory) : wrapperScript(this.deps.executable, this.deps.dataDirectory);
        mkdirSync(this.deps.folder ?? '', { recursive: true });
        const temporary = `${status.file}.jason-desktop.tmp`;
        try {
            writeFileSync(temporary, text, { mode: 0o755 });
            if (this.deps.platform !== 'win32') chmodSync(temporary, 0o755);
            renameSync(temporary, status.file);
        } finally {
            rmSync(temporary, { force: true });
        }
        this.deps.log.info('path_command_set', { On: true, Name: status.name, Platform: this.deps.platform });
        return this.status();
    }

    /** Removes the command of ours, whatever it points at; a file the desktop did not write is never removed. */
    remove(): CommandStatus {
        const status = this.status();
        if (status.state === 'placed' || status.state === 'elsewhere') {
            rmSync(status.file, { force: true });
            this.deps.log.info('path_command_set', { On: false, Name: status.name, Platform: this.deps.platform });
        }
        return this.status();
    }
}

export interface CommandMenu {
    /** What the menu shows, unclickable: the bundled executable's full path always, then where the command stands. */
    lines: string[];
    acts: { label: string; act: 'place' | 'remove' }[];
}

/** The PATH command's part of the Jason menu: what it shows and the acts the state allows. */
export function commandMenuOf(status: CommandStatus): CommandMenu {
    const runs = `The jason executable: ${status.executable}`;
    switch (status.state) {
        case 'unsupported':
            return { lines: [runs, 'A jason command on the PATH is offered on Windows and macOS.'], acts: [] };
        case 'taken':
            return { lines: [runs, `${status.file} is another program’s; nothing is placed.`], acts: [] };
        case 'placed':
            return { lines: [runs, `${status.name} on the PATH runs it: ${status.file}`], acts: [{ label: `Remove ${status.name} from the PATH…`, act: 'remove' }] };
        case 'elsewhere':
            return {
                lines: [runs, `${status.name} on the PATH points elsewhere: ${status.file} runs ${status.target}`],
                acts: [
                    { label: `Point ${status.name} at This Jason…`, act: 'place' },
                    { label: `Remove ${status.name} from the PATH…`, act: 'remove' },
                ],
            };
        case 'absent': {
            const lines = [runs];
            if (status.shadows !== null) lines.push(`Another jason is on the PATH (${status.shadows}); this one would be ${status.name}.`);
            if (!status.folderOnPath) {
                const folder = status.file.replace(/[\\/][^\\/]*$/, '');
                lines.push(
                    status.file.endsWith('.cmd')
                        ? `${folder} is not on the PATH, so nothing is placed; run the executable by its full path.`
                        : `${folder} is not on the PATH yet; a terminal finds ${status.name} once it is.`,
                );
            }
            const placeable = status.folderOnPath || !status.file.endsWith('.cmd');
            return { lines, acts: placeable ? [{ label: `Put ${status.name} on the PATH…`, act: 'place' }] : [] };
        }
    }
}
