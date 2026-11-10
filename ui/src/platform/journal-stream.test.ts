import { describe, expect, it } from 'vitest';
import type { JournalEntry } from '../api/types';
import { JournalStream, retryDelay, streamAddress, type StreamSocket } from './journal-stream';

class FakeSocket implements StreamSocket {
    onopen: (() => void) | null = null;
    onmessage: ((event: { data: unknown }) => void) | null = null;
    onclose: (() => void) | null = null;
    closed = false;
    constructor(
        readonly url: string,
        readonly protocols: string[],
    ) {}
    close(): void {
        this.closed = true;
    }
    open(): void {
        this.onopen?.();
    }
    send(id: string): void {
        this.onmessage?.({ data: JSON.stringify({ id, kind: 'campaign_created', ts: '2026-10-16T09:00:00Z' }) });
    }
    drop(): void {
        this.onclose?.();
    }
}

function harness(options: { reachable?: boolean; token?: string | null } = {}) {
    const sockets: FakeSocket[] = [];
    const timers: { delay: number; run: () => void }[] = [];
    const lines: JournalEntry[] = [];
    let asked = 0;
    const stream = new JournalStream({
        origin: 'http://127.0.0.1:47613',
        token: () => (options.token === undefined ? 'tok' : options.token),
        open: (url, protocols) => {
            const socket = new FakeSocket(url, protocols);
            sockets.push(socket);
            return socket;
        },
        later: (run, delay) => {
            timers.push({ delay, run });
            return timers.length;
        },
        cancel: () => undefined,
        reachable: () => {
            asked++;
            return Promise.resolve(options.reachable ?? true);
        },
    });
    stream.onLine((line) => lines.push(line));
    const fire = async () => {
        const timer = timers.shift();
        timer?.run();
        await Promise.resolve();
    };
    return { stream, sockets, timers, lines, fire, asked: () => asked };
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('the journal stream', () => {
    it('is addressed at the page origin with the ws scheme, and offers its form and the token as subprotocols', () => {
        expect(streamAddress('http://127.0.0.1:47613', null)).toBe('ws://127.0.0.1:47613/v1/journal.stream');
        expect(streamAddress('https://localhost:9', null)).toBe('wss://localhost:9/v1/journal.stream');
        const { stream, sockets } = harness();
        stream.start();
        expect(sockets[0]?.url).toBe('ws://127.0.0.1:47613/v1/journal.stream');
        expect(sockets[0]?.protocols).toEqual(['jason.journal.v1', 'jason.bearer.tok']);
    });

    it('resumes after the last line it received, and passes each line on once', async () => {
        const { stream, sockets, lines, fire } = harness();
        stream.start();
        sockets[0]?.open();
        sockets[0]?.send('jrn_01');
        sockets[0]?.send('jrn_02');
        sockets[0]?.drop();
        await fire();
        expect(sockets[1]?.url).toBe('ws://127.0.0.1:47613/v1/journal.stream?after=jrn_02');
        sockets[1]?.open();
        sockets[1]?.send('jrn_01');
        sockets[1]?.send('jrn_02');
        sockets[1]?.send('jrn_03');
        expect(lines.map((line) => line.id)).toEqual(['jrn_01', 'jrn_02', 'jrn_03']);
        expect(stream.state.last).toBe('jrn_03');
    });

    it('backs off on each failure up to thirty seconds, and starts again from one second once it was open', async () => {
        expect([0, 1, 2, 3, 4, 5, 6].map(retryDelay)).toEqual([1000, 2000, 4000, 8000, 16000, 30000, 30000]);
        const { stream, sockets, timers, fire } = harness({ token: 'tok' });
        stream.start();
        sockets[0]?.drop();
        expect(timers.map((timer) => timer.delay)).toEqual([1000]);
        await fire();
        sockets[1]?.drop();
        expect(timers.map((timer) => timer.delay)).toEqual([2000]);
        await fire();
        sockets[2]?.open();
        expect(stream.state.status).toBe('open');
        sockets[2]?.drop();
        expect(timers.map((timer) => timer.delay)).toEqual([1000]);
        expect(stream.state.status).toBe('waiting');
    });

    it('waits without a token rather than opening a stream that would be refused', () => {
        const { stream, sockets, timers } = harness({ token: null });
        stream.start();
        expect(sockets).toHaveLength(0);
        expect(timers.map((timer) => timer.delay)).toEqual([1000]);
    });

    it('starts fresh, without after, when a resume is refused while the runtime answers, and says so in its state', async () => {
        const { stream, sockets, timers, fire, asked } = harness({ reachable: true });
        stream.start();
        sockets[0]?.open();
        sockets[0]?.send('jrn_09');
        sockets[0]?.drop();
        await fire();
        expect(sockets[1]?.url).toContain('after=jrn_09');
        sockets[1]?.drop();
        await settle();
        expect(asked()).toBe(1);
        expect(stream.state.fresh).toBe(true);
        expect(stream.state.last).toBeNull();
        await fire();
        expect(sockets[2]?.url).toBe('ws://127.0.0.1:47613/v1/journal.stream');
        expect(timers).toHaveLength(0);
        sockets[2]?.open();
        sockets[2]?.send('jrn_09');
        expect(stream.state.fresh).toBe(true);
    });

    it('keeps its place when a resume fails because the runtime is not there', async () => {
        const { stream, sockets, fire, asked } = harness({ reachable: false });
        stream.start();
        sockets[0]?.open();
        sockets[0]?.send('jrn_09');
        sockets[0]?.drop();
        await fire();
        sockets[1]?.drop();
        await settle();
        expect(asked()).toBe(1);
        expect(stream.state.fresh).toBe(false);
        await fire();
        expect(sockets[2]?.url).toContain('after=jrn_09');
    });

    it('stops for good: the socket closed, no timer, no line passed on', async () => {
        const { stream, sockets, timers, lines } = harness();
        stream.start();
        sockets[0]?.open();
        stream.stop();
        expect(sockets[0]?.closed).toBe(true);
        sockets[0]?.drop();
        sockets[0]?.send('jrn_01');
        await settle();
        expect(timers).toHaveLength(0);
        expect(lines).toHaveLength(0);
        expect(stream.state.status).toBe('stopped');
    });
});
