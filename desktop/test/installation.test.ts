import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { loggedCli, startInstallation, StartFailed } from '../src/installation.ts';
import type { DesktopSettings } from '../src/settings.ts';
import { bench, memoryLog, type Bench } from './harness.ts';

const bundleVersion = '0.34.0-rc.1';

async function start(b: Bench, settings: DesktopSettings = { feed: null, skillsDeployedFor: null }) {
    const log = memoryLog();
    const saved: DesktopSettings[] = [];
    const started = await startInstallation({
        cli: loggedCli(b.cli, log),
        paths: b.paths,
        log,
        bundle: join(b.root, 'bundle'),
        bundleVersion,
        settings,
        saveSettings: (next) => void saved.push(next),
    });
    return { started, log, saved, verbs: b.record().filter((entry) => entry.name === 'jason').map((entry) => entry.args) };
}

function runtimeAlreadyUp(b: Bench): void {
    mkdirSync(join(b.paths.data, 'run'), { recursive: true });
    writeFileSync(b.paths.runFile, JSON.stringify({ base_url: 'http://127.0.0.1:41234', token: 't' }));
}

test('with no runtime up, the desktop starts one through jason runtime start and loads the pairing link', async (t) => {
    const b = bench(t);
    const { started, verbs, saved } = await start(b, { feed: 'http://127.0.0.1:9/manifest.json', skillsDeployedFor: null });
    const bundle = join(b.root, 'bundle');
    assert.deepEqual(verbs, [
        ['status'],
        ['runtime', 'start'],
        ['status'],
        ['runtime', 'status'],
        ['ui', 'install', '--feed', 'http://127.0.0.1:9/manifest.json'],
        ['skills', 'install', '--roles-only', '--source', bundle],
        ['skills', 'install', '--root', b.paths.chat, '--host', 'claude_code', '--source', bundle],
        ['ui', 'open', '--no-open'],
    ]);
    assert.equal(started.origin, 'http://127.0.0.1:41234');
    assert.equal(started.link, 'http://127.0.0.1:41234/ui/#pair=planted-pair-code');
    assert.equal(started.versionsDiffer, false);
    assert.ok(existsSync(b.paths.chat));
    assert.deepEqual(readdirSync(b.paths.chat), []);
    assert.deepEqual(saved, [{ feed: 'http://127.0.0.1:9/manifest.json', skillsDeployedFor: bundleVersion }]);
});

test('a runtime already up is used as it is: no start, and never runtime run', async (t) => {
    const b = bench(t);
    runtimeAlreadyUp(b);
    const { verbs, log } = await start(b);
    assert.equal(verbs.some((args) => args[0] === 'runtime' && args[1] !== 'status'), false);
    assert.ok(log.lines.some((line) => line.name === 'runtime_found'));
    assert.equal(log.lines.some((line) => line.name === 'runtime_starting'), false);
});

test('a runtime of another version keeps its own UI: no ui install, no role skills, and the versions are said to differ', async (t) => {
    const b = bench(t, { env: { STUB_SAME_VERSION: 'no', STUB_RUNTIME_VERSION: '0.33.0' } });
    runtimeAlreadyUp(b);
    const { started, verbs, log, saved } = await start(b);
    assert.equal(verbs.some((args) => args[0] === 'ui' && args[1] === 'install'), false);
    assert.equal(verbs.some((args) => args.includes('--roles-only')), false);
    assert.ok(verbs.some((args) => args.includes('--root')));
    assert.equal(started.versionsDiffer, true);
    assert.equal(started.runtimeVersion, '0.33.0');
    assert.deepEqual(log.lines.find((line) => line.name === 'versions_differ')?.fields, { RuntimeVersion: '0.33.0', BundleVersion: bundleVersion });
    assert.deepEqual(saved, [], 'skills deployed half-way are deployed again next time');
});

test('the skills are deployed once per bundle version', async (t) => {
    const b = bench(t);
    runtimeAlreadyUp(b);
    const { verbs } = await start(b, { feed: null, skillsDeployedFor: bundleVersion });
    assert.equal(verbs.some((args) => args[0] === 'skills'), false);
});

test('a start that fails, or a link that is not the runtime’s UI, stops with what the CLI said', async (t) => {
    const failing = bench(t, { env: { STUB_START_FAILS: 'yes' } });
    await assert.rejects(start(failing), (error: unknown) => error instanceof StartFailed && error.step === 'runtime start' && /did not start/.test(error.message));

    const foreign = bench(t, { env: { STUB_LINK: 'https://example.com/ui/#pair=x' } });
    await assert.rejects(start(foreign), (error: unknown) => error instanceof StartFailed && error.step === 'ui open');
});

test('the desktop’s environment reaches the CLI as it is: the same data directory and harness root a terminal has', async (t) => {
    const b = bench(t);
    await start(b);
    for (const entry of b.record()) {
        assert.equal(entry.env.JASON_DATA_DIR, b.paths.data);
        assert.equal(entry.env.JASON_Harness__Root, b.env.JASON_Harness__Root);
    }
});

test('every CLI run is logged by its verb, exit and time, never by its arguments', async (t) => {
    const b = bench(t);
    const { log } = await start(b, { feed: 'http://127.0.0.1:9/manifest.json', skillsDeployedFor: null });
    const runs = log.lines.filter((line) => line.name === 'cli_exited');
    assert.equal(runs.length, 8);
    for (const run of runs) assert.deepEqual(Object.keys(run.fields).sort(), ['ElapsedMs', 'Exit', 'Verb']);
    assert.equal(JSON.stringify(log.lines).includes('manifest.json'), false);
});
