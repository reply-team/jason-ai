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
 *   the person's own and stays. And only where the folder then holds nothing but what Jason's installer or this
 *   application writes there, the rule `jason uninstall` keeps (`PathEntry.UnixInstallerFiles` in C#): `~/.local/bin`
 *   is shared, and where the line is what puts it on the PATH, the other programs in it are found through it too.
 *
 * Nothing here starts a process, and nothing reads the person's own home: every function is given the home it works in.
 */

import { accessSync, appendFileSync, chmodSync, closeSync, constants, lstatSync, openSync, readdirSync, readFileSync, renameSync, rmSync, statSync, truncateSync, writeFileSync } from 'node:fs';
import { dirname, join, sep } from 'node:path';

/** What `install.sh` ever leaves in its folder: the executable and its two notices (`PathEntry.UnixInstallerFiles`). */
export const installerFiles: readonly string[] = ['jason', 'jason-LICENSE', 'jason-THIRD-PARTY-NOTICES'];

/** What this application writes there: its command, under either name. */
export const desktopFiles: readonly string[] = ['jason', 'jason-desktop'];

/**
 * What keeps the PATH lines in place at a removal, from what the folder holds once the removal's own files are gone: a
 * `jason` or `jason-desktop` of any installation, which they still serve; anything Jason did not write, other programs
 * found through them; or nothing, and they come out. A folder that cannot be read keeps them.
 */
export function heldBy(folder: string, removing: readonly string[]): 'jason' | 'others' | null {
    let entries: string[];
    try {
        entries = readdirSync(folder);
    } catch (error) {
        return (error as NodeJS.ErrnoException).code === 'ENOENT' ? null : 'others';
    }
    // A hidden entry (a Finder's .DS_Store, say) and a temporary file of this application's are no program on the PATH.
    const left = entries.filter((entry) => !removing.includes(entry) && !entry.startsWith('.') && !entry.endsWith('.jason-desktop.tmp'));
    if (left.some((entry) => desktopFiles.includes(entry))) return 'jason';
    return left.every((entry) => installerFiles.includes(entry)) ? null : 'others';
}

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
    // Exactly what the append wrote, `\n<marker>\n<line>\n`, ending the file or a line of it: taking it out leaves every
    // other byte as it was, a last line the profile ended without a newline among them.
    const block = `\n${profileMarker}\n${asBytes(exportLine(folder))}`;
    let out = text;
    let from = 0;
    for (;;) {
        const at = out.indexOf(block, from);
        if (at < 0) break;
        const end = at + block.length;
        if (end === out.length) out = out.slice(0, at);
        else if (out[end] === '\n') out = out.slice(0, at) + out.slice(end + 1);
        else from = at + 1;
    }
    return out === text ? text : out;
}

/**
 * Whether the folder holds a character the shell reads inside the line's double quotes — `"`, `$`, a backtick or a
 * backslash — which would make the line code every login runs, or a line it cannot parse. The machine's own separator
 * is a separator, not a character of a name: on macOS and Linux it is `/`, so a backslash is always one of them there.
 * A line break as well: it runs nothing, but the whole-line search could never find the line again, so every act would
 * append another block.
 */
export function unsafeFolder(folder: string): boolean {
    return /["$`\n\r]/.test(folder) || (sep !== '\\' && folder.includes('\\'));
}

/** The line to add by hand: the script's own, or for a folder it cannot spell, the folder in single quotes. */
export function handLine(folder: string): string {
    return unsafeFolder(folder) ? `export PATH='${folder.replaceAll("'", "'\\''")}':"$PATH"` : exportLine(folder);
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
    /** The line the words give to add by hand: `line`, or the folder in single quotes where it holds a character `line` cannot carry. */
    byHand: string;
    /** The files the act writes the marker and the line into, in order; empty where a new terminal finds the folder already, or every file holds the line. */
    add: string[];
    /** Why a file of `add` cannot take them: then no profile is written, and the line is to be added by hand. */
    refused: string[];
    /** Whether the account's shell reads an interactive file: a new terminal finds the folder then, otherwise a new sign-in. */
    interactive: boolean;
    /** The files a removal takes the marker and the line back out of; empty while something keeps them (`held`). */
    take: string[];
    /** The files that carry them and that a removal leaves, because of what the folder still holds (`heldBy`). */
    kept: string[];
    held: 'jason' | 'others' | null;
}

/** The account the lines are written for: its home, its shell as `SHELL` names it, and the PATH a new terminal of it has. */
export interface ProfileAccount {
    home: string;
    shellNamed: string | null;
    /**
     * The PATH the account's login shell printed, before the desktop added the usual places to its own; null where the
     * shell gave none, and the folder is then taken as not on it: the line is harmless where the folder is there another
     * way, the whole-line search keeps it from doubling, and the confirmation names what is written.
     */
    terminalPath(): string | null;
}

/** Whether a colon-separated PATH names the folder, a trailing slash aside. */
export function namesFolder(path: string | null, folder: string): boolean {
    if (path === null) return false;
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

/** What the lines stand at for that folder; `held` is what keeps them at a removal (`heldBy`). A look writes nothing. */
export function pathLines(account: ProfileAccount, folder: string, held: 'jason' | 'others' | null): PathLines {
    const line = exportLine(folder);
    const interactive = interactiveProfile(account.home, account.shellNamed);
    const targets = [loginProfile(account.home, account.shellNamed), ...(interactive === null ? [] : [interactive])];
    const add = namesFolder(account.terminalPath(), folder) ? [] : targets.filter((file) => !carriesLine(bytesOf(file) ?? '', folder));
    const unsafe = add.length > 0 && unsafeFolder(folder) ? [`${folder} holds a character that line cannot carry: a double quote, a dollar sign, a backtick, a backslash or a line break`] : [];
    const refused = [...unsafe, ...add.flatMap((file) => refusal(file) ?? [])];
    const marked = markedIn(account.home, folder);
    return { folder, line, byHand: handLine(folder), add, refused, interactive: interactive !== null, take: held === null ? marked : [], kept: held === null ? [] : marked, held: marked.length > 0 ? held : null };
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
export function takeLines(lines: PathLines, write: (descriptor: number, data: Buffer) => void = (descriptor, data) => writeFileSync(descriptor, data)): string[] {
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
        // Made exclusively: whatever stands at that name already, a link among them, is never written through or removed.
        const temporary = `${file}.jason-desktop.tmp`;
        // What this act made is removed on every failure, a write cut short by a full disk among them.
        let made = false;
        let descriptor: number | null = null;
        try {
            descriptor = openSync(temporary, 'wx', mode);
            made = true;
            write(descriptor, Buffer.from(without, 'latin1'));
            closeSync(descriptor);
            descriptor = null;
            chmodSync(temporary, mode);
            renameSync(temporary, file);
        } catch {
            if (descriptor !== null) {
                try {
                    closeSync(descriptor);
                } catch {
                    // Closed or not, the file goes next.
                }
            }
            if (made) rmSync(temporary, { force: true });
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
    return `${refused.join('; ')}, so no profile was changed and ${lines.folder} is not on the PATH yet. Add this line to ${lines.add.join(' and ')} by hand: ${lines.byHand}`;
}
