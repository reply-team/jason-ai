// The chat's log read back from the file the desktop writes: a turn's words, a command it ran, a path it read and the
// provider's key are planted, and none of them is found; the lines are the chat's three templates and the CLI's verbs.

import assert from 'node:assert/strict';
import { mkdirSync, readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent } from '../src/chat.ts';
import { readFence } from '../src/chat-deny.ts';
import { loggedTurns } from '../src/chat-log.ts';
import { loggedCli } from '../src/installation.ts';
import { fileLogger } from '../src/log.ts';
import { bench } from './harness.ts';

const lines0 = (text: string) => text.split(/\r?\n/).filter(Boolean).map((line) => JSON.parse(line) as Record<string, unknown>);
const fence = readFence(join(import.meta.dirname, '..'));

test('the chat’s log carries none of a turn’s words, commands, paths or keys', async (t) => {
    const removals: (() => void)[] = [];
    t.after(() => removals.forEach((remove) => remove()));
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
    const chat: Chat = new Chat({
        paths: b.paths,
        env: b.env,
        bundle: join(b.root, 'bundle'),
        dataNamed: true,
        cli: loggedCli(b.cli, log),
        keyFor: (id) => (id === 'deepseek' ? planted.key : undefined),
        fence: () => fence,
        emit(_chatId, event) {
            events.push(event);
            // Every card is answered yes, so each request is settled by the person or the model and logged.
            if (event.kind === 'permission') chat.answer('c1', event.request, { allow: true, always: false });
            if (event.kind === 'done') ended();
        },
        ...loggedTurns(log),
    });
    t.after(() => chat.closeAll());

    await chat.send('c1', planted.prompt, { host: 'deepseek', campaignId: planted.campaign });
    await done;
    assert.deepEqual(events.at(-1), { kind: 'done', error: null });
    assert.ok(events.some((event) => event.kind === 'permission'), 'a card was made and answered');

    const text = readdirSync(b.paths.logs).map((name) => readFileSync(join(b.paths.logs, name), 'utf8')).join('');
    for (const [what, value] of Object.entries(planted)) assert.equal(text.includes(value), false, `the ${what} reached the log`);
    assert.equal(text.includes('planted'), false, 'nothing planted reached the log');
    assert.equal(text.includes(folder), false, 'the campaign folder is not logged');
    const names = text.split('\n').filter(Boolean).map((line) => (JSON.parse(line) as { EventName: string }).EventName);
    assert.deepEqual(names.filter((name) => name.startsWith('chat_')), ['chat_turn_started', 'chat_request_decided', 'chat_request_decided', 'chat_request_decided', 'chat_turn_ended']);
    assert.deepEqual(lines0(text).filter((line) => line.EventName === 'cli_exited').map((line) => line.Verb), ['campaign get', 'project get']);
    const lines = text.split('\n').filter(Boolean).map((line) => JSON.parse(line) as Record<string, unknown>);
    const { '@t': _at, ...startLine } = lines.find((line) => line.EventName === 'chat_turn_started') ?? {};
    assert.deepEqual(startLine, {
        '@mt': 'A chat turn started on {Host}; resumed {Resumed}',
        Host: 'deepseek',
        Resumed: false,
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
