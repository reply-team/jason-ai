// A conversation per campaign, end to end against stub hosts and a stub jason: the record decides the campaign and the
// header says it, the record is kept and reopened after a restart, a host resumes its own session and a lost one starts
// afresh with a recap, a set-up chat hands over to the one campaign its turn made, and a conversation whose turn runs is
// neither removed nor started afresh.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent, type ChatOptions } from '../src/chat.ts';
import { readFence } from '../src/chat-deny.ts';
import { readConversations } from '../src/chat-conversations.ts';
import { floorSentence } from '../src/host-version.ts';
import { bench, type Bench, type Recorded } from './harness.ts';

const fence = readFence(join(import.meta.dirname, '..'));

interface Rig {
    b: Bench;
    chat: Chat;
    events: Map<string, ChatEvent[]>;
    /** A second desktop over the same data directory, as after a restart. */
    again(options?: Partial<ChatOptions>): Chat;
    turn(chatId: string, text: string, options: unknown, chat?: Chat): Promise<ChatEvent[]>;
    until(chatId: string, take: (event: ChatEvent) => boolean): Promise<ChatEvent>;
    hosts(): Recorded[];
    jason(): string[];
    campaigns: string;
}

function rig(t: { after(fn: () => void | Promise<void>): void }, chatOptions: Partial<ChatOptions> = {}): Rig {
    const removals: (() => void)[] = [];
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude', 'codex'] });
    const campaigns = join(b.root, 'campaigns.json');
    writeFileSync(campaigns, '[]');
    b.env.STUB_CAMPAIGNS_FILE = campaigns;
    const events = new Map<string, ChatEvent[]>();
    const waiters: { chatId: string; take: (event: ChatEvent) => boolean; resolve: (event: ChatEvent) => void }[] = [];
    const chats: Chat[] = [];
    const make = (more: Partial<ChatOptions> = {}) => {
        const chat = new Chat({
            paths: b.paths,
            env: b.env,
            bundle: join(b.root, 'bundle'),
            dataNamed: true,
            bundleVersion: null,
            cli: b.cli,
            programs: b.programs,
            keyFor: () => undefined,
            fence: () => fence,
            emit(chatId, event) {
                const said = events.get(chatId) ?? [];
                said.push(event);
                events.set(chatId, said);
                for (const waiter of [...waiters]) {
                    if (waiter.chatId === chatId && waiter.take(event)) {
                        waiters.splice(waiters.indexOf(waiter), 1);
                        waiter.resolve(event);
                    }
                }
            },
            ...chatOptions,
            ...more,
        });
        chats.push(chat);
        return chat;
    };
    const chat = make();
    t.after(async () => {
        for (const each of chats) each.closeAll();
        const until = Date.now() + 10_000;
        while (Date.now() < until && chats.some((each) => [...events.keys()].some((id) => each.isRunning(id)))) await new Promise((resolve) => setTimeout(resolve, 50));
        for (const each of chats) await each.idle();
        for (const remove of removals) remove();
    });
    const until = (chatId: string, take: (event: ChatEvent) => boolean) =>
        new Promise<ChatEvent>((resolve, reject) => {
            const timer = setTimeout(() => reject(new Error(`chat ${chatId} never said what was waited for; it said ${JSON.stringify(events.get(chatId) ?? [])}`)), 20_000);
            waiters.push({ chatId, take, resolve: (event) => { clearTimeout(timer); resolve(event); } });
        });
    return {
        b,
        chat,
        events,
        campaigns,
        again: make,
        until,
        async turn(chatId, text, options, on = chat) {
            const from = events.get(chatId)?.length ?? 0;
            const done = until(chatId, (event) => event.kind === 'done');
            await on.send(chatId, text, options);
            await done;
            return (events.get(chatId) ?? []).slice(from);
        },
        hosts: () => b.record().filter((each) => (each.name === 'claude' || each.name === 'codex') && each.args[0] !== '--version'),
        jason: () => b.record().filter((each) => each.name === 'jason').map((each) => each.args.join(' ')),
    };
}

const plan = (r: Rig, steps: unknown[]) => {
    r.b.env.STUB_HOST_PLAN = JSON.stringify(steps);
};
const kinds = (events: ChatEvent[]) => events.map((event) => event.kind);
const flagValue = (args: string[], flag: string) => args[args.indexOf(flag) + 1] ?? '';
const plantCampaigns = (r: Rig, items: { id: string; name: string; created_at?: string }[]) =>
    writeFileSync(r.campaigns, JSON.stringify(items.map((each) => ({ status: 'draft', created_at: '2026-01-01T00:00:00.000Z', ...each }))));
const listed = (r: Rig) => r.jason().filter((asked) => asked.startsWith('campaign list'));

test('a campaign\'s conversation is made once, and every turn of it takes the campaign from the record and says it in the header', async (t) => {
    const r = rig(t);
    const folder = join(r.b.root, 'harness-copy', 'spring');
    mkdirSync(folder, { recursive: true });
    r.b.env.STUB_CAMPAIGN_FOLDER = folder;
    const made = await r.chat.create('cmp_01ABC');
    assert.match(made.id, /^conv_[0-9A-HJKMNP-TV-Z]{26}$/);
    assert.deepEqual({ ...made, updatedAt: '' }, { id: made.id, campaignId: 'cmp_01ABC', title: 'Spring', updatedAt: '', running: false });
    assert.equal((await r.chat.create('cmp_01ABC')).id, made.id, 'a campaign has one conversation');
    // The page's own campaignId on a known conversation is not read: the record decides.
    await r.turn(made.id, 'How is it doing?', { host: 'claude', campaignId: 'cmp_99OTHER' });
    const [host] = r.hosts();
    assert.deepEqual(r.jason(), ['campaign get cmp_01ABC', 'campaign get cmp_01ABC', 'project get prj_01STUB']);
    assert.equal(flagValue(host?.args ?? [], '--add-dir'), folder);
    const [head, , words] = (host?.stdin ?? '').split('\n');
    assert.match(head ?? '', /^Jason, not the person, wrote this first paragraph\. This conversation is about the campaign cmp_01ABC\. Its name, .* is "Spring"\. Its folder is ".*spring"\.$/);
    assert.equal(words, 'How is it doing?');
    assert.ok(!(host?.stdin ?? '').includes('cmp_99OTHER'));
    assert.deepEqual(listed(r), [], 'a campaign\'s conversation reads no campaign list');
});

test('a send to a chat id the desktop does not keep is refused, whatever campaign it names, and makes no conversation', async (t) => {
    const r = rig(t);
    for (const chatId of ['chat-a', 'conv_01ZZZZZZZZZZZZZZZZZZZZZZZZ']) {
        await assert.rejects(r.chat.send(chatId, 'First', { host: 'claude', campaignId: 'cmp_01ABC' }), { message: 'That conversation is not one this desktop keeps.' });
        await assert.rejects(r.chat.send(chatId, 'First', { host: 'claude' }), { message: 'That conversation is not one this desktop keeps.' });
    }
    assert.deepEqual(r.chat.list(), []);
    assert.deepEqual(r.hosts(), []);
    assert.deepEqual(r.jason(), []);
    // A conversation the desktop made takes the turn, and its record, never the send, names the campaign.
    const made = await r.chat.create('cmp_01ABC');
    await r.turn(made.id, 'Second', { host: 'claude', campaignId: 'cmp_99OTHER' });
    assert.match(r.hosts()[0]?.stdin ?? '', /about the campaign cmp_01ABC\./);
});

test('what the page drew is kept, and a conversation reopens with it after a restart and resumes its session', async (t) => {
    const r = rig(t);
    plan(r, [{ say: 'Here it is.' }, { use: { tool: 'Read', input: { file_path: '/x/notes.md' } } }, { ask: { tool: 'Bash', input: { command: 'git status' } } }]);
    const chat = await r.chat.create();
    const done = r.until(chat.id, (event) => event.kind === 'done');
    const card = r.until(chat.id, (event) => event.kind === 'permission');
    await r.chat.send(chat.id, 'Show me <b>everything</b>', { host: 'claude' });
    const asked = (await card) as Extract<ChatEvent, { kind: 'permission' }>;
    r.chat.answer(chat.id, asked.request, { allow: false, always: false });
    await done;
    plan(r, [{ end: 'error', text: 'Claude Code stopped.' }]);
    await r.turn(chat.id, 'Again', { host: 'claude' });

    const files = readdirSync(r.b.paths.conversations);
    assert.deepEqual(files, [`${chat.id}.json`]);
    const after = r.again();
    assert.deepEqual(after.list().map((each) => [each.id, each.title]), [[chat.id, 'Show me <b>everything</b>']]);
    const opened = after.open(chat.id);
    // The stub's own lines carry a count and the model's refusal; each is kept as the page was given it.
    const stub = (entry: unknown) => (entry as { text?: string }).text?.replace(/^heard \d+ characters$/, 'heard').replace(/^decision deny: .*$/, 'decision deny');
    assert.deepEqual(opened.entries.map((entry) => (entry.kind === 'text' ? { kind: 'text', text: stub(entry) } : entry)), [
        { kind: 'user', text: 'Show me <b>everything</b>' },
        { kind: 'text', text: 'heard' },
        { kind: 'text', text: 'Here it is.' },
        { kind: 'step', text: 'Read a file', detail: '/x/notes.md' },
        { kind: 'step', text: 'Ran a command', detail: 'git status' },
        { kind: 'permission', title: asked.title, detail: asked.detail, answer: 'deny' },
        { kind: 'text', text: 'decision deny' },
        { kind: 'user', text: 'Again' },
        { kind: 'text', text: 'heard' },
        { kind: 'error', text: 'Claude Code stopped.' },
    ]);
    assert.equal(opened.dropped, 0);
    plan(r, []);
    await r.turn(chat.id, 'After the restart', { host: 'claude' }, after);
    assert.equal(flagValue(r.hosts().at(-1)?.args ?? [], '--resume'), 'stub-session-1');
});

test('a conversation\'s turn on a Claude Code below the floor is refused before its host starts, and its record is unchanged', async (t) => {
    const r = rig(t);
    const chat = await r.chat.create('cmp_01ABC');
    await r.turn(chat.id, 'First', { host: 'claude' });
    const kept = readFileSync(join(r.b.paths.conversations, `${chat.id}.json`), 'utf8');
    const ran = r.hosts().length;
    const asked: string[][] = [];
    const below = r.again({ programs: { run: async (name, args) => (asked.push([name, ...args]), { exit: 0, stdout: '2.1.200 (Claude Code)\n', stderr: '' }) } });
    assert.deepEqual(await r.turn(chat.id, 'Second', { host: 'claude' }, below), [{ kind: 'done', error: floorSentence('2.1.200') }]);
    assert.deepEqual(asked, [['claude', '--version']], 'the version is read for a conversation\'s turn as for any other');
    assert.equal(r.hosts().length, ran, 'no host started');
    assert.equal(readFileSync(join(r.b.paths.conversations, `${chat.id}.json`), 'utf8'), kept, 'the record is not touched');
    assert.equal(below.isRunning(chat.id), false);
});

test('the record is written when the host names its session, before the turn ends, and again at the end', async (t) => {
    const r = rig(t);
    const pid = join(r.b.root, 'host.pid');
    plan(r, [{ wait: pid }]);
    const chat = await r.chat.create();
    await r.chat.send(chat.id, 'Wait', { host: 'claude' });
    const file = join(r.b.paths.conversations, `${chat.id}.json`);
    const until = Date.now() + 15_000;
    while (!existsSync(file) && Date.now() < until) await new Promise((resolve) => setTimeout(resolve, 50));
    assert.deepEqual(JSON.parse(readFileSync(file, 'utf8')).sessions, { claude: 'stub-session-1' });
    const done = r.until(chat.id, (event) => event.kind === 'done');
    r.chat.stop(chat.id);
    await done;
    assert.deepEqual(readConversations(r.b.paths.conversations).kept[0]?.entries.map((entry) => entry.kind), ['user', 'text']);
});

for (const host of ['claude', 'codex']) {
    for (const mode of ['missing', 'other']) {
        test(`a ${host} resume that ${mode === 'missing' ? 'is not found' : 'answers with another session'} falls to a fresh session given the recap`, async (t) => {
            const r = rig(t);
            const chat = await r.chat.create();
            await r.turn(chat.id, 'First words', { host });
            r.b.env.STUB_RESUME = mode;
            const said = await r.turn(chat.id, 'Second words', { host });
            const [, resumed, fresh] = r.hosts();
            assert.ok(resumed && fresh, 'the host was started again');
            assert.equal(r.hosts().length, 3);
            assert.ok(!fresh.args.includes('resume') && !fresh.args.includes('--resume'), 'the second start resumes nothing');
            assert.match(fresh.stdin, /<earlier-messages>\nperson: "First words"\nagent: "heard \d+ characters"\n<\/earlier-messages>\n\nSecond words$/);
            assert.deepEqual(said.filter((event) => event.kind === 'fresh'), [{ kind: 'fresh', reason: 'not_found' }]);
            assert.deepEqual(said.at(-1), { kind: 'done', error: null });
            const kept = r.chat.open(chat.id).entries.filter((entry) => entry.kind === 'text');
            assert.equal(kept.length, 2, 'one answer per turn is kept, none from the attempt that fell');
        });
    }
}

test('a resume that fails after the host said something is the turn\'s error, and is not started again', async (t) => {
    const r = rig(t);
    const chat = await r.chat.create();
    await r.turn(chat.id, 'First', { host: 'claude' });
    plan(r, [{ end: 'error', text: 'Rate limited.' }]);
    const said = await r.turn(chat.id, 'Second', { host: 'claude' });
    assert.equal(r.hosts().length, 2);
    assert.deepEqual(said.at(-1), { kind: 'done', error: 'Rate limited.' });
    assert.ok(!kinds(said).includes('fresh'));
});

for (const host of ['claude', 'codex']) {
    test(`a ${host} resume that fails having said nothing tries a fresh session, and the record gives up its session only when the fresh one did something`, async (t) => {
        const r = rig(t);
        const chat = await r.chat.create();
        await r.turn(chat.id, 'First', { host });
        const first = r.chat.open(chat.id);
        const kept = () => readConversations(r.b.paths.conversations).kept.find((each) => each.id === chat.id)?.sessions[host];
        const earlier = kept();
        assert.ok(earlier);
        // An account that is signed out: the resume and the fresh session both end in a failure with nothing said.
        r.b.env.STUB_HOST_SESSION = 'stub-session-fresh';
        r.b.env.STUB_SILENT = 'always';
        plan(r, [{ end: 'error', text: 'Not signed in.' }]);
        const said = await r.turn(chat.id, 'Second', { host });
        assert.equal(r.hosts().length, 3, 'the resume, then a fresh session');
        assert.deepEqual(said.filter((event) => event.kind === 'fresh'), [{ kind: 'fresh', reason: 'not_found' }]);
        assert.deepEqual(said.at(-1), { kind: 'done', error: 'Not signed in.' });
        assert.equal(kept(), earlier, 'the record keeps the session it had');
        assert.equal(r.chat.open(chat.id).entries.length, first.entries.length + 2, 'the words and the error of the turn');
        // The resume fails the same way, and the fresh session does something: it is the conversation's session now.
        r.b.env.STUB_SILENT = 'resumed';
        await r.turn(chat.id, 'Third', { host });
        assert.equal(r.hosts().length, 5);
        assert.equal(kept(), 'stub-session-fresh');
    });
}

test('a host start that throws ends the turn like any failure: the conversation is free, its set-up mark gone, its files closed, and done said', async (t) => {
    const r = rig(t);
    // A value no process can be started with: the start throws before any child exists.
    const env: Record<string, string | undefined> = { ...r.b.env, PLANTED_START: `a${String.fromCharCode(0)}b` };
    const chat = r.again({ env });
    const setup = await chat.create();
    const said = await r.turn(setup.id, 'Hello', { host: 'claude' }, chat);
    assert.deepEqual(said.at(-1), { kind: 'done', error: 'Claude Code could not be started: ERR_INVALID_ARG_VALUE' });
    assert.equal(chat.isRunning(setup.id), false);
    assert.deepEqual(existsSync(r.b.paths.turns) ? readdirSync(r.b.paths.turns) : [], [], 'the turn\'s file is removed');
    assert.deepEqual(chat.open(setup.id).entries, [{ kind: 'user', text: 'Hello' }, { kind: 'error', text: 'Claude Code could not be started: ERR_INVALID_ARG_VALUE' }]);
    // The next turn runs, and no set-up turn is left marked as running beside it: the one campaign it makes is handed over.
    delete env.PLANTED_START;
    env.STUB_HOST_PLAN = JSON.stringify([{ campaign: 'Autumn' }]);
    const next = await r.turn(setup.id, 'Again', { host: 'claude' }, chat);
    assert.equal(next.at(-2)?.kind, 'attached', JSON.stringify(next));
});

test('a card\'s answer is kept only from the conversation that was asked', async (t) => {
    const r = rig(t);
    plan(r, [{ ask: { tool: 'Bash', input: { command: 'git status' } } }]);
    const chat = await r.chat.create('cmp_01ABC');
    const done = r.until(chat.id, (event) => event.kind === 'done');
    const card = r.until(chat.id, (event) => event.kind === 'permission');
    await r.chat.send(chat.id, 'Look', { host: 'claude' });
    const asked = (await card) as Extract<ChatEvent, { kind: 'permission' }>;
    // Another conversation's answer, by the request's id, is no answer to this card.
    const other = await r.chat.create();
    r.chat.answer(other.id, asked.request, { allow: true, always: true });
    r.chat.answer(chat.id, asked.request, { allow: false, always: false });
    await done;
    assert.deepEqual(r.chat.open(chat.id).entries.filter((entry) => entry.kind === 'permission').map((entry) => entry.kind === 'permission' && entry.answer), ['deny']);
});

for (const order of ['set-up first', 'campaign first']) {
    test(`a set-up turn with a campaign conversation's turn beside it (${order}) hands over nothing by itself and offers the pick`, async (t) => {
        const r = rig(t);
        const setup = await r.chat.create();
        const campaign = await r.chat.create('cmp_01ABC');
        const ends = [r.until(setup.id, (event) => event.kind === 'done'), r.until(campaign.id, (event) => event.kind === 'done')];
        // Only the campaign's turn makes a campaign, while the set-up turn runs; the set-up turn cannot tell whose it is.
        if (order === 'set-up first') {
            plan(r, [{ sleep: 3000 }]);
            await r.chat.send(setup.id, 'Set one up', { host: 'claude' });
            plan(r, [{ campaign: 'Copy' }]);
            await r.chat.send(campaign.id, 'Make a copy of this campaign', { host: 'claude' });
        } else {
            plan(r, [{ sleep: 2500 }, { campaign: 'Copy' }]);
            await r.chat.send(campaign.id, 'Make a copy of this campaign', { host: 'claude' });
            plan(r, [{ sleep: 4000 }]);
            await r.chat.send(setup.id, 'Set one up', { host: 'claude' });
        }
        await Promise.all(ends);
        const [made] = (JSON.parse(readFileSync(r.campaigns, 'utf8')) as { id: string }[]).map((each) => each.id);
        assert.ok(made);
        const said = r.events.get(setup.id) ?? [];
        assert.ok(!kinds(said).includes('attached'), JSON.stringify(said));
        assert.deepEqual(said.at(-2), { kind: 'pick', campaignIds: [made] });
        assert.equal(r.chat.open(setup.id).campaignId, null);
    });
}

test('a set-up chat whose turn made exactly one campaign becomes that campaign\'s conversation, said before the turn\'s end', async (t) => {
    const handed: [number | null, boolean][] = [];
    const r = rig(t, { setupEnded: (found, handedOver) => void handed.push([found, handedOver]) });
    plantCampaigns(r, [{ id: 'cmp_OLD', name: 'Older' }]);
    plan(r, [{ campaign: 'Autumn\noutreach' }]);
    const chat = await r.chat.create();
    const said = await r.turn(chat.id, 'Help me start a new campaign.', { host: 'claude' });
    const made = (JSON.parse(readFileSync(r.campaigns, 'utf8')) as { id: string }[])[1]!.id;
    assert.deepEqual(said.slice(-2), [{ kind: 'attached', campaignId: made, title: 'Autumn outreach' }, { kind: 'done', error: null }]);
    assert.deepEqual(r.chat.list().map((each) => [each.campaignId, each.title]), [[made, 'Autumn outreach']]);
    assert.deepEqual(listed(r), ['campaign list --all-projects --include-archived --limit 1000', 'campaign list --all-projects --include-archived --limit 1000']);
    assert.deepEqual(handed, [[1, true]]);
    assert.equal(readConversations(r.b.paths.conversations).kept[0]?.campaign_id, made, 'kept');
    // From now on the conversation is the campaign's: no list is read, and the header names it.
    await r.turn(chat.id, 'Now what?', { host: 'claude' });
    assert.equal(listed(r).length, 2);
    assert.match(r.hosts().at(-1)?.stdin ?? '', new RegExp(`about the campaign ${made}\\.`));
    await assert.rejects(r.chat.attach((await r.chat.create()).id, made), /has its conversation already/);
    assert.equal((await r.chat.create(made)).id, chat.id);
});

test('a set-up turn that made no campaign offers nothing, and one that made two hands over neither and offers both to pick from', async (t) => {
    const handed: [number | null, boolean][] = [];
    const r = rig(t, { setupEnded: (found, handedOver) => void handed.push([found, handedOver]) });
    const none = await r.chat.create();
    assert.deepEqual(kinds(await r.turn(none.id, 'Hello', { host: 'claude' })).filter((kind) => kind === 'attached' || kind === 'pick'), []);
    plan(r, [{ campaign: { id: 'cmp_TWO', name: 'Two' } }, { campaign: { id: 'cmp_ONE', name: 'One' } }]);
    const two = await r.chat.create();
    const said = await r.turn(two.id, 'Make two', { host: 'claude' });
    assert.deepEqual(said.slice(-2), [{ kind: 'pick', campaignIds: ['cmp_ONE', 'cmp_TWO'] }, { kind: 'done', error: null }]);
    assert.deepEqual(r.chat.list().map((each) => each.campaignId), [null, null]);
    assert.deepEqual(handed, [[0, false], [2, false]]);
    assert.deepEqual(r.chat.open(two.id).entries.at(-1), { kind: 'pick', campaignIds: ['cmp_ONE', 'cmp_TWO'] }, 'the pick is kept as the page drew it');
    assert.deepEqual(r.again().open(two.id).entries.at(-1), { kind: 'pick', campaignIds: ['cmp_ONE', 'cmp_TWO'] }, 'and read back after a restart');
    await assert.rejects(r.chat.attach(two.id, 'cmp_THREE'), { message: 'That campaign is not one this chat made. Open it from the campaign.' });
    await assert.rejects(r.chat.attach(none.id, 'cmp_ONE'), { message: 'That campaign is not one this chat made. Open it from the campaign.' });
    assert.equal((await r.chat.attach(two.id, 'cmp_TWO')).campaignId, 'cmp_TWO');
});

test('a campaign counts as new only when it is absent before, created at or after the turn\'s start, and has no conversation', async (t) => {
    const r = rig(t);
    const backdated = await r.chat.create();
    plan(r, [{ campaign: { id: 'cmp_BACKDATED', name: 'Old', created_at: '2020-01-01T00:00:00.000Z' } }]);
    assert.deepEqual(kinds(await r.turn(backdated.id, 'Hello', { host: 'claude' })).slice(-1), ['done'], 'created before the turn started: neither handed over nor offered');
    const owned = await r.chat.create('cmp_OWNED');
    const chat = await r.chat.create();
    plan(r, [{ campaign: { id: 'cmp_OWNED', name: 'Owned' } }]);
    assert.deepEqual(kinds(await r.turn(chat.id, 'Hello', { host: 'claude' })).slice(-1), ['done'], 'a campaign that has its conversation: neither handed over nor offered');
    assert.equal(r.chat.open(owned.id).campaignId, 'cmp_OWNED');
    assert.deepEqual([r.chat.open(backdated.id).campaignId, r.chat.open(chat.id).campaignId], [null, null]);
});

test('a hand-over reads every page of the campaign list, and a page that is not read hands over nothing', async (t) => {
    const r = rig(t);
    r.b.env.STUB_PAGE_SIZE = '2';
    plantCampaigns(r, [1, 2, 3, 4, 5].map((n) => ({ id: `cmp_P${n}`, name: `P${n}` })));
    plan(r, [{ campaign: 'Paged' }]);
    const chat = await r.chat.create();
    const said = await r.turn(chat.id, 'Hello', { host: 'claude' });
    assert.equal(said.at(-2)?.kind, 'attached', JSON.stringify(said));
    assert.deepEqual(listed(r).map((asked) => asked.replace('campaign list --all-projects --include-archived --limit 1000', '').trim()), ['', '--cursor 2', '--cursor 4', '', '--cursor 2', '--cursor 4']);

    r.b.env.STUB_LIST_FAIL_PAGE = '2';
    const other = await r.chat.create();
    plan(r, [{ campaign: 'Unread' }]);
    assert.ok(!kinds(await r.turn(other.id, 'Hello', { host: 'claude' })).includes('attached'));
    assert.equal(r.chat.open(other.id).campaignId, null);
});

test('two set-up turns that ran at the same time hand over neither, and both offer what was made to pick from', async (t) => {
    const r = rig(t);
    const first = await r.chat.create();
    const second = await r.chat.create();
    const ends = [r.until(first.id, (event) => event.kind === 'done'), r.until(second.id, (event) => event.kind === 'done')];
    // Only the first makes a campaign, once the second has begun beside it; neither can tell whose it is.
    plan(r, [{ sleep: 2500 }, { campaign: 'Mine' }, { sleep: 500 }]);
    await r.chat.send(first.id, 'One', { host: 'claude' });
    plan(r, [{ sleep: 3500 }]);
    await r.chat.send(second.id, 'Two', { host: 'claude' });
    await Promise.all(ends);
    const [made] = (JSON.parse(readFileSync(r.campaigns, 'utf8')) as { id: string }[]).map((each) => each.id);
    assert.ok(made);
    for (const chat of [first, second]) {
        const said = r.events.get(chat.id) ?? [];
        assert.ok(!kinds(said).includes('attached'), chat.id);
        assert.deepEqual(said.at(-2), { kind: 'pick', campaignIds: [made] }, chat.id);
    }
    const picked = await r.chat.attach(first.id, made);
    assert.deepEqual([picked.campaignId, picked.title], [made, 'Spring']);
    assert.deepEqual(r.events.get(first.id)?.at(-1), { kind: 'attached', campaignId: made, title: 'Spring' });
    await assert.rejects(r.chat.attach(second.id, made), { message: 'That campaign has its conversation already. Open it from the campaign.' });
    await assert.rejects(r.chat.attach(first.id, 'cmp_OTHER'), /already about a campaign/);
});

test('a conversation whose turn runs is neither removed nor started afresh, and says so; once it ends, both are done', async (t) => {
    const r = rig(t);
    const pid = join(r.b.root, 'host.pid');
    plan(r, [{ wait: pid }]);
    const chat = await r.chat.create('cmp_01ABC');
    await r.chat.send(chat.id, 'Wait', { host: 'claude' });
    // The campaign's conversation is the one the page opens again, and a second send to it while it answers is refused.
    assert.equal((await r.chat.create('cmp_01ABC')).id, chat.id);
    const refused = r.until(chat.id, (event) => event.kind === 'done' && event.error !== null);
    await r.chat.send(chat.id, 'Beside it', { host: 'claude' });
    assert.deepEqual(await refused, { kind: 'done', error: 'This chat is still answering. Wait for it to finish, or stop it, then send again.' });
    assert.throws(() => r.chat.remove(chat.id), { message: 'This conversation is still answering. Wait for it to finish, or stop it, then remove it.' });
    assert.throws(() => r.chat.restart(chat.id), { message: 'This conversation is still answering. Wait for it to finish, or stop it, then start it afresh.' });
    await assert.rejects(r.chat.attach(chat.id, 'cmp_02'), /still answering/);
    const done = r.until(chat.id, (event) => event.kind === 'done');
    r.chat.stop(chat.id);
    await done;

    r.chat.restart(chat.id);
    const opened = r.chat.open(chat.id);
    assert.deepEqual([opened.campaignId, opened.entries.length > 0], ['cmp_01ABC', true], 'the campaign and the entries stay');
    plan(r, []);
    const said = await r.turn(chat.id, 'Afresh', { host: 'claude' });
    assert.ok(!r.hosts().at(-1)?.args.includes('--resume'), 'afresh resumes nothing');
    assert.ok(!kinds(said).includes('fresh'), 'and is no lost session');
    r.chat.remove(chat.id);
    assert.deepEqual(r.chat.list(), []);
    assert.deepEqual(readdirSync(r.b.paths.conversations), []);
    assert.throws(() => r.chat.open(chat.id), { message: 'That conversation is not one this desktop keeps.' });
    for (const bad of ['', 'conv_../x', 42, null]) assert.throws(() => r.chat.open(bad), { message: 'That conversation is not one this desktop keeps.' });
    await assert.rejects(r.chat.create('../cmp'), { message: 'That is not a campaign id.' });
});

test('a conversation that never had a turn is not listed and leaves no record, and Start afresh on it writes nothing', async (t) => {
    const r = rig(t);
    const setup = await r.chat.create();
    const campaign = await r.chat.create('cmp_01ABC');
    assert.deepEqual(r.chat.list(), []);
    r.chat.restart(setup.id);
    r.chat.restart(campaign.id);
    assert.deepEqual(existsSync(r.b.paths.conversations) ? readdirSync(r.b.paths.conversations) : [], []);
    assert.deepEqual(r.chat.list(), []);
    assert.equal(r.chat.open(setup.id).id, setup.id, 'the window that made it still opens it');
    // Its first turn keeps it: it is listed from the turn's start.
    const done = r.until(setup.id, (event) => event.kind === 'done');
    await r.chat.send(setup.id, 'Hello', { host: 'claude' });
    assert.deepEqual(r.chat.list().map((each) => each.id), [setup.id]);
    await done;
    assert.deepEqual(readdirSync(r.b.paths.conversations), [`${setup.id}.json`]);
});

test('list and open say whether a conversation\'s turn runs, so a page loaded again finds it running', async (t) => {
    const r = rig(t);
    plan(r, [{ wait: join(r.b.root, 'host.pid') }]);
    const chat = await r.chat.create('cmp_01ABC');
    assert.equal(r.chat.open(chat.id).running, false);
    const hostPid = join(r.b.root, 'host.pid');
    await r.chat.send(chat.id, 'Wait', { host: 'claude' });
    assert.equal(r.chat.open(chat.id).running, true);
    assert.deepEqual(r.chat.list().map((each) => [each.id, each.running]), [[chat.id, true]]);
    // The host writes that file once it is up: a stop before it would end a turn that had not begun, which is another case.
    const deadline = Date.now() + 20_000;
    while (!existsSync(hostPid) && Date.now() < deadline) await new Promise((resolve) => setTimeout(resolve, 20));
    assert.ok(existsSync(hostPid), 'the host started');
    const done = r.until(chat.id, (event) => event.kind === 'done');
    r.chat.stop(chat.id);
    await done;
    assert.equal(r.chat.open(chat.id).running, false);
    assert.deepEqual(r.chat.list().map((each) => [each.id, each.running]), [[chat.id, false]]);
});
