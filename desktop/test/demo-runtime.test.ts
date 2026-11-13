// The demo's runtime: stopped by the demo's run file and the existing stop, never the person's; stopped when a demo run
// quits, the quit held until the stop has answered; and the desktop's menu offering nothing of the person's
// installation that would outlast a demo run.

import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { Cli, CliResult } from '../src/cli.ts';
import { DemoQuit, demoRuntimeDeps, stopDemoRuntime } from '../src/demo-runtime.ts';
import { demoMenuLine } from '../src/mode.ts';
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

test('a demo run’s quit is held while the demo’s runtime is stopped, then goes on, the stop asked once', async () => {
    const steps: string[] = [];
    let finish: () => void = () => undefined;
    const quit = new DemoQuit({
        stop: () => {
            steps.push('stop');
            return new Promise<void>((resolve) => (finish = resolve));
        },
        quit: () => steps.push('quit'),
    });
    assert.equal(quit.beforeQuit(), true, 'the first quit is held');
    assert.equal(quit.beforeQuit(), true, 'a second quit while stopping is held too');
    assert.deepEqual(steps, ['stop']);
    finish();
    await new Promise((resolve) => setImmediate(resolve));
    assert.deepEqual(steps, ['stop', 'quit']);
    assert.equal(quit.beforeQuit(), false, 'the quit that follows the stop goes on');
    assert.deepEqual(steps, ['stop', 'quit']);
});

test('a stop that fails still lets the quit go on', async () => {
    const steps: string[] = [];
    const quit = new DemoQuit({ stop: () => Promise.reject(new Error('jason gone')), quit: () => steps.push('quit') });
    assert.equal(quit.beforeQuit(), true);
    await new Promise((resolve) => setImmediate(resolve));
    assert.deepEqual(steps, ['quit']);
    assert.equal(quit.beforeQuit(), false);
});

test('the main module holds a demo run’s quit for the demo runtime’s stop, and a live run’s quit for nothing', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    assert.match(main, /const demoQuit =\s+runOn\.mode === 'demo'\s+\? new DemoQuit\(\{/);
    assert.ok(main.includes('await stopDemoRuntime({ platform: process.platform, executable, person: runOn.person, env });'));
    assert.ok(main.includes('if (demoQuit?.beforeQuit()) event.preventDefault();'));
});

test('a demo run offers no update, no PATH command and no start at logon, and makes none of them', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    assert.ok(main.includes('const updates = app.isPackaged && !inDemo'));
    assert.ok(main.includes('const autostart = inDemo || version === null || !carried.outlives ? null : new RuntimeAutostart({ cli, log });'));
    assert.ok(main.includes('const command = !inDemo && carried.outlives ? new PathCommand(commandDeps) : null;'));
    assert.ok(main.includes('demo: inDemo });'));
    const desk = readFileSync(new URL('../src/desk.ts', import.meta.url), 'utf8');
    for (const item of ['updates', 'autostart', 'command']) assert.ok(desk.includes(`demoMenuLine('${item}')`), item);
});

test('a demo run offers no Remove What Jason Placed: it is never made, and the menu says to go back to your own installation', () => {
    assert.equal(demoMenuLine('removal'), 'Remove What Jason Placed: not in the demo. Go back to your own installation for this.');
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    // The act is built only off the demo, so it never runs with the demo's directory in the process's environment.
    assert.ok(main.includes('const removal = !inDemo && removalOffered(process.platform)'));
    assert.ok(main.indexOf('const inDemo = runOn.mode') < main.indexOf('const removal = !inDemo'));
    const desk = readFileSync(new URL('../src/desk.ts', import.meta.url), 'utf8');
    assert.ok(desk.includes("if (this.deps.demo && removalOffered(process.platform)) return [{ label: demoMenuLine('removal'), enabled: false }];"));
    const items = desk.slice(desk.indexOf('private removalItems'));
    assert.ok(items.indexOf("demoMenuLine('removal')") < items.indexOf('removeLabel'), 'the demo line comes before the act is offered');
});
