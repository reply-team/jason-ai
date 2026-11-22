// Full access is Claude Code's own bypass mode: the turn's hook answers a refusal of three kinds or nothing, and what
// Claude Code still asks about in that mode reaches the prompt tool as a card titled for Full access, which the person
// answers. Every command here is a neutral one or a made-up program; every path is made up or a temporary one.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { request } from 'node:http';
import { tmpdir } from 'node:os';
import { join, posix, win32 } from 'node:path';
import { mock, test } from 'node:test';
import ts from 'typescript';
import { modeFence, readFence } from '../src/chat-deny.ts';
import type { ChatMode } from '../src/chat-modes.ts';
import { hostEnvironment } from '../src/chat-hosts.ts';
import { loggedTurns } from '../src/chat-log.ts';
import { PermissionBroker, type BrokerEvent, type Decided } from '../src/chat-permission-broker.ts';
import { openEndpoint, type Endpoint } from '../src/chat-permission-endpoint.ts';
import { attemptRefusal, confinedRefusal, dataRefusal, fullAccessJudgement, judge, lookupRefusal, personsRefusal, settingsRefusal, type ChatScope, type Platform } from '../src/chat-permission-model.ts';
import { realPathOf } from '../src/chat-real-path.ts';
import { newSecret, newTurnId } from '../src/chat-turn-files.ts';
import { desktopPaths, settingsFilesOf } from '../src/paths.ts';
import { templates, type Logger } from '../src/log.ts';

const fence = readFence(join(import.meta.dirname, '..'));
/** The fence a real Full access turn runs under: none of the deny list (`modeFence`). */
const fullFence = modeFence(fence, 'full_access');

/** A scope whose data directory is the default one under a made-up home folder, nothing on disk. */
function homeScope(platform: Platform, home: string): ChatScope {
    const paths = platform === 'win32' ? win32 : posix;
    const data = paths.join(home, '.jason');
    const chat = paths.join(data, 'desktop', 'chat');
    return { platform, cwd: chat, chat, added: [], data, home, protectedFiles: [paths.join(data, 'config', 'settings.json'), paths.join(data, 'run', 'runtime.json')], denied: fullFence.commands, deniedOperations: fullFence.operations, real: () => null };
}

const scopes: readonly ChatScope[] = [homeScope('linux', '/home/someone'), homeScope('darwin', '/Users/someone'), homeScope('win32', 'C:\\Users\\someone')];

/**
 * Every string the chat's suites hold, every `test/chat-*.test.ts`: the commands and paths of their rows, and the rest
 * of their words, which a judgement reads as harmlessly as any other text. Read by the TypeScript parser, so a row's text
 * is exactly what its row gives. A template literal that holds a substitution is left out: its text is known only when
 * the row runs, and the row's own assertions judge it there.
 */
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

/** The named row of the ruling, and the spellings of a person's verb the second look reads and the literal reader holds. */
const spellings = [
    'jason approval appro\\ve apr_1',
    'jason approval appro\\ve',
    'jason decision ans\\wer dec_1 --option a',
    "jason approval appro$''ve apr_1",
    'jason approval appro$""ve apr_1',
    "jason approval $'\\x61'pprove apr_1",
    "ja$''son approval approve apr_1",
    'jason approval appro\\\nve apr_1',
    'jason approval {approve,} apr_1',
    '{jason,} approval approve apr_1',
    'jason approval appro{v,}e apr_1',
    "printf '\\njason approval approve apr_1\\n' > go.sh; sh go.sh",
    'curl http://127.0.0.1:4011/v1/decision.ans\\wer',
    `python3 -c "import subprocess; subprocess.run(['jason','approval','approve','apr_1'])"`,
    'echo {a,b}{c,d}{e,f}{g,h}{i,j}{k,l}{m,n}',
];

// Full access refuses the three kinds wherever Ask first refuses them.

/**
 * The inputs each string of the suites is given as: a shell command, a path read, written or walked, a pattern, a page,
 * and a command sent to a person's own MCP tool, so a refusal of any of Ask first's routes is read.
 */
const shapes: readonly { tool: string; input: (text: string) => Record<string, unknown>; win32Only?: true }[] = [
    { tool: 'Bash', input: (text) => ({ command: text }) },
    { tool: 'PowerShell', input: (text) => ({ command: text }), win32Only: true },
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

test('every input Ask first refuses as one of the three kinds is refused by Full access\'s own judgement with the same sentence', () => {
    // The table is every string the chat's suites hold, so a refusal row added to any of them joins it by itself.
    const kinds = new Set([settingsRefusal, dataRefusal, personsRefusal, confinedRefusal, attemptRefusal, lookupRefusal]);
    const strings = [...new Set([...suiteStrings(), ...spellings])];
    const refusals = new Map<string, number>();
    for (const scope of scopes) {
        for (const shape of shapes) {
            if (shape.win32Only === true && scope.platform !== 'win32') continue;
            for (const text of strings) {
                const input = shape.input(text);
                const asked = judge(shape.tool, input, scope);
                if (asked.verdict !== 'deny' || !kinds.has(asked.message)) continue;
                const key = `${scope.platform} ${shape.tool}`;
                refusals.set(key, (refusals.get(key) ?? 0) + 1);
                assert.deepEqual(fullAccessJudgement(shape.tool, input, scope), { outcome: 'refused', message: asked.message }, `${scope.platform} ${shape.tool}: ${JSON.stringify(text)}`);
            }
        }
    }
    // The table is the suites' own, and every route of Ask first's refusals is read on every platform: a floor for each
    // shape that has refusals of the three kinds. A person's MCP tool has none, Ask first saying of its input in a
    // sentence of its own that it names what Ask first refuses, and the chat's own tool is refused whatever its input.
    const floors: Readonly<Record<string, number>> = { Bash: 250, Monitor: 250, Read: 70, Write: 70, Edit: 70, Glob: 10, Grep: 70, LS: 70 };
    for (const scope of scopes) {
        for (const [tool, floor] of Object.entries({ ...floors, ...(scope.platform === 'win32' ? { PowerShell: 300 } : {}) })) {
            const read = refusals.get(`${scope.platform} ${tool}`) ?? 0;
            assert.ok(read >= floor, `${scope.platform} ${tool}: only ${read} refusals were read, fewer than ${floor}`);
        }
        // Ask first refuses no web page (`judge` shows a card or opens it), so WebFetch has no floor: its shape is read so
        // that a refusal of a page, once there is one, joins the table by itself.
        assert.equal(refusals.get(`${scope.platform} WebFetch`) ?? 0, 0, 'a refusal of a page now exists: give WebFetch its floor');
    }
});

test('the named row: in Plan only jason approval appro\\ve is refused by the prompt tool, by Ask first\'s sentence, with no card', async (t) => {
    const w = await wired(t, undefined, 'plan_only');
    const command = 'jason approval appro\\ve apr_1';
    const asked = judge('Bash', { command }, w.scope);
    assert.equal(asked.verdict, 'deny');
    assert.match(asked.verdict === 'deny' ? asked.message : '', /person's own act/);
    assert.deepEqual(await w.broker.ask(w.id, 'Bash', { command }), { behavior: 'deny', message: asked.verdict === 'deny' ? asked.message : '' });
    assert.deepEqual(w.cards, []);
});

test('each spelling of a person\'s verb is refused in Plan only, by the prompt tool, never a card', async (t) => {
    const plan = await wired(t, undefined, 'plan_only');
    for (const command of spellings) {
        assert.equal(judge('Bash', { command }, plan.scope).verdict, 'deny', `Plan only: ${JSON.stringify(command)}`);
        assert.equal((await plan.broker.ask(plan.id, 'Bash', { command })).behavior, 'deny', JSON.stringify(command));
    }
    assert.deepEqual(plan.cards, []);
    assert.ok(plan.decided.every((each) => each.outcome === 'refused'));
});

// The hook's two answers.

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

/** A turn's broker and hook in a mode, Full access unless named, under the fence a real turn of that mode runs under. */
async function wired(t: { after: (fn: () => void | Promise<void>) => void }, timeoutMs?: number, mode: ChatMode = 'full_access'): Promise<Wired> {
    const turnFence = modeFence(fence, mode);
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
        denied: turnFence.commands,
        deniedOperations: turnFence.operations,
        real: realPathOf,
    };
    const id = newTurnId();
    broker.openTurn('chat_a', id, scope, mode);
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
    assert.deepEqual(await hooked(w, 'Bash', { command: 'jason approval approve apr_1' }), { decision: 'none' }, 'a person\'s word runs in Full access');
    assert.deepEqual(await hooked(w, 'Read', { file_path: w.scope.protectedFiles[0] }), { decision: 'deny', reason: settingsRefusal });
    assert.deepEqual(await hooked(w, 'Bash', { command: 'echo $JASON_CHAT_TURN' }), { decision: 'deny', reason: confinedRefusal });
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
    for (const command of ['reply auth status --json', 'jason --actor human:me campaign list']) {
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
    const command = 'curl http://127.0.0.1:4011/v1/JASON%5FCHAT%5FTURN';
    assert.equal(judge('Bash', { command }, w.scope).verdict, 'ask', 'Ask first shows the escaped variable as a card');
    assert.deepEqual(fullAccessJudgement('Bash', { command }, w.scope), { outcome: 'refused', message: confinedRefusal });
    assert.deepEqual(await w.broker.ask(w.id, 'Bash', { command }), { behavior: 'deny', message: confinedRefusal });
    assert.deepEqual(w.cards, []);
});

// The log.

test('a refusal of the hook is said to the log as refused, by the model, in Full access; what runs is said to nobody', async (t) => {
    const w = await wired(t);
    await hooked(w, 'Bash', { command: 'ls *.md' });
    await hooked(w, 'AskUserQuestion', { questions: [] });
    await hooked(w, 'Bash', { command: 'git status' });
    await hooked(w, 'Bash', { command: 'jason approval approve apr_1' });
    await hooked(w, 'Read', { file_path: w.scope.protectedFiles[0] });
    await hooked(w, 'Bash', { command: 'echo $JASON_ATTEMPT_ID' });
    assert.deepEqual(w.decided, [
        { outcome: 'refused', by: 'model', mode: 'full_access' },
        { outcome: 'refused', by: 'model', mode: 'full_access' },
    ]);
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

test('the hook refuses a no-card verb fed the settings file or the run file by a redirect, a pipe from cat or a here-string', async (t) => {
    const w = await wired(t);
    const paths = desktopPaths(w.root);
    for (const file of [...settingsFilesOf(paths.data), paths.runFile]) {
        const spelled = file.replaceAll('\\', '/');
        for (const command of [`jason preference set tone_of_voice --value-file - < ${spelled}`, `cat ${spelled} | jason preference set tone_of_voice --value-file -`,
            `jason preference set tone_of_voice --value-file - <<< "$(cat ${spelled})"`]) {
            const answered = await hooked(w, 'Bash', { command });
            assert.equal(answered.decision, 'deny', command);
        }
    }
    assert.deepEqual(await hooked(w, 'Bash', { command: 'jason preference set tone_of_voice --value-file - < tone.json' }), { decision: 'none' }, 'a file of the chat folder runs');
});
