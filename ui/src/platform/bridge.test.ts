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
