import assert from 'node:assert/strict';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { allowListFile, allowRules, denyListFile, denyRules, fenceRefusal, helpNouns, helpRules, instructionsFile, modeFence, parsePlanList, parseAllowList, parseDenyList, personsWordForms, personsWordPreferenceKeys, personsWordVerbs, cardedVerbs, planListFile, readFence } from '../src/chat-deny.ts';
import type { ChatMode } from '../src/chat-modes.ts';

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

test('the file refuses starting a campaign as a command, as a host rule and as an operation, and the allow list does not carry it', () => {
    assert.ok(entries.includes('campaign start'));
    assert.ok(denyRules(entries).includes('Bash(jason campaign start:*)'));
    assert.ok(list.operations.includes('campaign.start'));
    const fence = readFence(desktop);
    assert.ok(!fence.allowed.includes('campaign start'));
    assert.ok(!allowRules(fence.allowed).includes('Bash(jason campaign start:*)'));
    for (const verb of ['campaign pause', 'suppression add', 'reply record', 'workitem cancel']) {
        assert.ok(fence.allowed.includes(verb), verb);
        assert.ok(!entries.includes(verb), verb);
    }
});

test('the file refuses archiving a campaign as a command, as a host rule and as an operation, and the allow list does not carry it', () => {
    assert.ok(entries.includes('campaign archive'));
    assert.ok(denyRules(entries).includes('Bash(jason campaign archive:*)'));
    assert.ok(list.operations.includes('campaign.archive'));
    const fence = readFence(desktop);
    assert.ok(!fence.allowed.includes('campaign archive'));
    assert.ok(!allowRules(fence.allowed).includes('Bash(jason campaign archive:*)'));
    assert.ok(fence.allowed.includes('campaign pause'));
    assert.ok(!entries.includes('campaign pause'));
});

test('the file\'s second section names the operations the denied verbs call, each once, system.shutdown among them', () => {
    assert.deepEqual(parseDenyList('ui\n# operations\nsystem.shutdown\njournal.stream\n'), { commands: ['ui'], operations: ['system.shutdown', 'journal.stream'] });
    for (const operation of ['approval.approve', 'approval.reject', 'approval.set_standing', 'approval.unset_standing', 'decision.answer', 'preference.set',
        'preference.unset', 'campaign.set_send_mode', 'block.clear', 'workitem.resume', 'workitem.review', 'report.submit', 'subscription.rewind',
        'system.shutdown', 'system.drain', 'system.resume', 'plugin.reload', 'journal.stream', 'suppression.remove', 'route.set', 'route.unset',
        'plugin.grant', 'plugin.revoke', 'role.set_profile']) {
        assert.ok(list.operations.includes(operation), operation);
    }
    assert.ok(!list.operations.some((operation) => operation.startsWith('runtime.')));
});

test('the file refuses how messages are sent: a campaign\'s send mode and both preference verbs, as commands, as host rules and as operations', () => {
    const rules = denyRules(entries);
    for (const verb of ['campaign set-send-mode', 'preference set', 'preference unset']) {
        assert.ok(entries.includes(verb), verb);
        assert.ok(rules.includes(`Bash(jason ${verb}:*)`), verb);
    }
    for (const operation of ['campaign.set_send_mode', 'preference.set', 'preference.unset']) assert.ok(list.operations.includes(operation), operation);
});

// runtime is one of the verbs that change the installation or follow it without end (the deny list's own heading): it
// starts, stops and registers the runtime the window is paired with, and a chat that stopped it would cut its own window
// off. jason status asks over the loopback and changes nothing, so it stays allowed.
test('runtime stays refused to the chat, and jason status stays allowed', () => {
    assert.ok(entries.includes('runtime'), 'runtime is no longer on the deny list');
    assert.ok(denyRules(entries).includes('Bash(jason runtime:*)'));
    const allowed = parseAllowList(readFileSync(join(desktop, allowListFile), 'utf8'));
    assert.ok(!allowed.some((verb) => verb === 'runtime' || verb.startsWith('runtime ')), 'a runtime verb is allowed');
    assert.ok(allowed.includes('status'), 'jason status changes nothing and stays allowed');
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

test('the nouns whose help runs are the first words of the verbs of two words or more on either list, each once, and each noun has one exact rule', () => {
    assert.deepEqual(helpNouns(['campaign list', 'status', 'campaign get', 'logs', 'workitem list'], ['contact get', 'campaign list']), ['campaign', 'workitem', 'contact']);
    assert.deepEqual(helpNouns(['status', 'logs'], []), []);
    assert.deepEqual(helpRules(['campaign', 'workitem']), ['Bash(jason campaign --help)', 'Bash(jason workitem --help)']);
    const fence = readFence(desktop);
    for (const noun of helpNouns(fence.allowed, fence.reads)) {
        assert.ok(!entries.includes(noun), `${noun} is taken away whole by the deny list`);
        assert.ok(fence.allowed.some((verb) => verb.startsWith(`${noun} `)), noun);
    }
});

test('rule 18 of the chat\'s instructions names exactly the nouns whose help runs, in their order, gives a verb\'s help and the root\'s, and a noun\'s alone where the mode only reads', () => {
    const fence = readFence(desktop);
    const rule = (fence.instructions ?? '').split(/\r?\n/).find((line) => line.startsWith('18. ')) ?? '';
    const listed = /with one of these nouns: ([^.]+)\./.exec(rule)?.[1] ?? '';
    assert.deepEqual([...listed.matchAll(/`([a-z-]+)`/g)].map((match) => match[1]), helpNouns(fence.allowed, fence.reads));
    assert.match(rule, /run `jason <noun> --help` with one of these nouns/);
    assert.match(rule, /A verb's own options are in `jason <noun> <verb> --help`, and `jason --help` lists the nouns;/);
    // Plan only keeps the nouns' exact help rules (`helpRules`) and refuses root help, and so does a turn no hook rides below
    // Claude Code's floor of Full access, so the rule gives both the noun's alone.
    assert.match(rule, /where this mode only reads and proposes, or where `jason --help` does not run, read only `jason <noun> --help`\./);
    assert.doesNotMatch(rule, /refused|exactly those three words/);
});

test('the person\'s word runs with no card outside Plan only in five verbs and two preference keys, each verb on the deny list and none on the allow list or the plan list', () => {
    const fence = readFence(desktop);
    assert.deepEqual([...personsWordVerbs].sort(), ['approval approve', 'approval reject', 'approval unset-standing', 'campaign start', 'decision answer']);
    assert.deepEqual([...personsWordPreferenceKeys].sort(), ['sender.identity', 'tone_of_voice']);
    assert.deepEqual([...personsWordForms].sort(), [...personsWordVerbs, 'preference set sender.identity', 'preference set tone_of_voice', 'preference unset sender.identity', 'preference unset tone_of_voice'].sort());
    for (const verb of [...personsWordVerbs, 'preference set', 'preference unset']) {
        assert.ok(entries.includes(verb), `${verb} stays on the deny list, which Plan only and the CLI's table still read`);
        assert.ok(!fence.allowed.includes(verb) && !fence.reads.includes(verb), verb);
    }
    for (const key of personsWordPreferenceKeys) assert.ok(existsSync(join(desktop, '..', 'docs', 'contracts', 'preferences', `${key}.json`)), `${key} is a preference the runtime knows`);
});

test('an act that lets sends go without the person\'s approval is never a form the host runs by itself outside Plan only: setting a standing approval, recording a reply\'s verdict, the send modes and any other preference key', () => {
    const fence = readFence(desktop);
    assert.deepEqual([...cardedVerbs], ['reply record']);
    assert.ok(fence.allowed.includes('reply record'), 'reply record is on the allow list the CLI writes, and is taken off it per turn');
    for (const mode of ['ask_first', 'auto_edit', 'full_access'] satisfies ChatMode[]) {
        const rules = allowRules(modeFence(fence, mode).allowed);
        for (const form of ['approval set-standing', 'reply record', 'preference set', 'preference unset', 'preference set send_mode.cold', 'preference set send_mode.warm', 'preference unset send_mode.cold', 'preference set agent.interactive_host']) {
            assert.ok(!rules.includes(`Bash(jason ${form}:*)`), `${mode}: ${form}`);
        }
        for (const form of personsWordForms) assert.ok(rules.includes(`Bash(jason ${form}:*)`), `${mode}: ${form}`);
    }
});

test('Plan only runs under the fence as it is read: every command and operation of the deny list refused, the plan list its only reads', () => {
    const fence = readFence(desktop);
    assert.deepEqual(modeFence(fence, 'plan_only'), fence);
});

test('outside Plan only no command or operation of the deny list is refused: the forms of a person\'s word join the allow list, reply record leaves it, and every other one is left to the mode', () => {
    const fence = readFence(desktop);
    for (const mode of ['ask_first', 'auto_edit', 'full_access'] satisfies ChatMode[]) {
        const turn = modeFence(fence, mode);
        assert.deepEqual(turn.commands, [], mode);
        assert.deepEqual(turn.operations, [], mode);
        assert.deepEqual(turn.allowed, [...fence.allowed.filter((verb) => verb !== 'reply record'), ...personsWordForms], mode);
        assert.deepEqual(turn.reads, fence.reads, mode);
        assert.equal(turn.instructions, fence.instructions, mode);
        for (const verb of ['uninstall', 'update', 'export', 'import', 'runtime', 'plugin grant', 'plugin revoke', 'route set', 'route unset', 'suppression remove', 'campaign archive', 'approval set-standing', 'reply record']) {
            assert.ok(!turn.allowed.includes(verb), `${verb} is no verb the host runs by itself in ${mode}`);
        }
        assert.deepEqual(denyRules(turn.commands), ['Bash(jason -*)'], `${mode}: a jason command that starts with an option is still refused by the host`);
    }
    assert.deepEqual(fence.commands, entries, 'the fence as read is left as it was');
});
