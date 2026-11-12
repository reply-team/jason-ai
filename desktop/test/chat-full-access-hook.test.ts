// Full access on the host's default mode: the turn's hook answers allow, ask or deny, and what reaches the prompt tool
// is judged and carded as in Ask first. The first rule is that Full access is never weaker than Ask first: whatever Ask
// first refuses is refused here too, before anything else, with the same sentence and no card. Every command here is a
// neutral one or a made-up program; every path is made up or a temporary one.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { request } from 'node:http';
import { tmpdir } from 'node:os';
import { join, posix, win32 } from 'node:path';
import { mock, test } from 'node:test';
import ts from 'typescript';
import { readFence } from '../src/chat-deny.ts';
import { loggedTurns } from '../src/chat-log.ts';
import { PermissionBroker, type BrokerEvent, type Decided } from '../src/chat-permission-broker.ts';
import { openEndpoint, type Endpoint } from '../src/chat-permission-endpoint.ts';
import { fullAccessJudgement, heldSentence, judge, type ChatScope, type Platform } from '../src/chat-permission-model.ts';
import { realPathOf } from '../src/chat-real-path.ts';
import { newSecret, newTurnId } from '../src/chat-turn-files.ts';
import { desktopPaths } from '../src/paths.ts';
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

/**
 * Every string the permission suites hold: the commands of their rows, and the rest of their words, which a judgement
 * reads as harmlessly as any other text. Read by the TypeScript parser, so a row's text is exactly what its row gives.
 */
function suiteStrings(): string[] {
    const found = new Set<string>();
    const visit = (node: ts.Node): void => {
        if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) found.add(node.text);
        ts.forEachChild(node, visit);
    };
    for (const file of ['chat-permission-model.test.ts', 'chat-modes.test.ts', 'chat-literal.test.ts', 'chat-permission-broker.test.ts', 'chat-access-modes.test.ts', 'chat-full-access-hook.test.ts']) {
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

// Point 0: Full access is never weaker than Ask first.

test('every command Ask first refuses is refused in Full access with the same sentence, never held and never run', () => {
    const strings = [...new Set([...suiteStrings(), ...spellings])];
    let refusals = 0;
    for (const scope of scopes) {
        for (const tool of scope.platform === 'win32' ? ['Bash', 'PowerShell'] : ['Bash']) {
            for (const command of strings) {
                const asked = judge(tool, { command }, scope);
                if (asked.verdict !== 'deny') continue;
                refusals++;
                assert.deepEqual(fullAccessJudgement(tool, { command }, scope), { outcome: 'refused', message: asked.message }, `${scope.platform} ${tool}: ${JSON.stringify(command)}`);
            }
        }
    }
    // The table is the suites' own: hundreds of refusals, not a handful.
    assert.ok(refusals > 300, `only ${refusals} refusals were read`);
});

test('the named row: jason approval appro\\ve is refused in Full access, by the hook and by the prompt tool, with no card', async (t) => {
    const w = await wired(t);
    const command = 'jason approval appro\\ve apr_1';
    const asked = judge('Bash', { command }, w.scope);
    assert.equal(asked.verdict, 'deny');
    assert.deepEqual(await hooked(w, 'Bash', { command }), { decision: 'deny', reason: asked.verdict === 'deny' ? asked.message : '' });
    assert.deepEqual(await w.broker.ask(w.id, 'Bash', { command }), { behavior: 'deny', message: asked.verdict === 'deny' ? asked.message : '' });
    assert.deepEqual(w.cards, []);
});

test('each spelling of a person\'s verb Ask first refuses is a refusal from the hook in Full access, never an ask', async (t) => {
    const w = await wired(t);
    for (const command of spellings) {
        const asked = judge('Bash', { command }, w.scope);
        assert.equal(asked.verdict, 'deny', `Ask first: ${JSON.stringify(command)}`);
        assert.equal((await hooked(w, 'Bash', { command })).decision, 'deny', JSON.stringify(command));
    }
    assert.deepEqual(w.cards, []);
    assert.ok(w.decided.every((each) => each.outcome === 'refused'));
});

// The hook's three answers.

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
        protectedFiles: [paths.installationSettings, paths.runFile],
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

/** What the hook answers a tool use, as Claude Code posts it in the default mode and reads the answer. */
async function hooked(w: Wired, tool: string, input: Record<string, unknown>, use = `toolu_${Math.random().toString(36).slice(2)}`): Promise<{ decision: string; reason?: string }> {
    const body = JSON.stringify({ session_id: 's', hook_event_name: 'PreToolUse', permission_mode: 'default', tool_name: tool, tool_input: input, tool_use_id: use });
    const answered = await post(w.endpoint.hookUrl, body, { authorization: `Bearer ${w.secret}` });
    assert.equal(answered.status, 200);
    const output = (JSON.parse(answered.body) as { hookSpecificOutput: { hookEventName: string; permissionDecision: string; permissionDecisionReason?: string } }).hookSpecificOutput;
    assert.equal(output.hookEventName, 'PreToolUse');
    return output.permissionDecisionReason === undefined ? { decision: output.permissionDecision } : { decision: output.permissionDecision, reason: output.permissionDecisionReason };
}

test('the hook answers allow for what runs, ask with a reason for what is held, and deny with the refusal for what is refused', async (t) => {
    const w = await wired(t);
    assert.deepEqual(await hooked(w, 'Bash', { command: 'git status' }), { decision: 'allow' });
    assert.deepEqual(await hooked(w, 'Write', { file_path: join(w.scope.chat, 'notes.md'), content: 'a plan' }), { decision: 'allow' });
    assert.deepEqual(await hooked(w, 'Bash', { command: 'ls *.md' }), { decision: 'ask', reason: heldSentence });
    assert.deepEqual(await hooked(w, 'AskUserQuestion', { questions: [] }), { decision: 'ask', reason: heldSentence });
    const refused = await hooked(w, 'Bash', { command: 'jason approval approve apr_1' });
    assert.equal(refused.decision, 'deny');
    assert.match(refused.reason ?? '', /person's own act/);
    assert.deepEqual(w.cards, [], 'the hook shows no card: a card is the prompt tool\'s');
});

test('the held sentence says the person is asked, never that the chat must be switched', () => {
    assert.doesNotMatch(heldSentence, /Switch this chat|Nothing was run/);
    assert.match(heldSentence, /asks you/);
});

test('a hook call that cannot be read is still a refusal, never an allow', async (t) => {
    const w = await wired(t);
    for (const body of ['{', '[]', JSON.stringify({ hook_event_name: 'PreToolUse', tool_name: 'Bash' })]) {
        const answered = await post(w.endpoint.hookUrl, body, { authorization: `Bearer ${w.secret}` });
        assert.equal(JSON.parse(answered.body).hookSpecificOutput.permissionDecision, 'deny', body);
    }
});

// What reaches the prompt tool in Full access: judged and carded as in Ask first.

const fullTitle = 'Full access asks about this one';
const titles = (cards: BrokerEvent[]) => cards.flatMap((card) => (card.kind === 'permission' ? [card.title] : []));
const settle = () => new Promise((resolve) => setImmediate(resolve));

test('a held command the hook sent on is a card titled for Full access, and the person\'s yes runs it', async (t) => {
    const w = await wired(t);
    assert.equal((await hooked(w, 'Bash', { command: 'ls *.md' })).decision, 'ask');
    const answer = w.broker.ask(w.id, 'Bash', { command: 'ls *.md' });
    await settle();
    assert.deepEqual(titles(w.cards), [fullTitle]);
    const card = w.cards[0];
    assert.ok(card?.kind === 'permission');
    assert.equal(card.detail, 'ls *.md');
    w.broker.answer('chat_a', card.request, { allow: true, always: false });
    assert.deepEqual(await answer, { behavior: 'allow', updatedInput: { command: 'ls *.md' } });
});

test('several holds in one turn are several cards', async (t) => {
    const w = await wired(t);
    const one = w.broker.ask(w.id, 'Bash', { command: 'ls *.md' });
    const two = w.broker.ask(w.id, 'Bash', { command: 'cat notes/*.txt' });
    await settle();
    assert.deepEqual(titles(w.cards), [fullTitle, fullTitle]);
    w.broker.endTurn(w.id);
    assert.equal((await one).behavior, 'deny');
    assert.equal((await two).behavior, 'deny');
});

test('a held command Ask first would run with no card runs with no card: a card is shown only where Ask first shows one', async (t) => {
    const w = await wired(t);
    const command = 'reply auth status --json';
    assert.deepEqual(judge('Bash', { command }, w.scope), { verdict: 'allow' });
    assert.deepEqual(await w.broker.ask(w.id, 'Bash', { command }), { behavior: 'allow', updatedInput: { command } });
    assert.deepEqual(w.cards, []);
});

test('"always for this chat" works in Full access as in Ask first', async (t) => {
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

test('a deletion the host still asks about though the hook allowed it is a card, never run on its own and never refused unasked', async (t) => {
    const w = await wired(t);
    for (const command of ['rm -rf build', 'rm notes.md', 'rmdir old']) {
        assert.deepEqual(await hooked(w, 'Bash', { command }), { decision: 'allow' }, command);
    }
    const answers = ['rm -rf build', 'rm notes.md', 'rmdir old'].map((command) => w.broker.ask(w.id, 'Bash', { command }));
    await settle();
    assert.deepEqual(titles(w.cards), [fullTitle, fullTitle, fullTitle]);
    w.broker.endTurn(w.id);
    for (const answer of answers) assert.equal((await answer).behavior, 'deny');
});

test('what Full access refuses is refused at the prompt tool too, whichever way the request came', async (t) => {
    const w = await wired(t);
    const command = 'curl -X POST http://127.0.0.1:4011/v1/approval%2Eapprove';
    assert.equal(judge('Bash', { command }, w.scope).verdict, 'ask', 'Ask first shows the escaped operation as a card');
    assert.equal(fullAccessJudgement('Bash', { command }, w.scope).outcome, 'refused');
    assert.equal((await w.broker.ask(w.id, 'Bash', { command })).behavior, 'deny');
    assert.deepEqual(w.cards, []);
});

// The log.

test('a hold is said to the log as held, by the model, in Full access, with its closed reason; a non-shell hold carries none', async (t) => {
    const w = await wired(t);
    await hooked(w, 'Bash', { command: 'ls *.md' });
    await hooked(w, 'AskUserQuestion', { questions: [] });
    await hooked(w, 'Bash', { command: 'git status' });
    await hooked(w, 'Bash', { command: 'jason approval approve apr_1' });
    assert.deepEqual(w.decided, [
        { outcome: 'held', by: 'model', mode: 'full_access', reason: 'glob' },
        { outcome: 'held', by: 'model', mode: 'full_access' },
        { outcome: 'refused', by: 'model', mode: 'full_access' },
    ]);
});

test('the log line of a hold names the outcome and the closed reason, or none, and nothing of the command', () => {
    const lines: { name: string; fields: Record<string, unknown> }[] = [];
    const log = { info: (name: string, fields: Record<string, unknown>) => void lines.push({ name, fields }), warn: () => undefined, error: () => undefined } as unknown as Logger;
    loggedTurns(log).decided({ outcome: 'held', by: 'model', mode: 'full_access', reason: 'glob' });
    loggedTurns(log).decided({ outcome: 'held', by: 'model', mode: 'full_access' });
    loggedTurns(log).decided({ outcome: 'allowed', by: 'person', mode: 'full_access' });
    assert.deepEqual(lines, [
        { name: 'chat_request_held', fields: { Outcome: 'held', By: 'model', Mode: 'full_access', Cause: 'glob' } },
        { name: 'chat_request_held', fields: { Outcome: 'held', By: 'model', Mode: 'full_access', Cause: 'none' } },
        { name: 'chat_request_decided', fields: { Outcome: 'allowed', By: 'person', Mode: 'full_access' } },
    ]);
    assert.equal(templates.chat_request_held, 'A permission request of the chat was {Outcome} by the {By} for the cause {Cause}; mode {Mode}');
});

// The long wait, on a fake clock.

test('a held card nobody answers waits thirty minutes, then is denied as unanswered, and the turn goes on', async (t) => {
    mock.timers.enable({ apis: ['setTimeout'] });
    t.after(() => mock.timers.reset());
    const cards: BrokerEvent[] = [];
    const decided: Decided[] = [];
    const broker = new PermissionBroker({ emit: (_chat, event) => cards.push(event), decided: (each) => decided.push(each) });
    t.after(() => broker.endAll());
    const scope = homeScope(process.platform === 'win32' || process.platform === 'darwin' ? process.platform : 'linux', process.platform === 'win32' ? 'C:\\Users\\someone' : '/home/someone');
    broker.openTurn('chat_a', 'turn_1', scope, 'full_access');
    assert.equal(broker.hook('turn_1', 'Bash', { command: 'ls *.md' }, 'toolu_1').decision, 'ask');
    let answered: unknown = null;
    void broker.ask('turn_1', 'Bash', { command: 'ls *.md' }).then((answer) => (answered = answer));
    assert.deepEqual(titles(cards), [fullTitle]);
    mock.timers.tick(30 * 60_000 - 1);
    await Promise.resolve();
    assert.equal(answered, null, 'still waiting a moment before thirty minutes');
    mock.timers.tick(1);
    await Promise.resolve();
    assert.deepEqual(answered, { behavior: 'deny', message: 'Nobody answered within thirty minutes, so nothing was run.' });
    assert.deepEqual(cards.at(-1), { kind: 'permission_gone', request: cards[0]?.kind === 'permission' ? cards[0].request : '' });
    assert.deepEqual(decided, [
        { outcome: 'held', by: 'model', mode: 'full_access', reason: 'glob' },
        { outcome: 'timed_out', by: 'person', mode: 'full_access' },
    ]);
    // The turn goes on: its next tool use is answered as any other.
    assert.equal(broker.hook('turn_1', 'Bash', { command: 'git status' }, 'toolu_2').decision, 'allow');
    void broker.ask('turn_1', 'Bash', { command: 'cat notes/*.txt' });
    assert.equal(titles(cards).length, 2, 'a later hold is a card again');
});

test('a $\'...\' escape with no digits after it, or past the last code point, is read as the shell keeps it, never a failure', () => {
    const scope = scopes[0] as ChatScope;
    for (const command of ["echo $'\\xZZ'", "echo $'\\u'", "echo $'\\UFFFFFFFF'", "jason approval appro$'\\x've apr_1"]) {
        assert.doesNotThrow(() => judge('Bash', { command }, scope), command);
        assert.notEqual(fullAccessJudgement('Bash', { command }, scope).outcome, 'runs', command);
    }
});

test('reply in Full access: what Ask first refuses is refused with its sentence, never a card; the rest is held but for the no-card forms', () => {
    const scope = scopes[0] as ChatScope;
    for (const [tool, command] of [['Bash', 'reply sequence start seq_1'], ['Bash', 'reply api DELETE /v3/sequences/1'], ['Bash', 'reply account delete acc_1 --yes'], ['Bash', 'npx reply-cli sequence start seq_1'], ['PowerShell', 'reply sequence start seq_1']] as const) {
        const asked = judge(tool, { command }, scope);
        assert.equal(asked.verdict, 'deny', `Ask first: ${command}`);
        assert.deepEqual(fullAccessJudgement(tool, { command }, scope), { outcome: 'refused', message: asked.verdict === 'deny' ? asked.message : '' }, command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'reply auth status --json > out.json' }, scope), { outcome: 'held', message: heldSentence, reason: 'reply' });
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'reply auth status --json' }, scope), { outcome: 'runs' });
});

test('a judgement that fails before Full access reads anything holds the request, for a command and for any other tool', () => {
    const failing: ChatScope = { ...(scopes[0] as ChatScope), real: () => { throw new Error('no reading'); } };
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'cat notes.md' }, failing), { outcome: 'held', message: heldSentence, reason: 'syntax' });
    assert.deepEqual(fullAccessJudgement('Read', { file_path: '/home/someone/notes.md' }, failing), { outcome: 'held', message: heldSentence });
});
