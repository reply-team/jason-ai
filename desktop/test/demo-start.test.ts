// The demo's start-up order, in one function: the directory, then the provider and its route, then the fixed settings,
// and only then a start; a step that fails starts nothing and falls back to the person's own installation. And the
// stop of a demo runtime left running, asked wherever the person's own is stopped by a start of the person's.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { stopWithDemo } from '../src/demo-runtime.ts';
import { carriedDemoPlugin, placeDemoPlugin } from '../src/demo-plugin.ts';
import { prepareDemo, settingsFileOf, writeDemoRoute } from '../src/demo-start.ts';
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
        open: (data: string) => (note('open'), openDirectory(data)),
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

test('the demo is prepared in one order: the directory, the provider with its route, the fixed settings', (t) => {
    const person = join(scratch(t), '.jason');
    const data = demoDirectory(person);
    const calls: string[] = [];
    prepareDemo(data, realDeps(t, person, calls));
    assert.deepEqual(calls, ['open', 'place', 'route', 'settings']);
    assert.ok(existsSync(join(data, 'plugins', 'demo', 'plugin.yaml')));
    const settings = JSON.parse(readFileSync(settingsFileOf(data), 'utf8'));
    assert.deepEqual(settings.Routes, { Default: { Plugin: 'demo' } });
    assert.deepEqual(settings.Responder, { OnReply: false }, 'the fixed settings were written after the route and kept it');
});

test('the plugin is placed and the route written before a start is made, and the person’s installation gets neither', (t) => {
    const person = join(scratch(t), '.jason');
    mkdirSync(person);
    const started: string[] = [];
    const run = startOn(person, 'demo' as Mode, false, {
        open: (directory) => {
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
    for (const failing of ['open', 'place', 'route', 'settings'] as const) {
        const calls: string[] = [];
        const kept: Mode[] = [];
        const real = realDeps(t, person, calls);
        const broken = (name: string) => () => {
            calls.push(name);
            throw new Error(`planted ${name}`);
        };
        const wired = { ...real };
        if (failing === 'open') wired.open = broken('open');
        if (failing === 'place') wired.place = broken('place');
        if (failing === 'route') wired.route = broken('route');
        if (failing === 'settings') wired.fixedSettings = broken('settings');
        rmSync(demoDirectory(person), { recursive: true, force: true });
        const run = startOn(person, 'demo', false, { open: (directory) => prepareDemo(directory, wired), keep: (mode) => kept.push(mode) });
        assert.deepEqual([run.mode, run.data, run.unopened === null], ['live', person, false], failing);
        assert.deepEqual(kept, ['live'], failing);
        const order = ['open', 'place', 'route', 'settings'];
        assert.deepEqual(calls.slice(-1), [failing], `${failing}: nothing runs after it`);
        assert.ok(calls.every((name, at) => name === order[at]), failing);
        if (failing === 'place' || failing === 'open') assert.equal(existsSync(settingsFileOf(demoDirectory(person))), false, `${failing}: no route written`);
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
    assert.deepEqual(await stopWithDemo(stopped, failed), { kind: 'failed', exit: 1 });
    assert.deepEqual((await stopWithDemo(none, unanswered)).kind, 'unanswered');
    assert.deepEqual(await stopWithDemo(stopped, async () => Promise.reject(new Error('planted'))), { kind: 'failed', exit: -1 });
    let asked = 0;
    await stopWithDemo(failed, async () => (asked += 1, { kind: 'none' }));
    assert.equal(asked, 0, 'the demo’s is not asked once the person’s refused');
});

test('the update, the installer’s stop and the uninstall stop the demo’s runtime too, and the start-up is the one function', () => {
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    assert.ok(main.includes('stopRuntime: () => stopWithDemo(() => stopOwnRuntime(ownRuntimeDeps(process.platform, executable, paths.runFile, cli)), () => stopDemoRuntime({ platform: process.platform, executable, person: paths.data, env })),'));
    assert.ok(main.includes('open: (directory) => prepareDemo(directory, { open: openDirectory, place: (data) => placeDemoPlugin(carriedDemoPlugin(bundleDirectory()), data, personDirectory), route: writeDemoRoute }),'));
    const start = readFileSync(join(desktop, 'src', 'uninstall-start.ts'), 'utf8');
    assert.ok(start.includes('stopRuntimeCode(() => stopWithDemo(() => stopOwnRuntime(own), () => stopDemoRuntime('));
    assert.ok(start.includes('runtime: { stop: () => stopWithDemo(() => stopOwnRuntime(own), () => stopDemo()) },'));
    const placed = readFileSync(join(desktop, 'src', 'remove-placed.ts'), 'utf8');
    assert.ok(placed.includes('stopWithDemo(() => stopFromAny(candidates(), (executable) => input.stopAs(executable)), () => input.stopDemo())'));
    assert.equal(carriedDemoPlugin('b'), join('b', 'plugins', 'demo'));
});
