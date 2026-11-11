// desktop/THIRD-PARTY-NOTICES is what the lockfile and node_modules give: Electron with the Chromium note, and every
// package the application carries at run time with its own licence text. A dependency added or bumped without writing
// the file again is red here.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { notices } from '../scripts/notices.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const lock = JSON.parse(readFileSync(join(root, 'package-lock.json'), 'utf8')) as { packages: Record<string, { version?: string; dev?: boolean }> };

function current(): string | null {
    try {
        return readFileSync(join(root, 'THIRD-PARTY-NOTICES'), 'utf8');
    } catch {
        return null;
    }
}

test('desktop/THIRD-PARTY-NOTICES is what node scripts/notices.mjs writes', () => {
    assert.equal(current(), notices(), 'run node scripts/notices.mjs in desktop/ and commit the file');
});

test('the notices name Electron, Chromium’s licences, and every package the application carries at run time', () => {
    const text = notices();
    const electron = lock.packages['node_modules/electron']?.version;
    assert.ok(text.includes(`electron ${electron}: MIT`));
    assert.ok(text.includes('LICENSES.chromium.html'));
    const carried = Object.entries(lock.packages).filter(([path, entry]) => path.startsWith('node_modules/') && entry.dev !== true);
    assert.ok(carried.length > 0);
    for (const [path, entry] of carried) {
        const name = path.slice(path.lastIndexOf('node_modules/') + 'node_modules/'.length);
        assert.ok(text.includes(`\n${name} ${entry.version}: `), `${name} ${entry.version} is named`);
    }
    assert.ok(!text.includes('\r'), 'written with LF only');
});

test('a carried package that ships no licence file is refused unless it declares MIT and names its author', (t) => {
    const fixture = mkdtempSync(join(tmpdir(), 'jason-notices-'));
    t.after(() => rmSync(fixture, { recursive: true, force: true }));
    const place = (name: string, manifest: object, licence: string | null) => {
        const directory = join(fixture, 'node_modules', name);
        mkdirSync(directory, { recursive: true });
        writeFileSync(join(directory, 'package.json'), JSON.stringify({ name, ...manifest }));
        if (licence !== null) writeFileSync(join(directory, 'LICENSE'), licence);
    };
    writeFileSync(join(fixture, 'package-lock.json'), JSON.stringify({ packages: { '': {}, 'node_modules/electron': { version: '1.0.0', dev: true }, 'node_modules/plain': { version: '2.0.0' } } }));
    place('electron', { version: '1.0.0', license: 'MIT' }, 'Copyright (c) Electron contributors');

    place('plain', { version: '2.0.0', license: 'MIT', author: 'A. Author <a@example.com>' }, null);
    const text = notices(fixture);
    assert.ok(text.includes('names A. Author as its'));
    assert.ok(!text.includes('a@example.com'));
    assert.ok(!/Copyright \(c\) A\. Author/.test(text), 'no copyright line is made up');

    place('plain', { version: '2.0.0', license: 'ISC', author: 'A. Author' }, null);
    assert.throws(() => notices(fixture), /plain is carried by the desktop/);
    place('plain', { version: '2.0.0', license: 'MIT' }, null);
    assert.throws(() => notices(fixture), /plain is carried by the desktop/);
});
