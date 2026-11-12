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

import { isAbsolute, posix } from 'node:path';
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

/** The mount an AppImage runs from, `APPDIR`, when this application is an AppImage and it names an absolute folder. */
export function mountOf(form: LinuxPackage | null, env: Readonly<Record<string, string | undefined>>): string | undefined {
    const appdir = env['APPDIR'];
    return form?.form === 'appimage' && appdir && posix.isAbsolute(appdir) ? appdir : undefined;
}

/** The lists the pinned app-builder-lib's AppRun puts the mount's folders on (`appImageUtil.js`, `generateAppRunScript`). */
const mountLists = ['PATH', 'LD_LIBRARY_PATH', 'XDG_DATA_DIRS', 'GSETTINGS_SCHEMA_DIR'] as const;

/**
 * Pure: the environment every child is given, with no folder of the AppImage's mount on any list the AppRun put it on.
 * The mount lasts only while the application runs, and a runtime it starts outlasts it: the PATH a unit records, the
 * PATH the chat and the Connect cards' programs search, and the libraries a child loads are the person's own. A list
 * left empty is dropped. With no mount the environment is returned as it is.
 */
export function withoutMount<T extends Readonly<Record<string, string | undefined>>>(env: T, mount: string | undefined): T {
    if (mount === undefined) return env;
    const trimmed = (entry: string): string => (entry.endsWith('/') && entry.length > 1 ? trimmed(entry.slice(0, -1)) : entry);
    const root = trimmed(mount);
    const under = (entry: string) => trimmed(entry) === root || entry.startsWith(`${root}/`);
    const next: Record<string, string | undefined> = { ...env };
    for (const name of mountLists) {
        const value = next[name];
        if (value === undefined) continue;
        const kept = value.split(':').filter((entry) => entry !== '' && !under(entry));
        if (kept.length === 0) delete next[name];
        else next[name] = kept.join(':');
    }
    return next as T;
}
