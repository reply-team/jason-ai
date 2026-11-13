import type { JasonDesktop } from './desktop';

// The chat part of the desktop's seam, carried over HTTP to the bridge the desktop opens for a browser tab. The members
// are typed against the same declaration the desktop's window is typed by, so a member cannot drift from it. The bridge
// answers only the members of one table; the table is stated here once more so a call outside it is refused before it
// is sent, and the bridge refuses it again on its side.

/** The members a tab may call, by their name in the seam. Everything else is done in the desktop's own window. */
export const bridgeMembers = [
    'mode.get',
    'theme.get',
    'fullAccess.standing',
    'setup.status',
    'chat.list',
    'chat.open',
    'chat.create',
    'chat.send',
    'chat.stop',
    'chat.rename',
] as const;

export type BridgeMember = (typeof bridgeMembers)[number];

/** What a tab's seam holds: the members of the table, and the subscriptions the bridge's event stream feeds. */
export interface BridgeSeam {
    mode: Pick<JasonDesktop['mode'], 'get'>;
    theme: Pick<JasonDesktop['theme'], 'get'>;
    fullAccess: Pick<JasonDesktop['fullAccess'], 'standing' | 'onChanged'>;
    setup: Pick<JasonDesktop['setup'], 'status' | 'onChanged'>;
    chat: Pick<JasonDesktop['chat'], 'list' | 'open' | 'create' | 'send' | 'stop' | 'rename' | 'onEvent'>;
}

/** The sentence for a member outside the table. */
export const notInTab = "That is done in Jason Desktop's own window.";

export interface BridgePair {
    port: number;
    secret: string;
}

type Listener = (...args: never[]) => void;
type Fetch = typeof fetch;

/** The bridge's address and a call over it, for one pair. */
export class Bridge {
    private readonly listeners = new Map<string, Set<Listener>>();
    private stream: AbortController | null = null;

    constructor(
        readonly pair: BridgePair,
        private readonly doFetch: Fetch = (...args) => fetch(...args),
        /** Called once when the bridge says it no longer knows the secret. */
        private readonly onGone: () => void = () => undefined,
    ) {}

    private get base(): string {
        return `http://127.0.0.1:${this.pair.port}`;
    }

    /** One call. A member outside the table is refused here; the bridge's refusal sentence comes back as an error. */
    async call(member: BridgeMember, args: unknown[] = []): Promise<unknown> {
        if (!(bridgeMembers as readonly string[]).includes(member)) throw new Error(notInTab);
        const response = await this.doFetch(`${this.base}/call`, {
            method: 'POST',
            headers: { 'content-type': 'application/json', authorization: `Bearer ${this.pair.secret}` },
            body: JSON.stringify({ member, args }),
        });
        if (response.status === 401) {
            this.onGone();
            throw new BridgeGone();
        }
        const body = (await response.json().catch(() => ({}))) as { result?: unknown; refused?: unknown };
        if (typeof body.refused === 'string') throw new Error(body.refused);
        if (!response.ok) throw new Error(notInTab);
        return body.result ?? null;
    }

    /** Whether the pair still opens the bridge: one cheap read. */
    async alive(): Promise<boolean> {
        try {
            await this.call('mode.get');
            return true;
        } catch {
            return false;
        }
    }

    private on(channel: string, listener: Listener): () => void {
        let set = this.listeners.get(channel);
        if (!set) this.listeners.set(channel, (set = new Set()));
        set.add(listener);
        void this.listen();
        return () => {
            set.delete(listener);
            if ([...this.listeners.values()].every((each) => each.size === 0)) this.close();
        };
    }

    /** Ends the event stream. */
    close(): void {
        this.stream?.abort();
        this.stream = null;
    }

    /** Reads the bridge's event lines until closed; a line is `{channel, args}`, a heartbeat is skipped. */
    private async listen(): Promise<void> {
        if (this.stream) return;
        const stream = (this.stream = new AbortController());
        try {
            const response = await this.doFetch(`${this.base}/events`, {
                headers: { authorization: `Bearer ${this.pair.secret}` },
                signal: stream.signal,
            });
            if (!response.ok || !response.body) return;
            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            let rest = '';
            for (;;) {
                const { done, value } = await reader.read();
                if (done) break;
                rest += decoder.decode(value, { stream: true });
                const lines = rest.split('\n');
                rest = lines.pop() ?? '';
                for (const line of lines) this.deliver(line);
            }
        } catch {
            // A closed or broken stream ends the events; the tab opens again to read the conversation anew.
        } finally {
            if (this.stream === stream) this.stream = null;
        }
    }

    private deliver(line: string): void {
        if (!line.trim()) return;
        let message: { channel?: unknown; args?: unknown };
        try {
            message = JSON.parse(line) as typeof message;
        } catch {
            return;
        }
        if (typeof message.channel !== 'string' || !Array.isArray(message.args)) return;
        for (const listener of this.listeners.get(message.channel) ?? [])
            (listener as (...args: unknown[]) => void)(...(message.args as unknown[]));
    }

    /** The tab's seam over this bridge. */
    seam(): BridgeSeam {
        const call = <T>(member: BridgeMember, ...args: unknown[]) => this.call(member, args) as Promise<T>;
        return {
            mode: { get: () => call('mode.get') },
            theme: { get: () => call('theme.get') },
            fullAccess: {
                standing: () => call('fullAccess.standing'),
                onChanged: (handler) => this.on('fullAccess.changed', handler as Listener),
            },
            setup: {
                status: () => call('setup.status'),
                onChanged: (handler) => this.on('setup.changed', handler as Listener),
            },
            chat: {
                list: () => call('chat.list'),
                open: (chatId) => call('chat.open', chatId),
                create: (campaignId, host) => call('chat.create', campaignId, host),
                send: (chatId, text, options) => call('chat.send', chatId, text, options),
                stop: async (chatId) => void (await call('chat.stop', chatId)),
                rename: (chatId, title) => call('chat.rename', chatId, title),
                onEvent: (handler) => this.on('chat.event', handler as Listener),
            },
        };
    }
}

/** The bridge no longer knows the secret. */
export class BridgeGone extends Error {
    constructor() {
        super('The connection to Jason Desktop is gone.');
    }
}

/** The tab's seam shaped as the whole one, for the console's state; a member outside the table is not there. */
export const asDesktop = (seam: BridgeSeam): JasonDesktop => seam as unknown as JasonDesktop;
