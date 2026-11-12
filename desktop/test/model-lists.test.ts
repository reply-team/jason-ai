// The models Codex offers, as the desktop keeps and asks for them: shown at once from what is kept, asked again at most
// once a day per host id and one ask at a time, a failed ask keeping the last list; and one ask end to end against a
// stand-in `codex app-server`, started as a turn is started.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { setTimeout as delay } from 'node:timers/promises';
import { join } from 'node:path';
import { test } from 'node:test';
import type { ModelOffer } from '../src/chat-models.ts';
import { askAgainMs, askEveryMs, codexAsker, keptFileLimit, ModelLists, type AskResult } from '../src/model-lists.ts';
import { bench, type Bench } from './harness.ts';

const sol: ModelOffer = { id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', efforts: ['low', 'medium', 'high'] };
const luna: ModelOffer = { id: 'gpt-6-luna', label: 'GPT-6 Luna', efforts: ['max'] };
const start = Date.parse('2026-11-11T10:00:00.000Z');

/** An ask the row answers by hand, so it can see what runs at once. */
function asks() {
    const pending: { hostId: string; answer(result: AskResult): void }[] = [];
    const asked: string[] = [];
    return {
        pending,
        asked,
        ask: (hostId: string) =>
            new Promise<AskResult>((resolve) => {
                asked.push(hostId);
                pending.push({ hostId, answer: resolve });
            }),
    };
}

const tick = () => new Promise((resolve) => setImmediate(resolve));
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
        await delay(50);
    }
    return check();
}

function folder(t: { after(fn: () => void): void }): Bench {
    return bench(t);
}

test('a kept list is shown at once, before and whatever any ask does', (t) => {
    const b = folder(t);
    mkdirSync(b.paths.desktop, { recursive: true });
    writeFileSync(b.paths.models, JSON.stringify({ hosts: { codex: { asked_at: new Date(start).toISOString(), models: [sol] } } }));
    const fake = asks();
    const lists = new ModelLists({ file: b.paths.models, ask: fake.ask, now: () => start + 1000 });
    assert.deepEqual(lists.lists().get('codex')?.models, [sol]);
    assert.equal(lists.lists().get('codex-api-key'), undefined);
});

test('a list is asked again at most once a day per host id, one ask at a time across the desktop', async (t) => {
    const b = folder(t);
    let now = start;
    const fake = asks();
    let changed = 0;
    const listed: [string, number][] = [];
    const lists = new ModelLists({ file: b.paths.models, ask: fake.ask, now: () => now, changed: () => void changed++, listed: (hostId, count) => void listed.push([hostId, count]) });
    lists.refresh(['codex', 'codex-api-key', 'claude', 'gemini']);
    lists.refresh(['codex']);
    await tick();
    assert.deepEqual(fake.asked, ['codex'], 'one ask at a time, and only for a host whose list is asked');
    fake.pending.shift()?.answer({ models: [sol] });
    await tick();
    await tick();
    assert.deepEqual(fake.asked, ['codex', 'codex-api-key']);
    assert.deepEqual(lists.lists().get('codex')?.models, [sol]);
    assert.equal(changed, 1);
    fake.pending.shift()?.answer({ models: [luna] });
    await lists.settled();
    assert.equal(changed, 2);
    assert.deepEqual(listed, [['codex', 1], ['codex-api-key', 1]]);
    // Within the day nothing is asked again; once it has passed, the host is asked again.
    now = start + askEveryMs - 1;
    lists.refresh(['codex', 'codex-api-key']);
    await lists.settled();
    assert.deepEqual(fake.asked, ['codex', 'codex-api-key']);
    now = start + askEveryMs + 1;
    lists.refresh(['codex']);
    await tick();
    assert.deepEqual(fake.asked, ['codex', 'codex-api-key', 'codex']);
    fake.pending.shift()?.answer({ models: [luna] });
    await lists.settled();
    // What was asked is kept in the file and read back by the next start.
    const again = new ModelLists({ file: b.paths.models, ask: fake.ask, now: () => now });
    assert.deepEqual(again.lists().get('codex')?.models, [luna]);
    assert.deepEqual(again.lists().get('codex-api-key')?.models, [luna]);
});

test('a failed ask keeps the last list read and is said by its kind alone; only a list read counts for the day', async (t) => {
    const b = folder(t);
    let now = start;
    const failures: [string, string][] = [];
    const results: AskResult[] = [{ models: [sol] }, { failed: 'bound' }, { failed: 'refused' }, { models: [luna] }];
    const asked: string[] = [];
    const lists = new ModelLists({
        file: b.paths.models,
        ask: async (hostId) => {
            asked.push(hostId);
            return results.shift() ?? { failed: 'refused' };
        },
        now: () => now,
        failed: (hostId, kind) => void failures.push([hostId, kind]),
    });
    lists.refresh(['codex']);
    await lists.settled();
    // A list read stands for the day.
    now += askEveryMs - 1;
    lists.refresh(['codex']);
    await lists.settled();
    assert.deepEqual(asked, ['codex']);
    now += 1;
    lists.refresh(['codex']);
    await lists.settled();
    assert.deepEqual(lists.lists().get('codex'), { askedAt: new Date(start).toISOString(), models: [sol] }, 'the last list read stays, with its moment');
    assert.deepEqual(failures, [['codex', 'bound']]);
    // A failure is not asked again within the hour, and is once the hour has passed.
    now += askAgainMs - 1;
    lists.refresh(['codex']);
    await lists.settled();
    assert.deepEqual(asked, ['codex', 'codex']);
    now += 1;
    lists.refresh(['codex']);
    await lists.settled();
    assert.deepEqual(asked, ['codex', 'codex', 'codex']);
    assert.deepEqual(failures, [['codex', 'bound'], ['codex', 'refused']]);
    assert.equal(lists.unread('codex'), null, 'a list is kept, so the host offers it');
    // An ask that throws is a failed one too, and the desktop goes on.
    const throwing = new ModelLists({ file: join(b.root, 'other.json'), ask: () => Promise.reject(new Error('no')), now: () => now, failed: (hostId, kind) => void failures.push([hostId, kind]) });
    assert.equal(throwing.unread('codex'), 'waiting');
    throwing.refresh(['codex']);
    await throwing.settled();
    assert.deepEqual(failures.at(-1), ['codex', 'start']);
    assert.equal(throwing.lists().get('codex'), undefined, 'a failure keeps no list where none was read');
    assert.equal(throwing.unread('codex'), 'failed');
    assert.equal(throwing.unread('gemini'), null);
});

test('a failure where no list was ever read is asked again at the next start, and a success is not asked again within the day', async (t) => {
    const b = folder(t);
    const asked: string[] = [];
    const first = new ModelLists({ file: b.paths.models, ask: async (hostId) => (asked.push(hostId), { failed: 'ended' }), now: () => start });
    first.refresh(['codex']);
    await first.settled();
    first.refresh(['codex']);
    await first.settled();
    assert.deepEqual(asked, ['codex']);
    assert.equal(existsSync(b.paths.models), false, 'nothing of a failure is kept on disk');
    // The next start, a minute later: asked again, and this time read.
    const second = new ModelLists({ file: b.paths.models, ask: async (hostId) => (asked.push(hostId), { models: [sol] }), now: () => start + 60_000 });
    second.refresh(['codex']);
    await second.settled();
    assert.deepEqual(asked, ['codex', 'codex']);
    // The start after that, within the day: what was read stands and nothing is asked.
    const third = new ModelLists({ file: b.paths.models, ask: async (hostId) => (asked.push(hostId), { models: [luna] }), now: () => start + askEveryMs / 2 });
    third.refresh(['codex']);
    await third.settled();
    assert.deepEqual(asked, ['codex', 'codex']);
    assert.deepEqual(third.lists().get('codex')?.models, [sol]);
});

test('closing the lists ends a running ask as a turn is ended and drops the ones that wait; nothing is asked after', async (t) => {
    const b = folder(t);
    const asked: string[] = [];
    const signals: AbortSignal[] = [];
    let failures = 0;
    const lists = new ModelLists({
        file: b.paths.models,
        ask: (hostId, signal) =>
            new Promise<AskResult>((resolve) => {
                asked.push(hostId);
                signals.push(signal);
                signal.addEventListener('abort', () => resolve({ failed: 'closed' }));
            }),
        now: () => start,
        failed: () => void failures++,
    });
    lists.refresh(['codex', 'codex-api-key']);
    await tick();
    assert.deepEqual(asked, ['codex']);
    lists.close();
    await lists.settled();
    assert.equal(signals[0]?.aborted, true);
    assert.deepEqual(asked, ['codex'], 'the ask that waited is dropped');
    assert.equal(failures, 0, 'a close is no failure of the host');
    lists.refresh(['codex']);
    await lists.settled();
    assert.deepEqual(asked, ['codex']);
});

test('a list the file holds is not shown unless it was read whole, and a file that cannot be written leaves the lists in memory', async (t) => {
    const b = folder(t);
    mkdirSync(b.paths.desktop, { recursive: true });
    writeFileSync(b.paths.models, '{"hosts": {"codex": {"asked_at": "2026-11-11T10:00:00.000Z", "models": [');
    const lists = new ModelLists({ file: b.paths.models, ask: async () => ({ models: [sol] }), now: () => start });
    assert.equal(lists.lists().size, 0);
    // A folder where the file goes: the write fails, the list asked is still shown, and nothing is asked twice.
    const blocked = join(b.root, 'blocked.json');
    mkdirSync(blocked);
    let count = 0;
    const kept = new ModelLists({ file: blocked, ask: async () => (count++, { models: [sol] }), now: () => start });
    kept.refresh(['codex']);
    await kept.settled();
    kept.refresh(['codex']);
    await kept.settled();
    assert.deepEqual(kept.lists().get('codex')?.models, [sol]);
    assert.equal(count, 1);
});

test('a kept file larger than two hundred models a host could reach is not read at all', (t) => {
    const b = folder(t);
    mkdirSync(b.paths.desktop, { recursive: true });
    const one = { id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', efforts: ['low'] };
    writeFileSync(b.paths.models, JSON.stringify({ hosts: { codex: { asked_at: new Date(start).toISOString(), models: [one] } } }) + ' '.repeat(keptFileLimit));
    assert.equal(new ModelLists({ file: b.paths.models, ask: async () => ({ models: [] }), now: () => start }).lists().size, 0);
    writeFileSync(b.paths.models, JSON.stringify({ hosts: { codex: { asked_at: new Date(start).toISOString(), models: [one] } } }));
    assert.equal(new ModelLists({ file: b.paths.models, ask: async () => ({ models: [] }), now: () => start }).lists().size, 1);
});

const page = (entries: unknown[]) => entries;
const listed = [
    { id: 'gpt-6.1-sol', model: 'gpt-6.1-sol', displayName: 'GPT-6.1 Sol', hidden: false, isDefault: true, supportedReasoningEfforts: [{ reasoningEffort: 'low' }, { reasoningEffort: 'ultra' }] },
];

function asker(b: Bench, keys: Record<string, string> = {}, limitMs?: number) {
    const bundle = join(b.root, 'bundle');
    mkdirSync(bundle, { recursive: true });
    return codexAsker({ paths: b.paths, env: b.env, bundle, dataNamed: true, bundleVersion: '0.35.0', keyFor: (id) => keys[id], ...(limitMs === undefined ? {} : { limitMs }) });
}

test('an ask starts codex app-server in the chat folder with no message, as a turn is started, and reads every page', async (t) => {
    const b = bench(t, { programs: ['codex'], env: { OPENAI_API_KEY: 'sk-inherited', CLAUDE_AUTO_BACKGROUND_TASKS: '1' } });
    const log = join(b.root, 'app-server.log');
    b.env.STUB_APP_SERVER = JSON.stringify({ pages: [page(listed), page([{ id: 'gpt-6-luna', model: 'gpt-6-luna', displayName: 'GPT-6 Luna', hidden: false, supportedReasoningEfforts: [{ reasoningEffort: 'max' }] }])] });
    b.env.STUB_APP_SERVER_LOG = log;
    const result = await asker(b)('codex');
    assert.deepEqual(result, { models: [{ id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', efforts: ['low'] }, { id: 'gpt-6-luna', label: 'GPT-6 Luna', efforts: ['max'] }] });
    const [run] = b.record().filter((each) => each.name === 'codex');
    assert.deepEqual(run?.args, ['app-server']);
    assert.equal(run?.cwd.toLowerCase(), b.paths.chat.toLowerCase());
    assert.equal(run?.env.JASON_CONFINED, '1');
    assert.equal(run?.env.OPENAI_API_KEY, 'sk-inherited', "a plain ask keeps the person's own configuration, as a plain turn does");
    assert.ok(!('CLAUDE_AUTO_BACKGROUND_TASKS' in (run?.env ?? {})));
    const said = readFileSync(log, 'utf8').split('\n').filter(Boolean);
    assert.deepEqual(said.map((line) => (line.startsWith('{') ? (JSON.parse(line) as { method: string }).method : line)), ['initialize', 'initialized', 'model/list', 'model/list', '<stdin closed>']);
});

test('an ask on the OpenAI key runs in the desktop\'s own Codex folder with the key, and none of the credentials the desktop inherited', async (t) => {
    const b = bench(t, { programs: ['codex'], env: { OPENAI_API_KEY: 'sk-inherited', CODEX_HOME: '/elsewhere' } });
    b.env.STUB_APP_SERVER = JSON.stringify({ pages: [page(listed)] });
    assert.deepEqual(await asker(b)('codex-api-key'), { failed: 'no_key' });
    assert.equal(b.record().length, 0, 'no key, nothing started');
    const result = await asker(b, { 'codex-api-key': 'sk-openai-0123456789' })('codex-api-key');
    assert.deepEqual(result, { models: [{ id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', efforts: ['low'] }] });
    const [run] = b.record().filter((each) => each.name === 'codex');
    assert.equal(run?.env.CODEX_API_KEY, 'sk-openai-0123456789');
    assert.equal(run?.env.CODEX_HOME, b.paths.codexKeyed);
    assert.ok(!('OPENAI_API_KEY' in (run?.env ?? {})));
    assert.ok(existsSync(b.paths.codexKeyed));
    assert.deepEqual(await asker(b)('claude'), { failed: 'not_listed' });
});

test('an ask that is refused, answered with no list, left early or never answered keeps nothing, and is ended at its bound', async (t) => {
    const b = bench(t, { programs: ['codex'] });
    b.env.STUB_APP_SERVER = JSON.stringify({ refuse: true });
    assert.deepEqual(await asker(b)('codex'), { failed: 'refused' });
    b.env.STUB_APP_SERVER = JSON.stringify({ garbage: true });
    assert.deepEqual(await asker(b)('codex'), { failed: 'unreadable' });
    b.env.STUB_APP_SERVER = JSON.stringify({ leave: true });
    assert.deepEqual(await asker(b)('codex'), { failed: 'ended' });
    const pidFile = join(b.root, 'app-server.pid');
    b.env.STUB_APP_SERVER = JSON.stringify({ hang: true, pidFile });
    const began = Date.now();
    assert.deepEqual(await asker(b, {}, 1500)('codex'), { failed: 'bound' });
    assert.ok(Date.now() - began < 10_000);
    assert.ok(await settle(() => gone(Number(readFileSync(pidFile, 'utf8')))), 'the stand-in that ignored its end is gone');
});

test('a listing past two hundred models, or with a line past 1 MB, is not read', async (t) => {
    const b = bench(t, { programs: ['codex'] });
    const many = Array.from({ length: 201 }, (_, at) => ({ id: `m-${at}`, model: `m-${at}`, displayName: `M ${at}`, hidden: false, supportedReasoningEfforts: [] }));
    b.env.STUB_APP_SERVER = JSON.stringify({ pages: [many.slice(0, 150), many.slice(150)] });
    assert.deepEqual(await asker(b)('codex'), { failed: 'unreadable' });
    b.env.STUB_APP_SERVER = JSON.stringify({ pages: [many.slice(0, 200)] });
    assert.equal(((await asker(b)('codex')) as { models: unknown[] }).models.length, 200);
    b.env.STUB_APP_SERVER = JSON.stringify({ long: 1024 * 1024 + 1 });
    assert.deepEqual(await asker(b)('codex'), { failed: 'unreadable' });
});

test('quitting while an ask runs ends the stand-in, one that ignores the end of its input included: nothing of it is left', async (t) => {
    const b = bench(t, { programs: ['codex'] });
    const pidFile = join(b.root, 'app-server.pid');
    b.env.STUB_APP_SERVER = JSON.stringify({ hang: true, pidFile });
    const lists = new ModelLists({ file: b.paths.models, ask: asker(b), now: () => start });
    lists.refresh(['codex']);
    assert.ok(await settle(() => existsSync(pidFile) && readFileSync(pidFile, 'utf8') !== ''));
    lists.close();
    await lists.settled();
    assert.ok(await settle(() => gone(Number(readFileSync(pidFile, 'utf8')))), 'the listing was ended');
    assert.equal(lists.lists().size, 0);
});

test('an ask for a Codex that is not on the PATH starts nothing', async (t) => {
    const b = bench(t, { programs: [] });
    assert.deepEqual(await asker(b)('codex'), { failed: 'not_found' });
    assert.equal(b.record().length, 0);
});
