// A stand-in for `codex app-server` over standard input and output: one JSON-RPC message per line each way, the
// "jsonrpc" header left out as the vendor's page says. A request other than `initialize` sent before the `initialized`
// notification is answered with an error, as the page says the server does. STUB_APP_SERVER says what it does:
//   { "pages": [[entry, ...], [entry, ...]] }  answers model/list page by page, each naming the next by a cursor
//   { "hang": true }                           answers nothing, reads nothing, ignores the end of its input, and
//                                              outlives the first signal elsewhere than Windows
//   { "long": n }                              answers initialize with one line of n characters
//   "pidFile" beside any of them               writes the stand-in's process id to that file first
//   { "refuse": true }                         answers initialize with an error
//   { "garbage": true }                        answers model/list with a result that holds no list
//   { "leave": true }                          exits once it has answered initialize
// Every line it reads is appended to STUB_APP_SERVER_LOG, and when its standard input ends it says so there.
const { appendFileSync, writeFileSync } = require('node:fs');
const { createInterface } = require('node:readline');

module.exports = function appServer() {
    const plan = JSON.parse(process.env.STUB_APP_SERVER || '{}');
    const log = (line) => process.env.STUB_APP_SERVER_LOG && appendFileSync(process.env.STUB_APP_SERVER_LOG, `${line}\n`);
    const print = (value) => process.stdout.write(`${JSON.stringify(value)}\n`);
    let initialized = false;
    setInterval(() => undefined, 60_000);
    if (plan.pidFile) writeFileSync(plan.pidFile, String(process.pid));
    if (plan.hang) {
        process.on('SIGTERM', () => undefined);
        return;
    }
    const lines = createInterface({ input: process.stdin, crlfDelay: Infinity });
    lines.on('close', () => {
        log('<stdin closed>');
        process.exit(0);
    });
    lines.on('line', (line) => {
        log(line);
        const message = JSON.parse(line);
        if (message.method === 'initialized') {
            initialized = true;
            return;
        }
        if (message.method === 'initialize') {
            if (plan.refuse) return print({ id: message.id, error: { code: -32600, message: 'refused' } });
            if (plan.long) return process.stdout.write(`${'x'.repeat(plan.long)}\n`);
            print({ method: 'configWarning', params: { summary: 'a notification before the answer' } });
            print({ id: message.id, result: { userAgent: 'codex_stub/0.0.0', platformFamily: 'stub', platformOs: 'stub' } });
            if (plan.leave) process.exit(0);
            return;
        }
        if (!initialized) return print({ id: message.id, error: { code: -32002, message: 'Not initialized' } });
        if (message.method === 'model/list') {
            if (plan.garbage) return print({ id: message.id, result: { items: 'no list' } });
            const pages = plan.pages || [[]];
            const at = message.params && typeof message.params.cursor === 'string' ? Number(message.params.cursor.slice(5)) : 0;
            const next = at + 1 < pages.length ? `page-${at + 1}` : null;
            return print({ id: message.id, result: { data: pages[at] || [], nextCursor: next } });
        }
        print({ id: message.id, error: { code: -32601, message: 'unknown method' } });
    });
};
