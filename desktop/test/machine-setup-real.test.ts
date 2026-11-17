// The machine set-up of a fresh machine's first start against the real executable, the carried build: the rows of
// machine-setup.test.ts fake the CLI and prove calls and order, never what lands on disk or goes to the network.
//
// Opt-in, since it copies a built jason into a folder of its own:
//
//   JASON_EXE=<a built jason executable> npm test
//
// The folder JASON_EXE is in is copied whole beside a version.json, which makes it a jason the application carries. Every
// run has its own home, data directory and harness root, and every proxy variable names a listener of the test's own,
// which counts each connection and answers none, so any request the CLI makes is seen and none leaves the machine.

import test from 'node:test';
import assert from 'node:assert/strict';
import { cpSync, existsSync, mkdirSync, mkdtempSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { createServer, type Server } from 'node:net';
import { tmpdir } from 'node:os';
import { dirname, join, relative } from 'node:path';
import { cliAt, type Cli, type CliResult } from '../src/cli.ts';
import { finishArguments, finishingOf, MachineSetup, noLimit, runtimeDoor } from '../src/machine-setup.ts';
import { desktopPaths } from '../src/paths.ts';
import { isNewInstallation } from '../src/theme.ts';
import { memoryLog } from './harness.ts';

const executable = process.env.JASON_EXE;
const skip = !executable || !existsSync(executable) ? 'set JASON_EXE to run it' : process.platform === 'linux' ? 'the set-up runs on macOS and Windows' : false;

/** Every path under a folder, relative and with `/`, folders included. */
function listing(root: string): string[] {
    const out: string[] = [];
    const walk = (folder: string): void => {
        for (const name of readdirSync(folder).sort()) {
            const path = join(folder, name);
            out.push(relative(root, path).split('\\').join('/'));
            if (statSync(path).isDirectory()) walk(path);
        }
    };
    walk(root);
    return out;
}

/** A listener every proxy variable names: it counts each connection and closes it, so nothing leaves the machine. */
async function countingProxy(): Promise<{ server: Server; url: string; connections: () => number }> {
    let count = 0;
    const server = createServer((socket) => {
        count++;
        socket.destroy();
    });
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address();
    if (address === null || typeof address === 'string') throw new Error('no port');
    return { server, url: `http://127.0.0.1:${address.port}`, connections: () => count };
}

/** A fresh machine as the desktop's first start leaves it before the set-up: `<data>/desktop/mode` alone. */
function freshMachine(t: { after(fn: () => void): void }, proxy: string) {
    const root = mkdtempSync(join(tmpdir(), 'machine-setup-real-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const bundle = join(root, 'Jason', 'resources', 'bundle');
    cpSync(dirname(executable!), bundle, { recursive: true });
    writeFileSync(join(bundle, 'version.json'), '{"version":"0.49.0"}');
    const carried = join(bundle, executable!.slice(dirname(executable!).length + 1));
    const home = join(root, 'home');
    mkdirSync(home, { recursive: true });
    const data = join(root, 'data');
    mkdirSync(join(data, 'desktop'), { recursive: true });
    writeFileSync(desktopPaths(data).mode, 'demo\n');
    const env: Record<string, string> = {
        ...(process.env as Record<string, string>),
        HOME: home,
        USERPROFILE: home,
        ...(process.platform === 'win32' ? { HOMEDRIVE: home.slice(0, 2), HOMEPATH: home.slice(2), APPDATA: join(home, 'AppData', 'Roaming'), LOCALAPPDATA: join(home, 'AppData', 'Local') } : {}),
        JASON_DATA_DIR: data,
        JASON_Harness__Root: join(root, 'harness'),
        HTTP_PROXY: proxy,
        HTTPS_PROXY: proxy,
        ALL_PROXY: proxy,
        http_proxy: proxy,
        https_proxy: proxy,
        all_proxy: proxy,
        NO_PROXY: '',
        no_proxy: '',
    };
    return { data, carried, home, cli: cliAt(carried, env) };
}

test('L1: a fresh machine’s first start, with the real carried jason, writes nothing under the data directory but the set-up’s record and asks no network', { skip }, async (t) => {
    const proxy = await countingProxy();
    t.after(() => proxy.server.close());
    const { data, carried, cli } = freshMachine(t, proxy.url);
    // The detector sees a request when one is made: `jason update check` reads the release feed through it.
    const control = await cli.run(['update', 'check'], { timeoutMs: 60_000 });
    assert.ok(proxy.connections() > 0, `the feed's read reaches the counting proxy (${control.exit}: ${control.stderr.slice(0, 200)})`);
    const counted = proxy.connections();
    const before = listing(data);
    assert.deepEqual(before, ['desktop', 'desktop/mode']);
    assert.equal(isNewInstallation(data), true);

    // The set-up as main wires it: the finishing step and the runtime door through the real carried jason. On macOS the
    // routine is the real `jason install` in the scratch home; on Windows that routine writes the account's Path in the
    // registry, which no test may, so it is answered as a run that completed, as the real one does on a fresh machine.
    const routine: Cli = {
        run: async (args, options): Promise<CliResult> => {
            if (process.platform !== 'win32' || args[0] !== 'install') return cli.run(args, options);
            return { exit: 0, stdout: JSON.stringify({ version: '0.49.0', executable: carried, command: null, skills: [], roles: null, retired: null, runtime: 'not_running', done: [], left: [], problems: [], completed: true }), stderr: '' };
        },
    };
    const calls: string[] = [];
    const watched: Cli = { run: (args, options) => (calls.push(args.join(' ')), routine.run(args, options)) };
    const log = memoryLog();
    const setup = new MachineSetup({
        cli: watched,
        executable: carried,
        version: '0.49.0',
        platform: process.platform,
        offered: true,
        record: desktopPaths(data).machineSetup,
        terminalPath: async () => '/usr/bin:/bin',
        runtime: runtimeDoor(watched, desktopPaths(data).runFile),
        log,
        finishingStep: async () => finishingOf(await watched.run(finishArguments, { timeoutMs: noLimit })),
        isNew: () => isNewInstallation(data),
        startOwed: desktopPaths(data).startOwed,
    });
    await setup.atStart();

    assert.deepEqual(listing(data), ['desktop', 'desktop/machine-setup.json', 'desktop/mode'], 'the set-up’s record alone is added');
    assert.equal(isNewInstallation(data), true, 'the next start still opens on the demo');
    assert.equal(proxy.connections(), counted, 'no request was made');
    assert.equal(calls.some((call) => call.startsWith('update apply')), false, 'no finishing step on a new installation');
    assert.ok(log.lines.some((line) => line.name === 'machine_setup_finish_new'));
    assert.equal(setup.state.state, 'reported');
});

test('L1, the command line’s half: the real finishing step on a data directory holding the desktop’s records alone makes nothing and asks no network', { skip }, async (t) => {
    const proxy = await countingProxy();
    t.after(() => proxy.server.close());
    const { data, cli } = freshMachine(t, proxy.url);
    const finished = await cli.run(finishArguments, { timeoutMs: 120_000 });
    assert.equal(finished.exit, 0, finished.stdout + finished.stderr);
    assert.deepEqual(listing(data), ['desktop', 'desktop/mode']);
    assert.equal(proxy.connections(), 0, 'no request was made');
    // The command line's answer as release/49 states it: found, nothing done, each part naming the Desktop's own records.
    const answer = JSON.parse(finished.stdout) as { data_dir_found: boolean; complete: boolean; parts: { part: string; state: string; reason: string }[] };
    assert.equal(answer.data_dir_found, true);
    assert.equal(answer.complete, false);
    assert.deepEqual(answer.parts.map((part) => [part.part, part.state]), [['plugins', 'not_done'], ['skills', 'not_done'], ['ui', 'not_done']]);
    for (const part of answer.parts) assert.match(part.reason, /holds nothing of an installation but Jason Desktop's own/);
    // The Desktop never asks it where the installation is new (the row above); were it asked, it would read three problems.
    assert.equal(finishingOf(finished).completed, false);
});
