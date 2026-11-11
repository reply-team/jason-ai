// Which models each chat host offers and which effort each takes, the list kept of what Codex offers, and how that list
// is asked of `codex app-server`: all pure, so each rule is shown here without a host.

import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
    claudeCodeModels,
    currentModel,
    effortRefusal,
    efforts,
    isEffort,
    keptListsText,
    ListingExchange,
    listedHosts,
    modelLimit,
    modelRefusal,
    offeredModels,
    parseKeptLists,
    type KeptList,
} from '../src/chat-models.ts';
import { keyProviders } from '../src/providers-model.ts';

const all = ['low', 'medium', 'high', 'xhigh', 'max'];
const lists = (entries: [string, KeptList][] = []) => new Map(entries);
const codexList: KeptList = {
    askedAt: '2026-11-11T10:00:00.000Z',
    models: [
        { id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', efforts: ['low', 'medium', 'high'] },
        { id: 'gpt-6-luna', label: 'GPT-6 Luna', efforts: [] },
    ],
};

test('effort is one closed word of five levels, never ultracode or ultra', () => {
    assert.deepEqual([...efforts], all);
    for (const word of all) assert.equal(isEffort(word), true);
    for (const word of ['ultracode', 'ultra', 'LOW', 'none', 'minimal', '', ' low', null, 3, ['low']]) assert.equal(isEffort(word), false, String(word));
});

test("Claude Code offers the vendor's four aliases; haiku takes effort only from 2.1.293, the others always", () => {
    const at = (version: string | null) => Object.fromEntries(claudeCodeModels(version).map((model) => [model.id, model.efforts]));
    assert.deepEqual(Object.keys(at('2.1.293')), ['opus', 'sonnet', 'haiku', 'fable']);
    assert.deepEqual(at('2.1.293'), { opus: all, sonnet: all, haiku: all, fable: all });
    assert.deepEqual(at('2.1.300').haiku, all);
    assert.deepEqual(at('2.1.292').haiku, []);
    assert.deepEqual(at('2.1.293-beta.1').haiku, []);
    assert.deepEqual(at(null).haiku, []);
    assert.deepEqual(at('2.1.257').opus, all);
});

test('the hosts offer their models: Claude Code and the Anthropic key the aliases and a typed name, Codex and the OpenAI key the list kept, the other keys their table', () => {
    const kept = lists([
        ['codex', codexList],
        ['codex-api-key', { askedAt: codexList.askedAt, models: [{ id: 'gpt-6-luna', label: 'GPT-6 Luna', efforts: ['high'] }] }],
    ]);
    const ids = (host: string) => offeredModels(host, kept, '2.1.300').models.map((model) => model.id);
    assert.deepEqual(ids('claude'), ['opus', 'sonnet', 'haiku', 'fable']);
    assert.deepEqual(ids('claude-api-key'), ['opus', 'sonnet', 'haiku', 'fable']);
    assert.equal(offeredModels('claude', kept, null).typed, true);
    assert.equal(offeredModels('claude-api-key', kept, null).typed, true);
    assert.deepEqual(ids('codex'), ['gpt-6.1-sol', 'gpt-6-luna']);
    assert.deepEqual(ids('codex-api-key'), ['gpt-6-luna']);
    assert.equal(offeredModels('codex', kept, null).typed, false);
    assert.deepEqual(offeredModels('codex', lists(), null).models, []);
    assert.deepEqual(ids('deepseek'), ['deepseek-v4-pro', 'deepseek-flash']);
    assert.deepEqual(ids('moonshot'), ['kimi-k3']);
    assert.deepEqual(ids('zai'), ['glm-5.3', 'glm-5.3-flash']);
    assert.deepEqual(ids('minimax'), ['MiniMax-M3.1-Flash']);
    for (const host of ['deepseek', 'moonshot', 'zai', 'minimax', 'gemini', 'unknown']) assert.equal(offeredModels(host, kept, null).typed, false, host);
    assert.deepEqual(offeredModels('gemini', kept, null).models, []);
});

test("a key provider shows effort only where the vendor's guide names levels: MiniMax five, DeepSeek three, Kimi and GLM none", () => {
    const levels = (host: string) => [...new Set(offeredModels(host, lists(), null).models.map((model) => model.efforts.join(',')))];
    assert.deepEqual(levels('minimax'), [all.join(',')]);
    assert.deepEqual(levels('deepseek'), ['low,high,max']);
    assert.deepEqual(levels('moonshot'), ['']);
    assert.deepEqual(levels('zai'), ['']);
    for (const provider of keyProviders) for (const effort of provider.efforts ?? []) assert.equal(isEffort(effort), true, provider.id);
});

test("DeepSeek's retired name is read as the name its page gives now, for DeepSeek alone", () => {
    assert.equal(currentModel('deepseek', 'deepseek-v4-flash'), 'deepseek-flash');
    assert.equal(currentModel('deepseek', 'deepseek-v4-pro'), 'deepseek-v4-pro');
    assert.equal(currentModel('claude', 'deepseek-v4-flash'), 'deepseek-v4-flash');
    assert.equal(currentModel('deepseek', undefined), undefined);
    assert.ok(!keyProviders.some((provider) => provider.models.includes('deepseek-v4-flash')));
});

test('an effort is refused, in one sentence, unless the named model takes it by the table or the list kept', () => {
    const kept = lists([['codex', codexList]]);
    const refused = 'That effort is not one this model takes.';
    assert.equal(effortRefusal('claude', 'opus', 'xhigh', kept, '2.1.300'), null);
    assert.equal(effortRefusal('claude', 'haiku', 'low', kept, '2.1.300'), null);
    assert.equal(effortRefusal('claude', 'haiku', 'low', kept, '2.1.292'), refused);
    assert.equal(effortRefusal('claude', 'claude-opus-5-5', 'low', kept, '2.1.300'), refused, 'a typed name takes no effort');
    assert.equal(effortRefusal('claude', undefined, 'low', kept, '2.1.300'), refused, "the host's default model is not known to take one");
    assert.equal(effortRefusal('codex', 'gpt-6.1-sol', 'high', kept, null), null);
    assert.equal(effortRefusal('codex', 'gpt-6.1-sol', 'max', kept, null), refused);
    assert.equal(effortRefusal('codex', 'gpt-6-luna', 'low', kept, null), refused);
    assert.equal(effortRefusal('codex-api-key', 'gpt-6.1-sol', 'low', kept, null), refused, 'each host id has a list of its own');
    assert.equal(effortRefusal('deepseek', 'deepseek-flash', 'max', kept, null), null);
    assert.equal(effortRefusal('deepseek', 'deepseek-flash', 'medium', kept, null), refused);
    assert.equal(effortRefusal('moonshot', 'kimi-k3', 'max', kept, null), refused);
    assert.equal(effortRefusal('minimax', 'MiniMax-M3.1-Flash', 'xhigh', kept, null), null);
});

test('a kept list is read whole or not at all, and an entry is kept only when it passes the shapes', () => {
    const text = JSON.stringify({
        hosts: {
            codex: {
                asked_at: '2026-11-11T10:00:00.000Z',
                models: [
                    { id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', efforts: ['low', 'ultra', 'minimal', 'high', 'high'] },
                    { id: '-rm', label: 'option-shaped', efforts: [] },
                    { id: 'with space', label: 'x', efforts: [] },
                    { id: 'gpt-6-luna', label: 'Luna\u0007\u202e\nnext line', efforts: 'high' },
                    { id: 'gpt-6-luna', label: 'twice', efforts: [] },
                    { id: 'gpt-6-mini', label: 42, efforts: [] },
                ],
            },
            'codex-api-key': { asked_at: 'not a moment', models: [] },
            gemini: { asked_at: '2026-11-11T10:00:00.000Z', models: [{ id: 'gemini-9', label: 'Gemini 9', efforts: [] }] },
        },
    });
    const read = parseKeptLists(text);
    assert.deepEqual([...read.keys()], ['codex']);
    assert.deepEqual(read.get('codex')?.models, [
        { id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', efforts: ['low', 'high'] },
        { id: 'gpt-6-luna', label: 'Luna next line', efforts: [] },
        { id: 'gpt-6-mini', label: 'gpt-6-mini', efforts: [] },
    ]);
    for (const broken of ['', '{', '[]', 'null', '{"hosts":[]}', JSON.stringify({ hosts: { codex: { asked_at: '2026-11-11T10:00:00.000Z', models: {} } } })]) {
        assert.equal(parseKeptLists(broken).size, 0, broken);
    }
    assert.deepEqual([...listedHosts], ['codex', 'codex-api-key']);
});

test('a kept list is written as it is read back', () => {
    const kept = lists([['codex', codexList]]);
    assert.deepEqual(parseKeptLists(keptListsText(kept)), kept);
});

/** Runs an exchange against a scripted server: each request it sends is answered by `answer`. */
function exchange(answer: (message: { id?: number; method: string; params?: Record<string, unknown> }) => unknown[]) {
    const sent: { id?: number; method: string; params?: Record<string, unknown> }[] = [];
    const ask = new ListingExchange('0.35.0');
    let outcome = ask.outcome;
    const queue = [...ask.start()];
    while (queue.length > 0 && outcome === undefined) {
        const line = queue.shift()!;
        const message = JSON.parse(line) as { id?: number; method: string; params?: Record<string, unknown> };
        assert.ok(line.endsWith('\n') === false && !line.includes('\n'));
        sent.push(message);
        for (const reply of answer(message)) {
            queue.push(...ask.receive(typeof reply === 'string' ? reply : JSON.stringify(reply)));
            outcome = ask.outcome;
            if (outcome !== undefined) break;
        }
    }
    return { sent, outcome: ask.outcome };
}

const page = (id: number | undefined, data: unknown[], nextCursor: string | null) => ({ id, result: { data, nextCursor } });
const sol = { id: 'gpt-6.1-sol', model: 'gpt-6.1-sol', displayName: 'GPT-6.1 Sol', hidden: false, isDefault: true, defaultReasoningEffort: 'medium', supportedReasoningEfforts: [{ reasoningEffort: 'medium', description: 'd' }, { reasoningEffort: 'ultra', description: 'd' }] };
const luna = { id: 'gpt-6-luna', model: 'gpt-6-luna', displayName: 'GPT-6 Luna', hidden: false, isDefault: false, supportedReasoningEfforts: [{ reasoningEffort: 'max' }] };

test('the list is asked as the app-server page says: initialize, initialized, then model/list page by page, each after the answer before it', () => {
    const { sent, outcome } = exchange((message) => {
        if (message.method === 'initialize') return [{ method: 'some/notification', params: {} }, { id: message.id, result: { userAgent: 'x' } }];
        if (message.method === 'model/list' && message.params?.cursor === undefined) return [page(message.id, [sol], 'page-2')];
        if (message.method === 'model/list') return [page(message.id, [luna], null)];
        return [];
    });
    assert.deepEqual(sent.map((message) => message.method), ['initialize', 'initialized', 'model/list', 'model/list']);
    assert.deepEqual(sent[0]?.params?.clientInfo, { name: 'jason_desktop', title: 'Jason Desktop', version: '0.35.0' });
    assert.equal(sent[1]?.id, undefined, 'initialized is a notification');
    assert.equal(sent[2]?.params?.includeHidden, false);
    assert.equal(sent[3]?.params?.cursor, 'page-2');
    assert.ok(!sent.some((message) => message.method.startsWith('thread/') || message.method.startsWith('turn/')), 'nothing reaches a model');
    assert.deepEqual(outcome, {
        models: [
            { id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', efforts: ['medium'] },
            { id: 'gpt-6-luna', label: 'GPT-6 Luna', efforts: ['max'] },
        ],
    });
});

test('an answer that is an error, one that cannot be read, or a page that never ends is a failed ask', () => {
    const failed = exchange((message) => (message.method === 'initialize' ? [{ id: message.id, error: { code: -32600, message: 'no' } }] : []));
    assert.deepEqual(failed.outcome, { failed: 'refused' });
    const listRefused = exchange((message) => (message.method === 'initialize' ? [{ id: message.id, result: {} }] : message.method === 'model/list' ? [{ id: message.id, error: { code: -32600, message: 'restart' } }] : []));
    assert.deepEqual(listRefused.outcome, { failed: 'refused' });
    const unreadable = exchange((message) => (message.method === 'initialize' ? [{ id: message.id, result: {} }] : message.method === 'model/list' ? [{ id: message.id, result: { data: 'x', nextCursor: null } }] : []));
    assert.deepEqual(unreadable.outcome, { failed: 'unreadable' });
    let pages = 0;
    const endless = exchange((message) => (message.method === 'initialize' ? [{ id: message.id, result: {} }] : message.method === 'model/list' ? [page(message.id, [sol], `p${++pages}`)] : []));
    assert.deepEqual(endless.outcome, { failed: 'unreadable' });
    assert.ok(pages <= 20, String(pages));
    // A line that is no JSON, or an answer to an id never asked, is not an answer and is passed over.
    const noise = exchange((message) => (message.method === 'initialize' ? ['not json', { id: 99, result: {} }, { id: message.id, result: {} }] : message.method === 'model/list' ? [page(message.id, [], null)] : []));
    assert.deepEqual(noise.outcome, { models: [] });
});

test('a hidden entry, or one whose name a host would read as an option, is not offered', () => {
    const { outcome } = exchange((message) =>
        message.method === 'initialize'
            ? [{ id: message.id, result: {} }]
            : [page(message.id, [{ ...sol, hidden: true }, { ...luna, model: '--yolo' }, { ...luna, displayName: undefined }], null)],
    );
    assert.deepEqual(outcome, { models: [{ id: 'gpt-6-luna', label: 'gpt-6-luna', efforts: ['max'] }] });
});

test('a listing keeps at most two hundred models, and a kept list of more is not read', () => {
    const entry = (at: number) => ({ id: `m-${at}`, model: `m-${at}`, displayName: `M ${at}`, hidden: false, supportedReasoningEfforts: [] });
    const answer = (count: number) =>
        exchange((message) => (message.method === 'initialize' ? [{ id: message.id, result: {} }] : [page(message.id, Array.from({ length: count }, (_, at) => entry(at)), null)]));
    assert.equal((answer(200).outcome as { models: unknown[] }).models.length, 200);
    assert.deepEqual(answer(201).outcome, { failed: 'unreadable' });
    const kept = (count: number) =>
        parseKeptLists(JSON.stringify({ hosts: { codex: { asked_at: '2026-11-11T10:00:00.000Z', models: Array.from({ length: count }, (_, at) => ({ id: `m-${at}`, label: 'x', efforts: [] })) } } }));
    assert.equal(kept(200).get('codex')?.models.length, 200);
    assert.equal(kept(201).size, 0);
    assert.equal(modelLimit, 200);
});

test('a model is taken only where the host offers it, or as a typed name of the model shape on Claude Code and the Anthropic key', () => {
    const kept = lists([['codex', codexList]]);
    const refused = 'That model is not one this host offers.';
    assert.equal(modelRefusal('codex', undefined, kept), null);
    assert.equal(modelRefusal('codex', 'gpt-6.1-sol', kept), null);
    assert.equal(modelRefusal('codex', 'opus', kept), refused);
    assert.equal(modelRefusal('codex-api-key', 'gpt-6.1-sol', kept), refused);
    assert.equal(modelRefusal('claude', 'claude-opus-5-5', kept), null);
    assert.equal(modelRefusal('claude-api-key', 'opus', kept), null);
    assert.equal(modelRefusal('deepseek', 'deepseek-flash', kept), null);
    assert.equal(modelRefusal('deepseek', 'claude-opus-5-5', kept), refused);
    assert.equal(modelRefusal('gemini', 'gemini-9', kept), refused);
});
