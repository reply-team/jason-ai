import assert from 'node:assert/strict';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { allowListFile, allowRules, denyListFile, denyRules, fenceRefusal, instructionsFile, parsePlanList, parseAllowList, parseDenyList, planListFile, readFence } from '../src/chat-deny.ts';

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

test('the file refuses the installation verbs export and import, the six that take no default actor, and the two the chat refuses beyond the CLI\'s table', () => {
    for (const verb of ['export', 'import', 'suppression remove', 'route set', 'route unset', 'plugin grant', 'plugin revoke', 'role set-profile', 'role add', 'project link']) assert.ok(entries.includes(verb), verb);
    const operations = parseDenyList(readFileSync(join(import.meta.dirname, '..', denyListFile), 'utf8')).operations;
    assert.ok(operations.includes('role.add'));
    assert.ok(operations.includes('project.link'));
});

test('the file\'s second section names the operations the denied verbs call, each once, system.shutdown among them', () => {
    assert.deepEqual(parseDenyList('ui\n# operations\nsystem.shutdown\njournal.stream\n'), { commands: ['ui'], operations: ['system.shutdown', 'journal.stream'] });
    for (const operation of ['approval.approve', 'approval.reject', 'approval.set_standing', 'approval.unset_standing', 'decision.answer', 'preference.set',
        'preference.unset', 'block.clear', 'workitem.resume', 'report.submit', 'subscription.rewind', 'system.shutdown', 'system.drain', 'system.resume',
        'plugin.reload', 'journal.stream', 'suppression.remove', 'route.set', 'route.unset', 'plugin.grant', 'plugin.revoke', 'role.set_profile']) {
        assert.ok(list.operations.includes(operation), operation);
    }
    assert.ok(!list.operations.some((operation) => operation.startsWith('runtime.')));
});

// On Linux Claude Code's environment scrub runs a Bash command in a PID namespace of its own, where ps and kill cannot
// see the host's processes: jason runtime start would read a running runtime's process as gone and start another over
// it, which dies with the namespace; jason runtime stop would see it gone at once. jason status asks over the loopback
// and reads no process, so it stays allowed. Whatever a later change allows elsewhere, runtime stays refused there.
test('runtime stays refused to the chat, since on Linux its commands would misread the runtime from the scrub’s PID namespace', () => {
    assert.ok(entries.includes('runtime'), 'runtime is no longer on the deny list');
    assert.ok(denyRules(entries).includes('Bash(jason runtime:*)'));
    const allowed = parseAllowList(readFileSync(join(desktop, allowListFile), 'utf8'));
    assert.ok(!allowed.some((verb) => verb === 'runtime' || verb.startsWith('runtime ')), 'a runtime verb is allowed');
    assert.ok(allowed.includes('status'), 'jason status reads no process and stays allowed');
});

test('the allow list is one file of jason verbs, none of them covered by the deny list, and not doctor', () => {
    const allowed = parseAllowList(readFileSync(join(desktop, allowListFile), 'utf8'));
    assert.ok(allowed.length > 40);
    for (const verb of ['campaign list', 'approval list', 'decision get', 'status', 'logs']) assert.ok(allowed.includes(verb), verb);
    for (const verb of allowed) {
        assert.ok(!entries.some((entry) => verb === entry || verb.startsWith(`${entry} `)), `${verb} is denied`);
    }
    assert.ok(!allowed.includes('doctor'));
    assert.throws(() => parseAllowList('campaign list\ncampaign list\n'), /twice/);
    assert.throws(() => parseAllowList('system.shutdown\n'), /line 1/);
});

test('the host is allowed each verb of the allow list by a rule of its own, and nothing of jason beyond them', () => {
    assert.deepEqual(allowRules(['campaign list', 'status']), ['Bash(jason campaign list:*)', 'Bash(jason status:*)']);
});

test('the fence is read from the two files beside the desktop\'s package', () => {
    const fence = readFence(desktop);
    assert.deepEqual(fence.commands, entries);
    assert.deepEqual(fence.operations, list.operations);
    assert.deepEqual(fence.allowed, parseAllowList(readFileSync(join(desktop, allowListFile), 'utf8')));
    assert.equal(planListFile, 'chat-plan-list.txt');
    assert.deepEqual(fence.reads, parsePlanList(readFileSync(join(desktop, planListFile), 'utf8'), fence.allowed));
    assert.ok(fence.reads.includes('campaign list') && !fence.reads.includes('campaign create'));
    assert.equal(instructionsFile, 'chat-instructions.md');
    assert.equal(fence.instructions, readFileSync(join(desktop, instructionsFile), 'utf8'));
});

test('a folder without the instruction text gives a fence without it, and the lists still refuse when they are missing', (t) => {
    const folder = mkdtempSync(join(tmpdir(), 'jason-fence-'));
    t.after(() => rmSync(folder, { recursive: true, force: true }));
    assert.throws(() => readFence(folder));
    for (const file of [denyListFile, allowListFile, planListFile]) copyFileSync(join(desktop, file), join(folder, file));
    assert.equal(readFence(folder).instructions, null);
    mkdirSync(join(folder, instructionsFile));
    assert.throws(() => readFence(folder), 'an instruction text that is there and cannot be read refuses the fence');
});

test('a fence that cannot be read says which of its files failed: the instruction text, or the allow and deny lists', (t) => {
    const folder = mkdtempSync(join(tmpdir(), 'jason-fence-'));
    t.after(() => rmSync(folder, { recursive: true, force: true }));
    const refusalOf = () => {
        try {
            readFence(folder);
        } catch (error) {
            return fenceRefusal(error);
        }
        return 'read';
    };
    const lists = 'The chat’s allow and deny lists could not be read.';
    assert.equal(refusalOf(), lists, 'neither list is there');
    copyFileSync(join(desktop, denyListFile), join(folder, denyListFile));
    assert.equal(refusalOf(), lists, 'the allow list is not there');
    copyFileSync(join(desktop, allowListFile), join(folder, allowListFile));
    assert.equal(refusalOf(), lists, 'the plan list is not there');
    copyFileSync(join(desktop, planListFile), join(folder, planListFile));
    assert.equal(refusalOf(), 'read');
    mkdirSync(join(folder, instructionsFile));
    assert.equal(refusalOf(), 'The chat’s instruction text could not be read.');
    assert.equal(fenceRefusal(new Error('anything else')), lists);
});

test('the plan list names verbs of the allow list only, each once: a verb that is not on the allow list refuses the whole file', () => {
    const lines = (...each: string[]) => each.join('\n');
    assert.deepEqual(parsePlanList(lines('# reads', 'campaign list', '', 'status', ''), ['campaign list', 'campaign create', 'status']), ['campaign list', 'status']);
    assert.throws(() => parsePlanList(lines('campaign list', 'campaign create'), ['campaign list']), /not on the allow list/);
    assert.throws(() => parsePlanList(lines('status', 'status'), ['status']), /twice/);
    assert.throws(() => parsePlanList(lines('--yes'), ['status']));
});
