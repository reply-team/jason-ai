// The AppImage's carried jason: its bundle lives under a mount that lasts only while the application runs, so each start
// copies it under <data>/desktop/bundle/<version>/ and that copy is the jason the desktop carries.

import assert from 'node:assert/strict';
import { chmodSync, existsSync, lstatSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, realpathSync, renameSync, rmSync, statSync, symlinkSync, utimesSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { BundleDiffers, carriedBundle, carriedOf, copiesBundle, differsWords, isOtherCopy, removeOtherCopies, type CopyRemoval } from '../src/bundle-copy.ts';
import { rollbackVersions } from '../src/rollback.ts';
import { memoryLog } from './harness.ts';

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
    const carried = carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' });
    assert.equal(carried, join(b.bundles, '0.35.0'));
    assert.deepEqual(filesOf(carried), filesOf(b.source));
    assert.deepEqual(readdirSync(b.bundles), ['0.35.0'], 'no temporary folder is left');
    if (process.platform !== 'win32') assert.equal(statSync(join(carried, 'jason')).mode & 0o777, 0o755, 'the executable stays one');
});

test('the copy keeps version.json beside the copied jason, the record the command line reads there', (t) => {
    const b = bench(t);
    const carried = carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' });
    assert.ok(statSync(join(carried, 'jason')).isFile());
    assert.deepEqual(JSON.parse(readFileSync(join(carried, 'version.json'), 'utf8')), { version: '0.35.0' });
    // The folder is copied whole: a file the package adds later is carried without a list naming it.
    writeFileSync(join(b.source, 'a-later-record.json'), '{}');
    const next = carriedBundle({ source: b.source, bundles: join(b.root, 'other'), version: '0.35.0' });
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
    const carried = carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' });
    assert.equal(lstatSync(join(carried, 'LICENSE')).isSymbolicLink(), false);
    assert.equal(readFileSync(join(carried, 'LICENSE'), 'utf8'), 'MIT');
});

test('a copy already there is used only when every file of it, the executable among them, has the digest of the package’s', (t) => {
    const b = bench(t);
    const carried = carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' });
    const then = new Date('2026-01-01T00:00:00Z');
    utimesSync(join(carried, 'jason'), then, then);
    assert.equal(carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' }), carried);
    assert.equal(statSync(join(carried, 'jason')).mtime.toISOString(), then.toISOString(), 'a matching copy is not written again');

    writeFileSync(join(carried, 'jason'), 'a jason update apply put here');
    const before = filesOf(carried);
    assert.throws(
        () => carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' }),
        (error: unknown) => error instanceof BundleDiffers && error.message === differsWords(carried),
    );
    assert.deepEqual(filesOf(carried), before, 'a copy that differs is neither used nor overwritten');

    rmSync(carried, { recursive: true });
    carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' });
    writeFileSync(join(carried, 'planted'), 'a file the package does not carry');
    assert.throws(() => carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' }), BundleDiffers, 'a file more is a difference too');
    rmSync(join(carried, 'planted'));
    rmSync(join(carried, 'LICENSE'));
    assert.throws(() => carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' }), BundleDiffers, 'and a file fewer');
});

test('a copy that differs is named with the act that clears it', () => {
    assert.equal(
        differsWords('/home/a/.jason/desktop/bundle/0.35.0'),
        'The jason this application carries is copied to /home/a/.jason/desktop/bundle/0.35.0, and that copy no longer matches the one in this package, so Jason did not start with it. Something changed it after it was copied. Remove /home/a/.jason/desktop/bundle/0.35.0 and start Jason again: it copies the package’s jason there again.',
    );
});

test('a copy that cannot be made leaves nothing under its version and says so', (t) => {
    const b = bench(t);
    mkdirSync(join(b.bundles, '..'), { recursive: true });
    writeFileSync(b.bundles, 'not a folder');
    assert.throws(() => carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' }));
    assert.equal(existsSync(join(b.bundles, '0.35.0')), false);
});

test('a version that is not one name is refused before anything is written', (t) => {
    const b = bench(t);
    for (const version of ['', '.', '..', '../0.35.0', '0.35.0/x', '.0.35.0']) {
        assert.throws(() => carriedBundle({ source: b.source, bundles: b.bundles, version }), version);
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

/** A link to a folder: a junction on Windows, which this account can make where a symbolic link needs a privilege. */
function linkFolder(target: string, at: string): void {
    symlinkSync(target, at, 'junction');
}

/** A removal over the bench's bundles folder: nothing set up names a copy, and no runtime runs from one. */
function removal(bundles: string, overrides: Partial<CopyRemoval> = {}): CopyRemoval & { log: ReturnType<typeof memoryLog> } {
    const log = memoryLog();
    return { bundles, version: '0.36.0', keep: [], named: async () => [], whose: async () => 'none', log, ...overrides } as CopyRemoval & { log: ReturnType<typeof memoryLog> };
}

function plant(bundles: string, names: readonly string[]): void {
    for (const name of names) {
        mkdirSync(join(bundles, name), { recursive: true });
        writeFileSync(join(bundles, name, 'jason'), name);
    }
}

test('the start decides once what it carries: an AppImage’s copy outlasts the run, the mount’s jason never does', () => {
    const packaged = join('/tmp', '.mount_Jason', 'resources', 'bundle');
    const bundles = join('/data', 'desktop', 'bundle');
    const copied = join(bundles, '0.36.0');
    const appimage = { form: 'appimage' as const, file: '/home/a/Jason.AppImage' };

    assert.deepEqual(carriedOf({ form: { form: 'deb' }, packaged: '/opt/Jason/resources/bundle', version: '0.36.0', bundles }), {
        bundle: '/opt/Jason/resources/bundle',
        uncopied: null,
        failure: null,
        outlives: true,
        repoints: false,
    });
    assert.deepEqual(carriedOf({ form: null, packaged, version: null, bundles }).outlives, true, 'Windows, macOS and a development build are as they were');
    assert.deepEqual(carriedOf({ form: appimage, packaged, version: '0.36.0', bundles, copy: () => copied }), { bundle: copied, uncopied: null, failure: null, outlives: true, repoints: true });

    const unversioned = carriedOf({ form: appimage, packaged, version: null, bundles, copy: () => assert.fail('nothing is copied without a version') });
    assert.deepEqual(unversioned, { bundle: packaged, uncopied: null, failure: null, outlives: false, repoints: false }, 'a package that names no version re-points nothing to its mount');

    const differs = carriedOf({ form: appimage, packaged, version: '0.36.0', bundles, copy: () => { throw new BundleDiffers(differsWords(copied)); } });
    assert.deepEqual(differs, { bundle: packaged, uncopied: differsWords(copied), failure: { differs: true }, outlives: false, repoints: false });

    const full = carriedOf({ form: appimage, packaged, version: '0.36.0', bundles, copy: () => { throw new Error('ENOSPC'); } });
    assert.equal(full.uncopied, `The jason this application carries could not be copied to ${copied}, so Jason did not start. Check that the data directory can be written to, then start Jason again.`);
    assert.deepEqual({ ...full, uncopied: null }, { bundle: packaged, uncopied: null, failure: { differs: false }, outlives: false, repoints: false });
});

test('a link at the bundles folder stops the start with the differs sentence, and nothing behind it is followed or emptied', (t) => {
    const b = bench(t);
    const elsewhere = join(b.root, 'mnt', 'apps');
    mkdirSync(join(elsewhere, 'photos'), { recursive: true });
    writeFileSync(join(elsewhere, 'photos', 'a.jpg'), 'a photo');
    mkdirSync(join(b.bundles, '..'), { recursive: true });
    linkFolder(elsewhere, b.bundles);
    assert.throws(
        () => carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' }),
        (error: unknown) => error instanceof BundleDiffers && error.message === differsWords(b.bundles),
    );
    assert.deepEqual(readdirSync(elsewhere), ['photos'], 'nothing is written behind the link');
});

test('a link at the version’s folder, even to a folder of the very same files, is a difference and is not followed', (t) => {
    const b = bench(t);
    const lookalike = join(b.root, 'lookalike');
    carriedBundle({ source: b.source, bundles: join(b.root, 'made'), version: '0.35.0' });
    renameSync(join(b.root, 'made', '0.35.0'), lookalike);
    mkdirSync(b.bundles, { recursive: true });
    linkFolder(lookalike, join(b.bundles, '0.35.0'));
    assert.throws(
        () => carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' }),
        (error: unknown) => error instanceof BundleDiffers && error.message === differsWords(join(b.bundles, '0.35.0')),
    );
});

test('the copy’s modes are the package’s masked to 0755: a group-writable jason in the package is not one in the copy', (t) => {
    const b = bench(t);
    chmodSync(join(b.source, 'jason'), 0o775);
    const carried = carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' });
    if (process.platform === 'win32') {
        t.diagnostic('Windows keeps no group bits; CI’s Linux leg reads the mode');
        return;
    }
    assert.equal(statSync(join(carried, 'jason')).mode & 0o777, 0o755);
});

test('making the copy removes nothing else: another version’s copy waits for the re-points', (t) => {
    const b = bench(t);
    plant(b.bundles, ['0.34.0']);
    carriedBundle({ source: b.source, bundles: b.bundles, version: '0.35.0' });
    assert.deepEqual(readdirSync(b.bundles).sort(), ['0.34.0', '0.35.0']);
});

test('after the re-points, the copies of other versions go, and this desktop’s own temporaries; this start’s, the rollback record’s and anything not named as a version stay', async (t) => {
    const b = bench(t);
    plant(b.bundles, ['0.33.0', '0.34.0', '0.35.0', '0.36.0', '.0.35.0.tmp-4242', 'notes']);
    writeFileSync(join(b.bundles, 'a-file'), 'a file');
    const r = removal(b.bundles, { keep: ['0.34.0'] });
    assert.deepEqual((await removeOtherCopies(r)).sort(), ['.0.35.0.tmp-4242', '0.33.0', '0.35.0']);
    assert.deepEqual(readdirSync(b.bundles).sort(), ['0.34.0', '0.36.0', 'a-file', 'notes']);
    assert.deepEqual(r.log.lines, [{ level: 'info', name: 'bundle_copies_removed', fields: { Count: 2 } }]);
});

test('a copy something the person set up still names stays: a re-point that failed leaves its copy', async (t) => {
    const b = bench(t);
    plant(b.bundles, ['0.34.0', '0.35.0', '0.36.0']);
    const r = removal(b.bundles, { named: async () => [join(b.bundles, '0.35.0', 'jason'), '/opt/other/jason'] });
    assert.deepEqual(await removeOtherCopies(r), ['0.34.0']);
    assert.deepEqual(readdirSync(b.bundles).sort(), ['0.35.0', '0.36.0']);
});

test('a copy the running runtime was started from stays, told by the own-runtime check', async (t) => {
    const b = bench(t);
    plant(b.bundles, ['0.34.0', '0.35.0', '0.36.0']);
    const asked: string[] = [];
    const r = removal(b.bundles, {
        whose: async (executable) => {
            asked.push(executable);
            return executable === join(b.bundles, '0.35.0', 'jason') ? 'ours' : 'another';
        },
    });
    assert.deepEqual(await removeOtherCopies(r), ['0.34.0']);
    assert.deepEqual(asked.sort(), [join(b.bundles, '0.34.0', 'jason'), join(b.bundles, '0.35.0', 'jason')]);
    assert.deepEqual(readdirSync(b.bundles).sort(), ['0.35.0', '0.36.0']);
});

test('where the running runtime or what is set up cannot be told, nothing is removed at this start', async (t) => {
    const b = bench(t);
    plant(b.bundles, ['0.34.0', '0.35.0', '0.36.0', '.0.34.0.tmp-7']);
    const unanswered = removal(b.bundles, { whose: async () => 'unanswered' });
    assert.deepEqual(await removeOtherCopies(unanswered), []);
    assert.deepEqual(unanswered.log.lines, [{ level: 'warn', name: 'bundle_copies_kept', fields: { Why: 'unanswered' } }]);
    const unread = removal(b.bundles, { named: async () => null });
    assert.deepEqual(await removeOtherCopies(unread), []);
    assert.deepEqual(unread.log.lines, [{ level: 'warn', name: 'bundle_copies_kept', fields: { Why: 'unread' } }]);
    assert.deepEqual(readdirSync(b.bundles).sort(), ['.0.34.0.tmp-7', '0.34.0', '0.35.0', '0.36.0']);
});

test('a link under the bundles folder, or at it, is never removed through: the folder it names keeps what it holds', async (t) => {
    const b = bench(t);
    const elsewhere = join(b.root, 'mnt', 'apps');
    mkdirSync(join(elsewhere, 'photos'), { recursive: true });
    writeFileSync(join(elsewhere, 'photos', 'a.jpg'), 'a photo');
    plant(b.bundles, ['0.36.0']);
    linkFolder(elsewhere, join(b.bundles, '0.34.0'));
    await removeOtherCopies(removal(b.bundles));
    assert.deepEqual(readdirSync(join(elsewhere, 'photos')), ['a.jpg']);

    const linked = join(b.root, 'linked-bundles');
    linkFolder(elsewhere, linked);
    assert.deepEqual(await removeOtherCopies(removal(linked)), []);
    assert.deepEqual(readdirSync(elsewhere), ['photos'], 'a bundles folder that is a link is not emptied');
});

test('a removal that fails is logged and stops nothing', async (t) => {
    const b = bench(t);
    plant(b.bundles, ['0.34.0', '0.35.0', '0.36.0']);
    const r = removal(b.bundles, {
        remove: (folder) => {
            if (folder.endsWith('0.34.0')) throw Object.assign(new Error('EACCES'), { code: 'EACCES' });
            rmSync(folder, { recursive: true, force: true });
        },
    });
    assert.deepEqual(await removeOtherCopies(r), ['0.35.0']);
    assert.deepEqual(r.log.lines, [
        { level: 'error', name: 'bundle_copy_removal_failed', fields: {} },
        { level: 'info', name: 'bundle_copies_removed', fields: { Count: 1 } },
    ]);
});

/** The data directory reached through a folder link, as a home on another disk or a linked ~/.jason is: a junction on Windows. */
function linkedData(t: { after(fn: () => void): void }): { bundles: string; real: string } {
    const root = realpathSync.native(mkdtempSync(join(tmpdir(), 'jason-desktop-bundle-linked-')));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const disk = join(root, 'disk', 'jason-data');
    mkdirSync(disk, { recursive: true });
    mkdirSync(join(root, 'home'));
    symlinkSync(disk, join(root, 'home', '.jason'), 'junction');
    return { bundles: join(root, 'home', '.jason', 'desktop', 'bundle'), real: join(disk, 'desktop', 'bundle') };
}

test('with the data directory reached through a link, a jason named by its resolved path is told as another version’s copy, to re-point', (t) => {
    const { bundles, real } = linkedData(t);
    plant(bundles, ['0.35.0', '0.36.0']);
    const current = join(bundles, '0.36.0', 'jason');
    assert.equal(isOtherCopy(join(real, '0.35.0', 'jason'), bundles, current), true, 'a start at logon reports the copy resolved');
    assert.equal(isOtherCopy(join(real, '0.36.0', 'jason'), bundles, current), false, 'this start’s copy, resolved, is not another');
});

test('with the data directory reached through a link, a copy something set up names by its resolved path stays', async (t) => {
    const { bundles, real } = linkedData(t);
    plant(bundles, ['0.34.0', '0.35.0', '0.36.0']);
    const r = removal(bundles, { named: async () => [join(real, '0.35.0', 'jason')] });
    assert.deepEqual(await removeOtherCopies(r), ['0.34.0']);
    assert.deepEqual(readdirSync(bundles).sort(), ['0.35.0', '0.36.0']);
});
