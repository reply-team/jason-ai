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
 * - **Never between the person and the window.** A start runs it once. Live waits for it at most `liveWaitMs` and then
 *   goes on; the run is never ended by the desktop, and one past `setupLongMs` is said in the item as taking long. A
 *   failure is said in the item, which offers to put it back, and nothing is tried again in a loop.
 * - **A runtime it stops is started again.** Before the routine, whether a runtime answers on the data directory is read
 *   by a read that writes nothing (`jason runtime status`; none where there is no descriptor). After any outcome, a
 *   report, a refusal, no report or a run that ended after Live went on, where one answered before and none answers now,
 *   one is started from the carried file (`jason runtime start`): in Demo as in Live, and on Put Back alike, since
 *   whoever asked for the stop starts a runtime again.
 * - **The PATH it is told** is the login shell's own, as that shell printed it, or the empty value where it gave none, on
 *   macOS; never the desktop's, which adds places to it. Windows reads none.
 * - **Its report is shown as facts**: where a new terminal finds `jason`, only where the routine read it back as placed,
 *   and what a terminal already open types, the skills by the programs that read them, the routine's own sentences of
 *   what it left and why, and of what it did not do, and what became of a runtime it stopped. Never its JSON.
 */

import { accessSync, closeSync, constants, existsSync, mkdirSync, openSync, readFileSync, readSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, posix } from 'node:path';
import type { Cli, CliResult } from './cli.ts';
import { wrapperTarget } from './command-line.ts';
import type { Logger } from './log.ts';
import { sameFinalFile, type JasonOnPath } from './path-command.ts';
import { findOnPath } from './program-path.ts';
import { hosts } from './providers-model.ts';
import { hostWords } from './roles-profile.ts';

/**
 * How long Live waits for the start's run before it brings the installation up anyway. The run is left to end: a retired
 * install's runtime is drained for up to a minute and then stopped, and a stop that comes later is answered as one in time.
 */
export const liveWaitMs = 30_000;

/** Past this the Command Line item says the run is taking long; nothing else changes, and the run is never ended. */
export const setupLongMs = 300_000;

/** The routine's time limit, as `cli.ts` hands it to `execFile`: none. Node ends a child only where it is above 0. */
export const noLimit = 0;

/** How long the read of whether a runtime answers, and the start of one, are given: `installation.ts`'s own bounds. */
const lookLimitMs = 30_000;
const startLimitMs = 180_000;

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
    /**
     * The terminal command; null where the routine placed none. `placed` is whether a new terminal finds this jason, as the
     * routine read it back: a wrapper whose profile lines were refused, or a Path entry that was not written, is not.
     */
    command: { runs: string; placed: boolean; openShell: string; changed: boolean; profiles: string[]; takenBack: string[] } | null;
    /** One entry per skills root of an agent host found. */
    skills: { root: string; hosts: string[]; written: number; unchanged: number; leftAlone: string[] }[];
    /** What became of a runtime on the data directory: `not_running`, `running`, `stopped` or `would_not_stop`. */
    runtime: string;
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
    const { version, executable, command, skills, runtime, done, left, problems, completed } = raw;
    const doneSaid = texts(done);
    const leftSaid = texts(left);
    const problemsSaid = texts(problems);
    if (!isText(version) || !isText(executable) || !isText(runtime) || typeof completed !== 'boolean' || !Array.isArray(skills)) return null;
    if (doneSaid === null || leftSaid === null || problemsSaid === null) return null;
    let placed: SetupReport['command'] = null;
    if (command !== null) {
        if (!isObject(command)) return null;
        const profiles = texts(command.profiles);
        const takenBack = texts(command.taken_back);
        if (!isText(command.runs) || typeof command.placed !== 'boolean' || !isText(command.open_shell) || typeof command.changed !== 'boolean' || profiles === null || takenBack === null) return null;
        placed = { runs: command.runs, placed: command.placed, openShell: command.open_shell, changed: command.changed, profiles, takenBack };
    }
    const roots: SetupReport['skills'] = [];
    for (const entry of skills) {
        if (!isObject(entry)) return null;
        const hosts = texts(entry.hosts);
        const leftAlone = texts(entry.left_alone);
        if (!isText(entry.root) || hosts === null || leftAlone === null || typeof entry.written !== 'number' || typeof entry.unchanged !== 'number') return null;
        roots.push({ root: entry.root, hosts, written: entry.written, unchanged: entry.unchanged, leftAlone });
    }
    return { version, executable, command: placed, skills: roots, runtime, done: doneSaid, left: leftSaid, problems: problemsSaid, completed };
}

/** What a run of the routine answered: its report, or why there is none, in its own sentence where it said one. */
export type SetupAnswer = { report: SetupReport } | { refused: string };

/** The error sentence a CLI run printed as its document, or null. */
function errorSaid(stdout: string): string | null {
    let parsed: unknown = null;
    try {
        parsed = JSON.parse(stdout);
    } catch {
        return null;
    }
    const error = isObject(parsed) && isObject(parsed.error) ? parsed.error : null;
    return error !== null && isText(error.message) && error.message !== '' ? error.message : null;
}

/** Pure: a run's answer from what the CLI gave back. */
export function answerOf(result: CliResult): SetupAnswer {
    if (result.exit === -1) return { refused: 'jason install could not be started, or ended before it printed a report.' };
    const said = errorSaid(result.stdout);
    if (said !== null) return { refused: said };
    let parsed: unknown = null;
    try {
        parsed = JSON.parse(result.stdout);
    } catch {
        parsed = null;
    }
    const report = reportOf(parsed);
    return report === null ? { refused: `jason install exited ${result.exit} and printed no report.` } : { report };
}

/**
 * The runtime on the data directory, as the set-up's caller asks after it: whether one answers, by a read that writes
 * nothing, and the start of one from the carried file. `start` answers null where it started one, else why not.
 */
export interface RuntimeDoor {
    answers(): Promise<boolean>;
    start(): Promise<string | null>;
}

/**
 * The door through the carried jason: `jason runtime status`, which reads the descriptor and asks the runtime, run only
 * where the descriptor `<data>/run/runtime.json` is there: with none, or no data directory, no runtime answers and no
 * jason is run, so a new installation's first start writes nothing by asking. And `jason runtime start`.
 */
export function runtimeDoor(cli: Cli, runFile: string, exists: (path: string) => boolean = existsSync): RuntimeDoor {
    return {
        async answers() {
            if (!exists(runFile)) return false;
            return (await cli.run(['runtime', 'status'], { timeoutMs: lookLimitMs })).exit === 0;
        },
        async start() {
            const result = await cli.run(['runtime', 'start'], { timeoutMs: startLimitMs });
            if (result.exit === 0) return null;
            return errorSaid(result.stdout) ?? `jason runtime start exited ${result.exit}.`;
        },
    };
}

/** What the desktop did for a runtime that answered before the routine and did not after it. */
export type RuntimeAfter = { act: 'started' } | { act: 'not_started'; why: string };

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
    /** `long` once it has run past `setupLongMs`. */
    | { state: 'running'; long?: boolean }
    | { state: 'reported'; report: SetupReport; runtimeAfter?: RuntimeAfter }
    | { state: 'refused'; why: string; runtimeAfter?: RuntimeAfter };

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
    /** The runtime on the data directory (`runtimeDoor`), read before and after each run. */
    runtime: RuntimeDoor;
    log: Logger;
    /** Past how long a run is said as taking long; `setupLongMs` unless a test names another. */
    longAfterMs?: number;
    /**
     * THE FINISHING STEP'S SEAM: the command line's own finishing step of an update (`jason update apply --finish`, run on a
     * carried install with no update in flight, `docs/release-and-update.md` §7), which brings the plugin packages, the
     * recorded skills deployments and the UI up to the version installed and starts no runtime. Run after a routine that
     * completed; the record is written only when both completed. Absent until the command line offers it, and then the
     * routine alone decides.
     */
    finishingStep?(): Promise<FinishingStep>;
}

/** What the finishing step answered: whether it completed, and its sentences of what it did not do. */
export interface FinishingStep {
    completed: boolean;
    problems: string[];
}

const messageOf = (error: unknown): string => (error instanceof Error ? error.message : String(error));

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

    /**
     * Resolves once the start's run has ended (`ended`), or once `boundMs` has passed with it still running (`running`),
     * whichever is first; the run itself goes on either way.
     */
    async settledWithin(boundMs: number): Promise<'ended' | 'running'> {
        const run = this.#atStart;
        if (run === null) return 'ended';
        let timer: ReturnType<typeof setTimeout> | undefined;
        const bound = new Promise<'running'>((resolve) => {
            timer = setTimeout(() => resolve('running'), boundMs);
        });
        try {
            return await Promise.race([run.then(() => 'ended' as const), bound]);
        } finally {
            clearTimeout(timer);
        }
    }

    /** Put back, on the person's word: the routine run again, unless it is running already. */
    putBack(): Promise<SetupState> {
        if (!this.#deps.offered || this.#deps.version === null) return Promise.resolve(this.#state);
        return this.#run('put_back');
    }

    #run(why: 'start' | 'put_back'): Promise<SetupState> {
        if (this.#running !== null) return this.#running;
        this.#set({ state: 'running' });
        // Said once, as taking long; the run is never ended for it.
        const long = setTimeout(() => {
            if (this.#state.state === 'running') this.#set({ state: 'running', long: true });
        }, this.#deps.longAfterMs ?? setupLongMs);
        long.unref?.();
        this.#running = (async (): Promise<SetupState> => {
            const { runtime, log } = this.#deps;
            // Whether a runtime answers on the data directory before the routine, which may stop it.
            const before = await runtime.answers().catch(() => false);
            let next: Extract<SetupState, { state: 'reported' | 'refused' }>;
            try {
                next = await this.#routine(why);
            } catch (error) {
                next = { state: 'refused', why: messageOf(error) };
            }
            // After any outcome: one answered before and none answers now, so one is started from the carried file.
            if (!before || (await runtime.answers().catch(() => false))) return next;
            const failed = await runtime.start().catch(messageOf);
            log.info('machine_setup_runtime', { Why: why, Started: failed === null });
            return { ...next, runtimeAfter: failed === null ? { act: 'started' } : { act: 'not_started', why: failed } };
        })().then((next) => {
            clearTimeout(long);
            this.#running = null;
            this.#set(next);
            return next;
        });
        return this.#running;
    }

    /** One run of the routine and the finishing step after it, the record written where both completed. */
    async #routine(why: 'start' | 'put_back'): Promise<Extract<SetupState, { state: 'reported' | 'refused' }>> {
        const { cli, platform, log, record } = this.#deps;
        const version = this.#deps.version ?? '';
        let terminalPath: string | null = null;
        if (platform === 'darwin') terminalPath = await this.#deps.terminalPath().catch(() => null);
        // No time limit: a routine ended mid-write would leave a runtime draining or a retirement half done.
        const result = await cli.run(setupArguments(platform, terminalPath), { timeoutMs: noLimit });
        const answer = answerOf(result);
        const routineDone = 'report' in answer && answer.report.completed && result.exit === 0;
        log.info('machine_setup_ran', { Why: why, Exit: result.exit, Completed: routineDone });
        let finishing: FinishingStep | null = null;
        if (routineDone && this.#deps.finishingStep !== undefined) {
            finishing = await this.#deps.finishingStep().catch((error: unknown) => ({ completed: false, problems: [`The finishing step did not run: ${messageOf(error)}`] }));
            log.info('machine_setup_finished', { Why: why, Completed: finishing.completed });
        }
        const completed = routineDone && (finishing === null || finishing.completed);
        if (completed) {
            try {
                writeSetupRecord(record, version, JSON.parse(result.stdout));
            } catch {
                log.warn('machine_setup_unrecorded', { Version: version });
            }
        }
        if (!('report' in answer)) return { state: 'refused', why: answer.refused };
        // What the finishing step did not do is said beside the routine's own problems.
        const report = finishing === null || finishing.completed ? answer.report : { ...answer.report, problems: [...answer.report.problems, ...finishing.problems], completed: false };
        return { state: 'reported', report };
    }

    #set(next: SetupState): void {
        this.#state = next;
        for (const changed of this.#changed) changed();
    }
}

/** What the starting page says while Live waits for the start's run. */
export const settingUpSentence = 'Putting jason where a terminal finds it, and the skills where your agents read them…';

/** What the item says of a run past `setupLongMs`. */
export const takingLongSentence = 'The set-up is taking long: it has run for over five minutes, and it is left to finish.';

/** The act's label in the Command Line submenu. */
export const putBackLabel = 'Put Back jason and the Skills…';

/** The routine's host words, by the names the desktop already shows for them (`providers-model.ts`). */
const hostNames: Readonly<Record<string, string>> = Object.fromEntries(hosts.flatMap((host) => (hostWords[host.id] === undefined ? [] : [[hostWords[host.id], host.label]])));

/** Pure: the report as lines a person reads. */
export function reportLines(report: SetupReport): string[] {
    const lines: string[] = [];
    if (report.command === null || !report.command.placed) {
        // Said only where the routine read the command back as placed; what stopped it is among the problems below.
        lines.push('jason is not yet where a new terminal finds it.');
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

/** Pure: what became of a runtime that answered before the run and not after it, where the desktop started one or tried. */
export function runtimeLines(after: RuntimeAfter | undefined, report: SetupReport | null): string[] {
    if (after === undefined) return [];
    const stopped = report?.runtime === 'stopped' ? 'The set-up stopped the runtime of the older command-line install' : 'The runtime that was running stopped while the set-up ran';
    return after.act === 'started'
        ? [`${stopped}; Jason Desktop started one again from its own jason.`]
        : [`${stopped}, and starting one again did not work: ${after.why} \`jason runtime start\` starts one.`];
}

/** Pure: the Command Line submenu on macOS and Windows: the executable, where the set-up stands, and put back. */
export function setupMenuOf(state: SetupState, executable: string): { lines: string[]; acts: { label: string; act: 'put_back' }[] } {
    const lines = [`The jason executable: ${executable}`];
    const putBack = [{ label: putBackLabel, act: 'put_back' as const }];
    switch (state.state) {
        case 'unoffered':
            return { lines: [...lines, 'A development build sets nothing up: its jason is the checkout’s.'], acts: [] };
        case 'running':
            return { lines: [...lines, state.long === true ? takingLongSentence : settingUpSentence], acts: [] };
        case 'idle':
            return { lines: [...lines, 'No set-up of this version is recorded.'], acts: putBack };
        case 'refused':
            return { lines: [...lines, `The set-up did not finish: ${state.why}`, ...runtimeLines(state.runtimeAfter, null)], acts: putBack };
        case 'reported':
            return { lines: [...lines, ...reportLines(state.report), ...runtimeLines(state.runtimeAfter, state.report)], acts: putBack };
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
            'An older command-line install of Jason is retired, and a runtime running from it is stopped and then started again from this application’s jason.',
        ].join(' '),
        button: 'Put It Back',
    };
}

/** Pure: what put back tells once it ran, in the routine's own sentences. */
export function putBackTold(state: SetupState): { type: 'info' | 'warning'; message: string; detail: string } {
    if (state.state === 'reported') {
        const { report } = state;
        const runtime = runtimeLines(state.runtimeAfter, report);
        const runtimeFailed = state.runtimeAfter?.act === 'not_started';
        if (report.completed && !runtimeFailed) return { type: 'info', message: 'jason and the skills are in place.', detail: [...report.done, ...report.left, ...runtime].join('\n') };
        return { type: 'warning', message: 'Part of the set-up did not get there.', detail: [...report.problems.map((problem) => `Not done: ${problem}`), ...runtime, ...report.done, ...report.left].join('\n') };
    }
    if (state.state === 'refused') return { type: 'warning', message: 'The set-up did not run.', detail: [state.why, ...runtimeLines(state.runtimeAfter, null)].join('\n') };
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

/**
 * The first bytes of a file as text, or null where it cannot be read: a wrapper of ours is a few lines, and what the PATH
 * finds may be an executable of many megabytes, which is never read whole.
 */
export function readHead(file: string, cap = 4096): string | null {
    let descriptor: number | null = null;
    try {
        descriptor = openSync(file, 'r');
        const buffer = Buffer.alloc(cap);
        const read = readSync(descriptor, buffer, 0, cap, 0);
        return buffer.subarray(0, read).toString('utf8');
    } catch {
        return null;
    } finally {
        if (descriptor !== null) closeSync(descriptor);
    }
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
