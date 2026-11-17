/**
 * The starts a terminal's `jason update apply` makes (`update-request.ts`), and the updater a request is taken by where no
 * window's is there. Neither start makes a window, starts a runtime or reads the Live or Demo record; on macOS the main
 * module has set the activation policy to `accessory` before Electron is ready, so no Dock icon shows (`app.md`,
 * `setActivationPolicy`: "The application doesn’t appear in the Dock and doesn’t have a menu bar").
 *
 * - `--update-apply` holds the single-instance lock of its data directory (the main module took it) and takes its request
 *   to its end; its exit says what `applyExit` says, and while the installer has the update it exits as electron-updater
 *   quits it.
 * - `--update-settle` records a window-less update as kept (`settleByRequest`) and exits: 0 when it settled one or there was
 *   none to settle, 1 when the record could not be written.
 */

import { app } from 'electron';
import { autoUpdater } from 'electron-updater';
import { readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { cliAt } from './cli.ts';
import { stopDemoRuntime, stopWithDemo } from './demo-runtime.ts';
import { loggedCli } from './installation.ts';
import { keptAsideOf } from './linux-package.ts';
import { fileLogger, limitsFrom, type Logger } from './log.ts';
import { ownRuntimeDeps, stopOwnRuntime } from './own-runtime.ts';
import { dataDirectory, dataLayout, desktopPaths } from './paths.ts';
import { pathOf, withPath } from './programs.ts';
import { settleByRequest } from './rollback.ts';
import { applyExit, applyExits, busyRecord, headlessLimitMs, requestFile, requestSupport, takeRequest, usageRecord, windowOpenUpdate, writeRequestFile, type UpdateRequest, type UpdateRequests } from './update-request.ts';
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
    /** A window's: lets it go before the installer runs. A window-less start has none to let go. */
    beforeInstall?(): Promise<void>;
    /** A window's: brings back what `beforeInstall` let go when the installer stopped before it finished. */
    afterRefused?(): Promise<void>;
    changed?(state: UpdateState): void;
}

/**
 * The updater a request is taken by where no window's updater is there: a window-less start's, and the Demo's, which has
 * none of its own. It stops the runtime the application carries as the window's updater does, and an earlier demo's
 * left running, through the carried `jason`; it shows nothing, since the request's file is the answer.
 */
export function requestUpdater(deps: RequestUpdaterDeps): Updater {
    const platform = process.platform;
    const paths = desktopPaths(deps.data);
    const executable = join(process.resourcesPath, 'bundle', platform === 'win32' ? 'jason.exe' : 'jason');
    const env = withPath(process.env, pathOf(process.env));
    const cli = loggedCli(cliAt(executable, env), deps.log);
    const updater = new Updater({
        updater: autoUpdater,
        version: app.getVersion(),
        application: keptAsideOf(platform, null, process.execPath),
        rollback: paths.rollback,
        log: deps.log,
        stopRuntime: () =>
            stopWithDemo(
                () => stopOwnRuntime(ownRuntimeDeps(platform, executable, paths.runFile, cli)),
                () => stopDemoRuntime({ platform, executable, person: paths.data, env }),
            ),
        refused: () => undefined,
        beforeInstall: deps.beforeInstall ?? (async () => undefined),
        afterRefused: deps.afterRefused ?? (async () => undefined),
        changed: deps.changed ?? (() => undefined),
    });
    updater.prepare();
    return updater;
}

/** Writes the end of a request this application refused because it was taking another; a file that cannot be written is let go. */
export function refuseBusy(request: UpdateRequest, window: boolean): void {
    const { data } = installation();
    try {
        writeRequestFile(requestFile(data, request.id), busyRecord(request, app.getVersion(), window));
    } catch {
        // The caller waits out its bound and says the request had no answer.
    }
}

/**
 * The `--update-apply` start, once it holds the lock: its request taken by an updater of its own, bounded by
 * `headlessLimitMs`. Resolves to the exit, or never while the installer has the update and electron-updater quits.
 */
export function runUpdateApply(request: UpdateRequest, requests: UpdateRequests): Promise<number> {
    const { data, log } = installation();
    return new Promise((resolve) => {
        requests.serve(async (taken) => {
            const record = await takeRequest(taken, {
                data,
                from: app.getVersion(),
                where: 'headless',
                unsupported: requestSupport(app.isPackaged, process.platform),
                updater: () => requestUpdater({ data, log }),
                windowOpen: windowOpenUpdate,
                log,
                late: () => resolve(applyExits.answered),
                limit: {
                    ms: headlessLimitMs,
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

/** The start whose arguments were not read: writes the refusal where the id was read, and exits 2. */
export function runUpdateUsage(id: string | null, why: string): number {
    if (id !== null) {
        const { data } = installation();
        try {
            writeRequestFile(requestFile(data, id), usageRecord(id, why, app.getVersion()));
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
