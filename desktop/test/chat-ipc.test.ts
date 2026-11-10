import assert from 'node:assert/strict';
import { test } from 'node:test';
import { registerChat } from '../src/chat-ipc.ts';
import { RefusedSender, type IpcRegistrar, type SenderFrame } from '../src/ipc.ts';

const origin = 'http://127.0.0.1:41234';
type Listener = (event: { senderFrame: SenderFrame | null }, ...args: unknown[]) => unknown;

function registered(): { listeners: Map<string, Listener>; calls: unknown[][]; refusals: string[] } {
    const listeners = new Map<string, Listener>();
    const calls: unknown[][] = [];
    const refusals: string[] = [];
    const ipc: IpcRegistrar = { handle: (channel, listener) => void listeners.set(channel, listener) };
    registerChat(
        ipc,
        {
            send: async (...args) => void calls.push(['send', ...args]),
            stop: (...args) => void calls.push(['stop', ...args]),
            answer: (...args) => void calls.push(['answer', ...args]),
        },
        () => origin,
        (channel, why) => refusals.push(`${channel} ${why}`),
    );
    return { listeners, calls, refusals };
}

test('the chat registers its three calls, and each reaches the chat from the runtime\'s page', async () => {
    const { listeners, calls } = registered();
    assert.deepEqual([...listeners.keys()].sort(), ['jason:chat.answer', 'jason:chat.send', 'jason:chat.stop']);
    const page = { senderFrame: { url: `${origin}/ui/#/chat`, parent: null } };
    await listeners.get('jason:chat.send')?.(page, 'c1', 'hello', { host: 'claude' });
    listeners.get('jason:chat.stop')?.(page, 'c1');
    listeners.get('jason:chat.answer')?.(page, 'c1', 'r1', { allow: true, always: false });
    assert.deepEqual(calls, [
        ['send', 'c1', 'hello', { host: 'claude' }],
        ['stop', 'c1'],
        ['answer', 'c1', 'r1', { allow: true, always: false }],
    ]);
});

test('a chat call from another origin or a subframe never reaches the chat', () => {
    const { listeners, calls, refusals } = registered();
    for (const channel of listeners.keys()) {
        assert.throws(() => listeners.get(channel)?.({ senderFrame: { url: 'https://example.com/', parent: null } }, 'c1', 'planted words'), RefusedSender);
        assert.throws(() => listeners.get(channel)?.({ senderFrame: { url: `${origin}/ui/`, parent: {} } }, 'c1', 'planted words'), RefusedSender);
    }
    assert.deepEqual(calls, []);
    assert.equal(refusals.length, 6);
    assert.ok(refusals.every((line) => !line.includes('planted')));
});
