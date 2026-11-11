// Whether the runtime that is running is the one this application carries, and stopping it only then. An update
// replaces the application's folder and an uninstall removes it, the jason executable inside it included; a runtime
// started from that executable is stopped first, through `jason runtime stop`, and one started from any other
// executable is never stopped. What a runtime runs as is read from the run file it wrote, and believed only when the
// runtime answering there confirms that run file's instance. A run file whose runtime does not answer is taken for no
// runtime only when the process it names is not running; otherwise nothing goes on until a person has looked.

import assert from 'node:assert/strict';
import { execFileSync, spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, relative } from 'node:path';
import { test } from 'node:test';
import type { Cli, CliResult } from '../src/cli.ts';
import { ownRuntimeDeps, processIsRunning, readRunFileText, stopOwnRuntime, unansweredWords, whoseRuntime, type OwnRuntimeDeps, type Whose } from '../src/own-runtime.ts';

function scratch(t: { after(fn: () => void): void }) {
    const root = realpathSync.native(mkdtempSync(join(tmpdir(), 'jason-desktop-own-runtime-')));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const executable = join(root, 'Programs', 'jason-desktop', 'resources', 'bundle', 'jason.exe');
    const other = join(root, 'Programs', 'jason', 'jason.exe');
    for (const file of [executable, other]) {
        mkdirSync(join(file, '..'), { recursive: true });
        writeFileSync(file, 'an executable');
    }
    return { root, executable, other, runFile: join(root, 'data', 'run', 'runtime.json') };
}

/** The pid every planted run file names; the rows say whether it is running. */
const plantedPid = 4242;

const runFile = (instance: string, image?: unknown, pid: unknown = plantedPid) =>
    JSON.stringify({ api_version: 'v1', runtime_version: '0.34.0', instance_id: instance, pid, base_url: 'http://127.0.0.1:52766', token: 'planted-token', started_at: '2026-11-10T00:00:00Z', ...(image === undefined ? {} : { installed_image: image }) });

const ok = (value: unknown): CliResult => ({ exit: 0, stdout: `${JSON.stringify(value)}\n`, stderr: '' });
const refused = (exit: number, code: string): CliResult => ({ exit, stdout: `${JSON.stringify({ error: { code, message: 'said', retryable: true } })}\n`, stderr: '' });

/** A CLI that answers `runtime status` and `runtime stop` as given, recording each verb it was asked. */
function fakeCli(answers: { status: () => CliResult; stop?: () => CliResult }) {
    const asked: string[] = [];
    const cli: Cli = {
        async run(args) {
            const verb = args.join(' ');
            asked.push(verb);
            if (verb === 'runtime status') return answers.status();
            if (verb === 'runtime stop' && answers.stop) return answers.stop();
            throw new Error(`The test did not expect jason ${verb}.`);
        },
    };
    return { cli, asked };
}

function deps(s: ReturnType<typeof scratch>, run: { file: string | null }, cli: Cli, options: { platform?: NodeJS.Platform; alive?: boolean } = {}): OwnRuntimeDeps {
    return {
        platform: options.platform ?? process.platform,
        executable: s.executable,
        runFile: s.runFile,
        readRunFile: () => run.file,
        cli,
        realpath: (path) => realpathSync.native(path),
        isRunning: (pid) => pid === plantedPid && (options.alive ?? true),
    };
}

test('a runtime is this application’s only when its confirmed run file names the carried executable by an absolute path', async (t) => {
    const s = scratch(t);
    const confirmed = () => ok({ instance_id: 'rt_A', runtime_version: '0.34.0' });
    const cases: { name: string; file: string | null; whose: Whose }[] = [
        { name: 'no run file', file: null, whose: 'none' },
        { name: 'the carried executable, confirmed', file: runFile('rt_A', s.executable), whose: 'ours' },
        { name: 'another installation’s executable', file: runFile('rt_A', s.other), whose: 'another' },
        { name: 'no executable named, as a runtime before the field writes it', file: runFile('rt_A'), whose: 'another' },
        { name: 'an executable it does not know', file: runFile('rt_A', null), whose: 'another' },
        { name: 'an empty field', file: runFile('rt_A', ''), whose: 'another' },
        { name: 'a field that is no string', file: runFile('rt_A', 42), whose: 'another' },
        { name: 'a field that is a list', file: runFile('rt_A', [s.executable]), whose: 'another' },
        // A relative path would be resolved against the folder the desktop was started in, its own when a shortcut starts it.
        { name: 'a relative field naming the carried executable from here', file: runFile('rt_A', relative(process.cwd(), s.executable)), whose: 'another' },
        { name: 'a file that is not there', file: runFile('rt_A', join(s.root, 'gone', 'jason.exe')), whose: 'another' },
        { name: 'the carried folder, not the file', file: runFile('rt_A', join(s.executable, '..')), whose: 'another' },
    ];
    for (const one of cases) {
        const { cli } = fakeCli({ status: confirmed });
        assert.deepEqual(await whoseRuntime(deps(s, { file: one.file }, cli)), { whose: one.whose, pid: one.file === null ? null : plantedPid }, one.name);
    }
});

test('a run file whose runtime does not answer as itself is no runtime only when the process it names is not running', async (t) => {
    const s = scratch(t);
    const answers: { name: string; status: () => CliResult }[] = [
        { name: 'no run file any more (exit 3, no_descriptor)', status: () => refused(3, 'no_descriptor') },
        { name: 'nothing answers in time (exit 3, runtime_unreachable)', status: () => refused(3, 'runtime_unreachable') },
        { name: 'the token is refused (exit 3, unauthorized)', status: () => refused(3, 'unauthorized') },
        { name: 'another instance answers (exit 3, stale_descriptor)', status: () => refused(3, 'stale_descriptor') },
        { name: 'refused (exit 1)', status: () => refused(1, 'internal_error') },
        { name: 'the CLI could not be started or ran out of time (exit -1)', status: () => ({ exit: -1, stdout: '', stderr: 'timed out' }) },
        { name: 'another instance answering (exit 0)', status: () => ok({ instance_id: 'rt_B' }) },
        { name: 'an answer that is not one (exit 0)', status: () => ({ exit: 0, stdout: 'not json', stderr: '' }) },
    ];
    for (const image of [s.executable, s.other, undefined]) {
        for (const one of answers) {
            for (const alive of [true, false]) {
                const { cli } = fakeCli({ status: one.status });
                const look = await whoseRuntime(deps(s, { file: runFile('rt_A', image) }, cli, { alive }));
                assert.deepEqual(look, { whose: alive ? 'unanswered' : 'none', pid: plantedPid }, `${one.name}, ${image ?? 'no field'}, process ${alive ? 'running' : 'gone'}`);
            }
        }
    }

    // A run file that cannot be read names no process to look at: nothing goes on until a person has looked.
    for (const file of ['{ not json', runFile('rt_A', s.executable, null), runFile('rt_A', s.executable, 'a pid'), runFile('rt_A', s.executable, -1), runFile('rt_A', s.executable, 1.5)]) {
        const { cli } = fakeCli({ status: () => refused(3, 'runtime_unreachable') });
        assert.deepEqual(await whoseRuntime(deps(s, { file }, cli, { alive: false })), { whose: 'unanswered', pid: null }, file);
    }
    // One with no instance is never asked about; its process decides.
    for (const alive of [true, false]) {
        const { cli, asked } = fakeCli({ status: () => ok({ instance_id: 'rt_A' }) });
        assert.deepEqual(await whoseRuntime(deps(s, { file: runFile('', s.executable) }, cli, { alive })), { whose: alive ? 'unanswered' : 'none', pid: plantedPid });
        assert.deepEqual(asked, []);
    }
});

test('a run file that is not there is none, and one that is there and cannot be read is said rather than taken for none', async (t) => {
    const s = scratch(t);
    mkdirSync(dirname(s.runFile), { recursive: true });
    assert.equal(readRunFileText(s.runFile), null, 'not there');
    assert.equal(readRunFileText(join(s.executable, 'runtime.json')), null, 'a file where a folder of its path goes');
    writeFileSync(s.runFile, 'the run file');
    assert.equal(readRunFileText(s.runFile), 'the run file');
    rmSync(s.runFile);
    // A folder where the run file goes is there and cannot be read, as a file this account may not read is.
    mkdirSync(s.runFile);
    assert.throws(() => readRunFileText(s.runFile));

    const { cli, asked } = fakeCli({ status: () => ok({ instance_id: 'rt_A' }) });
    const own = ownRuntimeDeps(process.platform, s.executable, s.runFile, cli);
    assert.deepEqual(await whoseRuntime(own), { whose: 'unanswered', pid: null, unreadable: true });
    assert.deepEqual(await stopOwnRuntime(own), { kind: 'unanswered', pid: null, runFile: s.runFile, unreadable: true });
    assert.deepEqual(asked, [], 'nothing is asked about a run file that could not be read');
});

test('what a person reads about a runtime that did not answer asks the runtime first and ends a process last, only once it is known to be the runtime', () => {
    const file = 'C:/data/run/runtime.json';
    const deleting = `If no Jason runtime is running, deleting ${file} lets this go on.`;
    assert.deepEqual(unansweredWords({ pid: 4242, runFile: file }), {
        why: 'a runtime may be running that did not answer: its run file names process 4242, which is running',
        next: `jason runtime status answers once the runtime does. jason runtime start says whether process 4242 is the runtime: it names it if it is, and starts one in place of the run file if it is not. If it is the runtime and never answers, end process 4242. ${deleting}`,
    });
    assert.deepEqual(unansweredWords({ pid: null, runFile: file }), {
        why: 'a runtime may be running that did not answer, and its run file names no process',
        next: `jason runtime status answers once the runtime does, and jason runtime start starts one in place of a run file left behind. ${deleting}`,
    });
    assert.deepEqual(unansweredWords({ pid: null, runFile: file, unreadable: true }), {
        why: `a runtime may be running: its run file, ${file}, is there and could not be read`,
        next: `jason runtime status answers once the runtime does. ${deleting}`,
    });
});

test('on Windows the carried executable is recognised whatever the case of its path; elsewhere only as written', async (t) => {
    const s = scratch(t);
    const { cli } = fakeCli({ status: () => ok({ instance_id: 'rt_A' }) });
    const upper = runFile('rt_A', s.executable.toUpperCase());
    const exact = (path: string) => path;
    assert.equal((await whoseRuntime({ ...deps(s, { file: upper }, cli, { platform: 'win32' }), realpath: exact })).whose, 'ours');
    assert.equal((await whoseRuntime({ ...deps(s, { file: upper }, cli, { platform: 'darwin' }), realpath: exact })).whose, 'another');
});

test('the application’s own dependencies resolve links and short names as the system does, and look at processes by their id', async (t) => {
    const s = scratch(t);
    const { cli } = fakeCli({ status: () => ok({ instance_id: 'rt_A' }) });
    const own = ownRuntimeDeps(process.platform, s.executable, s.runFile, cli);
    assert.equal(own.realpath, realpathSync.native, 'the system’s own resolver: realpathSync neither expands a short name nor gives the true case');
    assert.equal(own.isRunning, processIsRunning);
    assert.equal(own.executable, s.executable);
    assert.equal(own.runFile, s.runFile);
    assert.equal(own.readRunFile(), null, 'no run file');
    mkdirSync(dirname(s.runFile), { recursive: true });

    // A link to the carried folder (a junction on Windows, a symbolic link elsewhere) names the carried executable.
    const link = join(s.root, 'linked');
    symlinkSync(dirname(s.executable), link, process.platform === 'win32' ? 'junction' : 'dir');
    writeFileSync(s.runFile, runFile('rt_A', join(link, 'jason.exe')));
    assert.equal((await whoseRuntime(own)).whose, 'ours', 'through a link');

    if (process.platform === 'win32') {
        // A short (8.3) name of the folder that holds the carried executable, where the volume makes them.
        const long = join(s.root, 'Programs', 'jason-desktop');
        const short = execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', '(New-Object -ComObject Scripting.FileSystemObject).GetFolder($env:JASON_LONG_PATH).ShortPath'], {
            env: { ...process.env, JASON_LONG_PATH: long },
            encoding: 'utf8',
            windowsHide: true,
        }).trim();
        if (short.toLowerCase() === long.toLowerCase()) {
            t.diagnostic('this volume makes no short names; the short-name case is not run');
        } else {
            writeFileSync(s.runFile, runFile('rt_A', join(short, 'resources', 'bundle', 'jason.exe')));
            assert.equal((await whoseRuntime(own)).whose, 'ours', `through the short name ${short}`);
        }
    }
});

test('a process is running while it runs, and not once it has ended', async () => {
    assert.equal(processIsRunning(process.pid), true);
    const child = spawn(process.execPath, ['-e', ''], { stdio: 'ignore', windowsHide: true });
    const pid = child.pid!;
    await new Promise((done) => child.on('exit', done));
    assert.equal(processIsRunning(pid), false);
});

test('this application’s runtime is stopped through jason runtime stop, and another installation’s never', async (t) => {
    const s = scratch(t);
    const run = { file: runFile('rt_A', s.executable) as string | null };
    const ours = fakeCli({
        status: () => (run.file === null ? refused(3, 'no_descriptor') : ok({ instance_id: 'rt_A' })),
        stop: () => {
            run.file = null;
            return ok({ instance_id: 'rt_A', pid: plantedPid });
        },
    });
    assert.deepEqual(await stopOwnRuntime(deps(s, run, ours.cli)), { kind: 'stopped' });
    assert.deepEqual(ours.asked, ['runtime status', 'runtime stop']);

    const another = fakeCli({ status: () => ok({ instance_id: 'rt_A' }) });
    assert.deepEqual(await stopOwnRuntime(deps(s, { file: runFile('rt_A', s.other) }, another.cli)), { kind: 'another' });
    assert.deepEqual(another.asked, ['runtime status'], 'a runtime of another executable is asked about and never stopped');

    const none = fakeCli({ status: () => ok({ instance_id: 'rt_A' }) });
    assert.deepEqual(await stopOwnRuntime(deps(s, { file: null }, none.cli)), { kind: 'none' });
    assert.deepEqual(none.asked, [], 'with no run file nothing is asked');

    const silent = fakeCli({ status: () => refused(3, 'runtime_unreachable') });
    assert.deepEqual(await stopOwnRuntime(deps(s, { file: runFile('rt_A', s.executable) }, silent.cli)), { kind: 'unanswered', pid: plantedPid, runFile: s.runFile });
    assert.deepEqual(silent.asked, ['runtime status'], 'a runtime that did not answer is not asked to stop');
});

test('a stop that fails or does not finish is said, and the runtime is left as it was found', async (t) => {
    const s = scratch(t);
    const still = () => ok({ instance_id: 'rt_A' });
    const failures: { name: string; stop: () => CliResult; exit: number }[] = [
        { name: 'acknowledged and not exited in time', stop: () => refused(1, 'shutdown_timeout'), exit: 1 },
        { name: 'refused', stop: () => refused(1, 'internal_error'), exit: 1 },
        { name: 'the CLI could not be started or ran out of time', stop: () => ({ exit: -1, stdout: '', stderr: 'timed out' }), exit: -1 },
        { name: 'said stopped while the runtime still answers', stop: () => ok({ instance_id: 'rt_A' }), exit: 0 },
        { name: 'said unreachable while the runtime still answers', stop: () => refused(3, 'runtime_unreachable'), exit: 3 },
        // Exit 3 says the runtime is gone only for no run file or nothing answering; a refused token says nothing of it.
        { name: 'the token refused (exit 3, unauthorized)', stop: () => refused(3, 'unauthorized'), exit: 3 },
        { name: 'a usage error (exit 2)', stop: () => ({ exit: 2, stdout: '', stderr: 'usage' }), exit: 2 },
    ];
    for (const one of failures) {
        const { cli, asked } = fakeCli({ status: still, stop: one.stop });
        assert.deepEqual(await stopOwnRuntime(deps(s, { file: runFile('rt_A', s.executable) }, cli)), { kind: 'failed', exit: one.exit }, one.name);
        // A stop that says the runtime is gone is checked again; one that says it did not stop is taken at its word.
        const gone = one.exit === 0 || one.stop().stdout.includes('runtime_unreachable');
        assert.deepEqual(asked, gone ? ['runtime status', 'runtime stop', 'runtime status'] : ['runtime status', 'runtime stop'], one.name);
    }
});

test('a runtime that ended between the look and the stop is stopped, whatever the stop then says', async (t) => {
    const s = scratch(t);
    for (const said of [refused(3, 'runtime_unreachable'), refused(3, 'no_descriptor')]) {
        const state = { alive: true, file: runFile('rt_A', s.executable) as string | null };
        const { cli, asked } = fakeCli({
            status: () => (state.alive ? ok({ instance_id: 'rt_A' }) : refused(3, 'runtime_unreachable')),
            stop: () => {
                state.alive = false;
                return said;
            },
        });
        const own = { ...deps(s, state, cli), isRunning: () => state.alive };
        assert.deepEqual(await stopOwnRuntime(own), { kind: 'stopped' }, said.stdout);
        assert.deepEqual(asked, ['runtime status', 'runtime stop', 'runtime status']);
    }
    // One that answers nothing after the stop while its process still runs did not stop, as far as anyone can tell.
    const { cli } = fakeCli({ status: (() => { let first = true; return () => (first ? ((first = false), ok({ instance_id: 'rt_A' })) : refused(3, 'runtime_unreachable')); })(), stop: () => refused(3, 'runtime_unreachable') });
    assert.deepEqual(await stopOwnRuntime(deps(s, { file: runFile('rt_A', s.executable) }, cli)), { kind: 'unanswered', pid: plantedPid, runFile: s.runFile });
});

test('the stop is given longer than the CLI waits for the runtime to exit', async (t) => {
    const s = scratch(t);
    const limits: (number | undefined)[] = [];
    const run = { file: runFile('rt_A', s.executable) as string | null };
    const cli: Cli = {
        async run(args, options) {
            limits.push(options?.timeoutMs);
            if (args.join(' ') === 'runtime stop') {
                run.file = null;
                return ok({ instance_id: 'rt_A' });
            }
            return run.file === null ? refused(3, 'no_descriptor') : ok({ instance_id: 'rt_A' });
        },
    };
    assert.deepEqual(await stopOwnRuntime(deps(s, run, cli)), { kind: 'stopped' });
    // jason runtime stop waits thirty seconds for the process to exit before it says it did not.
    assert.ok((limits[1] ?? 0) > 30_000, `the stop was given ${limits[1]} ms`);
});
