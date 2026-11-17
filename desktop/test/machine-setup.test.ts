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
    readHead,
    readSetupRecord,
    liveWaitMs,
    noLimit,
    reportLines,
    reportOf,
    runtimeDoor,
    runtimeLines,
    setupArguments,
    setupLongMs,
    setupMenuOf,
    setupOffered,
    takingLongSentence,
    type RuntimeDoor,
    type SetupState,
} from '../src/machine-setup.ts';
import { cliAt } from '../src/cli.ts';
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
        command: { runs: '/Users/a/.local/bin/jason', placed: true, open_shell: '/Users/a/.local/bin/jason', changed: true, profiles: ['/Users/a/.zprofile', '/Users/a/.zshrc'], taken_back: [] },
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

/** A runtime that never answers, which the rows that are not about the runtime are given. */
const noRuntime: RuntimeDoor = { answers: async () => false, start: async () => 'no runtime is started by this row' };

/** A runtime that answers as `answers` says in turn, the last answer repeated, and records what it was asked. */
function runtimeOf(answers: boolean[], started: string | null = null): RuntimeDoor & { asked: string[] } {
    const asked: string[] = [];
    return {
        asked,
        async answers() {
            asked.push('answers');
            return answers.length > 1 ? (answers.shift() ?? false) : (answers[0] ?? false);
        },
        async start() {
            asked.push('start');
            return started;
        },
    };
}

function setupOn(data: string, cli: Cli, over: Partial<ConstructorParameters<typeof MachineSetup>[0]> = {}) {
    const log = memoryLog();
    const setup = new MachineSetup({ cli, executable: macExe, version: '0.49.0', platform: 'darwin', offered: true, record: desktopPaths(data).machineSetup, terminalPath: async () => '/usr/bin:/bin', runtime: noRuntime, log, ...over });
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
    // The real door: with no descriptor it runs no jason to ask, so the routine is the one run.
    const { setup } = setupOn(data, cli, { terminalPath: async () => '/usr/bin:/bin:/usr/sbin', runtime: runtimeDoor(cli, desktopPaths(data).runFile) });
    await setup.atStart();
    assert.deepEqual(cli.calls, [{ args: ['install', '--terminal-path', '/usr/bin:/bin:/usr/sbin'], timeoutMs: noLimit }]);
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
    assert.ok(menu.lines.includes('jason is not yet where a new terminal finds it.'), menu.lines.join('\n'));
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
        [{ exit: -1, stdout: '', stderr: 'spawn ENOENT' }, /^jason install could not be started, or ended before it printed a report\.$/],
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
    const windows: SetupState = { state: 'reported', report: { version: '0.49.0', executable: 'C:\\J\\resources\\bundle\\jason.exe', command: { runs: 'C:\\J\\resources\\bundle', placed: true, openShell: 'C:\\J\\resources\\bundle\\jason.exe', changed: true, profiles: [], takenBack: ['C:\\W\\jason.cmd'] }, skills: [], runtime: 'not_running', done: [], left: ['No Claude Code (~/.claude) or Codex (~/.codex) was found, so no harness skills were deployed; a run after one is installed deploys them.'], problems: [], completed: true } };
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

// What runs around Electron's own start cannot be run here: how a start reaches the routine is held on the main module's text.
test('a windowed start runs the routine before the Demo or Live run goes on, and Live waits for it at most the bound before it brings the installation up', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const start = main.slice(main.indexOf('function startDesktop('));
    const lock = start.indexOf('const early = startLog();');
    const made = start.indexOf('machineSetup = makeMachineSetup(early);');
    const ran = start.indexOf('void app.whenReady().then(() => machineSetup?.atStart());');
    const chosen = start.indexOf("void app.whenReady().then(() => (runOn.mode === 'demo' ? runDemo(links) : run(links, installationWasNew)));");
    assert.ok(lock > 0 && made > lock && ran > made && chosen > ran, 'made once the lock is held, and run before the mode’s run');
    // The installer's, the uninstaller's and a terminal's update starts run none: the routine is made in the windowed branch alone.
    assert.equal(main.split('makeMachineSetup(').length - 1, 2, 'declared once, called once');
    const run = main.slice(main.indexOf('async function run('), main.indexOf('async function runDemo('));
    const waited = run.indexOf('await machineSetup?.settledWithin(liveWaitMs);');
    assert.equal(run.includes('await machineSetup?.settled();\n\n    try {'), false, 'never the whole run');
    assert.ok(waited > run.indexOf("await showStarting(window, 'Starting Jason…', false);") && waited < run.indexOf('const started = await startInstallation({'), 'the window is up first, and the installation after the routine');
    // The Demo's run itself still makes no jason and touches no set-up: the one start a Demo start may make, of a runtime the
    // routine left down, is the set-up's own, through the door the factory gives it on the person's descriptor.
    const demo = main.slice(main.indexOf('async function runDemo('), main.indexOf('function keepElectronUnderData('));
    for (const never of ['machineSetup', 'cliAt(', 'MachineSetup', 'runtimeDoor(']) assert.equal(demo.includes(never), false, never);
    assert.equal(main.split('runtimeDoor(').length - 1, 1, 'one door, the factory’s');
    // The routine is told the login shell's PATH from the one read every start shares, never the desktop's own.
    const factory = main.slice(main.indexOf('function makeMachineSetup('), main.indexOf('function makeMachineSetup(') + 1500);
    assert.ok(factory.includes('terminalPath: () => askedShell(),'));
    assert.ok(factory.includes('runtime: runtimeDoor(cli, desktopPaths(runOn.person).runFile),'));
    assert.ok(run.includes('const shellRead = askedShell();'));
    assert.equal(main.includes('shellPath()'), true);
    assert.equal(main.split('shellPath()').length - 1, 1, 'the login shell is asked once a start');
});

test('on Linux the PATH command and its menu stay as they are; on macOS and Windows the item is the routine’s status', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    assert.ok(main.includes('const command = offers.command && carried.outlives && !commandIsSetup(process.platform) ? new PathCommand(commandDeps) : null;'));
    const desk = readFileSync(new URL('../src/desk.ts', import.meta.url), 'utf8');
    const items = desk.slice(desk.indexOf('private commandItems('), desk.indexOf('private async commandAct('));
    // The routine's item where a set-up is given, and the PATH command's own, exactly as before, where none is.
    assert.ok(items.indexOf('if (this.deps.setup !== null) return this.setupItems(this.deps.setup);') < items.indexOf('const command = this.deps.command;'));
    assert.ok(items.includes('const menu = commandMenuOf(status);'));
    assert.ok(main.includes('setup: offers.command && carried.outlives ? machineSetup : null,'));
});

test('nothing on macOS or Windows places the PATH command any more: the WindowsApps shims and the 0.48 wrapper are jason install’s to take back', () => {
    const sources = readdirSync(new URL('../src/', import.meta.url)).filter((name) => name.endsWith('.ts'));
    // The one act that writes a command is the Linux menu's, through the one PathCommand a start makes, which only a
    // system whose item is not the routine's makes; the others only look, remove, or re-point an AppImage's copy.
    const placing = sources.filter((name) => readFileSync(new URL(`../src/${name}`, import.meta.url), 'utf8').includes('.place()'));
    assert.deepEqual(placing, ['desk.ts']);
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const made = [...main.matchAll(/new PathCommand\(/g)].length;
    assert.equal(made, 2, 'the menu’s, behind !commandIsSetup, and Remove What Jason Placed’s, which only removes');
    assert.ok(main.includes('if (otherCopy !== null && command !== null) {'), 'a re-point needs the menu’s command, which macOS and Windows do not make');
});

test('the look reads only the head of what the PATH finds: a wrapper whole, an executable never', () => {
    const at = folder();
    const wrapper = join(at, 'jason');
    writeFileSync(wrapper, wrapperScript(macExe));
    assert.equal(readHead(wrapper), wrapperScript(macExe));
    const big = join(at, 'big');
    writeFileSync(big, Buffer.alloc(100_000, 0x41));
    assert.equal(readHead(big)?.length, 4096);
    assert.equal(readHead(join(at, 'missing')), null);
});

test('the finishing step’s seam: run after a routine that completed, and the record written only when both did; no step given, the routine alone decides', async () => {
    // A routine that completed and a finishing step that did not: no record, and the item says what was not done.
    const data = join(folder(), '.jason');
    const order: string[] = [];
    const cli: Cli = { run: async () => (order.push('install'), printed(report())) };
    const failing = setupOn(data, cli, { finishingStep: async () => (order.push('finish'), { completed: false, problems: ['The plugins were not brought up to 0.49.0.'] }) });
    await failing.setup.atStart();
    assert.deepEqual(order, ['install', 'finish']);
    assert.equal(existsSync(desktopPaths(data).machineSetup), false, 'done for this version only when both reported success');
    assert.ok(setupMenuOf(failing.setup.state, macExe).lines.includes('Not done: The plugins were not brought up to 0.49.0.'));
    assert.deepEqual(failing.log.lines.at(-1), { level: 'info', name: 'machine_setup_finished', fields: { Why: 'start', Completed: false } });
    // The next start tries both again, and with both done the record is written.
    const next = setupOn(data, fakeCli(printed(report())), { finishingStep: async () => ({ completed: true, problems: [] }) });
    await next.setup.atStart();
    assert.equal(readSetupRecord(desktopPaths(data).machineSetup).ranFor, '0.49.0');
    // A routine that did not complete runs no finishing step.
    let finished = 0;
    const partly = setupOn(join(folder(), '.jason'), fakeCli(printed(report({ problems: ['x'], completed: false }), 1)), { finishingStep: async () => (finished++, { completed: true, problems: [] }) });
    await partly.setup.atStart();
    assert.equal(finished, 0);
    // A finishing step that throws is a step that did not complete.
    const thrown = setupOn(join(folder(), '.jason'), fakeCli(printed(report())), { finishingStep: async () => { throw new Error('gone'); } });
    await thrown.setup.atStart();
    assert.equal(thrown.setup.state.state, 'reported');
    assert.ok(setupMenuOf(thrown.setup.state, macExe).lines.includes('Not done: The finishing step did not run: gone'));
});

test('main gives the set-up no finishing step yet: the seam waits for the command line’s one, and no verb is stubbed', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const factory = main.slice(main.indexOf('function makeMachineSetup('), main.indexOf('function makeMachineSetup(') + 1500);
    assert.equal(factory.includes('finishingStep'), false);
    assert.equal(main.includes("'--finish'"), false);
});

const tick = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** A CLI whose routine runs until `finish` is called; every other verb answers at once. */
function hangingCli(): Cli & { calls: { args: readonly string[]; timeoutMs: number | undefined }[]; finish(result: CliResult): void } {
    const calls: { args: readonly string[]; timeoutMs: number | undefined }[] = [];
    let finish: (result: CliResult) => void = () => undefined;
    return {
        calls,
        finish: (result) => finish(result),
        run(args, options) {
            calls.push({ args, timeoutMs: options?.timeoutMs });
            return new Promise((resolve) => (finish = resolve));
        },
    };
}

test('M1: Live waits for the start’s run at most the bound and goes on; the run is never ended, and past five minutes the item says it is taking long', async () => {
    assert.equal(liveWaitMs, 30_000, 'thirty seconds, not five minutes');
    assert.equal(setupLongMs, 300_000);
    assert.equal(noLimit, 0);
    const data = join(folder(), '.jason');
    const cli = hangingCli();
    const { setup } = setupOn(data, cli, { longAfterMs: 40 });
    void setup.atStart();
    // The bound passes with the routine still running: Live is released, and the run goes on.
    assert.equal(await setup.settledWithin(20), 'running');
    assert.equal(setup.state.state, 'running');
    assert.deepEqual(cli.calls.map((call) => call.timeoutMs), [noLimit], 'the routine is given no time limit, so nothing ends it');
    assert.deepEqual(setupMenuOf(setup.state, macExe).lines.slice(1), ['Putting jason where a terminal finds it, and the skills where your agents read them…']);
    await tick(60);
    assert.deepEqual(setup.state, { state: 'running', long: true });
    assert.deepEqual(setupMenuOf(setup.state, macExe).lines.slice(1), [takingLongSentence], 'taking long, nothing more');
    assert.deepEqual(setupMenuOf(setup.state, macExe).acts, []);
    // It ends late, and what it reported is the item's as a report in time would have been.
    cli.finish(printed(report()));
    assert.equal(await setup.settledWithin(20), 'ended');
    assert.equal(setup.state.state, 'reported');
    assert.equal(readSetupRecord(desktopPaths(data).machineSetup).ranFor, '0.49.0');
    // A start that runs nothing is settled within any bound at once.
    assert.equal(await setupOn(join(folder(), '.jason'), fakeCli(), { offered: false }).setup.settledWithin(1), 'ended');
});

test('M1: the carried jason is started with no time limit, and a child that runs past what a limit would allow ends on its own', async () => {
    const cli = cliAt(process.execPath, { ...(process.env as Record<string, string>) }, ['-e', 'setTimeout(() => process.stdout.write("done"), 300)']);
    const result = await cli.run([], { timeoutMs: noLimit });
    assert.deepEqual([result.exit, result.stdout], [0, 'done']);
    // The same child under a limit is ended by Node, which is what the routine is never given.
    const limited = await cli.run([], { timeoutMs: 50 });
    assert.equal(limited.exit === 0 && limited.stdout === 'done', false);
});

test('M2+M4: a runtime that answered before the routine and not after it is started again from the carried file, after a report that says it was stopped', async () => {
    const data = join(folder(), '.jason');
    const runtime = runtimeOf([true, false]);
    const { setup, log } = setupOn(data, fakeCli(printed(report({ retired: { executable: '/Users/a/.local/bin/jason', aside: null, removed: true, reregistered: true }, runtime: 'stopped' }))), { runtime });
    await setup.atStart();
    assert.deepEqual(runtime.asked, ['answers', 'answers', 'start']);
    assert.equal(setup.state.state === 'reported' && setup.state.report.runtime, 'stopped', 'the report’s runtime is read');
    assert.deepEqual(setup.state.state === 'reported' ? setup.state.runtimeAfter : null, { act: 'started' });
    assert.ok(setupMenuOf(setup.state, macExe).lines.includes('The set-up stopped the runtime of the older command-line install; Jason Desktop started one again from its own jason.'));
    assert.deepEqual(log.lines.at(-1), { level: 'info', name: 'machine_setup_runtime', fields: { Why: 'start', Started: true } });
});

test('M2+M4: the same rule after a refusal, no report and a routine that ended with nothing: one runtime started, and said', async () => {
    const outcomes: CliResult[] = [
        printed({ error: { code: 'install_not_carried', message: 'No.', retryable: false } }, 1),
        { exit: 1, stdout: 'not json', stderr: '' },
        { exit: -1, stdout: '', stderr: 'killed' },
    ];
    for (const outcome of outcomes) {
        const runtime = runtimeOf([true, false]);
        const { setup } = setupOn(join(folder(), '.jason'), fakeCli(outcome), { runtime });
        await setup.atStart();
        assert.deepEqual(runtime.asked, ['answers', 'answers', 'start'], outcome.stdout);
        assert.equal(setup.state.state, 'refused');
        assert.ok(setupMenuOf(setup.state, macExe).lines.includes('The runtime that was running stopped while the set-up ran; Jason Desktop started one again from its own jason.'));
    }
});

test('M2+M4: none is started where none answered before, where one still answers after, or twice', async () => {
    for (const answers of [[false, false], [false, true], [true, true]]) {
        const runtime = runtimeOf([...answers]);
        const { setup } = setupOn(join(folder(), '.jason'), fakeCli(printed(report({ runtime: answers[0] === true ? 'running' : 'not_running' }))), { runtime });
        await setup.atStart();
        assert.equal(runtime.asked.includes('start'), false, answers.join());
        assert.equal(setup.state.state === 'reported' ? setup.state.runtimeAfter : 'none', undefined);
    }
});

test('M2: a stop that comes after Live went on is answered by the same code: the runtime started once the routine ends', async () => {
    const cli = hangingCli();
    const order: string[] = [];
    const runtime: RuntimeDoor = {
        answers: async () => (order.push('answers'), order.length === 1),
        start: async () => (order.push('start'), null),
    };
    const { setup } = setupOn(join(folder(), '.jason'), cli, { runtime });
    void setup.atStart();
    assert.equal(await setup.settledWithin(10), 'running');
    assert.deepEqual(order, ['answers'], 'nothing is started while the routine runs');
    cli.finish(printed(report({ runtime: 'stopped' })));
    await setup.settled();
    assert.deepEqual(order, ['answers', 'answers', 'start']);
    assert.deepEqual(setup.state.state === 'reported' ? setup.state.runtimeAfter : null, { act: 'started' });
});

test('M2: a start that fails is said with its own sentence and the verb that starts one, and Put Back warns', async () => {
    const runtime = runtimeOf([true, false], 'The port is taken.');
    const { setup } = setupOn(join(folder(), '.jason'), fakeCli(printed(report({ runtime: 'stopped' }))), { runtime });
    await setup.atStart();
    assert.ok(setupMenuOf(setup.state, macExe).lines.includes('The set-up stopped the runtime of the older command-line install, and starting one again did not work: The port is taken. `jason runtime start` starts one.'));
    const told = putBackTold(setup.state);
    assert.equal(told.type, 'warning');
    assert.match(told.detail, /starting one again did not work/);
});

test('M2: Put Back is the same rule: a runtime Live ran on that the routine stops is started again', async () => {
    const runtime = runtimeOf([true, false]);
    const { setup } = setupOn(join(folder(), '.jason'), fakeCli(printed(report({ runtime: 'stopped' }))), { runtime });
    const after = await setup.putBack();
    assert.deepEqual(runtime.asked, ['answers', 'answers', 'start']);
    assert.deepEqual(after.state === 'reported' ? after.runtimeAfter : null, { act: 'started' });
    const told = putBackTold(after);
    assert.equal(told.type, 'info');
    assert.match(told.detail, /Jason Desktop started one again from its own jason\./);
});

test('M4: a Demo start with a retired install and a running runtime starts a runtime at once; the Demo’s record and samples are untouched', async () => {
    const data = join(folder(), '.jason');
    // An installation that exists, its record set to the demo, an old command-line runtime up on its directory.
    mkdirSync(join(data, 'run'), { recursive: true });
    writeFileSync(desktopPaths(data).runFile, '{}');
    mkdirSync(join(data, 'desktop'), { recursive: true });
    writeFileSync(desktopPaths(data).mode, 'demo\n');
    let up = true;
    const calls: (readonly string[])[] = [];
    const cli: Cli = {
        async run(args) {
            calls.push(args);
            if (args[0] === 'runtime' && args[1] === 'status') return { exit: up ? 0 : 3, stdout: '{}', stderr: '' };
            if (args[0] === 'runtime' && args[1] === 'start') return (up = true), { exit: 0, stdout: '{}', stderr: '' };
            up = false;
            return printed(report({ retired: { executable: '/Users/a/.local/bin/jason', aside: null, removed: true, reregistered: true }, runtime: 'stopped' }));
        },
    };
    const { setup } = setupOn(data, cli, { runtime: runtimeDoor(cli, desktopPaths(data).runFile) });
    await setup.atStart();
    assert.deepEqual(calls, [['runtime', 'status'], ['install', '--terminal-path', '/usr/bin:/bin'], ['runtime', 'status'], ['runtime', 'start']]);
    assert.equal(up, true, 'the person’s runtime is up again');
    assert.equal(readMode(desktopPaths(data).mode), 'demo');
});

test('M2: the door asks nothing where there is no descriptor, reads a runtime by `jason runtime status` where there is, and starts one by `jason runtime start`', async () => {
    const data = join(folder(), '.jason');
    const cli = fakeCli({ exit: 0, stdout: '{}', stderr: '' }, { exit: 3, stdout: '{}', stderr: '' }, { exit: 1, stdout: JSON.stringify({ error: { code: 'x', message: 'It would not start.', retryable: false } }), stderr: '' }, { exit: 1, stdout: '', stderr: '' }, { exit: 0, stdout: '', stderr: '' });
    const door = runtimeDoor(cli, desktopPaths(data).runFile);
    assert.equal(await door.answers(), false);
    assert.equal(cli.calls.length, 0, 'no descriptor, no data directory: nothing is run to ask');
    mkdirSync(join(data, 'run'), { recursive: true });
    writeFileSync(desktopPaths(data).runFile, '{}');
    assert.equal(await door.answers(), true);
    assert.equal(await door.answers(), false, 'unreachable is none');
    assert.equal(await door.start(), 'It would not start.');
    assert.equal(await door.start(), 'jason runtime start exited 1.');
    assert.equal(await door.start(), null);
    assert.deepEqual(cli.calls.map((call) => call.args.join(' ')), ['runtime status', 'runtime status', 'runtime start', 'runtime start', 'runtime start']);
    assert.ok(cli.calls.every((call) => call.timeoutMs !== undefined && call.timeoutMs > 0), 'the read and the start are bounded; only the routine is not');
    assert.deepEqual(readdirSync(data), ['run'], 'asking wrote nothing');
});

test('M3: the item says a new terminal finds jason only where the routine read the command back as placed', () => {
    const notPlaced = reportOf(report({ command: { runs: '/Users/a/.local/bin/jason', placed: false, open_shell: '/Users/a/.local/bin/jason', changed: true, profiles: [], taken_back: [] }, problems: ['The profile /Users/a/.zshrc is not writable, so no line was added.'], completed: false }));
    assert.ok(notPlaced !== null);
    const lines = reportLines(notPlaced);
    assert.equal(lines[0], 'jason is not yet where a new terminal finds it.');
    assert.equal(lines.some((line) => line.includes('finds jason')), false, lines.join('\n'));
    assert.ok(lines.includes('Not done: The profile /Users/a/.zshrc is not writable, so no line was added.'), 'what stopped it is under it');
    // Windows: a Path entry that was not written.
    const windows = reportOf(report({ command: { runs: 'C:\\J\\resources\\bundle', placed: false, open_shell: 'C:\\J\\resources\\bundle\\jason.exe', changed: false, profiles: [], taken_back: [] }, problems: ['The Path could not be written.'], completed: false }));
    assert.equal(windows !== null && reportLines(windows)[0], 'jason is not yet where a new terminal finds it.');
    const placed = reportOf(report());
    assert.equal(placed !== null && reportLines(placed)[0], 'A new terminal finds jason through /Users/a/.local/bin/jason.');
    // A document without the member is no report: the carried jason prints it.
    const without = report();
    delete (without.command as Record<string, unknown>).placed;
    assert.equal(reportOf(without), null);
    assert.equal(reportOf({ ...report(), runtime: undefined }), null);
});

test('N1: the skills are named by the hosts’ display names the desktop already shows, and a word it has none for is left as it is', () => {
    const named = reportOf(report({ skills: [{ root: '/Users/a/.agents/skills', hosts: ['codex', 'copilot_cli', 'cursor_cli', 'gemini_cli', 'antigravity_cli', 'other_cli'], written: 1, unchanged: 0, left_alone: [] }] }));
    assert.ok(named !== null);
    assert.ok(reportLines(named).includes('Jason’s skills for Codex and GitHub Copilot CLI and Cursor CLI and Gemini CLI and Antigravity CLI and other_cli: /Users/a/.agents/skills'));
    assert.deepEqual(runtimeLines(undefined, named), []);
});

test('N3: the logon start is moved from another copy only once the machine set-up has ended, never beside it', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const block = main.slice(main.indexOf('const repointed = (async () => {'), main.indexOf('const repointed = (async () => {') + 400);
    assert.ok(block.indexOf('await machineSetup?.settled();') > 0);
    assert.ok(block.indexOf('await machineSetup?.settled();') < block.indexOf('autostart?.repoint('));
});

test('a fresh machine holding only the set-up’s record still counts as new', () => {
    const data = join(folder(), '.jason');
    mkdirSync(join(data, 'desktop'), { recursive: true });
    writeFileSync(desktopPaths(data).machineSetup, JSON.stringify({ ran_for: '0.49.0', report: report() }));
    assert.deepEqual(readdirSync(data), ['desktop']);
    assert.equal(isNewInstallation(data), true);
});
