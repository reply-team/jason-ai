import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { carriedStore, diskFiles, movedVersionName, restoreAtStart, restoreCarried } from '../src/carried-restore.ts';
import { memoryLog } from './harness.ts';

const folder = () => mkdtempSync(join(tmpdir(), 'carried-restore-'));

/** An application folder with its bundle's version and no jason.exe, and the updater's store with the images given. */
function scratch(images: { request: string; version: string | null; image?: boolean }[]) {
    const root = folder();
    const bundle = join(root, 'Programs', 'jason-desktop', 'resources', 'bundle');
    mkdirSync(bundle, { recursive: true });
    writeFileSync(join(bundle, 'version.json'), '{"version":"0.49.0"}');
    const cache = join(root, 'jason-desktop-updater');
    for (const { request, version, image } of images) {
        const at = join(carriedStore(cache), request);
        mkdirSync(at, { recursive: true });
        if (image !== false) writeFileSync(join(at, 'jason.exe'), `image of ${version ?? 'nothing'}`);
        if (version !== null) writeFileSync(join(at, movedVersionName), version);
    }
    return { executable: join(bundle, 'jason.exe'), cache };
}

test('S1a: a start puts a missing carried jason.exe back from the updater’s store only where the folder is still the version the stored image came from', () => {
    // The store's form is the command line's: <cache>\jason-carried\<request>\jason.exe, jason.version beside it (§7).
    assert.equal(carriedStore(join('Local', 'jason-desktop-updater')), join('Local', 'jason-desktop-updater', 'jason-carried'));
    const cs = readFileSync(new URL('../../dotnet/src/Jason.Cli/Update/CarriedUpdate.cs', import.meta.url), 'utf8');
    assert.ok(cs.includes('Path.Combine("jason-desktop-updater", "jason-carried")'));
    assert.ok(cs.includes(`public const string MovedVersionName = "${movedVersionName}";`));

    // The image of the version the folder still is, a newline after it as a hand may leave: put back, through a file beside it.
    const s = scratch([{ request: 'b-other', version: '0.48.3' }, { request: 'a-this', version: '0.49.0\n' }]);
    const log = memoryLog();
    assert.deepEqual(restoreAtStart({ executable: s.executable, version: '0.49.0', updaterCache: s.cache, log }), { act: 'restored', from: join(carriedStore(s.cache), 'a-this', 'jason.exe') });
    assert.equal(readFileSync(s.executable, 'utf8'), 'image of 0.49.0\n');
    assert.deepEqual(readdirSync(join(s.executable, '..')).sort(), ['jason.exe', 'version.json'], 'no file beside it left');
    assert.deepEqual(log.lines, [{ level: 'info', name: 'carried_restored', fields: { Restored: true } }]);

    // There already: nothing is looked at or said.
    const quiet = memoryLog();
    assert.deepEqual(restoreAtStart({ executable: s.executable, version: '0.49.0', updaterCache: s.cache, log: quiet }), { act: 'present' });
    assert.deepEqual(quiet.lines, []);

    // Only an image of another version, one with no jason.version, or one whose image is gone: nothing put back.
    const other = scratch([{ request: 'r1', version: '0.48.3' }, { request: 'r2', version: null }, { request: 'r3', version: '0.49.0', image: false }]);
    assert.deepEqual(restoreAtStart({ executable: other.executable, version: '0.49.0', updaterCache: other.cache, log: memoryLog() }), { act: 'none' });
    assert.equal(existsSync(other.executable), false);
    // A bundle that names no version, or no store at all.
    assert.deepEqual(restoreCarried({ executable: other.executable, version: null, store: carriedStore(other.cache), files: diskFiles }), { act: 'none' });
    assert.deepEqual(restoreCarried({ executable: other.executable, version: '0.49.0', store: join(folder(), 'none'), files: diskFiles }), { act: 'none' });
    // A copy that fails is said, and nothing half-written is left as the carried file.
    const failing = scratch([{ request: 'r', version: '0.49.0' }]);
    const failedLog = memoryLog();
    const refusing = { ...diskFiles, copy: () => { throw new Error('denied'); } };
    assert.equal(restoreAtStart({ executable: failing.executable, version: '0.49.0', updaterCache: failing.cache, log: failedLog, files: refusing }).act, 'failed');
    assert.deepEqual(failedLog.lines, [{ level: 'info', name: 'carried_restored', fields: { Restored: false } }]);
    assert.equal(existsSync(failing.executable), false);

    // main looks at a windowed start on Windows, before the set-up is made, with the bundle's own version.
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const start = main.slice(main.indexOf('function startDesktop('));
    const look = start.indexOf("if (process.platform === 'win32' && app.isPackaged) {\n            restoreAtStart({ executable: join(bundleDirectory(), 'jason.exe'), version: bundleVersion(bundleDirectory()), updaterCache: updaterCache(process.env, homedir(), process.platform), log: early });");
    assert.ok(look > start.indexOf('const early = startLog();') && look < start.indexOf('machineSetup = makeMachineSetup(early);'));
});
