// The chat's permission prompt tool: one MCP endpoint per turn, served by the main process over the Streamable HTTP
// transport on the loopback interface (the Model Context Protocol, revision 2025-11-25, "Transports"). Claude Code reaches it
// through the turn's MCP configuration, which carries the turn's secret as a bearer token. It decides nothing itself:
// every question goes to the broker, and only a click in the application's own page can say yes to a card.

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

export interface EndpointOptions {
    secret: string;
    decide: (tool: string, input: Record<string, unknown>) => Promise<ToolAnswer>;
    /** How often a waiting call's stream says it is alive; thirty seconds unless a test shortens it. */
    keepAliveMs?: number;
}

export interface Endpoint {
    port: number;
    url: string;
    close: () => Promise<void>;
}

const path = '/mcp';
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

    /** A browser page could reach a loopback port; a page always says its Origin, and a rebound name its Host. */
    function trusted(request: IncomingMessage): number | null {
        if (request.headers.origin !== undefined) return 403;
        if (request.headers.host !== `127.0.0.1:${port}`) return 403;
        const authorization = request.headers.authorization ?? '';
        if (!authorization.startsWith('Bearer ') || !sameSecret(authorization.slice('Bearer '.length), options.secret)) return 401;
        return null;
    }

    function stream(response: ServerResponse, id: unknown, progressToken: unknown, answer: Promise<ToolAnswer>): void {
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
            const result = { content: [{ type: 'text', text: JSON.stringify(body) }] };
            response.end(`event: message\ndata: ${JSON.stringify({ jsonrpc: '2.0', id, result })}\n\n`);
        };
        answer.then(finish, () => finish({ behavior: 'deny', message: 'The application could not decide, so nothing was run.' }));
        response.on('close', () => clearInterval(alive));
    }

    function answer(message: Message, response: ServerResponse): void {
        const { id, method, params } = message;
        if (id === undefined) {
            // A notification: the one this endpoint accepts is the client saying it is initialized.
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
            stream(response, id, object(meta) ? meta['progressToken'] : undefined, options.decide(tool, input));
        } else {
            json(response, { jsonrpc: '2.0', id, error: { code: -32601, message: 'Method not found.' } });
        }
    }

    async function serve(request: IncomingMessage, response: ServerResponse): Promise<void> {
        const refused = trusted(request);
        if (refused !== null) {
            plain(response, refused);
            request.resume();
            return;
        }
        if ((request.url ?? '').split('?')[0] !== path) {
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
        close: () =>
            new Promise<void>((resolve) => {
                server.close(() => resolve());
                server.closeAllConnections();
            }),
    };
}
