// "Continue this chat" in a terminal, as far as the chat's record goes: beside each session a host named, the record keeps
// whether a plain turn named it (the person's own login, outside Full access), and the conversation offers a session to
// continue only for Claude Code or Codex, only one a plain turn named, and says whether a turn of it runs now.

import assert from 'node:assert/strict';
import { mkdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent } from '../src/chat.ts';
import { readFence } from '../src/chat-deny.ts';
import { continuableOf, nameSession, newConversationId, parseConversation, serialize, type Conversation } from '../src/chat-conversations.ts';
import { bench, outsideStandIn, placeSandboxFiles, type Bench } from './harness.ts';

const fence = readFence(join(import.meta.dirname, '..'));

const record = (fields: Partial<Conversation> = {}): Conversation => ({
    id: newConversationId(),
    campaign_id: null,
    title: 'New chat',
    created_at: '2026-11-11T10:00:00.000Z',
    updated_at: '2026-11-11T10:00:00.000Z',
    sessions: {},
    entries: [],
    dropped: 0,
    ...fields,
});

// The record.

test('a session named by a plain turn is marked beside its id; one named by any other turn is not, and a later naming decides', () => {
    const conversation = record();
    assert.equal(nameSession(conversation, 'claude', 'session-a', true), true);
    assert.deepEqual(conversation.sessions, { claude: 'session-a' });
    assert.deepEqual(conversation.plain_sessions, ['claude']);
    assert.equal(nameSession(conversation, 'claude', 'session-a', true), false, 'the same id named again by a plain turn changes nothing');
    // A turn that is not plain, naming the same id or another, takes the mark away.
    assert.equal(nameSession(conversation, 'claude', 'session-a', false), true);
    assert.deepEqual(conversation.sessions, { claude: 'session-a' });
    assert.equal(conversation.plain_sessions, undefined);
    assert.equal(nameSession(conversation, 'codex', 'thread-b', true), true);
    assert.equal(nameSession(conversation, 'claude', 'session-c', true), true);
    assert.deepEqual(conversation.plain_sessions, ['claude', 'codex']);
    assert.equal(nameSession(conversation, 'claude', 'session-d', false), true);
    assert.deepEqual(conversation.sessions, { claude: 'session-d', codex: 'thread-b' });
    assert.deepEqual(conversation.plain_sessions, ['codex']);
});

test('the mark is written beside the sessions and read back; a mark for a host with no session, or out of shape, is no mark, and never a reason to refuse the record', () => {
    const conversation = record({ sessions: { claude: 'session-a', deepseek: 'session-k' }, plain_sessions: ['claude'] });
    const text = serialize(conversation);
    assert.match(text, /"sessions":\{"claude":"session-a","deepseek":"session-k"\},"plain_sessions":\["claude"\],"entries"/);
    assert.deepEqual(parseConversation(text, conversation.id)?.plain_sessions, ['claude']);
    // None marked: nothing written.
    assert.ok(!serialize(record({ sessions: { claude: 'session-a' } })).includes('plain_sessions'));
    for (const [given, read] of [
        [['claude', 'codex'], ['claude']],
        [['codex'], undefined],
        [['claude', 'claude'], ['claude']],
        [['Claude'], undefined],
        [[7, 'claude'], ['claude']],
        ['claude', undefined],
        [{ claude: true }, undefined],
        [null, undefined],
    ] as const) {
        const raw = JSON.parse(text) as Record<string, unknown>;
        raw.plain_sessions = given;
        const parsed = parseConversation(JSON.stringify(raw), conversation.id);
        assert.ok(parsed !== null, JSON.stringify(given));
        assert.deepEqual(parsed.plain_sessions, read, JSON.stringify(given));
        assert.deepEqual(parsed.sessions, { claude: 'session-a', deepseek: 'session-k' });
    }
    // A record an earlier desktop wrote has no mark: none of its sessions is one to continue.
    const earlier = JSON.parse(text) as Record<string, unknown>;
    delete earlier.plain_sessions;
    assert.equal(parseConversation(JSON.stringify(earlier), conversation.id)?.plain_sessions, undefined);
});

test('a conversation offers its session to continue only for Claude Code or Codex, the host of its last turn, and only one a plain turn named', () => {
    const offered = record({ sessions: { claude: 'session-a' }, plain_sessions: ['claude'], chosen: { host: 'claude', model: null, effort: null } });
    assert.deepEqual(continuableOf(offered), { host: 'claude', session: 'session-a' });
    assert.deepEqual(continuableOf(record({ sessions: { codex: 'thread-b' }, plain_sessions: ['codex'], chosen: { host: 'codex', model: null, effort: null } })), { host: 'codex', session: 'thread-b' });
    const rows: [string, Conversation][] = [
        ['no session kept', record({ chosen: { host: 'claude', model: null, effort: null } })],
        ['no mark: a record of an earlier desktop, or a session a Full access turn named', record({ sessions: { claude: 'session-a' }, chosen: { host: 'claude', model: null, effort: null } })],
        ['a key provider’s session', record({ sessions: { deepseek: 'session-k' }, plain_sessions: ['deepseek'], chosen: { host: 'deepseek', model: null, effort: null } })],
        ['the last turn was on a key provider', record({ sessions: { claude: 'session-a', deepseek: 'session-k' }, plain_sessions: ['claude'], chosen: { host: 'deepseek', model: null, effort: null } })],
        ['the last turn was on another host, which named none', record({ sessions: { claude: 'session-a' }, plain_sessions: ['claude'], chosen: { host: 'codex', model: null, effort: null } })],
        ['no turn chose a host', record({ sessions: { claude: 'session-a' }, plain_sessions: ['claude'] })],
        ['a host Open in has no words for', record({ sessions: { gemini: 'session-g' }, plain_sessions: ['gemini'], chosen: { host: 'gemini', model: null, effort: null } })],
    ];
    for (const [why, conversation] of rows) assert.equal(continuableOf(conversation), null, why);
});

// Whole turns against the stub hosts.

interface Rig {
    b: Bench;
    chat: Chat;
    keys: Record<string, string>;
    turn(id: string, text: string, options: unknown): Promise<ChatEvent[]>;
    until(id: string, take: (event: ChatEvent) => boolean): Promise<ChatEvent>;
    kept(id: string): { sessions: Record<string, string>; plain_sessions?: string[] };
}

function rig(t: { after(fn: () => void | Promise<void>): void }): Rig {
    const removals: (() => void)[] = [];
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude', 'codex'] });
    placeSandboxFiles(b);
    const bundle = join(b.root, 'bundle');
    mkdirSync(bundle, { recursive: true });
    const events = new Map<string, ChatEvent[]>();
    const waiters: { id: string; take: (event: ChatEvent) => boolean; resolve: (event: ChatEvent) => void }[] = [];
    const keys: Record<string, string> = {};
    const chat = new Chat({
        paths: b.paths,
        env: b.env,
        bundle,
        dataNamed: true,
        bundleVersion: null,
        cli: b.cli,
        programs: b.programs,
        keyFor: (id) => keys[id],
        fence: () => fence,
        sandboxOutside: outsideStandIn,
        confirmFullAccess: async () => true,
        fullAccessOffered: true,
        emit(id, event) {
            const said = events.get(id) ?? [];
            said.push(event);
            events.set(id, said);
            for (const waiter of [...waiters]) {
                if (waiter.id === id && waiter.take(event)) {
                    waiters.splice(waiters.indexOf(waiter), 1);
                    waiter.resolve(event);
                }
            }
        },
    });
    t.after(async () => {
        chat.closeAll();
        const until = Date.now() + 10_000;
        while (chat.list().some((each) => each.running) && Date.now() < until) await new Promise((resolve) => setTimeout(resolve, 50));
        await chat.idle();
        for (const remove of removals) remove();
    });
    const until = (id: string, take: (event: ChatEvent) => boolean) =>
        new Promise<ChatEvent>((resolve, reject) => {
            const timer = setTimeout(() => reject(new Error(`chat ${id} never said what was waited for; it said ${JSON.stringify(events.get(id) ?? [])}`)), 20_000);
            waiters.push({ id, take, resolve: (event) => { clearTimeout(timer); resolve(event); } });
        });
    return {
        b,
        chat,
        keys,
        until,
        async turn(id, text, options) {
            const from = events.get(id)?.length ?? 0;
            const done = until(id, (event) => event.kind === 'done');
            await chat.send(id, text, options);
            await done;
            return (events.get(id) ?? []).slice(from);
        },
        kept: (id) => JSON.parse(readFileSync(join(b.paths.conversations, `${id}.json`), 'utf8')) as { sessions: Record<string, string>; plain_sessions?: string[] },
    };
}

for (const host of ['claude', 'codex'] as const) {
    test(`a plain ${host} turn marks the session it named, and the chat offers that session to continue`, async (t) => {
        const r = rig(t);
        const { id } = await r.chat.create();
        await r.turn(id, 'Hello', { host });
        const kept = r.kept(id);
        const session = kept.sessions[host];
        assert.ok(session !== undefined);
        assert.deepEqual(kept.plain_sessions, [host]);
        assert.deepEqual(r.chat.continuable(id), { host, session, running: false });
    });
}

test('a Plan only turn is a plain turn: its session is marked', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'plan_only', 'claude');
    await r.turn(id, 'Plan it', { host: 'claude' });
    assert.deepEqual(r.kept(id).plain_sessions, ['claude']);
    assert.equal(r.chat.continuable(id)?.host, 'claude');
});

test('a Full access turn marks no session, and takes the mark from the one it continued: the chat offers nothing', async (t) => {
    const r = rig(t);
    const fresh = await r.chat.create();
    await r.chat.setMode(fresh.id, 'full_access', 'claude');
    await r.turn(fresh.id, 'Go', { host: 'claude' });
    assert.ok(r.kept(fresh.id).sessions.claude !== undefined);
    assert.equal(r.kept(fresh.id).plain_sessions, undefined);
    assert.equal(r.chat.continuable(fresh.id), null);
    // A plain session that a Full access turn then continued is no plain one any longer.
    const { id } = await r.chat.create();
    await r.turn(id, 'Hello', { host: 'claude' });
    assert.deepEqual(r.kept(id).plain_sessions, ['claude']);
    await r.chat.setMode(id, 'full_access', 'claude');
    await r.turn(id, 'Go on', { host: 'claude' });
    assert.equal(r.kept(id).plain_sessions, undefined);
    assert.equal(r.chat.continuable(id), null);
    // Back in Ask first, the next plain turn names it again, and it is offered again.
    await r.chat.setMode(id, 'ask_first', 'claude');
    await r.turn(id, 'And now', { host: 'claude' });
    assert.deepEqual(r.kept(id).plain_sessions, ['claude']);
    assert.equal(r.chat.continuable(id)?.host, 'claude');
});

test('a key provider’s turn marks no session, and the chat offers nothing while that provider is its host', async (t) => {
    const r = rig(t);
    r.keys.deepseek = 'sk-planted-deepseek';
    const { id } = await r.chat.create();
    await r.turn(id, 'Hello', { host: 'claude' });
    await r.turn(id, 'And on the key', { host: 'deepseek' });
    const kept = r.kept(id);
    assert.ok(kept.sessions.deepseek !== undefined);
    assert.deepEqual(kept.plain_sessions, ['claude'], 'the plain session keeps its own mark; the provider’s has none');
    assert.equal(r.chat.continuable(id), null);
});

test('while a turn of the chat runs it is offered as running; a chat the desktop does not keep, or an id of no shape, offers nothing', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.turn(id, 'Hello', { host: 'claude' });
    const pid = join(r.b.root, 'host.pid');
    r.b.env.STUB_HOST_PLAN = JSON.stringify([{ wait: pid }]);
    const done = r.until(id, (event) => event.kind === 'done');
    await r.chat.send(id, 'Wait', { host: 'claude' });
    assert.equal(r.chat.continuable(id)?.running, true);
    r.chat.stop(id);
    await done;
    assert.equal(r.chat.continuable(id)?.running, false);
    for (const other of ['conv_01ZZZZZZZZZZZZZZZZZZZZZZZZ', 'chat-a', '../x', 7, null, undefined, {}]) assert.equal(r.chat.continuable(other), null, String(other));
});

test('starting a chat afresh drops its sessions and their marks', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.turn(id, 'Hello', { host: 'claude' });
    r.chat.restart(id);
    assert.deepEqual(r.kept(id).sessions, {});
    assert.equal(r.kept(id).plain_sessions, undefined);
    assert.equal(r.chat.continuable(id), null);
});
