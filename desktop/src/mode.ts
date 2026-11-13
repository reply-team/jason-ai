/**
 * The mode is which installation the desktop runs on: Live, the person's own, or Demo, a second installation of sample
 * data beside it, whose data directory is the person's with `-demo` after it (`~/.jason` gives `~/.jason-demo`). The
 * record of it is one word in the PERSON'S installation, `<data>/desktop/mode`, also during a demo run, since the way
 * back writes it there. It is read once, at the top of the main module, before anything resolves a path; it is never
 * kept in the window's storage and never sent to a runtime. A record that is missing, cannot be read or holds anything
 * but one of the two words reads as the default. A switch writes the record and starts the desktop again.
 */

import { mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

export type Mode = 'live' | 'demo';

/** The mode of a record that says none: the one line that decides what a new installation opens in. */
export const defaultMode: Mode = 'live';

export const isMode = (value: unknown): value is Mode => value === 'live' || value === 'demo';

/** The desktop's sentence when the mode could not be kept: the desktop then stays as it is. */
export const modeUnkept = 'The mode could not be saved, so Jason stays as it is.';

/** Pure: the mode a record's text says, the default for anything but one of the two words. */
export function parseMode(text: string | null): Mode {
    const word = text?.trim();
    return isMode(word) ? word : defaultMode;
}

export function readMode(file: string): Mode {
    try {
        return parseMode(readFileSync(file, 'utf8'));
    } catch {
        return defaultMode;
    }
}

/** Writes the mode through a file beside it renamed over it, so a reader never meets half a word. */
export function writeMode(file: string, mode: Mode): void {
    mkdirSync(dirname(file), { recursive: true });
    const temporary = `${file}.${process.pid}.tmp`;
    try {
        writeFileSync(temporary, `${mode}\n`, { mode: 0o600 });
        renameSync(temporary, file);
    } catch (error) {
        rmSync(temporary, { force: true });
        throw error;
    }
}

/** Pure: the demo's data directory, a sibling of the person's: its path with `-demo` after it. */
export function demoDirectory(person: string): string {
    const trimmed = person.replace(/[\\/]+$/, '');
    return `${trimmed === '' ? person : trimmed}-demo`;
}

/** Which installation a start runs on. */
export interface RunsOn {
    /** The person's data directory, where the record is: the one `JASON_DATA_DIR` or the default names. */
    person: string;
    /** The data directory this run uses: the person's in Live, the demo's in Demo. */
    data: string;
    mode: Mode;
}

/**
 * Pure: the installation a start runs on, from the person's directory and what its record says. The installer's and
 * the uninstaller's starts ignore the record: they act on the person's installation alone.
 */
export function runsOn(person: string, record: Mode, installerStart: boolean): RunsOn {
    const mode = installerStart ? 'live' : record;
    return { person, data: mode === 'demo' ? demoDirectory(person) : person, mode };
}

/**
 * Pure: how the desktop starts itself again. A packaged start comes back with nothing after the program, so neither a
 * link nor the start-at-sign-in flag of this start is taken again; an unpackaged one names the application's folder, as
 * `electron .` did. `executable` is the program to start: on Linux an AppImage's file, never its mount.
 */
export function relaunchOptions(argv: readonly string[], packaged: boolean, executable: string): { execPath: string; args: string[] } {
    return { execPath: executable, args: packaged ? [] : argv.slice(1, 2) };
}

export interface ModeDeps {
    /** Keeps the mode in the person's record; throws when it cannot. */
    write(mode: Mode): void;
    /** Starts the desktop again, which then runs on the installation the record names. */
    relaunch(): void;
}

/**
 * The page asks for a mode: the record is written first and the desktop started again after, answering null. A write
 * that fails starts nothing and answers the refusal; a word that is neither mode is refused before anything is done.
 */
export function applyMode(choice: unknown, deps: ModeDeps): { refused: string } | null {
    if (!isMode(choice)) throw new Error('That is not a mode.');
    try {
        deps.write(choice);
    } catch {
        return { refused: modeUnkept };
    }
    deps.relaunch();
    return null;
}
