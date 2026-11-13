// The bridge a browser tab reaches the desktop's chat through, against made-up handlers and against the real chat: the
// checks every request meets before anything is read, the pairing a one-time code opens, the one table of members, the
// rules Full access and Ask first hold a tab to, the events a session reads, and the bridge's life.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { request } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { bridgeLink, bridgeMembers, BridgeHolder, BrowserBridge, elsewhere, fullAccessElsewhere, modeElsewhere, openOwnPage, openTab, type BridgeOptions } from '../src/browser-bridge.ts';
import { Chat, type ChatEvent } from '../src/chat.ts';
import { readFence } from '../src/chat-deny.ts';
import { registerChat } from '../src/chat-ipc.ts';
import { fullAccessConsent } from '../src/chat-modes.ts';
import { writeStanding } from '../src/full-access-standing.ts';
import { invokeChannels, withMembers, type Handler, type InvokeMember, type IpcRegistrar } from '../src/ipc.ts';
import { isOwnPage } from '../src/descriptor.ts';
import { fileLogger, holesOf, templates } from '../src/log.ts';
import { bench, memoryLog, outsideStandIn, placeSandboxFiles } from './harness.ts';

const runtime = 'http://127.0.0.1:41234';

interface Answer {
    status: number;
    headers: Record<string, string | string[] | undefined>;
    body: string;
}

/** One request to the bridge, with every header as given: the Host and the Origin among them. */
function ask(port: number, options: { method?: string; path?: string; headers?: Record<string, string>; body?: string | Buffer }): Promise<Answer> {
    return new Promise((resolve, reject) => {
        const headers: Record<string, string> = { host: `127.0.0.1:${port}`, ...options.headers };
        const sent = request({ host: '127.0.0.1', port, method: options.method ?? 'POST', path: options.path ?? '/call', headers, setHost: false }, (response) => {
            const chunks: Buffer[] = [];
            response.on('data', (chunk: Buffer) => chunks.push(chunk));
            response.on('end', () => resolve({ status: response.statusCode ?? 0, headers: response.headers, body: Buffer.concat(chunks).toString('utf8') }));
            response.on('error', reject);
        });
        sent.on('error', reject);
        sent.end(options.body);
    });
}

const json = (value: unknown) => JSON.stringify(value);
const fromPage = { origin: runtime, 'content-type': 'application/json' };

interface Rig {
    bridge: BrowserBridge;
    calls: { member: InvokeMember; args: unknown[] }[];
    log: ReturnType<typeof memoryLog>;
    now: { value: number };
    origin: { value: string | null };
    pair(): Promise<string>;
    call(secret: string, member: string, args: unknown[]): Promise<Answer>;
}

/** A handler for every member, recording what it was handed; `chat.list` answers the conversations given. */
function recording(calls: Rig['calls'], listed: () => unknown[]): Map<InvokeMember, Handler> {
    const members = new Map<InvokeMember, Handler>();
    for (const member of Object.keys(invokeChannels) as InvokeMember[]) {
        members.set(member, (...args: unknown[]) => {
            calls.push({ member, args });
            return member === 'chat.list' ? listed() : member === 'mode.get' ? { mode: 'live', unopened: null } : null;
        });
    }
    return members;
}

async function rig(t: { after(fn: () => void | Promise<void>): void }, extra: Partial<BridgeOptions> & { listed?: () => unknown[] } = {}): Promise<Rig> {
    const calls: Rig['calls'] = [];
    const log = memoryLog();
    const now = { value: 1_000_000 };
    const origin = { value: runtime as string | null };
    const { listed, ...options } = extra;
    const bridge = await BrowserBridge.open({
        origin: () => origin.value,
        members: recording(calls, listed ?? (() => [])),
        log,
        now: () => now.value,
        ...options,
    });
    t.after(() => bridge.close());
    const r: Rig = {
        bridge,
        calls,
        log,
        now,
        origin,
        async pair() {
            const code = bridge.mint();
            const answer = await ask(bridge.port, { path: '/pair', headers: fromPage, body: json({ code }) });
            assert.equal(answer.status, 200, answer.body);
            const secret = (JSON.parse(answer.body) as { secret: string }).secret;
            assert.match(secret, /^[A-Za-z0-9_-]{43}$/);
            return secret;
        },
        call: (secret, member, args) => ask(bridge.port, { headers: { ...fromPage, authorization: `Bearer ${secret}` }, body: json({ member, args }) }),
    };
    return r;
}

const refusals = (r: Rig) => r.log.lines.filter((line) => line.name === 'bridge_refused').map((line) => line.fields);

// --- every request, before anything is read --------------------------------------------------------------------------

test('a Host other than 127.0.0.1 and the bridge\'s port is refused with 403 and an empty body, and nothing is handled', async (t) => {
    const r = await rig(t);
    const secret = await r.pair();
    for (const host of ['localhost:' + r.bridge.port, `127.0.0.1:${r.bridge.port + 1}`, 'evil.example', `127.0.0.1`]) {
        const answer = await ask(r.bridge.port, { headers: { ...fromPage, host, authorization: `Bearer ${secret}` }, body: json({ member: 'chat.list', args: [] }) });
        assert.equal(answer.status, 403, host);
        assert.equal(answer.body, '', host);
        assert.equal(answer.headers['access-control-allow-origin'], undefined, host);
    }
    assert.deepEqual(r.calls, []);
    assert.ok(refusals(r).every((fields) => fields.Why === 'host'));
});

test('a request with no Origin, or with any Origin but the runtime\'s, is refused with 403 and an empty body', async (t) => {
    const r = await rig(t);
    const secret = await r.pair();
    const bearer = { 'content-type': 'application/json', authorization: `Bearer ${secret}` };
    for (const headers of [bearer, { ...bearer, origin: 'http://127.0.0.1:9999' }, { ...bearer, origin: 'https://example.com' }, { ...bearer, origin: 'null' }, { ...bearer, origin: runtime + '/' }]) {
        const answer = await ask(r.bridge.port, { headers, body: json({ member: 'chat.list', args: [] }) });
        assert.equal(answer.status, 403, JSON.stringify(headers));
        assert.equal(answer.body, '');
    }
    assert.deepEqual(r.calls, []);
});

test('the runtime\'s origin is read at each request: after the runtime moves, its old origin is refused, and with no run file every origin is', async (t) => {
    const r = await rig(t);
    const secret = await r.pair();
    r.origin.value = 'http://127.0.0.1:50000';
    assert.equal((await r.call(secret, 'chat.list', [])).status, 403);
    const moved = await ask(r.bridge.port, { headers: { origin: 'http://127.0.0.1:50000', 'content-type': 'application/json', authorization: `Bearer ${secret}` }, body: json({ member: 'chat.list', args: [] }) });
    assert.equal(moved.status, 200);
    r.origin.value = null;
    assert.equal((await ask(r.bridge.port, { headers: { origin: 'null', authorization: `Bearer ${secret}` }, body: json({ member: 'chat.list', args: [] }) })).status, 403);
});

test('a preflight is answered for the runtime\'s origin alone, GET and POST, authorization and content-type, Vary: Origin, never with credentials', async (t) => {
    const r = await rig(t);
    const preflight = await ask(r.bridge.port, { method: 'OPTIONS', headers: { origin: runtime, 'access-control-request-method': 'POST', 'access-control-request-headers': 'authorization, content-type' } });
    assert.equal(preflight.status, 204);
    assert.equal(preflight.body, '');
    assert.equal(preflight.headers['access-control-allow-origin'], runtime);
    assert.equal(preflight.headers['access-control-allow-methods'], 'GET, POST');
    assert.equal(preflight.headers['access-control-allow-headers'], 'authorization, content-type');
    assert.equal(preflight.headers.vary, 'Origin');
    assert.equal(preflight.headers['access-control-allow-credentials'], undefined);
    const foreign = await ask(r.bridge.port, { method: 'OPTIONS', headers: { origin: 'http://127.0.0.1:9999', 'access-control-request-method': 'POST' } });
    assert.equal(foreign.status, 403);
    assert.equal(foreign.headers['access-control-allow-origin'], undefined);
    // An answer to the page carries its origin and Vary too, and no credentials.
    const secret = await r.pair();
    const answered = await r.call(secret, 'chat.list', []);
    assert.equal(answered.headers['access-control-allow-origin'], runtime);
    assert.equal(answered.headers.vary, 'Origin');
    assert.equal(answered.headers['access-control-allow-credentials'], undefined);
});

test('a path or a method the bridge does not serve is 404 with an empty body', async (t) => {
    const r = await rig(t);
    for (const [method, path] of [['GET', '/call'], ['POST', '/events'], ['GET', '/'], ['PUT', '/pair'], ['POST', '/call/']]) {
        const answer = await ask(r.bridge.port, { method, path, headers: fromPage });
        assert.equal(answer.status, 404, `${method} ${path}`);
        assert.equal(answer.body, '');
    }
});

// --- pairing ---------------------------------------------------------------------------------------------------------

test('a code pairs once: spent, it is refused with 401 and an empty body', async (t) => {
    const r = await rig(t);
    const code = r.bridge.mint();
    assert.match(code, /^[A-Za-z0-9_-]{22}$/);
    const first = await ask(r.bridge.port, { path: '/pair', headers: fromPage, body: json({ code }) });
    assert.equal(first.status, 200);
    const spent = await ask(r.bridge.port, { path: '/pair', headers: fromPage, body: json({ code }) });
    assert.equal(spent.status, 401);
    assert.equal(spent.body, '');
});

test('a code is good for sixty seconds: a late one is refused', async (t) => {
    const r = await rig(t);
    const code = r.bridge.mint();
    r.now.value += 60_001;
    const late = await ask(r.bridge.port, { path: '/pair', headers: fromPage, body: json({ code }) });
    assert.equal(late.status, 401);
    const inTime = r.bridge.mint();
    r.now.value += 59_000;
    assert.equal((await ask(r.bridge.port, { path: '/pair', headers: fromPage, body: json({ code: inTime }) })).status, 200);
});

test('a wrong code, or a body that holds none, is refused, and the right one still pairs after it', async (t) => {
    const r = await rig(t);
    const code = r.bridge.mint();
    for (const body of [json({ code: 'A'.repeat(22) }), json({ code: code.slice(0, 21) }), json({ code: 7 }), json({}), 'not json', json({ code: code + 'x' })]) {
        const answer = await ask(r.bridge.port, { path: '/pair', headers: fromPage, body });
        assert.equal(answer.status, 401, body);
        assert.equal(answer.body, '');
    }
    assert.equal((await ask(r.bridge.port, { path: '/pair', headers: fromPage, body: json({ code }) })).status, 200);
    assert.ok(refusals(r).every((fields) => fields.Why === 'code'));
});

test('a call before pairing, or with a bearer the bridge did not give, is refused with 401 and nothing is handled', async (t) => {
    const r = await rig(t);
    const none = await ask(r.bridge.port, { headers: fromPage, body: json({ member: 'chat.list', args: [] }) });
    assert.equal(none.status, 401);
    assert.equal(none.body, '');
    await r.pair();
    for (const authorization of ['Bearer ' + 'B'.repeat(43), 'Bearer', 'Basic abc', `bearer ${'B'.repeat(43)}`]) {
        const answer = await ask(r.bridge.port, { headers: { ...fromPage, authorization }, body: json({ member: 'chat.list', args: [] }) });
        assert.equal(answer.status, 401, authorization);
    }
    const events = await ask(r.bridge.port, { method: 'GET', path: '/events', headers: { origin: runtime, authorization: 'Bearer ' + 'B'.repeat(43) } });
    assert.equal(events.status, 401);
    assert.deepEqual(r.calls, []);
});

test('past four live sessions the oldest is ended: its secret is refused and its stream closes', async (t) => {
    const r = await rig(t);
    const first = await r.pair();
    const stream = await openStream(r.bridge.port, first);
    const later = [await r.pair(), await r.pair(), await r.pair()];
    assert.equal((await r.call(first, 'chat.list', [])).status, 200, 'four live sessions all answer');
    const fifth = await r.pair();
    assert.equal((await r.call(first, 'chat.list', [])).status, 401, 'the oldest is ended');
    await stream.ended;
    for (const secret of [...later, fifth]) assert.equal((await r.call(secret, 'chat.list', [])).status, 200);
});

// --- one table ---------------------------------------------------------------------------------------------------------

const table: InvokeMember[] = ['mode.get', 'theme.get', 'fullAccess.standing', 'setup.status', 'chat.list', 'chat.open', 'chat.create', 'chat.send', 'chat.stop', 'chat.rename'];

test('the table is exactly the six reads and the four acts', () => {
    assert.deepEqual([...bridgeMembers].sort(), [...table].sort());
});

const outside = (Object.keys(invokeChannels) as InvokeMember[]).filter((member) => !table.includes(member));

test('every member the design names as outside the table is outside it', () => {
    for (const member of ['chat.answer', 'chat.setMode', 'chat.feed', 'chat.remove', 'chat.restart', 'chat.attach', 'chat.openFolder', 'mode.set', 'mode.startOver', 'theme.set', 'fullAccess.takeBack', 'openIn.targets', 'openIn.open', 'links.take', 'pairing.paired'] as const) {
        assert.ok(outside.includes(member), member);
    }
    for (const member of outside.filter((each) => each.startsWith('setup.') && each !== 'setup.status')) assert.ok(!bridgeMembers.includes(member), member);
});

for (const member of [...outside, 'constructor', '__proto__', 'hasOwnProperty', 'chat.list ', 'Chat.list', 'unknown']) {
    test(`${JSON.stringify(member)} is refused in one sentence, is never handled, and the log names it only if it is a member`, async (t) => {
        const r = await rig(t);
        const secret = await r.pair();
        const answer = await r.call(secret, member, ['cht_aaaaaaaaaaaaaaaaaaaaaaaaaa', 'some words', { allow: true, always: true }]);
        assert.equal(answer.status, 403);
        assert.deepEqual(JSON.parse(answer.body), { refused: elsewhere });
        assert.equal(elsewhere, "That is done in Jason Desktop's own window.");
        assert.deepEqual(r.calls, []);
        const known = (Object.keys(invokeChannels) as string[]).includes(member);
        assert.deepEqual(refusals(r).at(-1), { Why: 'member', Member: known ? member : 'unknown' });
    });
}

test('a read of the table hands the SAME handler its arguments, no more than it takes, and answers its value', async (t) => {
    const r = await rig(t, { listed: () => [{ id: 'cht_a', mode: 'ask_first' }] });
    const secret = await r.pair();
    assert.deepEqual(JSON.parse((await r.call(secret, 'mode.get', ['extra'])).body), { result: { mode: 'live', unopened: null } });
    assert.deepEqual(JSON.parse((await r.call(secret, 'chat.open', ['cht_a', 'extra'])).body), { result: null });
    assert.deepEqual(JSON.parse((await r.call(secret, 'chat.rename', ['cht_a', 'New name', 'extra'])).body), { result: null });
    assert.deepEqual(r.calls, [
        { member: 'mode.get', args: [] },
        { member: 'chat.open', args: ['cht_a'] },
        { member: 'chat.rename', args: ['cht_a', 'New name'] },
    ]);
});

test('a body over 1 MB is refused with 413 before it is handled; one of 1 MB is read', async (t) => {
    const r = await rig(t);
    const secret = await r.pair();
    const sized = (bytes: number) => {
        const shell = json({ member: 'chat.rename', args: ['cht_a', ''] });
        return shell.replace('""', json('x'.repeat(bytes - shell.length)));
    };
    const over = await ask(r.bridge.port, { headers: { ...fromPage, authorization: `Bearer ${secret}` }, body: sized(1024 * 1024 + 1) });
    assert.equal(over.status, 413);
    assert.equal(over.body, '');
    assert.deepEqual(r.calls, []);
    const full = await ask(r.bridge.port, { headers: { ...fromPage, authorization: `Bearer ${secret}` }, body: sized(1024 * 1024) });
    assert.equal(full.status, 200);
    assert.equal(r.calls.length, 1);
});

test('a body that is not a member and an array of arguments is refused with 400', async (t) => {
    const r = await rig(t);
    const secret = await r.pair();
    for (const body of ['nope', json({ member: 'chat.list' }), json({ member: 'chat.list', args: {} }), json({ args: [] }), json(['chat.list'])]) {
        const answer = await ask(r.bridge.port, { headers: { ...fromPage, authorization: `Bearer ${secret}` }, body });
        assert.equal(answer.status, 400, body);
    }
    assert.deepEqual(r.calls, []);
});

test('a handler that throws is answered 500 with its message, and a refusal it answers as a value stays a value', async (t) => {
    const calls: Rig['calls'] = [];
    const members = recording(calls, () => []);
    members.set('chat.stop', () => {
        throw new Error('It broke.');
    });
    members.set('chat.open', () => ({ refused: 'That chat is not kept.' }));
    const r = await rig(t, { members });
    const secret = await r.pair();
    const thrown = await r.call(secret, 'chat.stop', ['cht_a']);
    assert.equal(thrown.status, 500);
    assert.deepEqual(JSON.parse(thrown.body), { error: 'It broke.' });
    const refused = await r.call(secret, 'chat.open', ['cht_a']);
    assert.equal(refused.status, 200);
    assert.deepEqual(JSON.parse(refused.body), { result: { refused: 'That chat is not kept.' } });
});

// --- chat.send and chat.create ---------------------------------------------------------------------------------------

test('chat.send to a chat in Full access is refused, and the send handler never runs', async (t) => {
    const r = await rig(t, { listed: () => [{ id: 'cht_full', mode: 'full_access' }] });
    const secret = await r.pair();
    const answer = await r.call(secret, 'chat.send', ['cht_full', 'delete everything', { host: 'claude' }]);
    assert.equal(answer.status, 200);
    assert.deepEqual(JSON.parse(answer.body), { result: { refused: fullAccessElsewhere } });
    assert.deepEqual(r.calls.map((call) => call.member), ['chat.list']);
    assert.deepEqual(refusals(r).at(-1), { Why: 'full_access', Member: 'chat.send' });
});

test('chat.send is held to the chat\'s own mode: a tab naming another is refused, and one naming none sends in the mode the chat has', async (t) => {
    const r = await rig(t, { listed: () => [{ id: 'cht_auto', mode: 'auto_edit' }, { id: 'cht_plan', mode: 'plan_only' }] });
    const secret = await r.pair();
    for (const [chatId, mode] of [['cht_auto', 'full_access'], ['cht_auto', 'ask_first'], ['cht_plan', 'ask_first']]) {
        const answer = await r.call(secret, 'chat.send', [chatId, 'hello', { host: 'claude', mode }]);
        assert.deepEqual(JSON.parse(answer.body), { result: { refused: modeElsewhere } }, `${chatId} ${mode}`);
    }
    assert.ok(!r.calls.some((call) => call.member === 'chat.send'));
    await r.call(secret, 'chat.send', ['cht_auto', 'hello', { host: 'claude', model: 'm' }]);
    await r.call(secret, 'chat.send', ['cht_plan', 'hello', { host: 'claude', mode: 'plan_only' }]);
    // A chat the desktop does not list is sent in Ask first, which the chat refuses as no conversation of its own.
    await r.call(secret, 'chat.send', ['cht_gone', 'hello']);
    assert.deepEqual(r.calls.filter((call) => call.member === 'chat.send').map((call) => call.args), [
        ['cht_auto', 'hello', { host: 'claude', model: 'm', mode: 'auto_edit' }],
        ['cht_plan', 'hello', { host: 'claude', mode: 'plan_only' }],
        ['cht_gone', 'hello', { mode: 'ask_first' }],
    ]);
});

/** The real chat against stub hosts, its calls registered as main.ts registers them, so the bridge reaches its handlers. */
function realChat(t: { after(fn: () => void | Promise<void>): void }) {
    const removals: (() => void)[] = [];
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude', 'codex'] });
    placeSandboxFiles(b);
    const bundle = join(b.root, 'bundle');
    mkdirSync(bundle, { recursive: true });
    const events = new Map<string, ChatEvent[]>();
    const chat = new Chat({
        paths: b.paths,
        env: b.env,
        bundle,
        dataNamed: true,
        bundleVersion: null,
        cli: b.cli,
        programs: b.programs,
        keyFor: () => undefined,
        fence: () => readFence(join(import.meta.dirname, '..')),
        sandboxOutside: outsideStandIn,
        fullAccessOffered: true,
        emit: (id, event) => void events.set(id, [...(events.get(id) ?? []), event]),
        confirmFullAccess: async () => ({ always: true, newChats: false }),
    });
    const ipc: IpcRegistrar = { handle: () => undefined };
    const seam = withMembers(ipc);
    registerChat(seam, chat, () => runtime, () => undefined);
    t.after(async () => {
        chat.closeAll();
        await chat.idle();
        for (const remove of removals) remove();
    });
    return { b, chat, events, members: seam.members };
}

test('chat.create under a standing Full access for new chats makes an Ask first chat, whatever host the tab names', async (t) => {
    const real = realChat(t);
    writeStanding(real.b.paths.fullAccess, 'claude', { consent: fullAccessConsent, givenAt: '2026-01-02T03:04:05.000Z', newChats: true });
    // The window's own create is in Full access under this standing choice: the control.
    assert.equal((await real.chat.create(undefined, 'claude')).mode, 'full_access');
    const r = await rig(t, { members: real.members });
    const secret = await r.pair();
    const made = JSON.parse((await r.call(secret, 'chat.create', [null, 'claude'])).body) as { result: { id: string; mode: string } };
    assert.equal(made.result.mode, 'ask_first');
    assert.equal(real.chat.open(made.result.id).mode, 'ask_first');
});

test('the chat\'s own check is the second line: a send read as Ask first while the chat is in Full access is refused by the chat', async (t) => {
    const real = realChat(t);
    writeStanding(real.b.paths.fullAccess, 'claude', { consent: fullAccessConsent, givenAt: '2026-01-02T03:04:05.000Z', newChats: true });
    const full = await real.chat.create(undefined, 'claude');
    assert.equal(full.mode, 'full_access');
    // The bridge's read is stood in by one that is behind the chat: as if the window chose Full access between the two.
    const members = new Map(real.members);
    members.set('chat.list', () => [{ id: full.id, mode: 'ask_first' }]);
    const r = await rig(t, { members });
    const secret = await r.pair();
    const answer = await r.call(secret, 'chat.send', [full.id, 'hello', { host: 'claude' }]);
    assert.equal(answer.status, 200);
    const said = real.events.get(full.id) ?? [];
    const done = said.find((event) => event.kind === 'done');
    assert.ok(done !== undefined && done.kind === 'done' && typeof done.error === 'string', JSON.stringify(said));
    assert.ok(!real.b.record().some((each) => each.stdin.includes('hello') || each.args.some((arg) => arg.includes('hello'))), 'no turn was started');
});

// --- events ----------------------------------------------------------------------------------------------------------

interface Stream {
    lines: unknown[];
    ended: Promise<void>;
    next(): Promise<unknown>;
    pause(): void;
    resume(): void;
}

function openStream(port: number, secret: string): Promise<Stream> {
    return new Promise((resolve, reject) => {
        const sent = request({ host: '127.0.0.1', port, method: 'GET', path: '/events', headers: { host: `127.0.0.1:${port}`, origin: runtime, authorization: `Bearer ${secret}` }, setHost: false }, (response) => {
            if (response.statusCode !== 200) return reject(new Error(`events answered ${response.statusCode}`));
            assert.equal(response.headers['content-type'], 'application/x-ndjson');
            assert.equal(response.headers['access-control-allow-origin'], runtime);
            const lines: unknown[] = [];
            let read = 0;
            let waiting: (() => void) | null = null;
            let rest = '';
            response.setEncoding('utf8');
            response.on('data', (chunk: string) => {
                rest += chunk;
                let at: number;
                while ((at = rest.indexOf('\n')) >= 0) {
                    const line = JSON.parse(rest.slice(0, at)) as unknown;
                    rest = rest.slice(at + 1);
                    lines.push(line);
                    const wake = waiting;
                    waiting = null;
                    wake?.();
                }
            });
            const ended = new Promise<void>((done) => {
                response.on('close', () => done());
                response.on('error', () => done());
            });
            resolve({
                lines,
                ended,
                // Each line once, in order, whether it came before the ask or after.
                next: async () => {
                    while (read >= lines.length) await new Promise<void>((wake) => (waiting = wake));
                    return lines[read++];
                },
                pause: () => response.pause(),
                resume: () => response.resume(),
            });
        });
        sent.on('error', reject);
        sent.end();
    });
}

test('events reach a session as JSON lines of channel and arguments, addressed by chat id, with a heartbeat', async (t) => {
    const r = await rig(t, { heartbeatMs: 30 });
    const secret = await r.pair();
    const stream = await openStream(r.bridge.port, secret);
    const event = { kind: 'text', text: 'Hello' };
    r.bridge.publish('chat.event', ['cht_a', event]);
    r.bridge.publish('fullAccess.changed', [null]);
    r.bridge.publish('setup.changed', []);
    const got: unknown[] = [];
    while (got.length < 3) {
        const line = await stream.next();
        if (!(typeof line === 'object' && line !== null && 'heartbeat' in line)) got.push(line);
    }
    assert.deepEqual(got, [
        { channel: 'chat.event', args: ['cht_a', event] },
        { channel: 'fullAccess.changed', args: [null] },
        { channel: 'setup.changed', args: [] },
    ]);
    let beat: unknown;
    do beat = await stream.next();
    while (!(typeof beat === 'object' && beat !== null && 'heartbeat' in beat));
    assert.deepEqual(beat, { heartbeat: true });
});

test('one stream a session: a second one ends the first', async (t) => {
    const r = await rig(t);
    const secret = await r.pair();
    const first = await openStream(r.bridge.port, secret);
    const second = await openStream(r.bridge.port, secret);
    await first.ended;
    r.bridge.publish('setup.changed', []);
    assert.deepEqual(await second.next(), { channel: 'setup.changed', args: [] });
});

test('a stream that is not read past its bounded queue is closed, and the session can open another', async (t) => {
    const r = await rig(t, { queueBytes: 256 * 1024 });
    const secret = await r.pair();
    const stream = await openStream(r.bridge.port, secret);
    stream.pause();
    const big = { kind: 'text', text: 'x'.repeat(64 * 1024) };
    const overflowed = () => r.log.lines.some((line) => line.name === 'bridge_stream_closed' && line.fields.Why === 'overflow');
    for (let i = 0; i < 4000 && !overflowed(); i++) {
        r.bridge.publish('chat.event', ['cht_a', big]);
        if (i % 50 === 0) await new Promise((resolve) => setTimeout(resolve, 5));
    }
    assert.ok(overflowed(), 'the stream was closed past its bound');
    // The tab reads what reached it and then finds its stream ended.
    stream.resume();
    await stream.ended;
    const again = await openStream(r.bridge.port, secret);
    r.bridge.publish('setup.changed', []);
    assert.deepEqual(await again.next(), { channel: 'setup.changed', args: [] });
});

// --- life ------------------------------------------------------------------------------------------------------------

test('closing the bridge ends every stream and session, and its port answers no more', async (t) => {
    const r = await rig(t);
    const secret = await r.pair();
    const stream = await openStream(r.bridge.port, secret);
    await r.bridge.close();
    await stream.ended;
    await assert.rejects(r.call(secret, 'chat.list', []));
});

test('the holder opens one bridge on the first ask, the same one after, and none once it is closed with the window', async (t) => {
    const opened: BrowserBridge[] = [];
    const holder = new BridgeHolder(async () => {
        const bridge = await BrowserBridge.open({ origin: () => runtime, members: new Map(), log: memoryLog() });
        opened.push(bridge);
        return bridge;
    });
    t.after(() => holder.close());
    assert.equal(holder.current, null, 'nothing listens before the person asks');
    const [one, two] = await Promise.all([holder.ensure(), holder.ensure()]);
    assert.equal(one, two);
    assert.equal(opened.length, 1);
    const port = one!.port;
    await holder.close();
    assert.equal(await holder.ensure(), null);
    assert.equal(opened.length, 1);
    await assert.rejects(ask(port, { headers: fromPage, body: '{}' }));
});

test('main.ts closes the bridge with the window and at quit, beside the chat\'s own close', () => {
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    assert.match(main, /window\.on\('closed', \(\) => \{\s*chat\.closeAll\(\);\s*models\.close\(\);\s*void bridges\.close\(\);/);
    assert.match(main, /app\.on\('before-quit', \(\) => \{\s*chat\.closeAll\(\);\s*models\.close\(\);\s*void bridges\.close\(\);/);
});

// --- the opening act ---------------------------------------------------------------------------------------------------

test('the link carries d=<port>.<code> inside the fragment\'s query, after the ticket', () => {
    assert.equal(bridgeLink('http://127.0.0.1:41234/ui/#/attention?t=TICKET', 50123, 'C'.repeat(22)), `http://127.0.0.1:41234/ui/#/attention?t=TICKET&d=50123.${'C'.repeat(22)}`);
    assert.equal(bridgeLink('http://127.0.0.1:41234/ui/#/attention', 50123, 'C'.repeat(22)), `http://127.0.0.1:41234/ui/#/attention?d=50123.${'C'.repeat(22)}`);
    assert.equal(bridgeLink('http://127.0.0.1:41234/ui/', 50123, 'C'.repeat(22)), `http://127.0.0.1:41234/ui/#/?d=50123.${'C'.repeat(22)}`);
});

test('the opener of the tab opens only a page of the runtime\'s own UI at the runtime\'s origin', () => {
    assert.equal(isOwnPage(`${runtime}/ui/#/attention?t=T&d=50123.${'C'.repeat(22)}`, runtime), true);
    assert.equal(isOwnPage(`${runtime}/ui/`, runtime), true);
    for (const url of [`${runtime}/v1/status`, `${runtime}/uix/`, 'http://127.0.0.1:9999/ui/', 'https://example.com/ui/', `http://user@127.0.0.1:41234/ui/`, 'http://localhost:41234/ui/', 'file:///ui/', 'not a link']) {
        assert.equal(isOwnPage(url, runtime), false, url);
    }
});

test('the opener holds the link itself, to the runtime\'s origin as the run file says it at the moment it opens, and to /ui/', async () => {
    const opened: string[] = [];
    const now = { origin: runtime as string | null };
    const deps = { origin: () => now.origin, open: async (url: string) => void opened.push(url), log: memoryLog() };
    const link = `${runtime}/ui/#/attention?t=T&d=50123.${'C'.repeat(22)}`;
    await openOwnPage(link, deps);
    assert.deepEqual(opened, [link]);
    // A caller that held the link to the origin of a moment ago is not trusted: the runtime moved, the link is refused.
    now.origin = 'http://127.0.0.1:50000';
    await assert.rejects(openOwnPage(link, deps));
    now.origin = null;
    await assert.rejects(openOwnPage(link, deps));
    now.origin = runtime;
    for (const url of [`${runtime}/v1/status`, 'https://example.com/ui/', `${runtime}/uix/`]) await assert.rejects(openOwnPage(url, deps), url);
    assert.deepEqual(opened, [link], 'nothing refused reached the system opener');
});

test('the log names the act of opening and never the link: its two lines have no hole, and the code is in no field', async () => {
    assert.deepEqual(holesOf(templates.own_page_opened), []);
    assert.deepEqual(holesOf(templates.own_page_refused), []);
    const log = memoryLog();
    const code = 'C'.repeat(22);
    await openOwnPage(`${runtime}/ui/#/?t=T&d=1.${code}`, { origin: () => runtime, open: async () => undefined, log });
    await assert.rejects(openOwnPage(`http://127.0.0.1:9/ui/#/?d=1.${code}`, { origin: () => runtime, open: async () => undefined, log }));
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [['own_page_opened', {}], ['own_page_refused', {}]]);
});

test('the code rides in the link\'s fragment and nowhere else: not before the fragment, not to the CLI, not in the log', async (t) => {
    const r = await rig(t);
    const asked: string[][] = [];
    const opened: string[] = [];
    const log = memoryLog();
    const cli = { run: async (args: readonly string[]) => (asked.push([...args]), { exit: 0, stdout: json({ url: `${runtime}/ui/#/attention?t=TICKET` }), stderr: '' }) };
    assert.equal(await openTab({ origin: () => runtime, cli, bridge: async () => r.bridge, open: async (link) => void opened.push(link), log }), null);
    const link = opened[0] ?? '';
    const code = /d=\d+\.([A-Za-z0-9_-]{22})$/.exec(link)?.[1] ?? '';
    assert.equal(code.length, 22);
    assert.equal(link.indexOf(code), link.lastIndexOf(code), 'once in the link');
    assert.ok(link.indexOf(code) > link.indexOf('#'), 'after the fragment begins');
    assert.ok(!JSON.stringify(asked).includes(code), 'never on the CLI\'s command line');
    assert.ok(!JSON.stringify([...log.lines, ...r.log.lines]).includes(code), 'never in the log');
});

test('the tab is opened at the link the CLI printed, held to the runtime\'s origin and /ui/, with a code that pairs once', async (t) => {
    const r = await rig(t);
    const opened: string[] = [];
    const cli = (url: string) => ({ run: async (args: readonly string[]) => (assert.deepEqual(args, ['ui', 'open', '--no-open']), { exit: 0, stdout: json({ url }), stderr: '' }) });
    const deps = { origin: () => runtime, bridge: async () => r.bridge, open: async (link: string) => void opened.push(link) };
    assert.equal(await openTab({ ...deps, cli: cli(`${runtime}/ui/#/attention?t=TICKET`) }), null);
    const link = opened[0] ?? '';
    const d = /[?&]d=(\d+)\.([A-Za-z0-9_-]{22})$/.exec(link);
    assert.ok(d, link);
    assert.equal(Number(d[1]), r.bridge.port);
    assert.ok(link.startsWith(`${runtime}/ui/#/attention?t=TICKET&d=`));
    assert.equal((await ask(r.bridge.port, { path: '/pair', headers: fromPage, body: json({ code: d[2] }) })).status, 200);
    // A link at another origin or outside /ui/ is not opened, nor is anything with no runtime or no bridge.
    for (const url of ['http://127.0.0.1:9999/ui/#/?t=T', `${runtime}/v1/#/?t=T`, 'https://example.com/ui/']) {
        assert.equal(typeof (await openTab({ ...deps, cli: cli(url) })), 'string', url);
    }
    assert.equal(typeof (await openTab({ ...deps, origin: () => null, cli: cli(`${runtime}/ui/#/?t=T`) })), 'string');
    assert.equal(typeof (await openTab({ ...deps, bridge: async () => null, cli: cli(`${runtime}/ui/#/?t=T`) })), 'string');
    assert.equal(typeof (await openTab({ ...deps, cli: { run: async () => ({ exit: 3, stdout: '', stderr: '' }) } })), 'string');
    assert.equal(opened.length, 1);
    // A browser the system could not open is said in a sentence, not thrown at the page.
    const failing = { ...deps, cli: cli(`${runtime}/ui/#/?t=T`), open: () => Promise.reject(new Error('no browser')) };
    assert.equal(await openTab(failing), "The system's browser could not be opened.");
});

// --- what is written ---------------------------------------------------------------------------------------------------

test('no file the bridge writes, its log among them, holds the code or the secret; the log names refusals and never a text', async (t) => {
    const folder = mkdtempSync(join(tmpdir(), 'jason-bridge-log-'));
    t.after(() => rmSync(folder, { recursive: true, force: true }));
    const log = fileLogger(join(folder, 'logs'));
    const calls: Rig['calls'] = [];
    const bridge = await BrowserBridge.open({ origin: () => runtime, members: recording(calls, () => [{ id: 'cht_full', mode: 'full_access' }]), log });
    t.after(() => bridge.close());
    const code = bridge.mint();
    const secret = (JSON.parse((await ask(bridge.port, { path: '/pair', headers: fromPage, body: json({ code }) })).body) as { secret: string }).secret;
    const words = 'a message only the person typed';
    const bearer = { ...fromPage, authorization: `Bearer ${secret}` };
    await ask(bridge.port, { headers: bearer, body: json({ member: 'chat.send', args: ['cht_full', words, {}] }) });
    await ask(bridge.port, { headers: bearer, body: json({ member: words, args: [] }) });
    await ask(bridge.port, { headers: { ...bearer, host: 'evil.example' }, body: json({ member: 'chat.list', args: [] }) });
    await ask(bridge.port, { path: '/pair', headers: fromPage, body: json({ code }) });
    await bridge.close();
    const files: string[] = [];
    const walk = (here: string) => {
        for (const name of readdirSync(here)) {
            const path = join(here, name);
            if (statSync(path).isDirectory()) walk(path);
            else files.push(path);
        }
    };
    walk(folder);
    assert.ok(files.length > 0, 'the log was written');
    for (const file of files) {
        const text = readFileSync(file, 'utf8');
        for (const secretText of [code, secret, words]) assert.ok(!text.includes(secretText), `${file} holds ${secretText}`);
    }
});
