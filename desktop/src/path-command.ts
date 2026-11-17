/**
 * The PATH command: a `jason` the person's terminal finds, which runs the executable the application carries.
 *
 * - On Windows it is a one-line `jason.cmd` shim in `%LOCALAPPDATA%\Microsoft\WindowsApps\`, for Command Prompt and
 *   PowerShell, and beside it an extensionless `jason`, a `#!/bin/sh` wrapper for Git Bash, which never finds a `.cmd`
 *   by its bare name. That folder is the one user-writable folder every Windows 10 and 11 user PATH holds by default;
 *   the app-alias machinery there manages its own zero-byte alias files and ignores files it did not make. The two files
 *   are placed only when that folder is on the PATH, so no PATH entry is ever written, and they are removed by deleting
 *   them.
 * - On macOS and Linux it is a wrapper script in `~/.local/bin/`, and where a new terminal's PATH does not hold that
 *   folder, the same act puts it there as `install/install.sh` does, by the script's own rule (`path-profile.ts`): the
 *   script's marker and `export PATH="<folder>:$PATH"` in the account's login profile and, for zsh and bash, its
 *   interactive file, each only where the whole line is not there already. The confirmation names those files and the
 *   line; a profile that is a link, a folder, or cannot be written takes nothing, nor does any other, and the person is
 *   told the line to add by hand while the wrapper is still placed. A removal takes those lines back only where the
 *   folder then holds nothing but what Jason's installer or this application writes there (`heldBy`), the rule
 *   `jason uninstall` keeps; otherwise they stay, and the item says why.
 *
 * The files of a command are placed and removed together, only on the person's word, and read back together on every
 * look: a command of ours with any file that runs another executable, names another data directory than the desktop
 * runs with, or is missing, is shown as pointing elsewhere and never rewritten silently. A placed command that another
 * `jason` earlier on the PATH shadows is said to be. A command is never put in the place of a `jason` that is not ours: it is then
 * named `jason-desktop`, every file of it (`planCommand`). Nothing here starts a process.
 */

import { accessSync, chmodSync, constants, mkdirSync, readFileSync, realpathSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { basename, join, posix, win32 } from 'node:path';
import { gitBashWrapper, planCommand, shimTarget, windowsShim, wrapperScript, wrapperTarget } from './command-line.ts';
import type { Logger } from './log.ts';
import { addedWords, addLines, byHandWords, heldBy, markedIn, pathLines, profileMarker, takeLines, type PathLines, type ProfileAccount } from './path-profile.ts';
import { findOnPath } from './program-path.ts';

type Name = 'jason' | 'jason-desktop';

export type CommandStatus =
    | { state: 'unsupported'; executable: string }
    /**
     * Not placed: `name` and `files` are what placing it would write; `shadows` the other `jason` that made it
     * jason-desktop. `lines`, on macOS and Linux, is what the act writes into the shell profiles, and a removal takes back.
     */
    | { state: 'absent'; name: Name; files: readonly string[]; folderOnPath: boolean; shadows: string | null; executable: string; lines?: PathLines }
    /** Placed and read back; `shadowedBy` is what the PATH finds first for the name when that is not ours. */
    | { state: 'placed'; name: Name; files: readonly string[]; shadowedBy: string | null; executable: string; lines?: PathLines }
    /** A command of ours one of whose files differs: `why` says which and how. */
    | { state: 'elsewhere'; name: Name; files: readonly string[]; why: string; executable: string; lines?: PathLines }
    /** Both names are taken in the folder by files the desktop did not write. */
    | { state: 'taken'; files: readonly string[]; executable: string };

/**
 * Which `jason` the PATH finds by name (`PathCommand.onPath`), and the way out the Command Line submenu really offers:
 * `put` places `jason`, `name` places this window's Jason as `jason-desktop` (the other keeps `jason`), `point` points a
 * command of ours back at this window's Jason, each by `act`, the code of the menu's act that does it; `type` says
 * `jason-desktop` already finds this window's Jason. No way where the desktop offers none that would help. Codes only:
 * the page holds the words, the act's among them, letter for letter the menu's label (`actLabel`).
 */
export type JasonOnPath =
    | { found: 'this' }
    | { found: 'none'; way?: 'put'; act?: 'put_jason' }
    | { found: 'other'; way?: 'name' | 'point' | 'type'; act?: 'put_jason_desktop' | 'point_jason' | 'point_jason_desktop' };

/** The code of each act a way out names, by the name it concerns: the label the menu gives it is `actLabel`'s. */
export type ActCode = 'put_jason' | 'put_jason_desktop' | 'point_jason' | 'point_jason_desktop';

/** The menu's label of an act by its code, from `commandMenuOf`'s own words, so the two cannot be told apart. */
export function actLabel(code: ActCode): string {
    const name = code.endsWith('_desktop') ? 'jason-desktop' : 'jason';
    return code.startsWith('put') ? `Put ${name} on the PATH…` : `Point ${name} at This Jason…`;
}

/** The folder the command goes in, or null where none is offered. */
export function commandFolder(platform: NodeJS.Platform, env: Readonly<Record<string, string | undefined>>, home: string): string | null {
    if (platform === 'win32') return env['LOCALAPPDATA'] ? win32.join(env['LOCALAPPDATA'], 'Microsoft', 'WindowsApps') : null;
    if (platform === 'darwin' || platform === 'linux') return posix.join(home, '.local', 'bin');
    return null;
}

/**
 * Whether two paths name the same file as the platform reads them: normalized by its own path rules, and on Windows
 * without regard to case. The lookup joins as the platform does, while a command's files are joined where they were
 * made, so the two may differ in shape alone.
 */
export function sameFile(platform: NodeJS.Platform, a: string, b: string): boolean {
    const paths = platform === 'win32' ? win32 : posix;
    const shape = (file: string) => (platform === 'win32' ? paths.normalize(file).toLowerCase() : paths.normalize(file));
    return shape(a) === shape(b);
}

/**
 * Whether a file the PATH names is the bundled executable itself: the same path, or, through any link on either side
 * (a symbolic link, or on Windows a junction), the same final file. A path that cannot be resolved, a link that leads
 * nowhere among them, is not the executable; nothing here throws.
 */
export function sameFinalFile(platform: NodeJS.Platform, a: string, b: string): boolean {
    if (sameFile(platform, a, b)) return true;
    const final = (file: string): string | null => {
        try {
            return realpathSync.native(file);
        } catch {
            return null;
        }
    };
    const finalA = final(a);
    const finalB = final(b);
    return finalA !== null && finalB !== null && sameFile(platform, finalA, finalB);
}

/** Whether a file the PATH names is a program: a file and, off Windows, one that may be executed. */
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
    /**
     * macOS and Linux: the account whose shell profiles put the folder on a new terminal's PATH; injected, so a test
     * never names the person's own home. Absent, no profile is read or written.
     */
    account?: ProfileAccount;
    log: Logger;
}

/** What an act said beyond the command itself: the profiles it put the folder on the PATH in, or the line to add by hand. */
export interface Placed {
    status: CommandStatus;
    /** Null where no profile was to be written. */
    said: { added: boolean; words: string } | null;
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

    /** The files a command of this name is: the shim and the Git Bash wrapper on Windows, the wrapper on macOS and Linux. */
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
        return files.some((file) => sameFile(platform, file, found)) ? null : found;
    }

    status(): CommandStatus {
        const { executable, dataDirectory } = this.deps;
        if (this.deps.folder === null || (this.deps.platform !== 'win32' && this.deps.platform !== 'darwin' && this.deps.platform !== 'linux')) return { state: 'unsupported', executable };
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
            // A removal takes off the files of ours under this name, and judges the lines by what the folder holds then.
            const removing = looks.filter((look) => look.target !== null).map((look) => basename(look.file));
            if (missing) return { state: 'elsewhere', name, files, why: `${missing.file} is missing`, executable, ...this.linesOf(removing) };
            return { state: 'placed', name, files, shadowedBy: this.shadowing(name, files), executable, ...this.linesOf(removing) };
        }
        // No command of ours is placed, so whatever `jason` the PATH or the folder holds is another program's.
        const inFolder = this.looksOf('jason').find((look) => look.exists)?.file ?? null;
        const found = inFolder ?? findOnPath('jason', { platform: this.deps.platform, path: this.deps.path(), pathext: this.deps.pathext, isProgram: (file) => this.deps.isProgram(file) });
        const plan = planCommand(found, null);
        const looks = this.looksOf(plan.name);
        const files = looks.map((look) => look.file);
        if (looks.some((look) => look.exists)) return { state: 'taken', files, executable };
        const lines = this.linesOf([]);
        return { state: 'absent', name: plan.name, files, folderOnPath: this.folderOnPath(), shadows: plan.shadows ?? null, executable, ...lines };
    }

    /**
     * The PATH lines on macOS and Linux, where an account is given: what the act would write, and what a removal that
     * takes off the files named `removing` would take back: only where the folder then holds nothing but what Jason's
     * installer or this application writes there (`heldBy`). Nothing on Windows.
     */
    private linesOf(removing: readonly string[]): { lines?: PathLines } {
        const { account, folder, platform } = this.deps;
        if (platform === 'win32' || account === undefined || folder === null) return {};
        return { lines: pathLines(account, folder, heldBy(folder, removing)) };
    }

    /**
     * Places the command, or places it again over a command of ours that points elsewhere: only on the person's word. On
     * macOS and Linux the same act puts the folder on a new terminal's PATH where it is not (`PathLines`), a command
     * already placed included; a profile that cannot take the line leaves the command placed, and the words say so.
     */
    place(): Placed {
        const status = this.status();
        if (status.state === 'unsupported') throw new Error('The PATH command is offered on Windows, macOS and Linux only.');
        if (status.state === 'taken') throw new Error(`${status.files.join(' and ')} are there and are not the desktop’s; nothing was placed.`);
        if (status.state === 'placed') {
            const said = this.putOnPath(status.lines);
            return { status: said === null ? status : this.status(), said };
        }
        if (status.state === 'absent' && this.deps.platform === 'win32' && !status.folderOnPath) {
            throw new Error(`${this.deps.folder} is not on the PATH, so a command placed there would not be found; nothing was placed.`);
        }
        const looks = this.looksOf(status.name);
        const foreign = looks.find((look) => look.exists && look.target === null);
        if (foreign) throw new Error(`${foreign.file} is there and is not the desktop’s; nothing was placed.`);
        this.write(status.name);
        this.deps.log.info('path_command_set', { On: true, Name: status.name, Platform: this.deps.platform });
        const said = this.putOnPath(status.lines);
        return { status: this.status(), said };
    }

    /** Writes the PATH lines the look found wanting, and says what came of it in the script's own words. */
    private putOnPath(lines: PathLines | undefined): Placed['said'] {
        if (lines === undefined || lines.add.length === 0) return null;
        const written = addLines(lines);
        this.deps.log.info('path_lines_set', { On: true, Files: written.added.length, Refused: written.refused.length, Platform: this.deps.platform });
        return written.added.length > 0 ? { added: true, words: addedWords(lines, written.added) } : { added: false, words: byHandWords(lines, written.refused) };
    }

    /** The profiles that carry the script's marker and line for the folder; none on Windows, or with no account given. */
    marked(): string[] {
        const { account, folder, platform } = this.deps;
        if (platform === 'win32' || account === undefined || folder === null) return [];
        return markedIn(account.home, folder);
    }

    /**
     * Places the command again over this start's executable when every file of it is ours, names the data directory
     * the desktop runs with, and runs either this executable or one `stale` names, another version's copy, which at least
     * one does: the same act as place. A command not placed, or one that runs any other executable, is left as it is.
     */
    repoint(stale: (executable: string) => boolean): boolean {
        const status = this.status();
        if (status.state !== 'elsewhere') return false;
        const looks = this.looksOf(status.name);
        const ours = looks.every((look) => look.exists && look.target !== null && look.target.dataDirectory === this.deps.dataDirectory);
        const differing = looks.filter((look) => look.target?.executable !== look.runs).map((look) => look.target?.executable ?? '');
        if (!ours || differing.length === 0 || !differing.every((executable) => stale(executable))) return false;
        this.write(status.name);
        this.deps.log.info('path_command_repointed', { Name: status.name, Platform: this.deps.platform });
        return true;
    }

    /**
     * Which `jason` the PATH finds by name: this window's (the bundled executable, by its path or a link to it, or a file of a command of ours that runs
     * it with the data directory the desktop runs on), another, or none; with the way out the Command Line submenu really
     * offers, by its act's label, or `jason-desktop` to type where a command of ours by that name is what the PATH finds.
     * Undefined where it cannot be told (`unsupported`, `taken`). The PATH read is the one every program the desktop starts
     * is given, an editor's among them. A look writes nothing and starts no process.
     */
    onPath(): JasonOnPath | undefined {
        const status = this.status();
        if (status.state === 'unsupported' || status.state === 'taken') return undefined;
        const { platform } = this.deps;
        const lookup = (name: Name) => findOnPath(name, { platform, path: this.deps.path(), pathext: this.deps.pathext, isProgram: (file) => this.deps.isProgram(file) });
        const ours = (name: Name, file: string) =>
            sameFinalFile(platform, file, this.deps.executable) ||
            this.looksOf(name).some((look) => sameFile(platform, look.file, file) && look.target !== null && look.target.executable === look.runs && look.target.dataDirectory === this.deps.dataDirectory);
        // The act the menu carries for this very status, by its code; only one whose label is the menu's own.
        const label = commandMenuOf(status).acts.find((act) => act.act === 'place')?.label;
        const code = (verb: 'put' | 'point', name: Name): ActCode | undefined => {
            const act: ActCode = verb === 'put' ? (name === 'jason' ? 'put_jason' : 'put_jason_desktop') : name === 'jason' ? 'point_jason' : 'point_jason_desktop';
            return label === actLabel(act) ? act : undefined;
        };
        const found = lookup('jason');
        if (found === null) {
            const put = status.state === 'absent' && status.name === 'jason' && status.folderOnPath ? code('put', 'jason') : undefined;
            return put === 'put_jason' ? { found: 'none', way: 'put', act: put } : { found: 'none' };
        }
        if (ours('jason', found)) return { found: 'this' };
        const desktop = lookup('jason-desktop');
        if (desktop !== null && ours('jason-desktop', desktop)) return { found: 'other', way: 'type' };
        // The act helps only where the file it writes is the one the PATH would then find.
        const name = status.state === 'absent' && status.name === 'jason-desktop' && status.folderOnPath && desktop === null ? code('put', 'jason-desktop') : undefined;
        if (name === 'put_jason_desktop') return { found: 'other', way: 'name', act: name };
        const pointed = status.state === 'elsewhere' ? lookup(status.name) : null;
        const point = status.state === 'elsewhere' && pointed !== null && status.files.some((file) => sameFile(platform, file, pointed)) ? code('point', status.name) : undefined;
        if (point === 'point_jason' || point === 'point_jason_desktop') return { found: 'other', way: 'point', act: point };
        return { found: 'other' };
    }

    /** The executables the files of a command of ours run, under either name; a file that is not ours names none. */
    named(): string[] {
        const names: Name[] = ['jason', 'jason-desktop'];
        return names.flatMap((name) => this.looksOf(name).flatMap((look) => (look.target === null ? [] : [look.target.executable])));
    }

    /** Writes every file of the command by write and rename. */
    private write(name: Name): void {
        const files = this.filesOf(name);
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
    }

    /**
     * Removes every file of the command that is ours, whatever it points at; a file the desktop did not write stays. Then,
     * on macOS and Linux, the PATH lines come back out of the profiles that carry them, only where the folder then holds
     * nothing but what Jason writes there (`heldBy`); a profile that cannot be rewritten is named, with the line to take out by hand.
     */
    remove(): CommandStatus {
        const status = this.status();
        if (status.state === 'placed' || status.state === 'elsewhere') {
            for (const look of this.looksOf(status.name)) if (look.target !== null) rmSync(look.file, { force: true });
            this.deps.log.info('path_command_set', { On: false, Name: status.name, Platform: this.deps.platform });
        }
        const after = this.status();
        const lines = 'lines' in after ? after.lines : undefined;
        if (lines !== undefined && lines.take.length > 0) {
            const failed = takeLines(lines);
            this.deps.log.info('path_lines_set', { On: false, Files: lines.take.length - failed.length, Refused: failed.length, Platform: this.deps.platform });
            if (failed.length > 0) throw new Error(`${failed.join(' and ')} could not be rewritten: take the line ${lines.line} and the line ${profileMarker} above it out by hand.`);
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

/** Whether the act would write PATH lines: the folder is off a new terminal's PATH and a profile wants the line. */
const wanting = (lines: PathLines | undefined): lines is PathLines => lines !== undefined && lines.add.length > 0;

/** What the item says the act will do to the profiles, or null where it will do nothing to them. */
function linesLine(name: Name, lines: PathLines | undefined): string | null {
    if (!wanting(lines)) return null;
    const where = lines.add.join(' and ');
    return lines.refused.length === 0
        ? `${lines.folder} is not on a new terminal’s PATH yet; putting ${name} on the PATH adds it in ${where}.`
        : `${lines.folder} is not on a new terminal’s PATH, and ${lines.refused.join('; ')}: add ${lines.line} to ${where} by hand.`;
}

/** What the item says a removal leaves of the PATH lines, and why, or null where it leaves none it found. */
export function keptLine(lines: PathLines | undefined): string | null {
    if (lines === undefined || lines.kept.length === 0 || lines.held === null) return null;
    const why = lines.held === 'others' ? 'the folder still holds other programs, found through it' : 'the folder still holds another jason or jason-desktop, found through it';
    return `A removal leaves the line that puts ${lines.folder} on the PATH in ${lines.kept.join(' and ')}: ${why}.`;
}

/** The PATH command's part of the Jason menu: what it shows and the acts the state allows. */
export function commandMenuOf(status: CommandStatus): CommandMenu {
    const runs = `The jason executable: ${status.executable}`;
    switch (status.state) {
        case 'unsupported':
            return { lines: [runs, 'A jason command on the PATH is offered on Windows, macOS and Linux.'], acts: [] };
        case 'taken':
            return { lines: [runs, `${status.files.join(' and ')} are another program’s; nothing is placed.`], acts: [] };
        case 'placed': {
            const shells = isWindows(status.files) ? ' in Command Prompt, PowerShell and Git Bash' : '';
            const lines = [runs, `${status.name} on the PATH runs it${shells}: ${status.files.join(' and ')}`];
            if (status.shadowedBy !== null) lines.push(`Another ${status.name} earlier on the PATH is found first: ${status.shadowedBy}.`);
            const profiles = linesLine(status.name, status.lines);
            if (profiles !== null) lines.push(profiles);
            const kept = keptLine(status.lines);
            if (kept !== null) lines.push(kept);
            // A command an earlier version placed without the lines: the act that puts it on the PATH writes them alone.
            const put = wanting(status.lines) && status.lines.refused.length === 0 ? [{ label: actLabel(status.name === 'jason' ? 'put_jason' : 'put_jason_desktop'), act: 'place' as const }] : [];
            return { lines, acts: [...put, { label: `Remove ${status.name} from the PATH…`, act: 'remove' }] };
        }
        case 'elsewhere': {
            const profiles = linesLine(status.name, status.lines);
            return {
                lines: [runs, `${status.name} on the PATH points elsewhere: ${status.why}`, ...[profiles, keptLine(status.lines)].filter((line): line is string => line !== null)],
                acts: [
                    { label: actLabel(status.name === 'jason' ? 'point_jason' : 'point_jason_desktop'), act: 'place' },
                    { label: `Remove ${status.name} from the PATH…`, act: 'remove' },
                ],
            };
        }
        case 'absent': {
            const lines = [runs];
            if (status.shadows !== null) lines.push(`Another jason is on the PATH (${status.shadows}); this one would be ${status.name}.`);
            const windows = isWindows(status.files);
            if (windows && !status.folderOnPath) {
                const folder = (status.files[0] ?? '').replace(/[\\/][^\\/]*$/, '');
                lines.push(`${folder} is not on the PATH, so nothing is placed; run the executable by its full path.`);
            }
            const profiles = linesLine(status.name, status.lines);
            if (profiles !== null) lines.push(profiles);
            const placeable = status.folderOnPath || !windows;
            return { lines, acts: placeable ? [{ label: actLabel(status.name === 'jason' ? 'put_jason' : 'put_jason_desktop'), act: 'place' }] : [] };
        }
    }
}

/**
 * The confirmation an act asks for: the command by its name, and every file the act writes or removes, the shell
 * profiles and the two lines among them on macOS and Linux.
 */
export function commandQuestion(act: 'place' | 'remove', status: CommandStatus): { message: string; detail: string; button: string } {
    const name = 'name' in status ? status.name : 'jason';
    const files = 'files' in status ? status.files.join(' and ') : '';
    const lines = 'lines' in status ? status.lines : undefined;
    const block = (line: string) => `${profileMarker}\n${line}`;
    if (act === 'place') {
        const parts = status.state === 'placed' ? [] : [`This writes ${files}, which run ${status.executable}.`];
        if (wanting(lines)) {
            const where = lines.add.join(' and ');
            parts.push(
                lines.refused.length === 0
                    ? `To put ${lines.folder} on the PATH, it adds these two lines to ${where}:\n${block(lines.line)}`
                    : `${lines.refused.join('; ')}, so it changes no profile: add this line to ${where} by hand for a new terminal to find ${name}:\n${lines.line}`,
            );
        }
        return { message: `Put the ${name} command on the PATH?`, detail: parts.join('\n\n'), button: 'Place It' };
    }
    const takes = lines !== undefined && lines.take.length > 0 ? `\n\nIt also takes these two lines back out of ${lines.take.join(' and ')}:\n${block(lines.line)}` : '';
    const kept = keptLine(lines);
    return { message: `Remove the ${name} command from the PATH?`, detail: `This removes ${files}.${takes}${kept === null ? '' : `\n\n${kept}`}`, button: 'Remove It' };
}
