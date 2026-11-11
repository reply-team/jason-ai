/**
 * Remove What Jason Placed: the menu act that takes off, on macOS and on Linux, what the desktop placed outside the
 * application, before the person removes the application itself. No installer starts the application there to clean
 * up, as the Windows uninstaller does with `--uninstall`, so the person asks for it in the Jason menu (and the tray's).
 * The steps are the uninstall's own, in `uninstall.ts`: stop the runtime this desktop's jason runs as (on an AppImage,
 * any copy of its jason), going no further when it does not stop; turn off Open Jason at Login; turn off the runtime's
 * start at logon when it starts this application's jason; remove the PATH command's files that are its own; on an
 * AppImage, remove the copies of its jason when nothing runs from them. This module holds the dialogs and the wiring.
 *
 * A confirmation comes first, naming what will be taken off and what stays, with Cancel the default button; Cancel
 * changes nothing. The closing dialog says each step's outcome, names a step that failed with what the person can do by
 * hand, and says what removes the application itself: moving Jason.app to the Trash, `sudo apt remove` of the deb, or
 * deleting the AppImage file; and it offers to quit. Every value shown that a person or another program wrote is made
 * inert (`inert.ts`). Windows keeps its uninstaller, and the act is not offered there.
 */

import { join } from 'node:path';
import { bundleEntries, copiesBundle } from './bundle-copy.ts';
import { inert } from './inert.ts';
import type { LinuxPackage } from './linux-package.ts';
import type { Logger } from './log.ts';
import type { StopOutcome } from './own-runtime.ts';
import { commandFolder, type CommandStatus } from './path-command.ts';
import type { DesktopPaths } from './paths.ts';
import { applicationFolder } from './rollback.ts';
import { harnessRoot, runtimeRefusal, stopFromAny, uninstall, updaterCache, type RunsFrom, type UninstallDeps, type UninstallReport } from './uninstall.ts';

/** The menu item. */
export const removeLabel = 'Remove What Jason Placed…';

/** The name dpkg knows the deb by: the package's own name, which electron-builder gives the deb (`linuxPackageName`). */
export const debPackage = 'jason-desktop';

/** Whether the act is offered: on macOS and Linux, where no uninstaller starts the application; Windows keeps its own. */
export function removalOffered(platform: NodeJS.Platform): boolean {
    return platform === 'darwin' || platform === 'linux';
}

/**
 * What removes the application itself, once what it placed is off: per platform and Linux package. A Jason.app macOS
 * runs from a folder of its own (App Translocation, for an app opened where it was downloaded) is named without that
 * path, which is not where the person put it.
 */
export function removeApplication(platform: NodeJS.Platform, form: LinuxPackage | null, executable: string): string {
    if (platform === 'darwin') {
        if (executable.includes('/AppTranslocation/')) return 'move Jason.app to the Trash from the folder you opened it from';
        return `move ${inert(applicationFolder(platform, executable))} to the Trash`;
    }
    if (form?.form === 'deb') return `run sudo apt remove ${debPackage}`;
    if (form?.form === 'appimage') return `delete the AppImage file, ${inert(form.file)}`;
    return `delete its folder, ${inert(applicationFolder(platform, executable))}`;
}

export interface RemovalScope {
    /** The PATH command as it stands, whose files are named when there are any of this application's. */
    command: CommandStatus;
    /** An AppImage's copies of its jason, `<data>/desktop/bundle/`; null for every other form. */
    bundles: string | null;
    dataDirectory: string;
    harnessRoot: string;
    updaterCache: string;
    /** `removeApplication`'s words. */
    next: string;
}

export interface Question {
    message: string;
    detail: string;
    buttons: string[];
    defaultId: number;
    cancelId: number;
}

const list = (lines: readonly string[]) => lines.map((line) => `• ${line}`).join('\n');

/** The confirmation: what will be taken off and what stays, with Cancel the default button. */
export function removalQuestion(scope: RemovalScope): Question {
    const command =
        scope.command.state === 'placed' || scope.command.state === 'elsewhere'
            ? `The ${scope.command.name} command on the PATH: ${scope.command.files.map((file) => inert(file)).join(' and ')}`
            : 'The jason command on the PATH, if this application placed it';
    const off = [
        'The runtime this application carries, if it is running: it is stopped with jason runtime stop, and nothing else is taken off if it does not stop',
        'Open Jason at Login',
        'The runtime’s start at logon, if it starts this application’s jason',
        command,
        ...(scope.bundles === null ? [] : [`The copies of jason in ${inert(scope.bundles)}, if nothing runs from them`]),
    ];
    const stays = [
        `The data directory, ${inert(scope.dataDirectory)}, with the campaigns, the journal, the settings and the logs`,
        `The harness root, ${inert(scope.harnessRoot)}, with your folders and memory files`,
        `The updater’s cache, ${inert(scope.updaterCache)}`,
    ];
    return {
        message: 'Remove what Jason placed on this computer?',
        detail: `This takes off:\n${list(off)}\n\nThis leaves:\n${list(stays)}\n\nJason itself stays until you quit it and ${scope.next}.`,
        buttons: ['Remove', 'Cancel'],
        defaultId: 1,
        cancelId: 1,
    };
}

export interface Closing {
    message: string;
    detail: string;
    buttons: string[];
    defaultId: number;
    cancelId: number;
    /** The button that quits Jason, or null when none is offered. */
    quitId: number | null;
}

/** What the closing dialog says: each step's outcome, what failed with its hand act, and what removes the application. */
export function removalText(report: UninstallReport, next: string): Closing {
    if (report.refused !== null) {
        const runtime = report.runtime;
        const detail = runtime.kind === 'failed' || runtime.kind === 'unanswered' ? runtimeRefusal(runtime, 'choose Remove What Jason Placed again', report.jason) : report.refused;
        return { message: 'Nothing was taken off.', detail, buttons: ['OK'], defaultId: 0, cancelId: 0, quitId: null };
    }
    const parts: string[] = [];
    if (report.removed.length > 0) parts.push(`Taken off:\n${list(report.removed)}`);
    if (report.untouched.length > 0) parts.push(`Nothing to take off:\n${list(report.untouched)}`);
    if (report.failed.length > 0) parts.push(`Could not be taken off, and what does it by hand:\n${list(report.failed)}`);
    parts.push(`Left in place:\n${list(report.left)}`);
    // On an AppImage the jason this start runs went with its copies: nothing in Jason works on but quitting.
    const copiesGone = report.steps.some((one) => one.step === 'bundle' && one.outcome === 'removed');
    parts.push(`${copiesGone ? 'The jason this Jason ran went with its copies, so it does not work on until you quit it. ' : ''}To finish, quit Jason, then ${next}.`);
    const message = report.failed.length === 0 ? 'Jason took off what it placed.' : 'Jason took off what it could; some of it is left.';
    return { message, detail: parts.join('\n\n'), buttons: ['Quit Jason', 'Not Now'], defaultId: 0, cancelId: 1, quitId: 0 };
}

export interface RemovalDeps {
    /** What the confirmation names, read as things stand when the act is chosen. */
    scope(): RemovalScope;
    /** Shows the confirmation; the index of the button chosen. */
    ask(question: Question): Promise<number>;
    /** Takes the steps (`uninstall`). */
    remove(): Promise<UninstallReport>;
    /** Shows the closing dialog; the index of the button chosen. */
    tell(closing: Closing): Promise<number>;
    quit(): void;
    log: Logger;
}

/** The act: asks, takes the steps on the person's word alone, says what came of each, and quits when asked to. */
export async function removePlaced(deps: RemovalDeps): Promise<UninstallReport | null> {
    const scope = deps.scope();
    const question = removalQuestion(scope);
    const answer = await deps.ask(question);
    const confirmed = answer !== question.cancelId && question.buttons[answer] === 'Remove';
    deps.log.info('removal_asked', { Answer: confirmed ? 'remove' : 'cancel' });
    if (!confirmed) return null;
    const report = await deps.remove();
    const closing = removalText(report, scope.next);
    const chosen = await deps.tell(closing);
    if (closing.quitId !== null && chosen === closing.quitId) deps.quit();
    return report;
}

/**
 * The act as the menu holds it: one run at a time, a second choice while one runs doing nothing, and `changed` told when
 * a run begins and ends, so the menu turns the installation's acts off while it runs and reads their state again after.
 */
export class RemovalAct {
    private readonly deps: RemovalDeps & { changed(): void };
    private running = false;

    constructor(deps: RemovalDeps & { changed(): void }) {
        this.deps = deps;
    }

    get busy(): boolean {
        return this.running;
    }

    /** Runs the act; `busy` when one already runs. */
    async run(): Promise<UninstallReport | null | 'busy'> {
        if (this.running) return 'busy';
        this.running = true;
        this.deps.changed();
        try {
            return await removePlaced(this.deps);
        } finally {
            this.running = false;
            this.deps.changed();
        }
    }
}

/** The PATH command and the start at logon as the act uses them. */
type CommandLike = UninstallDeps['command'];
interface AutostartLike {
    registration(): Promise<{ registered: boolean; executable: string | null } | null>;
    set(on: boolean): Promise<boolean | null>;
}

export interface PlacedInput {
    platform: NodeJS.Platform;
    home: string;
    env: Readonly<Record<string, string | undefined>>;
    form: LinuxPackage | null;
    /** The application's own executable (`process.execPath`). */
    execPath: string;
    /** The jason this start carries, by its full path. */
    executable: string;
    /** The bundle's version; null for a build that carries no jason. */
    version: string | null;
    paths: DesktopPaths;
    /** The menu's PATH command and start at logon, or null where this start offers none. */
    command: CommandLike | null;
    autostart: AutostartLike | null;
    /** Make them for the act alone where the start offers none: they only read, remove and disable. */
    makeCommand(): CommandLike;
    makeAutostart(): AutostartLike;
    login: UninstallDeps['login'];
    /** `stopOwnRuntime` asked as if `executable` were the carried jason. */
    stopAs(executable: string): Promise<StopOutcome>;
    /** `processesRunFrom`. */
    runsFrom(folder: string): RunsFrom;
    log: Logger;
}

/**
 * The act's two halves over this start's pieces. A start that offers neither the PATH command nor the start at logon (an
 * AppImage whose copy was not made) still takes off what an earlier start placed, through ones made for the act; and on
 * an AppImage the runtime is looked for as started from this start's jason first, then from each copy of it.
 */
export function placedRemoval(input: PlacedInput): Pick<RemovalDeps, 'scope' | 'remove'> {
    const bundles = copiesBundle(input.form) ? input.paths.bundle : null;
    const command = input.command ?? input.makeCommand();
    const autostart = input.autostart ?? (input.version === null ? null : input.makeAutostart());
    const root = () => harnessRoot(input.env, input.home, input.paths.installationSettings);
    const cache = () => updaterCache(input.env, input.home, input.platform);
    const candidates = (): string[] => {
        const entries = bundles === null ? null : bundleEntries(bundles);
        const copies = entries?.kind === 'folder' ? entries.copies.map((name) => join(bundles ?? '', name, 'jason')) : [];
        return [input.executable, ...copies.filter((copy) => copy !== input.executable)];
    };
    return {
        scope: () => ({
            command: command.status(),
            bundles,
            dataDirectory: input.paths.data,
            harnessRoot: root(),
            updaterCache: cache(),
            next: removeApplication(input.platform, input.form, input.execPath),
        }),
        remove: () =>
            uninstall({
                platform: input.platform,
                folder: commandFolder(input.platform, input.env, input.home),
                command,
                login: input.login,
                runtime: input.version === null ? null : { stop: () => stopFromAny(candidates(), (executable) => input.stopAs(executable)) },
                autostart: autostart === null ? null : { status: () => autostart.registration(), disable: () => autostart.set(false) },
                application: applicationFolder(input.platform, input.execPath),
                bundles: bundles === null ? null : { folder: bundles, runsFrom: () => input.runsFrom(bundles) },
                jason: input.executable,
                dataDirectory: input.paths.data,
                harnessRoot: root(),
                updaterCache: cache(),
                log: input.log,
            }),
    };
}
