import { describe, expect, it, vi } from 'vitest';
import { standInBridge, standInSecret } from '../test/bridge';
import { stubDesktop } from '../test/desktop';
import { Bridge, bridgeMembers, notInTab } from './bridge';

// The chat part of the seam over fetch and a read stream: only the members of the table, typed by the desktop's own
// declaration, refused outside it before anything is sent, and the events reaching a subscriber.

const pair = { port: 43210, secret: standInSecret };

describe('the transport', () => {
    it('is typed by the desktop seam, and a member outside the table is not on it (a type row)', () => {
        const seam = new Bridge(pair, vi.fn() as never).seam();
        // @ts-expect-error answering a card is done in the desktop's own window
        void seam.chat.answer;
        // @ts-expect-error a chat's mode is changed there
        void seam.chat.setMode;
        // @ts-expect-error the window feeds
        void seam.chat.feed;
        // @ts-expect-error Full access is taken back there
        void seam.fullAccess.takeBack;
        // The members it has are the desktop's: their arguments and answers are the declaration's.
        const sent: Promise<unknown> = seam.chat.send('conv_1', 'hello', {
            host: 'claude_code',
            model: null,
            effort: null,
        } as never);
        void sent.catch(() => undefined);
        expect(Object.keys(seam).sort()).toEqual(['chat', 'fullAccess', 'mode', 'setup', 'theme']);
    });

    it('holds a table that is exactly the members its seam calls', async () => {
        const desktop = stubDesktop();
        const stand = standInBridge(desktop);
        const seam = new Bridge(pair, stand.fetch).seam();
        await seam.mode.get();
        await seam.theme.get();
        await seam.fullAccess.standing();
        await seam.setup.status();
        await seam.chat.list();
        await seam.chat.open('conv_none');
        await seam.chat.create();
        await seam.chat.send('conv_none', 'hello', { host: 'claude_code' } as never);
        await seam.chat.stop('conv_none');
        await seam.chat.rename('conv_none', 'A name');
        const called = stand.asked.filter((each) => each.path === '/call').map((each) => each.member);
        expect([...called].sort()).toEqual([...bridgeMembers].sort());
    });

    it('refuses a member outside the table at run time, before anything is sent', async () => {
        const fetched = vi.fn();
        const bridge = new Bridge(pair, fetched as never);
        for (const member of [
            'chat.answer',
            'chat.setMode',
            'chat.feed',
            'chat.remove',
            'chat.restart',
            'chat.attach',
            'chat.openFolder',
            'setup.replyDelete',
            'mode.set',
            'fullAccess.takeBack',
            'openIn.open',
            'openIn.continueChat',
            'links.take',
            'pairing.paired',
        ])
            await expect(bridge.call(member as never)).rejects.toThrow(notInTab);
        expect(fetched).not.toHaveBeenCalled();
    });

    it('sends the arguments in a call, as an array, with the secret as a bearer', async () => {
        const fetched = vi.fn(async () => Response.json({ result: [] }));
        await new Bridge(pair, fetched as never).seam().chat.open('conv_1');
        const [url, init] = fetched.mock.calls[0] as unknown as [string, RequestInit];
        expect(url).toBe('http://127.0.0.1:43210/call');
        expect(new Headers(init.headers).get('authorization')).toBe(`Bearer ${standInSecret}`);
        expect(JSON.parse(init.body as string)).toEqual({ member: 'chat.open', args: ['conv_1'] });
    });

    it("carries the bridge's refusal as the sentence alone", async () => {
        const fetched = vi.fn(async () => Response.json({ refused: 'This chat is in Full access.' }, { status: 403 }));
        await expect(new Bridge(pair, fetched as never).call('chat.send', ['conv_1', 'x', {}])).rejects.toThrow(
            'This chat is in Full access.',
        );
    });

    it('tells its owner once when the bridge no longer knows the secret', async () => {
        const gone = vi.fn();
        const fetched = vi.fn(async () => new Response('{}', { status: 401 }));
        const bridge = new Bridge(pair, fetched as never, gone);
        await expect(bridge.call('chat.list')).rejects.toThrow();
        expect(gone).toHaveBeenCalledTimes(1);
    });
});

describe('the events', () => {
    it('reach a subscriber by chat id, skip heartbeats and lines that are not events, and stop on unsubscribe', async () => {
        const desktop = stubDesktop();
        const stand = standInBridge(desktop);
        const bridge = new Bridge(pair, stand.fetch);
        const seen: unknown[] = [];
        const stop = bridge.seam().chat.onEvent((chatId, event) => seen.push([chatId, event]));
        await vi.waitFor(() => expect(stand.asked.some((each) => each.path === '/events')).toBe(true));
        stand.push({ channel: 'chat.event', args: ['conv_1', { kind: 'text', text: 'Hello' }] });
        stand.push({ heartbeat: true });
        stand.push({ channel: 'setup.changed', args: [] });
        await vi.waitFor(() => expect(seen).toEqual([['conv_1', { kind: 'text', text: 'Hello' }]]));
        stop();
        stand.push({ channel: 'chat.event', args: ['conv_1', { kind: 'text', text: 'After' }] });
        await new Promise((resolve) => setTimeout(resolve, 20));
        expect(seen).toHaveLength(1);
    });

    it('keeps one stream for all subscribers, and the setup and Full access changes come down it', async () => {
        const desktop = stubDesktop();
        const stand = standInBridge(desktop);
        const bridge = new Bridge(pair, stand.fetch);
        const seam = bridge.seam();
        const setup = vi.fn();
        const standing = vi.fn();
        seam.chat.onEvent(() => undefined);
        seam.setup.onChanged(setup);
        seam.fullAccess.onChanged(standing);
        await vi.waitFor(() => expect(stand.asked.some((each) => each.path === '/events')).toBe(true));
        stand.push({ channel: 'setup.changed', args: [] });
        stand.push({ channel: 'fullAccess.changed', args: [null] });
        await vi.waitFor(() => expect(setup).toHaveBeenCalledTimes(1));
        expect(standing).toHaveBeenCalledWith(null);
        expect(stand.asked.filter((each) => each.path === '/events')).toHaveLength(1);
        bridge.close();
    });
});

describe('what the bridge answers besides a result', () => {
    it("carries a handler's refusal as a value, the way the window gets it", async () => {
        const refusal = { refused: 'This chat is in Full access.' };
        const fetched = vi.fn(async () => Response.json({ result: refusal }));
        await expect(new Bridge(pair, fetched as never).seam().chat.send('c', 'x', {} as never)).resolves.toEqual(
            refusal,
        );
    });

    it('says a handler failure with its own message, and a refused member with the table sentence', async () => {
        const failing = vi.fn(async () => Response.json({ error: 'Could not read the chat.' }, { status: 500 }));
        await expect(new Bridge(pair, failing as never).call('chat.list')).rejects.toThrow('Could not read the chat.');
        const refusing = vi.fn(async () => new Response('', { status: 403 }));
        await expect(new Bridge(pair, refusing as never).call('chat.list')).rejects.toThrow(notInTab);
    });

    it('opens the stream again when the bridge ends it, and says so to those who read anew', async () => {
        const desktop = stubDesktop();
        const stand = standInBridge(desktop);
        const bridge = new Bridge(pair, stand.fetch, undefined, 5);
        const reopened = vi.fn();
        bridge.onReopen(reopened);
        bridge.seam().chat.onEvent(() => undefined);
        await vi.waitFor(() => expect(stand.asked.filter((each) => each.path === '/events')).toHaveLength(1));
        stand.end();
        await vi.waitFor(() => expect(reopened).toHaveBeenCalledTimes(1));
        await vi.waitFor(() => expect(stand.asked.filter((each) => each.path === '/events')).toHaveLength(2));
        bridge.close();
    });

    it('lets go of the pair when the bridge cannot be reached or refuses before reading (a TypeError)', async () => {
        const gone = vi.fn();
        const fetched = vi.fn(async () => {
            throw new TypeError('Failed to fetch');
        });
        await expect(new Bridge(pair, fetched as never, gone).call('chat.list')).rejects.toThrow(
            'The connection to Jason Desktop is gone.',
        );
        expect(gone).toHaveBeenCalledTimes(1);
    });

    it('surfaces a request the bridge could not take, with its status, and keeps the pair', async () => {
        const gone = vi.fn();
        for (const status of [400, 413]) {
            const fetched = vi.fn(async () => new Response('', { status }));
            await expect(new Bridge(pair, fetched as never, gone).call('chat.list')).rejects.toThrow(
                `Jason Desktop could not take that request (${status}).`,
            );
        }
        expect(gone).not.toHaveBeenCalled();
    });

    it('is refused by the stand-in as a body that is not a call, and a member outside its table', async () => {
        const stand = standInBridge(stubDesktop());
        const headers = { authorization: `Bearer ${standInSecret}` };
        const bad = await stand.fetch('http://127.0.0.1:43210/call', { method: 'POST', headers, body: '{"member":1}' });
        expect(bad.status).toBe(400);
        const out = await stand.fetch('http://127.0.0.1:43210/call', {
            method: 'POST',
            headers,
            body: JSON.stringify({ member: 'chat.answer', args: [] }),
        });
        expect(out.status).toBe(403);
        const events = await stand.fetch('http://127.0.0.1:43210/events', { headers });
        expect(events.headers.get('content-type')).toBe('application/x-ndjson');
    });
});

describe('the event stream that cannot be opened', () => {
    const subscribed = (fetched: unknown, gone = vi.fn()) => {
        const bridge = new Bridge(pair, fetched as never, gone, 10);
        bridge.seam().chat.onEvent(() => undefined);
        return { bridge, gone };
    };

    it('lets go of the pair on a 401 and does not try again', async () => {
        const fetched = vi.fn(async () => new Response('', { status: 401 }));
        const { gone } = subscribed(fetched);
        await vi.waitFor(() => expect(gone).toHaveBeenCalledTimes(1));
        await new Promise((resolve) => setTimeout(resolve, 80));
        expect(fetched).toHaveBeenCalledTimes(1);
    });

    it('waits longer after each failure, up to a limit, and does not read the chats anew while it fails', async () => {
        const stamps: number[] = [];
        const fetched = vi.fn(async () => {
            stamps.push(Date.now());
            return new Response('', { status: 500 });
        });
        const { bridge } = subscribed(fetched);
        const reread = vi.fn();
        bridge.onReopen(reread);
        await vi.waitFor(() => expect(stamps.length).toBeGreaterThanOrEqual(4), { timeout: 2000 });
        bridge.close();
        const gaps = stamps.slice(1).map((stamp, at) => stamp - stamps[at]!);
        expect(gaps[1]!).toBeGreaterThan(gaps[0]! * 1.5);
        expect(gaps[2]!).toBeGreaterThan(gaps[1]! * 1.5);
        expect(reread).not.toHaveBeenCalled();
    });

    it('starts the waits over after a stream that worked, and reads the chats anew once it is open again', async () => {
        const desktop = stubDesktop();
        const stand = standInBridge(desktop);
        let fail = 0;
        const fetched = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
            if (String(input).endsWith('/events') && fail > 0) {
                fail -= 1;
                return new Response('', { status: 500 });
            }
            return stand.fetch(input, init);
        });
        const { bridge } = subscribed(fetched);
        const reread = vi.fn();
        bridge.onReopen(reread);
        await vi.waitFor(() => expect(stand.asked.some((each) => each.path === '/events')).toBe(true));
        fail = 2;
        stand.end();
        await vi.waitFor(() => expect(reread).toHaveBeenCalledTimes(1), { timeout: 2000 });
        expect(stand.asked.filter((each) => each.path === '/events')).toHaveLength(2);
        bridge.close();
    });
});
