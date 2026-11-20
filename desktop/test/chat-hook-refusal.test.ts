// The turn's hook, in every mode but Plan only, and Full access as Claude Code's own bypass. The hook refuses three kinds
// and answers nothing else: the installation's settings files and run file, its data directory outside the turn's folder,
// and Jason's own variables. Each refusal says what the chat does not open and names the jason verb that does the act.
// In Full access everything else runs with no card; in Ask first and Auto-edit the cards and the host's rules decide it
// as before. Every command here is a neutral one or a made-up program; every path is made up.

import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join, posix, win32 } from 'node:path';
import { test } from 'node:test';
import ts from 'typescript';
import { readFence } from '../src/chat-deny.ts';
import { claudeArguments, hookedSettings } from '../src/chat-hosts.ts';
import type { ChatMode } from '../src/chat-modes.ts';
import { PermissionBroker, type BrokerEvent } from '../src/chat-permission-broker.ts';
import {
    attemptRefusal,
    confinedRefusal,
    dataRefusal,
    fullAccessJudgement,
    judge,
    lookupRefusal,
    personsRefusal,
    settingsRefusal,
    type ChatScope,
    type Platform,
} from '../src/chat-permission-model.ts';

const fence = readFence(join(import.meta.dirname, '..'));

/** A scope whose data directory is the default one under a made-up home folder, its turn in a conversation's own folder. */
function homeScope(platform: Platform, home: string, extra: Partial<ChatScope> = {}): ChatScope {
    const paths = platform === 'win32' ? win32 : posix;
    const data = paths.join(home, '.jason');
    const chat = paths.join(data, 'desktop', 'chats', 'c1');
    return {
        platform,
        cwd: chat,
        chat,
        added: [],
        data,
        home,
        protectedFiles: [paths.join(data, 'config', 'settings.json'), paths.join(data, 'run', 'runtime.json')],
        denied: fence.commands,
        deniedOperations: fence.operations,
        real: () => null,
        ...extra,
    };
}

const linux = homeScope('linux', '/home/someone');
const windows = homeScope('win32', 'C:\\Users\\someone');
const scopes: readonly ChatScope[] = [linux, homeScope('darwin', '/Users/someone'), windows];

const runs = { outcome: 'runs' } as const;
const refused = (message: string) => ({ outcome: 'refused', message }) as const;
const flag = (args: string[], name: string) => (args.includes(name) ? args[args.indexOf(name) + 1] : undefined);
const turnArgs = (mode: ChatMode) =>
    claudeArguments({ mcpConfig: 'turn.mcp.json', addDirs: [], denied: [], allowed: [], reads: [], mode, ...(mode === 'plan_only' ? {} : { settingsFile: 'turn.settings.json' }), dataRules: [], version: '2.1.329' });

test('every mode names its permission mode: Full access the host\'s bypass, Ask first and Auto-edit the default, Plan only plan', () => {
    const expected: [ChatMode, string][] = [['full_access', 'bypassPermissions'], ['ask_first', 'default'], ['auto_edit', 'default'], ['plan_only', 'plan']];
    for (const [mode, permission] of expected) {
        const args = turnArgs(mode);
        assert.equal(flag(args, '--permission-mode'), permission, mode);
        assert.equal(args.filter((arg) => arg === '--permission-mode').length, 1, mode);
    }
});

test('every mode but Plan only carries the turn\'s settings file, whose hook blocks the tool when it fails; Plan only carries its inline settings', () => {
    for (const mode of ['full_access', 'ask_first', 'auto_edit'] as const) assert.equal(flag(turnArgs(mode), '--settings'), 'turn.settings.json', mode);
    assert.ok(flag(turnArgs('plan_only'), '--settings')?.startsWith('{'));
    for (const mode of ['full_access', 'ask_first', 'auto_edit'] as const) {
        const settings = hookedSettings(mode, 'http://127.0.0.1:4100/hook', 'secret') as Record<string, unknown>;
        const hook = (settings['hooks'] as { PreToolUse: { matcher: string; hooks: Record<string, unknown>[] }[] }).PreToolUse[0];
        assert.equal(hook?.matcher, '*', mode);
        assert.deepEqual(hook?.hooks[0], { type: 'http', url: 'http://127.0.0.1:4100/hook', headers: { Authorization: 'Bearer secret' }, timeout: 30, onFailure: 'block' }, mode);
        assert.equal(settings['disableAllHooks'], false, mode);
        assert.deepEqual(settings['allowedHttpHookUrls'], ['http://127.0.0.1:4100/hook'], mode);
        const ask = (settings['permissions'] as { ask?: string[] }).ask;
        // Ask first and Auto-edit keep their own rule that every page asks; Full access opens a page with no card.
        assert.deepEqual(ask, mode === 'full_access' ? undefined : ['WebFetch(domain:*)'], mode);
    }
});

test('the PowerShell tool is the host\'s own in Full access and taken out of every other mode; Monitor is taken out of all', () => {
    const removed = (args: string[]) => args.slice(args.indexOf('--disallowedTools') + 1, args.indexOf('--setting-sources'));
    assert.ok(!removed(turnArgs('full_access')).includes('PowerShell'));
    assert.ok(removed(turnArgs('full_access')).includes('Monitor'));
    for (const mode of ['ask_first', 'auto_edit', 'plan_only'] as const) {
        assert.ok(removed(turnArgs(mode)).includes('PowerShell'), mode);
        assert.ok(removed(turnArgs(mode)).includes('Monitor'), mode);
    }
});

/** The strings the permission suites hold: commands, paths and words, as the TypeScript parser reads them. */
function suiteStrings(): string[] {
    const found = new Set<string>();
    const visit = (node: ts.Node): void => {
        if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) found.add(node.text);
        ts.forEachChild(node, visit);
    };
    for (const file of readdirSync(import.meta.dirname).filter((name) => /^chat-(literal|permission-model|full-access-hook|hook-refusal)\.test\.ts$/.test(name))) {
        visit(ts.createSourceFile(file, readFileSync(join(import.meta.dirname, file), 'utf8'), ts.ScriptTarget.Latest, true));
    }
    return [...found];
}

test('for every input the hook answers a refusal or nothing, in every mode it rides: never an allow and never a question', () => {
    const broker = new PermissionBroker({ emit: () => {} });
    const inputs = suiteStrings();
    let refusals = 0;
    for (const scope of scopes) {
        // The judgement is the same in every mode (the row below): every mode is asked on one platform, Full access on all.
        const modes = scope === linux ? (['full_access', 'ask_first', 'auto_edit'] as const) : (['full_access'] as const);
        for (const mode of modes) broker.openTurn('c1', `${scope.platform}-${mode}`, scope, mode);
        for (const text of inputs) {
            const shapes: [string, Record<string, unknown>][] = [['Bash', { command: text }], ['Read', { file_path: text }]];
            for (const [tool, input] of shapes) {
                for (const mode of modes) {
                    const answer = broker.hook(`${scope.platform}-${mode}`, tool, input, null);
                    assert.ok(answer.decision === 'deny' || answer.decision === 'none', `${mode} ${tool}: ${JSON.stringify(text)} was answered ${answer.decision}`);
                    if (answer.decision === 'deny') refusals++;
                }
            }
        }
    }
    assert.ok(refusals > 0, 'some of the suites\' inputs are refusals');
});

test('the hook answers the same in every mode it rides: its judgement is Full access\'s, three kinds and nothing else', () => {
    const broker = new PermissionBroker({ emit: () => {} });
    for (const mode of ['full_access', 'ask_first', 'auto_edit'] as const) broker.openTurn('c1', mode, linux, mode);
    for (const command of ['cat /home/someone/.jason/config/settings.json', 'frobnicate notes.md', 'echo $HOME', 'jason campaign list']) {
        const answers = (['full_access', 'ask_first', 'auto_edit'] as const).map((mode) => broker.hook(mode, 'Bash', { command }, null));
        assert.deepEqual(answers[1], answers[0], command);
        assert.deepEqual(answers[2], answers[0], command);
    }
});

test('the settings files and the run file are refused by the settings sentence, by a file tool, a command and a redirect', () => {
    for (const scope of scopes) {
        const paths = scope.platform === 'win32' ? win32 : posix;
        for (const file of scope.protectedFiles) {
            assert.deepEqual(fullAccessJudgement('Read', { file_path: file }, scope), refused(settingsRefusal), `${scope.platform} Read ${file}`);
            assert.deepEqual(fullAccessJudgement('Edit', { file_path: file, old_string: 'a', new_string: 'b' }, scope), refused(settingsRefusal), `${scope.platform} Edit ${file}`);
        }
        const settings = scope.protectedFiles[0] as string;
        const run = scope.protectedFiles[1] as string;
        const slashed = (path: string) => path.replace(/\\/g, '/');
        assert.deepEqual(fullAccessJudgement('Bash', { command: `cat ${slashed(run)}` }, scope), refused(settingsRefusal), `${scope.platform} cat`);
        assert.deepEqual(fullAccessJudgement('Bash', { command: `frobnicate --value-file - < ${slashed(settings)}` }, scope), refused(settingsRefusal), `${scope.platform} redirect`);
        assert.deepEqual(fullAccessJudgement('Read', { file_path: [scope.chat, '..', '..', '..', 'config', 'settings.json'].join(paths.sep) }, scope), refused(settingsRefusal), `${scope.platform} climbing`);
    }
});

test('the data directory outside the turn\'s folder is refused by the data sentence: a record, another conversation\'s folder, the shared chat folder', () => {
    for (const scope of scopes) {
        const paths = scope.platform === 'win32' ? win32 : posix;
        for (const place of [paths.join(scope.data, 'state', 'jason.db'), paths.join(scope.data, 'desktop', 'chats', 'c2', 'notes.md'), paths.join(scope.data, 'desktop', 'chat', 'notes.md'), paths.join(scope.data, 'logs', 'x.log')]) {
            assert.deepEqual(fullAccessJudgement('Read', { file_path: place }, scope), refused(dataRefusal), `${scope.platform} Read ${place}`);
            assert.deepEqual(fullAccessJudgement('Write', { file_path: place, content: 'x' }, scope), refused(dataRefusal), `${scope.platform} Write ${place}`);
            assert.deepEqual(fullAccessJudgement('Bash', { command: `cat ${place.replace(/\\/g, '/')}` }, scope), refused(dataRefusal), `${scope.platform} cat ${place}`);
        }
        assert.deepEqual(fullAccessJudgement('Read', { file_path: paths.join(scope.chat, 'notes.md') }, scope), runs, `${scope.platform}: the turn's own folder is the chat's`);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'echo "$JASON_DATA_DIR"' }, linux), refused(dataRefusal), 'the data directory\'s variable');
    assert.deepEqual(fullAccessJudgement('Write', { file_path: 'read.py', content: "open('/home/someone/.jason/config/settings.json').read()" }, linux), refused(dataRefusal), 'a file that names it');
    assert.deepEqual(fullAccessJudgement('mcp__files__read', { path: '/home/someone/.jason/run/runtime.json' }, linux), refused(dataRefusal), 'an MCP tool\'s input that names it');
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'cat ~/.jason/config/settings.json' }, linux).outcome, 'refused', 'by the home folder\'s ~');
    assert.deepEqual(fullAccessJudgement('Glob', { pattern: '../*.md' }, linux), refused(dataRefusal), 'a pattern that climbs from the turn\'s folder to the other conversations\' folders');
});

test('in a demo run the person\'s own installation is refused by its sentence', () => {
    const demo = homeScope('linux', '/home/someone', { personsOwn: ['/home/someone/.jason-own', '/home/someone/Jason'] });
    assert.deepEqual(fullAccessJudgement('Read', { file_path: '/home/someone/Jason/notes.md' }, demo), refused(personsRefusal));
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'cat /home/someone/.jason-own/config/settings.json' }, demo), refused(personsRefusal));
});

test('Jason\'s own variables are refused by their sentences: the confining variable, the attempt\'s, and where the shell finds a program', () => {
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'JASON_CONFINED= jason campaign list' }, linux), refused(confinedRefusal));
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'env -i jason campaign list' }, linux), refused(confinedRefusal));
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'JASON_ATTEMPT_ID=att_1 jason workitem list' }, linux), refused(attemptRefusal));
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'export PATH=/tmp/bin:$PATH && jason campaign list' }, linux), refused(lookupRefusal));
    assert.deepEqual(fullAccessJudgement('PowerShell', { command: '$env:PATH = "C:\\tmp;" + $env:PATH' }, windows), refused(lookupRefusal));
    assert.deepEqual(fullAccessJudgement('Write', { file_path: 'go.sh', content: '#!/bin/sh\nexport PATH=/tmp/bin\njason campaign list\n' }, linux), refused(lookupRefusal), 'a script that changes it');
});

test('each refusal of the three kinds says what the chat does not open and names the jason verb that does the act, never that it cannot', () => {
    for (const sentence of [settingsRefusal, dataRefusal, personsRefusal, confinedRefusal, attemptRefusal, lookupRefusal]) {
        assert.doesNotMatch(sentence, /\bcannot\b|can't|can not|unable|not able/i, sentence);
        assert.match(sentence, /does not open/, sentence);
        assert.match(sentence, /`jason [a-z]+(?: [a-z-]+)?`/, sentence);
        assert.match(sentence, /Nothing was (done|run)\.$/, sentence);
    }
});

/** One case of each kind Full access held before it was the host's bypass, as the suites held them. */
const onceHeld: [string, string, Record<string, unknown>, ChatScope][] = [
    ['expansion', 'Bash', { command: 'echo $HOME' }, linux],
    ['escape', 'Bash', { command: 'echo a\\ b' }, linux],
    ['glob', 'Bash', { command: 'echo *.md' }, linux],
    ['brace', 'Bash', { command: 'echo {a,b}' }, linux],
    ['group', 'Bash', { command: 'echo (a)' }, linux],
    ['history', 'Bash', { command: 'echo !1' }, linux],
    ['comment', 'Bash', { command: 'echo a #b' }, linux],
    ['tilde', 'Bash', { command: 'ls ~/notes' }, linux],
    ['non_ascii', 'Bash', { command: 'echo é' }, linux],
    ['control_character', 'Bash', { command: 'echo a\rb' }, linux],
    ['character', 'Bash', { command: 'echo a^b' }, linux],
    ['operator', 'Bash', { command: 'frobnicate <<< a' }, linux],
    ['background', 'Bash', { command: 'frobnicate &' }, linux],
    ['assignment', 'Bash', { command: 'X=1 frobnicate' }, linux],
    ['here_document', 'Bash', { command: 'sh <<EOF\nfrobnicate\nEOF' }, linux],
    ['process_substitution', 'Bash', { command: 'frobnicate <(echo a)' }, linux],
    ['quote', 'Bash', { command: "echo 'open" }, linux],
    ['array', 'PowerShell', { command: 'echo @a' }, windows],
    ['stop_parsing', 'PowerShell', { command: 'frobnicate --% a' }, windows],
    ['call_operator', 'PowerShell', { command: '& frobnicate' }, windows],
    ['syntax', 'Bash', { command: 'frobnicate >' }, linux],
    ['unknown_shell', 'PowerShell', { command: 'Get-ChildItem' }, windows],
    ['command_prompt', 'Bash', { command: 'cmd /c dir' }, windows],
    ['inline_code', 'Bash', { command: 'python3 -c "print(1)"' }, linux],
    ['runner', 'Bash', { command: 'xargs frobnicate' }, linux],
    ['builtin', 'Bash', { command: 'alias ll=frobnicate' }, linux],
    ['servers', 'Bash', { command: 'frobnicate mcp add tools' }, linux],
    ['directory_change', 'Bash', { command: 'cd sub && cat notes.md' }, linux],
    ['reply', 'Bash', { command: 'reply auth status --verbose' }, linux],
    ['guarded_name', 'Bash', { command: 'cat .git/config' }, linux],
    ['search', 'Bash', { command: 'echo PATH' }, linux],
    ['local_jason', 'Bash', { command: './jason campaign list' }, linux],
    ['holds_data', 'Bash', { command: 'ls /home/someone' }, linux],
    ['a tool of the person\'s own MCP server', 'mcp__notes__save', { text: 'hello' }, linux],
    ['a write of the AI\'s own settings in a folder below', 'Write', { file_path: 'project/.claude/settings.json', content: '{}' }, linux],
    ['a write of a repository\'s own files', 'Write', { file_path: 'repo/.git/config', content: '' }, linux],
    ['a write of a jason of the agent\'s own', 'Write', { file_path: 'jason', content: '' }, linux],
    ['a path that begins with ~', 'Read', { file_path: '~/notes.md' }, linux],
    ['a path Windows reads as another name', 'Read', { file_path: 'C:\\work\\notes.md.' }, windows],
    ['a tool Jason does not know', 'Frobnicate', { anything: 1 }, linux],
    ['a file tool given no file', 'Write', { content: 'x' }, linux],
];

test('every case Full access held before runs now: the judgement says runs and the hook answers nothing, PowerShell and a command that is not literal among them', () => {
    const broker = new PermissionBroker({ emit: () => {} });
    for (const [name, tool, input, scope] of onceHeld) {
        assert.deepEqual(fullAccessJudgement(tool, input, scope), runs, name);
        broker.openTurn('c1', name, scope, 'full_access');
        assert.deepEqual(broker.hook(name, tool, input, null), { decision: 'none' }, name);
    }
});

/** What Full access refused before that is none of the three kinds: it runs each now, and the hook answers nothing. */
const refusedBefore: [string, string, Record<string, unknown>, ChatScope][] = [
    ['a command longer than a card shows', 'Bash', { command: `echo ${'a'.repeat(5_000)} $HOME` }, linux],
    ['a person\'s verb, spelled to pass the host\'s rule', 'Bash', { command: 'jason approval appro\\ve apr_1' }, linux],
    ['a verb of the deny list', 'Bash', { command: 'jason campaign start cmp_1' }, linux],
    ['jason started with an option', 'Bash', { command: 'jason --help' }, linux],
    ['reply beyond its no-card forms', 'Bash', { command: 'reply sequence start seq_1' }, linux],
    ['a path on another computer', 'Read', { file_path: '\\\\server\\share\\notes.md' }, windows],
    ['a pattern that climbs out of the data directory', 'Glob', { pattern: '../../../../work/*.md' }, linux],
    ['a search of a folder that holds the data directory', 'Grep', { pattern: 'x', path: '/home/someone' }, linux],
    ['the chat\'s own instructions in its own folder', 'Write', { file_path: 'CLAUDE.md', content: 'x' }, linux],
    ['the chat\'s own skills in its own folder', 'Bash', { command: 'echo x > .claude/skills/x/SKILL.md' }, linux],
    ['an MCP tool\'s input longer than a card shows', 'mcp__notes__save', { text: 'a'.repeat(5_000) }, linux],
    ['a file that runs a verb of the deny list', 'Write', { file_path: 'go.sh', content: '#!/bin/sh\njason approval approve apr_1\n' }, linux],
    ['a file that starts a program through the system\'s opener', 'Write', { file_path: 'go.sh', content: '#!/bin/sh\nxdg-open notes.md\n' }, linux],
];

test('what Full access refused before that is none of the three kinds runs, with no card, and the hook answers nothing; Ask first still refuses it', () => {
    const broker = new PermissionBroker({ emit: () => {} });
    for (const [name, tool, input, scope] of refusedBefore) {
        if (tool !== 'Write') assert.equal(judge(tool, input, scope).verdict, 'deny', `Ask first refuses ${name}`);
        assert.deepEqual(fullAccessJudgement(tool, input, scope), runs, name);
        broker.openTurn('c1', name, scope, 'full_access');
        assert.deepEqual(broker.hook(name, tool, input, null), { decision: 'none' }, name);
    }
});

test('a turn in the shared chat folder refuses a write of the chat\'s own names there in every mode the hook rides; a conversation\'s own folder does not', () => {
    const paths = posix;
    const shared = homeScope('linux', '/home/someone', { chat: '/home/someone/.jason/desktop/chat', cwd: '/home/someone/.jason/desktop/chat', shared: true });
    const writes: [string, Record<string, unknown>][] = [
        ['Write', { file_path: '.claude/skills/x/SKILL.md', content: 'x' }],
        ['Write', { file_path: '.claude/settings.json', content: '{}' }],
        ['Edit', { file_path: 'CLAUDE.md', old_string: 'a', new_string: 'b' }],
        ['Bash', { command: 'echo x > .claude/skills/x/SKILL.md' }],
    ];
    for (const [tool, input] of writes) {
        assert.equal(fullAccessJudgement(tool, input, shared).outcome, 'refused', `${tool} ${JSON.stringify(input)}`);
        assert.deepEqual(fullAccessJudgement(tool, input, linux), runs, `own folder: ${tool} ${JSON.stringify(input)}`);
    }
    assert.equal(fullAccessJudgement('Write', { file_path: paths.join(shared.chat, '.claude', 'skills', 'x', 'SKILL.md'), content: 'x' }, shared).outcome, 'refused', 'by its whole path');
    assert.deepEqual(fullAccessJudgement('Read', { file_path: '.claude/skills/x/SKILL.md' }, shared), runs, 'a read of them runs');
});

test('Ask first\'s hook stands in front of the host\'s allow rules: a no-card verb fed the settings file or the run file is refused', () => {
    const broker = new PermissionBroker({ emit: () => {} });
    broker.openTurn('c1', 'ask', linux, 'ask_first');
    broker.openTurn('c1', 'edit', linux, 'auto_edit');
    for (const turn of ['ask', 'edit']) {
        for (const command of [
            'jason preference set tone_of_voice --value-file - < /home/someone/.jason/config/settings.json',
            'jason preference set tone_of_voice --value-file - < /home/someone/.jason/run/runtime.json',
            'jason rolenote set x --note-file - < /home/someone/.jason/config/settings.json',
        ]) {
            assert.deepEqual(broker.hook(turn, 'Bash', { command }, null), { decision: 'deny', reason: settingsRefusal }, `${turn}: ${command}`);
        }
        assert.deepEqual(broker.hook(turn, 'Bash', { command: 'jason preference set tone_of_voice --value-file - < tone.txt' }, null), { decision: 'none' }, `${turn}: a file of the chat's own`);
    }
});

test('what the host asks about in Full access is a card titled for Full access, never a run Ask first would allow nor a refusal of another kind', async () => {
    const events: BrokerEvent[] = [];
    const broker = new PermissionBroker({ emit: (_chat, event) => void events.push(event), newId: () => `r${events.length}` });
    broker.openTurn('c1', 't1', linux, 'full_access');
    // A person's own ask rule on an allowed verb, and the host's own question about a deletion.
    void broker.ask('t1', 'Bash', { command: 'jason campaign list' }, undefined);
    void broker.ask('t1', 'Bash', { command: 'rm -rf build' }, undefined);
    void broker.ask('t1', 'Read', { file_path: '\\\\server\\share\\notes.md' }, undefined);
    await new Promise((resolve) => setImmediate(resolve));
    const cards = events.filter((event) => event.kind === 'permission');
    assert.equal(cards.length, 3, 'each is a card');
    for (const card of cards) {
        assert.equal(card.kind === 'permission' ? card.title : '', 'Full access asks about this one');
        assert.deepEqual(card.kind === 'permission' ? card.why : undefined, { reason: 'host_asked' });
    }
    const settings = await broker.ask('t1', 'Read', { file_path: '/home/someone/.jason/config/settings.json' }, undefined);
    assert.deepEqual(settings, { behavior: 'deny', message: settingsRefusal }, 'the three kinds are refused there too');
    broker.endAll();
});
