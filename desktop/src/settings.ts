/**
 * The desktop's own settings, `<data>/desktop/settings.json`. They are the desktop's alone; the installation's settings
 * are `<data>/config/settings.json`, which the runtime reads and a Connect card edits as a person would.
 */

import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { isPlace, notFolderPlaces, type PlaceId } from './open-in.ts';

export interface DesktopSettings {
    /**
     * The release feed `jason ui install` and `jason plugin install` are given as `--feed`: the https address of a
     * `manifest.json`, or http on this machine. Unset, they read the public repository's releases.
     */
    feed: string | null;
    /** The desktop version whose skills were last deployed into the chat folder, so a new build deploys its own. */
    skillsDeployedFor: string | null;
    /**
     * The theme the application shows, which the page tells the desktop whenever the person chooses. The desktop keeps it
     * so the starting page and the window's first paint are in the same theme before the page loads. Null is a choice
     * never told: the system's, except for a new installation, which meets Light (`theme.ts`).
     */
    theme: ThemeChoice | null;
    /**
     * The place "Open in" opened last, which the split button's main part opens next. Kept here, by the main process, in
     * this run's own data directory: never in the window and never in the person's folders. Absent until one was used.
     */
    openIn?: PlaceId;
    /**
     * Whether the Dashboard shows its daily trends: the last 24 hours against the 24 before them. Told by the page whenever
     * the person flips the switch and kept here. Absent until one was told, which the page reads as on.
     */
    dailyTrends?: boolean;
}

export type ThemeChoice = 'system' | 'light' | 'dark';

export const isTheme = (value: unknown): value is ThemeChoice => value === 'system' || value === 'light' || value === 'dark';

export const defaultSettings: DesktopSettings = { feed: null, skillsDeployedFor: null, theme: null };

/** Whether a feed is one the CLI takes: https anywhere, or http on this machine. */
export function isFeed(value: string): boolean {
    let url: URL;
    try {
        url = new URL(value);
    } catch {
        return false;
    }
    const here = url.hostname === '127.0.0.1' || url.hostname === 'localhost' || url.hostname === '[::1]';
    return url.protocol === 'https:' || (url.protocol === 'http:' && here);
}

/** Pure: the settings a file says, with anything it says wrongly taken as unset. */
export function parseSettings(text: string | null): DesktopSettings {
    if (text === null) return { ...defaultSettings };
    let raw: unknown;
    try {
        raw = JSON.parse(text);
    } catch {
        return { ...defaultSettings };
    }
    const value = (raw ?? {}) as { feed?: unknown; skills_deployed_for?: unknown; theme?: unknown; open_in?: unknown; daily_trends?: unknown };
    return {
        feed: typeof value.feed === 'string' && isFeed(value.feed) ? value.feed : null,
        skillsDeployedFor: typeof value.skills_deployed_for === 'string' ? value.skills_deployed_for : null,
        theme: isTheme(value.theme) ? value.theme : null,
        ...(typeof value.daily_trends === 'boolean' ? { dailyTrends: value.daily_trends } : {}),
        ...(isPlace(value.open_in) && !notFolderPlaces.includes(value.open_in) ? { openIn: value.open_in } : {}),
    };
}

export function readSettings(file: string): DesktopSettings {
    try {
        return parseSettings(readFileSync(file, 'utf8'));
    } catch {
        return { ...defaultSettings };
    }
}

/**
 * The settings a start begins with, the file written when it is missing so the desktop's own file is always there: a
 * new installation that never chose a theme meets Light and is told so, so its second start is not an existing one's.
 * A file that is there and needs no change is left as it is.
 */
export function settingsAtStart(file: string, newInstallation: boolean): DesktopSettings {
    const settings = readSettings(file);
    const next = newInstallation && settings.theme === null ? { ...settings, theme: 'light' as const } : settings;
    if (next !== settings || !existsSync(file)) writeSettings(file, next);
    return next;
}

/** Writes the settings whole, through a file beside them renamed over them. */
export function writeSettings(file: string, settings: DesktopSettings): void {
    mkdirSync(dirname(file), { recursive: true });
    const body: Record<string, string | boolean> = {};
    if (settings.feed !== null) body.feed = settings.feed;
    if (settings.skillsDeployedFor !== null) body.skills_deployed_for = settings.skillsDeployedFor;
    if (settings.theme !== null) body.theme = settings.theme;
    if (settings.openIn !== undefined) body.open_in = settings.openIn;
    if (settings.dailyTrends !== undefined) body.daily_trends = settings.dailyTrends;
    const temporary = `${file}.${process.pid}.tmp`;
    writeFileSync(temporary, `${JSON.stringify(body, null, 2)}\n`, { mode: 0o600 });
    renameSync(temporary, file);
}

/**
 * The page tells the desktop whether the Dashboard's daily trends are shown. Anything but true or false is refused and
 * nothing is written; the choice is written with the other settings kept beside it. Returns the settings now held.
 */
export function applyDailyTrends(
    on: unknown,
    settings: DesktopSettings,
    write: (next: DesktopSettings) => void,
): DesktopSettings {
    if (typeof on !== 'boolean') throw new Error('That is not a choice of the daily trends.');
    if (settings.dailyTrends === on) return settings;
    const next = { ...settings, dailyTrends: on };
    write(next);
    return next;
}
