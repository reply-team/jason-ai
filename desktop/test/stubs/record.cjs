// What a stub was asked: appended as one JSON line to the file STUB_RECORD names, outside the data directory.
const { appendFileSync, readFileSync } = require('node:fs');

function readStdin() {
    try {
        return readFileSync(0, 'utf8');
    } catch {
        return '';
    }
}

module.exports = function record(name, args, stdin) {
    if (!process.env.STUB_RECORD) return;
    const env = Object.fromEntries(Object.entries(process.env).filter(([key]) => !key.startsWith('STUB_')));
    appendFileSync(process.env.STUB_RECORD, `${JSON.stringify({ name, args, stdin, cwd: process.cwd(), env })}\n`);
};
module.exports.readStdin = readStdin;
