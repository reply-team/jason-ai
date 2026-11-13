// The demo's runtime: stopped by the demo's run file and the existing stop, never the person's; stopped when a demo run
// quits, the quit held until the stop has answered or the cap has passed.

import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { Cli, CliResult } from '../src/cli.ts';
import { demoRuntimeDeps, demoStopCapMs, holdDemoQuit, stopDemoRuntime } from '../src/demo-runtime.ts';
import { desktopPaths } from '../src/paths.ts';

const ok = (stdout: string): CliResult => ({ exit: 0, stdout, stderr: '' });

/** A CLI made for one environment, answering status as the instance named and stop as done, recording what it asked. */
function cliMaker(instance: string) {
    const made: { env: Record<string, string>; asked: string[][] }[] = [];
    const make = (_executable: string, env: Record<string, string>): Cli => {
        const record = { env, asked: [] as string[][] };
        made.push(record);
        return {
            async run(args) {
                record.asked.push([...args]);
                if (args[0] === 'runtime' && args[1] === 'status') return ok(JSON.stringify({ instance_id: instance }));
                if (args[0] === 'runtime' && args[1] === 'stop') {
                    rmSync(join(env.JASON_DATA_DIR!, 'run', 'runtime.json'), { force: true });
                    return ok('{}');
                }
                return ok('{}');
            },
        };
    };
    return { made, make };
}

function installations() {
    const home = mkdtempSync(join(tmpdir(), 'demo-runtime-'));
    const person = join(home, '.jason');
    const demo = join(home, '.jason-demo');
    const executable = join(home, 'bundle', 'jason');
    mkdirSync(join(home, 'bundle'), { recursive: true });
    writeFileSync(executable, '');
    return { home, person, demo, executable };
}

function runFile(data: string, executable: string, instance: string, pid = process.pid): string {
    const file = desktopPaths(data).runFile;
    mkdirSync(join(data, 'run'), { recursive: true });
    writeFileSync(file, JSON.stringify({ pid, instance_id: instance, installed_image: executable }));
    return file;
}

test('the demo runtime’s stop reads the demo’s run file and asks a jason on the demo’s directory', () => {
    const { person, demo, executable } = installations();
    const { made, make } = cliMaker('rt_demo');
    const deps = demoRuntimeDeps({ platform: process.platform, executable, person, env: { PATH: '/bin', JASON_DATA_DIR: person } }, make);
    assert.equal(deps.runFile, desktopPaths(demo).runFile);
    assert.equal(made.length, 1);
    assert.equal(made[0]!.env.JASON_DATA_DIR, demo);
    assert.equal(made[0]!.env.PATH, '/bin');
});

test('stopping the demo runtime stops the one its run file names, with jason runtime stop, and never the person’s', async () => {
    const { person, demo, executable } = installations();
    runFile(demo, executable, 'rt_demo');
    const personRun = runFile(person, executable, 'rt_person');
    const before = readFileSync(personRun, 'utf8');
    const { made, make } = cliMaker('rt_demo');
    const outcome = await stopDemoRuntime({ platform: process.platform, executable, person, env: {} }, make);
    assert.equal(made.length, 1);
    assert.deepEqual(made[0]!.asked.map((args) => args.join(' ')).filter((verb) => verb === 'runtime stop'), ['runtime stop']);
    assert.equal(made[0]!.env.JASON_DATA_DIR, demo);
    assert.deepEqual(outcome, { kind: 'stopped' });
    assert.equal(readFileSync(personRun, 'utf8'), before, 'the person’s run file is untouched');
});

test('with no demo runtime running, the stop asks nothing and says so', async () => {
    const { person, executable } = installations();
    const { made, make } = cliMaker('rt_demo');
    assert.deepEqual(await stopDemoRuntime({ platform: process.platform, executable, person, env: {} }, make), { kind: 'none' });
    assert.deepEqual(made[0]!.asked, []);
});

/** Electron's app as far as a demo run's quit needs it: `before-quit` listeners, and a quit that emits it again. */
function fakeApp() {
    const listeners: ((event: { preventDefault(): void }) => void)[] = [];
    const quits: { prevented: boolean }[] = [];
    const app = {
        on(_event: 'before-quit', listener: (event: { preventDefault(): void }) => void) {
            listeners.push(listener);
        },
        quit() {
            const quit = { prevented: false };
            for (const listener of listeners) listener({ preventDefault: () => void (quit.prevented = true) });
            quits.push(quit);
        },
    };
    return { app, quits, listeners };
}

const settle = () => new Promise((resolve) => setImmediate(resolve));

test('a demo run’s quit is held while the demo’s runtime is stopped, then goes on, the stop asked once', async () => {
    const { app, quits } = fakeApp();
    let finish: () => void = () => undefined;
    let stops = 0;
    holdDemoQuit(app, 'demo', {
        stop: () => {
            stops += 1;
            return new Promise<void>((resolve) => (finish = resolve));
        },
        timedOut: () => assert.fail('the stop answered in time'),
    });
    app.quit();
    app.quit();
    assert.deepEqual(quits, [{ prevented: true }, { prevented: true }], 'both quits are held while the stop runs');
    assert.equal(stops, 1);
    finish();
    await settle();
    assert.deepEqual(quits.at(-1), { prevented: false }, 'the quit asked after the stop goes on');
    assert.equal(quits.length, 3);
    app.quit();
    assert.deepEqual(quits.at(-1), { prevented: false });
    assert.equal(stops, 1);
});

test('a stop that fails still lets the quit go on', async () => {
    const { app, quits } = fakeApp();
    holdDemoQuit(app, 'demo', { stop: () => Promise.reject(new Error('jason gone')), timedOut: () => assert.fail('the stop answered') });
    app.quit();
    await settle();
    assert.deepEqual(quits, [{ prevented: true }, { prevented: false }]);
});

test('a stop that never answers is waited for no longer than the cap: the quit goes on and says so once', async () => {
    const { app, quits } = fakeApp();
    let timedOut = 0;
    holdDemoQuit(app, 'demo', { stop: () => new Promise(() => undefined), timedOut: () => void (timedOut += 1), capMs: 20 });
    app.quit();
    assert.deepEqual(quits, [{ prevented: true }]);
    await new Promise((resolve) => setTimeout(resolve, 60));
    assert.deepEqual(quits, [{ prevented: true }, { prevented: false }]);
    assert.equal(timedOut, 1);
    assert.equal(demoStopCapMs, 15_000);
});

test('a live run’s quit is held for nothing and stops nothing', () => {
    const { app, quits, listeners } = fakeApp();
    assert.equal(holdDemoQuit(app, 'live', { stop: () => assert.fail('nothing is stopped'), timedOut: () => assert.fail('nothing waits') }), null);
    assert.equal(listeners.length, 0);
    app.quit();
    assert.deepEqual(quits, [{ prevented: false }]);
});

test('quitting a demo run stops the demo’s runtime through the existing stop, and never the person’s', async () => {
    const { person, demo, executable } = installations();
    runFile(demo, executable, 'rt_demo');
    const personRun = runFile(person, executable, 'rt_person');
    const before = readFileSync(personRun, 'utf8');
    const { made, make } = cliMaker('rt_demo');
    const { app, quits } = fakeApp();
    const outcomes: unknown[] = [];
    holdDemoQuit(app, 'demo', {
        stop: async () => void outcomes.push(await stopDemoRuntime({ platform: process.platform, executable, person, env: { JASON_DATA_DIR: demo } }, make)),
        timedOut: () => assert.fail('the stop answered'),
    });
    app.quit();
    for (let i = 0; i < 20 && quits.length < 2; i += 1) await settle();
    assert.deepEqual(outcomes, [{ kind: 'stopped' }]);
    assert.deepEqual(quits, [{ prevented: true }, { prevented: false }]);
    assert.equal(made[0]!.env.JASON_DATA_DIR, demo);
    assert.equal(readFileSync(personRun, 'utf8'), before, 'the person’s run file is untouched');
});
