// The chat's turns end to end against stub hosts on a PATH of their own: the arguments and the environment a host is
// started with, the person's words on its standard input, what it says on the page, the cards its questions make and
// their answers, a stop, a card nobody answers, Codex, and the hosts that are refused before anything starts.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readdirSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent, type ChatOptions, type TurnEnded } from '../src/chat.ts';
import { instructionsFile, readFence } from '../src/chat-deny.ts';
import type { Decided } from '../src/chat-permission-broker.ts';
import { bench, type Bench, type Recorded } from './harness.ts';

const fence = readFence(join(import.meta.dirname, '..'));
const words = 'Which campaigns are waiting on me?';

interface Rig {
    b: Bench;
    chat: Chat;
    bundle: string;
    events: Map<string, ChatEvent[]>;
    ended: TurnEnded[];
    decided: Decided[];
    started: { host: string; resumed: boolean }[];
    keys: Record<string, string>;
    /** Sends and waits for the turn's `done`. */
    turn(chatId: string, text: string, options: unknown): Promise<ChatEvent[]>;
    /** Resolves once the chat has said an event the predicate takes. */
    until(chatId: string, take: (event: ChatEvent) => boolean): Promise<ChatEvent>;
    hosts(): Recorded[];
}

function rig(t: { after(fn: () => void): void }, options: { programs?: string[]; env?: Record<string, string>; chat?: Partial<ChatOptions>; before?: (b: Bench) => void } = {}): Rig {
    // The bench's folder is removed only after every turn has been stopped and its host has exited.
    const removals: (() => void)[] = [];
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: options.programs ?? ['claude', 'codex', 'gemini'], env: { JASON_Plugins__Grants__slack__Env__0: 'SLACK_PROXY', CLAUDE_AUTO_BACKGROUND_TASKS: '1', ...options.env } });
    const bundle = join(b.root, 'bundle');
    mkdirSync(bundle, { recursive: true });
    mkdirSync(join(b.paths.data, 'config'), { recursive: true });
    mkdirSync(join(b.paths.data, 'logs'), { recursive: true });
    const events = new Map<string, ChatEvent[]>();
    const waiters: { chatId: string; take: (event: ChatEvent) => boolean; resolve: (event: ChatEvent) => void }[] = [];
    const ended: TurnEnded[] = [];
    const decided: Decided[] = [];
    const started: { host: string; resumed: boolean }[] = [];
    const keys: Record<string, string> = {};
    options.before?.(b);
    const chat = new Chat({
        paths: b.paths,
        env: b.env,
        bundle,
        dataNamed: true,
        cli: b.cli,
        keyFor: (id) => keys[id],
        fence: () => fence,
        emit(chatId, event) {
            const said = events.get(chatId) ?? [];
            said.push(event);
            events.set(chatId, said);
            for (const waiter of [...waiters]) {
                if (waiter.chatId === chatId && waiter.take(event)) {
                    waiters.splice(waiters.indexOf(waiter), 1);
                    waiter.resolve(event);
                }
            }
        },
        started: (host, resumed) => void started.push({ host, resumed }),
        ended: (turn) => void ended.push(turn),
        decided: (each) => void decided.push(each),
        ...options.chat,
    });
    t.after(async () => {
        const running = [...events.keys()].filter((chatId) => chat.isRunning(chatId));
        chat.closeAll();
        await settle(() => running.every((chatId) => !chat.isRunning(chatId)));
        for (const remove of removals) remove();
    });
    const until = (chatId: string, take: (event: ChatEvent) => boolean) =>
        new Promise<ChatEvent>((resolve, reject) => {
            const timer = setTimeout(() => reject(new Error(`chat ${chatId} never said what was waited for; it said ${JSON.stringify(events.get(chatId) ?? [])}`)), 20_000);
            waiters.push({ chatId, take, resolve: (event) => { clearTimeout(timer); resolve(event); } });
        });
    return {
        b,
        chat,
        bundle,
        events,
        ended,
        decided,
        started,
        keys,
        until,
        async turn(chatId, text, sendOptions) {
            const from = events.get(chatId)?.length ?? 0;
            const done = until(chatId, (event) => event.kind === 'done');
            await chat.send(chatId, text, sendOptions);
            await done;
            return (events.get(chatId) ?? []).slice(from);
        },
        hosts: () => b.record().filter((each) => (each.name === 'claude' || each.name === 'codex') && each.args[0] !== '--version'),
    };
}

const plan = (r: Rig, steps: unknown[]) => {
    r.b.env.STUB_HOST_PLAN = JSON.stringify(steps);
};
const texts = (events: ChatEvent[]) => events.flatMap((event) => (event.kind === 'text' ? [event.text] : []));
const kinds = (events: ChatEvent[]) => events.map((event) => event.kind);
const doneOf = (events: ChatEvent[]) => events.at(-1);
const same = (a: string, b: string) => realpathSync.native(a).toLowerCase() === realpathSync.native(b).toLowerCase();
const flagValue = (args: string[], flag: string) => args[args.indexOf(flag) + 1] ?? '';
/** The elements after a flag that takes a list, up to the next flag. */
const flagValues = (args: string[], flag: string) => {
    const after = args.slice(args.indexOf(flag) + 1);
    const end = after.findIndex((arg) => arg.startsWith('-'));
    return end < 0 ? after : after.slice(0, end);
};
const gone = (pid: number) => {
    try {
        process.kill(pid, 0);
        return false;
    } catch {
        return true;
    }
};
async function settle(check: () => boolean, ms = 10_000): Promise<boolean> {
    const until = Date.now() + ms;
    while (Date.now() < until) {
        if (check()) return true;
        await new Promise((resolve) => setTimeout(resolve, 50));
    }
    return check();
}

test('a turn with nothing to ask starts Claude Code in the chat folder with the words on stdin, and ends with no error', async (t) => {
    const r = rig(t);
    const said = await r.turn('c1', words, { host: 'claude' });
    assert.deepEqual(kinds(said), ['text', 'done']);
    assert.deepEqual(texts(said), [`heard ${words.length} characters`]);
    assert.deepEqual(doneOf(said), { kind: 'done', error: null });

    const [host] = r.hosts();
    assert.ok(host);
    assert.equal(host.stdin, words);
    assert.ok(host.args.every((arg) => !arg.includes(words)), 'the words are never on the command line');
    assert.ok(same(host.cwd, r.b.paths.chat), `started in ${host.cwd}`);
    for (const flag of ['-p', '--strict-mcp-config', '--permission-prompt-tool', '--mcp-config', '--setting-sources', '--settings', '--allowedTools', '--disallowedTools']) assert.ok(host.args.includes(flag), flag);
    assert.ok(!host.args.includes('--bare'));
    assert.ok(!host.args.includes('--resume'), 'a first turn resumes nothing');
    assert.ok(!host.args.includes('--add-dir'), 'a turn with no campaign adds no folder');
    const fence = flagValues(host.args, '--disallowedTools');
    assert.ok(fence.includes('Bash(jason approval approve:*)') && fence.includes('Bash(jason -*)'), fence.join(' '));
    assert.ok(fence.some((rule) => rule.endsWith('/config/**)')) && fence.some((rule) => rule.endsWith('/logs/**)')), 'the data directory outside the chat is fenced off');
    const config = flagValue(host.args, '--mcp-config');
    assert.ok(same(join(config, '..'), r.b.paths.turns), 'the turn file is in desktop/turns, outside the chat folder');
    assert.equal(existsSync(config), false, 'the turn file is gone once the turn is over');
    assert.deepEqual(readdirSync(r.b.paths.desktop).sort(), ['chat', 'turns'], 'nothing of the turn is left beside the chat folder');
    assert.deepEqual(readdirSync(r.b.paths.turns), [], 'nothing of the turn is left in turns/');
    assert.deepEqual(readdirSync(r.b.paths.chat).sort(), ['AGENTS.md', 'CLAUDE.md'], 'the turn writes nothing into the chat folder but the instruction text');
    assert.equal(existsSync(join(r.b.root, 'harness')), false, 'nothing reaches the harness root');
    assert.deepEqual(r.started, [{ host: 'claude', resumed: false }]);
    assert.equal(r.ended.length, 1);
    assert.equal(r.ended[0]?.exit, 0);
    assert.equal(r.ended[0]?.requests, 0);
});

test('the host is started with the bundle first on its PATH and the data directory, and never the plugins configuration or backgrounding', async (t) => {
    const r = rig(t);
    await r.turn('c1', words, { host: 'claude' });
    const [host] = r.hosts();
    assert.ok(host);
    const names = Object.keys(host.env);
    const path = Object.entries(host.env).filter(([name]) => name.toUpperCase() === 'PATH');
    assert.equal(path.length, 1, 'one PATH');
    assert.ok(path[0]?.[1].toLowerCase().startsWith(r.bundle.toLowerCase()), path[0]?.[1]);
    assert.equal(host.env.JASON_DATA_DIR, r.b.paths.data);
    assert.ok(!names.some((name) => name.toUpperCase().startsWith('JASON_PLUGINS__')), names.join(' '));
    assert.ok(!names.some((name) => name.toUpperCase() === 'CLAUDE_AUTO_BACKGROUND_TASKS'));
});

test('a request the model settles makes no card, and the host reads the answer', async (t) => {
    const r = rig(t);
    plan(r, [{ ask: { tool: 'Read', input: { file_path: join(r.b.paths.chat, 'notes.md') } } }, { ask: { tool: 'Bash', input: { command: 'jason approval approve apr_1' } } }]);
    const said = await r.turn('c1', words, { host: 'claude' });
    assert.ok(!kinds(said).includes('permission'), JSON.stringify(said));
    const answers = texts(said).filter((text) => text.startsWith('decision'));
    assert.equal(answers[0], 'decision allow');
    assert.match(answers[1] ?? '', /^decision deny: /);
    assert.deepEqual(r.decided, [{ outcome: 'allowed', by: 'model' }, { outcome: 'refused', by: 'model' }]);
    assert.equal(r.ended[0]?.requests, 2);
    assert.deepEqual(doneOf(said), { kind: 'done', error: null });
});

test('a card the person allows lets the tool run, and a card the person refuses tells the host no', async (t) => {
    const r = rig(t);
    plan(r, [{ ask: { tool: 'Bash', input: { command: 'git status' } } }]);
    for (const allow of [true, false]) {
        const card = r.until('c1', (event) => event.kind === 'permission');
        const done = r.until('c1', (event) => event.kind === 'done');
        await r.chat.send('c1', words, { host: 'claude' });
        const asked = await card;
        assert.ok(asked.kind === 'permission' && asked.remember === 'git status \u2026');
        r.chat.answer('c1', asked.request, { allow, always: false });
        await done;
        const answer = texts(r.events.get('c1') ?? []).filter((text) => text.startsWith('decision')).at(-1);
        if (allow) assert.equal(answer, 'decision allow');
        else assert.match(answer ?? '', /^decision deny: The person said no/);
    }
    assert.deepEqual(r.decided, [{ outcome: 'allowed', by: 'person' }, { outcome: 'refused', by: 'person' }]);
});

test('"always" makes no card for the same command in the next turn of that chat, which resumes its session', async (t) => {
    const r = rig(t);
    plan(r, [{ ask: { tool: 'Bash', input: { command: 'git status' } } }]);
    const card = r.until('c1', (event) => event.kind === 'permission');
    const done = r.until('c1', (event) => event.kind === 'done');
    await r.chat.send('c1', words, { host: 'claude' });
    const asked = await card;
    assert.ok(asked.kind === 'permission');
    r.chat.answer('c1', asked.request, { allow: true, always: true });
    await done;

    const again = await r.turn('c1', words, { host: 'claude' });
    assert.ok(!kinds(again).includes('permission'), JSON.stringify(again));
    assert.ok(texts(again).includes('decision allow'));
    const second = r.hosts()[1];
    assert.equal(flagValue(second?.args ?? [], '--resume'), 'stub-session-1');
    assert.deepEqual(r.started.map((each) => each.resumed), [false, true]);

    const other = r.until('c2', (event) => event.kind === 'permission');
    const otherDone = r.until('c2', (event) => event.kind === 'done');
    await r.chat.send('c2', words, { host: 'claude' });
    const asks = await other;
    assert.ok(asks.kind === 'permission', 'another chat is asked again');
    r.chat.answer('c2', asks.request, { allow: false, always: false });
    await otherDone;
});

test('a stop ends the host and what it started, then the turn is done with no error', async (t) => {
    const r = rig(t);
    const pidFile = join(r.b.root, 'grandchild.pid');
    plan(r, [{ wait: pidFile }]);
    const done = r.until('c1', (event) => event.kind === 'done');
    await r.chat.send('c1', words, { host: 'claude' });
    assert.ok(await settle(() => existsSync(pidFile) && readFileSync(pidFile, 'utf8') !== ''), 'the host started its own process');
    const grandchild = Number(readFileSync(pidFile, 'utf8'));
    assert.equal(gone(grandchild), false);
    assert.equal(r.chat.isRunning('c1'), true);
    r.chat.stop('c1');
    assert.deepEqual(await done, { kind: 'done', error: null });
    assert.equal(r.chat.isRunning('c1'), false);
    assert.ok(await settle(() => gone(grandchild)), 'the process the host started is gone too');
    assert.deepEqual(readdirSync(r.b.paths.turns), []);
});

test('a stop while a card waits takes the card away before the turn is done', async (t) => {
    const r = rig(t);
    plan(r, [{ ask: { tool: 'Bash', input: { command: 'git status' } } }]);
    const card = r.until('c1', (event) => event.kind === 'permission');
    const done = r.until('c1', (event) => event.kind === 'done');
    await r.chat.send('c1', words, { host: 'claude' });
    const asked = await card;
    assert.ok(asked.kind === 'permission');
    r.chat.stop('c1');
    assert.deepEqual(await done, { kind: 'done', error: null });
    const tail = kinds(r.events.get('c1') ?? []).slice(-2);
    assert.deepEqual(tail, ['permission_gone', 'done']);
    r.chat.answer('c1', asked.request, { allow: true, always: false });
    assert.deepEqual(r.events.get('c1')?.at(-1), { kind: 'permission_gone', request: asked.request }, 'a late answer changes nothing');
});

test('a card nobody answers is refused when its window ends, and the card goes', async (t) => {
    const r = rig(t, { chat: { answerWindowMs: 300 } });
    plan(r, [{ ask: { tool: 'Bash', input: { command: 'git status' } } }]);
    const said = await r.turn('c1', words, { host: 'claude' });
    assert.deepEqual(kinds(said).filter((kind) => kind.startsWith('permission')), ['permission', 'permission_gone']);
    assert.match(texts(said).at(-1) ?? '', /^decision deny: Nobody answered/);
    assert.deepEqual(r.decided, [{ outcome: 'timed_out', by: 'person' }]);
    assert.deepEqual(doneOf(said), { kind: 'done', error: null });
});

test('a Codex turn runs read-only with the words on stdin, shows its steps, and resumes its thread', async (t) => {
    const r = rig(t);
    const said = await r.turn('c1', words, { host: 'codex' });
    assert.deepEqual(said, [
        { kind: 'step', text: 'Ran a jason command', detail: 'jason campaign list --json' },
        { kind: 'text', text: `heard ${words.length} characters` },
        { kind: 'done', error: null },
    ]);
    await r.turn('c1', words, { host: 'codex' });
    const [first, second] = r.hosts();
    assert.deepEqual(first?.args, ['exec', '--json', '--sandbox', 'read-only', '--skip-git-repo-check', '-']);
    assert.deepEqual(second?.args, ['exec', '--json', '--sandbox', 'read-only', '--skip-git-repo-check', 'resume', '0199a213-81c0-7800-8aa1-bbab2a035a53', '-']);
    assert.equal(first?.stdin, words);
    assert.ok(same(first?.cwd ?? '', r.b.paths.chat));
    assert.deepEqual(readdirSync(r.b.paths.desktop), ['chat'], 'a Codex turn writes no turn file');
});

test('a Codex turn that reported an error and then completed ends with no error', async (t) => {
    const r = rig(t);
    plan(r, [{ recover: 'Reconnecting... 1/5' }, { say: 'back' }, { recover: 'Reconnecting... 2/5' }]);
    const said = await r.turn('c1', words, { host: 'codex' });
    assert.deepEqual(doneOf(said), { kind: 'done', error: null });
    assert.ok(texts(said).includes('back'));
});

test('a turn whose file cannot be removed still ends: it is no longer running, its end is told, and the chat sends again', async (t) => {
    const r = rig(t);
    plan(r, [{ jam: true }]);
    const said = await r.turn('c1', words, { host: 'claude' });
    assert.deepEqual(doneOf(said), { kind: 'done', error: null });
    assert.equal(r.chat.isRunning('c1'), false);
    assert.equal(r.ended.length, 1);
    const left = readdirSync(r.b.paths.turns).filter((name) => name.startsWith('turn-'));
    assert.equal(left.length, 1, 'the folder the host put there stays, for the next start to remove');
    plan(r, []);
    assert.deepEqual(doneOf(await r.turn('c1', words, { host: 'claude' })), { kind: 'done', error: null });
    assert.equal(r.ended.length, 2);
});

test('a turn resumes only on the host that named the session', async (t) => {
    const r = rig(t);
    await r.turn('c1', words, { host: 'claude' });
    await r.turn('c1', words, { host: 'codex' });
    await r.turn('c1', words, { host: 'claude' });
    const [, codex, claude] = r.hosts();
    assert.ok(!codex?.args.includes('resume'));
    assert.ok(!claude?.args.includes('--resume'));
});

test('a failed turn says the host\'s last words: an error result, a Codex failure, an exit with no result', async (t) => {
    const r = rig(t);
    const cases: [string, unknown[], string][] = [
        ['claude', [{ end: 'error', text: 'Not logged in. Please run /login.' }], 'Not logged in. Please run /login.'],
        ['codex', [{ end: 'error', text: 'You are not signed in. Run codex login.' }], 'You are not signed in. Run codex login.'],
        ['claude', [{ fail: { stderr: 'Invalid API key', code: 3 } }], 'Invalid API key'],
        ['claude', [{ end: 'no_result' }], 'Claude Code ended without an answer.'],
    ];
    for (const [host, steps, error] of cases) {
        plan(r, steps);
        const said = await r.turn(`c-${host}-${error.length}`, words, { host });
        assert.deepEqual(doneOf(said), { kind: 'done', error }, `${host} ${JSON.stringify(steps)}`);
    }
    assert.deepEqual(readdirSync(r.b.paths.turns), []);
});

test('a host that cannot drive the chat, one it does not know, one not on the PATH and a provider with no key are refused before anything starts', async (t) => {
    const r = rig(t, { programs: ['claude', 'gemini'] });
    const cases: [unknown, string][] = [
        ['gemini', 'Gemini CLI runs campaign work, not the chat yet. Choose Claude Code or Codex for the chat.'],
        ['vim', 'That is not an agent host this desktop knows. Choose Claude Code or Codex for the chat.'],
        [undefined, 'That is not an agent host this desktop knows. Choose Claude Code or Codex for the chat.'],
        ['codex', 'Codex cannot be started: codex is not on the PATH.'],
        ['deepseek', 'No key is stored for DeepSeek. Add it on the Connect card, then send again.'],
    ];
    for (const [host, error] of cases) {
        const said = await r.turn('c1', words, { host });
        assert.deepEqual(said, [{ kind: 'done', error }], String(host));
    }
    assert.deepEqual(r.b.record().filter((each) => each.args[0] !== '--version'), [], 'no host and no jason verb ran');
    assert.deepEqual(r.started, []);
});

test('what a send gives is checked before a host starts', async (t) => {
    const r = rig(t);
    const cases: [unknown, unknown][] = [
        ['', { host: 'claude' }],
        ['   ', { host: 'claude' }],
        [42, { host: 'claude' }],
        ['x'.repeat(1_000_001), { host: 'claude' }],
        [words, { host: 'claude', model: '--dangerously-skip-permissions' }],
        [words, { host: 'claude', model: 7 }],
        [words, { host: 'claude', campaignId: '../cmp_1' }],
        [words, { host: 'claude', campaignId: 'cmp_1 --add-dir /' }],
    ];
    for (const [text, options] of cases) {
        const said = await r.turn('c1', text as string, options);
        assert.equal(said.length, 1, JSON.stringify(options));
        assert.ok(said[0]?.kind === 'done' && typeof said[0].error === 'string');
    }
    assert.deepEqual(r.hosts(), []);
});

test('a send with a chat id the desktop does not take rejects with an Error the page shows, and starts nothing', async (t) => {
    const r = rig(t);
    for (const chatId of ['', 'c 1', 'c1/../x', 'x'.repeat(201), 42, null]) {
        await assert.rejects(r.chat.send(chatId, words, { host: 'claude' }), (error: unknown) => error instanceof Error && error.message === 'That is not a chat id the desktop takes.', String(chatId));
    }
    assert.deepEqual([...r.events.keys()], []);
    assert.deepEqual(r.hosts(), []);
});

test('a second send while the chat answers is refused, and the first turn goes on', async (t) => {
    const r = rig(t);
    const pidFile = join(r.b.root, 'grandchild.pid');
    plan(r, [{ wait: pidFile }]);
    await r.chat.send('c1', words, { host: 'claude' });
    await r.chat.send('c1', words, { host: 'claude' });
    assert.deepEqual(r.events.get('c1')?.filter((event) => event.kind === 'done'), [{ kind: 'done', error: 'This chat is still answering. Wait for it to finish, or stop it, then send again.' }]);
    assert.equal(r.chat.isRunning('c1'), true);
    // A stop before the host has recorded what it was asked would kill it unrecorded.
    assert.equal(await settle(() => r.hosts().length === 1), true);
    const done = r.until('c1', (event) => event.kind === 'done' && event.error === null);
    r.chat.stop('c1');
    await done;
    assert.equal(r.hosts().length, 1);
});

test('a send made the moment a turn says it is done starts the next turn, so the page\'s waiting message is never refused as busy', async (t) => {
    const r = rig(t);
    let next: Promise<void> | null = null;
    // The page sends its waiting message when it hears the end; here it does so inside the very call that says it.
    const handed = r.until('c1', (event) => {
        if (event.kind !== 'done' || next !== null) return false;
        next = r.chat.send('c1', 'And then?', { host: 'claude' });
        return true;
    });
    let ends = 0;
    const second = r.until('c1', (event) => event.kind === 'done' && ++ends === 2);
    await r.chat.send('c1', words, { host: 'claude' });
    await handed;
    await next;
    await second;
    assert.deepEqual(r.events.get('c1')?.filter((event) => event.kind === 'done'), [{ kind: 'done', error: null }, { kind: 'done', error: null }]);
    assert.deepEqual(r.hosts().map((host) => host.stdin), [words, 'And then?']);
});

test('a campaign turn adds the campaign\'s and its project\'s folders when they are recorded and on this disk', async (t) => {
    const r = rig(t);
    const campaign = join(r.b.root, 'harness-copy', 'campaign');
    const project = join(r.b.root, 'harness-copy', 'project');
    mkdirSync(campaign, { recursive: true });
    mkdirSync(project, { recursive: true });
    r.b.env.STUB_CAMPAIGN_FOLDER = campaign;
    r.b.env.STUB_PROJECT_FOLDER = project;
    await r.turn('c1', words, { host: 'claude', campaignId: 'cmp_01ABC' });
    const asked = r.b.record().filter((each) => each.name === 'jason').map((each) => each.args.join(' '));
    assert.deepEqual(asked, ['campaign get cmp_01ABC', 'project get prj_01STUB']);
    const added = r.hosts()[0]?.args.flatMap((arg, i, all) => (all[i - 1] === '--add-dir' ? [arg] : []));
    assert.deepEqual(added, [campaign, project]);

    r.b.env.STUB_CAMPAIGN_FOLDER = join(r.b.root, 'not-there');
    r.b.env.STUB_PROJECT_FOLDER = 'relative/folder';
    await r.turn('c2', words, { host: 'claude', campaignId: 'cmp_01ABC' });
    assert.ok(!r.hosts()[1]?.args.includes('--add-dir'), 'a folder not on this disk or not absolute is never added');

    r.b.env.STUB_CAMPAIGN_MISSING = 'yes';
    const said = await r.turn('c3', words, { host: 'claude', campaignId: 'cmp_01ABC' });
    assert.deepEqual(doneOf(said), { kind: 'done', error: null }, 'a campaign the runtime does not know adds nothing and stops nothing');
    assert.ok(!r.hosts()[2]?.args.includes('--add-dir'));
});

test('a provider\'s key reaches the host\'s environment only, and an Anthropic-compatible endpoint takes its model there', async (t) => {
    const r = rig(t);
    r.keys.deepseek = 'sk-planted-deepseek';
    r.keys['claude-api-key'] = 'sk-planted-anthropic';
    await r.turn('c1', words, { host: 'deepseek', model: 'deepseek-v4-flash' });
    await r.turn('c2', words, { host: 'claude-api-key', model: 'sonnet' });
    const [compatible, anthropic] = r.hosts();
    assert.equal(compatible?.env.ANTHROPIC_AUTH_TOKEN, 'sk-planted-deepseek');
    assert.equal(compatible?.env.ANTHROPIC_BASE_URL, 'https://api.deepseek.com/anthropic');
    assert.equal(compatible?.env.ANTHROPIC_MODEL, 'deepseek-v4-flash');
    assert.ok(!compatible?.args.includes('--model'));
    assert.equal(anthropic?.env.ANTHROPIC_API_KEY, 'sk-planted-anthropic');
    assert.equal(flagValue(anthropic?.args ?? [], '--model'), 'sonnet');
    for (const host of [compatible, anthropic]) assert.ok(!host?.args.some((arg) => arg.includes('sk-planted')), 'a key is never on the command line');
});

test('a key provider\'s turn is started without the provider the desktop inherited, and a plain turn with it; every Claude Code turn scrubs', async (t) => {
    const r = rig(t);
    Object.assign(r.b.env, { ANTHROPIC_API_KEY: 'sk-inherited', CLAUDE_CODE_USE_BEDROCK: '1', ANTHROPIC_DEFAULT_HAIKU_MODEL: 'inherited-haiku', OPENAI_BASE_URL: 'https://inherited.example/v1' });
    r.keys.deepseek = 'sk-planted-deepseek';
    r.keys['codex-api-key'] = 'sk-planted-openai';
    await r.turn('c1', words, { host: 'deepseek' });
    await r.turn('c2', words, { host: 'claude' });
    await r.turn('c3', words, { host: 'codex-api-key' });
    await r.turn('c4', words, { host: 'codex' });
    const [deepseek, claude, openai, codex] = r.hosts();
    assert.equal(deepseek?.env.ANTHROPIC_API_KEY, undefined);
    assert.equal(deepseek?.env.CLAUDE_CODE_USE_BEDROCK, undefined);
    assert.equal(deepseek?.env.ANTHROPIC_DEFAULT_HAIKU_MODEL, 'deepseek-v4-pro');
    assert.equal(claude?.env.ANTHROPIC_API_KEY, 'sk-inherited');
    assert.equal(claude?.env.CLAUDE_CODE_USE_BEDROCK, '1');
    assert.equal(openai?.env.OPENAI_BASE_URL, undefined);
    assert.equal(openai?.env.OPENAI_API_KEY, 'sk-planted-openai');
    assert.equal(codex?.env.OPENAI_BASE_URL, 'https://inherited.example/v1');
    for (const host of [deepseek, claude]) assert.equal(host?.env.CLAUDE_CODE_SUBPROCESS_ENV_SCRUB, '1');
    for (const host of [deepseek, claude]) assert.equal(host?.env.CLAUDE_CODE_DISABLE_BACKGROUND_TASKS, '1');
});

test('closing the window stops every turn', async (t) => {
    const r = rig(t);
    const pidFile = join(r.b.root, 'grandchild.pid');
    plan(r, [{ wait: pidFile }]);
    const done = r.until('c1', (event) => event.kind === 'done');
    await r.chat.send('c1', words, { host: 'claude' });
    assert.ok(await settle(() => existsSync(pidFile) && readFileSync(pidFile, 'utf8') !== ''));
    r.chat.closeAll();
    assert.deepEqual(await done, { kind: 'done', error: null });
    assert.ok(await settle(() => gone(Number(readFileSync(pidFile, 'utf8')))));
});

test('closing the window removes every running turn\'s file before it returns, since the desktop may quit at once', async (t) => {
    const r = rig(t);
    const pidFile = join(r.b.root, 'grandchild.pid');
    plan(r, [{ wait: pidFile }]);
    const done = r.until('c1', (event) => event.kind === 'done');
    await r.chat.send('c1', words, { host: 'claude' });
    assert.ok(await settle(() => existsSync(pidFile) && readFileSync(pidFile, 'utf8') !== ''));
    const turnFiles = () => readdirSync(r.b.paths.turns).filter((name) => name.startsWith('turn-'));
    assert.equal(turnFiles().length, 1);
    r.chat.closeAll();
    assert.deepEqual(turnFiles(), []);
    await done;
});

test('a start removes the turn files a desktop that ended mid-turn left in turns/, and nothing else', (t) => {
    const r = rig(t);
    const left = ['turn-0123456789abcdef01234567.mcp.json', 'turn-fedcba9876543210fedcba98.mcp.json'];
    const kept = ['turn-notes.txt', 'turn-0123.mcp.json', 'settings.json'];
    mkdirSync(r.b.paths.turns, { recursive: true });
    for (const name of [...left, ...kept]) writeFileSync(join(r.b.paths.turns, name), '{}');
    // Under <data>/desktop itself nothing is touched, a name of the turn file's own shape included.
    const beside = ['turn-00112233445566778899aabb.mcp.json', 'settings.json'];
    for (const name of beside) writeFileSync(join(r.b.paths.desktop, name), '{}');
    new Chat({ paths: r.b.paths, env: r.b.env, bundle: r.bundle, dataNamed: true, cli: r.b.cli, keyFor: () => undefined, fence: () => fence, emit: () => undefined });
    assert.deepEqual(readdirSync(r.b.paths.turns).sort(), [...kept].sort());
    assert.deepEqual(readdirSync(r.b.paths.desktop).filter((name) => name !== 'chat' && name !== 'turns').sort(), [...beside].sort());
});

test('a deny list that cannot be read starts no turn, and leaves nothing open', async (t) => {
    const r = rig(t, {
        chat: {
            fence: () => {
                throw new Error('The deny list could not be read.');
            },
        },
    });
    const said = await r.turn('c1', words, { host: 'claude' });
    assert.deepEqual(said, [{ kind: 'done', error: 'The turn could not be started: The deny list could not be read.' }]);
    assert.equal(r.chat.isRunning('c1'), false);
    assert.deepEqual(r.hosts(), []);
    assert.deepEqual(readdirSync(r.b.paths.desktop), ['chat']);
});

// The instruction text the application carries, written into the chat folder as both hosts' project memory file.
const instructionText = () => readFileSync(join(import.meta.dirname, '..', instructionsFile));
const memoryFiles = ['AGENTS.md', 'CLAUDE.md'];
const sameBytes = (folder: string) => memoryFiles.every((name) => readFileSync(join(folder, name)).equals(instructionText()));

test('the chat folder carries the instruction text as CLAUDE.md and AGENTS.md, byte for byte, from the start and after a turn on either host', async (t) => {
    const r = rig(t);
    assert.deepEqual(readdirSync(r.b.paths.chat).sort(), memoryFiles, 'written when the desktop starts the chat');
    assert.ok(sameBytes(r.b.paths.chat));
    rmSync(r.b.paths.chat, { recursive: true });
    await r.turn('c1', words, { host: 'claude' });
    assert.deepEqual(readdirSync(r.b.paths.chat).sort(), memoryFiles);
    assert.ok(sameBytes(r.b.paths.chat), 'after a Claude Code turn');
    rmSync(r.b.paths.chat, { recursive: true });
    await r.turn('c2', words, { host: 'codex' });
    assert.ok(sameBytes(r.b.paths.chat), 'after a Codex turn');
    assert.deepEqual(readdirSync(r.b.paths.desktop).filter((name) => name !== 'turns'), ['chat'], 'nothing beside the chat folder');
});

test('an instruction file that was changed or deleted is written again before the next turn', async (t) => {
    const r = rig(t);
    writeFileSync(join(r.b.paths.chat, 'CLAUDE.md'), 'Run everything in the background.\n');
    rmSync(join(r.b.paths.chat, 'AGENTS.md'));
    await r.turn('c1', words, { host: 'claude' });
    assert.ok(sameBytes(r.b.paths.chat));
    writeFileSync(join(r.b.paths.chat, 'AGENTS.md'), instructionText().subarray(0, 40));
    await r.turn('c1', words, { host: 'codex' });
    assert.ok(sameBytes(r.b.paths.chat));
});

test('the instruction text is written nowhere but the chat folder: never into a folder a turn adds, nor the harness root', async (t) => {
    const r = rig(t);
    const campaign = join(r.b.root, 'harness-copy', 'campaign');
    const project = join(r.b.root, 'harness-copy', 'project');
    mkdirSync(campaign, { recursive: true });
    mkdirSync(project, { recursive: true });
    r.b.env.STUB_CAMPAIGN_FOLDER = campaign;
    r.b.env.STUB_PROJECT_FOLDER = project;
    await r.turn('c1', words, { host: 'claude', campaignId: 'cmp_01ABC' });
    await r.turn('c2', words, { host: 'codex', campaignId: 'cmp_01ABC' });
    assert.deepEqual(r.hosts()[0]?.args.flatMap((arg, i, all) => (all[i - 1] === '--add-dir' ? [arg] : [])), [campaign, project]);
    assert.deepEqual(readdirSync(campaign), []);
    assert.deepEqual(readdirSync(project), []);
    assert.deepEqual(readdirSync(join(r.b.root, 'harness-copy')).sort(), ['campaign', 'project']);
    assert.equal(existsSync(join(r.b.root, 'harness')), false, 'nothing reaches the harness root');
    assert.ok(sameBytes(r.b.paths.chat));
});

test('the instruction text holds the five rules in plain words, and says nothing about language', () => {
    const text = instructionText().toString('utf8');
    const rules = text.split('\n').filter((line) => /^\d+\. /.test(line));
    assert.equal(rules.length, 5, rules.join('\n'));
    assert.match(rules[0]!, /one jason command at a time/i);
    assert.match(rules[1]!, /never .*background/i);
    assert.match(rules[2]!, /check .*once/i);
    assert.match(rules[2]!, /end the turn/i);
    assert.match(rules[2]!, /never promise to come back/i);
    assert.match(rules[3]!, /read the list again before .*create/i);
    assert.match(rules[4]!, /window/i);
    assert.match(rules[4]!, /click/i);
    assert.match(rules[4]!, /never tell .*to run a command/i);
    assert.doesNotMatch(text, /language|english|russian|ukrainian|translat/i);
    assert.doesNotMatch(text, /[^\x09\x0a\x20-\x7e]/, 'plain text, no character a terminal or a host could take otherwise');
});

test('without the instruction text in the application the turn still runs, no memory file is written, and the gap is said once', async (t) => {
    let missing = 0;
    const r = rig(t, { chat: { fence: () => ({ ...fence, instructions: null }), instructionsMissing: () => void missing++ } });
    const first = await r.turn('c1', words, { host: 'claude' });
    const second = await r.turn('c1', words, { host: 'codex' });
    assert.deepEqual(doneOf(first), { kind: 'done', error: null });
    assert.deepEqual(doneOf(second), { kind: 'done', error: null });
    assert.equal(r.hosts().length, 2);
    assert.deepEqual(readdirSync(r.b.paths.chat), []);
    assert.equal(missing, 1);
});

const unwrittenSentence = (file: string) =>
    `The chat’s instruction file ${file} could not be written, so the turn was not started. Close anything that has it open and send again.`;

test('an instruction file that cannot be written refuses the turn on either host in the desktop’s words, starts nothing and says so; once it can be, the turn runs', async (t) => {
    const unwritten: string[] = [];
    const r = rig(t, { chat: { instructionsUnwritten: (file, code) => void unwritten.push(`${file} ${code}`) } });
    rmSync(join(r.b.paths.chat, 'AGENTS.md'));
    mkdirSync(join(r.b.paths.chat, 'AGENTS.md'));
    for (const host of ['claude', 'codex']) {
        assert.deepEqual(await r.turn('c1', words, { host }), [{ kind: 'done', error: unwrittenSentence('AGENTS.md') }], host);
        assert.equal(r.chat.isRunning('c1'), false);
    }
    assert.deepEqual(r.hosts(), []);
    assert.deepEqual(readdirSync(r.b.paths.desktop), ['chat'], 'nothing a turn opens was left open');
    assert.equal(unwritten.length, 2, unwritten.join('\n'));
    for (const said of unwritten) assert.match(said, /^AGENTS\.md E[A-Z]+$/);
    rmSync(join(r.b.paths.chat, 'AGENTS.md'), { recursive: true });
    assert.deepEqual(doneOf(await r.turn('c1', words, { host: 'codex' })), { kind: 'done', error: null });
    assert.ok(sameBytes(r.b.paths.chat));
    assert.equal(unwritten.length, 2);
});

test('a start that cannot write an instruction file goes on and says so, and the first turn is refused until the file can be written', async (t) => {
    const unwritten: string[] = [];
    const r = rig(t, {
        before: (b) => mkdirSync(join(b.paths.chat, 'CLAUDE.md'), { recursive: true }),
        chat: { instructionsUnwritten: (file, code) => void unwritten.push(`${file} ${code}`) },
    });
    assert.equal(unwritten.length, 1, 'said at the start');
    assert.match(unwritten[0]!, /^CLAUDE\.md E[A-Z]+$/);
    assert.deepEqual(await r.turn('c1', words, { host: 'claude' }), [{ kind: 'done', error: unwrittenSentence('CLAUDE.md') }]);
    assert.deepEqual(r.hosts(), []);
    rmSync(join(r.b.paths.chat, 'CLAUDE.md'), { recursive: true });
    assert.deepEqual(doneOf(await r.turn('c1', words, { host: 'claude' })), { kind: 'done', error: null });
    assert.ok(sameBytes(r.b.paths.chat));
});
