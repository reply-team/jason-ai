import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileLogger } from '../src/log.ts';
import { loggedCli, startInstallation } from '../src/installation.ts';
import { ProviderKeys } from '../src/providers.ts';
import { readSettings, writeSettings, type DesktopSettings } from '../src/settings.ts';
import { Setup } from '../src/setup.ts';
import { bundledPlugin, Refused, serverPattern, withPluginConfigured } from '../src/setup-model.ts';
import { bench, fakeBox, memoryLog, snapshot, written, type Bench } from './harness.ts';

function setupOn(b: Bench, log = memoryLog(), settings: DesktopSettings = { feed: null, skillsDeployedFor: null }) {
    return new Setup({
        paths: b.paths,
        cli: loggedCli(b.cli, log),
        programs: b.programs,
        keys: new ProviderKeys(b.paths.providerKeys, fakeBox),
        log,
        bundleVersion: '0.34.0-rc.1',
        runtimeVersion: () => '0.34.0-rc.1',
        settings: () => settings,
    });
}

function writeInstallationSettings(b: Bench, value: unknown): void {
    mkdirSync(dirname(b.paths.installationSettings), { recursive: true });
    writeFileSync(b.paths.installationSettings, typeof value === 'string' ? value : JSON.stringify(value, null, 2));
}

const readInstallationSettings = (b: Bench) => JSON.parse(readFileSync(b.paths.installationSettings, 'utf8')) as Record<string, unknown>;

// ---- The settings edit, pure ---------------------------------------------------------------------------------------

test('configuring a plugin writes its token, its binding and its grant, and keeps every other key as it was', () => {
    const slack = bundledPlugin('slack');
    assert.ok(slack);
    const before = {
        Routes: { Default: { Plugin: 'reply' } },
        plugins: { Grants: { reply: { Exec: ['reply'] } }, Config: { slack: { other: 'kept' } } },
        Dispatcher: { MaxParallel: 2 },
    };
    const after = withPluginConfigured(before, slack, { token: ' xoxb-1 ', target: 'C0123ABCD' });
    assert.deepEqual(after, {
        Routes: { Default: { Plugin: 'reply' } },
        plugins: { Grants: { reply: { Exec: ['reply'] }, slack: { Http: ['slack.com'] } }, Config: { slack: { other: 'kept', token: 'xoxb-1' } } },
        Dispatcher: { MaxParallel: 2 },
        Notifications: { Plugins: { slack: { Binding: { channel: 'C0123ABCD' } } } },
    });
    assert.deepEqual(before.plugins.Config.slack, { other: 'kept' }, 'the input is not changed');
});

test('each of the four plugins writes its own closed set of keys', () => {
    const configure = (id: string, values: { token?: string; target?: string }) => withPluginConfigured({}, bundledPlugin(id)!, values);
    assert.deepEqual(configure('telegram', { token: 't', target: '@team_room' }), {
        Plugins: { Config: { telegram: { token: 't' } }, Grants: { telegram: { Http: ['api.telegram.org'] } } },
        Notifications: { Plugins: { telegram: { Binding: { chat: '@team_room' } } } },
    });
    assert.deepEqual(configure('ntfy', { target: 'my-topic' }), {
        Plugins: { Config: { ntfy: {} }, Grants: { ntfy: { Http: ['*'] } } },
        Notifications: { Plugins: { ntfy: { Binding: { topic: 'my-topic' } } } },
    });
    assert.deepEqual(configure('journal-forwarder', { token: 'b', target: 'https://hooks.example.com:8443/in' }), {
        Plugins: { Config: { 'journal-forwarder': { token: 'b', url: 'https://hooks.example.com:8443/in' } }, Grants: { 'journal-forwarder': { Http: ['hooks.example.com:8443'] } } },
    });
    assert.throws(() => configure('ntfy', { token: 'x' }), Refused);
    assert.throws(() => configure('slack', { token: 't', target: '' }), Refused, 'every binding requires its target');
    assert.throws(() => configure('journal-forwarder', { target: '' }), Refused);
    assert.throws(() => configure('journal-forwarder', { target: 'http://example.com/in' }), Refused);
    assert.throws(() => configure('slack', { target: 'two words' }), Refused);
    assert.throws(() => withPluginConfigured({ Plugins: { Config: 'not an object' } }, bundledPlugin('slack')!, { token: 't' }), Refused);
});

test('an empty value removes what the card wrote, and an absent one leaves it', () => {
    const slack = bundledPlugin('slack')!;
    const configured = withPluginConfigured({}, slack, { token: 't', target: 'C1234ABCD' });
    const cleared = withPluginConfigured(configured, slack, { token: '' });
    assert.deepEqual((cleared as { Plugins: { Config: { slack: unknown } } }).Plugins.Config.slack, {});
    assert.deepEqual((cleared as { Notifications: unknown }).Notifications, { Plugins: { slack: { Binding: { channel: 'C1234ABCD' } } } });
});

test('ntfy alone takes a server, checked before anything is written, and its grant follows the server', () => {
    const ntfy = bundledPlugin('ntfy')!;
    const own = withPluginConfigured({}, ntfy, { target: 'alerts', server: 'https://ntfy.example.com:8443' });
    assert.deepEqual(own, {
        Plugins: { Config: { ntfy: {} }, Grants: { ntfy: { Http: ['ntfy.example.com:8443'] } } },
        Notifications: { Plugins: { ntfy: { Binding: { topic: 'alerts', server: 'https://ntfy.example.com:8443' } } } },
    });
    // Absent keeps the server and so its grant; empty removes it and puts the grant back to any host.
    assert.deepEqual((withPluginConfigured(own, ntfy, { target: 'other' }) as { Plugins: unknown }).Plugins, own.Plugins);
    assert.deepEqual(withPluginConfigured(own, ntfy, { server: '' }), {
        Plugins: { Config: { ntfy: {} }, Grants: { ntfy: { Http: ['*'] } } },
        Notifications: { Plugins: { ntfy: { Binding: { topic: 'alerts' } } } },
    });
    assert.deepEqual((withPluginConfigured({}, ntfy, { target: 't', server: 'https://ntfy.example.com:443' }) as { Plugins: unknown }).Plugins, {
        Config: { ntfy: {} },
        Grants: { ntfy: { Http: ['ntfy.example.com'] } },
    });
    for (const here of ['http://localhost:2586', 'http://127.0.0.1', 'http://[::1]:8080']) {
        assert.doesNotThrow(() => withPluginConfigured({}, ntfy, { target: 't', server: here }), here);
    }
    for (const refused of ['http://ntfy.example.com', 'ftp://ntfy.example.com', 'https://ntfy.example.com/path', 'https://ntfy.example.com?x=1', 'ntfy.example.com', 'https://a b']) {
        assert.throws(() => withPluginConfigured({}, ntfy, { target: 't', server: refused }), Refused, refused);
    }
    assert.throws(() => withPluginConfigured({}, bundledPlugin('slack')!, { target: 'C0123ABCD', server: 'https://ntfy.example.com' }), Refused, 'a server is ntfy’s alone');
});

test('a target is checked against the pattern of the plugin’s own binding, as its manifest declares it', () => {
    const configure = (id: string, target: string) => () => withPluginConfigured({}, bundledPlugin(id)!, { target });
    for (const good of ['C0123ABCD', '#sales', 'general']) assert.doesNotThrow(configure('slack', good), good);
    for (const bad of ['#UPPER', 'c0123abcd!', 'C01']) assert.throws(configure('slack', bad), Refused, bad);
    for (const good of ['-1001234567890', '@team_room']) assert.doesNotThrow(configure('telegram', good), good);
    for (const bad of ['@ab', 'team', '12a']) assert.throws(configure('telegram', bad), Refused, bad);
    assert.doesNotThrow(configure('ntfy', 'jason_alerts-1'));
    for (const bad of ['dots.are.not', 'x'.repeat(65)]) assert.throws(configure('ntfy', bad), Refused, bad);

    // The table's patterns are the manifests' own, so a manifest that changes its binding cannot leave the card behind.
    const declared = (id: string, key: string) => {
        const manifest = readFileSync(join(import.meta.dirname, '..', '..', 'plugins', id, 'plugin.yaml'), 'utf8').replace(/\r\n/g, '\n');
        const binding = manifest.slice(manifest.indexOf('\nbinding:'));
        return new RegExp(`\\n {4}${key}:\\n(?: {6}.*\\n)*? {6}pattern: "([^"]+)"`).exec(binding)?.[1];
    };
    for (const id of ['slack', 'telegram', 'ntfy']) {
        const plugin = bundledPlugin(id)!;
        assert.ok(plugin.target.pattern, id);
        assert.equal(plugin.target.pattern, declared(id, plugin.target.key), `${id}'s ${plugin.target.key}`);
    }
    assert.equal(serverPattern, declared('ntfy', 'server'), 'the server pattern is the ntfy binding’s own');
});

// ---- The written set -----------------------------------------------------------------------------------------------

test('the desktop writes its own files and the keys it was asked to, and nothing else', async (t) => {
    const b = bench(t, { programs: ['reply', 'npm'] });
    const original = { Routes: { Default: { Plugin: 'reply' } }, Plugins: { Grants: { reply: { Exec: ['reply'] } } }, Harness: { Root: 'kept' } };
    writeInstallationSettings(b, original);
    const before = snapshot(b.paths.data);

    const log = fileLogger(b.paths.logs);
    let settings: DesktopSettings = readSettings(b.paths.settings);
    await startInstallation({
        cli: loggedCli(b.cli, log),
        paths: b.paths,
        log,
        bundle: join(b.root, 'bundle'),
        bundleVersion: '0.34.0-rc.1',
        settings,
        saveSettings: (next) => {
            settings = next;
            writeSettings(b.paths.settings, next);
        },
    });
    const setup = setupOn(b, memoryLog(), settings);
    await setup.configurePlugin('slack', { token: 'xoxb-planted', target: 'C0123ABCD' });
    await setup.configurePlugin('ntfy', { target: 'jason-topic' });
    await setup.setProviderKey('deepseek', 'sk-planted-provider');
    await setup.connectReplyWithKey('planted-reply-key');

    const changed = written(before, snapshot(b.paths.data));
    const theCli = ['run/runtime.json'];
    const ours = changed.filter((path) => !theCli.includes(path));
    for (const path of ours) {
        assert.ok(
            path.startsWith('desktop/') || /^logs\/desktop-\d{8}(?:_\d{3,9})?\.jsonl$/.test(path) || path === 'config/settings.json',
            `the desktop wrote ${path}`,
        );
    }
    assert.ok(ours.includes('desktop/settings.json'));
    assert.ok(ours.includes('desktop/provider-keys'));
    assert.ok(ours.some((path) => path.startsWith('desktop/chat/.claude/skills/reply-')));
    assert.equal(ours.some((path) => path.includes('campaign-launch')), false, 'only the Reply skills are copied');

    assert.deepEqual(readInstallationSettings(b), {
        Routes: { Default: { Plugin: 'reply' } },
        Plugins: {
            Grants: { reply: { Exec: ['reply'] }, slack: { Http: ['slack.com'] }, ntfy: { Http: ['*'] } },
            Config: { slack: { token: 'xoxb-planted' }, ntfy: {} },
        },
        Harness: { Root: 'kept' },
        Notifications: { Plugins: { slack: { Binding: { channel: 'C0123ABCD' } }, ntfy: { Binding: { topic: 'jason-topic' } } } },
    });
    const keys = readFileSync(b.paths.providerKeys, 'utf8');
    assert.equal(keys.includes('sk-planted-provider'), false, 'the key is kept encrypted');
    assert.equal(readFileSync(b.paths.settings, 'utf8').includes('planted'), false);
});

// ---- The cards' actions against stubs on the PATH ------------------------------------------------------------------

test('a plugin is placed with jason plugin install, its settings written, then jason plugin reload; its token on no command line and in no environment', async (t) => {
    const b = bench(t);
    const settings: DesktopSettings = { feed: 'http://127.0.0.1:9/manifest.json', skillsDeployedFor: null };
    const log = memoryLog();
    await setupOn(b, log, settings).configurePlugin('telegram', { token: 'planted-bot-token', target: '@team_room' });
    const runs = b.record().filter((entry) => entry.name === 'jason');
    assert.deepEqual(runs.map((entry) => entry.args), [['plugin', 'install', 'telegram', '--feed', 'http://127.0.0.1:9/manifest.json'], ['plugin', 'reload']]);
    for (const entry of b.record()) {
        assert.equal(JSON.stringify(entry.args).includes('planted-bot-token'), false);
        assert.equal(JSON.stringify(entry.env).includes('planted-bot-token'), false);
        assert.equal(entry.stdin.includes('planted-bot-token'), false);
    }
    assert.equal(JSON.stringify(log.lines).includes('planted-bot-token'), false);
    const status = await setupOn(b).status();
    assert.equal(status.plugins.find((plugin) => plugin.id === 'telegram')?.configured, true);
    assert.equal(status.plugins.find((plugin) => plugin.id === 'slack')?.configured, false);
    assert.equal(status.plugins.find((plugin) => plugin.id === 'ntfy')?.tokenLabel, null);
});

test('each card says what the settings file holds and what the runtime answered, in the shape the page declares', async (t) => {
    const b = bench(t);
    writeInstallationSettings(b, { Notifications: { Plugins: { slack: { Binding: { channel: 'C0123ABCD' } } } }, Plugins: { Config: { slack: { token: 't' } } } });
    const status = await setupOn(b).status();
    assert.equal(status.settingsRefused, null);
    assert.deepEqual(status.plugins.map((plugin) => plugin.id), ['slack', 'telegram', 'ntfy', 'journal-forwarder']);
    for (const plugin of status.plugins) {
        assert.deepEqual(Object.keys(plugin).sort(), ['binding', 'configured', 'description', 'id', 'label', 'note', 'runtime', 'serverLabel', 'target', 'tokenLabel']);
        assert.deepEqual(Object.keys(plugin.target).sort(), ['key', 'label']);
    }
    const [slack, telegram, ntfy, forwarder] = status.plugins;
    assert.deepEqual(status.plugins.map((plugin) => plugin.target.key), ['channel', 'chat', 'topic', 'url']);
    assert.deepEqual(status.plugins.map((plugin) => plugin.serverLabel !== null), [false, false, true, false]);
    assert.deepEqual(status.plugins.map((plugin) => plugin.binding), [true, false, false, null]);
    assert.deepEqual(status.plugins.map((plugin) => plugin.configured), [true, false, false, false]);
    assert.match(ntfy!.note ?? '', /NTFY_TOKEN/);
    assert.match(forwarder!.note ?? '', /Subscriptions/);
    assert.equal(slack!.note, null);
    // The stub's runtime lists slack valid with one Http and one Env grant, ntfy invalid with none, and no other.
    assert.deepEqual(slack!.runtime, { valid: true, grants: 2 });
    assert.deepEqual(ntfy!.runtime, { valid: false, grants: 0 });
    assert.equal(telegram!.runtime, null);
    assert.equal(b.record().filter((entry) => entry.name === 'jason' && entry.args[0] === 'plugin').length, 1, 'the runtime is asked once');

    const down = bench(t, { env: { STUB_PLUGIN_LIST_FAILS: 'yes' } });
    assert.deepEqual((await setupOn(down).status()).plugins.map((plugin) => plugin.runtime), [null, null, null, null]);
});

test('Reload is the call with no values: it writes nothing, and a refusal the reload answers is what the cards then say', async (t) => {
    const b = bench(t, { env: { STUB_RELOAD_REFUSED: 'yes' } });
    writeInstallationSettings(b, '{"kept": true}');
    const setup = setupOn(b);
    await assert.rejects(setup.configurePlugin('slack', {}), Refused);
    assert.deepEqual(b.record().filter((entry) => entry.name === 'jason').map((entry) => entry.args), [['plugin', 'reload']]);
    assert.equal(readFileSync(b.paths.installationSettings, 'utf8'), '{"kept": true}');
    assert.match((await setup.status()).settingsRefused ?? '', /^the settings file could not be read \(not valid JSON at line 1, byte 2\)/);

    const fine = bench(t);
    const ok = setupOn(fine);
    await ok.configurePlugin('ntfy', {});
    assert.deepEqual(fine.record().filter((entry) => entry.name === 'jason').map((entry) => entry.args), [['plugin', 'reload']]);
    assert.equal(existsSync(fine.paths.installationSettings), false);
    assert.equal((await ok.status()).settingsRefused, null);
});

test('a settings file the desktop cannot parse is refused, said so, and left as it was', async (t) => {
    const b = bench(t);
    writeInstallationSettings(b, '{ "Plugins": ');
    const log = memoryLog();
    await assert.rejects(setupOn(b, log).configurePlugin('slack', { token: 't', target: 'C1234ABCD' }), (error: unknown) => error instanceof Refused && /not JSON/.test(error.message));
    assert.equal(readFileSync(b.paths.installationSettings, 'utf8'), '{ "Plugins": ');
    assert.deepEqual(b.record(), [], 'nothing is installed or reloaded');
    assert.deepEqual(log.lines.map((line) => line.name), ['settings_refused']);
    assert.match((await setupOn(b).status()).settingsRefused ?? '', /not JSON/);
});

test('the Reply card signs in with the key on standard input, and the Reply CLI alone keeps it', async (t) => {
    const b = bench(t, { programs: ['reply'] });
    await setupOn(b).connectReplyWithKey('  planted-reply-key  ');
    const login = b.record().find((entry) => entry.name === 'reply' && entry.args[0] === 'auth');
    assert.deepEqual(login?.args, ['auth', 'login', '--with-token']);
    assert.equal(login?.stdin, 'planted-reply-key');
    for (const entry of b.record()) assert.equal(JSON.stringify(entry.args).includes('planted-reply-key'), false);
    assert.equal(existsSync(join(b.paths.chat, '.claude', 'skills', 'reply-cli', 'SKILL.md')), true);
    assert.equal(existsSync(join(b.paths.chat, '.claude', 'skills', 'campaign-launch')), false);
    await assert.rejects(setupOn(b).connectReplyWithKey('   '), Refused);
});

test('the Reply card’s browser sign-in runs reply auth login and nothing else of the sign-in', async (t) => {
    const b = bench(t, { programs: ['reply'] });
    await setupOn(b).connectReply();
    const replies = b.record().filter((entry) => entry.name === 'reply').map((entry) => entry.args.join(' '));
    assert.deepEqual(replies, ['auth login', 'skills install adapter --project --agent codex']);
});

test('the Reply card reads the CLI, its sign-in and whether Jason’s Reply plugin is valid', async (t) => {
    const b = bench(t, { programs: ['reply'], env: { STUB_REPLY_SIGNED_IN: 'yes' } });
    const { reply } = await setupOn(b).status();
    assert.deepEqual(reply, { cli: { found: true, version: '0.19.0', install: null }, signedIn: true, user: 'someone@example.com', wired: true, accounts: [] });

    const missing = bench(t);
    const absent = await setupOn(missing).status();
    assert.deepEqual(absent.reply.cli, { found: false, version: null, install: null }, 'without npm there is nothing to install it with');
});

test('a Reply CLI older than the one Jason’s Reply plugin requires is offered the update, and is not asked what it cannot answer', async (t) => {
    const b = bench(t, { programs: ['reply', 'npm'], env: { STUB_REPLY_SIGNED_IN: 'yes', STUB_REPLY_VERSION: '0.11.0' } });
    const { reply } = await setupOn(b).status();
    assert.deepEqual(reply.cli, { found: true, version: '0.11.0', install: 'reply' });
    assert.deepEqual(reply.accounts, []);
    const asked = b.record().filter((entry) => entry.name === 'reply').map((entry) => entry.args.join(' '));
    assert.equal(asked.includes('account list'), false, 'that version has no account command');

    const current = bench(t, { programs: ['reply', 'npm'], env: { STUB_REPLY_SIGNED_IN: 'yes' } });
    assert.equal((await setupOn(current).status()).reply.cli.install, null);
});

test('every host is listed, found or not; the missing ones name an install only where npm can do it', async (t) => {
    const b = bench(t, { programs: ['claude', 'gemini', 'npm'] });
    const { hosts } = await setupOn(b).status();
    const byId = new Map(hosts.map((host) => [host.id, host]));
    assert.deepEqual(byId.get('claude'), { id: 'claude', label: 'Claude Code', kind: 'program', found: true, version: '10.9.2', drivesChat: true, install: null });
    assert.equal(byId.get('codex')?.found, false);
    assert.equal(byId.get('codex')?.install, 'codex');
    assert.equal(byId.get('gemini')?.found, true);
    assert.equal(byId.get('gemini')?.drivesChat, false);
    assert.equal(byId.get('copilot')?.install, null, 'the desktop installs only the hosts it names an npm package for');
    assert.equal(byId.get('deepseek')?.kind, 'provider');
});

test('a missing program is installed with npm on a click, and only where npm is there; Node never', async (t) => {
    const b = bench(t, { programs: ['npm'] });
    await setupOn(b).installProgram('codex');
    assert.deepEqual(b.record().filter((entry) => entry.name === 'npm').map((entry) => entry.args), [['install', '-g', '@openai/codex']]);
    await assert.rejects(setupOn(b).installProgram('node'), Refused);
    await assert.rejects(setupOn(b).installProgram('rm'), Refused);

    const withoutNpm = bench(t);
    await assert.rejects(setupOn(withoutNpm).installProgram('claude'), (error: unknown) => error instanceof Refused && /nodejs\.org/.test(error.message));
});

test('a provider’s key is kept encrypted under <data>/desktop and removed by an empty one', async (t) => {
    const b = bench(t);
    const setup = setupOn(b);
    await setup.setProviderKey('moonshot', 'sk-planted');
    assert.equal(readFileSync(b.paths.providerKeys, 'utf8').includes('sk-planted'), false);
    assert.equal(new ProviderKeys(b.paths.providerKeys, fakeBox).keyFor('moonshot'), 'sk-planted');
    assert.equal((await setup.status()).hosts.find((host) => host.id === 'moonshot')?.found, true);
    await setup.setProviderKey('moonshot', '');
    assert.equal(existsSync(b.paths.providerKeys), false);
    await assert.rejects(setup.setProviderKey('unknown', 'k'), Refused);
    const closed = new ProviderKeys(join(b.root, 'k'), { ...fakeBox, isEncryptionAvailable: () => false });
    assert.throws(() => closed.set('deepseek', 'k'), Refused);
    assert.deepEqual(readdirSync(b.root).includes('k'), false);
});
