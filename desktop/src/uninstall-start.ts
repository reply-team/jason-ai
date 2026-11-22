/**
 * The starts the nsis installer and uninstaller make (`uninstall.ts`): no window and no runtime started. `--stop-runtime`
 * takes no single-instance lock, since the installer has already closed the running application; `--uninstall` holds it
 * while it runs (`main.ts`), and where another start holds it says so and goes no further (`runUninstallBusy`).
 *
 * - `--uninstall` runs the one uninstall, `jason uninstall --keep-application` of the jason this application carries,
 *   which stops the runtime and takes off what Jason and Jason Desktop placed; it shows what was taken off and what is
 *   left unless the uninstall is silent, and exits; the uninstaller then removes the application's folder, unless the
 *   exit says to go no further.
 * - `--stop-runtime` stops the runtime this application carries when that is the one running, and exits; it shows
 *   nothing and removes nothing; it writes its log lines and Electron's own profile under the data directory its
 *   environment names, as `--uninstall` does.
 */

import { dialog } from 'electron';
import { readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { cliAt } from './cli.ts';
import { loggedCli } from './installation.ts';
import { ownRuntimeDeps, stopOwnRuntime } from './own-runtime.ts';
import { fileLogger, limitsFrom } from './log.ts';
import { stopDemoRuntime, stopWithDemo } from './demo-runtime.ts';
import { dataDirectory, dataLayout, desktopPaths } from './paths.ts';
import { childEnvironment, packageFormOf } from './linux-package.ts';
import { carriedUninstallArgs, carriedUninstallExit, carriedUninstallOutcome, carriedUninstallText, carriedUninstallTimeoutMs, harnessRoot, stopRuntimeCode } from './uninstall.ts';
import { windowTitle } from './window-title.ts';

function readText(file: string): string | null {
    try {
        return readFileSync(file, 'utf8');
    } catch {
        return null;
    }
}

/** The installation both starts act on: the data directory this start was given, the carried executable and its CLI. */
function carried() {
    const home = homedir();
    const paths = desktopPaths(dataDirectory(process.env, home));
    const log = fileLogger(paths.logs, limitsFrom(readText(dataLayout(paths.data).userSettingsFile)));
    const executable = join(process.resourcesPath, 'bundle', process.platform === 'win32' ? 'jason.exe' : 'jason');
    const cli = loggedCli(cliAt(executable, childEnvironment(packageFormOf(process.platform, process.resourcesPath, process.env), process.env)), log);
    const own = ownRuntimeDeps(process.platform, executable, paths.runFile, cli);
    return { home, paths, log, executable, cli, own };
}

/** Returns the start's exit code (`stopRuntimeCode`). */
export async function runStopRuntime(): Promise<number> {
    const { paths, executable, own } = carried();
    // A demo runtime left running holds the carried jason.exe too: it is stopped with the person's. The person's is always
    // stopped with --start-owed, since this start is only ever made on an installer's behalf: the next start of the
    // application, Demo or Live, starts a runtime from the note (`docs/release-and-update.md` §9).
    return stopRuntimeCode(() => stopWithDemo(() => stopOwnRuntime(own, { startOwed: true }), () => stopDemoRuntime({ platform: process.platform, executable, person: paths.data, env: childEnvironment(packageFormOf(process.platform, process.resourcesPath, process.env), process.env) })));
}

/**
 * Returns the start's exit code (`carriedUninstallExit`): the one uninstall, run by the jason this application carries,
 * which leaves this folder to the uninstaller (`carriedUninstallArgs`).
 */
export async function runUninstall(quiet: boolean): Promise<number> {
    const { home, paths, cli } = carried();
    const outcome = carriedUninstallOutcome(await cli.run(carriedUninstallArgs, { timeoutMs: carriedUninstallTimeoutMs }));
    const shown = quiet ? null : carriedUninstallText(outcome, paths.data, harnessRoot(process.env, home, dataLayout(paths.data).userSettingsFile));
    if (shown !== null) {
        await dialog.showMessageBox({ type: outcome.kind === 'done' && outcome.problems.length === 0 ? 'info' : 'warning', title: windowTitle, message: shown.message, detail: shown.detail, buttons: ['OK'] });
    }
    return carriedUninstallExit(outcome);
}

/** The exit an uninstall start gives where another start holds the lock: go no further, as for a runtime that did not stop. */
export const uninstallBusyExit = 1;

/** What a person is told where the uninstall start could not take the lock; a silent uninstall is told nothing. */
export const uninstallBusyWords = {
    message: 'Jason Desktop is open, or an update of it is under way, so nothing was taken off.',
    detail: 'Close Jason Desktop, or let the update end, then uninstall Jason again.',
};

/** Returns `uninstallBusyExit`, having said why unless the uninstall is silent, and run nothing. */
export async function runUninstallBusy(quiet: boolean): Promise<number> {
    const { log } = carried();
    log.warn('uninstall_busy', {});
    if (!quiet) await dialog.showMessageBox({ type: 'warning', title: windowTitle, message: uninstallBusyWords.message, detail: uninstallBusyWords.detail, buttons: ['OK'] });
    return uninstallBusyExit;
}
