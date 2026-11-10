import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { PermissionBroker, type BrokerEvent } from '../src/chat-permission-broker.ts';
import { openEndpoint, serverName, toolName } from '../src/chat-permission-endpoint.ts';
import { realPathOf } from '../src/chat-real-path.ts';
import { newSecret, newTurnId, removeTurnFiles, writeTurnConfig } from '../src/chat-turn-files.ts';
import { desktopPaths } from '../src/paths.ts';

const require = createRequire(import.meta.url);
const client = require('./stubs/mcp-http.cjs') as {
    decide: (configFile: string, server: string, tool: string, name: string, input: Record<string, unknown>) => Promise<{ behavior: string; message?: string; updatedInput?: unknown }>;
};

/** One turn wired as the chat wires it: the broker, the turn's endpoint and the turn's configuration file. */
async function turn(t: { after: (fn: () => void | Promise<void>) => void }) {
    const root = mkdtempSync(join(tmpdir(), 'jason-flow-'));
    const paths = desktopPaths(root);
    mkdirSync(paths.chat, { recursive: true });
    const cards: { chat: string; event: BrokerEvent }[] = [];
    const broker = new PermissionBroker({ emit: (chat, event) => cards.push({ chat, event }) });
    const platform = process.platform === 'win32' || process.platform === 'darwin' ? process.platform : 'linux';
    const id = newTurnId();
    broker.openTurn('chat_a', id, { platform, cwd: paths.chat, chat: paths.chat, added: [], data: root, protectedFiles: [paths.installationSettings], denied: ['runtime'], deniedOperations: [], home: root, real: realPathOf });
    const secret = newSecret();
    const endpoint = await openEndpoint({ secret, decide: (tool, input) => broker.ask(id, tool, input) });
    const config = writeTurnConfig(paths.desktop, id, endpoint.url, secret);
    t.after(async () => {
        broker.endTurn(id);
        await endpoint.close();
        removeTurnFiles(paths.desktop, id);
        rmSync(root, { recursive: true, force: true });
    });
    return { root, paths, broker, cards, config };
}

test('through the HTTP tool, what the model allows comes back allowed with no card', async (t) => {
    const { paths, cards, config } = await turn(t);
    const input = { file_path: join(paths.chat, 'notes.md') };
    assert.deepEqual(await client.decide(config, serverName, toolName, 'Read', input), { behavior: 'allow', updatedInput: input });
    assert.deepEqual(cards, []);
});

test('through the HTTP tool, a read of the installation\'s data is refused with no card', async (t) => {
    const { paths, cards, config } = await turn(t);
    const decision = await client.decide(config, serverName, toolName, 'Read', { file_path: paths.installationSettings });
    assert.equal(decision.behavior, 'deny');
    assert.deepEqual(cards, []);
});

test('through the HTTP tool, a card waits for the page, and the person\'s yes comes back as an allow with the input', async (t) => {
    const { broker, cards, config } = await turn(t);
    const decided = client.decide(config, serverName, toolName, 'Bash', { command: 'git status' });
    while (cards.length === 0) await new Promise((resolve) => setTimeout(resolve, 10));
    const card = cards[0]?.event;
    assert.equal(card?.kind, 'permission');
    broker.answer('chat_a', card?.request ?? '', { allow: true, always: false });
    assert.deepEqual(await decided, { behavior: 'allow', updatedInput: { command: 'git status' } });
});

test('through the HTTP tool, the person\'s no comes back as a deny with a message', async (t) => {
    const { broker, cards, config } = await turn(t);
    const decided = client.decide(config, serverName, toolName, 'Bash', { command: 'git push' });
    while (cards.length === 0) await new Promise((resolve) => setTimeout(resolve, 10));
    broker.answer('chat_a', cards[0]?.event.request ?? '', { allow: false, always: false });
    const decision = await decided;
    assert.equal(decision.behavior, 'deny');
    assert.equal(typeof decision.message, 'string');
});
