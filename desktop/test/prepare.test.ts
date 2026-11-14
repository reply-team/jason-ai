// What scripts/prepare.mjs puts in a bundle folder, and which runs it refuses. The publish itself is dotnet's and is
// never started here: these rows hold the plan a run makes from its arguments and the folder it completes.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { checkBundle, demoArchive } from '../electron-builder.config.mjs';
import { carryDemo, complete, plan, versionPrefix } from '../scripts/prepare.mjs';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');

function temporary(t: { after(fn: () => void): void }): string {
    const folder = mkdtempSync(join(tmpdir(), 'jason-prepare-'));
    t.after(() => rmSync(folder, { recursive: true, force: true }));
    return folder;
}

test('a development run publishes into bundle/ at the repository’s version with a candidate’s word', () => {
    const run = plan(['node', 'prepare.mjs', '--rid', 'win-x64'], '0.34.0');
    assert.deepEqual(run, { output: join(desktop, 'bundle'), version: '0.34.0-rc.1', rid: 'win-x64', executable: null, name: 'jason.exe', demo: null });
    assert.equal(plan(['node', 'prepare.mjs', '--rid', 'osx-arm64', '--pre', 'rc.2'], '0.34.0').version, '0.34.0-rc.2');
    assert.match(versionPrefix(), /^\d+\.\d+\.\d+$/);
    assert.throws(() => plan(['node', 'prepare.mjs', '--pre', 'dev.1'], '0.34.0'), /never carries a dev version/);
});

test('a package’s run names its architecture’s folder, the whole version, and the executable the release signed', () => {
    const run = plan(['node', 'prepare.mjs', '--rid', 'osx-x64', '--output', 'bundles/x64', '--version', '0.34.0', '--executable', '/r/artifacts/package/osx-x64/jason'], '0.35.0');
    assert.equal(run.output, join(desktop, 'bundles', 'x64'));
    assert.equal(run.version, '0.34.0');
    assert.equal(run.executable, resolve('/r/artifacts/package/osx-x64/jason'));
    assert.equal(run.name, 'jason');
    for (const version of ['0.34.0-local', '0.0.0', 'v0.34.0']) {
        assert.throws(() => plan(['node', 'prepare.mjs', '--version', version], '0.34.0'), /JASON_VERSION/, version);
    }
});

test('a run never empties a folder outside desktop/, nor desktop/ itself', () => {
    for (const output of ['..', '.', '../ui', '/tmp/bundle']) {
        assert.throws(() => plan(['node', 'prepare.mjs', '--rid', 'win-x64', '--output', output], '0.34.0'), /not a folder inside desktop/, output);
    }
});

test('the executable given is the platform’s own file: jason.exe for Windows, jason elsewhere', () => {
    assert.throws(() => plan(['node', 'prepare.mjs', '--rid', 'win-x64', '--executable', 'out/jason'], '0.34.0'), /not a jason\.exe for win-x64/);
    assert.throws(() => plan(['node', 'prepare.mjs', '--rid', 'osx-arm64', '--executable', 'out/jason.exe'], '0.34.0'), /not a jason for osx-arm64/);
});

test('a completed bundle carries the licence and the notices beside the executable, the skills and its version', (t) => {
    const repository = temporary(t);
    writeFileSync(join(repository, 'LICENSE'), 'MIT');
    writeFileSync(join(repository, 'THIRD-PARTY-NOTICES'), 'notices');
    mkdirSync(join(repository, 'skills', 'runtime', 'a'), { recursive: true });
    writeFileSync(join(repository, 'skills', 'runtime', 'a', 'SKILL.md'), 'a');
    mkdirSync(join(repository, 'plugins', 'demo'), { recursive: true });
    writeFileSync(join(repository, 'plugins', 'demo', 'plugin.yaml'), 'id: demo');
    const folder = join(repository, 'out');
    mkdirSync(folder);
    writeFileSync(join(folder, 'jason.exe'), 'exe');
    writeFileSync(join(folder, 'jason.pdb'), 'symbols');
    complete(folder, '0.34.0-rc.1', repository);
    assert.deepEqual(readdirSync(folder).sort(), ['LICENSE', 'THIRD-PARTY-NOTICES', 'jason.exe', 'plugins', 'skills', 'version.json']);
    assert.equal(readFileSync(join(folder, 'LICENSE'), 'utf8'), 'MIT');
    assert.equal(readFileSync(join(folder, 'THIRD-PARTY-NOTICES'), 'utf8'), 'notices');
    assert.equal(readFileSync(join(folder, 'skills', 'runtime', 'a', 'SKILL.md'), 'utf8'), 'a');
    assert.equal(readFileSync(join(folder, 'plugins', 'demo', 'plugin.yaml'), 'utf8'), 'id: demo');
    assert.deepEqual(JSON.parse(readFileSync(join(folder, 'version.json'), 'utf8')), { version: '0.34.0-rc.1' });
    // What prepare completes is what a pack accepts, at that version and no other.
    checkBundle(folder, '0.34.0-rc.1', 'win32');
    assert.throws(() => checkBundle(folder, '0.34.0', 'win32'), /prepared as 0\.34\.0-rc\.1/);
});

test('a release’s run names the demo’s sample archive it carries; a run that names none carries none', () => {
    const release = plan(['node', 'prepare.mjs', '--rid', 'linux-x64', '--output', 'bundles/x64', '--version', '0.34.0', '--demo', '/r/artifacts/demo/jason-demo.zip'], '0.34.0');
    assert.equal(release.demo, resolve('/r/artifacts/demo/jason-demo.zip'));
    assert.equal(plan(['node', 'prepare.mjs', '--rid', 'linux-x64', '--output', 'bundles/x64', '--version', '0.34.0'], '0.34.0').demo, null);
    assert.equal(demoArchive, 'demo/jason-demo.zip');
});

test('a release’s bundle carries the demo’s sample archive it was given, byte for byte, at demo/jason-demo.zip', (t) => {
    const folder = temporary(t);
    // Bytes standing for the archive the release's demo job made: the copy is what is held, not the archive's content.
    const given = join(folder, 'given.zip');
    writeFileSync(given, Buffer.from([0x50, 0x4b, 0x05, 0x06, 1, 2, 3]));
    const bundle = join(folder, 'bundle');
    mkdirSync(bundle);
    carryDemo(bundle, given);
    assert.deepEqual(readFileSync(join(bundle, ...demoArchive.split('/'))), readFileSync(given));
    assert.deepEqual(readdirSync(bundle), ['demo']);
    assert.throws(() => carryDemo(bundle, join(folder, 'missing.zip')), /no archive at/);
});
