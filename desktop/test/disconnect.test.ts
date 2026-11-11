// Disconnect on a notification plugin's card: what it takes out of the installation's settings, what it keeps, and the
// reload it asks for afterwards.

import assert from 'node:assert/strict';
import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { cliAt } from '../src/cli.ts';
import { loggedCli } from '../src/installation.ts';
import type { Logger } from '../src/log.ts';
import { desktopPaths } from '../src/paths.ts';
import { programsOn } from '../src/programs.ts';
import { ProviderKeys } from '../src/providers.ts';
import { Setup } from '../src/setup.ts';
import { bundledPlugin, isConfigured, Refused, withPluginConfigured, withPluginDisconnected, type BundledPlugin } from '../src/setup-model.ts';
import { bench, fakeBox, memoryLog, snapshot, type Bench } from './harness.ts';

const desktopFolder = join(dirname(fileURLToPath(import.meta.url)), '..');

function setupOn(b: Bench, log: Logger = memoryLog()) {
    return new Setup({
        paths: b.paths,
        cli: loggedCli(b.cli, log),
        programs: b.programs,
        keys: new ProviderKeys(b.paths.providerKeys, fakeBox),
        log,
        bundleVersion: '0.34.0-rc.1',
        runtimeVersion: () => '0.34.0-rc.1',
        settings: () => ({ feed: null, skillsDeployedFor: null }),
    });
}

function writeInstallationSettings(b: Bench, value: unknown): void {
    mkdirSync(dirname(b.paths.installationSettings), { recursive: true });
    writeFileSync(b.paths.installationSettings, typeof value === 'string' ? value : JSON.stringify(value, null, 2));
}

const readInstallationSettings = (b: Bench) => JSON.parse(readFileSync(b.paths.installationSettings, 'utf8')) as Record<string, unknown>;

const notification = (id: string): BundledPlugin => {
    const plugin = bundledPlugin(id);
    assert.ok(plugin);
    return plugin;
};

/** A file as a person and the cards wrote it: the plugin configured, granted, and beside it what is not the plugin's. */
function configured(id: 'slack' | 'telegram' | 'ntfy'): Record<string, unknown> {
    const values = { slack: { token: 'xoxb-1', target: 'C0123ABCD' }, telegram: { token: '123:abc', target: '@team_room' }, ntfy: { target: 'jason-alerts', server: 'https://ntfy.example.test' } }[id];
    const around = {
        Routes: { Default: { Plugin: 'reply', Binding: { profile: 'default' } } },
        Plugins: { Grants: { reply: { Exec: ['reply'] }, [id]: { Env: ['NTFY_TOKEN'] } }, Config: { other: { url: 'https://example.test' } } },
        Notifications: { QuietSeconds: 30, Plugins: { other: { Binding: { topic: 't' } } } },
        Dispatcher: { MaxParallel: 2 },
    };
    return withPluginConfigured(around, notification(id), values);
}

// ---- The settings edit, pure ---------------------------------------------------------------------------------------

test('Disconnect takes out the plugin’s whole configuration and its binding, and keeps its grant and every other key', () => {
    for (const id of ['slack', 'telegram', 'ntfy'] as const) {
        const plugin = notification(id);
        const before = configured(id);
        assert.equal(isConfigured(before, plugin), true, id);
        const kept = structuredClone(before);
        const after = withPluginDisconnected(before, plugin);
        assert.deepEqual(before, kept, `${id}: the input is not changed`);
        assert.equal(isConfigured(after, plugin), false, id);
        const plugins = after.Plugins as { Config: Record<string, unknown>; Grants: Record<string, unknown> };
        assert.equal(id in plugins.Config, false, `${id}: nothing under Plugins:Config:${id}`);
        assert.deepEqual(plugins.Grants, (before.Plugins as { Grants: unknown }).Grants, `${id}: the grants as they were`);
        const notifications = after.Notifications as { Plugins: Record<string, unknown> };
        assert.equal(id in notifications.Plugins, false, `${id}: an entry left empty is dropped`);
        // Everything but the two removals is byte for byte what it was.
        const expected = structuredClone(before) as { Plugins: { Config: Record<string, unknown> }; Notifications: { Plugins: Record<string, unknown> } };
        delete expected.Plugins.Config[id];
        delete expected.Notifications.Plugins[id];
        assert.equal(JSON.stringify(after), JSON.stringify(expected), id);
    }
});

test('under the plugin’s notification entry only Binding goes: Enabled is the person’s and stays', () => {
    const slack = notification('slack');
    const before = {
        Plugins: { Config: { slack: { token: 'xoxb-1' } } },
        Notifications: { Plugins: { slack: { Enabled: true, Binding: { channel: 'C0123ABCD' } } } },
    };
    assert.deepEqual(withPluginDisconnected(before, slack), { Notifications: { Plugins: { slack: { Enabled: true } } } });
    const off = { Notifications: { Plugins: { slack: { Enabled: false, Binding: { channel: 'C0123ABCD' } } } } };
    assert.deepEqual(withPluginDisconnected(off, slack), { Notifications: { Plugins: { slack: { Enabled: false } } } });
});

test('a section left empty by the removal is dropped, and one that was empty before is left', () => {
    const telegram = notification('telegram');
    assert.deepEqual(
        withPluginDisconnected({ Plugins: { Config: { telegram: { token: 't' } } }, Notifications: { Plugins: { telegram: { Binding: { chat: '@team_room' } } } } }, telegram),
        {},
    );
    assert.deepEqual(withPluginDisconnected({ Plugins: { Config: {} }, Notifications: {} }, telegram), { Plugins: { Config: {} }, Notifications: {} });
    assert.deepEqual(withPluginDisconnected({ Kept: 1 }, telegram), { Kept: 1 });
});

test('names are found in any letter case, as the runtime’s configuration reads them', () => {
    const ntfy = notification('ntfy');
    const before = {
        plugins: { config: { NTFY: { token: 'x' } }, grants: { Ntfy: { http: ['ntfy.example.test'] } } },
        notifications: { plugins: { NtFy: { enabled: true, BINDING: { topic: 'alerts', server: 'https://ntfy.example.test' } } } },
    };
    assert.equal(isConfigured(before, ntfy), true);
    const after = withPluginDisconnected(before, ntfy);
    assert.deepEqual(after, { plugins: { grants: { Ntfy: { http: ['ntfy.example.test'] } } }, notifications: { plugins: { NtFy: { enabled: true } } } });
    assert.equal(isConfigured(after, ntfy), false);
});

test('a settings file whose path to the binding is not an object is refused and not changed', () => {
    const slack = notification('slack');
    assert.throws(() => withPluginDisconnected({ Notifications: { Plugins: { slack: 'on' } } }, slack), Refused);
    assert.throws(() => withPluginDisconnected({ Plugins: ['x'] }, slack), Refused);
});

test('the journal forwarder has no Disconnect: without a receiver it would fail every line it is still granted', () => {
    const forwarder = notification('journal-forwarder');
    assert.throws(
        () => withPluginDisconnected({ Plugins: { Config: { 'journal-forwarder': { url: 'https://example.test/in' } } } }, forwarder),
        (error: unknown) => error instanceof Refused && /journal forwarder/i.test(error.message),
    );
});

// ---- The act ---------------------------------------------------------------------------------------------------

test('Disconnect writes the file once, asks the runtime to reload and reads the plugin back; it places, moves and deletes no package', async (t) => {
    for (const id of ['slack', 'telegram', 'ntfy'] as const) {
        const b = bench(t);
        const before = configured(id);
        writeInstallationSettings(b, before);
        const data = b.env.JASON_DATA_DIR!;
        const packageFolder = join(data, 'plugins', id);
        mkdirSync(packageFolder, { recursive: true });
        writeFileSync(join(packageFolder, 'plugin.yaml'), `id: ${id}\n`);
        const files = snapshot(data);
        const log = memoryLog();
        await setupOn(b, log).disconnectPlugin(id);

        assert.deepEqual(b.record().filter((entry) => entry.name === 'jason').map((entry) => entry.args), [['plugin', 'reload'], ['plugin', 'list']], id);
        const after = readInstallationSettings(b);
        assert.equal(isConfigured(after, notification(id)), false, id);
        assert.equal(JSON.stringify(after), JSON.stringify(withPluginDisconnected(before, notification(id))), id);
        assert.equal(readFileSync(join(packageFolder, 'plugin.yaml'), 'utf8'), `id: ${id}\n`, `${id}: the package is where it was`);
        const changed = [...snapshot(data)].filter(([path, digest]) => files.get(path) !== digest).map(([path]) => path);
        assert.deepEqual(changed.filter((path) => !path.startsWith('logs/')), ['config/settings.json'], id);
        assert.deepEqual([...files.keys()].filter((path) => !existsSync(join(data, path))), [], `${id}: nothing removed`);
        const line = log.lines.find((each) => each.name === 'plugin_disconnected');
        assert.deepEqual(line?.fields, { PluginId: id, Exit: 0, Valid: id === 'slack' }, id);
        assert.equal(JSON.stringify(log.lines).includes('xoxb-1'), false);
        assert.equal((await setupOn(b).status()).plugins.find((plugin) => plugin.id === id)?.configured, false, id);
    }
});

test('Disconnect of the journal forwarder or of anything outside the table is refused before the settings file is read', async (t) => {
    for (const id of ['journal-forwarder', 'reply', '../slack', 'SLACK', 42, null]) {
        const b = bench(t);
        writeInstallationSettings(b, '{ "not": json');
        const log = memoryLog();
        await assert.rejects(setupOn(b, log).disconnectPlugin(id), (error: unknown) => error instanceof Refused && !/settings file/.test(error.message), String(id));
        assert.deepEqual(b.record(), [], String(id));
        assert.deepEqual(log.lines, [], `${String(id)}: the file was not read`);
        assert.equal(readFileSync(b.paths.installationSettings, 'utf8'), '{ "not": json');
    }
});

test('Disconnect over a settings file the desktop cannot keep is refused, said once, and leaves the file as it was', async (t) => {
    const b = bench(t);
    writeInstallationSettings(b, '{ "Plugins": { "Config": { "slack": { "token": "t" } } }, }');
    const log = memoryLog();
    await assert.rejects(
        setupOn(b, log).disconnectPlugin('slack'),
        (error: unknown) => error instanceof Refused && /comments and trailing commas/.test(error.message) && error.message.endsWith('Edit it by hand, then press Reload.'),
    );
    assert.equal(readFileSync(b.paths.installationSettings, 'utf8'), '{ "Plugins": { "Config": { "slack": { "token": "t" } } }, }');
    assert.deepEqual(b.record(), [], 'nothing is reloaded');
    assert.deepEqual(log.lines.map((line) => line.name), ['settings_refused']);
});

test('a reload the runtime refuses after a Disconnect is said, and the file keeps the Disconnect', async (t) => {
    const b = bench(t, { env: { STUB_RELOAD_REFUSED: 'yes' } });
    writeInstallationSettings(b, configured('slack'));
    const log = memoryLog();
    await assert.rejects(setupOn(b, log).disconnectPlugin('slack'), Refused);
    assert.equal(isConfigured(readInstallationSettings(b), notification('slack')), false);
    assert.deepEqual(b.record().filter((entry) => entry.name === 'jason').map((entry) => entry.args), [['plugin', 'reload']]);
    assert.deepEqual(log.lines.find((each) => each.name === 'plugin_disconnected')?.fields, { PluginId: 'slack', Exit: 1, Valid: false });
});

test('Disconnect with no settings file writes none', async (t) => {
    const b = bench(t);
    await setupOn(b).disconnectPlugin('ntfy');
    assert.equal(existsSync(b.paths.installationSettings), false);
    assert.deepEqual(b.record().filter((entry) => entry.name === 'jason').map((entry) => entry.args), [['plugin', 'reload'], ['plugin', 'list']]);
});

// ---- Against the real executable --------------------------------------------------------------------------------------
//
// Opt-in, since it starts a runtime of its own (its own data directory and harness root, stopped at the end):
//
//   JASON_EXE=<a jason executable> npm test

const executable = process.env.JASON_EXE;

test('against the real executable, the reload after Disconnect activates a snapshot read from a file with no value for the plugin, its package unchanged', { skip: !executable || !existsSync(executable) ? 'set JASON_EXE to run it' : false }, async (t) => {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-disconnect-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const data = join(root, 'data');
    const env: Record<string, string> = { ...(process.env as Record<string, string>), JASON_DATA_DIR: data, JASON_Harness__Root: join(root, 'harness') };
    const package_ = join(data, 'plugins', 'slack');
    cpSync(join(desktopFolder, '..', 'plugins', 'slack'), package_, { recursive: true });
    const paths = desktopPaths(data);
    mkdirSync(dirname(paths.installationSettings), { recursive: true });
    writeFileSync(
        paths.installationSettings,
        JSON.stringify({
            Plugins: { Config: { slack: { token: 'xoxb-planted-token' } }, Grants: { slack: { Http: ['slack.com'] } } },
            Notifications: { Plugins: { slack: { Enabled: true, Binding: { channel: 'C0123456' } } } },
        }),
    );
    const cli = cliAt(executable!, env);
    const started = await cli.run(['runtime', 'start']);
    assert.equal(started.exit, 0, started.stderr);
    try {
        const info = async () => JSON.parse((await cli.run(['runtime', 'status'])).stdout) as { runtime_version: string; plugins: { snapshot_id: string; last_reload_activated: boolean }; notifications: { channels: { channel: string }[] } };
        const listed = async () => (JSON.parse((await cli.run(['plugin', 'list'])).stdout) as { plugins: { id: string; digest: string; status: string; capabilities: { http: { granted: string[] } } }[] }).plugins.find((each) => each.id === 'slack');
        const before = await info();
        const slackBefore = await listed();
        assert.equal(slackBefore?.status, 'valid');
        const packageBefore = snapshot(package_);

        const log = memoryLog();
        await new Setup({
            paths,
            cli: loggedCli(cli, log),
            programs: programsOn(env),
            keys: new ProviderKeys(join(root, 'provider-keys'), fakeBox),
            log,
            bundleVersion: before.runtime_version,
            runtimeVersion: () => before.runtime_version,
            settings: () => ({ feed: null, skillsDeployedFor: null }),
        }).disconnectPlugin('slack');

        const kept = JSON.parse(readFileSync(paths.installationSettings, 'utf8')) as Record<string, unknown>;
        assert.deepEqual(kept, { Plugins: { Grants: { slack: { Http: ['slack.com'] } } }, Notifications: { Plugins: { slack: { Enabled: true } } } });
        assert.deepEqual(log.lines.find((each) => each.name === 'plugin_disconnected')?.fields, { PluginId: 'slack', Exit: 0, Valid: true });
        const after = await info();
        assert.notEqual(after.plugins.snapshot_id, before.plugins.snapshot_id, 'the runtime read its plugins again');
        assert.equal(after.plugins.last_reload_activated, true, 'and activated what it read');
        const slackAfter = await listed();
        assert.equal(slackAfter?.status, 'valid');
        assert.equal(slackAfter?.digest, slackBefore?.digest, 'the package the runtime reads is the one it read before');
        assert.deepEqual(slackAfter?.capabilities.http.granted, ['slack.com'], 'and its grant stays');
        assert.deepEqual(snapshot(package_), packageBefore, 'the package folder is where it was, byte for byte');
        console.log(`snapshot ${before.plugins.snapshot_id} -> ${after.plugins.snapshot_id}; slack ${slackAfter?.status} ${slackAfter?.digest}; channels ${JSON.stringify(after.notifications.channels)}`);
    } finally {
        await cli.run(['runtime', 'stop']);
    }
});
