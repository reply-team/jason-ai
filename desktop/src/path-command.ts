/**
 * The PATH command: a `jason` the person's terminal finds, which runs the executable the application carries.
 *
 * - On Windows it is a one-line `jason.cmd` shim in `%LOCALAPPDATA%\Microsoft\WindowsApps\`, for Command Prompt and
 *   PowerShell, and beside it an extensionless `jason`, a `#!/bin/sh` wrapper for Git Bash, which never finds a `.cmd`
 *   by its bare name. That folder is the one user-writable folder every Windows 10 and 11 user PATH holds by default;
 *   the app-alias machinery there manages its own zero-byte alias files and ignores files it did not make. The two files
 *   are placed only when that folder is on the PATH, so no PATH entry is ever written, and they are removed by deleting
 *   them.
 * - On macOS it is a wrapper script in `~/.local/bin/`.
 *
 * The files of a command are placed and removed together, only on the person's word, and read back together on every
 * look: a command of ours with any file that runs another executable, names another data directory than the desktop
 * runs with, or is missing, is shown as pointing elsewhere and never rewritten silently. A placed command that another
 * `jason` earlier on the PATH shadows is said to be. A command is never put in the place of a `jason` that is not ours: it is then
 * named `jason-desktop`, every file of it (`planCommand`). Nothing here starts a process.
 */

import { accessSync, chmodSync, constants, mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { join, posix, win32 } from 'node:path';
import { gitBashWrapper, planCommand, shimTarget, windowsShim, wrapperScript, wrapperTarget } from './command-line.ts';
import type { Logger } from './log.ts';
import { findOnPath } from './program-path.ts';

type Name = 'jason' | 'jason-desktop';

export type CommandStatus =
    | { state: 'unsupported'; executable: string }
    /** Not placed: `name` and `files` are what placing it would write; `shadows` the other `jason` that made it jason-desktop. */
    | { state: 'absent'; name: Name; files: readonly string[]; folderOnPath: boolean; shadows: string | null; executable: string }
    /** Placed and read back; `shadowedBy` is what the PATH finds first for the name when that is not ours. */
    | { state: 'placed'; name: Name; files: readonly string[]; shadowedBy: string | null; executable: string }
    /** A command of ours one of whose files differs: `why` says which and how. */
    | { state: 'elsewhere'; name: Name; files: readonly string[]; why: string; executable: string }
    /** Both names are taken in the folder by files the desktop did not write. */
    | { state: 'taken'; files: readonly string[]; executable: string };

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

/** One file of a command: where it goes, what it holds, and the executable a file of ours there must run. */
interface CommandFile {
    file: string;
    /** The file's text, made only when it is placed: a path a batch file would read is refused then. */
    text(): string;
    runs: string;
    read(text: string): { executable: string; dataDirectory: string | null } | null;
}

/** One file as a look finds it. */
interface Look extends CommandFile {
    exists: boolean;
    target: { executable: string; dataDirectory: string | null } | null;
}

export class PathCommand {
    private readonly deps: PathCommandDeps;

    constructor(deps: PathCommandDeps) {
        this.deps = deps;
    }

    /** The files a command of this name is: the shim and the Git Bash wrapper on Windows, the wrapper on macOS. */
    private filesOf(name: Name): CommandFile[] {
        const { executable, dataDirectory } = this.deps;
        // The folder is a path of the machine the desktop runs on, so it is joined as that machine joins it.
        const at = (file: string) => join(this.deps.folder ?? '', file);
        if (this.deps.platform !== 'win32') return [{ file: at(name), text: () => wrapperScript(executable, dataDirectory), runs: executable, read: wrapperTarget }];
        return [
            { file: at(`${name}.cmd`), text: () => windowsShim(executable, dataDirectory), runs: executable, read: shimTarget },
            { file: at(name), text: () => gitBashWrapper(executable, dataDirectory), runs: executable.replaceAll('\\', '/'), read: wrapperTarget },
        ];
    }

    private look(file: CommandFile): Look {
        let text: string;
        try {
            text = readFileSync(file.file, 'utf8');
        } catch {
            return { ...file, exists: false, target: null };
        }
        return { ...file, exists: true, target: file.read(text) };
    }

    /** The files of a name as they are; a look writes nothing. */
    private looksOf(name: Name): Look[] {
        return this.filesOf(name).map((file) => this.look(file));
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

    /** What the PATH finds first for the name, when that is not one of the command's own files. */
    private shadowing(name: Name, files: readonly string[]): string | null {
        const { platform } = this.deps;
        const found = findOnPath(name, { platform, path: this.deps.path(), pathext: this.deps.pathext, isProgram: (file) => this.deps.isProgram(file) });
        if (found === null) return null;
        const same = (a: string, b: string) => (platform === 'win32' ? a.toLowerCase() === b.toLowerCase() : a === b);
        return files.some((file) => same(file, found)) ? null : found;
    }

    status(): CommandStatus {
        const { executable, dataDirectory } = this.deps;
        if (this.deps.folder === null || (this.deps.platform !== 'win32' && this.deps.platform !== 'darwin')) return { state: 'unsupported', executable };
        const names: Name[] = ['jason', 'jason-desktop'];
        for (const name of names) {
            const looks = this.looksOf(name);
            if (!looks.some((look) => look.target !== null)) continue;
            const files = looks.map((look) => look.file);
            const foreign = looks.find((look) => look.exists && look.target === null);
            if (foreign) return { state: 'elsewhere', name, files, why: `${foreign.file} is another program’s`, executable };
            const differs = looks.find((look) => look.target !== null && look.target.executable !== look.runs);
            if (differs?.target) return { state: 'elsewhere', name, files, why: `${differs.file} runs ${differs.target.executable}`, executable };
            const otherData = looks.find((look) => look.target !== null && look.target.dataDirectory !== dataDirectory);
            if (otherData?.target) {
                const named = otherData.target.dataDirectory;
                return { state: 'elsewhere', name, files, why: named === null ? `${otherData.file} names no data directory` : `${otherData.file} names the data directory ${named}`, executable };
            }
            const missing = looks.find((look) => !look.exists);
            if (missing) return { state: 'elsewhere', name, files, why: `${missing.file} is missing`, executable };
            return { state: 'placed', name, files, shadowedBy: this.shadowing(name, files), executable };
        }
        // No command of ours is placed, so whatever `jason` the PATH or the folder holds is another program's.
        const inFolder = this.looksOf('jason').find((look) => look.exists)?.file ?? null;
        const found = inFolder ?? findOnPath('jason', { platform: this.deps.platform, path: this.deps.path(), pathext: this.deps.pathext, isProgram: (file) => this.deps.isProgram(file) });
        const plan = planCommand(found, null);
        const looks = this.looksOf(plan.name);
        const files = looks.map((look) => look.file);
        if (looks.some((look) => look.exists)) return { state: 'taken', files, executable };
        return { state: 'absent', name: plan.name, files, folderOnPath: this.folderOnPath(), shadows: plan.shadows ?? null, executable };
    }

    /** Places the command, or places it again over a command of ours that points elsewhere: only on the person's word. */
    place(): CommandStatus {
        const status = this.status();
        if (status.state === 'unsupported') throw new Error('The PATH command is offered on Windows and macOS only.');
        if (status.state === 'taken') throw new Error(`${status.files.join(' and ')} are there and are not the desktop’s; nothing was placed.`);
        if (status.state === 'placed') return status;
        if (status.state === 'absent' && this.deps.platform === 'win32' && !status.folderOnPath) {
            throw new Error(`${this.deps.folder} is not on the PATH, so a command placed there would not be found; nothing was placed.`);
        }
        const looks = this.looksOf(status.name);
        const foreign = looks.find((look) => look.exists && look.target === null);
        if (foreign) throw new Error(`${foreign.file} is there and is not the desktop’s; nothing was placed.`);
        const files = this.filesOf(status.name);
        const texts = files.map((file) => file.text());
        mkdirSync(this.deps.folder ?? '', { recursive: true });
        const temporaries = files.map((file) => `${file.file}.jason-desktop.tmp`);
        try {
            files.forEach((_file, index) => {
                const temporary = temporaries[index] ?? '';
                writeFileSync(temporary, texts[index] ?? '', { mode: 0o755 });
                if (this.deps.platform !== 'win32') chmodSync(temporary, 0o755);
            });
            files.forEach((file, index) => renameSync(temporaries[index] ?? '', file.file));
        } finally {
            for (const temporary of temporaries) rmSync(temporary, { force: true });
        }
        this.deps.log.info('path_command_set', { On: true, Name: status.name, Platform: this.deps.platform });
        return this.status();
    }

    /** Removes every file of the command that is ours, whatever it points at; a file the desktop did not write stays. */
    remove(): CommandStatus {
        const status = this.status();
        if (status.state === 'placed' || status.state === 'elsewhere') {
            for (const look of this.looksOf(status.name)) if (look.target !== null) rmSync(look.file, { force: true });
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

const isWindows = (files: readonly string[]) => files.some((file) => file.endsWith('.cmd'));

/** The PATH command's part of the Jason menu: what it shows and the acts the state allows. */
export function commandMenuOf(status: CommandStatus): CommandMenu {
    const runs = `The jason executable: ${status.executable}`;
    switch (status.state) {
        case 'unsupported':
            return { lines: [runs, 'A jason command on the PATH is offered on Windows and macOS.'], acts: [] };
        case 'taken':
            return { lines: [runs, `${status.files.join(' and ')} are another program’s; nothing is placed.`], acts: [] };
        case 'placed': {
            const shells = isWindows(status.files) ? ' in Command Prompt, PowerShell and Git Bash' : '';
            const lines = [runs, `${status.name} on the PATH runs it${shells}: ${status.files.join(' and ')}`];
            if (status.shadowedBy !== null) lines.push(`Another ${status.name} earlier on the PATH is found first: ${status.shadowedBy}.`);
            return { lines, acts: [{ label: `Remove ${status.name} from the PATH…`, act: 'remove' }] };
        }
        case 'elsewhere':
            return {
                lines: [runs, `${status.name} on the PATH points elsewhere: ${status.why}`],
                acts: [
                    { label: `Point ${status.name} at This Jason…`, act: 'place' },
                    { label: `Remove ${status.name} from the PATH…`, act: 'remove' },
                ],
            };
        case 'absent': {
            const lines = [runs];
            if (status.shadows !== null) lines.push(`Another jason is on the PATH (${status.shadows}); this one would be ${status.name}.`);
            const windows = isWindows(status.files);
            if (!status.folderOnPath) {
                const folder = (status.files[0] ?? '').replace(/[\\/][^\\/]*$/, '');
                lines.push(windows ? `${folder} is not on the PATH, so nothing is placed; run the executable by its full path.` : `${folder} is not on the PATH yet; a terminal finds ${status.name} once it is.`);
            }
            const placeable = status.folderOnPath || !windows;
            return { lines, acts: placeable ? [{ label: `Put ${status.name} on the PATH…`, act: 'place' }] : [] };
        }
    }
}

/** The confirmation an act asks for: the command by its name, and every file the act writes or removes. */
export function commandQuestion(act: 'place' | 'remove', status: CommandStatus): { message: string; detail: string; button: string } {
    const name = 'name' in status ? status.name : 'jason';
    const files = 'files' in status ? status.files.join(' and ') : '';
    if (act === 'place') return { message: `Put the ${name} command on the PATH?`, detail: `This writes ${files}, which run ${status.executable}.`, button: 'Place It' };
    return { message: `Remove the ${name} command from the PATH?`, detail: `This removes ${files}.`, button: 'Remove It' };
}
