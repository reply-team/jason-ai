// The agents' roles put on the AI the chat runs on: one execution profile made with `jason profile create` and named the
// default in the installation's settings, in Live, only where the installation has no profile and names no default.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { loggedCli } from '../src/installation.ts';
import { dataLayout } from '../src/paths.ts';
import { programsOn } from '../src/programs.ts';
import { ProviderKeys } from '../src/providers.ts';
import { lastChosenHost, profileProgram, roleHostOf } from '../src/roles-profile.ts';
import { readSettings, writeSettings, type DesktopSettings } from '../src/settings.ts';
import { Setup, type SetupDeps } from '../src/setup.ts';
import { bench, fakeBox, memoryLog, type Bench } from './harness.ts';

/** A Setup on the bench whose desktop settings live in the bench's own file, read again by every Setup made on it. */
function setupOn(b: Bench, extra: Partial<SetupDeps> = {}): Setup {
    const log = memoryLog();
    let settings: DesktopSettings = readSettings(b.paths.settings);
    return new Setup({
        paths: b.paths,
        cli: loggedCli(b.cli, log),
        programs: b.programs,
        keys: new ProviderKeys(b.paths.providerKeys, fakeBox),
        log,
        bundleVersion: '0.34.0-rc.1',
        runtimeVersion: () => '0.34.0-rc.1',
        settings: () => settings,
        saveSettings: (next) => {
            settings = next;
            writeSettings(b.paths.settings, next);
        },
        ...extra,
    });
}

const settingsFile = (b: Bench) => dataLayout(b.paths.data).userSettingsFile;

function writeInstallationSettings(b: Bench, value: unknown): void {
    mkdirSync(dirname(settingsFile(b)), { recursive: true });
    writeFileSync(settingsFile(b), typeof value === 'string' ? value : JSON.stringify(value, null, 2));
}

const installationSettings = (b: Bench) => JSON.parse(readFileSync(settingsFile(b), 'utf8')) as Record<string, unknown>;

/** The profiles the stub runtime holds, beside the bench's record. */
const profilesFile = (b: Bench) => join(b.root, 'profiles.json');

interface StubProfile {
    name: string;
    disabled: boolean;
    revision: { host: string | null; program: string | null };
    mark_when_made?: string | null;
}

const profiles = (b: Bench): StubProfile[] => (existsSync(profilesFile(b)) ? (JSON.parse(readFileSync(profilesFile(b), 'utf8')) as StubProfile[]) : []);

function holdProfiles(b: Bench, held: StubProfile[]): void {
    writeFileSync(profilesFile(b), JSON.stringify(held));
}

const asked = (b: Bench, verb: string) => b.record().filter((entry) => entry.name === 'jason' && entry.args.slice(0, 2).join(' ') === verb);

/** The program a profile names for a stub host: the bare name where the PATH finds npm's batch file, as on Windows. */
const programOf = (b: Bench, name: 'claude' | 'codex') => (process.platform === 'win32' ? name : b.programs.find(name));

/** A Claude Code that says a version below the chat's floor. */
const below = (b: Bench) => {
    const file = join(b.root, 'claude-version.txt');
    writeFileSync(file, '2.1.200 (Claude Code)\n');
    return { STUB_CLAUDE_VERSION_FILE: file };
};

// ---- Pure ----------------------------------------------------------------------------------------------------------

test('the roles go on the host the chat is set to run on when that is Claude Code or Codex, else Claude Code, else Codex', () => {
    const both = { claude: true, codex: true };
    assert.equal(roleHostOf('codex', both), 'codex');
    assert.equal(roleHostOf('claude', both), 'claude');
    assert.equal(roleHostOf(null, both), 'claude');
    assert.equal(roleHostOf('claude-api-key', both), 'claude', 'a model-key provider never gets a profile');
    assert.equal(roleHostOf('codex-api-key', { claude: false, codex: true }), 'codex');
    assert.equal(roleHostOf(null, { claude: false, codex: true }), 'codex');
    assert.equal(roleHostOf('codex', { claude: true, codex: false }), null, 'a chosen host that is not connected is not traded for another');
    assert.equal(roleHostOf('claude', { claude: false, codex: true }), null);
    assert.equal(roleHostOf(null, { claude: false, codex: false }), null);
});

test('the program is the path the desktop found, and the bare name where that is a script only a shell runs', () => {
    assert.equal(profileProgram('claude', 'C:\\Users\\someone\\AppData\\Roaming\\npm\\claude.cmd'), 'claude');
    assert.equal(profileProgram('codex', 'C:\\Users\\someone\\AppData\\Roaming\\npm\\CODEX.CMD'), 'codex');
    assert.equal(profileProgram('claude', 'C:\\tools\\claude.bat'), 'claude');
    assert.equal(profileProgram('claude', 'C:\\tools\\claude.ps1'), 'claude');
    assert.equal(profileProgram('claude', 'C:\\Users\\someone\\.local\\bin\\claude.exe'), 'C:\\Users\\someone\\.local\\bin\\claude.exe');
    assert.equal(profileProgram('claude', '/usr/local/bin/claude'), '/usr/local/bin/claude');
});

test('the chat is set to run on the host its newest conversation last ran on', () => {
    const at = (updated: string, host?: string) => ({ updated_at: updated, ...(host === undefined ? {} : { chosen: { host, model: null, effort: null } }) });
    assert.equal(lastChosenHost([]), null);
    assert.equal(lastChosenHost([at('2026-11-01T00:00:00Z')]), null);
    assert.equal(lastChosenHost([at('2026-11-01T00:00:00Z', 'claude'), at('2026-11-03T00:00:00Z', 'codex'), at('2026-11-02T00:00:00Z', 'claude')]), 'codex');
    assert.equal(lastChosenHost([at('2026-11-01T00:00:00Z', 'claude'), at('2026-11-03T00:00:00Z')]), 'claude');
});

// ---- Against the stub runtime ---------------------------------------------------------------------------------------

test('a fresh installation with Claude Code connected gets one profile for it, named the default; the mark goes before the create and after the write', async (t) => {
    const b = bench(t, { programs: ['claude'] });
    writeInstallationSettings(b, { Routes: { Default: { Plugin: 'reply' } }, Roles: { MaxStdoutBytes: 1000 } });
    const setup = setupOn(b);
    let changed = 0;
    setup.onChanged(() => changed++);

    assert.equal(await setup.connectRoles(null), 'made');

    const [made] = profiles(b);
    assert.deepEqual(profiles(b).map((each) => each.name), ['claude-code']);
    assert.deepEqual(made?.revision, { host: 'claude_code', program: programOf(b, 'claude') });
    assert.equal(made?.mark_when_made, 'claude-code', "the desktop's mark is written before the profile is made");
    assert.deepEqual(asked(b, 'profile create')[0]?.args, ['profile', 'create', 'claude-code', '--host', 'claude_code', '--program', programOf(b, 'claude')]);
    assert.deepEqual(installationSettings(b), { Routes: { Default: { Plugin: 'reply' } }, Roles: { MaxStdoutBytes: 1000, DefaultExecutionProfile: 'claude-code' } });
    assert.equal(readSettings(b.paths.settings).rolesProfilePending, undefined, 'the mark is taken away once the default is written');
    assert.ok(changed > 0, 'the cards are read again, so the line appears');

    // Once done, a later turn asks the runtime nothing more in this run.
    const listed = asked(b, 'profile list').length;
    assert.equal(await setup.connectRoles('claude'), 'settled');
    assert.equal(asked(b, 'profile list').length, listed);
});

test('a chat set to run on Codex puts the roles on Codex, and a key provider puts them on Claude Code', async (t) => {
    const codex = bench(t, { programs: ['claude', 'codex'] });
    assert.equal(await setupOn(codex).connectRoles('codex'), 'made');
    assert.deepEqual(profiles(codex).map((each) => [each.name, each.revision.host, each.revision.program]), [['codex', 'codex', programOf(codex, 'codex')]]);
    assert.equal((installationSettings(codex).Roles as Record<string, unknown>).DefaultExecutionProfile, 'codex');

    const keyed = bench(t, { programs: ['claude', 'codex'] });
    assert.equal(await setupOn(keyed).connectRoles('deepseek'), 'made');
    assert.deepEqual(profiles(keyed).map((each) => each.name), ['claude-code']);

    const onlyCodex = bench(t, { programs: ['codex'] });
    assert.equal(await setupOn(onlyCodex).connectRoles('codex-api-key'), 'made');
    assert.deepEqual(profiles(onlyCodex).map((each) => each.name), ['codex']);
});

test('nothing is done when no AI is connected: none found, or the chosen one below its floor', async (t) => {
    const none = bench(t, { programs: ['gemini'] });
    writeInstallationSettings(none, {});
    assert.equal(await setupOn(none).connectRoles(null), 'no_host');
    assert.equal(asked(none, 'profile create').length, 0);
    assert.deepEqual(installationSettings(none), {});
    assert.equal(readSettings(none.paths.settings).rolesProfilePending, undefined, 'no mark without a host');

    const held = bench(t, { programs: ['claude', 'codex'] });
    held.env.STUB_CLAUDE_VERSION_FILE = below(held).STUB_CLAUDE_VERSION_FILE;
    // Programs read their environment when they are made.
    const setup = setupOn(held, { programs: programsOn(held.env, null) });
    assert.equal(await setup.connectRoles('claude'), 'no_host', 'a Claude Code below its floor is not connected, and Codex is not taken in its place');
    assert.equal(asked(held, 'profile create').length, 0);
    // Not settled: the next turn, on a connected host, does it.
    assert.equal(await setup.connectRoles('codex'), 'made');
});

test('nothing is done where a person made a profile, a disabled one too, or named a default, in any case of its keys', async (t) => {
    const made = bench(t, { programs: ['claude'] });
    writeInstallationSettings(made, { Dispatcher: { MaxParallel: 2 } });
    holdProfiles(made, [{ name: 'mine', disabled: true, revision: { host: 'gemini_cli', program: 'gemini' } }]);
    const before = readFileSync(settingsFile(made), 'utf8');
    const setup = setupOn(made);
    assert.equal(await setup.connectRoles('claude'), 'profiles_exist');
    assert.equal(asked(made, 'profile create').length, 0);
    assert.equal(readFileSync(settingsFile(made), 'utf8'), before);
    assert.deepEqual(asked(made, 'profile list')[0]?.args.slice(2).sort(), ['--include-disabled', '--limit', '2'].sort());
    // Settled: a later turn asks nothing.
    assert.equal(await setup.connectRoles('claude'), 'settled');
    assert.equal(asked(made, 'profile list').length, 1);

    for (const settings of [{ Roles: { DefaultExecutionProfile: 'house' } }, { roles: { defaultexecutionprofile: 'house' } }, { Roles: { DefaultExecutionProfile: null } }]) {
        const named = bench(t, { programs: ['claude'] });
        writeInstallationSettings(named, settings);
        const kept = readFileSync(settingsFile(named), 'utf8');
        assert.equal(await setupOn(named).connectRoles(null), 'default_named', JSON.stringify(settings));
        assert.equal(asked(named, 'profile list').length, 0, 'a default named asks the runtime nothing');
        assert.equal(asked(named, 'profile create').length, 0);
        assert.equal(readFileSync(settingsFile(named), 'utf8'), kept);
    }
});

test('nothing is done in the demo: no question to the runtime, no file written', async (t) => {
    const b = bench(t, { programs: ['claude', 'codex'] });
    writeInstallationSettings(b, {});
    assert.equal(await setupOn(b, { demo: true }).connectRoles('claude'), 'demo');
    assert.equal(b.record().filter((entry) => entry.name === 'jason').length, 0);
    assert.deepEqual(installationSettings(b), {});
    assert.equal(existsSync(b.paths.settings), false);
});

test('a runtime that does not answer leaves it for the next turn, and a refused create for the next start, with the mark kept', async (t) => {
    const b = bench(t, { programs: ['claude'], env: { STUB_PROFILE_LIST_FAILS: 'yes' } });
    const setup = setupOn(b);
    assert.equal(await setup.connectRoles(null), 'profiles_unread');
    delete b.env.STUB_PROFILE_LIST_FAILS;
    b.env.STUB_PROFILE_CREATE_FAILS = 'yes';
    assert.equal(await setup.connectRoles(null), 'create_refused');
    assert.equal(await setup.connectRoles(null), 'settled', 'a refusal is not asked again in the same run');
    assert.equal(readSettings(b.paths.settings).rolesProfilePending, 'claude-code');

    delete b.env.STUB_PROFILE_CREATE_FAILS;
    assert.equal(await setupOn(b).connectRoles(null), 'made', 'the next start makes it, nothing having been made');
    assert.deepEqual(profiles(b).map((each) => each.name), ['claude-code']);
});

test('the half-done case: made and not named, the next start names it and never makes a second profile', async (t) => {
    // The settings write fails after the create succeeded.
    const b = bench(t, { programs: ['claude'], env: { STUB_PROFILE_CREATE_BREAKS_SETTINGS: 'yes' } });
    writeInstallationSettings(b, {});
    assert.equal(await setupOn(b).connectRoles(null), 'default_unwritten');
    assert.equal(readSettings(b.paths.settings).rolesProfilePending, 'claude-code', 'the mark stays');
    delete b.env.STUB_PROFILE_CREATE_BREAKS_SETTINGS;
    writeInstallationSettings(b, { Routes: { Default: { Plugin: 'reply' } } });

    const next = setupOn(b);
    assert.equal(await next.connectRoles(null), 'named');
    assert.equal(asked(b, 'profile create').length, 1, 'never a second create');
    assert.deepEqual(profiles(b).map((each) => each.name), ['claude-code']);
    assert.deepEqual(installationSettings(b), { Routes: { Default: { Plugin: 'reply' } }, Roles: { DefaultExecutionProfile: 'claude-code' } });
    assert.equal(readSettings(b.paths.settings).rolesProfilePending, undefined);

    // The create's answer lost on its way: made, and the next start names it, with no host needed to finish.
    const lost = bench(t, { programs: ['claude'], env: { STUB_PROFILE_CREATE_LOST: 'yes' } });
    assert.equal(await setupOn(lost).connectRoles(null), 'create_refused');
    delete lost.env.STUB_PROFILE_CREATE_LOST;
    assert.equal(await setupOn(lost).connectRoles('claude-api-key'), 'named');
    assert.equal(asked(lost, 'profile create').length, 1);
    assert.equal((installationSettings(lost).Roles as Record<string, unknown>).DefaultExecutionProfile, 'claude-code');

    // A mark beside a profile a person made, or beside more than the marked one, is no half-done case: nothing is touched.
    const theirs = bench(t, { programs: ['claude'] });
    writeSettings(theirs.paths.settings, { ...readSettings(theirs.paths.settings), rolesProfilePending: 'claude-code' });
    holdProfiles(theirs, [{ name: 'mine', disabled: false, revision: { host: 'claude_code', program: 'claude' } }]);
    assert.equal(await setupOn(theirs).connectRoles(null), 'profiles_exist');
    holdProfiles(theirs, [
        { name: 'claude-code', disabled: false, revision: { host: 'claude_code', program: 'claude' } },
        { name: 'mine', disabled: false, revision: { host: 'claude_code', program: 'claude' } },
    ]);
    writeSettings(theirs.paths.settings, { ...readSettings(theirs.paths.settings), rolesProfilePending: 'claude-code' });
    assert.equal(await setupOn(theirs).connectRoles(null), 'profiles_exist');
    assert.equal(existsSync(settingsFile(theirs)), false, 'no settings written');
    assert.equal(readSettings(theirs.paths.settings).rolesProfilePending, undefined, 'the mark is let go');
});

test('the host the default profile runs on says so, by the profile name, and no other host does', async (t) => {
    const b = bench(t, { programs: ['claude', 'codex'] });
    const status = async () => new Map((await setupOn(b).status()).hosts.map((host) => [host.id, host.runsRoles]));

    assert.deepEqual([...(await status()).values()].filter((each) => each !== null), [], 'no default, no line');

    await setupOn(b).connectRoles(null);
    const named = await status();
    assert.equal(named.get('claude'), 'claude-code');
    assert.equal(named.get('codex'), null);
    assert.equal(named.get('claude-api-key'), null, 'a key provider runs no role');

    // A default of the person's own on Codex is said on Codex; one disabled, or one no profile answers, is said nowhere.
    holdProfiles(b, [...profiles(b), { name: 'night-run', disabled: false, revision: { host: 'codex', program: 'codex' } }, { name: 'off', disabled: true, revision: { host: 'claude_code', program: 'claude' } }]);
    for (const [name, where] of [['night-run', 'codex'], ['off', null], ['gone', null]] as const) {
        writeInstallationSettings(b, { Roles: { DefaultExecutionProfile: name } });
        const lines = await status();
        assert.deepEqual([...lines].filter(([, line]) => line !== null), where === null ? [] : [[where, name]], name);
    }
});
