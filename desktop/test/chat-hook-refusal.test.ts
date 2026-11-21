// The turn's hook, in every mode but Plan only, and Full access as Claude Code's own bypass. The hook refuses three kinds
// and answers nothing else: the installation's settings files and run file, its data directory outside the turn's folder,
// and what Jason itself sets for the turn: its variables, with where the shell finds a program, and its permission tool.
// Each refusal says what the chat does not open and names the jason verb that does the act. In Full access everything
// else runs with no card; in Ask first and Auto-edit the cards and the host's rules decide it as before. Every command
// here is a neutral one or a made-up program; every path is made up.

import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join, posix, win32 } from 'node:path';
import { test } from 'node:test';
import ts from 'typescript';
import { modeFence, readFence } from '../src/chat-deny.ts';
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
    searchRefusal,
    settingsRefusal,
    unplacedRefusal,
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
        const permissions = settings['permissions'] as { ask?: string[]; blockReadsOutsideWorkingDirectories?: boolean } | undefined;
        // Ask first and Auto-edit keep their own rule that every page asks; Full access opens a page with no card. No mode
        // the hook rides blocks a read: the hook refuses the three kinds wherever a tool names them.
        assert.deepEqual(permissions?.ask, mode === 'full_access' ? undefined : ['WebFetch(domain:*)'], mode);
        assert.equal(permissions?.blockReadsOutsideWorkingDirectories, undefined, mode);
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

test('what Jason itself sets for the turn is refused by its sentences: its variables, the confining one and the attempt\'s, with where the shell finds a program, and its permission tool', () => {
    const tool = fullAccessJudgement('mcp__jason_desktop__decide', { tool_name: 'Bash', input: { command: 'git push' } }, linux);
    assert.equal(tool.outcome, 'refused', 'the chat\'s own permission tool');
    assert.match(tool.outcome === 'refused' ? tool.message : '', /only the person answers them/);
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

test('with no read blocked, a walk of a folder that holds the data directory is the data\'s kind: a search of it by the Grep tool and a recursive command are refused by the hook, and a plain listing of it runs', () => {
    for (const [tool, input] of [
        ['Grep', { pattern: 'x', path: '/home/someone' }],
        ['Grep', { pattern: 'x', path: '/' }],
        ['Bash', { command: 'grep -r x /home/someone' }],
        ['Bash', { command: 'find / -name runtime.json' }],
    ] as const) {
        assert.deepEqual(fullAccessJudgement(tool, input, linux), refused(searchRefusal), `${tool} ${JSON.stringify(input)}`);
    }
    for (const [tool, input] of [['LS', { path: '/home/someone' }], ['Bash', { command: 'ls -la /home/someone' }], ['Grep', { pattern: 'x', path: '/home/someone/Documents' }]] as const) {
        assert.deepEqual(fullAccessJudgement(tool, input, linux), runs, `${tool} ${JSON.stringify(input)}`);
    }
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

/** Whether one of the host's allow rules, `Bash(<words>:*)`, lets a command through: those words, whole, at its start. */
function allowedBy(args: string[], command: string): boolean {
    const rules = args.slice(args.indexOf('--allowedTools') + 1, args.indexOf('--disallowedTools'));
    return rules.some((rule) => {
        const words = /^Bash\((.*):\*\)$/.exec(rule)?.[1];
        return words !== undefined && (command === words || command.startsWith(`${words} `));
    });
}

test('below the floor of Full access no hook rides Ask first or Auto-edit, so the host is given no allow rule for a no-card form whose verb reads a request\'s file: each reaches the permission tool, which runs its plain form with no card as the host\'s rule would, refuses it fed from the settings file or the run file, and asks about anything else', () => {
    const turn = (mode: ChatMode, hooked: boolean) => {
        const fenced = modeFence(fence, mode);
        return claudeArguments({ mcpConfig: 'turn.mcp.json', addDirs: [], denied: fenced.commands, allowed: fenced.allowed, reads: fenced.reads, mode, ...(hooked ? { settingsFile: 'turn.settings.json' } : {}), dataRules: [], version: hooked ? '2.1.329' : '2.1.300' });
    };
    const reading = [
        'jason preference set tone_of_voice --value-file - < tone.txt',
        'jason preference set sender.identity --value cheeky --actor human:someone',
        'jason rolenote set cmp_1 researcher --note-file - < note.md',
        'jason campaign create --file - < campaign.json',
        'jason campaign start cmp_1 --actor human:someone',
        'jason workitem create cmp_1 --file - < item.json',
        'jason journal append cmp_1 --kind decision --file - < entry.json',
    ];
    const plain = ['jason campaign list', 'jason approval approve apr_1 --actor human:someone', 'jason decision answer dec_1 --option yes --actor human:someone', 'jason preference unset tone_of_voice --actor human:someone'];
    for (const mode of ['ask_first', 'auto_edit'] as const) {
        for (const command of reading) {
            assert.equal(allowedBy(turn(mode, false), command), false, `${mode} below the floor: ${command}`);
            assert.equal(allowedBy(turn(mode, true), command), true, `${mode} from the floor on, the hook in front of the rule: ${command}`);
        }
        for (const command of plain) assert.equal(allowedBy(turn(mode, false), command), true, `${mode} below the floor, a form that reads no file: ${command}`);
    }
    // What reaches the permission tool there: a plain form of the host's rules runs with no card, as the rule would run it,
    // its content from a file in the chat's folder or a here-document included.
    const bare: ChatScope = { ...linux, denied: [], deniedOperations: [], entry: () => null };
    const askFirst: ChatScope = { ...bare, allowed: modeFence(fence, 'ask_first').allowed };
    const document = 'jason workitem create cmp_1 --file - <<\'EOF\'\n{"title":"Call Ada"}\nEOF';
    for (const command of [...reading, ...plain, document, 'jason campaign list 2>&1', 'jason campaign list 2>/dev/null']) {
        assert.deepEqual(judge('Bash', { command }, askFirst), { verdict: 'allow' }, command);
    }
    const onWindows: ChatScope = { ...windows, denied: [], deniedOperations: [], entry: () => null, allowed: modeFence(fence, 'ask_first').allowed };
    assert.deepEqual(judge('Bash', { command: 'jason workitem create cmp_1 --file - < item.json' }, onWindows), { verdict: 'allow' });
    assert.deepEqual(judge('Bash', { command: 'jason workitem create cmp_1 --file - < C:/Users/someone/.jason/config/settings.json' }, onWindows), { verdict: 'deny', message: settingsRefusal });
    // Fed from the settings file or the run file, it is refused.
    for (const fed of ['/home/someone/.jason/config/settings.json', '/home/someone/.jason/run/runtime.json']) {
        for (const command of [`jason preference set tone_of_voice --value-file - < ${fed}`, `jason campaign start cmp_1 < ${fed}`, `jason workitem create cmp_1 --file - < ${fed}`]) {
            assert.deepEqual(judge('Bash', { command }, askFirst), { verdict: 'deny', message: settingsRefusal }, command);
        }
    }
    // Anything that cannot be read as that plain form is a card, as before: a pipe, a file outside the chat's folders, a
    // redirection that writes, a variable, an option before the verb, a verb or a key no rule of the host's names.
    for (const command of [
        'cat item.json | jason workitem create cmp_1 --file -',
        'jason workitem create cmp_1 --file - < /home/someone/notes/item.json',
        'jason workitem create cmp_1 --file - < item.json > made.json',
        'jason campaign start "$CAMPAIGN"',
        'jason campaign --actor human:someone start cmp_1',
        'jason reply record cnv_1 --nature person',
        'jason approval set-standing cmp_1 interested',
        'jason preference set send_mode.cold --value "{}"',
        'jason preference set tone_of_voicex --value cheeky',
        `${document}\nrm item.json`,
        'jason campaign start cmp_1; jason campaign archive cmp_2',
    ]) {
        assert.equal(judge('Bash', { command }, askFirst).verdict, 'ask', command);
    }
    assert.equal(judge('Bash', { command: 'jason workitem create cmp_1 --file - < D:/notes/item.json' }, onWindows).verdict, 'ask');
    // A file named jason in the folder the shell runs in keeps the card.
    assert.equal(judge('Bash', { command: 'jason campaign start cmp_1' }, { ...askFirst, entry: (path) => (posix.basename(path) === 'jason' ? 'present' : null) }).verdict, 'ask');
    // A scope that names no rule of the host's, as Plan only's, keeps every card.
    assert.equal(judge('Bash', { command: 'jason campaign start cmp_1' }, bare).verdict, 'ask');
});

test('a redirect spelled as Windows spells it is refused in Ask first, by the permission tool and by the hook: the drive\'s path and the profile\'s variable', () => {
    const askFirst: ChatScope = { ...windows, denied: [], deniedOperations: [] };
    const broker = new PermissionBroker({ emit: () => {} });
    broker.openTurn('c1', 'ask', askFirst, 'ask_first');
    for (const [command, sentence] of [
        ['jason preference set tone_of_voice --value-file - < C:\\Users\\someone\\.jason\\config\\settings.json', settingsRefusal],
        ['jason preference set tone_of_voice --value-file - < C:/Users/someone/.jason/run/runtime.json', settingsRefusal],
        ['jason preference set tone_of_voice --value-file - < %USERPROFILE%\\.jason\\config\\settings.json', settingsRefusal],
        ['jason rolenote set cmp_1 researcher --note-file - < "%USERPROFILE%/.jason/config/settings.json"', settingsRefusal],
    ] as const) {
        assert.deepEqual(judge('Bash', { command }, askFirst), { verdict: 'deny', message: sentence }, `the permission tool: ${command}`);
        assert.deepEqual(broker.hook('ask', 'Bash', { command }, null), { decision: 'deny', reason: sentence }, `the hook: ${command}`);
    }
});

test('on Windows Full access reads this computer\'s own drive shares and the long-path forms as the drive, by Ask first\'s reader, and refuses a path in a device\'s namespace and a share of this computer that names no drive by the sentence that says it could not place them, which Ask first refuses too', () => {
    const named = homeScope('win32', 'C:\\Users\\someone', { machine: ['DESKTOP-1'] });
    const below = 'Users\\someone\\.jason\\config\\settings.json';
    const spellings = [
        `\\\\localhost\\C$\\${below}`,
        `\\\\127.0.0.1\\c$\\${below}`,
        `\\\\::1\\C$\\${below}`,
        `\\\\DESKTOP-1\\C$\\${below}`,
        `\\\\?\\UNC\\localhost\\C$\\${below}`,
        `\\\\.\\UNC\\desktop-1\\C$\\${below}`,
        `\\\\?\\C:\\${below}`,
        `//localhost/c$/${below.replace(/\\/g, '/')}`,
    ];
    for (const path of spellings) {
        assert.deepEqual(fullAccessJudgement('Read', { file_path: path }, named), refused(settingsRefusal), `Read ${path}`);
        assert.deepEqual(fullAccessJudgement('Write', { file_path: path, content: 'x' }, named), refused(settingsRefusal), `Write ${path}`);
        assert.deepEqual(fullAccessJudgement('Bash', { command: `cat '${path}'` }, named), refused(settingsRefusal), `cat ${path}`);
        assert.deepEqual(fullAccessJudgement('PowerShell', { command: `Get-Content '${path}'` }, named), refused(settingsRefusal), `Get-Content ${path}`);
    }
    assert.deepEqual(
        fullAccessJudgement('Write', { file_path: 'read.py', content: "open(r'\\\\localhost\\C$\\Users\\someone\\.jason\\config\\settings.json').read()" }, named),
        refused(dataRefusal),
        'a script whose text names it by this computer\'s share',
    );
    for (const path of ['\\\\?\\Volume{0b3a1c2d-0000-0000-0000-100000000000}\\Users\\someone\\.jason\\config\\settings.json', '\\\\.\\GLOBALROOT\\Device\\HarddiskVolume3\\Users\\someone\\notes.md']) {
        for (const [tool, input] of [['Read', { file_path: path }], ['Write', { file_path: path, content: 'x' }], ['Bash', { command: `cat '${path}'` }], ['PowerShell', { command: `Get-Content '${path}'` }]] as const) {
            assert.deepEqual(fullAccessJudgement(tool, input, named), refused(unplacedRefusal), `${tool} ${path}`);
            if (tool !== 'PowerShell') assert.equal(judge(tool, input, named).verdict, 'deny', `Ask first: ${tool} ${path}`);
        }
    }
    assert.deepEqual(fullAccessJudgement('Read', { file_path: '\\\\localhost\\C$\\Users\\someone\\notes.md' }, named), runs, 'a file of no kind, by this computer\'s drive share');
    assert.deepEqual(fullAccessJudgement('Read', { file_path: '\\\\localhost\\Users\\someone\\notes.md' }, named), refused(unplacedRefusal), 'a share of this computer that names no drive, which is not read');
    assert.match(unplacedRefusal, /could not place where that path leads/);
    assert.deepEqual(fullAccessJudgement('Read', { file_path: `\\\\server\\C$\\${below}` }, named), runs, 'another computer\'s share');
});

test('in Full access a verb of the deny list, a jason that starts with an option and a word that begins with @ are refused where they name one of the three kinds, and run where they do not', () => {
    const settings = '/home/someone/.jason/config/settings.json';
    const record = '/home/someone/.jason/desktop/conversations/c2.json';
    const cases: [string, ReturnType<typeof refused>][] = [
        [`jason plugin grant reply --host api.example.com < ${settings}`, refused(settingsRefusal)],
        [`jason route set campaign.enroll --plugin reply > ${record}`, refused(dataRefusal)],
        ['JASON_CONFINED= jason plugin grant reply --host api.example.com', refused(confinedRefusal)],
        [`jason --actor human:someone preference set tone_of_voice --value-file - < ${settings}`, refused(settingsRefusal)],
        [`jason --human campaign list > ${record}`, refused(dataRefusal)],
        ['JASON_CHAT_TURN= jason --actor human:someone campaign list', refused(confinedRefusal)],
        [`jason @${settings}`, refused(settingsRefusal)],
        [`jason campaign update cmp_1 --name @${record}`, refused(dataRefusal)],
        ['jason @words.txt JASON_ATTEMPT_ID=att_1', refused(attemptRefusal)],
    ];
    for (const [command, answer] of cases) assert.deepEqual(fullAccessJudgement('Bash', { command }, linux), answer, command);
    for (const command of ['jason plugin grant reply --host api.example.com', 'jason --actor human:someone approval approve apr_1', 'jason @words.txt']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, linux), runs, command);
    }
});

test('the limit: Full access refuses a command that names the settings file, the data directory or what Jason sets for the turn, and one that reaches them without naming them runs: a glob, a variable, a change to a folder a variable names, another account\'s ~', () => {
    for (const command of ['cat ~/.jas*/config/settings.json', 'd=.jason; cat ~/$d/config/settings.json', 'cd "$HOME" && cat .jason/config/settings.json', 'cat ~someone/.jason/config/settings.json']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, linux), runs, command);
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
