// The real Claude Code, signed out, started as a turn of each mode is started: its command line and its environment are
// the chat's own, built by the desktop for that turn, in a data directory, a home and a configuration directory of the
// row's own, with nothing to sign in by. Each start is stopped at its init line, so no turn runs. The init line names the
// mode Claude Code runs in, and its standard error says when Claude Code forced its default mode. Where no `claude` is on
// the PATH the rows are skipped, so a machine without Claude Code runs as before, and on macOS they run only where
// `realOnMac` says so. Where `printTo` names a file, each start's command line, environment and turn files are written
// there as the desktop built them, the turn's secret aside.
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { Chat } from '../src/chat.ts';
import { readFence } from '../src/chat-deny.ts';
import { permissionModes } from '../src/chat-hosts.ts';
import { fullAccessSentence, type ChatMode } from '../src/chat-modes.ts';
import { startHost, type HostProcess } from '../src/chat-process.ts';
import { pathOf, programsOn } from '../src/programs.ts';
import { bench, outsideStandIn } from './harness.ts';

const fence = readFence(join(import.meta.dirname, '..'));
const onPath = programsOn(process.env, null).find('claude');
/**
 * On macOS Claude Code may take a sign-in from the keychain, which a home of the row's own does not replace, so the rows
 * start it there only where this variable is `1`.
 */
const realOnMac = 'JASON_REAL_CLAUDE_ON_MAC';
/** The file each start's command line, environment and turn files are written to, where this variable names one. */
const printTo = 'JASON_REAL_CLAUDE_PRINT';
const skip =
    onPath === null
        ? 'Claude Code is not on the PATH, and these rows start the real one'
        : process.platform === 'darwin' && process.env[realOnMac] !== '1'
          ? `on macOS these rows start the real Claude Code only where ${realOnMac}=1 is set, since it may take a sign-in from the keychain`
          : false;
/** Claude Code's own words on its standard error when it starts in its default mode in place of the mode asked for. */
const forcedWords = 'Permission mode forced to default';

interface Start {
    launch: { file: string; prefix: string[] };
    args: string[];
    cwd: string;
    env: Record<string, string>;
    stdin: string;
    /** The turn's own files the command line names, as they were when it started (`turnFiles`). */
    files: Record<string, string>;
    init: Record<string, unknown> | null;
    host: HostProcess;
}

/** A text with the turn's secret taken out wherever it is given as a bearer token. */
const withoutSecret = (text: string) => text.replace(/Bearer [^"'\s]+/g, 'Bearer <secret>');

/** The files a start's `--settings` and `--mcp-config` name, read as the host would read them; an inline value names none. */
function turnFiles(args: readonly string[]): Record<string, string> {
    const files: Record<string, string> = {};
    for (const name of ['--settings', '--mcp-config']) {
        const value = flag(args, name);
        if (value === undefined || value.trimStart().startsWith('{')) continue;
        try {
            files[`${name} ${value}`] = withoutSecret(readFileSync(value, 'utf8'));
        } catch {
            files[`${name} ${value}`] = '(not there when read)';
        }
    }
    return files;
}

/** One start as the desktop built it: the program and its arguments, the folder, the environment by name, the turn files. */
function printed(mode: ChatMode, version: string | null, start: Start): string {
    const lines = [`== ${mode}: Claude Code ${version ?? '?'} on ${process.platform}`, 'program:', `  ${start.launch.file}`, 'arguments, one per line:'];
    for (const word of [...start.launch.prefix, ...start.args]) lines.push(`  ${withoutSecret(word)}`);
    lines.push('working folder:', `  ${start.cwd}`, 'environment:');
    for (const name of Object.keys(start.env).sort()) lines.push(`  ${name}=${withoutSecret(start.env[name] as string)}`);
    lines.push('standard input:', `  ${start.stdin}`);
    for (const [file, text] of Object.entries(start.files)) lines.push(`file ${file}:`, ...text.split('\n').map((line) => `  ${line}`));
    lines.push(`init line names: ${String(start.init?.permissionMode)}`, `forced line: ${start.host.stderrTail().includes(forcedWords) ? 'present' : 'absent'}`, '');
    return lines.join('\n');
}

const flag = (args: readonly string[], name: string) => (args.includes(name) ? args[args.indexOf(name) + 1] : undefined);
const scrubbed = (env: Record<string, string>) => Object.entries(env).some(([name, value]) => name.toUpperCase() === 'CLAUDE_CODE_SUBPROCESS_ENV_SCRUB' && value === '1');

function initOf(line: string): Record<string, unknown> | null {
    try {
        const event = JSON.parse(line) as Record<string, unknown> | null;
        return event !== null && event.type === 'system' && event.subtype === 'init' ? event : null;
    } catch {
        return null;
    }
}

/**
 * A desktop whose Claude Code is the real one on the PATH, given no sign-in: a home, a configuration directory and the
 * system's application folders of the row's own, no updater and no traffic a start does not need. Every start the chat
 * makes is kept with the command line and the environment the chat built for it, and stopped at its init line.
 */
function realDesktop(t: { after(fn: () => void | Promise<void>): void }) {
    const own = mkdtempSync(join(tmpdir(), 'jason-real-claude-'));
    const folders = Object.fromEntries(['home', 'config', 'appdata', 'local', 'temp'].map((name) => [name, join(own, name)]));
    for (const folder of Object.values(folders)) mkdirSync(folder, { recursive: true });
    const carried = (name: string) => (process.env[name] === undefined ? {} : { [name]: process.env[name] as string });
    const b = bench(
        { after: (fn) => t.after(fn) },
        {
            env: {
                PATH: pathOf(process.env),
                HOME: folders.home as string,
                USERPROFILE: folders.home as string,
                APPDATA: folders.appdata as string,
                LOCALAPPDATA: folders.local as string,
                TEMP: folders.temp as string,
                TMP: folders.temp as string,
                CLAUDE_CONFIG_DIR: folders.config as string,
                DISABLE_AUTOUPDATER: '1',
                CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: '1',
                // Where Claude Code finds the shell it needs on Windows, as the desktop it runs in would hand it on.
                ...(process.platform === 'win32' ? { ...carried('COMSPEC'), ...carried('WINDIR'), ...carried('CLAUDE_CODE_GIT_BASH_PATH') } : {}),
            },
        },
    );
    const bundle = join(b.root, 'bundle');
    mkdirSync(bundle, { recursive: true });
    const starts: Start[] = [];
    const started: ChatMode[] = [];
    const dones = new Map<string, () => void>();
    const chat = new Chat({
        paths: b.paths,
        env: b.env,
        bundle,
        dataNamed: true,
        bundleVersion: null,
        cli: b.cli,
        programs: b.programs,
        keyFor: () => undefined,
        fence: () => fence,
        sandboxOutside: outsideStandIn,
        emit(id, event) {
            if (event.kind === 'done') dones.get(id)?.();
        },
        started: (_host, _resumed, mode) => void started.push(mode),
        startHost: (launch, args, options) => {
            const start: Start = {
                launch: { file: launch.file, prefix: [...launch.prefix] },
                args: [...args],
                cwd: options.cwd,
                env: { ...options.env },
                stdin: options.stdin,
                files: turnFiles(args),
                init: null,
                host: undefined as unknown as HostProcess,
            };
            start.host = startHost(launch, args, {
                ...options,
                onLine: (line) => {
                    if (start.init === null) {
                        start.init = initOf(line);
                        if (start.init !== null) start.host.stop();
                    }
                    options.onLine(line);
                },
            });
            starts.push(start);
            return start.host;
        },
    });
    t.after(async () => {
        chat.closeAll();
        await chat.idle();
        for (const start of starts) await start.host.exited;
        rmSync(own, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
    });
    return {
        b,
        chat,
        starts,
        started,
        /** Sends one message to the chat and waits for its turn's end. */
        async turn(id: string): Promise<void> {
            const done = new Promise<void>((resolve, reject) => {
                const timer = setTimeout(() => reject(new Error('the turn never ended')), 60_000);
                dones.set(id, () => {
                    clearTimeout(timer);
                    resolve();
                });
            });
            await chat.send(id, 'hello', { host: 'claude' });
            await done;
        },
    };
}

const modes: readonly { mode: ChatMode; choose: (chat: Chat, id: string) => Promise<unknown> }[] = [
    { mode: 'full_access', choose: async () => undefined },
    { mode: 'ask_first', choose: (chat, id) => chat.setMode(id, 'ask_first', 'claude') },
    { mode: 'auto_edit', choose: (chat, id) => chat.setMode(id, 'auto_edit', 'claude') },
    { mode: 'plan_only', choose: (chat, id) => chat.setMode(id, 'plan_only') },
];

test('the real Claude Code, signed out, runs each mode\'s turn in the mode its init line names: Full access in its bypass with no word of a forced mode, Ask first and Auto-edit in its default mode, and Plan only, which carries the scrub off Linux, in what Claude Code 2.1.329 names under it', { skip }, async (t) => {
    const desk = realDesktop(t);
    const version = /\d+\.\d+\.\d+/.exec((await desk.b.programs.run('claude', ['--version'])).stdout)?.[0] ?? null;
    t.diagnostic(`Claude Code ${version ?? 'of a version that could not be read'}`);
    const prints: string[] = [];
    const print = process.env[printTo];
    if (print !== undefined && print !== '') t.after(() => writeFileSync(print, prints.join('\n'), 'utf8'));
    for (const { mode, choose } of modes) {
        await t.test(mode, async (row) => {
            if (mode === 'full_access' && fullAccessSentence(version) !== null) {
                row.skip(`Claude Code ${version ?? '?'} is below the floor of Full access`);
                return;
            }
            const { id } = await desk.chat.create();
            await choose(desk.chat, id);
            const from = desk.starts.length;
            await desk.turn(id);
            const start = desk.starts[from];
            assert.ok(start !== undefined, 'the turn started Claude Code');
            await start.host.exited;
            prints.push(printed(mode, version, start));
            const scrub = scrubbed(start.env);
            const named = start.init?.permissionMode;
            const forced = start.host.stderrTail().includes(forcedWords);
            row.diagnostic(`asked ${flag(start.args, '--permission-mode')}, scrub ${scrub ? 'set' : 'not set'}, init names ${String(named)}, forced line ${forced ? 'present' : 'absent'}`);
            assert.equal(desk.started[from], mode, 'the turn ran in its own mode');
            assert.equal(flag(start.args, '--permission-mode'), permissionModes[mode], 'the mode asked for is the mode\'s own');
            assert.equal(scrub, mode !== 'full_access' && process.platform !== 'linux', 'the scrub as the code decides it');
            assert.ok(start.init !== null, 'Claude Code said its init line');
            // A turn the hook rides carries no block of reads in its settings, and every other turn still does.
            const settings = Object.entries(start.files).find(([file]) => file.startsWith('--settings '))?.[1] ?? flag(start.args, '--settings') ?? '';
            const hooked = mode === 'full_access' || (mode !== 'plan_only' && fullAccessSentence(version) === null);
            assert.equal(settings.includes('blockReadsOutsideWorkingDirectories'), !hooked, 'the block of reads as the mode decides it');
            // A turn that runs in Full access loads the person's own configuration; every other turn keeps the sealed line.
            for (const sealing of ['--setting-sources', '--strict-mcp-config']) assert.equal(start.args.includes(sealing), mode !== 'full_access', sealing);
            if (mode === 'plan_only') {
                // Found on 2.1.329 on Windows: under the scrub Claude Code starts a Plan only turn in its default mode and says
                // so; with none, as on Linux, it runs in the plan mode asked for.
                assert.equal(named, scrub ? 'default' : 'plan');
                assert.equal(forced, scrub, 'the forced line where the scrub is set, and only there');
            } else {
                assert.equal(named, mode === 'full_access' ? 'bypassPermissions' : 'default');
                assert.equal(forced, false, 'Claude Code did not force its default mode');
            }
        });
    }
});
