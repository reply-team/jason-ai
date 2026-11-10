import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { denyListFile, parseDenyList } from '../src/chat-deny.ts';
import { claudeAllowedTools, claudeArguments, claudeLine, claudeStep, codexArguments, codexLine, dataRules, hostEnvironment, rulePath, type HostLine } from '../src/chat-hosts.ts';

const here = dirname(fileURLToPath(import.meta.url));
const entries = parseDenyList(readFileSync(join(here, '..', denyListFile), 'utf8'));
const fixture = (name: string) => readFileSync(join(here, 'fixtures', name), 'utf8').split(/\r?\n/).filter((line) => line !== '');

const turn = { mcpConfig: '/data/desktop/turn-0123456789abcdef01234567.mcp.json', addDirs: [], denied: entries, dataRules: [] };

/** The value that follows a flag, once. */
function valueOf(args: readonly string[], flag: string): string | undefined {
    const at = args.indexOf(flag);
    return at < 0 ? undefined : args[at + 1];
}

test('a Claude Code turn runs in print mode with the stream-json events, and never with --bare', () => {
    const args = claudeArguments(turn);
    assert.deepEqual(args.slice(0, 4), ['-p', '--output-format', 'stream-json', '--verbose']);
    assert.ok(!args.includes('--bare'));
    assert.ok(args.includes('--strict-mcp-config'));
    assert.equal(valueOf(args, '--setting-sources'), 'project');
    assert.equal(valueOf(args, '--permission-prompt-tool'), 'mcp__jason_desktop__decide');
    assert.equal(valueOf(args, '--mcp-config'), turn.mcpConfig);
});

test('the person\'s words are never on a host\'s command line: every argument is a flag or a flag\'s value', () => {
    const claude = claudeArguments({ ...turn, model: 'sonnet', resume: 'ses-1', addDirs: ['/c', '/p'] });
    const flagsWithValue = new Set(['--output-format', '--allowedTools', '--disallowedTools', '--setting-sources', '--add-dir', '--permission-prompt-tool', '--mcp-config', '--model', '--resume']);
    for (let i = 0; i < claude.length; i++) {
        const arg = claude[i] ?? '';
        assert.ok(arg.startsWith('-'), `${arg} is not a flag`);
        if (flagsWithValue.has(arg)) i++;
    }
    assert.deepEqual(codexArguments({}), ['exec', '--json', '--sandbox', 'read-only', '--skip-git-repo-check', '-']);
});

test('the allowed tools are the skills, web search and fetch, subagents, the task list, jason, and two reply reads', () => {
    assert.equal(valueOf(claudeArguments(turn), '--allowedTools'), claudeAllowedTools.join(','));
    assert.deepEqual(
        claudeAllowedTools.filter((tool) => tool.startsWith('Bash(')),
        ['Bash(jason:*)', 'Bash(reply auth status:*)', 'Bash(reply account list:*)'],
    );
    for (const tool of ['Skill', 'WebSearch', 'WebFetch', 'Task', 'TodoWrite']) assert.ok(claudeAllowedTools.includes(tool), tool);
    for (const tool of ['Bash', 'Read', 'Write', 'Edit', 'PowerShell', 'Bash(reply:*)', 'Bash(reply --version:*)', 'Bash(reply schedule list:*)']) {
        assert.ok(!claudeAllowedTools.includes(tool), `${tool} must ask`);
    }
});

test('the host\'s argv carries every deny-list entry as a disallowed tool, and a jason command that starts with an option', () => {
    const disallowed = (valueOf(claudeArguments(turn), '--disallowedTools') ?? '').split(',');
    assert.ok(entries.length > 10);
    for (const entry of entries) assert.ok(disallowed.includes(`Bash(jason ${entry}:*)`), `${entry} is not fenced`);
    assert.ok(disallowed.includes('Bash(jason -*)'));
});

test('the data rules join the disallowed tools after the deny list', () => {
    const rules = ['Read(//data/config)', 'Read(//data/config/**)'];
    const disallowed = (valueOf(claudeArguments({ ...turn, dataRules: rules }), '--disallowedTools') ?? '').split(',');
    assert.deepEqual(disallowed.slice(-2), rules);
});

test('the campaign\'s and the project\'s folders are added one --add-dir each; a model and a session are passed when given', () => {
    const args = claudeArguments({ ...turn, addDirs: ['/h/projects/acme/campaigns/winter', '/h/projects/acme'], model: 'opus', resume: 'ses-42' });
    const added = args.flatMap((arg, i) => (arg === '--add-dir' ? [args[i + 1]] : []));
    assert.deepEqual(added, ['/h/projects/acme/campaigns/winter', '/h/projects/acme']);
    assert.equal(valueOf(args, '--model'), 'opus');
    assert.equal(valueOf(args, '--resume'), 'ses-42');
    const first = claudeArguments(turn);
    assert.ok(!first.includes('--add-dir') && !first.includes('--model') && !first.includes('--resume'));
});

test('a Codex turn runs exec with JSON events in the read-only sandbox, resumed by its thread, the prompt from stdin', () => {
    assert.deepEqual(codexArguments({ model: 'gpt-5', resume: 'thr-1' }), [
        'exec', '--json', '--sandbox', 'read-only', '--skip-git-repo-check', '--model', 'gpt-5', 'resume', 'thr-1', '-',
    ]);
});

test('an absolute path is spelled as Claude Code\'s file rules spell one, on both platforms', () => {
    assert.equal(rulePath('win32', 'C:\\Users\\a b\\.jason\\config'), '//c/Users/a b/.jason/config');
    assert.equal(rulePath('win32', 'd:/x/'), '//d/x');
    assert.equal(rulePath('darwin', '/Users/a/.jason/run'), '//Users/a/.jason/run');
    assert.equal(rulePath('darwin', '/Users/a/[x]*?!#'), '//Users/a/\\[x\\]\\*\\?\\!\\#');
});

test('the data rules refuse every entry of the data directory but desktop, and every entry of desktop but the chat folder', () => {
    const rules = dataRules('darwin', '/Users/a/.jason', ['config', 'run', 'jason.db', 'desktop', 'Desktop'], ['settings.json', 'chat', 'provider-keys', 'turn-1.mcp.json']);
    const targets = [...new Set(rules.map((rule) => rule.replace(/^(?:Read|Edit)\((.*?)(?:\/\*\*)?\)$/, '$1')))];
    assert.deepEqual(targets, [
        '//Users/a/.jason/config',
        '//Users/a/.jason/run',
        '//Users/a/.jason/jason.db',
        '//Users/a/.jason/desktop/settings.json',
        '//Users/a/.jason/desktop/provider-keys',
        '//Users/a/.jason/desktop/turn-1.mcp.json',
    ]);
    assert.deepEqual(rules.slice(0, 4), ['Read(//Users/a/.jason/config)', 'Read(//Users/a/.jason/config/**)', 'Edit(//Users/a/.jason/config)', 'Edit(//Users/a/.jason/config/**)']);
    assert.ok(dataRules('win32', 'C:\\d', ['CONFIG'], ['CHAT']).every((rule) => rule.includes('//c/d/CONFIG')));
});

test('the host\'s environment puts the bundled jason first on PATH and names the data directory', () => {
    const env = hostEnvironment({ Path: '/usr/bin:/bin', HOME: '/h', JASON_DATA_DIR: '/old' }, { platform: 'darwin', bundle: '/app/bundle', data: '/d', provider: {} });
    assert.equal(env.PATH, '/app/bundle:/usr/bin:/bin');
    assert.equal(Object.keys(env).filter((name) => name.toUpperCase() === 'PATH').length, 1);
    assert.equal(env.JASON_DATA_DIR, '/d');
    assert.equal(env.HOME, '/h');
    assert.equal(hostEnvironment({ PATH: 'C:\\w' }, { platform: 'win32', bundle: 'C:\\b', data: null, provider: {} }).PATH, 'C:\\b;C:\\w');
    assert.equal('JASON_DATA_DIR' in hostEnvironment({ PATH: '/x' }, { platform: 'darwin', bundle: '/b', data: null, provider: {} }), false);
});

test('the host\'s environment never carries a plugin\'s configuration nor CLAUDE_AUTO_BACKGROUND_TASKS', () => {
    const env = hostEnvironment(
        { PATH: '/x', JASON_Plugins__slack__Token: 'xoxb-planted', jason_plugins__grants__reply: 'y', CLAUDE_AUTO_BACKGROUND_TASKS: '1', claude_auto_background_tasks: '1', JASON_Harness__Root: '/h/Jason' },
        { platform: 'darwin', bundle: '/b', data: '/d', provider: {} },
    );
    assert.deepEqual(Object.keys(env).sort(), ['JASON_DATA_DIR', 'JASON_Harness__Root', 'PATH']);
    assert.ok(!JSON.stringify(env).includes('xoxb-planted'));
});

test('a key provider\'s variables are added to the host\'s environment over the desktop\'s own', () => {
    const env = hostEnvironment({ PATH: '/x', ANTHROPIC_BASE_URL: 'https://elsewhere' }, { platform: 'darwin', bundle: '/b', data: null, provider: { ANTHROPIC_AUTH_TOKEN: 'k', ANTHROPIC_BASE_URL: 'https://api.deepseek.com/anthropic' } });
    assert.equal(env.ANTHROPIC_AUTH_TOKEN, 'k');
    assert.equal(env.ANTHROPIC_BASE_URL, 'https://api.deepseek.com/anthropic');
});

test('a recorded Claude Code turn maps to its session, its words and one step per tool use, in order, then the end', () => {
    const lines = fixture('claude-turn.jsonl').flatMap((line) => claudeLine(line));
    assert.deepEqual(lines, [
        { kind: 'session', id: '0b6f3c1e-2d4a-4c8e-9f10-5a7b8c9d0e1f' },
        { kind: 'text', text: 'Let me look at what is waiting.' },
        { kind: 'step', text: 'jason approval list' },
        { kind: 'step', text: 'Read a file' },
        { kind: 'step', text: 'Ran a command' },
        { kind: 'text', text: 'Nothing is waiting for you.' },
        { kind: 'result', error: null },
    ] satisfies HostLine[]);
});

test('a Claude Code turn that ended in an error ends with the host\'s own words', () => {
    const lines = fixture('claude-failed.jsonl').flatMap((line) => claudeLine(line));
    assert.deepEqual(lines.at(-1), { kind: 'result', error: 'Not logged in. Please run /login.' });
    assert.deepEqual(claudeLine('{"type":"result","subtype":"error_max_turns","is_error":true}'), [{ kind: 'result', error: 'Claude Code ended the turn: error_max_turns.' }]);
    assert.deepEqual(claudeLine('{"type":"result","subtype":"error_during_execution"}'), [{ kind: 'result', error: 'Claude Code ended the turn: error_during_execution.' }]);
});

test('a step never carries a command\'s arguments or a path', () => {
    assert.equal(claudeStep('Bash', { command: 'jason campaign get cmp_01J' }), 'jason campaign get');
    assert.equal(claudeStep('Bash', { command: 'jason status' }), 'jason status');
    assert.equal(claudeStep('Bash', { command: 'jason --actor human:x approval approve' }), 'Ran a command');
    assert.equal(claudeStep('Bash', { command: 'cat /home/someone/secret.txt' }), 'Ran a command');
    assert.equal(claudeStep('Read', { file_path: '/home/someone/secret.txt' }), 'Read a file');
    assert.equal(claudeStep('mcp__x__y', { path: '/p' }), 'Used a tool');
    assert.equal(claudeStep(undefined, null), 'Used a tool');
});

test('an unreadable line, a tool\'s output and a malformed session say nothing', () => {
    assert.deepEqual(claudeLine(''), []);
    assert.deepEqual(claudeLine('[1,2]'), []);
    assert.deepEqual(claudeLine('{"type":"user","message":{"content":[{"type":"tool_result","content":"secret"}]}}'), []);
    assert.deepEqual(claudeLine('{"type":"system","subtype":"init","session_id":"a b"}'), []);
    assert.deepEqual(codexLine('{"type":"thread.started","thread_id":"../x y"}', new Set()), []);
});

test('a recorded Codex turn maps to its thread, one step per command said once, its message, then the end', () => {
    const seen = new Set<string>();
    const lines = fixture('codex-turn.jsonl').flatMap((line) => codexLine(line, seen));
    assert.deepEqual(lines, [
        { kind: 'session', id: '0199a213-81c0-7800-8aa1-bbab2a035a53' },
        { kind: 'step', text: 'jason campaign list' },
        { kind: 'step', text: 'Ran a command' },
        { kind: 'text', text: 'You have no campaigns yet.' },
        { kind: 'result', error: null },
    ] satisfies HostLine[]);
});

test('a Codex turn that failed ends with the host\'s last words', () => {
    const seen = new Set<string>();
    const results = fixture('codex-failed.jsonl').flatMap((line) => codexLine(line, seen)).filter((line) => line.kind === 'result');
    assert.deepEqual(results.at(-1), { kind: 'result', error: 'You are not signed in. Run codex login.' });
});
