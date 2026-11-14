import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { handle, invokeChannels, RefusedSender } from '../src/ipc.ts';
import { changeJasonNoHost, changeJasonNotInDemo, hostUnchosen, OpenIn, type OpenInAnswer, type OpenInDeps, type PlaceId } from '../src/open-in.ts';
import { changeJasonFlags, continueFlags, terminalFlags } from '../src/open-in-terminal.ts';

// "Change Jason itself": the entry that opens the person's own AI on Jason, by `jason session open --setup`. The command
// line's own rule chooses the host, the preference first; only where it cannot choose does the desktop name the host it
// found. Every answer of the command line here is made up, in the shape it prints.

type Answer = { exit: number; stdout: string; stderr: string };

const refusal = (code: string, message = `The sentence of ${code}.`): Answer => ({ exit: 1, stdout: JSON.stringify({ error: { code, message, retryable: false } }), stderr: '' });
const opened: Answer = { exit: 0, stdout: '{"kind":"setup","started":true}', stderr: '' };
/** What `jason preference list` prints with the host preference naming `host`, or with none set. */
const preferences = (host: string | null): Answer => ({
    exit: 0,
    stdout: JSON.stringify({ items: [{ key: 'agent.interactive_host', value: host === null ? null : { host } }, { key: 'tone_of_voice', value: null }] }),
    stderr: '',
});

interface Bench {
    openIn: OpenIn;
    calls: { args: string[]; cwd: string | undefined }[];
    kept: PlaceId[];
    logged: string[];
}

/**
 * An installation whose command line answers `session open` from the list, in order, `preference list` with the answer
 * given (by default a preference naming a host that is neither of the two), and every other verb with nothing.
 */
function bench(options: { live?: boolean; offered?: boolean; hosts?: { claude: boolean; codex: boolean }; answers?: Answer[]; preference?: Answer } = {}): Bench {
    const calls: { args: string[]; cwd: string | undefined }[] = [];
    const kept: PlaceId[] = [];
    const logged: string[] = [];
    const answers = [...(options.answers ?? [opened])];
    const deps: OpenInDeps = {
        probes: { platform: 'win32', env: {}, home: 'C:\\Users\\ada', onPath: () => null, exists: () => false },
        hosts: () => options.hosts ?? { claude: true, codex: true },
        cli: {
            run: (args, given) => {
                calls.push({ args: [...args], cwd: given?.cwd });
                if (args[0] === 'session') return Promise.resolve(answers.shift() ?? assert.fail(`no answer left for ${args.join(' ')}`));
                return Promise.resolve(args[0] === 'preference' ? (options.preference ?? preferences('gemini_cli')) : { exit: 0, stdout: '{}', stderr: '' });
            },
        },
        harnessRoot: () => 'C:\\Jason',
        isDirectory: () => true,
        remembered: () => null,
        remember: (id) => void kept.push(id),
        start: () => Promise.resolve(null),
        copy: () => undefined,
        terminalFlags,
        continuable: () => null,
        chatFolder: () => 'C:\\data\\desktop\\chat',
        continueFlags,
        ...(options.offered === false ? {} : { changeJason: { live: () => options.live ?? true, flags: changeJasonFlags, log: (event: string, fields: { Host: string; Exit: number; Code?: string }) => void logged.push([event, fields.Host, fields.Exit, ...(fields.Code === undefined ? [] : [fields.Code])].join(':')) } }),
    };
    return { openIn: new OpenIn(deps), calls, kept, logged };
}

const sessions = (b: Bench) => b.calls.filter((call) => call.args[0] === 'session');

test('the flags of Change Jason itself are --setup alone, so the command line chooses the host, and --host only for a host the desktop names', () => {
    assert.deepEqual(changeJasonFlags(null), ['--setup']);
    assert.deepEqual(changeJasonFlags('claude'), ['--setup', '--host', 'claude_code']);
    assert.deepEqual(changeJasonFlags('codex'), ['--setup', '--host', 'codex']);
});

test('Change Jason itself is offered in the person\'s own installation whatever hosts are found, naming none, and never in a demo run', async () => {
    for (const hosts of [{ claude: true, codex: true }, { claude: false, codex: true }, { claude: false, codex: false }]) {
        const answer = (await bench({ hosts }).openIn.targets()) as OpenInAnswer;
        assert.equal(answer.changeJason, true, JSON.stringify(hosts));
    }
    assert.equal('changeJason' in ((await bench({ live: false }).openIn.targets()) as OpenInAnswer), false, 'a demo run');
    assert.equal('changeJason' in ((await bench({ offered: false }).openIn.targets()) as OpenInAnswer), false, 'a run that does not offer it');
    // It is no place: the places, and so the default, are the same with it as without it.
    const answer = (await bench().openIn.targets()) as OpenInAnswer;
    assert.ok(!answer.places.some((place) => (place.id as string) === 'changeJason' || (place.id as string) === 'setup'));
    assert.equal(answer.default, 'claude');
});

test('Change Jason itself runs jason session open --setup with no host and no working folder, and is never kept as the default', async () => {
    const b = bench();
    assert.equal(await b.openIn.changeJason(), null);
    assert.deepEqual(sessions(b), [{ args: ['session', 'open', '--setup'], cwd: undefined }]);
    assert.deepEqual(b.calls.filter((call) => call.args[0] !== 'session'), [], 'nothing else is asked');
    assert.deepEqual(b.kept, []);
    assert.deepEqual(b.logged, ['open_in_change_jason:preference:0']);
    assert.deepEqual(b.calls.filter((call) => call.args[0] === 'preference'), [], 'the preference is read only after a host was not found');
});

test('where the preference chooses a host that is there, that host opens and the desktop names none, Codex with Claude Code found included', async () => {
    // The command line chose by the preference (Codex, say) and started it: the desktop never runs a second time.
    const b = bench({ hosts: { claude: true, codex: true }, answers: [opened] });
    assert.equal(await b.openIn.changeJason(), null);
    assert.equal(sessions(b).length, 1);
    assert.ok(!sessions(b)[0]!.args.includes('--host'));
});

test('where the command line could not choose a host, it is run once more with the host the desktop found: Claude Code, else Codex', async () => {
    assert.deepEqual(hostUnchosen, ['host_unknown', 'preference_unreadable', 'host_not_found']);
    for (const code of hostUnchosen) {
        for (const [hosts, word, logged] of [
            [{ claude: true, codex: true }, 'claude_code', 'desktop_claude'],
            [{ claude: false, codex: true }, 'codex', 'desktop_codex'],
        ] as const) {
            const b = bench({ hosts, answers: [refusal(code), opened] });
            assert.equal(await b.openIn.changeJason(), null, `${code} ${word}`);
            assert.deepEqual(sessions(b).map((call) => call.args), [['session', 'open', '--setup'], ['session', 'open', '--setup', '--host', word]], `${code} ${word}`);
            assert.deepEqual(b.logged, [`open_in_change_jason:${logged}:0`]);
        }
    }
});

test('the second run is the last: a host it does not find either is said in the command line\'s own sentence, and nothing runs a third time', async () => {
    const b = bench({ answers: [refusal('host_not_found'), refusal('host_not_found', 'claude was not found on the PATH.')] });
    assert.deepEqual(await b.openIn.changeJason(), { refused: 'claude was not found on the PATH.' });
    assert.equal(sessions(b).length, 2);
    assert.deepEqual(b.logged, ['open_in_change_jason_refused:desktop_claude:1:host_not_found']);
});

test('the second run never names the host the command line tried: with no preference set that is Claude Code, so it is Codex, and with Codex not found there is none', async () => {
    const both = bench({ preference: preferences(null), answers: [refusal('host_not_found'), opened] });
    assert.equal(await both.openIn.changeJason(), null);
    assert.deepEqual(sessions(both).map((call) => call.args), [['session', 'open', '--setup'], ['session', 'open', '--setup', '--host', 'codex']]);
    assert.deepEqual(both.calls.filter((call) => call.args[0] === 'preference').map((call) => call.args), [['preference', 'list']], 'read once, to know which host was tried');
    assert.deepEqual(both.logged, ['open_in_change_jason:desktop_codex:0']);
    // Claude Code found by the desktop, not by the command line, and no Codex: nothing else to try.
    const claudeOnly = bench({ hosts: { claude: true, codex: false }, preference: preferences(null), answers: [refusal('host_not_found')] });
    assert.deepEqual(await claudeOnly.openIn.changeJason(), { refused: changeJasonNoHost });
    assert.equal(sessions(claudeOnly).length, 1);
    assert.deepEqual(claudeOnly.logged, ['open_in_change_jason_refused:preference:1:host_not_found']);
});

test('a preference naming Codex that is not installed is run once more on Claude Code, never on Codex again', async () => {
    const b = bench({ preference: preferences('codex'), answers: [refusal('host_not_found'), opened] });
    assert.equal(await b.openIn.changeJason(), null);
    assert.deepEqual(sessions(b).map((call) => call.args), [['session', 'open', '--setup'], ['session', 'open', '--setup', '--host', 'claude_code']]);
    const codexOnly = bench({ hosts: { claude: false, codex: true }, preference: preferences('codex'), answers: [refusal('host_not_found')] });
    assert.deepEqual(await codexOnly.openIn.changeJason(), { refused: changeJasonNoHost });
    assert.equal(sessions(codexOnly).length, 1);
});

test('where the preference list cannot be read, which host was tried is not known: there is no second run, and the command line\'s own sentence is said', async () => {
    const b = bench({ preference: { exit: 3, stdout: '', stderr: '' }, answers: [refusal('host_not_found', 'codex was not found on the PATH.')] });
    assert.deepEqual(await b.openIn.changeJason(), { refused: 'codex was not found on the PATH.' });
    assert.equal(sessions(b).length, 1);
    assert.deepEqual(b.logged, ['open_in_change_jason_refused:preference:1:host_not_found']);
});

test('a preference it could not read, or a word it does not know, tried no host: the second run takes Claude Code first, and no preference is read', async () => {
    for (const code of ['host_unknown', 'preference_unreadable']) {
        const b = bench({ answers: [refusal(code), opened] });
        assert.equal(await b.openIn.changeJason(), null, code);
        assert.deepEqual(sessions(b).at(-1)!.args, ['session', 'open', '--setup', '--host', 'claude_code'], code);
        assert.deepEqual(b.calls.filter((call) => call.args[0] === 'preference'), [], code);
    }
});

test('with neither host found, a command line that could not choose is not run again: a host not installed is said as the two it needs, any other code in its own sentence', async () => {
    const none = { claude: false, codex: false };
    const missing = bench({ hosts: none, answers: [refusal('host_not_found', 'gemini was not found on the PATH.')] });
    assert.deepEqual(await missing.openIn.changeJason(), { refused: changeJasonNoHost });
    assert.equal(sessions(missing).length, 1);
    const unknown = bench({ hosts: none, answers: [refusal('host_unknown', 'The preference names something else.')] });
    assert.deepEqual(await unknown.openIn.changeJason(), { refused: 'The preference names something else.' });
    assert.equal(sessions(unknown).length, 1);
});

test('a refusal that is not about the host is said in the command line\'s own sentence, read by its code and never run again', async () => {
    for (const code of ['folder_missing', 'terminal_not_found', 'terminal_start_failed']) {
        const b = bench({ answers: [refusal(code)] });
        assert.deepEqual(await b.openIn.changeJason(), { refused: `The sentence of ${code}.` }, code);
        assert.equal(sessions(b).length, 1, code);
        assert.deepEqual(b.logged, [`open_in_change_jason_refused:preference:1:${code}`]);
    }
    // The code decides, never the words: a sentence that speaks of a host under another code is not run again.
    const worded = bench({ answers: [refusal('folder_missing', 'No host was found, and no folder.')] });
    assert.deepEqual(await worded.openIn.changeJason(), { refused: 'No host was found, and no folder.' });
    assert.equal(sessions(worded).length, 1);
});

test('a runtime that cannot be reached is said so, and nothing is run again; a usage error says its first line', async () => {
    const down = bench({ answers: [{ exit: 3, stdout: '', stderr: 'No runtime answered.' }] });
    assert.deepEqual(await down.openIn.changeJason(), { refused: "Jason's runtime is not running." });
    assert.equal(sessions(down).length, 1);
    assert.deepEqual(down.logged, ['open_in_change_jason_refused:preference:3:none']);
    // A code outside the act's own table is logged as other: no word the command line printed reaches the log.
    const odd = bench({ answers: [refusal('C:\\Users\\someone said so')] });
    await odd.openIn.changeJason();
    assert.deepEqual(odd.logged, ['open_in_change_jason_refused:preference:1:other']);
    const usage = bench({ answers: [{ exit: 2, stdout: '', stderr: '--setup is not taken with --here.\nUsage: ...' }] });
    assert.deepEqual(await usage.openIn.changeJason(), { refused: '--setup is not taken with --here.' });
    const silent = bench({ answers: [{ exit: 4, stdout: 'not json', stderr: '' }] });
    assert.deepEqual(await silent.openIn.changeJason(), { refused: 'A terminal could not be opened.' });
});

test('in a demo run, or a run that does not offer it, Change Jason itself is refused and nothing is run', async () => {
    for (const b of [bench({ live: false }), bench({ offered: false })]) {
        assert.deepEqual(await b.openIn.changeJason(), { refused: changeJasonNotInDemo });
        assert.deepEqual(b.calls, []);
        assert.deepEqual(b.kept, []);
    }
});

test('the act answers only the runtime’s own page in its top frame, and the main process hands it nothing the page sent', async () => {
    const listeners = new Map<string, (event: { senderFrame: { url: string; parent: unknown } | null }, ...args: unknown[]) => unknown>();
    const reached: unknown[][] = [];
    handle({ handle: (channel, listener) => void listeners.set(channel, listener) }, 'openIn.changeJason', () => 'http://127.0.0.1:7788', (...args) => void reached.push(args), () => undefined);
    const channel = listeners.get(invokeChannels['openIn.changeJason']);
    assert.ok(channel !== undefined);
    for (const frame of [null, { url: 'http://127.0.0.1:7788/ui/', parent: {} }, { url: 'https://example.test/', parent: null }]) {
        assert.throws(() => channel({ senderFrame: frame }), RefusedSender);
    }
    channel({ senderFrame: { url: 'http://127.0.0.1:7788/ui/', parent: null } });
    assert.equal(reached.length, 1);
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    assert.match(main, /handle\(seam, 'openIn\.changeJason', origin, \(\) => openIn\.changeJason\(\), refused\);/);
    assert.match(main, /changeJason: \{\s+live: \(\) => runOn\.mode === 'live',\s+flags: changeJasonFlags,/);
    // Whatever a page sends with it is never read.
    const b = bench();
    const act = b.openIn.changeJason.bind(b.openIn) as (...args: unknown[]) => Promise<unknown>;
    assert.equal(await act('--here', 'C:\\Elsewhere', { cwd: '/tmp' }), null);
    assert.deepEqual(sessions(b), [{ args: ['session', 'open', '--setup'], cwd: undefined }]);
});
