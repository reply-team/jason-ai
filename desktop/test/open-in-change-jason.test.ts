import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { handle, invokeChannels, RefusedSender } from '../src/ipc.ts';
import { changeJasonNoHost, changeJasonUnoffered, hostUnchosen, inUseHost, OpenIn, type OpenInAnswer, type OpenInDeps, type PlaceId } from '../src/open-in.ts';
import { changeJasonFlags, continueFlags, terminalFlags } from '../src/open-in-terminal.ts';
import { hostWords } from '../src/roles-profile.ts';

// "Change Jason itself": the entry that opens the person's own AI on Jason, by `jason session open --setup`. A preference
// the person stated chooses the host by the command line's own rule; with none stated the desktop names the program the
// Models page marks In use; else the command line's rule chooses; only where the host could not be opened does the desktop
// name the host it found. Every answer of the command line here is made up, in the shape it prints.

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
    /** How many times the program In use was read. */
    inUseReads: () => number;
}

/** A promise that settles after the turns of the event loop given, so two reads can be made to land in either order. */
const after = <T>(turns: number, value: () => T): Promise<T> =>
    new Promise((resolve, reject) => {
        const step = (left: number) => {
            if (left > 0) return void setImmediate(() => step(left - 1));
            try {
                resolve(value());
            } catch (error) {
                reject(error as Error);
            }
        };
        step(turns);
    });

/**
 * An installation whose command line answers `session open` from the list, in order, `preference list` with the answer
 * given (by default a preference naming a host that is neither of the two), and every other verb with nothing; the
 * program In use is the host word given (by default none), or a read that throws. `preferenceTurns` and `inUseTurns`
 * delay each read's answer by that many turns of the event loop.
 */
function bench(
    options: {
        offered?: boolean;
        hosts?: { claude: boolean; codex: boolean };
        answers?: Answer[];
        preference?: Answer;
        inUse?: string | null | Error;
        preferenceTurns?: number;
        inUseTurns?: number;
    } = {},
): Bench {
    const calls: { args: string[]; cwd: string | undefined }[] = [];
    const kept: PlaceId[] = [];
    const logged: string[] = [];
    let inUseReads = 0;
    const answers = [...(options.answers ?? [opened])];
    const deps: OpenInDeps = {
        probes: { platform: 'win32', env: {}, home: 'C:\\Users\\ada', onPath: () => null, exists: () => false },
        hosts: () => options.hosts ?? { claude: true, codex: true },
        cli: {
            run: (args, given) => {
                calls.push({ args: [...args], cwd: given?.cwd });
                if (args[0] === 'session') return Promise.resolve(answers.shift() ?? assert.fail(`no answer left for ${args.join(' ')}`));
                if (args[0] === 'preference') return after(options.preferenceTurns ?? 0, () => options.preference ?? preferences('gemini_cli'));
                if (args[0] === 'campaign') return Promise.resolve({ exit: 0, stdout: JSON.stringify({ folder_path: 'C:\\Jason\\campaigns\\one' }), stderr: '' });
                return Promise.resolve({ exit: 0, stdout: '{}', stderr: '' });
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
        ...(options.offered === false
            ? {}
            : {
                  changeJason: {
                      flags: changeJasonFlags,
                      inUse: () => {
                          inUseReads += 1;
                          return after(options.inUseTurns ?? 0, () => {
                              const word = options.inUse ?? null;
                              if (word instanceof Error) throw word;
                              return word;
                          });
                      },
                      log: (event: string, fields: { Host: string; Exit: number; Code?: string }) =>
                          void logged.push([event, fields.Host, fields.Exit, ...(fields.Code === undefined ? [] : [fields.Code])].join(':')),
                  },
              }),
    };
    return { openIn: new OpenIn(deps), calls, kept, logged, inUseReads: () => inUseReads };
}

const sessions = (b: Bench) => b.calls.filter((call) => call.args[0] === 'session');

test('the flags of Change Jason itself are --setup alone, so the command line chooses the host, and --host only for a host the desktop names', () => {
    assert.deepEqual(changeJasonFlags(null), ['--setup']);
    assert.deepEqual(changeJasonFlags('claude'), ['--setup', '--host', 'claude_code']);
    assert.deepEqual(changeJasonFlags('codex'), ['--setup', '--host', 'codex']);
});

test('Change Jason itself is offered in the person\'s own installation whatever hosts are found, naming none, and never where the run does not offer it', async () => {
    for (const hosts of [{ claude: true, codex: true }, { claude: false, codex: true }, { claude: false, codex: false }]) {
        const answer = (await bench({ hosts }).openIn.targets()) as OpenInAnswer;
        assert.equal(answer.changeJason, true, JSON.stringify(hosts));
    }
    assert.equal('changeJason' in ((await bench({ offered: false }).openIn.targets()) as OpenInAnswer), false, 'a run that does not offer it');
    // It is no place: the places, and so the default, are the same with it as without it.
    const answer = (await bench().openIn.targets()) as OpenInAnswer;
    assert.ok(!answer.places.some((place) => (place.id as string) === 'changeJason' || (place.id as string) === 'setup'));
    assert.equal(answer.default, 'claude');
});

test('Change Jason itself runs jason session open --setup with no working folder, after one read of the preference, and is never kept as the default', async () => {
    const b = bench();
    assert.equal(await b.openIn.changeJason(), null);
    assert.deepEqual(sessions(b), [{ args: ['session', 'open', '--setup'], cwd: undefined }]);
    assert.deepEqual(
        b.calls.filter((call) => call.args[0] !== 'session').map((call) => call.args),
        [['preference', 'list']],
        'the preference is read once, at the click, and nothing else is asked of the command line',
    );
    assert.equal(b.inUseReads(), 1);
    assert.deepEqual(b.kept, []);
    assert.deepEqual(b.logged, ['open_in_change_jason:preference:0']);
});

test('the menu is built with exactly the processes it started before: neither the preference nor the program In use is read until the click', async () => {
    for (const campaign of [undefined, 'cmp_01ABC']) {
        const b = bench({ preference: preferences(null), inUse: 'codex' });
        const answer = (await b.openIn.targets(campaign)) as OpenInAnswer;
        assert.equal(answer.changeJason, true);
        assert.deepEqual(
            b.calls.map((call) => call.args),
            campaign === undefined ? [] : [['campaign', 'get', campaign]],
            `the menu of ${campaign ?? 'the harness root'}`,
        );
        assert.equal(b.inUseReads(), 0);
    }
});

test('with no preference stated, the program the Models page marks In use is named: Codex in use opens Codex, Claude Code in use opens Claude Code', async () => {
    for (const [word, flag, logged] of [
        ['codex', 'codex', 'in_use_codex'],
        ['claude_code', 'claude_code', 'in_use_claude'],
    ] as const) {
        const b = bench({ preference: preferences(null), inUse: word });
        assert.equal(await b.openIn.changeJason(), null, word);
        assert.deepEqual(sessions(b).map((call) => call.args), [['session', 'open', '--setup', '--host', flag]], word);
        assert.deepEqual(b.logged, [`open_in_change_jason:${logged}:0`], word);
    }
});

test('a preference the person stated wins whatever is in use: the command is run with no host, so the command line chooses by it', async () => {
    for (const stated of ['claude_code', 'codex', 'gemini_cli', 'no_such_host']) {
        for (const word of ['codex', 'claude_code', null]) {
            const b = bench({ preference: preferences(stated), inUse: word });
            assert.equal(await b.openIn.changeJason(), null, `${stated} ${word}`);
            assert.deepEqual(sessions(b).map((call) => call.args), [['session', 'open', '--setup']], `${stated} ${word}`);
            assert.deepEqual(b.logged, ['open_in_change_jason:preference:0']);
        }
    }
    // A stated value of a shape the desktop does not read is still the person's word.
    const odd = bench({ preference: { exit: 0, stdout: JSON.stringify({ items: [{ key: 'agent.interactive_host', value: 'codex' }] }), stderr: '' }, inUse: 'codex' });
    await odd.openIn.changeJason();
    assert.deepEqual(sessions(odd).map((call) => call.args), [['session', 'open', '--setup']]);
});

test('a preference that cannot be read, a program In use that cannot be read or names none: the command is run as before, with no host', async () => {
    const unread: Answer[] = [
        { exit: 3, stdout: '', stderr: 'No runtime answered.' },
        { exit: 1, stdout: JSON.stringify({ error: { code: 'internal', message: 'x', retryable: true } }), stderr: '' },
        { exit: 0, stdout: 'not json', stderr: '' },
        { exit: 0, stdout: '{"items":"none"}', stderr: '' },
    ];
    for (const preference of unread) {
        const b = bench({ preference, inUse: 'codex' });
        assert.equal(await b.openIn.changeJason(), null, preference.stdout);
        assert.deepEqual(sessions(b).map((call) => call.args), [['session', 'open', '--setup']], preference.stdout);
    }
    for (const inUse of [null, new Error('the runtime could not be asked')]) {
        const b = bench({ preference: preferences(null), inUse });
        assert.equal(await b.openIn.changeJason(), null, String(inUse));
        assert.deepEqual(sessions(b).map((call) => call.args), [['session', 'open', '--setup']], String(inUse));
        assert.deepEqual(b.logged, ['open_in_change_jason:preference:0']);
    }
});

test('every host word the program In use can name is a host session open takes, or the command is run as before', () => {
    const both = { claude: true, codex: true };
    // Named with --host, by the words of the command line's own host table.
    assert.deepEqual(changeJasonFlags(inUseHost('claude_code', both)), ['--setup', '--host', 'claude_code']);
    assert.deepEqual(changeJasonFlags(inUseHost('codex', both)), ['--setup', '--host', 'codex']);
    // Every other word a profile's host can carry, a word of no host, and none: no host, the command line's own rule.
    for (const word of [...Object.values(hostWords), 'no_such_host', '', null]) {
        if (word === 'claude_code' || word === 'codex') continue;
        assert.equal(inUseHost(word, both), null, String(word));
    }
    assert.deepEqual(new Set(Object.values(hostWords)), new Set(['claude_code', 'codex', 'gemini_cli', 'antigravity_cli', 'cursor_cli', 'copilot_cli']), 'a new host word is given a row here');
    // A program In use that the desktop does not find here is not named, as the card's In use needs it found.
    assert.equal(inUseHost('codex', { claude: true, codex: false }), null);
    assert.equal(inUseHost('claude_code', { claude: false, codex: true }), null);
});

test('a program In use that is not installed is not named: the command is run as before, with no host', async () => {
    const b = bench({ hosts: { claude: true, codex: false }, preference: preferences(null), inUse: 'codex' });
    assert.equal(await b.openIn.changeJason(), null);
    assert.deepEqual(sessions(b).map((call) => call.args), [['session', 'open', '--setup']]);
});

test('the two reads land in either order and the outcome is the same', async () => {
    for (const [preferenceTurns, inUseTurns] of [
        [0, 5],
        [5, 0],
        [2, 2],
    ] as const) {
        for (const [preference, expected] of [
            [preferences(null), ['session', 'open', '--setup', '--host', 'codex']],
            [preferences('claude_code'), ['session', 'open', '--setup']],
        ] as const) {
            const b = bench({ preference, inUse: 'codex', preferenceTurns, inUseTurns });
            assert.equal(await b.openIn.changeJason(), null);
            assert.deepEqual(sessions(b).map((call) => call.args), [expected], `${preferenceTurns}/${inUseTurns}`);
        }
    }
});

test('a host In use the command line does not find takes the same fallback: the other host the desktop found, never the one tried', async () => {
    for (const code of hostUnchosen) {
        const b = bench({ preference: preferences(null), inUse: 'codex', answers: [refusal(code), opened] });
        assert.equal(await b.openIn.changeJason(), null, code);
        assert.deepEqual(
            sessions(b).map((call) => call.args),
            [['session', 'open', '--setup', '--host', 'codex'], ['session', 'open', '--setup', '--host', 'claude_code']],
            code,
        );
        assert.deepEqual(b.logged, ['open_in_change_jason:desktop_claude:0'], code);
        assert.deepEqual(b.calls.filter((call) => call.args[0] === 'preference').length, 1, 'the click\'s one read');
    }
    // Codex In use, not found by the command line, and no other host here: said as the two the entry needs.
    const alone = bench({ hosts: { claude: false, codex: true }, preference: preferences(null), inUse: 'codex', answers: [refusal('host_not_found')] });
    assert.deepEqual(await alone.openIn.changeJason(), { refused: changeJasonNoHost });
    assert.equal(sessions(alone).length, 1);
    assert.deepEqual(alone.logged, ['open_in_change_jason_refused:in_use_codex:1:host_not_found']);
});

test('only Codex installed and nothing stated or in use: the command line tries Claude Code, and the desktop runs it once more on Codex', async () => {
    const b = bench({ hosts: { claude: false, codex: true }, preference: preferences(null), inUse: null, answers: [refusal('host_not_found'), opened] });
    assert.equal(await b.openIn.changeJason(), null);
    assert.deepEqual(sessions(b).map((call) => call.args), [['session', 'open', '--setup'], ['session', 'open', '--setup', '--host', 'codex']]);
    assert.deepEqual(b.logged, ['open_in_change_jason:desktop_codex:0']);
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
    assert.deepEqual(both.calls.filter((call) => call.args[0] === 'preference').map((call) => call.args), [['preference', 'list']], 'read once, at the click, which also says which host was tried');
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

test('a preference it could not read, or a word it does not know, tried no host: the second run takes Claude Code first, and the preference is not read again', async () => {
    for (const code of ['host_unknown', 'preference_unreadable']) {
        const b = bench({ answers: [refusal(code), opened] });
        assert.equal(await b.openIn.changeJason(), null, code);
        assert.deepEqual(sessions(b).at(-1)!.args, ['session', 'open', '--setup', '--host', 'claude_code'], code);
        assert.equal(b.calls.filter((call) => call.args[0] === 'preference').length, 1, code);
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

test('in a run that does not offer it, Change Jason itself is refused and nothing is run', async () => {
    for (const b of [bench({ offered: false })]) {
        assert.deepEqual(await b.openIn.changeJason(), { refused: changeJasonUnoffered });
        assert.deepEqual(b.calls, []);
        assert.equal(b.inUseReads(), 0);
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
    assert.match(main, /changeJason: \{\s+flags: changeJasonFlags,/);
    // The program In use is read by the routine the Models page's cards are read by, never a second reader.
    assert.match(main, /inUse: \(\) => setup\.rolesHost\(\),/);
    // Whatever a page sends with it is never read.
    const b = bench();
    const act = b.openIn.changeJason.bind(b.openIn) as (...args: unknown[]) => Promise<unknown>;
    assert.equal(await act('--here', 'C:\\Elsewhere', { cwd: '/tmp' }), null);
    assert.deepEqual(sessions(b), [{ args: ['session', 'open', '--setup'], cwd: undefined }]);
});
