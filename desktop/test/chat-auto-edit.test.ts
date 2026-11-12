// Auto-edit is Ask first, except that the six file commands run with no card on paths in the chat's own folder, read by
// Jason's literal reader and never by the host's accept-edits mode. Its one judgement (`autoEditJudgement`) is Ask
// first's (`judge`) word for word, or a run where Ask first would show a card. Every path here is made up or temporary.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, symlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, posix, win32 } from 'node:path';
import { test } from 'node:test';
import ts from 'typescript';
import { fileCommandsOf, fileGrammars } from '../src/chat-auto-edit.ts';
import { readFence } from '../src/chat-deny.ts';
import { readLiteral } from '../src/chat-literal.ts';
import { autoEditJudgement, judge, type ChatScope, type Platform } from '../src/chat-permission-model.ts';
import { realPathOf } from '../src/chat-real-path.ts';

const fence = readFence(join(import.meta.dirname, '..'));

/**
 * A scope on a platform's semantics, the real paths a map gives: a home folder with the default data directory, the chat
 * folder in it, a campaign's folder added, a folder outside, and in the chat folder a link `out` that leads outside and
 * a folder `docs`. Names fold on Windows and macOS, never on Linux.
 */
function scopeOf(platform: Platform): ChatScope {
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
    };
}

const platforms: readonly Platform[] = ['linux', 'darwin', 'win32'];
const scopes = platforms.map(scopeOf);

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
    const scope: ChatScope = { platform, cwd: chat, chat, added: [], data, home: root, protectedFiles: [], denied: fence.commands, deniedOperations: fence.operations, real: realPathOf };
    for (const command of ['rm out', 'rm out/a.md', 'touch out/a.md', 'mv out b', 'cp out/a.md b.md']) carded(scope, command);
    runs(scope, 'touch a.md');
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

const sixPrograms = new Set(['mkdir', 'touch', 'mv', 'cp', 'rm', 'rmdir']);

/**
 * The test's own reading of a command Auto-edit ran, never the model's: split into its parts on the separators the
 * reader takes, each part's words with their quotes taken off; every part one of the six, and every word that is not an
 * option a path with no `..`, strictly inside the chat folder as spelled.
 */
function onlyEditsTheChatFolder(command: string, scope: ChatScope): boolean {
    const paths = scope.platform === 'win32' ? win32 : posix;
    const fold = (path: string) => (scope.platform === 'linux' ? path : path.toLowerCase());
    const chat = fold(paths.resolve(scope.chat));
    for (const part of command.split(/&&|\|\||;|\n/)) {
        const words = (part.match(/'[^']*'|"[^"]*"|\S+/g) ?? []).map((word) => word.replace(/^['"]|['"]$/g, ''));
        if (!sixPrograms.has(words[0] ?? '')) return false;
        let options = true;
        for (const word of words.slice(1)) {
            if (options && word === '--') {
                options = false;
                continue;
            }
            if (options && word.startsWith('-')) continue;
            if (word.split(/[\\/]/).includes('..')) return false;
            const native = scope.platform === 'win32' ? word.replace(/^\/([a-z])(?=\/|$)/i, '$1:') : word;
            const path = fold(paths.resolve(scope.chat, native));
            if (path === chat || !path.startsWith(`${chat}${paths.sep}`)) return false;
        }
    }
    return true;
}

test('never wider than Ask first: over every string of the chat\'s suites, Auto-edit is Ask first\'s judgement, or a run of the six in the chat folder where Ask first shows a card', () => {
    const strings = suiteStrings();
    for (const scope of scopes) {
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
        assert.ok(ran >= 30, `${scope.platform}: only ${ran} commands ran`);
        assert.ok(refusals >= 1000, `${scope.platform}: only ${refusals} refusals were read`);
    }
});
