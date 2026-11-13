import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import { eventChannels, handle, invokeChannels, refusalOf, RefusedSender, type IpcRegistrar, type SenderFrame } from '../src/ipc.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const origin = 'http://127.0.0.1:41234';
const top = (url: string): SenderFrame => ({ url, parent: null });

// ---- The origin check ----------------------------------------------------------------------------------------------

test('a call from the runtime’s page in the top frame is answered', () => {
    assert.equal(refusalOf(top(`${origin}/ui/#/connect`), origin), null);
});

test('a call from another origin is refused', () => {
    assert.equal(refusalOf(top('https://example.com/ui/'), origin), 'other_origin');
    assert.equal(refusalOf(top('http://127.0.0.1:9999/ui/'), origin), 'other_origin');
    assert.equal(refusalOf(top('http://localhost:41234/ui/'), origin), 'other_origin');
});

test('a call from a subframe is refused, even one at the runtime’s origin', () => {
    assert.equal(refusalOf({ url: `${origin}/ui/`, parent: { url: `${origin}/ui/` } }, origin), 'subframe');
});

test('a call from a file: page is refused', () => {
    assert.equal(refusalOf(top('file:///C:/app/shell/starting.html'), origin), 'other_origin');
    assert.equal(refusalOf(top('file:///C:/app/shell/starting.html'), 'null'), 'other_origin');
});

test('a call from a frame already gone, or while no runtime has an origin, is refused', () => {
    assert.equal(refusalOf(null, origin), 'no_frame');
    assert.equal(refusalOf(top(`${origin}/ui/`), null), 'other_origin');
    assert.equal(refusalOf(top('not a url'), origin), 'other_origin');
});

test('a refused call never reaches its handler, and the refusal is told only the channel', () => {
    const registered = new Map<string, (event: { senderFrame: SenderFrame | null }, ...args: unknown[]) => unknown>();
    const ipc: IpcRegistrar = { handle: (channel, listener) => void registered.set(channel, listener) };
    const ran: unknown[][] = [];
    const refusals: [string, string][] = [];
    handle(ipc, 'setup.connectReplyWithKey', () => origin, (...args) => ran.push(args), (channel, why) => refusals.push([channel, why]));

    const listener = registered.get('jason:setup.connectReplyWithKey');
    assert.ok(listener);
    assert.throws(() => listener({ senderFrame: top('https://example.com/') }, 'planted-key'), RefusedSender);
    assert.throws(() => listener({ senderFrame: { url: `${origin}/ui/`, parent: {} } }, 'planted-key'), RefusedSender);
    assert.throws(() => listener({ senderFrame: top('file:///x.html') }, 'planted-key'), RefusedSender);
    assert.deepEqual(ran, []);
    assert.deepEqual(refusals, [
        ['jason:setup.connectReplyWithKey', 'other_origin'],
        ['jason:setup.connectReplyWithKey', 'subframe'],
        ['jason:setup.connectReplyWithKey', 'other_origin'],
    ]);
    listener({ senderFrame: top(`${origin}/ui/`) }, 'the-key');
    assert.deepEqual(ran, [['the-key']]);
});

// ---- The seam the preload exposes ----------------------------------------------------------------------------------

interface Recorded {
    exposed: Map<string, unknown>;
    invoked: { channel: string; args: unknown[] }[];
    listening: string[];
}

/** Runs the preload as Electron would, with an Electron that records what it is asked. */
function runPreload(): Recorded {
    const source = readFileSync(join(root, 'src', 'preload.ts'), 'utf8');
    const { outputText } = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } });
    const recorded: Recorded = { exposed: new Map(), invoked: [], listening: [] };
    const electron = {
        contextBridge: { exposeInMainWorld: (name: string, api: unknown) => void recorded.exposed.set(name, api) },
        ipcRenderer: {
            invoke: (channel: string, ...args: unknown[]) => {
                recorded.invoked.push({ channel, args });
                return Promise.resolve();
            },
            on: (channel: string) => void recorded.listening.push(channel),
            removeListener: () => undefined,
        },
    };
    const required = (name: string) => {
        if (name !== 'electron') throw new Error(`a sandboxed preload may require only electron, not ${name}`);
        return electron;
    };
    const module = { exports: {} };
    new Function('require', 'module', 'exports', 'process', outputText)(required, module, module.exports, { platform: 'win32' });
    return recorded;
}

function shape(value: unknown): unknown {
    if (typeof value === 'function') return 'function';
    if (typeof value === 'object' && value !== null) return Object.fromEntries(Object.entries(value).map(([key, inner]) => [key, shape(inner)]));
    return typeof value;
}

test('the preload exposes window.jasonDesktop with exactly the seam, and nothing else on the window', () => {
    const { exposed } = runPreload();
    assert.deepEqual([...exposed.keys()], ['jasonDesktop']);
    assert.deepEqual(shape(exposed.get('jasonDesktop')), {
        platform: 'string',
        onCommand: 'function',
        chat: {
            send: 'function',
            stop: 'function',
            answer: 'function',
            onEvent: 'function',
            list: 'function',
            open: 'function',
            create: 'function',
            remove: 'function',
            restart: 'function',
            attach: 'function',
            rename: 'function',
            setMode: 'function',
            openFolder: 'function',
            feed: 'function',
        },
        theme: { get: 'function', set: 'function' },
        links: {
            take: 'function',
            onOpen: 'function',
        },
        setup: {
            status: 'function',
            connectReply: 'function',
            connectReplyWithKey: 'function',
            setProviderKey: 'function',
            installProgram: 'function',
            configurePlugin: 'function',
            disconnectPlugin: 'function',
            replyConnect: 'function',
            replyReconnect: 'function',
            replyLimits: 'function',
            replyPause: 'function',
            replyResume: 'function',
            replyUnblock: 'function',
            replyDelete: 'function',
            onChanged: 'function',
        },
    });
});

test('each member of the seam calls the channel ipc.ts checks, with its own arguments only', () => {
    const recorded = runPreload();
    const api = recorded.exposed.get('jasonDesktop') as Record<string, Record<string, (...args: unknown[]) => unknown>>;
    const calls: [string, unknown[]][] = [
        ['chat.send', ['c1', 'hello', { host: 'claude' }]],
        ['chat.stop', ['c1']],
        ['chat.answer', ['c1', 'r1', { allow: true, always: false }]],
        ['chat.list', []],
        ['chat.open', ['conv_01']],
        ['chat.create', ['cmp_01']],
        ['chat.remove', ['conv_01']],
        ['chat.restart', ['conv_01']],
        ['chat.attach', ['conv_01', 'cmp_01']],
        ['chat.rename', ['conv_01', 'Mine']],
        ['chat.setMode', ['conv_01', 'plan_only', 'claude']],
        ['chat.openFolder', ['01JD0000000000000000000000-1788000000000']],
        ['chat.feed', [[], { catchUp: true }]],
        ['links.take', []],
        ['theme.get', []],
        ['theme.set', ['dark']],
        ['setup.status', []],
        ['setup.connectReply', []],
        ['setup.connectReplyWithKey', ['k']],
        ['setup.setProviderKey', ['deepseek', 'k']],
        ['setup.installProgram', ['claude']],
        ['setup.configurePlugin', ['slack', { token: 't', target: 'C1' }]],
        ['setup.disconnectPlugin', ['slack']],
        ['setup.replyConnect', ['email', { provider: 'gmail', sharedPage: true }]],
        ['setup.replyReconnect', ['linkedin', '34', { sharedPage: false }]],
        ['setup.replyLimits', ['12', 200]],
        ['setup.replyPause', ['34']],
        ['setup.replyResume', ['34']],
        ['setup.replyUnblock', ['12']],
        ['setup.replyDelete', ['linkedin', '34', { force: true }]],
    ];
    for (const [member, args] of calls) {
        const [group, name] = member.split('.') as [string, string];
        void api[group]?.[name]?.(...args);
    }
    assert.deepEqual(
        recorded.invoked,
        calls.map(([member, args]) => ({ channel: invokeChannels[member as keyof typeof invokeChannels], args })),
    );
    assert.deepEqual(Object.keys(invokeChannels).sort(), calls.map(([member]) => member).sort());

    (api as unknown as { onCommand: (handler: () => void) => () => void }).onCommand(() => undefined);
    void api.chat?.onEvent?.(() => undefined);
    void api.setup?.onChanged?.(() => undefined);
    void api.links?.onOpen?.(() => undefined);
    assert.deepEqual(recorded.listening, [eventChannels.command, eventChannels['chat.event'], eventChannels['setup.changed'], eventChannels['links.open']]);
});
