// The chat's log read back from the file the desktop writes: a turn's words, a command it ran, a path it read and the
// provider's key are planted, and none of them is found; the lines are the chat's three templates and the CLI's verbs.

import assert from 'node:assert/strict';
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent } from '../src/chat.ts';
import { readFence } from '../src/chat-deny.ts';
import { loggedModels, loggedTurns } from '../src/chat-log.ts';
import { newConversationId, writeConversation, type Conversation } from '../src/chat-conversations.ts';
import { loggedCli } from '../src/installation.ts';
import { fileLogger } from '../src/log.ts';
import { NamedChat } from './named-chat.ts';
import { bench } from './harness.ts';

const lines0 = (text: string) => text.split(/\r?\n/).filter(Boolean).map((line) => JSON.parse(line) as Record<string, unknown>);
const fence = readFence(join(import.meta.dirname, '..'));

test('the chat’s log carries none of a turn’s words, commands, paths or keys', async (t) => {
    const removals: (() => void)[] = [];
    let held: Chat | undefined;
    // A set-up chat's naming run, which a turn that ended well starts, ends before the folder goes.
    t.after(async () => {
        held?.closeAll();
        await held?.idle();
        removals.forEach((remove) => remove());
    });
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude'] });
    const planted = {
        prompt: 'planted words about the Acme renewal',
        command: 'git push planted-remote-name',
        path: join(b.root, 'planted-private-folder', 'notes.md'),
        key: 'sk-planted-provider-key-5e1d',
        campaign: 'cmp_01PLANTEDCAMPAIGN',
    };
    const folder = join(b.root, 'planted-campaign-folder');
    mkdirSync(folder, { recursive: true });
    b.env.STUB_CAMPAIGN_FOLDER = folder;
    b.env.STUB_HOST_PLAN = JSON.stringify([
        { use: { tool: 'Read', input: { file_path: planted.path } } },
        { ask: { tool: 'Bash', input: { command: planted.command } } },
        { ask: { tool: 'Read', input: { file_path: planted.path } } },
        { ask: { tool: 'Bash', input: { command: 'jason approval approve apr_planted' } } },
    ]);
    const log = fileLogger(b.paths.logs);
    const events: ChatEvent[] = [];
    let ended: () => void = () => undefined;
    const done = new Promise<void>((resolve) => (ended = resolve));
    const named = new NamedChat(() => chat);
    const chat: Chat = new Chat({
        paths: b.paths,
        env: b.env,
        bundle: join(b.root, 'bundle'),
        dataNamed: true,
        bundleVersion: null,
        cli: loggedCli(b.cli, log),
        programs: b.programs,
        keyFor: (id) => (id === 'deepseek' ? planted.key : undefined),
        fence: () => fence,
        emit(_chatId, event) {
            events.push(event);
            // Every card is answered yes, so each request is settled by the person or the model and logged.
            if (event.kind === 'permission') named.answer('c1', event.request, { allow: true, always: false });
            if (event.kind === 'done') ended();
        },
        ...loggedTurns(log),
    });
    held = chat;

    await named.send('c1', planted.prompt, { host: 'deepseek', campaignId: planted.campaign });
    await done;
    assert.deepEqual(events.at(-1), { kind: 'done', error: null });
    assert.ok(events.some((event) => event.kind === 'permission'), 'a card was made and answered');

    const text = readdirSync(b.paths.logs).map((name) => readFileSync(join(b.paths.logs, name), 'utf8')).join('');
    for (const [what, value] of Object.entries(planted)) assert.equal(text.includes(value), false, `the ${what} reached the log`);
    assert.equal(text.includes('planted'), false, 'nothing planted reached the log');
    assert.equal(text.includes(folder), false, 'the campaign folder is not logged');
    const names = text.split('\n').filter(Boolean).map((line) => (JSON.parse(line) as { EventName: string }).EventName);
    assert.deepEqual(names.filter((name) => name.startsWith('chat_')), ['chat_conversations_read', 'chat_turn_started', 'chat_request_decided', 'chat_request_decided', 'chat_request_decided', 'chat_turn_ended']);
    // The campaign's conversation is made by reading the campaign; its turn reads it again, with its project.
    assert.deepEqual(lines0(text).filter((line) => line.EventName === 'cli_exited').map((line) => line.Verb), ['campaign get', 'campaign get', 'project get']);
    const lines = text.split('\n').filter(Boolean).map((line) => JSON.parse(line) as Record<string, unknown>);
    const { '@t': _at, ...startLine } = lines.find((line) => line.EventName === 'chat_turn_started') ?? {};
    assert.deepEqual(startLine, {
        '@mt': 'A chat turn started on {Host}; resumed {Resumed}; mode {Mode}',
        Host: 'deepseek',
        Resumed: false,
        Mode: 'ask_first',
        SourceContext: 'Jason.Desktop',
        EventName: 'chat_turn_started',
    });
    const endLine = lines.find((line) => line.EventName === 'chat_turn_ended');
    assert.equal(endLine?.Steps, 4, 'a tool the allowed tools let through is a step and no request');
    assert.equal(endLine?.Requests, 3);
    assert.equal(endLine?.Exit, 0);
    assert.deepEqual(
        lines.filter((line) => line.EventName === 'chat_request_decided').map((line) => `${line.Outcome} ${line.By}`),
        ['allowed person', 'allowed person', 'refused model'],
    );
});

test('a missing instruction text is logged once, by a closed line that names the file and nothing of a turn', async (t) => {
    const removals: (() => void)[] = [];
    let held: Chat | undefined;
    // A set-up chat's naming run, which a turn that ended well starts, ends before the folder goes.
    t.after(async () => {
        held?.closeAll();
        await held?.idle();
        removals.forEach((remove) => remove());
    });
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude'] });
    const log = fileLogger(b.paths.logs);
    let ends = 0;
    let ended: () => void = () => undefined;
    const done = new Promise<void>((resolve) => (ended = resolve));
    const named = new NamedChat(() => chat);
    const chat: Chat = new Chat({
        paths: b.paths,
        env: b.env,
        bundle: join(b.root, 'bundle'),
        dataNamed: true,
        bundleVersion: null,
        cli: b.cli,
        programs: b.programs,
        keyFor: () => undefined,
        fence: () => ({ ...fence, instructions: null }),
        emit(_chatId, event) {
            if (event.kind === 'done' && ++ends === 2) ended();
            if (event.kind === 'done' && ends === 1) void named.send('c1', 'planted second words', { host: 'claude' });
        },
        ...loggedTurns(log),
    });
    held = chat;
    await named.send('c1', 'planted words', { host: 'claude' });
    await done;
    const text = readdirSync(b.paths.logs).map((name) => readFileSync(join(b.paths.logs, name), 'utf8')).join('');
    const missing = lines0(text).filter((line) => line.EventName === 'chat_instructions_missing');
    assert.equal(missing.length, 1);
    const { '@t': _at, ...line } = missing[0]!;
    assert.deepEqual(line, {
        '@mt': 'The chat instruction text {File} is not in the application; turns run without it',
        '@l': 'Warning',
        File: 'chat-instructions.md',
        SourceContext: 'Jason.Desktop',
        EventName: 'chat_instructions_missing',
    });
    assert.equal(text.includes('planted'), false);
});

test('an instruction file that cannot be written is logged by a closed line that names the file and its error code, at the start and at the turn it refuses', async (t) => {
    const removals: (() => void)[] = [];
    let held: Chat | undefined;
    // A set-up chat's naming run, which a turn that ended well starts, ends before the folder goes.
    t.after(async () => {
        held?.closeAll();
        await held?.idle();
        removals.forEach((remove) => remove());
    });
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude'] });
    mkdirSync(join(b.paths.chat, 'CLAUDE.md'), { recursive: true });
    const log = fileLogger(b.paths.logs);
    let ended: () => void = () => undefined;
    const done = new Promise<void>((resolve) => (ended = resolve));
    const said: ChatEvent[] = [];
    const named = new NamedChat(() => chat);
    const chat: Chat = new Chat({
        paths: b.paths,
        env: b.env,
        bundle: join(b.root, 'bundle'),
        dataNamed: true,
        bundleVersion: null,
        cli: b.cli,
        programs: b.programs,
        keyFor: () => undefined,
        fence: () => fence,
        emit(_chatId, event) {
            said.push(event);
            if (event.kind === 'done') ended();
        },
        ...loggedTurns(log),
    });
    held = chat;
    await named.send('c1', 'planted words', { host: 'claude' });
    await done;
    assert.equal(said.length, 1);
    const text = readdirSync(b.paths.logs).map((name) => readFileSync(join(b.paths.logs, name), 'utf8')).join('');
    const unwritten = lines0(text).filter((line) => line.EventName === 'chat_instructions_unwritten');
    assert.equal(unwritten.length, 2, 'once at the start, once at the turn');
    for (const each of unwritten) {
        const { '@t': _at, Code: code, ...line } = each;
        assert.match(String(code), /^E[A-Z]+$/);
        assert.deepEqual(line, {
            '@mt': 'The chat instruction file {File} could not be written ({Code}); turns are refused until it is',
            '@l': 'Warning',
            File: 'CLAUDE.md',
            SourceContext: 'Jason.Desktop',
            EventName: 'chat_instructions_unwritten',
        });
    }
    assert.equal(text.includes('planted'), false);
    assert.equal(text.includes(b.paths.chat), false, 'no path');
    assert.equal(text.includes(JSON.stringify(b.paths.chat).slice(1, -1)), false, 'no path, escaped either');
});

test('no conversation reaches the log: a planted record, a lost session and a set-up turn leave counts and closed words alone', async (t) => {
    const removals: (() => void)[] = [];
    let held: Chat | undefined;
    // A set-up chat's naming run, which a turn that ended well starts, ends before the folder goes.
    t.after(async () => {
        held?.closeAll();
        await held?.idle();
        removals.forEach((remove) => remove());
    });
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude'] });
    const campaigns = join(b.root, 'campaigns.json');
    writeFileSync(campaigns, '[]');
    b.env.STUB_CAMPAIGNS_FILE = campaigns;
    b.env.STUB_CAMPAIGN_NAME = 'planted campaign name';
    const kept: Conversation = {
        id: newConversationId(),
        campaign_id: 'cmp_01PLANTEDCONVERSATION',
        title: 'planted title',
        created_at: '2026-11-11T10:00:00.000Z',
        updated_at: '2026-11-11T10:00:00.000Z',
        sessions: { claude: 'planted-session-id' },
        entries: [
            { kind: 'user', text: 'planted earlier words' },
            { kind: 'step', text: 'Ran a jason command', detail: 'jason planted-verb' },
            { kind: 'text', text: 'planted earlier answer' },
        ],
        dropped: 0,
    };
    writeConversation(b.paths.conversations, kept);
    writeFileSync(join(b.paths.conversations, `${newConversationId()}.json`), '{"planted": "not a record"}');
    const log = fileLogger(b.paths.logs);
    const waiting = new Map<string, () => void>();
    const chat: Chat = new Chat({
        paths: b.paths,
        env: b.env,
        bundle: join(b.root, 'bundle'),
        dataNamed: true,
        bundleVersion: null,
        cli: loggedCli(b.cli, log),
        programs: b.programs,
        keyFor: () => undefined,
        fence: () => fence,
        emit(chatId, event) {
            if (event.kind === 'done') waiting.get(chatId)?.();
        },
        ...loggedTurns(log),
    });
    held = chat;
    const turn = async (chatId: string, words: string) => {
        const done = new Promise<void>((resolve) => waiting.set(chatId, resolve));
        await chat.send(chatId, words, { host: 'claude' });
        await done;
    };
    b.env.STUB_RESUME = 'missing';
    await turn(kept.id, 'planted new words');
    delete b.env.STUB_RESUME;
    b.env.STUB_HOST_PLAN = JSON.stringify([{ campaign: 'planted first campaign' }, { campaign: 'planted second campaign' }]);
    const setup = await chat.create();
    await turn(setup.id, 'planted set-up words');

    const text = readdirSync(b.paths.logs).map((name) => readFileSync(join(b.paths.logs, name), 'utf8')).join('');
    assert.equal(text.includes('planted'), false, 'nothing planted reached the log');
    for (const value of [kept.id, setup.id, kept.campaign_id!, 'stub-session']) assert.equal(text.includes(value), false, value);
    const fields = (name: string) =>
        lines0(text)
            .filter((line) => line.EventName === name)
            .map(({ '@t': _at, '@mt': _template, SourceContext: _source, EventName: _name, ...rest }) => rest);
    assert.deepEqual(fields('chat_conversations_read'), [{ Conversations: 1, Unreadable: 1 }]);
    assert.deepEqual(fields('chat_turn_fresh'), [{ Cause: 'not_found' }]);
    assert.deepEqual(fields('chat_setup_ended'), [{ ListRead: true, Found: 2, HandedOver: false }]);
    assert.deepEqual(fields('chat_conversation_unwritten'), []);
});

test('a key source that stops nothing is logged by its word alone', (t) => {
    const removals: (() => void)[] = [];
    t.after(() => removals.forEach((remove) => remove()));
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: [] });
    const log = fileLogger(b.paths.logs);
    for (const source of ['unexplained', 'unlisted', 'absent'] as const) loggedTurns(log).keySource(source);
    const text = readdirSync(b.paths.logs).map((name) => readFileSync(join(b.paths.logs, name), 'utf8')).join('');
    const said = lines0(text)
        .filter((line) => line.EventName === 'chat_key_source')
        .map(({ '@t': _at, '@mt': _template, SourceContext: _source, EventName: _name, ...rest }) => rest);
    assert.deepEqual(said, (['unexplained', 'unlisted', 'absent'] as const).map((source) => ({ '@l': 'Warning', Source: source })));
});

test('an ask of the models Codex offers is logged by its host id and a count, or the kind of its failure, and nothing the host said', (t) => {
    const removals: (() => void)[] = [];
    t.after(() => removals.forEach((remove) => remove()));
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: [] });
    const log = fileLogger(b.paths.logs);
    const logged = loggedModels(log);
    logged.listed('codex', 3);
    logged.failed('codex-api-key', 'bound');
    const text = readdirSync(b.paths.logs).map((name) => readFileSync(join(b.paths.logs, name), 'utf8')).join('');
    const said = lines0(text)
        .filter((line) => typeof line.EventName === 'string' && line.EventName.startsWith('chat_models_'))
        .map(({ '@t': _at, '@mt': _template, SourceContext: _source, ...rest }) => rest);
    assert.deepEqual(said, [
        { EventName: 'chat_models_listed', Host: 'codex', Models: 3 },
        { '@l': 'Warning', EventName: 'chat_models_unlisted', Host: 'codex-api-key', Kind: 'bound' },
    ]);
});

test('a naming run of a set-up chat is logged by how it ended, one word, never the name or the words it was given', (t) => {
    const removals: (() => void)[] = [];
    t.after(() => removals.forEach((remove) => remove()));
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: [] });
    const log = fileLogger(b.paths.logs);
    const outcomes = ['named', 'refused', 'failed', 'timed_out', 'stopped', 'held', 'not_started', 'overtaken'] as const;
    for (const outcome of outcomes) loggedTurns(log).named(outcome);
    const text = readdirSync(b.paths.logs).map((name) => readFileSync(join(b.paths.logs, name), 'utf8')).join('');
    const said = lines0(text)
        .filter((line) => line.EventName === 'chat_named')
        .map(({ '@t': _at, '@mt': _template, SourceContext: _source, EventName: _name, ...rest }) => rest);
    assert.deepEqual(said, outcomes.map((outcome) => ({ Outcome: outcome })));
});

test('the standing choice of Full access is logged by why it changed and whether new chats start in it, or by the code that kept it from its file', (t) => {
    const removals: (() => void)[] = [];
    t.after(() => removals.forEach((remove) => remove()));
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: [] });
    const log = fileLogger(b.paths.logs);
    loggedTurns(log).standingChanged({ givenAt: '2026-01-02T03:04:05.000Z', newChats: true }, 'given');
    loggedTurns(log).standingChanged(null, 'taken_back');
    loggedTurns(log).standingChanged(null, 'unchecked');
    loggedTurns(log).standingUnwritten('EPERM');
    const text = readdirSync(b.paths.logs).map((name) => readFileSync(join(b.paths.logs, name), 'utf8')).join('');
    const said = lines0(text)
        .filter((line) => String(line.EventName).startsWith('chat_full_access_standing'))
        .map(({ '@t': _at, '@mt': _template, SourceContext: _source, ...rest }) => rest);
    assert.deepEqual(said, [
        { EventName: 'chat_full_access_standing', Cause: 'given', Standing: true, NewChats: true },
        { EventName: 'chat_full_access_standing', Cause: 'taken_back', Standing: false, NewChats: false },
        { '@l': 'Warning', EventName: 'chat_full_access_standing', Cause: 'unchecked', Standing: false, NewChats: false },
        { '@l': 'Warning', EventName: 'chat_full_access_standing_unwritten', Code: 'EPERM' },
    ]);
    assert.ok(!text.includes('2026-01-02'), 'not when it was given');
});

test('the Windows check of Codex\'s sandbox is logged by its answer and whether the settings file opened inside, one word each, never a path or what was read', (t) => {
    const removals: (() => void)[] = [];
    t.after(() => removals.forEach((remove) => remove()));
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: [] });
    const log = fileLogger(b.paths.logs);
    const logged = loggedTurns(log);
    logged.sandboxChecked('holds', 'read');
    logged.sandboxChecked('reads', 'unread');
    logged.sandboxChecked('unproven', 'unknown');
    const text = readdirSync(b.paths.logs).map((name) => readFileSync(join(b.paths.logs, name), 'utf8')).join('');
    const said = lines0(text)
        .filter((line) => line.EventName === 'chat_sandbox_checked')
        .map(({ '@t': _at, SourceContext: _source, EventName: _name, ...rest }) => rest);
    const template = 'Codex\'s sandbox on Windows was checked: {Answer}; the desktop\'s settings file inside it: {Settings}';
    assert.deepEqual(said, [
        { '@mt': template, Answer: 'holds', Settings: 'read' },
        { '@mt': template, Answer: 'reads', Settings: 'unread' },
        { '@mt': template, Answer: 'unproven', Settings: 'unknown' },
    ]);
    assert.equal(text.includes(b.paths.settings), false);
    assert.equal(text.includes(b.paths.data), false);
});
