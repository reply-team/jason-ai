/**
 * The mode is which installation the desktop runs on: Live, the person's own, or Demo, a second installation of sample
 * data beside it, whose data directory is the person's with `-demo` after it (`~/.jason` gives `~/.jason-demo`). The
 * record of it is one word in the PERSON'S installation, `<data>/desktop/mode`, also during a demo run, since the way
 * back writes it there. It is read once, at the top of the main module, before anything resolves a path; it is never
 * kept in the window's storage and never sent to a runtime. A record that is missing, cannot be read or holds anything
 * but one of the two words reads as the default. A switch writes the record and starts the desktop again.
 */

import { mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';

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

/** What the first screen says when this start meant the demo and opened the person's own installation instead. */
export const demoUnopened = 'The demo could not be opened, so Jason opened your own installation.';

/** A start's installation, and why it is not the demo the record named; null when it is what the record named. */
export interface StartRun extends RunsOn {
    unopened: string | null;
}

/**
 * What the page is told of the run: its mode, and the sentence that says the demo the record named could not be opened,
 * null when the run is on the installation the record named.
 */
export interface ModeAnswer {
    mode: Mode;
    unopened: string | null;
}

/** Makes the demo's directory when it is not there and proves it is a folder this account can write in; throws when not. */
export function openDirectory(directory: string): void {
    mkdirSync(directory, { recursive: true });
    if (!statSync(directory).isDirectory()) throw new Error('not a folder');
    const probe = join(directory, `.mode-probe-${process.pid}`);
    writeFileSync(probe, '');
    rmSync(probe, { force: true });
}

/**
 * The installation a start runs on (`runsOn`), with the demo's directory opened first: one that cannot be made or used
 * makes the start the person's own, sets the record back to Live (a record that cannot be set back changes nothing of
 * that) and says so.
 */
export function startOn(person: string, record: Mode, installerStart: boolean, deps: { open(directory: string): void; keep(mode: Mode): void }): StartRun {
    const run = runsOn(person, record, installerStart);
    if (run.mode !== 'demo') return { ...run, unopened: null };
    try {
        deps.open(run.data);
        return { ...run, unopened: null };
    } catch {
        try {
            deps.keep('live');
        } catch {
            // The record stays as it was; this start is the person's all the same.
        }
        return { person, data: person, mode: 'live', unopened: demoUnopened };
    }
}

/** What a demo run's menu says where the person's installation offers an update, the start at logon or the PATH command. */
export function demoMenuLine(item: 'updates' | 'autostart' | 'command'): string {
    const what = { updates: 'Updates', autostart: 'Start the Runtime at Logon', command: 'The jason command' }[item];
    return `${what}: not in the demo. Go back to your own installation for this.`;
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
