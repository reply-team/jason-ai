// A stand-in for a program on the PATH: the Reply CLI, npm or an agent host, by the name it was started as.
const { mkdirSync, writeFileSync } = require('node:fs');
const { basename, dirname, join } = require('node:path');
const record = require('./record.cjs');

const script = process.argv[1];
const name = /^index\.c?js$/.test(basename(script)) ? basename(dirname(script)).replace(/-stub$/, '') : basename(script);
const args = process.argv.slice(2);
// `codex app-server` reads its standard input line by line for as long as it runs, so it is not read whole first.
const listing = name === 'codex' && args.join(' ') === 'app-server';
const stdin = listing ? '' : record.readStdin();
// A run that asks a host to name a set-up chat, told apart by the flag that keeps nothing of it, is recorded by a name of
// its own, so what a test reads of the chat's turns is the turns alone.
const naming = (name === 'claude' && args.includes('--no-session-persistence')) || (name === 'codex' && args.includes('--ephemeral'));
record(naming ? `${name}-naming` : name, args, stdin);
const said = args.join(' ');

if (process.env.STUB_HANG === name) {
    // A program that never answers, whatever it was asked, and ends at the first signal: what a runner's time limit is
    // for. Its id is written to the file STUB_HANG_PID names, so a test can see it has gone.
    if (process.env.STUB_HANG_PID) writeFileSync(process.env.STUB_HANG_PID, String(process.pid));
    setInterval(() => undefined, 60000);
} else if (listing) {
    require('./app-server.cjs')();
} else if (said === '--version' && name === 'claude' && process.env.STUB_CLAUDE_VERSION_STUCK) {
    // A Claude Code that never answers and outlives the first signal. Elsewhere it ignores SIGTERM. On Windows, where a
    // signal ends a process outright, it leaves a detached process of its own holding its output open, which outlives it
    // unless the whole tree is ended. The ids of both are written to the file named, as a JSON array.
    process.on('SIGTERM', () => undefined);
    const ids = [process.pid];
    if (process.platform === 'win32') {
        const { spawn } = require('node:child_process');
        const kept = spawn(process.execPath, ['-e', 'setInterval(() => undefined, 60000)'], { stdio: ['ignore', 'inherit', 'inherit'], windowsHide: true, detached: true });
        ids.push(kept.pid);
    }
    writeFileSync(process.env.STUB_CLAUDE_VERSION_STUCK, JSON.stringify(ids));
    setInterval(() => undefined, 60000);
} else if (said === '--version' && name === 'claude' && process.env.STUB_CLAUDE_VERSION_FILE) {
    // What Claude Code says of its version, read from the file at each start so a test can change it between two
    // listings: { "stdout": "...", "stderr": "...", "exit": 0 }.
    const answer = JSON.parse(require('node:fs').readFileSync(process.env.STUB_CLAUDE_VERSION_FILE, 'utf8'));
    process.stdout.write(answer.stdout ?? '');
    process.stderr.write(answer.stderr ?? '');
    process.exitCode = answer.exit ?? 0;
} else if (said === '--version') {
    process.stdout.write(name === 'reply' ? `reply-cli ${process.env.STUB_REPLY_VERSION || '0.19.0'}\n` :'10.9.2\n');
} else if (name === 'reply' && said === 'auth status --json') {
    // The shape reply-cli prints (src/auth/status.ts), and its exit code: 1 when no credential is there.
    const signedIn = process.env.STUB_REPLY_SIGNED_IN === 'yes';
    const profile = process.env.STUB_REPLY_PROFILE || 'default';
    process.stdout.write(JSON.stringify(signedIn ? { authenticated: true, profile, source: 'store', method: 'api_key', user: { id: 7, username: 'someone@example.com', team_id: 3 } } : { authenticated: false, profile }));
    if (!signedIn) process.exitCode = 1;
} else if (name === 'reply' && said === 'auth login') {
    process.exit(0);
} else if (name === 'reply' && said === 'auth login --with-token') {
    process.exit(stdin.trim() === '' ? 1 : 0);
} else if (name === 'reply' && said === 'account list --json') {
    process.stdout.write(JSON.stringify({ items: [] }));
} else if (name === 'reply' && said === 'skills install adapter --project --agent codex') {
    for (const skill of ['reply-cli', 'reply-api', 'campaign-launch']) {
        mkdirSync(join(process.cwd(), '.agents', 'skills', skill), { recursive: true });
        writeFileSync(join(process.cwd(), '.agents', 'skills', skill, 'SKILL.md'), `# ${skill}\n`);
    }
} else if (naming) {
    require('./host.cjs').naming(name, args, stdin);
} else if (name === 'claude' || name === 'codex') {
    require('./host.cjs')(name, args, stdin);
} else if (name === 'npm' && args[0] === 'install' && args[1] === '-g') {
    process.exit(process.env.STUB_NPM_FAILS === 'yes' ? 1 : 0);
} else {
    process.stderr.write(`the stub ${name} does not answer ${said}\n`);
    process.exit(2);
}
