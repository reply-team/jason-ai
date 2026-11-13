// Auto-edit is Ask first, except that the six file commands run with no card on paths in the chat's own folder, read by
// Jason's literal reader and never by the host's accept-edits mode. Its one judgement (`autoEditJudgement`) is Ask
// first's (`judge`) word for word, or a run where Ask first would show a card. Every path here is made up or temporary.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, posix, win32 } from 'node:path';
import { test } from 'node:test';
import ts from 'typescript';
import { fileCommandsOf, fileGrammars } from '../src/chat-auto-edit.ts';
import { readFence } from '../src/chat-deny.ts';
import { readLiteral } from '../src/chat-literal.ts';
import { autoEditJudgement, judge, type ChatScope, type Platform } from '../src/chat-permission-model.ts';
import { entryOf, realPathOf } from '../src/chat-real-path.ts';

const fence = readFence(join(import.meta.dirname, '..'));

/**
 * A scope on a platform's semantics, the real paths a map gives: a home folder with the default data directory, the chat
 * folder in it, a campaign's folder added, a folder outside, and in the chat folder a link `out` that leads outside, a
 * folder `docs`, a link `gone` that leads nowhere, a folder `drop` whose `a.md` is a link to a file outside, a plain
 * folder `plain` and a folder `.github`. `files` adds files directly in the chat folder. Names fold on Windows and
 * macOS, never on Linux.
 */
function scopeOf(platform: Platform, files: readonly string[] = []): ChatScope {
    const paths = platform === 'win32' ? win32 : posix;
    const home = platform === 'win32' ? 'C:\\Users\\someone' : platform === 'darwin' ? '/Users/someone' : '/home/someone';
    const data = paths.join(home, '.jason');
    const chat = paths.join(data, 'desktop', 'chat');
    const campaign = paths.join(home, 'Jason', 'Northwind', 'campaigns', 'spring');
    const elsewhere = platform === 'win32' ? 'C:\\elsewhere' : '/elsewhere';
    const fold = (path: string) => (platform === 'linux' ? path : path.toLowerCase());
    const existing = new Map<string, string>();
    const add = (path: string, real = path) => existing.set(fold(path), real);
    let at = chat;
    for (;;) {
        add(at);
        const up = paths.dirname(at);
        if (up === at) break;
        at = up;
    }
    for (const path of [paths.join(data, 'config'), paths.join(data, 'config', 'settings.json'), paths.join(data, 'run'), paths.join(home, 'Jason'), paths.join(home, 'Jason', 'Northwind'), paths.join(home, 'Jason', 'Northwind', 'campaigns'), campaign, elsewhere, paths.join(chat, 'docs'), paths.join(chat, 'docs', 'a.md')]) add(path);
    add(paths.join(chat, 'out'), elsewhere);
    for (const name of ['drop', 'plain', '.github', ...files]) add(paths.join(chat, name));
    add(paths.join(chat, 'drop', 'a.md'), paths.join(elsewhere, 'a.md'));
    const dangling = new Set([fold(paths.join(chat, 'gone'))]);
    return {
        platform,
        cwd: chat,
        chat,
        added: [campaign],
        data,
        home,
        protectedFiles: [paths.join(data, 'config', 'settings.json'), paths.join(data, 'run', 'runtime.json')],
        denied: fence.commands,
        deniedOperations: fence.operations,
        real: (path) => existing.get(fold(path)) ?? null,
        entry: (path) => (dangling.has(fold(path)) ? 'unresolved' : existing.has(fold(path)) ? 'present' : null),
    };
}

const platforms: readonly Platform[] = ['linux', 'darwin', 'win32'];
const scopes = platforms.map((platform) => scopeOf(platform));

function runs(scope: ChatScope, command: string): void {
    const asked = judge('Bash', { command }, scope);
    assert.equal(asked.verdict, 'ask', `${scope.platform}: Ask first cards ${JSON.stringify(command)}`);
    assert.deepEqual(autoEditJudgement('Bash', { command }, scope), { verdict: 'allow' }, `${scope.platform}: runs ${JSON.stringify(command)}`);
}

function carded(scope: ChatScope, command: string): void {
    const asked = judge('Bash', { command }, scope);
    assert.equal(asked.verdict, 'ask', `${scope.platform}: Ask first cards ${JSON.stringify(command)}`);
    assert.deepEqual(autoEditJudgement('Bash', { command }, scope), asked, `${scope.platform}: Ask first's own card for ${JSON.stringify(command)}`);
}

function refused(scope: ChatScope, command: string): void {
    const asked = judge('Bash', { command }, scope);
    assert.equal(asked.verdict, 'deny', `${scope.platform}: Ask first refuses ${JSON.stringify(command)}`);
    assert.deepEqual(autoEditJudgement('Bash', { command }, scope), asked, `${scope.platform}: refused as in Ask first ${JSON.stringify(command)}`);
}

test('the six file commands run with no card on relative paths in the chat folder, with the options their lists take', () => {
    const commands = [
        'mkdir notes',
        'mkdir -p notes/2026/spring',
        'mkdir -pv notes --verbose',
        'touch notes/plan.md',
        'touch -c docs/a.md',
        'mv docs/a.md docs/b.md',
        'mv -n -v docs/a.md notes',
        'cp docs/a.md notes/a.md',
        'cp -p --no-clobber docs/a.md notes',
        'rm docs/a.md',
        'rm -v --dir docs/old',
        'rm -- -x',
        'rmdir notes',
        'touch ./a.md',
        "touch 'a plan.md'",
        'mkdir -p notes && touch notes/plan.md',
        'mkdir notes; touch notes/plan.md',
        'mkdir notes\ntouch notes/plan.md',
        'rm a.md || touch b.md',
    ];
    for (const scope of scopes) for (const command of commands) runs(scope, command);
});

test('an absolute path in the chat folder runs, in each platform\'s spellings, Git Bash\'s /c/... among them', () => {
    runs(scopeOf('linux'), 'touch /home/someone/.jason/desktop/chat/plan.md');
    runs(scopeOf('darwin'), 'touch /Users/someone/.jason/desktop/chat/plan.md');
    // macOS folds names, so the folder spelled in another case is the same folder.
    runs(scopeOf('darwin'), 'touch /users/SOMEONE/.jason/desktop/chat/plan.md');
    runs(scopeOf('win32'), 'touch C:/Users/someone/.jason/desktop/chat/plan.md');
    runs(scopeOf('win32'), "touch 'C:\\Users\\someone\\.jason\\desktop\\chat\\plan.md'");
    runs(scopeOf('win32'), 'touch /c/Users/someone/.jason/desktop/chat/plan.md');
    // Linux does not fold: the same letters in another case are another folder, the data directory's, outside the chat
    // folder, which Ask first refuses.
    refused(scopeOf('linux'), 'touch /home/someone/.jason/Desktop/chat/plan.md');
});

test('a recursive, forced, interactive or valued form is Ask first\'s card, wherever its option stands', () => {
    const commands = [
        'rm -r docs', 'rm -R docs', 'rm -rf docs', 'rm -fr docs', 'rm -f docs/a.md', 'rm --recursive docs', 'rm --force docs/a.md',
        'rm -i docs/a.md', 'rm -I docs/a.md', 'rm --no-preserve-root docs', 'rm docs -r', 'rm docs/a.md -f', 'rm --rec docs', 'rm -dr docs',
        'cp -r docs notes', 'cp -R docs notes', 'cp -a docs notes', 'cp -l docs/a.md b.md', 'cp -s docs/a.md b.md', 'cp -L docs/a.md b.md',
        'cp -P docs/a.md b.md', 'cp -t notes docs/a.md', 'cp -f docs/a.md b.md', 'cp --preserve=links docs/a.md b.md',
        'mv -f docs/a.md b.md', 'mv -i docs/a.md b.md', 'mv -t notes docs/a.md', 'mv -b docs/a.md b.md',
        'mkdir -m 700 notes', 'mkdir --mode=700 notes', 'touch -d 2026-01-01 a.md', 'touch -r docs/a.md b.md', 'touch -h a.md', 'touch -a a.md',
        'rmdir -p notes/a', 'rm -', 'rm',
    ];
    for (const scope of scopes) for (const command of commands) carded(scope, command);
});

test('too few operands, another program, a program named otherwise, a pipe, a redirection and a folder change are cards', () => {
    const commands = [
        'mv docs/a.md', 'cp docs/a.md', 'touch', 'mkdir', 'rmdir',
        '/bin/rm docs/a.md', './rm docs/a.md', 'RM docs/a.md', 'Rm docs/a.md', 'rm.exe docs/a.md', 'sudo rm docs/a.md', 'command rm docs/a.md',
        'env rm docs/a.md', 'ln -s /elsewhere link', 'mkdir notes && ln -s /elsewhere notes/link', 'mkdir notes && echo hi',
        'touch a.md > b.md', 'rm a.md 2>/dev/null', 'touch a.md < b.md', 'mkdir a | cat', 'cat a | rm b', 'mkdir a &',
        'cd docs && rm a.md', 'cd /home/someone/.jason/desktop/chat && rm a.md', 'sed -i s/a/b/ a.md',
    ];
    for (const scope of scopes) for (const command of commands) carded(scope, command);
});

test('a word that is not literal is a card: a ~, a variable, a pattern, a brace, an escape, a substitution, a non-ASCII name', () => {
    const commands = [
        'rm ~/a.md', 'touch ~', 'rm $HOME/a.md', 'rm "$X"', 'rm *.md', 'rm a?.md', 'rm [ab].md', 'rm {a,b}.md', 'rm a\\ b.md',
        'rm $(echo a.md)', 'rm `echo a.md`', 'touch é.md', 'touch "a$b"', 'X=1 touch a.md',
    ];
    for (const scope of scopes) for (const command of commands) carded(scope, command);
});

test('.. anywhere, the chat folder itself, a path outside, a campaign\'s folder and a link that leads out are cards', () => {
    for (const scope of scopes) {
        const paths = scope.platform === 'win32' ? win32 : posix;
        const commands = [
            'rm docs/../a.md', 'touch docs/../b.md', 'mv docs/a.md docs/../b.md', 'cp docs/a.md notes/../b.md',
            'rmdir .', 'rm -d .', 'mv . notes',
            `touch ${posix.join(paths.join(scope.home, 'Jason', 'Northwind', 'campaigns', 'spring').replaceAll('\\', '/'), 'brief.md')}`,
            'rm out', 'rm out/a.md', 'mv out notes', 'cp out/a.md notes', 'touch out/new.md',
        ];
        for (const command of commands) carded(scope, command);
        // A `..` that climbs into the data directory is Ask first's refusal, before the reader.
        for (const command of ['rm ../a.md', 'touch docs/../../x.md', 'mv docs/a.md ../a.md']) refused(scope, command);
    }
    // The chat folder named whole is the folder itself, never a path in it.
    carded(scopeOf('linux'), 'rmdir /home/someone/.jason/desktop/chat');
    carded(scopeOf('win32'), 'rmdir C:/Users/someone/.jason/desktop/chat');
    carded(scopeOf('linux'), 'touch /elsewhere/a.md');
    carded(scopeOf('win32'), 'touch C:/elsewhere/a.md');
});

test('after a move or a copy, a later command that names what it moved or copied, or a path under it, is a card', () => {
    for (const scope of scopes) {
        // The move is judged before it runs: after it `notes/out` could be a link that leads out.
        carded(scope, 'mv docs notes && rm notes/out/a.md');
        carded(scope, 'cp docs/a.md b.md && rm b.md');
        carded(scope, 'mv docs/a.md notes; touch notes/x.md');
        carded(scope, 'mv docs/a.md notes\nrm docs/a.md');
        // Nothing a mkdir or a touch makes is a link, so what follows one runs.
        runs(scope, 'mkdir notes && touch notes/x.md && rm notes/x.md');
        runs(scope, 'touch a.md && mv a.md b.md');
    }
});

test('a guarded name and a jason of the agent\'s own are cards; the chat\'s own top names are refused as Ask first refuses them', () => {
    for (const scope of scopes) {
        for (const command of ['touch .git/config', 'rm docs/.git/HEAD', 'mkdir .jason', 'touch notes/AGENTS.md', 'touch .gemini/x', 'touch jason.exe', 'touch jason.cmd', 'cp docs/a.md jason.ps1']) carded(scope, command);
        for (const command of ['touch CLAUDE.md', 'rm AGENTS.md', 'mkdir .claude', 'touch .claude/x', 'rm .agents/skills/x', 'touch .mcp.json', 'mv docs/a.md CLAUDE.md']) refused(scope, command);
    }
});

test('a chat\'s own top name in another case is refused exactly as its own spelling is, where the system folds names', () => {
    for (const platform of ['darwin', 'win32'] as const) {
        const scope = scopeOf(platform);
        for (const command of ['touch claude.md', 'touch .Claude/x', 'rm Agents.MD', 'mv docs/a.md Claude.md', 'mkdir .CLAUDE']) refused(scope, command);
    }
    // Linux does not fold names: `claude.md` is another file than the instructions a host reads.
    runs(scopeOf('linux'), 'touch claude.md');
});

test('the data directory, its settings file and the standing choice are refused as Ask first refuses them', () => {
    for (const scope of scopes) {
        const paths = scope.platform === 'win32' ? win32 : posix;
        const slashed = (path: string) => path.replaceAll('\\', '/');
        refused(scope, `rm ${slashed(paths.join(scope.data, 'config', 'settings.json'))}`);
        refused(scope, `touch ${slashed(paths.join(scope.data, 'desktop', 'full-access.json'))}`);
        refused(scope, `mkdir -p notes && rm ${slashed(paths.join(scope.data, 'run', 'runtime.json'))}`);
        refused(scope, 'touch a.md && jason approval approve apr_1');
        refused(scope, 'rm JASON_CONFINED');
    }
});

test('on Windows a name the system reads as another, or a device\'s name, is a card', () => {
    const scope = scopeOf('win32');
    for (const command of ["touch 'a.'", "touch 'a '", "touch 'a:b'", 'touch nul', 'touch NUL.txt', 'touch con', 'touch docs/aux.md', 'touch com1', 'touch LPT9.log', "touch 'conin$'"]) carded(scope, command);
    // Elsewhere those are names like any other.
    runs(scopeOf('linux'), 'touch nul');
});

test('the closed lists: each program takes exactly its options and needs its operands', () => {
    assert.deepEqual(Object.keys(fileGrammars).sort(), ['cp', 'mkdir', 'mv', 'rm', 'rmdir', 'touch']);
    const read = (command: string) => {
        const reading = readLiteral('Bash', command, { platform: 'linux', home: null });
        assert.equal(reading.held, false, command);
        return reading.held ? null : fileCommandsOf(reading.commands);
    };
    assert.deepEqual(read('mkdir -p a b'), [{ program: 'mkdir', operands: ['a', 'b'] }]);
    assert.deepEqual(read('rm -- -r'), [{ program: 'rm', operands: ['-r'] }]);
    assert.deepEqual(read('mv -nv a b && rmdir c'), [{ program: 'mv', operands: ['a', 'b'] }, { program: 'rmdir', operands: ['c'] }]);
    assert.equal(read('rm a -r'), null);
    assert.equal(read('cp -pr a b'), null);
    assert.equal(read('mv --verb a b'), null);
    assert.equal(read("rm ''"), null);
});

test('a real link and a real junction that lead out of the chat folder are cards, read through the real path reader', (t) => {
    const root = mkdtempSync(join(tmpdir(), 'jason-auto-edit-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const data = join(root, 'data');
    const chat = join(data, 'desktop', 'chat');
    const outside = join(root, 'outside');
    mkdirSync(chat, { recursive: true });
    mkdirSync(outside);
    symlinkSync(outside, join(chat, 'out'), process.platform === 'win32' ? 'junction' : 'dir');
    const platform = process.platform === 'win32' ? 'win32' : process.platform === 'darwin' ? 'darwin' : 'linux';
    const scope: ChatScope = { platform, cwd: chat, chat, added: [], data, home: root, protectedFiles: [], denied: fence.commands, deniedOperations: fence.operations, real: realPathOf, entry: entryOf };
    for (const command of ['rm out', 'rm out/a.md', 'touch out/a.md', 'mv out b', 'cp out/a.md b.md']) carded(scope, command);
    runs(scope, 'touch a.md');
    // A scope that cannot say what is in the folder runs nothing unasked.
    carded({ ...scope, entry: undefined }, 'touch a.md');
});

/** A chat folder on disk, in a temporary folder, with a folder outside it; the scope reads both by the real readers. */
function onDisk(t: { after: (done: () => void) => void }): { chat: string; outside: string; scope: ChatScope } {
    const root = mkdtempSync(join(tmpdir(), 'jason-auto-edit-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const data = join(root, 'data');
    const chat = join(data, 'desktop', 'chat');
    const outside = join(root, 'outside');
    mkdirSync(chat, { recursive: true });
    mkdirSync(outside);
    const platform = process.platform === 'win32' ? 'win32' : process.platform === 'darwin' ? 'darwin' : 'linux';
    return { chat, outside, scope: { platform, cwd: chat, chat, added: [], data, home: root, protectedFiles: [], denied: fence.commands, deniedOperations: fence.operations, real: realPathOf, entry: entryOf } };
}

/** A link at `path` that leads to `target`: a symbolic link, or on Windows a junction, which needs no privilege. */
function linkTo(target: string, path: string): void {
    if (process.platform === 'win32') {
        mkdirSync(target, { recursive: true });
        symlinkSync(target, path, 'junction');
    } else {
        symlinkSync(target, path);
    }
}

test('a link that leads nowhere is a card, named itself or as a folder above the path', (t) => {
    const { chat, outside, scope } = onDisk(t);
    writeFileSync(join(chat, 'a.md'), 'a');
    mkdirSync(join(chat, 'docs'));
    // Made to lead somewhere, then left leading nowhere: its target is removed.
    linkTo(join(outside, 'gone'), join(chat, 'gone'));
    linkTo(join(outside, 'deep'), join(chat, 'docs', 'deep'));
    rmSync(join(outside, 'gone'), { recursive: true, force: true });
    rmSync(join(outside, 'deep'), { recursive: true, force: true });
    assert.equal(realPathOf(join(chat, 'gone')), null, 'the link leads nowhere');
    assert.equal(entryOf(join(chat, 'gone')), 'unresolved');
    for (const command of ['touch gone', 'cp a.md gone', 'mv a.md gone/x.md', 'touch gone/x.md', 'mkdir -p gone/x', 'rm gone', 'touch docs/deep', 'touch docs/deep/x.md']) carded(scope, command);
    runs(scope, 'touch docs/x.md');
});

test('the path a copy or a move writes into a folder is judged: through a link it is a card, and the plain case runs', (t) => {
    const { chat, outside, scope } = onDisk(t);
    writeFileSync(join(chat, 'a.md'), 'a');
    mkdirSync(join(chat, 'drop'));
    mkdirSync(join(chat, 'plain'));
    // `drop/a.md` leads outside, so `cp a.md drop` writes there; neither operand names it.
    if (process.platform === 'win32') linkTo(join(outside, 'a.md'), join(chat, 'drop', 'a.md'));
    else {
        writeFileSync(join(outside, 'a.md'), 'outside');
        symlinkSync(join(outside, 'a.md'), join(chat, 'drop', 'a.md'));
    }
    for (const command of ['cp a.md drop', 'cp a.md drop/', 'cp -p a.md ./drop', 'mv a.md drop']) carded(scope, command);
    for (const command of ['cp a.md plain', 'cp a.md plain/', 'mv a.md plain']) runs(scope, command);
});

test('a copy or a move into a folder by a guarded name of two parts is a card, as its whole path is', () => {
    for (const scope of scopes) {
        for (const command of [
            'cp docs/copilot-instructions.md .github', 'cp docs/copilot-instructions.md .github/', 'mv notes/copilot-instructions.md .github',
            // A folder moved to the name begins the guarded path, whatever it holds.
            'mv notes .github', 'mv notes docs/.github', 'mkdir notes && mv notes .github', 'mv docs/.github plain',
            // The written path of a folder destination through the map's link leads outside.
            'cp docs/a.md drop', 'mv docs/a.md drop/',
        ]) carded(scope, command);
        for (const command of ['cp docs/a.md .github', 'cp docs/a.md plain', 'mv docs/a.md plain/', 'mkdir .github/workflows']) runs(scope, command);
    }
});

test('a link in the folder that leads nowhere is a card on each system\'s semantics', () => {
    for (const scope of scopes) for (const command of ['touch gone', 'touch gone/x.md', 'cp docs/a.md gone', 'mv docs/a.md gone/x', 'rm gone', 'mkdir -p gone/x']) carded(scope, command);
});

test('a file in the chat folder named like one of the six makes that command a card, and so does naming one', () => {
    for (const platform of platforms) {
        // A file `rm` where the shell may look first: `rm` is a card, a command that is not `rm` still runs.
        carded(scopeOf(platform, ['rm']), 'rm docs/a.md');
        runs(scopeOf(platform, ['rm']), 'touch x.md');
        for (const command of ['touch rm', 'cp docs/a.md mkdir', 'mv docs/a.md touch', 'touch jason', 'mkdir rmdir']) carded(scopeOf(platform), command);
        runs(scopeOf(platform), 'touch notes/rm');
    }
    // On Windows a program's extension runs it as the program, in any case.
    carded(scopeOf('win32', ['mkdir.exe']), 'mkdir notes');
    carded(scopeOf('win32', ['Touch.CMD']), 'touch a.md');
    for (const command of ['touch rm.exe', 'touch Rm.Cmd', 'cp docs/a.md cp.ps1']) carded(scopeOf('win32'), command);
    // Elsewhere an extension makes another name.
    runs(scopeOf('linux', ['mkdir.exe']), 'mkdir notes');
    runs(scopeOf('linux'), 'touch rm.cmd');
    // On macOS names fold; on Linux they do not.
    carded(scopeOf('darwin'), 'touch RM');
    runs(scopeOf('linux'), 'touch RM');
});

/** Every string the chat's suites hold, read by the TypeScript parser, as the Full access property reads them. */
function suiteStrings(): string[] {
    const found = new Set<string>();
    const visit = (node: ts.Node): void => {
        if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) found.add(node.text);
        ts.forEachChild(node, visit);
    };
    for (const file of readdirSync(import.meta.dirname).filter((name) => /^chat-.*\.test\.ts$/.test(name))) {
        visit(ts.createSourceFile(file, readFileSync(join(import.meta.dirname, file), 'utf8'), ts.ScriptTarget.Latest, true));
    }
    return [...found];
}

const shapes: readonly { tool: string; input: (text: string) => Record<string, unknown> }[] = [
    { tool: 'Bash', input: (text) => ({ command: text }) },
    { tool: 'PowerShell', input: (text) => ({ command: text }) },
    { tool: 'Monitor', input: (text) => ({ command: text }) },
    { tool: 'Read', input: (text) => ({ file_path: text }) },
    { tool: 'Write', input: (text) => ({ file_path: text, content: 'a plan' }) },
    { tool: 'Edit', input: (text) => ({ file_path: text, old_string: 'a', new_string: 'b' }) },
    { tool: 'Glob', input: (text) => ({ pattern: text }) },
    { tool: 'Grep', input: (text) => ({ pattern: 'x', path: text }) },
    { tool: 'LS', input: (text) => ({ path: text }) },
    { tool: 'WebFetch', input: (text) => ({ url: text, prompt: 'read' }) },
    { tool: 'mcp__frobnicate__run', input: (text) => ({ command: text }) },
    { tool: 'mcp__jason_desktop__decide', input: (text) => ({ tool_name: 'Bash', input: { command: text } }) },
];

/**
 * The options each of the six takes, written here apart from the model's lists (`fileGrammars`), so a list widened there
 * is caught here: letters alone or in a cluster, long options whole, and how many operands it needs.
 */
const ownOptions: Readonly<Record<string, { letters: string; long: readonly string[]; least: number }>> = {
    mkdir: { letters: 'pv', long: ['--parents', '--verbose'], least: 1 },
    touch: { letters: 'c', long: ['--no-create'], least: 1 },
    mv: { letters: 'nv', long: ['--no-clobber', '--verbose'], least: 2 },
    cp: { letters: 'nvp', long: ['--no-clobber', '--verbose'], least: 2 },
    rm: { letters: 'dv', long: ['--dir', '--verbose'], least: 1 },
    rmdir: { letters: 'v', long: ['--verbose'], least: 1 },
};

/** The names an agent loads, written here as data apart from the model's: none may be a component of a path written. */
const ownGuards = [
    '.claude', '.git', '.jason', '.agents', '.codex', '.gemini', '.cursor', '.copilot', 'CLAUDE.md', 'CLAUDE.local.md', 'AGENTS.md',
    'AGENTS.override.md', 'GEMINI.md', '.cursorrules', '.github/copilot-instructions.md', '.mcp.json', '.claude.json',
];
const ownExtensions = 'exe com bat cmd vbs vbe js jse wsf wsh msc ps1'.split(' ');

/**
 * The test's own reading of a command Auto-edit ran, never the model's, as a second reading of everything the first one
 * decides: split into its parts on the separators the reader takes, each part's words with their quotes taken off; every
 * part one of the six with only the options of this file's own table and its operands; every operand with no `..` and
 * no name Windows reads as another, strictly inside the chat folder as spelled, with no link above it or at it that
 * leads nowhere, and strictly inside once links are followed by the scope's real path; for a copy or a move, the path it
 * writes into a folder judged the same way, and a move to a name that begins a guarded path refused; no component a
 * guarded name; nothing at or under what an earlier move or copy named; no program's name directly in the chat folder,
 * named or already there for a program the command runs.
 */
function onlyEditsTheChatFolder(command: string, scope: ChatScope): boolean {
    const paths = scope.platform === 'win32' ? win32 : posix;
    const fold = (text: string) => (scope.platform === 'linux' ? text : text.toLowerCase());
    const key = (path: string) => fold(paths.resolve(path));
    const strictlyUnder = (root: string, path: string) => key(path).startsWith(key(root).endsWith(paths.sep) ? key(root) : `${key(root)}${paths.sep}`);
    const atOrUnder = (root: string, path: string) => key(root) === key(path) || strictlyUnder(root, path);
    const realOf = (path: string): string => {
        const rest: string[] = [];
        for (let at = paths.resolve(path); ; at = paths.dirname(at)) {
            const real = scope.real(at);
            if (real !== null) return paths.join(real, ...rest.reverse());
            if (paths.dirname(at) === at) return paths.resolve(path);
            rest.push(paths.basename(at));
        }
    };
    const chatReal = realOf(scope.chat);
    const partsIn = (path: string) => paths.relative(key(chatReal), key(path)).split(paths.sep).filter((part) => part !== '');
    const guarded = (parts: readonly string[]) =>
        ownGuards.some((name) => parts.some((_, at) => name.split('/').every((each, step) => fold(parts[at + step] ?? '') === fold(each))));
    const beginsGuard = (parts: readonly string[]) => ownGuards.some((name) => name.includes('/') && fold(parts.at(-1) ?? '') === fold(name.split('/')[0]!));
    const programNamed = (name: string) => {
        const [, base, extension] = /^(.*?)(?:\.([^.]*))?$/.exec(fold(name)) ?? [];
        if (base === 'jason') return extension === undefined || ownExtensions.includes(extension);
        return Object.hasOwn(ownOptions, base ?? '') && (extension === undefined || (scope.platform === 'win32' && ownExtensions.includes(extension)));
    };
    const moved: string[] = [];
    const run = new Set<string>();
    const place = (operand: string): string | null => {
        const names = operand.split(/[\\/]/);
        if (names.includes('..')) return null;
        if (scope.platform === 'win32' && names.some((name, at) => (name !== '.' && /[. ]$/.test(name)) || (name.includes(':') && !(at === 0 && /^[a-z]:$/i.test(name))) || /^(con|prn|aux|nul|com\d|lpt\d|conin\$|conout\$)(\..*)?$/i.test(name))) return null;
        const native = scope.platform === 'win32' ? operand.replace(/^\/([a-z])(?=\/|$)/i, '$1:') : operand;
        const lexical = paths.resolve(scope.chat, native);
        if (!strictlyUnder(scope.chat, lexical)) return null;
        for (let at = lexical; strictlyUnder(scope.chat, at); at = paths.dirname(at)) if (scope.entry?.(at) === 'unresolved') return null;
        const located = realOf(lexical);
        if (!strictlyUnder(chatReal, located) || moved.some((earlier) => atOrUnder(earlier, located))) return null;
        const parts = partsIn(located);
        if (guarded(parts) || (parts.length === 1 && programNamed(parts[0]!))) return null;
        return located;
    };
    for (const part of command.split(/&&|\|\||;|\n/)) {
        const words = (part.match(/'[^']*'|"[^"]*"|\S+/g) ?? []).map((word) => word.replace(/^['"]|['"]$/g, ''));
        const program = words[0] ?? '';
        const grammar = Object.hasOwn(ownOptions, program) ? ownOptions[program] : undefined;
        if (grammar === undefined) return false;
        run.add(program);
        const operands: string[] = [];
        let options = true;
        for (const word of words.slice(1)) {
            if (word === '' || (options && word === '-')) return false;
            if (options && word === '--') options = false;
            else if (options && word.startsWith('--')) {
                if (!grammar.long.includes(word)) return false;
            } else if (options && word.startsWith('-')) {
                if (![...word.slice(1)].every((letter) => grammar.letters.includes(letter))) return false;
            } else operands.push(word);
        }
        if (operands.length < grammar.least) return false;
        const named: string[] = [];
        for (const operand of operands) {
            const at = place(operand);
            if (at === null) return false;
            named.push(at);
        }
        if (program === 'mv' || program === 'cp') {
            const destination = operands.at(-1)!.replace(/[\\/]+$/, '');
            for (const source of operands.slice(0, -1)) {
                const written = place(`${destination}/${source.replace(/[\\/]+$/, '').split(/[\\/]/).pop()}`);
                if (written === null) return false;
                if (program === 'mv' && beginsGuard(partsIn(written))) return false;
                named.push(written);
            }
            if (program === 'mv' && beginsGuard(partsIn(named[operands.length - 1]!))) return false;
            moved.push(...named);
        }
    }
    const programs = [...run].flatMap((program) => [program, ...(scope.platform === 'win32' ? ownExtensions.map((extension) => `${program}.${extension}`) : [])]);
    return programs.every((name) => scope.entry?.(paths.join(scope.cwd, name)) === null);
}

test('the property\'s own reading catches what a regression of the model would let run', () => {
    for (const scope of scopes) {
        // Each would run if the model lost one of its rules; this file's reading refuses it all the same.
        for (const command of ['rm -rf docs', 'rm --rec docs', 'cp -r docs notes', 'rm out/a.md', 'touch gone', 'touch gone/x.md', 'touch .git/x', 'touch docs/.git/x',
            'mv docs notes && rm notes/x', 'cp docs/a.md drop', 'cp docs/copilot-instructions.md .github', 'mv notes .github', 'touch rm']) {
            assert.equal(onlyEditsTheChatFolder(command, scope), false, `${scope.platform}: ${command}`);
        }
        assert.equal(onlyEditsTheChatFolder('rm docs/a.md', scopeOf(scope.platform, ['rm'])), false, scope.platform);
        for (const command of ['mkdir -p notes && touch notes/plan.md', 'cp docs/a.md plain', 'mv -n docs/a.md plain/']) assert.equal(onlyEditsTheChatFolder(command, scope), true, `${scope.platform}: ${command}`);
    }
});

test('never wider than Ask first: over every string of the chat\'s suites, Auto-edit is Ask first\'s judgement, or a run of the six in the chat folder where Ask first shows a card', () => {
    const strings = suiteStrings();
    // And two scopes whose chat folder holds a file named like a program the shell could run in place of the system's.
    const shadowing = [scopeOf('linux', ['mkdir']), scopeOf('win32', ['touch', 'rm.exe'])];
    for (const scope of [...scopes, ...shadowing]) {
        let ran = 0;
        let refusals = 0;
        for (const shape of shapes) {
            for (const text of strings) {
                const input = shape.input(text);
                const asked = judge(shape.tool, input, scope);
                const edited = autoEditJudgement(shape.tool, input, scope);
                if (asked.verdict === 'deny') refusals++;
                if (JSON.stringify(edited) === JSON.stringify(asked)) continue;
                const where = `${scope.platform} ${shape.tool}: ${JSON.stringify(text)}`;
                assert.deepEqual(edited, { verdict: 'allow' }, where);
                assert.equal(asked.verdict, 'ask', where);
                assert.equal(shape.tool, 'Bash', where);
                assert.ok(onlyEditsTheChatFolder(text, scope), where);
                ran++;
            }
        }
        // The table is read: this file's own runs are among the strings, and so are the refusals of every suite.
        if (shadowing.includes(scope)) continue;
        assert.ok(ran >= 30, `${scope.platform}: only ${ran} commands ran`);
        assert.ok(refusals >= 1000, `${scope.platform}: only ${refusals} refusals were read`);
    }
});
