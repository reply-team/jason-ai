import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { claudeServerNames, codexServerNames, nameLimit, ownServersFile, readOwnServers } from '../src/own-servers.ts';
import { claudeWithSecrets, codexWithSecrets, plantedSecrets } from './own-servers-fixture.ts';

// Every file here is planted in a home this test makes; no test reads a real home. Every name and value is made up.

function home(t: { after(fn: () => void): void }): string {
    const made = mkdtempSync(join(tmpdir(), 'jason-own-servers-'));
    t.after(() => rmSync(made, { recursive: true, force: true }));
    return made;
}

function plant(file: string, text: string): void {
    mkdirSync(join(file, '..'), { recursive: true });
    writeFileSync(file, text);
}

test('the planted secrets of both files never come back: names, and only names, leave the reader', async (t) => {
    const at = home(t);
    plant(join(at, '.claude.json'), claudeWithSecrets);
    plant(join(at, '.codex', 'config.toml'), codexWithSecrets);
    const claude = await readOwnServers('claude', {}, at);
    const codex = await readOwnServers('codex', {}, at);
    assert.deepEqual(claude, { names: ['alpha', 'beta', 'gamma'] });
    assert.deepEqual(codex, { names: ['alpha', 'beta', 'gamma'] });
    const out = JSON.stringify([claude, codex]);
    for (const secret of [...plantedSecrets, 'planted', 'PLANTED_VARIABLE', 'someone@example.invalid', 'project-local-server', 'gpt-made-up'])
        assert.ok(!out.includes(secret), secret);
});

test('Claude Code: the keys of the top-level mcpServers in the file order; a project folder\'s servers are not its user servers', () => {
    assert.deepEqual(claudeServerNames(JSON.stringify({ mcpServers: { github: {}, linear: {}, sentry: {} } })), ['github', 'linear', 'sentry']);
    assert.deepEqual(claudeServerNames(JSON.stringify({ projects: { '/x': { mcpServers: { local: {} } } } })), []);
    assert.deepEqual(claudeServerNames(JSON.stringify({ mcpServers: [] })), []);
    assert.deepEqual(claudeServerNames(`﻿${JSON.stringify({ mcpServers: { one: {} } })}`), ['one']);
    assert.equal(claudeServerNames('{"mcpServers": {"one": {}'), null, 'malformed JSON lists nothing');
    assert.equal(claudeServerNames('[1,2]'), null);
    assert.equal(claudeServerNames(''), null);
});

test('Codex: every way config.toml names a server, bare or quoted, and none twice', () => {
    const text = [
        '# a comment with [mcp_servers.not_a_server]',
        'mcp_servers.top.command = "x"',
        '[profiles.made-up]',
        'model = "m"',
        '[mcp_servers."quoted name"]',
        'command = "x" # trailing comment',
        "[mcp_servers.'literal.dotted']",
        'command = "x"',
        '[ mcp_servers . spaced ]',
        'command = "x"',
        '[mcp_servers."esc\\u0041ped"]',
        'command = "x"',
        '[mcp_servers]',
        'inline = { command = "x", args = ["a", "b"] }',
        'dotted.command = "x"',
        '[mcp_servers.top.env]',
        'K = "v"',
        '[other]',
        'mcp_servers = "not a server"',
    ].join('\r\n');
    assert.deepEqual(codexServerNames(text), ['top', 'quoted name', 'literal.dotted', 'spaced', 'escAped', 'inline', 'dotted']);
});

test('Codex: a multi-line string or array that holds what looks like a table is stepped over', () => {
    const text = [
        '[mcp_servers.real]',
        'command = "x"',
        'args = [',
        '  "a", # a comment',
        '  "[mcp_servers.fake_in_array]",',
        ']',
        'note = """',
        '[mcp_servers.fake_in_basic]',
        'a \\""" quote',
        '"""',
        "other = '''",
        '[mcp_servers.fake_in_literal]',
        "''''",
        'when = 1979-05-27 07:32:00Z',
        'mcp_servers = { from_inline = { command = "x" } }',
    ].join('\n');
    // The last line names mcp_servers inside [mcp_servers.real], so it is the key mcp_servers.real.mcp_servers, no server.
    assert.deepEqual(codexServerNames(text), ['real']);
    assert.deepEqual(codexServerNames('mcp_servers = { one = { command = "x" }, two = { url = "u" } }\n'), ['one', 'two']);
});

test('Codex: a server set enabled = false is not listed, in its table, as a dotted key or in its inline table', () => {
    const text = [
        '[mcp_servers.on]',
        'enabled = true',
        '[mcp_servers.off]',
        'command = "x"',
        'enabled = false',
        '[mcp_servers]',
        'inline_off = { command = "x", enabled = false }',
        'dotted_off.enabled = false',
        'still_on = { command = "x", env = { enabled = "false" } }',
    ].join('\n');
    assert.deepEqual(codexServerNames(text), ['on', 'still_on']);
});

test('Codex: a file this reader cannot follow lists nothing', () => {
    for (const malformed of [
        '[mcp_servers.one\ncommand = "x"\n',
        '[mcp_servers.one]\ncommand = "unterminated\n',
        '[mcp_servers.one]\nnote = """never closed\n',
        '[mcp_servers.one]\nargs = ["a", "b"\n',
        '[mcp_servers.one]\njust words\n',
        '[mcp_servers.one] trailing\n',
        '[mcp_servers.ünicode]\ncommand = "x"\n',
        '[mcp_servers.one]\ncommand = "bad \\q escape"\n',
    ])
        assert.equal(codexServerNames(malformed), null, malformed);
    assert.deepEqual(codexServerNames(''), []);
    assert.deepEqual(codexServerNames('model = "m"\n'), []);
});

test('a name is cut at forty characters, its hidden characters taken out, and an empty one dropped', async (t) => {
    const at = home(t);
    const long = 'n'.repeat(nameLimit + 5);
    plant(join(at, '.claude.json'), JSON.stringify({ mcpServers: { [long]: {}, ['bi‮di\u0007']: {}, ' ‏ ': {}, ['n'.repeat(nameLimit)]: {} } }));
    assert.deepEqual(await readOwnServers('claude', {}, at), { names: [`${'n'.repeat(nameLimit)}…`, 'bidi', 'n'.repeat(nameLimit)] });
});

test('the files: ~/.claude.json, or .claude.json in a moved CLAUDE_CONFIG_DIR; config.toml in CODEX_HOME or ~/.codex', async (t) => {
    const at = home(t);
    assert.equal(ownServersFile('claude', {}, at), join(at, '.claude.json'));
    assert.equal(ownServersFile('claude', { CLAUDE_CONFIG_DIR: join(at, 'moved') }, at), join(at, 'moved', '.claude.json'));
    assert.equal(ownServersFile('claude', { CLAUDE_CONFIG_DIR: '  ' }, at), join(at, '.claude.json'));
    assert.equal(ownServersFile('codex', {}, at), join(at, '.codex', 'config.toml'));
    assert.equal(ownServersFile('codex', { CODEX_HOME: join(at, 'codex-home') }, at), join(at, 'codex-home', 'config.toml'));
    plant(join(at, '.claude.json'), JSON.stringify({ mcpServers: { home_one: {} } }));
    plant(join(at, 'moved', '.claude.json'), JSON.stringify({ mcpServers: { moved_one: {} } }));
    plant(join(at, 'codex-home', 'config.toml'), '[mcp_servers.codex_moved]\ncommand = "x"\n');
    assert.deepEqual(await readOwnServers('claude', { CLAUDE_CONFIG_DIR: join(at, 'moved') }, at), { names: ['moved_one'] });
    assert.deepEqual(await readOwnServers('codex', { CODEX_HOME: join(at, 'codex-home') }, at), { names: ['codex_moved'] });
});

test('a file that is not there lists nothing and is no failure; one that cannot be read or parsed says only its kind', async (t) => {
    const at = home(t);
    assert.deepEqual(await readOwnServers('claude', {}, at), { names: [] });
    assert.deepEqual(await readOwnServers('codex', { CODEX_HOME: join(at, 'nowhere') }, at), { names: [] });
    mkdirSync(join(at, '.claude.json'));
    assert.deepEqual(await readOwnServers('claude', {}, at), { unread: 'unreadable' }, 'a folder where the file should be');
    const denied = Object.assign(new Error('planted secret text'), { code: 'EACCES' });
    assert.deepEqual(await readOwnServers('codex', {}, at, () => Promise.reject(denied)), { unread: 'unreadable' });
    plant(join(at, '.codex', 'config.toml'), '[mcp_servers.one\n');
    assert.deepEqual(await readOwnServers('codex', {}, at), { unread: 'malformed' });
    if (process.platform !== 'win32') {
        plant(join(at, 'locked', '.claude.json'), JSON.stringify({ mcpServers: { hidden: {} } }));
        chmodSync(join(at, 'locked', '.claude.json'), 0o000);
        const read = await readOwnServers('claude', { CLAUDE_CONFIG_DIR: join(at, 'locked') }, at);
        // A root user reads it anyway; either way nothing else comes back.
        assert.ok(JSON.stringify(read) === JSON.stringify({ unread: 'unreadable' }) || JSON.stringify(read) === JSON.stringify({ names: ['hidden'] }));
    }
});

test('only the Connect listing reads the files: the reader is used by setup.ts alone, and its names reach no chat and no log', () => {
    const source = join(import.meta.dirname, '..', 'src');
    const users = readdirSync(source)
        .filter((file) => file.endsWith('.ts') && file !== 'own-servers.ts')
        .filter((file) => /own-servers|ownServers/.test(readFileSync(join(source, file), 'utf8')));
    // main.ts hands Setup the environment and the home; setup.ts reads and puts the names on a host of `setup.status`.
    assert.deepEqual(users.sort(), ['main.ts', 'setup.ts']);
    const setup = readFileSync(join(source, 'setup.ts'), 'utf8');
    // The one log line about it names the program and the kind, never what the file said.
    assert.match(setup, /log\.info\('own_servers_unread', \{ Program: program, Kind: read\.unread \}\)/);
});
