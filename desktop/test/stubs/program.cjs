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

// The profile a run acts as, `--profile`, a global option of the Reply CLI. reply-cli 0.23.2 takes it anywhere
// (src/index.ts); the desktop puts it first and names nothing else of the account, and the stand-in reads it only there,
// so a run that names it anywhere else, or names a team, is a form it does not answer.
let replyProfile = null;
let rest = args;
if (name === 'reply' && rest[0] === '--profile' && rest[1] !== undefined) {
    replyProfile = rest[1];
    rest = rest.slice(2);
}
const said = rest.join(' ');

// The Reply CLI's profiles and sign-ins, as a world the file STUB_REPLY_WORLD holds, read and written as reply-cli 0.23.2
// keeps its own (src/profile.ts, src/commands/auth.ts):
//   {"current": "default", "profiles": {"outbound": {"team_id"?: 9}}, "credentials": {"default": "ada@example.test"},
//    "seats": {"ada@example.test": [3, 9]}, "lists": {"ada@example.test#3": {"email": [...], "linkedin": [...]}},
//    "browser": "ada@example.test", "keys": {"made-up-key": {"login": "bea@example.test", "team": 9}},
//    "teams": {"9": "Made-up Nine"}, "whoami": {"9": {"orgId": 12, "orgName": "Made-up Org"}},
//    "ids": {"ada@example.test": 41}, "renamed": {"ada@example.test": "ada.new@example.test"}}   (ada's id in team 9: 4109)
// `default` is always a profile; a sign-in is kept under its profile's name, apart from the profiles, so `profile add`
// replaces a profile's settings and never its sign-in. A browser's sign-in is its login, or {"login": …, "unnamed": true}
// where Reply would not name it: reply-cli asks Reply's /v3/whoami in the profile's pinned team right after the browser
// (src/commands/auth.ts `enrich_identity`), and keeps no `user` where Reply refuses it, as it does a login in several teams
// with none pinned, or one in another team than the pin. A key's sign-in is {"login": …, "key": <its team>}: Reply acts for
// a key in the key's own team whatever team is asked, so where a browser's sign-in is read, refused or named in the pinned
// team, a key's is in its own (whoami, the lists, and the key's sign-in itself, which never fails for the pin); `team list`
// and `team use` answer by the login's teams for both. Without the file the CLI holds `default` alone, or the profiles
// STUB_REPLY_PROFILES names as `profile list --json` prints them, and the sign-in and lists are the variables' below,
// whichever profile a run names.
const worldFile = name === 'reply' ? process.env.STUB_REPLY_WORLD : undefined;
const world = worldFile ? JSON.parse(require('node:fs').readFileSync(worldFile, 'utf8')) : null;
const keepWorld = () => writeFileSync(worldFile, JSON.stringify(world, null, 2));
const listedProfiles = world !== null
    ? { current: world.current || 'default', available: ['default', ...Object.keys(world.profiles ?? {}).filter((each) => each !== 'default')] }
    : process.env.STUB_REPLY_PROFILES
      ? JSON.parse(process.env.STUB_REPLY_PROFILES)
      : { current: 'default', available: ['default'] };
const loginOf = (profile) => {
    const credential = (world.credentials ?? {})[profile];
    return typeof credential === 'object' && credential !== null ? credential.login : credential;
};
// Reply's id of a login's user in a team: one id in each of its teams, as Reply gives a seat its own, made of the login's
// number (the world's `ids` where it names one, else one of the login's own) and the team; and the address Reply names it
// by now, the world's `renamed` where the person changed it at Reply since the Reply CLI stored it.
const userIdOf = (login, team) => ((world?.ids ?? {})[login] ?? [...login].reduce((sum, each) => (sum * 31 + each.codePointAt(0)) % 9_973, 7) + 1) * 100 + Number(team);
const nameOf = (login) => (world?.renamed ?? {})[login] ?? login;
const namedBy = (profile) => {
    const credential = (world.credentials ?? {})[profile];
    return typeof credential === 'string' || (typeof credential === 'object' && credential !== null && credential.key !== undefined);
};
// The team a key acts in, whatever is asked; undefined for a browser's sign-in.
const keyTeamOf = (profile) => {
    const credential = (world.credentials ?? {})[profile];
    return typeof credential === 'object' && credential !== null ? credential.key : undefined;
};
// The team a run acts in: a key's own; else the profile's pin, or the one seat a login holds; null for none.
const actingTeam = (profile, seats) => keyTeamOf(profile) ?? ((world.profiles ?? {})[profile] ?? {}).team_id ?? (seats.length === 1 ? seats[0] : null);
// Reply's 403 for a login in several teams with none pinned, or for a team it is not in, which reply-cli answers as
// access.denied carrying Reply's own code (src/operations/call.ts `refusal`).
const refusedAccess = (reply_code) => answer({ code: 'access.denied', error: { status: 403, title: 'Reply refused access.', reply_code } });
const declared = (profile) => profile === 'default' || listedProfiles.available.includes(profile);
const inUse = () => replyProfile ?? listedProfiles.current;
const usageProfile = (profile) => answer({ code: 'usage.profile', error: { hint: `Create it with \`profile add ${profile}\`.` } });

// A write of the Reply CLI's configuration (`team use`, `profile add`, `profile delete`), which reply-cli makes as a read
// of the whole file and a write of it whole: where STUB_REPLY_CONFIG_WRITES names a file, its start and its end are
// recorded there, a quarter of a second apart, so two writes that overlap show as two starts in a row.
function configWrite() {
    const file = process.env.STUB_REPLY_CONFIG_WRITES;
    if (!file) return;
    require('node:fs').appendFileSync(file, `start ${said}\n`);
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 250);
    require('node:fs').appendFileSync(file, `end ${said}\n`);
}

// An ending a test names as JSON: {"print": <result>} on stdout and exit 0; {"code": <code>, "exit"?} as the Reply CLI's
// error line on stderr and exit 1 (2 for a usage.* code); or {"exit", "stdout"?, "stderr"?} as it is.
function answer(given) {
    if (given.print !== undefined) {
        process.stdout.write(JSON.stringify(given.print) + String.fromCharCode(10));
    } else if (given.code !== undefined) {
        process.stderr.write(JSON.stringify({ error: { code: given.code, title: 'Words of the CLI that the desktop never shows.', ...(given.error ?? {}) } }) + String.fromCharCode(10));
        process.exitCode = given.exit ?? (given.code.startsWith('usage.') ? 2 : 1);
    } else {
        process.stdout.write(given.stdout ?? '');
        process.stderr.write(given.stderr ?? '');
        process.exitCode = given.exit ?? 0;
    }
}

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
} else if (said === '--version' && name === 'reply' && process.env.STUB_REPLY_VERSION_ENDS) {
    // A Reply CLI that does not say its version, as STUB_REPLY_VERSION_ENDS names the ending.
    answer(JSON.parse(process.env.STUB_REPLY_VERSION_ENDS));
} else if (said === '--version') {
    process.stdout.write(name === 'reply' ? `reply-cli ${process.env.STUB_REPLY_VERSION || '0.23.2'}\n` :'10.9.2\n');
} else if (name === 'reply' && said === 'profile list --json') {
    // `profile list` ignores --profile (src/commands/profile.ts): exactly {"current", "available"}, `default` first; a
    // configuration it cannot read ends as STUB_REPLY_PROFILE_LIST names it, as `config.read` or `config.corrupt`, exit 1.
    if (process.env.STUB_REPLY_PROFILE_LIST) answer(JSON.parse(process.env.STUB_REPLY_PROFILE_LIST));
    else answer({ print: listedProfiles });
} else if (name === 'reply' && rest[0] === 'profile' && rest[1] === 'add' && rest.length === 4 && rest[3] === '--json') {
    // `profile add <name>` refuses `default` and a blank name, and replaces a profile of that name whole, with no word,
    // leaving its sign-in, which is kept apart (src/profile.ts `add_profile`). STUB_REPLY_PROFILE_ADD names another ending.
    const added = rest[2];
    configWrite();
    if (process.env.STUB_REPLY_PROFILE_ADD) answer(JSON.parse(process.env.STUB_REPLY_PROFILE_ADD));
    else if (added.trim() === '' || added === 'default') answer({ code: 'usage.profile' });
    else {
        if (world !== null) {
            world.profiles = { ...(world.profiles ?? {}), [added]: {} };
            keepWorld();
        }
        answer({ print: { added } });
    }
} else if (name === 'reply' && rest[0] === 'profile' && rest[1] === 'delete' && rest.length === 5 && rest[3] === '--yes' && rest[4] === '--json') {
    // `profile delete <name> --yes` ignores --profile (src/commands/profile.ts `handle_delete`): `default` and a name it
    // does not hold are usage.profile, exit 2; else the profile goes, the current profile is reset to `default` where it
    // was that one, and its sign-in goes too. STUB_REPLY_PROFILE_DELETE names another ending.
    const deleted = rest[2];
    configWrite();
    if (process.env.STUB_REPLY_PROFILE_DELETE) answer(JSON.parse(process.env.STUB_REPLY_PROFILE_DELETE));
    else if (deleted === 'default') answer({ code: 'usage.profile', error: { title: 'The built-in default profile can\'t be deleted.' } });
    else if (!listedProfiles.available.includes(deleted)) answer({ code: 'usage.profile', error: { title: `Unknown profile '${deleted}'.`, hint: 'List profiles with `profile list`.' } });
    else if (world === null) answer({ print: { deleted, credential_removed: false, current_reset: false } });
    else {
        const { [deleted]: _gone, ...profiles } = world.profiles ?? {};
        const { [deleted]: credential, ...credentials } = world.credentials ?? {};
        const reset = listedProfiles.current === deleted;
        Object.assign(world, { profiles, credentials, ...(reset ? { current: 'default' } : {}) });
        keepWorld();
        answer({ print: { deleted, credential_removed: credential !== undefined, current_reset: reset } });
    }
} else if (name === 'reply' && rest[0] === 'profile' && rest[1] === 'show' && ((rest.length === 4 && rest[3] === '--json') || (rest.length === 5 && rest[2] === '--json' && rest[3] === '--'))) {
    // `profile show <name>` ignores --profile and asks Reply nothing (src/commands/profile.ts `handle_show`): the profile's
    // backend, each address its own or the built-in one it inherits (src/profile.ts `EMBEDDED`, `describe_profile`), its
    // pin, and its stored sign-in without a secret; an address without its trailing slashes (`strip_trailing_slash`). A
    // name it does not hold is usage.profile, exit 2. The name comes after `--`, which commander takes as the end of the
    // options; given before it, a name that is one of the CLI's flags is read as that flag, and the current profile shown.
    // STUB_REPLY_PROFILE_SHOW names another ending.
    const named = rest.length === 5 ? rest[4] : rest[2];
    const shown = rest.length === 4 && ['--json', '--pretty', '--verbose', '-q', '--quiet'].includes(named) ? listedProfiles.current : named;
    const own = world !== null ? ((world.profiles ?? {})[shown] ?? {}) : {};
    const builtIn = { authority: 'https://oauth.reply.io', api_base: 'https://api.reply.io' };
    if (process.env.STUB_REPLY_PROFILE_SHOW) answer(JSON.parse(process.env.STUB_REPLY_PROFILE_SHOW));
    else if (!declared(shown)) usageProfile(shown);
    else {
        const backend = {
            authority: own.authority ?? builtIn.authority,
            api_base: (own.api_base ?? builtIn.api_base).replace(/\/+$/, ''),
            inherited: { authority: own.authority === undefined, api_base: own.api_base === undefined },
        };
        answer({ print: { name: shown, current: listedProfiles.current === shown, backend, team_id: own.team_id ?? null, authorization: { flag: false, env: false, stored: null, effective_source: null } } });
    }
} else if (name === 'reply' && rest[0] !== 'skills' && !declared(inUse())) {
    // Every other command resolves its profile first, and an undeclared one but `default` is a usage error before
    // anything else (src/profile.ts `resolve_profile`).
    usageProfile(inUse());
} else if (name === 'reply' && said === 'auth status --json' && process.env.STUB_REPLY_AUTH_STATUS) {
    // A read of the sign-in that ended as STUB_REPLY_AUTH_STATUS names it.
    answer(JSON.parse(process.env.STUB_REPLY_AUTH_STATUS));
} else if (name === 'reply' && said === 'auth status --json') {
    // The shape reply-cli prints (src/auth/status.ts, src/commands/auth.ts), and its exit code: 1 when no credential is
    // there. `team_id` at the top is the team the run would send, here the profile's pin, since the desktop hands the CLI
    // no REPLY_TEAM_ID and no --team-id. Without the file, a sign-in is pinned to team 3.
    // A key's `user.team_id` is the team Reply named it in at its sign-in, its own; a browser's, the team it was named in.
    const profile = inUse();
    const login = world !== null ? loginOf(profile) : process.env.STUB_REPLY_SIGNED_IN === 'yes' ? 'someone@example.com' : undefined;
    const named = world === null || namedBy(profile);
    const key = world !== null ? keyTeamOf(profile) : undefined;
    const seat = key ?? (world !== null ? ((world.seats ?? {})[login] ?? [3])[0] : 3);
    const pinned = world !== null ? ((world.profiles ?? {})[profile] ?? {}).team_id : 3;
    const user = named ? { user: { id: world !== null ? userIdOf(login, seat) : 7, username: login, team_id: seat } } : {};
    process.stdout.write(JSON.stringify(login ? { authenticated: true, profile, source: 'store', method: key !== undefined ? 'api_key' : 'oauth', ...user, ...(pinned === undefined ? {} : { team_id: pinned }) } : { authenticated: false, profile }));
    if (!login) process.exitCode = 1;
} else if (name === 'reply' && said === 'auth whoami --json' && process.env.STUB_REPLY_WHOAMI) {
    // A read of who the sign-in is that ended as STUB_REPLY_WHOAMI names it.
    answer(JSON.parse(process.env.STUB_REPLY_WHOAMI));
} else if (name === 'reply' && world !== null && said === 'auth whoami --json') {
    // Reply's /v3/whoami as reply-cli 0.23.2 prints it, the body as it came (src/commands/auth.ts `handle_whoami`), asked
    // in the profile's pinned team, else in none: refused as Reply refuses a login in several teams with none, or a team
    // it is not in. A key is answered in its own team. A Reply that carries them, a world with `whoami` (by team id, what
    // its answer says besides), also answers the team's name and its organization's id and name, null where there is
    // none; a world without it is a Reply that answers the three members alone.
    const login = loginOf(inUse());
    const seats = (world.seats ?? {})[login] ?? [3];
    const team = actingTeam(inUse(), seats);
    const carried = world.whoami === undefined ? {} : { teamName: (world.teams ?? {})[String(team)] || null, orgId: null, orgName: null, ...(world.whoami[String(team)] ?? {}) };
    if (!login) answer({ code: 'auth.required', exit: 2 });
    else if (team === null) refusedAccess('TEAM_REQUIRED');
    else if (keyTeamOf(inUse()) === undefined && !seats.includes(team)) refusedAccess('TEAM_NOT_ACCESSIBLE');
    else answer({ print: { userId: userIdOf(login, team), username: nameOf(login), teamId: team, ...carried } });
} else if (name === 'reply' && said === 'team list --json' && process.env.STUB_REPLY_TEAM_LIST) {
    // A read of the teams that ended as STUB_REPLY_TEAM_LIST names it, as reply-cli prints `teams.unavailable` and the like.
    answer(JSON.parse(process.env.STUB_REPLY_TEAM_LIST));
} else if (name === 'reply' && world === null && said === 'team list --json') {
    // Without the file, a sign-in can act in team 3 alone, which its profile pins.
    if (process.env.STUB_REPLY_SIGNED_IN === 'yes') answer({ print: { current_team_id: 3, teams: [{ team_id: 3, team_name: 'Made-up Three' }] } });
    else answer({ code: 'auth.required', exit: 2 });
} else if (name === 'reply' && said === 'auth logout --json') {
    // The sign-in kept under the profile the run names goes, and nothing else, with no call to Reply; exit 0 whether there
    // was one or not (src/commands/auth.ts `handle_logout`). STUB_REPLY_AUTH_LOGOUT names another ending.
    if (process.env.STUB_REPLY_AUTH_LOGOUT) answer(JSON.parse(process.env.STUB_REPLY_AUTH_LOGOUT));
    else if (world === null) answer({ print: { logged_out: true } });
    else {
        const { [inUse()]: credential, ...credentials } = world.credentials ?? {};
        world.credentials = credentials;
        keepWorld();
        answer({ print: { logged_out: credential !== undefined } });
    }
} else if (name === 'reply' && said === 'auth login --json' && process.env.STUB_REPLY_LOGIN) {
    // A browser's sign-in that ended as STUB_REPLY_LOGIN names it, the way it was printed: {"exit", "stderr"?}.
    answer(JSON.parse(process.env.STUB_REPLY_LOGIN));
} else if (name === 'reply' && said === 'auth login --json') {
    // The browser's sign-in is kept under the profile the run names, replacing what it held; the current profile stays.
    // Reply names it in the profile's pinned team, else where it has one team alone; otherwise it is kept with no user.
    const login = world?.browser || 'browser@example.test';
    const seats = world !== null ? ((world.seats ?? {})[login] ?? [3]) : [3];
    const pinned = world !== null ? ((world.profiles ?? {})[inUse()] ?? {}).team_id : undefined;
    const named = pinned === undefined ? seats.length === 1 : seats.includes(pinned);
    if (world !== null) {
        world.credentials = { ...(world.credentials ?? {}), [inUse()]: named ? login : { login, unnamed: true } };
        keepWorld();
    }
    const user = named ? { user: { id: world !== null ? userIdOf(login, pinned ?? seats[0]) : 7, username: login, team_id: pinned ?? seats[0] } } : {};
    if (!named) process.stderr.write('Signed in, but could not fetch your identity: Reply refused access.\n');
    answer({ print: { logged_in: true, method: 'oauth', profile: inUse(), ...user } });
} else if (name === 'reply' && said === 'auth login --with-token --json') {
    // A key on standard input, checked as Reply's whoami would and kept under the profile only when it answers. A key Reply
    // refuses is its 401, printed under --json as reply-cli prints an API error (src/utils/errors.ts, src/utils/client.ts).
    // Reply answers a key in its own team, whatever the profile pins, so the pin never refuses it.
    const key = stdin.trim();
    if (key === '') answer({ code: 'usage.stdin' });
    else if (world === null) process.exit(0);
    else if (!Object.hasOwn(world.keys ?? {}, key)) {
        const hint = 'Invalid or expired credential. Re-check your key/token or run `reply auth login`.';
        answer({ exit: 1, stderr: `${JSON.stringify({ error: { status: 401, title: 'Unauthorized', detail: 'Authentication credentials are missing or invalid.', hint } })}\n` });
    } else {
        const { login, team } = world.keys[key];
        world.credentials = { ...(world.credentials ?? {}), [inUse()]: { login, key: team } };
        keepWorld();
        answer({ print: { logged_in: true, method: 'api_key', profile: inUse(), user: { id: userIdOf(login, team), username: login, team_id: team } } });
    }
} else if (name === 'reply' && world !== null && (said === 'team list --json' || (rest[0] === 'team' && rest[1] === 'use' && rest[3] === '--json' && rest.length === 4))) {
    // The teams a login can act in, as reply-cli 0.23.2 prints them (src/commands/team.ts): `team list` the profile's pinned
    // team and every team; `team use <id>` pins one the login is in on the profile, and refuses any other as usage.team.
    const login = loginOf(inUse());
    const seats = login ? ((world.seats ?? {})[login] ?? [3]) : null;
    const listed = (seats ?? []).map((id) => ({ team_id: id, team_name: (world.teams ?? {})[String(id)] ?? '' }));
    const pinned = ((world.profiles ?? {})[inUse()] ?? {}).team_id ?? null;
    // No sign-in under the profile, as reply-cli says it (src/auth/resolve.ts `not_authenticated`).
    if (seats === null) answer({ code: 'auth.required', exit: 2, error: { title: 'Not authenticated.' } });
    else if (rest[1] === 'list') answer({ print: { current_team_id: pinned, teams: listed } });
    else if (!/^\d+$/.test(rest[2])) answer({ code: 'usage.team', error: { title: 'Team id must be a positive integer.' } });
    else if (!seats.includes(Number(rest[2]))) answer({ code: 'usage.team', error: { title: `Team ${rest[2]} isn't one you can act in.` } });
    else {
        configWrite();
        const id = Number(rest[2]);
        world.profiles = { ...(world.profiles ?? {}), [inUse()]: { ...((world.profiles ?? {})[inUse()] ?? {}), team_id: id } };
        keepWorld();
        answer({ print: { profile: inUse(), team_id: id, team_name: listed.find((each) => each.team_id === id).team_name } });
    }
} else if (name === 'reply' && rest[0] === 'account' && world !== null && !(world.credentials ?? {})[inUse()]) {
    // No sign-in under the profile: `auth.required`, exit 2, before Reply is asked (src/operations/call.ts).
    answer({ code: 'auth.required', exit: 2 });
} else if (name === 'reply' && world !== null && (said === 'account list --channel email --json' || said === 'account list --channel linkedin --json')) {
    // What Reply lists for the sign-in in the team the run acts in: a key's own; else the one its profile pins, else the one
    // seat a sign-in holds; a sign-in with two seats and no team pinned, or a pinned team it has no seat in, is refused as
    // Reply refuses it (403, `access.denied`, exit 1).
    const login = loginOf(inUse());
    const seats = (world.seats ?? {})[login] ?? [3];
    const team = actingTeam(inUse(), seats);
    // STUB_REPLY_ACCOUNT_LIST names another ending of every channel's read, a network failure say.
    if (process.env.STUB_REPLY_ACCOUNT_LIST) answer(JSON.parse(process.env.STUB_REPLY_ACCOUNT_LIST));
    else if (team === null) refusedAccess('TEAM_REQUIRED');
    else if (keyTeamOf(inUse()) === undefined && !seats.includes(team)) refusedAccess('TEAM_NOT_ACCESSIBLE');
    else answer({ print: { items: ((world.lists ?? {})[`${login}#${team}`] ?? {})[rest[3]] ?? [] } });
} else if (name === 'reply' && (said === 'account list --channel email --json' || said === 'account list --channel linkedin --json')) {
    // Per channel, as reply-cli prints it: the items of that channel, or a refusal when the plan or role has none.
    const channel = rest[3];
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
} else if (name === 'reply' && rest[0] === 'account' && rest[1] !== 'list') {
    // The account verbs as the Reply CLI ends them under --json: the result on stdout and exit 0, a refusal as one line
    // {"error":{...}} on stderr and exit 1 (2 for a usage.* code), exit 3 for a write whose outcome it does not know. The
    // answer is named per verb by STUB_REPLY_ACCOUNT_<VERB>, with _FORCE where --force was passed: {"print": <result>}, {"code": <code>, "exit"?}, or {"exit", "stdout"?, "stderr"?} as it is.
    // `pending` may be answered per channel by STUB_REPLY_ACCOUNT_PENDING_<CHANNEL>, read before the verb's own answer, and
    // lists nothing when none is named.
    const verb = (rest[1] ?? '').toUpperCase();
    const key = `STUB_REPLY_ACCOUNT_${verb}${rest.includes('--force') ? '_FORCE' : ''}`;
    const perChannel = verb === 'PENDING' && rest[2] === '--channel' ? process.env[`${key}_${(rest[3] ?? '').toUpperCase()}`] : undefined;
    const named = perChannel || process.env[key];
    const given = named ? JSON.parse(named) : verb === 'PENDING' ? { print: { items: [], supported: true, unsupported_channels: [] } } : null;
    if (given === null) {
        process.stderr.write(`the stub reply does not answer ${said}\n`);
        process.exitCode = 2;
    } else {
        answer(given);
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
