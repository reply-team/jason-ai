// Why a card of a Full access turn asks: the closed code the desktop names it by, read from the two judgements the
// broker made, carried on the card and kept with it, and said by the page in its own words.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { afterEach, test } from 'node:test';
import { fullAccessReasons, fullAccessWhy, isFullAccessWhy, requestTitles, whyOrNone, type FullAccessWhy } from '../src/chat-full-access-why.ts';
import type { HeldReason } from '../src/chat-literal.ts';
import { newConversationId, parseConversation, serialize } from '../src/chat-conversations.ts';
import { PermissionBroker } from '../src/chat-permission-broker.ts';
import { fullAccessJudgement, judge, personsToolTitle, serversTitle, type ChatScope, type Judgement } from '../src/chat-permission-model.ts';

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

const card = (title: string): Extract<Judgement, { verdict: 'ask' }> => ({ verdict: 'ask', title, detail: 'x', remember: null });

/** What the real judgements say of one request, and the code the card names. */
function whyOf(tool: string, input: Record<string, unknown>): FullAccessWhy {
    const judged = judge(tool, input, scope);
    assert.equal(judged.verdict, 'ask', `${tool} is a card in Ask first`);
    const full = fullAccessJudgement(tool, input, scope);
    assert.notEqual(full.outcome, 'refused', `${tool} is not refused in Full access`);
    return fullAccessWhy(full, judged as Extract<Judgement, { verdict: 'ask' }>, tool);
}

test('a held shell command is named by the reason it was held for', () => {
    assert.deepEqual(whyOf('Bash', { command: 'ls *.md' }), { reason: 'command.glob' });
    assert.deepEqual(whyOf('Bash', { command: 'echo $HOME' }), { reason: 'command.expansion' });
    assert.deepEqual(whyOf('Bash', { command: 'claude mcp add x -- y' }), { reason: 'command.servers' });
    assert.deepEqual(whyOf('PowerShell', { command: 'Get-ChildItem' }), { reason: 'command.unknown_shell' });
    for (const reason of fullAccessReasons.filter((each) => each.startsWith('command.'))) {
        const held = reason.slice('command.'.length) as HeldReason;
        assert.deepEqual(fullAccessWhy({ outcome: 'held', message: 'm', reason: held }, card('Run a command'), 'Bash'), { reason });
    }
});

test('any other held request is named by Ask first\'s card for it, and a tool by its name alone', () => {
    assert.deepEqual(whyOf('Write', { file_path: '/Users/someone/work/.claude/settings.json', content: 'x' }), { reason: 'request.settings' });
    assert.deepEqual(whyOf('mcp__someones_server__do_it', { anything: 'at all' }), { reason: 'request.own_tool' });
    assert.deepEqual(whyOf('Frobnicate', { secret: 'never on the card' }), { reason: 'request.unknown_tool', tool: 'Frobnicate' });
    assert.deepEqual(whyOf('Read', {}), { reason: 'request.no_file_named', tool: 'Read' });
    // A tool's name the code would not carry is never carried: the case is said without it.
    assert.deepEqual(fullAccessWhy({ outcome: 'held', message: 'm' }, card('Use a tool name'), 'a tool name'), { reason: 'request.other' });
    assert.deepEqual(fullAccessWhy({ outcome: 'held', message: 'm' }, card('A title no table knows'), 'Bash'), { reason: 'request.other' });
});

test('a card Full access would have run and the host asked about all the same is the host\'s asking', () => {
    assert.deepEqual(fullAccessWhy({ outcome: 'runs' }, card('Run a command'), 'Bash'), { reason: 'host_asked' });
    assert.deepEqual(whyOf('WebFetch', { url: 'https://example.com/a' }), { reason: 'host_asked' });
});

// One row per title: every title Ask first's model writes is one Full access runs, one of the table's, or one that names
// its tool. A title added to the model is red here until it is placed.
const plain = [
    'Run a command',
    'Change a file outside the chat\'s folders',
    'Read a file outside the chat\'s folders',
    'Search a folder outside the chat\'s folders',
    'Find files in a folder outside the chat\'s folders',
    'List a folder outside the chat\'s folders',
    'Open a web page',
];
const tabled: [string, string][] = [
    [personsToolTitle, 'request.own_tool'],
    ['Change the AI\'s own settings', 'request.settings'],
    ['Change a repository\'s own files', 'request.repository'],
    ['Change a folder\'s Jason marker', 'request.marker'],
    ['Change the skills or settings the AI loads', 'request.loads'],
    ['Change instructions the AI reads', 'request.instructions'],
    [serversTitle, 'request.server_list'],
    ['Write or run a jason that is not the desktop\'s', 'request.local_jason'],
    ['Change where the shell finds programs', 'request.search'],
    ['Use a path Windows reads as another name', 'request.windows_name'],
    ['Use a path that begins with ~', 'request.tilde_path'],
];

test('every card title the permission model writes is placed: run by Full access, in the table, or naming its tool', () => {
    assert.deepEqual([...requestTitles], tabled);
    const model = readFileSync(new URL('../src/chat-permission-model.ts', import.meta.url), 'utf8');
    const literal = (text: string) => text.replace(/\\'/g, '\'');
    const written = new Set<string>();
    for (const match of model.matchAll(/(?:title: |Title = |: )'((?:[^'\\]|\\.)*)'/g)) {
        const title = literal(match[1] ?? '');
        if (/^[A-Z]/.test(title) && /^(Run|Change|Read|Search|Find|List|Open|Use|Write) /.test(title)) written.add(title);
    }
    assert.ok(written.size >= plain.length + tabled.length - 1, 'the titles were read from the model');
    for (const title of written) assert.ok(plain.includes(title) || requestTitles.has(title), `"${title}" is placed`);
    assert.ok(model.includes('title: `Use ${tool}`'), 'a tool it does not know names its tool');
    assert.ok(model.includes('title: `Use ${tool} with no file named`'), 'a file tool given no file names its tool');
});

test('a kept card\'s why is one the table can say, and nothing else', () => {
    for (const reason of fullAccessReasons) {
        const why = reason === 'request.no_file_named' || reason === 'request.unknown_tool' ? { reason, tool: 'Frobnicate' } : { reason };
        assert.ok(isFullAccessWhy(why), reason);
    }
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
    const kept = { ...base, entries: [{ kind: 'permission' as const, title: 'Full access asks about this one', detail: 'ls *.md', answer: null, why: { reason: 'command.glob' as const } }] };
    assert.deepEqual(parseConversation(serialize(kept), id)?.entries, kept.entries);
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

test('a Full access card says why it asks; an Ask first card says nothing of Full access', async () => {
    const full = bench('full_access');
    void full.broker.ask('turn_a', 'Bash', { command: 'ls *.md' });
    void full.broker.ask('turn_a', 'Frobnicate', { secret: 'never on the card' });
    await settle();
    assert.deepEqual(full.sent.map((event) => [event.title, event.why]), [
        ['Full access asks about this one', { reason: 'command.glob' }],
        ['Full access asks about this one', { reason: 'request.unknown_tool', tool: 'Frobnicate' }],
    ]);
    // The why carries the tool's name and nothing else of the request.
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

test('a failure while the why is worked out leaves the card without one, never without the card', () => {
    assert.deepEqual(whyOrNone(() => ({ reason: 'command.glob' })), { why: { reason: 'command.glob' } });
    assert.deepEqual(
        whyOrNone(() => {
            throw new Error('no why');
        }),
        {},
    );
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
    assert.deepEqual(sent.map((event) => event.why?.reason), ['command.glob', 'request.unknown_tool'], 'the cards carry their why');
    broker.answer('chat_a', 'req_1', { allow: true, always: false });
    broker.answer('chat_a', 'req_2', { allow: false, always: false });
    said.push(await allowed, await refused, ...decided);
    const given = JSON.stringify(said);
    assert.ok(!/"why"|command\.|request\.|host_asked/.test(given), given);
    for (const sentence of sentences) assert.ok(!given.includes(sentence.replace('{tool}', 'Frobnicate')), sentence);
});
