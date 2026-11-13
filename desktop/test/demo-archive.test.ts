// The prepared demo: an archive the release made, placed by `jason import` at a demo's first start and at the start after
// Start the Demo Over, when it is fresh; the sample made at the start otherwise, as before. The import here is a stand-in
// that does what `jason import` does to the places it is given: a harness root holding anything, and a part of the data
// directory an import places, are moved aside first; then the parts and the root are written; then a runtime is started.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, renameSync, rmSync, utimesSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { crc32, deflateRawSync } from 'node:zlib';
import type { Cli, CliResult } from '../src/cli.ts';
import {
    archiveFreshDays,
    archiveSkewMs,
    beginPlacing,
    clearPlaced,
    demoArchiveOf,
    exportedAtOf,
    holdsNothing,
    PlacingFailed,
    placingLimitMs,
    placingPlan,
    projectsWhole,
    sampleUnprepared,
    settlePlacing,
    writeSampleWhole,
    type PlacingEnd,
    type PlacingOutcome,
} from '../src/demo-archive.ts';
import { claimDemoFolder, demoMarkerName, demoMarkerText, hasDemoMarker, markPlacedFolder } from '../src/demo-folders.ts';
import { asideMarker, carryOutOnDisk, CarryOutRefused, carryOutStartOver, dropStartOver, importedBy, leftByAMaking, leftByAPlacing, leftRuntimeStops, noteLeftStopped, placingInFlight, requestStartOver } from '../src/demo-over.ts';
import { stopDemoRuntime, stopLeftDemoRuntime } from '../src/demo-runtime.ts';
import { cliAt } from '../src/cli.ts';
import { markerPath, readMarker, type DemoPlaces } from '../src/demo-seed.ts';
import { fileLines, LogHold } from '../src/log.ts';

const day = 24 * 60 * 60 * 1000;
const now = new Date('2026-12-01T12:00:00.000Z');

/** A zip as an export writes one: each entry deflated or stored, a central directory, the end record. */
function zip(entries: { name: string; data: Buffer; stored?: boolean }[]): Buffer {
    const locals: Buffer[] = [];
    const central: Buffer[] = [];
    let offset = 0;
    for (const entry of entries) {
        const body = entry.stored ? entry.data : deflateRawSync(entry.data);
        const name = Buffer.from(entry.name, 'utf8');
        const local = Buffer.alloc(30);
        local.writeUInt32LE(0x04034b50, 0);
        local.writeUInt16LE(20, 4);
        local.writeUInt16LE(entry.stored ? 0 : 8, 8);
        local.writeUInt32LE(crc32(entry.data), 14);
        local.writeUInt32LE(body.length, 18);
        local.writeUInt32LE(entry.data.length, 22);
        local.writeUInt16LE(name.length, 26);
        const header = Buffer.alloc(46);
        header.writeUInt32LE(0x02014b50, 0);
        header.writeUInt16LE(20, 4);
        header.writeUInt16LE(20, 6);
        header.writeUInt16LE(entry.stored ? 0 : 8, 10);
        header.writeUInt32LE(crc32(entry.data), 16);
        header.writeUInt32LE(body.length, 20);
        header.writeUInt32LE(entry.data.length, 24);
        header.writeUInt16LE(name.length, 28);
        header.writeUInt32LE(offset, 42);
        locals.push(local, name, body);
        central.push(header, name);
        offset += local.length + name.length + body.length;
    }
    const directory = Buffer.concat(central);
    const end = Buffer.alloc(22);
    end.writeUInt32LE(0x06054b50, 0);
    end.writeUInt16LE(entries.length, 8);
    end.writeUInt16LE(entries.length, 10);
    end.writeUInt32LE(directory.length, 12);
    end.writeUInt32LE(offset, 16);
    return Buffer.concat([...locals, directory, end]);
}

/** The record an export writes, with the moment it names. */
const record = (exportedAt: string | undefined): Buffer =>
    Buffer.from(JSON.stringify({ format: 1, jason_version: '0.41.0', ...(exportedAt === undefined ? {} : { exported_at: exportedAt }), operating_system: 'linux', entries: [], directories: [] }));

function scratch(t: { after(fn: () => void): void }): string {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-demo-archive-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    return root;
}

/** A bundle carrying an archive made at `exportedAt`, and the demo's two places beside a person's, none of them made. */
function world(t: { after(fn: () => void): void }, exportedAt = new Date(now.getTime() - 3 * day).toISOString()) {
    const root = scratch(t);
    const bundle = join(root, 'bundle');
    const archive = demoArchiveOf(bundle);
    mkdirSync(join(bundle, 'demo'), { recursive: true });
    writeFileSync(archive, zip([{ name: 'data/config/settings.json', data: Buffer.from('{}') }, { name: 'export.json', data: record(exportedAt) }]));
    const places: DemoPlaces = { data: join(root, '.jason-demo'), harnessRoot: join(root, 'Jason-demo') };
    const request = join(root, '.jason', 'desktop', 'demo-start-over');
    return { root, bundle, archive, places, request };
}

test('the archive is the one the package carries, under bundle/demo', () => {
    assert.equal(demoArchiveOf('b'), join('b', 'demo', 'jason-demo.zip'));
});

test('the archive’s moment is read from its own record, deflated or stored, and never from the file’s date', (t) => {
    const w = world(t, '2026-11-13T09:01:02.1234567+00:00');
    assert.equal(exportedAtOf(w.archive).toISOString(), '2026-11-13T09:01:02.123Z');
    writeFileSync(w.archive, zip([{ name: 'export.json', data: record('2026-11-20T00:00:00Z'), stored: true }]));
    assert.equal(exportedAtOf(w.archive).toISOString(), '2026-11-20T00:00:00.000Z');
    // A record thirty days old in a file written a moment ago is stale; a fresh one in a file dated long ago is placed.
    writeFileSync(w.archive, zip([{ name: 'export.json', data: record(new Date(now.getTime() - 30 * day).toISOString()) }]));
    utimesSync(w.archive, now, now);
    assert.deepEqual(placingPlan({ archive: w.archive, places: w.places, now }), { kind: 'make', step: 'archive_stale' });
    const fresh = new Date(now.getTime() - day);
    writeFileSync(w.archive, zip([{ name: 'export.json', data: record(fresh.toISOString()) }]));
    const longAgo = new Date(now.getTime() - 400 * day);
    utimesSync(w.archive, longAgo, longAgo);
    assert.deepEqual(placingPlan({ archive: w.archive, places: w.places, now }), { kind: 'place', archive: w.archive, exportedAt: fresh });
});

test('a fresh archive is placed up to twenty-one days after it was made, and not a moment later', (t) => {
    const w = world(t);
    assert.equal(archiveFreshDays, 21);
    const at = (age: number) => {
        writeFileSync(w.archive, zip([{ name: 'export.json', data: record(new Date(now.getTime() - age).toISOString()) }]));
        return placingPlan({ archive: w.archive, places: w.places, now }).kind;
    };
    assert.equal(at(0), 'place');
    assert.equal(at(21 * day), 'place');
    assert.equal(at(21 * day + 1), 'make');
});

test('a missing or broken archive is not placed, and says which by a step', (t) => {
    const w = world(t);
    const plan = () => placingPlan({ archive: w.archive, places: w.places, now });
    const broken: [string, Buffer][] = [
        ['not a zip', Buffer.from('this is not an archive')],
        ['cut short', readFileSync(w.archive).subarray(0, 40)],
        ['no record', zip([{ name: 'data/config/settings.json', data: Buffer.from('{}') }])],
        ['a record with no moment', zip([{ name: 'export.json', data: record(undefined) }])],
        ['a record that is not JSON', zip([{ name: 'export.json', data: Buffer.from('{ not json') }])],
        ['a moment that is no moment', zip([{ name: 'export.json', data: record('yesterday') }])],
    ];
    // A record whose bytes do not match its checksum.
    const damaged = zip([{ name: 'export.json', data: record(now.toISOString()), stored: true }]);
    // The local header is thirty bytes and the name eleven: the body begins at the forty-first.
    damaged[45] = damaged[45]! ^ 0xff;
    broken.push(['a damaged record', damaged]);
    for (const [what, bytes] of broken) {
        writeFileSync(w.archive, bytes);
        assert.deepEqual(plan(), { kind: 'make', step: 'archive_unreadable' }, what);
    }
    rmSync(w.archive);
    assert.deepEqual(plan(), { kind: 'make', step: 'archive_missing' });
});

test('an archive is placed only into a demo that holds nothing, and never over a harness root that is there', (t) => {
    const w = world(t);
    const plan = () => placingPlan({ archive: w.archive, places: w.places, now }).kind;
    assert.equal(holdsNothing(w.places.data), true, 'no data directory yet');
    assert.equal(plan(), 'place');
    claimDemoFolder(w.places.data);
    mkdirSync(join(w.places.data, 'desktop', 'electron'), { recursive: true });
    assert.equal(plan(), 'place', 'the desktop’s marker and its own folder are nothing an import places');
    // A demo that holds a sample, or anything of an installation, is started as any demo: nothing is placed or said.
    for (const part of ['demo-seed.json', 'state', 'config', 'plugins', 'logs']) {
        const path = join(w.places.data, part);
        if (part.endsWith('.json')) writeFileSync(path, '{}');
        else mkdirSync(path);
        assert.deepEqual(placingPlan({ archive: w.archive, places: w.places, now }), { kind: 'none' }, part);
        rmSync(path, { recursive: true });
    }
    // A harness root that is there, marked or not, would be moved aside by the import: the sample is made instead.
    mkdirSync(w.places.harnessRoot);
    assert.deepEqual(placingPlan({ archive: w.archive, places: w.places, now }), { kind: 'make', step: 'root_present' });
});

/** What the stand-in import saw and did. */
interface Seen {
    args: readonly string[] | null;
    /** At the moment the import began: whether the log's folder and the harness root were there, and what the data directory held. */
    logsThere: boolean | null;
    rootThere: boolean | null;
    data: string[] | null;
    asides: string[];
    started: boolean;
}

/**
 * `jason import` as it treats its places (`ImportCommand`): a root that holds anything goes aside to `<root>.replaced-*`,
 * a part present goes aside into `<data>/replaced-*`; then the parts and the root are written and a runtime started.
 * `fails` ends it with the verb's error code before anything is written, or after the parts and before the start.
 */
function importer(places: DemoPlaces, fails: 'before' | 'after' | null = null, marked = true) {
    const seen: Seen = { args: null, logsThere: null, rootThere: null, data: null, asides: [], started: false };
    const refusal = (code: string): CliResult => ({ exit: 1, stdout: JSON.stringify({ error: { code, message: `nothing at ${places.data} was written`, retryable: false } }), stderr: '' });
    const cli: Cli = {
        async run(args) {
            seen.args = args;
            seen.logsThere = existsSync(join(places.data, 'logs'));
            seen.rootThere = existsSync(places.harnessRoot);
            seen.data = readdirSync(places.data).sort();
            if (fails === 'before') return refusal('import_refused');
            if (existsSync(places.harnessRoot) && readdirSync(places.harnessRoot).length > 0) {
                renameSync(places.harnessRoot, `${places.harnessRoot}.replaced-20261201T120000Z`);
                seen.asides.push('root');
            }
            for (const part of ['state', 'config', 'plugins', 'logs']) {
                if (!existsSync(join(places.data, part))) continue;
                mkdirSync(join(places.data, 'replaced-20261201T120000Z'), { recursive: true });
                renameSync(join(places.data, part), join(places.data, 'replaced-20261201T120000Z', part));
                seen.asides.push(part);
            }
            mkdirSync(join(places.data, 'state'), { recursive: true });
            writeFileSync(join(places.data, 'state', 'jason.db'), 'db');
            mkdirSync(join(places.data, 'config'), { recursive: true });
            writeFileSync(join(places.data, 'config', 'settings.json'), JSON.stringify({ Routes: { Default: { Plugin: 'demo' } } }));
            mkdirSync(join(places.harnessRoot, 'projects', 'sample'), { recursive: true });
            writeFileSync(join(places.harnessRoot, 'projects', 'sample', 'AGENTS.md'), '');
            // The root as an export carries it: the demo's, with the marker it was given where it was made.
            if (marked) writeFileSync(join(places.harnessRoot, demoMarkerName), demoMarkerText);
            if (fails === 'after') return refusal('runtime_start_failed');
            seen.started = true;
            return { exit: 0, stdout: '{}', stderr: '' };
        },
    };
    return { cli, seen };
}

/** One placing start over the disk: the plan made, the data directory claimed, the import begun and ended, the log read back. */
async function placeOn(
    w: ReturnType<typeof world>,
    cli: Cli,
    over: Partial<PlacingEnd> = {},
): Promise<{ outcome: 'placed' | 'made' | PlacingFailed; lines: Record<string, unknown>[]; order: string[] }> {
    const plan = placingPlan({ archive: w.archive, places: w.places, now });
    assert.equal(plan.kind, 'place');
    claimDemoFolder(w.places.data);
    const hold = new LogHold();
    const logs = join(w.places.data, 'logs');
    const order: string[] = [];
    let clock = 0;
    const placing = beginPlacing({
        cli,
        archive: w.archive,
        places: w.places,
        request: () => requestStartOver(w.request, 4242, true, true),
        hold: () => hold.hold(),
        now: () => (clock += 1000),
    });
    // Said while the import runs, as the window's start and a second launch say them.
    const log = hold.logger(() => fileLines(logs));
    log.info('desktop_started', { Version: '0.41.0', Platform: 'linux', DataDirectory: w.places.data });
    log.info('deep_link_opened', { Page: 'feed' });
    let outcome: 'placed' | 'made' | PlacingFailed;
    try {
        outcome = await settlePlacing(placing, {
            log,
            release: () => (order.push('release'), hold.release()),
            linked: async () => (order.push('linked'), null),
            markRoot: () => (order.push('markRoot'), markPlacedFolder(w.places.harnessRoot)),
            seal: () => (order.push('seal'), writeSampleWhole(w.places.data)),
            drop: () => (order.push('drop'), dropStartOver(w.request, log)),
            runtimeAlive: () => false,
            stopRuntime: async () => (order.push('stop'), true),
            clear: () => (order.push('clear'), clearPlaced(w.places)),
            prepareLive: () => {
                order.push('prepareLive');
                // The making's own preparation, on a demo the fall-back emptied: the root is made and marked here, as before.
                assert.equal(existsSync(w.places.harnessRoot), false, 'the root the import made is gone before the making');
                assert.equal(existsSync(join(w.places.data, 'state')), false, 'and so is what it placed');
                claimDemoFolder(w.places.harnessRoot);
            },
            ...over,
        });
    } catch (error) {
        if (!(error instanceof PlacingFailed)) throw error;
        outcome = error;
    }
    const lines = existsSync(logs)
        ? readdirSync(logs).flatMap((name) => readFileSync(join(logs, name), 'utf8').split('\n').filter(Boolean)).map((line) => JSON.parse(line) as Record<string, unknown>)
        : [];
    return { outcome, lines, order };
}

const seal = (data: string) => readMarker({ readText: (file) => (existsSync(file) ? readFileSync(file, 'utf8') : null), markerFile: markerPath(data) });

test('a fresh archive is placed by jason import, before any log line reaches the disk and into a harness root that is not there', async (t) => {
    const w = world(t);
    // An archive whose root carries no marker: the end marks it.
    const { cli, seen } = importer(w.places, null, false);
    const { outcome, lines, order } = await placeOn(w, cli);
    assert.equal(outcome, 'placed');
    assert.deepEqual(seen.args, ['import', w.archive, '--root', w.places.harnessRoot]);
    assert.equal(seen.logsThere, false, 'no log line was written while the import ran');
    assert.equal(seen.rootThere, false, 'the harness root is not made before the import');
    assert.deepEqual(seen.data, [demoMarkerName], 'the data directory holds the marker alone');
    assert.deepEqual(seen.asides, [], 'nothing was moved aside');
    // Placed and sealed: the root is the demo's, the sample is whole, the request is gone.
    assert.ok(hasDemoMarker(w.places.harnessRoot));
    assert.deepEqual(seal(w.places.data), { version: 1, complete: true });
    assert.equal(existsSync(w.request), false);
    assert.deepEqual(order, ['linked', 'markRoot', 'seal', 'drop', 'release']);
    // The lines said during the import are written after it, in order, then the placing's own.
    assert.deepEqual(lines.map((line) => line.EventName), ['desktop_started', 'deep_link_opened', 'demo_archive_placed']);
    assert.equal(lines[2]?.ElapsedMs, 1000);
    assert.ok(String(lines[0]?.['@t']) <= String(lines[2]?.['@t']));
    assert.equal(readdirSync(w.root).filter((name) => name.includes('.replaced-')).length, 0);
});

test('the request left for a placing names it, so a start cut short during the import is started over at the next', async (t) => {
    const w = world(t);
    let during = '';
    const cli: Cli = {
        async run() {
            during = readFileSync(w.request, 'utf8');
            return { exit: 0, stdout: '{}', stderr: '' };
        },
    };
    const placing = beginPlacing({ cli, archive: w.archive, places: w.places, request: () => requestStartOver(w.request, 4242, true, true), hold: () => undefined });
    assert.equal((await placing).kind, 'placed');
    assert.ok(leftByAPlacing(during));
    // A request that cannot be left places nothing: no hold, no import.
    let ran = false;
    let held = false;
    const unrequested = await beginPlacing({
        cli: { run: async () => ((ran = true), { exit: 0, stdout: '', stderr: '' }) },
        archive: w.archive,
        places: w.places,
        request: () => {
            throw Object.assign(new Error('read-only'), { code: 'EROFS' });
        },
        hold: () => void (held = true),
    });
    assert.deepEqual(unrequested, { kind: 'failed', step: 'request', code: 'EROFS' });
    assert.equal(ran || held, false);
});

test('a refused import makes the sample at this start: the held lines first, then the step, never a path', async (t) => {
    const w = world(t);
    const { cli } = importer(w.places, 'before');
    const { outcome, lines, order } = await placeOn(w, cli);
    assert.equal(outcome, 'made');
    assert.deepEqual(order, ['clear', 'release', 'prepareLive']);
    assert.equal(existsSync(markerPath(w.places.data)), false, 'no sample marker is written without a placing');
    assert.deepEqual(lines.map((line) => line.EventName), ['desktop_started', 'deep_link_opened', 'demo_archive_unplaced']);
    assert.equal(lines[2]?.Step, 'import');
    assert.equal(lines[2]?.Code, 'import_refused');
    assert.equal(JSON.stringify(lines).includes(w.root), false, 'no line names a path of the demo');
    // The making's own request stays, as the making leaves one; the root is the making's, marked.
    assert.ok(hasDemoMarker(w.places.harnessRoot));
});

test('an import that placed and then failed is taken away whole before the sample is made', async (t) => {
    const w = world(t);
    const { cli } = importer(w.places, 'after');
    const { outcome, lines } = await placeOn(w, cli);
    assert.equal(outcome, 'made');
    assert.equal(existsSync(markerPath(w.places.data)), false);
    assert.equal(lines.at(-1)?.Code, 'runtime_start_failed');
    assert.deepEqual(readdirSync(w.places.data).sort(), [demoMarkerName, 'logs']);
});

test('a placed demo whose runtime cannot be stopped, or whose fall-back cannot be prepared, fails the start and says how to start over', async (t) => {
    for (const over of [
        { runtimeAlive: () => true, stopRuntime: async () => false },
        {
            prepareLive: () => {
                throw new Error('a folder of the demo’s name exists that this application did not make');
            },
        },
    ] satisfies Partial<PlacingEnd>[]) {
        const w = world(t);
        const { cli } = importer(w.places, 'after');
        const { outcome, lines } = await placeOn(w, cli, over);
        assert.ok(outcome instanceof PlacingFailed);
        assert.equal(outcome.message, sampleUnprepared);
        assert.deepEqual(lines.slice(0, 3).map((line) => line.EventName), ['desktop_started', 'deep_link_opened', 'demo_archive_unplaced']);
        assert.equal(lines.at(-1)?.EventName, 'demo_seed_failed');
        assert.equal(existsSync(markerPath(w.places.data)), false);
        assert.ok(existsSync(w.request), 'the request stays, so the next start begins it again');
    }
});

test('a placed archive whose root cannot be marked is not sealed, and the sample is made instead', async (t) => {
    const w = world(t);
    const { cli } = importer(w.places);
    const { outcome, lines, order } = await placeOn(w, cli, { markRoot: () => { throw Object.assign(new Error('no'), { code: 'EACCES' }); } });
    assert.equal(outcome, 'made');
    assert.equal(order.includes('seal'), false);
    assert.equal(lines.at(-1)?.Step, 'root');
    assert.equal(lines.at(-1)?.Code, 'EACCES');
});

test('Start the Demo Over removes both folders and the archive is placed again, leaving no folder replaced beside the root', async (t) => {
    const w = world(t);
    // A demo placed before and used since: both folders the demo's, a sample in them.
    {
        const { cli } = importer(w.places);
        assert.equal((await placeOn(w, cli)).outcome, 'placed');
    }
    writeFileSync(join(w.places.harnessRoot, 'projects', 'sample', 'notes.md'), 'what the person wrote');
    // Start the Demo Over: its request, whose run has ended; the next start carries it out before anything names the demo.
    requestStartOver(w.request, 4242);
    carryOutStartOver({ ...carryOutOnDisk(w.request, w.places, () => false), runtimeAlive: () => false, exists: existsSync, marked: hasDemoMarker });
    assert.equal(existsSync(w.places.data) || existsSync(w.places.harnessRoot), false);
    const { cli, seen } = importer(w.places);
    assert.equal((await placeOn(w, cli)).outcome, 'placed');
    assert.equal(seen.rootThere, false);
    assert.deepEqual(seen.asides, []);
    assert.deepEqual(readdirSync(w.root).filter((name) => name.includes('.replaced-') || name.includes('.removing-')), []);
    assert.equal(readdirSync(w.places.data).some((name) => name.startsWith('replaced-')), false);
    assert.equal(existsSync(join(w.places.harnessRoot, 'projects', 'sample', 'notes.md')), false, 'what was done in the demo is gone');
    assert.deepEqual(seal(w.places.data), { version: 1, complete: true });
});

test('a start cut short during the import leaves a root without the marker, which the next start takes away as the demo’s', (t) => {
    const w = world(t);
    claimDemoFolder(w.places.data);
    // The import made the root and the desktop ended before marking it; its request names the placing.
    mkdirSync(join(w.places.harnessRoot, 'projects'), { recursive: true });
    requestStartOver(w.request, 4242, true, true);
    carryOutStartOver({ ...carryOutOnDisk(w.request, w.places, () => false), runtimeAlive: () => false, exists: existsSync, marked: hasDemoMarker });
    assert.equal(existsSync(w.places.harnessRoot), false);
    assert.equal(existsSync(w.places.data), false);
    // A request that names no placing still leaves an unmarked root alone, and so does one whose data directory is not the demo's.
    for (const placing of [false, true]) {
        mkdirSync(join(w.places.harnessRoot, 'projects'), { recursive: true });
        if (!placing) claimDemoFolder(w.places.data);
        else mkdirSync(w.places.data, { recursive: true });
        requestStartOver(w.request, 4242, false, placing);
        assert.throws(
            () => carryOutStartOver({ ...carryOutOnDisk(w.request, w.places, () => false), runtimeAlive: () => false, exists: existsSync, marked: hasDemoMarker }),
            (error: unknown) => error instanceof CarryOutRefused && error.reason === 'foreign',
        );
        assert.ok(existsSync(w.places.harnessRoot));
        rmSync(w.places.harnessRoot, { recursive: true });
        rmSync(w.places.data, { recursive: true });
    }
});

test('the root an import made is marked only where it is a folder of its own', (t) => {
    const root = scratch(t);
    const folder = join(root, 'Jason-demo');
    mkdirSync(folder);
    markPlacedFolder(folder);
    assert.ok(hasDemoMarker(folder));
    // The archive's root carries the marker it had where it was made: it is kept as it is.
    assert.doesNotThrow(() => markPlacedFolder(folder));
    assert.ok(hasDemoMarker(folder));
    // A file of the marker's name that is not the marker is made the marker: the folder is the import's own.
    writeFileSync(join(folder, demoMarkerName), 'something else');
    markPlacedFolder(folder);
    assert.ok(hasDemoMarker(folder));
    writeFileSync(join(root, 'file'), '');
    assert.throws(() => markPlacedFolder(join(root, 'file')));
});

test('a placing that fails before it settles is ended as failed, never left waiting', async () => {
    const failing = Promise.reject(new Error('x')) as Promise<PlacingOutcome>;
    const order: string[] = [];
    const hold = new LogHold();
    const outcome = await settlePlacing(failing, {
        log: hold.logger(() => () => undefined),
        release: () => order.push('release'),
        linked: async () => (order.push('linked'), null),
        markRoot: () => order.push('markRoot'),
        seal: () => order.push('seal'),
        drop: () => order.push('drop'),
        runtimeAlive: () => false,
        stopRuntime: async () => true,
        clear: () => order.push('clear'),
        prepareLive: () => order.push('prepareLive'),
    });
    assert.equal(outcome, 'made');
    assert.deepEqual(order, ['clear', 'release', 'prepareLive']);
});

test('a second start while another run places the demo carries nothing out and prepares nothing', (t) => {
    const w = world(t);
    claimDemoFolder(w.places.data);
    mkdirSync(join(w.places.harnessRoot, 'projects'), { recursive: true });
    requestStartOver(w.request, 4242, true, true);
    const text = readFileSync(w.request, 'utf8');
    assert.ok(leftByAMaking(text) && leftByAPlacing(text));
    // The placing run is alive: the carry-out leaves everything, without waiting, and the start prepares nothing.
    let waited = 0;
    carryOutStartOver({ ...carryOutOnDisk(w.request, w.places, (pid) => pid === 4242), runtimeAlive: () => false, wait: () => void (waited += 1), exists: existsSync, marked: hasDemoMarker });
    assert.equal(waited, 0);
    assert.ok(existsSync(w.request) && existsSync(w.places.harnessRoot));
    assert.equal(placingInFlight(text, 1, (pid) => pid === 4242), true);
    // Not while that run has gone, nor for the run itself, nor for a request that names no placing.
    assert.equal(placingInFlight(text, 1, () => false), false);
    assert.equal(placingInFlight(text, 4242, () => true), false);
    assert.equal(placingInFlight(['pid=4242', 'making', ''].join('\n'), 1, () => true), false);
    assert.equal(placingInFlight(null, 1, () => true), false);
});

/** `jason project list` as the runtime answers it, page by page, each page's projects as given. */
function projectList(pages: { id: string; default?: boolean; folder_path: string | null }[][], exit = 0) {
    const asked: (readonly string[])[] = [];
    const cli: Cli = {
        async run(args) {
            asked.push(args);
            if (exit !== 0) return { exit, stdout: JSON.stringify({ error: { code: 'no_descriptor', message: 'no runtime', retryable: true } }), stderr: '' };
            const at = args.includes('--cursor') ? Number(args[args.indexOf('--cursor') + 1]) : 0;
            const items = (pages[at] ?? []).map((project) => ({ name: project.id, slug: project.id, default: project.default ?? false, ...project }));
            return { exit: 0, stdout: JSON.stringify({ items, next_cursor: at + 1 < pages.length ? String(at + 1) : null }), stderr: '' };
        },
    };
    return { cli, asked };
}

test('a placing is whole only when every project the archive carried is recorded at a folder under the demo’s root', async (t) => {
    const root = join(scratch(t), 'Jason-demo');
    const under = (name: string) => join(root, 'projects', name);
    // Whole: the sample's project in the root, and the default project, which has no folder until one is needed.
    const whole = projectList([[{ id: 'prj_d', default: true, folder_path: null }, { id: 'prj_1', folder_path: under('sample') }]]);
    assert.equal(await projectsWhole(whole.cli, root), null);
    assert.deepEqual(whole.asked[0], ['project', 'list', '--limit', '1000']);
    // Every page is read.
    const paged = projectList([[{ id: 'prj_1', folder_path: under('a') }], [{ id: 'prj_2', folder_path: join(scratch(t), 'build', 'Jason-demo', 'projects', 'b') }]]);
    assert.equal(await projectsWhole(paged.cli, root), 'unlinked');
    assert.deepEqual(paged.asked[1], ['project', 'list', '--limit', '1000', '--cursor', '1']);
    // A project left at the folder it was recorded at where the archive was made, one with no folder at all, a folder
    // beside the root whose name only begins as the root's does: none is under the root.
    for (const folder of [join(scratch(t), 'build-home', 'Jason-demo', 'projects', 'sample'), null, `${root}-other${join('/', 'projects', 'x')}`]) {
        assert.equal(await projectsWhole(projectList([[{ id: 'prj_1', folder_path: folder }]]).cli, root), 'unlinked', String(folder));
    }
    // A list the runtime does not answer is no proof either: its code is said.
    assert.equal(await projectsWhole(projectList([], 3).cli, root), 'no_descriptor');
});

test('a placing whose projects are not all linked is a failed one: its runtime is stopped first, then what it placed is taken away, and the sample made', async (t) => {
    const w = world(t);
    const { cli } = importer(w.places);
    let alive = true;
    const { outcome, lines, order } = await placeOn(w, cli, {
        linked: async () => 'unlinked',
        runtimeAlive: () => alive,
        stopRuntime: async () => {
            alive = false;
            // The stop comes before anything is taken away.
            assert.ok(existsSync(join(w.places.data, 'state', 'jason.db')) && existsSync(w.places.harnessRoot));
            return true;
        },
    });
    assert.equal(outcome, 'made');
    assert.deepEqual(order, ['clear', 'release', 'prepareLive']);
    assert.equal(alive, false);
    assert.equal(existsSync(markerPath(w.places.data)), false, 'no sample marker for a half-linked placing');
    assert.equal(lines.at(-1)?.EventName, 'demo_archive_unplaced');
    assert.equal(lines.at(-1)?.Step, 'unlinked');
    assert.equal(lines.at(-1)?.Code, 'none');
});

test('an archive whose moment is ahead of this machine’s clock is placed within a day of skew, and is stale beyond it', (t) => {
    const w = world(t);
    assert.equal(archiveSkewMs, day);
    const ahead = (by: number) => {
        writeFileSync(w.archive, zip([{ name: 'export.json', data: record(new Date(now.getTime() + by).toISOString()) }]));
        return placingPlan({ archive: w.archive, places: w.places, now });
    };
    assert.equal(ahead(day).kind, 'place');
    // Beyond a day, this clock cannot say how old the archive is; the making's story starts by this clock, so it is made.
    assert.deepEqual(ahead(day + 1), { kind: 'make', step: 'archive_stale' });
});

test('an import killed at the limit says so by a code of its own, and one that never started by another', async (t) => {
    const w = world(t);
    const ending = (elapsed: number) => {
        let clock = 0;
        return beginPlacing({
            cli: { run: async () => ({ exit: -1, stdout: '', stderr: 'killed' }) },
            archive: w.archive,
            places: w.places,
            request: () => undefined,
            hold: () => undefined,
            now: () => ((clock += elapsed), clock),
        });
    };
    assert.deepEqual(await ending(placingLimitMs), { kind: 'failed', step: 'import', code: 'timed_out' });
    assert.deepEqual(await ending(10), { kind: 'failed', step: 'import', code: 'unstarted' });
});

test('the import’s own process is named in the request as it starts, from the process the CLI helper started', async (t) => {
    const w = world(t);
    const pids: number[] = [];
    await beginPlacing({
        cli: { run: async (_args, options) => (options?.spawned?.(777), { exit: 0, stdout: '{}', stderr: '' }) },
        archive: w.archive,
        places: w.places,
        request: () => requestStartOver(w.request, 4242, true, true),
        spawned: (pid) => (pids.push(pid), requestStartOver(w.request, 4242, true, true, pid)),
        hold: () => undefined,
    });
    assert.deepEqual(pids, [777]);
    assert.equal(importedBy(readFileSync(w.request, 'utf8')), 777);
    // The helper names the real child.
    let named = 0;
    const result = await cliAt(process.execPath, process.env as Record<string, string>, ['-e', '']).run([], { spawned: (pid) => void (named = pid) });
    assert.equal(result.exit, 0);
    assert.ok(Number.isSafeInteger(named) && named > 0 && named !== process.pid);
});

test('an import that outlives the run that started it is waited for, and a start over is refused while it runs', (t) => {
    const w = world(t);
    claimDemoFolder(w.places.data);
    mkdirSync(join(w.places.harnessRoot, 'projects'), { recursive: true });
    // The run that placed is gone (a crash, a kill); its import, 5150, still writes.
    requestStartOver(w.request, 4242, true, true, 5150);
    const text = readFileSync(w.request, 'utf8');
    assert.equal(placingInFlight(text, 1, (pid) => pid === 5150), true, 'a second start prepares nothing while the import runs');
    let waited = 0;
    assert.throws(
        () => carryOutStartOver({ ...carryOutOnDisk(w.request, w.places, (pid) => pid === 5150), runtimeAlive: () => false, wait: () => void (waited += 1), askingWaitMs: 1000, exists: existsSync, marked: hasDemoMarker }),
        (error: unknown) => error instanceof CarryOutRefused && error.reason === 'import_alive',
    );
    assert.ok(waited > 0, 'it was waited for first');
    assert.ok(existsSync(w.places.data) && existsSync(w.places.harnessRoot) && existsSync(w.request), 'nothing was removed and the request stays');
    // Once it has ended, the next start carries the request out.
    assert.equal(placingInFlight(text, 1, () => false), false);
    carryOutStartOver({ ...carryOutOnDisk(w.request, w.places, () => false), runtimeAlive: () => false, exists: existsSync, marked: hasDemoMarker });
    assert.equal(existsSync(w.places.data) || existsSync(w.places.harnessRoot), false);
});

test('a root the import made that cannot be removed is left marked, and a later carry-out removes it', (t) => {
    const w = world(t);
    claimDemoFolder(w.places.data);
    mkdirSync(join(w.places.harnessRoot, 'projects'), { recursive: true });
    requestStartOver(w.request, 4242, true, true);
    const carry = (removeTree?: (folder: string) => void) =>
        carryOutStartOver({ ...carryOutOnDisk(w.request, w.places, () => false), runtimeAlive: () => false, exists: existsSync, marked: hasDemoMarker, ...(removeTree ? { removeTree } : {}) });
    carry(() => {
        throw new Error('held open');
    });
    const asides = readdirSync(w.root).filter((name) => name.includes(asideMarker));
    assert.equal(asides.length, 2);
    for (const aside of asides) assert.ok(hasDemoMarker(join(w.root, aside)), `${aside} is marked`);
    // The next Start the Demo Over collects them.
    claimDemoFolder(w.places.data);
    requestStartOver(w.request, 4242);
    carry();
    assert.deepEqual(readdirSync(w.root).filter((name) => name.includes(asideMarker)), []);
});

/**
 * A demo runtime that a crashed making or placing left running: the run that asked has gone and so has the import, and
 * the runtime the import started (or the making's own) still runs on the demo's data directory, holding the carry-out back.
 */
function leftRunning(t: { after(fn: () => void): void }, placing: boolean) {
    const w = world(t);
    claimDemoFolder(w.places.data);
    claimDemoFolder(w.places.harnessRoot);
    requestStartOver(w.request, 4242, true, placing, placing ? 5150 : null);
    return w;
}

test('a demo runtime a crashed placing or making left is stopped, and the start over carried out after it; never while either still runs', async (t) => {
    for (const placing of [true, false]) {
        const w = leftRunning(t, placing);
        let alive = true;
        const carry = () => carryOutStartOver({ ...carryOutOnDisk(w.request, w.places, () => false), runtimeAlive: () => alive, exists: existsSync, marked: hasDemoMarker });
        assert.throws(carry, (error: unknown) => error instanceof CarryOutRefused && error.reason === 'runtime_alive');
        const text = readFileSync(w.request, 'utf8');
        assert.equal(leftRuntimeStops(text, 1, () => false), true);
        const acts: string[] = [];
        const outcome = await stopLeftDemoRuntime({
            stop: async () => (acts.push('stop'), (alive = false), { kind: 'stopped' }),
            note: () => (acts.push('note'), noteLeftStopped(w.request)),
            startAgain: () => acts.push('start again'),
            refuse: () => acts.push('refuse'),
        });
        assert.equal(outcome, 'started-again');
        assert.deepEqual(acts, ['stop', 'note', 'start again']);
        // The start after it carries the request out.
        carry();
        assert.equal(existsSync(w.places.data) || existsSync(w.places.harnessRoot) || existsSync(w.request), false);
    }
    // Not while the run that asked, or its import, still runs; not for Start the Demo Over's own request; not for none.
    const w = leftRunning(t, true);
    const text = readFileSync(w.request, 'utf8');
    assert.equal(leftRuntimeStops(text, 1, (pid) => pid === 4242), false);
    assert.equal(leftRuntimeStops(text, 1, (pid) => pid === 5150), false);
    assert.equal(leftRuntimeStops('pid=4242\n', 1, () => false), false);
    assert.equal(leftRuntimeStops(null, 1, () => false), false);
});

test('a left demo runtime that cannot be stopped refuses as before: nothing is started again and the request stays', async (t) => {
    const w = leftRunning(t, true);
    for (const stop of [async () => ({ kind: 'failed', exit: 1 }) as const, async () => ({ kind: 'another' }) as const, async () => Promise.reject(new Error('x'))]) {
        const acts: string[] = [];
        const outcome = await stopLeftDemoRuntime({ stop, note: () => acts.push('note'), startAgain: () => acts.push('start again'), refuse: () => acts.push('refuse') });
        assert.equal(outcome, 'refused');
        assert.deepEqual(acts, ['refuse']);
    }
    assert.ok(existsSync(w.request) && existsSync(w.places.data) && existsSync(w.places.harnessRoot));
});

test('the stop of a left demo runtime asks the demo’s own runtime and never the person’s', async (t) => {
    const root = scratch(t);
    const person = join(root, '.jason');
    const demo = join(root, '.jason-demo');
    const executable = join(root, 'bundle', 'jason');
    mkdirSync(join(root, 'bundle'));
    writeFileSync(executable, '');
    // Both installations have a runtime running from the carried jason.
    for (const [data, instance] of [[person, 'ins_person'], [demo, 'ins_demo']] as const) {
        mkdirSync(join(data, 'run'), { recursive: true });
        writeFileSync(join(data, 'run', 'runtime.json'), JSON.stringify({ pid: process.pid, instance_id: instance, installed_image: executable }));
    }
    const asked: { data: string | undefined; args: readonly string[] }[] = [];
    let demoUp = true;
    const cliFor = (_file: string, env: Record<string, string>): Cli => ({
        async run(args) {
            asked.push({ data: env.JASON_DATA_DIR, args });
            if (args[1] === 'stop') {
                demoUp = false;
                rmSync(join(demo, 'run', 'runtime.json'));
                return { exit: 0, stdout: '{}', stderr: '' };
            }
            return { exit: 0, stdout: JSON.stringify({ instance_id: env.JASON_DATA_DIR === demo && demoUp ? 'ins_demo' : 'ins_person' }), stderr: '' };
        },
    });
    const outcome = await stopLeftDemoRuntime({
        stop: () => stopDemoRuntime({ platform: process.platform, executable, person, env: { JASON_DATA_DIR: person } }, cliFor),
        note: () => undefined,
        startAgain: () => undefined,
        refuse: () => assert.fail('the demo’s runtime was stopped'),
    });
    assert.equal(outcome, 'started-again');
    assert.ok(asked.some((ask) => ask.args.join(' ') === 'runtime stop'));
    assert.deepEqual([...new Set(asked.map((ask) => ask.data))], [demo], 'every verb ran on the demo’s data directory');
    assert.ok(existsSync(join(person, 'run', 'runtime.json')), 'the person’s runtime is as it was');
});

test('a left demo runtime is stopped once for a request: one that comes back after the stop is refused as before, with no second start', async (t) => {
    const w = leftRunning(t, true);
    let alive = true;
    const carry = () => carryOutStartOver({ ...carryOutOnDisk(w.request, w.places, () => false), runtimeAlive: () => alive, exists: existsSync, marked: hasDemoMarker });
    const starts: string[] = [];
    const once = () =>
        stopLeftDemoRuntime({
            stop: async () => ((alive = false), { kind: 'stopped' }),
            note: () => noteLeftStopped(w.request),
            startAgain: () => starts.push('start again'),
            refuse: () => starts.push('refuse'),
        });
    assert.equal(leftRuntimeStops(readFileSync(w.request, 'utf8'), 1, () => false), true);
    await once();
    assert.deepEqual(starts, ['start again']);
    // The runtime is back by the next start (something started it again): the request says a stop was already made.
    alive = true;
    assert.throws(carry, (error: unknown) => error instanceof CarryOutRefused && error.reason === 'runtime_alive');
    assert.equal(leftRuntimeStops(readFileSync(w.request, 'utf8'), 1, () => false), false, 'no second stop and start');
    assert.ok(existsSync(w.request), 'the request is kept for the next demo start');
    // Noting it twice writes the line once.
    noteLeftStopped(w.request);
    assert.equal(readFileSync(w.request, 'utf8').split(/\r?\n/).filter((line) => line === 'stopped').length, 1);
});
