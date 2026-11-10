// The client half of a permission prompt tool reached over MCP's Streamable HTTP transport, as the stub hosts use it:
// read the server from the turn's --mcp-config file, initialize, say initialized, list the tools, call the one tool,
// and check its answer the way Claude Code's documentation says it reads one (one text block, no structured content).
const { readFileSync } = require('node:fs');

const version = '2025-11-25';

/** The messages of an event stream body, in order. */
function events(body) {
    return body
        .split(/\r?\n\r?\n/)
        .map((block) => block.split(/\r?\n/).filter((line) => line.startsWith('data:')).map((line) => line.slice(5).trimStart()).join('\n'))
        .filter((data) => data !== '')
        .map((data) => JSON.parse(data));
}

async function post(server, message, protocol) {
    const headers = { ...server.headers, accept: 'application/json, text/event-stream', 'content-type': 'application/json' };
    if (protocol) headers['mcp-protocol-version'] = protocol;
    const response = await fetch(server.url, { method: 'POST', headers, body: JSON.stringify(message) });
    if (message.id === undefined) {
        if (response.status !== 202) throw new Error(`The server answered a notification with ${response.status}.`);
        return null;
    }
    if (response.status !== 200) throw new Error(`The server answered ${message.method} with ${response.status}.`);
    const type = response.headers.get('content-type') || '';
    const text = await response.text();
    const messages = type.startsWith('text/event-stream') ? events(text) : [JSON.parse(text)];
    const reply = messages.find((each) => each.id === message.id);
    if (!reply) throw new Error(`No answer to ${message.method}.`);
    if (reply.error) throw new Error(`${message.method} failed: ${reply.error.message}`);
    return reply.result;
}

/** Asks the permission prompt tool whether a tool may run; resolves to the decision object the tool's text carries. */
async function decide(configFile, serverName, toolName, tool, input) {
    const config = JSON.parse(readFileSync(configFile, 'utf8'));
    const server = config.mcpServers[serverName];
    if (!server || (server.type !== 'http' && server.type !== 'streamable-http')) throw new Error('The configuration names no HTTP server by that name.');
    const init = await post(server, { jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: version, capabilities: {}, clientInfo: { name: 'stub-host', version: '1' } } });
    const protocol = init.protocolVersion;
    await post(server, { jsonrpc: '2.0', method: 'notifications/initialized' }, protocol);
    const listed = await post(server, { jsonrpc: '2.0', id: 2, method: 'tools/list' }, protocol);
    if (!listed.tools.some((each) => each.name === toolName)) throw new Error(`The server offers no ${toolName}.`);
    const result = await post(server, { jsonrpc: '2.0', id: 3, method: 'tools/call', params: { name: toolName, arguments: { tool_name: tool, input, tool_use_id: 'toolu_stub' } } }, protocol);
    const content = result.content;
    if (result.structuredContent !== undefined || !Array.isArray(content) || content.length !== 1 || content[0].type !== 'text' || typeof content[0].text !== 'string') {
        throw new Error('Permission prompt tool returned an invalid result. Expected a single text block param with type="text" and a string text value.');
    }
    const decision = JSON.parse(content[0].text);
    if (decision.behavior !== 'allow' && !(decision.behavior === 'deny' && typeof decision.message === 'string')) throw new Error('The decision is neither an allow nor a deny with a message.');
    return decision;
}

module.exports = { decide };
