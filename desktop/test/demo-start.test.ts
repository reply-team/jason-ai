// The demo's start-up order, in one function: the directory, then the provider and its route, then the fixed settings,
// and only then a start; a step that fails starts nothing and falls back to the person's own installation. And the
// stop of a demo runtime left running, asked wherever the person's own is stopped by a start of the person's.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { stopWithDemo } from '../src/demo-runtime.ts';
import { runtimeRefusal as updateRefusal } from '../src/updates.ts';
import { runtimeRefusal as uninstallRefusal } from '../src/uninstall.ts';
import { carriedDemoPlugin, placeDemoPlugin } from '../src/demo-plugin.ts';
import { claimDemoFolder, demoMarkerName, hasDemoMarker } from '../src/demo-folders.ts';
import { openDemoDirectory, prepareDemo, settingsFileOf, writeDemoRoute } from '../src/demo-start.ts';
import { demoDirectory, openDirectory, startOn, type Mode } from '../src/mode.ts';
import type { StopOutcome } from '../src/own-runtime.ts';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');

function scratch(t: { after(fn: () => void): void }): string {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-demo-start-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    return root;
}

/** A carried package of the test's own, and the real functions over it. */
function realDeps(t: { after(fn: () => void): void }, person: string, calls?: string[]) {
    const carried = join(scratch(t), 'plugins', 'demo');
    mkdirSync(carried, { recursive: true });
    writeFileSync(join(carried, 'plugin.yaml'), 'id: demo\n');
    writeFileSync(join(carried, 'main.js'), 'export default {};\n');
    const note = (name: string) => calls?.push(name);
    return {
        harnessRoot: () => (note('harness'), claimDemoFolder(demoDirectory(join(dirname(person), 'Jason')))),
        place: (data: string) => (note('place'), placeDemoPlugin(carried, data, person)),
        route: (data: string) => (note('route'), writeDemoRoute(data)),
        fixedSettings: (data: string) => {
            note('settings');
            const file = settingsFileOf(data);
            const now = JSON.parse(readFileSync(file, 'utf8')) as Record<string, unknown>;
            writeFileSync(file, JSON.stringify({ ...now, Responder: { OnReply: false } }));
        },
    };
}

test('the demo is prepared in one order: the provider with its route, then the fixed settings', (t) => {
    const person = join(scratch(t), '.jason');
    const data = demoDirectory(person);
    const calls: string[] = [];
    prepareDemo(data, realDeps(t, person, calls));
    assert.deepEqual(calls, ['harness', 'place', 'route', 'settings']);
    assert.ok(existsSync(join(data, 'plugins', 'demo', 'plugin.yaml')));
    const settings = JSON.parse(readFileSync(settingsFileOf(data), 'utf8'));
    assert.deepEqual(settings.Routes, { Default: { Plugin: 'demo' } });
    assert.deepEqual(settings.Responder, { OnReply: false }, 'the fixed settings were written after the route and kept it');
});

test('a demo directory whose layout stamp this build refuses is prepared in no part, and the start is the person’s own', (t) => {
    for (const stamp of ['{"layout":2,"written_by":"0.50.0","at":"2026-12-01T00:00:00Z"}', '{"layout":']) {
        const person = join(scratch(t), '.jason');
        mkdirSync(person);
        const data = demoDirectory(person);
        mkdirSync(data);
        writeFileSync(join(data, 'layout.json'), stamp);
        const calls: string[] = [];
        assert.throws(() => prepareDemo(data, realDeps(t, person, calls)), /layout this build does not run/);
        assert.deepEqual(calls, [], 'no harness folder, plugin, route or setting');
        assert.deepEqual(readdirSync(data), ['layout.json']);
        const kept: string[] = [];
        const run = startOn(person, 'demo' as Mode, false, {
            open: openDirectory,
            prepare: (directory) => prepareDemo(directory, realDeps(t, person, calls)),
            keep: (mode) => kept.push(mode),
        });
        assert.equal(run.mode, 'live', stamp);
        assert.equal(run.data, person);
        assert.deepEqual(kept, ['live']);
        assert.deepEqual(calls, []);
        assert.deepEqual(readdirSync(data), ['layout.json']);
    }
});

test('the plugin is placed and the route written before a start is made, and the person’s installation gets neither', (t) => {
    const person = join(scratch(t), '.jason');
    mkdirSync(person);
    const started: string[] = [];
    const run = startOn(person, 'demo' as Mode, false, {
        open: openDirectory,
        prepare: (directory) => {
            prepareDemo(directory, realDeps(t, person));
            // At the moment the run is decided, everything a start reads is in place.
            started.push(`${existsSync(join(directory, 'plugins', 'demo', 'plugin.yaml'))}:${existsSync(settingsFileOf(directory))}`);
        },
        keep: () => assert.fail('a demo that was prepared is not given up'),
    });
    assert.equal(run.mode, 'demo');
    assert.deepEqual(started, ['true:true']);
    assert.equal(existsSync(join(person, 'plugins')), false);
    assert.equal(existsSync(join(person, 'config')), false);
});

test('a route is never written where the provider is absent', (t) => {
    const data = join(scratch(t), '.jason-demo');
    mkdirSync(data);
    assert.throws(() => writeDemoRoute(data), /not placed/);
    assert.equal(existsSync(settingsFileOf(data)), false);
    // And it keeps what was there: other keys survive, another route stays beside the default.
    mkdirSync(join(data, 'plugins', 'demo'), { recursive: true });
    writeFileSync(join(data, 'plugins', 'demo', 'plugin.yaml'), 'id: demo\n');
    mkdirSync(join(data, 'config'));
    writeFileSync(settingsFileOf(data), JSON.stringify({ Dispatcher: { TickSeconds: 2 }, Routes: { Operations: { 'audience.search': { Plugin: 'x' } } } }));
    writeDemoRoute(data);
    assert.deepEqual(JSON.parse(readFileSync(settingsFileOf(data), 'utf8')), {
        Dispatcher: { TickSeconds: 2 },
        Routes: { Operations: { 'audience.search': { Plugin: 'x' } }, Default: { Plugin: 'demo' } },
    });
    // A settings file that is no object is refused, not replaced.
    writeFileSync(settingsFileOf(data), '[1]');
    assert.throws(() => writeDemoRoute(data), /not an object/);
    assert.equal(readFileSync(settingsFileOf(data), 'utf8'), '[1]');
});

test('a step that fails stops the rest, writes no route, and makes the run the person’s own with the record set back', (t) => {
    const person = join(scratch(t), '.jason');
    mkdirSync(person);
    const order = ['open', 'harness', 'place', 'route', 'settings'];
    for (const failing of order) {
        const calls: string[] = [];
        const kept: Mode[] = [];
        const real = realDeps(t, person, calls);
        const broken = (name: string) => () => {
            calls.push(name);
            throw new Error(`planted ${name}`);
        };
        const wired = { ...real };
        if (failing === 'harness') wired.harnessRoot = broken('harness');
        if (failing === 'place') wired.place = broken('place');
        if (failing === 'route') wired.route = broken('route');
        if (failing === 'settings') wired.fixedSettings = broken('settings');
        rmSync(demoDirectory(person), { recursive: true, force: true });
        const run = startOn(person, 'demo', false, {
            open: failing === 'open' ? broken('open') : (directory) => (calls.push('open'), openDirectory(directory)),
            prepare: (directory) => prepareDemo(directory, wired),
            keep: (mode) => kept.push(mode),
        });
        assert.deepEqual([run.mode, run.data, run.unopened === null], ['live', person, false], failing);
        assert.deepEqual(kept, ['live'], failing);
        assert.deepEqual(calls.slice(-1), [failing], `${failing}: nothing runs after it`);
        assert.ok(calls.every((name, at) => name === order[at]), failing);
        if (failing === 'harness' || failing === 'place' || failing === 'open') assert.equal(existsSync(settingsFileOf(demoDirectory(person))), false, `${failing}: no route written`);
    }
});

test('a demo runtime left running is stopped with the person’s: a refusal of either is the answer, and otherwise the person’s', async () => {
    const stopped = async (): Promise<StopOutcome> => ({ kind: 'stopped' });
    const none = async (): Promise<StopOutcome> => ({ kind: 'none' });
    const another = async (): Promise<StopOutcome> => ({ kind: 'another' });
    const failed = async (): Promise<StopOutcome> => ({ kind: 'failed', exit: 1 });
    const unanswered = async (): Promise<StopOutcome> => ({ kind: 'unanswered', pid: 9, runFile: 'run.json' });
    assert.deepEqual(await stopWithDemo(none, stopped), { kind: 'stopped' }, 'only the demo’s ran');
    assert.deepEqual(await stopWithDemo(stopped, none), { kind: 'stopped' });
    assert.deepEqual(await stopWithDemo(none, none), { kind: 'none' });
    assert.deepEqual(await stopWithDemo(another, stopped), { kind: 'another' });
    assert.deepEqual(await stopWithDemo(failed, stopped), { kind: 'failed', exit: 1 });
    assert.deepEqual(await stopWithDemo(stopped, failed), { kind: 'failed', exit: 1, demo: true });
    assert.deepEqual((await stopWithDemo(none, unanswered)).kind, 'unanswered');
    assert.deepEqual(await stopWithDemo(stopped, async () => Promise.reject(new Error('planted'))), { kind: 'failed', exit: -1, demo: true });
    let asked = 0;
    await stopWithDemo(failed, async () => (asked += 1, { kind: 'none' }));
    assert.equal(asked, 0, 'the demo’s is not asked once the person’s refused');
});

test('a demo runtime that will not stop is said to be the demo’s in what the person reads, for the update and for the removal', async () => {
    const failed = async (): Promise<StopOutcome> => ({ kind: 'failed', exit: 1 });
    const none = async (): Promise<StopOutcome> => ({ kind: 'none' });
    const demoRefused = await stopWithDemo(none, failed);
    assert.deepEqual(demoRefused, { kind: 'failed', exit: 1, demo: true });
    const personRefused = await stopWithDemo(failed, none);
    assert.deepEqual(personRefused, { kind: 'failed', exit: 1 }, 'the person’s own refusal is not called the demo’s');
    const unanswered = await stopWithDemo(none, async () => ({ kind: 'unanswered', pid: 5, runFile: 'run.json' }));
    assert.equal(unanswered.kind === 'unanswered' && unanswered.demo, true);
    for (const outcome of [demoRefused, unanswered]) {
        assert.ok(outcome.kind === 'failed' || outcome.kind === 'unanswered');
        const update = updateRefusal(outcome);
        const removal = uninstallRefusal(outcome);
        for (const text of [update, removal]) {
            assert.match(text, /the demo’s runtime, the sample installation’s own and not yours, is running and did not stop/);
            assert.match(text, /Open Jason in the demo and quit it/);
            assert.doesNotMatch(text, /this application carries/);
        }
    }
    assert.match(updateRefusal(personRefused as { kind: 'failed'; exit: number }), /the runtime this application carries did not stop/);
    assert.match(uninstallRefusal(personRefused as { kind: 'failed'; exit: number }), /the runtime this application carries is running/);
});

test('the update, the installer’s stop and the uninstall stop the demo’s runtime too, and the start-up is the one function', () => {
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    assert.ok(main.includes('stopRuntime: () => stopWithDemo(() => stopOwnRuntime(ownRuntimeDeps(process.platform, executable, paths.runFile, cli)), () => stopDemoRuntime({ platform: process.platform, executable, person: paths.data, env })),'));
    const flat = main.split(/\s+/).join(' ');
    assert.ok(flat.includes('prepareLive = (demo) => prepareDemo(demo, { harnessRoot: () => claimDemoFolder(demoDirectory(harnessRoot(process.env, homedir(), dataLayout(personDirectory).userSettingsFile))), place: (data) => placeDemoPlugin(carriedDemoPlugin(bundleDirectory()), data, personDirectory), route: writeDemoRoute,'));
    assert.ok(flat.includes('open: openDemoDirectory,'));
    assert.ok(main.includes('fixedSettings: (data) => writeDemoSettings(data, harnessRoot('));
    const start = readFileSync(join(desktop, 'src', 'uninstall-start.ts'), 'utf8');
    assert.ok(start.includes('stopRuntimeCode(() => stopWithDemo(() => stopOwnRuntime(own), () => stopDemoRuntime('));
    assert.ok(start.includes('runtime: { stop: () => stopWithDemo(() => stopOwnRuntime(own), () => stopDemo()) },'));
    const placed = readFileSync(join(desktop, 'src', 'remove-placed.ts'), 'utf8');
    assert.ok(placed.includes('stopWithDemo(() => stopFromAny(candidates(), (executable) => input.stopAs(executable)), () => input.stopDemo())'));
    assert.equal(carriedDemoPlugin('b'), join('b', 'plugins', 'demo'));
});

// The marker: the demo's two folders are known by a file the desktop writes into each when it makes it, because their
// names alone can already belong to the person.

/** A start of the demo over the real steps, with the person's harness root named `Jason` beside their data directory. */
function startReal(t: { after(fn: () => void): void }, person: string) {
    const root = join(dirname(person), 'Jason');
    const deps = realDeps(t, person);
    const kept: Mode[] = [];
    const run = startOn(person, 'demo', false, {
        open: openDemoDirectory,
        prepare: (demo) => prepareDemo(demo, { ...deps, harnessRoot: () => claimDemoFolder(demoDirectory(root)) }),
        keep: (mode) => kept.push(mode),
    });
    return { run, kept, root: demoDirectory(root) };
}

test('a start that makes the demo’s folders marks both, and a later start uses them', (t) => {
    const person = join(scratch(t), '.jason');
    mkdirSync(person);
    const first = startReal(t, person);
    assert.equal(first.run.mode, 'demo');
    assert.equal(hasDemoMarker(demoDirectory(person)), true);
    assert.equal(hasDemoMarker(first.root), true);
    const second = startReal(t, person);
    assert.equal(second.run.mode, 'demo', 'marked folders are used');
    assert.deepEqual(second.kept, []);
});

test('a folder of either name that is not ours is neither used nor written into: the start falls back to the person’s own', (t) => {
    for (const foreign of ['data', 'root'] as const) {
        const person = join(scratch(t), '.jason');
        mkdirSync(person);
        const folder = foreign === 'data' ? demoDirectory(person) : demoDirectory(join(dirname(person), 'Jason'));
        mkdirSync(folder);
        writeFileSync(join(folder, 'mine.txt'), 'the person’s own');
        const { run, kept } = startReal(t, person);
        assert.deepEqual([run.mode, run.data, run.unopened === null], ['live', person, false], foreign);
        assert.deepEqual(kept, ['live'], foreign);
        assert.deepEqual(readdirSync(folder), ['mine.txt'], `${foreign}: nothing was written into it, no marker among it`);
        assert.equal(existsSync(join(folder, demoMarkerName)), false, foreign);
        if (foreign === 'root') assert.equal(existsSync(join(demoDirectory(person), 'plugins')), false, 'no plugin was placed once the root was refused');
    }
});

test('a folder that is a file or a link, or whose marker is not the desktop’s, is not ours either', (t) => {
    const home = scratch(t);
    const file = join(home, 'a-demo');
    writeFileSync(file, 'x');
    assert.throws(() => claimDemoFolder(file));
    const wrong = join(home, 'b-demo');
    mkdirSync(wrong);
    writeFileSync(join(wrong, demoMarkerName), 'somebody else wrote this');
    assert.throws(() => claimDemoFolder(wrong));
    assert.equal(hasDemoMarker(wrong), false);
    assert.equal(readFileSync(join(wrong, demoMarkerName), 'utf8'), 'somebody else wrote this');
});
