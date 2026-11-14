/**
 * The desktop never runs without Chromium's sandbox, on any platform. Electron runs unsandboxed when its command line
 * carries --no-sandbox, and an AppImage's launcher adds that switch by itself wherever the system refuses unprivileged
 * user namespaces: electron-builder writes the launcher, AppRun, into every AppImage and no option changes it
 * (app-builder-lib 26.18.0, out/targets/appimage/appImageUtil.js, generateAppRunScript:
 * `if [ $HAVE_NO_SANDBOX -eq 0 ] && ! unshare -Ur true 2>/dev/null ; then NO_SANDBOX=(--no-sandbox)`). So a start
 * carrying the switch is refused before anything else of it is decided: no lock, no copy, no jason, no window. The
 * switch is read as Electron holds it (`app.commandLine.hasSwitch`) and in argv as well, so it is seen however it
 * arrived. An AppImage is told of the deb, whose install sets the sandbox up for the program
 * (app-builder-lib 26.18.0, templates/linux/after-install.tpl).
 */

import type { LinuxPackage } from './linux-package.ts';
import type { Logger } from './log.ts';
import { debFile } from './updates.ts';
import { windowTitle } from './window-title.ts';

/** Which form a refused start ran as, for its log line, and the sentence its dialog says. */
export interface Refusal {
    form: 'appimage' | 'deb' | 'folder' | 'none';
    message: string;
}

/** Pure: whether this start is refused, from the switch as Electron holds it, argv, and the package it runs as. */
export function sandboxRefusal(held: boolean, argv: readonly string[], form: LinuxPackage | null): Refusal | null {
    const carried = held || argv.some((arg) => arg === '--no-sandbox' || arg.startsWith('--no-sandbox='));
    if (!carried) return null;
    if (form?.form === 'appimage') {
        return {
            form: 'appimage',
            message: `This system does not let Jason run in its sandbox, so Jason did not start. Install the deb package instead, with sudo apt install ./${debFile}.`,
        };
    }
    return { form: form?.form ?? 'none', message: 'Jason does not start without its sandbox.' };
}

export interface RefusalDeps {
    whenReady(): Promise<unknown>;
    /** The desktop's log, made only once Electron is ready. */
    logger(): Logger;
    showErrorBox(title: string, text: string): void;
    exit(code: number): void;
}

/**
 * A refused start: once Electron is ready, one log line that carries no path, a native dialog with no window made, so
 * no page process ever starts unsandboxed, and a non-zero exit.
 */
export async function refuseStart(refusal: Refusal, deps: RefusalDeps): Promise<void> {
    await deps.whenReady();
    deps.logger().warn('sandbox_refused', { Form: refusal.form });
    deps.showErrorBox(windowTitle, refusal.message);
    deps.exit(1);
}
