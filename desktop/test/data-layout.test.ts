// The data directory's layout as the desktop reads it: the table held to the one JasonPaths writes, the stamp read by the
// cases the runtime's reader is held to, and the paths that move answered by it.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
    dataLayout,
    layoutNewerCode,
    layoutRefusal,
    layoutTable,
    layoutUnreadableCode,
    newestLayout,
    readLayoutStamp,
    stampMaxBytes,
    type LayoutParts,
    type StampDisk,
} from '../src/paths.ts';

const here = dirname(fileURLToPath(import.meta.url));

interface Layouts {
    stamp: string;
    this_build_runs: number;
    own_entries: string[];
    layouts: Record<string, Record<string, string | boolean>>;
}

const written = JSON.parse(readFileSync(join(here, '..', 'data-layouts.json'), 'utf8')) as Layouts;

const snake = (name: string) => name.replace(/[A-Z]/g, (letter) => `_${letter.toLowerCase()}`);

function scratch(t: { after(fn: () => void): void }): string {
    const folder = mkdtempSync(join(tmpdir(), 'jason-layout-'));
    t.after(() => rmSync(folder, { recursive: true, force: true }));
    return folder;
}

test('the desktop’s table of the parts that move is the one data-layouts.json holds, layout by layout, and every other path there is the same in both layouts', () => {
    assert.equal(layoutTable.stamp, written.stamp);
    assert.equal(layoutTable.thisBuildRuns, written.this_build_runs);
    assert.deepEqual(layoutTable.ownEntries, written.own_entries);
    assert.deepEqual(Object.keys(layoutTable.layouts), Object.keys(written.layouts));
    assert.equal(newestLayout, Math.max(...Object.keys(written.layouts).map(Number)));
    const carried = new Set<string>();
    for (const [number, parts] of Object.entries(layoutTable.layouts) as [string, LayoutParts][]) {
        for (const [name, value] of Object.entries(parts)) {
            carried.add(snake(name));
            assert.equal(value, written.layouts[number]?.[snake(name)], `${name} in layout ${number}`);
        }
    }
    // A member the desktop does not carry is one JasonPaths places alike in every layout: the day one starts to move, the
    // desktop has a part to place by the stamp, and this is red until it does.
    const layouts = Object.values(written.layouts);
    for (const name of Object.keys(layouts[0] ?? {}).filter((name) => !carried.has(name))) {
        assert.equal(new Set(layouts.map((layout) => layout[name])).size, 1, `${name} moves between layouts, and the desktop does not place it`);
    }
});

test('a missing stamp is layout 1: every moving path is the one the desktop composed before the stamp', (t) => {
    const data = scratch(t);
    const layout = dataLayout(data);
    assert.deepEqual(layout.stamp, { state: 'missing', path: join(data, 'layout.json') });
    assert.equal(layout.layout, 1);
    assert.equal(layout.userSettingsFile, join(data, 'config', 'settings.json'));
    assert.equal(layout.secretsFile, join(data, 'config', 'settings.json'));
    assert.equal(layout.userSettingsFileHoldsSecrets, true);
    assert.equal(layout.pluginsDirectory, join(data, 'plugins'));
    assert.equal(layout.roleSkillsDirectory, join(data, 'skills', 'roles'));
    assert.equal(layout.skillsStagingDirectory, join(data, 'skills', '.staging'));
    assert.equal(layout.uiCustomDirectory, join(data, 'ui', 'custom'));
    assert.equal(layoutRefusal(layout, '0.41.0'), null);
});

interface StampCase {
    name: string;
    text?: string;
    base64?: string;
    on_disk?: 'missing' | 'folder' | 'oversized';
    state: 'missing' | 'read' | 'unreadable';
    layout?: number;
    written_by?: string;
    at?: string | null;
    problem?: string;
    desktop_problem?: string;
    answered: number;
    refusal: string | null;
}

const fixture = JSON.parse(readFileSync(join(here, 'fixtures', 'layout-stamps.json'), 'utf8')) as {
    current: string;
    sentences: Record<string, string>;
    cases: StampCase[];
};

/** A data directory holding the case, as the runtime's test makes it. */
function holding(t: { after(fn: () => void): void }, stamp: StampCase): string {
    const data = scratch(t);
    const file = join(data, 'layout.json');
    if (stamp.on_disk === 'folder') mkdirSync(file);
    else if (stamp.on_disk === 'oversized') writeFileSync(file, `{"layout":1,"written_by":"0.42.0","x":"${'x'.repeat(stampMaxBytes)}"}`);
    else if (stamp.on_disk === undefined) writeFileSync(file, stamp.base64 !== undefined ? Buffer.from(stamp.base64, 'base64') : (stamp.text ?? ''));
    return data;
}

test('a stamp is read as the shared cases say: its state, layout, writer, moment and problem, and the layout it is answered by', (t) => {
    assert.ok(fixture.cases.length > 30);
    for (const stamp of fixture.cases) {
        const data = holding(t, stamp);
        const read = readLayoutStamp(data);
        const path = join(data, 'layout.json');
        const expected =
            stamp.state === 'missing'
                ? { state: 'missing', path }
                : stamp.state === 'read'
                  ? { state: 'read', path, layout: stamp.layout, writtenBy: stamp.written_by, at: stamp.at ?? null }
                  : { state: 'unreadable', path, problem: stamp.desktop_problem ?? stamp.problem };
        assert.deepEqual(read, expected, stamp.name);
        assert.equal(dataLayout(data).layout, stamp.answered, stamp.name);
    }
});

test('a stamp naming a later layout refuses with layout_newer in its exact sentence, said for this desktop’s version, and one that cannot be read with layout_unreadable', (t) => {
    for (const stamp of fixture.cases) {
        const data = holding(t, stamp);
        const refusal = layoutRefusal(dataLayout(data), fixture.current);
        if (stamp.refusal === null) {
            assert.equal(refusal, null, stamp.name);
            continue;
        }
        const sentence = (fixture.sentences[stamp.refusal] ?? '')
            .replace('{written_by}', stamp.written_by ?? '')
            .replace('{current}', fixture.current)
            .replace('{path}', join(data, 'layout.json'))
            .replace('{problem}', stamp.desktop_problem ?? stamp.problem ?? '');
        assert.deepEqual(refusal, { code: stamp.refusal, message: sentence }, stamp.name);
    }
    assert.deepEqual(
        [layoutNewerCode, layoutUnreadableCode],
        Object.keys(fixture.sentences),
    );
    const data = scratch(t);
    writeFileSync(join(data, 'layout.json'), '{"layout":2,"written_by":"0.50.0"}');
    assert.equal(
        layoutRefusal(dataLayout(data), '0.41.0')?.message,
        'This data directory was set up by Jason 0.50.0, newer than this jason (0.41.0): update this jason, or use the one that set it up.',
    );
});

test('every layout above the one this build runs refuses, so no part is ever placed by a layout this build does not run', (t) => {
    for (let layout = layoutTable.thisBuildRuns + 1; layout <= newestLayout + 1; layout++) {
        const data = scratch(t);
        writeFileSync(join(data, 'layout.json'), JSON.stringify({ layout, written_by: '9.0.0' }));
        assert.equal(layoutRefusal(dataLayout(data), '0.41.0')?.code, layoutNewerCode, `layout ${layout}`);
    }
});

test('a stamp that cannot be read is recorded, never thrown: a folder, a file larger than a stamp, a file the system will not open', (t) => {
    const data = scratch(t);
    mkdirSync(join(data, 'layout.json'));
    assert.deepEqual(readLayoutStamp(data), { state: 'unreadable', path: join(data, 'layout.json'), problem: 'it is a folder' });
    const closed: StampDisk = {
        kind: () => 'file',
        read: () => {
            throw Object.assign(new Error('EACCES'), { code: 'EACCES' });
        },
    };
    assert.deepEqual(readLayoutStamp(resolve('/d'), closed), { state: 'unreadable', path: join(resolve('/d'), 'layout.json'), problem: 'it could not be read' });
    assert.deepEqual(dataLayout(resolve('/d'), undefined, closed).layout, 1);
    const larger: StampDisk = { kind: () => 'file', read: () => 'larger' };
    assert.deepEqual(readLayoutStamp(resolve('/d'), larger), { state: 'unreadable', path: join(resolve('/d'), 'layout.json'), problem: 'it is larger than a stamp' });
    const unaskable: StampDisk = { kind: () => 'none', read: () => assert.fail('nothing is read where nothing is') };
    assert.deepEqual(readLayoutStamp(resolve('/d'), unaskable), { state: 'missing', path: join(resolve('/d'), 'layout.json') });
});

test('the stamp is read at every call, never kept: a stamp written after a first answer is the next answer', (t) => {
    const data = scratch(t);
    assert.equal(dataLayout(data).layout, 1);
    writeFileSync(join(data, 'layout.json'), '{"layout":2,"written_by":"0.50.0"}');
    assert.equal(dataLayout(data).layout, 2);
    rmSync(join(data, 'layout.json'));
    assert.equal(dataLayout(data).stamp?.state, 'missing');
});

test('a layout given reads no stamp and refuses nothing, as JasonPaths given its layout', (t) => {
    const data = scratch(t);
    writeFileSync(join(data, 'layout.json'), '{"layout":3,"written_by":"9.0.0"}');
    const given = dataLayout(data, 2, { kind: () => assert.fail('a given layout reads no stamp'), read: () => assert.fail('a given layout reads no stamp') });
    assert.equal(given.stamp, null);
    assert.equal(layoutRefusal(given, '0.41.0'), null);
    assert.equal(given.userSettingsFile, join(data, 'setup', 'settings.json'));
    assert.equal(given.secretsFile, join(data, 'config', 'secrets.json'));
    assert.equal(given.userSettingsFileHoldsSecrets, false);
    assert.equal(given.pluginsDirectory, join(data, 'setup', 'plugins'));
    assert.equal(given.roleSkillsDirectory, join(data, 'setup', 'roles'));
    assert.equal(given.skillsStagingDirectory, join(data, 'setup', '.staging'));
    assert.equal(given.uiCustomDirectory, join(data, 'setup', 'ui'));
});
