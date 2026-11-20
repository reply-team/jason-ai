import { execFile } from 'node:child_process';
import { homedir } from 'node:os';
import { delimiter, join } from 'node:path';
import { withoutMount } from './linux-package.ts';
import { withoutSessionMarkers } from './session-markers.ts';

const marker = '__JASON_PATH__';
const closing = '__JASON_PATH_END__';

/**
 * How the login shell is started: killed outright at five seconds, since an interactive shell may ignore SIGTERM and a
 * hanging rc file would otherwise hold the desktop before any window exists; and with the desktop's environment less a
 * parent Claude Code session's markers (`sessionMarkers`), since the person's rc files run there.
 */
export function loginShellOptions(env: Readonly<Record<string, string | undefined>>) {
    return { timeout: 5000, killSignal: 'SIGKILL', windowsHide: true, env: withoutSessionMarkers(env) } as const;
}

/**
 * Pure: a PATH with the places an agent's command line lands added at its end, which a profile may not name yet:
 * Homebrew's on macOS, Homebrew on Linux's own folder on Linux.
 */
export function withUsualPlaces(path: string, home: string, platform: NodeJS.Platform = 'darwin'): string {
    const entries = path.split(delimiter).filter(Boolean);
    for (const place of usualPlaces(home, platform)) {
        if (!entries.includes(place)) entries.push(place);
    }
    return entries.join(delimiter);
}

function usualPlaces(home: string, platform: NodeJS.Platform): string[] {
    const brew = platform === 'linux' ? ['/usr/local/bin', '/home/linuxbrew/.linuxbrew/bin'] : ['/opt/homebrew/bin', '/usr/local/bin'];
    return [join(home, '.local', 'bin'), join(home, '.claude', 'local'), join(home, '.npm-global', 'bin'), ...brew];
}

/** A folder the AppImage runtime mounts an AppImage in, `<temporary folder>/.mount_<name>`, or one inside it. */
const appImageMount = /(^|\/)\.mount_[^/]*(\/|$)/;

/**
 * Pure: the PATH a start of the desktop was handed, less what an earlier start put on its own PATH for the programs it
 * starts. A desktop started by another inherits that one's environment: an AppImage's update starts the new file with
 * it, and the mode switch starts the desktop again with it. That PATH ends with the usual places the earlier start added
 * (`withUsualPlaces`), and on Linux it holds the folders of the AppImage mounts each AppRun put in front, the earlier
 * start's and this one's (`appdir`), which end with their start and which no terminal has. The usual places at its end
 * and every mount's folders are taken off; what is left is what the session handed, which the login shell is asked with
 * (`shellPath`), so that its answer is the PATH a new terminal has, and which stands in where it answers nothing.
 *
 * A usual place a person's own profile puts last is taken off too, and the login shell's reading of that profile puts it
 * back. One that something other than a shell put last on the session's PATH, `environment.d` say, is not put back: the
 * machine set-up then writes the PATH lines for a folder a terminal has already. Off macOS and Linux the PATH is the
 * person's own and is returned as it is.
 */
export function handedPath(path: string, appdir: string | undefined, home: string, platform: NodeJS.Platform): string {
    if (platform !== 'darwin' && platform !== 'linux') return path;
    const mount = appdir !== undefined && appdir.startsWith('/') ? appdir : undefined;
    const entries = (withoutMount({ PATH: path }, mount).PATH ?? '').split(delimiter).filter((entry) => entry !== '' && !appImageMount.test(entry));
    const usual = usualPlaces(home, platform);
    while (entries.length > 0 && usual.includes(entries[entries.length - 1]!)) entries.pop();
    return entries.join(delimiter);
}

/** Pure: the login shell asked for the PATH, or null where none is: `SHELL`, else the one each system starts a terminal with. */
export function loginShell(platform: NodeJS.Platform, env: Readonly<Record<string, string | undefined>>): string | null {
    if (platform !== 'darwin' && platform !== 'linux') return null;
    return env['SHELL'] || (platform === 'darwin' ? '/bin/zsh' : '/bin/sh');
}

/**
 * Pure: the PATH the login shell printed between the two markers, or null when it printed no such pair. What a logout
 * script prints after the closing marker is cut off. Only its absolute folders are taken, in order: an entry that is
 * not absolute, or that carries a control character, is dropped, since a relative one would be read against the folder
 * a program the desktop starts runs in, the chat's among them. With no absolute folder left the answer is refused and
 * the inherited PATH stands: a shell that does not expand `"$PATH"` in double quotes (nushell, say) prints it as it is,
 * and one that prints its PATH as a list prints no colon-separated one.
 */
export function pathFromOutput(stdout: string): string | null {
    const at = stdout.lastIndexOf(marker);
    if (at < 0) return null;
    const end = stdout.indexOf(closing, at + marker.length);
    if (end < 0) return null;
    const path = stdout.slice(at + marker.length, end).trim();
    if (path === '') return null;
    const folders = path.split(':').filter((entry) => entry.startsWith('/') && !/[\u0000-\u001f\u007f]/.test(entry));
    return folders.length > 0 ? folders.join(':') : null;
}

/**
 * The PATH every program the desktop starts is given. A program started from the Finder gets launchd's bare PATH, and one
 * started from a Linux session's menu gets the session's, which reads no `~/.bashrc`, where a version manager or npm's
 * own folder most often adds itself; on neither is an agent's command line found as a terminal finds it. The person's
 * login shell knows the real one, so on macOS and Linux it is asked once (`shellPath`), before anything is started, the
 * PATH this start was handed (`handedPath`) standing in where it answers nothing, and the usual places are added. On
 * Windows the inherited PATH is the person's own and is used as it is.
 */
export async function loginPath(handed: string, home: string, asked: Promise<string | null> = shellPath()): Promise<string> {
    if (process.platform !== 'darwin' && process.platform !== 'linux') return handed;
    return withUsualPlaces((await asked) ?? handed, home, process.platform);
}

/**
 * The PATH the person's login shell itself has, or null where it printed none, or anywhere but macOS and Linux: what a
 * new terminal finds programs by, before the desktop adds the usual places to its own (`withUsualPlaces`). Whether the
 * PATH command's folder is on it decides whether placing the command also writes the PATH lines (`path-profile.ts`, and
 * the machine set-up's `--terminal-path`). The shell starts from the PATH this start was handed (`handedPath`), never one
 * an earlier start extended for its own children: a profile that keeps the PATH it is given, as Arch's does, would print
 * that start's additions back as a terminal's.
 */
export async function shellPath(): Promise<string | null> {
    if (process.platform !== 'darwin' && process.platform !== 'linux') return null;
    const shell = loginShell(process.platform, process.env) ?? '/bin/sh';
    const env = { ...process.env, PATH: handedPath(process.env['PATH'] ?? '', process.env['APPDIR'], homedir(), process.platform) };
    return new Promise((resolve) => {
        execFile(shell, ['-ilc', `printf '${marker}%s${closing}' "$PATH"`], loginShellOptions(env), (error, stdout) => {
            resolve(error ? null : pathFromOutput(String(stdout)));
        });
    });
}

/** The account's shell as `SHELL` names it, or null where it names none: the PATH lines are chosen by it, as the installer chooses them. */
export function accountShell(env: Readonly<Record<string, string | undefined>>): string | null {
    return env['SHELL'] || null;
}
