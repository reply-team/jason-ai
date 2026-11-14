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
import { dataLayout, desktopPaths } from '../src/paths.ts';
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
        settings: () => ({ feed: null, skillsDeployedFor: null, theme: null }),
    });
}

function writeInstallationSettings(b: Bench, value: unknown): void {
    mkdirSync(dirname(dataLayout(b.paths.data).userSettingsFile), { recursive: true });
    writeFileSync(dataLayout(b.paths.data).userSettingsFile, typeof value === 'string' ? value : JSON.stringify(value, null, 2));
}

const readInstallationSettings = (b: Bench) => JSON.parse(readFileSync(dataLayout(b.paths.data).userSettingsFile, 'utf8')) as Record<string, unknown>;

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

test('Disconnect takes out the plugin’s whole configuration and its binding; every other key keeps its value, its grant included but for the Http grant derived from a server of its own', () => {
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
        const grants = structuredClone((before.Plugins as { Grants: Record<string, Record<string, unknown>> }).Grants);
        // ntfy's server is its own here, so the Http grant Configure derived from it goes with it; Env stays.
        if (id === 'ntfy') delete grants.ntfy!.Http;
        assert.deepEqual(plugins.Grants, grants, `${id}: the grants as they were, but for the derived Http grant`);
        const notifications = after.Notifications as { Plugins: Record<string, unknown> };
        assert.equal(id in notifications.Plugins, false, `${id}: an entry left empty is dropped`);
        // Every other key keeps its value.
        const expected = structuredClone(before) as { Plugins: { Config: Record<string, unknown>; Grants: Record<string, Record<string, unknown>> }; Notifications: { Plugins: Record<string, unknown> } };
        delete expected.Plugins.Config[id];
        if (id === 'ntfy') delete expected.Plugins.Grants.ntfy!.Http;
        delete expected.Notifications.Plugins[id];
        assert.equal(JSON.stringify(after), JSON.stringify(expected), id);
    }
});

test('the plugin is no longer a channel that is on: its notification entry goes whole, unless the person set Enabled false', () => {
    const slack = notification('slack');
    const config = { Plugins: { Config: { slack: { token: 'xoxb-1' } } } };
    const binding = { channel: 'C0123ABCD' };
    const disconnect = (entry: unknown) => withPluginDisconnected({ ...config, Notifications: { QuietSeconds: 30, Plugins: { slack: entry } } }, slack);
    assert.deepEqual(disconnect({ Enabled: true, Binding: binding }), { Notifications: { QuietSeconds: 30 } }, 'Enabled true: no entry');
    assert.deepEqual(disconnect({ Binding: binding }), { Notifications: { QuietSeconds: 30 } }, 'no Enabled: no entry');
    assert.deepEqual(disconnect({ Enabled: 'false', Binding: binding }), { Notifications: { QuietSeconds: 30 } }, 'only the JSON false is an off');
    assert.deepEqual(
        disconnect({ Enabled: false, Binding: binding }),
        { Notifications: { QuietSeconds: 30, Plugins: { slack: { Enabled: false } } } },
        'Enabled false: the person keeps their off, and only Binding goes',
    );
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
    assert.deepEqual(after, {}, 'the Http grant derived from the server goes with it, and the sections it leaves empty');
    assert.equal(isConfigured(after, ntfy), false);
    const off = { NOTIFICATIONS: { Plugins: { nTfY: { ENABLED: false, binding: { topic: 'alerts' } } } } };
    assert.deepEqual(withPluginDisconnected(off, ntfy), { NOTIFICATIONS: { Plugins: { nTfY: { ENABLED: false } } } });
});

test('after a Disconnect, ntfy configured again on its public server is granted the public host, not the old server’s', () => {
    const ntfy = notification('ntfy');
    const own = withPluginConfigured({}, ntfy, { target: 'jason-alerts', server: 'https://ntfy.example.test' });
    assert.deepEqual(own, { Notifications: { Plugins: { ntfy: { Binding: { topic: 'jason-alerts', server: 'https://ntfy.example.test' } } } }, Plugins: { Grants: { ntfy: { Http: ['ntfy.example.test'] } } } });
    const disconnected = withPluginDisconnected(own, ntfy);
    assert.deepEqual(disconnected, {});
    const again = withPluginConfigured(disconnected, ntfy, { target: 'jason-alerts' });
    assert.deepEqual((again.Plugins as { Grants: unknown }).Grants, { ntfy: { Http: ['ntfy.sh'] } });
});

test('an Http grant a person widened, or one with other keys beside it, survives a Disconnect as it was', () => {
    const ntfy = notification('ntfy');
    const binding = { Notifications: { Plugins: { ntfy: { Binding: { topic: 'alerts', server: 'https://ntfy.example.test' } } } } };
    const widened = { ...binding, Plugins: { Grants: { ntfy: { Http: ['ntfy.example.test', 'ntfy.sh'] } } } };
    assert.deepEqual(withPluginDisconnected(widened, ntfy), { Plugins: { Grants: { ntfy: { Http: ['ntfy.example.test', 'ntfy.sh'] } } } });
    const other = { ...binding, Plugins: { Grants: { ntfy: { Http: ['elsewhere.test'] } } } };
    assert.deepEqual(withPluginDisconnected(other, ntfy), { Plugins: { Grants: { ntfy: { Http: ['elsewhere.test'] } } } });
    const beside = { ...binding, Plugins: { Grants: { ntfy: { Http: ['ntfy.example.test'], Env: ['NTFY_TOKEN'] } } } };
    assert.deepEqual(withPluginDisconnected(beside, ntfy), { Plugins: { Grants: { ntfy: { Env: ['NTFY_TOKEN'] } } } }, 'only the derived Http key goes');
});

test('a plugin with no server of its own keeps its Http grant', () => {
    const ntfy = notification('ntfy');
    const public_ = withPluginConfigured({}, ntfy, { target: 'jason-alerts' });
    assert.deepEqual(withPluginDisconnected(public_, ntfy), { Plugins: { Grants: { ntfy: { Http: ['ntfy.sh'] } } } });
    const slack = notification('slack');
    const configuredSlack = withPluginConfigured({}, slack, { token: 'xoxb-1', target: 'C0123ABCD' });
    assert.deepEqual(withPluginDisconnected(configuredSlack, slack), { Plugins: { Grants: { slack: { Http: ['slack.com'] } } } });
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
        assert.deepEqual(line?.fields, { PluginId: id, Written: true, Exit: 0, Valid: id === 'slack' }, id);
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
        assert.equal(readFileSync(dataLayout(b.paths.data).userSettingsFile, 'utf8'), '{ "not": json');
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
    assert.equal(readFileSync(dataLayout(b.paths.data).userSettingsFile, 'utf8'), '{ "Plugins": { "Config": { "slack": { "token": "t" } } }, }');
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
    assert.deepEqual(log.lines.find((each) => each.name === 'plugin_disconnected')?.fields, { PluginId: 'slack', Written: true, Exit: 1, Valid: false });
});

test('Disconnect with no settings file writes none, and its line says nothing was written', async (t) => {
    const b = bench(t);
    const log = memoryLog();
    await setupOn(b, log).disconnectPlugin('ntfy');
    assert.equal(existsSync(dataLayout(b.paths.data).userSettingsFile), false);
    assert.deepEqual(b.record().filter((entry) => entry.name === 'jason').map((entry) => entry.args), [['plugin', 'reload'], ['plugin', 'list']]);
    assert.deepEqual(log.lines.find((each) => each.name === 'plugin_disconnected')?.fields, { PluginId: 'ntfy', Written: false, Exit: 0, Valid: false });
});

test('Disconnect over a file whose entry for the plugin is not an object is refused with the words of any file the desktop cannot keep', async (t) => {
    const b = bench(t);
    writeInstallationSettings(b, { Notifications: { Plugins: { slack: 'on' } } });
    const text = readFileSync(dataLayout(b.paths.data).userSettingsFile, 'utf8');
    await assert.rejects(
        setupOn(b).disconnectPlugin('slack'),
        (error: unknown) => error instanceof Refused && /as something other than an object/.test(error.message) && error.message.endsWith('Edit it by hand, then press Reload.'),
    );
    assert.equal(readFileSync(dataLayout(b.paths.data).userSettingsFile, 'utf8'), text);
    assert.deepEqual(b.record(), [], 'nothing is reloaded');
});

// ---- Against the real executable --------------------------------------------------------------------------------------
//
// Opt-in, since it starts a runtime of its own (its own home, data directory and harness root, stopped at the end):
//
//   JASON_EXE=<a jason executable> npm test

const executable = process.env.JASON_EXE;

test('against the real executable, the reload after Disconnect activates a snapshot read from a file with no value for the plugin, which is no longer a channel that is on, its package unchanged', { skip: !executable || !existsSync(executable) ? 'set JASON_EXE to run it' : false }, async (t) => {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-disconnect-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const data = join(root, 'data');
    const home = join(root, 'home');
    mkdirSync(home, { recursive: true });
    const env: Record<string, string> = {
        ...(process.env as Record<string, string>),
        // A home of its own, so nothing the runtime makes or reads lands in the person's.
        HOME: home,
        USERPROFILE: home,
        ...(process.platform === 'win32' ? { HOMEDRIVE: home.slice(0, 2), HOMEPATH: home.slice(2), APPDATA: join(home, 'AppData', 'Roaming'), LOCALAPPDATA: join(home, 'AppData', 'Local') } : {}),
        JASON_DATA_DIR: data,
        JASON_Harness__Root: join(root, 'harness'),
    };
    const package_ = join(data, 'plugins', 'slack');
    cpSync(join(desktopFolder, '..', 'plugins', 'slack'), package_, { recursive: true });
    const paths = desktopPaths(data);
    mkdirSync(dirname(dataLayout(paths.data).userSettingsFile), { recursive: true });
    writeFileSync(
        dataLayout(paths.data).userSettingsFile,
        JSON.stringify({
            Plugins: { Config: { slack: { token: 'xoxb-planted-token' } }, Grants: { slack: { Http: ['slack.com'] } } },
            Notifications: { Plugins: { slack: { Enabled: true, Binding: { channel: 'C0123456' } } } },
        }),
    );
    const cli = cliAt(executable!, env);
    const started = await cli.run(['runtime', 'start']);
    assert.equal(started.exit, 0, started.stderr);
    try {
        const info = async () => JSON.parse((await cli.run(['runtime', 'status'])).stdout) as { runtime_version: string; plugins: { snapshot_id: string; last_reload_activated: boolean }; notifications: { channels: { channel: string; on: boolean }[] } };
        const listed = async () => (JSON.parse((await cli.run(['plugin', 'list'])).stdout) as { plugins: { id: string; digest: string; status: string; capabilities: { http: { granted: string[] } } }[] }).plugins.find((each) => each.id === 'slack');
        const before = await info();
        const slackBefore = await listed();
        assert.equal(slackBefore?.status, 'valid');
        const packageBefore = snapshot(package_);

        const log = memoryLog();
        await new Setup({
            paths,
            cli: loggedCli(cli, log),
            programs: programsOn(env, null),
            keys: new ProviderKeys(join(root, 'provider-keys'), fakeBox),
            log,
            bundleVersion: before.runtime_version,
            runtimeVersion: () => before.runtime_version,
            settings: () => ({ feed: null, skillsDeployedFor: null, theme: null }),
        }).disconnectPlugin('slack');

        const kept = JSON.parse(readFileSync(dataLayout(paths.data).userSettingsFile, 'utf8')) as Record<string, unknown>;
        assert.deepEqual(kept, { Plugins: { Grants: { slack: { Http: ['slack.com'] } } } });
        assert.deepEqual(log.lines.find((each) => each.name === 'plugin_disconnected')?.fields, { PluginId: 'slack', Written: true, Exit: 0, Valid: true });
        const after = await info();
        assert.notEqual(after.plugins.snapshot_id, before.plugins.snapshot_id, 'the runtime read its plugins again');
        assert.equal(after.plugins.last_reload_activated, true, 'and activated what it read');
        assert.ok(before.notifications.channels.some((each) => each.channel === 'slack' && each.on), 'slack was a channel that is on');
        assert.equal(after.notifications.channels.some((each) => each.channel === 'slack' && each.on), false, 'and after the reload it is not');
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
