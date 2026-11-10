/**
 * What a person's uninstall of the desktop takes off the machine besides the application's own folder, and what it says
 * it leaves. The nsis uninstaller starts the application with `--uninstall` before it removes the installed files, and
 * never on the uninstall an update runs (`installer/uninstall.nsh`). In that start the application opens no window and
 * starts no runtime: it takes off what it placed on the person's word and quits.
 *
 * - The PATH command's files that are ours, by the same read-back the menu makes (`PathCommand.remove`); a file of that
 *   name another program wrote stays, and is named.
 * - "Open Jason at login", when it is on.
 * - The runtime's logon registration, when it starts an executable inside this application's folder; one that starts
 *   another executable stays, and is named.
 *
 * What stays is said by name: the data directory with everything Jason keeps there, the harness root with the person's
 * folders, and the updater's cache under the profile. A removal that fails is named there too, with what takes it off,
 * and the others are still made: the uninstall goes on, since what is left outside the folder is the person's to finish.
 *
 * Before any of it, a runtime started from the jason executable this application carries is stopped through
 * `jason runtime stop` (`own-runtime.ts`), since the uninstaller then removes that executable. One that does not stop,
 * or a run file whose runtime did not answer while the process it names still runs, ends the uninstall there: nothing
 * is taken off, the start exits 1, and the uninstaller quits before it removes a file, so no folder is left half
 * removed. A runtime started from any other executable is not stopped, and is named.
 */

import { existsSync, readFileSync } from 'node:fs';
import { join, posix, win32 } from 'node:path';
import { unansweredWords, type StopOutcome } from './own-runtime.ts';
import type { CommandStatus } from './path-command.ts';

/** On the command line of the start the uninstaller makes. */
export const uninstallFlag = '--uninstall';
/** Beside it when the uninstall was asked to be silent: nothing is shown. */
export const quietFlag = '--quiet';

/** Whether this start is the uninstaller's, and whether it is to show what it did. */
export function uninstallStart(argv: readonly string[]): { quiet: boolean } | null {
    return argv.includes(uninstallFlag) ? { quiet: argv.includes(quietFlag) } : null;
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

/** The updater's cache: electron-updater keeps a downloaded update in `<LOCALAPPDATA>/<package name>-updater`. */
export function updaterCache(env: Readonly<Record<string, string | undefined>>, home: string): string {
    return win32.join(env['LOCALAPPDATA'] ?? win32.join(home, 'AppData', 'Local'), 'jason-desktop-updater');
}

/** Whether `file` is inside `folder`, as the platform compares paths: on Windows without regard to case. */
export function inside(platform: NodeJS.Platform, folder: string, file: string): boolean {
    const paths = platform === 'win32' ? win32 : posix;
    const relative = paths.relative(folder, file);
    return relative !== '' && !relative.startsWith('..') && !paths.isAbsolute(relative);
}

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
    /** The application's own folder, the one the uninstaller removes. */
    application: string;
    dataDirectory: string;
    harnessRoot: string;
    updaterCache: string;
}

export interface UninstallReport {
    /** Files and registrations taken off, each as the person would recognise it. */
    removed: string[];
    /** What is not the desktop's to take off and stays, each with why. */
    left: string[];
    /** Why nothing was taken off and the uninstall goes no further: the runtime this application carries did not stop, or did not answer. */
    refused: string | null;
}

/** What the person is told when the runtime this application carries did not stop, or did not answer, so nothing was removed. */
export function runtimeRefusal(outcome: { kind: 'failed'; exit: number } | { kind: 'unanswered'; pid: number | null }): string {
    if (outcome.kind === 'unanswered') {
        const { why, next } = unansweredWords(outcome.pid);
        return `Nothing was removed, because ${why}. ${next} Then uninstall Jason again.`;
    }
    const how = outcome.exit === -1 ? 'jason runtime stop did not finish' : `jason runtime stop exited ${outcome.exit}`;
    return `Nothing was removed, because the runtime this application carries is running and did not stop (${how}). Stop it with jason runtime stop, then uninstall Jason again.`;
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

/** Takes off what the desktop placed outside its folder, and says what it took and what it left. */
export async function uninstall(deps: UninstallDeps): Promise<UninstallReport> {
    const removed: string[] = [];
    const left: string[] = [];

    const running: StopOutcome = deps.runtime === null ? { kind: 'none' } : await deps.runtime.stop().catch((): StopOutcome => ({ kind: 'failed', exit: -1 }));
    if (running.kind === 'failed' || running.kind === 'unanswered') return { removed: [], left: [], refused: runtimeRefusal(running) };
    if (running.kind === 'stopped') removed.push('The runtime this application carries, which was running: it was stopped with jason runtime stop');

    if (deps.folder !== null) {
        const files = commandFiles(deps.platform, deps.folder);
        const before = files.filter((file) => existsSync(file));
        let failed = false;
        try {
            deps.command.remove();
        } catch {
            failed = true;
        }
        for (const file of before) {
            if (!existsSync(file)) removed.push(file);
            else if (failed) left.push(`${file}, which could not be taken off: deleting the file takes it off`);
            else left.push(`${file}, which another program wrote`);
        }
    }

    if (deps.login.isOn()) {
        try {
            await deps.login.set(false);
            removed.push('Open Jason at login');
        } catch {
            left.push('Open Jason at login, which could not be turned off: the system’s list of apps that open at login holds it');
        }
    }

    let registration: { registered: boolean; executable: string | null } | null = null;
    try {
        registration = deps.autostart === null ? null : await deps.autostart.status();
    } catch {
        left.push('The runtime’s start at logon, which could not be read: jason runtime autostart status says whether it is there');
    }
    if (registration?.registered === true) {
        const executable = registration.executable;
        if (executable !== null && inside(deps.platform, deps.application, executable)) {
            const still = await deps.autostart?.disable().catch(() => null);
            if (still === false) removed.push('The runtime’s start at logon');
            else left.push('The runtime’s start at logon, which could not be taken off: jason runtime autostart disable takes it off');
        } else {
            left.push(`The runtime’s start at logon, which starts ${executable ?? 'an executable it does not name'}, not this application’s`);
        }
    }

    left.push(`The data directory, ${deps.dataDirectory}, with the campaigns, the journal, the settings and the logs`);
    left.push(`The harness root, ${deps.harnessRoot}, with your folders and memory files`);
    left.push(`The updater’s cache, ${deps.updaterCache}`);
    if (running.kind === 'another') left.push('A runtime is running that this application could not confirm as its own: it was not stopped; jason runtime stop stops it');
    return { removed, left, refused: null };
}

/** What the uninstall shows the person. */
export function uninstallText(report: UninstallReport): { message: string; detail: string } {
    if (report.refused !== null) return { message: 'Jason was not removed.', detail: report.refused };
    const list = (lines: readonly string[]) => lines.map((line) => `• ${line}`).join('\n');
    const taken = report.removed.length === 0 ? 'Nothing was taken off.' : `Taken off:\n${list(report.removed)}`;
    return { message: 'Jason is being removed from this computer.', detail: `${taken}\n\nLeft in place:\n${list(report.left)}` };
}
