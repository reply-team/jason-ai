import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { Cli, CliResult } from '../src/cli.ts';
import { wrapperScript } from '../src/command-line.ts';
import {
    commandIsSetup,
    jasonOnPath,
    MachineSetup,
    placeLine,
    putBackLabel,
    putBackQuestion,
    putBackTold,
    readSetupRecord,
    setupArguments,
    setupLimitMs,
    setupMenuOf,
    setupOffered,
    type SetupState,
} from '../src/machine-setup.ts';
import { keepFirstStart, readMode } from '../src/mode.ts';
import { desktopPaths } from '../src/paths.ts';
import { isNewInstallation } from '../src/theme.ts';
import { memoryLog } from './harness.ts';

const folder = () => mkdtempSync(join(tmpdir(), 'machine-setup-'));
const macExe = '/Applications/Jason.app/Contents/Resources/bundle/jason';

/** A report as `jason install` prints it, snake_case, with what a test changes laid over it. */
function report(over: Record<string, unknown> = {}): Record<string, unknown> {
    return {
        version: '0.49.0',
        executable: macExe,
        command: { runs: '/Users/a/.local/bin/jason', open_shell: '/Users/a/.local/bin/jason', changed: true, profiles: ['/Users/a/.zprofile', '/Users/a/.zshrc'], taken_back: [] },
        skills: [
            { root: '/Users/a/.claude/skills', hosts: ['claude_code'], written: 12, unchanged: 0, left_alone: [] },
            { root: '/Users/a/.agents/skills', hosts: ['codex'], written: 12, unchanged: 0, left_alone: ['sdr-operations'] },
        ],
        roles: null,
        retired: null,
        runtime: 'not_running',
        done: ['Wrote /Users/a/.local/bin/jason, which runs ' + macExe + '.', 'Added /Users/a/.local/bin to the PATH in /Users/a/.zprofile and /Users/a/.zshrc.'],
        left: ['/Users/a/.agents/skills/sdr-operations is a skill under a name Jason ships that no record of Jason’s names there, so it is yours: left as it is.'],
        problems: [],
        completed: true,
        ...over,
    };
}

const printed = (body: unknown, exit = 0): CliResult => ({ exit, stdout: `${JSON.stringify(body)}\n`, stderr: '' });

/** A CLI that answers each run with the next result, and records what it was asked. */
function fakeCli(...results: CliResult[]): Cli & { calls: { args: readonly string[]; timeoutMs: number | undefined }[] } {
    const calls: { args: readonly string[]; timeoutMs: number | undefined }[] = [];
    return {
        calls,
        async run(args, options) {
            calls.push({ args, timeoutMs: options?.timeoutMs });
            return results.shift() ?? printed(report());
        },
    };
}

function setupOn(data: string, cli: Cli, over: Partial<ConstructorParameters<typeof MachineSetup>[0]> = {}) {
    const log = memoryLog();
    const setup = new MachineSetup({ cli, executable: macExe, version: '0.49.0', platform: 'darwin', offered: true, record: desktopPaths(data).machineSetup, terminalPath: async () => '/usr/bin:/bin', log, ...over });
    return { setup, log };
}

test('the routine is the Command Line item on macOS and Windows alone; Linux keeps its PATH command, and a development build runs nothing', () => {
    assert.equal(commandIsSetup('darwin'), true);
    assert.equal(commandIsSetup('win32'), true);
    assert.equal(commandIsSetup('linux'), false, 'Linux keeps today’s Command Line item and path-profile.ts');
    assert.equal(setupOffered('darwin', true), true);
    assert.equal(setupOffered('win32', true), true);
    assert.equal(setupOffered('linux', true), false);
    assert.equal(setupOffered('darwin', false), false, 'a development build’s jason is the checkout’s');
    assert.equal(setupOffered('win32', false), false);
});

test('on macOS the routine is told the login shell’s own PATH, or the empty value where the shell gave none; Windows is told none', () => {
    assert.deepEqual(setupArguments('darwin', '/usr/bin:/bin'), ['install', '--terminal-path', '/usr/bin:/bin']);
    assert.deepEqual(setupArguments('darwin', null), ['install', '--terminal-path', '']);
    assert.deepEqual(setupArguments('win32', null), ['install']);
    assert.deepEqual(setupArguments('win32', 'C:\\anything'), ['install'], 'Windows reads no --terminal-path');
});

test('a first start runs the routine once, with the shell’s PATH and no --roles, and its record leaves the data directory new for the Demo', async () => {
    const home = folder();
    const data = join(home, '.jason');
    // A new installation's first start writes its mode record as the demo, then runs the routine.
    assert.equal(keepFirstStart(desktopPaths(data).mode, isNewInstallation(data), false), true);
    const cli = fakeCli(printed(report()));
    const { setup } = setupOn(data, cli, { terminalPath: async () => '/usr/bin:/bin:/usr/sbin' });
    await setup.atStart();
    assert.deepEqual(cli.calls, [{ args: ['install', '--terminal-path', '/usr/bin:/bin:/usr/sbin'], timeoutMs: setupLimitMs }]);
    assert.equal(cli.calls[0]?.args.includes('--roles'), false);
    assert.equal(readSetupRecord(desktopPaths(data).machineSetup).ranFor, '0.49.0');
    // What is under the data directory now is the desktop's own records alone: the installation is still new, and its
    // record still says Demo.
    assert.deepEqual(readdirSync(data), ['desktop']);
    assert.deepEqual(readdirSync(join(data, 'desktop')).sort(), ['machine-setup.json', 'mode']);
    assert.equal(isNewInstallation(data), true);
    assert.equal(readMode(desktopPaths(data).mode), 'demo');
    assert.deepEqual(readdirSync(home), ['.jason'], 'nothing else beside the data directory');
});

test('a start of the version the routine last ran for runs nothing, and shows what that run reported', async () => {
    const data = join(folder(), '.jason');
    await setupOn(data, fakeCli(printed(report()))).setup.atStart();
    const again = fakeCli();
    const { setup } = setupOn(data, again);
    await setup.atStart();
    await setup.settled();
    assert.deepEqual(again.calls, []);
    assert.equal(setup.state.state, 'reported');
    assert.equal(setup.state.state === 'reported' && setup.state.report.command?.runs, '/Users/a/.local/bin/jason');
});

test('a start of another version runs it again, an older and a newer record alike', async () => {
    for (const before of ['0.48.2', '0.50.0']) {
        const data = join(folder(), '.jason');
        await setupOn(data, fakeCli(printed(report({ version: before }))), { version: before }).setup.atStart();
        const cli = fakeCli(printed(report()));
        await setupOn(data, cli).setup.atStart();
        assert.equal(cli.calls.length, 1, before);
        assert.equal(readSetupRecord(desktopPaths(data).machineSetup).ranFor, '0.49.0');
    }
});

test('a run that reports a problem writes no record, says what was not placed, offers put back and is not tried again', async () => {
    const data = join(folder(), '.jason');
    const failing = report({ command: null, problems: ['The profile /Users/a/.zshrc is a link to a folder, so no line was added: add export PATH="/Users/a/.local/bin:$PATH" by hand.'], completed: false });
    const cli = fakeCli(printed(failing, 1));
    const { setup } = setupOn(data, cli);
    await setup.atStart();
    await setup.atStart();
    assert.equal(cli.calls.length, 1, 'nothing is retried in a loop: once a start');
    assert.equal(existsSync(desktopPaths(data).machineSetup), false, 'the record is written only on success');
    const menu = setupMenuOf(setup.state, macExe);
    assert.ok(menu.lines.includes('jason was not placed where a terminal finds it.'), menu.lines.join('\n'));
    assert.ok(menu.lines.some((line) => line.startsWith('Not done: The profile /Users/a/.zshrc is a link')));
    assert.deepEqual(menu.acts, [{ label: putBackLabel, act: 'put_back' }]);
    // The next start tries once more, since no record says it ran.
    const next = fakeCli(printed(report()));
    await setupOn(data, next).setup.atStart();
    assert.equal(next.calls.length, 1);
});

test('a refusal, an answer that is no report and a run that could not end are said as the routine’s own sentence or why; no record', async () => {
    const cases: [CliResult, RegExp][] = [
        [printed({ error: { code: 'install_not_carried', message: 'This jason is not the one Jason Desktop carries.', retryable: false } }, 1), /^This jason is not the one Jason Desktop carries\.$/],
        [{ exit: 1, stdout: 'not json', stderr: '' }, /exited 1 and printed no report/],
        [{ exit: -1, stdout: '', stderr: 'spawn ENOENT' }, /could not be started, or did not end within 5 minutes/],
        [printed({ version: '0.49.0' }, 0), /exited 0 and printed no report/],
    ];
    for (const [result, said] of cases) {
        const data = join(folder(), '.jason');
        const { setup } = setupOn(data, fakeCli(result));
        await setup.atStart();
        assert.equal(setup.state.state, 'refused');
        assert.match(setup.state.state === 'refused' ? setup.state.why : '', said);
        assert.equal(existsSync(desktopPaths(data).machineSetup), false);
        const menu = setupMenuOf(setup.state, macExe);
        assert.ok(menu.lines.some((line) => line.startsWith('The set-up did not finish: ')), menu.lines.join('\n'));
        assert.deepEqual(menu.acts.map((act) => act.act), ['put_back']);
    }
});

test('a development build, and a bundle with no version, run nothing and offer nothing to run', async () => {
    const data = join(folder(), '.jason');
    const cli = fakeCli();
    const dev = setupOn(data, cli, { offered: false }).setup;
    await dev.atStart();
    assert.equal(dev.state.state, 'unoffered');
    assert.deepEqual(setupMenuOf(dev.state, macExe).acts, []);
    assert.equal(await dev.putBack(), dev.state, 'put back runs nothing where it is not offered');
    const unversioned = setupOn(data, cli, { version: null }).setup;
    await unversioned.atStart();
    assert.deepEqual(cli.calls, []);
    assert.equal(existsSync(join(data, 'desktop')), false);
});

test('the start’s run is waited on by settled(), and a start that runs nothing is settled at once', async () => {
    const data = join(folder(), '.jason');
    let finish: (result: CliResult) => void = () => undefined;
    const cli: Cli = { run: () => new Promise((resolve) => (finish = resolve)) };
    const { setup } = setupOn(data, cli);
    const started = setup.atStart();
    let settled = false;
    const waiting = setup.settled().then(() => (settled = true));
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(settled, false);
    assert.equal(setup.state.state, 'running');
    assert.deepEqual(setupMenuOf(setup.state, macExe).acts, [], 'nothing to press while it runs');
    finish(printed(report()));
    await started;
    await waiting;
    assert.equal(settled, true);
    // Nothing to run: settled at once.
    const idle = setupOn(join(folder(), '.jason'), fakeCli(), { offered: false }).setup;
    await idle.settled();
});

test('put back runs the routine again on the person’s word, one run at a time, writes the record on success and tells the changed menu', async () => {
    const data = join(folder(), '.jason');
    const failing = report({ problems: ['x'], completed: false });
    let finish: (result: CliResult) => void = () => undefined;
    const runs: (readonly string[])[] = [];
    let first = true;
    const cli: Cli = {
        run: (args) => {
            runs.push(args);
            if (first) {
                first = false;
                return Promise.resolve(printed(failing, 1));
            }
            return new Promise((resolve) => (finish = resolve));
        },
    };
    const { setup } = setupOn(data, cli);
    let changes = 0;
    setup.onChanged(() => void changes++);
    await setup.atStart();
    const pressed = setup.putBack();
    const twice = setup.putBack();
    await new Promise((resolve) => setImmediate(resolve));
    finish(printed(report()));
    const [now, other] = await Promise.all([pressed, twice]);
    assert.equal(runs.length, 2, 'the start’s run and one put back; the second press ran nothing');
    assert.equal(now.state, 'reported');
    assert.equal(other, now);
    assert.equal(readSetupRecord(desktopPaths(data).machineSetup).ranFor, '0.49.0');
    assert.ok(changes >= 3, `the menu was told at each change, ${changes}`);
});

test('the item shows the report as facts a person reads: where a terminal finds jason, the skills by program, what was left and why; never JSON', async () => {
    const data = join(folder(), '.jason');
    const { setup } = setupOn(data, fakeCli(printed(report())));
    await setup.atStart();
    const menu = setupMenuOf(setup.state, macExe);
    assert.deepEqual(menu.lines, [
        `The jason executable: ${macExe}`,
        'A new terminal finds jason through /Users/a/.local/bin/jason.',
        'A terminal that was already open finds it by its full path only: /Users/a/.local/bin/jason.',
        'Jason’s skills for Claude Code: /Users/a/.claude/skills',
        'Jason’s skills for Codex: /Users/a/.agents/skills',
        '/Users/a/.agents/skills/sdr-operations is a skill under a name Jason ships that no record of Jason’s names there, so it is yours: left as it is.',
    ]);
    for (const line of menu.lines) assert.equal(/[{}[\]]|"\w+":/.test(line), false, line);
    // Windows: the folder on the Path.
    const windows: SetupState = { state: 'reported', report: { version: '0.49.0', executable: 'C:\\J\\resources\\bundle\\jason.exe', command: { runs: 'C:\\J\\resources\\bundle', openShell: 'C:\\J\\resources\\bundle\\jason.exe', changed: true, profiles: [], takenBack: ['C:\\W\\jason.cmd'] }, skills: [], done: [], left: ['No Claude Code (~/.claude) or Codex (~/.codex) was found, so no harness skills were deployed; a run after one is installed deploys them.'], problems: [], completed: true } };
    assert.ok(setupMenuOf(windows, 'C:\\J\\resources\\bundle\\jason.exe').lines.includes('A new terminal finds jason through C:\\J\\resources\\bundle.'));
});

test('put back asks first, naming what it writes on each system, and tells what came of it in the routine’s own sentences', async () => {
    const mac = putBackQuestion('darwin', macExe);
    assert.equal(mac.button, 'Put It Back');
    assert.match(mac.detail, /~\/\.local\/bin\/jason/);
    assert.match(mac.detail, /Claude Code and Codex/);
    assert.match(mac.detail, /older command-line install/);
    const windows = putBackQuestion('win32', 'C:\\J\\jason.exe');
    assert.match(windows.detail, /Path/);
    assert.match(windows.detail, /WindowsApps/);
    const data = join(folder(), '.jason');
    const { setup } = setupOn(data, fakeCli(printed(report())));
    await setup.atStart();
    const told = putBackTold(setup.state);
    assert.equal(told.type, 'info');
    assert.ok(told.detail.startsWith('Wrote /Users/a/.local/bin/jason'));
    const failed = putBackTold({ state: 'refused', why: 'It said no.' });
    assert.equal(failed.type, 'warning');
    assert.equal(failed.detail, 'It said no.');
    const partly = putBackTold({ state: 'reported', report: { ...(setup.state.state === 'reported' ? setup.state.report : ({} as never)), problems: ['p'], completed: false } });
    assert.equal(partly.type, 'warning');
    assert.ok(partly.detail.startsWith('Not done: p'));
});

test('the log says why the routine ran, its exit and whether it completed: no path, no sentence of its report', async () => {
    const data = join(folder(), '.jason');
    const { setup, log } = setupOn(data, fakeCli(printed(report())));
    await setup.atStart();
    assert.deepEqual(log.lines, [{ level: 'info', name: 'machine_setup_ran', fields: { Why: 'start', Exit: 0, Completed: true } }]);
    // A record that cannot be written is said by the version alone.
    const blocked = join(folder(), '.jason');
    writeFileSync(blocked, '');
    const failing = setupOn(blocked, fakeCli(printed(report())));
    await failing.setup.atStart();
    assert.deepEqual(failing.log.lines.at(-1), { level: 'warn', name: 'machine_setup_unrecorded', fields: { Version: '0.49.0' } });
});

test('a record that is no record reads as none, so the next start runs the routine', () => {
    const data = join(folder(), '.jason');
    mkdirSync(join(data, 'desktop'), { recursive: true });
    for (const text of ['', 'not json', '[]', '{"ran_for":7}', '{"ran_for":"0.49.0","report":"x"}']) {
        writeFileSync(desktopPaths(data).machineSetup, text);
        assert.deepEqual(readSetupRecord(desktopPaths(data).machineSetup), { ranFor: null, report: null }, text);
    }
    assert.deepEqual(readSetupRecord(join(data, 'none.json')), { ranFor: null, report: null });
    writeFileSync(desktopPaths(data).machineSetup, JSON.stringify({ ran_for: '0.49.0', report: report() }));
    assert.equal(readSetupRecord(desktopPaths(data).machineSetup).ranFor, '0.49.0');
    assert.equal(readFileSync(desktopPaths(data).machineSetup, 'utf8').includes('"ran_for"'), true);
});

test('Open in’s look at jason on macOS and Windows: this window’s, a wrapper of ours, another, or none, with no act the menu no longer has', () => {
    const wrapper = '/Users/a/.local/bin/jason';
    const look = (found: string | null, text: string | null, dataDirectory: string | null = null) =>
        jasonOnPath({ platform: 'darwin', path: '/Users/a/.local/bin:/usr/bin', executable: macExe, dataDirectory, isProgram: (file) => file === found, read: () => text });
    assert.deepEqual(look(null, null), { found: 'none' });
    assert.deepEqual(look(wrapper, wrapperScript(macExe)), { found: 'this' });
    assert.deepEqual(look(wrapper, wrapperScript(macExe), '/elsewhere'), { found: 'other' }, 'a wrapper naming another data directory');
    assert.deepEqual(look(wrapper, wrapperScript('/Applications/Old.app/Contents/Resources/bundle/jason')), { found: 'other' });
    assert.deepEqual(look(wrapper, 'binary'), { found: 'other' });
    const exe = 'C:\\Users\\a\\AppData\\Local\\Programs\\jason-desktop\\resources\\bundle\\jason.exe';
    assert.deepEqual(jasonOnPath({ platform: 'win32', path: 'C:\\Users\\a\\AppData\\Local\\Programs\\jason-desktop\\resources\\bundle', executable: exe, dataDirectory: null, isProgram: (file) => file.toLowerCase() === exe.toLowerCase(), read: () => null }), { found: 'this' });
});

test('on macOS the update item says where Jason is and whether an update can write there, by the test Squirrel itself makes', () => {
    const app = '/Applications/Jason.app';
    assert.equal(placeLine('darwin', app, () => true), 'Jason is in /Applications/Jason.app, which an update can write to.');
    assert.equal(placeLine('darwin', app, (path) => path !== '/Applications'), 'Jason is in /Applications/Jason.app, which this account cannot write to, so an update asks for an administrator’s password.');
    assert.equal(placeLine('darwin', app, (path) => path !== app), 'Jason is in /Applications/Jason.app, which this account cannot write to, so an update asks for an administrator’s password.');
    assert.equal(placeLine('win32', 'C:\\J', () => false), null);
    assert.equal(placeLine('linux', '/opt/Jason', () => false), null);
});
