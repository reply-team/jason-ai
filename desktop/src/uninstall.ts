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
 * folders, and the updater's cache under the profile.
 */

import { existsSync, readFileSync } from 'node:fs';
import { join, posix, win32 } from 'node:path';
import type { CommandStatus } from './path-command.ts';

/** On the command line of the start the uninstaller makes. */
export const uninstallFlag = '--uninstall';
/** Beside it when the uninstall was asked to be silent: nothing is shown. */
export const quietFlag = '--quiet';

/** Whether this start is the uninstaller's, and whether it is to show what it did. */
export function uninstallStart(argv: readonly string[]): { quiet: boolean } | null {
    return argv.includes(uninstallFlag) ? { quiet: argv.includes(quietFlag) } : null;
}

/** The harness root as the runtime reads it: `JASON_Harness__Root`, else the installation's settings, else `~/Jason`. */
export function harnessRoot(env: Readonly<Record<string, string | undefined>>, home: string, installationSettings: string): string {
    const named = env['JASON_Harness__Root']?.trim();
    if (named) return named;
    try {
        const root = (JSON.parse(readFileSync(installationSettings, 'utf8')) as { Harness?: { Root?: unknown } }).Harness?.Root;
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

    if (deps.folder !== null) {
        const files = commandFiles(deps.platform, deps.folder);
        const before = files.filter((file) => existsSync(file));
        deps.command.remove();
        for (const file of before) {
            if (existsSync(file)) left.push(`${file}, which another program wrote`);
            else removed.push(file);
        }
    }

    if (deps.login.isOn()) {
        await deps.login.set(false);
        removed.push('Open Jason at login');
    }

    const registration = deps.autostart === null ? null : await deps.autostart.status();
    if (registration?.registered === true) {
        const executable = registration.executable;
        if (executable !== null && inside(deps.platform, deps.application, executable)) {
            const still = await deps.autostart?.disable();
            if (still === false) removed.push('The runtime’s start at logon');
            else left.push('The runtime’s start at logon, which could not be taken off: jason runtime autostart disable takes it off');
        } else {
            left.push(`The runtime’s start at logon, which starts ${executable ?? 'an executable it does not name'}, not this application’s`);
        }
    }

    left.push(`The data directory, ${deps.dataDirectory}, with the campaigns, the journal, the settings and the logs`);
    left.push(`The harness root, ${deps.harnessRoot}, with your folders and memory files`);
    left.push(`The updater’s cache, ${deps.updaterCache}`);
    left.push('A runtime that is running: it is not stopped here; jason runtime stop stops it');
    return { removed, left };
}

/** What the uninstall shows the person. */
export function uninstallText(report: UninstallReport): { message: string; detail: string } {
    const list = (lines: readonly string[]) => lines.map((line) => `• ${line}`).join('\n');
    const taken = report.removed.length === 0 ? 'Nothing else was placed on this computer.' : `Taken off:\n${list(report.removed)}`;
    return { message: 'Jason is being removed from this computer.', detail: `${taken}\n\nLeft in place:\n${list(report.left)}` };
}
