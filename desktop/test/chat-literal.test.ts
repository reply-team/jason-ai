// The literal reading of a shell command, and Full access read through it: a word runs only if it is literal for its
// shell, anything else in a command holds the command, and a literal command is still refused or held by what its
// resolved words say. Every command here is a neutral one or a made-up program; every path is made up or a temporary one.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, posix, win32 } from 'node:path';
import { test } from 'node:test';
import { readFence } from '../src/chat-deny.ts';
import { readLiteral, type HeldReason, type LiteralContext } from '../src/chat-literal.ts';
import { powerShellRunsInFullAccess } from '../src/chat-modes.ts';
import { fullAccessJudgement, heldSentence, judge, type ChatScope, type Platform } from '../src/chat-permission-model.ts';
import { realPathOf } from '../src/chat-real-path.ts';

const fence = readFence(join(import.meta.dirname, '..'));
const linux: LiteralContext = { platform: 'linux', home: '/home/someone' };

/** The resolved words of each simple command, or a failure saying why it was held. */
function wordsOf(tool: string, command: string, context: LiteralContext = linux): string[][] {
    const reading = readLiteral(tool, command, context);
    assert.ok(!reading.held, `${tool} holds ${JSON.stringify(command)}: ${reading.held ? reading.reason : ''}`);
    return reading.commands.map((each) => each.words);
}

function reasonOf(tool: string, command: string, context: LiteralContext = linux): HeldReason | null {
    const reading = readLiteral(tool, command, context);
    return reading.held ? reading.reason : null;
}

/** A scope whose data directory is the default one under a made-up home folder, nothing on disk. */
function homeScope(platform: Platform, home: string): ChatScope {
    const paths = platform === 'win32' ? win32 : posix;
    const data = paths.join(home, '.jason');
    const chat = paths.join(data, 'desktop', 'chat');
    return { platform, cwd: chat, chat, added: [], data, home, protectedFiles: [], denied: fence.commands, deniedOperations: fence.operations, real: () => null };
}

const scope = homeScope('linux', '/home/someone');
const heldAs = (reason: HeldReason) => ({ outcome: 'held', message: heldSentence, reason });
const runs = { outcome: 'runs' };

// Table 1.2, POSIX: the Bash tool's shell, Git Bash on Windows.

test('POSIX: each bare-word character is literal and passes on as it is', () => {
    for (const character of [...'AZaz09_./:@+,%-=']) {
        assert.deepEqual(wordsOf('Bash', `echo x${character}y`), [['echo', `x${character}y`]], character);
    }
    assert.deepEqual(wordsOf('Bash', 'frobnicate --level=2 a,b user@host:1 50%'), [['frobnicate', '--level=2', 'a,b', 'user@host:1', '50%']]);
});

test('POSIX: a ~ alone or before a slash at a word\'s start is the home folder; anywhere else it is held', () => {
    assert.deepEqual(wordsOf('Bash', 'ls ~'), [['ls', '/home/someone']]);
    assert.deepEqual(wordsOf('Bash', 'ls ~/notes'), [['ls', '/home/someone/notes']]);
    for (const command of ['ls ~other', 'ls a~', 'frobnicate --dir=~', 'frobnicate a=~/x', 'ls ~:x']) assert.equal(reasonOf('Bash', command), 'tilde', command);
});

test('POSIX: single and double quotes are removed, pieces glued into one word', () => {
    assert.deepEqual(wordsOf('Bash', "echo 'a b' \"c d\""), [['echo', 'a b', 'c d']]);
    assert.deepEqual(wordsOf('Bash', "echo fro''bnic\"ate\""), [['echo', 'frobnicate']]);
    assert.deepEqual(wordsOf('Bash', "echo '$x \\ * ! #' \"* ? ' #\""), [['echo', '$x \\ * ! #', "* ? ' #"]]);
    assert.deepEqual(wordsOf('Bash', "echo ''"), [['echo', '']]);
});

test('POSIX: a quote held open, a newline in quotes, or a $, a backtick, a backslash or a ! in double quotes is held', () => {
    assert.equal(reasonOf('Bash', "echo 'open"), 'quote');
    assert.equal(reasonOf('Bash', 'echo "open'), 'quote');
    assert.equal(reasonOf('Bash', "echo 'a\nb'"), 'quote');
    assert.equal(reasonOf('Bash', 'echo "a\nb"'), 'quote');
    assert.equal(reasonOf('Bash', 'echo "$HOME"'), 'expansion');
    assert.equal(reasonOf('Bash', 'echo "`date`"'), 'expansion');
    assert.equal(reasonOf('Bash', 'echo "a\\b"'), 'escape');
    assert.equal(reasonOf('Bash', 'echo "hi!"'), 'history');
});

test('POSIX: the operators && || ; | and a newline separate commands; a pipe is the next command\'s input', () => {
    assert.deepEqual(wordsOf('Bash', 'echo a && echo b || echo c; echo d\necho e'), [['echo', 'a'], ['echo', 'b'], ['echo', 'c'], ['echo', 'd'], ['echo', 'e']]);
    const reading = readLiteral('Bash', 'echo a | frobnicate', linux);
    assert.ok(!reading.held);
    assert.deepEqual(reading.commands.map((each) => each.input), ['none', 'pipe']);
    assert.deepEqual(wordsOf('Bash', 'echo a &&\n  echo b'), [['echo', 'a'], ['echo', 'b']]);
    assert.deepEqual(wordsOf('Bash', 'echo a;'), [['echo', 'a']]);
});

test('POSIX: > >> 2> 2>&1 and < before a literal target are read, the target kept apart from the words', () => {
    const reading = readLiteral('Bash', 'frobnicate a > out.txt 2>&1; frobnicate >> log.txt 2> err.txt < in.txt', linux);
    assert.ok(!reading.held);
    assert.deepEqual(reading.commands.map((each) => [each.words, each.targets, each.input]), [
        [['frobnicate', 'a'], ['out.txt'], 'none'],
        [['frobnicate'], ['log.txt', 'err.txt', 'in.txt'], 'file'],
    ]);
    assert.deepEqual(readLiteral('Bash', "echo a > 'my file'", linux), { held: false, commands: [{ words: ['echo', 'a'], targets: ['my file'], input: 'none', cwd: null }] });
});

test('POSIX: a here-document with a quoted tag is the command\'s input, and its body is data', () => {
    const reading = readLiteral('Bash', "frobnicate --file - <<'EOF'\n$HOME `x` * ü \\\nEOF\necho done", linux);
    assert.ok(!reading.held);
    assert.deepEqual(reading.commands.map((each) => [each.words, each.input]), [[['frobnicate', '--file', '-'], 'here_document'], [['echo', 'done'], 'none']]);
    assert.ok(!readLiteral('Bash', 'frobnicate <<"END"\nline\nEND', linux).held);
});

test('POSIX: an unquoted, open-ended or tab-stripped here-document tag is held', () => {
    for (const command of ['frobnicate <<EOF\nx\nEOF', "frobnicate <<'EOF'\nx", 'frobnicate <<-\'EOF\'\nx\nEOF', "frobnicate <<'E'OF\nx\nEOF"]) {
        assert.equal(reasonOf('Bash', command), 'here_document', JSON.stringify(command));
    }
});

test('POSIX: each held character is held with its reason', () => {
    const rows: [string, HeldReason][] = [
        ['echo $HOME', 'expansion'],
        ['echo `date`', 'expansion'],
        ['echo a\\ b', 'escape'],
        ['echo a \\\n b', 'escape'],
        ['echo *.md', 'glob'],
        ['echo a?', 'glob'],
        ['echo [ab]', 'glob'],
        ['echo a]', 'glob'],
        ['echo {a,b}', 'brace'],
        ['echo a}', 'brace'],
        ['echo (a)', 'group'],
        ['echo a)', 'group'],
        ['echo !1', 'history'],
        ['echo a #b', 'comment'],
        ['echo a#b', 'comment'],
        ['echo a^b', 'character'],
        ['frobnicate &', 'background'],
        ['frobnicate & echo a', 'background'],
        ['echo é', 'non_ascii'],
        ['echo a\rb', 'control_character'],
        ['X=1 frobnicate', 'assignment'],
        ['X+=1 frobnicate', 'assignment'],
        ['frobnicate <(echo a)', 'process_substitution'],
        ['frobnicate >(echo a)', 'process_substitution'],
        ['frobnicate <<< a', 'operator'],
        ['frobnicate &> out', 'background'],
        ['frobnicate 1> out', 'operator'],
        ['frobnicate >| out', 'operator'],
        ['frobnicate |& frobnicate', 'operator'],
        ['frobnicate >', 'syntax'],
        ['| frobnicate', 'syntax'],
        ['frobnicate &&', 'syntax'],
        ['frobnicate ;; frobnicate', 'syntax'],
        ['', 'syntax'],
    ];
    for (const [command, reason] of rows) assert.equal(reasonOf('Bash', command), reason, JSON.stringify(command));
});

test('POSIX: an assignment word is held only as the first word; as an argument it is literal', () => {
    assert.deepEqual(wordsOf('Bash', 'frobnicate X=1'), [['frobnicate', 'X=1']]);
    assert.deepEqual(wordsOf('Bash', "'X=1'"), [['X=1']]);
});

// Table 1.2, PowerShell: the PowerShell tool's shell.

test('PowerShell: each bare-word character is literal and passes on as it is', () => {
    for (const character of [...'AZaz09_./\\:+=%-']) {
        assert.deepEqual(wordsOf('PowerShell', `echo x${character}y`), [['echo', `x${character}y`]], character);
    }
});

test('PowerShell: single quotes with a doubled quote inside, and double quotes without $ or a backtick, are removed', () => {
    assert.deepEqual(wordsOf('PowerShell', "echo 'it''s' \"a b\" fro'bni'cate"), [['echo', "it's", 'a b', 'frobnicate']]);
    assert.deepEqual(wordsOf('PowerShell', "echo '$x * ~ @ ,'"), [['echo', '$x * ~ @ ,']]);
    assert.equal(reasonOf('PowerShell', 'echo "$x"'), 'expansion');
    assert.equal(reasonOf('PowerShell', 'echo "a`b"'), 'expansion');
    assert.equal(reasonOf('PowerShell', "echo 'open"), 'quote');
    assert.equal(reasonOf('PowerShell', 'echo \'say "hi"\''), 'quote', 'a word that holds a double quote is not passed on as it reads');
    assert.equal(reasonOf('PowerShell', "'frobnicate' a"), 'call_operator', 'a quoted first word is a value, not a program');
});

test('PowerShell: the operators ; | && || and > >> before a literal target are read', () => {
    assert.deepEqual(wordsOf('PowerShell', 'echo a; echo b | frobnicate && echo c || echo d'), [['echo', 'a'], ['echo', 'b'], ['frobnicate'], ['echo', 'c'], ['echo', 'd']]);
    const reading = readLiteral('PowerShell', 'frobnicate > out.txt; frobnicate >> log.txt', linux);
    assert.ok(!reading.held);
    assert.deepEqual(reading.commands.map((each) => each.targets), [['out.txt'], ['log.txt']]);
});

test('PowerShell: each held character or form is held with its reason', () => {
    const rows: [string, HeldReason][] = [
        ['echo $x', 'expansion'],
        ['echo a`b', 'expansion'],
        ['echo (a)', 'group'],
        ['echo a)', 'group'],
        ['echo {a}', 'brace'],
        ['echo a}', 'brace'],
        ['echo @a', 'array'],
        ['echo a,b', 'array'],
        ['frobnicate --% a', 'stop_parsing'],
        ['& frobnicate', 'call_operator'],
        ['. ./x.ps1', 'call_operator'],
        ['frobnicate &', 'background'],
        ['ls ~', 'tilde'],
        ['ls ~/notes', 'tilde'],
        ['echo *.md', 'glob'],
        ['echo a?', 'glob'],
        ['echo [a]', 'glob'],
        ['echo a]', 'glob'],
        ['echo #a', 'comment'],
        ['echo a#b', 'comment'],
        ['frobnicate < in.txt', 'operator'],
        ['frobnicate 2> err.txt', 'operator'],
        ['frobnicate *> all.txt', 'glob'],
        ['echo a\necho b', 'operator'],
        ['echo a!', 'character'],
        ['frobnicate |', 'syntax'],
    ];
    for (const [command, reason] of rows) assert.equal(reasonOf('PowerShell', command), reason, JSON.stringify(command));
});

test('PowerShell: the three characters outside ASCII it reads as quotes and a dash are held', () => {
    assert.equal(reasonOf('PowerShell', 'echo ‘a‘'), 'non_ascii', 'a left single quotation mark');
    assert.equal(reasonOf('PowerShell', 'echo ’a’'), 'non_ascii', 'a right single quotation mark');
    assert.equal(reasonOf('PowerShell', 'frobnicate –level 2'), 'non_ascii', 'an en dash');
});

test('another tool\'s command is held whole: its shell\'s grammar is not known', () => {
    assert.equal(reasonOf('Shell', 'echo a'), 'unknown_shell');
});

// Table 1.2, cmd: never the tool's shell, so it is held whole.

test('cmd, a .bat or a .cmd with arguments is held whole, in either shell', () => {
    for (const tool of ['Bash', 'PowerShell']) {
        for (const command of ['cmd /c echo a', 'cmd.exe /k frobnicate', 'go.bat a', 'tools/go.cmd a', 'echo a | cmd']) {
            assert.deepEqual(fullAccessJudgement(tool, { command }, scope), heldAs('command_prompt'), `${tool}: ${command}`);
        }
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'go.bat' }, scope), runs, 'a batch file with no arguments is a file the agent wrote');
});

// 1.3 (a): a literal command is still refused by its resolved words.

test('a person\'s verbs are refused in every mode, Full access included, never a card', () => {
    for (const command of ['jason approval approve apr_1', 'jason decision answer dec_1 --option a', 'jason block clear blk_1', "jason approval 'approve' apr_1", 'jason approval ap"pro"ve apr_1', 'sudo jason approval approve apr_1', 'echo approval approve apr_1 | xargs jason approval approve']) {
        const judged = fullAccessJudgement('Bash', { command }, scope);
        assert.equal(judged.outcome, 'refused', command);
        assert.match(judged.outcome === 'refused' ? judged.message : '', /a person's own act/, command);
        assert.equal(judge('Bash', { command }, scope).verdict, 'deny', `Ask first: ${command}`);
    }
    assert.equal(fullAccessJudgement('PowerShell', { command: 'jason approval approve apr_1' }, scope).outcome, 'refused');
});

test('a URL word is read with its percent-escapes decoded', () => {
    const judged = fullAccessJudgement('Bash', { command: 'curl -X POST http://127.0.0.1:4011/v1/approval%2Eapprove' }, scope);
    assert.equal(judged.outcome, 'refused');
});

test('the data directory is refused by its resolved path, the home folder\'s ~ resolved', () => {
    for (const command of ['cat ~/.jason/run/runtime.json', 'cat /home/someone/.jason/run/runtime.json', 'cat ../../run/runtime.json', 'frobnicate > ~/.jason/x', 'frobnicate --out=/home/someone/.jason/x']) {
        assert.equal(fullAccessJudgement('Bash', { command }, scope).outcome, 'refused', command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'cat notes.md' }, scope), runs, 'a file in the chat folder');
});

test('the chat\'s own instructions and agent settings are refused to a literal command', () => {
    for (const command of ['cat .claude/settings.json', 'echo a >> CLAUDE.md']) assert.equal(fullAccessJudgement('Bash', { command }, scope).outcome, 'refused', command);
});

test('a resolved command that clears the environment around a jason is refused for the confined variable', () => {
    const judged = fullAccessJudgement('Bash', { command: 'env -i jason campaign list' }, scope);
    assert.equal(judged.outcome, 'refused');
    assert.match(judged.outcome === 'refused' ? judged.message : '', /JASON_CONFINED/);
});

// 1.3 (b): held, every word literal.

test('a shell or an interpreter given code as text, or code piped or here-documented into it, is held', () => {
    for (const command of [
        'bash -c "echo a"',
        'sh -lc "echo a"',
        'python3 -c "print(1)"',
        'node -e "1"',
        'node --eval=1',
        'perl -ne "print"',
        'ruby -e "p 1"',
        "awk '{print}' notes.md",
        'echo a | sh',
        'echo a | python3 x.py',
        "sh <<'EOF'\necho a\nEOF",
    ]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('inline_code'), command);
    }
    for (const command of ['pwsh -Command Get-Date', 'pwsh -c Get-Date', 'pwsh -EncodedCommand ZQBjAGgAbwA=', 'powershell Get-Date']) {
        assert.deepEqual(fullAccessJudgement('PowerShell', { command }, scope), heldAs('inline_code'), command);
    }
});

test('a shell or an interpreter given a file runs: it is a program the agent wrote', () => {
    for (const command of ['sh go.sh', 'python3 x.py', 'node build.js', 'awk -f prog.awk notes.md']) assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, command);
    assert.ok(!readLiteral('PowerShell', 'pwsh -File go.ps1', linux).held, 'PowerShell runs nothing unasked in Full access; its grammar still reads this');
});

test('a runner that starts a later or another command is held', () => {
    for (const command of ['xargs frobnicate', 'env frobnicate', 'sudo frobnicate', 'nohup frobnicate', 'timeout 5 frobnicate', 'eval frobnicate', 'source go.sh', '. go.sh', 'exec frobnicate', 'ssh host frobnicate', "find . -name x -exec frobnicate '{}' ';'", 'git -c alias.x=status x', 'git config core.hooksPath hooks']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('runner'), command);
    }
    for (const command of ['Start-Process frobnicate', 'Invoke-Expression frobnicate', 'iex frobnicate', 'Invoke-Command frobnicate']) {
        assert.deepEqual(fullAccessJudgement('PowerShell', { command }, scope), heldAs('runner'), command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'git status' }, scope), runs);
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'find . -name notes.md' }, scope), runs);
});

test('a builtin that changes how later words are read is held', () => {
    for (const command of ['alias frobnicate=ls', 'set -f', 'shopt -s extglob', 'enable -n echo', 'export X=1', 'unset X', 'trap frobnicate EXIT']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('builtin'), command);
    }
    for (const command of ['Set-Alias frobnicate ls', 'New-Alias frobnicate ls', 'Set-Item function:frobnicate x']) {
        assert.deepEqual(fullAccessJudgement('PowerShell', { command }, scope), heldAs('builtin'), command);
    }
});

test('a command part holding mcp followed by add, remove or login is held, whatever its program', () => {
    for (const command of ['frobnicate mcp add tools', 'frobnicate mcp remove tools', 'frobnicate mcp login tools']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('servers'), command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'echo a && frobnicate --scope user MCP Add tools' }, scope), heldAs('servers'), 'options between, any case, a later part');
    assert.deepEqual(fullAccessJudgement('PowerShell', { command: 'frobnicate mcp add tools' }, scope), heldAs('servers'));
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'frobnicate mcp list' }, scope), runs);
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'frobnicate add mcp' }, scope), runs, 'the verb before the word is not the server command');
});

test('a guarded name in a path, at any depth, is held', () => {
    for (const command of ['cat notes/.git/config', 'echo a >> repo/.git/hooks/pre-commit', 'cat a/b/AGENTS.md', 'ls work/.jason']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('guarded_name'), command);
    }
});

test('a search variable named, a jason of the agent\'s own, and a folder that holds the data directory are held', () => {
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'echo PATH' }, scope), heldAs('search'));
    assert.equal(fullAccessJudgement('Bash', { command: 'PATH=/tmp frobnicate' }, scope).outcome, 'refused', 'a change of the search is refused as in Ask first');
    assert.deepEqual(fullAccessJudgement('Bash', { command: './jason campaign list' }, scope), heldAs('local_jason'));
    for (const command of ['ls /home/someone', 'ls ~', 'du -s /']) assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('holds_data'), command);
});

test('a relative word after a directory change to a relative target is held; after a literal absolute one it is read from there', () => {
    for (const command of ['cd sub && cat notes.md', 'cd sub/.. && ls', 'pushd sub; cat notes.md', 'cd - && ls', 'popd && ls']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('directory_change'), command);
    }
    assert.deepEqual(fullAccessJudgement('PowerShell', { command: 'Set-Location sub; cat notes.md' }, scope), heldAs('directory_change'));
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'cd sub' }, scope), runs, 'a change with nothing after it reads nothing');
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'cd /home/someone/work && cat notes.md' }, scope), runs);
    const located = readLiteral('PowerShell', 'Set-Location -Path /home/someone/work && cat notes.md', linux);
    assert.deepEqual(located.held ? null : located.commands.map((each) => each.cwd), [null, '/home/someone/work']);
    assert.equal(fullAccessJudgement('Bash', { command: 'cd /home/someone && cat .jason/run/runtime.json' }, scope).outcome, 'refused', 'read from the absolute target');
    assert.equal(fullAccessJudgement('Bash', { command: 'cd ~ && cat .jason/run/runtime.json' }, scope).outcome, 'refused');
});

test('the data directory is reached through a link by its resolved path, never by its spelling', (t) => {
    const platform: Platform = process.platform === 'win32' || process.platform === 'darwin' ? process.platform : 'linux';
    const root = realpathSync.native(mkdtempSync(join(tmpdir(), 'jason-literal-')));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const data = join(root, 'data');
    const chat = join(data, 'desktop', 'chat');
    mkdirSync(join(data, 'run'), { recursive: true });
    mkdirSync(chat, { recursive: true });
    const link = join(root, 'link');
    symlinkSync(data, link, 'junction');
    const linked: ChatScope = { platform, cwd: chat, chat, added: [], data, home: join(root, 'home'), protectedFiles: [], denied: fence.commands, deniedOperations: fence.operations, real: realPathOf };
    const slashed = (path: string) => path.replace(/\\/g, '/');
    assert.equal(fullAccessJudgement('Bash', { command: `cat ${slashed(join(link, 'run', 'runtime.json'))}` }, linked).outcome, 'refused');
    assert.equal(fullAccessJudgement('Bash', { command: `cd ${slashed(link)} && cat run/runtime.json` }, linked).outcome, 'refused');
    assert.deepEqual(fullAccessJudgement('Bash', { command: `cat ${slashed(join(root, 'elsewhere.md'))}` }, linked), runs);
});

// Ruling 4: reply in Full access.

test('reply is held in Full access but for its exact no-card forms', () => {
    for (const command of ['reply sequence start seq_1', 'reply api DELETE /v3/sequences/1', 'reply account delete acc_1 --yes', 'npx reply-cli sequence start seq_1', 'reply auth status --json > out.json']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('reply'), command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'reply auth status --json' }, scope), runs);
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'reply account list --channel email' }, scope), runs);
    assert.deepEqual(fullAccessJudgement('PowerShell', { command: 'reply sequence start seq_1' }, scope), heldAs('reply'));
});

// What runs.

test('a plain literal command runs with no question', () => {
    for (const command of ['echo a', 'frobnicate --level=2 notes.md', 'git status', 'jason campaign list', 'git commit -m "a plain message"', "jason campaign create --file - <<'EOF'\n{\"name\":\"x\"}\nEOF"]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, command);
    }
});

// F4a's order: a tool of the person's own servers is held whatever its input, ahead of the literal reading.

test('a tool of an MCP server is held in Full access whatever its input', () => {
    for (const input of [{}, { command: 'echo a' }, { file_path: 'notes.md' }]) {
        assert.equal(fullAccessJudgement('mcp__frobnicate__run', input, scope).outcome, 'held', JSON.stringify(input));
    }
});

// The fix round, POSIX first: what the module called the program was not always the program that runs.

test('POSIX: a reserved word in first position is held, bash\'s and zsh\'s, since the program that runs is a later word', () => {
    const reserved = ['if', 'then', 'elif', 'else', 'fi', 'do', 'done', 'case', 'esac', 'while', 'until', 'for', 'select', 'function', 'coproc', 'time', 'in'];
    const zsh = ['-', 'noglob', 'nocorrect', 'repeat', 'foreach'];
    for (const word of [...reserved, ...zsh]) {
        assert.equal(reasonOf('Bash', `${word} frobnicate a`), 'syntax', word);
        assert.equal(fullAccessJudgement('Bash', { command: `echo a; ${word} frobnicate a` }, scope).outcome, 'held', `a later command: ${word}`);
    }
    assert.equal(fullAccessJudgement('Bash', { command: 'if frobnicate a; then python3 -c x; fi' }, scope).outcome, 'held');
    for (const command of ['builtin echo a', 'command echo a']) assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('runner'), command);
    assert.deepEqual(wordsOf('Bash', 'echo if then fi'), [['echo', 'if', 'then', 'fi']], 'a reserved word as an argument is a word');
});

test('POSIX: a word that starts with = is held, since zsh rewrites it to a program\'s path', () => {
    for (const command of ['echo =frobnicate', '=frobnicate a', "echo '=x' =y"]) assert.equal(reasonOf('Bash', command), 'expansion', command);
    assert.deepEqual(wordsOf('Bash', 'echo a=b --x=1'), [['echo', 'a=b', '--x=1']]);
});

test('POSIX: a character outside ASCII or a control character inside quotes is held; a here-document\'s body stays data', () => {
    const rows: [string, HeldReason][] = [
        ["echo '\uFF02'", 'non_ascii'],
        ['echo "\u00E9"', 'non_ascii'],
        ["echo 'a\u0000b'", 'control_character'],
        ['echo "a\rb"', 'control_character'],
        ["echo 'a\u001Bb'", 'control_character'],
    ];
    for (const [command, reason] of rows) assert.equal(reasonOf('Bash', command), reason, JSON.stringify(command));
    assert.deepEqual(wordsOf('Bash', "echo 'a\tb'"), [['echo', 'a\tb']], 'a tab inside quotes is a character of the word');
    assert.ok(!readLiteral('Bash', "frobnicate <<'EOF'\n\u00E9 \u0000\nEOF", linux).held);
});

test('POSIX: the redirections outside the literal set, a NUL outside quotes and ;& are held; a tab is a blank', () => {
    const rows: [string, HeldReason][] = [
        ['frobnicate <> f', 'operator'],
        ['frobnicate >&2', 'operator'],
        ['frobnicate <&0', 'operator'],
        ['frobnicate 2>> f', 'operator'],
        ['echo a ;& echo b', 'syntax'],
        ['echo a\u0000b', 'control_character'],
    ];
    for (const [command, reason] of rows) assert.equal(reasonOf('Bash', command), reason, JSON.stringify(command));
    assert.deepEqual(wordsOf('Bash', 'echo\ta\tb'), [['echo', 'a', 'b']]);
});

test('POSIX: a redirection to a device the shell emulates is held; /dev/null is a file', () => {
    for (const command of ['frobnicate > /dev/tcp/127.0.0.1/80', 'frobnicate < /dev/udp/127.0.0.1/53', 'frobnicate 2> /dev/fd/3', 'frobnicate > /dev/stderr']) {
        assert.equal(reasonOf('Bash', command), 'operator', command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'frobnicate a > /dev/null 2>&1' }, scope), runs);
});

test('code glued to its option, given by --command=, beside a file, or in a module option is held', () => {
    for (const command of [
        "python3 -c'import os'",
        'python3 -Bc"print(1)"',
        "perl -e'print 1'",
        "ruby -e'p 1'",
        "php -r'echo 1;'",
        "node -e'1'",
        'bash --command=frobnicate',
        "awk -f prog.awk -e 'BEGIN{}' notes.md",
        'gawk -f prog.awk --source x notes.md',
        'julia -E 1',
        "perl -M'strict;print 1' x.pl",
    ]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('inline_code'), command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'perl -Mstrict x.pl' }, scope), runs, 'a module named, no code');
});

test('a builtin that evaluates text or runs a callback is held, and an array subscript handed to one that evaluates it', () => {
    for (const command of ["let 'a[1]'", 'mapfile -C frobnicate a', 'readarray a', 'compgen -C frobnicate', 'complete -C frobnicate x']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('runner'), command);
    }
    for (const command of ["printf -v 'a[1]' x", "read 'a[1]'", "test -v 'a[1]'", "getopts ab 'a[1]'", "wait -p 'a[1]'", "printf '$x'", "printf '`x`'"]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('expansion'), command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: "printf '%s' a" }, scope), runs);
});

test('a package runner given shell code, git\'s execute options, and the runners that start a command they are given are held', () => {
    for (const command of ["npx -c 'frobnicate a'", "npx --call='frobnicate a'", "npm exec -c 'frobnicate a'", "npm x --call='frobnicate a'", "pnpm exec -c 'frobnicate a'"]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('inline_code'), command);
    }
    for (const command of [
        'git clone -u frobnicate repo',
        'git clone --upload-pack=frobnicate repo',
        'git fetch --upload-pack=frobnicate',
        'git ls-remote --upload-pack frobnicate origin',
        'git push --receive-pack=frobnicate',
        'git archive --exec=frobnicate --remote=origin HEAD',
        'git grep -O frobnicate',
        'git grep --open-files-in-pager=frobnicate x',
        'git clone -c core.x=y repo',
        'git clone --config core.x=y repo',
        'git send-email --sendmail-cmd=frobnicate a.patch',
        'docker run img',
        'podman run img',
        'kubectl exec pod -- frobnicate',
        'rsync -e frobnicate a b',
        'tar --to-command=frobnicate -xf a.tar',
        'tar -I frobnicate -xf a.tar',
        'sort --compress-program=frobnicate notes.md',
    ]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('runner'), command);
    }
    for (const command of ['vim -c q notes.md', 'vim +q notes.md', 'gdb -ex run frobnicate']) assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('inline_code'), command);
    for (const command of ['git push -u origin main', 'git grep -n x', 'git clone repo', 'npx frobnicate', 'tar -xf a.tar', 'sort notes.md']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, command);
    }
});

test('a server verb that starts with add, and the plugin verbs, are held whatever the program', () => {
    for (const command of ["frobnicate mcp add-json tools '{}'", 'frobnicate mcp add-from-claude-desktop', 'frobnicate plugin install tools', 'frobnicate plugin marketplace add tools', 'frobnicate plugins install tools', 'frobnicate plugin enable tools']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('servers'), command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'frobnicate plugin list' }, scope), runs);
});

test('a directory change sets the folder only when && joins it to the next command; after anything else the folder is unknown', () => {
    for (const command of [
        'cd /home/someone/work | cat notes.md',
        'cd /home/someone/work || cat notes.md',
        'cd /home/someone/work; cat notes.md',
        'cd /home/someone/work\ncat notes.md',
        'cd /home/someone/work && echo a; cat notes.md',
        'cd /home/someone/work && echo a || cat notes.md',
        'echo a | cd /home/someone/work && cat notes.md',
    ]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), heldAs('directory_change'), JSON.stringify(command));
    }
    for (const command of ['cd /home/someone/work && cat notes.md', 'cd /home/someone/work && echo a | cat notes.md', 'cd /home/someone/work && echo a && cat notes.md']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, command);
    }
});

test('~ resolves to the home the host\'s shell carries, and is held when that differs from the profile', () => {
    const elsewhere: ChatScope = { ...scope, shellHome: '/home/other' };
    for (const command of ['ls ~/notes', 'cat ~', 'frobnicate > ~/out.txt']) assert.deepEqual(fullAccessJudgement('Bash', { command }, elsewhere), heldAs('tilde'), command);
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'ls /home/someone/notes' }, elsewhere), runs);
    const same: ChatScope = { ...scope, shellHome: '/home/someone/' };
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'ls ~/notes' }, same), runs);
    const windows = homeScope('win32', 'C:\\Users\\someone');
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'ls ~/notes' }, { ...windows, shellHome: '/c/Users/someone' }), runs, 'Git Bash spells the same folder its own way');
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'ls ~/notes' }, { ...windows, shellHome: 'D:\\home' }), heldAs('tilde'));
});

test('on Windows a Bash word Git Bash would rewrite for a Windows program is held; a drive\'s own path is not', () => {
    const windows = homeScope('win32', 'C:\\Users\\someone');
    for (const command of ['cat /tmp/x', 'frobnicate --out=/tmp/x', 'frobnicate /a:/b', '/usr/bin/frobnicate a', 'frobnicate > /tmp/x']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, windows), heldAs('character'), command);
    }
    for (const command of ['cat /c/Work/notes.md', 'cat /d', 'frobnicate a 2>/dev/null', 'frobnicate --level=2']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, windows), runs, command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'cat /tmp/x' }, scope), runs, 'not on Linux');
});

test('anything that goes wrong while a command is judged in Full access holds it, never runs it', () => {
    const failing: ChatScope = { ...scope, real: () => { throw new Error('no reading'); } };
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'cat notes.md' }, failing), heldAs('syntax'));
});

// PowerShell in this version: every command of the PowerShell tool is held in Full access, the grammar kept behind one switch.

test('the PowerShell tool runs nothing unasked in Full access while its switch is off: every command is held or refused', () => {
    assert.equal(powerShellRunsInFullAccess, false);
    for (const command of ['echo a', 'Get-ChildItem notes', 'frobnicate --level=2 notes.md', 'git status']) {
        assert.deepEqual(fullAccessJudgement('PowerShell', { command }, scope), heldAs('unknown_shell'), command);
    }
    assert.deepEqual(fullAccessJudgement('PowerShell', { command: 'Invoke-Expression frobnicate' }, scope), heldAs('runner'), 'a hold of its own keeps its reason');
    assert.equal(fullAccessJudgement('PowerShell', { command: 'jason approval approve apr_1' }, scope).outcome, 'refused');
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'echo a' }, scope), runs, 'the Bash tool\'s literal commands still run');
});
