// The data directory's layout as the desktop reads it: the table held to the one JasonPaths writes, the stamp read by the
// cases the runtime's reader is held to, and the paths that move answered by it.

import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
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
    findStamp,
    readLayoutStamp,
    stampMaxBytes,
    type LayoutParts,
    type StampDisk,
    type StampFound,
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
    on_disk?: 'missing' | 'folder' | 'oversized' | 'under_a_file' | 'dangling_link' | 'fifo' | 'device_link' | 'unaskable';
    /** The systems a case can be made on: a pipe, a device and a folder's mode are macOS's and Linux's, and root is refused no look. */
    only?: 'posix' | 'posix_unprivileged';
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

/** Whether this run can make the case. */
function applies(stamp: StampCase): boolean {
    if (stamp.only === undefined) return true;
    if (process.platform === 'win32') return false;
    return stamp.only === 'posix' || process.getuid?.() !== 0;
}

/** The cases this run makes: every one but those kept to systems it is not on. */
const made = fixture.cases.filter(applies);

/** A data directory holding the case, as the runtime's test makes it; the directory the stamp is read from. */
function holding(t: { after(fn: () => void): void }, stamp: StampCase): string {
    const data = scratch(t);
    const file = join(data, 'layout.json');
    switch (stamp.on_disk) {
        case 'missing':
            return data;
        case 'folder':
            mkdirSync(file);
            return data;
        case 'oversized':
            writeFileSync(file, `{"layout":1,"written_by":"0.42.0","x":"${'x'.repeat(stampMaxBytes)}"}`);
            return data;
        case 'under_a_file':
            writeFileSync(join(data, 'inner'), '');
            return join(data, 'inner');
        case 'dangling_link':
            symlinkSync(join(data, 'gone.json'), file);
            return data;
        case 'device_link':
            symlinkSync('/dev/null', file);
            return data;
        case 'fifo':
            execFileSync('mkfifo', [file]);
            return data;
        case 'unaskable': {
            const shut = join(data, 'shut');
            mkdirSync(shut);
            writeFileSync(join(shut, 'layout.json'), '{"layout":1,"written_by":"0.42.0"}');
            chmodSync(shut, 0o600);
            t.after(() => chmodSync(shut, 0o700));
            return shut;
        }
        case undefined:
            writeFileSync(file, stamp.base64 !== undefined ? Buffer.from(stamp.base64, 'base64') : (stamp.text ?? ''));
            return data;
    }
}

test('a stamp is read as the shared cases say: its state, layout, writer, moment and problem, and the layout it is answered by', (t) => {
    assert.ok(made.length > 30);
    for (const stamp of made) {
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
    for (const stamp of made) {
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

test('what is at the name is asked before anything is opened, and only nothing there is no stamp', (t) => {
    const path = join(resolve('/d'), 'layout.json');
    const never = () => assert.fail('nothing is opened');
    const at = (found: StampFound, read: StampDisk['read'] = never) => readLayoutStamp(resolve('/d'), { find: () => found, read });
    assert.deepEqual(at({ kind: 'none' }), { state: 'missing', path });
    assert.deepEqual(at({ kind: 'folder' }), { state: 'unreadable', path, problem: 'it is a folder' });
    assert.deepEqual(at({ kind: 'unaskable' }), { state: 'unreadable', path, problem: 'it could not be read' }, 'a name that cannot be asked about is never taken for none');
    assert.deepEqual(at({ kind: 'other' }), { state: 'unreadable', path, problem: 'it is empty, or not a file' }, 'a pipe or a device is never opened');
    assert.deepEqual(at({ kind: 'file', size: 0 }), { state: 'unreadable', path, problem: 'it is empty, or not a file' });
    assert.deepEqual(at({ kind: 'file', size: stampMaxBytes + 1 }), { state: 'unreadable', path, problem: 'it is larger than a stamp' });
    const closed = () => {
        throw Object.assign(new Error('EACCES'), { code: 'EACCES' });
    };
    assert.deepEqual(at({ kind: 'file', size: 10 }, closed), { state: 'unreadable', path, problem: 'it could not be read' });
    assert.deepEqual(at({ kind: 'file', size: 10 }, () => 'larger'), { state: 'unreadable', path, problem: 'it is larger than a stamp' }, 'a file that grew after it was asked');
    assert.equal(dataLayout(resolve('/d'), undefined, { find: () => ({ kind: 'unaskable' }), read: never }).layout, 1);

    // On disk: a stat that fails for any reason but "nothing there" is a stamp that cannot be read; a link or a junction
    // whose target is gone is one too, never none.
    const data = scratch(t);
    symlinkSync(join(data, 'gone'), join(data, 'layout.json'), 'junction');
    assert.deepEqual(readLayoutStamp(data), { state: 'unreadable', path: join(data, 'layout.json'), problem: 'it could not be read' });
    const asked: string[] = [];
    const errors: Record<string, string> = { ENOENT: 'none', ENOTDIR: 'none', EACCES: 'unaskable', ELOOP: 'unaskable', ENAMETOOLONG: 'unaskable', EPERM: 'unaskable' };
    for (const [code, kind] of Object.entries(errors)) {
        const found = findStamp(join(data, 'x'), {
            stat: () => {
                throw Object.assign(new Error(code), { code });
            },
            lstat: (path) => {
                asked.push(path);
                throw Object.assign(new Error('ENOENT'), { code: 'ENOENT' });
            },
        });
        assert.deepEqual(found, { kind }, code);
    }
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
    const given = dataLayout(data, 2, { find: () => assert.fail('a given layout reads no stamp'), read: () => assert.fail('a given layout reads no stamp') });
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
