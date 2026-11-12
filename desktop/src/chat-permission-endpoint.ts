// The chat's permission prompt tool: one MCP endpoint per turn, served by the main process over the Streamable HTTP
// transport on the loopback interface (the Model Context Protocol, revision 2025-11-25, "Transports"). Claude Code reaches it
// through the turn's MCP configuration, which carries the turn's secret as a bearer token. It decides nothing itself:
// every question goes to the broker, and only a click in the application's own page can say yes to a card.
//
// A call's stream carries no event ids, so it is neither primed nor resumable: the endpoint lives on loopback for one
// turn, and a stream that is cut means the host or the desktop is gone, when nothing is left to resume. That is the
// stream 2025-03-26 describes as well, which is why that revision is still spoken. A client that wants the call back
// cancels it (notifications/cancelled), and the card it waited on goes.
//
// A turn in Full access is also served its `PreToolUse` hook on the same port, at `/hook`, behind the same checks: Claude
// Code posts each tool use there before it runs, and the broker's answer is one of three: allow, ask, or deny.

import { timingSafeEqual } from 'node:crypto';
import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';
import type { ToolAnswer } from './chat-permission-broker.ts';

/** The protocol revisions the endpoint speaks, newest first. */
export const protocolVersions: readonly string[] = ['2025-11-25', '2025-06-18', '2025-03-26'];

export const serverName = 'jason_desktop';
export const toolName = 'decide';
/** The tool's name as Claude Code's `--permission-prompt-tool` takes it. */
export const promptToolName = `mcp__${serverName}__${toolName}`;

/**
 * What the turn's hook answers a tool use: `allow`, it runs with no question; `ask`, Claude Code puts it to the permission
 * prompt tool, where the broker shows its card, and `reason` says why; `deny`, it does not run, and `reason` is what
 * the agent is told.
 */
export type HookAnswer = { decision: 'allow' } | { decision: 'ask'; reason: string } | { decision: 'deny'; reason: string };

export interface EndpointOptions {
    secret: string;
    /** `signal` is aborted when the client cancels the call. */
    decide: (tool: string, input: Record<string, unknown>, signal: AbortSignal) => Promise<ToolAnswer>;
    /** How often a waiting call's stream says it is alive; thirty seconds unless a test shortens it. */
    keepAliveMs?: number;
    /**
     * A turn in Full access: what the turn's `PreToolUse` hook is answered for a tool use, by its tool, its input and the
     * id Claude Code gave it (`HookAnswer`). Absent, the hook's path is not served.
     */
    hook?: (tool: string, input: Record<string, unknown>, toolUseId: string | null) => HookAnswer;
}

export interface Endpoint {
    port: number;
    url: string;
    /** Where the turn's hook posts, on the same loopback port, behind the same secret. */
    hookUrl: string;
    close: () => Promise<void>;
}

const path = '/mcp';
const hookPath = '/hook';
/** What a hook call the endpoint cannot read is answered with: a refusal, never silence, since silence decides nothing. */
const unreadHook: HookAnswer = { decision: 'deny', reason: 'The desktop could not read what this tool would do, so it was not run.' };

/**
 * The hook's answer, in the shape Anthropic gives (https://code.claude.com/docs/en/hooks, read 2026-11-11): a 2xx response
 * whose JSON body's `hookSpecificOutput` names the event and its `permissionDecision`: `"allow"` "skips the permission
 * prompt", `"ask"` "prompts the user to confirm", which under `-p` is the permission prompt tool, and `"deny"`
 * "prevents the tool call", each of the last two with its `permissionDecisionReason`. Every answer decides: none is the
 * empty body, which decides nothing.
 */
function hookAnswer(response: ServerResponse, answer: HookAnswer): void {
    const output = answer.decision === 'allow' ? { permissionDecision: 'allow' } : { permissionDecision: answer.decision, permissionDecisionReason: answer.reason };
    json(response, { hookSpecificOutput: { hookEventName: 'PreToolUse', ...output } });
}
const bodyLimit = 8 * 1024 * 1024;
const inputSchema = {
    type: 'object',
    properties: { tool_name: { type: 'string' }, input: { type: 'object' }, tool_use_id: { type: 'string' } },
    required: ['tool_name', 'input'],
};

type Message = { jsonrpc?: unknown; id?: unknown; method?: unknown; params?: unknown; result?: unknown; error?: unknown };

function sameSecret(given: string, secret: string): boolean {
    const one = Buffer.from(given);
    const other = Buffer.from(secret);
    return one.length === other.length && timingSafeEqual(one, other);
}

function plain(response: ServerResponse, status: number, headers: Record<string, string> = {}): void {
    response.writeHead(status, { 'content-length': '0', ...headers });
    response.end();
}

function json(response: ServerResponse, body: object): void {
    const text = JSON.stringify(body);
    response.writeHead(200, { 'content-type': 'application/json', 'content-length': String(Buffer.byteLength(text)) });
    response.end(text);
}

function object(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readBody(request: IncomingMessage): Promise<string | null> {
    return new Promise((resolve, reject) => {
        const chunks: Buffer[] = [];
        let size = 0;
        request.on('data', (chunk: Buffer) => {
            size += chunk.length;
            if (size > bodyLimit) {
                resolve(null);
                request.removeAllListeners('data');
                request.resume();
                return;
            }
            chunks.push(chunk);
        });
        request.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
        request.on('error', reject);
    });
}

/** Opens the turn's endpoint on an ephemeral loopback port. */
export async function openEndpoint(options: EndpointOptions): Promise<Endpoint> {
    const keepAliveMs = options.keepAliveMs ?? 30_000;
    let port = 0;
    /** The calls still waiting, by their JSON-RPC id as the client wrote it, so a cancellation can name one. */
    const waiting = new Map<string, AbortController>();
    const idKey = (id: unknown) => JSON.stringify(id);

    /** A browser page could reach a loopback port; a page always says its Origin, and a rebound name its Host. */
    function trusted(request: IncomingMessage): number | null {
        if (request.headers.origin !== undefined) return 403;
        if (request.headers.host !== `127.0.0.1:${port}`) return 403;
        const authorization = request.headers.authorization ?? '';
        if (!authorization.startsWith('Bearer ') || !sameSecret(authorization.slice('Bearer '.length), options.secret)) return 401;
        return null;
    }

    function stream(response: ServerResponse, id: unknown, progressToken: unknown, withdrawn: AbortSignal, answer: Promise<ToolAnswer>): void {
        response.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' });
        response.write(': waiting for the answer\n\n');
        let progress = 0;
        const alive = setInterval(() => {
            if (response.writableEnded) return;
            if (typeof progressToken === 'string' || typeof progressToken === 'number') {
                progress += 1;
                response.write(`data: ${JSON.stringify({ jsonrpc: '2.0', method: 'notifications/progress', params: { progressToken, progress } })}\n\n`);
            } else {
                response.write(': still waiting\n\n');
            }
        }, keepAliveMs);
        const finish = (body: ToolAnswer) => {
            clearInterval(alive);
            if (response.writableEnded || response.destroyed) return;
            // A cancelled request is not answered: its stream just ends.
            if (withdrawn.aborted) {
                response.end();
                return;
            }
            const result = { content: [{ type: 'text', text: JSON.stringify(body) }] };
            response.end(`event: message\ndata: ${JSON.stringify({ jsonrpc: '2.0', id, result })}\n\n`);
        };
        answer.then(finish, () => finish({ behavior: 'deny', message: 'The application could not decide, so nothing was run.' }));
        response.on('close', () => clearInterval(alive));
    }

    function answer(message: Message, response: ServerResponse): void {
        const { id, method, params } = message;
        if (id === undefined) {
            // A notification: the client saying it is initialized, or withdrawing a call it made.
            if (method === 'notifications/cancelled') {
                const named = object(params) ? params['requestId'] : undefined;
                if (typeof named === 'string' || typeof named === 'number') waiting.get(idKey(named))?.abort();
                plain(response, 202);
                return;
            }
            plain(response, method === 'notifications/initialized' ? 202 : 400);
            return;
        }
        if (typeof method !== 'string') {
            plain(response, 400);
            return;
        }
        if (method === 'initialize') {
            const asked = object(params) ? params['protocolVersion'] : undefined;
            const protocolVersion = typeof asked === 'string' && protocolVersions.includes(asked) ? asked : protocolVersions[0];
            json(response, { jsonrpc: '2.0', id, result: { protocolVersion, capabilities: { tools: {} }, serverInfo: { name: 'jason-desktop', version: '1' } } });
        } else if (method === 'ping') {
            json(response, { jsonrpc: '2.0', id, result: {} });
        } else if (method === 'tools/list') {
            json(response, { jsonrpc: '2.0', id, result: { tools: [{ name: toolName, description: 'Asks the person whether a tool may run.', inputSchema }] } });
        } else if (method === 'tools/call') {
            const args = object(params) ? params['arguments'] : undefined;
            const tool = object(args) ? args['tool_name'] : undefined;
            const input = object(args) ? args['input'] : undefined;
            if (!object(params) || params['name'] !== toolName || typeof tool !== 'string' || !object(input)) {
                json(response, { jsonrpc: '2.0', id, error: { code: -32602, message: 'Only decide, with a tool name and its input, is offered here.' } });
                return;
            }
            const meta = params['_meta'];
            const withdrawn = new AbortController();
            const key = idKey(id);
            waiting.set(key, withdrawn);
            const answer = options.decide(tool, input, withdrawn.signal);
            void answer.finally(() => {
                if (waiting.get(key) === withdrawn) waiting.delete(key);
            }).catch(() => undefined);
            stream(response, id, object(meta) ? meta['progressToken'] : undefined, withdrawn.signal, answer);
        } else {
            json(response, { jsonrpc: '2.0', id, error: { code: -32601, message: 'Method not found.' } });
        }
    }

    /**
     * One call of the turn's hook, as Claude Code posts it: the event's JSON, its tool's name, input and use id among it.
     * A body too large or out of shape is a refusal.
     */
    async function serveHook(request: IncomingMessage, response: ServerResponse, hook: NonNullable<EndpointOptions['hook']>): Promise<void> {
        const text = await readBody(request);
        let event: unknown = null;
        try {
            event = text === null ? null : JSON.parse(text);
        } catch {
            event = null;
        }
        const tool = object(event) ? event['tool_name'] : undefined;
        const input = object(event) ? event['tool_input'] : undefined;
        if (!object(event) || typeof tool !== 'string' || !object(input)) {
            hookAnswer(response, unreadHook);
            return;
        }
        const use = event['tool_use_id'];
        hookAnswer(response, hook(tool, input, typeof use === 'string' ? use : null));
    }

    async function serve(request: IncomingMessage, response: ServerResponse): Promise<void> {
        const refused = trusted(request);
        if (refused !== null) {
            plain(response, refused);
            request.resume();
            return;
        }
        const asked = (request.url ?? '').split('?')[0];
        if (asked === hookPath && options.hook !== undefined && request.method === 'POST') {
            await serveHook(request, response, options.hook);
            return;
        }
        if (asked !== path) {
            plain(response, 404);
            request.resume();
            return;
        }
        if (request.method !== 'POST') {
            plain(response, 405, { allow: 'POST' });
            request.resume();
            return;
        }
        const version = request.headers['mcp-protocol-version'];
        if (typeof version === 'string' && !protocolVersions.includes(version)) {
            plain(response, 400);
            request.resume();
            return;
        }
        const text = await readBody(request);
        if (text === null) {
            plain(response, 413);
            return;
        }
        let message: unknown;
        try {
            message = JSON.parse(text);
        } catch {
            plain(response, 400);
            return;
        }
        if (!object(message) || message['jsonrpc'] !== '2.0' || (message['method'] === undefined && message['id'] !== undefined)) {
            plain(response, 400);
            return;
        }
        answer(message, response);
    }

    const server = createServer((request, response) => {
        serve(request, response).catch(() => {
            if (!response.headersSent) plain(response, 500);
            else response.end();
        });
    });
    await new Promise<void>((resolve, reject) => {
        server.once('error', reject);
        server.listen(0, '127.0.0.1', () => resolve());
    });
    const bound = server.address() as AddressInfo;
    port = bound.port;
    return {
        port,
        url: `http://${bound.address}:${port}${path}`,
        hookUrl: `http://${bound.address}:${port}${hookPath}`,
        close: () =>
            new Promise<void>((resolve) => {
                server.close(() => resolve());
                server.closeAllConnections();
            }),
    };
}
