/**
 * The demo provider's package, `plugins/demo`, as the desktop carries it and places it. The build puts the package
 * beside the executable and the skills (`bundle/plugins/demo`, files exactly as in the tree, so its digest is the one
 * `plugins/demo.digests` lists), and the demo's seed places a copy of it as its first step into the demo installation's
 * `<data>/plugins/demo/`, where the runtime reads packages from. Only the demo's data directory is ever placed into: the
 * person's installation never receives the package.
 */

import { cpSync, existsSync, mkdirSync, rmSync, statSync } from 'node:fs';
import { join } from 'node:path';

/** Where the carried package is inside a bundle folder (`resources/bundle` once packaged). */
export function carriedDemoPlugin(bundle: string): string {
    return join(bundle, 'plugins', 'demo');
}

/** Whether a directory is named as a demo installation's: its name ends in `-demo` (`demoDirectory` in `mode.ts` makes it so). */
function isDemoDirectory(data: string): boolean {
    return /[^\\/]-demo$/.test(data.replace(/[\\/]+$/, ''));
}

/**
 * Puts a copy of the carried package into `<data>/plugins/demo/`, replacing an earlier copy whole. `data` must be a demo
 * installation's data directory; any other is refused before a thing is written. Throws when the carried package is not
 * there or the copy fails, leaving no half-placed package behind.
 */
export function placeDemoPlugin(carried: string, data: string): void {
    if (!isDemoDirectory(data)) throw new Error('That is not a demo installation’s data directory, so the demo provider is not placed there.');
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
