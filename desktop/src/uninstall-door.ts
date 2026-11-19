/**
 * Uninstall Jason…, the macOS and Linux menu item: the one uninstall, reached from the Jason menu and the tray's. A dialog says what
 * goes and what stays and asks whether the data directory goes too, Cancel the default; on the person's word the jason
 * this application carries is started on its own, `jason uninstall [--purge-data --yes] --wait-for <this pid>`, and the
 * application quits. The verb waits for this process to end, so nothing of the application runs or writes into the data
 * directory while it works; it then takes off what Jason and Jason Desktop placed and the application: Jason.app last on
 * macOS; on Linux the AppImage file last, or for the deb its package, first, through the system's own password dialog.
 * What it did is written to a file in the temporary folder, which the dialog names. Windows keeps Installed apps.
 */

import { join } from 'node:path';
import { inert } from './inert.ts';
import type { Logger } from './log.ts';

/** The menu item. */
export const uninstallLabel = 'Uninstall Jason…';

/** Whether the item is offered: on macOS and Linux, where no uninstaller of the application's own exists. */
export function uninstallOffered(platform: NodeJS.Platform): boolean {
    return platform === 'darwin' || platform === 'linux';
}

export interface UninstallScope {
    /** The application: Jason.app, the AppImage file, or the deb's /opt/Jason. */
    application: string;
    /** On Linux, which package the application is; none on macOS. */
    form?: 'deb' | 'appimage';
    dataDirectory: string;
    harnessRoot: string;
    /** The file the uninstall writes what it did to. */
    output: string;
}

export interface UninstallQuestion {
    message: string;
    detail: string;
    buttons: string[];
    defaultId: number;
    cancelId: number;
    checkboxLabel: string;
    checkboxChecked: boolean;
}

/** The file what the uninstall did is written to: in the temporary folder, by the moment it was asked. */
export function uninstallOutput(temporary: string, now: Date): string {
    return join(temporary, `jason-uninstall-${now.toISOString().replace(/[:.]/g, '-')}.json`);
}

/** The dialog: what goes, what stays, and the data directory's choice, off unless the person turns it on. */
export function uninstallQuestion(scope: UninstallScope): UninstallQuestion {
    const list = (lines: readonly string[]) => lines.map((line) => `• ${line}`).join('\n');
    const goes = [
        'The runtime, stopped first; nothing else is taken off if it does not stop',
        'Open Jason at Login and the runtime’s start at logon',
        'The jason command in the terminal, and the skills Jason put in your agents’ folders',
        scope.form === 'deb'
            ? 'Jason itself, the package jason-desktop, taken off by your system first: it asks for an administrator’s password'
            : `Jason itself, ${inert(scope.application)}, last`,
    ];
    const stays = [
        `The data directory, ${inert(scope.dataDirectory)}, with the campaigns, the journal, the settings and the logs, unless you tick the box below`,
        `The harness root, ${inert(scope.harnessRoot)}, with your folders and memory files, always`,
    ];
    return {
        message: scope.form === undefined ? 'Uninstall Jason from this Mac?' : 'Uninstall Jason from this computer?',
        detail: `Jason quits, then this is taken off:\n${list(goes)}\n\nThis stays:\n${list(stays)}\n\nWhat was done is written to ${inert(scope.output)}.`,
        buttons: ['Uninstall', 'Cancel'],
        defaultId: 1,
        cancelId: 1,
        checkboxLabel: 'Also delete the data directory',
        checkboxChecked: false,
    };
}

/** The verb as the item starts it: the data directory only on the box, and the wait for this process always. */
export function uninstallArgs(purge: boolean, pid: number): string[] {
    return ['uninstall', ...(purge ? ['--purge-data', '--yes'] : []), '--wait-for', String(pid)];
}

export interface UninstallDoorDeps {
    scope(): UninstallScope;
    /** Shows the dialog: the button chosen, and whether the box was ticked. */
    ask(question: UninstallQuestion): Promise<{ response: number; checkboxChecked: boolean }>;
    /** Starts the carried jason on its own with these arguments, its output to the file; it outlives this process. */
    start(args: string[], output: string): void;
    /** This process, which the verb waits for. */
    pid: number;
    quit(): void;
    log: Logger;
}

/** The item: asks, starts the uninstall on the person's word alone, and quits so it can begin. */
export async function uninstallJason(deps: UninstallDoorDeps): Promise<'cancelled' | 'started'> {
    const scope = deps.scope();
    const question = uninstallQuestion(scope);
    const answer = await deps.ask(question);
    const confirmed = answer.response !== question.cancelId && question.buttons[answer.response] === 'Uninstall';
    deps.log.info('uninstall_asked', { Answer: confirmed ? (answer.checkboxChecked ? 'uninstall_and_data' : 'uninstall') : 'cancel' });
    if (!confirmed) return 'cancelled';
    deps.start(uninstallArgs(answer.checkboxChecked, deps.pid), scope.output);
    deps.quit();
    return 'started';
}
