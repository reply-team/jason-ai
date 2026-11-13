/**
 * The demo installation's settings: what its runtime must never run without, written into its own settings file,
 * `<demo>/config/settings.json`, when the desktop makes the demo's directory and before the demo's runtime is ever
 * started. They are in the file, never only in the desktop's environment, so a terminal with `JASON_DATA_DIR` naming the
 * demo's directory and nothing else finds the same installation. The person's own settings file is read for their
 * harness root and never written.
 *
 * The harness root is the person's with `-demo` after it, a folder of its own beside theirs, so the demo's projects and
 * campaigns are never made in the person's tree. The responder is off, and the manager, which has no switch of its own,
 * keeps only the trigger it cannot do without at its slowest cadence, so no AI role is woken by sample data. The
 * provider's events are read at the shortest cadence and the dispatcher ticks often, so the sample moves within minutes.
 * No route is written here: a route to a plugin not yet placed fails the start, so the route is written beside the
 * plugin's placing, after this.
 */

import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { demoDirectory } from './mode.ts';
import { desktopPaths } from './paths.ts';

/** The cadence the demo's manager is left with: the slowest `Manager:ReviewSeconds` allows, a week. */
export const demoReviewSeconds = 604800;

/** The settings every demo installation holds, as the settings file nests them. */
export function demoSettingsValues(harnessRoot: string): Record<string, Record<string, unknown>> {
    return {
        Harness: { Root: harnessRoot },
        Responder: { OnReply: false },
        Manager: { Triggers: ['decision_answered'], ReviewSeconds: demoReviewSeconds },
        Dispatcher: { EventsCadenceSeconds: 60, TickSeconds: 2 },
    };
}

const isObject = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value);

/** The key of `record` that names `key` without regard to case, as the runtime's configuration reads it; else `key`. */
function keyOf(record: Record<string, unknown>, key: string): string {
    return Object.keys(record).find((each) => each.toLowerCase() === key.toLowerCase()) ?? key;
}

/**
 * Pure: `settings` with `values` merged in, section by section: each value replaces the one of the same name, whatever
 * its case, and every other key is kept as it was.
 */
export function mergeSettings(settings: Record<string, unknown>, values: Record<string, Record<string, unknown>>): Record<string, unknown> {
    const merged: Record<string, unknown> = { ...settings };
    for (const [section, entries] of Object.entries(values)) {
        const name = keyOf(merged, section);
        const kept: Record<string, unknown> = isObject(merged[name]) ? { ...merged[name] } : {};
        for (const [key, value] of Object.entries(entries)) {
            for (const each of Object.keys(kept)) if (each !== key && each.toLowerCase() === key.toLowerCase()) delete kept[each];
            kept[key] = value;
        }
        merged[name] = kept;
    }
    return merged;
}

/**
 * Writes the demo installation's settings into `<demo>/config/settings.json`, the harness root the sibling of
 * `personHarnessRoot`: a file already there keeps its other keys; a file that is not a JSON object is refused, and the
 * start that called this is then the person's own (`startOn`). Written through a file renamed over it.
 */
export function writeDemoSettings(demo: string, personHarnessRoot: string): void {
    const file = desktopPaths(demo).installationSettings;
    let settings: Record<string, unknown> = {};
    if (existsSync(file)) {
        const read: unknown = JSON.parse(readFileSync(file, 'utf8'));
        if (!isObject(read)) throw new Error('The demo settings file is not a JSON object.');
        settings = read;
    }
    const next = mergeSettings(settings, demoSettingsValues(demoDirectory(personHarnessRoot)));
    mkdirSync(dirname(file), { recursive: true });
    const temporary = `${file}.${process.pid}.tmp`;
    try {
        writeFileSync(temporary, `${JSON.stringify(next, null, 2)}\n`);
        renameSync(temporary, file);
    } catch (error) {
        rmSync(temporary, { force: true });
        throw error;
    }
}
