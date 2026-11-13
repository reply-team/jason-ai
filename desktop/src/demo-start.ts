/**
 * The demo's start-up order, in one function: `startOn`'s `prepare` step (`mode.ts`). A route that names a plugin that
 * is not placed fails the runtime's start, so once the demo's directory is made (`openDirectory`) the demo installation
 * is made ready in this order, and the runtime is started only after all of it:
 *
 * 0. the demo's harness root is made and marked, or used when it carries the marker (`claimDemoFolder`); one without the
 *    marker is refused with nothing written into it. The data directory was claimed the same way by the open step before;
 * 1. the demo provider is placed in `<data>/plugins/demo/` (`placeDemoPlugin`) and, together with it and only once it is
 *    there, `Routes:Default` is written as the demo provider (`writeDemoRoute`);
 * 2. the demo's fixed settings are written (`writeDemoSettings`, passed as `fixedSettings`).
 *
 * A step that throws stops the rest and the function throws: `startOn` then makes the run the person's own, sets the
 * record back to Live and says so, so no runtime is ever started on a half-prepared demo.
 */

import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { claimDemoFolder } from './demo-folders.ts';
import { mergeSettings } from './demo-settings.ts';
import { openDirectory } from './mode.ts';
import { desktopPaths } from './paths.ts';

export interface DemoStartDeps {
    /** Makes and marks the demo's harness root, or uses it when marked; throws on a folder of that name that is not ours. */
    harnessRoot(): void;
    /** Places the demo provider in `<data>/plugins/demo/` (`placeDemoPlugin`); throws when it cannot. */
    place(data: string): void;
    /** Writes `Routes:Default` as the demo provider into the demo's settings file (`writeDemoRoute`). */
    route(data: string): void;
    /** Writes the demo's fixed settings (`writeDemoSettings`). */
    fixedSettings(data: string): void;
    /**
     * Whether a demo runtime is alive on `data` (`demoRuntimeAlive`). This runs before the single-instance lock, so a second
     * launch reaches it while a demo instance runs; with a runtime alive nothing is placed or written.
     */
    runtimeAlive?(data: string): boolean;
}

/** The installation's settings file under a data directory: `<data>/config/settings.json`. */
export const settingsFileOf = (data: string): string => desktopPaths(data).installationSettings;

/**
 * `startOn`'s open step for the demo: the data directory is claimed (made and marked, or used when marked; a folder of that
 * name that is not ours is refused with nothing written into it) and then proved usable (`openDirectory`).
 */
export function openDemoDirectory(directory: string): void {
    claimDemoFolder(directory);
    openDirectory(directory);
}

/** Prepares the demo installation in the order above; throws at the first step that fails, having done none after it. */
export function prepareDemo(data: string, deps: DemoStartDeps): void {
    // A live demo runtime holds the plugin and the settings it loaded: nothing is removed, copied or written under it.
    if (deps.runtimeAlive?.(data) === true) return;
    deps.harnessRoot();
    deps.place(data);
    deps.route(data);
    deps.fixedSettings(data);
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
    const next = mergeSettings(settings, { Routes: { Default: { Plugin: 'demo' } } });
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
