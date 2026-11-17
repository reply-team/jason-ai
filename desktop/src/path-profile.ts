/**
 * The PATH lines of macOS and Linux: what puts the PATH command's folder on a new terminal's PATH, by the rule
 * `install/install.sh` writes its own by. `jason status`'s repair and `jason uninstall` spell the same rule in C#, and a
 * test runs the script's own functions against this copy, case by case.
 *
 * - The login profile by the account's shell. zsh reads `~/.zprofile`; bash reads the first of `~/.bash_profile` and
 *   `~/.bash_login` that is a file, and `~/.profile` only when neither is; any other shell, and an account that names
 *   none, reads `~/.profile`.
 * - The interactive file as well, for zsh and bash: `~/.zshrc`, `~/.bashrc`. A terminal a desktop opens is no login
 *   shell, and reads that file alone; another shell has no such file, and a new sign-in reads the login profile.
 * - Into each, the script's marker line and then `export PATH="<folder>:$PATH"`, appended as the script appends them, and
 *   only where that whole line is not already a line of the file, so a second act adds nothing.
 * - A file that is a link or a folder, or that cannot be written, takes nothing, and nor does any other: no profile is
 *   ever half-written, and the person is told which file and the line to add by hand.
 * - Taken back as the uninstall takes it back: the marker, the line under it and the blank line above them, out of the
 *   regular files among the six either rule may have written, every other byte as it was; a line no marker heads is
 *   the person's own and stays.
 *
 * Nothing here starts a process, and nothing reads the person's own home: every function is given the home it works in.
 */

import { accessSync, appendFileSync, chmodSync, constants, lstatSync, readFileSync, renameSync, rmSync, statSync, truncateSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';

/** The line `install.sh` writes above the one it appends. */
export const profileMarker = '# added by jason install';

/** The line itself, spelled exactly as `install.sh` spells it. */
export function exportLine(folder: string): string {
    return `export PATH="${folder}:$PATH"`;
}

/** What sh's `zsh|*\/zsh` and `bash|*\/bash` match: everything after the last slash of the shell named, or nothing. */
function shellName(named: string | null): string {
    return named === null ? '' : named.slice(named.lastIndexOf('/') + 1);
}

/** What sh's `[ -f ]` answers: a regular file, found through any link. */
function regularFile(file: string): boolean {
    try {
        return statSync(file).isFile();
    } catch {
        return false;
    }
}

/** The one file a login shell of the account reads (`install.sh`'s `login_profile`). */
export function loginProfile(home: string, named: string | null, isFile: (file: string) => boolean = regularFile): string {
    const name = shellName(named);
    if (/^zsh$/.test(name)) return join(home, '.zprofile');
    if (/^bash$/.test(name)) {
        for (const candidate of ['.bash_profile', '.bash_login']) if (isFile(join(home, candidate))) return join(home, candidate);
    }
    return join(home, '.profile');
}

/** The file every interactive shell of the account reads, or null for a shell with none (`install.sh`'s `interactive_profile`). */
export function interactiveProfile(home: string, named: string | null): string | null {
    const name = shellName(named);
    if (/^zsh$/.test(name)) return join(home, '.zshrc');
    if (/^bash$/.test(name)) return join(home, '.bashrc');
    return null;
}

/** Every file the script, the repair or this act may have written the lines into, whatever the shell is now. */
export function profileFiles(home: string): string[] {
    return ['.profile', '.bash_profile', '.bash_login', '.zprofile', '.bashrc', '.zshrc'].map((name) => join(home, name));
}

/** A string as the characters its UTF-8 bytes map to one for one, which is how a profile's bytes are read here. */
const asBytes = (text: string) => Buffer.from(text, 'utf8').toString('latin1');

/** A profile's bytes as text, one character a byte, or null where it cannot be read. */
function bytesOf(file: string): string | null {
    try {
        return readFileSync(file).toString('latin1');
    } catch {
        return null;
    }
}

/** Whether a profile's text holds the line as a whole line of its own, as `grep -qxF` looks for it. */
export function carriesLine(text: string, folder: string): boolean {
    return text.split('\n').includes(asBytes(exportLine(folder)));
}

/**
 * A profile's text with every block the script appends for that folder taken out — the marker, the line under it, and
 * the blank line above them where there is one — or the same string when it carries none. Every other byte stays.
 */
export function withoutLines(text: string, folder: string): string {
    const line = asBytes(exportLine(folder));
    if (!text.includes(line)) return text;
    const lines = text.split('\n');
    let removed = false;
    for (let index = lines.length - 1; index > 0; index--) {
        if ((lines[index] ?? '').trimEnd() !== line || (lines[index - 1] ?? '').trim() !== profileMarker) continue;
        let first = index - 1;
        if (first > 0 && (lines[first - 1] ?? '').trim() === '') first--;
        lines.splice(first, index - first + 1);
        removed = true;
        index = first;
    }
    return removed ? lines.join('\n') : text;
}

/** Why a profile file cannot take the lines, or null where it can. */
function refusal(file: string): string | null {
    let kind;
    try {
        kind = lstatSync(file);
    } catch {
        kind = null;
    }
    if (kind?.isSymbolicLink()) return `${file} is a link`;
    if (kind?.isDirectory()) return `${file} is a folder`;
    if (kind !== null && !kind.isFile()) return `${file} is not a file`;
    try {
        accessSync(kind === null ? dirname(file) : file, constants.W_OK);
    } catch {
        return `${file} cannot be written`;
    }
    return null;
}

/** What putting the folder on a new terminal's PATH would write, and what removing it would take back. */
export interface PathLines {
    folder: string;
    /** `export PATH="<folder>:$PATH"`, which goes under the marker. */
    line: string;
    /** The files the act writes the marker and the line into, in order; empty where a new terminal finds the folder already, or every file holds the line. */
    add: string[];
    /** Why a file of `add` cannot take them: then no profile is written, and the line is to be added by hand. */
    refused: string[];
    /** Whether the account's shell reads an interactive file: a new terminal finds the folder then, otherwise a new sign-in. */
    interactive: boolean;
    /** The files a removal takes the marker and the line back out of; empty while the folder holds a jason it still serves. */
    take: string[];
}

/** The account the lines are written for: its home, its shell as `SHELL` names it, and the PATH a new terminal of it has. */
export interface ProfileAccount {
    home: string;
    shellNamed: string | null;
    /** The PATH the account's login shell printed, before the desktop added the usual places to its own. */
    terminalPath(): string;
}

/** Whether a colon-separated PATH names the folder, a trailing slash aside. */
export function namesFolder(path: string, folder: string): boolean {
    const bare = (entry: string) => entry.trim().replace(/\/+$/, '');
    return path.split(':').some((entry) => entry.trim() !== '' && bare(entry) === bare(folder));
}

/** The regular files among the six that carry the script's block for the folder: a link is never written through. */
export function markedIn(home: string, folder: string): string[] {
    return profileFiles(home).filter((file) => {
        try {
            if (!lstatSync(file).isFile()) return false;
        } catch {
            return false;
        }
        const text = bytesOf(file);
        return text !== null && withoutLines(text, folder) !== text;
    });
}

/** What the lines stand at for that folder; `serves` says the folder still holds a jason after a removal. A look writes nothing. */
export function pathLines(account: ProfileAccount, folder: string, serves: boolean): PathLines {
    const line = exportLine(folder);
    const interactive = interactiveProfile(account.home, account.shellNamed);
    const targets = [loginProfile(account.home, account.shellNamed), ...(interactive === null ? [] : [interactive])];
    const add = namesFolder(account.terminalPath(), folder) ? [] : targets.filter((file) => !carriesLine(bytesOf(file) ?? '', folder));
    const refused = add.flatMap((file) => refusal(file) ?? []);
    return { folder, line, add, refused, interactive: interactive !== null, take: serves ? [] : markedIn(account.home, folder) };
}

/** What an act did to the profiles: the files it added the lines to, or why it added them to none. */
export interface LinesWritten {
    added: string[];
    refused: string[];
}

/**
 * Appends the marker and the line to every file of `add`, as the script appends them; with any file refused, to none.
 * An append that fails puts every file it already wrote back as it was, so nothing is half-written.
 */
export function addLines(lines: PathLines): LinesWritten {
    if (lines.add.length === 0) return { added: [], refused: [] };
    if (lines.refused.length > 0) return { added: [], refused: lines.refused };
    const block = Buffer.from(`\n${profileMarker}\n${lines.line}\n`, 'utf8');
    const before = new Map<string, number | null>();
    for (const file of lines.add) {
        try {
            before.set(file, lstatSync(file).size);
        } catch {
            before.set(file, null);
        }
        try {
            appendFileSync(file, block);
        } catch (error) {
            for (const [written, size] of before) {
                try {
                    if (size === null) rmSync(written, { force: true });
                    else truncateSync(written, size);
                } catch {
                    // Left as the failed append left it: the words say this file.
                }
            }
            const code = (error as NodeJS.ErrnoException).code;
            return { added: [], refused: [`${file} could not be written${code ? ` (${code})` : ''}`] };
        }
    }
    return { added: [...lines.add], refused: [] };
}

/**
 * Takes the marker and the line back out of every file of `take`, by a write beside it and one rename, keeping the file's
 * mode; a file that is no longer a regular file, or no longer carries them, is left. Returns the files it could not
 * rewrite.
 */
export function takeLines(lines: PathLines): string[] {
    const failed: string[] = [];
    for (const file of lines.take) {
        let mode: number;
        try {
            const kind = lstatSync(file);
            if (!kind.isFile()) continue;
            mode = kind.mode & 0o7777;
        } catch {
            continue;
        }
        const text = bytesOf(file);
        if (text === null) {
            failed.push(file);
            continue;
        }
        const without = withoutLines(text, lines.folder);
        if (without === text) continue;
        const temporary = `${file}.jason-desktop.tmp`;
        try {
            writeFileSync(temporary, Buffer.from(without, 'latin1'), { mode });
            chmodSync(temporary, mode);
            renameSync(temporary, file);
        } catch {
            rmSync(temporary, { force: true });
            failed.push(file);
        }
    }
    return failed;
}

/** `install.sh`'s own words after it adds the lines: a new terminal where the shell has an interactive file, a new sign-in otherwise. */
export function addedWords(lines: PathLines, added: readonly string[]): string {
    const where = added.join(' and ');
    return lines.interactive
        ? `Added ${lines.folder} to the PATH in ${where}; open a new terminal, or run: ${lines.line}`
        : `Added ${lines.folder} to the PATH in ${where}; sign out and back in for a new terminal to find it, or run: ${lines.line}`;
}

/** The words where no profile took the lines: which file, why, and the line to add by hand. */
export function byHandWords(lines: PathLines, refused: readonly string[]): string {
    return `${refused.join('; ')}, so no profile was changed and ${lines.folder} is not on the PATH yet. Add this line to ${lines.add.join(' and ')} by hand: ${lines.line}`;
}
