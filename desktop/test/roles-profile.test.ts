// The agents' roles put on the AI the chat runs on: one execution profile made with `jason profile create` and named the
// default with `jason profile set-default`, in Live, only where the installation has no profile and the runtime answers no
// default in force.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { loggedCli } from '../src/installation.ts';
import { dataLayout } from '../src/paths.ts';
import { programsOn } from '../src/programs.ts';
import { ProviderKeys } from '../src/providers.ts';
import { rolesMoments } from '../src/roles-moments.ts';
import { lastChosenHost, profileProgram, roleHostOf } from '../src/roles-profile.ts';
import { readSettings, writeSettings, type DesktopSettings } from '../src/settings.ts';
import { namingReason, Setup, type SetupDeps } from '../src/setup.ts';
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
    id?: string;
    name: string;
    disabled: boolean;
    revision: { host: string | null; program: string | null };
    mark_when_made?: { name: string; id?: string } | null;
}

const profiles = (b: Bench): StubProfile[] => (existsSync(profilesFile(b)) ? (JSON.parse(readFileSync(profilesFile(b), 'utf8')) as StubProfile[]) : []);

function holdProfiles(b: Bench, held: StubProfile[]): void {
    writeFileSync(profilesFile(b), JSON.stringify(held));
}

const asked = (b: Bench, verb: string) => b.record().filter((entry) => entry.name === 'jason' && entry.args.slice(0, 2).join(' ') === verb);

/** The profile verbs the desktop asked the runtime, in order. */
const profileVerbs = (b: Bench) => b.record().filter((entry) => entry.name === 'jason' && entry.args[0] === 'profile').map((entry) => entry.args.slice(0, 2).join(' '));

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

// ---- The wiring --------------------------------------------------------------------------------------------------

test('in Live the start connects with the host the chat holds and a turn with its own; in the demo nothing is asked or read', async () => {
    const calls: (string | null)[] = [];
    let read = 0;
    let failed = 0;
    const deps = (answer: () => Promise<unknown>) => ({
        connect: (chosen: string | null) => {
            calls.push(chosen);
            return answer();
        },
        chosen: () => {
            read++;
            return 'codex';
        },
        failed: () => failed++,
    });

    const demo = rolesMoments('demo', deps(() => Promise.resolve('made')));
    demo.atStart();
    demo.atTurn('claude');
    assert.deepEqual([calls, read], [[], 0], "the demo asks nothing, and does not read the chat's host");

    const live = rolesMoments('live', deps(() => Promise.resolve('made')));
    live.atStart();
    live.atTurn('claude');
    assert.deepEqual([calls, read], [['codex', 'claude'], 1], 'the start reads the host once; a turn brings its own');

    // A failure, thrown or rejected, is told and stops neither moment.
    const broken = rolesMoments('live', deps(() => Promise.reject(new Error('no runtime'))));
    broken.atTurn('claude');
    await new Promise((resolve) => setImmediate(resolve));
    const throwing = rolesMoments('live', { ...deps(() => Promise.resolve()), chosen: () => { throw new Error('unreadable'); } });
    throwing.atStart();
    assert.equal(failed, 2);
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
    assert.deepEqual(made?.mark_when_made, { name: 'claude-code' }, "the desktop's mark is written before the profile is made, with no id yet");
    assert.deepEqual(asked(b, 'profile create')[0]?.args, ['profile', 'create', 'claude-code', '--host', 'claude_code', '--program', programOf(b, 'claude')]);
    // The default is named by the runtime's own operation, after a look at the default in force just before it.
    assert.deepEqual(profileVerbs(b), ['profile list', 'profile create', 'profile list', 'profile set-default']);
    assert.deepEqual(asked(b, 'profile set-default')[0]?.args, ['profile', 'set-default', 'claude-code', '--reason', namingReason]);
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

test('nothing is done where a person made a profile, a disabled one too, or the runtime answers a default in force', async (t) => {
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

    // In any case of its keys, and one a JASON_ variable sets with the file naming none: the runtime's answer says it.
    const named: [unknown, Record<string, string>][] = [
        [{ Roles: { DefaultExecutionProfile: 'house' } }, {}],
        [{ roles: { defaultexecutionprofile: 'house' } }, {}],
        [{ Roles: { MaxStdoutBytes: 1000 } }, { STUB_DEFAULT_IN_FORCE: 'house' }],
    ];
    for (const [settings, env] of named) {
        const one = bench(t, { programs: ['claude'], env: { ...env } });
        writeInstallationSettings(one, settings);
        const kept = readFileSync(settingsFile(one), 'utf8');
        const setup = setupOn(one);
        assert.equal(await setup.connectRoles(null), 'default_named', JSON.stringify([settings, env]));
        assert.deepEqual(profileVerbs(one), ['profile list'], 'one look, and nothing made or named');
        assert.equal(readFileSync(settingsFile(one), 'utf8'), kept);
        assert.equal(await setup.connectRoles(null), 'settled');
    }

    // A null the file holds is no default in force, as the runtime binds it, so the roles get one.
    const none = bench(t, { programs: ['claude'] });
    writeInstallationSettings(none, { Roles: { DefaultExecutionProfile: null } });
    assert.equal(await setupOn(none).connectRoles(null), 'made');
    assert.deepEqual(installationSettings(none), { Roles: { DefaultExecutionProfile: 'claude-code' } });
});

test('a runtime whose list does not say the default in force is left alone, and asked again at the next turn', async (t) => {
    const b = bench(t, { programs: ['claude'], env: { STUB_PROFILE_LIST_NO_DEFAULT: 'yes' } });
    const setup = setupOn(b);
    assert.equal(await setup.connectRoles(null), 'profiles_unread');
    assert.deepEqual(profileVerbs(b), ['profile list'], 'nothing made or named where the default in force is not known');
    assert.equal(existsSync(settingsFile(b)), false);
    assert.equal(readSettings(b.paths.settings).rolesProfilePending, undefined);
    delete b.env.STUB_PROFILE_LIST_NO_DEFAULT;
    assert.equal(await setup.connectRoles(null), 'made');
});

test('a runtime that does not answer leaves it for the next turn, and a refused create for the next start, with the mark kept', async (t) => {
    const b = bench(t, { programs: ['claude'], env: { STUB_PROFILE_LIST_FAILS: 'yes' } });
    const setup = setupOn(b);
    assert.equal(await setup.connectRoles(null), 'profiles_unread');
    delete b.env.STUB_PROFILE_LIST_FAILS;
    b.env.STUB_PROFILE_CREATE_FAILS = 'yes';
    assert.equal(await setup.connectRoles(null), 'create_refused');
    assert.equal(await setup.connectRoles(null), 'settled', 'a refusal is not asked again in the same run');
    assert.deepEqual(readSettings(b.paths.settings).rolesProfilePending, { name: 'claude-code' }, 'a mark with no id: nothing was shown to be made');

    delete b.env.STUB_PROFILE_CREATE_FAILS;
    assert.equal(await setupOn(b).connectRoles(null), 'made', 'the next start makes it, nothing having been made');
    assert.deepEqual(profiles(b).map((each) => each.name), ['claude-code']);
});

test('the half-done case: made and not named, the next start names the profile its mark shows it made, and never makes a second', async (t) => {
    // The runtime refuses the default after the create succeeded: the mark holds the made profile's id by then.
    const b = bench(t, { programs: ['claude'], env: { STUB_PROFILE_SET_DEFAULT_REFUSED: 'setting_not_in_force' } });
    writeInstallationSettings(b, {});
    assert.equal(await setupOn(b).connectRoles(null), 'default_unwritten');
    const [made] = profiles(b);
    assert.match(made?.id ?? '', /^[a-z]{2,8}_[0-9A-HJKMNP-TV-Z]{26}$/);
    assert.deepEqual(readSettings(b.paths.settings).rolesProfilePending, { name: 'claude-code', id: made?.id }, 'the mark stays, with the id the create answered');
    assert.deepEqual(installationSettings(b), {}, 'the desktop writes nothing of its own where the runtime refused');
    delete b.env.STUB_PROFILE_SET_DEFAULT_REFUSED;
    writeInstallationSettings(b, { Routes: { Default: { Plugin: 'reply' } } });

    const next = setupOn(b);
    assert.equal(await next.connectRoles('claude-api-key'), 'named', 'no host is needed to finish');
    assert.equal(asked(b, 'profile create').length, 1, 'never a second create');
    assert.deepEqual(profiles(b).map((each) => each.name), ['claude-code']);
    assert.deepEqual(installationSettings(b), { Routes: { Default: { Plugin: 'reply' } }, Roles: { DefaultExecutionProfile: 'claude-code' } });
    assert.equal(readSettings(b.paths.settings).rolesProfilePending, undefined);
});

test('a runtime that stops answering after the create leaves the naming to the next turn, which names it with one set-default', async (t) => {
    // The look just before the set finds no runtime: nothing is named, and the run is not settled.
    const stopped = bench(t, { programs: ['claude'], env: { STUB_PROFILE_CREATE_STOPS_RUNTIME: 'yes' } });
    const setup = setupOn(stopped);
    assert.equal(await setup.connectRoles(null), 'default_unanswered');
    assert.deepEqual(profileVerbs(stopped), ['profile list', 'profile create', 'profile list']);
    assert.deepEqual(readSettings(stopped.paths.settings).rolesProfilePending, { name: 'claude-code', id: profiles(stopped)[0]?.id });

    // The runtime is back by the next turn: the profile the mark shows is named, once, and never made again.
    delete stopped.env.STUB_PROFILE_CREATE_STOPS_RUNTIME;
    rmSync(join(stopped.root, 'runtime-stopped'));
    assert.equal(await setup.connectRoles('claude'), 'named');
    assert.equal(asked(stopped, 'profile set-default').length, 1);
    assert.equal(asked(stopped, 'profile create').length, 1);
    assert.equal((installationSettings(stopped).Roles as Record<string, unknown>).DefaultExecutionProfile, 'claude-code');
    assert.equal(readSettings(stopped.paths.settings).rolesProfilePending, undefined);

    // The same where the naming itself finds no runtime.
    const naming = bench(t, { programs: ['claude'], env: { STUB_PROFILE_SET_DEFAULT_UNREACHABLE: 'yes' } });
    const again = setupOn(naming);
    assert.equal(await again.connectRoles(null), 'default_unanswered');
    delete naming.env.STUB_PROFILE_SET_DEFAULT_UNREACHABLE;
    assert.equal(await again.connectRoles(null), 'named');
    assert.equal(asked(naming, 'profile create').length, 1);

    // And where the naming did not finish: killed at its limit, or never started.
    const killed = bench(t, { programs: ['claude'] });
    const cli = loggedCli(killed.cli, memoryLog());
    let kills = 1;
    const once = setupOn(killed, {
        cli: { run: (args, options) => (args[1] === 'set-default' && kills-- > 0 ? Promise.resolve({ exit: -1, stdout: '', stderr: 'killed' }) : cli.run(args, options)) },
    });
    assert.equal(await once.connectRoles(null), 'default_unanswered');
    assert.equal(await once.connectRoles(null), 'named');

    // A profile made at an earlier start, its naming unanswered now: left to the next turn too.
    const half = bench(t, { programs: ['claude'], env: { STUB_PROFILE_SET_DEFAULT_UNREACHABLE: 'yes' } });
    writeSettings(half.paths.settings, { ...readSettings(half.paths.settings), rolesProfilePending: { name: 'claude-code', id: 'exp_01JD0000000000000000000000' } });
    holdProfiles(half, [{ id: 'exp_01JD0000000000000000000000', name: 'claude-code', disabled: false, revision: { host: 'claude_code', program: 'claude' } }]);
    const later = setupOn(half);
    assert.equal(await later.connectRoles(null), 'default_unanswered');
    delete half.env.STUB_PROFILE_SET_DEFAULT_UNREACHABLE;
    assert.equal(await later.connectRoles(null), 'named');
    assert.equal(asked(half, 'profile set-default').length, 2);
});

test('a naming the runtime refuses, or answers with another name, is unwritten, the mark and its id kept, and settles the run', async (t) => {
    // A success that answers a name other than the one asked is not taken for it.
    const other = bench(t, { programs: ['claude'], env: { STUB_PROFILE_SET_DEFAULT_ANSWERS: 'house' } });
    const setup = setupOn(other);
    assert.equal(await setup.connectRoles(null), 'default_unwritten');
    assert.deepEqual(readSettings(other.paths.settings).rolesProfilePending, { name: 'claude-code', id: profiles(other)[0]?.id });
    assert.equal(await setup.connectRoles(null), 'settled');

    // A default a JASON_ variable sets by the naming: refused setting_shadowed, nothing written; the next start finds it in
    // force and lets the mark go.
    const shadowed = bench(t, { programs: ['claude'] });
    writeInstallationSettings(shadowed, { Roles: { MaxStdoutBytes: 1000 } });
    const cli = loggedCli(shadowed.cli, memoryLog());
    const first = setupOn(shadowed, {
        cli: {
            run: (args, options) => {
                if (args[1] === 'set-default') shadowed.env.STUB_DEFAULT_IN_FORCE = 'house';
                return cli.run(args, options);
            },
        },
    });
    assert.equal(await first.connectRoles(null), 'default_unwritten');
    assert.deepEqual(installationSettings(shadowed), { Roles: { MaxStdoutBytes: 1000 } });
    assert.deepEqual(readSettings(shadowed.paths.settings).rolesProfilePending, { name: 'claude-code', id: profiles(shadowed)[0]?.id });
    assert.equal(await setupOn(shadowed).connectRoles(null), 'default_named');
    assert.equal(readSettings(shadowed.paths.settings).rolesProfilePending, undefined);

    // The profile gone by the naming: refused profile_not_found, the mark kept; the next start finds no profile and makes it.
    const gone = bench(t, { programs: ['claude'] });
    const goneCli = loggedCli(gone.cli, memoryLog());
    const before = setupOn(gone, {
        cli: {
            run: (args, options) => {
                if (args[1] === 'set-default') holdProfiles(gone, []);
                return goneCli.run(args, options);
            },
        },
    });
    assert.equal(await before.connectRoles(null), 'default_unwritten');
    assert.equal(existsSync(settingsFile(gone)), false);
    assert.equal(readSettings(gone.paths.settings).rolesProfilePending?.name, 'claude-code');
    assert.equal(await setupOn(gone).connectRoles(null), 'made');
    assert.equal(asked(gone, 'profile set-default').length, 2);
});

test('a naming the runtime took and did not answer in time is never asked again blind: the next start reads it back', async (t) => {
    const b = bench(t, { programs: ['claude'], env: { STUB_PROFILE_SET_DEFAULT_LOST: 'yes' } });
    assert.equal(await setupOn(b).connectRoles(null), 'default_unwritten');
    assert.deepEqual(readSettings(b.paths.settings).rolesProfilePending, { name: 'claude-code', id: profiles(b)[0]?.id }, 'the mark is kept');

    delete b.env.STUB_PROFILE_SET_DEFAULT_LOST;
    assert.equal(await setupOn(b).connectRoles(null), 'default_named', 'the next start lists first and finds the default in force');
    assert.equal(readSettings(b.paths.settings).rolesProfilePending, undefined, 'and lets the mark go');
    assert.equal(asked(b, 'profile set-default').length, 1, 'one naming in all');
});

test('a mark that cannot show the profile is the desktop\'s is let go and nothing is written: a lost answer, or a person\'s own profile of the same name', async (t) => {
    // The create's answer lost on its way: the profile is there, but the mark never got its id.
    const lost = bench(t, { programs: ['claude'], env: { STUB_PROFILE_CREATE_LOST: 'yes' } });
    assert.equal(await setupOn(lost).connectRoles(null), 'create_refused');
    assert.deepEqual(readSettings(lost.paths.settings).rolesProfilePending, { name: 'claude-code' });
    delete lost.env.STUB_PROFILE_CREATE_LOST;
    assert.equal(await setupOn(lost).connectRoles('claude'), 'profiles_exist');
    assert.equal(asked(lost, 'profile create').length, 1, 'never a second create');
    assert.equal(existsSync(settingsFile(lost)), false, 'no default written');
    assert.equal(readSettings(lost.paths.settings).rolesProfilePending, undefined, 'the mark is let go');

    // A refused create left a mark with no id; the person then typed the repair jason status prints, of the same name.
    const theirs = bench(t, { programs: ['claude'] });
    writeSettings(theirs.paths.settings, { ...readSettings(theirs.paths.settings), rolesProfilePending: { name: 'claude-code' } });
    holdProfiles(theirs, [{ id: 'exp_01JD0000000000000000000000', name: 'claude-code', disabled: false, revision: { host: 'claude_code', program: 'claude' } }]);
    assert.equal(await setupOn(theirs).connectRoles(null), 'profiles_exist');
    assert.equal(existsSync(settingsFile(theirs)), false, "a person's profile is never named the default by the desktop");
    assert.equal(readSettings(theirs.paths.settings).rolesProfilePending, undefined);

    // A mark with an id that is not the one profile there, or beside more than one, is no half-done case either.
    for (const held of [
        [{ id: 'exp_01JD0000000000000000000001', name: 'claude-code', disabled: false, revision: { host: 'claude_code', program: 'claude' } }],
        [
            { id: 'exp_01JD0000000000000000000000', name: 'claude-code', disabled: false, revision: { host: 'claude_code', program: 'claude' } },
            { id: 'exp_01JD0000000000000000000002', name: 'mine', disabled: false, revision: { host: 'claude_code', program: 'claude' } },
        ],
        [{ id: 'exp_01JD0000000000000000000000', name: 'claude-code', disabled: true, revision: { host: 'claude_code', program: 'claude' } }],
    ]) {
        const other = bench(t, { programs: ['claude'] });
        writeSettings(other.paths.settings, { ...readSettings(other.paths.settings), rolesProfilePending: { name: 'claude-code', id: 'exp_01JD0000000000000000000000' } });
        holdProfiles(other, held);
        assert.equal(await setupOn(other).connectRoles(null), 'profiles_exist', JSON.stringify(held));
        assert.equal(existsSync(settingsFile(other)), false);
        assert.equal(readSettings(other.paths.settings).rolesProfilePending, undefined);
    }
});

test('a default a person names between the look and the write is kept, and the outcome says the profile was made and not named', async (t) => {
    // The stub's create writes the person's default into the settings file, as a person editing it at that moment would.
    const b = bench(t, { programs: ['claude'], env: { STUB_PROFILE_CREATE_NAMES_DEFAULT: 'house' } });
    writeInstallationSettings(b, { Roles: { MaxStdoutBytes: 1000 } });
    assert.equal(await setupOn(b).connectRoles(null), 'made_unnamed');
    assert.deepEqual(installationSettings(b), { Roles: { MaxStdoutBytes: 1000, DefaultExecutionProfile: 'house' } });
    assert.equal(asked(b, 'profile set-default').length, 0, 'the runtime is not asked to name over it');
    assert.equal(readSettings(b.paths.settings).rolesProfilePending, undefined, 'nothing is left to finish');

    // The same for a profile made earlier: a default in force by the look just before the set is kept.
    const half = bench(t, { programs: ['claude'] });
    writeSettings(half.paths.settings, { ...readSettings(half.paths.settings), rolesProfilePending: { name: 'claude-code', id: 'exp_01JD0000000000000000000000' } });
    holdProfiles(half, [{ id: 'exp_01JD0000000000000000000000', name: 'claude-code', disabled: false, revision: { host: 'claude_code', program: 'claude' } }]);
    // The first look answers none and the second the person's: a default named between the two.
    const cli = loggedCli(half.cli, memoryLog());
    const setup = setupOn(half, {
        cli: {
            run: (args, options) => {
                if (args.join(' ') === 'profile list --include-disabled --limit 1') half.env.STUB_DEFAULT_IN_FORCE = 'house';
                return cli.run(args, options);
            },
        },
    });
    assert.equal(await setup.connectRoles(null), 'default_named');
    assert.equal(asked(half, 'profile set-default').length, 0);
    assert.equal(readSettings(half.paths.settings).rolesProfilePending, undefined);
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

    // The default is the one in force as the runtime answers it, not the file's: a JASON_ variable's, the file naming none.
    writeInstallationSettings(b, {});
    b.env.STUB_DEFAULT_IN_FORCE = 'night-run';
    assert.deepEqual([...(await status())].filter(([, line]) => line !== null), [['codex', 'night-run']]);
    delete b.env.STUB_DEFAULT_IN_FORCE;

    // Only under a host the chat can use: never under a Claude Code below its floor, nor under a host that drives no chat.
    const held = bench(t, { programs: ['claude', 'gemini'] });
    held.env.STUB_CLAUDE_VERSION_FILE = below(held).STUB_CLAUDE_VERSION_FILE;
    holdProfiles(held, [
        { name: 'claude-code', disabled: false, revision: { host: 'claude_code', program: 'claude' } },
        { name: 'gem', disabled: false, revision: { host: 'gemini_cli', program: 'gemini' } },
    ]);
    for (const name of ['claude-code', 'gem']) {
        writeInstallationSettings(held, { Roles: { DefaultExecutionProfile: name } });
        const hosts = (await setupOn(held, { programs: programsOn(held.env, null) }).status()).hosts;
        assert.deepEqual(hosts.filter((host) => host.runsRoles !== null).map((host) => host.id), [], name);
    }
});
