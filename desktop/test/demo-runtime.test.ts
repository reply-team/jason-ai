// The runtime an earlier version's demo left running: stopped by the demo's run file and the existing stop, never the
// person's, and asked wherever the person's own is stopped by a start of the person's.

import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { Cli, CliResult } from '../src/cli.ts';
import { demoRuntimeDeps, stopDemoRuntime, stopWithDemo } from '../src/demo-runtime.ts';
import type { StopOutcome } from '../src/own-runtime.ts';
import { desktopPaths } from '../src/paths.ts';
import { runtimeRefusal as uninstallRefusal } from '../src/uninstall.ts';
import { runtimeRefusal as updateRefusal } from '../src/updates.ts';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');

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

test('the update, the installer’s stop and the uninstall stop a runtime an earlier demo left too; no start prepares a demo any more', () => {
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    assert.ok(main.includes('stopRuntime: () => stopWithDemo(() => stopOwnRuntime(ownRuntimeDeps(process.platform, executable, paths.runFile, cli)), () => stopDemoRuntime({ platform: process.platform, executable, person: paths.data, env })),'));
    for (const gone of ['prepareDemo(', 'openDemoDirectory', 'writeDemoSettings(', 'placeDemoPlugin(']) assert.equal(main.includes(gone), false, gone);
    const start = readFileSync(join(desktop, 'src', 'uninstall-start.ts'), 'utf8');
    assert.ok(start.includes('stopRuntimeCode(() => stopWithDemo(() => stopOwnRuntime(own), () => stopDemoRuntime('));
    assert.ok(start.includes('runtime: { stop: () => stopWithDemo(() => stopOwnRuntime(own), () => stopDemo()) },'));
    const placed = readFileSync(join(desktop, 'src', 'remove-placed.ts'), 'utf8');
    assert.ok(placed.includes('stopWithDemo(() => stopFromAny(candidates(), (executable) => input.stopAs(executable)), () => input.stopDemo())'));
});
