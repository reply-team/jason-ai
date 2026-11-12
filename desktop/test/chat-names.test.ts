// A conversation's name, end to end against stub hosts and a stub jason: a person renames a conversation and nothing
// replaces that name afterwards, and a set-up chat is named once, after its first turn ended well, by its provider's
// fastest model in a run with no tool, kept only when the answer is a short clean name.

import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent, type ChatOptions } from '../src/chat.ts';
import { readFence } from '../src/chat-deny.ts';
import { readConversations } from '../src/chat-conversations.ts';
import { quoted } from '../src/chat-header.ts';
import { namingInstruction, type NamingOutcome } from '../src/chat-naming.ts';
import { bench, outsideStandIn, placeSandboxFiles, type Bench, type Recorded } from './harness.ts';

const fence = readFence(join(import.meta.dirname, '..'));

interface Rig {
    b: Bench;
    chat: Chat;
    events: Map<string, ChatEvent[]>;
    outcomes: NamingOutcome[];
    again(): Chat;
    turn(chatId: string, text: string, options: unknown): Promise<ChatEvent[]>;
    /**
     * A turn held by its host after the host has spoken: `act` runs once the page has heard it, while the turn is certainly
     * still running, then the turn goes on through `after` and its events are returned. No clock decides the order.
     */
    during(chatId: string, text: string, options: unknown, after: unknown[], act: () => void | Promise<void>): Promise<ChatEvent[]>;
    /** Every naming run started, by its host. */
    namings(): Recorded[];
    jason(): string[];
    campaigns: string;
}

function rig(t: { after(fn: () => void | Promise<void>): void }, options: Partial<ChatOptions> = {}, keys: Record<string, string> = {}, env: Record<string, string> = {}): Rig {
    const removals: (() => void)[] = [];
    // The program runner reads Claude Code's version with the bench's environment as it is made.
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude', 'codex'], env });
    placeSandboxFiles(b);
    const campaigns = join(b.root, 'campaigns.json');
    writeFileSync(campaigns, '[]');
    b.env.STUB_CAMPAIGNS_FILE = campaigns;
    const events = new Map<string, ChatEvent[]>();
    const outcomes: NamingOutcome[] = [];
    const chats: Chat[] = [];
    let gates = 0;
    const waiters: { chatId: string; take: (event: ChatEvent) => boolean; resolve: () => void }[] = [];
    const make = () => {
        const chat = new Chat({
            paths: b.paths,
            env: b.env,
            bundle: join(b.root, 'bundle'),
            dataNamed: true,
            bundleVersion: null,
            cli: b.cli,
            programs: b.programs,
            keyFor: (id) => keys[id],
            fence: () => fence,
            sandboxOutside: outsideStandIn,
            emit(chatId, event) {
                events.set(chatId, [...(events.get(chatId) ?? []), event]);
                for (const waiter of [...waiters]) {
                    if (waiter.chatId === chatId && waiter.take(event)) {
                        waiters.splice(waiters.indexOf(waiter), 1);
                        waiter.resolve();
                    }
                }
            },
            named: (outcome) => void outcomes.push(outcome),
            ...options,
        });
        chats.push(chat);
        return chat;
    };
    const chat = make();
    t.after(async () => {
        for (const each of chats) each.closeAll();
        for (const each of chats) await each.idle();
        for (const remove of removals) remove();
    });
    return {
        b,
        chat,
        events,
        outcomes,
        campaigns,
        again: make,
        async turn(chatId, text, sent) {
            const from = events.get(chatId)?.length ?? 0;
            const done = new Promise<void>((resolve) => waiters.push({ chatId, take: (event) => event.kind === 'done', resolve }));
            await chat.send(chatId, text, sent);
            await done;
            return (events.get(chatId) ?? []).slice(from);
        },
        async during(chatId, text, sent, after, act) {
            const gate = join(b.root, `gate-${++gates}`);
            b.env.STUB_HOST_PLAN = JSON.stringify([{ hold: gate }, ...after]);
            const heard = new Promise<void>((resolve) => waiters.push({ chatId, take: (event) => event.kind === 'text', resolve }));
            const turn = this.turn(chatId, text, sent);
            await heard;
            try {
                await act();
            } finally {
                writeFileSync(gate, '');
            }
            return turn;
        },
        namings: () => b.record().filter((each) => each.name.endsWith('-naming')),
        jason: () => b.record().filter((each) => each.name === 'jason').map((each) => each.args.join(' ')),
    };
}

const naming = (r: Rig, answer: Record<string, unknown>) => {
    r.b.env.STUB_NAMING = JSON.stringify(answer);
};
/** A file Claude Code's stand-in reads its version from at each start, outside the bench, so a test names it first. */
function versionFile(t: { after(fn: () => void): void }, version: string): string {
    const folder = mkdtempSync(join(tmpdir(), 'jason-version-'));
    t.after(() => rmSync(folder, { recursive: true, force: true }));
    const file = join(folder, 'version.json');
    writeFileSync(file, JSON.stringify({ stdout: `${version} (Claude Code)` }));
    return file;
}
const titleOf = (r: Rig, id: string) => r.chat.list().find((each) => each.id === id)?.title;
const keptOf = (r: Rig, id: string) => readConversations(r.b.paths.conversations).kept.find((each) => each.id === id);

const claudeNaming = (model: string[], effort: string[]) => [
    '-p',
    '--output-format',
    'stream-json',
    '--verbose',
    '--tools',
    '',
    '--disallowedTools',
    'mcp__*',
    '--strict-mcp-config',
    '--setting-sources',
    'project',
    '--no-session-persistence',
    '--max-turns',
    '1',
    '--system-prompt',
    namingInstruction,
    ...model,
    ...effort,
];

const codexNaming = [
    'exec',
    '--json',
    '--sandbox',
    'read-only',
    '--skip-git-repo-check',
    '--ephemeral',
    '--ignore-user-config',
    '-c',
    'features.shell_tool=false',
    '-c',
    'web_search=disabled',
    '-c',
    'features.multi_agent=false',
    '-c',
    'features.apps=false',
    '-c',
    'features.view_image=false',
    '-',
];

// N1.1: a person renames a conversation.

test('a person renames a kept conversation from its header: one clean line within the cap, kept with the mark and said to the page', async (t) => {
    const r = rig(t);
    const setup = await r.chat.create();
    await r.turn(setup.id, 'Help me start a campaign', { host: 'claude' });
    await r.chat.idle();
    const renamed = r.chat.rename(setup.id, '  Spring \n plan\u202e ');
    assert.equal(renamed.title, 'Spring plan');
    assert.equal(titleOf(r, setup.id), 'Spring plan');
    assert.deepEqual(r.events.get(setup.id)?.at(-1), { kind: 'renamed', title: 'Spring plan' });
    assert.equal(keptOf(r, setup.id)?.named_by_person, true);
    assert.equal(r.again().list().find((each) => each.id === setup.id)?.title, 'Spring plan', 'read back after a restart');
    assert.equal(r.chat.rename(setup.id, 'x'.repeat(200)).title, 'x'.repeat(60));
});

test('a rename is refused for a conversation the desktop does not keep, one never sent to, and a name with nothing that shows', async (t) => {
    const r = rig(t);
    assert.throws(() => r.chat.rename('conv_01JD0000000000000000000000', 'A'), { message: 'That conversation is not one this desktop keeps.' });
    const fresh = await r.chat.create();
    assert.throws(() => r.chat.rename(fresh.id, 'A name'), { message: 'Send this chat a message first; then it can be renamed.' });
    assert.equal(existsSync(r.b.paths.conversations), false, 'nothing written');
    await r.turn(fresh.id, 'Hello', { host: 'claude' });
    for (const blank of ['', '   ', '\u200b\u202e', 42, null]) {
        assert.throws(() => r.chat.rename(fresh.id, blank), { message: 'A name needs at least one character that shows.' }, JSON.stringify(blank));
    }
    assert.throws(() => r.chat.rename(fresh.id, 'x'.repeat(10_001)), { message: 'That name is too long. Keep it to a short line.' });
    assert.equal(keptOf(r, fresh.id)?.named_by_person, undefined);
});

test('a campaign conversation a person renamed keeps that name across its turns, and the campaign\'s own name is not changed', async (t) => {
    const r = rig(t);
    const made = await r.chat.create('cmp_01ABC');
    assert.equal(made.title, 'Spring');
    await r.turn(made.id, 'How is it going?', { host: 'claude' });
    r.chat.rename(made.id, 'My spring list');
    r.b.env.STUB_CAMPAIGN_NAME = 'Spring renamed in the runtime';
    await r.turn(made.id, 'And now?', { host: 'claude' });
    assert.equal(titleOf(r, made.id), 'My spring list');
    assert.ok(r.jason().every((asked) => !asked.startsWith('campaign update')), 'the runtime is never asked to change the campaign');
});

test('a set-up chat a person renamed keeps that name when its turn hands it over to a campaign, and when the person picks one', async (t) => {
    const r = rig(t);
    const setup = await r.chat.create();
    const said = await r.during(setup.id, 'Make one', { host: 'claude' }, [{ campaign: { id: 'cmp_ONE', name: 'One' } }], () => {
        r.chat.rename(setup.id, 'Named while it ran');
    });
    assert.deepEqual(said.filter((event) => event.kind === 'attached'), [{ kind: 'attached', campaignId: 'cmp_ONE', title: 'Named while it ran' }]);
    assert.equal(titleOf(r, setup.id), 'Named while it ran');
    assert.equal(keptOf(r, setup.id)?.campaign_id, 'cmp_ONE');

    r.b.env.STUB_HOST_PLAN = JSON.stringify([{ campaign: { id: 'cmp_TWO', name: 'Two' } }, { campaign: { id: 'cmp_THREE', name: 'Three' } }]);
    const two = await r.chat.create();
    await r.turn(two.id, 'Make two', { host: 'claude' });
    r.chat.rename(two.id, 'Picked by hand');
    assert.equal((await r.chat.attach(two.id, 'cmp_TWO')).title, 'Picked by hand');
});

// N1.2: a set-up chat named by its provider's fastest model.

test('a set-up chat is named once after its first turn ended well, by Claude Code\'s fastest model with no tool, and the page hears the title', async (t) => {
    const r = rig(t);
    r.b.env.CLAUDE_CODE_EFFORT_LEVEL = 'max';
    naming(r, { say: 'Spring launch for dentists', touch: true });
    const setup = await r.chat.create();
    const words = `Help me reach dentists in Ohio ${'x'.repeat(1500)}`;
    await r.turn(setup.id, words, { host: 'claude' });
    await r.chat.idle();
    assert.equal(titleOf(r, setup.id), 'Spring launch for dentists');
    assert.deepEqual(r.events.get(setup.id)?.at(-1), { kind: 'renamed', title: 'Spring launch for dentists' });
    assert.equal(keptOf(r, setup.id)?.named_by_person, undefined, 'a name the desktop asked for carries no person\'s mark');
    assert.deepEqual(r.outcomes, ['named']);
    const [run, ...more] = r.namings();
    assert.equal(more.length, 0, 'one run');
    assert.deepEqual(run?.args, claudeNaming(['--model', 'haiku'], ['--effort', 'low']));
    assert.equal(run?.stdin, quoted(Array.from(words).slice(0, 1000).join('')), 'the first thousand characters, as one JSON string, on standard input');
    assert.ok(!run?.args.some((arg) => arg.includes('dentists')), 'never on the command line');
    assert.equal(run?.env.CLAUDE_CODE_EFFORT_LEVEL, undefined, 'an inherited effort is withheld');
    assert.equal(run?.env.JASON_CONFINED, '1', 'the environment a turn has');
    // It ran in an empty folder of the desktop's own under turns/, removed after with what was left in it.
    assert.equal(join(run!.cwd, '..'), r.b.paths.turns);
    assert.deepEqual(readdirSync(r.b.paths.turns), []);
    // Once: a second turn asks nothing more.
    await r.turn(setup.id, 'Next', { host: 'claude' });
    await r.chat.idle();
    assert.equal(r.namings().length, 1);
});

test('the naming run offers Haiku no effort below the version that makes it take one', async (t) => {
    const version = versionFile(t, '2.1.292');
    const r = rig(t, {}, {}, { STUB_CLAUDE_VERSION_FILE: version });
    naming(r, { say: 'Dentists in Ohio' });
    const setup = await r.chat.create();
    await r.turn(setup.id, 'Help me reach dentists', { host: 'claude' });
    await r.chat.idle();
    assert.deepEqual(r.namings()[0]?.args, claudeNaming(['--model', 'haiku'], []));
    assert.equal(titleOf(r, setup.id), 'Dentists in Ohio');
});

test('a Claude Code that fell below the floor after the turn runs no naming run', async (t) => {
    const version = versionFile(t, '2.1.300');
    const r = rig(t, {}, {}, { STUB_CLAUDE_VERSION_FILE: version });
    naming(r, { say: 'Dentists in Ohio' });
    const setup = await r.chat.create();
    await r.during(setup.id, 'Help me reach dentists', { host: 'claude' }, [], () => {
        writeFileSync(version, JSON.stringify({ stdout: '2.1.100 (Claude Code)\n' }));
    });
    await r.chat.idle();
    assert.deepEqual(r.namings(), []);
    assert.deepEqual(r.outcomes, ['held']);
    assert.equal(titleOf(r, setup.id), 'Help me reach dentists');
});

test('what comes back is kept only as two to five plain words; anything else keeps the first words', async (t) => {
    const r = rig(t);
    const cases: [Record<string, unknown>, string | null, NamingOutcome][] = [
        [{ say: '"Dentist outreach."' }, 'Dentist outreach', 'named'],
        [{ say: '\u201cZahnärzte in Köln\u201d' }, 'Zahnärzte in Köln', 'named'],
        [{ say: 'R&D leads, Q3' }, null, 'refused'],
        [{ say: 'Q3 R&D leads' }, 'Q3 R&D leads', 'named'],
        [{ say: 'Dentists' }, null, 'refused'],
        [{ say: 'one two three four five six' }, null, 'refused'],
        [{ say: 'See https://example.com now' }, null, 'refused'],
        [{ say: 'Read /etc/passwd now' }, null, 'refused'],
        [{ say: 'Use `rm` here' }, null, 'refused'],
        [{ say: 'Two\nlines here' }, 'Two lines here', 'named'],
        [{ say: `${'a'.repeat(30)} ${'b'.repeat(30)}` }, null, 'refused'],
        [{ say: 'Dentist outreach plan', use: true }, null, 'refused'],
        [{ error: 'Credit balance is too low' }, null, 'failed'],
        [{}, null, 'refused'],
    ];
    for (const [answer, name, outcome] of cases) {
        naming(r, answer);
        const setup = await r.chat.create();
        await r.turn(setup.id, 'Help me reach dentists in Ohio', { host: 'claude' });
        await r.chat.idle();
        assert.equal(titleOf(r, setup.id), name ?? 'Help me reach dentists in Ohio', JSON.stringify(answer));
        assert.equal(r.outcomes.at(-1), outcome, JSON.stringify(answer));
    }
});

test('no naming run for a chat a person named, a turn that failed or was stopped, a hand-over, or a campaign\'s conversation', async (t) => {
    const r = rig(t);
    naming(r, { say: 'Dentist outreach plan' });
    // Named by the person while the first turn ran.
    const named = await r.chat.create();
    await r.during(named.id, 'Help me', { host: 'claude' }, [], () => {
        r.chat.rename(named.id, 'Mine');
    });
    // A first turn that failed, then a second that ended well: still no run, since it is no longer the first.
    r.b.env.STUB_HOST_PLAN = JSON.stringify([{ end: 'error', text: 'no' }]);
    const failed = await r.chat.create();
    await r.turn(failed.id, 'Help me', { host: 'claude' });
    r.b.env.STUB_HOST_PLAN = '[]';
    await r.turn(failed.id, 'Again', { host: 'claude' });
    // A stopped first turn.
    const stopped = await r.chat.create();
    await r.during(stopped.id, 'Help me', { host: 'claude' }, [], () => {
        r.chat.stop(stopped.id);
    });
    // A first turn that handed the chat over to the campaign it made.
    r.b.env.STUB_HOST_PLAN = JSON.stringify([{ campaign: { id: 'cmp_ONE', name: 'One' } }]);
    const handed = await r.chat.create();
    await r.turn(handed.id, 'Make one', { host: 'claude' });
    // A campaign's conversation.
    r.b.env.STUB_HOST_PLAN = '[]';
    const campaign = await r.chat.create('cmp_01ABC');
    await r.turn(campaign.id, 'How is it doing?', { host: 'claude' });
    await r.chat.idle();
    assert.deepEqual(r.namings(), []);
    assert.deepEqual(
        [named, failed, stopped, handed, campaign].map((each) => titleOf(r, each.id)),
        ['Mine', 'Help me', 'Help me', 'One', 'Spring'],
    );
});

test('a name the desktop asked for gives way to a person\'s rename, and to the campaign a later turn hands the chat over to', async (t) => {
    const r = rig(t);
    naming(r, { say: 'Dentist outreach plan' });
    const first = await r.chat.create();
    await r.turn(first.id, 'Help me', { host: 'claude' });
    await r.chat.idle();
    assert.equal(r.chat.rename(first.id, 'Mine after all').title, 'Mine after all');
    const second = await r.chat.create();
    await r.turn(second.id, 'Help me', { host: 'claude' });
    await r.chat.idle();
    assert.equal(titleOf(r, second.id), 'Dentist outreach plan');
    r.b.env.STUB_HOST_PLAN = JSON.stringify([{ campaign: { id: 'cmp_ONE', name: 'One' } }]);
    await r.turn(second.id, 'Make it', { host: 'claude' });
    assert.equal(titleOf(r, second.id), 'One');
});

test('a Codex set-up chat is named with every tool the configuration names turned off, and any step refuses the name', async (t) => {
    const r = rig(t);
    naming(r, { say: 'Dentist outreach plan' });
    const setup = await r.chat.create();
    await r.turn(setup.id, 'Help me reach dentists', { host: 'codex' });
    await r.chat.idle();
    const [run] = r.namings();
    assert.equal(run?.name, 'codex-naming');
    assert.deepEqual(run?.args, codexNaming);
    assert.equal(run?.stdin, `${namingInstruction}\n\n${quoted('Help me reach dentists')}`);
    assert.equal(titleOf(r, setup.id), 'Dentist outreach plan');

    naming(r, { say: 'Dentist outreach plan', use: true });
    const stepped = await r.chat.create();
    await r.turn(stepped.id, 'Help me reach dentists', { host: 'codex' });
    await r.chat.idle();
    assert.equal(titleOf(r, stepped.id), 'Help me reach dentists');
    assert.deepEqual(r.outcomes, ['named', 'refused']);
});

test('a keyed set-up chat is named on its provider\'s fast model, with the key in the environment alone and nothing kept', async (t) => {
    const key = 'sk-test-0123456789abcdef0123';
    const r = rig(t, {}, { deepseek: key, moonshot: key, 'claude-api-key': key });
    naming(r, { say: 'Dentist outreach plan' });
    for (const host of ['deepseek', 'moonshot', 'claude-api-key']) {
        const setup = await r.chat.create();
        await r.turn(setup.id, 'Help me', { host });
        await r.chat.idle();
        assert.equal(titleOf(r, setup.id), 'Dentist outreach plan', host);
    }
    const [deepseek, moonshot, anthropic] = r.namings();
    assert.deepEqual(deepseek?.args, claudeNaming([], ['--effort', 'low']));
    assert.equal(deepseek?.env.ANTHROPIC_MODEL, 'deepseek-flash');
    assert.equal(deepseek?.env.ANTHROPIC_DEFAULT_HAIKU_MODEL, 'deepseek-flash');
    assert.equal(deepseek?.env.ANTHROPIC_AUTH_TOKEN, key);
    assert.equal(deepseek?.env.CLAUDE_CONFIG_DIR, join(r.b.paths.claudeKeyed, 'deepseek'));
    assert.deepEqual(moonshot?.args, claudeNaming([], []));
    assert.equal(moonshot?.env.ANTHROPIC_MODEL, 'kimi-k2.6');
    assert.deepEqual(anthropic?.args, claudeNaming(['--model', 'haiku'], ['--effort', 'low']));
    assert.equal(anthropic?.env.ANTHROPIC_API_KEY, key);
    for (const run of r.namings()) {
        assert.ok(!run.args.some((arg) => arg.includes(key)) && !run.stdin.includes(key), 'the key is never on argv or stdin');
    }
});

test('naming runs go one at a time across the desktop, and one past its bound keeps the first words', async (t) => {
    const r = rig(t, { namingLimitMs: 1500 });
    const log = join(r.b.root, 'naming.log');
    r.b.env.STUB_NAMING_LOG = log;
    naming(r, { say: 'Dentist outreach plan', sleep: 300 });
    const one = await r.chat.create();
    const two = await r.chat.create();
    await Promise.all([r.turn(one.id, 'First chat', { host: 'claude' }), r.turn(two.id, 'Second chat', { host: 'claude' })]);
    await r.chat.idle();
    const lines = readFileSync(log, 'utf8').trim().split('\n').map((line) => line.split(' ')[0]);
    assert.deepEqual(lines, ['start', 'end', 'start', 'end']);

    naming(r, { say: 'Dentist outreach plan', sleep: 5000 });
    const slow = await r.chat.create();
    await r.turn(slow.id, 'Slow chat', { host: 'claude' });
    await r.chat.idle();
    assert.equal(titleOf(r, slow.id), 'Slow chat');
    assert.equal(r.outcomes.at(-1), 'timed_out');
    // The run ended at its bound is ended by the system a moment later; its folder goes with the test's.
    await new Promise((resolve) => setTimeout(resolve, 1500));
});

test('deny by default: a Codex item that is not the agent\'s message or its reasoning, and a Claude Code block that is not text or thinking, refuses the name, known type or not', async (t) => {
    const r = rig(t);
    const say = 'Dentist outreach plan';
    const cases: [string, Record<string, unknown>, NamingOutcome][] = [
        ['codex', { item: 'collab_tool_call', say }, 'refused'],
        ['codex', { item: 'image_view', say }, 'refused'],
        ['codex', { item: 'a_tool_a_later_codex_adds', say }, 'refused'],
        ['codex', { item: 'reasoning', say }, 'named'],
        ['claude', { block: 'server_tool_use', say }, 'refused'],
        ['claude', { block: 'a_block_a_later_claude_code_adds', say }, 'refused'],
        ['claude', { block: 'thinking', say }, 'named'],
    ];
    for (const [host, answer, outcome] of cases) {
        naming(r, answer);
        const setup = await r.chat.create();
        await r.turn(setup.id, 'Help me reach dentists in Ohio', { host });
        await r.chat.idle();
        assert.equal(r.outcomes.at(-1), outcome, `${host} ${JSON.stringify(answer)}`);
        assert.equal(titleOf(r, setup.id), outcome === 'named' ? say : 'Help me reach dentists in Ohio', `${host} ${JSON.stringify(answer)}`);
    }
});

test('closing the chat ends a running naming run as a stop ends a turn\'s host, drops one that waits, and leaves nothing of either', { timeout: 60_000 }, async (t) => {
    const r = rig(t);
    const ids = join(r.b.root, 'stuck.json');
    const alive = (id: number) => {
        try {
            process.kill(id, 0);
            return true;
        } catch {
            return false;
        }
    };
    t.after(() => {
        if (!existsSync(ids)) return;
        for (const id of JSON.parse(readFileSync(ids, 'utf8')) as number[]) if (alive(id)) process.kill(id);
    });
    naming(r, { stuck: ids });
    const one = await r.chat.create();
    const two = await r.chat.create();
    await Promise.all([r.turn(one.id, 'First chat', { host: 'claude' }), r.turn(two.id, 'Second chat', { host: 'claude' })]);
    const until = async (done: () => boolean, ms: number) => {
        const end = Date.now() + ms;
        while (!done() && Date.now() < end) await new Promise((resolve) => setTimeout(resolve, 100));
    };
    await until(() => existsSync(ids), 10_000);
    assert.ok(existsSync(ids), 'the stuck run started');
    const started = JSON.parse(readFileSync(ids, 'utf8')) as number[];
    r.chat.closeAll();
    await r.chat.idle();
    await until(() => !started.some(alive), 8000);
    assert.deepEqual(started.filter(alive), [], 'the run and what it started are ended, past the first signal');
    await until(() => readdirSync(r.b.paths.turns).length === 0, 8000);
    assert.deepEqual(readdirSync(r.b.paths.turns), [], 'its folder is gone');
    assert.equal(r.namings().length, 1, 'the run that waited never started');
    assert.deepEqual(r.outcomes, ['stopped']);
    assert.deepEqual([titleOf(r, one.id), titleOf(r, two.id)], ['First chat', 'Second chat']);
});

test('a name that draws nothing is no name: a filler-only answer keeps the first words, and a filler-only rename is refused', async (t) => {
    const r = rig(t);
    const fillers = ['\u3164 \u3164', '\u115f\u1160 \uffa0', '\u034f\ufe0f \u2060\u3164', '\u0301 \u0302'];
    for (const filler of fillers) {
        naming(r, { say: filler });
        const setup = await r.chat.create();
        await r.turn(setup.id, 'Help me reach dentists in Ohio', { host: 'claude' });
        await r.chat.idle();
        assert.equal(r.outcomes.at(-1), 'refused', JSON.stringify(filler));
        assert.equal(titleOf(r, setup.id), 'Help me reach dentists in Ohio', JSON.stringify(filler));
        // A person's rename is refused only when nothing is left once the title's dropped characters are: marks alone are
        // kept there, as a person typed them, and refused only as a model's answer, whose words begin with a letter or a digit.
        if (!filler.startsWith('\u0301')) assert.throws(() => r.chat.rename(setup.id, filler), { message: 'A name needs at least one character that shows.' }, JSON.stringify(filler));
    }
    // A word begins with a letter or a digit; a filler between the words of a real name is dropped.
    naming(r, { say: '\u0301Dentist outreach' });
    const marked = await r.chat.create();
    await r.turn(marked.id, 'Help me reach dentists in Ohio', { host: 'claude' });
    await r.chat.idle();
    assert.equal(r.outcomes.at(-1), 'refused');
    naming(r, { say: 'Dentist\u3164 outreach\u2060 plan' });
    const kept = await r.chat.create();
    await r.turn(kept.id, 'Help me reach dentists in Ohio', { host: 'claude' });
    await r.chat.idle();
    assert.equal(titleOf(r, kept.id), 'Dentist outreach plan');
    assert.equal(r.chat.rename(kept.id, 'Mine\u3164 \u115fnow').title, 'Mine now');
});

/** Resolves once the condition holds, or after `ms`. */
async function until(done: () => boolean, ms = 10_000): Promise<void> {
    const end = Date.now() + ms;
    while (!done() && Date.now() < end) await new Promise((resolve) => setTimeout(resolve, 50));
}

test('a rename, a removal or a hand-over while the naming run is out stands: the late name replaces nothing', async (t) => {
    const r = rig(t);
    const log = join(r.b.root, 'naming.log');
    r.b.env.STUB_NAMING_LOG = log;
    const started = (count: number) => () => existsSync(log) && readFileSync(log, 'utf8').split('\n').filter((line) => line.startsWith('start')).length >= count;
    // Each naming run is held at a gate of its own until the test has done what it tests, so no clock decides the order.
    let gates = 0;
    const held = () => {
        const gate = join(r.b.root, `naming-gate-${++gates}`);
        naming(r, { say: 'Dentist outreach plan', hold: gate });
        return () => writeFileSync(gate, '');
    };

    let release = held();
    const renamed = await r.chat.create();
    await r.turn(renamed.id, 'First chat', { host: 'claude' });
    await until(started(1));
    assert.ok(started(1)(), 'the naming run started');
    r.chat.rename(renamed.id, 'Mine while it ran');
    release();
    await r.chat.idle();
    assert.equal(titleOf(r, renamed.id), 'Mine while it ran');
    assert.equal(r.outcomes.at(-1), 'overtaken');

    release = held();
    const removed = await r.chat.create();
    await r.turn(removed.id, 'Second chat', { host: 'claude' });
    await until(started(2));
    assert.ok(started(2)(), 'the naming run started');
    r.chat.remove(removed.id);
    release();
    await r.chat.idle();
    assert.equal(r.outcomes.at(-1), 'overtaken');
    assert.equal(r.chat.list().some((each) => each.id === removed.id), false);
    assert.equal(existsSync(join(r.b.paths.conversations, `${removed.id}.json`)), false, 'no record written again');

    release = held();
    const handed = await r.chat.create();
    await r.turn(handed.id, 'Third chat', { host: 'claude' });
    await until(started(3));
    assert.ok(started(3)(), 'the naming run started');
    r.b.env.STUB_HOST_PLAN = JSON.stringify([{ campaign: { id: 'cmp_ONE', name: 'One' } }]);
    await r.turn(handed.id, 'Make it', { host: 'claude' });
    release();
    await r.chat.idle();
    assert.equal(r.outcomes.at(-1), 'overtaken');
    assert.equal(titleOf(r, handed.id), 'One');
    assert.ok(!(r.events.get(handed.id) ?? []).some((event) => event.kind === 'renamed'), 'the page never heard the late name');
});

test('a turn sent while the naming run is out is neither held for it nor disturbed by the late name', async (t) => {
    const r = rig(t);
    const gate = join(r.b.root, 'naming-gate');
    naming(r, { say: 'Dentist outreach plan', hold: gate });
    const setup = await r.chat.create();
    await r.turn(setup.id, 'First chat', { host: 'claude' });
    // The naming run is held at its gate for the whole second turn: a turn that waited for it would never end.
    let giveUp: NodeJS.Timeout | undefined;
    const waited = new Promise<never>((_, reject) => {
        giveUp = setTimeout(() => reject(new Error('the turn waited for the naming run')), 30_000);
    });
    let said: ChatEvent[];
    try {
        said = await Promise.race([r.turn(setup.id, 'And next?', { host: 'claude' }), waited]);
    } finally {
        clearTimeout(giveUp);
        writeFileSync(gate, '');
    }
    assert.deepEqual(said.map((event) => event.kind), ['text', 'done']);
    assert.equal(titleOf(r, setup.id), 'First chat', 'not named yet');
    await r.chat.idle();
    assert.deepEqual(r.events.get(setup.id)?.at(-1), { kind: 'renamed', title: 'Dentist outreach plan' });
    const kept = keptOf(r, setup.id);
    assert.equal(kept?.title, 'Dentist outreach plan');
    assert.deepEqual(kept?.entries.filter((entry) => entry.kind === 'user').map((entry) => (entry as { text: string }).text), ['First chat', 'And next?']);
    assert.equal(kept?.entries.at(-1)?.kind, 'text', 'the late name added no entry');
});

test('a keyed Codex naming run has the provider\'s environment as its turn has it, and none of the person\'s', async (t) => {
    const key = 'sk-test-codex-0123456789abcdef';
    const r = rig(t, {}, { 'codex-api-key': key }, { OPENAI_API_KEY: 'sk-inherited-0000000000000000', CODEX_HOME: join(tmpdir(), 'a-person-s-codex-home') });
    naming(r, { say: 'Dentist outreach plan' });
    const setup = await r.chat.create();
    await r.turn(setup.id, 'Help me', { host: 'codex-api-key' });
    await r.chat.idle();
    const [run] = r.namings();
    assert.equal(run?.name, 'codex-naming');
    assert.deepEqual(run?.args, codexNaming);
    assert.equal(run?.env.CODEX_API_KEY, key);
    assert.equal(run?.env.CODEX_HOME, r.b.paths.codexKeyed);
    assert.equal(run?.env.OPENAI_API_KEY, undefined);
    assert.ok(!run?.args.some((arg) => arg.includes(key)) && !run?.stdin.includes(key));
    assert.equal(titleOf(r, setup.id), 'Dentist outreach plan');
});

test('a Claude Code whose version cannot be read after the turn runs no naming run', async (t) => {
    const version = versionFile(t, '2.1.300');
    const r = rig(t, {}, {}, { STUB_CLAUDE_VERSION_FILE: version });
    naming(r, { say: 'Dentists in Ohio' });
    const setup = await r.chat.create();
    await r.during(setup.id, 'Help me reach dentists', { host: 'claude' }, [], () => {
        writeFileSync(version, JSON.stringify({ stdout: 'no version here' }));
    });
    await r.chat.idle();
    assert.deepEqual(r.namings(), []);
    assert.deepEqual(r.outcomes, ['held']);
});

test('a Codex naming run names Codex\'s fast model when the kept list offers it, with low effort when the model takes it', async (t) => {
    const key = 'sk-test-codex-0123456789abcdef';
    const luna = (efforts: ('low' | 'medium' | 'high')[]) => ({ askedAt: '2026-11-11T10:00:00.000Z', models: [{ id: 'gpt-6-orion', label: 'GPT-6 Orion', efforts }, { id: 'gpt-6-luna', label: 'GPT-6 Luna', efforts }] });
    const kept = new Map([
        ['codex', luna(['low', 'medium'])],
        ['codex-api-key', luna(['high'])],
    ]);
    const r = rig(t, { models: () => kept }, { 'codex-api-key': key });
    naming(r, { say: 'Dentist outreach plan' });
    for (const host of ['codex', 'codex-api-key']) {
        const setup = await r.chat.create();
        await r.turn(setup.id, 'Help me', { host });
        await r.chat.idle();
    }
    kept.set('codex', { askedAt: '2026-11-11T10:00:00.000Z', models: [{ id: 'gpt-6-orion', label: 'GPT-6 Orion', efforts: ['low'] }] });
    const unlisted = await r.chat.create();
    await r.turn(unlisted.id, 'Help me', { host: 'codex' });
    await r.chat.idle();
    const [plain, keyed, none] = r.namings();
    const beforeInput = codexNaming.slice(0, -1);
    assert.deepEqual(plain?.args, [...beforeInput, '-m', 'gpt-6-luna', '-c', 'model_reasoning_effort=low', '-']);
    assert.deepEqual(keyed?.args, [...beforeInput, '-m', 'gpt-6-luna', '-']);
    assert.deepEqual(none?.args, codexNaming);
});
