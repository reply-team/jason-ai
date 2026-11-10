// A stand-in for a program on the PATH: the Reply CLI, npm or an agent host, by the name it was started as.
const { mkdirSync, writeFileSync } = require('node:fs');
const { basename, dirname, join } = require('node:path');
const record = require('./record.cjs');

const script = process.argv[1];
const name = /^index\.c?js$/.test(basename(script)) ? basename(dirname(script)).replace(/-stub$/, '') : basename(script);
const args = process.argv.slice(2);
const stdin = record.readStdin();
record(name, args, stdin);
const said = args.join(' ');

if (said === '--version') {
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
} else if (name === 'claude' || name === 'codex') {
    require('./host.cjs')(name, args, stdin);
} else if (name === 'npm' && args[0] === 'install' && args[1] === '-g') {
    process.exit(process.env.STUB_NPM_FAILS === 'yes' ? 1 : 0);
} else {
    process.stderr.write(`the stub ${name} does not answer ${said}\n`);
    process.exit(2);
}
