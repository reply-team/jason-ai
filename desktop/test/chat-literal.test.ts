// The literal reading of a shell command, and Full access read through it: a word is read only if it is literal for its
// shell, and anything else in a command holds the command, with a closed reason. Full access is Claude Code's own bypass
// now: a command the reading holds runs there all the same, the hook answering nothing, and a literal command is refused
// only where its resolved words name one of the three kinds. Every command here is a neutral one or a made-up program;
// every path is made up or a temporary one.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, posix, win32 } from 'node:path';
import { test } from 'node:test';
import { modeFence, personsWordForms, readFence } from '../src/chat-deny.ts';
import { heldByWhatRuns, readLiteral, type HeldReason, type LiteralContext } from '../src/chat-literal.ts';
import { autoEditJudgement, confinedRefusal, fullAccessJudgement, judge, searchRefusal, type ChatScope, type Platform } from '../src/chat-permission-model.ts';
import { realPathOf } from '../src/chat-real-path.ts';

const fence = readFence(join(import.meta.dirname, '..'));
/** The fence of a real turn in Ask first, Auto-edit and Full access alike: none of the deny list (`modeFence`). */
const turnFence = modeFence(fence, 'full_access');
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

/**
 * A scope whose data directory is the default one under a made-up home folder, nothing on disk, under the fence a real
 * turn of Ask first, Auto-edit and Full access runs under; `planned` gives it Plan only's, the deny list whole.
 */
function homeScope(platform: Platform, home: string, planned = false): ChatScope {
    const paths = platform === 'win32' ? win32 : posix;
    const data = paths.join(home, '.jason');
    const chat = paths.join(data, 'desktop', 'chat');
    const used = planned ? fence : turnFence;
    return { platform, cwd: chat, chat, added: [], data, home, protectedFiles: [], denied: used.commands, deniedOperations: used.operations, real: () => null };
}

const scope = homeScope('linux', '/home/someone');
/** The same, under Plan only's fence: the deny list whole. */
const planned = homeScope('linux', '/home/someone', true);
const runs = { outcome: 'runs' };

/** The reasons an earlier Full access held a command for that were its own, not the literal reading's. */
const fullAccessOwn = new Set<HeldReason>(['guarded_name', 'search', 'local_jason', 'holds_data', 'reply', 'unknown_shell', 'tilde']);

/** Why the literal reading holds a command: its grammar's reason, or what the command runs; null when it holds nothing. */
function readingHolds(tool: string, command: string, at: ChatScope): HeldReason | null {
    const reading = readLiteral(tool, command, { platform: at.platform, home: at.home });
    return reading.held ? reading.reason : heldByWhatRuns(reading.commands);
}

/**
 * A command an earlier Full access held, by the reason it held it for: it runs in Full access now, the hook answering
 * nothing. Where the reason is the literal reading's own, the reading still gives it, as Ask first's "always" reads it.
 */
function runsHeld(tool: string, command: string, reason: HeldReason, at: ChatScope = scope): void {
    assert.deepEqual(fullAccessJudgement(tool, { command }, at), runs, `Full access runs ${JSON.stringify(command)}`);
    if (!fullAccessOwn.has(reason) && at.shellHome === undefined) assert.equal(readingHolds(tool, command, at), reason, JSON.stringify(command));
}

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
        // An .exe, since a % in the arguments of a name PATHEXT may complete to a batch file is held.
        assert.deepEqual(wordsOf('PowerShell', `frobnicate.exe x${character}y`), [['frobnicate.exe', `x${character}y`]], character);
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

test('cmd, a .bat or a .cmd with arguments is held whole by the reading, in either shell, and runs in Full access', () => {
    for (const tool of ['Bash', 'PowerShell']) {
        for (const command of ['cmd /c echo a', 'cmd.exe /k frobnicate', 'go.bat a', 'tools/go.cmd a', 'echo a | cmd']) runsHeld(tool, command, 'command_prompt');
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'go.bat' }, scope), runs, 'a batch file with no arguments is a file the agent wrote');
});

// 1.3 (a): a person's verbs are Plan only's to refuse; Ask first and Auto-edit card them or let the host's allow rule run
// them, and in Full access the hook refuses only the three kinds, so each runs there in every form.

test('in Plan only a person\'s verbs are refused in every form, never a card; in a real Full access turn a plain one runs, and in Ask first the permission model cards each, while the host\'s allow rule runs those of the no-card set before the model is asked', () => {
    for (const command of ['jason approval approve apr_1', 'jason decision answer dec_1 --option a', 'jason block clear blk_1', "jason approval 'approve' apr_1", 'jason approval ap"pro"ve apr_1', 'sudo jason approval approve apr_1', 'echo approval approve apr_1 | xargs jason approval approve']) {
        const judged = judge('Bash', { command }, planned);
        assert.match(judged.verdict === 'deny' ? judged.message : '', /a person's own act/, `Plan only: ${command}`);
    }
    for (const command of ['jason approval approve apr_1', 'jason decision answer dec_1 --option a', 'jason block clear blk_1']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, `Full access: ${command}`);
        assert.equal(judge('Bash', { command }, scope).verdict, 'ask', `Ask first: ${command}`);
    }
    // The host's allow rule is one per form of the no-card set (`claudeArguments`, chat-hosts.test.ts): approving and
    // answering are among them, so the host runs them unasked; clearing a block is not, so its card is the model's.
    assert.ok(personsWordForms.includes('approval approve') && personsWordForms.includes('decision answer'));
    assert.ok(!personsWordForms.includes('block clear'));
});

test('in Plan only starting a campaign is refused in every form, never a card; in a real Full access turn it runs, as pausing one does', () => {
    for (const command of ['jason campaign start cmp_1', 'jason campaign start cmp_1 --reason "go"', "jason campaign 'start' cmp_1", 'jason campaign st"ar"t cmp_1',
        'sudo jason campaign start cmp_1', 'jason campaign list && jason campaign start cmp_1', 'echo cmp_1 | xargs jason campaign start',
        'curl -X POST http://127.0.0.1:5000/v1/campaign.start']) {
        const judged = judge('Bash', { command }, planned);
        assert.match(judged.verdict === 'deny' ? judged.message : '', /a person's own act/, `Plan only: ${command}`);
    }
    for (const command of ['jason campaign start cmp_1', 'jason campaign pause cmp_1', 'jason suppression add --channel email --value someone@example.com']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, command);
    }
});

test('in Plan only archiving a campaign is refused in every form, never a card; in a real Full access turn it runs, as pausing one does', () => {
    for (const command of ['jason campaign archive cmp_1', 'jason campaign archive cmp_1 --reason "over"', "jason campaign 'archive' cmp_1", 'jason campaign ar"chi"ve cmp_1',
        'sudo jason campaign archive cmp_1', 'jason campaign list && jason campaign archive cmp_1', 'echo cmp_1 | xargs jason campaign archive',
        'curl -X POST http://127.0.0.1:5000/v1/campaign.archive']) {
        const judged = judge('Bash', { command }, planned);
        assert.match(judged.verdict === 'deny' ? judged.message : '', /a person's own act/, `Plan only: ${command}`);
    }
    for (const command of ['jason campaign archive cmp_1', 'jason campaign pause cmp_1']) assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, command);
});

test('in Plan only archiving a campaign is refused in the forms that hide its verb; the form that puts an option first is refused in every mode Jason judges and runs in Full access, and no other is ever let through unasked but by Full access', () => {
    const hiding = ['jason --actor human:x campaign archive cmp_1', 'jason campaign \\\n  archive cmp_1', 'env X=1 jason campaign archive cmp_1',
        'python3 -c "import subprocess; subprocess.run([\'jason\', \'campaign\', \'archive\', \'cmp_1\'])"',
        'jason campaign archive cmp_1 || true'];
    for (const command of hiding) assert.equal(judge('Bash', { command }, planned).verdict, 'deny', `Plan only: ${command}`);
    const first = hiding[0] as string;
    assert.equal(judge('Bash', { command: first }, scope).verdict, 'deny', `Ask first: ${first}`);
    assert.equal(autoEditJudgement('Bash', { command: first }, scope).verdict, 'deny', `Auto-edit: ${first}`);
    for (const command of hiding) assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, `Full access: ${command}`);
    for (const command of hiding.slice(1)) {
        assert.equal(judge('Bash', { command }, scope).verdict, 'ask', `Ask first: ${command}`);
        assert.equal(autoEditJudgement('Bash', { command }, scope).verdict, 'ask', `Auto-edit: ${command}`);
    }
});

test('in Plan only starting a campaign is refused in the forms that hide its verb; the form that puts an option first is refused in every mode Jason judges and runs in Full access, and no other is ever let through unasked but by Full access', () => {
    const hiding = ['jason --actor human:x campaign start cmp_1', 'jason campaign \\\n  start cmp_1', 'env X=1 jason campaign start cmp_1',
        'python3 -c "import subprocess; subprocess.run([\'jason\', \'campaign\', \'start\', \'cmp_1\'])"',
        'jason campaign start cmp_1 || true'];
    for (const command of hiding) assert.equal(judge('Bash', { command }, planned).verdict, 'deny', `Plan only: ${command}`);
    const first = hiding[0] as string;
    assert.equal(judge('Bash', { command: first }, scope).verdict, 'deny', `Ask first: ${first}`);
    assert.equal(autoEditJudgement('Bash', { command: first }, scope).verdict, 'deny', `Auto-edit: ${first}`);
    for (const command of hiding) assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, `Full access: ${command}`);
    for (const command of hiding.slice(1)) {
        assert.equal(judge('Bash', { command }, scope).verdict, 'ask', `Ask first: ${command}`);
        assert.equal(autoEditJudgement('Bash', { command }, scope).verdict, 'ask', `Auto-edit: ${command}`);
    }
});

test('a URL word is read with its percent-escapes decoded: a file URL into the data directory and an escaped variable of Jason\'s are refused however they are escaped', () => {
    for (const command of ['curl file:///home/someone/.jason/run/runtime.json', 'curl file:///home/someone/%2Ejason/run/runtime.json', 'curl file:///home/someone/.jason%2Frun%2Fruntime.json']) {
        assert.equal(fullAccessJudgement('Bash', { command }, scope).outcome, 'refused', command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'curl http://127.0.0.1:4011/v1/JASON%5FCHAT%5FTURN' }, scope), { outcome: 'refused', message: confinedRefusal });
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'curl -X POST http://127.0.0.1:4011/v1/approval%2Eapprove' }, scope), runs, 'an operation of the deny list runs in Full access');
    const plan = fullAccessJudgement('Bash', { command: 'curl -X POST http://127.0.0.1:4011/v1/approval%2Eapprove' }, planned);
    assert.equal(plan.outcome, 'runs', 'Full access\'s judgement reads the three kinds alone, whatever fence the scope carries');
});

test('the data directory is refused by its resolved path, the home folder\'s ~ resolved', () => {
    for (const command of ['cat ~/.jason/run/runtime.json', 'cat /home/someone/.jason/run/runtime.json', 'cat ../../run/runtime.json', 'frobnicate > ~/.jason/x', 'frobnicate --out=/home/someone/.jason/x']) {
        assert.equal(fullAccessJudgement('Bash', { command }, scope).outcome, 'refused', command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'cat notes.md' }, scope), runs, 'a file in the chat folder');
});

test('a resolved command that clears the environment around a jason is refused by the sentence for Jason\'s own variables', () => {
    const judged = fullAccessJudgement('Bash', { command: 'env -i jason campaign list' }, scope);
    assert.equal(judged.outcome, 'refused');
    assert.match(judged.outcome === 'refused' ? judged.message : '', /JASON_CONFINED/);
});

// 1.3 (b): held by the reading, every word literal. Each such command runs in Full access now (`runsHeld`); the reading's
// reason stays what Ask first's card and its "always" read.

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
        runsHeld('Bash', command, 'inline_code');
    }
    for (const command of ['pwsh -Command Get-Date', 'pwsh -c Get-Date', 'pwsh -EncodedCommand ZQBjAGgAbwA=', 'powershell Get-Date']) {
        runsHeld('PowerShell', command, 'inline_code');
    }
});

test('a shell or an interpreter given a file runs: it is a program the agent wrote', () => {
    for (const command of ['sh go.sh', 'python3 x.py', 'node build.js', 'awk -f prog.awk notes.md']) assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, command);
    assert.ok(!readLiteral('PowerShell', 'pwsh -File go.ps1', linux).held, 'the PowerShell grammar reads this');
});

test('a runner that starts a later or another command is held', () => {
    for (const command of ['xargs frobnicate', 'env frobnicate', 'sudo frobnicate', 'nohup frobnicate', 'timeout 5 frobnicate', 'eval frobnicate', 'source go.sh', '. go.sh', 'exec frobnicate', 'ssh host frobnicate', "find . -name x -exec frobnicate '{}' ';'", 'git -c alias.x=status x', 'git config core.hooksPath hooks']) {
        runsHeld('Bash', command, 'runner');
    }
    for (const command of ['Start-Process frobnicate', 'Invoke-Expression frobnicate', 'iex frobnicate', 'Invoke-Command frobnicate']) {
        runsHeld('PowerShell', command, 'runner');
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'git status' }, scope), runs);
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'find . -name notes.md' }, scope), runs);
});

test('a builtin that changes how later words are read is held', () => {
    for (const command of ['alias frobnicate=ls', 'set -f', 'shopt -s extglob', 'enable -n echo', 'export X=1', 'unset X', 'trap frobnicate EXIT']) {
        runsHeld('Bash', command, 'builtin');
    }
    for (const command of ['Set-Alias frobnicate ls', 'New-Alias frobnicate ls', 'Set-Item function:frobnicate x']) {
        runsHeld('PowerShell', command, 'builtin');
    }
});

test('a command part holding mcp followed by add, remove or login is held, whatever its program', () => {
    for (const command of ['frobnicate mcp add tools', 'frobnicate mcp remove tools', 'frobnicate mcp login tools']) {
        runsHeld('Bash', command, 'servers');
    }
    runsHeld('Bash', 'echo a && frobnicate --scope user MCP Add tools', 'servers');
    runsHeld('PowerShell', 'frobnicate mcp add tools', 'servers');
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'frobnicate mcp list' }, scope), runs);
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'frobnicate add mcp' }, scope), runs, 'the verb before the word is not the server command');
});

test('a guarded name in a path, at any depth, is held', () => {
    for (const command of ['cat notes/.git/config', 'echo a >> repo/.git/hooks/pre-commit', 'cat a/b/AGENTS.md', 'ls work/.jason']) {
        runsHeld('Bash', command, 'guarded_name');
    }
});

test('a search variable named, a jason of the agent\'s own, and a folder that holds the data directory are held', () => {
    runsHeld('Bash', 'echo PATH', 'search');
    assert.equal(fullAccessJudgement('Bash', { command: 'PATH=/tmp frobnicate' }, scope).outcome, 'refused', 'a change of the search is refused as in Ask first');
    runsHeld('Bash', './jason campaign list', 'local_jason');
    for (const command of ['ls /home/someone', 'ls ~']) runsHeld('Bash', command, 'holds_data');
    // A walk of such a folder reaches the data without naming it: with no read blocked where the hook rides, the hook
    // refuses it as the data's kind.
    assert.deepEqual(judge('Bash', { command: 'du -s /' }, scope), { verdict: 'ask', title: 'Run a command', detail: 'du -s /', remember: null }, 'below the floor, a card each time');
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'du -s /' }, scope), { outcome: 'refused', message: searchRefusal });
});

test('a relative word after a directory change to a relative target is held; after a literal absolute one it is read from there', () => {
    for (const command of ['cd sub && cat notes.md', 'cd sub/.. && ls', 'pushd sub; cat notes.md', 'cd - && ls', 'popd && ls']) {
        runsHeld('Bash', command, 'directory_change');
    }
    runsHeld('PowerShell', 'Set-Location sub; cat notes.md', 'directory_change');
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
    const linked: ChatScope = { platform, cwd: chat, chat, added: [], data, home: join(root, 'home'), protectedFiles: [], denied: turnFence.commands, deniedOperations: turnFence.operations, real: realPathOf };
    const slashed = (path: string) => path.replace(/\\/g, '/');
    assert.equal(fullAccessJudgement('Bash', { command: `cat ${slashed(join(link, 'run', 'runtime.json'))}` }, linked).outcome, 'refused');
    assert.equal(fullAccessJudgement('Bash', { command: `cd ${slashed(link)} && cat run/runtime.json` }, linked).outcome, 'refused');
    assert.deepEqual(fullAccessJudgement('Bash', { command: `cat ${slashed(join(root, 'elsewhere.md'))}` }, linked), runs);
});

// Ruling 4: reply in Full access.

test('reply is refused in Ask first beyond its no-card forms; in Full access it runs, the Reply card\'s account verbs among it', () => {
    const asAskFirst = (tool: string, command: string) => {
        const judged = judge(tool, { command }, scope);
        assert.equal(judged.verdict, 'deny', `Ask first: ${command}`);
        return runs;
    };
    // The Reply card's account verbs among them: they stay the person's acts in both modes.
    for (const command of [
        'reply sequence start seq_1',
        'reply api DELETE /v3/sequences/1',
        'reply account delete acc_1 --yes',
        'npx reply-cli sequence start seq_1',
        'reply account connect --channel email --provider gmail --allow-shared-page --json',
        'reply account reconnect 34 --channel linkedin --json',
        'reply account pause 34 --channel linkedin --json',
        'reply account delete 34 --channel linkedin --yes --force --json',
    ]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), asAskFirst('Bash', command), command);
    }
    assert.deepEqual(fullAccessJudgement('PowerShell', { command: 'reply sequence start seq_1' }, scope), asAskFirst('PowerShell', 'reply sequence start seq_1'));
    runsHeld('Bash', 'reply auth status --json > out.json', 'reply');
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'reply auth status --json' }, scope), runs);
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'reply account list --channel email' }, scope), runs);
});

// What runs.

test('a plain literal command runs with no question', () => {
    for (const command of ['echo a', 'frobnicate --level=2 notes.md', 'git status', 'jason campaign list', 'git commit -m "a plain message"', "jason campaign create --file - <<'EOF'\n{\"name\":\"x\"}\nEOF"]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, command);
    }
});

// A tool of the person's own servers runs in Full access whatever its input, which is read for the three kinds alone.

test('a tool of an MCP server runs in Full access whatever its input, where nothing in it names the three kinds', () => {
    for (const input of [{}, { command: 'echo a' }, { file_path: 'notes.md' }]) {
        assert.deepEqual(fullAccessJudgement('mcp__frobnicate__run', input, scope), runs, JSON.stringify(input));
    }
});

test('a person\'s MCP tool whose input has a command is read as text for the three kinds, never as a shell command', () => {
    // Claude Code's and Codex's mcp command, any verb: Ask first cards it; Full access runs it.
    for (const command of ['claude mcp list', '/usr/local/bin/codex mcp get crm', '/opt/tools/claude-code --debug mcp list']) {
        runsHeld('Bash', command, 'servers');
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'grep -r mcp notes' }, scope), runs);
    assert.deepEqual(fullAccessJudgement('mcp__s__run', { command: 'ls' }, scope), runs);
    assert.equal(fullAccessJudgement('mcp__s__run', { command: 'cat /home/someone/.jason/run/runtime.json' }, scope).outcome, 'refused', 'its input is still read for a refusal');
    assert.deepEqual(fullAccessJudgement('mcp__s__run', { command: 'echo $JASON_CHAT_TURN' }, scope), { outcome: 'refused', message: confinedRefusal });
});

// The fix round, POSIX first: what the module called the program was not always the program that runs.

test('POSIX: a reserved word in first position is held, bash\'s and zsh\'s, since the program that runs is a later word', () => {
    const reserved = ['if', 'then', 'elif', 'else', 'fi', 'do', 'done', 'case', 'esac', 'while', 'until', 'for', 'select', 'function', 'coproc', 'time', 'in'];
    const zsh = ['-', 'noglob', 'nocorrect', 'repeat', 'foreach'];
    for (const word of [...reserved, ...zsh]) {
        assert.equal(reasonOf('Bash', `${word} frobnicate a`), 'syntax', word);
        runsHeld('Bash', `echo a; ${word} frobnicate a`, 'syntax');
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'if frobnicate a; then python3 -c x; fi' }, scope), runs);
    for (const command of ['builtin echo a', 'command echo a']) runsHeld('Bash', command, 'runner');
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
        runsHeld('Bash', command, 'inline_code');
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'perl -Mstrict x.pl' }, scope), runs, 'a module named, no code');
});

test('a builtin that evaluates text or runs a callback is held, and an array subscript handed to one that evaluates it', () => {
    for (const command of ["let 'a[1]'", 'mapfile -C frobnicate a', 'readarray a', 'compgen -C frobnicate', 'complete -C frobnicate x']) {
        runsHeld('Bash', command, 'runner');
    }
    for (const command of ["printf -v 'a[1]' x", "read 'a[1]'", "test -v 'a[1]'", "getopts ab 'a[1]'", "wait -p 'a[1]'", "printf '$x'", "printf '`x`'"]) {
        runsHeld('Bash', command, 'expansion');
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: "printf '%s' a" }, scope), runs);
});

test('a package runner given shell code, git\'s execute options, and the runners that start a command they are given are held', () => {
    for (const command of ["npx -c 'frobnicate a'", "npx --call='frobnicate a'", "npm exec -c 'frobnicate a'", "npm x --call='frobnicate a'", "pnpm exec -c 'frobnicate a'"]) {
        runsHeld('Bash', command, 'inline_code');
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
        runsHeld('Bash', command, 'runner');
    }
    for (const command of ['vim -c q notes.md', 'vim +q notes.md', 'gdb -ex run frobnicate']) runsHeld('Bash', command, 'inline_code');
    for (const command of ['git push -u origin main', 'git grep -n x', 'git clone repo', 'tar -xf a.tar', 'sort notes.md']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, command);
    }
});

test('a server verb that starts with add, and the plugin verbs, are held whatever the program', () => {
    for (const command of ["frobnicate mcp add-json tools '{}'", 'frobnicate mcp add-from-claude-desktop', 'frobnicate plugin install tools', 'frobnicate plugin marketplace add tools', 'frobnicate plugins install tools', 'frobnicate plugin enable tools']) {
        runsHeld('Bash', command, 'servers');
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
        runsHeld('Bash', command, 'directory_change');
    }
    for (const command of ['cd /home/someone/work && cat notes.md', 'cd /home/someone/work && echo a | cat notes.md', 'cd /home/someone/work && echo a && cat notes.md']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, command);
    }
});

test('~ resolves to the home the host\'s shell carries, and is held when that differs from the profile', () => {
    const elsewhere: ChatScope = { ...scope, shellHome: '/home/other' };
    for (const command of ['ls ~/notes', 'cat ~', 'frobnicate > ~/out.txt']) runsHeld('Bash', command, 'tilde', elsewhere);
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'ls /home/someone/notes' }, elsewhere), runs);
    const same: ChatScope = { ...scope, shellHome: '/home/someone/' };
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'ls ~/notes' }, same), runs);
    const windows = homeScope('win32', 'C:\\Users\\someone');
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'ls ~/notes' }, { ...windows, shellHome: '/c/Users/someone' }), runs, 'Git Bash spells the same folder its own way');
    runsHeld('Bash', 'ls ~/notes', 'tilde', { ...windows, shellHome: 'D:\\home' });
});

test('on Windows a Bash word Git Bash would rewrite for a Windows program is held; a drive\'s own path is not', () => {
    const windows = homeScope('win32', 'C:\\Users\\someone');
    for (const command of ['cat /tmp/x', 'frobnicate --out=/tmp/x', 'frobnicate /a:/b', '/usr/bin/frobnicate a', 'frobnicate > /tmp/x']) {
        runsHeld('Bash', command, 'character', windows);
    }
    for (const command of ['cat /c/Work/notes.md', 'cat /d', 'frobnicate a 2>/dev/null', 'frobnicate --level=2']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, windows), runs, command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'cat /tmp/x' }, scope), runs, 'not on Linux');
});

test('anything that goes wrong while a command is judged in Full access refuses it, never runs it', () => {
    const failing: ChatScope = { ...scope, real: () => { throw new Error('no reading'); } };
    assert.equal(fullAccessJudgement('Bash', { command: 'cat notes.md' }, failing).outcome, 'refused');
});

// The PowerShell tool in Full access: its commands run as the Bash tool's do, read for the three kinds.

test('the PowerShell tool runs its commands in Full access, a hold of the reading included; a command naming the three kinds is refused', () => {
    for (const command of ['echo a', 'Get-ChildItem notes', 'frobnicate --level=2 notes.md', 'git status']) {
        runsHeld('PowerShell', command, 'unknown_shell');
    }
    runsHeld('PowerShell', 'Invoke-Expression frobnicate', 'runner');
    assert.equal(fullAccessJudgement('PowerShell', { command: 'Get-Content /home/someone/.jason/run/runtime.json' }, scope).outcome, 'refused');
    assert.deepEqual(fullAccessJudgement('PowerShell', { command: 'echo $env:JASON_CHAT_TURN' }, scope), { outcome: 'refused', message: confinedRefusal });
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'echo a' }, scope), runs, 'the Bash tool\'s literal commands still run');
});

// The PowerShell grammar: what it holds, as Ask first's "always" reads it.

test('PowerShell: an empty quoted word, and a quoted word that holds whitespace and a backslash, are held', () => {
    for (const command of ["git '' status", 'echo ""', "frobnicate 'a \\' b 'c -x d'", "frobnicate 'a\\b c'"]) {
        assert.equal(reasonOf('PowerShell', command), 'quote', command);
    }
    assert.deepEqual(wordsOf('PowerShell', "frobnicate 'a\\b' 'c d'"), [['frobnicate', 'a\\b', 'c d']]);
});

test('PowerShell: the built-in functions that change the folder leave it unknown', () => {
    for (const command of ['cd..; cat notes.md', 'cd.. && cat notes.md', 'cd\\; cat notes.md', 'D:; cat notes.md', 'd: && cat notes.md']) {
        runsHeld('PowerShell', command, 'directory_change');
    }
});

test('PowerShell: the commands that run code from literal words are held', () => {
    for (const command of ['Add-Type -TypeDefinition x', '% Frobnicate', 'ForEach-Object Frobnicate', 'foreach Frobnicate', 'Invoke-WmiMethod x', 'iwmi x', 'wmic x', 'New-Object -ComObject x']) {
        runsHeld('PowerShell', command, 'runner');
    }
    runsHeld('PowerShell', 'New-Object Frobnicate.Thing', 'unknown_shell');
});

test('PowerShell: a provider-qualified path is held, and a search variable is read case-folded on Windows', () => {
    for (const command of ['Get-Item Microsoft.PowerShell.Core\\Function::frobnicate', 'Get-Item Environment::path', 'Get-Item Alias::x']) {
        runsHeld('PowerShell', command, 'builtin');
    }
    const windows = homeScope('win32', 'C:\\Users\\someone');
    runsHeld('PowerShell', 'frobnicate path', 'search', windows);
    runsHeld('PowerShell', 'Set-Location -Path C:\\Work', 'unknown_shell', windows);
});

test('PowerShell: a word read as a number other than plain digits, and a dashed word holding a dot, are held', () => {
    for (const command of ['frobnicate 0x10', 'frobnicate 1kb', 'frobnicate 1e3', 'frobnicate 007', 'frobnicate 1.50', 'frobnicate 10L', 'frobnicate -a.b']) {
        assert.equal(reasonOf('PowerShell', command), 'character', command);
    }
    for (const command of ['frobnicate 10', 'frobnicate 0', 'frobnicate a.b', 'frobnicate -level 2']) assert.equal(reasonOf('PowerShell', command), null, command);
});

test('a program is named without the trailing dots Windows drops, and % in a PowerShell program\'s arguments is held unless it is an .exe', () => {
    runsHeld('Bash', 'bash.exe. -c x', 'inline_code');
    runsHeld('Bash', "'go.bat.' a", 'command_prompt');
    for (const command of ['frobnicate 50%', 'tools\\frobnicate a%b']) assert.equal(reasonOf('PowerShell', command), 'command_prompt', command);
    assert.equal(reasonOf('PowerShell', 'frobnicate.exe 50%'), null);
});

test('PowerShell: a redirection to a stream and the other quotes and dashes outside ASCII are held', () => {
    assert.equal(reasonOf('PowerShell', 'frobnicate >&1'), 'operator');
    for (const character of ['\u201C', '\u201D', '\u201E', '\u201A', '\u201B', '\u2014', '\u2015']) {
        assert.equal(reasonOf('PowerShell', `frobnicate ${character}a${character}`), 'non_ascii', character);
    }
});

// The delta read's classes: each holds more in the reading, and runs in Full access all the same.

test('delta F1: an option cluster is read with its digits, perl module text other than a name is code, fish -C, and a data: URL to node or deno', () => {
    for (const command of [
        "perl -0ne 'print' notes.md",
        "ruby -0ne 'p 1' notes.md",
        "perl -l0pe 'print' notes.md",
        "perl -MList::Util=sum,max '-Mfrobnicate(1)' x.pl",
        "perl '-Mstrict qw(x)' x.pl",
        'fish -C frobnicate',
        'fish --init-command=frobnicate',
        'node --import=data:text/javascript,1 x.js',
        'deno run data:text/javascript,1',
    ]) {
        runsHeld('Bash', command, 'inline_code');
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'perl -MList::Util=sum,max -Mstrict x.pl' }, scope), runs, 'module names and imports only');
});

test('delta F2: an editor or a debugger fed commands on its input is held, ed among the editors', () => {
    for (const command of ['echo a | vim -es notes.md', "ex -s notes.md <<'EOF'\nq\nEOF", 'echo run | gdb frobnicate', 'echo q | ed notes.md']) {
        runsHeld('Bash', command, 'inline_code');
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'ed -s notes.md < cmds.txt' }, scope), runs, 'commands from a file the agent wrote, as an interpreter given a file');
});

test('delta F3: a long execute option is matched by any prefix of three or more letters, and tar\'s dashless cluster is read', () => {
    for (const command of [
        'git fetch --upload-p=frobnicate',
        'git grep --open-files=frobnicate x',
        'tar --to-com=frobnicate -xf a.tar',
        'sort --compress-prog=frobnicate notes.md',
        'tar xIf frobnicate a.tar',
        'tar xfI a.tar frobnicate',
    ]) {
        runsHeld('Bash', command, 'runner');
    }
    for (const command of ["npx --cal='frobnicate a'", 'bash --comm=frobnicate', 'node --eva=1', 'gdb --eval-com=run frobnicate']) {
        runsHeld('Bash', command, 'inline_code');
    }
    for (const command of ['tar xf a.tar', 'tar -tf a.tar', 'git log --oneline', 'tar --to-stdout -xf a.tar']) assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, command);
});

test('delta F4: package runners by name and the execute options the lists missed are held; sed runs only a plain print, delete or quit', () => {
    for (const command of [
        'npx frobnicate',
        'bunx frobnicate',
        'pnpm dlx frobnicate',
        'yarn dlx frobnicate',
        'uvx frobnicate',
        'pipx run frobnicate',
        'make --eval=x:',
        'make -E x:',
        'scp -S frobnicate a host:b',
        'scp -o ProxyCommand=frobnicate a host:b',
        'zip -TT frobnicate a.zip notes.md',
        'zip --unzip-command=frobnicate a.zip notes.md',
        'hyperfine frobnicate',
        'valgrind frobnicate',
        'nodemon --exec frobnicate',
        'git send-email --smtp-server=frobnicate a.patch',
    ]) {
        runsHeld('Bash', command, 'runner');
    }
    for (const command of ["sed 's/a/b/' notes.md", "sed -n 's/a/b/e' notes.md", "sed -e '1p' -e 'w out.txt' notes.md", "sed --expression='1e frobnicate' notes.md", 'sed -f prog.sed notes.md', "sed -ne '1e' notes.md"]) {
        runsHeld('Bash', command, 'inline_code');
    }
    for (const command of ["sed -n '1,5p' notes.md", "sed '$d' notes.md", 'sed 10q notes.md', "sed -e '1!d' -e '2p' notes.md", "sed -n -e '$p' notes.md"]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, command);
    }
});

test('delta F5: zsh\'s builtins that change how later words are read are held', () => {
    for (const command of ['setopt rc_quotes', 'unsetopt glob', 'emulate sh', 'zmodload zsh/files', 'disable -r if']) {
        runsHeld('Bash', command, 'builtin');
    }
});

test('delta F6 and F7: on Windows a Bash word Git Bash may rewrite or a program may expand is held', () => {
    const windows = homeScope('win32', 'C:\\Users\\someone');
    for (const command of ['frobnicate lib:/y', 'frobnicate ./a:/b', 'frobnicate -X/y', 'frobnicate --out=./x', "frobnicate '*'", "frobnicate 'a?b'", "frobnicate '[ab]'"]) {
        runsHeld('Bash', command, 'character', windows);
    }
    for (const command of ['frobnicate C:/Work/x', 'frobnicate x:/y', 'frobnicate https://example.com/x', "frobnicate 'a * b'", 'frobnicate -X y']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, windows), runs, command);
    }
    for (const command of ['frobnicate lib:/y', 'frobnicate -X/y', "frobnicate '*'"]) assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, `not on Linux: ${command}`);
});
