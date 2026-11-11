import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { readFence } from '../src/chat-deny.ts';
import { claudeAllowedTools, claudeArguments, claudeLine, claudeStep, codexArguments, codexLine, dataRules, hostEnvironment, knownDataEntries, knownDesktopEntries, rulePath, type HostLine } from '../src/chat-hosts.ts';
import { desktopPaths } from '../src/paths.ts';
import { markersIn, plantedConfiguration, plantedMarkers } from './planted-session.ts';

const here = dirname(fileURLToPath(import.meta.url));
const fence = readFence(join(here, '..'));
const entries = fence.commands;
const fixture = (name: string) => readFileSync(join(here, 'fixtures', name), 'utf8').split(/\r?\n/).filter((line) => line !== '');

const turn = { mcpConfig: '/data/desktop/turn-0123456789abcdef01234567.mcp.json', addDirs: [], denied: entries, allowed: fence.allowed, dataRules: [] };

/** The value that follows a flag, once. */
function valueOf(args: readonly string[], flag: string): string | undefined {
    const at = args.indexOf(flag);
    return at < 0 ? undefined : args[at + 1];
}

/** The values that follow a flag taking a list: every element up to the next one that starts with a dash. */
function valuesOf(args: readonly string[], flag: string): string[] {
    const at = args.indexOf(flag);
    if (at < 0) return [];
    const values: string[] = [];
    for (let i = at + 1; i < args.length && !(args[i] ?? '-').startsWith('-'); i++) values.push(args[i] ?? '');
    return values;
}

const listFlags = new Set(['--allowedTools', '--disallowedTools']);

test('a Claude Code turn runs in print mode with the stream-json events, and never with --bare', () => {
    const args = claudeArguments(turn);
    assert.deepEqual(args.slice(0, 4), ['-p', '--output-format', 'stream-json', '--verbose']);
    assert.ok(!args.includes('--bare'));
    assert.ok(args.includes('--strict-mcp-config'));
    assert.equal(valueOf(args, '--setting-sources'), 'project');
    assert.equal(valueOf(args, '--permission-prompt-tool'), 'mcp__jason_desktop__decide');
    assert.equal(valueOf(args, '--mcp-config'), turn.mcpConfig);
});

test('every Claude Code turn blocks reads outside its working folders, so a read-only command on such a path asks', () => {
    for (const args of [claudeArguments(turn), claudeArguments({ ...turn, resume: 'ses-1', addDirs: ['/c'] })]) {
        assert.equal(args.filter((arg) => arg === '--settings').length, 1);
        assert.equal(valueOf(args, '--settings'), '{"permissions":{"blockReadsOutsideWorkingDirectories":true}}');
    }
});

test('the person\'s words are never on a host\'s command line: every argument is a flag or a flag\'s value', () => {
    const claude = claudeArguments({ ...turn, model: 'sonnet', resume: 'ses-1', addDirs: ['/c', '/p'] });
    const flagsWithValue = new Set(['--output-format', '--setting-sources', '--settings', '--add-dir', '--permission-prompt-tool', '--mcp-config', '--model', '--resume']);
    for (let i = 0; i < claude.length; i++) {
        const arg = claude[i] ?? '';
        assert.ok(arg.startsWith('-'), `${arg} is not a flag`);
        if (listFlags.has(arg)) i += valuesOf(claude.slice(i), arg).length;
        else if (flagsWithValue.has(arg)) i++;
    }
    assert.deepEqual(codexArguments({}), ['exec', '--json', '--sandbox', 'read-only', '--skip-git-repo-check', '-']);
});

test('the allowed tools are the skills, web search, subagents, the task list, two reply reads, and each allowed jason verb', () => {
    assert.deepEqual(valuesOf(claudeArguments(turn), '--allowedTools'), [...claudeAllowedTools, ...fence.allowed.map((verb) => `Bash(jason ${verb}:*)`)]);
    assert.deepEqual(claudeAllowedTools.filter((tool) => tool.startsWith('Bash(')), ['Bash(reply auth status:*)', 'Bash(reply account list:*)']);
    assert.ok(!claudeArguments(turn).includes('Bash(jason:*)'));
    for (const tool of ['Skill', 'WebSearch', 'Task', 'TodoWrite']) assert.ok(claudeAllowedTools.includes(tool), tool);
    // A web page is opened by a card: the agent reads text outsiders wrote, and a fetch could carry what it sees out in a URL.
    assert.ok(!valuesOf(claudeArguments(turn), '--allowedTools').some((rule) => rule.startsWith('WebFetch')), 'WebFetch must ask');
    for (const tool of ['Bash', 'Read', 'Write', 'Edit', 'PowerShell', 'WebFetch', 'Bash(reply:*)', 'Bash(reply --version:*)', 'Bash(reply schedule list:*)']) {
        assert.ok(!claudeAllowedTools.includes(tool), `${tool} must ask`);
    }
});

test('no allowed rule begins the spellings that ran with no prompt under one rule for all of jason', () => {
    const prefixes = valuesOf(claudeArguments(turn), '--allowedTools')
        .filter((rule) => rule.startsWith('Bash(jason'))
        .map((rule) => rule.slice('Bash('.length, -':*)'.length));
    for (const spelling of ['jason  runtime stop', 'jason runtime\tstop', 'jason approval --actor human:x approve apr_A', 'jason @args.txt', 'jason export --stop', 'jason import --replace backup.zip']) {
        assert.ok(!prefixes.some((prefix) => spelling.startsWith(prefix)), spelling);
    }
    assert.ok(prefixes.every((prefix) => prefix.startsWith('jason ') && prefix.length > 'jason '.length), 'a rule for all of jason');
});

test('the host\'s argv carries every deny-list entry as a disallowed tool, and a jason command that starts with an option', () => {
    const disallowed = valuesOf(claudeArguments(turn), '--disallowedTools');
    assert.ok(entries.length > 10);
    for (const entry of entries) assert.ok(disallowed.includes(`Bash(jason ${entry}:*)`), `${entry} is not fenced`);
    assert.ok(disallowed.includes('Bash(jason -*)'));
});

test('a Claude turn is denied adding a role and linking a project\'s folder and is not allowed either: 66 verbs are allowed', () => {
    const args = claudeArguments(turn);
    const disallowed = valuesOf(args, '--disallowedTools');
    assert.ok(disallowed.includes('Bash(jason role add:*)'));
    assert.ok(disallowed.includes('Bash(jason project link:*)'));
    const allowed = valuesOf(args, '--allowedTools').filter((rule) => rule.startsWith('Bash(jason '));
    assert.ok(!allowed.includes('Bash(jason role add:*)'));
    assert.ok(!allowed.includes('Bash(jason project link:*)'));
    assert.ok(allowed.includes('Bash(jason role list:*)'));
    assert.ok(allowed.includes('Bash(jason project list:*)'));
    assert.equal(allowed.length, 66);
});

test('the PowerShell and Monitor tools are taken out of every turn by a bare rule, so the one shell is the fenced one', () => {
    for (const args of [claudeArguments(turn), claudeArguments({ ...turn, resume: 'ses-1', addDirs: ['/c'], dataRules: ['Read(//d/config)'] })]) {
        const disallowed = valuesOf(args, '--disallowedTools');
        for (const tool of ['PowerShell', 'Monitor']) {
            assert.equal(disallowed.filter((rule) => rule === tool).length, 1, tool);
            assert.ok(!valuesOf(args, '--allowedTools').some((rule) => rule.startsWith(tool)), tool);
        }
    }
});

test('the data rules join the disallowed tools after the deny list', () => {
    const rules = ['Read(//data/config)', 'Read(//data/config/**)'];
    const disallowed = valuesOf(claudeArguments({ ...turn, dataRules: rules }), '--disallowedTools');
    assert.deepEqual(disallowed.slice(-2), rules);
});

test('each allowed and disallowed rule is an argument of its own, so a comma in a data path never splits a rule', () => {
    const rules = ['Read(//Users/a,b/.jason/config)', 'Read(//Users/a,b/.jason/config/**)'];
    const args = claudeArguments({ ...turn, dataRules: rules });
    assert.equal(args.filter((arg) => arg === '--allowedTools').length, 1);
    assert.equal(args.filter((arg) => arg === '--disallowedTools').length, 1);
    for (const rule of [...claudeAllowedTools, ...fence.allowed.map((verb) => `Bash(jason ${verb}:*)`), ...rules]) assert.equal(args.filter((arg) => arg === rule).length, 1, rule);
    assert.deepEqual(valuesOf(args, '--disallowedTools').slice(-2), rules);
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

/** The folders and files the data rules refuse, one per entry, from the four rules each one gets. */
const targetsOf = (rules: readonly string[]) => [...new Set(rules.map((rule) => rule.replace(/^(?:Read|Edit)\((.*?)(?:\/\*\*)?\)$/, '$1')))];

test('the data rules refuse every entry of the data directory but desktop, and every entry of desktop but the chat folder', () => {
    const rules = dataRules('darwin', '/Users/a/.jason', ['config', 'run', 'jason.db', 'desktop', 'Desktop'], ['settings.json', 'chat', 'provider-keys', 'notes.txt']);
    assert.deepEqual(targetsOf(rules), [
        ...['config', 'run', 'state', 'logs', 'plugins', 'ui', 'work', 'skills', 'update', 'doctor', 'autostart'].map((name) => `//Users/a/.jason/${name}`),
        '//Users/a/.jason/jason.db',
        ...['settings.json', 'provider-keys', 'electron', 'turns', 'rollback', 'bundle'].map((name) => `//Users/a/.jason/desktop/${name}`),
        '//Users/a/.jason/desktop/notes.txt',
    ]);
    assert.deepEqual(rules.slice(0, 4), ['Read(//Users/a/.jason/config)', 'Read(//Users/a/.jason/config/**)', 'Edit(//Users/a/.jason/config)', 'Edit(//Users/a/.jason/config/**)']);
    const windows = targetsOf(dataRules('win32', 'C:\\d', ['CONFIG'], ['CHAT', 'Turns']));
    assert.ok(windows.includes('//c/d/CONFIG') && windows.includes('//c/d/config') && windows.includes('//c/d/desktop/Turns'));
    assert.ok(!windows.some((target) => /chat$/i.test(target)));
});

test('the data rules name the known entries even before they exist, so one made during a turn is refused too', () => {
    assert.deepEqual(targetsOf(dataRules('linux', '/home/a/.jason', [], [])), [
        ...['config', 'run', 'state', 'logs', 'plugins', 'ui', 'work', 'skills', 'update', 'doctor', 'autostart'].map((name) => `//home/a/.jason/${name}`),
        ...['settings.json', 'provider-keys', 'electron', 'turns', 'rollback', 'bundle'].map((name) => `//home/a/.jason/desktop/${name}`),
    ]);
});

test('every entry the desktop names in its folder but the chat folder is a known entry, so the data rules cover it before it exists', () => {
    const data = join('/home', 'a', '.jason');
    const paths = desktopPaths(data);
    const named = Object.values(paths)
        .filter((path) => dirname(path) === paths.desktop && path !== paths.chat)
        .map((path) => path.slice(paths.desktop.length + 1));
    assert.ok(named.includes('rollback') && named.includes('bundle'), named.join(' '));
    assert.deepEqual(named.filter((name) => !knownDesktopEntries.includes(name)), []);
});

test('every top-level entry the runtime\'s paths and the command line write in the data directory is a known entry', () => {
    const dotnet = join(here, '..', '..', 'dotnet', 'src');
    const names = new Set<string>();
    const add = (text: string, pattern: RegExp) => {
        for (const match of text.matchAll(pattern)) names.add(match[1] ?? '');
    };
    add(readFileSync(join(dotnet, 'Jason.Contracts', 'Discovery', 'JasonPaths.cs'), 'utf8'), /Path\.Combine\(Root, "([^"]+)"/g);
    add(readFileSync(join(dotnet, 'Jason.Contracts', 'Update', 'UpdateLedger.cs'), 'utf8'), /DirectoryName = "([^"]+)"/g);
    const cli = join(dotnet, 'Jason.Cli');
    for (const file of readdirSync(cli, { recursive: true, encoding: 'utf8' }).filter((name) => name.endsWith('.cs'))) {
        const text = readFileSync(join(cli, file), 'utf8');
        add(text, /DirectoryName = "([^"]+)"/g);
        add(text, /Path\.Combine\((?:dataDirectory|data|paths\.Root|env\.Paths\.Root), "([^"]+)"/g);
    }
    // A repository's .git is only looked for, to refuse removing a data directory under version control; nothing writes it.
    names.delete('.git');
    for (const name of ['autostart', 'update', 'doctor', 'state', 'config']) assert.ok(names.has(name), [...names].join(' '));
    assert.deepEqual([...names].filter((name) => !knownDataEntries.includes(name)), []);
});

test('the host\'s environment puts the bundled jason first on PATH and names the data directory', () => {
    const env = hostEnvironment({ Path: '/usr/bin:/bin', HOME: '/h', JASON_DATA_DIR: '/old' }, { platform: 'darwin', bundleVersion: null, bundle: '/app/bundle', data: '/d', program: 'codex', keyed: false, provider: {} });
    assert.equal(env.PATH, '/app/bundle:/usr/bin:/bin');
    assert.equal(Object.keys(env).filter((name) => name.toUpperCase() === 'PATH').length, 1);
    assert.equal(env.JASON_DATA_DIR, '/d');
    assert.equal(env.HOME, '/h');
    assert.equal(hostEnvironment({ PATH: 'C:\\w' }, { platform: 'win32', bundleVersion: null, bundle: 'C:\\b', data: null, program: 'codex', keyed: false, provider: {} }).PATH, 'C:\\b;C:\\w');
    assert.equal('JASON_DATA_DIR' in hostEnvironment({ PATH: '/x' }, { platform: 'darwin', bundleVersion: null, bundle: '/b', data: null, program: 'codex', keyed: false, provider: {} }), false);
});

test('every host runs the CLI confined: the environment sets JASON_CONFINED whatever the desktop inherited', () => {
    for (const program of ['claude', 'codex'] as const) {
        for (const inherited of [{}, { JASON_CONFINED: '' }, { jason_confined: '0' }, { Jason_Confined: '' }]) {
            const env = hostEnvironment({ PATH: '/x', ...inherited }, { platform: 'win32', bundleVersion: null, bundle: '/b', data: null, program, keyed: false, provider: {} });
            assert.deepEqual(Object.entries(env).filter(([name]) => name.toUpperCase() === 'JASON_CONFINED'), [['JASON_CONFINED', '1']], `${program} ${JSON.stringify(inherited)}`);
        }
    }
});

test('every host\'s environment carries the harness\'s label for reply-cli, built from the bundle\'s version, in place of an inherited one in any letter case', () => {
    for (const program of ['claude', 'codex'] as const) {
        for (const keyed of [false, true]) {
            for (const inherited of [{}, { REPLY_CLIENT: 'someone-else/1.0' }, { reply_client: 'someone-else/1.0' }, { Reply_Client: 'other/2', REPLY_CLIENT: 'someone-else/1.0' }]) {
                const env = hostEnvironment({ PATH: '/x', ...inherited }, { platform: 'win32', bundleVersion: '0.35.0-rc.1+4f2a9c1', bundle: '/b', data: null, program, keyed, provider: keyed ? { ANTHROPIC_API_KEY: 'k' } : {} });
                assert.deepEqual(Object.entries(env).filter(([name]) => name.toUpperCase() === 'REPLY_CLIENT'), [['REPLY_CLIENT', 'jason-harness/0.35.0-rc.1']], `${program} ${keyed} ${JSON.stringify(inherited)}`);
            }
        }
    }
});

test('with no bundle version a host\'s environment carries no label for reply-cli, not even an inherited one', () => {
    for (const program of ['claude', 'codex'] as const) {
        const env = hostEnvironment({ PATH: '/x', REPLY_CLIENT: 'someone-else/1.0', reply_client: 'other/2' }, { platform: 'darwin', bundleVersion: null, bundle: '/b', data: null, program, keyed: false, provider: {} });
        assert.deepEqual(Object.keys(env).filter((name) => name.toUpperCase() === 'REPLY_CLIENT'), [], program);
    }
});

test('a Claude turn is denied, by the host itself, any command that names JASON_CONFINED', () => {
    assert.ok(valuesOf(claudeArguments(turn), '--disallowedTools').includes('Bash(*JASON_CONFINED*)'));
});

test('the host\'s environment never carries a plugin\'s configuration nor CLAUDE_AUTO_BACKGROUND_TASKS', () => {
    const env = hostEnvironment(
        { PATH: '/x', JASON_Plugins__slack__Token: 'xoxb-planted', jason_plugins__grants__reply: 'y', CLAUDE_AUTO_BACKGROUND_TASKS: '1', claude_auto_background_tasks: '1', JASON_Harness__Root: '/h/Jason' },
        { platform: 'darwin', bundleVersion: null, bundle: '/b', data: '/d', program: 'codex', keyed: false, provider: {} },
    );
    assert.deepEqual(Object.keys(env).sort(), ['JASON_CONFINED', 'JASON_DATA_DIR', 'JASON_Harness__Root', 'PATH']);
    assert.ok(!JSON.stringify(env).includes('xoxb-planted'));
});

test('a desktop started from inside a Claude Code session hands the host none of that session\'s markers, and keeps a person\'s configuration', () => {
    const env = hostEnvironment({ PATH: '/x', ...plantedMarkers, claudecode: '1', ...plantedConfiguration }, { platform: 'darwin', bundleVersion: null, bundle: '/b', data: null, program: 'claude', keyed: false, provider: {} });
    assert.deepEqual(markersIn(Object.keys(env)), []);
    assert.deepEqual(Object.keys(env).sort(), ['PATH', 'JASON_CONFINED', 'CLAUDE_CODE_SUBPROCESS_ENV_SCRUB', 'CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR', 'CLAUDE_CODE_DISABLE_BACKGROUND_TASKS', ...Object.keys(plantedConfiguration)].sort());
    for (const [name, value] of Object.entries(plantedConfiguration)) assert.equal(env[name], value, name);
    assert.ok(!JSON.stringify(env).includes('planted-messaging-token'));
});

test('a key provider\'s variables are added to the host\'s environment over the desktop\'s own', () => {
    const env = hostEnvironment({ PATH: '/x', ANTHROPIC_BASE_URL: 'https://elsewhere' }, { platform: 'darwin', bundleVersion: null, bundle: '/b', data: null, program: 'claude', keyed: true, provider: { ANTHROPIC_AUTH_TOKEN: 'k', ANTHROPIC_BASE_URL: 'https://api.deepseek.com/anthropic' } });
    assert.equal(env.ANTHROPIC_AUTH_TOKEN, 'k');
    assert.equal(env.ANTHROPIC_BASE_URL, 'https://api.deepseek.com/anthropic');
});

/** What a person's shell may already say about which provider and credentials Claude Code and Codex use. */
const inheritedClaudeProvider: Readonly<Record<string, string>> = {
    ANTHROPIC_API_KEY: 'sk-inherited-anthropic',
    ANTHROPIC_AUTH_TOKEN: 'inherited-auth-token',
    ANTHROPIC_BASE_URL: 'https://inherited.example',
    ANTHROPIC_MODEL: 'inherited-model',
    ANTHROPIC_DEFAULT_OPUS_MODEL: 'inherited-opus',
    ANTHROPIC_DEFAULT_SONNET_MODEL: 'inherited-sonnet',
    ANTHROPIC_DEFAULT_HAIKU_MODEL: 'inherited-haiku',
    ANTHROPIC_SMALL_FAST_MODEL: 'inherited-small',
    CLAUDE_CODE_USE_BEDROCK: '1',
    CLAUDE_CODE_USE_VERTEX: '1',
    CLAUDE_CODE_USE_FOUNDRY: '1',
    CLAUDE_CODE_OAUTH_TOKEN: 'inherited-oauth',
};
const inheritedCodexProvider: Readonly<Record<string, string>> = { OPENAI_API_KEY: 'sk-inherited-openai', OPENAI_BASE_URL: 'https://inherited.example/v1' };

test('a key provider\'s turn is started without the provider and credentials the desktop inherited; a plain turn keeps them', () => {
    const base = { PATH: '/x', HOME: '/h', ...inheritedClaudeProvider, ...inheritedCodexProvider };
    const deepseek = { ANTHROPIC_AUTH_TOKEN: 'sk-deepseek', ANTHROPIC_BASE_URL: 'https://api.deepseek.com/anthropic', ANTHROPIC_MODEL: 'deepseek-v4-pro' };
    const keyed = hostEnvironment({ ...base, anthropic_api_key: 'sk-lower', claude_code_use_bedrock: '1' }, { platform: 'darwin', bundleVersion: null, bundle: '/b', data: null, program: 'claude', keyed: true, provider: deepseek });
    for (const name of Object.keys(inheritedClaudeProvider)) {
        const kept = Object.keys(keyed).filter((each) => each.toUpperCase() === name);
        if (name in deepseek) assert.deepEqual(kept.map((each) => keyed[each]), [deepseek[name as keyof typeof deepseek]], name);
        else assert.deepEqual(kept, [], name + ' is withheld');
    }
    for (const value of [...Object.values(inheritedClaudeProvider).filter((each) => each !== '1'), 'sk-lower']) assert.ok(!Object.values(keyed).includes(value), value);
    assert.equal(keyed.HOME, '/h');

    const openai = hostEnvironment(base, { platform: 'darwin', bundleVersion: null, bundle: '/b', data: null, program: 'codex', keyed: true, provider: { OPENAI_API_KEY: 'sk-openai' } });
    assert.equal(openai.OPENAI_API_KEY, 'sk-openai');
    assert.equal('OPENAI_BASE_URL' in openai, false);

    for (const program of ['claude', 'codex'] as const) {
        const plain = hostEnvironment(base, { platform: 'darwin', bundleVersion: null, bundle: '/b', data: null, program, keyed: false, provider: {} });
        for (const [name, value] of Object.entries({ ...inheritedClaudeProvider, ...inheritedCodexProvider })) assert.equal(plain[name], value, program + ' keeps ' + name);
    }
});

test('every Claude Code turn asks the host to scrub credentials from the commands it runs', () => {
    for (const keyed of [false, true]) {
        const env = hostEnvironment({ PATH: '/x', CLAUDE_CODE_SUBPROCESS_ENV_SCRUB: '0', claude_code_subprocess_env_scrub: '0' }, { platform: 'darwin', bundleVersion: null, bundle: '/b', data: null, program: 'claude', keyed, provider: keyed ? { ANTHROPIC_API_KEY: 'k' } : {} });
        assert.deepEqual(Object.entries(env).filter(([name]) => name.toUpperCase() === 'CLAUDE_CODE_SUBPROCESS_ENV_SCRUB'), [['CLAUDE_CODE_SUBPROCESS_ENV_SCRUB', '1']]);
    }
});

test('every Claude Code turn asks the host to run nothing in the background, and a Codex turn is left as it was', () => {
    const name = 'CLAUDE_CODE_DISABLE_BACKGROUND_TASKS';
    for (const keyed of [false, true]) {
        const env = hostEnvironment({ PATH: '/x', [name]: '0', [name.toLowerCase()]: '0' }, { platform: 'darwin', bundleVersion: null, bundle: '/b', data: null, program: 'claude', keyed, provider: keyed ? { ANTHROPIC_API_KEY: 'k' } : {} });
        assert.deepEqual(Object.entries(env).filter(([each]) => each.toUpperCase() === name), [[name, '1']]);
    }
    const codex = hostEnvironment({ PATH: '/x' }, { platform: 'darwin', bundleVersion: null, bundle: '/b', data: null, program: 'codex', keyed: false, provider: {} });
    assert.deepEqual(Object.keys(codex), ['PATH', 'JASON_CONFINED']);
});

test('every Claude Code turn asks the host to run each command in the chat folder, so a cd does not carry to the next one', () => {
    const name = 'CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR';
    for (const keyed of [false, true]) {
        const env = hostEnvironment({ PATH: '/x', [name]: '0', [name.toLowerCase()]: '0' }, { platform: 'darwin', bundleVersion: null, bundle: '/b', data: null, program: 'claude', keyed, provider: keyed ? { ANTHROPIC_API_KEY: 'k' } : {} });
        assert.deepEqual(Object.entries(env).filter(([each]) => each.toUpperCase() === name), [[name, '1']]);
    }
});

test('a recorded Claude Code turn maps to its session, its words and one step per tool use, in order, then the end', () => {
    const lines = fixture('claude-turn.jsonl').flatMap((line) => claudeLine(line));
    assert.deepEqual(lines, [
        { kind: 'session', id: '0b6f3c1e-2d4a-4c8e-9f10-5a7b8c9d0e1f' },
        { kind: 'text', text: 'Let me look at what is waiting.' },
        { kind: 'step', text: 'Ran a jason command', detail: 'jason approval list --json' },
        { kind: 'step', text: 'Read a file', detail: '/data/desktop/chat/notes.md' },
        { kind: 'step', text: 'Ran a command', detail: 'git log --oneline -5 /home/someone/private' },
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

test('a step says in plain words what the tool did, with the command, path or query beneath it as text', () => {
    assert.deepEqual(claudeStep('Bash', { command: 'jason campaign get cmp_01J' }), { text: 'Ran a jason command', detail: 'jason campaign get cmp_01J' });
    assert.deepEqual(claudeStep('PowerShell', { command: 'jason status' }), { text: 'Ran a jason command', detail: 'jason status' });
    assert.deepEqual(claudeStep('Bash', { command: 'cat notes.md' }), { text: 'Ran a command', detail: 'cat notes.md' });
    assert.deepEqual(claudeStep('Bash', { command: 'jasonx status' }), { text: 'Ran a command', detail: 'jasonx status' });
    assert.deepEqual(claudeStep('Read', { file_path: '/home/someone/notes.md' }), { text: 'Read a file', detail: '/home/someone/notes.md' });
    assert.deepEqual(claudeStep('Edit', { file_path: 'draft.md' }), { text: 'Changed a file', detail: 'draft.md' });
    assert.deepEqual(claudeStep('Grep', { pattern: 'TODO' }), { text: 'Looked through files', detail: 'TODO' });
    assert.deepEqual(claudeStep('WebSearch', { query: 'acme pricing' }), { text: 'Searched the web', detail: 'acme pricing' });
    assert.deepEqual(claudeStep('WebFetch', { url: 'https://example.com/a' }), { text: 'Read a web page', detail: 'https://example.com/a' });
    assert.deepEqual(claudeStep('Skill', { skill: 'campaign-launch' }), { text: 'Used a skill', detail: 'campaign-launch' });
    assert.deepEqual(claudeStep('TodoWrite', { todos: [{ content: 'x' }] }), { text: 'Updated its task list' });
    // A tool the table does not know is named as text under a neutral label, never as the label itself.
    assert.deepEqual(claudeStep('mcp__x__y', { path: '/p' }), { text: 'Used a tool', detail: 'mcp__x__y' });
    assert.deepEqual(claudeStep(undefined, null), { text: 'Used a tool' });
    assert.deepEqual(claudeStep('Read', null), { text: 'Read a file' });
    assert.deepEqual(claudeStep('Read', { file_path: 42 }), { text: 'Read a file' });
    // A long command is cut, never dropped.
    const long = claudeStep('Bash', { command: `echo ${'x'.repeat(1000)}` });
    assert.equal(long.text, 'Ran a command');
    assert.ok(long.detail !== undefined && long.detail.length <= 300 && long.detail.endsWith('…'), String(long.detail?.length));
});

test('a step of a tool that reads the task list says so, and one that changes it says it was updated', () => {
    assert.deepEqual(claudeStep('TaskGet', { taskId: '1' }), { text: 'Read its task list' });
    assert.deepEqual(claudeStep('TaskList', {}), { text: 'Read its task list' });
    assert.deepEqual(claudeStep('TaskCreate', { subject: 'x' }), { text: 'Updated its task list' });
    assert.deepEqual(claudeStep('TaskUpdate', { taskId: '1' }), { text: 'Updated its task list' });
});

test('a long detail is cut between two characters, never inside one written as two code units', () => {
    const face = String.fromCodePoint(0x1f600);
    // The cut falls between the two halves of the first face; the face goes whole.
    const cut = claudeStep('Bash', { command: `echo ${'x'.repeat(293)}${face.repeat(10)}` });
    assert.equal(cut.detail, `echo ${'x'.repeat(293)}…`);
    const whole = claudeStep('Bash', { command: `echo ${'x'.repeat(292)}${face.repeat(10)}` });
    assert.equal(whole.detail, `echo ${'x'.repeat(292)}${face}…`);
});

test('the table of step words answers only its own names: an inherited name is a tool it does not know', () => {
    for (const name of ['constructor', '__proto__', 'toString', 'hasOwnProperty', 'valueOf']) {
        assert.deepEqual(claudeStep(name, { file_path: 'x' }), { text: 'Used a tool', detail: name }, name);
        assert.deepEqual(codexLine(JSON.stringify({ type: 'item.started', item: { id: `i-${name}`, type: name } }), new Set()), [], name);
    }
});

test('a Codex item says its words and what it acted on', () => {
    const one = (item: Record<string, unknown>) => codexLine(JSON.stringify({ type: 'item.started', item: { id: 'i1', ...item } }), new Set());
    assert.deepEqual(one({ type: 'web_search', query: 'acme pricing' }), [{ kind: 'step', text: 'Searched the web', detail: 'acme pricing' }]);
    assert.deepEqual(one({ type: 'file_change', changes: [{ path: 'a.md', kind: 'add' }, { path: 'b.md', kind: 'update' }] }), [{ kind: 'step', text: 'Changed files', detail: 'a.md, b.md' }]);
    assert.deepEqual(one({ type: 'mcp_tool_call', server: 'jason', tool: 'status' }), [{ kind: 'step', text: 'Used a tool', detail: 'status' }]);
    assert.deepEqual(one({ type: 'todo_list', items: [] }), [{ kind: 'step', text: 'Updated its task list' }]);
    assert.deepEqual(one({ type: 'command_execution' }), [{ kind: 'step', text: 'Ran a command' }]);
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
        { kind: 'step', text: 'Ran a jason command', detail: 'jason campaign list --json' },
        { kind: 'step', text: 'Ran a command', detail: "bash -lc 'cat /home/someone/secret.txt'" },
        { kind: 'text', text: 'You have no campaigns yet.' },
        { kind: 'result', error: null },
    ] satisfies HostLine[]);
});

test('a Codex turn that failed ends with the host\'s last words', () => {
    const seen = new Set<string>();
    const results = fixture('codex-failed.jsonl').flatMap((line) => codexLine(line, seen)).filter((line) => line.kind === 'result');
    assert.deepEqual(results.at(-1), { kind: 'result', error: 'You are not signed in. Run codex login.' });
});
