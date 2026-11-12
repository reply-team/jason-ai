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
// The Windows check of Codex's sandbox is recorded by a name of its own too, so a test's turns are the turns alone.
const sandboxCheck = name === 'codex' && args[0] === 'sandbox';
record(naming ? `${name}-naming` : sandboxCheck ? 'codex-sandbox' : name, args, stdin);
const said = args.join(' ');

if (process.env.STUB_HANG === name) {
    // A program that never answers, whatever it was asked, and ends at the first signal: what a runner's time limit is
    // for. Its id is written to the file STUB_HANG_PID names, so a test can see it has gone.
    if (process.env.STUB_HANG_PID) writeFileSync(process.env.STUB_HANG_PID, String(process.pid));
    setInterval(() => undefined, 60000);
} else if (sandboxCheck) {
    // `codex sandbox` as the version STUB_CODEX_VERSION names would parse it, never any argv: 0.167.0-alpha.3.2 (and an
    // unnamed version) takes no platform word, as its recorded help says (test/fixtures); 0.122.0 is the older shape the
    // reference names, `codex sandbox windows`, which no help of ours records yet. Any other version, a word where the
    // grammar has none, an option but `-c <value>`, or a command but cmd by its full path reading its line from
    // JASON_SANDBOX_CHECK ends as a usage error that prints no marker. What the command would print in a sandbox is set
    // by STUB_CODEX_SANDBOX: `holds` (the default) the ran and control markers, `refuses-type` the ran marker alone,
    // `reads` every marker, `reads-second` those two and the second data file's, `silent` none, `hangs` nothing ever. The
    // command itself is never run.
    const version = process.env.STUB_CODEX_VERSION || '0.167.0-alpha.3.2';
    const grammar = { '0.167.0-alpha.3.2': 'bare', '0.122.0': 'worded' }[version];
    const usage = (why) => {
        process.stderr.write(`error: ${why}\n\nUsage: codex sandbox [OPTIONS] [COMMAND]...\n`);
        process.exit(2);
    };
    if (grammar === undefined) usage(`the stand-in knows no grammar of codex ${version}`);
    let at = 1;
    if (grammar === 'worded') {
        if (args[at] !== 'windows') usage("'codex sandbox' requires a subcommand");
        at += 1;
    } else if (args[at] === 'windows') {
        // A word this grammar does not have is the command to run: no such program.
        process.stderr.write('Error: failed to start windows: program not found\n');
        process.exit(1);
    }
    while (args[at] === '-c' && args[at + 1] !== undefined) at += 2;
    if (args[at] !== '--') usage(`unexpected argument '${args[at]}' found`);
    const command = args.slice(at + 1);
    const line = process.env.JASON_SANDBOX_CHECK ?? '';
    if (command.length !== 4 || !/^[A-Za-z]:\\.*\\System32\\cmd\.exe$/.test(command[0]) || command.slice(1).join(' ') !== '/d /c %JASON_SANDBOX_CHECK%' || line === '') {
        process.stderr.write(`'${command.join(' ')}' is not recognized as an internal or external command\n`);
        process.exit(1);
    }
    // The line: the ran marker, then the control file's part (the system's hosts file), then each data file's.
    const ran = /^echo (\S+)/.exec(line)?.[1];
    const parts = [...line.matchAll(/type "([^"]+)" >nul 2>nul && echo (\S+)/g)].map((match) => ({ file: match[1], marker: match[2] }));
    const control = parts.find((part) => /\\drivers\\etc\\hosts$/.test(part.file))?.marker;
    const reads = parts.filter((part) => part.marker !== control).map((part) => part.marker);
    // `holds` the ran and control markers, `refuses-type` the ran marker alone (a sandbox that refuses `type` or nul).
    const shape = process.env.STUB_CODEX_SANDBOX || 'holds';
    if (shape === 'hangs') {
        if (process.env.STUB_HANG_PID) writeFileSync(process.env.STUB_HANG_PID, String(process.pid));
        setInterval(() => undefined, 60000);
    }
    else if (shape === 'holds') process.stdout.write(`${ran} \r\n${control} \r\n`);
    else if (shape === 'refuses-type') process.stdout.write(`${ran} \r\n`);
    else if (shape === 'reads') process.stdout.write([`${ran} `, `${control} `, ...reads].join('\r\n') + '\r\n');
    else if (shape === 'reads-second') process.stdout.write(`${ran} \r\n${control} \r\n${reads[1]}\r\n`);
    if (shape !== 'hangs') process.exitCode = 1;
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
} else if (said === '--version' && name === 'codex' && process.env.STUB_CODEX_VERSION) {
    // What Codex says of its version, as `codex-cli <version>`.
    process.stdout.write(`codex-cli ${process.env.STUB_CODEX_VERSION}\n`);
} else if (said === '--version') {
    process.stdout.write(name === 'reply' ? `reply-cli ${process.env.STUB_REPLY_VERSION || '0.21.0'}\n` :'10.9.2\n');
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
} else if (name === 'reply' && (said === 'account list --channel email --json' || said === 'account list --channel linkedin --json')) {
    // Per channel, as reply-cli prints it: the items of that channel, or a refusal when the plan or role has none.
    const channel = args[3];
    const raw = process.env[`STUB_REPLY_ACCOUNTS_${channel.toUpperCase()}`];
    // The endings reply-cli 0.20.0 has: JSON error line on stderr and exit 1 (--json), here the plan/role refusal, a
    // network failure and an expired sign-in; an exit nobody explained; and an answer that is not a list.
    const failing = { refused: 'access.feature_unavailable', network: 'reply.unavailable', expired: 'access.denied' };
    if (Object.hasOwn(failing, raw ?? '')) {
        process.stderr.write(JSON.stringify({ error: { code: failing[raw], title: 'Some words of the CLI that are never shown.' } }) + String.fromCharCode(10));
        process.exitCode = 1;
    } else if (raw === 'unexplained') {
        process.stderr.write('something nobody explained' + String.fromCharCode(10));
        process.exitCode = 7;
    } else if (raw === 'garbage') {
        process.stdout.write('not json');
    } else {
        process.stdout.write(JSON.stringify({ items: raw ? JSON.parse(raw) : [] }));
    }
} else if (name === 'reply' && args[0] === 'account' && args[1] !== 'list') {
    // The account verbs as the Reply CLI ends them under --json: the result on stdout and exit 0, a refusal as one line
    // {"error":{...}} on stderr and exit 1 (2 for a usage.* code), exit 3 for a write whose outcome it does not know. The
    // answer is named per verb by STUB_REPLY_ACCOUNT_<VERB>, with _SHARED where --allow-shared-page was passed and _FORCE
    // where --force was: {"print": <result>}, {"code": <code>, "exit"?}, or {"exit", "stdout"?, "stderr"?} as it is.
    // `pending` lists nothing when none is named.
    const verb = (args[1] ?? '').toUpperCase();
    const key = `STUB_REPLY_ACCOUNT_${verb}${args.includes('--allow-shared-page') ? '_SHARED' : ''}${args.includes('--force') ? '_FORCE' : ''}`;
    const named = process.env[key];
    const answer = named ? JSON.parse(named) : verb === 'PENDING' ? { print: { items: [], supported: true, unsupported_channels: [] } } : null;
    if (answer === null) {
        process.stderr.write(`the stub reply does not answer ${said}\n`);
        process.exitCode = 2;
    } else if (answer.print !== undefined) {
        process.stdout.write(JSON.stringify(answer.print) + String.fromCharCode(10));
    } else if (answer.code !== undefined) {
        process.stderr.write(JSON.stringify({ error: { code: answer.code, title: 'Words of the CLI that the desktop never shows.', ...(answer.error ?? {}) } }) + String.fromCharCode(10));
        process.exitCode = answer.exit ?? (answer.code.startsWith('usage.') ? 2 : 1);
    } else {
        process.stdout.write(answer.stdout ?? '');
        process.stderr.write(answer.stderr ?? '');
        process.exitCode = answer.exit ?? 0;
    }
} else if (name === 'reply' && said === 'skills install adapter --project --agent codex') {
    // STUB_REPLY_SKILLS_FAIL=yes makes the install fail before it writes anything.
    if (process.env.STUB_REPLY_SKILLS_FAIL === 'yes') process.exit(1);
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
