import assert from 'node:assert/strict';
import { afterEach, test } from 'node:test';
import { PermissionBroker, type ToolAnswer } from '../src/chat-permission-broker.ts';
import { fullAccessJudgement, judge, type ChatScope } from '../src/chat-permission-model.ts';
import type { ChatMode } from '../src/chat-modes.ts';

// In a demo run the desktop runs on `~/.jason-demo` and the person's own installation is `~/.jason` with its harness root
// `~/Jason`: the chat refuses both whole, in every mode, Full access included.

const denied = ['approval approve', 'runtime'];
const deniedOperations = ['approval.approve'];

/** A demo run's scope on macOS' semantics, every path what it says it is. */
function demoMac(personsRoot = '/Users/someone/Jason'): ChatScope {
    return {
        platform: 'darwin',
        cwd: '/Users/someone/.jason-demo/desktop/chat',
        chat: '/Users/someone/.jason-demo/desktop/chat',
        added: ['/Users/someone/Jason-demo/Northwind'],
        data: '/Users/someone/.jason-demo',
        home: '/Users/someone',
        personsOwn: ['/Users/someone/.jason', personsRoot],
        protectedFiles: [
            '/Users/someone/.jason-demo/config/settings.json',
            '/Users/someone/.jason-demo/run/runtime.json',
            '/Users/someone/.jason/config/settings.json',
            '/Users/someone/.jason/run/runtime.json',
        ],
        denied,
        deniedOperations,
        real: (path) => path,
    };
}

/** The same run's scope on Windows' semantics. */
function demoWindows(): ChatScope {
    return {
        platform: 'win32',
        cwd: 'C:\\Users\\someone\\.jason-demo\\desktop\\chat',
        chat: 'C:\\Users\\someone\\.jason-demo\\desktop\\chat',
        added: ['C:\\Users\\someone\\Jason-demo\\Northwind'],
        data: 'C:\\Users\\someone\\.jason-demo',
        home: 'C:\\Users\\someone',
        personsOwn: ['C:\\Users\\someone\\.jason', 'C:\\Users\\someone\\Jason'],
        protectedFiles: ['C:\\Users\\someone\\.jason-demo\\config\\settings.json', 'C:\\Users\\someone\\.jason\\config\\settings.json', 'C:\\Users\\someone\\.jason\\run\\runtime.json'],
        denied,
        deniedOperations,
        real: (path) => path,
    };
}

/** The person's own installation's scope: the data directory is the one fenced folder. */
function liveMac(personsRoot?: string): ChatScope {
    const { personsOwn: _demoOnly, ...rest } = demoMac();
    return {
        ...rest,
        cwd: '/Users/someone/.jason/desktop/chat',
        chat: '/Users/someone/.jason/desktop/chat',
        added: [personsRoot === undefined ? '/Users/someone/Jason/Northwind' : `${personsRoot}/Northwind`],
        data: '/Users/someone/.jason',
        protectedFiles: ['/Users/someone/.jason/config/settings.json', '/Users/someone/.jason/run/runtime.json'],
    };
}

const persons = /person's own installation, and this chat runs in the demo/;
const ownData = /installation's own data/;

const made: PermissionBroker[] = [];
afterEach(() => {
    for (const broker of made.splice(0)) broker.endAll();
});

/** One request of a turn in a mode, through the broker as the host asks it; a card fails the row. */
async function asked(mode: ChatMode, scope: ChatScope, tool: string, input: Record<string, unknown>): Promise<ToolAnswer> {
    const broker = new PermissionBroker({ emit: () => assert.fail(`${mode}: ${tool} ${JSON.stringify(input)} made a card`), newId: () => 'req' });
    made.push(broker);
    broker.openTurn('chat_a', 'turn_1', scope, mode);
    const answer = await broker.ask('turn_1', tool, input);
    if (mode === 'full_access') {
        const hooked = broker.hook('turn_1', tool, input, null);
        assert.equal(hooked.decision, 'deny', `the Full access hook: ${tool} ${JSON.stringify(input)}`);
    }
    return answer;
}

const modes: readonly ChatMode[] = ['ask_first', 'plan_only', 'auto_edit', 'full_access'];

for (const mode of modes) {
    test(`${mode}: a demo run's chat refuses a read of the person's run file`, async () => {
        for (const scope of [demoMac(), demoWindows()]) {
            const file = scope.platform === 'win32' ? 'C:\\Users\\someone\\.jason\\run\\runtime.json' : '/Users/someone/.jason/run/runtime.json';
            const answer = await asked(mode, scope, 'Read', { file_path: file });
            assert.equal(answer.behavior, 'deny');
            assert.match(answer.behavior === 'deny' ? answer.message : '', persons, file);
        }
    });

    test(`${mode}: a demo run's chat refuses a write of the person's mode record`, async () => {
        for (const scope of [demoMac(), demoWindows()]) {
            const file = scope.platform === 'win32' ? 'C:\\Users\\someone\\.jason\\desktop\\mode' : '/Users/someone/.jason/desktop/mode';
            for (const [tool, input] of [['Write', { file_path: file, content: 'live' }], ['Edit', { file_path: file, old_string: 'demo', new_string: 'live' }]] as const) {
                const answer = await asked(mode, scope, tool, input);
                assert.equal(answer.behavior, 'deny', `${tool} ${file}`);
                // Plan only refuses every writer before the path is read; every other mode refuses it as the person's.
                if (mode !== 'plan_only') assert.match(answer.behavior === 'deny' ? answer.message : '', persons, `${tool} ${file}`);
            }
        }
    });

    test(`${mode}: a demo run's chat refuses a command naming ~/.jason or ~/Jason, in literal and home spellings`, async () => {
        const mac = [
            'cat /Users/someone/.jason/run/runtime.json',
            'cat ~/.jason/run/runtime.json',
            'cat $HOME/.jason/config/settings.json',
            'cat "${HOME}/.jason/desktop/mode"',
            'echo live > ~/.jason/desktop/mode',
            'ls /Users/someone/Jason',
            'ls ~/Jason',
            'cat ~/Jason/Northwind/AGENTS.md',
            'cat $HOME/Jason/Northwind/campaigns/spring/AGENTS.md',
        ];
        const windows = [
            'type C:\\Users\\someone\\.jason\\run\\runtime.json',
            'cat /c/Users/someone/.jason/run/runtime.json',
            'cat C:/Users/someone/.jason/config/settings.json',
            'cat %USERPROFILE%\\.jason\\run\\runtime.json',
            'Get-Content $env:USERPROFILE\\.jason\\desktop\\mode',
            'cat $USERPROFILE/Jason/Northwind/AGENTS.md',
            'dir C:\\Users\\someone\\JASON',
            'ls ~/jason',
        ];
        for (const [scope, commands] of [[demoMac(), mac], [demoWindows(), windows]] as const) {
            for (const command of commands) {
                const answer = await asked(mode, scope, 'Bash', { command });
                assert.equal(answer.behavior, 'deny', command);
                assert.match(answer.behavior === 'deny' ? answer.message : '', persons, command);
            }
        }
    });
}

test('a demo run refuses a file tool anywhere in the person\'s directories, their folders included, and walks of them or of a folder holding them', () => {
    const scope = demoMac();
    for (const file_path of ['/Users/someone/.jason/desktop/chat/notes.md', '/Users/someone/.jason/jason.db', '/Users/someone/Jason/Northwind/AGENTS.md', '/Users/someone/Jason']) {
        const judged = judge('Read', { file_path }, scope);
        assert.match(judged.verdict === 'deny' ? judged.message : '', persons, file_path);
    }
    for (const path of ['/Users/someone/Jason/Northwind', '/Users/someone/.jason/desktop']) assert.equal(judge('Grep', { pattern: 'key', path }, scope).verdict, 'deny', path);
    for (const pattern of ['/Users/someone/J*/**', '~/.jas*/run/*', '/Users/someone/Jason/**/AGENTS.md']) assert.equal(judge('Glob', { pattern }, scope).verdict, 'deny', pattern);
    assert.equal(judge('LS', { path: '/Users/someone' }, scope).verdict, 'deny');
});

test('a prefix is not a folder: in a demo run ~/.jason never covers ~/.jason-demo, nor ~/Jason ~/Jason-demo', () => {
    const scope = demoMac();
    // The demo's own chat folder and its own harness root are the chat's, as the person's own are in their installation.
    assert.deepEqual(judge('Read', { file_path: '/Users/someone/.jason-demo/desktop/chat/notes.md' }, scope), { verdict: 'allow' });
    assert.deepEqual(judge('Write', { file_path: '/Users/someone/Jason-demo/Northwind/brief.md', content: 'x' }, scope), { verdict: 'allow' });
    for (const command of ['cat /Users/someone/Jason-demo/Northwind/brief.md', 'cat ~/Jason-demo/Northwind/brief.md', 'ls /Users/someone/.jason-demo/desktop/chat']) {
        const judged = judge('Bash', { command }, scope);
        assert.ok(judged.verdict !== 'deny' || !persons.test(judged.message), `${command}: ${JSON.stringify(judged)}`);
    }
    // The demo's own data outside its chat folder stays its own data, in its home spellings too.
    for (const command of ['cat ~/.jason-demo/run/runtime.json', 'cat $HOME/.jason-demo/config/settings.json']) {
        const judged = judge('Bash', { command }, scope);
        assert.match(judged.verdict === 'deny' ? judged.message : '', ownData, command);
    }
    // On Windows, case aside: `~/JASON-DEMO` is no spelling of the person's `~/Jason`.
    const windows = demoWindows();
    assert.deepEqual(judge('Read', { file_path: 'C:\\Users\\someone\\JASON-DEMO\\Northwind\\brief.md' }, windows), { verdict: 'allow' });
    const judged = judge('Bash', { command: 'type C:\\Users\\someone\\JASON-DEMO\\Northwind\\brief.md' }, windows);
    assert.ok(judged.verdict !== 'deny' || !persons.test(judged.message), JSON.stringify(judged));
});

test('a prefix is not a folder the other way: in the person\'s own installation ~/.jason-demo is not their data, and ~/.jason still is', () => {
    const scope = liveMac();
    assert.equal(judge('Read', { file_path: '/Users/someone/.jason-demo/config/settings.json' }, scope).verdict, 'ask');
    assert.equal(judge('Bash', { command: 'cat ~/.jason-demo/config/settings.json' }, scope).verdict, 'ask');
    const judged = judge('Bash', { command: 'cat ~/.jason/config/settings.json' }, scope);
    assert.match(judged.verdict === 'deny' ? judged.message : '', ownData);
});

test('a command naming a folder that holds a person\'s directory is a card no "always" covers, in a demo run only', () => {
    // The person's harness root where no fenced folder of the demo is: only the person's root makes the folder hold data.
    const root = '/Users/someone/Documents/Jason';
    const demo = judge('Bash', { command: 'ls /Users/someone/Documents' }, demoMac(root));
    assert.equal(demo.verdict, 'ask');
    assert.equal(demo.verdict === 'ask' ? demo.remember : 'not a card', null);
    assert.equal(judge('Grep', { pattern: 'key', path: '/Users/someone/Documents' }, demoMac(root)).verdict, 'deny');
    assert.equal(fullAccessJudgement('Bash', { command: 'ls /Users/someone/Documents' }, demoMac(root)).outcome, 'held');
    const live = judge('Bash', { command: 'ls /Users/someone/Documents' }, liveMac(root));
    assert.equal(live.verdict, 'ask');
    assert.notEqual(live.verdict === 'ask' ? live.remember : null, null);
    assert.equal(judge('Grep', { pattern: 'key', path: '/Users/someone/Documents' }, liveMac(root)).verdict, 'ask');
    assert.equal(fullAccessJudgement('Bash', { command: 'ls /Users/someone/Documents' }, liveMac(root)).outcome, 'runs');
});

test('in the person\'s own installation nothing changes: a scope with no person\'s directories judges as one with an empty list', () => {
    const cases: [string, Record<string, unknown>][] = [
        ['Read', { file_path: '/Users/someone/.jason/run/runtime.json' }],
        ['Read', { file_path: '/Users/someone/Jason/Northwind/AGENTS.md' }],
        ['Read', { file_path: '/Users/someone/.jason-demo/run/runtime.json' }],
        ['Write', { file_path: '/Users/someone/.jason/desktop/mode', content: 'demo' }],
        ['Bash', { command: 'cat ~/.jason/run/runtime.json' }],
        ['Bash', { command: 'ls ~/Jason' }],
        ['Bash', { command: 'ls /Users/someone' }],
        ['Grep', { pattern: 'key', path: '/Users/someone/Jason' }],
        ['Glob', { pattern: '/Users/someone/J*/**' }],
    ];
    for (const [tool, input] of cases) {
        const absent = judge(tool, input, liveMac());
        assert.deepEqual(judge(tool, input, { ...liveMac(), personsOwn: [] }), absent, `${tool} ${JSON.stringify(input)}`);
        assert.ok(absent.verdict !== 'deny' || !persons.test(absent.message), `${tool} ${JSON.stringify(input)}`);
        assert.deepEqual(fullAccessJudgement(tool, input, { ...liveMac(), personsOwn: [] }), fullAccessJudgement(tool, input, liveMac()));
    }
    // The person's harness root is the chat's in their own installation: an added folder is read without a card.
    assert.deepEqual(judge('Read', { file_path: '/Users/someone/Jason/Northwind/AGENTS.md' }, liveMac()), { verdict: 'allow' });
});

// ---- Every fenced folder, in the person's own installation as in a demo run: a path in its one spelling ------------------

/** The person's own installation's scope on Windows' semantics. */
function liveWindows(): ChatScope {
    const { personsOwn: _demoOnly, ...rest } = demoWindows();
    return {
        ...rest,
        cwd: 'C:\\Users\\someone\\.jason\\desktop\\chat',
        chat: 'C:\\Users\\someone\\.jason\\desktop\\chat',
        added: ['C:\\Users\\someone\\Jason\\Northwind'],
        data: 'C:\\Users\\someone\\.jason',
        protectedFiles: ['C:\\Users\\someone\\.jason\\config\\settings.json', 'C:\\Users\\someone\\.jason\\run\\runtime.json'],
    };
}

const linuxOf = (scope: ChatScope): ChatScope => ({ ...scope, platform: 'linux' });
const host = /another computer/;

/** What one request comes to in a mode: refused with its sentence, a card with what "always" would cover, or allowed. */
type Outcome = { refused: string } | { card: string | null } | 'allowed';

async function outcome(mode: ChatMode, scope: ChatScope, tool: string, input: Record<string, unknown>): Promise<Outcome> {
    let card: { remember?: string | null } | undefined;
    const broker = new PermissionBroker({
        emit: (_chat, event) => {
            if (event.kind === 'permission') card = event;
        },
        newId: () => 'req',
    });
    made.push(broker);
    broker.openTurn('chat_a', 'turn_1', scope, mode);
    const pending = broker.ask('turn_1', tool, input);
    await new Promise((resolve) => setImmediate(resolve));
    if (card !== undefined) {
        broker.endAll();
        await pending;
        return { card: card.remember ?? null };
    }
    const answer = await pending;
    return answer.behavior === 'deny' ? { refused: answer.message } : 'allowed';
}

/** Refused in the mode, by its broker and in Full access by its hook too, with a sentence the pattern matches. */
async function refusedIn(mode: ChatMode, scope: ChatScope, tool: string, input: Record<string, unknown>, sentence: RegExp): Promise<void> {
    const said = `${mode} ${scope.platform} ${tool} ${JSON.stringify(input)}`;
    const got = await outcome(mode, scope, tool, input);
    assert.ok(typeof got === 'object' && 'refused' in got, `${said}: ${JSON.stringify(got)}`);
    // Plan only refuses every writer and every command before reading its path.
    if (!(mode === 'plan_only' && /plan/i.test(got.refused))) assert.match(got.refused, sentence, said);
    if (mode === 'full_access') assert.equal(broker(scope).hook('turn_1', tool, input, null).decision, 'deny', said);
}

/** A broker with one Full access turn open on the scope, for its hook. */
function broker(scope: ChatScope): PermissionBroker {
    const made_ = new PermissionBroker({ emit: () => undefined, newId: () => 'req' });
    made.push(made_);
    made_.openTurn('chat_a', 'turn_1', scope, 'full_access');
    return made_;
}

for (const mode of modes) {
    test(`${mode}: a path that names a host, whatever the host, is never read or walked, in a demo run and in the person's own installation`, async () => {
        for (const scope of [demoWindows(), liveWindows()]) {
            for (const path of [
                '\\\\localhost\\C$\\Users\\someone\\.jason\\run\\runtime.json',
                '//localhost/c$/Users/someone/.jason/config/settings.json',
                '\\\\?\\UNC\\localhost\\c$\\Users\\someone\\.jason\\run\\runtime.json',
                '\\\\127.0.0.1\\c$\\Users\\someone\\Jason',
                '\\\\server\\share\\notes.md',
            ]) {
                await refusedIn(mode, scope, 'Read', { file_path: path }, host);
                await refusedIn(mode, scope, 'Grep', { pattern: 'token', path }, host);
                await refusedIn(mode, scope, 'Glob', { pattern: '**/*.json', path }, host);
                await refusedIn(mode, scope, 'LS', { path }, host);
            }
            for (const command of ["cat '//localhost/c$/Users/someone/.jason/run/runtime.json'", 'cat //server/share/notes.md', 'curl file://server/share/notes.md', "cat '\\\\localhost\\c$\\Users\\someone\\.jason\\run\\runtime.json'"]) {
                await refusedIn(mode, scope, 'Bash', { command }, host);
            }
        }
        for (const scope of [demoMac(), liveMac(), linuxOf(demoMac())]) {
            await refusedIn(mode, scope, 'Read', { file_path: '/net/server/share/notes.md' }, host);
            await refusedIn(mode, scope, 'LS', { path: '/net/server/share' }, host);
            await refusedIn(mode, scope, 'Bash', { command: 'cat /net/server/share/notes.md' }, host);
        }
    });

    test(`${mode}: a command that hands a file or a command to the system's opener or scheduler is never run unasked, nor covered by "always"`, async () => {
        const launches: [ChatScope, string[]][] = [
            [demoWindows(), ['explorer.exe s.bat', 'start s.bat', 'pcalua -a s.bat', 'wt s.bat', 'schtasks /run /tn x', 'msiexec /i s.msi']],
            [liveWindows(), ['explorer s.bat']],
            [demoMac(), ['open s.command', 'launchctl submit -l x -- ./s.sh', 'osascript s.scpt', 'tmux new-session -d ./s.sh', 'automator s.workflow']],
            [liveMac(), ['open s.command']],
            [linuxOf(demoMac()), ['xdg-open s.desktop', 'gio launch s.desktop', 'gtk-launch s', 'systemd-run --user ./s.sh', 'kde-open s.desktop', 'dbus-send --session --dest=x /x x.y']],
        ];
        for (const [scope, commands] of launches) {
            for (const command of commands) {
                const said = `${mode} ${scope.platform} ${command}`;
                const got = await outcome(mode, scope, 'Bash', { command });
                // Refused where Ask first refuses (Plan only refuses every command), and otherwise a card no "always" covers.
                if (mode === 'plan_only') assert.ok(typeof got === 'object' && 'refused' in got, `${said}: ${JSON.stringify(got)}`);
                else assert.deepEqual(got, { card: null }, said);
                if (mode === 'full_access') {
                    assert.equal(broker(scope).hook('turn_1', 'Bash', { command }, null).decision, 'ask', said);
                    assert.equal(fullAccessJudgement('Bash', { command }, scope).outcome, 'held', said);
                }
            }
        }
    });

    test(`${mode}: a fenced folder is matched in its one spelling: the data volume's prefix on macOS, Windows' trailing dots, spaces and streams`, async () => {
        for (const [scope, sentence] of [[demoMac(), persons], [liveMac(), ownData]] as const) {
            await refusedIn(mode, scope, 'Read', { file_path: '/System/Volumes/Data/Users/someone/.jason/config/settings.json' }, sentence);
            await refusedIn(mode, scope, 'Bash', { command: 'cat /System/Volumes/Data/Users/someone/.jason/run/runtime.json' }, sentence);
            await refusedIn(mode, scope, 'Grep', { pattern: 'token', path: '/system/volumes/data/Users/someone/.jason' }, /installation/);
        }
        await refusedIn(mode, demoMac(), 'Write', { file_path: '/System/Volumes/Data/Users/someone/Jason/Northwind/notes.md', content: 'x' }, persons);
        for (const [scope, sentence] of [[demoWindows(), persons], [liveWindows(), ownData]] as const) {
            for (const file_path of [
                'C:\\Users\\someone\\.jason.\\run\\runtime.json',
                'C:\\Users\\someone\\.jason \\run\\runtime.json',
                'C:\\Users\\someone\\.jason::$INDEX_ALLOCATION\\run\\runtime.json',
                'C:\\Users\\someone\\.jason\\run\\runtime.json.',
                'C:\\Users\\someone\\.jason\\config\\settings.json:x',
            ]) {
                await refusedIn(mode, scope, 'Read', { file_path }, sentence);
            }
        }
        await refusedIn(mode, demoWindows(), 'Read', { file_path: 'C:\\Users\\someone\\Jason. \\Northwind\\AGENTS.md' }, persons);
    });

    test(`${mode}: a fenced folder is matched under the home folder's spellings at any depth, and in a file tool's ~ path`, async () => {
        const deep = '/Users/someone/Documents/Jason';
        for (const command of ['cat ~/Documents/Jason/Northwind/AGENTS.md', 'cat $HOME/Documents/Jason/Northwind/AGENTS.md', 'echo x > $HOME/Documents/Jason/Northwind/notes.md', 'cat "${HOME}/Documents/Jason/Northwind/AGENTS.md"']) {
            await refusedIn(mode, demoMac(deep), 'Bash', { command }, persons);
        }
        for (const command of ['type %USERPROFILE%\\Documents\\Jason\\Northwind\\AGENTS.md', 'cat $USERPROFILE/Documents/Jason/Northwind/AGENTS.md']) {
            await refusedIn(mode, { ...demoWindows(), personsOwn: ['C:\\Users\\someone\\.jason', 'C:\\Users\\someone\\Documents\\Jason'] }, 'Bash', { command }, persons);
        }
        for (const [scope, sentence] of [[demoMac(), persons], [liveMac(), ownData], [demoWindows(), persons], [liveWindows(), ownData]] as const) {
            await refusedIn(mode, scope, 'Read', { file_path: '~/.jason/run/runtime.json' }, sentence);
            await refusedIn(mode, scope, 'Write', { file_path: '~/.jason/desktop/mode', content: 'live' }, sentence);
            await refusedIn(mode, scope, 'Grep', { pattern: 'token', path: '~/.jason/config' }, sentence);
        }
        await refusedIn(mode, demoMac(deep), 'Read', { file_path: '~/Documents/Jason/Northwind/AGENTS.md' }, persons);
    });

    test(`${mode}: a relative path after a directory change is read from the folder the change sets`, async () => {
        for (const [scope, sentence] of [[demoMac(), persons], [liveMac(), ownData]] as const) {
            for (const command of ['cd ~ && cat .jason/config/settings.json', 'cd /Users/someone && cat .jason/run/runtime.json', 'cd ../../.. && cat .jason/run/runtime.json', 'cd .. && cd ../.. && cat .jason/run/runtime.json']) {
                await refusedIn(mode, scope, 'Bash', { command }, sentence);
            }
        }
        await refusedIn(mode, demoMac(), 'Bash', { command: 'cd /Users/someone && cat Jason/Northwind/AGENTS.md' }, persons);
        await refusedIn(mode, demoWindows(), 'Bash', { command: 'cd /c/Users/someone && cat Jason/Northwind/AGENTS.md' }, persons);
    });

    test(`${mode}: where the reader cannot see the path, a command that could reach a fenced folder is never run unasked, nor covered by "always"`, async () => {
        // A variable joined at run time, another person's ~, a home built from two variables, a change to a folder named by
        // a variable: the text does not say where they lead.
        for (const [scope, command] of [
            [demoMac(), 'X=~/.ja; cat ${X}son/run/runtime.json'],
            [demoMac(), 'cat ~someone/.jason/run/runtime.json'],
            [demoMac(), 'cd "$HOME" && cat .jason/run/runtime.json'],
            [demoWindows(), 'cat $HOMEDRIVE$HOMEPATH/.jason/run/runtime.json'],
            [liveMac(), 'X=~/.ja; cat ${X}son/run/runtime.json'],
        ] as const) {
            const said = `${mode} ${command}`;
            const got = await outcome(mode, scope, 'Bash', { command });
            if (mode === 'plan_only') assert.ok(typeof got === 'object' && 'refused' in got, said);
            else assert.ok(typeof got === 'object' && ('refused' in got || got.card === null), `${said}: ${JSON.stringify(got)}`);
            if (mode === 'full_access') assert.notEqual(fullAccessJudgement('Bash', { command }, scope).outcome, 'runs', said);
        }
    });
}
