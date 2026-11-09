import type { JournalEntry } from '../api/types';

/** What the stream needs of a WebSocket: the browser's own satisfies it, and a test passes a fake. */
export interface StreamSocket {
    onopen: (() => void) | null;
    onmessage: ((event: { data: unknown }) => void) | null;
    onclose: (() => void) | null;
    close(): void;
}

export interface StreamOptions {
    /** The page's origin, `location.origin`: the stream is the runtime's that served the page, in both shells. */
    origin: string;
    /** The token the page holds, or null while it holds none. */
    token: () => string | null;
    open: (url: string, protocols: string[]) => StreamSocket;
    later: (run: () => void, delay: number) => unknown;
    cancel: (handle: unknown) => void;
    /**
     * Whether the runtime answers an operation with the same token. A browser cannot read why an upgrade failed, so a
     * resume that closes before it opens while this is true was refused for its `after`, and the stream starts fresh.
     */
    reachable: () => Promise<boolean>;
}

export interface StreamState {
    status: 'connecting' | 'open' | 'waiting' | 'stopped';
    /** The id of the last line passed on, which the next connection resumes after. */
    last: string | null;
    /** True once a resume was refused and the stream started again from the lines written after it reopened. */
    fresh: boolean;
}

const route = '/v1/journal.stream';
const form = 'jason.journal.v1';
const bearer = 'jason.bearer.';
/** How many ids are remembered to drop a line sent twice; a resume repeats a few lines at most. */
const remembered = 1000;

export const retryDelay = (failures: number): number => Math.min(1000 * 2 ** failures, 30_000);

/** The stream's address: the origin with the ws scheme, and `after` when it resumes. */
export function streamAddress(origin: string, after: string | null): string {
    const base = origin.replace(/^http/, 'ws') + route;
    return after === null ? base : `${base}?after=${encodeURIComponent(after)}`;
}

/** Keeps one connection to the runtime's journal stream, resuming after the last line and passing each line on once. */
export class JournalStream {
    private current: StreamState = { status: 'stopped', last: null, fresh: false };
    private failures = 0;
    private socket: StreamSocket | null = null;
    private timer: unknown = null;
    private readonly seen = new Set<string>();
    private readonly lineListeners = new Set<(line: JournalEntry) => void>();
    private readonly stateListeners = new Set<(state: StreamState) => void>();

    constructor(private readonly options: StreamOptions) {}

    get state(): StreamState {
        return this.current;
    }

    onLine(listener: (line: JournalEntry) => void): () => void {
        this.lineListeners.add(listener);
        return () => this.lineListeners.delete(listener);
    }

    onState(listener: (state: StreamState) => void): () => void {
        this.stateListeners.add(listener);
        return () => this.stateListeners.delete(listener);
    }

    start(): void {
        if (this.current.status !== 'stopped') return;
        this.set({ status: 'connecting' });
        this.connect();
    }

    stop(): void {
        this.set({ status: 'stopped' });
        if (this.timer !== null) this.options.cancel(this.timer);
        this.timer = null;
        const socket = this.socket;
        this.socket = null;
        socket?.close();
    }

    private connect(): void {
        this.timer = null;
        if (this.current.status === 'stopped') return;
        const token = this.options.token();
        if (token === null) return this.again();
        const after = this.current.last;
        const socket = this.options.open(streamAddress(this.options.origin, after), [form, bearer + token]);
        this.socket = socket;
        let opened = false;
        socket.onopen = () => {
            if (this.socket !== socket) return;
            opened = true;
            this.failures = 0;
            this.set({ status: 'open' });
        };
        socket.onmessage = (event) => {
            if (this.socket === socket) this.receive(event.data);
        };
        socket.onclose = () => {
            if (this.socket !== socket) return;
            this.socket = null;
            if (!opened && after !== null) void this.refused();
            else this.again();
        };
    }

    private async refused(): Promise<void> {
        const answers = await this.options.reachable().catch(() => false);
        if (this.current.status === 'stopped') return;
        if (answers) this.set({ last: null, fresh: true });
        this.again();
    }

    private again(): void {
        if (this.current.status === 'stopped') return;
        this.set({ status: 'waiting' });
        this.timer = this.options.later(() => this.connect(), retryDelay(this.failures++));
    }

    private receive(data: unknown): void {
        let line: JournalEntry;
        try {
            line = JSON.parse(String(data)) as JournalEntry;
        } catch {
            return;
        }
        if (typeof line.id !== 'string' || this.seen.has(line.id)) return;
        this.seen.add(line.id);
        if (this.seen.size > remembered) {
            const oldest = this.seen.values().next();
            if (!oldest.done) this.seen.delete(oldest.value);
        }
        this.set({ last: line.id });
        for (const listener of this.lineListeners) listener(line);
    }

    private set(change: Partial<StreamState>): void {
        this.current = { ...this.current, ...change };
        for (const listener of this.stateListeners) listener(this.current);
    }
}

/** The stream as a page opens it: the browser's WebSocket and timers, and `system.info` as the runtime's answer. */
export function pageStream(token: () => string | null, reachable: () => Promise<boolean>): JournalStream {
    return new JournalStream({
        origin: location.origin,
        token,
        open: (url, protocols) => {
            const socket = new WebSocket(url, protocols);
            const stream: StreamSocket = { onopen: null, onmessage: null, onclose: null, close: () => socket.close() };
            socket.addEventListener('open', () => stream.onopen?.());
            socket.addEventListener('message', (event) => stream.onmessage?.(event));
            socket.addEventListener('close', () => stream.onclose?.());
            return stream;
        },
        later: (run, delay) => setTimeout(run, delay),
        cancel: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
        reachable,
    });
}
