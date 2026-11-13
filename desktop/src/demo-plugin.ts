/**
 * The demo provider's package, `plugins/demo`, as the desktop carries it and places it. The build puts the package
 * beside the executable and the skills (`bundle/plugins/demo`, files exactly as in the tree, so its digest is the one
 * `plugins/demo.digests` lists), and the demo's start-up (`demo-start.ts`) places a copy of it into the demo
 * installation's `<data>/plugins/demo/`, where the runtime reads packages from, before the runtime is started. Only the
 * demo's data directory is ever placed into: the person's installation never receives the package.
 */

import { cpSync, existsSync, mkdirSync, rmSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { demoDirectory } from './mode.ts';

/** Where the carried package is inside a bundle folder (`resources/bundle` once packaged). */
export function carriedDemoPlugin(bundle: string): string {
    return join(bundle, 'plugins', 'demo');
}

/**
 * Puts a copy of the carried package into `<data>/plugins/demo/`, replacing an earlier copy whole. `data` must be the
 * demo's data directory for this person, `demoDirectory(person)` exactly (`mode.ts`); any other is refused before a thing
 * is written. Throws when the carried package is not there or the copy fails, leaving no half-placed package behind.
 */
export function placeDemoPlugin(carried: string, data: string, person: string): void {
    if (data !== demoDirectory(person)) throw new Error('That is not the demo installation’s data directory, so the demo provider is not placed there.');
    if (!existsSync(join(carried, 'plugin.yaml')) || !statSync(carried).isDirectory()) throw new Error('This build carries no demo provider.');
    const plugins = join(data, 'plugins');
    const target = join(plugins, 'demo');
    mkdirSync(plugins, { recursive: true });
    rmSync(target, { recursive: true, force: true });
    try {
        cpSync(carried, target, { recursive: true, dereference: true });
    } catch (error) {
        rmSync(target, { recursive: true, force: true });
        throw error;
    }
}
