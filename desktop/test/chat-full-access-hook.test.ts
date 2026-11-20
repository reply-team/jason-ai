// Full access is Claude Code's own bypass mode: the turn's hook answers a refusal of three kinds or nothing, and what
// Claude Code still asks about in that mode reaches the prompt tool as a card titled for Full access, which the person
// answers. Every command here is a neutral one or a made-up program; every path is made up or a temporary one.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { request } from 'node:http';
import { tmpdir } from 'node:os';
import { join, posix, win32 } from 'node:path';
import { mock, test } from 'node:test';
import { readFence } from '../src/chat-deny.ts';
import { hostEnvironment } from '../src/chat-hosts.ts';
import { loggedTurns } from '../src/chat-log.ts';
import { PermissionBroker, type BrokerEvent, type Decided } from '../src/chat-permission-broker.ts';
import { openEndpoint, type Endpoint } from '../src/chat-permission-endpoint.ts';
import { fullAccessJudgement, judge, settingsRefusal, type ChatScope, type Platform } from '../src/chat-permission-model.ts';
import { realPathOf } from '../src/chat-real-path.ts';
import { newSecret, newTurnId } from '../src/chat-turn-files.ts';
import { desktopPaths, settingsFilesOf } from '../src/paths.ts';
import { templates, type Logger } from '../src/log.ts';

const fence = readFence(join(import.meta.dirname, '..'));

/** A scope whose data directory is the default one under a made-up home folder, nothing on disk. */
function homeScope(platform: Platform, home: string): ChatScope {
    const paths = platform === 'win32' ? win32 : posix;
    const data = paths.join(home, '.jason');
    const chat = paths.join(data, 'desktop', 'chat');
    return { platform, cwd: chat, chat, added: [], data, home, protectedFiles: [paths.join(data, 'config', 'settings.json'), paths.join(data, 'run', 'runtime.json')], denied: fence.commands, deniedOperations: fence.operations, real: () => null };
}

const scopes: readonly ChatScope[] = [homeScope('linux', '/home/someone'), homeScope('darwin', '/Users/someone'), homeScope('win32', 'C:\\Users\\someone')];

interface Wired {
    root: string;
    scope: ChatScope;
    broker: PermissionBroker;
    cards: BrokerEvent[];
    decided: Decided[];
    endpoint: Endpoint;
    secret: string;
    id: string;
}

async function wired(t: { after: (fn: () => void | Promise<void>) => void }, timeoutMs?: number): Promise<Wired> {
    const root = mkdtempSync(join(tmpdir(), 'jason-full-'));
    const paths = desktopPaths(root);
    mkdirSync(paths.chat, { recursive: true });
    const cards: BrokerEvent[] = [];
    const decided: Decided[] = [];
    const broker = new PermissionBroker({ emit: (_chat, event) => cards.push(event), decided: (each) => decided.push(each), ...(timeoutMs === undefined ? {} : { timeoutMs }) });
    const platform = process.platform === 'win32' || process.platform === 'darwin' ? process.platform : 'linux';
    const scope: ChatScope = {
        platform,
        cwd: paths.chat,
        chat: paths.chat,
        added: [],
        data: root,
        home: root,
        protectedFiles: [...settingsFilesOf(paths.data), paths.runFile],
        denied: fence.commands,
        deniedOperations: fence.operations,
        real: realPathOf,
    };
    const id = newTurnId();
    broker.openTurn('chat_a', id, scope, 'full_access');
    const secret = newSecret();
    const endpoint = await openEndpoint({ secret, decide: (tool, input, signal) => broker.ask(id, tool, input, signal), hook: (tool, input, use) => broker.hook(id, tool, input, use) });
    t.after(async () => {
        broker.endAll();
        await endpoint.close();
        rmSync(root, { recursive: true, force: true });
    });
    return { root, scope, broker, cards, decided, endpoint, secret, id };
}

function post(url: string, body: string, headers: Record<string, string>): Promise<{ status: number; body: string }> {
    return new Promise((resolve, reject) => {
        const asked = request(url, { method: 'POST', headers: { 'content-type': 'application/json', 'content-length': String(Buffer.byteLength(body)), ...headers } }, (response) => {
            const chunks: Buffer[] = [];
            response.on('data', (chunk: Buffer) => chunks.push(chunk));
            response.on('end', () => resolve({ status: response.statusCode ?? 0, body: Buffer.concat(chunks).toString('utf8') }));
        });
        asked.on('error', reject);
        asked.end(body);
    });
}

/**
 * What the hook answers a tool use, as Claude Code posts it and reads the answer: `none` for a body with no decision,
 * which leaves the tool use to the host's own mode and rules.
 */
async function hooked(w: Wired, tool: string, input: Record<string, unknown>, use = `toolu_${Math.random().toString(36).slice(2)}`): Promise<{ decision: string; reason?: string }> {
    const body = JSON.stringify({ session_id: 's', hook_event_name: 'PreToolUse', permission_mode: 'bypassPermissions', tool_name: tool, tool_input: input, tool_use_id: use });
    const answered = await post(w.endpoint.hookUrl, body, { authorization: `Bearer ${w.secret}` });
    assert.equal(answered.status, 200);
    const parsed = JSON.parse(answered.body) as { hookSpecificOutput?: { hookEventName: string; permissionDecision: string; permissionDecisionReason?: string } };
    const output = parsed.hookSpecificOutput;
    if (output === undefined) {
        assert.deepEqual(parsed, {}, 'an answer that decides nothing carries nothing');
        return { decision: 'none' };
    }
    assert.equal(output.hookEventName, 'PreToolUse');
    return output.permissionDecisionReason === undefined ? { decision: output.permissionDecision } : { decision: output.permissionDecision, reason: output.permissionDecisionReason };
}

test('the hook answers nothing for what runs and a refusal for the three kinds, never an allow and never a question', async (t) => {
    const w = await wired(t);
    assert.deepEqual(await hooked(w, 'Bash', { command: 'git status' }), { decision: 'none' });
    assert.deepEqual(await hooked(w, 'Write', { file_path: join(w.scope.chat, 'notes.md'), content: 'a plan' }), { decision: 'none' });
    assert.deepEqual(await hooked(w, 'Bash', { command: 'ls *.md' }), { decision: 'none' });
    assert.deepEqual(await hooked(w, 'AskUserQuestion', { questions: [] }), { decision: 'none' });
    assert.deepEqual(await hooked(w, 'Read', { file_path: w.scope.protectedFiles[0] }), { decision: 'deny', reason: settingsRefusal });
    assert.deepEqual(w.cards, [], 'the hook shows no card: a card is the prompt tool\'s');
});

test('a hook call that cannot be read is still a refusal, never the answer that decides nothing', async (t) => {
    const w = await wired(t);
    for (const body of ['{', '[]', JSON.stringify({ hook_event_name: 'PreToolUse', tool_name: 'Bash' })]) {
        const answered = await post(w.endpoint.hookUrl, body, { authorization: `Bearer ${w.secret}` });
        assert.equal(JSON.parse(answered.body).hookSpecificOutput.permissionDecision, 'deny', body);
    }
});

// What Claude Code still asks about in Full access reaches the prompt tool: a card titled for Full access.

const fullTitle = 'Full access asks about this one';
const titles = (cards: BrokerEvent[]) => cards.flatMap((card) => (card.kind === 'permission' ? [card.title] : []));
const settle = () => new Promise((resolve) => setImmediate(resolve));

test('a request Claude Code asks about in Full access is a card titled for Full access, and the person\'s yes runs it', async (t) => {
    const w = await wired(t);
    assert.equal((await hooked(w, 'Bash', { command: 'ls *.md' })).decision, 'none');
    const answer = w.broker.ask(w.id, 'Bash', { command: 'ls *.md' });
    await settle();
    assert.deepEqual(titles(w.cards), [fullTitle]);
    const card = w.cards[0];
    assert.ok(card?.kind === 'permission');
    assert.equal(card.detail, 'ls *.md');
    w.broker.answer('chat_a', card.request, { allow: true, always: false });
    assert.deepEqual(await answer, { behavior: 'allow', updatedInput: { command: 'ls *.md' } });
});

test('several requests Claude Code asks about in one turn are several cards', async (t) => {
    const w = await wired(t);
    const one = w.broker.ask(w.id, 'Bash', { command: 'ls *.md' });
    const two = w.broker.ask(w.id, 'Bash', { command: 'cat notes/*.txt' });
    await settle();
    assert.deepEqual(titles(w.cards), [fullTitle, fullTitle]);
    w.broker.endTurn(w.id);
    assert.equal((await one).behavior, 'deny');
    assert.equal((await two).behavior, 'deny');
});

test('a request Claude Code asks about in Full access is the person\'s to answer, one Ask first runs with no card or refuses included', async (t) => {
    const w = await wired(t);
    for (const command of ['reply auth status --json', 'jason approval approve apr_1']) {
        assert.notEqual(judge('Bash', { command }, w.scope).verdict, 'ask', command);
        void w.broker.ask(w.id, 'Bash', { command });
    }
    await settle();
    assert.deepEqual(titles(w.cards), [fullTitle, fullTitle]);
});

test('"always for this chat" works in Full access as in Ask first for a card Claude Code asks', async (t) => {
    const w = await wired(t);
    const first = w.broker.ask(w.id, 'Bash', { command: 'git push' });
    await settle();
    const card = w.cards[0];
    assert.ok(card?.kind === 'permission' && card.remember !== null);
    w.broker.answer('chat_a', card.request, { allow: true, always: true });
    assert.equal((await first).behavior, 'allow');
    assert.equal((await w.broker.ask(w.id, 'Bash', { command: 'git push' })).behavior, 'allow');
    assert.equal(w.cards.filter((each) => each.kind === 'permission').length, 1, 'the second is settled without a card');
});

test('a tool the hook never saw that reaches the prompt tool in Full access is a card, not a run', async (t) => {
    const w = await wired(t);
    const answer = w.broker.ask(w.id, 'Bash', { command: 'git push' });
    await settle();
    assert.deepEqual(titles(w.cards), [fullTitle]);
    assert.ok(!w.broker.hooked(w.id, 'toolu_stub'));
    w.broker.endTurn(w.id);
    assert.equal((await answer).behavior, 'deny');
});

test('a deletion Claude Code still asks about though the hook said nothing is a card, never run on its own and never refused unasked', async (t) => {
    const w = await wired(t);
    for (const command of ['rm -rf build', 'rm notes.md', 'rmdir old']) {
        assert.deepEqual(await hooked(w, 'Bash', { command }), { decision: 'none' }, command);
    }
    const answers = ['rm -rf build', 'rm notes.md', 'rmdir old'].map((command) => w.broker.ask(w.id, 'Bash', { command }));
    await settle();
    assert.deepEqual(titles(w.cards), [fullTitle, fullTitle, fullTitle]);
    w.broker.endTurn(w.id);
    for (const answer of answers) assert.equal((await answer).behavior, 'deny');
});

test('what the hook refuses is refused at the prompt tool too, whichever way the request came, with no card', async (t) => {
    const w = await wired(t);
    for (const input of [{ file_path: w.scope.protectedFiles[0] }, { command: `cat ${w.scope.protectedFiles[0]}` }]) {
        const tool = 'command' in input ? 'Bash' : 'Read';
        assert.equal(fullAccessJudgement(tool, input, w.scope).outcome, 'refused');
        assert.deepEqual(await w.broker.ask(w.id, tool, input), { behavior: 'deny', message: settingsRefusal });
    }
    assert.deepEqual(w.cards, []);
});

// The log.

test('a refusal of the hook is said to the log as refused, by the model, in Full access; what runs is said to nobody', async (t) => {
    const w = await wired(t);
    await hooked(w, 'Bash', { command: 'ls *.md' });
    await hooked(w, 'AskUserQuestion', { questions: [] });
    await hooked(w, 'Bash', { command: 'git status' });
    await hooked(w, 'Read', { file_path: w.scope.protectedFiles[0] });
    assert.deepEqual(w.decided, [{ outcome: 'refused', by: 'model', mode: 'full_access' }]);
});

test('a settled request has one log line, its outcome, by whom and the mode, and nothing of the request', () => {
    const lines: { name: string; fields: Record<string, unknown> }[] = [];
    const log = { info: (name: string, fields: Record<string, unknown>) => void lines.push({ name, fields }), warn: () => undefined, error: () => undefined } as unknown as Logger;
    loggedTurns(log).decided({ outcome: 'refused', by: 'model', mode: 'full_access' });
    loggedTurns(log).decided({ outcome: 'allowed', by: 'person', mode: 'full_access' });
    assert.deepEqual(lines, [
        { name: 'chat_request_decided', fields: { Outcome: 'refused', By: 'model', Mode: 'full_access' } },
        { name: 'chat_request_decided', fields: { Outcome: 'allowed', By: 'person', Mode: 'full_access' } },
    ]);
    assert.equal(templates.chat_request_decided, 'A permission request of the chat was {Outcome} by the {By}; mode {Mode}');
    assert.equal(Object.keys(templates).filter((name) => name.startsWith('chat_request_')).length, 1);
});

// The long wait, on a fake clock.

test('a card of a Full access turn nobody answers waits thirty minutes, then is denied as unanswered, and the turn goes on', async (t) => {
    mock.timers.enable({ apis: ['setTimeout'] });
    t.after(() => mock.timers.reset());
    const cards: BrokerEvent[] = [];
    const decided: Decided[] = [];
    const broker = new PermissionBroker({ emit: (_chat, event) => cards.push(event), decided: (each) => decided.push(each) });
    t.after(() => broker.endAll());
    const scope = homeScope(process.platform === 'win32' || process.platform === 'darwin' ? process.platform : 'linux', process.platform === 'win32' ? 'C:\\Users\\someone' : '/home/someone');
    broker.openTurn('chat_a', 'turn_1', scope, 'full_access');
    assert.equal(broker.hook('turn_1', 'Bash', { command: 'rm -rf build' }, 'toolu_1').decision, 'none');
    let answered: unknown = null;
    void broker.ask('turn_1', 'Bash', { command: 'rm -rf build' }).then((answer) => (answered = answer));
    assert.deepEqual(titles(cards), [fullTitle]);
    mock.timers.tick(30 * 60_000 - 1);
    await Promise.resolve();
    assert.equal(answered, null, 'still waiting a moment before thirty minutes');
    mock.timers.tick(1);
    await Promise.resolve();
    assert.deepEqual(answered, { behavior: 'deny', message: 'Nobody answered within thirty minutes, so nothing was run.' });
    assert.deepEqual(cards.at(-1), { kind: 'permission_gone', request: cards[0]?.kind === 'permission' ? cards[0].request : '' });
    assert.deepEqual(decided, [{ outcome: 'timed_out', by: 'person', mode: 'full_access' }]);
    // The turn goes on: its next tool use is answered as any other.
    assert.equal(broker.hook('turn_1', 'Bash', { command: 'git status' }, 'toolu_2').decision, 'none');
    void broker.ask('turn_1', 'Bash', { command: 'cat notes/*.txt' });
    assert.equal(titles(cards).length, 2, 'a later question is a card again');
});

test('a $\'...\' escape with no digits after it, or past the last code point, is read as the shell keeps it, never a failure', () => {
    const scope = scopes[0] as ChatScope;
    for (const command of ["echo $'\\xZZ'", "echo $'\\u'", "echo $'\\UFFFFFFFF'", "jason approval appro$'\\x've apr_1"]) {
        assert.doesNotThrow(() => judge('Bash', { command }, scope), command);
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, command);
    }
});

test('a judgement that fails is a refusal, for a command and for any other tool, never a run', () => {
    const failing: ChatScope = { ...(scopes[0] as ChatScope), real: () => { throw new Error('no reading'); } };
    for (const [tool, input] of [['Bash', { command: 'cat notes.md' }], ['Read', { file_path: '/home/someone/notes.md' }]] as const) {
        const judged = fullAccessJudgement(tool, input, failing);
        assert.equal(judged.outcome, 'refused', tool);
        assert.match(judged.outcome === 'refused' ? judged.message : '', /could not read what this tool would do/);
    }
});

test('a turn\'s environment names HOME as the folder the judgement takes for home, in place of any other spelling of it', () => {
    const options = { bundleVersion: null, bundle: '/b', data: null, program: 'claude', keyed: false, provider: {} } as const;
    const posixEnv = hostEnvironment({ PATH: '/x', HOME: '/elsewhere' }, { ...options, platform: 'linux', home: '/home/someone' });
    assert.equal(posixEnv.HOME, '/home/someone');
    const windowsEnv = hostEnvironment({ PATH: '/x', Home: '/a', HOME: '/b' }, { ...options, platform: 'win32', home: '/c/Users/someone' });
    assert.deepEqual(Object.keys(windowsEnv).filter((name) => name.toUpperCase() === 'HOME'), ['HOME']);
    assert.equal(windowsEnv.HOME, '/c/Users/someone');
    assert.equal(hostEnvironment({ PATH: '/x' }, { ...options, platform: 'linux' }).HOME, undefined, 'without a home named, nothing is added');
    assert.equal(hostEnvironment({ PATH: '/x', HOME: '/h' }, { ...options, platform: 'linux' }).HOME, '/h', 'and nothing is changed');
});
