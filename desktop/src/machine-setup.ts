/**
 * The machine set-up at a start, on macOS and Windows: the carried `jason install` (`docs/release-and-update.md` §9), which
 * puts `jason` where a new terminal finds it, deploys Jason's skills for every agent host found and retires an older
 * command-line install. The desktop runs it at a start where it has not yet run for the version the application carries,
 * before the window's Demo or Live run goes on, and again on the person's word from the Command Line item, which shows what
 * its report said. On Linux none of this is offered: the Command Line item there is the PATH command (`path-command.ts`).
 *
 * - **Nothing under the data directory but a record of the desktop's own.** It is run with no `--roles`, so the routine
 *   writes nothing there; where it ran is `<data>/desktop/machine-setup.json`, which `isNewInstallation` counts among the
 *   desktop's own records, so a new installation's first start still opens on the Demo. The record is written only when
 *   the routine reported that it completed, so a run that did not get there runs again at the next start.
 * - **Never between the person and the window.** A start runs it once, with a time limit; a failure or a run that does
 *   not end is said in the item, which offers to put it back, and nothing is tried again in a loop.
 * - **The PATH it is told** is the login shell's own, as that shell printed it, or the empty value where it gave none, on
 *   macOS; never the desktop's, which adds places to it. Windows reads none.
 * - **Its report is shown as facts**: where a new terminal finds `jason` and what a terminal already open types, the
 *   skills by the programs that read them, and the routine's own sentences of what it left and why, and of what it did
 *   not do. Never its JSON.
 */

import { accessSync, constants, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, posix } from 'node:path';
import type { Cli, CliResult } from './cli.ts';
import { wrapperTarget } from './command-line.ts';
import type { Logger } from './log.ts';
import { sameFinalFile, type JasonOnPath } from './path-command.ts';
import { findOnPath } from './program-path.ts';

/** How long a run is given: a retired install's runtime is drained for up to a minute, then stopped. */
export const setupLimitMs = 300_000;

/** Whether the Command Line item is the routine's status: on macOS and Windows; Linux keeps its PATH command. */
export function commandIsSetup(platform: NodeJS.Platform): boolean {
    return platform === 'darwin' || platform === 'win32';
}

/** Whether a start runs the routine: an installed build on macOS or Windows; a development build's jason is the checkout's. */
export function setupOffered(platform: NodeJS.Platform, packaged: boolean): boolean {
    return packaged && commandIsSetup(platform);
}

/** The routine's arguments: on macOS the login shell's own PATH, or the empty value where it gave none; on Windows none. */
export function setupArguments(platform: NodeJS.Platform, terminalPath: string | null): string[] {
    return platform === 'darwin' ? ['install', '--terminal-path', terminalPath ?? ''] : ['install'];
}

/** The routine's report, as the desktop reads it out of the document it prints. */
export interface SetupReport {
    version: string;
    executable: string;
    /** The terminal command; null where it was not placed. */
    command: { runs: string; openShell: string; changed: boolean; profiles: string[]; takenBack: string[] } | null;
    /** One entry per skills root of an agent host found. */
    skills: { root: string; hosts: string[]; written: number; unchanged: number; leftAlone: string[] }[];
    done: string[];
    left: string[];
    problems: string[];
    completed: boolean;
}

const isText = (value: unknown): value is string => typeof value === 'string';
const texts = (value: unknown): string[] | null => (Array.isArray(value) && value.every(isText) ? value : null);
const isObject = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);

/** Pure: the report a document says, or null for anything that is not one. */
export function reportOf(raw: unknown): SetupReport | null {
    if (!isObject(raw)) return null;
    const { version, executable, command, skills, done, left, problems, completed } = raw;
    const doneSaid = texts(done);
    const leftSaid = texts(left);
    const problemsSaid = texts(problems);
    if (!isText(version) || !isText(executable) || typeof completed !== 'boolean' || !Array.isArray(skills)) return null;
    if (doneSaid === null || leftSaid === null || problemsSaid === null) return null;
    let placed: SetupReport['command'] = null;
    if (command !== null) {
        if (!isObject(command)) return null;
        const profiles = texts(command.profiles);
        const takenBack = texts(command.taken_back);
        if (!isText(command.runs) || !isText(command.open_shell) || typeof command.changed !== 'boolean' || profiles === null || takenBack === null) return null;
        placed = { runs: command.runs, openShell: command.open_shell, changed: command.changed, profiles, takenBack };
    }
    const roots: SetupReport['skills'] = [];
    for (const entry of skills) {
        if (!isObject(entry)) return null;
        const hosts = texts(entry.hosts);
        const leftAlone = texts(entry.left_alone);
        if (!isText(entry.root) || hosts === null || leftAlone === null || typeof entry.written !== 'number' || typeof entry.unchanged !== 'number') return null;
        roots.push({ root: entry.root, hosts, written: entry.written, unchanged: entry.unchanged, leftAlone });
    }
    return { version, executable, command: placed, skills: roots, done: doneSaid, left: leftSaid, problems: problemsSaid, completed };
}

/** What a run of the routine answered: its report, or why there is none, in its own sentence where it said one. */
export type SetupAnswer = { report: SetupReport } | { refused: string };

/** Pure: a run's answer from what the CLI gave back. */
export function answerOf(result: CliResult): SetupAnswer {
    if (result.exit === -1) return { refused: `jason install could not be started, or did not end within ${setupLimitMs / 60_000} minutes.` };
    let parsed: unknown = null;
    try {
        parsed = JSON.parse(result.stdout);
    } catch {
        parsed = null;
    }
    const error = isObject(parsed) && isObject(parsed.error) ? parsed.error : null;
    if (error !== null && isText(error.message) && error.message !== '') return { refused: error.message };
    const report = reportOf(parsed);
    return report === null ? { refused: `jason install exited ${result.exit} and printed no report.` } : { report };
}

/** Where the routine last ran, by the desktop's own record: the version and the report of a run that completed. */
export interface SetupRecord {
    ranFor: string | null;
    report: SetupReport | null;
}

const noRecord: SetupRecord = { ranFor: null, report: null };

/** The record, or none where it is missing, cannot be read or is not one. */
export function readSetupRecord(file: string): SetupRecord {
    let raw: unknown;
    try {
        raw = JSON.parse(readFileSync(file, 'utf8'));
    } catch {
        return noRecord;
    }
    if (!isObject(raw) || !isText(raw.ran_for)) return noRecord;
    const report = reportOf(raw.report);
    return report === null ? noRecord : { ranFor: raw.ran_for, report };
}

/** Writes the record whole, through a file beside it renamed over it, the report as the routine printed it. */
export function writeSetupRecord(file: string, version: string, printed: unknown): void {
    mkdirSync(dirname(file), { recursive: true });
    const temporary = `${file}.${process.pid}.tmp`;
    try {
        writeFileSync(temporary, `${JSON.stringify({ ran_for: version, report: printed }, null, 2)}\n`, { mode: 0o600 });
        renameSync(temporary, file);
    } catch (error) {
        rmSync(temporary, { force: true });
        throw error;
    }
}

/** Where the set-up stands, as the Command Line item shows it. */
export type SetupState =
    /** A development build, or a system where the routine is not run. */
    | { state: 'unoffered' }
    /** Offered, and nothing of a run of this version to show. */
    | { state: 'idle' }
    | { state: 'running' }
    | { state: 'reported'; report: SetupReport }
    | { state: 'refused'; why: string };

export interface MachineSetupDeps {
    /** The carried jason. */
    cli: Cli;
    /** Its path, which the item names. */
    executable: string;
    /** The version the application carries; null where the bundle names none, and nothing is run. */
    version: string | null;
    platform: NodeJS.Platform;
    /** `setupOffered`'s answer. */
    offered: boolean;
    /** `<data>/desktop/machine-setup.json`. */
    record: string;
    /** The login shell's own PATH, or null where it printed none. */
    terminalPath(): Promise<string | null>;
    log: Logger;
}

export class MachineSetup {
    readonly #deps: MachineSetupDeps;
    #state: SetupState;
    #running: Promise<SetupState> | null = null;
    #atStart: Promise<void> | null = null;
    #changed: (() => void)[] = [];

    constructor(deps: MachineSetupDeps) {
        this.#deps = deps;
        if (!deps.offered) {
            this.#state = { state: 'unoffered' };
        } else {
            const kept = readSetupRecord(deps.record);
            this.#state = kept.report !== null && kept.ranFor === deps.version ? { state: 'reported', report: kept.report } : { state: 'idle' };
        }
    }

    get state(): SetupState {
        return this.#state;
    }

    get executable(): string {
        return this.#deps.executable;
    }

    get platform(): NodeJS.Platform {
        return this.#deps.platform;
    }

    /** Told whenever the state changes, the menu to draw again. */
    onChanged(changed: () => void): void {
        this.#changed.push(changed);
    }

    /**
     * The start's run: once a start, where it is offered and the record names another version than the one carried, or
     * none. Anything else runs nothing.
     */
    atStart(): Promise<void> {
        this.#atStart ??= (async () => {
            const { offered, version, record } = this.#deps;
            if (!offered || version === null || readSetupRecord(record).ranFor === version) return;
            await this.#run('start');
        })();
        return this.#atStart;
    }

    /** Resolves once the start's run has ended, at once where the start runs none. */
    async settled(): Promise<void> {
        await (this.#atStart ?? Promise.resolve());
    }

    /** Put back, on the person's word: the routine run again, unless it is running already. */
    putBack(): Promise<SetupState> {
        if (!this.#deps.offered || this.#deps.version === null) return Promise.resolve(this.#state);
        return this.#run('put_back');
    }

    #run(why: 'start' | 'put_back'): Promise<SetupState> {
        if (this.#running !== null) return this.#running;
        this.#set({ state: 'running' });
        this.#running = (async () => {
            const { cli, platform, log, record } = this.#deps;
            const version = this.#deps.version ?? '';
            let terminalPath: string | null = null;
            if (platform === 'darwin') terminalPath = await this.#deps.terminalPath().catch(() => null);
            const result = await cli.run(setupArguments(platform, terminalPath), { timeoutMs: setupLimitMs });
            const answer = answerOf(result);
            const completed = 'report' in answer && answer.report.completed && result.exit === 0;
            log.info('machine_setup_ran', { Why: why, Exit: result.exit, Completed: completed });
            if (completed) {
                try {
                    writeSetupRecord(record, version, JSON.parse(result.stdout));
                } catch {
                    log.warn('machine_setup_unrecorded', { Version: version });
                }
            }
            return 'report' in answer ? ({ state: 'reported', report: answer.report } as const) : ({ state: 'refused', why: answer.refused } as const);
        })()
            .catch((error: unknown) => ({ state: 'refused', why: error instanceof Error ? error.message : String(error) }) as const)
            .then((next) => {
                this.#running = null;
                this.#set(next);
                return next;
            });
        return this.#running;
    }

    #set(next: SetupState): void {
        this.#state = next;
        for (const changed of this.#changed) changed();
    }
}

/** What the starting page says while Live waits for the start's run. */
export const settingUpSentence = 'Putting jason where a terminal finds it, and the skills where your agents read them…';

/** The act's label in the Command Line submenu. */
export const putBackLabel = 'Put Back jason and the Skills…';

const hostNames: Readonly<Record<string, string>> = { claude_code: 'Claude Code', codex: 'Codex' };

/** Pure: the report as lines a person reads. */
export function reportLines(report: SetupReport): string[] {
    const lines: string[] = [];
    if (report.command === null) {
        lines.push('jason was not placed where a terminal finds it.');
    } else {
        lines.push(`A new terminal finds jason through ${report.command.runs}.`);
        lines.push(`A terminal that was already open finds it by its full path only: ${report.command.openShell}.`);
    }
    for (const root of report.skills) {
        if (root.hosts.length === 0) continue;
        lines.push(`Jason’s skills for ${root.hosts.map((host) => hostNames[host] ?? host).join(' and ')}: ${root.root}`);
    }
    lines.push(...report.left, ...report.problems.map((problem) => `Not done: ${problem}`));
    return lines;
}

/** Pure: the Command Line submenu on macOS and Windows: the executable, where the set-up stands, and put back. */
export function setupMenuOf(state: SetupState, executable: string): { lines: string[]; acts: { label: string; act: 'put_back' }[] } {
    const lines = [`The jason executable: ${executable}`];
    const putBack = [{ label: putBackLabel, act: 'put_back' as const }];
    switch (state.state) {
        case 'unoffered':
            return { lines: [...lines, 'A development build sets nothing up: its jason is the checkout’s.'], acts: [] };
        case 'running':
            return { lines: [...lines, settingUpSentence], acts: [] };
        case 'idle':
            return { lines: [...lines, 'No set-up of this version is recorded.'], acts: putBack };
        case 'refused':
            return { lines: [...lines, `The set-up did not finish: ${state.why}`], acts: putBack };
        case 'reported':
            return { lines: [...lines, ...reportLines(state.report)], acts: putBack };
    }
}

/** Pure: what put back asks before it runs, naming what it writes on the system it runs on. */
export function putBackQuestion(platform: NodeJS.Platform, executable: string): { message: string; detail: string; button: string } {
    const command =
        platform === 'win32'
            ? 'It adds the folder that file is in to your account’s Path, and takes back the jason command files an earlier Jason Desktop put in WindowsApps.'
            : 'It writes ~/.local/bin/jason, which runs that file, and where a new terminal’s PATH lacks ~/.local/bin it adds the installer’s two lines to your shell profiles.';
    return {
        message: 'Put back jason and the skills?',
        detail: [
            `This runs ${executable} install.`,
            command,
            'It deploys Jason’s skills for Claude Code and Codex where they are installed, and leaves a skill of your own under the same name as it is.',
            'An older command-line install of Jason is retired, and a runtime running from it is stopped.',
        ].join(' '),
        button: 'Put It Back',
    };
}

/** Pure: what put back tells once it ran, in the routine's own sentences. */
export function putBackTold(state: SetupState): { type: 'info' | 'warning'; message: string; detail: string } {
    if (state.state === 'reported') {
        const { report } = state;
        if (report.completed) return { type: 'info', message: 'jason and the skills are in place.', detail: [...report.done, ...report.left].join('\n') };
        return { type: 'warning', message: 'Part of the set-up did not get there.', detail: [...report.problems.map((problem) => `Not done: ${problem}`), ...report.done, ...report.left].join('\n') };
    }
    if (state.state === 'refused') return { type: 'warning', message: 'The set-up did not run.', detail: state.why };
    return { type: 'warning', message: 'The set-up did not run.', detail: setupMenuOf(state, '').lines.slice(1).join('\n') };
}

export interface JasonLook {
    platform: NodeJS.Platform;
    /** The PATH an editor is started with: the desktop's. */
    path: string;
    pathext?: string | undefined;
    executable: string;
    /** `JASON_DATA_DIR` when the desktop runs with one. */
    dataDirectory: string | null;
    isProgram(file: string): boolean;
    /** A file's text, or null where it cannot be read. */
    read(file: string): string | null;
}

/**
 * Pure but for what it is given: which `jason` the PATH finds by name on macOS and Windows, for Open in. This window's: the
 * carried file, by its path or a link, or the routine's wrapper running it with the data directory the desktop runs on;
 * another; or none. No way out is named, since the item's one act is put back.
 */
export function jasonOnPath(look: JasonLook): JasonOnPath {
    const found = findOnPath('jason', { platform: look.platform, path: look.path, pathext: look.pathext, isProgram: (file) => look.isProgram(file) });
    if (found === null) return { found: 'none' };
    if (sameFinalFile(look.platform, found, look.executable)) return { found: 'this' };
    const text = look.read(found);
    const target = text === null ? null : wrapperTarget(text);
    return target !== null && target.executable === look.executable && target.dataDirectory === look.dataDirectory ? { found: 'this' } : { found: 'other' };
}

/** Whether this account may write a path, as `access(2)` answers. */
export function writable(path: string): boolean {
    try {
        accessSync(path, constants.W_OK);
        return true;
    } catch {
        return false;
    }
}

/**
 * Pure: where Jason is on macOS and whether its update can write there: Squirrel.Mac installs as this account only where
 * the application and the folder it is in are both writable, and otherwise asks for an administrator's password. Null on
 * every other system. The application copies itself nowhere.
 */
export function placeLine(platform: NodeJS.Platform, application: string, canWrite: (path: string) => boolean): string | null {
    if (platform !== 'darwin') return null;
    return canWrite(application) && canWrite(posix.dirname(application))
        ? `Jason is in ${application}, which an update can write to.`
        : `Jason is in ${application}, which this account cannot write to, so an update asks for an administrator’s password.`;
}
