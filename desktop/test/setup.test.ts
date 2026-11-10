import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent } from '../src/chat.ts';
import { denyListFile, parseDenyList } from '../src/chat-deny.ts';
import { loggedTurns } from '../src/chat-log.ts';
import { fileLogger, type Logger } from '../src/log.ts';
import { loggedCli, startInstallation } from '../src/installation.ts';
import { LoginItem, loginFlag, RuntimeAutostart } from '../src/login-item.ts';
import { PathCommand } from '../src/path-command.ts';
import { ProviderKeys } from '../src/providers.ts';
import { readSettings, writeSettings, type DesktopSettings } from '../src/settings.ts';
import { Setup, writeAtomically } from '../src/setup.ts';
import {
    bundledPlugin,
    defaultRoutePlugin,
    parseInstallationSettings,
    parseReplyAccounts,
    parseReplyAuth,
    Refused,
    serverPattern,
    withPluginConfigured,
    withReplyWired,
} from '../src/setup-model.ts';
import { bench, fakeBox, memoryLog, snapshot, written, type Bench } from './harness.ts';

function setupOn(b: Bench, log: Logger = memoryLog(), settings: DesktopSettings = { feed: null, skillsDeployedFor: null }) {
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
        Plugins: { Grants: { ntfy: { Http: ['ntfy.sh'] } } },
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
        Plugins: { Grants: { ntfy: { Http: ['ntfy.example.com:8443'] } } },
        Notifications: { Plugins: { ntfy: { Binding: { topic: 'alerts', server: 'https://ntfy.example.com:8443' } } } },
    });
    // Absent keeps the server and so its grant; empty removes it and puts the grant back to the public server.
    assert.deepEqual((withPluginConfigured(own, ntfy, { target: 'other' }) as { Plugins: unknown }).Plugins, own.Plugins);
    assert.deepEqual(withPluginConfigured(own, ntfy, { server: '' }), {
        Plugins: { Grants: { ntfy: { Http: ['ntfy.sh'] } } },
        Notifications: { Plugins: { ntfy: { Binding: { topic: 'alerts' } } } },
    });
    assert.deepEqual((withPluginConfigured({}, ntfy, { target: 't', server: 'https://ntfy.example.com:443' }) as { Plugins: unknown }).Plugins, {
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

    // A default grant is the host the manifest declares, never a star.
    for (const id of ['slack', 'telegram', 'ntfy']) {
        const manifest = readFileSync(join(import.meta.dirname, '..', '..', 'plugins', id, 'plugin.yaml'), 'utf8').replace(/\r\n/g, '\n');
        const hosts = /\n {2}http:\n(?: {4}#.*\n| {4}any_granted_host:.*\n)* {4}hosts: \[([^\]]*)\]/.exec(manifest)?.[1]?.split(',').map((host) => host.trim());
        assert.deepEqual(bundledPlugin(id)!.http, hosts, id);
    }
});

test('a grant is written when there is none, or when what it derives from changes; a person’s own grant is kept', () => {
    const slack = bundledPlugin('slack')!;
    const theirs = { Plugins: { Grants: { slack: { Http: ['slack.com', 'files.slack.com'] } } } };
    const kept = withPluginConfigured(theirs, slack, { token: 't2', target: 'C0123ABCD' }) as typeof theirs;
    assert.deepEqual(kept.Plugins.Grants.slack.Http, ['slack.com', 'files.slack.com'], 'a token or a channel change keeps it');

    const ntfy = bundledPlugin('ntfy')!;
    // A self-hosted server set by hand, with its grant: configuring the topic keeps both.
    const selfHosted = {
        Plugins: { Grants: { ntfy: { Http: ['ntfy.mine.example'], Env: ['NTFY_TOKEN'] } } },
        Notifications: { Plugins: { ntfy: { Binding: { topic: 'old', server: 'https://ntfy.mine.example' } } } },
    };
    const topic = withPluginConfigured(selfHosted, ntfy, { target: 'new-topic' }) as typeof selfHosted;
    assert.deepEqual(topic.Plugins.Grants.ntfy, { Http: ['ntfy.mine.example'], Env: ['NTFY_TOKEN'] });
    assert.equal(topic.Notifications.Plugins.ntfy.Binding.topic, 'new-topic');
    // A grant written by hand for the public server stays as it is when only the topic changes.
    const star = withPluginConfigured({ Plugins: { Grants: { ntfy: { Http: ['*'] } } } }, ntfy, { target: 'a' }) as typeof selfHosted;
    assert.deepEqual(star.Plugins.Grants.ntfy.Http, ['*']);
    // The server it derives from changes: the grant follows it, and the Env grant beside it is kept.
    const moved = withPluginConfigured(selfHosted, ntfy, { server: 'https://other.example:8443' }) as typeof selfHosted;
    assert.deepEqual(moved.Plugins.Grants.ntfy, { Http: ['other.example:8443'], Env: ['NTFY_TOKEN'] });
    assert.deepEqual((withPluginConfigured(selfHosted, ntfy, { server: 'https://ntfy.mine.example' }) as typeof selfHosted).Plugins.Grants.ntfy.Http, ['ntfy.mine.example']);

    const forwarder = bundledPlugin('journal-forwarder')!;
    const receiving = { Plugins: { Config: { 'journal-forwarder': { url: 'https://hooks.example.com/in' } }, Grants: { 'journal-forwarder': { Http: ['hooks.example.com', 'backup.example.com'] } } } };
    const token = withPluginConfigured(receiving, forwarder, { token: 'b' }) as typeof receiving;
    assert.deepEqual(token.Plugins.Grants['journal-forwarder'].Http, ['hooks.example.com', 'backup.example.com']);
    const url = withPluginConfigured(receiving, forwarder, { target: 'https://elsewhere.example.com/in' }) as typeof receiving;
    assert.deepEqual(url.Plugins.Grants['journal-forwarder'].Http, ['elsewhere.example.com']);
    assert.throws(() => withPluginConfigured(receiving, forwarder, { target: '' }), Refused, 'an empty target is refused, not removed');
});

test('a section is made only when a value is written into it', () => {
    assert.deepEqual(withPluginConfigured({}, bundledPlugin('slack')!, { token: '' }), {}, 'removing what is not there writes nothing');
    assert.deepEqual(withPluginConfigured({ Harness: { Root: 'r' } }, bundledPlugin('ntfy')!, { server: '' }), { Harness: { Root: 'r' } });
    const ntfy = withPluginConfigured({}, bundledPlugin('ntfy')!, { target: 'a' }) as { Plugins: Record<string, unknown> };
    assert.deepEqual(Object.keys(ntfy.Plugins), ['Grants'], 'ntfy takes nothing under Plugins:Config');
});

test('the parse refusal says comments and trailing commas are among what the desktop cannot keep', () => {
    for (const text of ['{ // a note\n "Dispatcher": {} }', '{ "Dispatcher": {}, }', '{ "Plugins": ']) {
        assert.throws(() => parseInstallationSettings(text), (error: unknown) => error instanceof Refused && /comments and trailing commas/.test(error.message) && /runtime reads/.test(error.message), text);
    }
    assert.deepEqual(parseInstallationSettings(null), {});
});

test('wiring Reply grants it its executable and names it the global default, only where no default is set', () => {
    const fresh = withReplyWired({ Harness: { Root: 'r' } }, 'default');
    assert.deepEqual(fresh, {
        routeWritten: true,
        settings: {
            Harness: { Root: 'r' },
            Plugins: { Grants: { reply: { Exec: ['reply'] } } },
            Routes: { Default: { Plugin: 'reply', Binding: { profile: 'default' } } },
        },
    });
    assert.deepEqual((withReplyWired({}, 'work').settings as { Routes: unknown }).Routes, { Default: { Plugin: 'reply', Binding: { profile: 'work' } } });
    const foreign = { routes: { default: { Plugin: 'other' } }, Plugins: { Grants: { reply: { Exec: ['reply'], Http: ['x'] } } } };
    assert.deepEqual(withReplyWired(foreign, 'default'), { routeWritten: false, settings: foreign });
    // Operations routed by hand are a Routes key of their own, and a default is still set beside them.
    const operations = withReplyWired({ Routes: { Operations: { 'contact.find': { Plugin: 'other' } } } }, 'default');
    assert.equal(operations.routeWritten, true);
    assert.deepEqual((operations.settings as { Routes: unknown }).Routes, { Operations: { 'contact.find': { Plugin: 'other' } }, Default: { Plugin: 'reply', Binding: { profile: 'default' } } });
});

test('wiring Reply keeps an Exec grant that already holds reply, and writes ["reply"] only where none does', () => {
    const widened = { Plugins: { Grants: { reply: { Exec: ['reply', 'x'] } } }, Routes: { Default: { Plugin: 'other' } } };
    assert.deepEqual(withReplyWired(widened, 'default').settings, widened);
    const lowerCase = { plugins: { grants: { reply: { exec: ['x', 'reply'] } } }, Routes: { Default: { Plugin: 'other' } } };
    assert.deepEqual(withReplyWired(lowerCase, 'default').settings, lowerCase);
    for (const exec of [['x'], [], 'reply', null]) {
        const wired = withReplyWired({ Plugins: { Grants: { reply: { Exec: exec } } }, Routes: { Default: { Plugin: 'other' } } }, 'default');
        assert.deepEqual((wired.settings as { Plugins: unknown }).Plugins, { Grants: { reply: { Exec: ['reply'] } } }, JSON.stringify(exec));
    }
});

test('Reply’s answers are read in the shapes reply-cli prints, and the route list by its global default', () => {
    assert.deepEqual(parseReplyAuth(JSON.stringify({ authenticated: true, profile: 'work', source: 'store', method: 'api_key', user: { id: 7, username: 'someone@example.com', team_id: 3 } })), {
        signedIn: true,
        user: 'someone@example.com',
        profile: 'work',
    });
    assert.deepEqual(parseReplyAuth(JSON.stringify({ authenticated: false, profile: 'default' })), { signedIn: false, user: null, profile: 'default' });
    assert.deepEqual(parseReplyAuth('Not signed in.'), { signedIn: false, user: null, profile: null });
    assert.equal(parseReplyAuth(JSON.stringify({ authenticated: true, profile: 'x'.repeat(65) })).profile, null, 'a profile the binding cannot hold is not taken');
    assert.deepEqual(parseReplyAccounts(JSON.stringify({ items: [{ id: 5, channel: 'email', name: 'Sales', address: 'sales@example.com', owner_user_id: 7, status: 'active', reply_status: 'ok' }] })), [
        { id: '5', channel: 'email', name: 'Sales', address: 'sales@example.com', status: 'active' },
    ]);
    const routes = (global: unknown) => JSON.stringify({ routing_snapshot_id: 'rts_1', plugin_snapshot_id: 'snp_1', global: { default: global, operations: {} }, campaigns: [] });
    assert.equal(defaultRoutePlugin(routes({ plugin_id: 'reply', binding: { profile: 'default' }, binding_identity: null })), 'reply');
    assert.equal(defaultRoutePlugin(routes(null)), null);
    assert.equal(defaultRoutePlugin('{'), null);
});

// ---- The written set -----------------------------------------------------------------------------------------------

/** Every leaf of a settings object by its path, `A:B:C`, an array being one leaf. */
function leaves(value: unknown, prefix = ''): Map<string, string> {
    const found = new Map<string, string>();
    if (typeof value === 'object' && value !== null && !Array.isArray(value)) {
        for (const [key, inner] of Object.entries(value)) for (const [path, leaf] of leaves(inner, prefix === '' ? key : `${prefix}:${key}`)) found.set(path, leaf);
    } else if (prefix !== '') {
        found.set(prefix, JSON.stringify(value));
    }
    return found;
}

function settingsLeaves(b: Bench): Map<string, string> {
    return existsSync(b.paths.installationSettings) ? leaves(readInstallationSettings(b)) : new Map();
}

const replyLeftovers = () => readdirSync(tmpdir()).filter((name) => name.startsWith('jason-reply-skills-')).length;

test('each act of the Connect cards and a chat turn writes under <data> what it names and nothing else, and nothing outside <data>', async (t) => {
    const b = bench(t, { programs: ['reply', 'npm', 'claude'] });
    writeInstallationSettings(b, { Plugins: { Grants: { reply: { Exec: ['reply'] } } }, Harness: { Root: 'kept' } });
    // The bench's root holds the data directory, the stubs' PATH and Node, and the stubs' own record of what they ran.
    const outside = () => ({ top: readdirSync(b.root).filter((name) => name !== 'record.jsonl').sort(), bin: [...snapshot(join(b.root, 'bin'))] });
    const outsideBefore = outside();
    const leftoversBefore = replyLeftovers();

    const log = fileLogger(b.paths.logs);
    const isLog = (path: string) => /^logs\/desktop-\d{8}(?:_\d{3,9})?\.jsonl$/.test(path);
    const act = async (name: string, run: () => Promise<unknown>, files: readonly (string | RegExp)[], keys: readonly string[]) => {
        const before = snapshot(b.paths.data);
        const settingsBefore = settingsLeaves(b);
        await run();
        for (const path of written(before, snapshot(b.paths.data)).filter((path) => !isLog(path))) {
            assert.ok(files.some((file) => (typeof file === 'string' ? file === path : file.test(path))), `${name} wrote ${path}`);
        }
        const settingsAfter = settingsLeaves(b);
        for (const path of new Set([...settingsBefore.keys(), ...settingsAfter.keys()])) {
            if (settingsBefore.get(path) === settingsAfter.get(path)) continue;
            assert.ok(keys.some((key) => path === key || path.startsWith(`${key}:`)), `${name} changed ${path}`);
        }
    };

    let desktopSettings: DesktopSettings = readSettings(b.paths.settings);
    await act('the start', () => startInstallation({
        cli: loggedCli(b.cli, log),
        paths: b.paths,
        log,
        bundle: join(b.root, 'bundle'),
        bundleVersion: '0.34.0-rc.1',
        settings: desktopSettings,
        saveSettings: (next) => {
            desktopSettings = next;
            writeSettings(b.paths.settings, next);
        },
    }), ['run/runtime.json', 'desktop/settings.json'], []);

    const setup = setupOn(b, log, desktopSettings);
    const replySkills = /^desktop\/chat\/\.claude\/skills\/reply-[a-z-]+\/SKILL\.md$/;
    const replyKeys = ['Plugins:Grants:reply:Exec', 'Routes:Default'];
    const pluginKeys = (id: string) => [`Plugins:Config:${id}`, `Plugins:Grants:${id}:Http`, `Notifications:Plugins:${id}:Binding`];
    await act('status', () => setup.status(), [], []);
    await act('connectReplyWithKey', () => setup.connectReplyWithKey('planted-reply-key'), ['config/settings.json', replySkills], replyKeys);
    await act('connectReply', () => setup.connectReply(), ['config/settings.json', replySkills], replyKeys);
    await act('setProviderKey', () => setup.setProviderKey('deepseek', 'sk-planted-provider'), ['desktop/provider-keys'], []);
    await act('installProgram', () => setup.installProgram('codex'), [], []);
    await act('configurePlugin slack', () => setup.configurePlugin('slack', { token: 'xoxb-planted', target: 'C0123ABCD' }), ['config/settings.json'], pluginKeys('slack'));
    await act('configurePlugin telegram', () => setup.configurePlugin('telegram', { token: 'planted-bot', target: '@team_room' }), ['config/settings.json'], pluginKeys('telegram'));
    await act('configurePlugin ntfy', () => setup.configurePlugin('ntfy', { target: 'jason-topic', server: 'https://ntfy.example.com' }), ['config/settings.json'], pluginKeys('ntfy'));
    await act('configurePlugin journal-forwarder', () => setup.configurePlugin('journal-forwarder', { token: 'b', target: 'https://hooks.example.com/in' }), ['config/settings.json'], pluginKeys('journal-forwarder'));
    await act('Reload', () => setup.configurePlugin('slack', {}), [], []);

    // A chat turn writes one file, its MCP configuration beside the chat folder, and removes it when the turn is over.
    const turnFile = /^desktop\/turn-[0-9a-f]{24}\.mcp\.json$/;
    let during: string[] = [];
    await act('a chat turn', async () => {
        b.env.STUB_HOST_PLAN = JSON.stringify([{ ask: { tool: 'Bash', input: { command: 'git status' } } }]);
        const before = snapshot(b.paths.data);
        let finished: () => void = () => undefined;
        const done = new Promise<void>((resolve) => (finished = resolve));
        const chat: Chat = new Chat({
            paths: b.paths,
            env: b.env,
            bundle: join(b.root, 'bin'),
            dataNamed: true,
            cli: loggedCli(b.cli, log),
            keyFor: () => undefined,
            denied: () => parseDenyList(readFileSync(join(import.meta.dirname, '..', denyListFile), 'utf8')),
            emit(_chatId, event: ChatEvent) {
                if (event.kind === 'permission') {
                    during = written(before, snapshot(b.paths.data)).filter((path) => !isLog(path));
                    chat.answer('c1', event.request, { allow: true, always: false });
                }
                if (event.kind === 'done') finished();
            },
            ...loggedTurns(log),
        });
        await chat.send('c1', 'hello', { host: 'claude' });
        await done;
    }, [], []);
    assert.equal(during.length, 1, `while the turn waited: ${during.join(', ')}`);
    assert.match(during[0] ?? '', turnFile);
    assert.ok(![...snapshot(b.paths.data).keys()].some((path) => turnFile.test(path)), 'the turn file is gone once the turn is over');

    assert.deepEqual(readInstallationSettings(b), {
        Plugins: {
            Grants: {
                reply: { Exec: ['reply'] },
                slack: { Http: ['slack.com'] },
                telegram: { Http: ['api.telegram.org'] },
                ntfy: { Http: ['ntfy.example.com'] },
                'journal-forwarder': { Http: ['hooks.example.com'] },
            },
            Config: { slack: { token: 'xoxb-planted' }, telegram: { token: 'planted-bot' }, 'journal-forwarder': { token: 'b', url: 'https://hooks.example.com/in' } },
        },
        Harness: { Root: 'kept' },
        Routes: { Default: { Plugin: 'reply', Binding: { profile: 'default' } } },
        Notifications: {
            Plugins: {
                slack: { Binding: { channel: 'C0123ABCD' } },
                telegram: { Binding: { chat: '@team_room' } },
                ntfy: { Binding: { topic: 'jason-topic', server: 'https://ntfy.example.com' } },
            },
        },
    });
    assert.equal(readFileSync(b.paths.installationSettings, 'utf8').includes('planted-reply-key'), false, 'the Reply key is the Reply CLI’s alone');
    assert.equal(readFileSync(b.paths.providerKeys, 'utf8').includes('sk-planted-provider'), false, 'the key is kept encrypted');
    assert.equal(readFileSync(b.paths.settings, 'utf8').includes('planted'), false);

    assert.deepEqual(outside(), outsideBefore, 'nothing is written beside <data>');
    assert.equal(replyLeftovers(), leftoversBefore, 'the folder the Reply skills are written to is removed');
});

test('each act of the desk’s menu writes what it names and nothing else: under the home only the LaunchAgent and the PATH command, under <data> nothing', async (t) => {
    const b = bench(t);
    const home = join(b.root, 'home');
    mkdirSync(home);
    const log = fileLogger(b.paths.logs);
    const isLog = (path: string) => /^logs\/desktop-\d{8}(?:_\d{3,9})?\.jsonl$/.test(path);
    const outside = () => readdirSync(b.root).filter((name) => name !== 'record.jsonl' && name !== 'home').sort();
    const outsideBefore = outside();
    const act = async (name: string, run: () => Promise<unknown>, inHome: readonly string[]) => {
        const data = snapshot(b.paths.data);
        const before = snapshot(home);
        await run();
        assert.deepEqual(written(data, snapshot(b.paths.data)).filter((path) => !isLog(path)), [], `${name} wrote under <data>`);
        const after = snapshot(home);
        const changed = [...new Set([...before.keys(), ...after.keys()])].filter((path) => before.get(path) !== after.get(path)).sort();
        assert.deepEqual(changed, inHome, `${name} changed in the home`);
    };

    const registered: unknown[] = [];
    const electron = { getLoginItemSettings: () => ({ openAtLogin: false }), setLoginItemSettings: (settings: unknown) => void registered.push(settings) };
    const login = (platform: NodeJS.Platform) => new LoginItem({ platform, home, executable: '/Applications/Jason.app/Contents/MacOS/Jason', electron, pathRead: Promise.resolve(), log });
    const autostart = new RuntimeAutostart({ cli: loggedCli(b.cli, log), log });
    const agent = 'Library/LaunchAgents/ai.jasonharness.desktop.plist';

    await act('Open Jason at login, on, macOS', () => login('darwin').set(true), [agent]);
    await act('Open Jason at login, off, macOS', () => login('darwin').set(false), [agent]);
    await act('Open Jason at login, on, Windows', () => login('win32').set(true), []);
    await act('Open Jason at login, off, Windows', () => login('win32').set(false), []);
    await act('Start the runtime at logon, read', () => autostart.registered(), []);
    await act('Start the runtime at logon, on', () => autostart.set(true), []);
    await act('Start the runtime at logon, off', () => autostart.set(false), []);

    // The PATH command's folders, injected under the test's home: %LOCALAPPDATA%\Microsoft\WindowsApps (the shim and its
    // Git Bash wrapper) and ~/.local/bin.
    const windowsApps = join(home, 'AppData', 'Local', 'Microsoft', 'WindowsApps');
    mkdirSync(windowsApps, { recursive: true });
    const command = (platform: NodeJS.Platform, folder: string) =>
        new PathCommand({ platform, folder, executable: platform === 'win32' ? 'C:\\J\\jason.exe' : '/J/jason', dataDirectory: null, path: () => folder, isProgram: () => false, log });
    await act('The PATH command, read, Windows', async () => command('win32', windowsApps).status(), []);
    await act('The PATH command, placed, Windows', async () => command('win32', windowsApps).place(), ['AppData/Local/Microsoft/WindowsApps/jason', 'AppData/Local/Microsoft/WindowsApps/jason.cmd']);
    await act('The PATH command, removed, Windows', async () => command('win32', windowsApps).remove(), ['AppData/Local/Microsoft/WindowsApps/jason', 'AppData/Local/Microsoft/WindowsApps/jason.cmd']);
    await act('The PATH command, placed, macOS', async () => command('darwin', join(home, '.local', 'bin')).place(), ['.local/bin/jason']);
    await act('The PATH command, removed, macOS', async () => command('darwin', join(home, '.local', 'bin')).remove(), ['.local/bin/jason']);

    assert.deepEqual(registered, [{ openAtLogin: true, args: [loginFlag] }, { openAtLogin: false, args: [loginFlag] }]);
    assert.deepEqual(b.record().map((run) => run.args), [['runtime', 'autostart', 'status'], ['runtime', 'autostart', 'enable'], ['runtime', 'autostart', 'disable']]);
    assert.deepEqual(outside(), outsideBefore, 'nothing is written beside <data> and the home');
});

test('the settings file is written whole or not at all, and a write that cannot be renamed into place leaves no file beside it', (t) => {
    const b = bench(t);
    const file = join(b.paths.data, 'config', 'settings.json');
    writeAtomically(file, '{"a":1}\n');
    assert.equal(readFileSync(file, 'utf8'), '{"a":1}\n');
    assert.deepEqual(readdirSync(dirname(file)), ['settings.json']);

    // A folder where the file should be: the rename fails, and the temporary file is removed.
    const blocked = join(b.paths.data, 'blocked', 'settings.json');
    mkdirSync(join(blocked, 'inside'), { recursive: true });
    assert.throws(() => writeAtomically(blocked, '{}'));
    assert.deepEqual(readdirSync(dirname(blocked)), ['settings.json']);
});

// ---- The cards' actions against stubs on the PATH ------------------------------------------------------------------

test('a plugin is placed with jason plugin install, its settings written, then jason plugin reload; its token on no command line and in no environment', async (t) => {
    const b = bench(t);
    const settings: DesktopSettings = { feed: 'http://127.0.0.1:9/manifest.json', skillsDeployedFor: null };
    const log = memoryLog();
    await setupOn(b, log, settings).configurePlugin('telegram', { token: 'planted-bot-token', target: '@team_room' });
    const runs = b.record().filter((entry) => entry.name === 'jason');
    assert.deepEqual(runs.map((entry) => entry.args), [['plugin', 'install', 'telegram', '--feed', 'http://127.0.0.1:9/manifest.json'], ['plugin', 'reload'], ['plugin', 'list']]);
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
    assert.deepEqual(fine.record().filter((entry) => entry.name === 'jason').map((entry) => entry.args), [['plugin', 'reload'], ['plugin', 'list']]);
    assert.equal(existsSync(fine.paths.installationSettings), false);
    assert.equal((await ok.status()).settingsRefused, null);

    // A file there is left byte for byte, its time included, and nothing but the reload and its read-back is run.
    const kept = bench(t);
    writeInstallationSettings(kept, '{ "Dispatcher": { "MaxParallel": 2 } }');
    const mtime = statSync(kept.paths.installationSettings).mtimeMs;
    await setupOn(kept).configurePlugin('journal-forwarder', {});
    assert.equal(readFileSync(kept.paths.installationSettings, 'utf8'), '{ "Dispatcher": { "MaxParallel": 2 } }');
    assert.equal(statSync(kept.paths.installationSettings).mtimeMs, mtime);
    assert.deepEqual(kept.record().map((entry) => [entry.name, ...entry.args]), [['jason', 'plugin', 'reload'], ['jason', 'plugin', 'list']]);

    const unknown = bench(t);
    await assert.rejects(setupOn(unknown).configurePlugin('reply', {}), Refused, 'the Reload of a plugin outside the four is refused');
    await assert.rejects(setupOn(unknown).configurePlugin('../x', {}), Refused);
    assert.deepEqual(unknown.record(), []);
});

test('a settings file the desktop cannot parse is refused, said so once, and left as it was', async (t) => {
    const b = bench(t);
    writeInstallationSettings(b, '{ "Plugins": {}, }');
    const log = memoryLog();
    await assert.rejects(
        setupOn(b, log).configurePlugin('slack', { token: 't', target: 'C1234ABCD' }),
        (error: unknown) => error instanceof Refused && /comments and trailing commas/.test(error.message) && error.message.endsWith('Edit it by hand, then press Reload.'),
    );
    assert.equal(readFileSync(b.paths.installationSettings, 'utf8'), '{ "Plugins": {}, }');
    assert.deepEqual(b.record(), [], 'nothing is installed or reloaded');
    assert.deepEqual(log.lines.map((line) => line.name), ['settings_refused']);
    // The page adds the sentence that says what to do to what this says, so it is not said here as well.
    const refused = (await setupOn(b).status()).settingsRefused ?? '';
    assert.match(refused, /comments and trailing commas/);
    assert.equal(refused.includes('press Reload'), false);
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
    assert.deepEqual(replies, ['auth login', 'auth status --json', 'skills install adapter --project --agent codex']);
});

test('a sign-in wires Jason’s Reply plugin: placed, granted reply, named the global default, and reloaded', async (t) => {
    const b = bench(t, { programs: ['reply'] });
    const settings: DesktopSettings = { feed: 'http://127.0.0.1:9/manifest.json', skillsDeployedFor: null };
    const log = memoryLog();
    await setupOn(b, log, settings).connectReplyWithKey('planted-reply-key');
    assert.deepEqual(b.record().filter((entry) => entry.name === 'jason').map((entry) => entry.args), [
        ['plugin', 'install', 'reply', '--feed', 'http://127.0.0.1:9/manifest.json'],
        ['plugin', 'reload'],
    ]);
    assert.deepEqual(readInstallationSettings(b), {
        Plugins: { Grants: { reply: { Exec: ['reply'] } } },
        Routes: { Default: { Plugin: 'reply', Binding: { profile: 'default' } } },
    });
    assert.equal(readFileSync(b.paths.installationSettings, 'utf8').includes('planted-reply-key'), false);
    assert.equal(JSON.stringify(log.lines).includes('planted-reply-key'), false);
    assert.deepEqual(log.lines.find((line) => line.name === 'reply_wired')?.fields, { RouteWritten: true, Exit: 0 });
    assert.equal((await setupOn(b).status()).reply.wired, true);

    // The profile reply auth status names is the one the route binds.
    const work = bench(t, { programs: ['reply'], env: { STUB_REPLY_PROFILE: 'work' } });
    await setupOn(work).connectReply();
    assert.deepEqual((readInstallationSettings(work) as { Routes: unknown }).Routes, { Default: { Plugin: 'reply', Binding: { profile: 'work' } } });
});

test('a global default a person set to another plugin is kept, and the Reply card does not say it is wired', async (t) => {
    const b = bench(t, { programs: ['reply'] });
    writeInstallationSettings(b, { Routes: { Default: { Plugin: 'other', Binding: { account: 'a' } } } });
    const log = memoryLog();
    await setupOn(b, log).connectReply();
    assert.deepEqual(readInstallationSettings(b), {
        Routes: { Default: { Plugin: 'other', Binding: { account: 'a' } } },
        Plugins: { Grants: { reply: { Exec: ['reply'] } } },
    });
    assert.deepEqual(log.lines.find((line) => line.name === 'reply_wired')?.fields, { RouteWritten: false, Exit: 0 });
    assert.equal((await setupOn(b).status()).reply.wired, false);

    // A file the desktop cannot parse is left as it is, and the sign-in itself still stands.
    const unreadable = bench(t, { programs: ['reply'] });
    writeInstallationSettings(unreadable, '{ /* mine */ }');
    await assert.rejects(setupOn(unreadable).connectReply(), (error: unknown) => error instanceof Refused && /comments/.test(error.message));
    assert.equal(readFileSync(unreadable.paths.installationSettings, 'utf8'), '{ /* mine */ }');
    assert.equal(unreadable.record().some((entry) => entry.name === 'jason'), false);
    assert.ok(existsSync(join(unreadable.paths.chat, '.claude', 'skills', 'reply-cli', 'SKILL.md')), 'the Reply skills are deployed all the same');
});

test('the Reply card reads the CLI, its sign-in and whether Jason’s Reply plugin is valid and the global default', async (t) => {
    const b = bench(t, { programs: ['reply'], env: { STUB_REPLY_SIGNED_IN: 'yes' } });
    assert.equal((await setupOn(b).status()).reply.wired, false, 'valid, but no route names it');
    writeInstallationSettings(b, { Routes: { Default: { Plugin: 'reply', Binding: { profile: 'default' } } } });
    const { reply } = await setupOn(b).status();
    assert.deepEqual(reply, { cli: { found: true, version: '0.19.0', install: null }, signedIn: true, user: 'someone@example.com', wired: true, accounts: [] });
    assert.ok(b.record().some((entry) => entry.name === 'reply' && entry.args.join(' ') === 'account list --json'));

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
    assert.equal(asked.some((said) => said.startsWith('account list')), false, 'that version has no account command');

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
