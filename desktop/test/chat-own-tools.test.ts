// The fence around the person's own tools: an MCP tool of a server other than the chat's own is a card of its own in Ask
// first and held in Full access, whatever its arguments are named, each of its strings read as a command's text is, and
// never cut; the files and commands that add a server to an agent; and what every turn's command line and settings carry
// so that no server, connector or browser of the person's reaches a turn.

import assert from 'node:assert/strict';
import { join, posix } from 'node:path';
import { test } from 'node:test';
import { readFence } from '../src/chat-deny.ts';
import { chatRules, claudeArguments, claudeTurnSettings, codexArguments, codexWindowsSandbox, fullAccessSettings, planTurnSettings } from '../src/chat-hosts.ts';
import { fullAccessJudgement, heldSentence, judge, personsToolTitle, serversTitle, type ChatScope } from '../src/chat-permission-model.ts';
import { claudeFloor, codexFloor, codexFloorSentence, noChromeFrom, printedCodexVersion } from '../src/host-version.ts';

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
const held = { outcome: 'held', message: heldSentence };

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

test('in Full access an MCP tool of the person\'s is held, never run: its arguments are no shell words', () => {
    assert.deepEqual(fullAccessJudgement('mcp__s__run', { command: 'ls' }, scope), held);
    assert.deepEqual(fullAccessJudgement('mcp__s__run', { command: 'jason campaign list' }, scope), held);
    assert.deepEqual(fullAccessJudgement('mcp__acme-crm__find_contact', { query: 'dentists' }, scope), held);
    assert.deepEqual(fullAccessJudgement('mcp__claude-in-chrome__navigate', { url: 'https://example.com/' }, scope), held);
});

test('every string of an MCP tool\'s input, at any depth, is held to the text checks: a refusal with no card', () => {
    const refusals: Record<string, unknown>[] = [
        { query: 'jason approval approve x' },
        { nested: { list: ['fine', { deeper: 'jason decision answer y --option a' }] } },
        { query: 'jason approv\\al approve x' },
        { path: posix.join(data, 'run', 'x') },
        { path: '~/.jason/desktop/provider-keys/openai' },
        { text: 'echo $JASON_CONFINED' },
        { text: 'unset jason_confined' },
        { call: 'system.shutdown' },
        { [posix.join(data, 'config', 'settings.json')]: 'a key that names the data' },
    ];
    for (const input of refusals) {
        const judged = judge('mcp__s__run', input, scope);
        assert.equal(judged.verdict, 'deny', JSON.stringify(input));
        assert.equal(fullAccessJudgement('mcp__s__run', input, scope).outcome, 'refused', JSON.stringify(input));
    }
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
        assert.equal(fullAccessJudgement('mcp__s__run', input, scope).outcome, 'refused', JSON.stringify(input));
    }
    // Joined in order, so the words a person reads in that order are the ones judged.
    assert.equal(judge('mcp__s__run', { argv: ['jason', 'approval', 'list'] }, scope).verdict, 'ask');
    assert.equal(judge('mcp__s__run', { argv: ['approve', 'approval', 'jason'] }, scope).verdict, 'ask');
});

test('an MCP tool\'s input longer than the card shows whole is refused, never cut', () => {
    const long = { text: 'a'.repeat(5000) };
    const judged = judge('mcp__s__run', long, scope);
    assert.equal(judged.verdict, 'deny');
    assert.match(judged.verdict === 'deny' ? judged.message : '', /longer than the card can show whole/);
    assert.equal(fullAccessJudgement('mcp__s__run', long, scope).outcome, 'refused');
    const fits = judge('mcp__s__run', { text: 'a'.repeat(3900) }, scope);
    assert.equal(fits.verdict, 'ask');
    assert.ok(fits.verdict === 'ask' && !fits.detail.endsWith('…'), 'a detail that fits is shown whole');
});

test('the chat\'s own permission tool stays refused to the agent', () => {
    assert.equal(judge('mcp__jason_desktop__approve', { allow: true }, scope).verdict, 'deny');
});

test('.mcp.json is refused at the chat folder\'s top and a card elsewhere, held in Full access', () => {
    const top = { file_path: posix.join(chat, '.mcp.json'), content: '{"mcpServers":{}}' };
    const judged = judge('Write', top, scope);
    assert.equal(judged.verdict, 'deny');
    assert.equal(fullAccessJudgement('Write', top, scope).outcome, 'refused');
    assert.equal(judge('Bash', { command: 'echo {} > .mcp.json' }, scope).verdict, 'deny', 'a command too');
    for (const path of [posix.join(campaign, '.mcp.json'), posix.join(chat, 'sub', '.mcp.json'), posix.join(home, 'elsewhere', '.mcp.json')]) {
        const input = { file_path: path, content: '{}' };
        const card = judge('Write', input, scope);
        assert.equal(card.verdict, 'ask', path);
        assert.equal(card.verdict === 'ask' ? card.remember : 'x', null, path);
        assert.deepEqual(fullAccessJudgement('Write', input, scope), held, path);
    }
    assert.ok(chatRules('linux', chat).includes(`Edit(/${posix.join(chat, '.mcp.json')})`));
});

test('.claude.json is a card each time wherever it lies, held in Full access', () => {
    for (const path of [posix.join(home, '.claude.json'), posix.join(chat, '.claude.json'), posix.join(campaign, '.claude.json')]) {
        for (const [tool, input] of [
            ['Write', { file_path: path, content: '{}' }],
            ['Edit', { file_path: path, old_string: 'a', new_string: 'b' }],
        ] as const) {
            const card = judge(tool, input, scope);
            assert.equal(card.verdict, 'ask', `${tool} ${path}`);
            assert.equal(card.verdict === 'ask' ? card.remember : 'x', null);
            assert.deepEqual(fullAccessJudgement(tool, input, scope), held, `${tool} ${path}`);
        }
    }
    assert.equal(judge('Read', { file_path: posix.join(chat, '.claude.json') }, scope).verdict, 'allow', 'a read in a working folder is not a write');
});

test('claude mcp and codex mcp are a card each time, never "always", and held in Full access', () => {
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
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), held, command);
    }
    const plain = judge('Bash', { command: 'claude --version' }, scope);
    assert.equal(plain.verdict === 'ask' ? plain.title : '', 'Run a command', 'another word of claude is a command like any');
    assert.equal(judge('Bash', { command: 'grep -r mcp notes' }, scope).verdict === 'ask' && (judge('Bash', { command: 'grep -r mcp notes' }, scope) as { title: string }).title, 'Run a command');
});

const base = { mcpConfig: '/data/desktop/turns/turn-0123456789abcdef01234567.mcp.json', addDirs: [], denied: fence.commands, allowed: fence.allowed, reads: fence.reads, dataRules: [] };
const settingsFile = '/data/desktop/turns/turn-0123456789abcdef01234567.settings.json';

test('every Claude Code turn at or above the version that knows it carries --no-chrome, in each mode', () => {
    for (const version of [noChromeFrom, '2.1.300', '3.0.0']) {
        for (const mode of ['ask_first', 'plan_only', 'full_access'] as const) {
            const args = claudeArguments({ ...base, mode, settingsFile, version });
            assert.ok(args.includes('--no-chrome'), `${version} ${mode}`);
            assert.ok(args.includes('--strict-mcp-config'), `${version} ${mode}`);
        }
    }
    for (const version of [claudeFloor, '2.1.284']) {
        for (const mode of ['ask_first', 'plan_only', 'full_access'] as const) {
            assert.ok(!claudeArguments({ ...base, mode, settingsFile, version }).includes('--no-chrome'), `below: ${version} ${mode}`);
        }
    }
    assert.equal(noChromeFrom, '2.1.285');
});

test('every Claude Code turn\'s settings turn the claude.ai connectors off and deny the browser\'s server, in each mode', () => {
    const defences = { disableClaudeAiConnectors: true, deniedMcpServers: [{ serverName: 'claude-in-chrome' }] };
    for (const settings of [JSON.parse(claudeTurnSettings), JSON.parse(planTurnSettings), fullAccessSettings('http://127.0.0.1:4100/hook', 'the-secret')]) {
        assert.equal(settings.disableClaudeAiConnectors, defences.disableClaudeAiConnectors);
        assert.deepEqual(settings.deniedMcpServers, defences.deniedMcpServers);
    }
});

test('Claude Code 2.1.284 gets the settings keys alone, 2.1.285 the keys and --no-chrome', () => {
    const defences = { disableClaudeAiConnectors: true, deniedMcpServers: [{ serverName: 'claude-in-chrome' }] };
    for (const [version, flag] of [['2.1.284', false], ['2.1.285', true]] as const) {
        for (const mode of ['ask_first', 'plan_only'] as const) {
            const args = claudeArguments({ ...base, mode, version });
            const settings = JSON.parse(args[args.indexOf('--settings') + 1] ?? '{}') as Record<string, unknown>;
            assert.equal(settings.disableClaudeAiConnectors, defences.disableClaudeAiConnectors, `${version} ${mode}`);
            assert.deepEqual(settings.deniedMcpServers, defences.deniedMcpServers, `${version} ${mode}`);
            assert.equal(args.includes('--no-chrome'), flag, `${version} ${mode}`);
        }
        // Full access's settings are a file of the turn's own, written from these, whatever the version.
        const full = claudeArguments({ ...base, mode: 'full_access', settingsFile, version });
        assert.equal(full[full.indexOf('--settings') + 1], settingsFile);
        assert.equal(full.includes('--no-chrome'), flag, `${version} full_access`);
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
