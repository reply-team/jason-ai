// The demo provider's package, carried by the desktop and placed in the demo installation: the carried copy is the
// tree's package byte for byte, placing copies it into the demo's plugins folder and refuses the person's, and the
// build gathers and checks it with the executable and the skills.

import assert from 'node:assert/strict';
import { lstatSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, relative, sep } from 'node:path';
import { test } from 'node:test';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { bundleFiles, checkBundle } from '../electron-builder.config.mjs';
import { carriedDemoPlugin, placeDemoPlugin } from '../src/demo-plugin.ts';
import { demoDirectory } from '../src/mode.ts';
import { complete } from '../scripts/prepare.mjs';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');
const repository = join(desktop, '..');

function scratch(t: { after(fn: () => void): void }): string {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-demo-carry-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    return root;
}

/** Every file under a folder, as a path with forward slashes, and its bytes. */
function files(folder: string): Map<string, string> {
    const found = new Map<string, string>();
    const walk = (at: string) => {
        for (const entry of readdirSync(at, { withFileTypes: true })) {
            const path = join(at, entry.name);
            if (entry.isDirectory()) walk(path);
            else found.set(relative(folder, path).split(sep).join('/'), readFileSync(path).toString('base64'));
        }
    };
    walk(folder);
    return found;
}

/** A bundle folder as the build prepares it, from this repository's own tree. */
function prepared(t: { after(fn: () => void): void }): string {
    const folder = join(scratch(t), 'bundle');
    mkdirSync(folder);
    writeFileSync(join(folder, 'jason.exe'), 'exe');
    complete(folder, '0.39.0-rc.1');
    return folder;
}

/** A package of the test's own, standing for the carried one. */
function carriedPackage(t: { after(fn: () => void): void }): string {
    const folder = join(scratch(t), 'plugins', 'demo');
    mkdirSync(join(folder, 'modules'), { recursive: true });
    writeFileSync(join(folder, 'plugin.yaml'), 'id: demo\n');
    writeFileSync(join(folder, 'main.js'), 'export default {};\n');
    writeFileSync(join(folder, 'modules', 'clock.js'), 'export const now = 1;\n');
    return folder;
}

test('the build carries the demo provider: the carried copy is the tree’s package, file for file and byte for byte', (t) => {
    const folder = prepared(t);
    const tree = files(join(repository, 'plugins', 'demo'));
    assert.ok(tree.has('plugin.yaml') && tree.has('main.js'), 'the tree holds the package');
    assert.deepEqual(files(carriedDemoPlugin(folder)), tree);
});

/**
 * The package digest as docs/plugins.md describes it (the runtime's PackageDigest is the same stream): sha256 over every file
 * whose relative path has no segment starting with a dot, sorted ordinally by its forward-slashed path, each contributing
 * its path, a zero byte, its decimal byte length, a zero byte, then its bytes.
 */
function packageDigest(folder: string): string {
    const paths: string[] = [];
    const walk = (at: string) => {
        for (const entry of readdirSync(at, { withFileTypes: true })) {
            const path = join(at, entry.name);
            if (entry.isDirectory()) walk(path);
            else paths.push(relative(folder, path).split(sep).join('/'));
        }
    };
    walk(folder);
    const hash = createHash('sha256');
    for (const path of paths.filter((one) => !one.split('/').some((segment) => segment.startsWith('.'))).sort((a, b) => (a < b ? -1 : a > b ? 1 : 0))) {
        const bytes = readFileSync(join(folder, ...path.split('/')));
        hash.update(Buffer.concat([Buffer.from(path, 'utf8'), Buffer.from([0]), Buffer.from(String(bytes.length), 'ascii'), Buffer.from([0]), bytes]));
    }
    return `sha256:${hash.digest('hex')}`;
}

test('the carried copy’s package digest, computed as the plugin host does, is the last line plugins/demo.digests lists', (t) => {
    const lines = readFileSync(join(repository, 'plugins', 'demo.digests'), 'utf8')
        .split(/\r?\n/)
        .map((line) => line.trim())
        .filter((line) => line !== '' && !line.startsWith('#'));
    const last = lines.at(-1) ?? '';
    assert.match(last, /^[0-9]+\.[0-9]+\.[0-9]+ sha256:[0-9a-f]{64}$/);
    assert.equal(packageDigest(carriedDemoPlugin(prepared(t))), last.split(' ')[1]);
});

test('a pack refuses a bundle that does not carry the demo provider', (t) => {
    for (const platform of ['win32', 'darwin', 'linux'] as const) assert.ok(bundleFiles(platform).includes('plugins'), platform);
    const folder = prepared(t);
    checkBundle(folder, '0.39.0-rc.1', 'win32');
    rmSync(join(folder, 'plugins'), { recursive: true });
    assert.throws(() => checkBundle(folder, '0.39.0-rc.1', 'win32'), /has no plugins/);
});

test('placing copies the carried package into the demo installation’s plugins folder, a copy and never a link', (t) => {
    const carried = carriedPackage(t);
    const person = join(scratch(t), '.jason');
    const data = demoDirectory(person);
    mkdirSync(data);
    placeDemoPlugin(carried, data, person);
    const there = join(data, 'plugins', 'demo');
    assert.deepEqual(files(there), files(carried));
    assert.ok(lstatSync(there).isDirectory() && !lstatSync(there).isSymbolicLink());
    assert.ok(!lstatSync(join(there, 'main.js')).isSymbolicLink());
});

test('placing again replaces an earlier copy whole: a file the package no longer has is gone', (t) => {
    const carried = carriedPackage(t);
    const person = join(scratch(t), '.jason');
    const data = demoDirectory(person);
    mkdirSync(join(data, 'plugins', 'demo'), { recursive: true });
    writeFileSync(join(data, 'plugins', 'demo', 'stale.js'), 'old');
    writeFileSync(join(data, 'plugins', 'demo', 'main.js'), 'old');
    placeDemoPlugin(carried, data, person);
    assert.deepEqual(files(join(data, 'plugins', 'demo')), files(carried));
    assert.deepEqual(readdirSync(join(data, 'plugins')), ['demo'], 'nothing else is left beside it for the runtime to read as a plugin');
});

test('placing never reaches the person’s installation: a directory that is not the demo’s is refused and nothing is written', (t) => {
    const carried = carriedPackage(t);
    const home = scratch(t);
    const own = join(home, '.jason');
    // The person's own directory, and every other directory, even one named like a demo's: only demoDirectory(person) is placed into.
    for (const data of [own, `${own}/`, join(home, 'demo'), join(home, '.jason-demo', 'inner'), join(home, 'other-demo'), join(home, '.JASON-demo'), home]) {
        mkdirSync(data, { recursive: true });
        assert.throws(() => placeDemoPlugin(carried, data, own), /not the demo installation/, data);
        assert.throws(() => lstatSync(join(data, 'plugins')), /ENOENT/, data);
    }
    // The demo's own directory is accepted for that person alone.
    mkdirSync(demoDirectory(own), { recursive: true });
    assert.throws(() => placeDemoPlugin(carried, demoDirectory(own), join(home, 'someone-else')), /not the demo installation/);
    placeDemoPlugin(carried, demoDirectory(own), own);
});

test('placing a package that is not there, or is no plugin, throws and leaves nothing in the demo’s plugins folder', (t) => {
    const person = join(scratch(t), '.jason');
    const data = demoDirectory(person);
    mkdirSync(data);
    assert.throws(() => placeDemoPlugin(join(scratch(t), 'missing'), data, person), /carries no demo provider/);
    const empty = join(scratch(t), 'empty');
    mkdirSync(empty);
    assert.throws(() => placeDemoPlugin(empty, data, person), /carries no demo provider/);
    assert.throws(() => lstatSync(join(data, 'plugins', 'demo')), /ENOENT/);
});

test('the carried folder is where the build puts it, beside the executable and the skills', () => {
    assert.equal(carriedDemoPlugin(join('res', 'bundle')), join('res', 'bundle', 'plugins', 'demo'));
    assert.equal(dirname(dirname(carriedDemoPlugin(join('res', 'bundle')))), join('res', 'bundle'));
});
