import assert from 'node:assert/strict';
import { AgentFolderUnowned } from '../src/chat-agent-folder.ts';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent } from '../src/chat.ts';
import { readFence } from '../src/chat-deny.ts';
import { loggedTurns } from '../src/chat-log.ts';
import { floorSentence, sharingVersionReads, versionLimitMs } from '../src/host-version.ts';
import { codexAutoEdit, fullAccessNotYet, fullAccessSentence, tabFullAccess } from '../src/chat-modes.ts';
import { fileLogger, type Logger } from '../src/log.ts';
import { loggedCli, startInstallation } from '../src/installation.ts';
import { LoginItem, loginFlag, RuntimeAutostart } from '../src/login-item.ts';
import { PathCommand } from '../src/path-command.ts';
import { ProviderKeys } from '../src/providers.ts';
import { npmLine, readUpdateSwitch, replyAtWork, replyStartCheck, runtimeReplyVersion, updateSwitch, versionUnsaid } from '../src/reply-update.ts';
import { parseSettings, readSettings, saveSettings, writeSettings, type DesktopSettings } from '../src/settings.ts';
import type { Programs, RunResult } from '../src/programs.ts';
import type { Cli } from '../src/cli.ts';
import { installLimit, Setup, writeAtomically, type SetupDeps, type SetupState } from '../src/setup.ts';
import {
    bundledPlugin,
    parseInstallationSettings,
    parseReplySenders,
    parseReplyAuth,
    readReplyAuth,
    readReplyChannel,
    Refused,
    replyMinimumVersion,
    replyPlacement,
    serverPattern,
    withPluginConfigured,
    withReplyGranted,
} from '../src/setup-model.ts';
import { claudeCodeModels } from '../src/chat-models.ts';
import { accountsOf, defaultReason, readProfiles, readRoutes, readTeams } from '../src/accounts.ts';
import { belowMinimumSentence, type ReplyBox } from '../src/reply-accounts.ts';
import { bench, fakeBox, memoryLog, snapshot, written, type Bench } from './harness.ts';
import { printed } from './reply-cli-account-fixture.ts';
import { dataLayout } from '../src/paths.ts';
import { claudeWithSecrets, codexWithSecrets, plantedSecrets } from './own-servers-fixture.ts';

function setupDeps(b: Bench, log: Logger = memoryLog(), settings: DesktopSettings = { feed: null, skillsDeployedFor: null, theme: null }): SetupDeps {
    return {
        paths: b.paths,
        cli: loggedCli(b.cli, log),
        programs: b.programs,
        keys: new ProviderKeys(b.paths.providerKeys, fakeBox),
        environment: {},
        log,
        bundleVersion: '0.34.0-rc.1',
        runtimeVersion: () => '0.34.0-rc.1',
        settings: () => settings,
        person: 'made-up-person',
    };
}

function setupOn(b: Bench, log: Logger = memoryLog(), settings: DesktopSettings = { feed: null, skillsDeployedFor: null, theme: null }) {
    return new Setup(setupDeps(b, log, settings));
}

function writeInstallationSettings(b: Bench, value: unknown): void {
    mkdirSync(dirname(dataLayout(b.paths.data).userSettingsFile), { recursive: true });
    writeFileSync(dataLayout(b.paths.data).userSettingsFile, typeof value === 'string' ? value : JSON.stringify(value, null, 2));
}

const readInstallationSettings = (b: Bench) => JSON.parse(readFileSync(dataLayout(b.paths.data).userSettingsFile, 'utf8')) as Record<string, unknown>;

/** The Reply CLI's profiles and sign-ins as the stand-in keeps them (`STUB_REPLY_WORLD` in `stubs/program.cjs`). */
interface ReplyWorld {
    current?: string;
    profiles?: Record<string, { team_id?: number }>;
    /**
     * A browser's sign-in by its login, or one Reply did not name, which `auth status` holds no login for; or a key's, which
     * acts in its own team whatever is pinned.
     */
    credentials?: Record<string, string | { login: string; unnamed: true } | { login: string; key: number }>;
    seats?: Record<string, number[]>;
    lists?: Record<string, { email?: unknown[]; linkedin?: unknown[] }>;
    browser?: string;
    /** Each key a person may paste, by the login it is and the team it acts in. */
    keys?: Record<string, { login: string; team: number }>;
    /** Each team's name, by its id, as Reply lists a login's teams. */
    teams?: Record<string, string>;
}

/** A bench whose Reply CLI holds `world`, read back as the CLI left it. */
function replyBench(t: { after(fn: () => void): void }, world: ReplyWorld, env: Record<string, string> = {}): Bench & { world(): ReplyWorld } {
    const folder = mkdtempSync(join(tmpdir(), 'jason-reply-world-'));
    t.after(() => rmSync(folder, { recursive: true, force: true }));
    const file = join(folder, 'world.json');
    writeFileSync(file, JSON.stringify(world));
    const b = bench(t, { programs: ['reply'], env: { STUB_REPLY_WORLD: file, ...env } });
    return Object.assign(b, { world: () => JSON.parse(readFileSync(file, 'utf8')) as ReplyWorld });
}

/** A campaign's routes as `route.list` answers them, its default to Reply bound as given. */
const campaignRoute = (id: string, binding: Record<string, unknown> | null) => ({ campaign_id: id, default: { plugin_id: 'reply', binding, binding_identity: null }, operations: {}, channels: {} });

/** What `reply` was started with, each run's arguments as one line. */
const replyRuns = (b: Bench) => b.record().filter((entry) => entry.name === 'reply').map((entry) => entry.args.join(' '));

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

test('placing Jason’s Reply plugin grants it its executable and writes no route', () => {
    // The grant: ["reply"] only where the grant does not hold reply, every other key as it was, and never a route: a
    // default is the runtime's to write, at a person's click.
    assert.deepEqual(withReplyGranted({ Harness: { Root: 'r' } }), { Harness: { Root: 'r' }, Plugins: { Grants: { reply: { Exec: ['reply'] } } } });
    const foreign = { routes: { default: { Plugin: 'other' } }, Plugins: { Grants: { reply: { Exec: ['reply'], Http: ['x'] } } } };
    assert.deepEqual(withReplyGranted(foreign), foreign);
    assert.deepEqual(withReplyGranted({}), { Plugins: { Grants: { reply: { Exec: ['reply'] } } } }, 'no default is written where none is set');
});

test('the grant keeps an Exec grant that already holds reply, and writes ["reply"] only where none does', () => {
    const widened = { Plugins: { Grants: { reply: { Exec: ['reply', 'x'] } } }, Routes: { Default: { Plugin: 'other' } } };
    assert.deepEqual(withReplyGranted(widened), widened);
    const lowerCase = { plugins: { grants: { reply: { exec: ['x', 'reply'] } } }, Routes: { Default: { Plugin: 'other' } } };
    assert.deepEqual(withReplyGranted(lowerCase), lowerCase);
    for (const exec of [['x'], [], 'reply', null]) {
        const granted = withReplyGranted({ Plugins: { Grants: { reply: { Exec: exec } } }, Routes: { Default: { Plugin: 'other' } } });
        assert.deepEqual((granted as { Plugins: unknown }).Plugins, { Grants: { reply: { Exec: ['reply'] } } }, JSON.stringify(exec));
    }
});

/** A route to Jason's Reply plugin as `route.list` answers it, bound as given. */
const replyRoute = (binding: unknown) => ({ plugin_id: 'reply', binding, binding_identity: null });

/** What `route.list` answers (`RoutesDto`): the global routes, and each campaign that has routes of its own. */
const routeList = (global: unknown, campaigns: unknown[] = [], operations: unknown = {}, channels: unknown = {}) =>
    JSON.stringify({ routing_snapshot_id: 'rts_1', plugin_snapshot_id: 'snp_1', global: { default: global, operations, channels }, campaigns });

/** Routes to Reply at every level: a global default to another plugin, global operation and channel routes, three campaigns. */
const routesEverywhere = routeList(
    { plugin_id: 'slack', binding: { channel: 'C1' }, binding_identity: null },
    [
        { campaign_id: 'cmp_1', default: replyRoute({ profile: 'outbound', team_id: '9' }), operations: { 'message.send': replyRoute(null) }, channels: { 'message.send': { email: replyRoute({}) } } },
        { campaign_id: 'cmp_2', default: null, operations: {}, channels: { 'message.send': { linkedin: replyRoute({ profile: 'Made-up Ops', derive_first_name: false }) } } },
        { campaign_id: 'cmp_3', default: replyRoute({ team_id: '7' }), operations: {}, channels: {} },
    ],
    { 'contact.find': replyRoute({ profile: 'research' }), 'sender.list': replyRoute({}) },
    { 'message.send': { email: replyRoute(null) } },
);

test('Reply’s answers are read in the shapes reply-cli and the runtime print, each told from a read that did not answer', () => {
    // The team a sign-in is bound to is the top-level `team_id`, the team its runs send, never the team Reply's whoami gave
    // at the sign-in (`user.team_id`); one that is no positive whole number is none. A key acts in its own team whatever
    // is asked: for a key (`method` `api_key`), the team Reply's whoami gave at its sign-in is that team; a browser's has none.
    assert.deepEqual(parseReplyAuth(JSON.stringify({ authenticated: true, profile: 'work', source: 'store', method: 'api_key', user: { id: 7, username: 'someone@example.com', team_id: 3 } })), {
        signedIn: true,
        user: 'someone@example.com',
        profile: 'work',
        team: null,
        keyTeam: '3',
    });
    assert.deepEqual(parseReplyAuth(JSON.stringify({ authenticated: true, profile: 'work', method: 'oauth', user: { username: 'someone@example.com', team_id: 3 }, team_id: 9 })), {
        signedIn: true,
        user: 'someone@example.com',
        profile: 'work',
        team: '9',
        keyTeam: null,
    });
    for (const team of [0, -9, 2.5, 2 ** 53, '9', null]) {
        const read = parseReplyAuth(JSON.stringify({ authenticated: true, profile: 'work', method: 'api_key', user: { username: 'someone@example.com', team_id: team }, team_id: team }));
        assert.deepEqual([read.team, read.keyTeam], [null, null], JSON.stringify(team));
    }
    assert.deepEqual(parseReplyAuth(JSON.stringify({ authenticated: false, profile: 'default' })), { signedIn: false, user: null, profile: 'default', team: null, keyTeam: null });
    assert.deepEqual(parseReplyAuth('Not signed in.'), { signedIn: false, user: null, profile: null, team: null, keyTeam: null });
    assert.equal(parseReplyAuth(JSON.stringify({ authenticated: true, profile: 'x'.repeat(65) })).profile, null, 'a profile the binding cannot hold is not taken');
    assert.deepEqual(parseReplySenders(JSON.stringify({ items: [{ id: 5, channel: 'email', name: 'Sales', address: 'sales@example.com', owner_user_id: 7, status: 'active', reply_status: 'connected' }] })), [
        { id: '5', channel: 'email', name: 'Sales', address: 'sales@example.com', status: 'connected' },
    ], "the state is Reply's own word, reply_status, never the CLI's summary of it");
    const states = (items: Record<string, unknown>[]) => parseReplySenders(JSON.stringify({ items })).map((each) => each.status);
    assert.deepEqual(
        states([
            { channel: 'linkedin', status: 'inactive', reply_status: 'cookieInvalid' },
            { channel: 'email', status: 'other', reply_status: null },
            { channel: 'email', status: 'active' },
            { channel: 'email', status: 'active', reply_status: 7 },
        ]),
        ['cookieInvalid', '', '', ''],
        'a state Reply did not report, or one that is no word, is empty',
    );
    // The sign-in is read only from the object reply-cli prints, ending 0 (a credential) or 1 (none); every other ending
    // is a read that did not answer: a usage error (exit 2), a store it cannot read (exit 1, nothing on stdout), the time
    // limit (-1), or what is not that object.
    const auth = (exit: number, stdout: string) => readReplyAuth({ exit, stdout });
    assert.deepEqual(auth(0, JSON.stringify({ authenticated: true, profile: 'work', source: 'store', method: 'oauth', user: { id: 7, username: 'someone@example.com', team_id: 3 }, team_id: 3 })), { signedIn: true, user: 'someone@example.com', profile: 'work', team: '3', keyTeam: null });
    assert.deepEqual(auth(1, JSON.stringify({ authenticated: false, profile: 'default' })), { signedIn: false, user: null, profile: 'default', team: null, keyTeam: null });
    for (const [exit, stdout] of [[2, ''], [2, JSON.stringify({ authenticated: false, profile: 'default' })], [1, ''], [-1, ''], [0, ''], [0, 'Not signed in.'], [0, '{}'], [0, '{"authenticated":"yes"}'], [0, 'null'], [0, '[]']] as const) {
        assert.equal(auth(exit, stdout), null, `${exit} ${stdout}`);
    }
    // The profiles, as reply-cli 0.23.2 prints `profile list --json` (src/profile.ts `list_profiles`), `default` first; a
    // configuration it cannot read or parse ends 1 with its line on stderr alone, and anything but that object is unread.
    const profiles = (exit: number, stdout: string) => readProfiles({ exit, stdout });
    assert.deepEqual(profiles(0, JSON.stringify({ current: 'outbound', available: ['default', 'outbound', 'Made-up Ops'] })), { current: 'outbound', available: ['default', 'outbound', 'Made-up Ops'] });
    assert.deepEqual(profiles(0, JSON.stringify({ current: 'default', available: ['default'] })), { current: 'default', available: ['default'] }, 'a missing configuration holds default alone');
    for (const [exit, stdout] of [[1, ''], [2, ''], [-1, ''], [0, ''], [0, '{}'], [0, '{"current":"default"}'], [0, '{"current":7,"available":[]}'], [0, '{"current":"default","available":"default"}'], [0, 'null']] as const) {
        assert.equal(profiles(exit, stdout), null, `${exit} ${stdout}`);
    }
    // The teams a sign-in can act in, as reply-cli 0.23.2 prints `team list --json` (src/commands/team.ts): each once, by a
    // positive whole id, its name with control and format characters made spaces, trimmed and cut to 100 characters, since
    // the page draws it; a list with none of those, or any other ending, offers none.
    const teams = (exit: number, stdout: string) => readTeams({ exit, stdout });
    const listedTeams = (each: unknown[]) => JSON.stringify({ current_team_id: null, teams: each });
    assert.deepEqual(
        teams(0, listedTeams([
            { team_id: 5, team_name: ' Made-up\u0007Five\u200b ' },
            { team_id: 5, team_name: 'Made-up Five again' },
            { team_id: 0, team_name: 'zero' },
            { team_id: -9, team_name: 'below zero' },
            { team_id: 2.5, team_name: 'a fraction' },
            { team_id: 2 ** 53, team_name: 'past a safe integer' },
            { team_id: '7', team_name: 'an id in words' },
            { team_id: 9, team_name: 'N'.repeat(101) },
            { team_id: 11 },
        ])),
        [{ id: '5', name: 'Made-up Five' }, { id: '9', name: 'N'.repeat(100) }, { id: '11', name: '' }],
    );
    for (const [exit, stdout] of [[1, listedTeams([{ team_id: 5, team_name: 'x' }])], [0, listedTeams([])], [0, listedTeams([{ team_id: 0 }])], [0, '{}'], [0, 'not json'], [0, 'null']] as const) {
        assert.equal(teams(exit, stdout), null, `${exit} ${stdout}`);
    }
    // The routes, as the runtime answers route.list (RoutesDto): the global default, or none; the profile every route to
    // Reply names, at any level, global or a campaign's, null where it names none, a team beside it or alone no part of it;
    // and each global route to Reply but the default, by the kind of work it routes: an operation, or an operation's
    // channel.
    const route = (exit: number, stdout: string) => readRoutes({ exit, stdout });
    assert.deepEqual(route(0, routeList(replyRoute({ profile: 'default' }))), { globalDefault: { plugin: 'reply', binding: { profile: 'default' } }, reply: ['default'], globalReply: [] });
    assert.deepEqual(route(0, routeList(null)), { globalDefault: null, reply: [], globalReply: [] }, 'no global default is an answer');
    const everywhere = route(0, routesEverywhere);
    assert.deepEqual(everywhere, {
        globalDefault: { plugin: 'slack', binding: { channel: 'C1' } },
        reply: ['research', null, null, 'outbound', null, null, 'Made-up Ops', null],
        globalReply: [
            { mark: { kind: 'operation', operation: 'contact.find' }, profile: 'research' },
            { mark: { kind: 'operation', operation: 'sender.list' }, profile: null },
            { mark: { kind: 'channel', operation: 'message.send', channel: 'email' }, profile: null },
        ],
    });
    const otherPlugin = routeList(null, [], { 'contact.find': { plugin_id: 'slack', binding: null, binding_identity: null } }, { 'message.send': { linkedin: { plugin_id: 'slack', binding: null, binding_identity: null } } });
    assert.deepEqual(route(0, otherPlugin)?.globalReply, [], 'a global route to another plugin marks no Reply account');
    const unreachable = JSON.stringify({ error: { code: 'runtime_unreachable', message: 'No runtime answered.', retryable: true } });
    for (const [exit, stdout] of [[0, '{'], [0, '{}'], [0, '{"global":{}}'], [0, '{"global":{"default":{}}}'], [0, 'null'], [3, unreachable], [1, routeList(null)], [-1, '']] as const) {
        assert.equal(route(exit, stdout), null, `${exit} ${stdout}`);
    }
    // The Reply plugin as the runtime's plugin list holds it, read once for the card and for a placing: listed or not,
    // valid or in another state, its Exec grant holding the Reply CLI or not; or no list given.
    const plugins = (exit: number, stdout: string) => replyPlacement({ exit, stdout });
    const granted = { exec: { granted: ['reply'] } };
    assert.deepEqual(plugins(0, JSON.stringify({ plugins: [{ id: 'reply', status: 'valid', capabilities: granted }] })), { placed: true, valid: true, granted: true });
    assert.deepEqual(plugins(0, JSON.stringify({ plugins: [{ id: 'reply', status: 'valid' }] })), { placed: true, valid: true, granted: false });
    assert.deepEqual(plugins(0, JSON.stringify({ plugins: [{ id: 'reply', status: 'unavailable', capabilities: granted }, { id: 'slack', status: 'valid' }] })), { placed: true, valid: false, granted: true });
    assert.deepEqual(plugins(0, JSON.stringify({ plugins: [{ id: 'slack', status: 'valid' }] })), { placed: false, valid: false, granted: false });
    for (const [exit, stdout] of [[3, unreachable], [3, ''], [1, JSON.stringify({ plugins: [] })], [-1, ''], [0, 'not json'], [0, '{}'], [0, '{"plugins":{}}'], [0, 'null']] as const) {
        assert.equal(plugins(exit, stdout), null, `${exit} ${stdout}`);
    }
});

test('the accounts are every profile the Reply CLI holds and every profile a route to Reply names, one each: the default first, then the CLI’s own order, then the profiles only a route names; a route that names no profile is the profile the Reply CLI is set to at that read; and which of them a route names, which Sign out and Remove say', () => {
    // A route's profile is its binding's, else the one the Reply CLI is set to at this read, as Jason's Reply plugin runs it.
    const route = (stdout: string) => readRoutes({ exit: 0, stdout })!;
    const named = (name: string) => ({ type: 'reply', name });
    const cli = { current: 'outbound', available: ['default', 'outbound', 'quiet'] };
    const listing = (accounts: string[], fallback: string | null, routed: string[] | null, defaultFor: [string, unknown[]][] = [], followed: string | null = null) => ({
        accounts: accounts.map(named),
        default: fallback === null ? null : named(fallback),
        routed: routed === null ? null : new Set(routed),
        defaultFor: new Map(defaultFor),
        followed,
    });
    // Each global route to Reply but the default marks the account it names, one that names no profile the CLI's current one.
    assert.deepEqual(
        accountsOf(cli, route(routesEverywhere)),
        listing(['default', 'outbound', 'quiet', 'research', 'Made-up Ops'], null, ['research', 'outbound', 'Made-up Ops'], [
            ['research', [{ kind: 'operation', operation: 'contact.find' }]],
            ['outbound', [{ kind: 'operation', operation: 'sender.list' }, { kind: 'channel', operation: 'message.send', channel: 'email' }]],
        ], 'outbound'),
        'a global default to another plugin marks no Reply account; the routes that name no profile follow the current one',
    );
    assert.deepEqual(accountsOf(cli, route(routeList(replyRoute({ profile: 'research' })))), listing(['research', 'default', 'outbound', 'quiet'], 'research', ['research']), 'the default is the binding’s account, held by the CLI or not');
    assert.deepEqual(accountsOf(cli, route(routeList(replyRoute(null)))), listing(['outbound', 'default', 'quiet'], 'outbound', ['outbound'], [], 'outbound'), 'a default that names no profile is the profile the CLI is set to, which it follows');
    assert.deepEqual(accountsOf({ ...cli, current: 'quiet' }, route(routeList(replyRoute({})))), listing(['quiet', 'default', 'outbound'], 'quiet', ['quiet'], [], 'quiet'), 'and moves with it');
    const teamed = [{ campaign_id: 'cmp_9', default: replyRoute({ profile: 'quiet', team_id: '7' }), operations: {}, channels: {} }];
    assert.deepEqual(accountsOf(cli, route(routeList(null, teamed))), listing(['default', 'outbound', 'quiet'], null, ['quiet']), 'a team in a binding is no part of the account');
    assert.deepEqual(accountsOf(cli, null), listing(['default', 'outbound', 'quiet'], null, null, [], 'outbound'), 'routes not read: the CLI’s profiles, none marked, which a route names not known, and the current one taken as followed');
    assert.deepEqual(accountsOf({ current: 'default', available: ['default'] }, route(routeList(null))), listing(['default'], null, []), 'a fresh installation: one account, no default, no route');
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
    return existsSync(dataLayout(b.paths.data).userSettingsFile) ? leaves(readInstallationSettings(b)) : new Map();
}

/**
 * The folder the system keeps temporary files in, made this process's own for one test: the desktop writes the Reply skills
 * to a folder there, and a count of such folders in the shared one also counts another test process's, or another run's,
 * that is made or removed meanwhile. Put back, and the private one removed, when the test ends. Returns the shared one.
 */
function privateTemporaryFolder(t: { after(fn: () => void): void }): string {
    const shared = tmpdir();
    const own = mkdtempSync(join(shared, 'jason-own-temp-'));
    const names = ['TMPDIR', 'TMP', 'TEMP'];
    const kept = names.map((name) => process.env[name]);
    for (const name of names) process.env[name] = own;
    t.after(() => {
        names.forEach((name, at) => (kept[at] === undefined ? delete process.env[name] : (process.env[name] = kept[at])));
        rmSync(own, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
    });
    return shared;
}

const replyLeftovers = () => readdirSync(tmpdir()).filter((name) => name.startsWith('jason-reply-skills-')).length;

test('each act of the Connect cards and a chat turn writes under <data> what it names and nothing else, and nothing else under the bench\'s root', async (t) => {
    const shared = privateTemporaryFolder(t);
    const b = bench(t, { programs: ['reply', 'npm', 'claude'], env: { STUB_REPLY_PROFILES: JSON.stringify({ current: 'default', available: ['default', 'outbound'] }) } });
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

    // The desktop deploys Jason's skills for the chat through the CLI, into the folders Claude Code and Codex read them from.
    const jasonSkills = /^desktop\/(?:chat\/\.(?:claude|agents)\/skills\/[a-z-]+\/SKILL\.md|skills-records\/(?:claude|agents)\.json)$/;
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
    }), ['run/runtime.json', 'desktop/settings.json', jasonSkills], []);

    const setup = new Setup({ ...setupDeps(b, log, desktopSettings), confirm: () => Promise.resolve(true) });
    const replySkills = /^desktop\/(?:chat\/\.(?:claude|agents)\/skills\/reply-[a-z-]+\/SKILL\.md|skills-records\/reply\.json)$/;
    const replyKeys = ['Plugins:Grants:reply:Exec', 'Routes:Default'];
    const pluginKeys = (id: string) => [`Plugins:Config:${id}`, `Plugins:Grants:${id}:Http`, `Notifications:Plugins:${id}:Binding`];
    await act('status', () => setup.status(), [], []);
    // Make default, while no default is set: the grant where it is missing, and the default the runtime writes, through its
    // one writer of the file. The sign-ins after it find the default set.
    await act('makeDefault', () => setup.makeDefault({ type: 'reply', name: 'outbound' }), ['config/settings.json'], replyKeys);
    await act('connectReplyWithKey', () => setup.connectReplyWithKey('planted-reply-key'), ['config/settings.json', replySkills], replyKeys);
    await act('connectReply', () => setup.connectReply(), ['config/settings.json', replySkills], replyKeys);
    // Add Reply account, Sign out and Remove are the Reply CLI's to write, in its own files: none of them under <data>.
    await act('addAccount', () => setup.addAccount('Made-up Ops'), [], []);
    await act('signOut', () => setup.signOut({ type: 'reply', name: 'outbound' }), [], []);
    await act('removeAccount', () => setup.removeAccount({ type: 'reply', name: 'outbound' }), [], []);
    await act('setProviderKey', () => setup.setProviderKey('deepseek', 'sk-planted-provider'), ['desktop/provider-keys'], []);
    await act('installProgram', () => setup.installProgram('codex'), [], []);
    await act('configurePlugin slack', () => setup.configurePlugin('slack', { token: 'xoxb-planted', target: 'C0123ABCD' }), ['config/settings.json'], pluginKeys('slack'));
    await act('configurePlugin telegram', () => setup.configurePlugin('telegram', { token: 'planted-bot', target: '@team_room' }), ['config/settings.json'], pluginKeys('telegram'));
    await act('configurePlugin ntfy', () => setup.configurePlugin('ntfy', { target: 'jason-topic', server: 'https://ntfy.example.com' }), ['config/settings.json'], pluginKeys('ntfy'));
    await act('configurePlugin journal-forwarder', () => setup.configurePlugin('journal-forwarder', { token: 'b', target: 'https://hooks.example.com/in' }), ['config/settings.json'], pluginKeys('journal-forwarder'));
    await act('Reload', () => setup.configurePlugin('slack', {}), [], []);

    // A chat turn in Ask first writes two files in desktop/turns/ beside the chat folder, its MCP configuration and its
    // settings, which carry its hook, and removes them when the turn is over; the chat writes the instruction text into the
    // chat folder as the hosts' two memory files, places that text and the recorded skills in the conversation's own folder
    // under desktop/chats/, where the turn runs, and keeps the conversation's record in desktop/conversations/, written when
    // the host names its session and at the end. The file that says the records carry their shape is written by a start
    // that listed their folder, which this one, the folder's first, did not.
    const instructionFiles = ['desktop/chat/CLAUDE.md', 'desktop/chat/AGENTS.md'];
    const ownFolderFile = /^desktop\/chats\/conv_[0-9A-HJKMNP-TV-Z]{26}\/(?:CLAUDE\.md|AGENTS\.md|\.(?:claude|agents)\/skills\/[a-z-]+\/SKILL\.md)$/;
    const conversationFile = /^desktop\/conversations\/conv_[0-9A-HJKMNP-TV-Z]{26}\.json$/;
    const turnFile = /^desktop\/turns\/turn-[0-9a-f]{24}\.(?:mcp|settings)\.json$/;
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
            bundleVersion: null,
            cli: loggedCli(b.cli, log),
            programs: b.programs,
            keyFor: () => undefined,
            fence: () => readFence(join(import.meta.dirname, '..')),
            emit(_chatId, event: ChatEvent) {
                if (event.kind === 'permission') {
                    during = written(before, snapshot(b.paths.data)).filter((path) => !isLog(path));
                    chat.answer(conversation, event.request, { allow: true, always: false });
                }
                if (event.kind === 'done') finished();
            },
            ...loggedTurns(log),
        });
        const conversation = (await chat.create()).id;
        // A new chat is in Full access; this turn is Ask first's, which shows the command it asks about as a card.
        await chat.setMode(conversation, 'ask_first', 'claude');
        await chat.send(conversation, 'hello', { host: 'claude' });
        await done;
        // The set-up chat's naming run, which leaves nothing behind when the host gives no name.
        await chat.idle();
    }, [...instructionFiles, ownFolderFile, conversationFile], []);
    const turnFiles = during.filter((path) => !instructionFiles.includes(path) && !ownFolderFile.test(path) && !conversationFile.test(path));
    assert.equal([...snapshot(b.paths.data).keys()].filter((path) => conversationFile.test(path)).length, 1, 'one conversation kept');
    assert.equal(turnFiles.length, 2, `while the turn waited: ${during.join(', ')}`);
    for (const file of turnFiles) assert.match(file, turnFile);
    assert.ok(![...snapshot(b.paths.data).keys()].some((path) => turnFile.test(path)), 'the turn file is gone once the turn is over');

    // Jason's skills and the Reply ones sit in one folder per host, .claude/skills for Claude Code and .agents/skills for
    // Codex, and nothing of them at the chat folder's top, where the two memory files are.
    assert.deepEqual(readdirSync(b.paths.chat).sort(), ['.agents', '.claude', 'AGENTS.md', 'CLAUDE.md']);
    for (const host of ['.claude', '.agents']) {
        assert.deepEqual(readdirSync(join(b.paths.chat, host)), ['skills']);
        const skills = readdirSync(join(b.paths.chat, host, 'skills')).sort();
        assert.deepEqual(skills.filter((name) => !name.startsWith('reply-')), ['campaign-launch'], host);
        assert.ok(skills.some((name) => name.startsWith('reply-')), `the Reply skills beside Jason's in ${host}: ${skills.join(', ')}`);
    }
    // The conversation's own folder, where its turn ran, holds the same: the two memory files and both hosts' skills.
    const owned = readdirSync(b.paths.chats);
    assert.equal(owned.length, 1, `one conversation's folder: ${owned.join(', ')}`);
    const own = join(b.paths.chats, owned[0] ?? '');
    assert.deepEqual(readdirSync(own).sort(), ['.agents', '.claude', 'AGENTS.md', 'CLAUDE.md']);
    for (const host of ['.claude', '.agents']) {
        assert.deepEqual(readdirSync(join(own, host, 'skills')).sort(), readdirSync(join(b.paths.chat, host, 'skills')).sort(), host);
    }

    assert.deepEqual(readInstallationSettings(b), {
        Plugins: {
            Grants: {
                reply: { Exec: ['reply'] },
                slack: { Http: ['slack.com'] },
                telegram: { Http: ['api.telegram.org'] },
                ntfy: { Http: ['ntfy.example.com'] },
                'journal-forwarder': { Http: ['hooks.example.com'] },
            },
            Config: {
                slack: { token: 'xoxb-planted' },
                telegram: { token: 'planted-bot' },
                'journal-forwarder': { token: 'b', url: 'https://hooks.example.com/in' },
            },
        },
        Harness: { Root: 'kept' },
        Routes: { Default: { Plugin: 'reply', Binding: { profile: 'outbound' } } },
        Notifications: {
            Plugins: {
                slack: { Binding: { channel: 'C0123ABCD' } },
                telegram: { Binding: { chat: '@team_room' } },
                ntfy: { Binding: { topic: 'jason-topic', server: 'https://ntfy.example.com' } },
            },
        },
    });
    assert.equal(readFileSync(dataLayout(b.paths.data).userSettingsFile, 'utf8').includes('planted-reply-key'), false, 'the Reply key is the Reply CLI’s alone');
    assert.equal(readFileSync(b.paths.providerKeys, 'utf8').includes('sk-planted-provider'), false, 'the key is kept encrypted');
    assert.equal(readFileSync(b.paths.settings, 'utf8').includes('planted'), false);

    assert.deepEqual(outside(), outsideBefore, 'nothing is written beside <data>');
    // Another process's folder of the same kind, made in the shared one meanwhile, is not this test's to count.
    const other = mkdtempSync(join(shared, 'jason-reply-skills-'));
    t.after(() => rmSync(other, { recursive: true, force: true }));
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
    await act('Start the runtime at logon, off', () => autostart.disable(), []);

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
    // With the account given, as a start gives it: a new terminal's PATH without the folder, so the act writes the PATH
    // lines into the zsh account's two files, and the removal takes them back out of the same two.
    const withLines = () =>
        new PathCommand({ platform: 'darwin', folder: join(home, '.local', 'bin'), executable: '/J/jason', dataDirectory: null, path: () => '/usr/bin', isProgram: () => false, account: { home, shellNamed: '/bin/zsh', terminalPath: () => '/usr/bin:/bin' }, log });
    await act('The PATH command and its PATH lines, placed, macOS', async () => withLines().place(), ['.local/bin/jason', '.zprofile', '.zshrc']);
    await act('The PATH command and its PATH lines, removed, macOS', async () => withLines().remove(), ['.local/bin/jason', '.zprofile', '.zshrc']);

    assert.deepEqual(registered, [{ openAtLogin: true, args: [loginFlag] }, { openAtLogin: false, args: [loginFlag] }]);
    assert.deepEqual(b.record().map((run) => run.args), [['runtime', 'autostart', 'status'], ['runtime', 'autostart', 'disable']]);
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
    const settings: DesktopSettings = { feed: 'http://127.0.0.1:9/manifest.json', skillsDeployedFor: null, theme: null };
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
    assert.equal(readFileSync(dataLayout(b.paths.data).userSettingsFile, 'utf8'), '{"kept": true}');
    assert.match((await setup.status()).settingsRefused ?? '', /^the settings file could not be read \(not valid JSON at line 1, byte 2\)/);

    const fine = bench(t);
    const ok = setupOn(fine);
    await ok.configurePlugin('ntfy', {});
    assert.deepEqual(fine.record().filter((entry) => entry.name === 'jason').map((entry) => entry.args), [['plugin', 'reload'], ['plugin', 'list']]);
    assert.equal(existsSync(dataLayout(fine.paths.data).userSettingsFile), false);
    assert.equal((await ok.status()).settingsRefused, null);

    // A file there is left byte for byte, its time included, and nothing but the reload and its read-back is run.
    const kept = bench(t);
    writeInstallationSettings(kept, '{ "Dispatcher": { "MaxParallel": 2 } }');
    const mtime = statSync(dataLayout(kept.paths.data).userSettingsFile).mtimeMs;
    await setupOn(kept).configurePlugin('journal-forwarder', {});
    assert.equal(readFileSync(dataLayout(kept.paths.data).userSettingsFile, 'utf8'), '{ "Dispatcher": { "MaxParallel": 2 } }');
    assert.equal(statSync(dataLayout(kept.paths.data).userSettingsFile).mtimeMs, mtime);
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
    assert.equal(readFileSync(dataLayout(b.paths.data).userSettingsFile, 'utf8'), '{ "Plugins": {}, }');
    assert.deepEqual(b.record(), [], 'nothing is installed or reloaded');
    assert.deepEqual(log.lines.map((line) => line.name), ['settings_refused']);
    // The page adds the sentence that says what to do to what this says, so it is not said here as well.
    const refused = (await setupOn(b).status()).settingsRefused ?? '';
    assert.match(refused, /comments and trailing commas/);
    assert.equal(refused.includes('press Reload'), false);
});

/** A Setup that has read the accounts, as a click on a card always follows a read. */
async function readSetup(b: Bench, log: Logger = memoryLog(), settings?: DesktopSettings): Promise<Setup> {
    const setup = setupOn(b, log, settings);
    await setup.status();
    return setup;
}

/** The runs of one program after the first `from` the record held. */
const runsAfter = (b: Bench, from: number, name: 'reply' | 'jason') => b.record().slice(from).filter((entry) => entry.name === name).map((entry) => entry.args.join(' '));

test('a sign-in signs in the account its card names: its key on standard input, kept by the Reply CLI alone, or the browser’s, as that profile; a profile only a route names is made in the Reply CLI first and one it holds never is, and a name the last read did not list signs nobody in', async (t) => {
    // A key, on standard input and in no argument, kept under that account's profile and no other.
    const keyed = replyBench(t, { profiles: { outbound: {} }, keys: { 'planted-reply-key': { login: 'bea@example.test', team: 3 } } });
    const withKey = await readSetup(keyed);
    let from = keyed.record().length;
    await withKey.connectReplyWithKey('  planted-reply-key  ', { type: 'reply', name: 'outbound' });
    const login = keyed.record().slice(from).find((entry) => entry.name === 'reply' && entry.args.includes('login'));
    assert.deepEqual(login?.args, ['--profile', 'outbound', 'auth', 'login', '--with-token', '--json']);
    assert.equal(login?.stdin, 'planted-reply-key');
    assert.deepEqual(keyed.world().credentials, { outbound: { login: 'bea@example.test', key: 3 } }, 'that account, and no other, holds the sign-in');
    for (const entry of keyed.record()) assert.equal(JSON.stringify(entry.args).includes('planted-reply-key'), false);
    for (const host of ['.claude', '.agents']) {
        assert.equal(existsSync(join(keyed.paths.chat, host, 'skills', 'reply-cli', 'SKILL.md')), true, host);
        assert.equal(existsSync(join(keyed.paths.chat, host, 'skills', 'campaign-launch')), false, host);
    }
    await assert.rejects(withKey.connectReplyWithKey('   '), Refused);
    // The browser's, as the card's profile.
    const b = replyBench(t, { profiles: { outbound: {} }, browser: 'ada@example.test' }, { STUB_CAMPAIGN_ROUTES: JSON.stringify([campaignRoute('cmp_1', { profile: 'routed-only', team_id: '9' })]) });
    const setup = await readSetup(b);
    from = b.record().length;
    // No account named, as a page of an earlier version asks: the default account, here none, so the profile default.
    await setup.connectReply();
    assert.deepEqual(runsAfter(b, from, 'reply'), ['profile list --json', '--profile default auth status --json', '--profile default auth login --json', 'skills install adapter --project --agent codex']);
    // A Reply account signed in, as the Reply CLI says just before, is signed in nobody else: another login is another Reply
    // account, or a sign-out first. A page of an earlier version, which still offers the sign-in, is told so too.
    const signedInAlready = 'This Reply account is signed in already, so nobody was signed in: sign it out first, or add another Reply account.';
    from = b.record().length;
    await assert.rejects(setup.connectReply(), (error: unknown) => error instanceof Refused && error.message === signedInAlready);
    await assert.rejects(setup.connectReplyWithKey('planted-reply-key', { type: 'reply', name: 'default' }), (error: unknown) => error instanceof Refused && error.message === signedInAlready);
    assert.equal(runsAfter(b, from, 'reply').some((run) => run.includes('login')), false);
    assert.deepEqual(b.world().credentials, { default: 'ada@example.test' });
    from = b.record().length;
    // A route that names a team beside the profile is the profile's: the sign-in names the profile alone.
    await setup.connectReply({ type: 'reply', name: 'routed-only' });
    assert.deepEqual(runsAfter(b, from, 'reply'), ['profile list --json', 'profile add routed-only --json', '--profile routed-only auth login --json', 'skills install adapter --project --agent codex']);
    assert.deepEqual(b.world().credentials, { default: 'ada@example.test', 'routed-only': 'ada@example.test' });
    // Made meanwhile in a terminal, the profile is there by the time of the click: never added again, which would replace
    // its settings without a word.
    const again = replyBench(t, { profiles: { outbound: {} } }, { STUB_CAMPAIGN_ROUTES: JSON.stringify([campaignRoute('cmp_1', { profile: 'later' })]) });
    const late = await readSetup(again);
    const world = again.world();
    writeFileSync(again.env['STUB_REPLY_WORLD']!, JSON.stringify({ ...world, profiles: { ...world.profiles, later: { team_id: 9 } } }));
    from = again.record().length;
    await late.connectReply({ type: 'reply', name: 'later' });
    assert.equal(runsAfter(again, from, 'reply').some((run) => run.startsWith('profile add')), false);
    assert.deepEqual(again.world().profiles?.later, { team_id: 9 }, 'its settings are as they were');
    // An account the last read did not list, a value of another shape, or a profile list that could not be read signs
    // nobody in.
    for (const named of [{ type: 'reply', name: 'nobody' }, { type: 'reply', name: 'outbound', team: null }, { type: 'reply', name: 'outbound', team: '9' }, { type: 'other', name: 'outbound' }, 'outbound']) {
        from = b.record().length;
        await assert.rejects(setup.connectReply(named), (error: unknown) => error instanceof Refused && error.message === 'That is not a Reply account the desktop listed.', JSON.stringify(named));
        assert.equal(runsAfter(b, from, 'reply').some((run) => run.includes('login')), false, JSON.stringify(named));
    }
    const unread = replyBench(t, {});
    const reading = await readSetup(unread);
    writeFileSync(unread.env['STUB_REPLY_WORLD']!, '{');
    await assert.rejects(reading.connectReply(), (error: unknown) => error instanceof Refused && error.message === 'The Reply accounts could not be read, so nobody was signed in.');
    // No account named and none listed, as a page of an earlier version asks while this desktop lists none: why there is
    // none, the Reply CLI below the minimum or of a version not read, or accounts that could not be read; never that the
    // account is not one the desktop listed. Nobody is signed in.
    const minimum = belowMinimumSentence(replyMinimumVersion);
    for (const [env, sentence] of [
        [{ STUB_REPLY_VERSION: '0.23.0' }, minimum],
        [{ STUB_REPLY_VERSION_ENDS: JSON.stringify({ exit: 0, stdout: '' }) }, minimum],
        [{ STUB_REPLY_PROFILE_LIST: JSON.stringify({ code: 'config.corrupt' }) }, 'The Reply accounts could not be read, so nobody was signed in.'],
    ] as const) {
        const none = bench(t, { programs: ['reply'], env });
        const earlier = await readSetup(none);
        assert.deepEqual((await earlier.status()).accounts?.accounts, [], JSON.stringify(env));
        await assert.rejects(earlier.connectReply(), (error: unknown) => error instanceof Refused && error.message === sentence, JSON.stringify(env));
        await assert.rejects(earlier.connectReplyWithKey('made-up-key'), (error: unknown) => error instanceof Refused && error.message === sentence, JSON.stringify(env));
        assert.equal(none.record().some((entry) => entry.name === 'reply' && entry.args.includes('login')), false, JSON.stringify(env));
    }
});

test('a refused sign-in says the desktop’s sentence, then the Reply CLI’s reason: the detail, else the title, of the error it prints under --json', async (t) => {
    // Never its hint, which tells the person to sign in with no profile named, nor its other fields; an error that does not
    // read as that leaves the sentence alone.
    const setup = await readSetup(replyBench(t, { profiles: { outbound: {} } }));
    await assert.rejects(setup.connectReplyWithKey('made-up-unknown-key', { type: 'reply', name: 'outbound' }), (error: unknown) => error instanceof Refused && error.message === 'Reply did not accept that key. Authentication credentials are missing or invalid.');
    const timedOut = { code: 'oauth.timeout', title: 'Timed out waiting for the browser login.', hint: 'Re-run the login and complete it in the browser.' };
    for (const [stderr, said] of [
        [`Opening your browser to complete sign-in…\nIf it doesn't open, visit:\n  http://127.0.0.1:9/authorize\n${JSON.stringify({ error: timedOut })}\n`, 'Reply did not sign in. Timed out waiting for the browser login.'],
        [`${JSON.stringify({ error: { code: 'oauth.authorize' } })}\n`, 'Reply did not sign in.'],
        ['Timed out waiting for the browser login.\n  Hint: Re-run the login and complete it in the browser.\n', 'Reply did not sign in.'],
    ] as const) {
        const refusing = await readSetup(replyBench(t, { profiles: { outbound: {} } }, { STUB_REPLY_LOGIN: JSON.stringify({ exit: 1, stderr }) }));
        await assert.rejects(refusing.connectReply({ type: 'reply', name: 'outbound' }), (error: unknown) => error instanceof Refused && error.message === said, said);
    }
});

/** A refusal of an act on a Reply account, as the desktop answers it. */
const no = (refused: string, code: string | null = null, reread = false) => ({ refused, code, offer: null, reread });

test('Add Reply account makes the profile in the Reply CLI under exactly the name given and nothing of an account’s own, its card at once and not signed in; a name a route’s binding cannot hold, default, or one a fresh profile list holds is refused before the profile is made', async (t) => {
    const b = replyBench(t, { profiles: { outbound: { team_id: 9 } } });
    const setup = await readSetup(b);
    let from = b.record().length;
    assert.deepEqual(await setup.addAccount('Made-up Ops'), { done: true });
    assert.deepEqual(b.record().slice(from).filter((entry) => entry.name === 'reply').map((entry) => entry.args), [['profile', 'list', '--json'], ['profile', 'add', 'Made-up Ops', '--json']]);
    assert.deepEqual(b.world().profiles?.['Made-up Ops'], {});
    const made = (await setup.status()).accounts?.accounts.find((each) => each.name === 'Made-up Ops');
    assert.deepEqual({ held: made?.held, signedIn: made?.signedIn }, { held: true, signedIn: false });
    // Refused before anything runs: empty after trimming, past the 64 characters a route's binding holds, beginning with a
    // hyphen, holding a control character or a line break, or not text; and `default`, which every Reply CLI holds.
    const badName = 'Name the Reply account in one line of up to 64 characters that does not begin with a hyphen.';
    const taken = 'The Reply CLI holds a Reply account of that name already; choose another name.';
    from = b.record().length;
    for (const [name, said] of [['', badName], ['   ', badName], ['x'.repeat(65), badName], ['-p', badName], ['two\nlines', badName], ['a\ttab', badName], ['a\u0007bell', badName], ['a line', badName], [7, badName], [null, badName], ['default', taken]] as const) {
        assert.deepEqual(await setup.addAccount(name), no(said), JSON.stringify(name));
    }
    assert.deepEqual(runsAfter(b, from, 'reply'), []);
    // A name the Reply CLI, asked now, holds, made in a terminal meanwhile: never added again, which would replace that
    // profile's settings without a word; the page reads the accounts again to show it.
    writeFileSync(b.env['STUB_REPLY_WORLD']!, JSON.stringify({ ...b.world(), profiles: { ...b.world().profiles, later: { team_id: 5 } } }));
    from = b.record().length;
    assert.deepEqual(await setup.addAccount('later'), no(taken, null, true));
    assert.deepEqual(await setup.addAccount('outbound'), no(taken, null, true));
    assert.deepEqual(runsAfter(b, from, 'reply'), ['profile list --json', 'profile list --json']);
    assert.deepEqual([b.world().profiles?.['later'], b.world().profiles?.['outbound']], [{ team_id: 5 }, { team_id: 9 }]);
    assert.deepEqual(await setup.addAccount('x'.repeat(64)), { done: true });
    // A profile list that could not be read adds nothing; the Reply CLI's refusal is said with its reason, never its hint.
    const reading = replyBench(t, {}, { STUB_REPLY_PROFILE_LIST: JSON.stringify({ code: 'config.corrupt' }) });
    assert.deepEqual(await (await readSetup(reading)).addAccount('spare'), no('The Reply accounts could not be read, so none was added.', null, true));
    assert.equal(replyRuns(reading).some((run) => run.startsWith('profile add')), false);
    const refusing = replyBench(t, {}, { STUB_REPLY_PROFILE_ADD: JSON.stringify({ code: 'config.write', exit: 1, error: { title: 'Could not write the configuration.', hint: 'Check the folder.' } }) });
    assert.deepEqual(await (await readSetup(refusing)).addAccount('spare'), no('The Reply CLI did not make the profile. Could not write the configuration.', 'config.write', true));
});

test('Sign out asks first in the desktop’s own box, naming the Reply account, saying that the Reply CLI itself signs it out and, where a route names it, that campaigns sending through it stop; on a yes it signs out that profile alone and changes no route', async (t) => {
    const boxes: ReplyBox[] = [];
    let yes = false;
    const b = replyBench(t, {
        profiles: { default: { team_id: 3 }, outbound: { team_id: 9 }, spare: { team_id: 4 } },
        credentials: { default: 'ada@example.test', outbound: 'bea@example.test', spare: 'cy@example.test' },
        seats: { 'ada@example.test': [3], 'bea@example.test': [5, 9], 'cy@example.test': [4] },
    }, { STUB_CAMPAIGN_ROUTES: JSON.stringify([campaignRoute('cmp_1', { profile: 'outbound' })]) });
    writeInstallationSettings(b, { Routes: { Default: { Plugin: 'reply', Binding: { profile: 'default' } } } });
    const settingsBefore = readFileSync(dataLayout(b.paths.data).userSettingsFile, 'utf8');
    const setup = new Setup({ ...setupDeps(b), confirm: (box) => (boxes.push(box), Promise.resolve(yes)) });
    await setup.status();
    const outbound = { type: 'reply', name: 'outbound' } as const;
    let from = b.record().length;
    assert.deepEqual(await setup.signOut(outbound), no('Nothing was done.'));
    assert.equal(runsAfter(b, from, 'reply').some((run) => run.includes('logout')), false);
    const box = (name: string, detail: string) => ({ type: 'question', title: `Sign out of the Reply account “${name}”?`, message: `Sign out of the Reply account “${name}”?`, detail, buttons: ['Sign out', 'Cancel'], defaultId: 1, cancelId: 1, noLink: true });
    const itself = 'It is signed out in the Reply CLI itself, so reply in a terminal loses this sign-in too.';
    assert.deepEqual(boxes, [box('outbound', `${itself} Campaigns sending through it stop until it is signed in again.`)]);
    yes = true;
    from = b.record().length;
    assert.deepEqual(await setup.signOut(outbound), { done: true });
    assert.deepEqual(runsAfter(b, from, 'reply'), ['--profile outbound auth logout --json']);
    assert.deepEqual(b.world().credentials, { default: 'ada@example.test', spare: 'cy@example.test' });
    // The global default's account is named by a route too; one no route names is told nothing of campaigns.
    await setup.signOut({ type: 'reply', name: 'default' });
    await setup.signOut({ type: 'reply', name: 'spare' });
    assert.deepEqual(boxes.slice(-2), [box('default', `${itself} Campaigns sending through it stop until it is signed in again.`), box('spare', itself)]);
    assert.deepEqual(b.world().credentials, {});
    assert.equal(readFileSync(dataLayout(b.paths.data).userSettingsFile, 'utf8'), settingsBefore, 'no route changes');
    assert.deepEqual(b.world().profiles, { default: { team_id: 3 }, outbound: { team_id: 9 }, spare: { team_id: 4 } }, 'the profiles stay, and their teams');
    // An account the last read did not list is no act; the Reply CLI's refusal is said with its reason, and the accounts are
    // read again.
    assert.deepEqual(await setup.signOut({ type: 'reply', name: 'nobody' }), no('That is not a Reply account the desktop listed.'));
    const refusing = replyBench(t, { profiles: { outbound: { team_id: 9 } }, credentials: { outbound: 'bea@example.test' }, seats: { 'bea@example.test': [9] } }, { STUB_REPLY_AUTH_LOGOUT: JSON.stringify({ code: 'store.write', exit: 1, error: { title: 'Could not write the credential store.' } }) });
    const refused = new Setup({ ...setupDeps(refusing), confirm: () => Promise.resolve(true) });
    await refused.status();
    assert.deepEqual(await refused.signOut(outbound), no('The Reply CLI did not sign it out. Could not write the credential store.', 'store.write', true));
});

test('Remove, never for the Reply CLI’s own default nor for the account a route that names no profile follows, asks first in the desktop’s own box, naming the Reply account, saying that the Reply CLI itself deletes its profile and sign-in and, where a route names it, that campaigns sending through it stop and its card stays; on a yes it deletes that profile and changes no route', async (t) => {
    const boxes: ReplyBox[] = [];
    let yes = false;
    const b = replyBench(t, {
        current: 'outbound',
        profiles: { default: { team_id: 3 }, outbound: { team_id: 9 }, spare: { team_id: 4 } },
        credentials: { default: 'ada@example.test', outbound: 'bea@example.test', spare: 'cy@example.test' },
        seats: { 'ada@example.test': [3], 'bea@example.test': [5, 9], 'cy@example.test': [4] },
    }, { STUB_CAMPAIGN_ROUTES: JSON.stringify([campaignRoute('cmp_1', { profile: 'outbound' })]) });
    writeInstallationSettings(b, { Routes: { Default: { Plugin: 'reply', Binding: { profile: 'default' } } } });
    const settingsBefore = readFileSync(dataLayout(b.paths.data).userSettingsFile, 'utf8');
    const setup = new Setup({ ...setupDeps(b), confirm: (box) => (boxes.push(box), Promise.resolve(yes)) });
    await setup.status();
    const outbound = { type: 'reply', name: 'outbound' } as const;
    let from = b.record().length;
    assert.deepEqual(await setup.removeAccount(outbound), no('Nothing was done.'));
    assert.equal(runsAfter(b, from, 'reply').some((run) => run.includes('delete')), false);
    const box = (name: string, detail: string) => ({ type: 'warning', title: `Remove the Reply account “${name}”?`, message: `Remove the Reply account “${name}”?`, detail, buttons: ['Remove', 'Cancel'], defaultId: 1, cancelId: 1, noLink: true });
    const itself = 'Its profile and its sign-in are deleted in the Reply CLI itself, so reply in a terminal loses them too.';
    assert.deepEqual(boxes, [box('outbound', `${itself} Campaigns sending through it stop, and its card stays, not signed in.`)]);
    yes = true;
    from = b.record().length;
    assert.deepEqual(await setup.removeAccount(outbound), { done: true });
    assert.deepEqual(b.record().slice(from).filter((entry) => entry.name === 'reply').map((entry) => entry.args), [['profile', 'list', '--json'], ['profile', 'delete', 'outbound', '--yes', '--json']]);
    assert.deepEqual([b.world().profiles, b.world().credentials], [{ default: { team_id: 3 }, spare: { team_id: 4 } }, { default: 'ada@example.test', spare: 'cy@example.test' }]);
    // A route names it, so its card stays, not signed in; one no route names is told nothing of campaigns, and goes.
    await setup.removeAccount({ type: 'reply', name: 'spare' });
    assert.deepEqual(boxes.at(-1), box('spare', itself));
    const left = (await setup.status()).accounts?.accounts.map((each) => `${each.name} ${each.held ? 'held' : 'routed'} ${each.signedIn ? 'in' : 'out'}`);
    assert.deepEqual(left, ['default held in', 'outbound routed out']);
    // A Reply account only a route names has no profile to remove: refused, with no box and no run.
    const before = [boxes.length, b.record().length];
    assert.deepEqual(await setup.removeAccount(outbound), no('The Reply CLI holds no profile for this Reply account, so nothing was removed.'));
    assert.deepEqual([boxes.length, runsAfter(b, before[1]!, 'reply')], [before[0], []]);
    assert.equal(readFileSync(dataLayout(b.paths.data).userSettingsFile, 'utf8'), settingsBefore, 'no route changes');
    // The Reply CLI's own default is never removed: refused with no box and no run.
    const asked = boxes.length;
    from = b.record().length;
    assert.deepEqual(await setup.removeAccount({ type: 'reply', name: 'default' }), no('The Reply CLI’s own profile default is always there, so nothing was removed.'));
    assert.deepEqual([boxes.length, runsAfter(b, from, 'reply')], [asked, []]);
    // An account the last read did not list is no act; the Reply CLI's refusal is said with its reason, and the accounts are
    // read again.
    assert.deepEqual(await setup.removeAccount({ type: 'reply', name: 'nobody' }), no('That is not a Reply account the desktop listed.'));
    const refusing = replyBench(t, { profiles: { spare: {} } }, { STUB_REPLY_PROFILE_DELETE: JSON.stringify({ code: 'config.write', exit: 1, error: { title: 'Could not write the configuration.' } }) });
    const refused = new Setup({ ...setupDeps(refusing), confirm: () => Promise.resolve(true) });
    await refused.status();
    assert.deepEqual(await refused.removeAccount({ type: 'reply', name: 'spare' }), no('The Reply CLI did not remove the profile. Could not write the configuration.', 'config.write', true));
    // A route that names no profile follows the Reply CLI's current profile, and `reply profile delete` of that profile
    // resets the current one to `default`: such a route would then send as `default`'s login. That account is not removed,
    // before any box, whether the last read showed it so or the Reply CLI, asked again, says so now; its Sign out stays.
    const followedBy = 'A route of Jason’s follows this Reply account as the Reply CLI’s current profile, and removing it would move that route to another Reply account, so nothing was removed.';
    const following = replyBench(t, {
        current: 'outbound',
        profiles: { default: { team_id: 3 }, outbound: { team_id: 9 }, quiet: {} },
        credentials: { default: 'ada@example.test', outbound: 'bea@example.test' },
        seats: { 'ada@example.test': [3], 'bea@example.test': [5, 9] },
    });
    writeInstallationSettings(following, { Plugins: { Grants: { reply: { Exec: ['reply'] } } }, Routes: { Default: { Plugin: 'reply' } } });
    const followedBoxes: ReplyBox[] = [];
    const follows = new Setup({ ...setupDeps(following), confirm: (each) => (followedBoxes.push(each), Promise.resolve(true)) });
    const read = (await follows.status()).accounts?.accounts.map((each) => `${each.name} ${each.default ? 'default' : '-'} ${each.followed ? 'followed' : '-'}`);
    assert.deepEqual(read, ['outbound default followed', 'default - -', 'quiet - -']);
    from = following.record().length;
    assert.deepEqual(await follows.removeAccount(outbound), no(followedBy));
    // The last read showed `quiet` as no one's; a terminal's `reply profile use quiet` makes it the current profile meanwhile.
    writeFileSync(following.env['STUB_REPLY_WORLD']!, JSON.stringify({ ...following.world(), current: 'quiet' }));
    assert.deepEqual(await follows.removeAccount({ type: 'reply', name: 'quiet' }), no(followedBy, null, true));
    assert.deepEqual([followedBoxes, runsAfter(following, from, 'reply').filter((run) => run.startsWith('profile delete'))], [[], []]);
    assert.deepEqual(Object.keys(following.world().profiles ?? {}), ['default', 'outbound', 'quiet']);
    assert.equal(following.world().credentials?.['outbound'], 'bea@example.test');
    // Where the routes could not be read, nothing tells whether such a route follows the Reply CLI's current profile, so
    // that account is taken as followed: no Remove, as the last read showed it or as the Reply CLI says now, in a sentence
    // that says why; any other account is removed as before.
    const routesUnread = 'Jason’s routes could not be read, so Jason Desktop cannot tell whether one follows this Reply account as the Reply CLI’s current profile, and nothing was removed.';
    writeFileSync(following.env['STUB_REPLY_WORLD']!, JSON.stringify({ ...following.world(), current: 'outbound', profiles: { ...following.world().profiles, spare: {} } }));
    following.env['STUB_ROUTE_LIST'] = 'unreachable';
    const unread = (await follows.status()).accounts;
    assert.deepEqual([unread?.notRead, unread?.accounts.map((each) => `${each.name} ${each.followed ? 'followed' : '-'}`)], [['routes'], ['default -', 'outbound followed', 'quiet -', 'spare -']]);
    from = following.record().length;
    assert.deepEqual(await follows.removeAccount(outbound), no(routesUnread));
    writeFileSync(following.env['STUB_REPLY_WORLD']!, JSON.stringify({ ...following.world(), current: 'quiet' }));
    assert.deepEqual(await follows.removeAccount({ type: 'reply', name: 'quiet' }), no(routesUnread, null, true));
    assert.deepEqual([followedBoxes, runsAfter(following, from, 'reply').filter((run) => run.startsWith('profile delete'))], [[], []]);
    assert.deepEqual(await follows.removeAccount({ type: 'reply', name: 'spare' }), { done: true });
    assert.deepEqual([followedBoxes.length, Object.keys(following.world().profiles ?? {})], [1, ['default', 'outbound', 'quiet']]);
});

test('the desktop notes who each Reply account was last signed in as, and in which team, in a file of its own: at the look that finds it signed in and bound with nothing noted, at Choose and at Keep it; after a sign-in, a look that finds another login, a team left, another of its teams or a login it cannot know says that change, and until Keep it or Sign out nothing is done in the account and nothing asked of Reply for its senders; a sign-out keeps the note, a removal forgets it, and the note never decides which accounts there are', async (t) => {
    const log = memoryLog();
    const b = replyBench(t, {
        profiles: { default: { team_id: 3 }, outbound: { team_id: 9 }, spare: {} },
        credentials: { default: 'ada@example.test', outbound: 'bea@example.test' },
        seats: { 'ada@example.test': [3, 6], 'bea@example.test': [5, 9], 'cy@example.test': [4] },
        teams: { '3': 'Made-up Three', '4': 'Made-up Four', '6': 'Made-up Six', '9': 'Made-up Nine' },
        lists: { 'cy@example.test#4': { email: [mailbox] }, 'ada@example.test#6': { email: [mailbox] } },
    });
    const setup = new Setup({ ...setupDeps(b, log), confirm: () => Promise.resolve(true) });
    const file = b.paths.replyAccountNotes;
    assert.equal(file, join(b.paths.data, 'desktop', 'reply-account-notes.json'));
    const noted = () => JSON.parse(readFileSync(file, 'utf8')) as unknown;
    const seen = async () =>
        Object.fromEntries(((await setup.status()).accounts?.accounts ?? []).map((each) => [each.name, { last: each.lastSignIn, change: each.change }]));
    const ada3 = { user: 'ada@example.test', team: { id: '3', name: 'Made-up Three' } };
    const bea9 = { user: 'bea@example.test', team: { id: '9', name: 'Made-up Nine' } };
    const cy4 = { user: 'cy@example.test', team: { id: '4', name: 'Made-up Four' } };
    const world = (change: (current: ReplyWorld) => ReplyWorld) => writeFileSync(b.env['STUB_REPLY_WORLD']!, JSON.stringify(change(b.world())));
    // The first look notes each account signed in and bound; one signed out, or never signed in, has nothing noted.
    assert.deepEqual(await seen(), { default: { last: ada3, change: null }, outbound: { last: bea9, change: null }, spare: { last: null, change: null } });
    assert.deepEqual(noted(), { accounts: [{ type: 'reply', name: 'default', ...ada3 }, { type: 'reply', name: 'outbound', ...bea9 }] });
    assert.deepEqual(log.lines.filter((line) => line.name === 'reply_account_noted').map((line) => line.fields), [{ Moment: 'look', Written: true }, { Moment: 'look', Written: true }]);
    // Signed out in a terminal: who it was last signed in as stays.
    world((current) => ({ ...current, credentials: { default: 'ada@example.test' } }));
    assert.deepEqual((await seen())['outbound'], { last: bea9, change: null });
    // Signed in again in a terminal as another login, in its own team: the note is the old one, and every look says the
    // change. The old team is not the new login's: the account shows no team and lists nothing until Keep it. Another of
    // the same login's teams pinned is a change too, the account listed as it now is.
    world((current) => ({ ...current, profiles: { ...current.profiles, outbound: { team_id: 4 }, default: { team_id: 6 } }, credentials: { ...current.credentials, outbound: 'cy@example.test' } }));
    for (const look of ['the first look', 'the next look']) {
        const now = await seen();
        assert.deepEqual([now['outbound'], now['default']], [{ last: bea9, change: 'login' }, { last: ada3, change: 'team' }], look);
    }
    const shown = async (name: string) => {
        const each = (await setup.status()).accounts?.accounts.find((one) => one.name === name);
        return { user: each?.user, team: each?.team, ids: each?.senders.map((sender) => sender.id) };
    };
    assert.deepEqual(await shown('outbound'), { user: 'cy@example.test', team: null, ids: [] });
    assert.deepEqual(await shown('default'), { user: 'ada@example.test', team: { id: '6', name: 'Made-up Six' }, ids: ['12'] });
    // Until the change is answered nothing is done in the account, in the desktop's own sentence: no connect, no act on a
    // sender, even one it lists, and no choice of a team; nothing runs.
    const outbound = { type: 'reply', name: 'outbound' } as const;
    const ada = { type: 'reply', name: 'default' } as const;
    const unanswered = no('The sign-in of this Reply account changed: keep it or sign it out on its card first, so nothing was changed.');
    let from = b.record().length;
    for (const [what, act] of [
        ['a connect, another login', () => setup.replyConnect('email', { provider: 'gmail' }, outbound)],
        ['a connect, another team', () => setup.replyConnect('linkedin', { name: 'Made-up Ops' }, ada)],
        ['a sender’s act on a sender it lists', () => setup.replyLimits(String(mailbox.id), undefined, ada)],
        ['a sender’s delete', () => setup.replyDelete('email', String(mailbox.id), {}, ada)],
        ['Choose', () => setup.chooseTeam(ada, '3')],
    ] as const) assert.deepEqual(await act(), unanswered, what);
    assert.deepEqual(runsAfter(b, from, 'reply').filter((run) => !run.startsWith('--version')), [], 'nothing runs');
    // Keep it on another login picks its team afresh: the next look binds it as a first sign-in is, here to its one team,
    // and notes it. An account that shows no change has nothing to keep.
    assert.deepEqual(await setup.keepChange(outbound), { done: true });
    assert.deepEqual((await seen())['outbound'], { last: cy4, change: null });
    assert.deepEqual(await shown('outbound'), { ...cy4, ids: ['12'] });
    assert.deepEqual(await setup.keepChange(outbound), no('That Reply account shows no change to keep.'));
    assert.deepEqual(await setup.keepChange({ type: 'reply', name: 'nobody' }), no('That is not a Reply account the desktop listed.'));
    // Sign out keeps the note, so the card says who it was; Remove forgets it.
    await setup.signOut(outbound);
    assert.deepEqual((await seen())['outbound'], { last: cy4, change: null });
    await setup.removeAccount(outbound);
    assert.deepEqual(noted(), { accounts: [{ type: 'reply', name: 'default', ...ada3 }] });
    assert.deepEqual(Object.keys(await seen()), ['default', 'spare']);
    // The note never decides which accounts there are: one it names that the Reply CLI holds not, and no route names, is
    // no account. A note that cannot be read is no note: the next look finds nothing noted, and notes again.
    writeFileSync(file, JSON.stringify({ accounts: [{ type: 'reply', name: 'ghost', ...cy4 }, { type: 'reply', name: 'default', ...ada3 }] }));
    assert.deepEqual(Object.keys(await seen()), ['default', 'spare']);
    writeFileSync(file, '{');
    assert.deepEqual((await seen())['default'], { last: { user: 'ada@example.test', team: { id: '6', name: 'Made-up Six' } }, change: null });
    assert.deepEqual(b.world().profiles, { default: { team_id: 6 }, spare: {} }, 'the note is never written into the Reply CLI’s files');
    assert.equal(existsSync(dataLayout(b.paths.data).userSettingsFile), false, 'nor into the installation’s settings');
    // Two looks overlap, both having found nothing noted: the one that ends last answers the note the other wrote.
    const overlapping = replyBench(t, { profiles: { default: { team_id: 3 } }, credentials: { default: 'ada@example.test' }, seats: { 'ada@example.test': [3] } });
    let release: () => void = () => undefined;
    let holding = false;
    const slow: Programs = {
        find: (name) => overlapping.programs.find(name),
        run: async (name, args, options) => {
            const result = await overlapping.programs.run(name, args, options);
            if (holding && name === 'reply' && args.join(' ') === '--profile default auth status --json') {
                holding = false;
                await new Promise<void>((resolve) => (release = resolve));
            }
            return result;
        },
    };
    const looks = new Setup({ ...setupDeps(overlapping), programs: slow });
    holding = true;
    const older = looks.status();
    await new Promise((resolve) => setTimeout(resolve, 500));
    looks.announceChange();
    const newer = (await looks.status()).accounts?.accounts[0];
    release();
    const late = (await older).accounts?.accounts[0];
    const adaNoName = { user: 'ada@example.test', team: { id: '3', name: '' } };
    assert.deepEqual([newer?.lastSignIn, late?.lastSignIn, late?.change], [adaNoName, adaNoName, null]);
    // A sign-in the Reply CLI holds no login for, a browser's of a login in several teams on a profile that pins none, cannot
    // be named before it has a team: on an account noted as another sign-in, that is the change said, with no team and
    // nothing listed, and no pin is written before it is known who signed in. Keep it picks its team afresh, by Choose,
    // which names it and notes it at once, so the next look says nothing; a team then pinned in a terminal is a change.
    const unnamed = replyBench(t, {
        profiles: { web: {} },
        seats: { 'bea@example.test': [5], 'cy@example.test': [4, 5] },
        teams: { '4': 'Made-up Four', '5': 'Made-up Five' },
        browser: 'cy@example.test',
    });
    writeInstallationSettings(unnamed, { Plugins: { Grants: { reply: { Exec: ['reply'] } } }, Routes: { Default: { Plugin: 'reply', Binding: { profile: 'web' } } } });
    mkdirSync(dirname(unnamed.paths.replyAccountNotes), { recursive: true });
    const bea5 = { user: 'bea@example.test', team: { id: '5', name: 'Made-up Five' } };
    const cy5 = { user: 'cy@example.test', team: { id: '5', name: 'Made-up Five' } };
    writeFileSync(unnamed.paths.replyAccountNotes, JSON.stringify({ accounts: [{ type: 'reply', name: 'web', ...bea5 }] }));
    const web = { type: 'reply', name: 'web' } as const;
    const webSetup = new Setup({ ...setupDeps(unnamed), confirm: () => Promise.resolve(true) });
    const webSeen = async () => {
        const each = (await webSetup.status()).accounts?.accounts.find((one) => one.name === 'web');
        return { user: each?.user, team: each?.team, last: each?.lastSignIn, change: each?.change, teams: each?.teams?.map((team) => team.id) };
    };
    await webSetup.status();
    await webSetup.connectReply(web);
    assert.deepEqual(await webSeen(), { user: null, team: null, last: bea5, change: 'unknown', teams: undefined }, 'who it is is not known before it has a team');
    assert.deepEqual(unnamed.world().profiles?.['web'], {}, 'no pin is written before it is known who signed in');
    assert.deepEqual(await webSetup.keepChange(web), { done: true });
    assert.deepEqual(await webSeen(), { user: null, team: null, last: null, change: null, teams: ['4', '5'] });
    assert.deepEqual(await webSetup.chooseTeam(web, '5'), { done: true });
    assert.deepEqual(JSON.parse(readFileSync(unnamed.paths.replyAccountNotes, 'utf8')), { accounts: [{ type: 'reply', name: 'web', ...cy5 }] }, 'the pick is noted at once');
    assert.deepEqual(await webSeen(), { ...cy5, last: cy5, change: null, teams: undefined });
    writeFileSync(unnamed.env['STUB_REPLY_WORLD']!, JSON.stringify({ ...unnamed.world(), profiles: { web: { team_id: 4 } } }));
    assert.deepEqual(await webSeen(), { user: 'cy@example.test', team: { id: '4', name: 'Made-up Four' }, last: cy5, change: 'team', teams: undefined });

    // The binding is one login's, checked after a sign-in: one made on the card, by a key or a browser, or one a read gives
    // away. `web` is noted as bea in team 5, which its profile pins. Each case is the card as each look shows it,
    // `login team [listed] change last <noted> pin <its pin>`, with `unlisted` where the look asked Reply for none of its
    // mailboxes and LinkedIn accounts and `unread` where they could not be read: after the act, again while a change is
    // unanswered, then after Keep it, and after Choose where Keep it offered teams. A key acts in its own team (the stand-in's
    // `keys`), whatever is pinned.
    const bea = 'bea@example.test';
    const cy = 'cy@example.test';
    const dee = 'dee@example.test';
    const room: ReplyWorld = {
        profiles: { web: { team_id: 5 } },
        seats: { [bea]: [5], [cy]: [4, 5], [dee]: [7] },
        teams: { '4': 'Made-up Four', '5': 'Made-up Five', '6': 'Made-up Six', '7': 'Made-up Seven', '8': 'Made-up Eight' },
        keys: { 'k-bea': { login: bea, team: 5 }, 'k-bea8': { login: bea, team: 8 }, 'k-cy': { login: cy, team: 4 }, 'k-dee': { login: dee, team: 7 } },
        lists: { [`${bea}#5`]: { email: [mailbox] }, [`${cy}#5`]: { email: [mailbox] } },
    };
    const checked = async (world: ReplyWorld, noted: string | null, act: (setup: Setup, change: (to: ReplyWorld) => void) => Promise<unknown>, pick: string | null, env: Record<string, string> = {}) => {
        const each = replyBench(t, world, env);
        writeInstallationSettings(each, { Plugins: { Grants: { reply: { Exec: ['reply'] } } }, Routes: { Default: { Plugin: 'reply', Binding: { profile: 'web' } } } });
        if (noted !== null) {
            mkdirSync(dirname(each.paths.replyAccountNotes), { recursive: true });
            writeFileSync(each.paths.replyAccountNotes, JSON.stringify({ accounts: [{ type: 'reply', name: 'web', user: noted, team: { id: '5', name: 'Made-up Five' } }] }));
        }
        const setup = new Setup({ ...setupDeps(each), confirm: () => Promise.resolve(true) });
        await setup.status();
        await act(setup, (to) => writeFileSync(each.env['STUB_REPLY_WORLD']!, JSON.stringify(to)));
        const view = async () => {
            const from = each.record().length;
            const card = (await setup.status()).accounts?.accounts.find((one) => one.name === 'web');
            const listed = runsAfter(each, from, 'reply').some((run) => run.startsWith('--profile web account list'));
            const last = card?.lastSignIn ? `${card.lastSignIn.user} ${card.lastSignIn.team.id}` : '-';
            const choice = card?.teams === undefined ? '' : ` choose ${card.teams.map((team) => team.id).join()}`;
            const read = `${listed ? '' : ' unlisted'}${card?.unread.length ? ' unread' : ''}`;
            return `${card?.user ?? '?'} ${card?.team?.id ?? '-'} [${card?.senders.map((sender) => sender.id).join()}]${read} ${card?.change ?? 'same'}${choice} last ${last} pin ${each.world().profiles?.['web']?.team_id ?? '-'}`;
        };
        const views = [await view()];
        if (views[0]!.includes(' same ')) return views;
        views.push(await view());
        assert.deepEqual(await setup.keepChange({ type: 'reply', name: 'web' }), { done: true });
        views.push(await view());
        if (pick !== null) assert.deepEqual(await setup.chooseTeam({ type: 'reply', name: 'web' }, pick), { done: true });
        views.push(await view());
        return views;
    };
    const byKey = (key: string) => (setup: Setup) => setup.connectReplyWithKey(key, { type: 'reply', name: 'web' });
    const byBrowser = (setup: Setup) => setup.connectReply({ type: 'reply', name: 'web' });
    const pinned = (to: ReplyWorld) => async (_: Setup, change: (to: ReplyWorld) => void) => change(to);
    const none = async () => undefined;
    const cases: [string, ReplyWorld, string | null, (setup: Setup, change: (to: ReplyWorld) => void) => Promise<unknown>, string | null, string[], Record<string, string>?][] = [
        ['the same login, still in the noted team, by a key', room, bea, byKey('k-bea'), null, [`${bea} 5 [12] same last ${bea} 5 pin 5`]],
        ['the same login, still in the noted team, by a browser', { ...room, browser: bea }, bea, byBrowser, null, [`${bea} 5 [12] same last ${bea} 5 pin 5`]],
        // Keep it picks afresh: the old pin does not stand, even where the new login is in it.
        ['another login, by a key', room, bea, byKey('k-cy'), null, [`${cy} - [] unlisted login last ${bea} 5 pin 5`, `${cy} - [] unlisted login last ${bea} 5 pin 5`, `${cy} 4 [] same last ${cy} 4 pin 4`, `${cy} 4 [] same last ${cy} 4 pin 4`]],
        ['another login in several teams, the old pin among them, by a browser', { ...room, browser: cy }, bea, byBrowser, '5', [`${cy} - [] unlisted login last ${bea} 5 pin 5`, `${cy} - [] unlisted login last ${bea} 5 pin 5`, `${cy} - [] unlisted unread same choose 4,5 last - pin 5`, `${cy} 5 [12] same last ${cy} 5 pin 5`]],
        ['another login not in the pinned team, by a key', room, bea, byKey('k-dee'), null, [`${dee} - [] unlisted login last ${bea} 5 pin 5`, `${dee} - [] unlisted login last ${bea} 5 pin 5`, `${dee} 7 [] same last ${dee} 7 pin 7`, `${dee} 7 [] same last ${dee} 7 pin 7`]],
        // Reply does not name it, in a team it is not in: who it is is not known until it has a team.
        ['another login not in the pinned team, by a browser', { ...room, browser: dee }, bea, byBrowser, null, [`? - [] unlisted unknown last ${bea} 5 pin 5`, `? - [] unlisted unknown last ${bea} 5 pin 5`, `${dee} 7 [] same last ${dee} 7 pin 7`, `${dee} 7 [] same last ${dee} 7 pin 7`]],
        ['the same login, no longer in the noted team, by a key', { ...room, seats: { ...room.seats, [bea]: [8] } }, bea, byKey('k-bea8'), null, [`${bea} - [] unlisted left last ${bea} 5 pin 5`, `${bea} - [] unlisted left last ${bea} 5 pin 5`, `${bea} 8 [] same last ${bea} 8 pin 8`, `${bea} 8 [] same last ${bea} 8 pin 8`]],
        ['the same login, no longer in the noted team, by a browser', { ...room, seats: { ...room.seats, [bea]: [8] }, browser: bea }, bea, byBrowser, null, [`? - [] unlisted unknown last ${bea} 5 pin 5`, `? - [] unlisted unknown last ${bea} 5 pin 5`, `${bea} 8 [] same last ${bea} 8 pin 8`, `${bea} 8 [] same last ${bea} 8 pin 8`]],
        // A key acts in its own team: another of its login's teams pinned in a terminal is not its team, and is put back.
        ['the same login by a key, another of its login’s teams pinned in a terminal', { ...room, seats: { ...room.seats, [bea]: [5, 6] }, credentials: { web: { login: bea, key: 5 } } }, bea, pinned({ ...room, profiles: { web: { team_id: 6 } }, seats: { ...room.seats, [bea]: [5, 6] }, credentials: { web: { login: bea, key: 5 } } }), null, [`${bea} 5 [12] same last ${bea} 5 pin 5`]],
        ['the same login in another of its teams, pinned in a terminal, signed in by a browser', { ...room, seats: { ...room.seats, [bea]: [5, 6] }, credentials: { web: bea } }, bea, pinned({ ...room, profiles: { web: { team_id: 6 } }, seats: { ...room.seats, [bea]: [5, 6] }, credentials: { web: bea } }), null, [`${bea} 6 [] team last ${bea} 5 pin 6`, `${bea} 6 [] team last ${bea} 5 pin 6`, `${bea} 6 [] same last ${bea} 6 pin 6`, `${bea} 6 [] same last ${bea} 6 pin 6`]],
        ['the same login in another of its teams, pinned in a terminal, a browser’s sign-in Reply did not name', { ...room, seats: { ...room.seats, [bea]: [5, 6] }, credentials: { web: { login: bea, unnamed: true } } }, bea, pinned({ ...room, profiles: { web: { team_id: 6 } }, seats: { ...room.seats, [bea]: [5, 6] }, credentials: { web: { login: bea, unnamed: true } } }), null, [`${bea} 6 [] team last ${bea} 5 pin 6`, `${bea} 6 [] team last ${bea} 5 pin 6`, `${bea} 6 [] same last ${bea} 6 pin 6`, `${bea} 6 [] same last ${bea} 6 pin 6`]],
        // The same login, still in the noted team, its pin cleared or set to a team it is not in: put back, nothing said.
        ['the same login, its pin cleared in a terminal', { ...room, seats: { ...room.seats, [bea]: [5, 6] }, credentials: { web: bea } }, bea, pinned({ ...room, profiles: { web: {} }, seats: { ...room.seats, [bea]: [5, 6] }, credentials: { web: bea } }), null, [`${bea} 5 [12] same last ${bea} 5 pin 5`]],
        ['the same login, pinned in a terminal to a team it is not in', { ...room, seats: { ...room.seats, [bea]: [5, 6] }, credentials: { web: bea } }, bea, pinned({ ...room, profiles: { web: { team_id: 7 } }, seats: { ...room.seats, [bea]: [5, 6] }, credentials: { web: bea } }), null, [`${bea} 5 [12] same last ${bea} 5 pin 5`]],
        ['a pin of a team the sign-in is not in, nothing noted, by a key', room, null, byKey('k-dee'), null, [`${dee} 7 [] same last ${dee} 7 pin 7`]],
        ['a pin of a team the sign-in is not in, nothing noted, by a browser', { ...room, browser: dee }, null, byBrowser, null, [`${dee} 7 [] same last ${dee} 7 pin 7`]],
        // A key of a login in two teams is never offered them: it is bound to its own.
        ['a key of a login in two teams, nothing noted, nothing pinned', { ...room, profiles: { web: {} }, seats: { ...room.seats, [bea]: [5, 6] } }, null, byKey('k-bea'), null, [`${bea} 5 [12] same last ${bea} 5 pin 5`]],
        // A sign-in the Reply CLI does not name, another login whose one team is the noted one, under a pin of the noted
        // login's other team: it cannot be named before it has a team, so it is not taken as the noted login, and the pin
        // stays until Keep it.
        ['no sign-in on the card, a pin of another team and a login the Reply CLI does not name', { ...room, profiles: { web: { team_id: 6 } }, seats: { ...room.seats, [bea]: [5, 6], [cy]: [5] }, credentials: { web: { login: cy, unnamed: true } } }, bea, none, null, [`? - [] unlisted unknown last ${bea} 5 pin 6`, `? - [] unlisted unknown last ${bea} 5 pin 6`, `${cy} 5 [12] same last ${cy} 5 pin 5`, `${cy} 5 [12] same last ${cy} 5 pin 5`]],
        // No sign-in: the same login, which lost the noted team. Only Reply refusing the account's own read in its pinned
        // team gives it away, for a sign-in the Reply CLI names and for one it does not, at the look before these.
        ['no sign-in, the same login refused in the noted team, named', { ...room, seats: { ...room.seats, [bea]: [8] }, credentials: { web: bea } }, bea, none, null, [`${bea} - [] unlisted left last ${bea} 5 pin 5`, `${bea} - [] unlisted left last ${bea} 5 pin 5`, `${bea} 8 [] same last ${bea} 8 pin 8`, `${bea} 8 [] same last ${bea} 8 pin 8`]],
        ['no sign-in, the same login refused in the noted team, unnamed', { ...room, seats: { ...room.seats, [bea]: [8] }, credentials: { web: { login: bea, unnamed: true } } }, bea, none, null, [`? - [] unlisted unknown last ${bea} 5 pin 5`, `? - [] unlisted unknown last ${bea} 5 pin 5`, `${bea} 8 [] same last ${bea} 8 pin 8`, `${bea} 8 [] same last ${bea} 8 pin 8`]],
        // A read that failed otherwise, for the network, gives nothing away: the noted login in the noted team, nothing rebound.
        ['no sign-in, a read that failed for the network', { ...room, credentials: { web: { login: bea, unnamed: true } } }, bea, none, null, [`${bea} 5 [] unread same last ${bea} 5 pin 5`], { STUB_REPLY_ACCOUNT_LIST: JSON.stringify({ code: 'reply.unavailable' }) }],
    ];
    for (const [name, world, noted, act, pick, expected, env] of cases) assert.deepEqual(await checked(world, noted, act, pick, env), expected, name);
    // Reply refusing the read in the noted team while the teams cannot be read decides nothing: the noted login in its pin,
    // the refused reads said as not read, and made once.
    const refusedUnread = replyBench(t, { ...room, seats: { ...room.seats, [bea]: [8] }, credentials: { web: bea } }, { STUB_REPLY_TEAM_LIST: JSON.stringify({ code: 'teams.unavailable' }) });
    mkdirSync(dirname(refusedUnread.paths.replyAccountNotes), { recursive: true });
    writeFileSync(refusedUnread.paths.replyAccountNotes, JSON.stringify({ accounts: [{ type: 'reply', name: 'web', ...bea5 }] }));
    const refusedCard = (await setupOn(refusedUnread).status()).accounts?.accounts.find((one) => one.name === 'web');
    assert.deepEqual(
        { team: refusedCard?.team, change: refusedCard?.change, unread: refusedCard?.unread, notRead: refusedCard?.notRead, lists: replyRuns(refusedUnread).filter((run) => run.startsWith('--profile web account list')).length },
        { team: bea5.team, change: null, unread: ['email', 'linkedin'], notRead: ['teams'], lists: 2 },
    );
    // A look that began before a sign-in on the card does not count as its check: the next look still checks it.
    const racing = replyBench(t, { ...room, credentials: { web: bea } });
    writeInstallationSettings(racing, { Plugins: { Grants: { reply: { Exec: ['reply'] } } }, Routes: { Default: { Plugin: 'reply', Binding: { profile: 'web' } } } });
    let held = false;
    let resume: () => void = () => undefined;
    const holdingTeams: Programs = {
        find: (name) => racing.programs.find(name),
        run: async (name, args, options) => {
            if (held && name === 'reply' && args.join(' ') === '--profile web team list --json') {
                held = false;
                await new Promise<void>((resolve) => (resume = resolve));
            }
            return racing.programs.run(name, args, options);
        },
    };
    const raced = new Setup({ ...setupDeps(racing), programs: holdingTeams, confirm: () => Promise.resolve(true) });
    await raced.status();
    await raced.signOut(web);
    await raced.connectReplyWithKey('k-bea', web);
    held = true;
    const before = raced.status();
    while (held) await new Promise((resolve) => setTimeout(resolve, 10));
    await raced.signOut(web);
    await raced.connectReplyWithKey('k-bea', web);
    resume();
    await before;
    from = racing.record().length;
    await raced.status();
    assert.deepEqual(runsAfter(racing, from, 'reply').filter((run) => run.startsWith('--profile web team list')), ['--profile web team list --json'], 'the sign-in made while the look was going is checked');
});

test('one act on a Reply account at a time, whatever it is: while Make default or a sign-in places the plugin, every other act on a Reply account, a sign-in, Make default and Keep it among them, is refused as busy and runs nothing', async (t) => {
    // `default` noted in another of its login's teams, so it shows a change to keep; `several` offered its two teams.
    const b = replyBench(t, {
        profiles: { default: { team_id: 3 }, outbound: { team_id: 9 }, several: {}, spare: {} },
        credentials: { default: 'ada@example.test', outbound: 'bea@example.test', several: 'bea@example.test' },
        seats: { 'ada@example.test': [3, 6], 'bea@example.test': [5, 9] },
        lists: { 'ada@example.test#3': { email: [mailbox] }, 'bea@example.test#9': { email: [mailbox] } },
        keys: { 'planted-reply-key': { login: 'cy@example.test', team: 3 } },
        browser: 'cy@example.test',
    }, { STUB_REPLY_PLUGIN: 'absent' });
    writeInstallationSettings(b, {});
    mkdirSync(dirname(b.paths.replyAccountNotes), { recursive: true });
    writeFileSync(b.paths.replyAccountNotes, JSON.stringify({ accounts: [{ type: 'reply', name: 'default', user: 'ada@example.test', team: { id: '6', name: '' } }] }));
    let release: () => void = () => undefined;
    let fetching: () => void = () => undefined;
    const fetched = () => new Promise<void>((resolve) => (fetching = resolve));
    const held: Cli = {
        run: async (args, options) => {
            if (args[0] === 'plugin' && args[1] === 'install') {
                fetching();
                await new Promise<void>((resolve) => (release = resolve));
            }
            return b.cli.run(args, options);
        },
    };
    const boxes: ReplyBox[] = [];
    const setup = new Setup({ ...setupDeps(b), cli: held, confirm: (box) => (boxes.push(box), Promise.resolve(true)) });
    await setup.status();
    const busy = 'Another act on a Reply account is going; wait for it.';
    const outbound = { type: 'reply', name: 'outbound' } as const;
    const spare = { type: 'reply', name: 'spare' } as const;
    // Each ask is answered at once, or said as still waiting after five seconds: one queued behind the held act.
    const answered = (asked: Promise<unknown>) => Promise.race([asked, new Promise((resolve) => setTimeout(() => resolve('still waiting'), 5000))]);
    const others = async () => {
        const answers: unknown[] = [];
        for (const act of [
            () => setup.removeAccount(outbound),
            () => setup.signOut(outbound),
            () => setup.addAccount('Made-up Ops'),
            () => setup.keepChange({ type: 'reply', name: 'default' }),
            () => setup.chooseTeam({ type: 'reply', name: 'several' }, '5'),
            () => setup.makeDefault(spare),
            () => setup.replyLimits(String(mailbox.id), undefined, outbound),
        ])
            answers.push(await answered(act()));
        for (const signIn of [() => setup.connectReply(spare), () => setup.connectReplyWithKey('planted-reply-key', spare)]) {
            answers.push(await answered(signIn().then(() => 'signed in', (error: unknown) => (error instanceof Refused ? error.message : String(error)))));
        }
        return answers;
    };
    const refusedAll = [...Array.from({ length: 7 }, () => no(busy)), busy, busy];
    // Make default held at the plugin's fetch.
    let from = b.record().length;
    let fetch = fetched();
    const making = setup.makeDefault(spare);
    await fetch;
    assert.deepEqual(await others(), refusedAll);
    assert.deepEqual([boxes, runsAfter(b, from, 'reply').filter((run) => !run.startsWith('--version'))], [[], []]);
    release();
    assert.deepEqual(await making, { done: true });
    // A sign-in held at the plugin's fetch, after its login: the same.
    writeInstallationSettings(b, {});
    await setup.status();
    fetch = fetched();
    const signingIn = setup.connectReply(spare);
    await fetch;
    from = b.record().length;
    assert.deepEqual(await others(), refusedAll);
    assert.deepEqual([boxes, runsAfter(b, from, 'reply').filter((run) => !run.startsWith('--version'))], [[], []]);
    release();
    await signingIn;
    assert.deepEqual([b.world().profiles?.['outbound'], b.world().profiles?.['several']], [{ team_id: 9 }, {}], 'nothing of the refused acts was done');
});

test('every signed-in Reply account is bound to one team, which it shows: a profile that pins none is pinned at the look that finds its sign-in in one team, and the log says so; a key is bound to its own team; a sign-in in several is offered them, and a pick pins one; the team is named as Reply names it, or as the note keeps it once Reply named it; teams that could not be read are said, the other reads standing; and a pick for an account bound already is refused in the desktop’s own words', async (t) => {
    const log = memoryLog();
    const b = replyBench(t, {
        profiles: { outbound: {}, keyed: {} },
        credentials: { default: 'ada@example.test' },
        keys: { 'planted-reply-key': { login: 'bea@example.test', team: 5 } },
        seats: { 'ada@example.test': [3], 'bea@example.test': [5, 9] },
        teams: { '5': 'Made-up Five', '9': 'Made-up Nine' },
        lists: { 'ada@example.test#3': { email: [mailbox] }, 'bea@example.test#5': { email: [mailbox] }, 'bea@example.test#9': { linkedin: [linkedIn] } },
        browser: 'bea@example.test',
    });
    const accountOf = async (setup: Setup, name: string) => (await setup.status()).accounts?.accounts.find((each) => each.name === name);
    const bound = (account: Awaited<ReturnType<typeof accountOf>>) => ({ team: account?.team, teams: account?.teams, notRead: account?.notRead, ids: account?.senders.map((each) => each.id), unread: account?.unread });
    // ada's sign-in can act in team 3 alone and `default` pins none: the first look pins it, with no question, and reads the
    // account as that team, with no name since Reply gives it none.
    let from = b.record().length;
    const setup = setupOn(b, log);
    assert.deepEqual(bound(await accountOf(setup, 'default')), { team: { id: '3', name: '' }, teams: undefined, notRead: [], ids: ['12'], unread: [] });
    assert.deepEqual(b.world().profiles?.['default'], { team_id: 3 });
    assert.deepEqual(runsAfter(b, from, 'reply').filter((run) => run.startsWith('--profile default team')), ['--profile default team list --json', '--profile default team use 3 --json']);
    assert.deepEqual(log.lines.filter((line) => line.name === 'reply_team_bound').map((line) => line.fields), [{ Exit: 0 }]);
    // The next look finds it as noted, the same login in the same pinned team: no sign-in since, so nothing is taken to have
    // changed, its team is named from the note, and Reply is asked for no teams.
    from = b.record().length;
    assert.deepEqual(bound(await accountOf(setup, 'default')), { team: { id: '3', name: '' }, teams: undefined, notRead: [], ids: ['12'], unread: [] });
    assert.deepEqual(runsAfter(b, from, 'reply').filter((run) => run.startsWith('--profile default team')), []);
    // bea's key acts in its own team, whatever is pinned, though her login is in two: the look right after its sign-in binds
    // `keyed` to that team, with no question, and never offers the other.
    await setup.connectReplyWithKey('planted-reply-key', { type: 'reply', name: 'keyed' });
    assert.deepEqual(bound(await accountOf(setup, 'keyed')), { team: { id: '5', name: 'Made-up Five' }, teams: undefined, notRead: [], ids: ['12'], unread: [] });
    assert.deepEqual(b.world().profiles?.['keyed'], { team_id: 5 });
    // bea's sign-in by a browser can act in two teams and `outbound` pins none: the look right after it offers its teams,
    // by name, and asks Reply for none of its mailboxes or LinkedIn accounts, which Reply would refuse without a team.
    await setup.connectReply({ type: 'reply', name: 'outbound' });
    from = b.record().length;
    assert.deepEqual(bound(await accountOf(setup, 'outbound')), { team: null, teams: [{ id: '5', name: 'Made-up Five' }, { id: '9', name: 'Made-up Nine' }], notRead: [], ids: [], unread: ['email', 'linkedin'] });
    assert.deepEqual(runsAfter(b, from, 'reply').filter((run) => run.startsWith('--profile outbound')), ['--profile outbound auth status --json', '--profile outbound team list --json']);
    // A pick names a team the last read offered that account, and nothing else runs; a pick for an account bound already is
    // refused as that, and so is one for an account a terminal bound meanwhile, before anything changes its team; Reply's
    // refusal of a pick is said in the desktop's own words, which name no team.
    const outbound = { type: 'reply', name: 'outbound' } as const;
    const pinned = 'This Reply account is bound to its team already, and Jason Desktop does not change it, so nothing was changed.';
    from = b.record().length;
    for (const [named, team] of [[outbound, '7'], [outbound, 9]] as const) {
        assert.deepEqual(await setup.chooseTeam(named, team), no('That is not a team this Reply account was offered.'), `${JSON.stringify(named)} ${team}`);
    }
    assert.deepEqual(await setup.chooseTeam({ type: 'reply', name: 'default' }, '3'), no(pinned));
    assert.deepEqual(await setup.chooseTeam({ type: 'reply', name: 'nobody' }, '9'), no('That is not a Reply account the desktop listed.'));
    assert.deepEqual(runsAfter(b, from, 'reply'), []);
    writeFileSync(b.env['STUB_REPLY_WORLD']!, JSON.stringify({ ...b.world(), profiles: { ...b.world().profiles, outbound: { team_id: 5 } } }));
    assert.deepEqual(await setup.chooseTeam(outbound, '9'), no(pinned, null, true));
    assert.deepEqual(b.world().profiles?.['outbound'], { team_id: 5 });
    writeFileSync(b.env['STUB_REPLY_WORLD']!, JSON.stringify({ ...b.world(), profiles: { ...b.world().profiles, outbound: {} }, seats: { ...b.world().seats, 'bea@example.test': [5, 7] } }));
    assert.deepEqual(await setup.chooseTeam(outbound, '9'), no('The team was not chosen: this sign-in can no longer work in it.', 'usage.team', true));
    assert.deepEqual(b.world().profiles?.['outbound'], {});
    writeFileSync(b.env['STUB_REPLY_WORLD']!, JSON.stringify({ ...b.world(), seats: { ...b.world().seats, 'bea@example.test': [5, 9] } }));
    from = b.record().length;
    assert.deepEqual(await setup.chooseTeam(outbound, '9'), { done: true });
    // Who it is, which Reply names only in a team, is asked once the team is pinned, and the pick is noted at once.
    assert.deepEqual(runsAfter(b, from, 'reply'), ['--profile outbound auth status --json', '--profile outbound team use 9 --json', '--profile outbound auth whoami --json']);
    assert.deepEqual(b.world().profiles?.['outbound'], { team_id: 9 });
    // The account then reads as that team, shown by its name, and offers none.
    assert.deepEqual(bound(await accountOf(setup, 'outbound')), { team: { id: '9', name: 'Made-up Nine' }, teams: undefined, notRead: [], ids: ['34'], unread: [] });
    // Teams that could not be read are said, and the account's other reads stand: its pinned team, its name not read. It is
    // noted so, and a later look asks for the teams again, that account's alone, until Reply names the team; from then on
    // the name is the note's and no teams are asked.
    const world = { ...b.world(), credentials: { default: 'ada@example.test', outbound: 'bea@example.test' } };
    const unread = replyBench(t, world, { STUB_REPLY_TEAM_LIST: JSON.stringify({ code: 'teams.unavailable' }) });
    assert.deepEqual(bound(await accountOf(setupOn(unread), 'outbound')), { team: { id: '9', name: null }, teams: undefined, notRead: ['teams'], ids: ['34'], unread: [] });
    const notedUnread = readFileSync(unread.paths.replyAccountNotes, 'utf8');
    assert.deepEqual((JSON.parse(notedUnread) as { accounts: unknown[] }).accounts.at(-1), { type: 'reply', name: 'outbound', user: 'bea@example.test', team: { id: '9', name: null } });
    // While they still cannot be read, the noted login in its noted pin is taken as noted: nothing rebound, nothing said.
    const stillUnread = (await setupOn(unread).status()).accounts?.accounts.find((each) => each.name === 'outbound');
    assert.deepEqual({ ...bound(stillUnread), change: stillUnread?.change }, { team: { id: '9', name: null }, teams: undefined, notRead: ['teams'], ids: ['34'], unread: [], change: null });
    const readable = replyBench(t, world);
    mkdirSync(dirname(readable.paths.replyAccountNotes), { recursive: true });
    writeFileSync(readable.paths.replyAccountNotes, notedUnread);
    const later = setupOn(readable);
    const teamRuns = (since: number) => runsAfter(readable, since, 'reply').filter((run) => run.startsWith('--profile outbound team list'));
    from = readable.record().length;
    assert.deepEqual(bound(await accountOf(later, 'outbound')), { team: { id: '9', name: 'Made-up Nine' }, teams: undefined, notRead: [], ids: ['34'], unread: [] });
    assert.deepEqual(teamRuns(from), ['--profile outbound team list --json']);
    from = readable.record().length;
    assert.deepEqual(bound(await accountOf(later, 'outbound')), { team: { id: '9', name: 'Made-up Nine' }, teams: undefined, notRead: [], ids: ['34'], unread: [] });
    assert.deepEqual(teamRuns(from), []);
    // A profile that pins none, whose teams could not be read, is bound to none, and is read all the same.
    const loose = replyBench(t, { credentials: { default: 'ada@example.test' }, seats: { 'ada@example.test': [3] }, lists: { 'ada@example.test#3': { email: [mailbox] } } }, { STUB_REPLY_TEAM_LIST: JSON.stringify({ code: 'teams.unavailable' }) });
    assert.deepEqual(bound(await accountOf(setupOn(loose), 'default')), { team: null, teams: undefined, notRead: ['teams'], ids: ['12'], unread: [] });
    assert.equal(replyRuns(loose).some((run) => run.includes(' team use ')), false);
    // Two accounts bound at one look, side by side: the Reply CLI rewrites its configuration whole, so their writes go one
    // after the other, or one pin would be lost.
    const writes = join(mkdtempSync(join(tmpdir(), 'jason-config-writes-')), 'writes.log');
    t.after(() => rmSync(dirname(writes), { recursive: true, force: true }));
    const two = replyBench(t, { profiles: { outbound: {} }, credentials: { default: 'ada@example.test', outbound: 'cy@example.test' }, seats: { 'ada@example.test': [3], 'cy@example.test': [4] } }, { STUB_REPLY_CONFIG_WRITES: writes });
    await setupOn(two).status();
    const steps = readFileSync(writes, 'utf8').trim().split('\n').map((line) => line.split(' ')[0]);
    assert.deepEqual(steps, ['start', 'end', 'start', 'end'], readFileSync(writes, 'utf8'));
    assert.deepEqual([two.world().profiles?.['default'], two.world().profiles?.['outbound']], [{ team_id: 3 }, { team_id: 4 }]);
    // A terminal pins a team between the look's read of the teams and its bind: the bind reads the pin again in its own
    // turn of the writes, finds it, and writes nothing over it; a team not among those the look read is no team of the
    // sign-in's then. The next look reads the teams again and names it.
    const racing = replyBench(t, { credentials: { default: 'ada@example.test' }, seats: { 'ada@example.test': [3, 6] }, teams: { '6': 'Made-up Six' } });
    writeFileSync(racing.env['STUB_REPLY_WORLD']!, JSON.stringify({ ...racing.world(), seats: { 'ada@example.test': [3] } }));
    const terminal: Programs = {
        find: (name) => racing.programs.find(name),
        run: async (name, args, options) => {
            const result = await racing.programs.run(name, args, options);
            if (name === 'reply' && args.join(' ') === '--profile default team list --json') {
                writeFileSync(racing.env['STUB_REPLY_WORLD']!, JSON.stringify({ ...racing.world(), profiles: { default: { team_id: 6 } }, seats: { 'ada@example.test': [3, 6] } }));
            }
            return result;
        },
    };
    const racedSetup = new Setup({ ...setupDeps(racing), programs: terminal });
    const raced = await accountOf(racedSetup, 'default');
    assert.equal(replyRuns(racing).some((run) => run.includes(' team use ')), false);
    assert.deepEqual([racing.world().profiles?.['default'], raced?.team], [{ team_id: 6 }, null]);
    assert.deepEqual((await accountOf(racedSetup, 'default'))?.team, { id: '6', name: 'Made-up Six' });
    // A browser's sign-in of a login in several teams, on a profile that pins none, is one Reply does not name, so the Reply
    // CLI's `auth status` holds no login for it: signed in, its teams offered, and nobody asked who it is, which Reply
    // answers only in a team. Once a team is bound, the Reply CLI is asked who it is as that account, wherever `auth status`
    // did not say and nowhere else; where that could not be read, it is said, the other reads standing.
    const several = replyBench(t, {
        profiles: { default: { team_id: 3 }, teams: {} },
        credentials: { default: 'ada@example.test' },
        seats: { 'ada@example.test': [3], 'cy@example.test': [4, 6] },
        teams: { '4': 'Made-up Four', '6': 'Made-up Six' },
        lists: { 'cy@example.test#6': { email: [mailbox] } },
        browser: 'cy@example.test',
    });
    const browsing = setupOn(several);
    await browsing.status();
    await browsing.connectReply({ type: 'reply', name: 'teams' });
    assert.deepEqual(several.world().credentials?.['teams'], { login: 'cy@example.test', unnamed: true });
    const asWho = async () => {
        const each = await accountOf(browsing, 'teams');
        return { signedIn: each?.signedIn, user: each?.user, team: each?.team, teams: each?.teams?.map((team) => team.id), ids: each?.senders.map((sender) => sender.id), notRead: each?.notRead };
    };
    const whoami = (since: number) => runsAfter(several, since, 'reply').filter((run) => run.includes('auth whoami'));
    from = several.record().length;
    assert.deepEqual(await asWho(), { signedIn: true, user: null, team: null, teams: ['4', '6'], ids: [], notRead: [] });
    assert.deepEqual(whoami(from), []);
    assert.deepEqual(await browsing.chooseTeam({ type: 'reply', name: 'teams' }, '6'), { done: true });
    // Where the Reply CLI could not say who it is, that is said, its other reads standing, and nothing is noted.
    const whoUnread = replyBench(t, several.world(), { STUB_REPLY_WHOAMI: JSON.stringify({ code: 'reply.unavailable' }) });
    const unreadWho = (await setupOn(whoUnread).status()).accounts?.accounts.find((each) => each.name === 'teams');
    assert.deepEqual(
        { user: unreadWho?.user, team: unreadWho?.team, ids: unreadWho?.senders.map((sender) => sender.id), notRead: unreadWho?.notRead },
        { user: null, team: { id: '6', name: 'Made-up Six' }, ids: ['12'], notRead: ['login'] },
    );
    assert.equal(readFileSync(whoUnread.paths.replyAccountNotes, 'utf8').includes('"teams"'), false, 'nothing noted');
    from = several.record().length;
    assert.deepEqual(await asWho(), { signedIn: true, user: 'cy@example.test', team: { id: '6', name: 'Made-up Six' }, teams: undefined, ids: ['12'], notRead: [] });
    assert.deepEqual(whoami(from), ['--profile teams auth whoami --json'], 'asked as that account alone');
    // Noted, the next look takes it as the noted login in the noted team, and asks Reply nothing of it.
    from = several.record().length;
    assert.deepEqual(await asWho(), { signedIn: true, user: 'cy@example.test', team: { id: '6', name: 'Made-up Six' }, teams: undefined, ids: ['12'], notRead: [] });
    assert.deepEqual(runsAfter(several, from, 'reply').filter((run) => run.startsWith('--profile teams') && /whoami|team list/.test(run)), []);
});

test('a sign-in places only what is missing of Jason’s Reply plugin and its grant, as the runtime’s plugin list says, and where no global default is set has the runtime name that account the default, under the person’s name; a default already set, whatever its plugin or binding, is left as it is', async (t) => {
    const settings: DesktopSettings = { feed: 'http://127.0.0.1:9/manifest.json', skillsDeployedFor: null, theme: null };
    // A default made at a sign-in is journaled as one, told from a click on Make default.
    const set = (profile: string) => `route set --global --plugin reply --binding {"profile":"${profile}"} --actor human:made-up-person --reason Made the default at a sign-in in Jason Desktop`;
    // A fresh installation: no plugin, no grant, no default. All three, the package fetched from the release feed.
    const fresh = replyBench(t, { profiles: { outbound: {} }, keys: { 'planted-reply-key': { login: 'bea@example.test', team: 3 } } }, { STUB_REPLY_PLUGIN: 'absent' });
    const log = memoryLog();
    const first = await readSetup(fresh, log, settings);
    let from = fresh.record().length;
    await first.connectReplyWithKey('planted-reply-key', { type: 'reply', name: 'outbound' });
    assert.deepEqual(runsAfter(fresh, from, 'jason'), ['plugin list', 'plugin install reply --feed http://127.0.0.1:9/manifest.json', 'plugin reload', 'route list', set('outbound')]);
    assert.deepEqual(readInstallationSettings(fresh), {
        Plugins: { Grants: { reply: { Exec: ['reply'] } } },
        Routes: { Default: { Plugin: 'reply', Binding: { profile: 'outbound' } } },
    });
    assert.equal(readFileSync(dataLayout(fresh.paths.data).userSettingsFile, 'utf8').includes('planted-reply-key'), false);
    assert.equal(JSON.stringify(log.lines).includes('planted-reply-key'), false);
    assert.equal(JSON.stringify(log.lines).includes('outbound'), false, 'a profile’s name, an address as often as not, is never logged');
    assert.deepEqual(log.lines.find((line) => line.name === 'reply_placed_after_sign_in')?.fields, { Placed: 'placed_granted', Default: 'made' });
    assert.deepEqual((await first.status()).accounts?.accounts.map((each) => `${each.name}${each.default ? ' (default)' : ''}`), ['outbound (default)', 'default']);

    // The plugin listed and not granted: the grant alone, and the load that takes it up; nothing is fetched.
    const listed = replyBench(t, { profiles: { outbound: {} } });
    const granting = await readSetup(listed, memoryLog(), settings);
    from = listed.record().length;
    await granting.connectReply({ type: 'reply', name: 'outbound' });
    assert.deepEqual(runsAfter(listed, from, 'jason'), ['plugin list', 'plugin reload', 'route list', set('outbound')]);
    assert.equal((await granting.status()).reply.wired, true);

    // Listed and granted, with a default set: nothing placed, nothing written by the desktop, the default left as it is.
    const kept = { Plugins: { Grants: { reply: { Exec: ['reply'] } } }, Routes: { Default: { Plugin: 'reply', Binding: { profile: 'outbound' } } } };
    const ready = replyBench(t, { profiles: { outbound: {} } });
    writeInstallationSettings(ready, kept);
    const keptLog = memoryLog();
    const next = await readSetup(ready, keptLog, settings);
    from = ready.record().length;
    const before = readFileSync(dataLayout(ready.paths.data).userSettingsFile, 'utf8');
    await next.connectReply({ type: 'reply', name: 'default' });
    assert.deepEqual(runsAfter(ready, from, 'jason'), ['plugin list', 'route list']);
    assert.equal(readFileSync(dataLayout(ready.paths.data).userSettingsFile, 'utf8'), before, 'the file as it was, byte for byte');
    assert.deepEqual(keptLog.lines.filter((line) => line.name === 'reply_placed_after_sign_in').map((line) => line.fields), [{ Placed: 'in_place', Default: 'kept' }]);

    // Listed and granted, but not valid, as after a Reply CLI installed or updated outside the desktop: nothing is fetched
    // or written, and the runtime loads its plugins again, which is what makes it valid.
    const unavailable = replyBench(t, { profiles: { outbound: {} } }, { STUB_REPLY_PLUGIN: 'unavailable' });
    writeInstallationSettings(unavailable, kept);
    const reloading = await readSetup(unavailable, memoryLog(), settings);
    from = unavailable.record().length;
    await reloading.connectReply({ type: 'reply', name: 'default' });
    assert.deepEqual(runsAfter(unavailable, from, 'jason'), ['plugin list', 'plugin reload', 'route list']);

    // Listed and granted in a file with a comment, which the desktop cannot parse and the runtime reads: no default set,
    // so the runtime makes this one, and the desktop asks no release feed and writes nothing itself.
    const commented = replyBench(t, {});
    writeInstallationSettings(commented, '{\n  // mine\n  "Plugins": { "Grants": { "reply": { "Exec": ["reply"] } } }\n}\n');
    const signing = await readSetup(commented, memoryLog(), settings);
    from = commented.record().length;
    await signing.connectReply();
    assert.deepEqual(runsAfter(commented, from, 'jason'), ['plugin list', 'route list', set('default')]);

    // A route that names a team beside the profile is that profile's, where no default is set: the sign-in names no team,
    // and the account is made the default by its profile alone.
    const teamed = replyBench(t, { profiles: { outbound: {} } }, { STUB_CAMPAIGN_ROUTES: JSON.stringify([campaignRoute('cmp_1', { profile: 'outbound', team_id: '9' })]) });
    writeInstallationSettings(teamed, { Plugins: { Grants: { reply: { Exec: ['reply'] } } } });
    const teamLog = memoryLog();
    const onTeam = await readSetup(teamed, teamLog, settings);
    from = teamed.record().length;
    await onTeam.connectReply({ type: 'reply', name: 'outbound' });
    assert.deepEqual(runsAfter(teamed, from, 'jason'), ['plugin list', 'route list', set('outbound')]);
    assert.deepEqual(teamLog.lines.find((line) => line.name === 'reply_placed_after_sign_in')?.fields, { Placed: 'in_place', Default: 'made' });
    assert.deepEqual(readInstallationSettings(teamed).Routes, { Default: { Plugin: 'reply', Binding: { profile: 'outbound' } } });

    // The routes could not be read at the click: signed in, and said so: none was made the default.
    const unread = replyBench(t, {});
    writeInstallationSettings(unread, { Plugins: { Grants: { reply: { Exec: ['reply'] } } } });
    const blind = await readSetup(unread, memoryLog(), settings);
    unread.env['STUB_ROUTE_LIST'] = 'unreachable';
    await assert.rejects(blind.connectReply(), (error: unknown) => error instanceof Refused && error.message === 'Signed in, but which Reply account is the default could not be read, so none was made the default.');
    assert.equal(unread.world().credentials?.['default'], 'browser@example.test');
    assert.equal(unread.record().some((entry) => entry.name === 'jason' && entry.args[1] === 'set'), false);

    // A default the runtime refused leaves the sign-in standing, and says so in the desktop's sentence.
    const refused = replyBench(t, {}, { STUB_ROUTE_SET: JSON.stringify({ code: 'validation_failed', details: [{ code: 'not_representable_globally' }] }) });
    const refusing = await readSetup(refused);
    await assert.rejects(refusing.connectReply(), (error: unknown) => error instanceof Refused && error.message === 'Signed in, but this Reply account was not made the default: Make default on its card says why.');
    assert.equal(refused.world().credentials?.['default'], 'browser@example.test');
    assert.equal((readInstallationSettings(refused) as { Routes?: unknown }).Routes, undefined);

    // A placing that fails after the sign-in is the desktop's sentence, then the reason of the program that refused, the
    // package's fetch or the load that would take the grant up, without the "Nothing was changed." a fetch ends with, which
    // the sign-in belies.
    const offline = replyBench(t, {}, { STUB_REPLY_PLUGIN: 'absent', STUB_PLUGIN_INSTALL_FAILS: 'yes' });
    await assert.rejects((await readSetup(offline, memoryLog(), settings)).connectReply(), (error: unknown) => error instanceof Refused && error.message === 'Signed in, but Jason’s Reply plugin could not be placed or loaded. The update feed at http://127.0.0.1:9/manifest.json could not be reached.');
    assert.equal(offline.world().credentials?.['default'], 'browser@example.test');
    const reloadRefused = replyBench(t, {}, { STUB_RELOAD_REFUSED: 'yes' });
    await assert.rejects((await readSetup(reloadRefused)).connectReply(), (error: unknown) => error instanceof Refused && error.message === 'Signed in, but Jason’s Reply plugin could not be placed or loaded. The settings file could not be read (not valid JSON at line 1, byte 2), so nothing it grants or routes can be resolved; fix it, or close what holds it open, and reload again.');

    // A default a person set to another plugin: kept, and the grant written beside it.
    const other = bench(t, { programs: ['reply'] });
    writeInstallationSettings(other, { Routes: { Default: { Plugin: 'other', Binding: { account: 'a' } } } });
    const otherLog = memoryLog();
    await (await readSetup(other, otherLog)).connectReply();
    assert.deepEqual(readInstallationSettings(other), {
        Routes: { Default: { Plugin: 'other', Binding: { account: 'a' } } },
        Plugins: { Grants: { reply: { Exec: ['reply'] } } },
    });
    assert.deepEqual(otherLog.lines.find((line) => line.name === 'reply_placed_after_sign_in')?.fields, { Placed: 'granted', Default: 'kept' });
    assert.equal(other.record().some((entry) => entry.name === 'jason' && entry.args[1] === 'set'), false);
    // So is a Reply default that names no profile: no route is rewritten for it.
    const unnamed = bench(t, { programs: ['reply'] });
    writeInstallationSettings(unnamed, { Plugins: { Grants: { reply: { Exec: ['reply'] } } }, Routes: { Default: { Plugin: 'reply', Binding: {} } } });
    const unnamedLog = memoryLog();
    await (await readSetup(unnamed, unnamedLog)).connectReply();
    assert.deepEqual(readInstallationSettings(unnamed).Routes, { Default: { Plugin: 'reply', Binding: {} } });
    assert.deepEqual(unnamedLog.lines.find((line) => line.name === 'reply_placed_after_sign_in')?.fields, { Placed: 'in_place', Default: 'kept' });

    // A grant to write in a file the desktop cannot parse: the file is left as it is, nothing is placed, and the sign-in
    // itself still stands.
    const unreadable = bench(t, { programs: ['reply'] });
    const parsing = await readSetup(unreadable);
    writeInstallationSettings(unreadable, '{ /* mine */ }');
    from = unreadable.record().length;
    await assert.rejects(parsing.connectReply(), (error: unknown) => error instanceof Refused && error.message.startsWith('Signed in, but Jason’s Reply plugin could not be placed or loaded. The settings file is not plain JSON') && /comments/.test(error.message));
    assert.equal(readFileSync(dataLayout(unreadable.paths.data).userSettingsFile, 'utf8'), '{ /* mine */ }');
    assert.deepEqual(runsAfter(unreadable, from, 'jason'), ['plugin list']);
    assert.ok(existsSync(join(unreadable.paths.chat, '.claude', 'skills', 'reply-cli', 'SKILL.md')), 'the Reply skills are deployed all the same');
});

test('Make default, only while Jason has no global default, places what is missing of the plugin and its grant, reads the routes again and has the runtime name the account by its profile, under the person’s name, asking nothing; while a default is set, whatever its plugin or account, at the click or by the time it is placed, it is refused in the desktop’s sentence and the runtime names nobody', async (t) => {
    const make = async (env: Record<string, string>, settings: unknown, options: { person?: string | null; named?: string } = {}) => {
        const b = replyBench(t, twoSignIns, env);
        writeInstallationSettings(b, settings);
        const boxes: ReplyBox[] = [];
        const log = memoryLog();
        const setup = new Setup({ ...setupDeps(b, log), ...(options.person === undefined ? {} : { person: options.person }), confirm: (box) => (boxes.push(box), Promise.resolve(true)) });
        await setup.status();
        const from = b.record().length;
        const answer = await setup.makeDefault({ type: 'reply', name: options.named ?? 'outbound' });
        let routesNow: unknown;
        try {
            routesNow = (readInstallationSettings(b) as { Routes?: unknown }).Routes;
        } catch {
            routesNow = 'not JSON';
        }
        assert.deepEqual(boxes, [], 'no box is asked');
        assert.equal(JSON.stringify(log.lines).includes('outbound'), false, 'a profile’s name is never logged');
        return { answer: 'refused' in answer ? answer.refused : 'done', routes: routesNow, runs: runsAfter(b, from, 'jason') };
    };
    const set = `route set --global --plugin reply --binding {"profile":"outbound"} --actor human:made-up-person --reason ${defaultReason}`;
    const named = { Default: { Plugin: 'reply', Binding: { profile: 'outbound' } } };
    const granted = { Plugins: { Grants: { reply: { Exec: ['reply'] } } } };
    // No default: what is missing is placed, as the plugin list says, the routes read again, then the runtime names the
    // account by its profile.
    assert.deepEqual(await make({ STUB_REPLY_PLUGIN: 'absent' }, {}), { answer: 'done', routes: named, runs: ['route list', 'plugin list', 'plugin install reply', 'plugin reload', 'route list', set] });
    assert.deepEqual((await make({}, {})).runs, ['route list', 'plugin list', 'plugin reload', 'route list', set], 'listed and not granted: the grant alone, and its load');
    assert.deepEqual((await make({ STUB_REPLY_PLUGIN: 'unavailable' }, granted)).runs, ['route list', 'plugin list', 'plugin reload', 'route list', set], 'listed and granted, but not valid: the load alone');
    assert.deepEqual((await make({}, granted)).runs, ['route list', 'plugin list', 'route list', set], 'all in place: the route alone');
    // A default set, whatever its plugin or binding, the account's own included, or set by the time of the click though the
    // last read showed none: refused, and nothing runs past the routes.
    const exists = 'Jason has a default already, and Jason Desktop does not move it, so nothing was changed.';
    for (const routes of [
        { Default: { Plugin: 'reply', Binding: { profile: 'default' } } },
        { Default: { Plugin: 'slack', Binding: { channel: 'C1' } } },
        { Default: { Plugin: 'reply', Binding: { derive_first_name: false } } },
        named,
    ]) {
        assert.deepEqual(await make({}, { ...granted, Routes: routes }), { answer: exists, routes, runs: ['route list'] }, JSON.stringify(routes));
    }
    const late = replyBench(t, twoSignIns);
    writeInstallationSettings(late, granted);
    const lateSetup = setupOn(late);
    await lateSetup.status();
    writeInstallationSettings(late, { ...granted, Routes: { Default: { Plugin: 'reply', Binding: { profile: 'default' } } } });
    assert.deepEqual(await lateSetup.makeDefault({ type: 'reply', name: 'outbound' }), { refused: exists, code: null, offer: null, reread: false });
    // Set while it placed: a terminal's `jason route set --global` lands while the package is fetched. Placed, and refused,
    // and the default set meanwhile stays.
    const placing = replyBench(t, twoSignIns, { STUB_REPLY_PLUGIN: 'absent' });
    writeInstallationSettings(placing, {});
    const theirs = { Plugin: 'slack', Binding: { channel: 'C1' } };
    const meanwhile: Cli = {
        run: (args, options) => {
            if (args[0] === 'plugin' && args[1] === 'install') writeInstallationSettings(placing, { ...readInstallationSettings(placing), Routes: { Default: theirs } });
            return placing.cli.run(args, options);
        },
    };
    const whilePlacing = new Setup({ ...setupDeps(placing), cli: meanwhile });
    await whilePlacing.status();
    const placingFrom = placing.record().length;
    assert.deepEqual(await whilePlacing.makeDefault({ type: 'reply', name: 'outbound' }), { refused: exists, code: null, offer: null, reread: false });
    assert.deepEqual((readInstallationSettings(placing) as { Routes?: unknown }).Routes, { Default: theirs });
    assert.equal(runsAfter(placing, placingFrom, 'jason').some((run) => run.startsWith('route set')), false);
    // Every other refusal is the desktop's sentence, and the default stays unset.
    const refusals: [Record<string, string>, unknown, string][] = [
        [{ STUB_ROUTE_SET: JSON.stringify({ exit: 3 }) }, granted, 'Jason is not running, so nothing was changed. Start it, then try again.'],
        [{ STUB_ROUTE_SET: JSON.stringify({ code: 'validation_failed', details: [{ code: 'not_representable_globally' }] }) }, granted, 'The settings file cannot hold this Reply account’s name as written, so nothing was changed: route a campaign to it with `jason route set`, which keeps it as written.'],
        [{ STUB_ROUTE_SET: JSON.stringify({ code: 'plugin_reload_rejected' }) }, granted, 'The runtime did not change the default: it answered plugin_reload_rejected, so nothing was changed.'],
        // A placing that fails is the desktop's sentence, then the refusing program's reason, as at a sign-in.
        [{ STUB_RELOAD_REFUSED: 'yes' }, {}, 'Jason’s Reply plugin could not be placed or loaded, so nothing was changed. The settings file could not be read (not valid JSON at line 1, byte 2), so nothing it grants or routes can be resolved; fix it, or close what holds it open, and reload again.'],
        [{ STUB_REPLY_PLUGIN: 'absent', STUB_PLUGIN_INSTALL_FAILS: 'yes' }, granted, 'Jason’s Reply plugin could not be placed or loaded, so nothing was changed. The update feed at https://releases.example.test/manifest.json could not be reached.'],
    ];
    for (const [env, settings, sentence] of refusals) {
        const { runs: _, ...made } = await make(env, settings);
        assert.deepEqual(made, { answer: sentence, routes: undefined }, sentence);
    }
    // A grant to write in a file the desktop cannot parse: the same sentence, then the desktop's reason for that file, and
    // nothing is placed or written.
    const unparsed = await make({}, '{\n  // mine\n  "Harness": { "Root": "kept" }\n}\n');
    assert.match(unparsed.answer, /^Jason’s Reply plugin could not be placed or loaded, so nothing was changed\. The settings file is not plain JSON, so the desktop left it as it is: .* Edit it by hand, then press Reload\.$/);
    assert.deepEqual(unparsed.runs, ['route list', 'plugin list']);
    // Nobody can be named as having made the change, or the routes could not be read: nothing runs, or nothing past them.
    assert.deepEqual(await make({}, granted, { person: null }), { answer: 'Jason Desktop could not read the name you are signed in to this computer as, which a change of the default is recorded under, so nothing was changed.', routes: undefined, runs: [] });
    assert.deepEqual(await make({ STUB_ROUTE_LIST: 'unreachable' }, granted), { answer: 'Which Reply account is the default could not be read, so nothing was changed.', routes: undefined, runs: ['route list'] });
    // An account the last read did not list, or a value of another shape, is refused before anything runs.
    const b = replyBench(t, twoSignIns);
    const setup = await readSetup(b);
    for (const value of [{ type: 'reply', name: 'nobody' }, { type: 'reply', name: 'outbound', team: '7' }, { type: 'reply', name: 'outbound', team: null }, undefined, null, 'outbound']) {
        assert.deepEqual(await setup.makeDefault(value), { refused: 'That is not a Reply account the desktop listed.', code: null, offer: null, reread: false }, JSON.stringify(value));
    }
});

test('the members a page of an earlier version reads are the default account’s, or the profile default’s where none is: its sign-in, whether Jason’s Reply plugin is valid and the global default names that very account, and each of the last two it could not read', async (t) => {
    const b = bench(t, { programs: ['reply'], env: { STUB_REPLY_SIGNED_IN: 'yes' } });
    assert.equal((await setupOn(b).status()).reply.wired, false, 'valid, but no route names it');
    writeInstallationSettings(b, { Routes: { Default: { Plugin: 'reply', Binding: { profile: 'default' } } } });
    const { reply } = await setupOn(b).status();
    assert.deepEqual(reply, { cli: { found: true, version: '0.23.2', install: null }, signedIn: true, user: 'someone@example.com', wired: true, accounts: [], absent: [], unread: [], pending: [], pendingUnread: [], notRead: [], belowMinimum: null, startCheck: 'pending' });
    const asked = b.record().filter((entry) => entry.name === 'reply').map((entry) => entry.args.join(' '));
    assert.ok(asked.includes('--profile default account list --channel email --json') && asked.includes('--profile default account list --channel linkedin --json'));
    assert.equal(asked.some((said) => said.endsWith('account list --json')), false, 'one read per channel, never the unsplit list');

    const missing = bench(t);
    const absent = await setupOn(missing).status();
    assert.deepEqual(absent.reply.cli, { found: false, version: null, install: null }, 'without npm there is nothing to install it with');
    assert.deepEqual(absent.reply.notRead, [], 'without the Reply CLI nothing was asked, so nothing was not read');
    assert.equal(absent.reply.startCheck, 'pending', 'the start’s check of it has not ended, whatever the CLI');

    const read = async (env: Record<string, string>, routes: unknown = { Routes: { Default: { Plugin: 'reply', Binding: { profile: 'default' } } } }) => {
        const each = bench(t, { programs: ['reply'], env: { STUB_REPLY_SIGNED_IN: 'yes', ...env } });
        writeInstallationSettings(each, routes);
        const log = memoryLog();
        const { reply: state } = await setupOn(each, log).status();
        assert.equal(JSON.stringify(log.lines).includes('never shows'), false, 'nothing of the CLI error text is logged');
        const ran = each.record().map((entry) => `${entry.name} ${entry.args.join(' ')}`);
        return { signedIn: state.signedIn, user: state.user, wired: state.wired, notRead: state.notRead, listed: ran.some((each) => each.startsWith('reply --profile default account list')), routes: ran.includes('jason route list') };
    };

    // The sign-in: the CLI's own answer of none is the state "not signed in"; an ending before it answered, a profile it
    // does not know (reply-cli 0.23.2's usage.profile, exit 2), a credential store it cannot read (store.corrupt, exit 1,
    // its line on stderr alone) or nothing printed, is a sign-in that could not be read. Neither asks for an account.
    assert.deepEqual(await read({}), { signedIn: true, user: 'someone@example.com', wired: true, notRead: [], listed: true, routes: true });
    assert.deepEqual(await read({ STUB_REPLY_SIGNED_IN: 'no' }), { signedIn: false, user: null, wired: true, notRead: [], listed: false, routes: true });
    for (const ending of [{ code: 'usage.profile' }, { code: 'store.corrupt' }, { exit: 0, stdout: '' }]) {
        assert.deepEqual(await read({ STUB_REPLY_AUTH_STATUS: JSON.stringify(ending) }), { signedIn: false, user: null, wired: true, notRead: ['auth'], listed: false, routes: true }, JSON.stringify(ending));
    }

    // Whether the default names this account: a route list that names another plugin, or none, says no; one that did not
    // answer (no runtime answered, exit 3; it refused, exit 1) or printed what is no list could not be read.
    assert.deepEqual(await read({}, { Routes: { Default: { Plugin: 'other', Binding: { account: 'a' } } } }), { signedIn: true, user: 'someone@example.com', wired: false, notRead: [], listed: true, routes: true });
    assert.deepEqual(await read({}, {}), { signedIn: true, user: 'someone@example.com', wired: false, notRead: [], listed: true, routes: true });
    for (const ending of ['unreachable', 'refused', 'garbage']) {
        assert.deepEqual(await read({ STUB_ROUTE_LIST: ending }), { signedIn: true, user: 'someone@example.com', wired: false, notRead: ['route'], listed: true, routes: true }, ending);
    }
    // The runtime's plugin list, read once for the whole status: a Reply plugin it lists as not valid, or does not list,
    // is the state "not used"; a list it did not give leaves it unread. The routes are read all the same: they name the
    // accounts.
    for (const listed of ['invalid', 'absent']) {
        assert.deepEqual(await read({ STUB_REPLY_PLUGIN: listed }), { signedIn: true, user: 'someone@example.com', wired: false, notRead: [], listed: true, routes: true }, listed);
    }
    for (const ending of ['yes', 'unreachable', 'garbage']) {
        assert.deepEqual(await read({ STUB_PLUGIN_LIST_FAILS: ending }), { signedIn: true, user: 'someone@example.com', wired: false, notRead: ['route'], listed: true, routes: true }, ending);
    }
    assert.deepEqual(await read({ STUB_REPLY_AUTH_STATUS: JSON.stringify({ code: 'store.corrupt' }), STUB_ROUTE_LIST: 'unreachable' }), { signedIn: false, user: null, wired: false, notRead: ['auth', 'route'], listed: false, routes: true });
    // Of several accounts, the default's: bea's, its channels unread for want of a team, and the default names it.
    const several = replyBench(t, twoSignIns);
    writeInstallationSettings(several, { Routes: { Default: { Plugin: 'reply', Binding: { profile: 'outbound' } } } });
    assert.deepEqual((await setupOn(several).status()).reply, {
        cli: { found: true, version: '0.23.2', install: null },
        signedIn: true,
        user: 'bea@example.test',
        wired: true,
        accounts: [],
        absent: [],
        unread: ['email', 'linkedin'],
        pending: [],
        pendingUnread: [],
        notRead: [],
        belowMinimum: null,
        startCheck: 'pending',
    });
    // A Reply default that names no profile is the account of the profile the Reply CLI is set to, and names it.
    const unnamed = bench(t, { programs: ['reply'] });
    writeInstallationSettings(unnamed, { Plugins: { Grants: { reply: { Exec: ['reply'] } } }, Routes: { Default: { Plugin: 'reply', Binding: {} } } });
    const { reply: unnamedReply, accounts: marked } = await setupOn(unnamed).status();
    assert.deepEqual([unnamedReply.wired, marked?.accounts.filter((each) => each.default).map((each) => each.name)], [true, ['default']]);
});

// Two made-up sign-ins: ada, one seat, with a mailbox; bea, two seats, with a LinkedIn account in seat 9.
const mailbox = { id: 12, channel: 'email', name: 'Made-up Sales', address: 'sales@example.test', reply_status: 'connected' };
const linkedIn = { id: 34, channel: 'linkedin', name: 'Made-up Bea', address: '', reply_status: 'enabled' };
const twoSignIns: ReplyWorld = {
    current: 'quiet',
    profiles: { outbound: {}, quiet: {} },
    credentials: { default: 'ada@example.test', outbound: 'bea@example.test' },
    seats: { 'ada@example.test': [3], 'bea@example.test': [5, 9] },
    lists: { 'ada@example.test#3': { email: [mailbox] }, 'bea@example.test#9': { linkedin: [linkedIn] } },
};

test('each account is read under its own profile’s name alone, a profile only a route names asked nothing, the default first and marked, and every other global route to Reply marked on the account it names; a route that names no profile is, at each read, the profile the Reply CLI is set to', async (t) => {
    // bea's login can act in teams 5 and 9: `outbound` pins none, `outbound-nine` pins 9 in the Reply CLI.
    const world: ReplyWorld = {
        ...twoSignIns,
        profiles: { outbound: {}, 'outbound-nine': { team_id: 9 }, quiet: {} },
        credentials: { ...twoSignIns.credentials, 'outbound-nine': 'bea@example.test' },
        teams: { '5': 'Made-up Five', '9': 'Made-up Nine' },
    };
    const b = replyBench(t, world, {
        STUB_CAMPAIGN_ROUTES: JSON.stringify([campaignRoute('cmp_1', { profile: 'outbound', team_id: '9' }), campaignRoute('cmp_2', { profile: 'routed-only' }), campaignRoute('cmp_3', null)]),
    });
    // Global routes of an operation and of a channel's sends, one of them naming no profile, and one to another plugin.
    const operations = {
        'contact.find': { Plugin: 'reply', Binding: { profile: 'outbound-nine' } },
        'campaign.get': { Plugin: 'slack', Binding: { channel: 'C1' } },
        'message.send': { Channels: { linkedin: { Plugin: 'reply', Binding: { profile: 'outbound' } }, email: { Plugin: 'reply', Binding: {} } } },
    };
    writeInstallationSettings(b, { Routes: { Default: { Plugin: 'reply', Binding: { profile: 'outbound' } }, Operations: operations } });
    const setup = setupOn(b);
    const { accounts } = await setup.status();
    const state = (name: string, over: Record<string, unknown>) => ({
        type: 'reply', name, default: false, defaultFor: [], held: true, followed: false, signedIn: false, user: null, team: null, senders: [], absent: [], unread: [], pending: [], pendingUnread: [], notRead: [], lastSignIn: null, change: null, ...over,
    });
    const bea = { signedIn: true, user: 'bea@example.test' };
    const asListed = (row: typeof mailbox) => ({ id: String(row.id), channel: row.channel, name: row.name, address: row.address, status: row.reply_status });
    assert.deepEqual(accounts, {
        accounts: [
            // The default, whatever team a campaign's route names beside it: bea's profile with no team pinned, which Reply
            // will not list until one is; it is offered the login's teams, its channels unread.
            state('outbound', { ...bea, default: true, defaultFor: [{ kind: 'channel', operation: 'message.send', channel: 'linkedin' }], unread: ['email', 'linkedin'], teams: [{ id: '5', name: 'Made-up Five' }, { id: '9', name: 'Made-up Nine' }] }),
            // ada's sign-in in one team, bound to it by this look, with no name since Reply names it not.
            // Each account signed in and bound is noted as who it was signed in as at this first look.
            state('default', { signedIn: true, user: 'ada@example.test', team: { id: '3', name: '' }, senders: [asListed(mailbox)], lastSignIn: { user: 'ada@example.test', team: { id: '3', name: '' } } }),
            // A second team is a second profile, pinned to it in the Reply CLI, and shown by its name.
            state('outbound-nine', { ...bea, defaultFor: [{ kind: 'operation', operation: 'contact.find' }], team: { id: '9', name: 'Made-up Nine' }, senders: [asListed(linkedIn)], lastSignIn: { user: 'bea@example.test', team: { id: '9', name: 'Made-up Nine' } } }),
            // The profile the CLI is set to is an account like any other, and the route naming no profile marks it.
            state('quiet', { defaultFor: [{ kind: 'channel', operation: 'message.send', channel: 'email' }], followed: true }),
            state('routed-only', { held: false }),
        ],
        notRead: [],
        noDefault: false,
    });
    const runs = replyRuns(b);
    assert.ok(runs.includes('profile list --json'));
    assert.ok(runs.includes('--profile outbound auth status --json'));
    assert.ok(runs.includes('--profile outbound-nine account list --channel linkedin --json'));
    assert.ok(runs.includes('--profile outbound-nine account pending --channel linkedin --json'));
    assert.ok(runs.includes('--profile quiet auth status --json'));
    assert.equal(runs.some((run) => run.includes('routed-only')), false, 'nothing is asked of the CLI for a profile it does not hold');
    assert.equal(runs.some((run) => run.startsWith('--profile quiet account')), false, 'nothing is listed for a profile with no sign-in');
    for (const run of runs) assert.ok(run === '--version' || run === 'profile list --json' || run.startsWith('--profile '), `every other run names its account: ${run}`);
    // A global default that names no profile is, at each read, the account of the profile the Reply CLI is set to then: a
    // `reply profile use` in a terminal moves the mark. Jason's Reply plugin keeps no profile of its own for such a route,
    // so nothing in its configuration is read for one.
    writeInstallationSettings(b, { Routes: { Default: { Plugin: 'reply', Binding: {} } }, Plugins: { Config: { reply: { profile: 'outbound' } } } });
    const marked = async () => (await setup.status()).accounts?.accounts.filter((each) => each.default).map((each) => each.name);
    assert.deepEqual(await marked(), ['quiet']);
    writeFileSync(b.env['STUB_REPLY_WORLD']!, JSON.stringify({ ...b.world(), current: 'outbound-nine' }));
    assert.deepEqual(await marked(), ['outbound-nine']);
});

test('a listing asked while one is read is answered by that read, and one asked after a change ended is read afresh, whatever the change', async (t) => {
    const b = replyBench(t, twoSignIns, {
        STUB_CAMPAIGN_ROUTES: JSON.stringify([campaignRoute('cmp_1', { profile: 'routed-only' })]),
        STUB_REPLY_ACCOUNT_LIMITS: JSON.stringify({ print: printed.limitsEmail }),
    });
    const setup = setupOn(b);
    await setup.status();
    const listed = (from: number) => runsAfter(b, from, 'reply').filter((run) => run === 'profile list --json').length;
    // One check reads each account once: three listings asked together, as when a click on Check again also brings the
    // window forward, or an act's end and the change it announces both ask, are one read.
    let from = b.record().length;
    await Promise.all([setup.status(), setup.status(), setup.status()]);
    assert.equal(listed(from), 1);
    // A listing asked after a change that ended while a read was going is never answered by that read. The kinds of change,
    // each with the reads of the profiles it makes itself.
    const announced = async () => {
        let told = 0;
        const stop = setup.onChanged(() => void told++);
        setup.announceChange();
        stop();
        assert.equal(told, 1, 'the page is told');
    };
    const changes: [string, () => Promise<unknown>, number][] = [
        ['an act of the setup’s queue', () => setup.setProviderKey('moonshot', 'sk-planted'), 0],
        ['a Reply verb', () => setup.replyLimits(String(mailbox.id), undefined, { type: 'reply', name: 'default' }), 0],
        ['a team’s pick', () => setup.chooseTeam({ type: 'reply', name: 'outbound' }, '5'), 0],
        ['a sign-in refused once it made its profile, which announces nothing', () => setup.connectReplyWithKey('made-up-unknown-key', { type: 'reply', name: 'routed-only' }).catch(() => undefined), 1],
        ['a change the desktop learnt of elsewhere, the models Codex offers or Full access’s bypass', announced, 0],
    ];
    for (const [kind, change, ownReads] of changes) {
        from = b.record().length;
        let answered = false;
        const before = setup.status().then((state) => ((answered = true), state));
        await change();
        assert.equal(answered, false, `${kind}: the first read is still going when it ends`);
        await Promise.all([before, setup.status()]);
        assert.equal(listed(from), 2 + ownReads, kind);
    }
    assert.ok(b.world().profiles?.['routed-only'] !== undefined, 'the refused sign-in made its profile');
    assert.deepEqual(b.world().profiles?.['outbound'], { team_id: 5 }, 'the pick pinned its team');
    // A read that began before another and ends after it never replaces what the later one listed: the acts are checked
    // against the accounts of the read that began last.
    let release: () => void = () => undefined;
    let holding = false;
    const slow: Programs = {
        find: (name) => b.programs.find(name),
        run: async (name, args, options) => {
            const result = await b.programs.run(name, args, options);
            if (holding && name === 'reply' && args.join(' ') === 'profile list --json') {
                holding = false;
                await new Promise<void>((resolve) => (release = resolve));
            }
            return result;
        },
    };
    const ordered = new Setup({ ...setupDeps(b), programs: slow, confirm: () => Promise.resolve(false) });
    await ordered.status();
    holding = true;
    const older = ordered.status();
    await new Promise((resolve) => setTimeout(resolve, 500));
    writeFileSync(b.env['STUB_REPLY_WORLD']!, JSON.stringify({ ...b.world(), profiles: { ...b.world().profiles, later: {} } }));
    ordered.announceChange();
    const newer = await ordered.status();
    assert.ok(newer.accounts?.accounts.some((each) => each.name === 'later'));
    release();
    assert.equal((await older).accounts?.accounts.some((each) => each.name === 'later'), false, 'the older read answers its own asker');
    assert.deepEqual(await ordered.removeAccount({ type: 'reply', name: 'later' }), no('Nothing was done.'), 'an act on an account the later read listed');
    // A window's start: its first listing goes while, in the setup's queue, the start puts the agents' roles on the AI
    // the chat runs on and finds nothing to do there (no host found), then looks at the Reply CLI, finds it at the
    // minimum and runs nothing; told that the check ended, the window asks again, as it does over the seam, a moment
    // later. That is one read, and it says the check ended.
    // The first read is held at the profiles, past where it reads the Reply CLI's version.
    const starting = new Setup({ ...setupDeps(b), programs: slow });
    const asked: Promise<SetupState>[] = [];
    starting.onChanged(() => void setTimeout(() => asked.push(starting.status()), 10));
    from = b.record().length;
    holding = true;
    const window = starting.status();
    await new Promise((resolve) => setTimeout(resolve, 500));
    assert.equal(holding, false, 'the first read is held');
    assert.equal(await starting.connectRoles(null), 'no_host');
    assert.equal(await starting.updateReplyAtStart(), 'current');
    await new Promise((resolve) => setTimeout(resolve, 50));
    release();
    const answers = await Promise.all([window, ...asked]);
    assert.deepEqual([listed(from), answers.map((state) => state.reply.startCheck)], [1, ['ended', 'ended']]);
});

test('what could not be read of the accounts is said, never drawn as a state: the profiles, the routes, a sign-in; and a default that is not set is told from one that is another plugin’s', async (t) => {
    const read = async (env: Record<string, string>, routes: unknown = { Routes: { Default: { Plugin: 'reply', Binding: { profile: 'outbound' } } } }) => {
        const b = replyBench(t, twoSignIns, env);
        writeInstallationSettings(b, routes);
        const { reply, accounts } = await setupOn(b).status();
        return {
            accounts: (accounts?.accounts ?? []).map((each) => `${each.name}${each.default ? ' (default)' : ''}${each.notRead.length > 0 ? ` [${each.notRead.join()}]` : ''}`),
            notRead: accounts?.notRead,
            noDefault: accounts?.noDefault,
            reply: { signedIn: reply.signedIn, user: reply.user, wired: reply.wired, notRead: reply.notRead },
            runs: replyRuns(b),
        };
    };
    // The profiles could not be read: no account at all, and nothing asked of one; a page of an earlier version says the
    // sign-in could not be read, never that nobody is signed in.
    const corrupt = await read({ STUB_REPLY_PROFILE_LIST: JSON.stringify({ code: 'config.corrupt' }) });
    assert.deepEqual(corrupt, { accounts: [], notRead: ['profiles'], noDefault: false, reply: { signedIn: false, user: null, wired: false, notRead: ['auth'] }, runs: ['--version', 'profile list --json'] });
    // The routes could not be read: the CLI's accounts stay, none is marked, and the line of a missing default is not said.
    for (const ending of ['unreachable', 'refused', 'garbage']) {
        assert.deepEqual(
            (({ runs: _, ...rest }) => rest)(await read({ STUB_ROUTE_LIST: ending })),
            { accounts: ['default', 'outbound', 'quiet'], notRead: ['routes'], noDefault: false, reply: { signedIn: true, user: 'ada@example.test', wired: false, notRead: ['route'] } },
            ending,
        );
    }
    // No global default at all is a state, said as that; one to another plugin marks no Reply account and is no missing one.
    assert.deepEqual((({ runs: _, ...rest }) => rest)(await read({}, {})), { accounts: ['default', 'outbound', 'quiet'], notRead: [], noDefault: true, reply: { signedIn: true, user: 'ada@example.test', wired: false, notRead: [] } });
    assert.deepEqual((({ runs: _, ...rest }) => rest)(await read({}, { Routes: { Default: { Plugin: 'slack', Binding: { channel: 'C1' } } } })), { accounts: ['default', 'outbound', 'quiet'], notRead: [], noDefault: false, reply: { signedIn: true, user: 'ada@example.test', wired: false, notRead: [] } });
    // A sign-in that could not be read is that account's own line, and no sign-in: nothing of it is listed.
    const unreadSignIn = await read({ STUB_REPLY_AUTH_STATUS: JSON.stringify({ code: 'store.corrupt' }) });
    assert.deepEqual(unreadSignIn.accounts, ['outbound (default) [auth]', 'default [auth]', 'quiet [auth]']);
    assert.deepEqual(unreadSignIn.reply, { signedIn: false, user: null, wired: true, notRead: ['auth'] });
    assert.equal(unreadSignIn.runs.some((run) => run.includes(' account ')), false);
});

test('the Reply card keeps three outcomes per channel: listed, not in the plan or role, and could not be read; and of a listed one, whether its links still waiting could be read', async (t) => {
    const email = JSON.stringify([{ id: 'e1', channel: 'email', name: 'Sales', address: 'sales@example.test', reply_status: 'connected' }]);
    const linkedin = JSON.stringify([{ id: 'l1', channel: 'linkedin', name: 'Ada', address: '', reply_status: 'enabled' }]);
    const read = async (emailEnd: string, linkedinEnd: string, pendingEnd?: object, perChannel: { email?: object; linkedin?: object } = {}) => {
        const env: Record<string, string> = { STUB_REPLY_SIGNED_IN: 'yes', STUB_REPLY_ACCOUNTS_EMAIL: emailEnd, STUB_REPLY_ACCOUNTS_LINKEDIN: linkedinEnd };
        if (pendingEnd !== undefined) env['STUB_REPLY_ACCOUNT_PENDING'] = JSON.stringify(pendingEnd);
        if (perChannel.email !== undefined) env['STUB_REPLY_ACCOUNT_PENDING_EMAIL'] = JSON.stringify(perChannel.email);
        if (perChannel.linkedin !== undefined) env['STUB_REPLY_ACCOUNT_PENDING_LINKEDIN'] = JSON.stringify(perChannel.linkedin);
        const b = bench(t, { programs: ['reply'], env });
        const log = memoryLog();
        const { reply } = await setupOn(b, log).status();
        const logged = JSON.stringify(log.lines);
        assert.equal(logged.includes('never shown'), false, 'nothing of the CLI error text is logged');
        return {
            ids: reply.accounts.map((each) => each.id),
            absent: reply.absent,
            unread: reply.unread,
            pendingUnread: reply.pendingUnread,
            waiting: reply.pending.map((row) => `${row.channel} ${row.id}`),
        };
    };

    assert.deepEqual(await read(email, linkedin), { ids: ['e1', 'l1'], absent: [], unread: [], pendingUnread: [], waiting: [] });
    for (const [channel, other, otherId] of [['linkedin', email, 'e1'], ['email', linkedin, 'l1']] as const) {
        const ended = (end: string) => (channel === 'linkedin' ? [other, end] as const : [end, other] as const);
        // reply-cli 0.20.0 answers access.feature_unavailable, exit 1, for the plan or role lacking the channel.
        assert.deepEqual(await read(...ended('refused')), { ids: [otherId], absent: [channel], unread: [], pendingUnread: [], waiting: [] });
        // Every other ending is only that it could not be read: a network failure, an expired sign-in, an exit
        // nobody explained, an answer that is no list.
        for (const end of ['network', 'expired', 'unexplained', 'garbage']) {
            assert.deepEqual(await read(...ended(end)), { ids: [otherId], absent: [], unread: [channel], pendingUnread: [], waiting: [] }, end);
        }
    }

    // A read of the links still waiting that does not end done, or prints no list, names its channel, and only a channel
    // whose accounts were listed: its accounts stay. Here the stand-in answers both channels' reads alike.
    const failed = [
        { code: 'reply.unavailable' },
        { code: 'access.denied' },
        { exit: 7, stderr: 'something nobody explained' },
        { exit: 0, stdout: 'not json' },
        { print: { supported: true, unsupported_channels: [] } },
    ];
    for (const end of failed) {
        const why = JSON.stringify(end);
        assert.deepEqual(await read(email, linkedin, end), { ids: ['e1', 'l1'], absent: [], unread: [], pendingUnread: ['email', 'linkedin'], waiting: [] }, why);
        assert.deepEqual(await read(email, 'refused', end), { ids: ['e1'], absent: ['linkedin'], unread: [], pendingUnread: ['email'], waiting: [] }, why);
        assert.deepEqual(await read('network', linkedin, end), { ids: ['l1'], absent: [], unread: ['email'], pendingUnread: ['linkedin'], waiting: [] }, why);
    }
    // The CLI saying it has none for a channel is no failure, nor is a list of none: a channel it says is unsupported, a
    // Reply that issues no such links (reply-cli 0.23.0's email_link.not_available, exit 1), a plan or role that does
    // not include them (access.feature_unavailable, exit 1).
    const notAvailable = { code: 'email_link.not_available' };
    const none = [
        { print: { items: [], supported: false, unsupported_channels: ['email', 'linkedin'] } },
        { print: { items: [], supported: true, unsupported_channels: [] } },
        notAvailable,
        { code: 'access.feature_unavailable' },
    ];
    for (const end of none) {
        const why = JSON.stringify(end);
        assert.deepEqual(await read(email, linkedin, end), { ids: ['e1', 'l1'], absent: [], unread: [], pendingUnread: [], waiting: [] }, why);
        assert.deepEqual(await read(email, 'refused', end), { ids: ['e1'], absent: ['linkedin'], unread: [], pendingUnread: [], waiting: [] }, why);
        assert.deepEqual(await read('network', linkedin, end), { ids: ['l1'], absent: [], unread: ['email'], pendingUnread: [], waiting: [] }, why);
    }

    // Each channel's read is its own. As on a Reply with LinkedIn's pending route and none for email, the email read
    // saying none here leaves LinkedIn's read to list its link or to fail on its own; and a failed email read names
    // email alone.
    const linkedInWaiting = { print: { items: [{ channel: 'linkedin', id: 7, name: 'Made-up LinkedIn', kind: 'connect', account_id: null, expires_at: null, expired: false }], supported: true, unsupported_channels: [] } };
    assert.deepEqual(await read(email, linkedin, undefined, { email: notAvailable, linkedin: linkedInWaiting }), { ids: ['e1', 'l1'], absent: [], unread: [], pendingUnread: [], waiting: ['linkedin 7'] });
    assert.deepEqual(await read(email, linkedin, undefined, { email: notAvailable, linkedin: { code: 'reply.unavailable' } }), { ids: ['e1', 'l1'], absent: [], unread: [], pendingUnread: ['linkedin'], waiting: [] });
    assert.deepEqual(await read(email, linkedin, undefined, { email: { code: 'reply.unavailable' }, linkedin: linkedInWaiting }), { ids: ['e1', 'l1'], absent: [], unread: [], pendingUnread: ['email'], waiting: ['linkedin 7'] });
});

test('a channel read is the plan-or-role refusal only by the code reply-cli gives for it, and a refusal in a team the sign-in cannot act in only by Reply’s own code; a sign-in with no team chosen and a timeout are reads that did not answer', () => {
    const json = (code: string, more: Record<string, unknown> = {}) => JSON.stringify({ error: { code, ...more } }) + String.fromCharCode(10);
    assert.deepEqual(readReplyChannel({ exit: 1, stdout: '', stderr: json('access.feature_unavailable') }), { kind: 'absent' });
    // Reply's 403 TEAM_REQUIRED, which reply-cli 0.23.2 answers as access.denied carrying Reply's code (src/operations/call.ts
    // `refusal`): the sign-in belongs to more than one team and the profile has none. The teams are read before any channel,
    // so one that comes anyway is a read that did not answer.
    const teamRequired = { status: 403, title: 'Reply refused access.', reply_code: 'TEAM_REQUIRED', hint: 'You belong to multiple teams. Choose one:' };
    for (const result of [
        { exit: 1, stdout: '', stderr: json('access.denied', teamRequired) },
        { exit: 1, stdout: '', stderr: json('reply.refused', { ...teamRequired, reply_code: 'TEAM_NOT_ACCESSIBLE' }) },
        { exit: 1, stdout: '', stderr: json('reply.refused', teamRequired) },
        { exit: 2, stdout: '', stderr: json('access.denied', teamRequired) },
        { exit: 2, stdout: '', stderr: json('access.denied', { ...teamRequired, reply_code: 'TEAM_NOT_ACCESSIBLE' }) },
    ]) assert.deepEqual(readReplyChannel(result), { kind: 'unread' }, JSON.stringify(result));
    // Reply's 403 TEAM_NOT_ACCESSIBLE, as reply-cli 0.23.2 printed it for a browser sign-in in a team it is not in: the
    // sign-in cannot act in the profile's pinned team.
    const notAccessible = { status: 403, title: 'Reply refused access.', detail: 'The requested team is not one you can act in.', reply_code: 'TEAM_NOT_ACCESSIBLE' };
    assert.deepEqual(readReplyChannel({ exit: 1, stdout: '', stderr: json('access.denied', notAccessible) }), { kind: 'outOfTeam' });
    // Progress lines may come first; the last line is the verdict.
    assert.deepEqual(readReplyChannel({ exit: 1, stdout: '', stderr: 'checking' + String.fromCharCode(10) + json('access.feature_unavailable') }), { kind: 'absent' });
    for (const result of [
        { exit: 1, stdout: '', stderr: json('access.denied') },
        { exit: 1, stdout: '', stderr: json('reply.unavailable') },
        { exit: 2, stdout: '', stderr: json('access.feature_unavailable') },
        { exit: -1, stdout: '', stderr: json('access.feature_unavailable') },
        { exit: -1, stdout: '', stderr: '' },
        { exit: 1, stdout: '', stderr: 'access.feature_unavailable' },
        { exit: 0, stdout: '{"items":"no"}', stderr: '' },
        { exit: 0, stdout: '', stderr: '' },
    ]) assert.deepEqual(readReplyChannel(result), { kind: 'unread' }, JSON.stringify(result));
    assert.deepEqual(readReplyChannel({ exit: 0, stdout: '{"items":[]}', stderr: '' }), { kind: 'listed', senders: [] });
});

test('with no usable Reply CLI there is no account: one older than Jason’s Reply plugin requires is offered the update and asked nothing more; one whose version could not be read is named so, and asked nothing either; a missing one is asked nothing', async (t) => {
    const b = bench(t, { programs: ['reply', 'npm'], env: { STUB_REPLY_SIGNED_IN: 'yes', STUB_REPLY_VERSION: '0.11.0' } });
    const { reply, accounts } = await setupOn(b).status();
    assert.deepEqual(accounts, { accounts: [], notRead: [], noDefault: false }, 'no account, and nothing said of one');
    assert.deepEqual(reply.cli, { found: true, version: '0.11.0', install: 'reply' });
    assert.deepEqual(reply.accounts, []);
    assert.equal(reply.belowMinimum, 'Managing Reply accounts here needs the Reply CLI 0.23.2 or later: update the Reply CLI.');
    // Nothing was asked of a sign-in, so a page of an earlier version says it was not read, never that nobody is signed in.
    assert.deepEqual(reply.notRead, ['auth'], 'a version that was read, below the minimum, is no failed read');
    assert.equal(reply.signedIn, false);
    assert.deepEqual(replyRuns(b), ['--version'], 'that version is asked nothing of the accounts');
    const missing = bench(t);
    assert.deepEqual((await setupOn(missing).status()).accounts, { accounts: [], notRead: [], noDefault: false });
    assert.deepEqual(missing.record().filter((entry) => entry.name === 'jason' && entry.args[0] === 'route'), [], 'nor are the routes, which name no account without the CLI');

    const current = bench(t, { programs: ['reply', 'npm'], env: { STUB_REPLY_SIGNED_IN: 'yes' } });
    assert.equal((await setupOn(current).status()).reply.cli.install, null);
    assert.deepEqual((await setupOn(current).status()).reply.notRead, []);

    // A `reply --version` that said no version: a Node error and exit 1 (an install npm is replacing), exit 127 (no node
    // for the shim), or exit 0 and nothing. It is named, and the card asks no account, as below the minimum; the
    // sentence below the minimum stays for a page of an earlier version, which reads no name and offers no act.
    for (const ending of [{ exit: 1, stderr: "Error: Cannot find module '/made/up/reply-cli/dist/index.js'\n" }, { exit: 127, stderr: "/usr/bin/env: 'node': No such file or directory\n" }, { exit: 0, stdout: '' }]) {
        const unread = bench(t, { programs: ['reply', 'npm'], env: { STUB_REPLY_SIGNED_IN: 'yes', STUB_REPLY_VERSION_ENDS: JSON.stringify(ending) } });
        const { reply: state } = await setupOn(unread).status();
        const why = JSON.stringify(ending);
        assert.deepEqual(state.cli, { found: true, version: null, install: 'reply' }, why);
        assert.deepEqual(state.notRead, ['version', 'auth'], why);
        assert.equal(state.belowMinimum, 'Managing Reply accounts here needs the Reply CLI 0.23.2 or later: update the Reply CLI.', why);
        assert.equal(state.signedIn, false, why);
        assert.deepEqual(replyRuns(unread), ['--version'], why);
    }
});

test('every host is listed, found or not; the missing ones name an install only where npm can do it', async (t) => {
    const b = bench(t, { programs: ['claude', 'gemini', 'npm'] });
    const { hosts } = await setupOn(b).status();
    const byId = new Map(hosts.map((host) => [host.id, host]));
    assert.deepEqual(byId.get('claude'), { id: 'claude', label: 'Claude Code', kind: 'program', found: true, version: '10.9.2', drivesChat: true, install: null, belowFloor: null, fullAccess: null, autoEdit: null, runs: { window: {}, tab: { full_access: { mode: 'ask_first', why: tabFullAccess } } }, models: claudeCodeModels('10.9.2'), typedModel: true, modelsUnread: null, ownServers: [], runsRoles: null });
    assert.equal(byId.get('codex')?.found, false);
    assert.equal(byId.get('codex')?.install, 'codex');
    assert.equal(byId.get('gemini')?.found, true);
    assert.equal(byId.get('gemini')?.drivesChat, false);
    assert.equal(byId.get('copilot')?.install, null, 'the desktop installs only the hosts it names an npm package for');
    assert.equal(byId.get('deepseek')?.kind, 'provider');
});

test('each host carries the models it offers with their efforts, and a listing asks Codex again only for a host found or a key stored', async (t) => {
    const b = bench(t, { programs: ['claude', 'codex', 'gemini'] });
    const refreshed: string[][] = [];
    const kept = new Map([['codex', { askedAt: '2026-11-11T10:00:00.000Z', models: [{ id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', efforts: ['low' as const] }] }]]);
    const unread = new Map<string, 'waiting' | 'failed'>([['codex-api-key', 'failed']]);
    const setup = new Setup({ ...setupDeps(b), models: { lists: () => kept, refresh: (ids) => void refreshed.push([...ids]), unread: (id) => unread.get(id) ?? null } });
    const every = ['low', 'medium', 'high', 'xhigh', 'max'];
    let byId = new Map((await setup.status()).hosts.map((host) => [host.id, host]));
    assert.deepEqual(byId.get('claude')?.models, [
        { id: 'opus', label: 'Opus (latest)', efforts: every },
        { id: 'sonnet', label: 'Sonnet (latest)', efforts: every },
        { id: 'haiku', label: 'Haiku (latest)', efforts: every },
        { id: 'fable', label: 'Fable (latest)', efforts: every },
    ]);
    assert.equal(byId.get('claude')?.typedModel, true);
    assert.equal(byId.get('claude-api-key')?.typedModel, true);
    assert.deepEqual(byId.get('codex')?.models, [{ id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', efforts: ['low'] }]);
    assert.equal(byId.get('codex')?.typedModel, false);
    assert.deepEqual(byId.get('codex-api-key')?.models, []);
    // Where no list was ever read, one line says so, and the turn goes on the host's own default.
    assert.equal(byId.get('codex')?.modelsUnread, null);
    assert.equal(byId.get('codex-api-key')?.modelsUnread, "OpenAI's models could not be read, so the turn uses its own default model.");
    assert.equal(byId.get('claude')?.modelsUnread, null);
    unread.set('codex-api-key', 'waiting');
    assert.equal(new Map((await setup.status()).hosts.map((host) => [host.id, host])).get('codex-api-key')?.modelsUnread, "OpenAI's models are being read; until then the turn uses its own default model.");
    assert.deepEqual(byId.get('deepseek')?.models.map((model) => [model.id, model.efforts.join(',')]), [['deepseek-v4-pro', 'low,high,max'], ['deepseek-flash', 'low,high,max']]);
    assert.deepEqual(byId.get('gemini')?.models, []);
    assert.equal(byId.get('gemini')?.typedModel, false);
    assert.deepEqual(refreshed, [['codex'], ['codex']], 'each listing, with no key stored');
    await setup.setProviderKey('codex-api-key', 'sk-o');
    byId = new Map((await setup.status()).hosts.map((host) => [host.id, host]));
    assert.deepEqual(refreshed.at(-1), ['codex', 'codex-api-key']);
    // With nothing kept the hosts still list, and Codex offers no model of its own until it has been asked.
    assert.deepEqual((await setupOn(b).status()).hosts.find((host) => host.id === 'codex')?.models, []);
});

// ---- Claude Code's version and its floor ---------------------------------------------------------------------------

/** A bench whose Claude Code says of its version what `say` writes, read again at each start. */
function claudeSaying(t: { after(fn: () => void): void }, programs: string[] = ['claude', 'codex']) {
    // The file is named before the bench exists, since the programs' environment is fixed when the bench is made.
    const file = join(tmpdir(), `jason-claude-version-${process.pid}-${Math.random().toString(36).slice(2)}.json`);
    t.after(() => rmSync(file, { force: true }));
    const b = bench(t, { programs, env: { STUB_CLAUDE_VERSION_FILE: file } });
    const say = (answer: { stdout?: string; stderr?: string; exit?: number }) => writeFileSync(file, JSON.stringify(answer));
    return { b, say };
}

test('a Claude Code below the floor is listed and marked with the version found, the floor and the cure; a key provider on it too, Codex never', async (t) => {
    const { b, say } = claudeSaying(t);
    say({ stdout: '2.1.200 (Claude Code)\n' });
    const setup = setupOn(b);
    await setup.setProviderKey('claude-api-key', 'sk-a');
    await setup.setProviderKey('codex-api-key', 'sk-o');
    const byId = new Map((await setup.status()).hosts.map((host) => [host.id, host]));
    const sentence = floorSentence('2.1.200');
    assert.ok(sentence);
    assert.deepEqual(byId.get('claude'), { id: 'claude', label: 'Claude Code', kind: 'program', found: true, version: '2.1.200', drivesChat: true, install: null, belowFloor: sentence, fullAccess: fullAccessSentence('2.1.200'), autoEdit: null, runs: { window: { full_access: { mode: 'ask_first', why: fullAccessSentence('2.1.200') } }, tab: { full_access: { mode: 'ask_first', why: tabFullAccess } } }, models: claudeCodeModels('2.1.200'), typedModel: true, modelsUnread: null, ownServers: [], runsRoles: null });
    assert.equal(byId.get('claude-api-key')?.belowFloor, sentence, 'a key provider runs on that same claude');
    assert.equal(byId.get('deepseek')?.belowFloor, sentence);
    assert.equal(byId.get('codex')?.belowFloor, null, 'only Claude Code has a floor');
    assert.equal(byId.get('codex-api-key')?.belowFloor, null);
});

test('Claude Code’s version is read again at each listing, never remembered, so an update shows without a restart', async (t) => {
    const { b, say } = claudeSaying(t);
    const setup = setupOn(b);
    say({ stdout: 'Claude Code 2.1.200\n' });
    assert.equal((await setup.status()).hosts.find((host) => host.id === 'claude')?.belowFloor, floorSentence('2.1.200'));
    say({ stdout: 'Claude Code 2.1.257\n' });
    const claude = (await setup.status()).hosts.find((host) => host.id === 'claude');
    assert.equal(claude?.version, '2.1.257');
    assert.equal(claude?.belowFloor, null, 'exactly at the floor');
    assert.equal(b.record().filter((entry) => entry.name === 'claude' && entry.args.join(' ') === '--version').length, 2);
});

test('a Claude Code whose version cannot be read is held as below the floor: a non-zero exit, a timeout, or no number', async (t) => {
    const { b, say } = claudeSaying(t);
    const setup = setupOn(b);
    const unreadable = floorSentence(null);
    say({ stdout: '2.1.300 (Claude Code)\n', stderr: 'it failed\n', exit: 1 });
    const failed = (await setup.status()).hosts.find((host) => host.id === 'claude');
    assert.equal(failed?.version, null);
    assert.equal(failed?.belowFloor, unreadable);
    say({ stdout: 'Claude Code, a new build\n' });
    assert.equal((await setup.status()).hosts.find((host) => host.id === 'claude')?.belowFloor, unreadable);
    say({ stderr: 'warning: slow start\nversion 2.1.300 ready\n' });
    assert.equal((await setup.status()).hosts.find((host) => host.id === 'claude')?.belowFloor, null, 'standard error is read too');

    // The runner reports a program stopped at its time limit as -1, and the limit it is given is the generous one.
    const asked: { name: string; args: readonly string[]; timeoutMs: number | undefined }[] = [];
    const timedOut = new Setup({
        paths: b.paths,
        cli: b.cli,
        programs: {
            find: (name) => b.programs.find(name),
            run: async (name, args, options) => {
                asked.push({ name, args, timeoutMs: options?.timeoutMs });
                return name === 'claude' ? { exit: -1, stdout: '', stderr: 'timed out' } : b.programs.run(name, args, options);
            },
        },
        keys: new ProviderKeys(b.paths.providerKeys, fakeBox),
        environment: {},
        log: memoryLog(),
        bundleVersion: '0.34.0-rc.1',
        runtimeVersion: () => null,
        settings: () => ({ feed: null, skillsDeployedFor: null, theme: null }),
    });
    assert.equal((await timedOut.status()).hosts.find((host) => host.id === 'claude')?.belowFloor, unreadable);
    assert.deepEqual(asked.filter((each) => each.name === 'claude'), [{ name: 'claude', args: ['--version'], timeoutMs: versionLimitMs }]);
});

test('two listings and a chat turn that arrive while Claude Code’s version is read wait on that one read; the next listing reads again', async (t) => {
    const b = bench(t, { programs: ['claude', 'codex'] });
    const releases: ((result: RunResult) => void)[] = [];
    // The desktop's own runner, as main.ts builds it, around a stand-in that holds Claude Code's version read.
    const programs = sharingVersionReads({
        find: (name: Parameters<Programs['find']>[0]) => b.programs.find(name),
        run: (name: Parameters<Programs['run']>[0], args: readonly string[], options?: Parameters<Programs['run']>[2]) =>
            name === 'claude' && args[0] === '--version' ? new Promise<RunResult>((resolve) => releases.push(resolve)) : b.programs.run(name, args, options),
    });
    const setup = new Setup({ ...setupDeps(b), programs });
    const said: ChatEvent[] = [];
    const chat = new Chat({
        paths: b.paths,
        env: b.env,
        bundle: join(b.root, 'bin'),
        dataNamed: true,
        bundleVersion: null,
        cli: b.cli,
        programs,
        keyFor: () => undefined,
        fence: () => readFence(join(import.meta.dirname, '..')),
        emit: (_chatId, event) => void said.push(event),
    });
    t.after(() => chat.closeAll());
    const listings = [setup.status(), setup.status()];
    const sending = chat.send((await chat.create()).id, 'Which campaigns wait?', { host: 'claude' });
    await new Promise((resolve) => setTimeout(resolve, 50));
    assert.equal(releases.length, 1, 'one read for the two listings and the turn');
    releases[0]!({ exit: 0, stdout: '2.1.200 (Claude Code)\n', stderr: '' });
    for (const listed of await Promise.all(listings)) assert.equal(listed.hosts.find((host) => host.id === 'claude')?.belowFloor, floorSentence('2.1.200'));
    await sending;
    assert.deepEqual(said, [{ kind: 'done', error: floorSentence('2.1.200') }]);
    const again = setup.status();
    await new Promise((resolve) => setTimeout(resolve, 50));
    assert.equal(releases.length, 2, 'nothing is remembered once the read answered');
    releases[1]!({ exit: 0, stdout: '2.1.300 (Claude Code)\n', stderr: '' });
    assert.equal((await again).hosts.find((host) => host.id === 'claude')?.belowFloor, null);
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

test('installing or updating the Reply CLI on a click has the runtime read its plugins again; no other program does', async (t) => {
    const reloads = (b: Bench) => b.record().filter((entry) => entry.name === 'jason' && entry.args.join(' ') === 'plugin reload').length;
    const b = bench(t, { programs: ['npm', 'reply'], env: { STUB_REPLY_VERSION: '0.22.0' } });
    await setupOn(b).installProgram('reply');
    assert.deepEqual(
        b.record().filter((entry) => (entry.name === 'npm' && entry.args[0] === 'install') || (entry.name === 'jason' && entry.args[0] === 'plugin')).map((entry) => [entry.name, ...entry.args].join(' ')),
        ['npm install -g reply-cli', 'jason plugin reload'],
        'the reload after the install, once',
    );
    const others = bench(t, { programs: ['npm'] });
    await setupOn(others).installProgram('codex');
    await setupOn(others).installProgram('claude');
    assert.equal(reloads(others), 0);

    // npm that failed put nothing there to read again.
    const failed = bench(t, { programs: ['npm', 'reply'], env: { STUB_REPLY_VERSION: '0.22.0' } });
    const programs: Programs = {
        find: (name) => failed.programs.find(name),
        run: (name, args, options) => (name === 'npm' ? Promise.resolve({ exit: 1, stdout: '', stderr: 'npm error code EACCES' }) : failed.programs.run(name, args, options)),
    };
    await assert.rejects(new Setup({ ...setupDeps(failed, memoryLog(), { feed: null, skillsDeployedFor: null, theme: null }), programs }).installProgram('reply'), Refused);
    assert.equal(reloads(failed), 0);
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

test('on Linux a key is neither kept nor read where the store is basic_text or not known, since that encryption is a fixed password', (t) => {
    const b = bench(t);
    const file = join(b.root, 'k');
    new ProviderKeys(file, { ...fakeBox, getSelectedStorageBackend: () => 'gnome_libsecret' }, 'linux').set('moonshot', 'sk-planted-before');
    assert.equal(new ProviderKeys(file, { ...fakeBox, getSelectedStorageBackend: undefined }, 'linux').keyFor('moonshot'), undefined, 'a box that names no backend on Linux is taken as none');
    for (const backend of ['basic_text', 'unknown'] as const) {
        const keys = new ProviderKeys(file, { ...fakeBox, getSelectedStorageBackend: () => backend }, 'linux');
        assert.throws(
            () => keys.set('deepseek', 'sk-planted'),
            (error: unknown) => error instanceof Refused && error.message === 'This session offers no keyring (GNOME Keyring or KWallet) to the desktop, so the key was not kept. Start one in your session and enter the key again.',
            backend,
        );
        assert.equal(keys.keyFor('moonshot'), undefined, `${backend}: a key kept before is not read back`);
        assert.deepEqual(keys.stored(), [], `${backend}: and is not shown as kept`);
    }
    for (const backend of ['gnome_libsecret', 'kwallet', 'kwallet5', 'kwallet6'] as const) {
        const keys = new ProviderKeys(file, { ...fakeBox, getSelectedStorageBackend: () => backend }, 'linux');
        assert.equal(keys.keyFor('moonshot'), 'sk-planted-before', backend);
    }
    const elsewhere = new ProviderKeys(file, { ...fakeBox, getSelectedStorageBackend: () => 'basic_text' }, 'darwin');
    assert.equal(elsewhere.keyFor('moonshot'), 'sk-planted-before', 'the backend is Linux’s question only');
});

test('every host that drives the chat says why Full access cannot be chosen on it, Codex none, and with the switch off each says it is not offered', async (t) => {
    const { b, say } = claudeSaying(t);
    say({ stdout: '2.1.290 (Claude Code)\n' });
    const on = new Map((await setupOn(b).status()).hosts.map((host) => [host.id, host.fullAccess]));
    assert.equal(on.get('claude'), fullAccessSentence('2.1.290'));
    assert.equal(on.get('codex'), null, 'Codex takes Full access');
    assert.equal(on.get('gemini'), null, 'a host that does not drive the chat offers no mode');
    const off = new Map((await new Setup({ ...setupDeps(b), fullAccessOffered: false }).status()).hosts.map((host) => [host.id, host.fullAccess]));
    assert.equal(off.get('claude'), fullAccessNotYet);
    assert.equal(off.get('codex'), fullAccessNotYet);
});

test('every host that drives the chat says whether Auto-edit can be chosen on it: Codex cannot ask before it acts, and Claude Code at any version can', async (t) => {
    const { b, say } = claudeSaying(t);
    say({ stdout: '2.1.200 (Claude Code)\n' });
    const hosts = (await setupOn(b).status()).hosts;
    const on = new Map(hosts.map((host) => [host.id, host.autoEdit]));
    assert.equal(on.get('claude'), null, 'below Full access\'s floor Auto-edit is still offered: it needs no hook');
    assert.equal(on.get('codex'), codexAutoEdit);
    assert.equal(on.get('gemini'), null, 'a host that does not drive the chat offers no mode');
    for (const provider of hosts.filter((host) => host.kind === 'provider')) {
        assert.equal(provider.autoEdit, provider.id.startsWith('codex') ? codexAutoEdit : null, provider.id);
    }
    const off = new Map((await new Setup({ ...setupDeps(b), fullAccessOffered: false }).status()).hosts.map((host) => [host.id, host.autoEdit]));
    assert.equal(off.get('claude'), null, 'Full access\'s switch is not Auto-edit\'s');
});

test('every host hands what a turn on it runs in, by the mode a chat keeps, from the window and from a browser tab, each with the desktop\'s sentence why, so the page draws the mode the next turn runs in', async (t) => {
    const { b, say } = claudeSaying(t);
    const ask = (why: string | null) => ({ mode: 'ask_first', why });
    say({ stdout: '2.1.290 (Claude Code)\n' });
    const listing = setupOn(b);
    assert.equal(listing.handedClaudeVersion(), undefined, 'before a listing, no version was handed');
    const below = new Map((await listing.status()).hosts.map((host) => [host.id, host.runs]));
    assert.equal(listing.handedClaudeVersion(), '2.1.290', 'the version the listing read, which a turn holds its own read to');
    assert.deepEqual(below.get('claude'), { window: { full_access: ask(fullAccessSentence('2.1.290')) }, tab: { full_access: ask(tabFullAccess) } });
    assert.deepEqual(below.get('deepseek'), below.get('claude'), 'a key provider on Claude Code runs as Claude Code does');
    assert.deepEqual(below.get('codex'), { window: { auto_edit: ask(codexAutoEdit) }, tab: { full_access: ask(tabFullAccess), auto_edit: ask(codexAutoEdit) } }, 'a Full access Codex turn from the window runs in it');
    assert.deepEqual(below.get('codex-api-key'), below.get('codex'));
    assert.deepEqual(below.get('gemini'), { window: {}, tab: {} }, 'a host that does not drive the chat runs no turn of it');
    say({ stdout: '2.1.322 (Claude Code)\n' });
    const at = new Map((await setupOn(b).status()).hosts.map((host) => [host.id, host.runs]));
    assert.deepEqual(at.get('claude'), { window: {}, tab: { full_access: ask(tabFullAccess) } }, 'at the floor a Full access turn from the window runs in it');
    say({ stdout: 'not a version\n' });
    const unread = setupOn(b);
    await unread.status();
    assert.equal(unread.handedClaudeVersion(), null, 'a version that could not be read is handed as none');
    say({ stdout: '2.1.322 (Claude Code)\n' });
    const off = new Map((await new Setup({ ...setupDeps(b), fullAccessOffered: false }).status()).hosts.map((host) => [host.id, host.runs]));
    assert.deepEqual(off.get('claude'), { window: { full_access: ask(fullAccessNotYet) }, tab: { full_access: ask(tabFullAccess) } });
    assert.deepEqual(off.get('codex')?.window.full_access, ask(fullAccessNotYet), 'Codex, with the switch off, as a turn says it');
});

test('a listing whose Claude Code stands across the floor of Full access from the last listing\'s asks every page to read the hosts again, whichever page asked', async (t) => {
    const { b, say } = claudeSaying(t);
    say({ stdout: '2.1.290 (Claude Code)\n' });
    const listing = setupOn(b);
    let changed = 0;
    listing.onChanged(() => void changed++);
    await listing.status();
    assert.equal(changed, 0, 'a first listing crosses nothing');
    say({ stdout: '2.1.322 (Claude Code)\n' });
    await listing.status();
    assert.equal(changed, 1, 'up across the floor: a tab\'s listing tells the window too');
    await listing.status();
    assert.equal(changed, 1, 'the same side again: nothing');
    say({ stdout: 'not a version\n' });
    await listing.status();
    assert.equal(changed, 2, 'down to a version that could not be read');
    say({ stdout: '2.1.300 (Claude Code)\n' });
    await listing.status();
    assert.equal(changed, 2, 'below the floor still: nothing');
});

test('a sign-in whose chat folder cannot be put right keeps its own result: the folder\'s code is logged, never its sentence said', async (t) => {
    const b = bench(t, { programs: ['reply'] });
    const log = memoryLog();
    const setup = new Setup({ ...setupDeps(b, log), ownChatFolder: () => {
        throw new AgentFolderUnowned('EBUSY');
    } });
    await setup.status();
    await setup.connectReplyWithKey('planted-reply-key');
    assert.ok(log.lines.some((line) => line.name === 'agent_folder_unowned' && line.fields.Code === 'EBUSY'));
    assert.ok(!existsSync(join(b.paths.skillsRecords, 'reply.json')), 'nothing was recorded');
});

// ---- The person's own MCP servers ----------------------------------------------------------------------------------

test('Claude Code and Codex found here name their own servers; every other host, a key provider and a host not found name none', async (t) => {
    const b = bench(t, { programs: ['claude', 'gemini'] });
    const home = join(b.root, 'person-home');
    mkdirSync(join(home, '.codex'), { recursive: true });
    writeFileSync(join(home, '.claude.json'), claudeWithSecrets);
    writeFileSync(join(home, '.codex', 'config.toml'), codexWithSecrets);
    const log = memoryLog();
    const setup = new Setup({ ...setupDeps(b, log), ownServers: { env: {}, home } });
    const state = await setup.status();
    const byId = new Map(state.hosts.map((host) => [host.id, host]));
    assert.deepEqual(byId.get('claude')?.ownServers, ['alpha', 'beta', 'gamma']);
    assert.deepEqual(byId.get('codex')?.ownServers, [], 'Codex is not found here, whatever its file holds');
    for (const host of state.hosts.filter((each) => each.id !== 'claude')) assert.deepEqual(host.ownServers, [], host.id);
    // What the page is handed and what the log holds carry none of the planted values, and the log no name.
    const handed = JSON.stringify(state);
    const logged = JSON.stringify(log.lines);
    for (const secret of [...plantedSecrets, 'planted', 'someone@example.invalid', 'project-local-server']) {
        assert.ok(!handed.includes(secret), secret);
        assert.ok(!logged.includes(secret), secret);
    }
    assert.ok(!/alpha|beta|gamma/.test(logged), 'a name is never logged');
});

test('a moved CLAUDE_CONFIG_DIR and CODEX_HOME are read where they point; a file that cannot be read is logged by program and kind only', async (t) => {
    const b = bench(t, { programs: ['claude', 'codex'] });
    const home = join(b.root, 'person-home');
    const moved = join(b.root, 'moved-claude');
    const codexHome = join(b.root, 'moved-codex');
    mkdirSync(moved, { recursive: true });
    mkdirSync(codexHome, { recursive: true });
    mkdirSync(home, { recursive: true });
    writeFileSync(join(home, '.claude.json'), JSON.stringify({ mcpServers: { not_this_one: {} } }));
    writeFileSync(join(moved, '.claude.json'), JSON.stringify({ mcpServers: { moved_server: { env: { K: 'sk-planted-env-0123456789abcdef' } } } }));
    writeFileSync(join(codexHome, 'config.toml'), '[mcp_servers.half_written\ncommand = "npx-planted-command"\n');
    const log = memoryLog();
    const setup = new Setup({ ...setupDeps(b, log), ownServers: { env: { CLAUDE_CONFIG_DIR: moved, CODEX_HOME: codexHome }, home } });
    const byId = new Map((await setup.status()).hosts.map((host) => [host.id, host]));
    assert.deepEqual(byId.get('claude')?.ownServers, ['moved_server']);
    assert.deepEqual(byId.get('codex')?.ownServers, []);
    assert.deepEqual(
        log.lines.filter((line) => line.name === 'own_servers_unread'),
        [{ level: 'info', name: 'own_servers_unread', fields: { Program: 'codex', Kind: 'malformed' } }],
    );
    assert.ok(!JSON.stringify(log.lines).includes('half_written'));
});

test('a file nested ten thousand deep never fails the listing: setup.status answers, every other row is there, and no line is drawn', async (t) => {
    const b = bench(t, { programs: ['claude', 'codex'] });
    const home = join(b.root, 'person-home');
    mkdirSync(join(home, '.codex'), { recursive: true });
    const depth = 10_000;
    writeFileSync(join(home, '.claude.json'), `{"mcpServers": {"deep": ${'['.repeat(depth)}${']'.repeat(depth)}}}`);
    writeFileSync(join(home, '.codex', 'config.toml'), `[mcp_servers.deep]\nargs = ${'['.repeat(depth)}${']'.repeat(depth)}\n`);
    const log = memoryLog();
    const state = await new Setup({ ...setupDeps(b, log), ownServers: { env: {}, home } }).status();
    const byId = new Map(state.hosts.map((host) => [host.id, host]));
    assert.equal(byId.get('claude')?.found, true);
    assert.equal(byId.get('codex')?.found, true);
    for (const host of state.hosts) assert.deepEqual(host.ownServers, [], host.id);
    assert.deepEqual(
        log.lines.filter((line) => line.name === 'own_servers_unread').map((line) => line.fields),
        [
            { Program: 'claude', Kind: 'malformed' },
            { Program: 'codex', Kind: 'malformed' },
        ],
    );
});

test('an unread file is logged once while its kind stays, again when its kind changes, and its line goes at once when it is gone', async (t) => {
    const b = bench(t, { programs: ['claude'] });
    const home = join(b.root, 'person-home');
    const file = join(home, '.claude.json');
    mkdirSync(home, { recursive: true });
    writeFileSync(file, '{"mcpServers": ');
    const log = memoryLog();
    const setup = new Setup({ ...setupDeps(b, log), ownServers: { env: {}, home } });
    const kinds = () => log.lines.filter((line) => line.name === 'own_servers_unread').map((line) => line.fields['Kind']);
    const claude = async () => (await setup.status()).hosts.find((host) => host.id === 'claude')?.ownServers;
    for (let call = 0; call < 3; call++) assert.deepEqual(await claude(), []);
    assert.deepEqual(kinds(), ['malformed'], 'once for an unchanged file');
    rmSync(file);
    mkdirSync(file);
    for (let call = 0; call < 3; call++) assert.deepEqual(await claude(), []);
    assert.deepEqual(kinds(), ['malformed', 'unreadable'], 'again when the kind changed');
    rmSync(file, { recursive: true });
    writeFileSync(file, JSON.stringify({ mcpServers: { now_read: {} } }));
    assert.deepEqual(await claude(), ['now_read']);
    rmSync(file);
    assert.deepEqual(await claude(), [], 'a file gone lists nothing at once');
    writeFileSync(file, '{"mcpServers": ');
    assert.deepEqual(await claude(), []);
    assert.deepEqual(kinds(), ['malformed', 'unreadable', 'malformed'], 'a kind after it listed is a change');
});

test('without the environment and home to read under, no file is read and no host names a server', async (t) => {
    const b = bench(t, { programs: ['claude', 'codex'] });
    const { hosts } = await setupOn(b).status();
    for (const host of hosts) assert.deepEqual(host.ownServers, [], host.id);
});

// ---- The Reply CLI updated at the start -------------------------------------------------------------------------------

interface StartUpdate {
    b: Bench;
    setup: Setup;
    log: ReturnType<typeof memoryLog>;
    kept: () => DesktopSettings;
    /** Every npm and jason run and every version read, in the order they were asked. */
    ran: string[];
    /** The mark in the desktop's own settings file as npm started. */
    markAtNpm: (string | undefined)[];
    /** The time limit each npm run was given. */
    limits: (number | undefined)[];
    /** How often the page was asked to read the setup again. */
    told: () => number;
}

/**
 * A start on a bench with a stand-in `reply` and npm: npm puts `after` in place of `before`, or ends as `npm` says, and the
 * runtime answers `runtime` as the version it found. No npm is ever run.
 */
function startUpdate(
    t: Parameters<typeof bench>[0],
    options: {
        before?: string | null;
        after?: string | null;
        npm?: RunResult | Promise<RunResult>;
        /** npm puts `after` in place however it ended. */
        putsAnyway?: boolean;
        programs?: readonly string[];
        runtime?: string | null;
        reload?: number;
        /** What `jason plugin list` answers before a reload; the Reply plugin, unavailable for its CLI, where not given. */
        listed?: RunResult;
        mark?: string;
        environment?: Record<string, string | undefined>;
        installation?: unknown;
        /** Every write of the desktop's settings fails, or only one that takes the mark away. */
        unwritable?: true | 'clearing';
    } = {},
): StartUpdate {
    const b = bench(t, { programs: [...(options.programs ?? ['reply', 'npm'])] });
    if (options.installation !== undefined) writeInstallationSettings(b, options.installation);
    const log = memoryLog();
    writeSettings(b.paths.settings, { feed: null, skillsDeployedFor: 'kept', theme: 'dark', ...(options.mark === undefined ? {} : { replyUpdateTried: options.mark }) });
    let kept = readSettings(b.paths.settings);
    let version = options.before === undefined ? '0.22.0' : options.before;
    let reloaded = false;
    const ran: string[] = [];
    const markAtNpm: (string | undefined)[] = [];
    const limits: (number | undefined)[] = [];
    const answer = (stdout: string): RunResult => ({ exit: 0, stdout, stderr: '' });
    const programs: Programs = {
        find: (name) => b.programs.find(name),
        run: async (name, args, runOptions) => {
            if (name === 'npm') {
                limits.push(runOptions?.timeoutMs);
                ran.push(['npm', ...args].join(' '));
                markAtNpm.push(readSettings(b.paths.settings).replyUpdateTried);
                const result = await (options.npm ?? answer('added 1 package'));
                if (result.exit === 0 || options.putsAnyway) version = options.after === undefined ? '0.24.0' : options.after;
                return result;
            }
            if (name === 'reply' && args[0] === '--version') {
                ran.push(`reply ${version ?? 'unread'}`);
                return version === null ? { exit: 1, stdout: '', stderr: '' } : answer(`reply-cli ${version}`);
            }
            return answer('{}');
        },
    };
    const cli = {
        run: (args: readonly string[]): Promise<RunResult> => {
            const verb = args.join(' ');
            if (verb === 'plugin reload') {
                ran.push(`jason ${verb}`);
                reloaded = (options.reload ?? 0) === 0;
                return Promise.resolve({ exit: options.reload ?? 0, stdout: '{}', stderr: '' });
            }
            if (verb === 'plugin list') {
                ran.push(`jason ${verb}`);
                if (!reloaded && options.listed !== undefined) return Promise.resolve(options.listed);
                const found = options.runtime === undefined ? version : options.runtime;
                return Promise.resolve(answer(pluginList([{ id: 'reply', status: reloaded ? 'valid' : 'unavailable', program: 'reply', version: found }])));
            }
            return b.cli.run(args);
        },
    };
    const setup = new Setup({
        ...setupDeps(b, log),
        cli,
        programs,
        environment: options.environment ?? {},
        settings: () => kept,
        saveSettings: (next) => {
            if (options.unwritable === true || (options.unwritable === 'clearing' && next.replyUpdateTried === undefined)) throw new Error('EACCES');
            kept = next;
            writeSettings(b.paths.settings, next);
        },
    });
    let told = 0;
    setup.onChanged(() => told++);
    return { b, setup, log, kept: () => readSettings(b.paths.settings), ran, markAtNpm, limits, told: () => told };
}

/** What `jason plugin list` prints of plugins that each request one program, or none. */
function pluginList(plugins: { id: string; status: string; program: string | null; version?: string | null }[]): string {
    return JSON.stringify({
        plugins: plugins.map(({ id, status, program, version }) => ({
            id,
            status,
            capabilities: { exec: program === null ? null : { requested: [{ name: program, path: null, version: version ?? null, min_version: null }], granted: [program] } },
        })),
    });
}

const npmRuns = (s: StartUpdate) => s.ran.filter((each) => each.startsWith('npm'));
const line = async (s: StartUpdate) => (await s.setup.status()).reply.startUpdate;
/**
 * Where the start's check of the Reply CLI stands as the page reads it, and how often the page was told of a change.
 */
const checked = async (s: StartUpdate) => ({ check: (await s.setup.status()).reply.startCheck, told: s.told() });
const ended = { check: 'ended', told: 1 };
const installLine = 'npm install -g reply-cli';

test('the start updates a Reply CLI below the minimum with the click’s own npm line, reads its version again and has the runtime read its plugins again; its check is not ended from the start until then', async (t) => {
    const s = startUpdate(t);
    let changed = 0;
    s.setup.onChanged(() => changed++);
    assert.equal(await s.setup.updateReplyAtStart(), 'updated');
    assert.deepEqual(s.ran, ['reply 0.22.0', 'jason plugin list', installLine, 'reply 0.24.0', 'jason plugin reload', 'jason plugin list']);
    assert.equal(changed, 1, 'the page is asked to read the card again');
    assert.deepEqual(s.limits, [installLimit], 'npm is stopped at the install’s own time limit');
    assert.equal(await line(s), 'Jason updated the Reply CLI to 0.24.0 when it started.');
    assert.deepEqual(s.log.lines.filter((each) => each.name === 'reply_start_update').map((each) => each.fields), [{ Outcome: 'updated' }]);
    assert.deepEqual(await checked(s), ended);

    // Once a start, whatever came of it.
    assert.equal(await s.setup.updateReplyAtStart(), 'settled');
    assert.equal(npmRuns(s).length, 1);
    assert.deepEqual(await checked(s), ended, 'nothing more to tell');

    // The page reads the check as not ended from the desktop's start, before anything is decided, and while npm runs.
    let finish!: (result: RunResult) => void;
    const slow = startUpdate(t, { npm: new Promise<RunResult>((resolve) => (finish = resolve)) });
    assert.deepEqual(await checked(slow), { check: 'pending', told: 0 });
    const updating = slow.setup.updateReplyAtStart();
    for (let waited = 0; npmRuns(slow).length === 0; waited++) {
        assert.ok(waited < 400, 'npm was asked');
        await new Promise((resolve) => setTimeout(resolve, 5));
    }
    assert.deepEqual(await checked(slow), { check: 'pending', told: 0 });
    finish({ exit: 0, stdout: 'added 1 package', stderr: '' });
    assert.equal(await updating, 'updated');
    assert.deepEqual(await checked(slow), ended);
});

test('the mark is in the desktop’s settings before npm starts, beside what was there, and is taken away once the update succeeded', async (t) => {
    const s = startUpdate(t);
    await s.setup.updateReplyAtStart();
    assert.deepEqual(s.markAtNpm, [replyMinimumVersion]);
    assert.deepEqual(s.kept(), { feed: null, skillsDeployedFor: 'kept', theme: 'dark' });

    const failed = startUpdate(t, { npm: { exit: 1, stdout: '', stderr: 'npm error code EACCES' } });
    await failed.setup.updateReplyAtStart();
    assert.deepEqual(failed.kept(), { feed: null, skillsDeployedFor: 'kept', theme: 'dark', replyUpdateTried: replyMinimumVersion });
    assert.deepEqual(await checked(failed), ended, 'a failed update ends the check');

    // A mark that is not a version is none.
    for (const bad of ['"soon"', '1', 'null', '"0.23"']) assert.equal(parseSettings(`{"reply_update_tried": ${bad}}`).replyUpdateTried, undefined, bad);
});

test('one try for each minimum, whatever came of it: the same one runs nothing again, a higher one tries once again', async (t) => {
    const same = startUpdate(t, { mark: replyMinimumVersion });
    assert.equal(await same.setup.updateReplyAtStart(), 'tried');
    assert.deepEqual(npmRuns(same), []);
    assert.deepEqual(await checked(same), ended);
    const higher = startUpdate(t, { mark: '0.23.0' });
    assert.equal(await higher.setup.updateReplyAtStart(), 'updated');
    assert.deepEqual(higher.markAtNpm, [replyMinimumVersion]);

    // Compared as versions, never as text: a mark above the minimum has been tried, and 0.9.0 is below 0.23.2.
    for (const [mark, outcome] of [['0.100.0', 'tried'], ['1.0.0', 'tried'], ['0.9.0', 'updated']] as const) {
        assert.equal(await startUpdate(t, { mark }).setup.updateReplyAtStart(), outcome, mark);
    }

    // The click is never held back by the mark.
    await same.setup.installProgram('reply');
    assert.deepEqual(npmRuns(same), [installLine]);
});

test('settings that cannot be written run nothing, and the log says so by the line’s name', async (t) => {
    const s = startUpdate(t, { unwritable: true });
    assert.equal(await s.setup.updateReplyAtStart(), 'unmarked');
    assert.deepEqual(npmRuns(s), []);
    assert.deepEqual(s.log.lines.filter((each) => each.name === 'reply_start_update_unmarked').map((each) => each.level), ['warn']);
    assert.equal(await line(s), undefined);
    assert.deepEqual(await checked(s), ended);
});

test('a write of the desktop’s settings that failed is not held as written, so it burns no try', (t) => {
    const b = bench(t);
    const before: DesktopSettings = { feed: null, skillsDeployedFor: 'kept', theme: null };
    let held = before;
    const hold = (next: DesktopSettings) => (held = next);
    // A file where the settings' folder belongs.
    writeFileSync(join(b.root, 'a-file'), '');
    assert.throws(() => saveSettings(join(b.root, 'a-file', 'settings.json'), { ...before, replyUpdateTried: replyMinimumVersion }, hold));
    assert.equal(held, before);
    saveSettings(b.paths.settings, { ...before, replyUpdateTried: replyMinimumVersion }, hold);
    assert.equal(held.replyUpdateTried, replyMinimumVersion);
    assert.deepEqual(readSettings(b.paths.settings), held);

    // Both of the desktop's own places that save them for the set-up and the start do it by this.
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    assert.equal(main.split('saveSettings: (next) => saveSettings(paths.settings, next, (kept) => (settings = kept)),').length - 1, 2);
    assert.doesNotMatch(main, /settings = next;/);
});

test('a mark that could not be taken away after a success is said as that in the log, and the update is still said as made', async (t) => {
    const s = startUpdate(t, { unwritable: 'clearing' });
    assert.equal(await s.setup.updateReplyAtStart(), 'updated');
    assert.equal(npmRuns(s).length, 1);
    assert.equal(await line(s), 'Jason updated the Reply CLI to 0.24.0 when it started.');
    assert.deepEqual(s.log.lines.filter((each) => each.level === 'warn').map((each) => each.name), ['reply_start_update_mark_left']);
    assert.equal(s.kept().replyUpdateTried, replyMinimumVersion);
});

test('nothing is run and nothing is marked for a CLI at the minimum, one whose version was not read, a missing one, or with no npm; the check ends all the same', async (t) => {
    for (const [outcome, options] of [
        ['current', { before: replyMinimumVersion }],
        ['version_unread', { before: null }],
        ['not_found', { programs: ['npm'] }],
        ['no_npm', { programs: ['reply'] }],
    ] as const) {
        const s = startUpdate(t, options);
        assert.equal(await s.setup.updateReplyAtStart(), outcome);
        assert.deepEqual(npmRuns(s), [], outcome);
        assert.equal(s.kept().replyUpdateTried, undefined, outcome);
        assert.equal(s.ran.includes('jason plugin reload'), false, outcome);
        assert.equal(await line(s), undefined, outcome);
        assert.deepEqual(await checked(s), ended, outcome);
    }

    // The check ended before the update, by a start that asked for none or by the bound on the machine set-up: it ends
    // once and is told once; an update that runs after it still runs, and its sentence is told to the page.
    const early = startUpdate(t);
    early.setup.endStartCheck();
    early.setup.endStartCheck();
    assert.deepEqual(await checked(early), ended);
    assert.equal(await early.setup.updateReplyAtStart(), 'updated');
    assert.equal(npmRuns(early).length, 1);
    assert.equal(await line(early), 'Jason updated the Reply CLI to 0.24.0 when it started.');
    assert.deepEqual(await checked(early), { check: 'ended', told: 2 });
    // An update after it that sets no sentence tells nothing more.
    const quiet = startUpdate(t, { before: replyMinimumVersion });
    quiet.setup.endStartCheck();
    assert.equal(await quiet.setup.updateReplyAtStart(), 'current');
    assert.deepEqual(await checked(quiet), ended);

    // An update that throws ends it too.
    const thrown = startUpdate(t, { npm: { then: (_done: unknown, fail: (error: Error) => void) => fail(new Error('npm could not be started')) } as unknown as Promise<RunResult> });
    await assert.rejects(thrown.setup.updateReplyAtStart(), /npm could not be started/);
    assert.deepEqual(await checked(thrown), ended);
});

test('npm never replaces the Reply CLI while a plugin that runs it can be at work: a valid one that requests it, or an unread list, runs nothing and uses up no try', async (t) => {
    const said = (stdout: string, exit = 0): RunResult => ({ exit, stdout, stderr: '' });
    const older = { id: 'reply', status: 'valid', program: 'reply', version: '0.22.0' };
    for (const [outcome, listed] of [
        // A Reply plugin of an earlier release, valid on the CLI that is below this one's minimum.
        ['in_use', said(pluginList([older]))],
        // Another plugin that requests reply, beside a Reply plugin that is not valid.
        ['in_use', said(pluginList([{ ...older, status: 'unavailable' }, { id: 'own-crm', status: 'valid', program: 'reply' }]))],
        ['plugins_unread', said('', 3)],
        ['plugins_unread', said(pluginList([]), 1)],
        ['plugins_unread', said('not JSON')],
        ['plugins_unread', said('{}')],
        ['plugins_unread', said('{"plugins":[{"id":"own","status":"valid","capabilities":{"exec":{"requested":"reply"}}}]}')],
    ] as const) {
        const s = startUpdate(t, { listed });
        assert.equal(await s.setup.updateReplyAtStart(), outcome, listed.stdout);
        assert.deepEqual(npmRuns(s), [], listed.stdout);
        assert.equal(s.kept().replyUpdateTried, undefined, listed.stdout);
        assert.equal(s.ran.includes('jason plugin reload'), false, listed.stdout);
        assert.equal(await line(s), undefined, listed.stdout);
        assert.deepEqual(s.log.lines.filter((each) => each.name === 'reply_start_update').map((each) => each.fields), [{ Outcome: outcome }]);
        assert.deepEqual(await checked(s), ended, listed.stdout);
    }
    // The ordinary case: the one plugin that requests it is not valid, and a valid one runs another program, or none.
    const ordinary = startUpdate(t, { listed: said(pluginList([{ ...older, status: 'unavailable' }, { id: 'slack', status: 'valid', program: null }, { id: 'own', status: 'valid', program: 'crm-cli' }])) });
    assert.equal(await ordinary.setup.updateReplyAtStart(), 'updated');

    assert.equal(replyAtWork(pluginList([])), false);
    assert.equal(replyAtWork(pluginList([{ ...older, program: 'REPLY' }])), true);
    assert.equal(replyAtWork('{"plugins":[null]}'), null);
    assert.equal(replyAtWork('{"plugins":[{"id":"own","status":"valid","capabilities":{"exec":{"requested":[{}]}}}]}'), null);
    assert.equal(replyAtWork('{"plugins":[{"id":"own","status":"valid"}]}'), null);
});

test('Update:CheckEnabled false in the settings file or in the desktop’s own environment runs nothing, and uses up no try', async (t) => {
    const off = { Update: { CheckEnabled: false } };
    for (const options of [{ installation: off }, { environment: { JASON_Update__CheckEnabled: 'false' } }]) {
        const s = startUpdate(t, options);
        assert.equal(await s.setup.updateReplyAtStart(), 'off');
        assert.deepEqual(s.ran, [], 'nothing is asked of any program');
        assert.equal(s.kept().replyUpdateTried, undefined);
        assert.deepEqual(await checked(s), ended);
    }
    // False in either place turns it off: what the other says does not turn it on again, in both directions.
    const fileOff = startUpdate(t, { installation: off, environment: { jason_update__checkenabled: 'TRUE' } });
    assert.equal(await fileOff.setup.updateReplyAtStart(), 'off');
    assert.deepEqual(fileOff.ran, []);
    const bothOn = startUpdate(t, { installation: { Update: { CheckEnabled: true } }, environment: { JASON_Update__CheckEnabled: 'true' } });
    assert.equal(await bothOn.setup.updateReplyAtStart(), 'updated');
    const turnedOff = startUpdate(t, { installation: { Update: { CheckEnabled: true } }, environment: { JASON_UPDATE__CHECKENABLED: 'False' } });
    assert.equal(await turnedOff.setup.updateReplyAtStart(), 'off');
});

test('the switch is read in the settings file the data directory’s layout names, and in no other', async (t) => {
    for (const [config, setup, outcome] of [[true, false, 'off'], [false, true, 'updated']] as const) {
        const s = startUpdate(t);
        writeFileSync(join(s.b.paths.data, 'layout.json'), '{"layout":2,"written_by":"0.50.0"}');
        for (const [folder, on] of [['config', config], ['setup', setup]] as const) {
            mkdirSync(join(s.b.paths.data, folder), { recursive: true });
            writeFileSync(join(s.b.paths.data, folder, 'settings.json'), JSON.stringify({ Update: { CheckEnabled: on } }));
        }
        assert.equal(dataLayout(s.b.paths.data).userSettingsFile, join(s.b.paths.data, 'setup', 'settings.json'));
        assert.equal(await s.setup.updateReplyAtStart(), outcome);
    }
});

test('a settings file the desktop cannot parse runs nothing and uses up no try, whatever the variable says; a missing one is on', async (t) => {
    for (const text of ['{ // a comment\n }', '{"Update": {"CheckEnabled": true},}', '[]', 'no', '﻿{}']) {
        for (const environment of [{}, { JASON_Update__CheckEnabled: 'true' }]) {
            const s = startUpdate(t, { installation: text, environment });
            assert.equal(await s.setup.updateReplyAtStart(), 'switch_unread', text);
            assert.deepEqual(s.ran, [], text);
            assert.equal(s.kept().replyUpdateTried, undefined, text);
            assert.deepEqual(await checked(s), ended, text);
        }
    }
    const missing = startUpdate(t);
    assert.equal(existsSync(dataLayout(missing.b.paths.data).userSettingsFile), false);
    assert.equal(await missing.setup.updateReplyAtStart(), 'updated');
});

test('the switch is read as the runtime binds it: any case, nested or joined by a colon, true and false as words; anything else is not read', (t) => {
    const file = (value: unknown) => updateSwitch({}, JSON.stringify(value));
    assert.equal(updateSwitch({}, null), 'on');
    assert.equal(file({}), 'on');
    assert.equal(file({ Update: {} }), 'on');
    assert.equal(file({ Update: { FeedUrl: 'https://example.test/m.json' }, Routes: { CheckEnabled: false } }), 'on');
    assert.equal(file({ update: { checkenabled: 'False' } }), 'off');
    assert.equal(file({ 'Update:CheckEnabled': false }), 'off');
    assert.equal(file({ Update: { CheckEnabled: true } }), 'on');
    for (const odd of [0, 'no', null, {}, []]) assert.equal(file({ Update: { CheckEnabled: odd } }), 'unread', JSON.stringify(odd));
    assert.equal(file({ Update: { CheckEnabled: true }, update: { checkEnabled: false } }), 'off');
    const variable = (name: string, value: string) => updateSwitch({ [name]: value }, null);
    assert.equal(variable('JASON_Update__CheckEnabled', ' false '), 'off');
    assert.equal(variable('JASON_Update:CheckEnabled', 'false'), 'off');
    assert.equal(variable('Update__CheckEnabled', 'false'), 'on');
    assert.equal(variable('JASON_Update__CheckEnabled', '0'), 'unread');
    assert.equal(updateSwitch({ JASON_Update__CheckEnabled: 'true', JASON_UPDATE__CHECKENABLED: 'false' }, null), 'off');
    // A value in one place that is not a switch runs nothing, whatever the other says.
    assert.equal(updateSwitch({ JASON_Update__CheckEnabled: 'true' }, '{"Update": {"CheckEnabled": "maybe"}}'), 'unread');
    assert.equal(updateSwitch({ JASON_Update__CheckEnabled: 'maybe' }, '{"Update": {"CheckEnabled": true}}'), 'unread');

    // A file that is there and cannot be read is not a missing one.
    const b = bench(t);
    mkdirSync(join(b.root, 'a-folder'));
    assert.equal(readUpdateSwitch({}, join(b.root, 'a-folder')), 'unread');
    assert.equal(readUpdateSwitch({}, join(b.root, 'nothing.json')), 'on');
});

test('a failed update says one line of npm’s, the one that names the error, on the card alone and never in the log, with Update as it was', async (t) => {
    const s = startUpdate(t, { npm: { exit: 243, stdout: 'a line of its output', stderr: '\n\u001b[31mnpm error\u001b[0m code EACCES planted-words.\nnpm error second line\n' } });
    assert.equal(await s.setup.updateReplyAtStart(), 'failed');
    const { reply } = await s.setup.status();
    assert.equal(reply.startUpdate, 'Jason tried to update the Reply CLI when it started and could not: npm error code EACCES planted-words.');
    assert.equal(reply.cli.install, 'reply');
    assert.notEqual(reply.belowMinimum, null);
    assert.equal(s.ran.includes('jason plugin reload'), false, 'npm changed nothing for the runtime to read again');
    assert.equal(JSON.stringify(s.log.lines).includes('planted-words'), false);

    const said = (stderr: string, stdout = '') => npmLine({ exit: 1, stdout, stderr });
    // First the line that names the error, in either stream, under npm's two spellings of it.
    assert.equal(said('npm warn deprecated thing\nnpm notice new version\n(node:41) Warning: old\nsomething else\nnpm error code EACCES\nnpm error second'), 'npm error code EACCES');
    assert.equal(said('something else', 'npm ERR! code ENOSPC'), 'npm ERR! code ENOSPC');
    assert.equal(said('npm errors are not one\nnpm error code E404'), 'npm error code E404');
    // Else the first line that is no warning, no notice and not Node's own, the errors before the output.
    assert.equal(said('npm WARN old lockfile\nnpm Notice x\n(node:1234) [DEP0040] DeprecationWarning: x\n(Use `node --trace-deprecation ...` to show where the warning was created)\nEPERM: not permitted', 'output'), 'EPERM: not permitted');
    assert.equal(said('npm warn one', 'npm notice two\nthe output'), 'the output');
    // Else the first line there is.
    assert.equal(said('\nnpm warn one\nnpm warn two', 'npm notice three'), 'npm warn one');
    assert.equal(said('', 'npm notice three'), 'npm notice three');
    assert.equal(npmLine({ exit: 1, stdout: '  \r\n only output\n', stderr: ' \n' }), 'only output');
    assert.equal(npmLine({ exit: 7, stdout: '', stderr: '' }), 'npm ended with code 7');
    assert.equal(npmLine({ exit: 1, stdout: '', stderr: 'left‮right\tand\u0007more' }), 'left right and more');
    assert.equal(npmLine({ exit: 1, stdout: '', stderr: 'x'.repeat(5000) }), `${'x'.repeat(200)}…`);
});

test('npm that ended badly or ran out of time and still put a CLI at the minimum in use is an update: the plugins are read again and the mark is taken away', async (t) => {
    for (const npm of [
        { exit: 1, stdout: '', stderr: 'npm error code EPERM' },
        { exit: -1, stdout: '', stderr: 'It was stopped at its time limit of 300000 ms.' },
    ]) {
        const s = startUpdate(t, { npm, putsAnyway: true });
        assert.equal(await s.setup.updateReplyAtStart(), 'updated', npm.stderr);
        assert.deepEqual(s.ran, ['reply 0.22.0', 'jason plugin list', installLine, 'reply 0.24.0', 'jason plugin reload', 'jason plugin list']);
        assert.equal(await line(s), 'Jason updated the Reply CLI to 0.24.0 when it started.');
        assert.equal(s.kept().replyUpdateTried, undefined);
    }
    // One that left the old CLI, or one that says no version, is a failure in npm's words, with no reload.
    for (const after of ['0.22.0', null]) {
        const s = startUpdate(t, { npm: { exit: -1, stdout: '', stderr: 'npm error code EPERM' }, putsAnyway: true, after });
        assert.equal(await s.setup.updateReplyAtStart(), 'failed');
        assert.equal(await line(s), 'Jason tried to update the Reply CLI when it started and could not: npm error code EPERM.');
        assert.equal(s.ran.includes('jason plugin reload'), false);
        assert.equal(s.kept().replyUpdateTried, replyMinimumVersion);
    }
});

test('npm that ended well and left the old CLI in use is said as that, by the desktop’s own read or by what the runtime found', async (t) => {
    const still = (version: string) => `Jason updated the Reply CLI when it started, but the one in use is still ${version}: it was installed another way. Update it the way you installed it.`;
    const own = startUpdate(t, { after: '0.22.0' });
    assert.equal(await own.setup.updateReplyAtStart(), 'still_old');
    assert.equal(await line(own), still('0.22.0'));
    assert.equal(own.kept().replyUpdateTried, replyMinimumVersion, 'tried once for this minimum');
    assert.deepEqual(await checked(own), ended);

    const runtime = startUpdate(t, { runtime: '0.22.5' });
    assert.equal(await runtime.setup.updateReplyAtStart(), 'still_old');
    assert.equal(await line(runtime), still('0.22.5'));

    // The runtime's list is taken at its word only after a reload it made; what it says nothing of leaves the desktop's read.
    const refused = startUpdate(t, { runtime: '0.22.5', reload: 1 });
    assert.equal(await refused.setup.updateReplyAtStart(), 'updated');
    assert.equal(refused.ran.filter((each) => each === 'jason plugin list').length, 1, 'the one before npm alone');
    const silent = startUpdate(t, { runtime: null });
    assert.equal(await silent.setup.updateReplyAtStart(), 'updated');
    assert.equal(runtimeReplyVersion('not JSON'), null);
    assert.equal(runtimeReplyVersion('{"plugins":[{"id":"slack","capabilities":{"exec":null}}]}'), null);

    const unsaid = startUpdate(t, { after: null });
    assert.equal(await unsaid.setup.updateReplyAtStart(), 'failed');
    assert.equal(await line(unsaid), `Jason tried to update the Reply CLI when it started and could not: ${versionUnsaid}.`);
});

test('the outcome is this run’s alone, and a failure is no longer said once the CLI in use is at the minimum', async (t) => {
    const s = startUpdate(t, { npm: { exit: 1, stdout: '', stderr: 'npm error code EACCES' } });
    await s.setup.updateReplyAtStart();
    assert.equal(typeof (await line(s)), 'string');
    // The next start of the application: nothing of it is kept but the mark.
    assert.equal((await new Setup({ ...setupDeps(s.b, memoryLog(), s.kept()) }).status()).reply.startUpdate, undefined);

    let exit = 1;
    const healed = startUpdate(t, {
        get npm(): RunResult {
            return { exit, stdout: '', stderr: exit === 0 ? '' : 'npm error code EACCES' };
        },
    });
    await healed.setup.updateReplyAtStart();
    assert.equal(typeof (await line(healed)), 'string');
    exit = 0;
    await healed.setup.installProgram('reply');
    assert.equal(await line(healed), undefined);
});

test('a click and the start never run npm together: the start waits in the queue the click waits in, and finds the CLI updated', async (t) => {
    let release = (_: RunResult): void => undefined;
    const held = new Promise<RunResult>((resolve) => (release = resolve));
    const s = startUpdate(t, { npm: held });
    const click = s.setup.installProgram('reply');
    const start = s.setup.updateReplyAtStart();
    await new Promise((resolve) => setTimeout(resolve, 20));
    assert.deepEqual(s.ran, ['reply 0.22.0', installLine], 'the start has asked nothing while the click runs');
    release({ exit: 0, stdout: '', stderr: '' });
    await click;
    assert.equal(await start, 'current');
    assert.equal(npmRuns(s).length, 1);
});

test('a click made while the start’s update runs is not a second npm: in its turn it finds the CLI at the minimum, and has the runtime read its plugins again', async (t) => {
    let release = (_: RunResult): void => undefined;
    const held = new Promise<RunResult>((resolve) => (release = resolve));
    const s = startUpdate(t, { npm: held });
    let changed = 0;
    s.setup.onChanged(() => changed++);
    const start = s.setup.updateReplyAtStart();
    const click = s.setup.installProgram('reply');
    await new Promise((resolve) => setTimeout(resolve, 20));
    release({ exit: 0, stdout: '', stderr: '' });
    assert.equal(await start, 'updated');
    await click;
    assert.deepEqual(npmRuns(s), [installLine]);
    assert.equal(s.ran.filter((each) => each === 'jason plugin reload').length, 2, 'the start’s, and the click’s own');
    assert.equal(changed, 2, 'the card is drawn again by each');

    // The version is read again, never taken from an earlier read: a CLI put back to an older one since is updated.
    let version = '0.24.0';
    const b = bench(t, { programs: ['reply', 'npm'] });
    const runs: string[] = [];
    const programs: Programs = {
        find: (name) => b.programs.find(name),
        run: (name, args) => {
            if (name === 'npm') runs.push(args.join(' '));
            return Promise.resolve({ exit: 0, stdout: name === 'reply' && args[0] === '--version' ? `reply-cli ${version}` : '{}', stderr: '' });
        },
    };
    const setup = new Setup({ ...setupDeps(b), programs });
    await setup.status();
    await setup.installProgram('reply');
    assert.deepEqual(runs, []);
    version = '0.22.0';
    await setup.installProgram('reply');
    assert.deepEqual(runs, ['install -g reply-cli']);
    version = '';
    await setup.installProgram('reply');
    assert.equal(runs.length, 2, 'a CLI that says no version is updated on a click');

    // A missing one is installed as before, with nothing read first.
    const missing = startUpdate(t, { programs: ['npm'] });
    await missing.setup.installProgram('reply');
    assert.deepEqual(missing.ran, [installLine, 'jason plugin reload']);
});

test('in Live the update is asked for once the machine set-up has ended; in the demo nothing is asked or waited for; the check ends at the latest at the bound on the set-up, or when the start is over unasked', async (t) => {
    const calls: string[] = [];
    let ended = (): void => undefined;
    let bound = (): void => undefined;
    const slept: number[] = [];
    const deps = (setUp: () => Promise<void>, update: () => Promise<unknown>, end: () => void = () => void calls.push('end')) => ({
        setUp: () => (calls.push('set-up'), setUp()),
        update: () => (calls.push('update'), update()),
        failed: () => void calls.push('failed'),
        end,
        waitMs: 30_000,
        sleep: (ms: number) => (slept.push(ms), new Promise<void>((resolve) => (bound = resolve))),
    });
    const turn = () => new Promise((resolve) => setImmediate(resolve));
    const demo = replyStartCheck('demo', deps(() => Promise.resolve(), () => Promise.resolve()));
    demo.ask();
    await turn();
    assert.deepEqual(calls, ['end'], 'nothing is asked or waited for, and the check ends');
    demo.over();
    assert.deepEqual(calls, ['end']);
    calls.length = 0;
    const live = replyStartCheck('live', deps(() => new Promise((resolve) => (ended = resolve)), () => Promise.resolve()));
    live.ask();
    await turn();
    assert.deepEqual(calls, ['set-up']);
    assert.deepEqual(slept, [30_000], 'the set-up is waited for at most the bound handed in');
    ended();
    await turn();
    assert.deepEqual(calls, ['set-up', 'update']);
    // The set-up ended within the bound: the update ends the check, and the bound passing later ends nothing; nor does
    // the start being over, the update asked.
    bound();
    await turn();
    live.over();
    assert.deepEqual(calls, ['set-up', 'update']);
    // The bound is on the set-up alone: passing while the update still runs, it ends nothing; the update's own end does.
    calls.length = 0;
    let updated = (): void => undefined;
    replyStartCheck('live', deps(() => Promise.resolve(), () => new Promise<void>((resolve) => (updated = resolve)))).ask();
    await turn();
    assert.deepEqual(calls, ['set-up', 'update']);
    bound();
    await turn();
    assert.deepEqual(calls, ['set-up', 'update'], 'the bound passing while the update runs ends nothing');
    updated();
    await turn();
    assert.deepEqual(calls, ['set-up', 'update']);

    // A start over with no ask, whichever way it went: the check ends.
    calls.length = 0;
    replyStartCheck('live', deps(() => Promise.resolve(), () => Promise.resolve())).over();
    await turn();
    assert.deepEqual(calls, ['end']);

    // A set-up that has not ended at the bound: the check ends there, the update not asked; once the set-up ends the
    // update runs, and its sentence is told to the page.
    const s = startUpdate(t);
    const late = replyStartCheck('live', deps(() => new Promise((resolve) => (ended = resolve)), () => s.setup.updateReplyAtStart(), () => s.setup.endStartCheck()));
    late.ask();
    await turn();
    assert.deepEqual(await checked(s), { check: 'pending', told: 0 });
    bound();
    await turn();
    assert.deepEqual(await checked(s), { check: 'ended', told: 1 });
    assert.deepEqual(npmRuns(s), []);
    ended();
    for (let waited = 0; s.told() < 2; waited++) {
        assert.ok(waited < 400, 'the update ran and told the page');
        await new Promise((resolve) => setTimeout(resolve, 5));
    }
    assert.deepEqual(npmRuns(s), [installLine]);
    assert.equal(await line(s), 'Jason updated the Reply CLI to 0.24.0 when it started.');
    assert.deepEqual(await checked(s), { check: 'ended', told: 2 });

    // A set-up that failed has ended too; a failure of the update is told and stops nothing.
    calls.length = 0;
    replyStartCheck('live', deps(() => Promise.reject(new Error('set-up')), () => Promise.reject(new Error('update')))).ask();
    await turn();
    assert.deepEqual(calls, ['set-up', 'update', 'failed']);

    // The start holds it so: the check is made with the Setup, asked once the window has loaded, and over in one finally
    // that every later exit of the start passes, the copy that could not be made, no jason carried and a throw among them.
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    const made = main.indexOf('const replyCheck = replyStartCheck(runOn.mode, {');
    const opened = main.indexOf('    try {', made);
    const over = main.indexOf('    } finally {\n        replyCheck.over();\n    }', made);
    const at = (text: string) => main.indexOf(text, opened);
    assert.ok(main.indexOf('const setup = new Setup({') < made && made < opened && opened < over, 'made after the Setup, then the try');
    // What it is handed: the Setup's own end of the check, the start's own bound on the set-up, and a timer.
    const handed = main.slice(made, opened);
    for (const argument of ['end: () => setup.endStartCheck(),', 'waitMs: liveWaitMs,', 'sleep: (ms) => new Promise((resolve) => setTimeout(resolve, ms)),']) {
        assert.ok(handed.includes(argument), `the start hands it ${argument}`);
    }
    for (const exit of ["await showStarting(window, 'Starting Jason…', false);", 'if (uncopied !== null) {', 'if (version === null) {', "log.info('window_loaded'", 'replyCheck.ask();', '} catch (error) {']) {
        assert.ok(at(exit) > opened && at(exit) < over, `${exit} is inside the try the finally closes`);
    }
    assert.equal(main.split('replyCheck.ask();').length - 1, 1);
    assert.equal(main.split('replyCheck.over();').length - 1, 1);
    assert.ok(at('replyCheck.ask();') > at("log.info('window_loaded'"));
});
