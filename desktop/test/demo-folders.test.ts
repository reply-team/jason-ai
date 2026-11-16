// What an earlier version's demo left on disk and how the removal knows it: a folder is the demo's only when it carries
// the marker the desktop of 0.44 and earlier wrote into it, a copy a start over left is taken off with its marker last,
// and the request a start over left is still a record of the desktop's own.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { knownDesktopEntries } from '../src/chat-hosts.ts';
import { demoMarkerName, demoMarkerText, hasDemoMarker } from '../src/demo-folders.ts';
import { asidesOf, removeMarkedLast } from '../src/demo-over.ts';
import { desktopPaths } from '../src/paths.ts';

function scratch(t: { after(fn: () => void): void }): string {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-demo-folders-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    return root;
}

/** A folder as the desktop of 0.44 and earlier made it: there, and marked. */
function marked(folder: string): string {
    mkdirSync(folder, { recursive: true });
    writeFileSync(join(folder, demoMarkerName), demoMarkerText);
    return folder;
}

test('the marker is the one line the earlier desktop wrote, in a file of its name at the folder’s top', () => {
    assert.equal(demoMarkerName, '.jason-demo-folder');
    assert.equal(demoMarkerText, 'This folder is Jason’s demo, made by Jason Desktop. Remove What Jason Placed removes it.\n');
});

test('a folder is the demo’s only when it carries the marker exactly: not a file, not another text, not a folder without it', (t) => {
    const home = scratch(t);
    assert.equal(hasDemoMarker(marked(join(home, 'a-demo'))), true);
    const file = join(home, 'b-demo');
    writeFileSync(file, 'x');
    assert.equal(hasDemoMarker(file), false);
    const wrong = join(home, 'c-demo');
    mkdirSync(wrong);
    writeFileSync(join(wrong, demoMarkerName), 'somebody else wrote this');
    assert.equal(hasDemoMarker(wrong), false);
    assert.equal(readFileSync(join(wrong, demoMarkerName), 'utf8'), 'somebody else wrote this', 'the check writes nothing');
    const bare = join(home, 'd-demo');
    mkdirSync(bare);
    assert.equal(hasDemoMarker(bare), false);
    assert.equal(hasDemoMarker(join(home, 'absent-demo')), false);
    const markerFolder = join(home, 'e-demo');
    mkdirSync(join(markerFolder, demoMarkerName), { recursive: true });
    assert.equal(hasDemoMarker(markerFolder), false, 'a folder of the marker’s name is no marker');
});

test('a marker that is a link is no marker, wherever it points', (t) => {
    const home = scratch(t);
    const real = join(home, 'marker.txt');
    writeFileSync(real, demoMarkerText);
    const folder = join(home, 'f-demo');
    mkdirSync(folder);
    try {
        symlinkSync(real, join(folder, demoMarkerName), 'file');
    } catch {
        t.skip('this account cannot make a link here');
        return;
    }
    assert.equal(hasDemoMarker(folder), false);
});

test('a copy is removed with its marker last: a removal that stops part-way leaves it marked, and a later one finishes it', (t) => {
    const home = scratch(t);
    const copy = marked(join(home, 'jason-demo.removing-1'));
    mkdirSync(join(copy, 'a'));
    writeFileSync(join(copy, 'a', 'one.txt'), 'x');
    writeFileSync(join(copy, 'z.txt'), 'y');
    // The second entry cannot be removed (a file held open on Windows): the marker is still there.
    const taken: string[] = [];
    const holdZ = (path: string): void => {
        if (path.endsWith('z.txt')) throw new Error('EBUSY');
        taken.push(path);
        rmSync(path, { recursive: true, force: true });
    };
    assert.throws(() => removeMarkedLast(copy, holdZ), /EBUSY/);
    assert.ok(hasDemoMarker(copy), 'the marker is the last thing to go');
    assert.ok(!taken.some((path) => path.endsWith(demoMarkerName)));
    // Released: a later removal finishes it.
    removeMarkedLast(copy);
    assert.equal(existsSync(copy), false);
});

test('the copies a start over left beside a folder are found by name, and none where the parent cannot be read', (t) => {
    const home = scratch(t);
    const folder = join(home, '.jason-demo');
    marked(join(home, '.jason-demo.removing-1'));
    mkdirSync(join(home, '.jason-demo.removing-2-2'));
    mkdirSync(join(home, '.jason-demo-other'));
    mkdirSync(join(home, '.jason-demox.removing-3'));
    assert.deepEqual(asidesOf(folder).sort(), [join(home, '.jason-demo.removing-1'), join(home, '.jason-demo.removing-2-2')]);
    assert.deepEqual(asidesOf(join(home, 'absent', 'x')), []);
});

test('the request a start over left is a known entry of the person’s desktop folder, so the chat host is refused it', () => {
    const data = join(tmpdir(), 'someone', '.jason');
    assert.equal(desktopPaths(data).demoStartOver, join(data, 'desktop', 'demo-start-over'));
    assert.ok(knownDesktopEntries.includes('demo-start-over'));
});
