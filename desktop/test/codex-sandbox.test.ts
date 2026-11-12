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
    sandboxPathSentence,
    sandboxSentence,
    type CheckRuns,
    type SandboxCheck,
} from '../src/codex-sandbox.ts';
import type { RunResult } from '../src/programs.ts';

const runFile = 'C:\\Users\\someone\\.jason\\run\\runtime.json';
const settingsFile = 'C:\\Users\\someone\\.jason\\desktop\\settings.json';
const markers = { ran: 'ran-0123456789abcdef01234567', reads: ['read-89abcdef0123456701234567', 'read-fedcba98765432100123abcd'] };
const systemRoot = 'C:\\Windows';
const made = sandboxCheck([runFile, settingsFile], systemRoot, markers);
const check = made as SandboxCheck;
const help = readFileSync(join(import.meta.dirname, 'fixtures', 'codex-sandbox-help-0.167.0-alpha.3.2.txt'), 'utf8');

const said = (stdout: string, exit = 0, stderr = ''): RunResult => ({ exit, stdout, stderr });
const all = said(`${markers.ran} \r\n${markers.reads[0]} \r\n${markers.reads[1]}\r\n`, 1);
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
    const answer = await runSandboxCheck(check, runs, new AbortController().signal);
    return { answer, ran, forms };
}

test('the check reads two files, the run file and one whose access is inherited, by one line cmd is given through a variable, so every run sees the same quoting', () => {
    assert.equal(typeof made, 'object');
    assert.equal(check.cmd, 'C:\\Windows\\System32\\cmd.exe');
    assert.deepEqual(check.inner, ['/d', '/c', `%${checkVariable}%`]);
    assert.equal(
        check.line,
        `echo ${markers.ran} & type "${runFile}" >nul 2>nul && echo ${markers.reads[0]} & type "${settingsFile}" >nul 2>nul && echo ${markers.reads[1]}`,
    );
    // Nothing of a file reaches a pipe: each type goes to nul, and only markers are echoed.
    assert.equal(check.line.match(/>nul 2>nul/g)?.length, 2);
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
    const each = [one.ran, ...one.reads, two.ran, ...two.reads];
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

test('it holds only on all markers outside, the ran marker alone inside, then all markers outside again', async () => {
    const held = await checkWith([all, all], [ranAlone]);
    assert.equal(held.answer, 'holds');
    assert.deepEqual(held.ran, ['cmd', 'codex', 'cmd']);
    assert.deepEqual(held.forms, [check.forms[0]]);
});

test('both markers inside are refused: the command read a file', async () => {
    assert.equal((await checkWith([all, all], [all])).answer, 'reads');
    // The inherited file alone readable is refused too.
    assert.equal((await checkWith([all, all], [said(`${markers.ran}\n${markers.reads[1]}\n`)])).answer, 'reads');
    assert.equal((await checkWith([all, all], [said(`${markers.ran}\n${markers.reads[0]}\n`)])).answer, 'reads');
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
    assert.equal((await checkWith([all, timedOut], [ranAlone])).answer, 'unproven');
    assert.deepEqual((await checkWith([timedOut], [])).ran, ['cmd'], 'nothing is run in the sandbox when the control did not answer');
});

test('the worded form is tried only when the bare one gave no ran marker, and holds the same way', async () => {
    const older = await checkWith([all, all], [nothing, ranAlone]);
    assert.equal(older.answer, 'holds');
    assert.deepEqual(older.forms, check.forms);
    assert.equal((await checkWith([all, all], [nothing, all])).answer, 'reads');
    const junk = await checkWith([all, all], [said(`${markers.ran}\nsomething else\n`)]);
    assert.equal(junk.answer, 'unproven');
    assert.deepEqual(junk.forms, [check.forms[0]], 'a ran marker with more is no reason to try the other form');
});

test('every combination but all, the ran marker alone, all is not proven', async () => {
    const shapes: [string, RunResult][] = [
        ['all', all],
        ['ran alone', ranAlone],
        ['nothing', nothing],
        ['one read', said(`${markers.ran}\n${markers.reads[0]}\n`)],
        ['timed out', timedOut],
        ['out of order', said(`${markers.reads[0]}\n${markers.ran}\n${markers.reads[1]}\n`)],
    ];
    for (const [beforeName, before] of shapes) {
        for (const [insideName, inside] of shapes) {
            for (const [afterName, after] of shapes) {
                const { answer } = await checkWith([before, after], [inside, inside]);
                const name = `${beforeName} / ${insideName} / ${afterName}`;
                if (beforeName === 'all' && insideName === 'ran alone' && afterName === 'all') assert.equal(answer, 'holds', name);
                else assert.notEqual(answer, 'holds', name);
            }
        }
    }
});

test('a read marker only on standard error is not taken as a read, and so not kept as one', async () => {
    assert.equal((await checkWith([all, all], [said(`${markers.ran}\n`, 1, `${markers.reads[0]}\n`)])).answer, 'unproven');
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
    assert.equal(checkingSandbox, 'Checking Codex\'s sandbox…');
    assert.match(sandboxSentence, /elevated sandbox/);
    assert.ok(sandboxSentence.includes('`codex sandbox setup --elevated`'));
    assert.match(sandboxSentence, /Claude Code/);
    assert.equal(sandboxKeyedSentence, 'Codex on a key cannot be checked on Windows yet: sign in to Codex, or chat on Claude Code.');
    assert.ok(!/unelevated/.test(sandboxSentence + sandboxKeyedSentence));
});
