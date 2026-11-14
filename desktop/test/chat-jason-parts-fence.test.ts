import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join, posix, win32 } from 'node:path';
import { afterEach, test } from 'node:test';
import { codexArguments, dataRules, rulePath } from '../src/chat-hosts.ts';
import { chatModes, type ChatMode } from '../src/chat-modes.ts';
import { PermissionBroker, type ToolAnswer } from '../src/chat-permission-broker.ts';
import type { ChatScope, Platform } from '../src/chat-permission-model.ts';

// The chat is kept from changing Jason's own parts, so that it can never rewrite the instructions Jason's own agents work by:
// the role skills folder, the plugins folder and the settings file are refused to a Claude Code turn in every mode, Full
// access included, by the desktop's judgement of each request and by the host's own file rules, however the path is
// spelled; a Codex turn is held by its own read-only sandbox. "Change Jason itself" in Open in is the way to change them.
// True since the chat had a fence; this file keeps it true.

/**
 * Jason's own parts, by the data directory, each as a path inside it. A part a later layout adds or moves is a row here;
 * nothing else in this file names one.
 */
function parts(platform: Platform, data: string): { part: string; inside: string }[] {
    const paths = platform === 'win32' ? win32 : posix;
    return [
        { part: 'role skills folder', inside: paths.join(data, 'skills', 'roles', 'copywriter', 'SKILL.md') },
        { part: 'plugins folder', inside: paths.join(data, 'plugins', 'crm', 'main.js') },
        { part: 'settings file', inside: paths.join(data, 'config', 'settings.json') },
        // Layout 2 keeps what a person sets up under `setup/` and the plugins' secrets apart (`layoutTable` in paths.ts).
        { part: 'layout 2 role skills folder', inside: paths.join(data, 'setup', 'roles', 'copywriter', 'SKILL.md') },
        { part: 'layout 2 plugins folder', inside: paths.join(data, 'setup', 'plugins', 'crm', 'main.js') },
        { part: 'layout 2 settings file', inside: paths.join(data, 'setup', 'settings.json') },
        { part: 'layout 2 secrets file', inside: paths.join(data, 'config', 'secrets.json') },
    ];
}

/**
 * Other spellings of a path inside a part, each naming the same file: relative to the turn's working folder, and on
 * Windows in another case, with forward slashes, and under the `\\?\` prefix. A spelling a later change finds is a row
 * here.
 */
function spellings(platform: Platform, cwd: string, inside: string): string[] {
    const paths = platform === 'win32' ? win32 : posix;
    const relative = paths.relative(cwd, inside);
    if (platform !== 'win32') return [relative];
    return [relative, relative.replaceAll('\\', '/'), inside.toUpperCase(), inside.replaceAll('\\', '/'), `\\\\?\\${inside}`];
}

/**
 * A shell's other ways to reach a path inside a part, each a request of a shell tool that reads it or writes it: by the
 * home folder's variables, by a change of folder (`cd`, `pushd`, after `&&` or `;`) to the data directory in its literal
 * spelling or by the home folder's, and by the variable the host's environment carries the data directory in; on Windows
 * also in PowerShell's forms and the command prompt's. The data directory is the default one, `.jason` in the home
 * folder. A form a later change finds is a row here.
 */
function shellSpellings(platform: Platform, data: string, inside: string): [string, string][] {
    const paths = platform === 'win32' ? win32 : posix;
    const relative = paths.relative(data, inside);
    const slashed = relative.replaceAll('\\', '/');
    const rows: [string, string][] = [];
    const homes = ['~', '$HOME', '${HOME}', ...(platform === 'win32' ? ['$USERPROFILE'] : [])];
    for (const home of homes) rows.push(['Bash', `cat ${home}/.jason/${slashed}`], ['Bash', `echo x > ${home}/.jason/${slashed}`]);
    const folders = [`"${data}"`, ...homes.map((home) => `${home}/.jason`)];
    for (const folder of folders) {
        for (const change of ['cd', 'pushd']) {
            for (const then of [' && ', '; ']) {
                rows.push(['Bash', `${change} ${folder}${then}cat ${slashed}`], ['Bash', `${change} ${folder}${then}echo x > ${slashed}`]);
            }
        }
    }
    rows.push(
        ['Bash', `cat $JASON_DATA_DIR/${slashed}`],
        ['Bash', `echo x > \${JASON_DATA_DIR}/${slashed}`],
        ['Bash', `cd "$JASON_DATA_DIR" && cat ${slashed}`],
        ['Bash', `pushd $JASON_DATA_DIR; echo x > ${slashed}`],
    );
    if (platform !== 'win32') return rows;
    const powerShell: string[] = [];
    for (const home of ['$env:USERPROFILE', '~']) {
        powerShell.push(
            `Get-Content ${home}\\.jason\\${relative}`,
            `Set-Content -Path ${home}\\.jason\\${relative} -Value x`,
            `cd ${home}\\.jason; Get-Content ${relative}`,
            `Push-Location ${home}\\.jason; Set-Content ${relative} x`,
        );
    }
    powerShell.push(`Get-Content $env:JASON_DATA_DIR\\${relative}`, `cd $env:JASON_DATA_DIR; Set-Content ${relative} x`);
    for (const command of powerShell) rows.push(['PowerShell', command], ['Bash', `powershell -Command "${command}"`]);
    rows.push(
        ['Bash', `cmd /c type %USERPROFILE%\\.jason\\${relative}`],
        ['Bash', `cmd /c "echo x > %USERPROFILE%\\.jason\\${relative}"`],
        ['Bash', `cmd /c "cd /d %USERPROFILE%\\.jason && type ${relative}"`],
        ['Bash', `cmd /c type %JASON_DATA_DIR%\\${relative}`],
    );
    return rows;
}

/**
 * The person's own installation, its data directory the one fenced folder, as the chat builds a turn's scope: a Full
 * access turn works in its grant's folder below `chat-full`, every other in the chat folder.
 */
function scopeOf(platform: Platform, mode: ChatMode = 'ask_first'): ChatScope {
    const paths = platform === 'win32' ? win32 : posix;
    const home = platform === 'win32' ? 'C:\\Users\\someone' : platform === 'darwin' ? '/Users/someone' : '/home/someone';
    const data = paths.join(home, '.jason');
    const chat = mode === 'full_access' ? paths.join(data, 'desktop', 'chat-full', 'grant-1') : paths.join(data, 'desktop', 'chat');
    return {
        platform,
        cwd: chat,
        chat,
        added: [paths.join(home, 'Jason', 'Northwind')],
        data,
        home,
        protectedFiles: [paths.join(data, 'config', 'settings.json'), paths.join(data, 'run', 'runtime.json')],
        denied: ['approval approve', 'runtime'],
        deniedOperations: ['approval.approve'],
        real: (path) => path,
    };
}

const made: PermissionBroker[] = [];
afterEach(() => {
    for (const broker of made.splice(0)) broker.endAll();
});

/** One request of a turn in a mode, through the broker as the host asks it; a card fails the row. */
async function asked(mode: ChatMode, scope: ChatScope, tool: string, input: Record<string, unknown>): Promise<ToolAnswer> {
    const broker = new PermissionBroker({ emit: () => assert.fail(`${mode}: ${tool} ${JSON.stringify(input)} made a card`), newId: () => 'req' });
    made.push(broker);
    broker.openTurn('chat_a', 'turn_1', scope, mode);
    const answer = await broker.ask('turn_1', tool, input);
    if (mode === 'full_access') {
        const hooked = broker.hook('turn_1', tool, input, null);
        assert.equal(hooked.decision, 'deny', `the Full access hook: ${tool} ${JSON.stringify(input)}`);
    }
    return answer;
}

const platforms: readonly Platform[] = ['linux', 'darwin', 'win32'];

test('the modes this file holds are every mode the chat has', () => {
    assert.deepEqual(chatModes, ['ask_first', 'plan_only', 'auto_edit', 'full_access']);
});

for (const mode of chatModes) {
    test(`${mode}: a Claude Code turn may neither read nor change Jason's role skills folder, its plugins folder or its settings file, however it spells them`, async () => {
        for (const platform of platforms) {
            const scope = scopeOf(platform, mode);
            for (const { part, inside } of parts(platform, scope.data)) {
                for (const path of [inside, ...spellings(platform, scope.cwd, inside)]) {
                    const requests: [string, Record<string, unknown>][] = [
                        ['Read', { file_path: path }],
                        ['Write', { file_path: path, content: 'x' }],
                        ['Edit', { file_path: path, old_string: 'a', new_string: 'b' }],
                        ['Bash', { command: `cat "${path}"` }],
                        ['Bash', { command: `echo x > "${path}"` }],
                    ];
                    for (const [tool, input] of requests) {
                        const answer = await asked(mode, scope, tool, input);
                        assert.equal(answer.behavior, 'deny', `${platform} ${part} ${path}: ${tool} ${JSON.stringify(input)}`);
                    }
                }
                for (const [tool, command] of shellSpellings(platform, scope.data, inside)) {
                    const answer = await asked(mode, scope, tool, { command });
                    assert.equal(answer.behavior, 'deny', `${platform} ${part}: ${tool} ${JSON.stringify(command)}`);
                }
            }
        }
    });

    test(`${mode}: the host's own file rules refuse Jason's role skills folder, its plugins folder and its settings file`, () => {
        for (const platform of platforms) {
            const scope = scopeOf(platform, mode);
            const paths = platform === 'win32' ? win32 : posix;
            // A Full access turn runs in its grant's folder, and its rules are made with that grant.
            const rules = dataRules(platform, scope.data, [], [], mode === 'full_access' ? { id: 'grant-1', present: ['grant-1'] } : undefined);
            for (const { part, inside } of parts(platform, scope.data)) {
                // The rule of the data directory's entry the part is in, which covers everything under it.
                const entry = paths.join(scope.data, paths.relative(scope.data, inside).split(paths.sep)[0]!);
                for (const kind of ['Read', 'Edit']) {
                    assert.ok(rules.includes(`${kind}(${rulePath(platform, entry)}/**)`), `${platform} ${part}: ${kind}`);
                }
            }
        }
    });
}

test('a Codex turn, which the desktop does not fence, runs only in Ask first and Plan only, in its own read-only sandbox, so it changes none of them', () => {
    for (const platform of platforms) {
        // Its command line takes no mode: the sandbox is read-only whatever the conversation's mode.
        const args = codexArguments({ platform });
        assert.deepEqual(args.slice(args.indexOf('--sandbox'), args.indexOf('--sandbox') + 2), ['--sandbox', 'read-only'], platform);
    }
    // Auto-edit and Full access are refused on Codex before a turn starts.
    const chat = readFileSync(join(import.meta.dirname, '..', 'src', 'chat.ts'), 'utf8');
    assert.match(chat, /if \(program === 'codex'\) throw new ChatRefused\(codexAutoEdit\);/);
    assert.match(chat, /if \(program === 'codex'\) throw new ChatRefused\(codexFullAccess\);/);
});
