/**
 * The demo's start-up order, in one function. A route that names a plugin that is not placed fails the runtime's start,
 * so the demo installation is made ready in this order and the runtime is started only after all of it:
 *
 * 1. the demo's directory is made and proved usable (`openDirectory`);
 * 2. the demo provider is placed in `<data>/plugins/demo/` (`placeDemoPlugin`) and, together with it and only once it is
 *    there, `Routes:Default` is written as the demo provider (`writeDemoRoute`);
 * 3. the demo's fixed settings are written (`fixedSettings`, the mode's own).
 *
 * A step that throws stops the rest and the function throws: `startOn` (`mode.ts`) then makes the run the person's own,
 * sets the record back to Live and says so, so no runtime is ever started on a half-prepared demo.
 */

import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';

export interface DemoStartDeps {
    /** Makes the demo's directory and proves it usable (`openDirectory`); throws when not. */
    open(data: string): void;
    /** Places the demo provider in `<data>/plugins/demo/` (`placeDemoPlugin`); throws when it cannot. */
    place(data: string): void;
    /** Writes `Routes:Default` as the demo provider into the demo's settings file (`writeDemoRoute`). */
    route(data: string): void;
    /** Writes the demo's fixed settings; absent where the mode has none to write. */
    fixedSettings?(data: string): void;
}

/** The installation's settings file under a data directory: `<data>/config/settings.json`. */
export const settingsFileOf = (data: string): string => join(data, 'config', 'settings.json');

/** Prepares the demo installation in the order above; throws at the first step that fails, having done none after it. */
export function prepareDemo(data: string, deps: DemoStartDeps): void {
    deps.open(data);
    deps.place(data);
    deps.route(data);
    deps.fixedSettings?.(data);
}

/**
 * Writes `Routes:Default` = `{ "Plugin": "demo" }` into the settings file under `data`, keeping every other key, through
 * a file beside it renamed over it. It refuses, writing nothing, when the demo provider is not placed under `data`, so
 * a route never names a plugin that is absent; and a settings file it cannot read as an object is refused, not replaced.
 */
export function writeDemoRoute(data: string): void {
    if (!existsSync(join(data, 'plugins', 'demo', 'plugin.yaml'))) throw new Error('The demo provider is not placed, so no route to it is written.');
    const file = settingsFileOf(data);
    let settings: Record<string, unknown> = {};
    if (existsSync(file)) {
        const parsed: unknown = JSON.parse(readFileSync(file, 'utf8'));
        if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) throw new Error('The demo’s settings file is not an object.');
        settings = parsed as Record<string, unknown>;
    }
    const routes = typeof settings['Routes'] === 'object' && settings['Routes'] !== null && !Array.isArray(settings['Routes']) ? (settings['Routes'] as Record<string, unknown>) : {};
    settings['Routes'] = { ...routes, Default: { Plugin: 'demo' } };
    mkdirSync(dirname(file), { recursive: true });
    const temporary = `${file}.${process.pid}.tmp`;
    try {
        writeFileSync(temporary, `${JSON.stringify(settings, null, 2)}\n`);
        renameSync(temporary, file);
    } catch (error) {
        rmSync(temporary, { force: true });
        throw error;
    }
}
