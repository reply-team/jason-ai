/**
 * Pure: which Linux package this application runs as, decided as the pinned electron-updater decides which updater it
 * makes (`electron-updater@6.8.11/out/main.js`, `doLoadAutoUpdater`): off Windows and macOS it makes an AppImageUpdater,
 * unless `path.join(process.resourcesPath, "package-type")` exists and reads `deb` (or `rpm`, `pacman`), which makes a
 * DebUpdater. The AppImageUpdater is active only with `APPIMAGE` set (`out/AppImageUpdater.js`, `isUpdaterActive`: "APPIMAGE
 * env is not defined, current application is not an AppImage"), and installs over that file (`doInstall`), which must be
 * an absolute path. So:
 *
 * - `deb`: the resources carry `package-type` reading `deb`; the application is the folder dpkg installed.
 * - `appimage`: no such file, and `APPIMAGE` an absolute path; the application is that one file, and what it carries is
 *   under a mount that lasts only while it runs.
 * - `folder`: neither, an unpacked application or a development build; no updater installs anything there.
 *
 * Only the `deb` form is acted on by name: an rpm or pacman package is never built, so a `package-type` naming one is
 * read as a folder.
 */

import { isAbsolute } from 'node:path';
import { applicationFolder } from './rollback.ts';

export type LinuxPackage = { form: 'deb' } | { form: 'appimage'; file: string } | { form: 'folder' };

export function linuxPackage(env: Readonly<Record<string, string | undefined>>, packageType: string | null): LinuxPackage {
    if (packageType !== null && packageType.trim() === 'deb') return { form: 'deb' };
    if (packageType !== null && packageType.trim() !== '') return { form: 'folder' };
    const file = env['APPIMAGE'];
    if (file && isAbsolute(file) && !file.includes('\0')) return { form: 'appimage', file };
    return { form: 'folder' };
}

/** What the session starts to open this application: the AppImage file itself, else the executable. */
export function launcherOf(form: LinuxPackage, executable: string): string {
    return form.form === 'appimage' ? form.file : executable;
}

/**
 * What an update keeps aside to go back to (`keepPrevious`): on Linux the AppImage file, nothing for the deb (its previous
 * release is installed again from its package), and the folder the application is installed as anywhere else.
 */
export function keptAsideOf(platform: NodeJS.Platform, form: LinuxPackage | null, executable: string): string | null {
    if (form?.form === 'appimage') return form.file;
    if (form?.form === 'deb') return null;
    return applicationFolder(platform, executable);
}
