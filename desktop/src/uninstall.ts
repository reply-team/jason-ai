/**
 * What a person's uninstall of the desktop takes off the machine besides the application's own folder, and what it says
 * it leaves. The nsis uninstaller starts the application with `--uninstall` before it removes the installed files, and
 * never on the uninstall an update runs (`installer/uninstall.nsh`). In that start the application opens no window and
 * starts no runtime: it takes off what it placed on the person's word and quits. On macOS and Linux, where no uninstaller
 * starts the application, the same steps are the menu's Remove What Jason Placed (`remove-placed.ts`).
 *
 * In this order, each step logged by its name and outcome and never a path:
 *
 * 1. A runtime started from the jason executable this application carries is stopped through `jason runtime stop`
 *    (`own-runtime.ts`), since the uninstaller then removes that executable. One that does not stop, or a run file whose
 *    runtime did not answer while the process it names still runs, ends it there: nothing is taken off, the start exits
 *    1, and the uninstaller quits before it removes a file, so no folder is left half removed. A runtime started from any
 *    other executable is not stopped, and is named.
 * 2. "Open Jason at login", when it is on.
 * 3. The runtime's logon registration, when it starts an executable of this application's: inside its folder, or, for
 *    an AppImage, one of the copies of its jason under `<data>/desktop/bundle/`; one that starts another executable
 *    stays, and is named.
 * 4. The PATH command's files that are ours, by the same read-back the menu makes (`PathCommand.remove`), by the name
 *    they were put there with; a file of either name another program wrote stays, and is named.
 * 5. For an AppImage, the copies of its jason under `<data>/desktop/bundle/`, only when no process runs from them.
 *
 * What stays is said by name: the data directory with everything Jason keeps there, the harness root with the person's
 * folders, and the updater's cache under the profile. A removal that fails is named with what takes it off by hand, and
 * the others are still made, since what is left outside the application is the person's to finish.
 */

import { existsSync, readdirSync, readFileSync, readlinkSync, rmSync } from 'node:fs';
import { join, posix, win32 } from 'node:path';
import type { Logger } from './log.ts';
import { unansweredWords, type StopOutcome, type Unanswered } from './own-runtime.ts';
import type { CommandStatus } from './path-command.ts';

/** On the command line of the start the uninstaller makes. */
export const uninstallFlag = '--uninstall';
/** Beside it when the uninstall was asked to be silent: nothing is shown. */
export const quietFlag = '--quiet';

/**
 * Whether this start is the uninstaller's, and whether it is to show what it did: only a packaged application on
 * Windows has an uninstaller that makes it, so anywhere else the flag is not read.
 */
export function uninstallStart(argv: readonly string[], where: { packaged: boolean; platform: NodeJS.Platform }): { quiet: boolean } | null {
    if (!where.packaged || where.platform !== 'win32') return null;
    return argv.includes(uninstallFlag) ? { quiet: argv.includes(quietFlag) } : null;
}

/**
 * On the command line of the start the nsis installer and uninstaller make when they find a runtime running from the
 * carried `jason.exe`: it stops that runtime the way the uninstall start does, through `jason runtime stop`, and does
 * nothing else. Later installers make it of the version they replace, so its name and its exits are kept.
 */
export const stopRuntimeFlag = '--stop-runtime';

/** Whether this start is the installer's stop of the carried runtime: on Windows, packaged, and an uninstall beside it wins. */
export function stopRuntimeStart(argv: readonly string[], where: { packaged: boolean; platform: NodeJS.Platform }): boolean {
    if (!where.packaged || where.platform !== 'win32') return false;
    return argv.includes(stopRuntimeFlag) && !argv.includes(uninstallFlag);
}

/**
 * The exit of that start: 0 when no runtime of this application's runs any more (none, another installation's, or
 * stopped), 1 when one may still run (a stop that failed, or a runtime that did not answer), and 2 when the start
 * itself failed. The installer reads none of them: it looks again, and the look decides.
 */
export async function stopRuntimeCode(stop: () => Promise<StopOutcome>): Promise<number> {
    let outcome: StopOutcome;
    try {
        outcome = await stop();
    } catch {
        return 2;
    }
    return outcome.kind === 'failed' || outcome.kind === 'unanswered' ? 1 : 0;
}

/** The value of a key read as the runtime's configuration reads keys: without regard to case. */
function keyed(record: unknown, key: string): unknown {
    if (typeof record !== 'object' || record === null || Array.isArray(record)) return undefined;
    const found = Object.keys(record).find((name) => name.toLowerCase() === key.toLowerCase());
    return found === undefined ? undefined : (record as Record<string, unknown>)[found];
}

/**
 * The harness root as the runtime reads it: `JASON_Harness__Root`, else the installation's settings, else `~/Jason`;
 * the variable's name and the settings' keys without regard to case, as the runtime's configuration reads them.
 */
export function harnessRoot(env: Readonly<Record<string, string | undefined>>, home: string, installationSettings: string): string {
    const named = (keyed(env, 'JASON_Harness__Root') as string | undefined)?.trim();
    if (named) return named;
    try {
        const root = keyed(keyed(JSON.parse(readFileSync(installationSettings, 'utf8')), 'Harness'), 'Root');
        if (typeof root === 'string' && root.trim() !== '') return root.trim();
    } catch {
        // No settings, or none that say: the default.
    }
    return join(home, 'Jason');
}

/**
 * The updater's cache: electron-updater keeps a downloaded update in `<cache>/<package name>-updater`, `<cache>` being
 * `<LOCALAPPDATA>` on Windows, `~/Library/Caches` on macOS, and on Linux `$XDG_CACHE_HOME`, else `~/.cache` (the pinned
 * electron-updater's `AppAdapter.getAppCacheDir`: `path.join(homedir, "Library", "Caches")` on darwin, and
 * `process.env["XDG_CACHE_HOME"] || path.join(homedir, ".cache")` elsewhere off Windows).
 */
export function updaterCache(env: Readonly<Record<string, string | undefined>>, home: string, platform: NodeJS.Platform): string {
    if (platform === 'darwin') return posix.join(home, 'Library', 'Caches', 'jason-desktop-updater');
    if (platform === 'linux') return posix.join(env['XDG_CACHE_HOME'] || posix.join(home, '.cache'), 'jason-desktop-updater');
    return win32.join(env['LOCALAPPDATA'] ?? win32.join(home, 'AppData', 'Local'), 'jason-desktop-updater');
}

/** Whether `file` is inside `folder`, as the platform compares paths: on Windows without regard to case. */
export function inside(platform: NodeJS.Platform, folder: string, file: string): boolean {
    const paths = platform === 'win32' ? win32 : posix;
    const relative = paths.relative(folder, file);
    return relative !== '' && !relative.startsWith('..') && !paths.isAbsolute(relative);
}

/**
 * Whether any process runs an executable inside `folder`, read from the process table Linux keeps under `/proc`: each
 * process's `exe` link names the file it runs. A process this account may not read is not ours and is passed over; a
 * table that cannot be listed at all says that one may, so nothing is removed on a guess.
 */
export function processesRunFrom(folder: string, table: { list(): string[]; exe(pid: string): string } = procTable): boolean {
    let pids: string[];
    try {
        pids = table.list().filter((name) => /^\d+$/.test(name));
    } catch {
        return true;
    }
    for (const pid of pids) {
        let exe: string;
        try {
            exe = table.exe(pid);
        } catch {
            continue;
        }
        if (inside('linux', folder, exe)) return true;
    }
    return false;
}

const procTable = { list: () => readdirSync('/proc'), exe: (pid: string) => readlinkSync(`/proc/${pid}/exe`) };

export interface UninstallDeps {
    platform: NodeJS.Platform;
    /** The folder the PATH command goes in (`commandFolder`), or null where none is offered. */
    folder: string | null;
    command: { status(): CommandStatus; remove(): CommandStatus };
    login: { isOn(): boolean; set(on: boolean): Promise<void> };
    /** Stopping the runtime this application carries when it is the one running (`stopOwnRuntime`); null when the bundle carries no CLI. */
    runtime: { stop(): Promise<StopOutcome> } | null;
    /** `jason runtime autostart status` and `disable`, through the bundled CLI; null when the bundle carries none. */
    autostart: { status(): Promise<{ registered: boolean; executable: string | null } | null>; disable(): Promise<boolean | null> } | null;
    /** The application's own folder: the one the uninstaller removes, the .app on macOS, the installed folder on Linux. */
    application: string;
    /**
     * An AppImage's copies of its jason, `<data>/desktop/bundle/` (`bundle-copy.ts`), with the look whether a process runs
     * from them (`processesRunFrom`); null for every other form, which carries its jason inside its own folder.
     */
    bundles: { folder: string; runsFrom(): boolean } | null;
    dataDirectory: string;
    harnessRoot: string;
    updaterCache: string;
    log: Logger;
}

/** The steps, in the order they are taken, as the log names them. */
export type RemovalStep = 'runtime' | 'login' | 'autostart' | 'command' | 'bundle';

export interface UninstallReport {
    /** Files and registrations taken off, each as the person would recognise it. */
    removed: string[];
    /** What there was nothing of to take off. */
    untouched: string[];
    /** What could not be taken off, each with what takes it off by hand. */
    failed: string[];
    /** What is not the desktop's to take off and stays, each with why. */
    left: string[];
    /** Why nothing was taken off and the uninstall goes no further: the runtime this application carries did not stop, or did not answer. */
    refused: string | null;
    /** What came of stopping the runtime, which the menu's act words its own refusal from. */
    runtime: StopOutcome;
    /** Each step taken, with its outcome, as it was logged. */
    steps: { step: RemovalStep; outcome: string }[];
}

/**
 * What the person is told when the runtime this application carries did not stop, or did not answer, so nothing was
 * removed; `again` is the act to repeat once it has.
 */
export function runtimeRefusal(outcome: { kind: 'failed'; exit: number } | Unanswered, again = 'uninstall Jason again'): string {
    if (outcome.kind === 'unanswered') {
        const { why, next } = unansweredWords(outcome);
        return `Nothing was removed, because ${why}. ${next} Then ${again}.`;
    }
    const how = outcome.exit === -1 ? 'jason runtime stop did not finish' : `jason runtime stop exited ${outcome.exit}`;
    return `Nothing was removed, because the runtime this application carries is running and did not stop (${how}). Stop it with jason runtime stop, then ${again}.`;
}

/** The exit of the uninstaller's start: 1 when the uninstall is to go no further, which the uninstaller acts on. */
export function uninstallExit(report: UninstallReport): number {
    return report.refused === null ? 0 : 1;
}

/** The four files a PATH command of either name can be in the folder. */
function commandFiles(platform: NodeJS.Platform, folder: string): string[] {
    const names = platform === 'win32' ? ['jason.cmd', 'jason', 'jason-desktop.cmd', 'jason-desktop'] : ['jason', 'jason-desktop'];
    return names.map((name) => join(folder, name));
}

/** Takes off what the desktop placed outside its folder, in the order above, and says what it took and what it left. */
export async function uninstall(deps: UninstallDeps): Promise<UninstallReport> {
    const removed: string[] = [];
    const untouched: string[] = [];
    const failed: string[] = [];
    const left: string[] = [];
    const steps: { step: RemovalStep; outcome: string }[] = [];
    const ended = (step: RemovalStep, outcome: string) => {
        steps.push({ step, outcome });
        deps.log.info('removal_step', { Step: step, Outcome: outcome });
    };

    // 1. The runtime this application's jason runs as; one that does not stop ends it here.
    const running: StopOutcome = deps.runtime === null ? { kind: 'none' } : await deps.runtime.stop().catch((): StopOutcome => ({ kind: 'failed', exit: -1 }));
    ended('runtime', running.kind);
    if (running.kind === 'failed' || running.kind === 'unanswered') {
        return { removed: [], untouched: [], failed: [], left: [], refused: runtimeRefusal(running), runtime: running, steps };
    }
    if (running.kind === 'stopped') removed.push('The runtime this application carries, which was running: it was stopped with jason runtime stop');
    if (running.kind === 'none') untouched.push('No runtime of this application’s was running');

    // 2. Open Jason at login.
    try {
        if (deps.login.isOn()) {
            await deps.login.set(false);
            removed.push('Open Jason at login');
            ended('login', 'off');
        } else {
            untouched.push('Open Jason at login was off');
            ended('login', 'was_off');
        }
    } catch {
        failed.push('Open Jason at login, which could not be turned off: the system’s list of apps that open at login holds it');
        ended('login', 'failed');
    }

    // 3. The runtime's start at logon, only when it starts this application's jason.
    const ours = (executable: string) => inside(deps.platform, deps.application, executable) || (deps.bundles !== null && inside(deps.platform, deps.bundles.folder, executable));
    let registration: { registered: boolean; executable: string | null } | null = null;
    let unread = false;
    try {
        registration = deps.autostart === null ? null : await deps.autostart.status();
        unread = deps.autostart !== null && registration === null;
    } catch {
        unread = true;
    }
    if (unread) {
        failed.push('The runtime’s start at logon, which could not be read: jason runtime autostart status says whether it is there');
        ended('autostart', 'unread');
    } else if (registration?.registered === true) {
        const executable = registration.executable;
        if (executable !== null && ours(executable)) {
            const still = await deps.autostart?.disable().catch(() => null);
            if (still === false) {
                removed.push('The runtime’s start at logon');
                ended('autostart', 'off');
            } else {
                failed.push('The runtime’s start at logon, which could not be taken off: jason runtime autostart disable takes it off');
                ended('autostart', 'failed');
            }
        } else {
            left.push(`The runtime’s start at logon, which starts ${executable ?? 'an executable it does not name'}, not this application’s`);
            ended('autostart', 'another');
        }
    } else if (registration?.registered === false) {
        untouched.push('The runtime’s start at logon was not set up');
        ended('autostart', 'was_off');
    } else {
        ended('autostart', 'no_cli');
    }

    // 4. The PATH command's files that are this application's, by the name they were put there with.
    if (deps.folder !== null) {
        const files = commandFiles(deps.platform, deps.folder);
        const before = files.filter((file) => existsSync(file));
        let threw = false;
        try {
            deps.command.remove();
        } catch {
            threw = true;
        }
        let took = 0;
        for (const file of before) {
            if (!existsSync(file)) {
                removed.push(file);
                took += 1;
            } else if (threw) failed.push(`${file}, which could not be taken off: deleting the file takes it off`);
            else left.push(`${file}, which another program wrote`);
        }
        if (before.length === 0) untouched.push('No jason command of this application’s was on the PATH');
        ended('command', threw ? 'failed' : took > 0 ? 'removed' : before.length > 0 ? 'another' : 'was_off');
    }

    // 5. An AppImage's copies of its jason, only when nothing runs from them.
    if (deps.bundles !== null && existsSync(deps.bundles.folder)) {
        const folder = deps.bundles.folder;
        if (deps.bundles.runsFrom()) {
            failed.push(`The copies of jason in ${folder}, which a process still runs from: once no Jason runtime or jason command runs, deleting the folder takes them off`);
            ended('bundle', 'running');
        } else {
            try {
                rmSync(folder, { recursive: true, force: true });
                removed.push(`The copies of jason in ${folder}`);
                ended('bundle', 'removed');
            } catch {
                failed.push(`The copies of jason in ${folder}, which could not all be taken off: deleting the folder takes them off`);
                ended('bundle', 'failed');
            }
        }
    }

    left.push(`The data directory, ${deps.dataDirectory}, with the campaigns, the journal, the settings and the logs`);
    left.push(`The harness root, ${deps.harnessRoot}, with your folders and memory files`);
    left.push(`The updater’s cache, ${deps.updaterCache}`);
    if (running.kind === 'another') left.push('A runtime is running that this application could not confirm as its own: it was not stopped; jason runtime stop stops it');
    return { removed, untouched, failed, left, refused: null, runtime: running, steps };
}

/** What the uninstall shows the person: what could not be taken off is said first among what is left. */
export function uninstallText(report: UninstallReport): { message: string; detail: string } {
    if (report.refused !== null) return { message: 'Jason was not removed.', detail: report.refused };
    const list = (lines: readonly string[]) => lines.map((line) => `• ${line}`).join('\n');
    const taken = report.removed.length === 0 ? 'Nothing was taken off.' : `Taken off:\n${list(report.removed)}`;
    return { message: 'Jason is being removed from this computer.', detail: `${taken}\n\nLeft in place:\n${list([...report.failed, ...report.left])}` };
}
