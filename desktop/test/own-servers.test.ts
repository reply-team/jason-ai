import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { chmodSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, symlinkSync, truncateSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import {
    claudeServerNames,
    codexServerNames,
    depthLimit,
    fileLimit,
    nameLimit,
    namesLimit,
    ownServersFile,
    readOwnServers,
    readRegularFile,
} from '../src/own-servers.ts';
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
    assert.deepEqual(claudeServerNames(`\uFEFF${JSON.stringify({ mcpServers: { one: {} } })}`), ['one']);
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

/** The names Claude Code's file gives for these keys, through the whole reader. */
async function claudeNames(t: { after(fn: () => void): void }, keys: readonly string[]): Promise<unknown> {
    const at = home(t);
    plant(join(at, '.claude.json'), JSON.stringify({ mcpServers: Object.fromEntries(keys.map((key) => [key, {}])) }));
    return readOwnServers('claude', {}, at);
}

test('a name is drawn in forty characters at most: thirty-nine and an ellipsis, as the house cuts', async (t) => {
    const forty = 'n'.repeat(nameLimit);
    assert.equal(nameLimit, 40);
    assert.deepEqual(await claudeNames(t, ['n'.repeat(nameLimit + 5), forty]), { names: [`${'n'.repeat(nameLimit - 1)}…`, forty] });
    const cut = (await claudeNames(t, ['é'.repeat(80)])) as { names: string[] };
    assert.equal(Array.from(cut.names[0]!).length, nameLimit, 'counted in characters, not code units');
});

test('names are told apart before the cut: two that differ past it both stay, and the same name twice is one', async (t) => {
    const shared = 'p'.repeat(nameLimit);
    const both = (await claudeNames(t, [`${shared}-one`, `${shared}-two`, 'same', ' same '])) as { names: string[] };
    assert.deepEqual(both, { names: [`${'p'.repeat(nameLimit - 1)}…`, `${'p'.repeat(nameLimit - 1)}…`, 'same'] });
});

test('white space in a name is made single spaces, so words are not glued together', async (t) => {
    assert.deepEqual(await claudeNames(t, ['two\nlines\tand  tab', '  edges  ']), { names: ['two lines and tab', 'edges'] });
});

test('every unseen character is taken out of a name, class by class, and a name of them alone is dropped', async (t) => {
    // Written as escapes: no source file of this repository holds such a character raw.
    const classes: readonly [string, string][] = [
        ['a C0 control', 'bell\u0007'],
        ['a C1 control', 'next\u0085line'],
        ['DEL', 'del\u007f'],
        ['a bidirectional override and mark', 'bi\u202edi\u200f'],
        ['a bidirectional isolate', 'iso\u2066late\u2069'],
        ['the Arabic letter mark', 'alm\u061cmark'],
        ['the zero-width space, joiner and non-joiner', 'zero\u200bwi\u200dd\u200cth'],
        ['the word joiner', 'word\u2060joiner'],
        ['a soft hyphen', 'soft\u00adhyphen'],
        ['a tag character', 'tag\u{e0041}char'],
        ['a variation selector', 'vs\ufe0f'],
        ['a Hangul filler', 'han\u3164gul'],
    ];
    for (const [what, name] of classes) {
        const read = (await claudeNames(t, [name])) as { names: string[] };
        assert.equal(read.names.length, 1, what);
        assert.match(read.names[0]!, /^[a-z]+$/, `${what}: ${JSON.stringify(read.names[0])}`);
    }
    // The line and paragraph separators are white space: each becomes one space.
    assert.deepEqual(await claudeNames(t, ['line\u2028sep', 'para\u2029sep']), { names: ['line sep', 'para sep'] });
    assert.deepEqual(await claudeNames(t, ['\u202e\u200f', ' \u200b ', '\u{e0041}\u3164', 'kept']), { names: ['kept'] });
});

test('no source or test of the reader holds an unseen character raw: each is written as an escape', () => {
    for (const file of ['src/own-servers.ts', 'test/own-servers.test.ts', 'test/own-servers-fixture.ts']) {
        const text = readFileSync(join(import.meta.dirname, '..', file), 'utf8');
        const raw = /(?![\t\n\r])[\p{Cc}\p{Cf}\p{Default_Ignorable_Code_Point}\u2028\u2029]/u.exec(text);
        assert.equal(raw, null, `${file} holds U+${raw?.[0].codePointAt(0)?.toString(16)} raw`);
    }
});

// ---- No file can make the listing fail or hang ---------------------------------------------------------------------

/** A Codex file whose one server's arguments nest this deep. */
const deepCodex = (depth: number) => `[mcp_servers.deep]\nargs = ${'['.repeat(depth)}${']'.repeat(depth)}\n`;
/** A Claude Code file whose one server's entry nests this deep. */
const deepClaude = (depth: number) => `{"mcpServers": {"deep": ${'['.repeat(depth)}${']'.repeat(depth)}}}`;

test('a file nested past the depth cap lists nothing and the reader answers; one at the cap lists its names', async (t) => {
    assert.equal(depthLimit, 64);
    // In JSON the file's object and mcpServers are two levels already, so a server's arrays may go two levels less deep.
    assert.deepEqual(codexServerNames(deepCodex(depthLimit)), ['deep']);
    assert.equal(codexServerNames(deepCodex(depthLimit + 1)), null);
    assert.deepEqual(claudeServerNames(deepClaude(depthLimit - 2)), ['deep']);
    assert.equal(claudeServerNames(deepClaude(depthLimit - 1)), null);
    assert.deepEqual(codexServerNames(`[mcp_servers.inline]\nenv = ${'{ a = '.repeat(depthLimit)}1${' }'.repeat(depthLimit)}\n`), ['inline']);
    assert.equal(codexServerNames(`[mcp_servers.inline]\nenv = ${'{ a = '.repeat(depthLimit + 1)}1${' }'.repeat(depthLimit + 1)}\n`), null);
    // Brackets inside a JSON string are no nesting.
    assert.deepEqual(claudeServerNames(JSON.stringify({ mcpServers: { quoted: { args: ['['.repeat(500), '\\"[[['] } } })), ['quoted']);
    // Ten thousand and fifty thousand deep, which overflowed the stack before the cap: an answer, and nothing listed.
    const at = home(t);
    for (const depth of [10_000, 50_000]) {
        plant(join(at, '.codex', 'config.toml'), deepCodex(depth));
        plant(join(at, '.claude.json'), deepClaude(depth));
        assert.deepEqual(await readOwnServers('codex', {}, at), { unread: 'malformed' }, `codex ${depth}`);
        assert.deepEqual(await readOwnServers('claude', {}, at), { unread: 'malformed' }, `claude ${depth}`);
        plant(join(at, '.codex', 'config.toml'), `[mcp_servers.deep]\nenv = ${'{ a = '.repeat(depth)}1${' }'.repeat(depth)}\n`);
        assert.deepEqual(await readOwnServers('codex', {}, at), { unread: 'malformed' }, `codex inline ${depth}`);
    }
});

test('whatever the reading or the parse throws, the reader answers and lists nothing', async (t) => {
    const at = home(t);
    // A reader that hands back no text makes the parse throw; one that rejects with no error at all is a failed read.
    assert.deepEqual(await readOwnServers('claude', {}, at, () => Promise.resolve(42 as unknown as string)), { unread: 'malformed' });
    assert.deepEqual(await readOwnServers('codex', {}, at, () => Promise.resolve({} as unknown as string)), { unread: 'malformed' });
    assert.deepEqual(await readOwnServers('codex', {}, at, () => Promise.reject(undefined)), { unread: 'unreadable' });
    assert.deepEqual(await readOwnServers('claude', {}, at, () => Promise.reject(new RangeError('planted'))), { unread: 'unreadable' });
});

test('fifty thousand servers are read in linear time, and a file naming more than the cap lists nothing', async (t) => {
    const many = 50_000;
    const tables = Array.from({ length: many }, (_, index) => `[mcp_servers.s${index}]\ncommand = "x"\n`).join('');
    let started = performance.now();
    const codex = codexServerNames(tables);
    const codexMs = performance.now() - started;
    assert.equal(codex?.length, many);
    // Measured at over eight seconds while each name was looked for in a list; well under a tenth of that now.
    assert.ok(codexMs < 1500, `${Math.round(codexMs)} ms for ${many} Codex tables`);
    const at = home(t);
    plant(join(at, '.codex', 'config.toml'), tables);
    plant(join(at, '.claude.json'), JSON.stringify({ mcpServers: Object.fromEntries(Array.from({ length: many }, (_, index) => [`s${index}`, {}])) }));
    started = performance.now();
    assert.deepEqual(await readOwnServers('codex', {}, at), { unread: 'too_large' });
    assert.deepEqual(await readOwnServers('claude', {}, at), { unread: 'too_large' });
    const bothMs = performance.now() - started;
    assert.ok(bothMs < 3000, `${Math.round(bothMs)} ms for both files`);
    // Up to the cap every name is kept, so the page's "and N more" counts the file's own.
    assert.equal(namesLimit, 1000);
    const atCap = Array.from({ length: namesLimit }, (_, index) => `s${index}`);
    assert.equal(((await claudeNames(t, atCap)) as { names: string[] }).names.length, namesLimit);
    assert.deepEqual(await claudeNames(t, [...atCap, 'one-more']), { unread: 'too_large' });
});

test('a file larger than the size cap is not read and lists nothing', async (t) => {
    assert.equal(fileLimit, 64 * 1024 * 1024);
    const at = home(t);
    plant(join(at, '.claude.json'), JSON.stringify({ mcpServers: { big: {} } }));
    // Made larger without writing it: the size is read before anything is.
    truncateSync(join(at, '.claude.json'), fileLimit + 1);
    assert.deepEqual(await readOwnServers('claude', {}, at), { unread: 'too_large' });
    plant(join(at, 'small.toml'), '[mcp_servers.one]\n');
    assert.equal(await readRegularFile(join(at, 'small.toml'), 8), null, 'over a smaller limit');
    assert.equal(await readRegularFile(join(at, 'small.toml'), 64), '[mcp_servers.one]\n');
});

test('only a regular file is opened: a folder lists nothing, and so do a FIFO and a device where the system makes them', async (t) => {
    const at = home(t);
    mkdirSync(join(at, '.codex', 'config.toml'), { recursive: true });
    assert.deepEqual(await readOwnServers('codex', {}, at), { unread: 'unreadable' }, 'a folder');
    await assert.rejects(readRegularFile(join(at, '.codex', 'config.toml')), /not a regular file/);
    if (process.platform === 'win32') return;
    // A FIFO with no writer would hold an open for ever; it is refused by its kind, read before any open is made.
    const fifo = join(at, 'fifo-home');
    mkdirSync(fifo);
    execFileSync('mkfifo', [join(fifo, '.claude.json')]);
    assert.deepEqual(await readOwnServers('claude', { CLAUDE_CONFIG_DIR: fifo }, at), { unread: 'unreadable' }, 'a FIFO');
    // A link to an endless device is judged where it leads.
    const device = join(at, 'device-home');
    mkdirSync(device);
    symlinkSync('/dev/zero', join(device, 'config.toml'));
    assert.deepEqual(await readOwnServers('codex', { CODEX_HOME: device }, at), { unread: 'unreadable' }, 'a link to /dev/zero');
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
