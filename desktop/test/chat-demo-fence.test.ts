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
