/**
 * The theme the window and the starting page show before any page is drawn. The desktop keeps the person's choice as one
 * of three words, or nothing; nothing means the system's, except for an installation that is new, which meets Light.
 */

import { readdirSync } from 'node:fs';
import { basename } from 'node:path';
import { isDesktopLog } from './log.ts';
import { desktopPaths } from './paths.ts';
import { isTheme, type DesktopSettings, type ThemeChoice } from './settings.ts';

/** The two backgrounds the window's first paint uses; `shell/shell.css` states the same two for the starting page. */
export const lightBackground = '#f7f7f5';
export const darkBackground = '#1b1b1a';

/**
 * Whether no earlier use of this installation left anything in its data directory: it is absent, empty, or holds only the
 * desktop's own records that a start writes before the installation is made: the mode, a request to start the demo over
 * that a version of 0.44 or earlier left (`demoStartOver`, which nothing writes now), the record of where `jason install` last
 * ran (`machineSetup`, which a first start writes before the person chooses), Electron's own profile (`desktop/electron/`) and the desktop's own log files
 * (`logs/desktop-*.jsonl`), which a Demo run writes and nothing else. A new installation's first start opens on the demo
 * and writes the first, so the person's own installation is still new when they first choose Live.
 */
export function isNewInstallation(dataDirectory: string, list: (directory: string) => string[] = readdirSync): boolean {
    let entries: string[];
    try {
        entries = list(dataDirectory);
    } catch {
        return true;
    }
    if (entries.length === 0) return true;
    const desktop = desktopPaths(dataDirectory);
    const logs = basename(desktop.logs);
    if (!entries.every((name) => name === basename(desktop.desktop) || name === logs)) return false;
    const records = [basename(desktop.mode), basename(desktop.demoStartOver), basename(desktop.machineSetup), basename(desktop.electron)];
    try {
        // Each is listed, so a file where a folder of these would be is no record of the desktop's.
        const own = entries.includes(basename(desktop.desktop)) ? list(desktop.desktop) : [];
        if (own.includes(basename(desktop.electron))) list(desktop.electron);
        const logged = entries.includes(logs) ? list(desktop.logs) : [];
        return own.every((name) => records.includes(name)) && logged.every(isDesktopLog);
    } catch {
        return false;
    }
}

/** The theme to show at start: what is kept, else Light for a new installation and the system's for an existing one. */
export function startingTheme(stored: ThemeChoice | null, isNew: boolean): ThemeChoice {
    return stored ?? (isNew ? 'light' : 'system');
}

export interface ThemeDeps {
    /** Sets Electron's theme source, and with it the window's colours. */
    setSource(choice: ThemeChoice): void;
    /** Writes the settings whole. */
    write(settings: DesktopSettings): void;
}

/**
 * The page tells the desktop the person's choice. A word that is none of the three is refused and nothing is written; a
 * choice is written with the other settings kept beside it. Returns the settings now held.
 */
export function applyTheme(choice: unknown, settings: DesktopSettings, deps: ThemeDeps): DesktopSettings {
    if (!isTheme(choice)) throw new Error('That is not a theme.');
    deps.setSource(choice);
    if (settings.theme === choice) return settings;
    const next = { ...settings, theme: choice };
    deps.write(next);
    return next;
}
