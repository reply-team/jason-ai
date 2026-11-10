import { execFile } from 'node:child_process';
import { delimiter, join } from 'node:path';

const marker = '__JASON_PATH__';

/** Pure: a PATH with the places an agent's command line lands added at its end, which a profile may not name yet. */
export function withUsualPlaces(path: string, home: string): string {
    const entries = path.split(delimiter).filter(Boolean);
    for (const place of [join(home, '.local', 'bin'), join(home, '.claude', 'local'), join(home, '.npm-global', 'bin'), '/opt/homebrew/bin', '/usr/local/bin']) {
        if (!entries.includes(place)) entries.push(place);
    }
    return entries.join(delimiter);
}

/** Pure: the PATH the login shell printed after the marker, or null when it printed none. */
export function pathFromOutput(stdout: string): string | null {
    const at = stdout.lastIndexOf(marker);
    if (at < 0) return null;
    const path = stdout.slice(at + marker.length).trim();
    return path === '' ? null : path;
}

/**
 * The PATH every program the desktop starts is given. A program started from the Finder gets launchd's bare PATH, on
 * which no agent's command line is found; the person's login shell knows the real one, so on macOS it is asked once,
 * before anything is started, and failing that the usual places are added. Everywhere else the inherited PATH is the
 * person's own and is used as it is.
 */
export async function loginPath(inherited: string, home: string): Promise<string> {
    if (process.platform !== 'darwin') return inherited;
    const shell = process.env.SHELL || '/bin/zsh';
    return new Promise((resolve) => {
        execFile(shell, ['-ilc', `printf '${marker}%s' "$PATH"`], { timeout: 5000, windowsHide: true }, (error, stdout) => {
            resolve(withUsualPlaces((error ? null : pathFromOutput(String(stdout))) ?? inherited, home));
        });
    });
}
