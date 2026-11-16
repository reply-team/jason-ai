/**
 * The mode is what the desktop shows: Live, the person's own installation, or Demo, the page the application carries
 * (`demo-window.ts`), which has nothing to do with any installation or runtime. The record of it is one word in the
 * person's installation, `<data>/desktop/mode`. It is read once, at the top of the main module, before anything resolves
 * a path; it is never kept in the window's storage and never sent to a runtime. A record that is missing, cannot be read
 * or holds anything but one of the two words reads as the default. A switch writes the record and starts the desktop
 * again. A new installation's first start, with no record, writes the record as the demo first (`keepFirstStart`), so
 * the person meets the product on the page and nothing of their own installation is made until they choose Live.
 *
 * Up to 0.44 Demo was a second installation of sample data beside the person's, `~/.jason-demo`; of it only the name of
 * its data directory stays here (`demoDirectory`), which Live reads to stop and to take off what such a demo left.
 */

import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

export type Mode = 'live' | 'demo';

/** The mode of a record that says none: the one line that decides what a new installation opens in. */
export const defaultMode: Mode = 'live';

export const isMode = (value: unknown): value is Mode => value === 'live' || value === 'demo';

/** The desktop's sentence when the mode could not be kept: the desktop then stays as it is. */
export const modeUnkept = 'The mode could not be saved, so Jason stays as it is.';

/**
 * Pure: whether this start writes the record as the demo: the first start of a new installation (`isNewInstallation`,
 * read of the person's directory before anything of the start writes there) that has no record yet. An installation
 * that existed opens where it did, a record is kept as it is, and the installer's and the uninstaller's starts never do.
 */
export function opensOnDemo(recorded: boolean, isNew: boolean, installerStart: boolean): boolean {
    return !recorded && isNew && !installerStart;
}

/**
 * Writes the demo's record for a new installation's first start (`opensOnDemo`), the one thing written in the person's
 * directory before they choose Live; answers whether it wrote it. A record that cannot be written leaves the start as
 * it was: the default.
 */
export function keepFirstStart(file: string, isNew: boolean, installerStart: boolean): boolean {
    if (!opensOnDemo(existsSync(file), isNew, installerStart)) return false;
    try {
        writeMode(file, 'demo');
        return true;
    } catch {
        return false;
    }
}

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

/**
 * Pure: an earlier version's demo's data directory, a sibling of the person's: its path with `-demo` after it. Live reads
 * it to stop a runtime such a demo left running (`demo-runtime.ts`) and to take its folders off (`uninstall.ts`).
 */
export function demoDirectory(person: string): string {
    const trimmed = person.replace(/[\\/]+$/, '');
    return `${trimmed === '' ? person : trimmed}-demo`;
}

/** A start's installation: the person's own directory in both modes. */
export interface StartRun {
    /** The person's data directory, where the record is: the one `JASON_DATA_DIR` or the default names. */
    person: string;
    /** The data directory this run uses: the person's, in both modes. */
    data: string;
    mode: Mode;
    /** Null: every start is on the installation the record named. The seam's answer carries it for the page (`ModeAnswer`). */
    unopened: null;
}

/**
 * What the page is told of the run: its mode, the sentence that says the demo the record named could not be opened,
 * null when the run is on the installation the record named, and whether the demo's sample is still being made. A
 * desktop since 0.45 answers null and false: the demo is a page that opens no installation and makes no sample.
 */
export interface ModeAnswer {
    mode: Mode;
    unopened: string | null;
    preparing: boolean;
}

/**
 * Pure: the installation a start runs on since the demo is a page the application carries: the person's own directory in
 * both modes. A Demo run makes, opens and prepares no directory and starts no runtime; it keeps only the desktop's own
 * records in the person's directory, the mode, Electron's profile and its log (`isNewInstallation` counts them as none
 * of an installation's). The installer's and the uninstaller's starts ignore the record.
 */
export function startRun(person: string, record: Mode, installerStart: boolean): StartRun {
    return { person, data: person, mode: installerStart ? 'live' : record, unopened: null };
}

/** A process environment, as `process.env` is one. */
export type Environment = Record<string, string | undefined>;

/** What the person's environment held at start, which a start of the desktop again puts back (`relaunchAsPerson`). */
export interface PersonEnvironment {
    /** `JASON_DATA_DIR` as the person gave it, or undefined when they gave none. */
    dataDirectory: string | undefined;
    /**
     * Every runtime settings variable the environment held, `JASON_<Section>__<Key>` in any spelling, by its exact name
     * with its exact value.
     */
    settings: Record<string, string>;
}

/** Whether a variable is one of the runtime's settings: `JASON_`, a section, then `__` and a key, in any case. */
export function isSettingsVariable(name: string): boolean {
    return /^JASON_[^_]+__./i.test(name);
}

/**
 * Pure: what the person's environment says, taken as it stands and changing nothing: `JASON_DATA_DIR` and every runtime
 * settings variable, which `relaunchAsPerson` puts back before the desktop starts itself again.
 */
export function personEnvironmentOf(env: Environment): PersonEnvironment {
    const settings: Record<string, string> = {};
    for (const [name, value] of Object.entries(env)) {
        if (isSettingsVariable(name) && value !== undefined) settings[name] = value;
    }
    return { dataDirectory: env.JASON_DATA_DIR, settings };
}

/**
 * Puts back the person's environment exactly as it held it: `JASON_DATA_DIR` as theirs or none, and every runtime
 * settings variable by its name and value, with none of their own added since.
 */
export function leaveRun(env: Environment, person: PersonEnvironment): void {
    if (person.dataDirectory === undefined) delete env.JASON_DATA_DIR;
    else env.JASON_DATA_DIR = person.dataDirectory;
    for (const name of Object.keys(env)) {
        if (isSettingsVariable(name) && !(name in person.settings)) delete env[name];
    }
    Object.assign(env, person.settings);
}

/**
 * The desktop starting itself again: the person's environment first, since the new start inherits this process's, so
 * it reads the record in the person's own directory whatever this run was; then the new start asked for, then this
 * one's quit.
 */
export function relaunchAsPerson(deps: { env: Environment; person: PersonEnvironment; relaunch(): void; quit(): void }): void {
    leaveRun(deps.env, deps.person);
    deps.relaunch();
    deps.quit();
}

/** What a run offers of what outlasts it: everything in Live; in Demo none of it, and the way back instead. */
export interface Offers {
    updates: boolean;
    autostart: boolean;
    command: boolean;
    removal: boolean;
    /** Back to Your Own Installation, in the tray and the application's menu. */
    back: boolean;
}

export function offersOf(mode: Mode): Offers {
    const live = mode === 'live';
    return { updates: live, autostart: live, command: live, removal: live, back: !live };
}

/** The label of the way back, in the tray and the application's menu of a demo run. */
export const backLabel = 'Back to Your Own Installation';

/** A menu line as the desk draws it: a label, and whether it can be chosen. */
export interface MenuLine {
    label: string;
    enabled: boolean;
}

/**
 * What a demo run's menu holds of its own: the way back, which does what Live on the switch does; null in Live. A demo
 * run offers none of the acts on an installation (the update, the start at logon, the PATH command, Remove What Jason
 * Placed: `offersOf`), and its menu has no line where they would be, nor any other line of the demo's.
 */
export interface DemoMenu {
    back: MenuLine;
}

export function demoMenu(mode: Mode): DemoMenu | null {
    return mode === 'demo' ? { back: { label: backLabel, enabled: true } } : null;
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
