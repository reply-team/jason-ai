// The copy an update keeps aside, made where the desktop makes it: under the Electron binary, whose file system reads an
// `.asar` as an archive. Plain Node, which runs every other row, reads the same file as a file, so only a row under the
// binary sees how the application's own folder, which holds `resources/app.asar`, is copied there.
//
// The binary is the electron package's own, installed by `node node_modules/electron/install.js`; nothing here downloads
// it. Where it is absent the row skips and says so, except where JASON_DESKTOP_ELECTRON_REQUIRED is 1, as in CI, where
// its absence fails the row.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { readRollback } from '../src/rollback.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');

/** The Electron binary the electron package installed, or null when it is not installed. */
function electronBinary(): string | null {
    const folder = join(root, 'node_modules', 'electron');
    try {
        const binary = join(folder, 'dist', readFileSync(join(folder, 'path.txt'), 'utf8').trim());
        return existsSync(binary) ? binary : null;
    } catch {
        return null;
    }
}

/**
 * An `.asar` archive in the format Electron reads (`@electron/asar`): a pickle holding the header's size, a pickle holding
 * the header's JSON, then the files' bytes at the offsets the header gives.
 */
function packAsar(files: Readonly<Record<string, string>>): Buffer {
    const bodies: Buffer[] = [];
    const entries: Record<string, { size: number; offset: string }> = {};
    let offset = 0;
    for (const [name, text] of Object.entries(files)) {
        const body = Buffer.from(text, 'utf8');
        entries[name] = { size: body.length, offset: String(offset) };
        bodies.push(body);
        offset += body.length;
    }
    const json = Buffer.from(JSON.stringify({ files: entries }), 'utf8');
    const padded = Math.ceil(json.length / 4) * 4;
    // The header pickle: its payload's size, then the string's length, the string, and padding to four bytes.
    const header = Buffer.alloc(8 + padded);
    header.writeUInt32LE(4 + padded, 0);
    header.writeInt32LE(json.length, 4);
    json.copy(header, 8);
    // The size pickle: its payload's size, four, then the header pickle's size.
    const size = Buffer.alloc(8);
    size.writeUInt32LE(4, 0);
    size.writeUInt32LE(header.length, 4);
    return Buffer.concat([size, header, ...bodies]);
}

test('under the Electron binary, an update keeps the application whole, its app.asar a file of the same bytes, and the kept copy reads as the application did', (t) => {
    const binary = electronBinary();
    if (binary === null) {
        assert.notEqual(process.env['JASON_DESKTOP_ELECTRON_REQUIRED'], '1', 'the Electron binary is not installed: node node_modules/electron/install.js installs it');
        t.skip('the Electron binary is not installed, so the copy under it was not run: node node_modules/electron/install.js installs it');
        return;
    }
    const scratch = mkdtempSync(join(tmpdir(), 'jason-desktop-keep-electron-'));
    t.after(() => rmSync(scratch, { recursive: true, force: true }));
    const application = join(scratch, 'Programs', 'Jason');
    const rollback = join(scratch, 'data', 'desktop', 'rollback');
    mkdirSync(join(application, 'resources', 'bundle'), { recursive: true });
    writeFileSync(join(application, 'Jason.exe'), 'the application 0.34.0');
    writeFileSync(join(application, 'resources', 'bundle', 'jason.exe'), 'the command line it carries');
    const archive = packAsar({ 'package.json': '{"name":"jason-desktop","main":"lib/main.js"}', 'main.js': 'the main process of 0.34.0' });
    writeFileSync(join(application, 'resources', 'app.asar'), archive);

    const run = spawnSync(binary, [join(root, 'test', 'stubs', 'keep-under-electron.mjs'), join(root, 'src', 'rollback.ts'), rollback, application, 'main.js'], {
        encoding: 'utf8',
        env: { ...process.env, ELECTRON_RUN_AS_NODE: '1', NODE_OPTIONS: '--no-warnings' },
        timeout: 60_000,
    });
    assert.equal(run.status, 0, `the Electron binary ran the keep: ${run.error?.message ?? run.stderr}`);
    const said = JSON.parse(run.stdout.trim().split('\n').at(-1) ?? '{}') as { electron: string | null; archiveRead: string | null; kept: string | null; error: string | null; keptRead: string | null };
    assert.ok(said.electron, 'it ran under Electron');
    assert.equal(said.archiveRead, 'the main process of 0.34.0', 'the fixture is an archive the binary reads as one');
    assert.equal(said.error, null, 'the copy was kept');

    const copy = join(rollback, '0.34.0', 'Jason');
    assert.equal(said.kept, copy);
    const kept = join(copy, 'resources', 'app.asar');
    assert.ok(statSync(kept).isFile(), 'app.asar is kept as a file, not taken apart into a folder');
    assert.equal(statSync(kept).size, archive.length);
    assert.deepEqual(readFileSync(kept), archive);
    assert.equal(readFileSync(join(copy, 'Jason.exe'), 'utf8'), 'the application 0.34.0');
    assert.equal(readFileSync(join(copy, 'resources', 'bundle', 'jason.exe'), 'utf8'), 'the command line it carries');
    assert.deepEqual(readdirSync(rollback).sort(), ['0.34.0', 'state.json']);
    assert.deepEqual(readRollback(rollback), { pending: { fromVersion: '0.34.0', toVersion: '0.35.0', directory: copy }, rejected: [] });
    // What is kept is what going back puts in place: the kept copy's archive reads as the application's did.
    assert.equal(said.keptRead, 'the main process of 0.34.0');
});
