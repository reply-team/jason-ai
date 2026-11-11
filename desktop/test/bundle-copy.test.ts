// The AppImage's carried jason: its bundle lives under a mount that lasts only while the application runs, so each start
// copies it under <data>/desktop/bundle/<version>/ and that copy is the jason the desktop carries.

import assert from 'node:assert/strict';
import { existsSync, lstatSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, symlinkSync, utimesSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { BundleDiffers, carriedBundle, copiesBundle, differsWords, isOtherCopy } from '../src/bundle-copy.ts';
import { rollbackVersions } from '../src/rollback.ts';

function bench(t: { after(fn: () => void): void }) {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-bundle-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const source = join(root, 'mount', 'resources', 'bundle');
    mkdirSync(source, { recursive: true });
    writeFileSync(join(source, 'jason'), 'the jason 0.35.0', { mode: 0o755 });
    writeFileSync(join(source, 'version.json'), '{"version":"0.35.0"}');
    writeFileSync(join(source, 'LICENSE'), 'MIT');
    mkdirSync(join(source, 'notices'));
    writeFileSync(join(source, 'notices', 'THIRD-PARTY-NOTICES'), 'the notices');
    const bundles = join(root, 'data', 'desktop', 'bundle');
    return { root, source, bundles };
}

function filesOf(folder: string): Record<string, string> {
    const found: Record<string, string> = {};
    for (const name of readdirSync(folder, { recursive: true, encoding: 'utf8' })) {
        const path = join(folder, name);
        if (lstatSync(path).isFile()) found[name.replaceAll('\\', '/')] = readFileSync(path, 'utf8');
    }
    return found;
}

test('only the AppImage copies its bundle: the deb and a folder carry theirs where they are installed', () => {
    assert.equal(copiesBundle({ form: 'appimage', file: '/home/a/Jason.AppImage' }), true);
    assert.equal(copiesBundle({ form: 'deb' }), false);
    assert.equal(copiesBundle({ form: 'folder' }), false);
    assert.equal(copiesBundle(null), false);
});

test('the bundle is copied under its version, by write and rename, file by file, and that copy is carried', (t) => {
    const b = bench(t);
    const carried = carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0', keep: [] });
    assert.equal(carried, join(b.bundles, '0.35.0'));
    assert.deepEqual(filesOf(carried), filesOf(b.source));
    assert.deepEqual(readdirSync(b.bundles), ['0.35.0'], 'no temporary folder is left');
    if (process.platform !== 'win32') assert.equal(statSync(join(carried, 'jason')).mode & 0o777, 0o755, 'the executable stays one');
});

test('the copy keeps version.json beside the copied jason, the record the command line reads there', (t) => {
    const b = bench(t);
    const carried = carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0', keep: [] });
    assert.ok(statSync(join(carried, 'jason')).isFile());
    assert.deepEqual(JSON.parse(readFileSync(join(carried, 'version.json'), 'utf8')), { version: '0.35.0' });
    // The folder is copied whole: a file the package adds later is carried without a list naming it.
    writeFileSync(join(b.source, 'a-later-record.json'), '{}');
    const next = carriedBundle({ source: b.source, bundles: join(b.root, 'other'), version: '0.35.0', keep: [] });
    assert.deepEqual(filesOf(next), filesOf(b.source));
});

test('a link in the package is copied as the file it names, never as a link', (t) => {
    const b = bench(t);
    rmSync(join(b.source, 'LICENSE'));
    writeFileSync(join(b.root, 'LICENSE.real'), 'MIT');
    try {
        symlinkSync(join(b.root, 'LICENSE.real'), join(b.source, 'LICENSE'));
    } catch {
        t.skip('this account cannot make a link here');
        return;
    }
    const carried = carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0', keep: [] });
    assert.equal(lstatSync(join(carried, 'LICENSE')).isSymbolicLink(), false);
    assert.equal(readFileSync(join(carried, 'LICENSE'), 'utf8'), 'MIT');
});

test('a copy already there is used only when every file of it, the executable among them, has the digest of the package’s', (t) => {
    const b = bench(t);
    const carried = carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0', keep: [] });
    const then = new Date('2026-01-01T00:00:00Z');
    utimesSync(join(carried, 'jason'), then, then);
    assert.equal(carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0', keep: [] }), carried);
    assert.equal(statSync(join(carried, 'jason')).mtime.toISOString(), then.toISOString(), 'a matching copy is not written again');

    writeFileSync(join(carried, 'jason'), 'a jason update apply put here');
    const before = filesOf(carried);
    assert.throws(
        () => carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0', keep: [] }),
        (error: unknown) => error instanceof BundleDiffers && error.message === differsWords(carried),
    );
    assert.deepEqual(filesOf(carried), before, 'a copy that differs is neither used nor overwritten');

    rmSync(carried, { recursive: true });
    carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0', keep: [] });
    writeFileSync(join(carried, 'planted'), 'a file the package does not carry');
    assert.throws(() => carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0', keep: [] }), BundleDiffers, 'a file more is a difference too');
    rmSync(join(carried, 'planted'));
    rmSync(join(carried, 'LICENSE'));
    assert.throws(() => carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0', keep: [] }), BundleDiffers, 'and a file fewer');
});

test('a copy that differs is named with the act that clears it', () => {
    assert.equal(
        differsWords('/home/a/.jason/desktop/bundle/0.35.0'),
        'The jason this application carries is copied to /home/a/.jason/desktop/bundle/0.35.0, and that copy no longer matches the one in this package, so Jason did not start with it. Something changed it after it was copied. Remove /home/a/.jason/desktop/bundle/0.35.0 and start Jason again: it copies the package’s jason there again.',
    );
});

test('the copies of other versions go, but the one the rollback record names, and so does a copy left half made', (t) => {
    const b = bench(t);
    for (const name of ['0.33.0', '0.34.0', '.0.35.0.tmp-4242']) {
        mkdirSync(join(b.bundles, name), { recursive: true });
        writeFileSync(join(b.bundles, name, 'jason'), name);
    }
    carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0', keep: ['0.34.0'] });
    assert.deepEqual(readdirSync(b.bundles).sort(), ['0.34.0', '0.35.0']);
});

test('a copy that cannot be made leaves nothing under its version and says so', (t) => {
    const b = bench(t);
    mkdirSync(join(b.bundles, '..'), { recursive: true });
    writeFileSync(b.bundles, 'not a folder');
    assert.throws(() => carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0', keep: [] }));
    assert.equal(existsSync(join(b.bundles, '0.35.0')), false);
});

test('a version that is not one name is refused before anything is written', (t) => {
    const b = bench(t);
    for (const version of ['', '.', '..', '../0.35.0', '0.35.0/x', '.0.35.0']) {
        assert.throws(() => carriedBundle({ source: b.source, bundles: b.bundles, version, keep: [] }), version);
    }
    assert.equal(existsSync(b.bundles), false);
});

test('the copy the rollback record names stays: the version kept, or the one a pending update came from', () => {
    assert.deepEqual(rollbackVersions({ kept: { version: '0.34.0', directory: '/c' }, rejected: [] }), ['0.34.0']);
    assert.deepEqual(rollbackVersions({ pending: { fromVersion: '0.34.0', toVersion: '0.35.0', directory: null }, rejected: [] }), ['0.34.0']);
    assert.deepEqual(rollbackVersions({ rejected: ['0.35.0'] }), []);
});

test('a jason another version’s copy under the bundles folder is told from this start’s copy and from anything outside it', () => {
    const bundles = join('/data', 'desktop', 'bundle');
    const current = join(bundles, '0.36.0', 'jason');
    assert.equal(isOtherCopy(join(bundles, '0.35.0', 'jason'), bundles, current), true);
    assert.equal(isOtherCopy(current, bundles, current), false, 'this start’s copy is not another');
    assert.equal(isOtherCopy(join('/opt', 'Jason', 'resources', 'bundle', 'jason'), bundles, current), false, 'a jason outside the bundles folder is left as it is');
    assert.equal(isOtherCopy(join('/data', 'desktop', 'bundle-old', '0.35.0', 'jason'), bundles, current), false, 'a folder beside it is not under it');
    assert.equal(isOtherCopy(bundles, bundles, current), false);
});
