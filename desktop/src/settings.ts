/**
 * The desktop's own settings, `<data>/desktop/settings.json`. They are the desktop's alone; the installation's settings
 * are `<data>/config/settings.json`, which the runtime reads and a Connect card edits as a person would.
 */

import { mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

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
     * so the starting page and the window's first paint are in the same theme before the page loads. Light is what a new
     * installation meets.
     */
    theme: ThemeChoice;
}

export type ThemeChoice = 'system' | 'light' | 'dark';

export const isTheme = (value: unknown): value is ThemeChoice => value === 'system' || value === 'light' || value === 'dark';

export const defaultSettings: DesktopSettings = { feed: null, skillsDeployedFor: null, theme: 'light' };

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
    const value = (raw ?? {}) as { feed?: unknown; skills_deployed_for?: unknown; theme?: unknown };
    return {
        feed: typeof value.feed === 'string' && isFeed(value.feed) ? value.feed : null,
        skillsDeployedFor: typeof value.skills_deployed_for === 'string' ? value.skills_deployed_for : null,
        theme: isTheme(value.theme) ? value.theme : defaultSettings.theme,
    };
}

export function readSettings(file: string): DesktopSettings {
    try {
        return parseSettings(readFileSync(file, 'utf8'));
    } catch {
        return { ...defaultSettings };
    }
}

/** Writes the settings whole, through a file beside them renamed over them. */
export function writeSettings(file: string, settings: DesktopSettings): void {
    mkdirSync(dirname(file), { recursive: true });
    const body: Record<string, string> = {};
    if (settings.feed !== null) body.feed = settings.feed;
    if (settings.skillsDeployedFor !== null) body.skills_deployed_for = settings.skillsDeployedFor;
    if (settings.theme !== defaultSettings.theme) body.theme = settings.theme;
    const temporary = `${file}.${process.pid}.tmp`;
    writeFileSync(temporary, `${JSON.stringify(body, null, 2)}\n`, { mode: 0o600 });
    renameSync(temporary, file);
}
