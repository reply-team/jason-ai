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
            list: (...args) => (calls.push(['list', ...args]), []),
            open: (...args) => (calls.push(['open', ...args]), { id: 'conv_1', campaignId: null, title: 'New chat', updatedAt: '', entries: [], dropped: 0 }),
            create: async (...args) => (calls.push(['create', ...args]), { id: 'conv_1', campaignId: null, title: 'New chat', updatedAt: '' }),
            remove: (...args) => void calls.push(['remove', ...args]),
            restart: (...args) => void calls.push(['restart', ...args]),
            attach: async (...args) => (calls.push(['attach', ...args]), { id: 'conv_1', campaignId: 'cmp_1', title: 'Spring', updatedAt: '' }),
        },
        () => origin,
        (channel, why) => refusals.push(`${channel} ${why}`),
    );
    return { listeners, calls, refusals };
}

test('the chat registers its nine calls, and each reaches the chat from the runtime\'s page', async () => {
    const { listeners, calls } = registered();
    assert.deepEqual([...listeners.keys()].sort(), [
        'jason:chat.answer',
        'jason:chat.attach',
        'jason:chat.create',
        'jason:chat.list',
        'jason:chat.open',
        'jason:chat.remove',
        'jason:chat.restart',
        'jason:chat.send',
        'jason:chat.stop',
    ]);
    const page = { senderFrame: { url: `${origin}/ui/#/chat`, parent: null } };
    await listeners.get('jason:chat.send')?.(page, 'c1', 'hello', { host: 'claude' });
    listeners.get('jason:chat.stop')?.(page, 'c1');
    listeners.get('jason:chat.answer')?.(page, 'c1', 'r1', { allow: true, always: false });
    listeners.get('jason:chat.list')?.(page);
    listeners.get('jason:chat.open')?.(page, 'conv_1');
    await listeners.get('jason:chat.create')?.(page, 'cmp_1');
    listeners.get('jason:chat.remove')?.(page, 'conv_1');
    listeners.get('jason:chat.restart')?.(page, 'conv_1');
    await listeners.get('jason:chat.attach')?.(page, 'conv_1', 'cmp_1');
    assert.deepEqual(calls, [
        ['send', 'c1', 'hello', { host: 'claude' }],
        ['stop', 'c1'],
        ['answer', 'c1', 'r1', { allow: true, always: false }],
        ['list'],
        ['open', 'conv_1'],
        ['create', 'cmp_1'],
        ['remove', 'conv_1'],
        ['restart', 'conv_1'],
        ['attach', 'conv_1', 'cmp_1'],
    ]);
});

test('a chat call from another origin or a subframe never reaches the chat', () => {
    const { listeners, calls, refusals } = registered();
    for (const channel of listeners.keys()) {
        assert.throws(() => listeners.get(channel)?.({ senderFrame: { url: 'https://example.com/', parent: null } }, 'c1', 'planted words'), RefusedSender);
        assert.throws(() => listeners.get(channel)?.({ senderFrame: { url: `${origin}/ui/`, parent: {} } }, 'c1', 'planted words'), RefusedSender);
    }
    assert.deepEqual(calls, []);
    assert.equal(refusals.length, 18);
    assert.ok(refusals.every((line) => !line.includes('planted')));
});
