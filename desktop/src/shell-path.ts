import { execFile } from 'node:child_process';
import { delimiter, join } from 'node:path';
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
    const brew = platform === 'linux' ? ['/usr/local/bin', '/home/linuxbrew/.linuxbrew/bin'] : ['/opt/homebrew/bin', '/usr/local/bin'];
    for (const place of [join(home, '.local', 'bin'), join(home, '.claude', 'local'), join(home, '.npm-global', 'bin'), ...brew]) {
        if (!entries.includes(place)) entries.push(place);
    }
    return entries.join(delimiter);
}

/** Pure: the login shell asked for the PATH, or null where none is: `SHELL`, else the one each system starts a terminal with. */
export function loginShell(platform: NodeJS.Platform, env: Readonly<Record<string, string | undefined>>): string | null {
    if (platform !== 'darwin' && platform !== 'linux') return null;
    return env['SHELL'] || (platform === 'darwin' ? '/bin/zsh' : '/bin/sh');
}

/**
 * Pure: the PATH the login shell printed between the two markers, or null when it printed no such pair. What a logout
 * script prints after the closing marker is cut off.
 */
export function pathFromOutput(stdout: string): string | null {
    const at = stdout.lastIndexOf(marker);
    if (at < 0) return null;
    const end = stdout.indexOf(closing, at + marker.length);
    if (end < 0) return null;
    const path = stdout.slice(at + marker.length, end).trim();
    return path === '' ? null : path;
}

/**
 * The PATH every program the desktop starts is given. A program started from the Finder gets launchd's bare PATH, and one
 * started from a Linux session's menu gets the session's, which reads no `~/.bashrc`, where a version manager or npm's
 * own folder most often adds itself; on neither is an agent's command line found as a terminal finds it. The person's
 * login shell knows the real one, so on macOS and Linux it is asked once, before anything is started, and failing that
 * the usual places are added. On Windows the inherited PATH is the person's own and is used as it is.
 */
export async function loginPath(inherited: string, home: string): Promise<string> {
    if (process.platform !== 'darwin' && process.platform !== 'linux') return inherited;
    const shell = loginShell(process.platform, process.env) ?? '/bin/sh';
    const platform = process.platform;
    return new Promise((resolve) => {
        execFile(shell, ['-ilc', `printf '${marker}%s${closing}' "$PATH"`], loginShellOptions(process.env), (error, stdout) => {
            resolve(withUsualPlaces((error ? null : pathFromOutput(String(stdout))) ?? inherited, home, platform));
        });
    });
}
