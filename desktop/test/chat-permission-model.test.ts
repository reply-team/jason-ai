import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { commandPrefix, isCompound, judge, type ChatScope, type Platform } from '../src/chat-permission-model.ts';
import { realPathOf } from '../src/chat-real-path.ts';
import { denyListFile, parseDenyList } from '../src/chat-deny.ts';

const denied = ['approval approve', 'approval set-standing', 'decision answer', 'runtime', 'plugin install', 'ui', 'journal watch'];

/** A scope on Windows' semantics: the real paths are what the map says, a path absent from it does not exist. */
function windows(real: Record<string, string> = {}): ChatScope {
    const existing: Record<string, string> = {
        'c:\\': 'C:\\',
        'c:\\users': 'C:\\Users',
        'c:\\users\\someone': 'C:\\Users\\someone',
        'c:\\users\\someone\\.jason': 'C:\\Users\\someone\\.jason',
        'c:\\users\\someone\\.jason\\desktop': 'C:\\Users\\someone\\.jason\\desktop',
        'c:\\users\\someone\\.jason\\desktop\\chat': 'C:\\Users\\someone\\.jason\\desktop\\chat',
        'c:\\users\\someone\\.jason\\config': 'C:\\Users\\someone\\.jason\\config',
        'c:\\users\\someone\\.jason\\config\\settings.json': 'C:\\Users\\someone\\.jason\\config\\settings.json',
        'c:\\users\\someone\\.jason\\run': 'C:\\Users\\someone\\.jason\\run',
        'c:\\users\\someone\\jason': 'C:\\Users\\someone\\Jason',
        'c:\\users\\someone\\jason\\northwind': 'C:\\Users\\someone\\Jason\\Northwind',
        'c:\\users\\someone\\jason\\northwind\\campaigns': 'C:\\Users\\someone\\Jason\\Northwind\\campaigns',
        'c:\\users\\someone\\jason\\northwind\\campaigns\\spring': 'C:\\Users\\someone\\Jason\\Northwind\\campaigns\\spring',
        'c:\\elsewhere': 'C:\\elsewhere',
        // An 8.3 short name of the home folder, and a junction in the chat folder that leads to the data directory.
        'c:\\users\\someon~1': 'C:\\Users\\someone',
        'c:\\users\\someone\\.jason\\desktop\\chat\\out': 'C:\\Users\\someone\\.jason\\run',
        ...Object.fromEntries(Object.entries(real).map(([key, value]) => [key.toLowerCase(), value])),
    };
    return {
        platform: 'win32',
        cwd: 'C:\\Users\\someone\\.jason\\desktop\\chat',
        chat: 'C:\\Users\\someone\\.jason\\desktop\\chat',
        added: ['C:\\Users\\someone\\Jason\\Northwind\\campaigns\\spring', 'C:\\Users\\someone\\Jason\\Northwind'],
        data: 'C:\\Users\\someone\\.jason',
        protectedFiles: ['C:\\Users\\someone\\.jason\\config\\settings.json', 'C:\\Users\\someone\\.jason\\run\\runtime.json'],
        denied,
        real: (path) => existing[path.toLowerCase()] ?? null,
    };
}

/** A scope on macOS' semantics: case-insensitive like the default volume, `/var` a link to `/private/var`. */
function mac(): ChatScope {
    const existing: Record<string, string> = {
        '/': '/',
        '/users': '/Users',
        '/users/someone': '/Users/someone',
        '/users/someone/.jason': '/Users/someone/.jason',
        '/users/someone/.jason/desktop': '/Users/someone/.jason/desktop',
        '/users/someone/.jason/desktop/chat': '/Users/someone/.jason/desktop/chat',
        '/users/someone/.jason/desktop/chat/out': '/Users/someone/.jason/config',
        '/users/someone/.jason/config': '/Users/someone/.jason/config',
        '/users/someone/.jason/run': '/Users/someone/.jason/run',
        '/users/someone/jason': '/Users/someone/Jason',
        '/users/someone/jason/northwind': '/Users/someone/Jason/Northwind',
        '/var': '/private/var',
        '/private': '/private',
        '/private/var': '/private/var',
    };
    return {
        platform: 'darwin',
        cwd: '/Users/someone/.jason/desktop/chat',
        chat: '/Users/someone/.jason/desktop/chat',
        added: ['/Users/someone/Jason/Northwind'],
        data: '/Users/someone/.jason',
        protectedFiles: ['/Users/someone/.jason/config/settings.json', '/Users/someone/.jason/run/runtime.json'],
        denied,
        real: (path) => existing[path.toLowerCase()] ?? null,
    };
}

const read = (scope: ChatScope, file_path: string) => judge('Read', { file_path }, scope);
const write = (scope: ChatScope, file_path: string) => judge('Write', { file_path, content: 'x' }, scope);
const run = (scope: ChatScope, command: string) => judge('Bash', { command }, scope);

// ---- Files inside the chat's folders ---------------------------------------------------------------------------------

test('a read or a write inside the chat folder is allowed without a card, on both platforms', () => {
    assert.deepEqual(read(windows(), 'C:\\Users\\someone\\.jason\\desktop\\chat\\notes.md'), { verdict: 'allow' });
    assert.deepEqual(write(windows(), 'notes\\draft.md'), { verdict: 'allow' });
    assert.deepEqual(read(mac(), '/Users/someone/.jason/desktop/chat/notes.md'), { verdict: 'allow' });
    assert.deepEqual(write(mac(), 'draft.md'), { verdict: 'allow' });
});

test('a read or a write inside an added folder is allowed without a card', () => {
    assert.deepEqual(write(windows(), 'C:\\Users\\someone\\Jason\\Northwind\\campaigns\\spring\\AGENTS.md'), { verdict: 'allow' });
    assert.deepEqual(read(windows(), 'C:\\Users\\someone\\Jason\\Northwind\\brief.md'), { verdict: 'allow' });
    assert.deepEqual(write(mac(), '/Users/someone/Jason/Northwind/AGENTS.md'), { verdict: 'allow' });
});

test('a write to the host\'s own settings folder inside a working folder gets a card, a read of it does not', () => {
    const card = write(windows(), '.claude\\settings.json');
    assert.equal(card.verdict, 'ask');
    assert.deepEqual(read(windows(), '.claude\\skills\\x\\SKILL.md'), { verdict: 'allow' });
    assert.equal(write(mac(), '/Users/someone/Jason/Northwind/.claude/settings.json').verdict, 'ask');
});

test('a file tool with no path works in the chat folder, and Glob and Grep are judged by their folder', () => {
    assert.deepEqual(judge('Grep', { pattern: 'x' }, windows()), { verdict: 'allow' });
    assert.deepEqual(judge('Glob', { pattern: '**/*.md' }, mac()), { verdict: 'allow' });
    assert.equal(judge('Grep', { pattern: 'x', path: 'C:\\elsewhere' }, windows()).verdict, 'ask');
});

// ---- The data directory -----------------------------------------------------------------------------------------------

test('the settings file and the run file are refused without a card, on both platforms', () => {
    for (const scope of [windows(), mac()]) {
        for (const file of scope.protectedFiles) {
            assert.equal(read(scope, file).verdict, 'deny', file);
            assert.equal(write(scope, file).verdict, 'deny', file);
        }
    }
});

test('anything under the data directory outside the chat folder is refused without a card', () => {
    assert.equal(read(windows(), 'C:\\Users\\someone\\.jason\\jason.db').verdict, 'deny');
    assert.equal(write(windows(), 'C:\\Users\\someone\\.jason\\desktop\\settings.json').verdict, 'deny');
    assert.equal(judge('Grep', { pattern: 'token', path: 'C:\\Users\\someone\\.jason' }, windows()).verdict, 'deny');
    assert.equal(read(mac(), '/Users/someone/.jason/logs/runtime-20261110.jsonl').verdict, 'deny');
});

test('a relative path with .. that climbs out of the chat folder is judged where it lands', () => {
    assert.equal(read(windows(), '..\\..\\run\\runtime.json').verdict, 'deny');
    assert.equal(read(windows(), 'sub\\..\\..\\..\\config\\settings.json').verdict, 'deny');
    assert.equal(read(mac(), '../../config/settings.json').verdict, 'deny');
    assert.equal(judge('Glob', { pattern: '../../run/*' }, windows()).verdict, 'deny');
});

test('on Windows, case, drive letter case and slashes do not change where a path is', () => {
    assert.equal(read(windows(), 'c:/USERS/someone/.JASON/run/runtime.json').verdict, 'deny');
    assert.equal(read(windows(), 'C:/Users/someone/.jason/config/settings.json').verdict, 'deny');
    assert.deepEqual(read(windows(), 'c:/users/SOMEONE/.jason/desktop/CHAT/a.md'), { verdict: 'allow' });
});

test('case is folded on Windows and macOS even where the real path keeps the case it was given, and kept on Linux', () => {
    const keeping = (scope: ChatScope): ChatScope => ({ ...scope, real: (path) => (scope.real(path) === null ? null : path) });
    assert.equal(read(keeping(windows()), 'c:\\USERS\\someone\\.JASON\\run\\runtime.json').verdict, 'deny');
    assert.equal(read(keeping(mac()), '/users/SOMEONE/.jason/RUN/runtime.json').verdict, 'deny');
    const linux: ChatScope = { ...mac(), platform: 'linux', real: () => null };
    assert.equal(read(linux, '/Users/someone/.jason/run/runtime.json').verdict, 'deny');
    assert.equal(read(linux, '/users/someone/.jason/run/runtime.json').verdict, 'ask');
});

test('on Windows, the \\\\?\\ form and an 8.3 short name are the same path', () => {
    assert.equal(read(windows(), '\\\\?\\C:\\Users\\someone\\.jason\\run\\runtime.json').verdict, 'deny');
    assert.equal(read(windows(), 'C:\\Users\\SOMEON~1\\.jason\\run\\runtime.json').verdict, 'deny');
    assert.deepEqual(read(windows(), '\\\\?\\C:\\Users\\someone\\.jason\\desktop\\chat\\a.md'), { verdict: 'allow' });
});

test('on Windows, a UNC path is outside every folder of the chat and gets a card; its \\\\?\\UNC form is the same', () => {
    const plain = read(windows(), '\\\\server\\share\\file.txt');
    const long = read(windows(), '\\\\?\\UNC\\server\\share\\file.txt');
    assert.equal(plain.verdict, 'ask');
    assert.deepEqual(long, plain);
});

test('on macOS, case does not change where a path is, and a linked folder is judged where it leads', () => {
    assert.equal(read(mac(), '/users/SOMEONE/.jason/RUN/runtime.json').verdict, 'deny');
    assert.equal(read(mac(), '/var/folders/x').verdict, 'ask');
});

test('a link or a junction in the chat folder that leads into the data directory is refused', () => {
    assert.equal(read(windows(), 'out\\runtime.json').verdict, 'deny');
    assert.equal(write(mac(), 'out/settings.json').verdict, 'deny');
});

test('a path that does not exist yet is judged by its nearest existing parent', () => {
    assert.deepEqual(write(windows(), 'new\\deeper\\file.md'), { verdict: 'allow' });
    assert.equal(write(windows(), 'out\\new\\file.json').verdict, 'deny');
    assert.equal(write(windows(), 'C:\\Users\\someone\\.jason\\plugins\\new\\main.js').verdict, 'deny');
});

test('a real junction or symbolic link that leads out of the chat folder is followed by the real path reader', (t) => {
    const root = mkdtempSync(join(tmpdir(), 'jason-chat-link-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const data = join(root, 'data');
    const chat = join(data, 'desktop', 'chat');
    mkdirSync(chat, { recursive: true });
    mkdirSync(join(data, 'run'));
    writeFileSync(join(data, 'run', 'runtime.json'), '{}');
    symlinkSync(join(data, 'run'), join(chat, 'out'), process.platform === 'win32' ? 'junction' : 'dir');
    const scope: ChatScope = {
        platform: process.platform as Platform,
        cwd: chat,
        chat,
        added: [],
        data,
        protectedFiles: [join(data, 'run', 'runtime.json')],
        denied,
        real: realPathOf,
    };
    assert.equal(read(scope, join('out', 'runtime.json')).verdict, 'deny');
    assert.equal(write(scope, join('out', 'later', 'x.json')).verdict, 'deny');
    assert.deepEqual(write(scope, 'mine.md'), { verdict: 'allow' });
});

test('a read outside every folder gets a card that may be remembered by its folder; a write\'s may not', () => {
    const card = read(windows(), 'C:\\elsewhere\\report.txt');
    assert.equal(card.verdict, 'ask');
    assert.ok(card.verdict === 'ask' && card.remember !== null && card.detail === 'C:\\elsewhere\\report.txt');
    const same = read(windows(), 'c:/ELSEWHERE/other.txt');
    assert.ok(card.verdict === 'ask' && same.verdict === 'ask' && same.remember === card.remember);
    const change = write(windows(), 'C:\\elsewhere\\report.txt');
    assert.ok(change.verdict === 'ask' && change.remember === null);
});

// ---- Commands ---------------------------------------------------------------------------------------------------------

test('a jason command the deny list does not name is allowed without a card', () => {
    for (const scope of [windows(), mac()]) {
        assert.deepEqual(run(scope, 'jason campaign list'), { verdict: 'allow' });
        assert.deepEqual(run(scope, 'jason campaign create --name "Spring outreach"'), { verdict: 'allow' });
        assert.deepEqual(run(scope, 'jason'), { verdict: 'allow' });
    }
});

test('reply auth status and reply account list are allowed without a card, and nothing else of reply', () => {
    assert.deepEqual(run(windows(), 'reply auth status --json'), { verdict: 'allow' });
    assert.deepEqual(run(mac(), 'reply account list'), { verdict: 'allow' });
    assert.equal(run(windows(), 'reply auth login').verdict, 'ask');
    assert.equal(run(windows(), 'reply schedule list').verdict, 'ask');
    assert.equal(run(windows(), 'reply --version').verdict, 'ask');
});

test('every deny-list command is refused without a card, with its arguments and whatever follows', () => {
    for (const scope of [windows(), mac()]) {
        assert.equal(run(scope, 'jason approval approve apr_01J').verdict, 'deny');
        assert.equal(run(scope, 'jason approval set-standing cmp_A --category interested').verdict, 'deny');
        assert.equal(run(scope, 'jason decision answer dec_A --answer "Yes"').verdict, 'deny');
        assert.equal(run(scope, 'jason runtime stop').verdict, 'deny');
        assert.equal(run(scope, 'jason plugin install slack').verdict, 'deny');
        assert.equal(run(scope, 'jason ui').verdict, 'deny');
        assert.equal(run(scope, 'jason journal watch cmp_A').verdict, 'deny');
    }
    assert.deepEqual(run(windows(), 'jason plugin list'), { verdict: 'allow' });
    assert.deepEqual(run(windows(), 'jason journal read cmp_A'), { verdict: 'allow' });
});

test('with the file the desktop carries, export and import are refused, and so are the verbs the chat refuses beyond the CLI', () => {
    const file = parseDenyList(readFileSync(join(import.meta.dirname, '..', denyListFile), 'utf8'));
    for (const scope of [{ ...windows(), denied: file }, { ...mac(), denied: file }]) {
        for (const command of ['jason export --stop', 'jason export', 'jason import --replace backup.zip', 'jason suppression remove --channel email --value a@example.com',
            'jason route set --campaign cmp_A --plugin x --operation campaign.get', 'jason route unset --campaign cmp_A --operation campaign.get',
            'jason role set-profile researcher --execution-profile local-claude']) {
            assert.equal(run(scope, command).verdict, 'deny', command);
        }
    }
});

test('a jason command that starts with an option is refused, as the host\'s own rule refuses it', () => {
    assert.equal(run(windows(), 'jason --actor human:ada approval approve apr_A').verdict, 'deny');
    assert.equal(run(mac(), 'jason --human campaign list').verdict, 'deny');
});

test('a denied verb anywhere in a command refuses it, whatever form names the program', () => {
    for (const scope of [windows(), mac()]) {
        assert.equal(run(scope, 'jason campaign list && jason approval approve apr_A').verdict, 'deny');
        assert.equal(run(scope, 'echo $(jason decision answer dec_A --answer y)').verdict, 'deny');
        assert.equal(run(scope, 'echo `jason ui open`').verdict, 'deny');
        assert.equal(run(scope, "bash -c 'jason approval approve apr_A'").verdict, 'deny');
        assert.equal(run(scope, 'X=1 jason runtime stop').verdict, 'deny');
    }
    assert.equal(run(windows(), '"C:\\Program Files\\Jason\\jason.exe" approval approve apr_A').verdict, 'deny');
    assert.equal(run(windows(), 'JASON.EXE approval approve apr_A').verdict, 'deny');
    assert.equal(run(windows(), '& jason runtime stop').verdict, 'deny');
    assert.equal(run(mac(), '/usr/local/bin/jason approval approve apr_A').verdict, 'deny');
});

test('a call of a denied operation through the Runtime API is refused', () => {
    assert.equal(run(windows(), 'curl -X POST http://127.0.0.1:5000/v1/approval.approve -d "{}"').verdict, 'deny');
    assert.equal(run(mac(), 'curl http://127.0.0.1:5000/v1/approval.set_standing').verdict, 'deny');
    assert.equal(run(mac(), 'curl http://127.0.0.1:5000/v1/runtime.stop').verdict, 'deny');
});

test('a compound command is never allowed without a card and never remembered, even of allowed parts', () => {
    for (const command of ['jason campaign list && jason contact list', 'jason campaign list || true', 'jason campaign list; ls',
        'jason campaign list | more', 'jason campaign list > out.txt', 'jason campaign list < in.txt', 'echo `date`',
        'echo $(date)', 'jason campaign list\njason contact list', 'reply auth status & sleep 1']) {
        for (const scope of [windows(), mac()]) {
            const judged = run(scope, command);
            assert.equal(judged.verdict, 'ask', command);
            assert.ok(judged.verdict === 'ask' && judged.remember === null, command);
        }
    }
});

test('a jason program named by a path, or with an environment in front, gets a card: only the bare name is the bundled one', () => {
    assert.equal(run(windows(), '.\\jason campaign list').verdict, 'ask');
    assert.equal(run(mac(), './jason campaign list').verdict, 'ask');
    assert.equal(run(mac(), 'X=1 jason campaign list').verdict, 'ask');
});

test('any other command gets a card, remembered by the program and its first word', () => {
    const card = run(windows(), 'git status --short');
    assert.ok(card.verdict === 'ask' && card.title === 'Run a command' && card.detail === 'git status --short');
    const again = run(windows(), 'git status');
    const other = run(windows(), 'git push origin main');
    assert.ok(card.verdict === 'ask' && again.verdict === 'ask' && other.verdict === 'ask');
    assert.equal(card.remember, again.remember);
    assert.notEqual(card.remember, other.remember);
    assert.equal(commandPrefix('git status --short'), 'git status');
    assert.equal(commandPrefix('npm -v'), null);
    assert.equal(commandPrefix('ls'), null);
    assert.equal(commandPrefix('git status; rm -rf x'), null);
});

test('a read-only command naming a path outside the chat\'s folders gets a card, never an allowance', () => {
    // The host asks for these only because every turn blocks reads outside its working folders (claudeArguments);
    // once asked, the answer is the person's, whatever the program.
    const outside: [ChatScope, string, string][] = [
        [mac(), 'Bash', 'cat ~/.ssh/id_rsa'],
        [mac(), 'Bash', 'ls ~/Documents'],
        [mac(), 'Bash', 'grep -r password ~/.aws'],
        [mac(), 'Bash', 'head -n 5 /Users/someone/.zsh_history'],
        [windows(), 'Bash', 'cat C:/Users/someone/.ssh/id_rsa'],
        [windows(), 'PowerShell', 'Get-Content C:\\Users\\someone\\.aws\\credentials'],
        [windows(), 'PowerShell', 'Get-ChildItem C:\\Users\\someone\\Documents'],
    ];
    for (const [scope, tool, command] of outside) {
        const judged = judge(tool, { command }, scope);
        assert.ok(judged.verdict === 'ask' && judged.title === 'Run a command' && judged.detail === command, command);
    }
});

test('the shell operators that make a command compound are each recognised', () => {
    for (const command of ['a && b', 'a || b', 'a; b', 'a | b', 'a `b`', 'a $(b)', 'a > b', 'a >> b', 'a 2> b', 'a < b', 'a\nb', 'a & b']) {
        assert.ok(isCompound(command), command);
    }
    assert.ok(!isCompound('git status --short'));
});

test('PowerShell commands are judged as shell commands', () => {
    assert.equal(judge('PowerShell', { command: 'jason approval approve apr_A' }, windows()).verdict, 'deny');
    assert.deepEqual(judge('PowerShell', { command: 'jason campaign list' }, windows()), { verdict: 'allow' });
    assert.equal(judge('PowerShell', { command: 'Get-ChildItem' }, windows()).verdict, 'ask');
});

test('a web page gets a card remembered by its host; any other tool gets a card remembered by its name', () => {
    const page = judge('WebFetch', { url: 'https://example.com/a', prompt: 'p' }, mac());
    const same = judge('WebFetch', { url: 'https://example.com/b', prompt: 'p' }, mac());
    assert.ok(page.verdict === 'ask' && same.verdict === 'ask' && page.remember === same.remember && page.remember !== null);
    const other = judge('NotATool', { x: 1 }, mac());
    assert.ok(other.verdict === 'ask' && other.remember !== null);
});
