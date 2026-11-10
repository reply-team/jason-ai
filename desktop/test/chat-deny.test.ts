import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { denyListFile, denyRules, parseDenyList } from '../src/chat-deny.ts';

const desktop = join(import.meta.dirname, '..');
const list = parseDenyList(readFileSync(join(desktop, denyListFile), 'utf8'));
const entries = list.commands;

test('the deny list is one file of jason commands, one per line, words only, each named once', () => {
    assert.ok(entries.length > 0);
    for (const entry of entries) assert.match(entry, /^[a-z][a-z-]*( [a-z][a-z-]*)*$/, entry);
    assert.equal(new Set(entries).size, entries.length);
    assert.deepEqual(parseDenyList('# a comment\n\n  approval approve  \r\nui\n').commands, ['approval approve', 'ui']);
});

test('a line that is not a command of words is refused, not skipped', () => {
    assert.throws(() => parseDenyList('approval approve\njason ui; rm\n'), /line 2/);
    assert.throws(() => parseDenyList('Approval approve\n'), /line 1/);
    assert.throws(() => parseDenyList('ui\nui\n'), /twice/);
    assert.throws(() => parseDenyList('approval.approve\napproval.approve\n'), /twice/);
    assert.throws(() => parseDenyList('approval.Approve\n'), /line 1/);
});

test('the host is denied every entry of the file in its own rule syntax, and every jason command that starts with an option', () => {
    const rules = denyRules(entries);
    for (const entry of entries) assert.ok(rules.includes(`Bash(jason ${entry}:*)`), entry);
    assert.ok(rules.includes('Bash(jason -*)'));
    assert.equal(rules.length, entries.length + 1);
});

test('the file refuses the installation verbs export and import, and the four the chat refuses beyond the CLI\'s table', () => {
    for (const verb of ['export', 'import', 'suppression remove', 'route set', 'route unset', 'role set-profile']) assert.ok(entries.includes(verb), verb);
});

test('the file\'s second section names the operations the denied verbs call, each once, system.shutdown among them', () => {
    assert.deepEqual(parseDenyList('ui\n# operations\nsystem.shutdown\njournal.stream\n'), { commands: ['ui'], operations: ['system.shutdown', 'journal.stream'] });
    for (const operation of ['approval.approve', 'approval.reject', 'approval.set_standing', 'approval.unset_standing', 'decision.answer', 'preference.set',
        'preference.unset', 'block.clear', 'workitem.resume', 'report.submit', 'subscription.rewind', 'system.shutdown', 'system.drain', 'system.resume',
        'plugin.reload', 'journal.stream', 'suppression.remove', 'route.set', 'route.unset', 'role.set_profile']) {
        assert.ok(list.operations.includes(operation), operation);
    }
    assert.ok(!list.operations.some((operation) => operation.startsWith('runtime.')));
});
