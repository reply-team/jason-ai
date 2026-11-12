// The Windows check of Codex's sandbox: what it runs, and what its answer is taken to say.

import assert from 'node:assert/strict';
import { test } from 'node:test';
import { codexTurnSettings, codexWindowsSandbox } from '../src/chat-hosts.ts';
import { checkingSandbox, sandboxAnswer, sandboxCheck, sandboxLimitMs, sandboxSentence } from '../src/codex-sandbox.ts';

const runFile = 'C:\\Users\\someone\\.jason\\run\\runtime.json';
const markers = { ran: 'ran-0123456789abcdef01234567', read: 'read-89abcdef0123456701234567' };
const systemRoot = 'C:\\Windows';
const check = sandboxCheck(runFile, systemRoot, markers);

test('the check runs a command in the sandbox under exactly the turn\'s settings, which prints a marker and reads the run file by name only', () => {
    assert.ok(check !== null);
    const end = check.args.indexOf('--');
    assert.equal(check.args[0], 'sandbox');
    const settings = check.args.slice(1, end).flatMap((arg, i, all) => (all[i - 1] === '-c' ? [arg] : []));
    assert.deepEqual(settings, [...codexTurnSettings('win32'), 'sandbox_mode="read-only"']);
    assert.ok(settings.includes(codexWindowsSandbox));
    assert.deepEqual(check.args.slice(end + 1), ['C:\\Windows\\System32\\cmd.exe', '/d', '/c', 'echo', markers.ran, '&', 'type', runFile, '>nul', '2>nul', '&&', 'echo', markers.read]);
    assert.ok(!check.args.some((arg) => arg.includes('unelevated')));
});

test('each check has markers of its own, never the same twice', () => {
    const one = sandboxCheck(runFile, systemRoot);
    const two = sandboxCheck(runFile, systemRoot);
    assert.ok(one !== null && two !== null);
    assert.notEqual(one.ran, two.ran);
    assert.notEqual(one.read, two.read);
    assert.notEqual(one.ran, one.read);
});

test('a run file whose path cmd would read as more than a name is not checked', () => {
    for (const path of ['C:\\Users\\a&b\\.jason\\run\\runtime.json', 'C:\\Users\\%USERNAME%\\run\\runtime.json', 'C:\\x"y\\runtime.json', 'C:\\a|b\\runtime.json', 'C:\\a^b\\runtime.json', 'C:\\a!b\\runtime.json', 'C:\\a<b\\runtime.json', 'C:\\a\nb\\runtime.json']) {
        assert.equal(sandboxCheck(path, systemRoot, markers), null, path);
    }
    assert.ok(sandboxCheck('C:\\Users\\Some User (work)\\.jason\\run\\runtime.json', systemRoot, markers) !== null);
});

test('cmd is started by its full path under the system root, never found by its name in the chat folder', () => {
    for (const root of ['', 'Windows', 'C:\\Win&dows']) assert.equal(sandboxCheck(runFile, root, markers), null, root);
    assert.equal(sandboxCheck(runFile, 'D:\\WINNT\\', markers)?.args.at(-13), 'D:\\WINNT\\System32\\cmd.exe');
});

test('the ran marker alone holds', () => {
    assert.equal(sandboxAnswer({ exit: 1, stdout: `${markers.ran} \r\n`, stderr: '' }, check!), 'holds');
    assert.equal(sandboxAnswer({ exit: 0, stdout: `${markers.ran}\n`, stderr: 'a note of Codex\'s own\n' }, check!), 'holds');
});

test('both markers are refused: the command read the run file', () => {
    assert.equal(sandboxAnswer({ exit: 0, stdout: `${markers.ran} \r\n${markers.read}\r\n`, stderr: '' }, check!), 'reads');
    assert.equal(sandboxAnswer({ exit: 0, stdout: `${markers.ran}\n`, stderr: `${markers.read}\n` }, check!), 'reads');
});

test('neither marker is refused: nothing shows the command ran', () => {
    assert.equal(sandboxAnswer({ exit: 0, stdout: '', stderr: '' }, check!), 'unproven');
    assert.equal(sandboxAnswer({ exit: 1, stdout: 'Access is denied.\r\n', stderr: '' }, check!), 'unproven');
});

test('a check stopped at its time limit, or that did not start, is refused', () => {
    assert.equal(sandboxAnswer({ exit: -1, stdout: '', stderr: `It was stopped at its time limit of ${sandboxLimitMs} ms.` }, check!), 'unproven');
    assert.equal(sandboxAnswer({ exit: -1, stdout: `${markers.ran}\n`, stderr: 'It was stopped before it answered.' }, check!), 'unproven');
});

test('any other shape is refused', () => {
    assert.equal(sandboxAnswer({ exit: 0, stdout: `${markers.ran}\nsomething else\n`, stderr: '' }, check!), 'unproven');
    assert.equal(sandboxAnswer({ exit: 0, stdout: `${markers.ran}${markers.ran}\n`, stderr: '' }, check!), 'unproven');
    assert.equal(sandboxAnswer({ exit: 0, stdout: `x ${markers.ran}\n`, stderr: '' }, check!), 'unproven');
});

test('the words the person reads: the check, and the refusal naming the setup and Claude Code', () => {
    assert.equal(checkingSandbox, 'Checking Codex\'s sandbox…');
    assert.match(sandboxSentence, /elevated sandbox/);
    assert.match(sandboxSentence, /administrator/);
    assert.ok(sandboxSentence.includes('`codex sandbox setup --elevated`'));
    assert.match(sandboxSentence, /Claude Code/);
    assert.ok(!/unelevated/.test(sandboxSentence));
});
