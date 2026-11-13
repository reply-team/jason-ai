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

/** The person's own installation, seen from a demo run: its data directory and its harness root. */
export interface PersonsOwn {
    data: string;
    harnessRoot: string;
}

/**
 * Pure: the person's own directories a run's chat refuses whole (`ChatScope.personsOwn`): in a demo run the person's data
 * directory and harness root, none in the person's own installation. It is read before `enterRun`, which takes away the
 * variable that may name the person's harness root; the root is read only for a demo run.
 */
export function personsOwnOf(run: RunsOn, personsHarnessRoot: () => string): PersonsOwn | undefined {
    return run.mode === 'demo' ? { data: run.person, harnessRoot: personsHarnessRoot() } : undefined;
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

export interface StartDeps {
    /** Makes the demo's directory and proves it usable (`openDirectory`); throws when it cannot. */
    open(directory: string): void;
    /**
     * The seam after the demo's directory is made and before the demo's runtime is ever started: what the demo
     * installation must hold before it runs, its settings first (`writeDemoSettings`). Throws when it cannot, which makes
     * the start the person's own as an unusable directory does. Never called for a Live start.
     */
    prepare(directory: string): void;
    /** Keeps the mode in the person's record; throws when it cannot. */
    keep(mode: Mode): void;
}

/**
 * The installation a start runs on (`runsOn`), with the demo's directory opened and prepared first: one that cannot be
 * made, used or prepared makes the start the person's own, sets the record back to Live (a record that cannot be set
 * back changes nothing of that) and says so.
 */
export function startOn(person: string, record: Mode, installerStart: boolean, deps: StartDeps): StartRun {
    const run = runsOn(person, record, installerStart);
    if (run.mode !== 'demo') return { ...run, unopened: null };
    try {
        deps.open(run.data);
        deps.prepare(run.data);
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

/** A process environment, as `process.env` is one. */
export type Environment = Record<string, string | undefined>;

/** What a demo run changes in its own process's environment, as the person's environment held it at start. */
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
 * Points this process at the run's installation and answers what the person's environment said. A demo run sets
 * `JASON_DATA_DIR` to the demo's directory and takes away every runtime settings variable the person set (a harness
 * root, a route, a role's switch), so the demo's runtime is configured by the demo's own settings file
 * (`writeDemoSettings`) and nothing else, as a terminal with `JASON_DATA_DIR` alone would be. Variables without a
 * double underscore stay. A Live run changes nothing.
 */
export function enterRun(env: Environment, run: RunsOn): PersonEnvironment {
    const settings: Record<string, string> = {};
    for (const [name, value] of Object.entries(env)) {
        if (isSettingsVariable(name) && value !== undefined) settings[name] = value;
    }
    const person = { dataDirectory: env.JASON_DATA_DIR, settings };
    if (run.mode === 'demo') {
        env.JASON_DATA_DIR = run.data;
        for (const name of Object.keys(settings)) delete env[name];
    }
    return person;
}

/**
 * Puts back what `enterRun` changed, exactly as the person's environment held it: `JASON_DATA_DIR` as theirs or none,
 * and every runtime settings variable by its name and value, with none of their own added since.
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
 * What the desk draws in place of each act a demo run does not offer, and the way back; null in Live, where the desk
 * draws the acts themselves. Remove What Jason Placed has a line only where the platform offers the act at all.
 */
export interface DemoMenu {
    updates: MenuLine;
    autostart: MenuLine;
    command: MenuLine;
    removal: MenuLine | null;
    back: MenuLine;
}

export function demoMenu(mode: Mode, removalOnPlatform: boolean): DemoMenu | null {
    if (mode !== 'demo') return null;
    const off = (item: 'updates' | 'autostart' | 'command' | 'removal'): MenuLine => ({ label: demoMenuLine(item), enabled: false });
    return { updates: off('updates'), autostart: off('autostart'), command: off('command'), removal: removalOnPlatform ? off('removal') : null, back: { label: backLabel, enabled: true } };
}

/**
 * What a demo run's menu says where the person's installation offers an update, the start at logon, the PATH command or
 * Remove What Jason Placed: none of them runs with the demo's directory in the process's environment.
 */
export function demoMenuLine(item: 'updates' | 'autostart' | 'command' | 'removal'): string {
    const what = { updates: 'Updates', autostart: 'Start the Runtime at Logon', command: 'The jason command', removal: 'Remove What Jason Placed' }[item];
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
