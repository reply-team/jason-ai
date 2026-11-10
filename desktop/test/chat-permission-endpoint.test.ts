import assert from 'node:assert/strict';
import { request } from 'node:http';
import { afterEach, test } from 'node:test';
import type { ToolAnswer } from '../src/chat-permission-broker.ts';
import { openEndpoint, protocolVersions, type Endpoint } from '../src/chat-permission-endpoint.ts';

const secret = 'per-turn-secret-for-the-test';
const opened: Endpoint[] = [];
afterEach(async () => {
    for (const endpoint of opened.splice(0)) await endpoint.close();
});

interface Reply {
    status: number;
    type: string;
    body: string;
}

/** One HTTP request to the endpoint, headers as given; the Host header is the endpoint's own unless a row sets it. */
function send(endpoint: Endpoint, options: { method?: string; path?: string; headers?: Record<string, string>; body?: unknown; raw?: string }): Promise<Reply> {
    const body = options.raw ?? (options.body === undefined ? '' : JSON.stringify(options.body));
    const headers: Record<string, string> = {
        host: `127.0.0.1:${endpoint.port}`,
        authorization: `Bearer ${secret}`,
        accept: 'application/json, text/event-stream',
        'content-type': 'application/json',
        ...options.headers,
    };
    for (const [name, value] of Object.entries(headers)) if (value === '') delete headers[name];
    return new Promise((resolve, reject) => {
        const asked = request({ host: '127.0.0.1', port: endpoint.port, method: options.method ?? 'POST', path: options.path ?? '/mcp', headers, setHost: false }, (response) => {
            let text = '';
            response.setEncoding('utf8');
            response.on('data', (chunk: string) => {
                text += chunk;
            });
            response.on('end', () => resolve({ status: response.statusCode ?? 0, type: String(response.headers['content-type'] ?? ''), body: text }));
            response.on('error', reject);
            response.on('close', () => {
                if (!response.complete) reject(new Error('The stream was cut.'));
            });
        });
        asked.on('error', reject);
        asked.end(body);
    });
}

/** The JSON-RPC messages an SSE body carries, in order; comments and blank lines are not messages. */
function events(body: string): Record<string, unknown>[] {
    return body
        .split('\n\n')
        .map((block) => block.split('\n').filter((line) => line.startsWith('data:')).map((line) => line.slice(5).trimStart()).join('\n'))
        .filter((data) => data !== '')
        .map((data) => JSON.parse(data) as Record<string, unknown>);
}

type Decide = (tool: string, input: Record<string, unknown>, signal: AbortSignal) => Promise<ToolAnswer>;

async function open(decide: Decide = async () => ({ behavior: 'deny', message: 'no' }), keepAliveMs?: number) {
    const asked: { tool: string; input: Record<string, unknown> }[] = [];
    const endpoint = await openEndpoint({
        secret,
        decide: (tool, input, signal) => {
            asked.push({ tool, input });
            return decide(tool, input, signal);
        },
        ...(keepAliveMs === undefined ? {} : { keepAliveMs }),
    });
    opened.push(endpoint);
    return { endpoint, asked };
}

const call = (input: Record<string, unknown>, extra: Record<string, unknown> = {}) => ({
    jsonrpc: '2.0',
    id: 7,
    method: 'tools/call',
    params: { name: 'decide', arguments: { tool_name: 'Bash', input, tool_use_id: 'toolu_1' }, ...extra },
});

test('the endpoint listens on the loopback interface only, at /mcp, and its url says so', async () => {
    const { endpoint } = await open();
    assert.equal(endpoint.url, `http://127.0.0.1:${endpoint.port}/mcp`);
    assert.ok(endpoint.port > 0);
    assert.equal((await send(endpoint, { path: '/other', body: { jsonrpc: '2.0', id: 1, method: 'tools/list' } })).status, 404);
});

test('initialize answers the version the client asked for when it is one the endpoint speaks, and offers tools', async () => {
    const { endpoint } = await open();
    for (const version of protocolVersions) {
        const reply = await send(endpoint, { body: { jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: version, capabilities: {}, clientInfo: { name: 'test', version: '1' } } } });
        assert.equal(reply.status, 200);
        assert.match(reply.type, /^application\/json/);
        assert.deepEqual(JSON.parse(reply.body), { jsonrpc: '2.0', id: 1, result: { protocolVersion: version, capabilities: { tools: {} }, serverInfo: { name: 'jason-desktop', version: '1' } } });
    }
});

test('initialize answers the newest version it speaks when the client asks for another', async () => {
    const { endpoint } = await open();
    const reply = await send(endpoint, { body: { jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2099-01-01' } } });
    assert.equal((JSON.parse(reply.body) as { result: { protocolVersion: string } }).result.protocolVersion, protocolVersions[0]);
});

test('the initialized notification is accepted with 202 and no body', async () => {
    const { endpoint } = await open();
    const reply = await send(endpoint, { body: { jsonrpc: '2.0', method: 'notifications/initialized' } });
    assert.deepEqual([reply.status, reply.body], [202, '']);
});

test('ping is answered with an empty result', async () => {
    const { endpoint } = await open();
    const reply = await send(endpoint, { body: { jsonrpc: '2.0', id: 9, method: 'ping' } });
    assert.deepEqual(JSON.parse(reply.body), { jsonrpc: '2.0', id: 9, result: {} });
});

test('a cancellation is accepted with 202 and withdraws the call it names, whose stream ends with no answer', async () => {
    let withdrawn: AbortSignal | undefined;
    const { endpoint } = await open((_tool, _input, signal) => {
        withdrawn = signal;
        return new Promise((resolve) => signal.addEventListener('abort', () => resolve({ behavior: 'deny', message: 'withdrawn' })));
    });
    const replied = send(endpoint, { body: call({ command: 'git push' }) });
    await new Promise((resolve) => setTimeout(resolve, 50));
    const other = await send(endpoint, { body: { jsonrpc: '2.0', method: 'notifications/cancelled', params: { requestId: 8 } } });
    assert.equal(other.status, 202);
    assert.equal(withdrawn?.aborted, false, 'a cancellation of another request changes nothing');
    const cancelled = await send(endpoint, { body: { jsonrpc: '2.0', method: 'notifications/cancelled', params: { requestId: 7, reason: 'The user interrupted.' } } });
    assert.deepEqual([cancelled.status, cancelled.body], [202, '']);
    assert.equal(withdrawn?.aborted, true);
    const reply = await replied;
    assert.deepEqual(events(reply.body).filter((each) => 'id' in each), []);
});

test('tools/list offers one tool, decide, with the input Claude Code sends a permission prompt tool', async () => {
    const { endpoint } = await open();
    const reply = await send(endpoint, { body: { jsonrpc: '2.0', id: 2, method: 'tools/list' } });
    const tools = (JSON.parse(reply.body) as { result: { tools: { name: string; inputSchema: { required: string[]; properties: Record<string, unknown> } }[] } }).result.tools;
    assert.deepEqual(tools.map((tool) => tool.name), ['decide']);
    assert.deepEqual(Object.keys(tools[0]?.inputSchema.properties ?? {}).sort(), ['input', 'tool_name', 'tool_use_id']);
    assert.deepEqual(tools[0]?.inputSchema.required, ['tool_name', 'input']);
    assert.equal('outputSchema' in (tools[0] ?? {}), false);
});

test('a call is answered on an event stream with one text block whose JSON allows with the input, and no structured content', async () => {
    const { endpoint, asked } = await open(async (_tool, input) => ({ behavior: 'allow', updatedInput: input }));
    const reply = await send(endpoint, { body: call({ command: 'git status' }) });
    assert.equal(reply.status, 200);
    assert.match(reply.type, /^text\/event-stream/);
    assert.deepEqual(asked, [{ tool: 'Bash', input: { command: 'git status' } }]);
    assert.deepEqual(events(reply.body), [{ jsonrpc: '2.0', id: 7, result: { content: [{ type: 'text', text: '{"behavior":"allow","updatedInput":{"command":"git status"}}' }] } }]);
});

test('a refusal is one text block whose JSON denies with the message', async () => {
    const { endpoint } = await open(async () => ({ behavior: 'deny', message: 'The person said no.' }));
    const reply = await send(endpoint, { body: call({ command: 'git push' }) });
    assert.deepEqual(events(reply.body), [{ jsonrpc: '2.0', id: 7, result: { content: [{ type: 'text', text: '{"behavior":"deny","message":"The person said no."}' }] } }]);
});

test('while a person decides, the stream stays alive and reports progress when the client asked for it', async () => {
    let answer: (value: ToolAnswer) => void = () => undefined;
    const { endpoint } = await open(() => new Promise((resolve) => (answer = resolve)), 20);
    const replied = send(endpoint, { body: call({ command: 'git push' }, { _meta: { progressToken: 'tok' } }) });
    await new Promise((resolve) => setTimeout(resolve, 120));
    answer({ behavior: 'deny', message: 'no' });
    const reply = await replied;
    assert.match(reply.body, /^: /m);
    const said = events(reply.body);
    const progress = said.filter((each) => each['method'] === 'notifications/progress');
    assert.ok(progress.length >= 2, reply.body);
    assert.deepEqual(progress[0], { jsonrpc: '2.0', method: 'notifications/progress', params: { progressToken: 'tok', progress: 1 } });
    assert.deepEqual(said.at(-1)?.['id'], 7);
});

test('a request without the turn\'s secret is refused before anything is asked', async () => {
    const { endpoint, asked } = await open();
    for (const authorization of ['', 'Bearer wrong', `Bearer ${secret}x`, secret, `Basic ${secret}`]) {
        const reply = await send(endpoint, { headers: { authorization }, body: call({ command: 'git status' }) });
        assert.equal(reply.status, 401, authorization);
    }
    assert.deepEqual(asked, []);
});

test('a request a browser could send is refused: any Origin, or a Host that is not the endpoint\'s own', async () => {
    const { endpoint, asked } = await open();
    assert.equal((await send(endpoint, { headers: { origin: 'http://127.0.0.1' }, body: call({}) })).status, 403);
    assert.equal((await send(endpoint, { headers: { origin: 'null' }, body: call({}) })).status, 403);
    assert.equal((await send(endpoint, { headers: { host: `evil.example:${endpoint.port}` }, body: call({}) })).status, 403);
    assert.equal((await send(endpoint, { headers: { host: '127.0.0.1:1' }, body: call({}) })).status, 403);
    assert.deepEqual(asked, []);
});

test('GET and DELETE are not offered', async () => {
    const { endpoint } = await open();
    assert.equal((await send(endpoint, { method: 'GET', headers: { accept: 'text/event-stream' } })).status, 405);
    assert.equal((await send(endpoint, { method: 'DELETE' })).status, 405);
});

test('a version header the endpoint does not speak is refused', async () => {
    const { endpoint } = await open();
    assert.equal((await send(endpoint, { headers: { 'mcp-protocol-version': '1999-01-01' }, body: { jsonrpc: '2.0', id: 2, method: 'tools/list' } })).status, 400);
    assert.equal((await send(endpoint, { headers: { 'mcp-protocol-version': protocolVersions[0] ?? '' }, body: { jsonrpc: '2.0', id: 2, method: 'tools/list' } })).status, 200);
});

test('a body that is not one JSON-RPC message is refused', async () => {
    const { endpoint } = await open();
    assert.equal((await send(endpoint, { raw: '{not json' })).status, 400);
    assert.equal((await send(endpoint, { body: [{ jsonrpc: '2.0', id: 1, method: 'tools/list' }] })).status, 400);
    assert.equal((await send(endpoint, { body: { jsonrpc: '2.0', id: 1 } })).status, 400);
    assert.equal((await send(endpoint, { raw: 'x'.repeat(9 * 1024 * 1024) })).status, 413);
});

test('anything but the four messages is refused: another method, another notification, another tool, a call without its arguments', async () => {
    const { endpoint, asked } = await open();
    const ping = JSON.parse((await send(endpoint, { body: { jsonrpc: '2.0', id: 3, method: 'resources/list' } })).body) as { error: { code: number } };
    assert.equal(ping.error.code, -32601);
    assert.equal((await send(endpoint, { body: { jsonrpc: '2.0', method: 'notifications/roots/list_changed' } })).status, 400);
    const other = JSON.parse((await send(endpoint, { body: { jsonrpc: '2.0', id: 4, method: 'tools/call', params: { name: 'run', arguments: { tool_name: 'Bash', input: {} } } } })).body) as { error: { code: number } };
    assert.equal(other.error.code, -32602);
    const bare = JSON.parse((await send(endpoint, { body: { jsonrpc: '2.0', id: 5, method: 'tools/call', params: { name: 'decide', arguments: { tool_name: 'Bash' } } } })).body) as { error: { code: number } };
    assert.equal(bare.error.code, -32602);
    assert.deepEqual(asked, []);
});

test('closed, the endpoint takes no more connections', async () => {
    const { endpoint } = await open();
    await endpoint.close();
    await assert.rejects(send(endpoint, { body: { jsonrpc: '2.0', id: 2, method: 'tools/list' } }));
});

test('closing ends a stream still open', async () => {
    const { endpoint } = await open(() => new Promise(() => undefined));
    const replied = send(endpoint, { body: call({ command: 'git push' }) });
    await new Promise((resolve) => setTimeout(resolve, 50));
    const late = new Promise((_, reject) => setTimeout(() => reject(new Error('the endpoint did not close')), 1000).unref());
    await Promise.race([endpoint.close(), late]);
    await assert.rejects(replied, /cut|socket hang up|ECONNRESET|aborted/);
});
