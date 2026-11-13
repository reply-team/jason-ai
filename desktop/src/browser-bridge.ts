/**
 * The bridge a browser tab reaches the desktop's chat through: one more view onto the same chat the window shows, while
 * the desktop runs. It listens on the loopback address, on a port the system picks, only once the person chose "Open in"
 * a browser in this run, and closes with the window and at quit; every session dies with it.
 *
 * Every request meets two checks before anything of it is read: its `Host` is exactly `127.0.0.1:<port>`, so a name
 * rebound to the loopback address reaches nothing, and its `Origin` is the runtime's own, read from the run file at that
 * moment as the window's check reads it, so only the page the runtime serves can ask. Anything else is 403 with an
 * empty body. A preflight is answered for that origin alone, and never with credentials.
 *
 * A tab is let in by a code the main process mints for one opening and carries in the link's fragment, never on disk:
 * good for sixty seconds and for one use. Trading it at `/pair` gives the tab a secret, kept in memory here and in the
 * tab, which every later call carries as a bearer. Nothing on disk and nothing the runtime holds opens the bridge.
 *
 * What a tab may ask is one table. A member not in it is refused with one sentence; a member in it hands its arguments
 * to the very handler the window's channel runs (`ipc.ts`'s registry), behind the row's own rule. A tab never says yes:
 * a permission card is answered, a chat's mode is changed and Full access is chosen in the desktop's own window, a chat
 * in Full access takes no message from a tab, and a chat a tab makes starts in Ask first.
 *
 * The log names what was refused, and never a text: no code, no secret, no argument.
 */

import { createHash, randomBytes, timingSafeEqual } from 'node:crypto';
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';
import { isOwnPage, pairingLink } from './descriptor.ts';
import { invokeChannels, type Handler, type InvokeMember } from './ipc.ts';
import type { Logger } from './log.ts';

/** What a tab is told when it asks for a member outside the table. */
export const elsewhere = "That is done in Jason Desktop's own window.";
/** What a tab is told when it sends to a chat in Full access. */
export const fullAccessElsewhere = 'This chat is in Full access. Send messages to it from Jason Desktop.';
/** What a tab is told when it sends to a chat in Auto-edit. */
export const autoEditElsewhere = 'This chat is in Auto-edit. Send messages to it from Jason Desktop.';
/** What a tab is told when it sends to a chat in any other mode a tab does not send in. */
export const otherModeElsewhere = 'This chat is in a mode a tab cannot send to. Send messages to it from Jason Desktop.';
/** What a tab is told when its send names a mode the chat is not in. */
export const modeElsewhere = "A browser tab sends in the chat's own mode. Its mode is changed in Jason Desktop's own window.";

/** The pushed channels a session reads, of those the window is sent. */
export type BridgeChannel = 'chat.event' | 'fullAccess.changed' | 'setup.changed';
export const bridgeChannels: readonly BridgeChannel[] = ['chat.event', 'fullAccess.changed', 'setup.changed'];
export const isBridgeChannel = (channel: string): channel is BridgeChannel => (bridgeChannels as readonly string[]).includes(channel);

/** A row's own rule: the arguments to hand the handler, or a refusal the tab is answered with as a value. */
type Ruled = { args: unknown[] } | { refused: string; why: 'full_access' | 'auto_edit' | 'other_mode' | 'no_chat' | 'mode' };

interface Row {
    /** How many arguments the handler takes; what a tab sends past them is not handed on. */
    arity: number;
    rule?: (args: unknown[], modeOf: (chatId: unknown) => string | null) => Promise<Ruled>;
}

/** The modes a tab sends in: an allowlist, so a mode added later is refused until it is named here. */
const tabModes: readonly string[] = ['ask_first', 'plan_only'];

/** The refusal for a chat in a mode a tab does not send in, named where the mode has a sentence of its own. */
function modeRefusal(mode: string): Ruled {
    if (mode === 'full_access') return { refused: fullAccessElsewhere, why: 'full_access' };
    if (mode === 'auto_edit') return { refused: autoEditElsewhere, why: 'auto_edit' };
    return { refused: otherModeElsewhere, why: 'other_mode' };
}

/**
 * The table, exactly: six reads and four acts. Everything else the window may ask is outside it by not being here, among
 * them every answer to a card, every choice of a mode, the Feed (the window feeds it; two feeders would double it), and
 * every act of set-up, of the demo, of the theme, of Open in, of links and of the window's pairing.
 */
const rows = new Map<InvokeMember, Row>([
    ['mode.get', { arity: 0 }],
    ['theme.get', { arity: 0 }],
    ['fullAccess.standing', { arity: 0 }],
    ['setup.status', { arity: 0 }],
    ['chat.list', { arity: 0 }],
    ['chat.open', { arity: 1 }],
    // The host is not handed on: it chooses no program (a send does), and it is what lets a standing choice put a new chat
    // in Full access. Without it the chat starts in Ask first, whatever the standing choice says.
    ['chat.create', { arity: 1, rule: async (args) => ({ args: [args[0]] }) }],
    [
        'chat.send',
        {
            arity: 3,
            // The chat's mode is read as the chat holds it now, kept or not (`Chat.modeNow`), with no side effect. A tab sends
            // only to a chat in Ask first or Plan only; any other mode is refused. The send carries that mode, which the chat
            // checks again as it takes the send: a mode changed between the two refuses it. A chat the desktop does not hold
            // has no mode a tab sends in, so it is refused here and the send handler never runs.
            rule: async (args, modeOf) => {
                const [chatId, text, options] = args;
                const mode = modeOf(chatId);
                if (mode === null) return { refused: otherModeElsewhere, why: 'no_chat' };
                if (!tabModes.includes(mode)) return modeRefusal(mode);
                const given = typeof options === 'object' && options !== null && !Array.isArray(options) ? (options as Record<string, unknown>) : {};
                if (given.mode !== undefined && given.mode !== mode) return { refused: modeElsewhere, why: 'mode' };
                return { args: [chatId, text, { ...given, mode }] };
            },
        },
    ],
    ['chat.stop', { arity: 1 }],
    ['chat.rename', { arity: 2 }],
]);

/** The members a tab may ask for. */
export const bridgeMembers: readonly InvokeMember[] = [...rows.keys()];

const knownMembers = new Set<string>(Object.keys(invokeChannels));

/** A body larger than this is refused before it is read further. */
export const bodyLimit = 1024 * 1024;
const pairBodyLimit = 4 * 1024;
/** How much of a body past its limit is read and dropped before the connection is cut. */
const drainLimit = 8 * 1024 * 1024;
/** How long a minted code pairs. */
export const codeLifeMs = 60_000;
/** How many sessions live at once; past it the oldest is ended. */
export const sessionLimit = 4;
/** How many codes wait to be traded at once; past it the oldest is dropped. */
const pendingLimit = 4;

export interface BridgeOptions {
    /** The runtime's origin, read from the run file at each request, as the window's check reads it; null with none. */
    origin(): string | null;
    /** Each member's handler, as `ipc.ts` registered it for the window. */
    members: ReadonlyMap<InvokeMember, Handler>;
    /** A chat's mode as the chat holds it now, by its id, kept or not; null for one it does not hold. Reads alone. */
    modeOf(chatId: unknown): string | null;
    log: Logger;
    now?(): number;
    /** How often a stream says it is alive; fifteen seconds unless a test shortens it. */
    heartbeatMs?: number;
    /** How much a stream may hold that its tab has not read; past it the stream is closed. */
    queueBytes?: number;
}

const digest = (text: string) => createHash('sha256').update(text, 'utf8').digest();
/** Whether two digests are equal, in a time that does not depend on where they differ. */
const same = (a: Buffer, b: Buffer) => timingSafeEqual(a, b);

/** One stream of a session: lines written while the tab reads, held while it does not, closed past the bound. */
class Stream {
    readonly #response: ServerResponse;
    readonly #limit: number;
    readonly #closed: (why: 'overflow' | 'replaced' | 'ended' | 'closed') => void;
    readonly #beat: ReturnType<typeof setInterval>;
    #held: string[] = [];
    #heldBytes = 0;
    #blocked = false;
    #done = false;

    constructor(response: ServerResponse, limit: number, heartbeatMs: number, closed: (why: 'overflow' | 'replaced' | 'ended' | 'closed') => void) {
        this.#response = response;
        this.#limit = limit;
        this.#closed = closed;
        this.#beat = setInterval(() => this.send(JSON.stringify({ heartbeat: true }) + '\n'), heartbeatMs);
        this.#beat.unref();
        response.on('close', () => this.end('ended'));
    }

    send(line: string): void {
        if (this.#done) return;
        if (this.#blocked) {
            this.#held.push(line);
            this.#heldBytes += Buffer.byteLength(line);
            // The tab reads the conversation anew when its stream is closed; a stream it does not read is not kept growing.
            if (this.#heldBytes > this.#limit) this.end('overflow');
            return;
        }
        if (!this.#response.write(line)) {
            this.#blocked = true;
            this.#response.once('drain', () => this.#flush());
        }
    }

    #flush(): void {
        this.#blocked = false;
        while (this.#held.length > 0 && !this.#done) {
            const line = this.#held.shift() as string;
            this.#heldBytes -= Buffer.byteLength(line);
            if (!this.#response.write(line)) {
                this.#blocked = true;
                this.#response.once('drain', () => this.#flush());
                return;
            }
        }
    }

    end(why: 'overflow' | 'replaced' | 'ended' | 'closed'): void {
        if (this.#done) return;
        this.#done = true;
        clearInterval(this.#beat);
        this.#held = [];
        // A stream past its bound or replaced is cut: what it holds is not sent, and the tab reads the conversation anew.
        this.#response.destroy();
        this.#closed(why);
    }
}

interface Session {
    secret: Buffer;
    stream: Stream | null;
}

export class BrowserBridge {
    readonly #options: BridgeOptions;
    readonly #server: Server;
    readonly #sessions: Session[] = [];
    #pending: { code: Buffer; until: number }[] = [];
    #port = 0;
    #closed = false;

    private constructor(options: BridgeOptions) {
        this.#options = options;
        this.#server = createServer((request, response) => {
            this.#serve(request, response).catch(() => {
                if (!response.headersSent) response.writeHead(500).end();
                else response.destroy();
            });
        });
    }

    /** Opens the bridge on a port of the loopback address the system picks. */
    static async open(options: BridgeOptions): Promise<BrowserBridge> {
        const bridge = new BrowserBridge(options);
        await new Promise<void>((resolve, reject) => {
            bridge.#server.once('error', reject);
            bridge.#server.listen(0, '127.0.0.1', () => resolve());
        });
        bridge.#port = (bridge.#server.address() as AddressInfo).port;
        options.log.info('bridge_opened', { Port: bridge.#port });
        return bridge;
    }

    get port(): number {
        return this.#port;
    }

    #now(): number {
        return this.#options.now?.() ?? Date.now();
    }

    /** A code for one opening: random, good for sixty seconds and for one use. Kept here as its digest alone. */
    mint(): string {
        const code = randomBytes(16).toString('base64url');
        const now = this.#now();
        this.#pending = this.#pending.filter((each) => each.until > now);
        this.#pending.push({ code: digest(code), until: now + codeLifeMs });
        while (this.#pending.length > pendingLimit) this.#pending.shift();
        return code;
    }

    /** Hands a pushed event to every session's stream; a session with no stream open misses it and reads anew. */
    publish(channel: BridgeChannel, args: unknown[]): void {
        if (this.#closed || this.#sessions.length === 0) return;
        const line = JSON.stringify({ channel, args }) + '\n';
        for (const session of this.#sessions) session.stream?.send(line);
    }

    /** Ends every stream and session and stops listening. */
    close(): Promise<void> {
        if (this.#closed) return Promise.resolve();
        this.#closed = true;
        const ended = this.#sessions.length;
        for (const session of this.#sessions.splice(0)) session.stream?.end('closed');
        this.#pending = [];
        this.#options.log.info('bridge_closed', { Sessions: ended });
        return new Promise<void>((resolve) => {
            this.#server.close(() => resolve());
            this.#server.closeAllConnections();
        });
    }

    #refuse(why: string, member = '-'): void {
        this.#options.log.warn('bridge_refused', { Why: why, Member: member });
    }

    async #serve(request: IncomingMessage, response: ServerResponse): Promise<void> {
        // Nothing of the request is read before these two: not its path, not its body.
        if (request.headers.host !== `127.0.0.1:${this.#port}`) {
            this.#refuse('host');
            return void response.writeHead(403).end();
        }
        const origin = this.#options.origin();
        if (origin === null || request.headers.origin !== origin) {
            this.#refuse('origin');
            return void response.writeHead(403).end();
        }
        response.setHeader('access-control-allow-origin', origin);
        response.setHeader('vary', 'Origin');
        response.setHeader('cache-control', 'no-store');
        if (request.method === 'OPTIONS') {
            response.writeHead(204, { 'access-control-allow-methods': 'GET, POST', 'access-control-allow-headers': 'authorization, content-type' });
            return void response.end();
        }
        if (request.method === 'POST' && request.url === '/pair') return this.#pair(request, response);
        if (request.method === 'POST' && request.url === '/call') return this.#call(request, response);
        if (request.method === 'GET' && request.url === '/events') return this.#events(request, response);
        response.writeHead(404).end();
    }

    /** The body, or the status it is refused with: 413 past the limit, read no further. */
    #body(request: IncomingMessage, response: ServerResponse, limit: number): Promise<string | null> {
        return new Promise((resolve, reject) => {
            const chunks: Buffer[] = [];
            let size = 0;
            let over = false;
            const declared = Number(request.headers['content-length']);
            // A body past the limit is kept no further: the rest is read and dropped, so the tab is answered 413 rather than
            // cut off mid-send, up to a bound past which the connection is cut.
            const refuse = () => {
                over = true;
                chunks.length = 0;
                this.#refuse('body');
            };
            if (Number.isFinite(declared) && declared > drainLimit) {
                this.#refuse('body');
                request.destroy();
                return resolve(null);
            }
            if (Number.isFinite(declared) && declared > limit) refuse();
            request.on('data', (chunk: Buffer) => {
                size += chunk.length;
                if (over) {
                    if (size > drainLimit) request.destroy();
                    return;
                }
                if (size > limit) return refuse();
                chunks.push(chunk);
            });
            request.on('end', () => {
                if (!over) return resolve(Buffer.concat(chunks).toString('utf8'));
                response.writeHead(413, { connection: 'close' }).end();
                resolve(null);
            });
            request.on('close', () => resolve(null));
            request.on('error', reject);
        });
    }

    #session(request: IncomingMessage): Session | null {
        const match = /^Bearer ([A-Za-z0-9_-]{43})$/.exec(request.headers.authorization ?? '');
        if (match === null) return null;
        const given = digest(match[1] as string);
        let found: Session | null = null;
        for (const session of this.#sessions) if (same(session.secret, given)) found = session;
        return found;
    }

    async #pair(request: IncomingMessage, response: ServerResponse): Promise<void> {
        const body = await this.#body(request, response, pairBodyLimit);
        if (body === null) return;
        let code: unknown;
        try {
            code = (JSON.parse(body) as { code?: unknown }).code;
        } catch {
            code = undefined;
        }
        const now = this.#now();
        this.#pending = this.#pending.filter((each) => each.until > now);
        const given = typeof code === 'string' && /^[A-Za-z0-9_-]{22}$/.test(code) ? digest(code) : null;
        let matched: { code: Buffer; until: number } | null = null;
        if (given !== null) for (const each of this.#pending) if (same(each.code, given)) matched = each;
        if (matched === null || this.#closed) {
            this.#refuse('code');
            return void response.writeHead(401).end();
        }
        this.#pending = this.#pending.filter((each) => each !== matched);
        const secret = randomBytes(32).toString('base64url');
        this.#sessions.push({ secret: digest(secret), stream: null });
        while (this.#sessions.length > sessionLimit) this.#sessions.shift()?.stream?.end('closed');
        this.#options.log.info('bridge_paired', { Sessions: this.#sessions.length });
        response.writeHead(200, { 'content-type': 'application/json' }).end(JSON.stringify({ secret }));
    }

    async #call(request: IncomingMessage, response: ServerResponse): Promise<void> {
        const session = this.#session(request);
        if (session === null) {
            this.#refuse('bearer');
            return void response.writeHead(401).end();
        }
        const body = await this.#body(request, response, bodyLimit);
        if (body === null) return;
        let parsed: unknown;
        try {
            parsed = JSON.parse(body);
        } catch {
            parsed = undefined;
        }
        const { member, args } = (typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed) ? parsed : {}) as { member?: unknown; args?: unknown };
        if (typeof member !== 'string' || !Array.isArray(args)) {
            this.#refuse('shape');
            return void response.writeHead(400).end();
        }
        const row = knownMembers.has(member) ? rows.get(member as InvokeMember) : undefined;
        if (row === undefined) {
            // The member is named in the log only when it is one of the desktop's own names: anything else is a text.
            this.#refuse('member', knownMembers.has(member) ? member : 'unknown');
            return void answer(response, 403, { refused: elsewhere });
        }
        const handler = this.#options.members.get(member as InvokeMember);
        if (handler === undefined) return void answer(response, 500, { error: 'The desktop has no handler for that yet.' });
        const given = args.slice(0, row.arity);
        const ruled: Ruled = row.rule === undefined ? { args: given } : await row.rule(given, (chatId) => this.#options.modeOf(chatId));
        if ('refused' in ruled) {
            this.#refuse(ruled.why, member);
            return void answer(response, 200, { result: { refused: ruled.refused } });
        }
        let value: unknown;
        try {
            value = await handler(...ruled.args);
        } catch (error) {
            return void answer(response, 500, { error: error instanceof Error ? error.message : String(error) });
        }
        answer(response, 200, { result: value === undefined ? null : value });
    }

    #events(request: IncomingMessage, response: ServerResponse): void {
        const session = this.#session(request);
        if (session === null) {
            this.#refuse('bearer');
            return void response.writeHead(401).end();
        }
        // One stream a session: a second one ends the first.
        session.stream?.end('replaced');
        response.writeHead(200, { 'content-type': 'application/x-ndjson' });
        response.flushHeaders();
        const stream = new Stream(response, this.#options.queueBytes ?? 4 * 1024 * 1024, this.#options.heartbeatMs ?? 15_000, (why) => {
            if (session.stream === stream) session.stream = null;
            if (why === 'overflow' || why === 'replaced') this.#options.log.info('bridge_stream_closed', { Why: why });
        });
        session.stream = stream;
    }
}

function answer(response: ServerResponse, status: number, body: unknown): void {
    response.writeHead(status, { 'content-type': 'application/json' }).end(JSON.stringify(body));
}

/**
 * The one bridge of this run: opened on the first ask, the same one after, and none once it is closed, which the window's
 * close and the quit do. Two asks at once open one.
 */
export class BridgeHolder {
    readonly #open: () => Promise<BrowserBridge>;
    #opening: Promise<BrowserBridge> | null = null;
    #current: BrowserBridge | null = null;
    #closed = false;

    constructor(open: () => Promise<BrowserBridge>) {
        this.#open = open;
    }

    get current(): BrowserBridge | null {
        return this.#current;
    }

    async ensure(): Promise<BrowserBridge | null> {
        if (this.#closed) return null;
        this.#opening ??= this.#open().then((bridge) => {
            this.#current = bridge;
            // Closed while it opened: it does not outlive the window either.
            if (this.#closed) void bridge.close();
            return bridge;
        });
        const bridge = await this.#opening;
        return this.#closed ? null : bridge;
    }

    /** Hands a pushed event to the bridge's sessions, when a bridge is open. */
    publish(channel: string, args: unknown[]): void {
        if (isBridgeChannel(channel)) this.#current?.publish(channel, args);
    }

    async close(): Promise<void> {
        this.#closed = true;
        const bridge = this.#current;
        this.#current = null;
        await bridge?.close();
    }
}

/** The link with `d=<port>.<code>` inside its fragment's query, after the ticket the runtime put there. */
export function bridgeLink(link: string, port: number, code: string): string {
    const pair = `d=${port}.${code}`;
    const at = link.indexOf('#');
    if (at < 0) return `${link}#/?${pair}`;
    return link.slice(at).includes('?') ? `${link}&${pair}` : `${link}?${pair}`;
}

/**
 * The second way to the system browser, beside the https-only one: a page of the runtime's own UI, held here, at the
 * moment it is opened, to the runtime's origin as the run file says it now and to a path under `/ui/`, whatever its
 * caller held it to. main.ts's navigation lock hands it the system's opener; the log names the act and never the link.
 */
export async function openOwnPage(url: string, deps: { origin(): string | null; open(url: string): Promise<void>; log: Logger }): Promise<void> {
    const here = deps.origin();
    if (here === null || !isOwnPage(url, here)) {
        deps.log.warn('own_page_refused', {});
        throw new Error("Only Jason's own page goes to the browser this way.");
    }
    try {
        await deps.open(url);
    } catch (error) {
        deps.log.warn('link_open_failed', {});
        throw error;
    }
    deps.log.info('own_page_opened', {});
}

export interface TabDeps {
    origin(): string | null;
    cli: { run(args: readonly string[]): Promise<{ exit: number; stdout: string; stderr: string }> };
    bridge(): Promise<BrowserBridge | null>;
    /** Opens the link in the system's browser. */
    open(link: string): Promise<void>;
    log?: Logger;
}

/**
 * Opens the chat in a browser tab: the bundled CLI is asked for a pairing link, which is held to the runtime's origin and
 * its `/ui/` path as the window's own link is held, and a code of the bridge is added to its fragment. No part of the
 * address comes from the page. Answers the sentence why no tab was opened, or null.
 */
export async function openTab(deps: TabDeps): Promise<string | null> {
    const origin = deps.origin();
    if (origin === null) {
        deps.log?.warn('bridge_tab_refused', { Why: 'no_runtime' });
        return "Jason's runtime is not running.";
    }
    const bridge = await deps.bridge();
    if (bridge === null) {
        deps.log?.warn('bridge_tab_refused', { Why: 'closing' });
        return 'Jason Desktop is closing.';
    }
    const open = await deps.cli.run(['ui', 'open', '--no-open']);
    const link = open.exit === 0 ? pairingLink(open.stdout, origin) : null;
    if (link === null) {
        deps.log?.warn('bridge_tab_refused', { Why: 'no_link' });
        return "Jason's page could not be opened in a browser.";
    }
    try {
        await deps.open(bridgeLink(link, bridge.port, bridge.mint()));
    } catch {
        deps.log?.warn('bridge_tab_refused', { Why: 'not_opened' });
        return "The system's browser could not be opened.";
    }
    return null;
}
