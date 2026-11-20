// The fence around the person's own tools: an MCP tool of a server other than the chat's own is a card of its own in Ask
// first and runs in Full access, whatever its arguments are named, each of its strings read as a command's text is, and
// never cut; the files and commands that add a server to an agent; what a turn's command line and settings carry in each
// mode, so that a turn that runs in Full access loads the person's own Claude Code configuration as in a terminal and
// every other turn loads none of it; and the chat's own permission tool, on a server named for each turn alone and
// refused to the agent by the name's fixed prefix.

import assert from 'node:assert/strict';
import { join, posix } from 'node:path';
import { test } from 'node:test';
import { readFence } from '../src/chat-deny.ts';
import { chatRules, claudeArguments, claudeTurnSettings, codexArguments, codexWindowsSandbox, hookedSettings, planTurnSettings } from '../src/chat-hosts.ts';
import { fullAccessFloor } from '../src/chat-modes.ts';
import { fullAccessJudgement, judge, personsToolTitle, serversTitle, type ChatScope } from '../src/chat-permission-model.ts';
import { claudeFloor, codexFloor, codexFloorSentence, compareVersions, noChromeFrom, printedCodexVersion } from '../src/host-version.ts';

const fence = readFence(join(import.meta.dirname, '..'));
const home = '/home/someone';
const data = posix.join(home, '.jason');
const chat = posix.join(data, 'desktop', 'chat');
const campaign = posix.join(home, 'Jason', 'acme', 'campaigns', 'spring');
const scope: ChatScope = {
    platform: 'linux',
    cwd: chat,
    chat,
    added: [campaign],
    data,
    home,
    protectedFiles: [posix.join(data, 'config', 'settings.json'), posix.join(data, 'run', 'runtime.json')],
    denied: fence.commands,
    deniedOperations: fence.operations,
    real: () => null,
};
const runs = { outcome: 'runs' };

test('an MCP tool of a server other than the chat\'s own is a card of its own, even when an argument is named command', () => {
    assert.deepEqual(judge('mcp__s__run', { command: 'ls' }, scope), { verdict: 'ask', title: personsToolTitle, detail: 's · run\n{"command":"ls"}', remember: null });
    assert.deepEqual(judge('mcp__acme-crm__find_contact', { query: 'dentists', limit: 5 }, scope), {
        verdict: 'ask',
        title: personsToolTitle,
        detail: 'acme-crm · find_contact\n{"query":"dentists","limit":5}',
        remember: null,
    });
    assert.equal(personsToolTitle, 'Use one of your own tools');
});

test('in Full access an MCP tool of the person\'s runs, the hook answering nothing: its arguments are no shell words', () => {
    assert.deepEqual(fullAccessJudgement('mcp__s__run', { command: 'ls' }, scope), runs);
    assert.deepEqual(fullAccessJudgement('mcp__s__run', { command: 'jason campaign list' }, scope), runs);
    assert.deepEqual(fullAccessJudgement('mcp__acme-crm__find_contact', { query: 'dentists' }, scope), runs);
    assert.deepEqual(fullAccessJudgement('mcp__claude-in-chrome__navigate', { url: 'https://example.com/' }, scope), runs);
});

test('every string of an MCP tool\'s input, at any depth, is held to the text checks: a refusal with no card in Ask first, and in Full access where it names the three kinds', () => {
    const kinds: Record<string, unknown>[] = [
        { path: posix.join(data, 'run', 'x') },
        { path: '~/.jason/desktop/provider-keys/openai' },
        { text: 'echo $JASON_CONFINED' },
        { text: 'unset jason_confined' },
        { nested: { list: ['fine', { deeper: `cat ${posix.join(data, 'run', 'runtime.json')}` }] } },
    ];
    const verbs: Record<string, unknown>[] = [
        { query: 'jason approval approve x' },
        { nested: { list: ['fine', { deeper: 'jason decision answer y --option a' }] } },
        { query: 'jason approv\\al approve x' },
        { call: 'system.shutdown' },
    ];
    for (const input of [...kinds, ...verbs]) assert.equal(judge('mcp__s__run', input, scope).verdict, 'deny', JSON.stringify(input));
    for (const input of kinds) assert.equal(fullAccessJudgement('mcp__s__run', input, scope).outcome, 'refused', JSON.stringify(input));
    for (const input of verbs) assert.deepEqual(fullAccessJudgement('mcp__s__run', input, scope), runs, JSON.stringify(input));
    assert.equal(judge('mcp__s__run', { query: 'jason approval list' }, scope).verdict, 'ask', 'a verb that is no person\'s act stays a card');
    assert.equal(judge('mcp__s__run', { path: posix.join(chat, 'notes.md') }, scope).verdict, 'ask', 'the chat folder is not the data');
});

test('an MCP tool\'s input that spreads one command over several strings is judged joined as well, in order', () => {
    const spread: Record<string, unknown>[] = [
        { argv: ['jason', 'approval', 'approve', 'x'] },
        { program: 'jason', args: ['approval', 'approve', 'x'] },
        { program: 'jason', args: ['decision', 'answer', 'y', '--option', 'a'] },
        { steps: [{ run: 'jason' }, { run: 'approval approve x' }] },
    ];
    for (const input of spread) {
        assert.equal(judge('mcp__s__run', input, scope).verdict, 'deny', JSON.stringify(input));
        assert.deepEqual(fullAccessJudgement('mcp__s__run', input, scope), runs, `Full access leaves a verb to the host's rules and the CLI: ${JSON.stringify(input)}`);
    }
    assert.equal(fullAccessJudgement('mcp__s__run', { argv: ['cat', posix.join(data, 'run'), 'runtime.json'] }, scope).outcome, 'refused', 'the data, joined in order');
    // Joined in order, so the words a person reads in that order are the ones judged.
    assert.equal(judge('mcp__s__run', { argv: ['jason', 'approval', 'list'] }, scope).verdict, 'ask');
    assert.equal(judge('mcp__s__run', { argv: ['approve', 'approval', 'jason'] }, scope).verdict, 'ask');
});

test('an MCP tool\'s input longer than the card shows whole is refused in Ask first, never cut; Full access runs it', () => {
    const long = { text: 'a'.repeat(5000) };
    const judged = judge('mcp__s__run', long, scope);
    assert.equal(judged.verdict, 'deny');
    assert.match(judged.verdict === 'deny' ? judged.message : '', /longer than the card can show whole/);
    assert.deepEqual(fullAccessJudgement('mcp__s__run', long, scope), runs);
    const fits = judge('mcp__s__run', { text: 'a'.repeat(3900) }, scope);
    assert.equal(fits.verdict, 'ask');
    assert.ok(fits.verdict === 'ask' && !fits.detail.endsWith('…'), 'a detail that fits is shown whole');
});

test('the chat\'s own permission tool stays refused to the agent', () => {
    assert.equal(judge('mcp__jason_desktop__approve', { allow: true }, scope).verdict, 'deny');
});

test('a tool of any server whose name begins with the fixed prefix is the chat\'s own, refused to the agent by the hook and the permission model: a turn\'s, and one of the old fixed name from the person\'s own scopes', () => {
    const own = /answers the chat's own permission requests/;
    for (const tool of ['mcp__jason_desktop__decide', 'mcp__jason_desktop__approve', 'mcp__jason_desktop_0123456789abcdef__decide', 'mcp__jason_desktop_fedcba9876543210__other']) {
        const judged = judge(tool, { tool_name: 'Bash', input: { command: 'ls' } }, scope);
        assert.match(judged.verdict === 'deny' ? judged.message : judged.verdict, own, tool);
        const hooked = fullAccessJudgement(tool, { allow: true }, scope);
        assert.match(hooked.outcome === 'refused' ? hooked.message : hooked.outcome, own, tool);
    }
    // A server whose name only starts the same way is another: the person's own, a card in Ask first, and it runs in Full access.
    assert.equal(judge('mcp__jason_desktopx__run', { text: 'hi' }, scope).verdict === 'ask' && (judge('mcp__jason_desktopx__run', { text: 'hi' }, scope) as { title: string }).title, personsToolTitle);
    assert.deepEqual(fullAccessJudgement('mcp__jason_desktopx__run', { text: 'hi' }, scope), runs);
});

test('.mcp.json is refused at the chat folder\'s top and a card elsewhere in Ask first; Full access runs it but in the shared chat folder', () => {
    const top = { file_path: posix.join(chat, '.mcp.json'), content: '{"mcpServers":{}}' };
    const judged = judge('Write', top, scope);
    assert.equal(judged.verdict, 'deny');
    assert.deepEqual(fullAccessJudgement('Write', top, scope), runs, 'a conversation\'s own folder is its own');
    assert.equal(fullAccessJudgement('Write', top, { ...scope, shared: true }).outcome, 'refused', 'the shared chat folder\'s top');
    assert.equal(judge('Bash', { command: 'echo {} > .mcp.json' }, scope).verdict, 'deny', 'a command too');
    for (const path of [posix.join(campaign, '.mcp.json'), posix.join(chat, 'sub', '.mcp.json'), posix.join(home, 'elsewhere', '.mcp.json')]) {
        const input = { file_path: path, content: '{}' };
        const card = judge('Write', input, scope);
        assert.equal(card.verdict, 'ask', path);
        assert.equal(card.verdict === 'ask' ? card.remember : 'x', null, path);
        assert.deepEqual(fullAccessJudgement('Write', input, scope), runs, path);
    }
    assert.ok(chatRules('linux', chat).includes(`Edit(/${posix.join(chat, '.mcp.json')})`));
});

test('.claude.json is a card each time wherever it lies, and runs in Full access', () => {
    for (const path of [posix.join(home, '.claude.json'), posix.join(chat, '.claude.json'), posix.join(campaign, '.claude.json')]) {
        for (const [tool, input] of [
            ['Write', { file_path: path, content: '{}' }],
            ['Edit', { file_path: path, old_string: 'a', new_string: 'b' }],
        ] as const) {
            const card = judge(tool, input, scope);
            assert.equal(card.verdict, 'ask', `${tool} ${path}`);
            assert.equal(card.verdict === 'ask' ? card.remember : 'x', null);
            assert.deepEqual(fullAccessJudgement(tool, input, scope), runs, `${tool} ${path}`);
        }
    }
    assert.equal(judge('Read', { file_path: posix.join(chat, '.claude.json') }, scope).verdict, 'allow', 'a read in a working folder is not a write');
});

test('claude mcp and codex mcp are a card each time, never "always", and run in Full access', () => {
    for (const command of [
        'claude mcp add crm -- node crm.js',
        'claude mcp list',
        'claude --debug mcp add-json crm {}',
        'codex mcp add crm -- node crm.js',
        '/usr/local/bin/codex mcp remove crm',
        'npx @anthropic-ai/claude-code mcp add crm -- node crm.js',
        'claude m\\cp add crm -- node crm.js',
    ]) {
        assert.deepEqual(judge('Bash', { command }, scope), { verdict: 'ask', title: serversTitle, detail: command, remember: null }, command);
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), runs, command);
    }
    const plain = judge('Bash', { command: 'claude --version' }, scope);
    assert.equal(plain.verdict === 'ask' ? plain.title : '', 'Run a command', 'another word of claude is a command like any');
    assert.equal(judge('Bash', { command: 'grep -r mcp notes' }, scope).verdict === 'ask' && (judge('Bash', { command: 'grep -r mcp notes' }, scope) as { title: string }).title, 'Run a command');
});

const server = 'jason_desktop_0123456789abcdef';
const base = { mcpConfig: '/data/desktop/turns/turn-0123456789abcdef01234567.mcp.json', server, addDirs: [], denied: fence.commands, allowed: fence.allowed, reads: fence.reads, dataRules: [] };
const settingsFile = '/data/desktop/turns/turn-0123456789abcdef01234567.settings.json';
/** Claude Code at the chat's floor, below the floor of Full access, at it, and a current one. */
const versions = [claudeFloor, '2.1.300', fullAccessFloor, '2.1.329'] as const;
const after = (args: string[], flag: string) => (args.includes(flag) ? args[args.indexOf(flag) + 1] : undefined);
const defences = { disableClaudeAiConnectors: true, deniedMcpServers: [{ serverName: 'claude-in-chrome' }], disableSkillShellExecution: true };
const hookUrl = 'http://127.0.0.1:4100/hook';

test('every turn that does not run in Full access keeps the sealed line whole, in each mode, at 2.1.257, 2.1.300, 2.1.322 and current: project settings alone, the turn\'s MCP file alone, no browser from the version that knows the flag, and settings that keep the rest out', () => {
    for (const version of versions) {
        for (const mode of ['ask_first', 'auto_edit', 'plan_only'] as const) {
            // Ask first and Auto-edit ride the hook from the floor of Full access on, their settings in the turn's own file.
            const hooked = mode !== 'plan_only' && compareVersions(version, fullAccessFloor) >= 0;
            const args = claudeArguments({ ...base, mode, ...(hooked ? { settingsFile } : {}), version });
            assert.equal(after(args, '--setting-sources'), 'project', `${version} ${mode}`);
            assert.ok(args.includes('--strict-mcp-config'), `${version} ${mode}`);
            assert.equal(args.includes('--no-chrome'), compareVersions(version, noChromeFrom) >= 0, `${version} ${mode}`);
            assert.equal(after(args, '--settings'), hooked ? settingsFile : mode === 'plan_only' ? planTurnSettings : claudeTurnSettings, `${version} ${mode}`);
            assert.equal(after(args, '--mcp-config'), base.mcpConfig, `${version} ${mode}`);
            assert.equal(after(args, '--permission-prompt-tool'), `mcp__${server}__decide`, `${version} ${mode}`);
        }
    }
    // Each of those settings carries the server defences: inline below the floor of Full access and in Plan only, in the
    // file the hook rides at the floor and above.
    const sealed = [JSON.parse(claudeTurnSettings), JSON.parse(planTurnSettings), hookedSettings('ask_first', hookUrl, 'the-secret'), hookedSettings('auto_edit', hookUrl, 'the-secret')] as Record<string, unknown>[];
    for (const each of sealed) {
        for (const [key, value] of Object.entries(defences)) assert.deepEqual(each[key], value, key);
        assert.equal((each['permissions'] as { blockReadsOutsideWorkingDirectories?: unknown }).blockReadsOutsideWorkingDirectories, true);
    }
    assert.equal(noChromeFrom, '2.1.285');
});

test('a turn that runs in Full access, at the floor of Full access and above, loads the person\'s own configuration as in a terminal: none of --setting-sources, --strict-mcp-config and --no-chrome, and its hook in settings that carry none of the server defences', () => {
    for (const version of [fullAccessFloor, '2.1.329']) {
        const args = claudeArguments({ ...base, mode: 'full_access', settingsFile, version });
        for (const flag of ['--setting-sources', '--strict-mcp-config', '--no-chrome']) assert.ok(!args.includes(flag), `${version}: ${flag}`);
        assert.equal(after(args, '--permission-mode'), 'bypassPermissions', version);
        assert.equal(after(args, '--settings'), settingsFile, version);
        // The turn's own MCP file still names the prompt tool's server, beside the person's own servers.
        assert.equal(after(args, '--mcp-config'), base.mcpConfig, version);
        assert.equal(after(args, '--permission-prompt-tool'), `mcp__${server}__decide`, version);
    }
    const full = hookedSettings('full_access', hookUrl, 'the-secret') as Record<string, unknown>;
    for (const key of Object.keys(defences)) assert.equal(key in full, false, key);
    assert.equal((full['permissions'] as { blockReadsOutsideWorkingDirectories?: unknown }).blockReadsOutsideWorkingDirectories, true);
    assert.equal(full['disableAllHooks'], false);
    assert.deepEqual(full['allowedHttpHookUrls'], [hookUrl]);
    assert.throws(() => claudeArguments({ ...base, mode: 'full_access', version: fullAccessFloor }), /settings file/, 'never without the file its hook rides in');
});

test('every turn\'s prompt tool is on a server named for that turn alone, the fixed prefix and a nonce, so a server of the old fixed name in the person\'s own scopes answers no card; a name of any other shape is refused', () => {
    for (const mode of ['full_access', 'ask_first', 'auto_edit', 'plan_only'] as const) {
        const args = claudeArguments({ ...base, mode, ...(mode === 'plan_only' ? {} : { settingsFile }), version: '2.1.329' });
        assert.equal(after(args, '--permission-prompt-tool'), 'mcp__jason_desktop_0123456789abcdef__decide', mode);
        assert.ok(!args.includes('mcp__jason_desktop__decide'), mode);
    }
    for (const named of ['jason_desktop', 'jason_desktop_', 'jason_desktop_0123', 'jason_desktop_0123456789ABCDEF', 'jason_desktop_0123456789abcdef0', 'other_0123456789abcdef', '--strict-mcp-config']) {
        assert.throws(() => claudeArguments({ ...base, server: named, mode: 'ask_first', version: '2.1.329' }), /server/, named);
    }
});

test('every Codex turn leaves the person\'s config.toml unread, a resumed one too', () => {
    for (const shape of [{}, { model: 'gpt-6.1-sol' }, { model: 'gpt-6.1-sol', effort: 'high' as const }, { resume: 'thr-1' }]) {
        for (const platform of ['win32', 'darwin', 'linux'] as const) {
            const args = codexArguments({ ...shape, platform });
            assert.ok(args.includes('--ignore-user-config'), JSON.stringify(shape));
            const resume = args.indexOf('resume');
            if (resume >= 0) assert.ok(args.indexOf('--ignore-user-config') < resume, 'before the resume');
        }
    }
});

test('every Codex turn turns the person\'s ChatGPT connectors off, as the naming run does, a resumed one too', () => {
    for (const shape of [{}, { model: 'gpt-6.1-sol', effort: 'high' as const }, { resume: 'thr-1' }]) {
        for (const platform of ['win32', 'darwin', 'linux'] as const) {
            const args = codexArguments({ ...shape, platform });
            const settings = args.flatMap((arg, i) => (args[i - 1] === '-c' ? [arg] : []));
            assert.equal(settings.filter((setting) => setting === 'features.apps=false').length, 1, `${platform} ${JSON.stringify(shape)}`);
            assert.ok(!settings.some((setting) => setting.startsWith('features.apps=') && setting !== 'features.apps=false'));
            const resume = args.indexOf('resume');
            if (resume >= 0) assert.ok(args.indexOf('features.apps=false') < resume, 'before the resume');
        }
    }
});

test('a Windows Codex turn names the elevated sandbox for the run, and a macOS or Linux turn names no kind', () => {
    assert.equal(codexWindowsSandbox, 'windows.sandbox="elevated"');
    for (const shape of [{}, { model: 'gpt-6.1-sol', effort: 'high' as const }, { resume: 'thr-1' }]) {
        const windows = codexArguments({ ...shape, platform: 'win32' });
        const settings = windows.flatMap((arg, i) => (windows[i - 1] === '-c' ? [arg] : []));
        assert.deepEqual(settings.filter((setting) => setting.startsWith('windows.')), [codexWindowsSandbox], JSON.stringify(shape));
        const resume = windows.indexOf('resume');
        if (resume >= 0) assert.ok(windows.indexOf(codexWindowsSandbox) < resume, 'before the resume');
        for (const platform of ['darwin', 'linux'] as const) {
            const other = codexArguments({ ...shape, platform });
            assert.ok(!other.some((arg) => arg.startsWith('windows.')), `${platform} ${JSON.stringify(shape)}`);
            // Unchanged but for the kind.
            assert.deepEqual(other, windows.filter((arg, i) => arg !== codexWindowsSandbox && windows[i + 1] !== codexWindowsSandbox));
        }
    }
    for (const platform of ['win32', 'darwin', 'linux'] as const) assert.ok(!codexArguments({ platform }).some((arg) => arg.includes('unelevated')), platform);
});

test('Codex below the version that knows --ignore-user-config, or one whose version cannot be read, is held with the words to update it', () => {
    assert.equal(codexFloor, '0.122.0');
    assert.equal(codexFloorSentence(codexFloor), null);
    assert.equal(codexFloorSentence('0.130.2'), null);
    for (const version of ['0.121.9', '0.122.0-alpha.3', null]) {
        assert.match(codexFloorSentence(version) ?? '', /[Uu]pdate Codex/, String(version));
    }
    assert.match(codexFloorSentence('0.121.0') ?? '', /0\.121\.0.*0\.122\.0/);
    assert.equal(printedCodexVersion({ exit: 0, stdout: 'codex-cli 0.122.0\n', stderr: '' }), '0.122.0');
    assert.equal(printedCodexVersion({ exit: 0, stdout: 'node 22.11.0 found\ncodex-cli 0.130.1\n', stderr: '' }), '0.130.1');
    assert.equal(printedCodexVersion({ exit: 1, stdout: 'codex-cli 0.130.1\n', stderr: '' }), null);
});
