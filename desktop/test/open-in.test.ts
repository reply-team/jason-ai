import assert from 'node:assert/strict';
import { mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { invokeChannels, handle, RefusedSender, type IpcRegistrar } from '../src/ipc.ts';
import { defaultPlace, editorLine, fileManager, fileManagerLine, findEditor, OpenIn, placesFound, type Line, type OpenInDeps, type PlaceId, type Probes } from '../src/open-in.ts';
import { editorEnvironment, openInHarnessRoot } from '../src/open-in-env.ts';
import { enterRun, type PersonEnvironment } from '../src/mode.ts';
import { startDetached } from '../src/open-in-start.ts';
import { verbOf } from '../src/installation.ts';
import { continueFlags, terminalFlags } from '../src/open-in-terminal.ts';
import { continuableOf, type Conversation } from '../src/chat-conversations.ts';
import { parseSettings, readSettings, writeSettings } from '../src/settings.ts';

// Made-up machines: each probe answers from a list, so every system's detection is read without the system.

function probes(platform: NodeJS.Platform, present: string[], onPath: Record<string, string> = {}, env: Record<string, string> = {}): Probes {
    return {
        platform,
        env,
        home: platform === 'win32' ? 'C:\\Users\\ada' : '/home/ada',
        onPath: (command) => onPath[command] ?? null,
        exists: (path) => present.includes(path),
    };
}

const windows = probes('win32', ['C:\\Users\\ada\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe', 'C:\\Tools\\Zed\\Zed.exe'], { zed: 'C:\\Tools\\Zed\\bin\\zed.cmd' }, { LOCALAPPDATA: 'C:\\Users\\ada\\AppData\\Local', ProgramFiles: 'C:\\Program Files', SystemRoot: 'C:\\Windows' });
const mac = probes('darwin', ['/Applications/Visual Studio Code.app', '/home/ada/Applications/Cursor.app'], { windsurf: '/usr/local/bin/windsurf' });
const linux = probes('linux', [], { code: '/usr/bin/code', zeditor: '/usr/bin/zeditor', 'xdg-open': '/usr/bin/xdg-open' });

const ids = (list: { id: string }[]) => list.map((each) => each.id);

test('on Windows an editor is found by its own program under the programs folders, or beside the bin folder of a command on the PATH, and never by a batch file', () => {
    assert.deepEqual(findEditor('vscode', windows), { kind: 'program', path: 'C:\\Users\\ada\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe' });
    assert.deepEqual(findEditor('zed', windows), { kind: 'program', path: 'C:\\Tools\\Zed\\Zed.exe' });
    assert.equal(findEditor('cursor', windows), null);
    // A command whose program is not beside its bin folder is not an editor to start: the batch file is never started.
    assert.equal(findEditor('windsurf', probes('win32', [], { windsurf: 'C:\\Elsewhere\\bin\\windsurf.cmd' })), null);
    assert.equal(fileManager(windows), 'C:\\Windows\\explorer.exe');
});

test('on macOS an editor is an application in an Applications folder, else a command on the PATH, and the file manager is open', () => {
    assert.deepEqual(findEditor('vscode', mac), { kind: 'app', path: '/Applications/Visual Studio Code.app' });
    assert.deepEqual(findEditor('cursor', mac), { kind: 'app', path: '/home/ada/Applications/Cursor.app' });
    assert.deepEqual(findEditor('windsurf', mac), { kind: 'program', path: '/usr/local/bin/windsurf' });
    assert.equal(findEditor('zed', mac), null);
    assert.equal(fileManager(mac), '/usr/bin/open');
});

test('on Linux an editor is a command on the PATH, Zed under either of its names, and the file manager is xdg-open when there is one', () => {
    assert.deepEqual(findEditor('vscode', linux), { kind: 'program', path: '/usr/bin/code' });
    assert.deepEqual(findEditor('zed', linux), { kind: 'program', path: '/usr/bin/zeditor' });
    assert.equal(findEditor('antigravity', linux), null);
    assert.equal(fileManager(linux), '/usr/bin/xdg-open');
    assert.equal(fileManager(probes('linux', [])), null);
});

test('the places are the hosts the desktop finds, a terminal, the editors installed, the file manager and Copy path, in that order', () => {
    assert.deepEqual(ids(placesFound(windows, { claude: true, codex: true })), ['claude', 'codex', 'terminal', 'vscode', 'zed', 'files', 'copy']);
    assert.deepEqual(ids(placesFound(windows, { claude: false, codex: true })), ['codex', 'terminal', 'vscode', 'zed', 'files', 'copy']);
    assert.deepEqual(ids(placesFound(mac, { claude: true, codex: false })), ['claude', 'terminal', 'vscode', 'cursor', 'windsurf', 'files', 'copy']);
    assert.deepEqual(ids(placesFound(probes('linux', []), { claude: false, codex: false })), ['terminal', 'copy']);
    assert.deepEqual(placesFound(linux, { claude: true, codex: false }).find((place) => place.id === 'claude'), { id: 'claude', label: 'Claude Code' });
    // The generic places carry no label, and the page has its own words for them; a product's name is the desktop's.
    assert.equal(placesFound(windows, { claude: false, codex: false }).find((place) => place.id === 'terminal')?.label, '');
    assert.equal(placesFound(windows, { claude: false, codex: false }).find((place) => place.id === 'copy')?.label, '');
    // The file manager goes by the system's own name.
    assert.equal(placesFound(windows, { claude: false, codex: false }).find((place) => place.id === 'files')?.label, 'File Explorer');
    assert.equal(placesFound(mac, { claude: false, codex: false }).find((place) => place.id === 'files')?.label, 'Finder');
    assert.equal(placesFound(linux, { claude: false, codex: false }).find((place) => place.id === 'files')?.label, '');
});

test('the default is the place used last while it is here, else Claude Code, else the first place that opens something; never Copy path', () => {
    const all = placesFound(windows, { claude: true, codex: true });
    assert.equal(defaultPlace(all, null), 'claude');
    assert.equal(defaultPlace(all, 'vscode'), 'vscode');
    assert.equal(defaultPlace(all, 'cursor'), 'claude', 'a place no longer here is not the default');
    assert.equal(defaultPlace(all, 'copy'), 'claude');
    const none = placesFound(windows, { claude: false, codex: true });
    assert.equal(defaultPlace(none, null), 'codex');
    assert.equal(defaultPlace(placesFound(probes('linux', []), { claude: false, codex: false }), null), 'terminal');
});

test('an editor is started with the folder: its program, or on macOS open with the application', () => {
    assert.deepEqual(editorLine({ kind: 'program', path: 'C:\\Tools\\Zed\\Zed.exe' }, 'C:\\Jason'), { file: 'C:\\Tools\\Zed\\Zed.exe', args: ['C:\\Jason'] });
    assert.deepEqual(editorLine({ kind: 'app', path: '/Applications/Cursor.app' }, '/home/ada/Jason'), { file: '/usr/bin/open', args: ['-a', '/Applications/Cursor.app', '/home/ada/Jason'] });
});

test('the terminal entries name the folder by the campaign and the place by a host or a shell, in one function', () => {
    assert.deepEqual(terminalFlags('claude', null), ['--host', 'claude_code']);
    assert.deepEqual(terminalFlags('codex', 'cmp_01'), ['--campaign', 'cmp_01', '--host', 'codex']);
    assert.deepEqual(terminalFlags('terminal', 'cmp_01'), ['--campaign', 'cmp_01', '--shell']);
    assert.deepEqual(terminalFlags('terminal', null), ['--shell']);
});

// The whole act, over a made-up installation: what the CLI says, what is started, what the clipboard got, what was kept.

interface Bench {
    openIn: OpenIn;
    cliCalls: string[][];
    cwds: (string | undefined)[];
    started: Line[];
    copied: string[];
    kept: PlaceId[];
    flagsGiven: [string, string | null][];
    remembered: { value: PlaceId | null };
    logged: string[];
}

/** A conversation as the desktop's record keeps it; the chat's answer is read from these by `continuableOf`, as the chat reads it. */
const conversation = (id: string, fields: Partial<Conversation> = {}): Conversation => ({
    id,
    campaign_id: null,
    title: 'New chat',
    created_at: '2026-11-11T10:00:00.000Z',
    updated_at: '2026-11-11T10:00:00.000Z',
    sessions: {},
    entries: [],
    dropped: 0,
    ...fields,
});
const chatA = 'conv_01JD0000000000000000000001';
const chatB = 'conv_01JD0000000000000000000002';
const keptChats = [
    conversation(chatA, { sessions: { claude: 'session-a' }, plain_sessions: ['claude'], chosen: { host: 'claude', model: null, effort: null } }),
    conversation(chatB, { sessions: { codex: 'thread-b' }, plain_sessions: ['codex'], chosen: { host: 'codex', model: null, effort: null } }),
];

function bench(options: { probes?: Probes; hosts?: { claude: boolean; codex: boolean }; campaigns?: Record<string, unknown>; root?: string; folders?: string[]; failStart?: string; cliExit?: number; cliOut?: string; cliErr?: string; remembered?: PlaceId | null; chats?: Conversation[]; running?: string[]; chatFolder?: string } = {}): Bench {
    const cliCalls: string[][] = [];
    const cwds: (string | undefined)[] = [];
    const started: Line[] = [];
    const copied: string[] = [];
    const kept: PlaceId[] = [];
    const flagsGiven: [string, string | null][] = [];
    const logged: string[] = [];
    const remembered = { value: options.remembered ?? null };
    const campaigns = options.campaigns ?? { cmp_01: { folder_path: 'C:\\Jason\\campaigns\\spring' } };
    const folders = options.folders ?? ['C:\\Jason', 'C:\\Jason\\campaigns\\spring'];
    const deps: OpenInDeps = {
        probes: options.probes ?? windows,
        hosts: () => options.hosts ?? { claude: true, codex: true },
        cli: {
            run: (args, given) => {
                cliCalls.push([...args]);
                if (args[0] === 'session') cwds.push(given?.cwd);
                if (args[0] === 'campaign') {
                    const record = campaigns[args[2] ?? ''];
                    return Promise.resolve(record === undefined ? { exit: 1, stdout: '{"error":{"code":"not_found","message":"No such campaign."}}', stderr: '' } : { exit: 0, stdout: JSON.stringify(record), stderr: '' });
                }
                return Promise.resolve({ exit: options.cliExit ?? 0, stdout: options.cliOut ?? '{}', stderr: options.cliErr ?? '' });
            },
        },
        harnessRoot: () => options.root ?? 'C:\\Jason',
        isDirectory: (path) => folders.includes(path),
        remembered: () => remembered.value,
        remember: (id) => {
            kept.push(id);
            remembered.value = id;
        },
        start: (line) => {
            started.push(line);
            return Promise.resolve(options.failStart ?? null);
        },
        copy: (text) => void copied.push(text),
        terminalFlags: (kind, campaignId) => {
            flagsGiven.push([kind, campaignId]);
            return terminalFlags(kind, campaignId);
        },
        log: (event, fields) => void logged.push(`${event}:${fields.Place}`),
        // As the chat answers (`Chat.continuable`): the record's session by `continuableOf`, and whether a turn runs.
        continuable: (chatId) => {
            const kept = (options.chats ?? keptChats).find((each) => each.id === chatId);
            const found = kept === undefined ? null : continuableOf(kept);
            return found === null ? null : { ...found, running: (options.running ?? []).includes(kept!.id) };
        },
        chatFolder: () => options.chatFolder ?? 'C:\\data\\desktop\\chat',
        continueFlags,
        logContinue: (event, fields) => void logged.push(`${event}:${fields.Host}`),
    };
    return { openIn: new OpenIn(deps), cwds, cliCalls, started, copied, kept, flagsGiven, remembered, logged };
}

test('with no campaign the folder is the harness root, and the answer carries its name and not its path', async () => {
    const b = bench();
    const answer = await b.openIn.targets();
    assert.deepEqual(answer, { places: placesFound(windows, { claude: true, codex: true }), default: 'claude', folder: 'Jason' });
    assert.deepEqual(b.cliCalls, [], 'the harness root is read from the run, not asked of the runtime');
    assert.ok(!JSON.stringify(answer).includes('C:\\'), 'no path reaches the page');
});

test('with a campaign the folder is its recorded one, read from the runtime', async () => {
    const b = bench();
    assert.deepEqual((await b.openIn.targets('cmp_01') as { folder: string }).folder, 'spring');
    assert.deepEqual(b.cliCalls, [['campaign', 'get', 'cmp_01']]);
    assert.equal(await b.openIn.open('copy', 'cmp_01'), null);
    assert.deepEqual(b.copied, ['C:\\Jason\\campaigns\\spring']);
});

test('in the demo the folder is the demo installation’s harness root', async () => {
    const b = bench({ root: 'C:\\Jason-demo', folders: ['C:\\Jason-demo'] });
    assert.equal((await b.openIn.targets() as { folder: string }).folder, 'Jason-demo');
    assert.equal(await b.openIn.open('copy'), null);
    assert.deepEqual(b.copied, ['C:\\Jason-demo']);
});

test('a campaign that is not known, or whose folder is not recorded or not on this computer, or an id of no shape, is refused and nothing is started', async () => {
    const b = bench({ campaigns: { cmp_02: { folder_path: null }, cmp_03: { folder_path: 'C:\\Gone' }, cmp_04: { folder_path: 'relative\\dir' } } });
    for (const id of ['cmp_01', 'cmp_02', 'cmp_03', 'cmp_04']) {
        const answer = await b.openIn.open('vscode', id);
        assert.match((answer as { refused: string }).refused, id === 'cmp_01' ? /not in this installation/ : /folder is not recorded|not on this computer/, id);
        assert.match(((await b.openIn.targets(id)) as { refused: string }).refused, id === 'cmp_01' ? /not in this installation/ : /not recorded/, id);
    }
    const callsBefore = b.cliCalls.length;
    for (const id of ['../x', 'cmp_', 'C:\\Windows', 7, {}]) {
        assert.match(((await b.openIn.open('vscode', id)) as { refused: string }).refused, /not an agent this desktop knows/);
    }
    assert.equal(b.cliCalls.length, callsBefore, 'an id of no shape is never asked of the runtime');
    assert.deepEqual(b.started, []);
    assert.deepEqual(b.kept, []);
});

test('a runtime that is not running is said so when the campaign is read, as when a terminal is opened, and a campaign the runtime does not know keeps its own sentence', async () => {
    const down = new OpenIn({
        probes: windows,
        hosts: () => ({ claude: true, codex: true }),
        cli: { run: () => Promise.resolve({ exit: 3, stdout: '', stderr: '' }) },
        harnessRoot: () => 'C:\\Jason',
        isDirectory: () => true,
        remembered: () => null,
        remember: () => undefined,
        start: () => Promise.resolve(null),
        copy: () => undefined,
        terminalFlags,
        continuable: () => null,
        chatFolder: () => 'C:\\data\\desktop\\chat',
        continueFlags,
    });
    assert.deepEqual(await down.targets('cmp_01'), { refused: "Jason's runtime is not running." });
    assert.deepEqual(await down.open('vscode', 'cmp_01'), { refused: "Jason's runtime is not running." });
    const known = bench({ campaigns: {} });
    assert.deepEqual(await known.openIn.open('vscode', 'cmp_01'), { refused: 'That agent is not in this installation.' });
    assert.deepEqual(await known.openIn.targets('cmp_01'), { refused: 'That agent is not in this installation.' });
});

test('on macOS the file manager is shown with open -a Finder, so a folder named like an application is never launched; elsewhere the folder is the argument', async () => {
    assert.deepEqual(fileManagerLine('/usr/bin/open', '/Users/ada/Jason/odd.app', 'darwin'), { file: '/usr/bin/open', args: ['-a', 'Finder', '/Users/ada/Jason/odd.app'] });
    assert.deepEqual(fileManagerLine('/usr/bin/xdg-open', '/home/ada/Jason', 'linux'), { file: '/usr/bin/xdg-open', args: ['/home/ada/Jason'] });
    const b = bench({ probes: mac, root: '/Users/ada/Jason', folders: ['/Users/ada/Jason'] });
    assert.equal(await b.openIn.open('files'), null);
    assert.deepEqual(b.started, [{ file: '/usr/bin/open', args: ['-a', 'Finder', '/Users/ada/Jason'] }]);
});

test('on Windows a folder whose path holds a comma is not given to File Explorer, with a sentence that points to Copy path; editors and Copy path still take it', async () => {
    const b = bench({ root: 'C:\\Jason, Inc', folders: ['C:\\Jason, Inc'] });
    const answer = (await b.openIn.open('files')) as { refused: string };
    assert.match(answer.refused, /comma.*Copy path/);
    assert.deepEqual(b.started, []);
    assert.deepEqual(b.kept, []);
    assert.equal(await b.openIn.open('vscode'), null);
    assert.equal(await b.openIn.open('copy'), null);
    assert.deepEqual(b.copied, ['C:\\Jason, Inc']);
});

test('on macOS a folder whose last name ends in .app, in any case, is not given to Finder, with a sentence that points to Copy path; editors and Copy path still take it', async () => {
    for (const root of ['/Users/ada/Jason/odd.app', '/Users/ada/Jason/ODD.App']) {
        const b = bench({ probes: mac, root, folders: [root] });
        const answer = (await b.openIn.open('files')) as { refused: string };
        assert.match(answer.refused, /named like an application.*Copy path/, root);
        assert.deepEqual(b.started, [], root);
        assert.deepEqual(b.kept, [], root);
        assert.equal(await b.openIn.open('copy'), null);
        assert.deepEqual(b.copied, [root]);
    }
    for (const root of ['/Users/ada/Jason/sales-app', '/Users/ada/Jason/webapp']) {
        const b = bench({ probes: mac, root, folders: [root] });
        assert.equal(await b.openIn.open('files'), null, root);
        assert.deepEqual(b.started, [{ file: '/usr/bin/open', args: ['-a', 'Finder', root] }], root);
    }
    for (const root of ['/Users/ada/Jason/Tool.app', '/Users/ada/Jason/x.APP']) {
        const b = bench({ probes: mac, root, folders: [root] });
        assert.match(((await b.openIn.open('files')) as { refused: string }).refused, /named like an application/, root);
    }
});

test('a Windows editor found as an .exe on the PATH is started by that file, and a batch file is not', () => {
    const exe = probes('win32', [], { cursor: 'C:\\Tools\\bin\\cursor.exe', zed: 'C:\\Tools\\bin\\zed.cmd' });
    assert.deepEqual(findEditor('cursor', exe), { kind: 'program', path: 'C:\\Tools\\bin\\cursor.exe' });
    assert.equal(findEditor('zed', exe), null);
    assert.deepEqual(editorLine({ kind: 'program', path: 'C:\\Tools\\bin\\cursor.exe' }, 'C:\\Jason'), { file: 'C:\\Tools\\bin\\cursor.exe', args: ['C:\\Jason'] });
});

test('a demo run hands an editor the person’s environment, and the run keeps the demo’s for the terminals', () => {
    const env: Record<string, string | undefined> = { PATH: '/bin', JASON_DATA_DIR: '/home/ada/.jason', JASON_Harness__Root: '/home/ada/Jason', HOME: '/home/ada' };
    const person: PersonEnvironment = enterRun(env, { mode: 'demo', data: '/home/ada/.jason-demo' } as never);
    assert.equal(env.JASON_DATA_DIR, '/home/ada/.jason-demo', 'the run is on the demo');
    assert.equal(env.JASON_Harness__Root, undefined);
    const forEditor = editorEnvironment(env, person, 'linux');
    assert.equal(forEditor.JASON_DATA_DIR, '/home/ada/.jason');
    assert.equal(forEditor.JASON_Harness__Root, '/home/ada/Jason');
    assert.equal(forEditor.PATH, '/bin');
    assert.equal(env.JASON_DATA_DIR, '/home/ada/.jason-demo', 'the run’s own environment, which a terminal is started with, stays the demo’s');
    assert.equal(env.JASON_Harness__Root, undefined);
    // A person with no data directory of their own gets none, not the demo’s.
    const bare: Record<string, string | undefined> = { PATH: '/bin' };
    const nothing = enterRun(bare, { mode: 'demo', data: '/d' } as never);
    assert.equal(editorEnvironment(bare, nothing, 'linux').JASON_DATA_DIR, undefined);
});

test('on Linux the program started gets XDG_CURRENT_DESKTOP back from the original Electron kept, and nowhere else is it touched', () => {
    const person: PersonEnvironment = { dataDirectory: undefined, settings: {} };
    const env = { XDG_CURRENT_DESKTOP: 'Unity', ORIGINAL_XDG_CURRENT_DESKTOP: 'GNOME' };
    assert.equal(editorEnvironment(env, person, 'linux').XDG_CURRENT_DESKTOP, 'GNOME');
    assert.equal(editorEnvironment(env, person, 'darwin').XDG_CURRENT_DESKTOP, 'Unity');
    assert.equal(editorEnvironment({ XDG_CURRENT_DESKTOP: 'KDE' }, person, 'linux').XDG_CURRENT_DESKTOP, 'KDE');
    assert.equal(env.XDG_CURRENT_DESKTOP, 'Unity', 'a copy is changed, never the run’s environment');
});

test('main.ts hands an editor the person’s environment, and the demo’s harness root is named in a demo run', () => {
    assert.equal(openInHarnessRoot('demo', '/home/ada/Jason-demo', () => '/home/ada/Jason'), '/home/ada/Jason-demo');
    assert.equal(openInHarnessRoot('live', '/home/ada/Jason-demo', () => '/home/ada/Jason'), '/home/ada/Jason');
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    assert.match(main, /harnessRoot: \(\) => openInHarnessRoot\(runOn\.mode, demoWhere\.harnessRoot, /);
    assert.match(main, /startDetached\(line, editorEnvironment\(env, personEnvironment, process\.platform\)\)/);
});

test('the log names jason session open by its verb', () => {
    assert.equal(verbOf(['session', 'open', '--shell']), 'session open');
    assert.equal(verbOf(['session', 'open', '--campaign', 'cmp_01', '--host', 'codex']), 'session open');
});

test('with no harness root yet, the act is refused with a sentence', async () => {
    const b = bench({ folders: [] });
    assert.match(((await b.openIn.open('files')) as { refused: string }).refused, /not there yet/);
    assert.deepEqual(b.started, []);
});

test('a name that is not on the list is refused, and so is a path in its place: nothing is started, run or kept', async () => {
    const b = bench({ hosts: { claude: true, codex: false } });
    for (const target of ['notepad', 'C:\\Windows\\System32\\cmd.exe', '/bin/sh', '..\\..\\x', 'vscode ', 'VSCODE', 'cursor', 'codex', '', null, undefined, 4, { id: 'vscode' }, ['vscode']]) {
        assert.match(((await b.openIn.open(target)) as { refused: string }).refused, /not a place this computer can open/, String(target));
    }
    assert.deepEqual(b.started, []);
    assert.deepEqual(b.cliCalls, []);
    assert.deepEqual(b.copied, []);
    assert.deepEqual(b.kept, []);
});

test('each place is started the way it should be, and the folder is the one resolved here', async () => {
    const b = bench();
    for (const target of ['vscode', 'zed', 'files']) assert.equal(await b.openIn.open(target, 'cmp_01'), null, target);
    assert.deepEqual(b.started, [
        { file: 'C:\\Users\\ada\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe', args: ['C:\\Jason\\campaigns\\spring'] },
        { file: 'C:\\Tools\\Zed\\Zed.exe', args: ['C:\\Jason\\campaigns\\spring'] },
        { file: 'C:\\Windows\\explorer.exe', args: ['C:\\Jason\\campaigns\\spring'] },
    ]);
    assert.deepEqual(b.cliCalls.filter((call) => call[0] === 'session'), [], 'no terminal for an editor');
    assert.deepEqual(b.copied, []);
});

test('the three terminals run the bundled jason session open, with the flags of the one function, and start no program of their own', async () => {
    const b = bench();
    assert.equal(await b.openIn.open('claude'), null);
    assert.equal(await b.openIn.open('codex', 'cmp_01'), null);
    assert.equal(await b.openIn.open('terminal', 'cmp_01'), null);
    assert.deepEqual(b.cliCalls.filter((call) => call[0] === 'session'), [
        ['session', 'open', '--host', 'claude_code'],
        ['session', 'open', '--campaign', 'cmp_01', '--host', 'codex'],
        ['session', 'open', '--campaign', 'cmp_01', '--shell'],
    ]);
    // No campaign: the harness root is the verb's working folder; a campaign is named by its flag and the folder is left.
    assert.deepEqual(b.cwds, ['C:\\Jason', undefined, undefined]);
    assert.deepEqual(b.flagsGiven, [['claude', null], ['codex', 'cmp_01'], ['terminal', 'cmp_01']]);
    assert.deepEqual(b.started, []);
});

test('Claude Code and Codex are offered only where the desktop finds the host, and refused where it does not', async () => {
    const b = bench({ hosts: { claude: false, codex: false } });
    assert.match(((await b.openIn.open('claude')) as { refused: string }).refused, /not a place/);
    assert.match(((await b.openIn.open('codex')) as { refused: string }).refused, /not a place/);
    assert.equal(await b.openIn.open('terminal'), null);
});

test('a terminal the CLI refused is refused with the CLI’s sentence, a program that could not be started with its own, and neither is kept', async () => {
    const refusedCli = bench({ cliExit: 1, cliOut: '{"error":{"code":"host_not_found","message":"Claude Code is not on the search path."}}' });
    assert.deepEqual(await refusedCli.openIn.open('claude'), { refused: 'Claude Code is not on the search path.' });
    const noMessage = bench({ cliExit: 1, cliOut: 'not json' });
    assert.deepEqual(await noMessage.openIn.open('terminal'), { refused: 'A terminal could not be opened.' });
    const failed = bench({ failStart: 'That program could not be started.' });
    assert.deepEqual(await failed.openIn.open('zed'), { refused: 'That program could not be started.' });
    for (const each of [refusedCli, noMessage, failed]) {
        assert.deepEqual(each.kept, []);
        assert.ok(each.logged.every((line) => line.startsWith('open_in_refused:')));
    }
});

test('the place used last is kept, Copy path excepted, and becomes the default; a place that is not here any longer is not', async () => {
    const b = bench();
    assert.equal(await b.openIn.open('zed'), null);
    assert.equal(await b.openIn.open('copy'), null);
    assert.deepEqual(b.kept, ['zed']);
    assert.equal(((await b.openIn.targets()) as { default: string }).default, 'zed');
    const gone = bench({ remembered: 'cursor' });
    assert.equal(((await gone.openIn.targets()) as { default: string }).default, 'claude');
});

test('nothing is written into the folder: it is the same after every place is opened', async () => {
    const folder = mkdtempSync(join(tmpdir(), 'open-in-'));
    try {
        writeFileSync(join(folder, 'memory.md'), 'mine\n');
        const b = bench({ root: folder, folders: [folder] });
        for (const place of placesFound(windows, { claude: true, codex: true })) assert.equal(await b.openIn.open(place.id), null, place.id);
        assert.deepEqual(readdirSync(folder), ['memory.md']);
        assert.equal(readFileSync(join(folder, 'memory.md'), 'utf8'), 'mine\n');
    } finally {
        rmSync(folder, { recursive: true, force: true });
    }
});

test('no source of Open in writes a file, a folder or a link: it names no .vscode, .claude or wrapper and only reads the disk', () => {
    for (const name of ['open-in.ts', 'open-in-probes.ts', 'open-in-start.ts', 'open-in-terminal.ts']) {
        const text = readFileSync(new URL(`../src/${name}`, import.meta.url), 'utf8');
        assert.ok(!/\b(?:writeFile|writeFileSync|appendFile|mkdir|mkdirSync|rename|renameSync|copyFile|symlink|rmSync|unlink)\b/.test(text), name);
        assert.ok(!/[\\/'"`]\.(?:vscode|claude|cursor)/.test(text), name);
    }
});

// The place kept, in the desktop's own settings.

test('the remembered place is written to the desktop’s settings and read back, and anything else there is taken as unset', () => {
    const directory = mkdtempSync(join(tmpdir(), 'open-in-settings-'));
    try {
        const file = join(directory, 'desktop', 'settings.json');
        writeSettings(file, { feed: null, skillsDeployedFor: null, theme: 'dark', openIn: 'cursor' });
        assert.equal(readFileSync(file, 'utf8').includes('"open_in": "cursor"'), true);
        assert.deepEqual(readSettings(file), { feed: null, skillsDeployedFor: null, theme: 'dark', openIn: 'cursor' });
        writeSettings(file, { feed: null, skillsDeployedFor: null, theme: 'dark' });
        assert.deepEqual(readSettings(file), { feed: null, skillsDeployedFor: null, theme: 'dark' });
    } finally {
        rmSync(directory, { recursive: true, force: true });
    }
    for (const bad of ['7', 'null', '"copy"', '"/bin/sh"', '"C:\\\\x.exe"']) {
        assert.deepEqual(parseSettings(`{"open_in": ${bad}}`), { feed: null, skillsDeployedFor: null, theme: null }, bad);
    }
});

// The starter, with a program of the test's own.

test('a program the system starts is left running and answered with null; one it cannot start is answered with a sentence', async () => {
    assert.equal(await startDetached({ file: process.execPath, args: ['-e', ''] }, process.env), null);
    assert.equal(await startDetached({ file: join(tmpdir(), 'no-such-program-here.exe'), args: [] }, process.env), 'That program could not be started.');
});

test('the program is given the desktop’s environment less a parent session’s markers', async () => {
    const directory = mkdtempSync(join(tmpdir(), 'open-in-env-'));
    try {
        const out = join(directory, 'env.json');
        const script = `require('fs').writeFileSync(${JSON.stringify(out)}, JSON.stringify({ data: process.env.JASON_DATA_DIR ?? null, marker: process.env.CLAUDECODE ?? null }))`;
        assert.equal(await startDetached({ file: process.execPath, args: ['-e', script] }, { ...process.env, JASON_DATA_DIR: 'C:\\demo-data', CLAUDECODE: '1' }), null);
        for (let waited = 0; waited < 5000 && !readdirSync(directory).includes('env.json'); waited += 50) await new Promise((done) => setTimeout(done, 50));
        assert.deepEqual(JSON.parse(readFileSync(out, 'utf8')), { data: 'C:\\demo-data', marker: null });
    } finally {
        rmSync(directory, { recursive: true, force: true });
    }
});

// Who may ask.

function registrar(): { ipc: IpcRegistrar; listeners: Map<string, (event: { senderFrame: { url: string; parent: unknown } | null }, ...args: unknown[]) => unknown> } {
    const listeners = new Map<string, (event: { senderFrame: { url: string; parent: unknown } | null }, ...args: unknown[]) => unknown>();
    return { ipc: { handle: (channel, listener) => void listeners.set(channel, listener) }, listeners };
}

test('the three calls answer only the runtime’s own page in its top frame: another origin, a subframe or no frame is refused before the handler runs', () => {
    const { ipc, listeners } = registrar();
    const reached: string[] = [];
    const why: string[] = [];
    handle(ipc, 'openIn.targets', () => 'http://127.0.0.1:7788', () => void reached.push('targets'), (channel) => void why.push(channel));
    handle(ipc, 'openIn.open', () => 'http://127.0.0.1:7788', () => void reached.push('open'), (channel) => void why.push(channel));
    handle(ipc, 'openIn.continueChat', () => 'http://127.0.0.1:7788', () => void reached.push('continueChat'), (channel) => void why.push(channel));
    assert.deepEqual([...listeners.keys()].sort(), [invokeChannels['openIn.open'], invokeChannels['openIn.targets'], invokeChannels['openIn.continueChat']].sort());
    for (const channel of listeners.values()) {
        for (const frame of [null, { url: 'http://127.0.0.1:7788/ui/', parent: {} }, { url: 'https://example.test/', parent: null }, { url: 'file:///x.html', parent: null }]) {
            assert.throws(() => channel({ senderFrame: frame }, 'vscode'), RefusedSender);
        }
        assert.doesNotThrow(() => channel({ senderFrame: { url: 'http://127.0.0.1:7788/ui/', parent: null } }, 'vscode'));
    }
    assert.deepEqual(reached, ['targets', 'open', 'continueChat']);
    assert.equal(why.length, 12);
});

test('the chat’s AI cannot reach the acts: its tools carry no IPC, the chat’s sources name neither channel, and jason session is on its deny list', () => {
    const here = (name: string) => readFileSync(new URL(`../${name}`, import.meta.url), 'utf8');
    const denied = here('chat-deny-list.txt').split(/\r?\n/).filter((line) => line !== '' && !line.startsWith('#'));
    assert.ok(denied.includes('session'), 'the whole session noun, open included, is refused to the chat');
    for (const name of readdirSync(new URL('../src/', import.meta.url))) {
        if (!/^chat.*\.ts$/.test(name)) continue;
        const text = here(`src/${name}`);
        assert.ok(!/openIn|open-in|jason:openIn/.test(text), `${name} reaches Open in`);
    }
});

// Continue this chat in a terminal: the page names the chat, and the desktop reads the host and the session from its own
// record, and the working folder from the run.

test('a chat whose session a terminal may continue is offered with its host’s name, and never its session id', async () => {
    const b = bench();
    const offered = (await b.openIn.targets(undefined, chatA)) as { continue?: unknown };
    assert.deepEqual(offered.continue, { host: 'claude', label: 'Claude Code', running: false });
    assert.deepEqual(((await b.openIn.targets('cmp_01', chatB)) as { continue?: unknown }).continue, { host: 'codex', label: 'Codex', running: false });
    assert.ok(!JSON.stringify(offered).includes('session-a'), 'the session id never reaches the page');
    // With no chat named, there is no entry, and the answer is as it was.
    assert.ok(!('continue' in ((await b.openIn.targets()) as object)));
    assert.ok(!('continue' in ((await b.openIn.targets('cmp_01')) as object)));
});

test('the entry is offered only when every condition holds: a row per condition that fails', async () => {
    const chosen = (host: string) => ({ chosen: { host, model: null, effort: null } });
    const rows: [string, Conversation][] = [
        ['no session kept for Claude Code or Codex', conversation(chatA, chosen('claude'))],
        ['a key provider’s session', conversation(chatA, { sessions: { deepseek: 'session-k' }, plain_sessions: ['deepseek'], ...chosen('deepseek') })],
        ['a session no plain turn named', conversation(chatA, { sessions: { claude: 'session-a' }, ...chosen('claude') })],
    ];
    for (const [why, kept] of rows) {
        const b = bench({ chats: [kept] });
        assert.ok(!('continue' in ((await b.openIn.targets(undefined, chatA)) as object)), why);
        assert.deepEqual(await b.openIn.continueChat(chatA), { refused: 'This chat cannot be continued in a terminal.' }, why);
        assert.deepEqual(b.cliCalls, [], why);
    }
    // A turn that runs: the entry is drawn, as running, and the act is refused without asking the CLI.
    const running = bench({ running: [chatA] });
    assert.deepEqual(((await running.openIn.targets(undefined, chatA)) as { continue?: unknown }).continue, { host: 'claude', label: 'Claude Code', running: true });
    assert.deepEqual(await running.openIn.continueChat(chatA), { refused: 'A turn of this chat is running. Stop it or wait for it to end, then continue it in a terminal.' });
    assert.deepEqual(running.cliCalls, []);
    // All of them hold: offered.
    assert.deepEqual(((await bench().openIn.targets(undefined, chatA)) as { continue?: unknown }).continue, { host: 'claude', label: 'Claude Code', running: false });
});

test('the act runs the bundled jason session open --here with the host and the id from the record, in the folder a plain turn runs in', async () => {
    const b = bench();
    assert.equal(await b.openIn.continueChat(chatA), null);
    assert.equal(await b.openIn.continueChat(chatB), null);
    assert.deepEqual(b.cliCalls, [
        ['session', 'open', '--here', '--host', 'claude_code', '--resume', 'session-a'],
        ['session', 'open', '--here', '--host', 'codex', '--resume', 'thread-b'],
    ]);
    assert.deepEqual(b.cwds, ['C:\\data\\desktop\\chat', 'C:\\data\\desktop\\chat']);
    assert.deepEqual(b.started, [], 'the detached starter is not used');
    assert.deepEqual(b.logged, ['open_in_continued:claude', 'open_in_continued:codex']);
    assert.deepEqual(continueFlags('claude', 'session-a'), ['--here', '--host', 'claude_code', '--resume', 'session-a']);
});

test('the entry is never the remembered default: continuing keeps nothing, and the main part opens what it opened before', async () => {
    const b = bench({ remembered: 'zed' });
    assert.equal(await b.openIn.continueChat(chatA), null);
    assert.deepEqual(b.kept, []);
    assert.equal(b.remembered.value, 'zed');
    assert.equal(((await b.openIn.targets(undefined, chatA)) as { default: string }).default, 'zed');
});

test('what the page sends besides the chat’s id is never read: a path or a session id in its place, or after it, opens nothing of its own', async () => {
    const b = bench();
    const act = b.openIn.continueChat.bind(b.openIn) as (...args: unknown[]) => Promise<unknown>;
    for (const given of ['session-a', 'C:\\Windows\\System32\\cmd.exe', '/bin/sh', '../x', 'conv_', '', null, undefined, 7, { id: chatA }, [chatA]]) {
        assert.deepEqual(await act(given), { refused: 'This chat cannot be continued in a terminal.' }, String(given));
    }
    assert.deepEqual(b.cliCalls, []);
    assert.equal(await act(chatA, 'other-session', 'C:\\Elsewhere', { cwd: '/tmp' }), null);
    assert.deepEqual(b.cliCalls, [['session', 'open', '--here', '--host', 'claude_code', '--resume', 'session-a']]);
    assert.deepEqual(b.cwds, ['C:\\data\\desktop\\chat']);
    // The main process hands the act the chat's id alone.
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    assert.match(main, /handle\(ipcMain, 'openIn\.continueChat', origin, \(chatId\) => openIn\.continueChat\(chatId\), refused\);/);
    assert.match(main, /handle\(ipcMain, 'openIn\.targets', origin, \(campaignId, chatId\) => openIn\.targets\(campaignId, chatId\), refused\);/);
});

test('a refusal of the CLI is said in the CLI’s own sentence; a runtime that is not running is said so; nothing is kept', async () => {
    const cases: [Parameters<typeof bench>[0], string][] = [
        [{ cliExit: 1, cliOut: '{"error":{"code":"host_cannot_resume","message":"Codex has no way this jason knows to continue one of its own conversations, so nothing was started."}}' }, 'Codex has no way this jason knows to continue one of its own conversations, so nothing was started.'],
        [{ cliExit: 1, cliOut: '{"error":{"code":"terminal_not_found","message":"No terminal was found on this computer, so nothing was started."}}' }, 'No terminal was found on this computer, so nothing was started.'],
        [{ cliExit: 2, cliOut: '', cliErr: "--here opens at the folder this command runs in; it is not taken with --campaign or --project.\nRun 'jason --help' for usage.\n" }, '--here opens at the folder this command runs in; it is not taken with --campaign or --project.'],
        [{ cliExit: 3, cliOut: '{"error":{"code":"runtime_unreachable","message":"No runtime answered."}}' }, "Jason's runtime is not running."],
        [{ cliExit: 1, cliOut: 'not json' }, 'A terminal could not be opened.'],
        [{ cliExit: -1, cliOut: '', cliErr: 'spawn ENOENT' }, 'A terminal could not be opened.'],
    ];
    for (const [options, sentence] of cases) {
        const b = bench(options);
        assert.deepEqual(await b.openIn.continueChat(chatA), { refused: sentence }, sentence);
        assert.deepEqual(b.kept, []);
        assert.deepEqual(b.logged, ['open_in_continue_refused:claude']);
    }
});

test('in a demo run the working folder is the run’s own chat folder, and the CLI is the run’s, which carries the demo’s data directory', async () => {
    const b = bench({ chatFolder: '/home/ada/.jason-demo/desktop/chat' });
    assert.equal(await b.openIn.continueChat(chatA), null);
    assert.deepEqual(b.cwds, ['/home/ada/.jason-demo/desktop/chat']);
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    // `paths` is the run's (the demo's in a demo run) and `cli` the run's, the one the three terminals use.
    assert.match(main, /const openIn = new OpenIn\(\{[\s\S]*?\n        cli,\n[\s\S]*?continuable: \(chatId\) => chat\.continuable\(chatId\),\n        chatFolder: \(\) => paths\.chat,\n/);
});
