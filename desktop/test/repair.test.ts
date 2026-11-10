// The restart re-pair: when the runtime comes back at another origin, or as another process, the window pairs again
// through `jason ui open --no-open` and loads the new link, and the IPC check follows the run file.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, unlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import type { Cli, CliResult } from '../src/cli.ts';
import { runtimeOrigin } from '../src/descriptor.ts';
import { refusalOf } from '../src/ipc.ts';
import { fileLogger } from '../src/log.ts';
import { Repairer, repairCadenceMs, runIdentity } from '../src/repair.ts';
import { memoryLog } from './harness.ts';

const first = 'http://127.0.0.1:41234';
const second = 'http://127.0.0.1:52345';

function scratch(t: { after(fn: () => void): void }) {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-repair-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const runFile = join(root, 'run', 'runtime.json');
    mkdirSync(join(root, 'run'), { recursive: true });
    const read = () => {
        try {
            return readFileSync(runFile, 'utf8');
        } catch {
            return null;
        }
    };
    const write = (origin: string, pid: number) => writeFileSync(runFile, JSON.stringify({ api_version: 'v1', base_url: origin, token: 'planted-run-token-77', pid }));
    return { root, runFile, read, write, gone: () => unlinkSync(runFile) };
}

/** A CLI whose `ui open --no-open` prints a link at the origin the run file names, as the real one does. */
function pairingCli(read: () => string | null, exit = () => 0): Cli & { asked: string[][] } {
    const asked: string[][] = [];
    return {
        asked,
        async run(args): Promise<CliResult> {
            asked.push([...args]);
            const origin = runtimeOrigin(read() ?? '');
            if (exit() !== 0 || origin === null) return { exit: 3, stdout: '', stderr: 'No runtime answered.' };
            return { exit: 0, stdout: JSON.stringify({ url: `${origin}/ui/#pair=planted-pair-${asked.length}` }), stderr: '' };
        },
    };
}

test('the run file gives the runtime’s identity: its loopback origin and its process', () => {
    assert.deepEqual(runIdentity(JSON.stringify({ base_url: first, pid: 42, token: 'x' })), { origin: first, pid: 42 });
    assert.deepEqual(runIdentity(JSON.stringify({ base_url: `${first}/`, token: 'x' })), { origin: first, pid: null });
    assert.equal(runIdentity(JSON.stringify({ base_url: 'http://example.com:1', pid: 1 })), null);
    assert.equal(runIdentity(null), null);
    assert.equal(runIdentity('{'), null);
});

test('when the run file’s origin changes, the window pairs again through jason ui open --no-open and loads the new link', async (t) => {
    const s = scratch(t);
    s.write(first, 100);
    const cli = pairingCli(s.read);
    const loaded: string[] = [];
    const log = memoryLog();
    const repairer = new Repairer({ readRunFile: s.read, cli, load: async (link) => void loaded.push(link), log });
    repairer.pairedWith(s.read());

    assert.equal(await repairer.check(), 'same');
    assert.deepEqual(cli.asked, [], 'an unchanged runtime is not paired again');

    s.write(second, 101);
    assert.equal(await repairer.check(), 'repaired');
    assert.deepEqual(cli.asked, [['ui', 'open', '--no-open']]);
    assert.deepEqual(loaded, [`${second}/ui/#pair=planted-pair-1`]);
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [['window_repaired', { From: first, Origin: second }]]);

    assert.equal(await repairer.check(), 'same', 'once paired, the new origin is the window’s');
    assert.equal(cli.asked.length, 1);
});

test('when the run file goes away and comes back, the window waits, then pairs with the runtime that came back', async (t) => {
    const s = scratch(t);
    s.write(first, 100);
    const cli = pairingCli(s.read);
    const loaded: string[] = [];
    const log = memoryLog();
    const repairer = new Repairer({ readRunFile: s.read, cli, load: async (link) => void loaded.push(link), log });
    repairer.pairedWith(s.read());

    s.gone();
    assert.equal(await repairer.check(), 'gone');
    assert.equal(await repairer.check(), 'gone');
    assert.deepEqual(cli.asked, [], 'nothing is asked of a runtime that is not there, and none is started');

    // The same port, another process: the page's pairing was the old runtime's.
    s.write(first, 200);
    assert.equal(await repairer.check(), 'repaired');
    assert.deepEqual(loaded, [`${first}/ui/#pair=planted-pair-1`]);
    assert.deepEqual(log.lines.map((line) => line.name), ['runtime_gone', 'window_repaired']);
});

test('a call from the page at the old origin is refused once the run file names the new one, and the new page’s is answered', (t) => {
    const s = scratch(t);
    s.write(first, 100);
    const origin = () => runtimeOrigin(s.read() ?? '');
    const oldPage = { url: `${first}/ui/#/connect`, parent: null };
    const newPage = { url: `${second}/ui/#/connect`, parent: null };
    assert.equal(refusalOf(oldPage, origin()), null);
    s.write(second, 101);
    assert.equal(refusalOf(oldPage, origin()), 'other_origin');
    assert.equal(refusalOf(newPage, origin()), null);
    s.gone();
    assert.equal(refusalOf(newPage, origin()), 'other_origin');
});

test('a pairing that fails is logged by its exit and tried again on a later look, not on every one', async (t) => {
    const s = scratch(t);
    s.write(first, 100);
    let refuse = true;
    const cli = pairingCli(s.read, () => (refuse ? 3 : 0));
    const loaded: string[] = [];
    let now = 0;
    const log = memoryLog();
    const repairer = new Repairer({ readRunFile: s.read, cli, load: async (link) => void loaded.push(link), log, clock: () => now });
    repairer.pairedWith(s.read());

    s.write(second, 101);
    assert.equal(await repairer.check(), 'failed');
    now += repairCadenceMs;
    assert.equal(await repairer.check(), 'waiting', 'a failed pairing is not repeated at the next look');
    assert.equal(cli.asked.length, 1);
    refuse = false;
    now += 30_000;
    assert.equal(await repairer.check(), 'repaired');
    assert.deepEqual(loaded, [`${second}/ui/#pair=planted-pair-2`]);
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [
        ['repair_failed', { Origin: second, Exit: 3 }],
        ['window_repaired', { From: first, Origin: second }],
    ]);
});

test('Reload pairs again at the runtime’s current origin even when nothing changed', async (t) => {
    const s = scratch(t);
    s.write(first, 100);
    const cli = pairingCli(s.read);
    const loaded: string[] = [];
    const repairer = new Repairer({ readRunFile: s.read, cli, load: async (link) => void loaded.push(link), log: memoryLog() });
    repairer.pairedWith(s.read());
    assert.equal(await repairer.reload(), 'repaired');
    assert.deepEqual(loaded, [`${first}/ui/#pair=planted-pair-1`]);
});

test('two looks at once pair once', async (t) => {
    const s = scratch(t);
    s.write(first, 100);
    const cli = pairingCli(s.read);
    const loaded: string[] = [];
    const repairer = new Repairer({ readRunFile: s.read, cli, load: async (link) => void loaded.push(link), log: memoryLog() });
    repairer.pairedWith(s.read());
    s.write(second, 101);
    const both = await Promise.all([repairer.check(), repairer.check()]);
    assert.deepEqual(both.sort(), ['busy', 'repaired']);
    assert.equal(loaded.length, 1);
});

test('the re-pair’s log lines, read back, carry the origins and an exit, never the link’s pairing code or the run file’s token', async (t) => {
    const s = scratch(t);
    const logs = join(s.root, 'logs');
    s.write(first, 100);
    let refuse = true;
    const cli = pairingCli(s.read, () => (refuse ? 3 : 0));
    let now = 0;
    const repairer = new Repairer({ readRunFile: s.read, cli, load: async () => undefined, log: fileLogger(logs), clock: () => now });
    repairer.pairedWith(s.read());
    s.gone();
    await repairer.check();
    s.write(second, 101);
    await repairer.check();
    refuse = false;
    now += 60_000;
    await repairer.check();
    const text = readdirSync(logs).map((name) => readFileSync(join(logs, name), 'utf8')).join('');
    for (const planted of ['planted-run-token-77', 'planted-pair', '#pair=']) assert.equal(text.includes(planted), false, `${planted} reached the log`);
    assert.deepEqual(text.split(/\r?\n/).filter(Boolean).map((line) => (JSON.parse(line) as { EventName: string }).EventName), ['runtime_gone', 'repair_failed', 'window_repaired']);
});
