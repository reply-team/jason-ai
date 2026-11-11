/**
 * Pure: whether a start runs without Chromium's sandbox, and what a person is told. The desktop never runs without it.
 * Electron runs unsandboxed when its command line carries --no-sandbox, and an AppImage's launcher adds that switch by
 * itself wherever the system refuses unprivileged user namespaces: electron-builder writes the launcher, AppRun, into
 * every AppImage and no option changes it (app-builder-lib 26.18.0, out/targets/appimage/appImageUtil.js,
 * generateAppRunScript: `if [ $HAVE_NO_SANDBOX -eq 0 ] && ! unshare -Ur true 2>/dev/null ; then NO_SANDBOX=(--no-sandbox)`).
 * So such a start is refused, and an AppImage is told of the deb, whose install brings an AppArmor profile for the
 * program where the system's AppArmor reads one (app-builder-lib 26.18.0, templates/linux/after-install.tpl).
 */

import type { LinuxPackage } from './linux-package.ts';

export function sandboxRefusal(hasNoSandbox: boolean, form: LinuxPackage | null): string | null {
    if (!hasNoSandbox) return null;
    if (form?.form === 'appimage') {
        return 'This system does not let Jason run in its sandbox, so Jason did not start. Install the deb package instead, with sudo apt install ./jason-desktop-linux-x64.deb.';
    }
    return 'Jason does not run without its sandbox, so it did not start. Start it without --no-sandbox.';
}
