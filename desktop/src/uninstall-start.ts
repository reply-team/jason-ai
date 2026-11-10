/**
 * The start the nsis uninstaller makes (`uninstall.ts`): no window, no runtime started, no single-instance lock, since
 * the uninstaller has already closed the running application. It stops the runtime this application carries when that
 * is the one running, takes off what the desktop placed, shows what it took and left unless the uninstall is silent,
 * and exits; the uninstaller then removes the application's folder, unless the exit says to go no further.
 */

import { app, dialog } from 'electron';
import { readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { cliAt } from './cli.ts';
import { loggedCli } from './installation.ts';
import { LoginItem, RuntimeAutostart } from './login-item.ts';
import { ownRuntimeDeps, stopOwnRuntime } from './own-runtime.ts';
import { fileLogger, limitsFrom } from './log.ts';
import { commandFolder, isProgramFile, PathCommand } from './path-command.ts';
import { dataDirectory, desktopPaths } from './paths.ts';
import { pathOf, withPath } from './programs.ts';
import { applicationFolder } from './rollback.ts';
import { harnessRoot, uninstall, uninstallExit, uninstallText, updaterCache } from './uninstall.ts';

function readText(file: string): string | null {
    try {
        return readFileSync(file, 'utf8');
    } catch {
        return null;
    }
}

/** Returns the start's exit code (`uninstallExit`). */
export async function runUninstall(quiet: boolean): Promise<number> {
    const home = homedir();
    const paths = desktopPaths(dataDirectory(process.env, home));
    const log = fileLogger(paths.logs, limitsFrom(readText(paths.installationSettings)));
    const executable = join(process.resourcesPath, 'bundle', process.platform === 'win32' ? 'jason.exe' : 'jason');
    const cli = loggedCli(cliAt(executable, withPath(process.env, pathOf(process.env))), log);
    const folder = commandFolder(process.platform, process.env, home);
    const autostart = new RuntimeAutostart({ cli, log });
    const own = ownRuntimeDeps(process.platform, executable, () => readText(paths.runFile), cli);
    const report = await uninstall({
        platform: process.platform,
        runtime: { stop: () => stopOwnRuntime(own) },
        folder,
        command: new PathCommand({
            platform: process.platform,
            folder,
            executable,
            dataDirectory: process.env['JASON_DATA_DIR'] || null,
            path: () => process.env.PATH ?? '',
            pathext: process.env['PATHEXT'],
            isProgram: (file) => isProgramFile(file, process.platform),
            log,
        }),
        login: new LoginItem({ platform: process.platform, home, executable: process.execPath, electron: app, pathRead: Promise.resolve(), log }),
        autostart: { status: () => autostart.registration(), disable: () => autostart.set(false) },
        application: applicationFolder(process.platform, process.execPath),
        dataDirectory: paths.data,
        harnessRoot: harnessRoot(process.env, home, paths.installationSettings),
        updaterCache: updaterCache(process.env, home),
    });
    if (!quiet) {
        const { message, detail } = uninstallText(report);
        await dialog.showMessageBox({ type: report.refused === null ? 'info' : 'warning', title: 'Jason', message, detail, buttons: ['OK'] });
    }
    return uninstallExit(report);
}
