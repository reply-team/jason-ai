// What the packaged desktop carries besides Electron itself: one runtime dependency, the updater. Everything else the
// desktop's folder installs is for building and testing it and never reaches the application.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const manifest = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8')) as { dependencies?: Record<string, string>; devDependencies?: Record<string, string> };

/**
 * The runtime dependencies and why each is there. electron-updater checks this repository's releases for a newer
 * desktop and installs one on the person's word; no other package is shipped inside the application.
 */
const allowed: Readonly<Record<string, string>> = {
    'electron-updater':
        'the updater: the stable releases of this repository, downloaded and installed on the person’s word; its quitAndInstall starts the installer from inside node_modules, the one start the process guard cannot see',
};

test('the desktop’s runtime dependencies are the allowed ones, each pinned to one version', () => {
    const dependencies = manifest.dependencies ?? {};
    assert.deepEqual(Object.keys(dependencies).sort(), Object.keys(allowed).sort());
    for (const [name, version] of Object.entries(dependencies)) assert.match(version, /^\d+\.\d+\.\d+$/, `${name} is pinned`);
});

test('every package for building and testing is pinned to one version', () => {
    for (const [name, version] of Object.entries(manifest.devDependencies ?? {})) assert.match(version, /^\d+\.\d+\.\d+$/, `${name} is pinned`);
});
