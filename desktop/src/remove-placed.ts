/**
 * Remove What Jason Placed: the menu act that takes off, on macOS and on Linux, what the desktop placed outside the
 * application, before the person removes the application itself. No installer starts the application there to clean
 * up, as the Windows uninstaller does with `--uninstall`, so the person asks for it in the Jason menu (and the tray's).
 * The steps are the uninstall's own (`uninstall.ts`): stop the runtime this desktop's jason runs as, going no further
 * when it does not stop; turn off Open Jason at Login; turn off the runtime's start at logon when it starts this
 * application's jason; remove the PATH command's files that are its own; on an AppImage, remove the copies of its jason.
 *
 * A confirmation comes first, naming what will be taken off and what stays, with Cancel the default button; Cancel
 * changes nothing. The closing dialog says each step's outcome, names a step that failed with what the person can do by
 * hand, and says what removes the application itself: moving Jason.app to the Trash, `sudo apt remove` of the deb, or
 * deleting the AppImage file; and it offers to quit. Windows keeps its uninstaller, and the act is not offered there.
 */

import type { Logger } from './log.ts';
import type { LinuxPackage } from './linux-package.ts';
import type { CommandStatus } from './path-command.ts';
import { applicationFolder } from './rollback.ts';
import { runtimeRefusal, type UninstallReport } from './uninstall.ts';

/** The menu item. */
export const removeLabel = 'Remove What Jason Placed…';

/** The name dpkg knows the deb by: the package's own name, which electron-builder gives the deb (`linuxPackageName`). */
export const debPackage = 'jason-desktop';

/** Whether the act is offered: on macOS and Linux, where no uninstaller starts the application; Windows keeps its own. */
export function removalOffered(platform: NodeJS.Platform): boolean {
    return platform === 'darwin' || platform === 'linux';
}

/** What removes the application itself, once what it placed is off: per platform and Linux package. */
export function removeApplication(platform: NodeJS.Platform, form: LinuxPackage | null, executable: string): string {
    if (platform === 'darwin') return `move ${applicationFolder(platform, executable)} to the Trash`;
    if (form?.form === 'deb') return `run sudo apt remove ${debPackage}`;
    if (form?.form === 'appimage') return `delete the AppImage file, ${form.file}`;
    return `delete its folder, ${applicationFolder(platform, executable)}`;
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
    const command = scope.command.state === 'placed' || scope.command.state === 'elsewhere' ? `The ${scope.command.name} command on the PATH: ${scope.command.files.join(' and ')}` : 'The jason command on the PATH, if this application placed it';
    const off = [
        'The runtime this application carries, if it is running: it is stopped with jason runtime stop, and nothing else is taken off if it does not stop',
        'Open Jason at Login',
        'The runtime’s start at logon, if it starts this application’s jason',
        command,
        ...(scope.bundles === null ? [] : [`The copies of jason in ${scope.bundles}, if nothing runs from them`]),
    ];
    const stays = [
        `The data directory, ${scope.dataDirectory}, with the campaigns, the journal, the settings and the logs`,
        `The harness root, ${scope.harnessRoot}, with your folders and memory files`,
        `The updater’s cache, ${scope.updaterCache}`,
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
        const detail = runtime.kind === 'failed' || runtime.kind === 'unanswered' ? runtimeRefusal(runtime, 'choose Remove What Jason Placed again') : report.refused;
        return { message: 'Nothing was taken off.', detail, buttons: ['OK'], defaultId: 0, cancelId: 0, quitId: null };
    }
    const parts: string[] = [];
    if (report.removed.length > 0) parts.push(`Taken off:\n${list(report.removed)}`);
    if (report.untouched.length > 0) parts.push(`Nothing to take off:\n${list(report.untouched)}`);
    if (report.failed.length > 0) parts.push(`Could not be taken off, and what does it by hand:\n${list(report.failed)}`);
    parts.push(`Left in place:\n${list(report.left)}`);
    parts.push(`To finish, quit Jason, then ${next}.`);
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
