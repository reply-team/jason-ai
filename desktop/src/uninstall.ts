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
 *    (`own-runtime.ts`), since the uninstaller then removes that executable; on an AppImage, one started from any of the
 *    copies of its jason is this application's too (`stopFromAny`). Nothing is ever killed. One that does not stop, or a
 *    run file whose runtime did not answer while the process it names still runs, ends it there: nothing is taken off,
 *    the start exits 1, and the uninstaller quits before it removes a file, so no folder is left half removed. A runtime
 *    started from any other executable is not stopped, and is named.
 * 2. "Open Jason at login", when it is on. A file under the desktop's own name that starts another path stays, and is
 *    named with what it starts.
 * 3. The runtime's logon registration, when it starts an executable of this application's: inside its folder, or, for
 *    an AppImage, one of the copies of its jason under `<data>/desktop/bundle/`, decided on resolved paths
 *    (`resolved-path.ts`). One that starts another executable stays, and is named; so does one whose executable cannot be
 *    told to be this application's or not.
 * 4. The PATH command's files that are ours, by the same read-back the menu makes (`PathCommand.remove`), by the name
 *    they were put there with; a file of either name another program wrote stays, and is named.
 * 5. For an AppImage, the copies of its jason under `<data>/desktop/bundle/`: only the real folders named as a version,
 *    and this desktop's temporaries, the one selection the removal of other versions' copies makes (`bundleEntries`);
 *    only when no process runs from them, read from `/proc` on resolved paths; never while a hand act above runs the
 *    carried jason; and the folder itself only once that leaves it empty. A link or anything else there stays, and is
 *    named.
 *
 * 6. The demo's two folders, run from the person's installation: the demo's data directory (the person's with `-demo` after
 *    it) and the demo's harness root (the person's with `-demo` after it). The demo's runtime is stopped first, through the
 *    demo's own run file (`demo-runtime.ts`), never the person's; one that does not stop keeps both folders. A folder that is
 *    a link or a file, or that is, holds or lies inside the person's own data directory, harness root or application, is
 *    left and named. A demo run offers no removal at all (`mode.ts`).
 *
 * What stays is said by name: the data directory with everything Jason keeps there, the harness root with the person's
 * folders, and the updater's cache under the profile. A removal that fails is named with what takes it off by hand,
 * naming the carried jason by its full path, and the others are still made, since what is left outside the application
 * is the person's to finish. Every value a person or another program wrote is made inert before it is shown (`inert`).
 */

import { existsSync, lstatSync, readdirSync, readFileSync, readlinkSync, rmdirSync, rmSync } from 'node:fs';
import { join, posix, win32 } from 'node:path';
import { bundleEntries } from './bundle-copy.ts';
import { inert } from './inert.ts';
import type { Logger } from './log.ts';
import { unansweredWords, type StopOutcome, type Unanswered } from './own-runtime.ts';
import type { CommandStatus } from './path-command.ts';
import { containedIn, lexicallyInside, resolveReal, type Resolve } from './resolved-path.ts';

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

/** Whether `file` is inside `folder` as written, as the platform compares paths: on Windows without regard to case. */
export function inside(platform: NodeJS.Platform, folder: string, file: string): boolean {
    return lexicallyInside(platform, folder, file);
}

/** Whether a process runs from a folder: none, one does, or the process table could not be read. */
export type RunsFrom = 'none' | 'running' | 'unread';

/**
 * Whether any process runs an executable inside `folder`, read from the process table Linux keeps under `/proc`: each
 * process's `exe` link names the file it runs, resolved and, once that file is deleted, with ` (deleted)` after it. The
 * folder is resolved too, since the data directory may pass through a link. A process this account may not read is not
 * ours and is passed over; a table that cannot be listed, or a folder that cannot be resolved, cannot be read, and
 * nothing is removed on a guess.
 */
export function processesRunFrom(folder: string, table: { list(): string[]; exe(pid: string): string } = procTable, resolve: Resolve = resolveReal): RunsFrom {
    let real: string;
    let pids: string[];
    try {
        real = resolve(folder);
        pids = table.list().filter((name) => /^\d+$/.test(name));
    } catch {
        return 'unread';
    }
    for (const pid of pids) {
        let exe: string;
        try {
            exe = table.exe(pid).replace(/ \(deleted\)$/, '');
        } catch {
            continue;
        }
        if (lexicallyInside('linux', real, exe) || lexicallyInside('linux', folder, exe)) return 'running';
    }
    return 'none';
}

const procTable = { list: () => readdirSync('/proc'), exe: (pid: string) => readlinkSync(`/proc/${pid}/exe`) };

/**
 * Stops the runtime when it runs as any of `executables` (`stopOwnRuntime` asked of each in turn, through jason and never
 * by a kill): the first answer that is not `another` is the answer. On an AppImage, a runtime started from any of the
 * copies of its jason is this application's.
 */
export async function stopFromAny(executables: readonly string[], stop: (executable: string) => Promise<StopOutcome>): Promise<StopOutcome> {
    let outcome: StopOutcome = { kind: 'none' };
    for (const executable of executables) {
        outcome = await stop(executable);
        if (outcome.kind !== 'another') return outcome;
    }
    return outcome;
}

/** The desktop's own login file that does not start this application: on macOS the LaunchAgent, on Linux the autostart entry. */
export interface LoginElsewhere {
    file: string;
    /** What it starts, as written there, or null when it names nothing. */
    starts: string | null;
    /** A Linux entry that is turned off (`Hidden=true`). */
    hidden: boolean;
}

export interface UninstallDeps {
    platform: NodeJS.Platform;
    /** The folder the PATH command goes in (`commandFolder`), or null where none is offered. */
    folder: string | null;
    command: { status(): CommandStatus; remove(): CommandStatus };
    /** Open Jason at login; `elsewhere` reads the desktop's own file when it starts another path (`LoginItem.elsewhere`). */
    login: { isOn(): boolean; set(on: boolean): Promise<void>; elsewhere?(): LoginElsewhere | null };
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
    bundles: { folder: string; runsFrom(): RunsFrom } | null;
    /**
     * The carried jason by its full path, which every hand act names. On Windows the uninstaller removes it next, so
     * there a hand act after the runtime's own names the registration, or the jason the runtime was started from.
     */
    jason: string;
    dataDirectory: string;
    harnessRoot: string;
    updaterCache: string;
    /**
     * The demo's two folders and the stop of the demo's runtime (`stopDemoRuntime`), which step 6 takes off; absent where
     * the start knows no demo. Never the person's own: the demo's data directory is the person's with `-demo` after it.
     */
    demo?: { data: string; harnessRoot: string; stop(): Promise<StopOutcome> };
    log: Logger;
    /** Resolves a path (`resolved-path.ts`); the system's own unless a test says otherwise. */
    resolve?: Resolve;
    /**
     * The `jason-desktop` link scheme's registration, on Windows, where the application wrote it at each start
     * (`deep-link.ts`): whether it names this application, and taking it off. Absent elsewhere. The runtime's `jason`
     * scheme is never touched.
     */
    link?: { registered(): boolean; remove(): boolean };
}

/** The steps, in the order they are taken, as the log names them. */
export type RemovalStep = 'runtime' | 'login' | 'autostart' | 'command' | 'link' | 'bundle' | 'demo';

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
    /** The carried jason, by its full path, which a hand act names. */
    jason: string;
}

/** How a hand act names the carried jason: its full path, quoted when it holds a space, made inert. */
export function handJason(jason: string | null): string {
    if (jason === null) return 'jason';
    const shown = inert(jason);
    return /\s/.test(shown) ? `"${shown}"` : shown;
}

/**
 * What the person is told when the runtime this application carries did not stop, or did not answer, so nothing was
 * removed; `again` is the act to repeat once it has, and `jason` the carried jason the hand acts name.
 */
export function runtimeRefusal(outcome: { kind: 'failed'; exit: number } | Unanswered, again = 'uninstall Jason again', jason: string | null = null): string {
    const hand = handJason(jason);
    if (outcome.kind === 'unanswered') {
        const { why, next } = unansweredWords({ ...outcome, runFile: inert(outcome.runFile) }, hand);
        return `Nothing was removed, because ${why}. ${next} Then ${again}.`;
    }
    const how = outcome.exit === -1 ? 'jason runtime stop did not finish' : `jason runtime stop exited ${outcome.exit}`;
    return `Nothing was removed, because the runtime this application carries is running and did not stop (${how}). Stop it with ${hand} runtime stop, then ${again}.`;
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

/** Where the Windows start at logon is, for a hand act once the uninstaller has removed the jason that would take it off. */
const windowsTask = 'Task Scheduler holds it as the task named Jason- followed by your account’s SID; deleting that task takes it off';

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
    const resolve = deps.resolve ?? resolveReal;
    const hand = handJason(deps.jason);
    // On Windows the uninstaller removes the carried jason once this start exits: a hand act cannot run it.
    const goes = deps.platform === 'win32';
    const data = inert(deps.dataDirectory);
    // Whether a line above names the carried jason for the person to run by hand: its copies then stay.
    let handNeedsJason = false;

    // 1. The runtime this application's jason runs as; one that does not stop ends it here.
    const running: StopOutcome = deps.runtime === null ? { kind: 'none' } : await deps.runtime.stop().catch((): StopOutcome => ({ kind: 'failed', exit: -1 }));
    ended('runtime', deps.runtime === null ? 'no_cli' : running.kind);
    if (running.kind === 'failed' || running.kind === 'unanswered') {
        return { removed: [], untouched: [], failed: [], left: [], refused: runtimeRefusal(running, undefined, deps.jason), runtime: running, steps, jason: deps.jason };
    }
    if (deps.runtime === null) left.push('The runtime and its start at logon, which were not looked at, since this build carries no jason');
    else if (running.kind === 'stopped') removed.push('The runtime this application carries, which was running: it was stopped with jason runtime stop');
    else if (running.kind === 'none') untouched.push(`No runtime was running with the data directory ${data}`);

    // 2. Open Jason at login; the desktop's own file that starts another path stays, and is named.
    try {
        if (deps.login.isOn()) {
            await deps.login.set(false);
            removed.push('Open Jason at login');
            ended('login', 'off');
        } else {
            const there = deps.login.elsewhere?.() ?? null;
            if (there === null) {
                untouched.push('Open Jason at login was off');
                ended('login', 'was_off');
            } else {
                const what = there.hidden ? 'which is turned off (Hidden=true)' : `which starts ${there.starts === null ? 'nothing it names' : inert(there.starts)}, not this application`;
                left.push(`${inert(there.file)}, under Jason’s name, ${what}: deleting it takes it off`);
                ended('login', 'another');
            }
        }
    } catch {
        failed.push('Open Jason at login, which could not be turned off: the system’s list of apps that open at login holds it');
        ended('login', 'failed');
    }

    // 3. The runtime's start at logon, only when it starts this application's jason, decided on resolved paths.
    const ownership = (executable: string): 'inside' | 'outside' | 'unknown' => {
        const folders = [deps.application, ...(deps.bundles === null ? [] : [deps.bundles.folder])];
        const answers = folders.map((folder) => containedIn(deps.platform, folder, executable, resolve));
        if (answers.includes('inside')) return 'inside';
        return answers.includes('unknown') ? 'unknown' : 'outside';
    };
    let registration: { registered: boolean; executable: string | null } | null = null;
    let unread = false;
    try {
        registration = deps.autostart === null ? null : await deps.autostart.status();
        unread = deps.autostart !== null && registration === null;
    } catch {
        unread = true;
    }
    if (unread) {
        failed.push(`The runtime’s start at logon, which could not be read: ${goes ? windowsTask : `${hand} runtime autostart status says whether it is there`}`);
        handNeedsJason = true;
        ended('autostart', 'unread');
    } else if (registration?.registered === true) {
        const executable = registration.executable;
        const owned = executable === null ? 'outside' : ownership(executable);
        const named = executable === null ? 'an executable it does not name' : inert(executable);
        if (owned === 'inside') {
            const still = await deps.autostart?.disable().catch(() => null);
            if (still === false) {
                removed.push('The runtime’s start at logon');
                ended('autostart', 'off');
            } else {
                failed.push(`The runtime’s start at logon, which could not be taken off: ${goes ? windowsTask : `${hand} runtime autostart disable takes it off`}`);
                handNeedsJason = true;
                ended('autostart', 'failed');
            }
        } else if (owned === 'unknown') {
            left.push(`The runtime’s start at logon, which starts ${named}: whether that is this application’s could not be told, so it was left; ${goes ? windowsTask : `${hand} runtime autostart disable takes it off`}`);
            handNeedsJason = true;
            ended('autostart', 'unknown');
        } else {
            left.push(`The runtime’s start at logon, which starts ${named}, not this application’s`);
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
                removed.push(inert(file));
                took += 1;
            } else if (threw) failed.push(`${inert(file)}, which could not be taken off: deleting the file takes it off`);
            else left.push(`${inert(file)}, which another program wrote`);
        }
        if (before.length === 0) untouched.push('No jason command of this application’s was on the PATH');
        ended('command', threw ? 'failed' : took > 0 ? 'removed' : before.length > 0 ? 'another' : 'was_off');
    }

    // 4b. The jason-desktop link scheme's registration, when it names this application; one that names another is left.
    if (deps.link !== undefined) {
        let outcome: string;
        try {
            if (!deps.link.registered()) {
                untouched.push('jason-desktop links were not registered to this application');
                outcome = 'was_off';
            } else if (deps.link.remove() && !deps.link.registered()) {
                removed.push('The registration of jason-desktop links to this application');
                outcome = 'removed';
            } else {
                failed.push('The registration of jason-desktop links to this application, which could not be taken off: deleting HKEY_CURRENT_USER\\Software\\Classes\\jason-desktop takes it off');
                outcome = 'failed';
            }
        } catch {
            failed.push('The registration of jason-desktop links to this application, which could not be read: deleting HKEY_CURRENT_USER\\Software\\Classes\\jason-desktop takes it off');
            outcome = 'failed';
        }
        ended('link', outcome);
    }

    // The runtime another installation runs is said with the carried jason that stops it.
    if (running.kind === 'another') {
        left.push(`A runtime is running that this application could not confirm as its own: it was not stopped; ${goes ? 'the jason it was started from stops it with runtime stop' : `${hand} runtime stop stops it`}`);
        handNeedsJason = true;
    }

    // 5. An AppImage's copies of its jason: the real folders named as versions, when nothing runs from them.
    if (deps.bundles !== null) bundleStep(deps.bundles, { removed, failed, left, ended, handNeedsJason, jasonInCopies: containedIn(deps.platform, deps.bundles.folder, deps.jason, resolve) === 'inside', hand });

    if (deps.demo !== undefined) await demoStep(deps, deps.demo, { removed, untouched, failed, left, ended });

    left.push(`The data directory, ${data}, with the campaigns, the journal, the settings and the logs`);
    left.push(`The harness root, ${inert(deps.harnessRoot)}, with your folders and memory files`);
    left.push(`The updater’s cache, ${inert(deps.updaterCache)}`);
    return { removed, untouched, failed, left, refused: null, runtime: running, steps, jason: deps.jason };
}

type DemoKind = 'absent' | 'folder' | 'other' | 'unread';

/** What a demo folder is on the disk, a link looked at and never followed. */
function demoKind(path: string): DemoKind {
    try {
        const found = lstatSync(path);
        return found.isDirectory() && !found.isSymbolicLink() ? 'folder' : 'other';
    } catch (error) {
        return (error as NodeJS.ErrnoException).code === 'ENOENT' ? 'absent' : 'unread';
    }
}

/** Whether two paths are one, or either holds the other, as written. */
function overlaps(platform: NodeJS.Platform, a: string, b: string): boolean {
    const paths = platform === 'win32' ? win32 : posix;
    return paths.relative(a, b) === '' || inside(platform, a, b) || inside(platform, b, a);
}

/** Step 6: the demo's data directory and harness root, when the demo's runtime is not running from them. */
async function demoStep(
    deps: UninstallDeps,
    demo: NonNullable<UninstallDeps['demo']>,
    into: { removed: string[]; untouched: string[]; failed: string[]; left: string[]; ended(step: RemovalStep, outcome: string): void },
): Promise<void> {
    const folders = [
        { what: 'The demo’s data directory', has: 'with the sample campaigns in it', path: demo.data },
        { what: 'The demo’s harness root', has: 'with the sample folders in it', path: demo.harnessRoot },
    ].map((one) => ({ ...one, kind: demoKind(one.path), shown: inert(one.path) }));
    if (folders.every((one) => one.kind === 'absent')) {
        into.untouched.push('No demo was set up: the demo’s data directory and harness root were not there');
        return into.ended('demo', 'absent');
    }
    const person = [deps.dataDirectory, deps.harnessRoot, deps.application];
    const mine = folders.map((one) => ({ ...one, own: person.some((other) => overlaps(deps.platform, other, one.path)) || overlaps(deps.platform, demo.data, demo.harnessRoot) }));
    // The outcome logged is the first of these that any folder came to.
    const order = ['unsafe', 'not_folder', 'unread', 'failed', 'removed'];
    const came = new Set<string>(['removed']);
    const takeable = mine.filter((one) => one.kind === 'folder' && !one.own);
    for (const one of mine) {
        if (one.kind === 'absent') into.untouched.push(`${one.what} was not there`);
        else if (one.own) {
            into.left.push(`${one.what}, ${one.shown}, which is, holds or lies inside a folder of your own installation: it was left`);
            came.add('unsafe');
        } else if (one.kind === 'other') {
            into.left.push(`${one.shown}, which is not a folder this application made (a link or a file): it was left`);
            came.add('not_folder');
        } else if (one.kind === 'unread') {
            into.failed.push(`${one.what}, ${one.shown}, which could not be read: deleting the folder takes it off`);
            came.add('unread');
        }
    }
    if (takeable.length > 0) {
        const stopped: StopOutcome = await demo.stop().catch((): StopOutcome => ({ kind: 'failed', exit: -1 }));
        if (stopped.kind !== 'none' && stopped.kind !== 'stopped') {
            for (const one of takeable) {
                into.failed.push(`${one.what}, ${one.shown}, which was kept because the demo’s runtime did not stop: once it has stopped, deleting the folder takes it off`);
            }
            return into.ended('demo', 'runtime_running');
        }
        for (const one of takeable) {
            try {
                rmSync(one.path, { recursive: true, force: true });
                into.removed.push(`${one.what}, ${one.shown}, ${one.has}`);
            } catch {
                into.failed.push(`${one.what}, ${one.shown}, which could not be taken off: deleting the folder takes it off`);
                came.add('failed');
            }
        }
    }
    into.ended('demo', order.find((name) => came.has(name)) ?? 'removed');
}

/** Step 5: what of the bundles folder is this application's copies, and only then, and only those, removed. */
function bundleStep(
    bundles: { folder: string; runsFrom(): RunsFrom },
    into: { removed: string[]; failed: string[]; left: string[]; ended(step: RemovalStep, outcome: string): void; handNeedsJason: boolean; jasonInCopies: boolean; hand: string },
): void {
    const folder = bundles.folder;
    const shown = inert(folder);
    const entries = bundleEntries(folder);
    if (entries.kind === 'absent') return into.ended('bundle', 'absent');
    if (entries.kind === 'other') {
        into.left.push(`${shown}, which is not a folder this application made (a link or a file): it was left`);
        return into.ended('bundle', 'not_folder');
    }
    if (entries.kind === 'unread') {
        into.failed.push(`The copies of jason in ${shown}, which could not be read: once nothing runs from them, deleting the folders named as versions in it takes them off`);
        return into.ended('bundle', 'unread');
    }
    for (const name of entries.others) into.left.push(`${inert(join(folder, name))}, which is not a copy of jason this application made: it was left`);
    const ours = [...entries.copies, ...entries.temporaries];
    if (ours.length === 0) return into.ended('bundle', 'none');
    if (into.handNeedsJason && into.jasonInCopies) {
        into.left.push(`The copies of jason in ${shown}, kept because a hand act above runs ${into.hand}: once it is done and nothing runs from them, deleting the folders named as versions in it takes them off`);
        return into.ended('bundle', 'kept');
    }
    const runs = bundles.runsFrom();
    if (runs !== 'none') {
        const why = runs === 'running' ? 'which a process still runs from' : 'since whether a process runs from them could not be read';
        into.failed.push(`The copies of jason in ${shown}, ${why}: once nothing runs from them, deleting the folders named as versions in it takes them off`);
        return into.ended('bundle', runs);
    }
    let failed = false;
    for (const name of ours) {
        const copy = join(folder, name);
        try {
            rmSync(copy, { recursive: true, force: true });
            into.removed.push(`The copy of jason in ${inert(copy)}`);
        } catch {
            failed = true;
            into.failed.push(`The copy of jason in ${inert(copy)}, which could not be taken off: deleting the folder takes it off`);
        }
    }
    if (!failed && entries.others.length === 0) {
        try {
            if (readdirSync(folder).length === 0) {
                rmdirSync(folder);
                into.removed.push(`The folder that held them, ${shown}`);
            }
        } catch {
            // Left as it is: it holds nothing of this application's any more.
        }
    }
    into.ended('bundle', failed ? 'failed' : 'removed');
}

/** What the uninstall shows the person: what could not be taken off is said first among what is left. */
export function uninstallText(report: UninstallReport): { message: string; detail: string } {
    if (report.refused !== null) return { message: 'Jason was not removed.', detail: report.refused };
    const list = (lines: readonly string[]) => lines.map((line) => `• ${line}`).join('\n');
    const taken = report.removed.length === 0 ? 'Nothing was taken off.' : `Taken off:\n${list(report.removed)}`;
    return { message: 'Jason is being removed from this computer.', detail: `${taken}\n\nLeft in place:\n${list([...report.failed, ...report.left])}` };
}
