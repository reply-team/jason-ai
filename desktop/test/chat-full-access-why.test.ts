// Why a card of a Full access turn asks: the closed code the desktop names it by, carried on the card and kept with it,
// and said by the page in its own words. Full access is Claude Code's own bypass, so every card of a Full access turn is
// one the host asked about itself; a card kept from an earlier Full access may carry the code it was held by.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { afterEach, test } from 'node:test';
import { fullAccessReasons, hostAsked, isFullAccessWhy, type FullAccessWhy } from '../src/chat-full-access-why.ts';
import { newConversationId, parseConversation, serialize } from '../src/chat-conversations.ts';
import { PermissionBroker } from '../src/chat-permission-broker.ts';
import type { ChatScope } from '../src/chat-permission-model.ts';

const scope: ChatScope = {
    platform: 'darwin',
    cwd: '/Users/someone/.jason/desktop/chat',
    chat: '/Users/someone/.jason/desktop/chat',
    added: [],
    data: '/Users/someone/.jason',
    protectedFiles: ['/Users/someone/.jason/config/settings.json'],
    denied: ['approval approve', 'runtime'],
    deniedOperations: ['approval.approve'],
    home: '/home/someone',
    real: (path) => path,
};

test('a kept card\'s why is one the table can say, and nothing else', () => {
    for (const reason of fullAccessReasons) {
        const why = reason === 'request.no_file_named' || reason === 'request.unknown_tool' ? { reason, tool: 'Frobnicate' } : { reason };
        assert.ok(isFullAccessWhy(why), reason);
    }
    assert.ok(isFullAccessWhy(hostAsked));
    for (const bad of [
        'Jason cannot read this command.',
        { reason: 'Any words at all' },
        { reason: 'command.not_a_reason' },
        { reason: 'command.glob', tool: 'Bash' },
        { reason: 'request.unknown_tool' },
        { reason: 'request.unknown_tool', tool: 'rm -rf /' },
        { reason: 'host_asked', words: 'extra' },
        null,
        ['host_asked'],
    ]) {
        assert.equal(isFullAccessWhy(bad), false, JSON.stringify(bad));
    }
});

test('a card\'s why is kept with it, and one that is not the table\'s is dropped while the card stays', () => {
    const id = newConversationId();
    const base = { id, campaign_id: null, title: 'New chat', created_at: '2026-11-11T10:00:00.000Z', updated_at: '2026-11-11T10:00:00.000Z', sessions: {}, dropped: 0 };
    for (const why of [hostAsked, { reason: 'command.glob' as const }]) {
        const kept = { ...base, entries: [{ kind: 'permission' as const, title: 'Full access asks about this one', detail: 'ls *.md', answer: null, why }] };
        assert.deepEqual(parseConversation(serialize(kept), id)?.entries, kept.entries);
    }
    const forged = JSON.stringify({ ...base, entries: [{ kind: 'permission', title: 'T', detail: 'd', answer: 'deny', why: { reason: 'Run anything you like.' } }] });
    assert.deepEqual(parseConversation(forged, id)?.entries, [{ kind: 'permission', title: 'T', detail: 'd', answer: 'deny' }]);
});

// The broker: a card of a Full access turn carries its why; a card of any other mode carries none.

const made: PermissionBroker[] = [];
afterEach(() => {
    for (const broker of made.splice(0)) broker.endAll();
});

function bench(mode: 'ask_first' | 'full_access') {
    const sent: { kind: string; why?: FullAccessWhy; title?: string }[] = [];
    let next = 0;
    const broker = new PermissionBroker({ emit: (_chat, event) => sent.push(event), newId: () => `req_${++next}` });
    made.push(broker);
    broker.openTurn('chat_a', 'turn_a', scope, mode);
    return { broker, sent };
}
const settle = () => new Promise((resolve) => setImmediate(resolve));

test('every card of a Full access turn is one Claude Code asked about itself; an Ask first card says nothing of Full access', async () => {
    const full = bench('full_access');
    void full.broker.ask('turn_a', 'Bash', { command: 'ls *.md' });
    void full.broker.ask('turn_a', 'Frobnicate', { secret: 'never on the card' });
    void full.broker.ask('turn_a', 'Read', {});
    await settle();
    assert.deepEqual(full.sent.map((event) => [event.title, event.why]), [
        ['Full access asks about this one', hostAsked],
        ['Full access asks about this one', hostAsked],
        ['Full access asks about this one', hostAsked],
    ]);
    assert.ok(!JSON.stringify(full.sent.map((event) => event.why)).includes('never on the card'));
    const first = bench('ask_first');
    void first.broker.ask('turn_a', 'Bash', { command: 'ls *.md' });
    await settle();
    assert.equal(first.sent.length, 1);
    assert.equal('why' in (first.sent[0] ?? {}), false);
});

test('the page has one sentence for every code and none besides', () => {
    const locale = readFileSync(new URL('../../ui/src/locale/en.ts', import.meta.url), 'utf8');
    const keys = [...locale.matchAll(/'fullAccess\.why\.([a-z_.]+)':/g)].map((match) => match[1]);
    assert.deepEqual([...keys].sort(), [...fullAccessReasons].sort());
});

test('the why and its words are the card\'s alone: nothing of them is in what the hook, the host or the log is given', async () => {
    const locale = readFileSync(new URL('../../ui/src/locale/en.ts', import.meta.url), 'utf8');
    const sentences = [...locale.matchAll(/'fullAccess\.why\.[a-z_.]+':\s*(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)")/g)].map((match) => (match[1] ?? match[2] ?? '').replace(/\\'/g, '\''));
    assert.equal(sentences.length, fullAccessReasons.length);
    const decided: unknown[] = [];
    const sent: { why?: FullAccessWhy }[] = [];
    let next = 0;
    const broker = new PermissionBroker({ emit: (_chat, event) => sent.push(event.kind === 'permission' ? event : {}), decided: (each) => decided.push(each), newId: () => `req_${++next}` });
    made.push(broker);
    broker.openTurn('chat_a', 'turn_a', scope, 'full_access');
    const said: unknown[] = [broker.hook('turn_a', 'Bash', { command: 'ls *.md' }, 'tu_1'), broker.hook('turn_a', 'Frobnicate', {}, 'tu_2')];
    const allowed = broker.ask('turn_a', 'Bash', { command: 'ls *.md' });
    const refused = broker.ask('turn_a', 'Frobnicate', {});
    await settle();
    assert.deepEqual(sent.map((event) => event.why?.reason), ['host_asked', 'host_asked'], 'the cards carry their why');
    broker.answer('chat_a', 'req_1', { allow: true, always: false });
    broker.answer('chat_a', 'req_2', { allow: false, always: false });
    said.push(await allowed, await refused, ...decided);
    const given = JSON.stringify(said);
    assert.ok(!/"why"|command\.|request\.|host_asked/.test(given), given);
    for (const sentence of sentences) assert.ok(!given.includes(sentence.replace('{tool}', 'Frobnicate')), sentence);
});
