import assert from 'node:assert/strict';
import { posix, win32 } from 'node:path';
import { afterEach, test } from 'node:test';
import { codexArguments, dataRules, rulePath } from '../src/chat-hosts.ts';
import { chatModes, type ChatMode } from '../src/chat-modes.ts';
import { PermissionBroker, type ToolAnswer } from '../src/chat-permission-broker.ts';
import type { ChatScope, Platform } from '../src/chat-permission-model.ts';

// The chat is kept out of Jason's own parts, so that it can never rewrite the instructions Jason's own agents work by:
// the role skills folder, the plugins folder and the settings file are refused to a turn in every mode, Full access
// included, by the desktop's judgement of each request and by the host's own file rules. "Change Jason itself" in Open
// in is the way to change them. True since the chat had a fence; this file keeps it true.

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
    ];
}

/** The person's own installation, its data directory the one fenced folder, as the chat builds a turn's scope. */
function scopeOf(platform: Platform): ChatScope {
    const paths = platform === 'win32' ? win32 : posix;
    const home = platform === 'win32' ? 'C:\\Users\\someone' : '/home/someone';
    const data = paths.join(home, '.jason');
    const chat = paths.join(data, 'desktop', 'chat');
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

const platforms: readonly Platform[] = ['linux', 'win32'];

test('the modes this file holds are every mode the chat has', () => {
    assert.deepEqual(chatModes, ['ask_first', 'plan_only', 'auto_edit', 'full_access']);
});

for (const mode of chatModes) {
    test(`${mode}: a turn may neither read nor change Jason's role skills folder, its plugins folder or its settings file`, async () => {
        for (const platform of platforms) {
            const scope = scopeOf(platform);
            for (const { part, inside } of parts(platform, scope.data)) {
                const requests: [string, Record<string, unknown>][] = [
                    ['Read', { file_path: inside }],
                    ['Write', { file_path: inside, content: 'x' }],
                    ['Edit', { file_path: inside, old_string: 'a', new_string: 'b' }],
                    ['Bash', { command: `echo x > "${inside}"` }],
                ];
                for (const [tool, input] of requests) {
                    const answer = await asked(mode, scope, tool, input);
                    assert.equal(answer.behavior, 'deny', `${platform} ${part}: ${tool}`);
                }
            }
        }
    });

    test(`${mode}: the host's own file rules refuse Jason's role skills folder, its plugins folder and its settings file`, () => {
        for (const platform of platforms) {
            const scope = scopeOf(platform);
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

test('a Codex turn, which the desktop does not fence, runs in its own read-only sandbox in every mode, so it changes none of them', () => {
    for (const platform of platforms) {
        const args = codexArguments({ platform });
        assert.deepEqual(args.slice(args.indexOf('--sandbox'), args.indexOf('--sandbox') + 2), ['--sandbox', 'read-only'], platform);
    }
});
