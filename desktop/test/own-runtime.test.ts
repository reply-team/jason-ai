// Whether the runtime that is running is the one this application carries, and stopping it only then. An update
// replaces the application's folder and an uninstall removes it, the jason executable inside it included; a runtime
// started from that executable is stopped first, through `jason runtime stop`, and one started from any other
// executable is never stopped. What a runtime runs as is read from the run file it wrote, and believed only when the
// runtime answering there confirms that run file's instance.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import type { Cli, CliResult } from '../src/cli.ts';
import { stopOwnRuntime, whoseRuntime, type OwnRuntimeDeps } from '../src/own-runtime.ts';

function scratch(t: { after(fn: () => void): void }) {
    const root = realpathSync.native(mkdtempSync(join(tmpdir(), 'jason-desktop-own-runtime-')));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const executable = join(root, 'Programs', 'jason-desktop', 'resources', 'bundle', 'jason.exe');
    const other = join(root, 'Programs', 'jason', 'jason.exe');
    for (const file of [executable, other]) {
        mkdirSync(join(file, '..'), { recursive: true });
        writeFileSync(file, 'an executable');
    }
    return { root, executable, other };
}

const runFile = (instance: string, image?: string | null) =>
    JSON.stringify({ api_version: 'v1', runtime_version: '0.34.0', instance_id: instance, pid: 4242, base_url: 'http://127.0.0.1:52766', token: 'planted-token', started_at: '2026-11-10T00:00:00Z', ...(image === undefined ? {} : { installed_image: image }) });

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

function deps(s: ReturnType<typeof scratch>, run: { file: string | null }, cli: Cli, platform: NodeJS.Platform = process.platform): OwnRuntimeDeps {
    return { platform, executable: s.executable, readRunFile: () => run.file, cli, realpath: (path) => realpathSync.native(path) };
}

test('a runtime is this application’s only when its confirmed run file names the carried executable', async (t) => {
    const s = scratch(t);
    const confirmed = () => ok({ instance_id: 'rt_A', runtime_version: '0.34.0' });
    const cases: { name: string; file: string | null; status: () => CliResult; whose: 'none' | 'ours' | 'another' }[] = [
        { name: 'no run file', file: null, status: confirmed, whose: 'none' },
        { name: 'the carried executable, confirmed', file: runFile('rt_A', s.executable), status: confirmed, whose: 'ours' },
        { name: 'another installation’s executable', file: runFile('rt_A', s.other), status: confirmed, whose: 'another' },
        { name: 'no executable named, as a runtime before the field writes it', file: runFile('rt_A'), status: confirmed, whose: 'another' },
        { name: 'an executable it does not know', file: runFile('rt_A', null), status: confirmed, whose: 'another' },
        { name: 'a file that is not there', file: runFile('rt_A', join(s.root, 'gone', 'jason.exe')), status: confirmed, whose: 'another' },
        { name: 'the carried folder, not the file', file: runFile('rt_A', join(s.executable, '..')), status: confirmed, whose: 'another' },
        { name: 'another instance answering', file: runFile('rt_B', s.executable), status: confirmed, whose: 'another' },
        { name: 'a run file the runtime answering says is stale', file: runFile('rt_A', s.executable), status: () => refused(3, 'stale_descriptor'), whose: 'another' },
        { name: 'a run file nothing answers at', file: runFile('rt_A', s.executable), status: () => refused(3, 'runtime_unreachable'), whose: 'none' },
        { name: 'a run file that is not one', file: '{ not json', status: confirmed, whose: 'another' },
        { name: 'an answer that is not one', file: runFile('rt_A', s.executable), status: () => ({ exit: 0, stdout: 'not json', stderr: '' }), whose: 'another' },
    ];
    for (const one of cases) {
        const { cli } = fakeCli({ status: one.status });
        assert.equal(await whoseRuntime(deps(s, { file: one.file }, cli)), one.whose, one.name);
    }
});

test('on Windows the carried executable is recognised whatever the case of its path; elsewhere only as written', async (t) => {
    const s = scratch(t);
    const { cli } = fakeCli({ status: () => ok({ instance_id: 'rt_A' }) });
    const upper = runFile('rt_A', s.executable.toUpperCase());
    const exact = (path: string) => path;
    assert.equal(await whoseRuntime({ ...deps(s, { file: upper }, cli, 'win32'), realpath: exact }), 'ours');
    assert.equal(await whoseRuntime({ ...deps(s, { file: upper }, cli, 'darwin'), realpath: exact }), 'another');
});

test('this application’s runtime is stopped through jason runtime stop, and another installation’s never', async (t) => {
    const s = scratch(t);
    const run = { file: runFile('rt_A', s.executable) as string | null };
    const ours = fakeCli({
        status: () => (run.file === null ? refused(3, 'no_descriptor') : ok({ instance_id: 'rt_A' })),
        stop: () => {
            run.file = null;
            return ok({ instance_id: 'rt_A', pid: 4242 });
        },
    });
    assert.equal(await stopOwnRuntime(deps(s, run, ours.cli)), null);
    assert.deepEqual(ours.asked, ['runtime status', 'runtime stop']);

    const another = fakeCli({ status: () => ok({ instance_id: 'rt_A' }) });
    assert.equal(await stopOwnRuntime(deps(s, { file: runFile('rt_A', s.other) }, another.cli)), null);
    assert.deepEqual(another.asked, ['runtime status'], 'a runtime of another executable is asked about and never stopped');

    const none = fakeCli({ status: () => ok({ instance_id: 'rt_A' }) });
    assert.equal(await stopOwnRuntime(deps(s, { file: null }, none.cli)), null);
    assert.deepEqual(none.asked, [], 'with no run file nothing is asked');
});

test('a stop that fails or does not finish is said, and the runtime is left as it was found', async (t) => {
    const s = scratch(t);
    const still = () => ok({ instance_id: 'rt_A' });
    const failures: { name: string; stop: () => CliResult; exit: number }[] = [
        { name: 'acknowledged and not exited in time', stop: () => refused(1, 'shutdown_timeout'), exit: 1 },
        { name: 'refused', stop: () => refused(1, 'internal_error'), exit: 1 },
        { name: 'the CLI could not be started or ran out of time', stop: () => ({ exit: -1, stdout: '', stderr: 'timed out' }), exit: -1 },
        { name: 'said stopped while the runtime still answers', stop: () => ok({ instance_id: 'rt_A' }), exit: 0 },
    ];
    for (const one of failures) {
        const { cli, asked } = fakeCli({ status: still, stop: one.stop });
        assert.deepEqual(await stopOwnRuntime(deps(s, { file: runFile('rt_A', s.executable) }, cli)), { exit: one.exit }, one.name);
        // A stop that says it worked is checked again; one that says it did not is taken at its word.
        assert.deepEqual(asked, one.exit === 0 ? ['runtime status', 'runtime stop', 'runtime status'] : ['runtime status', 'runtime stop'], one.name);
    }
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
    assert.equal(await stopOwnRuntime(deps(s, run, cli)), null);
    // jason runtime stop waits thirty seconds for the process to exit before it says it did not.
    assert.ok((limits[1] ?? 0) > 30_000, `the stop was given ${limits[1]} ms`);
});
