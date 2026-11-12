// The Windows check of Codex's sandbox: what it runs, in which order, and what the answers are taken to say.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { codexTurnSettings, codexWindowsSandbox } from '../src/chat-hosts.ts';
import {
    checkingSandbox,
    checkVariable,
    runSandboxCheck,
    sandboxCheck,
    sandboxKey,
    sandboxKeyedSentence,
    sandboxLimitMs,
    sandboxNoSettingsSentence,
    sandboxPathSentence,
    sandboxSentence,
    type CheckRuns,
    type SandboxCheck,
} from '../src/codex-sandbox.ts';
import type { RunResult } from '../src/programs.ts';

const runFile = 'C:\\Users\\someone\\.jason\\run\\runtime.json';
const settingsFile = 'C:\\Users\\someone\\.jason\\desktop\\settings.json';
const markers = { ran: 'ran-0123456789abcdef01234567', control: 'control-5a5a5a5a5a5a5a5a5a5a5a5a', reads: ['read-89abcdef0123456701234567', 'read-fedcba98765432100123abcd'] };
const hosts = 'C:\\Windows\\System32\\drivers\\etc\\hosts';
const systemRoot = 'C:\\Windows';
const made = sandboxCheck([runFile, settingsFile], systemRoot, markers);
const check = made as SandboxCheck;
const help = readFileSync(join(import.meta.dirname, 'fixtures', 'codex-sandbox-help-0.167.0-alpha.3.2.txt'), 'utf8');

const said = (stdout: string, exit = 0, stderr = ''): RunResult => ({ exit, stdout, stderr });
const all = said(`${markers.ran} \r\n${markers.control} \r\n${markers.reads[0]} \r\n${markers.reads[1]}\r\n`, 1);
/** What a sandbox that keeps both files prints: the ran marker and the control's, the one file every account may read. */
const kept = said(`${markers.ran} \r\n${markers.control} \r\n`, 1);
/** What Codex's elevated sandbox prints: the run file kept, the settings file, with the access it inherits, read. */
const settingsRead = said(`${markers.ran} \r\n${markers.control} \r\n${markers.reads[1]}\r\n`, 1);
/** What a sandbox that refuses `type` or its redirection prints, whatever it keeps: the ran marker alone. */
const ranAlone = said(`${markers.ran} \r\n`, 1);
const nothing = said('', 2, 'error: unexpected argument found');
const timedOut = said('', -1, `It was stopped at its time limit of ${sandboxLimitMs} ms.`);

/** Runs the check against answers given in order, cmd's and Codex's apart; records what was run. */
async function checkWith(cmd: RunResult[], codex: RunResult[]) {
    const ran: string[] = [];
    const forms: string[][] = [];
    const runs: CheckRuns = {
        cmd: async () => {
            ran.push('cmd');
            return cmd.shift() ?? said('');
        },
        codex: async (args) => {
            ran.push('codex');
            forms.push([...args]);
            return codex.shift() ?? said('');
        },
    };
    const { answer, opened } = await runSandboxCheck(check, runs, new AbortController().signal);
    return { answer, opened, ran, forms };
}

test('the check reads the system\'s hosts file as a control, then two files, the run file and one whose access is inherited, by one line cmd is given through a variable, so every run sees the same quoting', () => {
    assert.equal(typeof made, 'object');
    assert.equal(check.cmd, 'C:\\Windows\\System32\\cmd.exe');
    assert.deepEqual(check.inner, ['/d', '/c', `%${checkVariable}%`]);
    assert.equal(
        check.line,
        `echo ${markers.ran} & type "${hosts}" >nul 2>nul && echo ${markers.control} & type "${runFile}" >nul 2>nul && echo ${markers.reads[0]} & type "${settingsFile}" >nul 2>nul && echo ${markers.reads[1]}`,
    );
    assert.equal(check.controlFile, hosts, 'named from the system root');
    assert.equal((sandboxCheck([runFile, settingsFile], 'D:\\WINNT', markers) as SandboxCheck).controlFile, 'D:\\WINNT\\System32\\drivers\\etc\\hosts');
    // Nothing of a file reaches a pipe: each type goes to nul, and only markers are echoed.
    assert.equal(check.line.match(/>nul 2>nul/g)?.length, 3);
    assert.ok(!check.line.includes('|'));
});

test('the check is tried in the form without a platform word first, then with it, each under exactly the turn\'s settings and its read-only mode', () => {
    assert.equal(check.forms.length, 2);
    const [bare, worded] = check.forms;
    const expected = [...codexTurnSettings('win32'), 'sandbox_mode="read-only"'].flatMap((setting) => ['-c', setting]);
    assert.deepEqual(bare, ['sandbox', ...expected, '--', check.cmd, ...check.inner]);
    assert.deepEqual(worded, ['sandbox', 'windows', ...expected, '--', check.cmd, ...check.inner]);
    assert.ok(expected.includes(codexWindowsSandbox));
    assert.ok(expected.includes('features.apps=false'));
    assert.ok(!check.forms.flat().some((arg) => arg.includes('unelevated')));
});

test('the form without a platform word is the one the recorded help of 0.167.0-alpha.3.2 takes; the worded form is not in it', () => {
    assert.match(help, /^Usage: codex sandbox \[OPTIONS\] \[COMMAND\]\.\.\. \[COMMAND\]$/m);
    assert.match(help, /^ {2}-c, --config <key=value>$/m);
    const commands = help.slice(help.indexOf('Commands:'), help.indexOf('Arguments:'));
    assert.deepEqual([...commands.matchAll(/^ {2}(\S+)/gm)].map((match) => match[1]), ['uninstall', 'help']);
    // Options, each one the help names, then `--` and the command.
    const [bare] = check.forms;
    const end = bare!.indexOf('--');
    for (let i = 1; i < end; i += 2) assert.equal(bare![i], '-c', `${i}`);
    assert.equal(bare![end + 1], check.cmd);
    assert.ok(!commands.includes('windows'), 'the worded form is an older shape, on the real-host list');
});

test('fresh markers each check, none the same', () => {
    const one = sandboxCheck([runFile, settingsFile], systemRoot) as SandboxCheck;
    const two = sandboxCheck([runFile, settingsFile], systemRoot) as SandboxCheck;
    const each = [one.ran, one.control, ...one.reads, two.ran, two.control, ...two.reads];
    assert.equal(new Set(each).size, each.length);
});

test('a path cmd would read as more than a name, an argument separator among them, is not checked, and says so', () => {
    for (const path of ['C:\\a,b\\run\\runtime.json', 'C:\\a;b\\run\\runtime.json', 'C:\\a=b\\run\\runtime.json', 'C:\\a&b\\x', 'C:\\%USERNAME%\\x', 'C:\\x"y\\x', 'C:\\a|b\\x', 'C:\\a^b\\x', 'C:\\a!b\\x', 'C:\\a<b\\x', 'C:\\a\nb\\x', '']) {
        assert.equal(sandboxCheck([path, settingsFile], systemRoot, markers), 'path', JSON.stringify(path));
        assert.equal(sandboxCheck([runFile, path], systemRoot, markers), 'path', JSON.stringify(path));
    }
    assert.equal(typeof sandboxCheck(['C:\\Users\\Some User (work)\\.jason\\run\\runtime.json', settingsFile], systemRoot, markers), 'object');
    assert.match(sandboxPathSentence, /Claude Code/);
});

test('cmd is started by its full path under the system root, never found by its name in the chat folder', () => {
    for (const root of ['', 'Windows', 'C:\\Win&dows', 'C:\\Win,dows']) assert.equal(sandboxCheck([runFile, settingsFile], root, markers), 'root', root);
    assert.equal((sandboxCheck([runFile, settingsFile], 'D:\\WINNT', markers) as SandboxCheck).cmd, 'D:\\WINNT\\System32\\cmd.exe');
});

test('it holds only on all markers outside, the ran and control markers alone inside, then all markers outside again', async () => {
    const held = await checkWith([all, all], [kept]);
    assert.equal(held.answer, 'holds');
    assert.deepEqual(held.opened, [false, false], 'neither file opened inside');
    assert.deepEqual(held.ran, ['cmd', 'codex', 'cmd']);
    assert.deepEqual(held.forms, [check.forms[0]]);
});

test('the run file\'s read marker inside is refused: the command read the run token', async () => {
    const every = await checkWith([all, all], [all]);
    assert.equal(every.answer, 'reads');
    assert.deepEqual(every.opened, [true, true]);
    const runOnly = await checkWith([all, all], [said(`${markers.ran}\n${markers.control}\n${markers.reads[0]}\n`)]);
    assert.equal(runOnly.answer, 'reads');
    assert.deepEqual(runOnly.opened, [true, false]);
    assert.equal((await checkWith([all, all], [said(`${markers.ran}\n${markers.reads[0]}\n`)])).answer, 'reads');
});

test('the run file kept and the settings file read inside holds, and says the settings file was read: the verdict rests on the run file', async () => {
    const elevated = await checkWith([all, all], [settingsRead]);
    assert.equal(elevated.answer, 'holds');
    assert.deepEqual(elevated.opened, [false, true], 'the settings file\'s read is a fact the check reports');
    assert.deepEqual(elevated.ran, ['cmd', 'codex', 'cmd']);
    assert.deepEqual(elevated.forms, [check.forms[0]]);
    assert.equal((await checkWith([all, all], [nothing, settingsRead])).answer, 'holds', 'in the worded form too');
});

test('the settings file\'s marker without the control\'s, before it, twice, or not on a line of its own proves nothing', async () => {
    for (const stdout of [
        `${markers.ran}\n${markers.reads[1]}\n`,
        `${markers.ran}\n${markers.reads[1]}\n${markers.control}\n`,
        `${markers.ran}\n${markers.control}\n${markers.reads[1]}\n${markers.reads[1]}\n`,
        `${markers.ran}\n${markers.control}\nx ${markers.reads[1]}\n`,
    ]) {
        const found = await checkWith([all, all], [said(stdout, 1)]);
        assert.equal(found.answer, 'unproven', JSON.stringify(stdout));
        assert.equal(found.opened, null, JSON.stringify(stdout));
    }
    const alsoOnError = said(`${markers.ran}\n${markers.control}\n${markers.reads[1]}\n`, 1, `${markers.reads[1]}\n`);
    assert.equal((await checkWith([all, all], [alsoOnError])).answer, 'unproven', 'and on standard error as well');
});

test('neither marker inside, in either form, is refused', async () => {
    const neither = await checkWith([all, all], [nothing, nothing]);
    assert.equal(neither.answer, 'unproven');
    assert.deepEqual(neither.forms, check.forms, 'both forms tried');
    assert.deepEqual(neither.ran, ['cmd', 'codex', 'codex']);
});

test('a time limit is refused, outside or inside', async () => {
    assert.equal((await checkWith([timedOut], [])).answer, 'unproven');
    assert.equal((await checkWith([all], [timedOut])).answer, 'unproven');
    assert.equal((await checkWith([all, timedOut], [kept])).answer, 'unproven');
    assert.deepEqual((await checkWith([timedOut], [])).ran, ['cmd'], 'nothing is run in the sandbox when the control did not answer');
});

test('the worded form is tried only when the bare one gave no ran marker, and holds the same way', async () => {
    const older = await checkWith([all, all], [nothing, kept]);
    assert.equal(older.answer, 'holds');
    assert.deepEqual(older.forms, check.forms);
    assert.equal((await checkWith([all, all], [nothing, all])).answer, 'reads');
    const junk = await checkWith([all, all], [said(`${markers.ran}\nsomething else\n`)]);
    assert.equal(junk.answer, 'unproven');
    assert.deepEqual(junk.forms, [check.forms[0]], 'a ran marker with more is no reason to try the other form');
});

test('the ran marker alone inside proves nothing: the sandbox may refuse type or nul itself, so no read marker could have shown', async () => {
    const refusing = await checkWith([all, all], [ranAlone]);
    assert.equal(refusing.answer, 'unproven');
    assert.deepEqual(refusing.forms, [check.forms[0]], 'it ran, so the other form is not tried');
});

test('the control file absent or unreadable outside proves nothing, and nothing runs inside', async () => {
    const noControl = said(`${markers.ran}\r\n${markers.reads[0]}\r\n${markers.reads[1]}\r\n`);
    const absent = await checkWith([noControl, noControl], [kept]);
    assert.equal(absent.answer, 'unproven');
    assert.deepEqual(absent.ran, ['cmd']);
    assert.equal((await checkWith([all, noControl], [kept])).answer, 'unproven');
});

test('every combination but all, the ran and control markers with or without the settings file\'s, all is not proven', async () => {
    const shapes: [string, RunResult][] = [
        ['all', all],
        ['kept', kept],
        ['ran alone', ranAlone],
        ['nothing', nothing],
        ['one read', said(`${markers.ran}\n${markers.reads[0]}\n`)],
        ['settings read', settingsRead],
        ['timed out', timedOut],
        ['out of order', said(`${markers.reads[0]}\n${markers.ran}\n${markers.reads[1]}\n`)],
    ];
    for (const [beforeName, before] of shapes) {
        for (const [insideName, inside] of shapes) {
            for (const [afterName, after] of shapes) {
                const { answer } = await checkWith([before, after], [inside, inside]);
                const name = `${beforeName} / ${insideName} / ${afterName}`;
                if (beforeName === 'all' && (insideName === 'kept' || insideName === 'settings read') && afterName === 'all') assert.equal(answer, 'holds', name);
                else assert.notEqual(answer, 'holds', name);
            }
        }
    }
});

test('a read marker only on standard error is not taken as a read, and so not kept as one', async () => {
    assert.equal((await checkWith([all, all], [said(`${markers.ran}\n${markers.control}\n`, 1, `${markers.reads[0]}\n`)])).answer, 'unproven');
    assert.equal((await checkWith([all, all], [said('', 2, `error: ${markers.ran} ${markers.reads[0]}`)])).answer, 'unproven');
});

test('the kept answer is told apart by the file Codex runs from, the script it is given, its version and its home', () => {
    const native = sandboxKey({ file: 'C:\\codex\\codex.exe', prefix: [] }, '0.167.0', 'C:\\Users\\someone\\.codex');
    const npm = sandboxKey({ file: 'C:\\node\\node.exe', prefix: ['C:\\npm\\a\\codex.js'] }, '0.167.0', '');
    const other = sandboxKey({ file: 'C:\\node\\node.exe', prefix: ['C:\\npm\\b\\codex.js'] }, '0.167.0', '');
    assert.equal(new Set([native, npm, other, sandboxKey({ file: 'C:\\node\\node.exe', prefix: ['C:\\npm\\a\\codex.js'] }, '0.168.0', ''), sandboxKey({ file: 'C:\\node\\node.exe', prefix: ['C:\\npm\\a\\codex.js'] }, '0.167.0', 'C:\\keyed')]).size, 5);
    assert.equal(npm, sandboxKey({ file: 'C:\\node\\node.exe', prefix: ['C:\\npm\\a\\codex.js'] }, '0.167.0', ''));
});

test('the words the person reads: the check, the refusal naming the setup and Claude Code, and a keyed turn\'s own', () => {
    // Said where the person chose Codex, before the check: the administrator prompt, the way out, and what a command reaches.
    assert.equal(
        checkingSandbox,
        'Checking Codex\'s sandbox… Windows may ask once for an administrator; answering No means chatting on Claude Code instead. A Codex command can read files on this computer, Jason\'s own data and settings among them, but cannot change anything or act for you.',
    );
    assert.match(sandboxSentence, /elevated sandbox/);
    assert.ok(sandboxSentence.includes('`codex sandbox setup --elevated`'));
    assert.match(sandboxSentence, /Claude Code/);
    assert.equal(sandboxKeyedSentence, 'Codex on a key cannot be checked on Windows yet: sign in to Codex, or chat on Claude Code.');
    assert.ok(!/unelevated/.test(sandboxSentence + sandboxKeyedSentence));
    // The desktop writes its settings file at start, so a restart is the cure when it is missing.
    assert.match(sandboxNoSettingsSentence, /restart Jason/);
    assert.ok(!/change any setting/i.test(sandboxNoSettingsSentence));
});
