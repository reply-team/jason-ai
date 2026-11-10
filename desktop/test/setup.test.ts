import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileLogger } from '../src/log.ts';
import { loggedCli, startInstallation } from '../src/installation.ts';
import { ProviderKeys } from '../src/providers.ts';
import { readSettings, writeSettings, type DesktopSettings } from '../src/settings.ts';
import { Setup } from '../src/setup.ts';
import { bundledPlugin, Refused, withPluginConfigured } from '../src/setup-model.ts';
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
    const after = withPluginConfigured(before, slack, { token: ' xoxb-1 ', target: 'C0123' });
    assert.deepEqual(after, {
        Routes: { Default: { Plugin: 'reply' } },
        plugins: { Grants: { reply: { Exec: ['reply'] }, slack: { Http: ['slack.com'] } }, Config: { slack: { other: 'kept', token: 'xoxb-1' } } },
        Dispatcher: { MaxParallel: 2 },
        Notifications: { Plugins: { slack: { Binding: { channel: 'C0123' } } } },
    });
    assert.deepEqual(before.plugins.Config.slack, { other: 'kept' }, 'the input is not changed');
});

test('each of the four plugins writes its own closed set of keys', () => {
    const configure = (id: string, values: { token?: string; target?: string }) => withPluginConfigured({}, bundledPlugin(id)!, values);
    assert.deepEqual(configure('telegram', { token: 't', target: '@team' }), {
        Plugins: { Config: { telegram: { token: 't' } }, Grants: { telegram: { Http: ['api.telegram.org'] } } },
        Notifications: { Plugins: { telegram: { Binding: { chat: '@team' } } } },
    });
    assert.deepEqual(configure('ntfy', { target: 'my-topic' }), {
        Plugins: { Config: { ntfy: {} }, Grants: { ntfy: { Http: ['ntfy.sh'] } } },
        Notifications: { Plugins: { ntfy: { Binding: { topic: 'my-topic' } } } },
    });
    assert.deepEqual(configure('journal-forwarder', { token: 'b', target: 'https://hooks.example.com:8443/in' }), {
        Plugins: { Config: { 'journal-forwarder': { token: 'b', url: 'https://hooks.example.com:8443/in' } }, Grants: { 'journal-forwarder': { Http: ['hooks.example.com:8443'] } } },
    });
    assert.throws(() => configure('ntfy', { token: 'x' }), Refused);
    assert.throws(() => configure('journal-forwarder', { target: 'http://example.com/in' }), Refused);
    assert.throws(() => configure('slack', { target: 'two words' }), Refused);
    assert.throws(() => withPluginConfigured({ Plugins: { Config: 'not an object' } }, bundledPlugin('slack')!, { token: 't' }), Refused);
});

test('an empty value removes what the card wrote, and an absent one leaves it', () => {
    const slack = bundledPlugin('slack')!;
    const configured = withPluginConfigured({}, slack, { token: 't', target: 'C1' });
    const cleared = withPluginConfigured(configured, slack, { token: '' });
    assert.deepEqual((cleared as { Plugins: { Config: { slack: unknown } } }).Plugins.Config.slack, {});
    assert.deepEqual((cleared as { Notifications: unknown }).Notifications, { Plugins: { slack: { Binding: { channel: 'C1' } } } });
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
    await setup.configurePlugin('slack', { token: 'xoxb-planted', target: 'C0123' });
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
            Grants: { reply: { Exec: ['reply'] }, slack: { Http: ['slack.com'] }, ntfy: { Http: ['ntfy.sh'] } },
            Config: { slack: { token: 'xoxb-planted' }, ntfy: {} },
        },
        Harness: { Root: 'kept' },
        Notifications: { Plugins: { slack: { Binding: { channel: 'C0123' } }, ntfy: { Binding: { topic: 'jason-topic' } } } },
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
    await setupOn(b, log, settings).configurePlugin('telegram', { token: 'planted-bot-token', target: '@team' });
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

test('a settings file the desktop cannot parse is refused, said so, and left as it was', async (t) => {
    const b = bench(t);
    writeInstallationSettings(b, '{ "Plugins": ');
    const log = memoryLog();
    await assert.rejects(setupOn(b, log).configurePlugin('slack', { token: 't', target: 'C1' }), (error: unknown) => error instanceof Refused && /not JSON/.test(error.message));
    assert.equal(readFileSync(b.paths.installationSettings, 'utf8'), '{ "Plugins": ');
    assert.deepEqual(b.record(), [], 'nothing is installed or reloaded');
    assert.deepEqual(log.lines.map((line) => line.name), ['settings_refused']);
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
