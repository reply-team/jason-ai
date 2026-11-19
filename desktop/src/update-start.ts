/**
 * The starts a terminal's `jason update apply` makes (`update-request.ts`), and the updater a request is taken by where no
 * window's is there. Neither start makes a window, starts a runtime, or is ruled by or writes the Live or Demo record (the
 * main module reads the mode file and ignores it, as for an installer's start); on macOS the main module has set the
 * activation policy to `accessory` before Electron is ready, so no Dock icon shows (`app.md`,
 * `setActivationPolicy`: "The application doesn’t appear in the Dock and doesn’t have a menu bar").
 *
 * - `--update-apply` holds the single-instance lock of its data directory (the main module took it) and takes its request
 *   to its end; its exit says what `applyExit` says, and while the installer has the update it exits as electron-updater
 *   quits it.
 * - `--update-settle` holds the same lock (the main module took it, or exited 0 where another Jason Desktop holds it,
 *   leaving the record to the next Live window's start), records a window-less update as kept (`settleByRequest`) and exits: 0 when it settled one
 *   or there was none to settle, 1 when the record could not be written.
 */

import { app } from 'electron';
import { autoUpdater } from 'electron-updater';
import { readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { cliAt } from './cli.ts';
import { stopDemoRuntime, stopWithDemo } from './demo-runtime.ts';
import { loggedCli } from './installation.ts';
import { runtimeDoor, startAgain } from './machine-setup.ts';
import { childEnvironment, keptAsideOf, packageFormOf, type LinuxPackage } from './linux-package.ts';
import { fileLogger, limitsFrom, type Logger } from './log.ts';
import { ownRuntimeDeps, stopOwnRuntime } from './own-runtime.ts';
import { dataDirectory, dataLayout, desktopPaths } from './paths.ts';
import { settleByRequest } from './rollback.ts';
import { applyExit, applyExits, busyRecord, createRequestFile, headlessLimitOf, requestFile, requestSupport, systemShell, takeRequest, usageRecord, windowOpenUpdate, type UpdateRequest, type UpdateRequests } from './update-request.ts';
import { Updater, type UpdateState } from './updates.ts';

function readText(file: string): string | null {
    try {
        return readFileSync(file, 'utf8');
    } catch {
        return null;
    }
}

/** The data directory a start a terminal made acts on, the one its environment names, and its log. */
function installation(): { data: string; log: Logger } {
    const data = dataDirectory(process.env, homedir());
    return { data, log: fileLogger(desktopPaths(data).logs, limitsFrom(readText(dataLayout(data).userSettingsFile))) };
}

export interface RequestUpdaterDeps {
    data: string;
    log: Logger;
    changed?(state: UpdateState): void;
}

/**
 * The start of the runtime the application carries, through the carried `jason`, for a request whose file could not be
 * told the updater stopped it (`TakeDeps.startRuntime`): a start refused because one answers meanwhile is one answering.
 */
export function carriedRuntimeStart(data: string, log: Logger): () => Promise<'started' | 'answering' | 'not_started'> {
    return async () => {
        const executable = carriedJason(data);
        const cli = loggedCli(cliAt(executable, childEnvironment(thisForm(), process.env)), log);
        return (await startAgain(runtimeDoor(cli, desktopPaths(data).runFile))).act;
    };
}

/** Which Linux package this application runs as (`linux-package.ts`); null off Linux. */
function thisForm(): LinuxPackage | null {
    return packageFormOf(process.platform, process.resourcesPath, process.env);
}

/**
 * The jason the application carries: in its resources, and for an AppImage the copy its starts make under the data
 * directory, `<data>/desktop/bundle/<version>/` (`bundle-copy.ts`), which is the one a runtime runs from.
 */
function carriedJason(data: string): string {
    if (thisForm()?.form === 'appimage') return join(desktopPaths(data).bundle, app.getVersion(), 'jason');
    return join(process.resourcesPath, 'bundle', process.platform === 'win32' ? 'jason.exe' : 'jason');
}

/**
 * The updater a request is taken by where no window's updater is there: a window-less start's, and a Live window's that
 * has none of its own. A window that shows the Demo makes none: it refuses every request as `demo_open` (`takeRequest`).
 * It stops the runtime the application carries as the window's updater does, and an earlier demo's left running, through
 * the carried `jason`; it shows nothing, since the request's file is the answer.
 */
export function requestUpdater(deps: RequestUpdaterDeps): Updater {
    const platform = process.platform;
    const paths = desktopPaths(deps.data);
    const executable = carriedJason(deps.data);
    const env = childEnvironment(thisForm(), process.env);
    const cli = loggedCli(cliAt(executable, env), deps.log);
    const updater = new Updater({
        updater: autoUpdater,
        version: app.getVersion(),
        application: keptAsideOf(platform, thisForm(), process.execPath),
        rollback: paths.rollback,
        log: deps.log,
        stopRuntime: () =>
            stopWithDemo(
                () => stopOwnRuntime(ownRuntimeDeps(platform, executable, paths.runFile, cli)),
                () => stopDemoRuntime({ platform, executable, person: paths.data, env }),
            ),
        refused: () => undefined,
        beforeInstall: async () => undefined,
        afterRefused: async () => undefined,
        changed: deps.changed ?? (() => undefined),
    });
    updater.prepare();
    return updater;
}

/**
 * Writes the end of a request this application refused because it was taking another, never over a file already there,
 * the one of a request under way with the same id included; a file that cannot be written is let go.
 */
export function refuseBusy(request: UpdateRequest, window: boolean): void {
    const { data } = installation();
    try {
        createRequestFile(requestFile(data, request.id), busyRecord(request, app.getVersion(), window));
    } catch {
        // The caller waits out its bound and says the request had no answer.
    }
}

/**
 * The `--update-apply` start, once it holds the lock: its request taken by an updater of its own, bounded by
 * `headlessLimitOf` (`headlessLimitMs`, and the go's wait beside it for a request that waits for one). Resolves to the
 * exit, or never while the installer has the update and electron-updater quits.
 */
export function runUpdateApply(request: UpdateRequest, requests: UpdateRequests): Promise<number> {
    const { data, log } = installation();
    // On Windows electron-updater checks the installer's publisher through a shell (`systemShell`): the system's, not the caller's.
    const shell = systemShell(process.env, process.platform);
    if (shell !== null) {
        Object.assign(process.env, shell);
        log.info('update_request_shell', { Platform: process.platform });
    }
    return new Promise((resolve) => {
        requests.serve(async (taken) => {
            const record = await takeRequest(taken, {
                data,
                from: app.getVersion(),
                where: 'headless',
                unsupported: requestSupport(app.isPackaged, process.platform, thisForm()),
                updater: () => requestUpdater({ data, log }),
                windowOpen: windowOpenUpdate,
                log,
                startRuntime: carriedRuntimeStart(data, log),
                late: () => resolve(applyExits.answered),
                limit: {
                    ms: headlessLimitOf(taken),
                    timers: { setTimeout: (run, ms) => setTimeout(run, ms), clearTimeout: (handle) => clearTimeout(handle as NodeJS.Timeout) },
                    expired: () => resolve(applyExits.failed),
                },
            });
            const code = applyExit(record);
            if (code !== null) resolve(code);
        });
        requests.arrived(request);
    });
}

/** The start whose arguments were not read: writes the refusal where the id was read and no file is there yet, and exits 2. */
export function runUpdateUsage(id: string | null, why: string): number {
    if (id !== null) {
        const { data } = installation();
        try {
            createRequestFile(requestFile(data, id), usageRecord(id, why, app.getVersion()));
        } catch {
            // The exit says it all the same.
        }
    }
    return applyExits.usage;
}

/** The `--update-settle` start: 0 when it settled the update or there was none to settle, 1 when the record could not be written. */
export function runUpdateSettle(): number {
    const { data, log } = installation();
    try {
        settleByRequest(desktopPaths(data).rollback, app.getVersion(), log);
        return 0;
    } catch {
        return 1;
    }
}
