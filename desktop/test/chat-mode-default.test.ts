// Full access by default, and the mode a person chooses kept on the conversation's record, through whole turns against
// stub hosts: a record that names no mode opens in Full access, Ask first, Auto-edit and Plan only are kept through a
// restart, choosing Full access takes the mode off the record, a standing choice an earlier desktop left changes no mode
// and is gone once the desktop has started, and the Feed opens in Full access too, by one named constant, and offers every
// mode. Then one folder per conversation: every turn of a new conversation runs in its own folder beside the chat folder,
// under `chats`, in every mode and on every host, with the instruction files and the recorded skills there, goes with the
// conversation, and is where a terminal continues it; a conversation an earlier desktop made, whose record names no
// folder, keeps running and is continued in the shared chat folder; nothing is made for a grant of Full access.

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readdirSync, readFileSync, realpathSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent, type ChatOptions } from '../src/chat.ts';
import { newConversationId } from '../src/chat-conversations.ts';
import { readFence } from '../src/chat-deny.ts';
import * as chatModes from '../src/chat-modes.ts';
import { type ChatMode } from '../src/chat-modes.ts';
import { OpenIn, type OpenInDeps } from '../src/open-in.ts';
import { continueFlags } from '../src/open-in-terminal.ts';
import { bench, outsideStandIn, placeSandboxFiles, type Bench, type Recorded } from './harness.ts';

const fence = readFence(join(import.meta.dirname, '..'));
const claude = { host: 'claude' };

interface Rig {
    b: Bench;
    chat: Chat;
    events: Map<string, ChatEvent[]>;
    started: ChatMode[];
    /** How many times a box asked the person before Full access. */
    boxes: number;
    /** Sends and waits for the turn's `done`; the events it said. */
    turn(id: string, text: string, options: unknown): Promise<ChatEvent[]>;
    hosts(): Recorded[];
    /** Closes the window and starts another desktop over the same data directory, as a restart does; that one is `chat` now. */
    restart(): Chat;
}

function rig(t: { after(fn: () => void | Promise<void>): void }): Rig {
    const removals: (() => void)[] = [];
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude', 'codex'] });
    placeSandboxFiles(b);
    const bundle = join(b.root, 'bundle');
    mkdirSync(bundle, { recursive: true });
    const events = new Map<string, ChatEvent[]>();
    const waiters: { id: string; take: (event: ChatEvent) => boolean; resolve: (event: ChatEvent) => void }[] = [];
    const made: Chat[] = [];
    const until = (id: string, take: (event: ChatEvent) => boolean) =>
        new Promise<ChatEvent>((resolve, reject) => {
            const timer = setTimeout(() => reject(new Error(`chat ${id} never said what was waited for; it said ${JSON.stringify(events.get(id) ?? [])}`)), 20_000);
            waiters.push({ id, take, resolve: (event) => { clearTimeout(timer); resolve(event); } });
        });
    const r: Rig = {
        b,
        chat: undefined as unknown as Chat,
        events,
        started: [],
        boxes: 0,
        async turn(id, text, sendOptions) {
            const from = events.get(id)?.length ?? 0;
            const done = until(id, (event) => event.kind === 'done');
            await r.chat.send(id, text, sendOptions);
            await done;
            return (events.get(id) ?? []).slice(from);
        },
        hosts: () => b.record().filter((each) => (each.name === 'claude' || each.name === 'codex') && each.args[0] !== '--version'),
        restart() {
            r.chat.closeAll();
            r.chat = make();
            return r.chat;
        },
    };
    const make = () => {
        const options: ChatOptions = {
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
                const said = events.get(id) ?? [];
                said.push(event);
                events.set(id, said);
                for (const waiter of [...waiters]) {
                    if (waiter.id === id && waiter.take(event)) {
                        waiters.splice(waiters.indexOf(waiter), 1);
                        waiter.resolve(event);
                    }
                }
            },
            started: (_host, _resumed, mode) => void r.started.push(mode),
        };
        // A desktop that asks before Full access enters it from the menu only on a yes: this box says yes, so a row that
        // chooses Full access reaches what it holds. A desktop that asks nothing never calls it.
        const box: Record<string, unknown> = {
            confirmFullAccess: async () => {
                r.boxes += 1;
                return true;
            },
        };
        const chat = new Chat(Object.assign(options, box));
        made.push(chat);
        return chat;
    };
    r.chat = make();
    t.after(async () => {
        for (const chat of made) chat.closeAll();
        const end = Date.now() + 10_000;
        while (made.some((chat) => chat.list().some((each) => each.running)) && Date.now() < end) await new Promise((resolve) => setTimeout(resolve, 50));
        for (const chat of made) await chat.idle();
        for (const remove of removals) remove();
    });
    return r;
}

const flag = (args: string[], name: string) => (args.includes(name) ? args[args.indexOf(name) + 1] : undefined);
const notices = (events: ChatEvent[] | undefined) => (events ?? []).flatMap((event) => (event.kind === 'notice' ? [event.text] : []));
const recordOf = (r: Rig, id: string): Record<string, unknown> => JSON.parse(readFileSync(join(r.b.paths.conversations, `${id}.json`), 'utf8'));
const entriesOf = (folder: string) => (existsSync(folder) ? readdirSync(folder) : []);
const sha = (text: string) => createHash('sha256').update(text).digest('hex');

/** A path as the system spells it, where it is there; as it was given where it is not. */
const real = (path: string): string => {
    try {
        return realpathSync.native(path);
    } catch {
        return path;
    }
};
/** Whether the folder a host was started in is the one named. */
const same = (cwd: string | undefined, folder: string): boolean => cwd !== undefined && real(cwd) === real(folder);
/** A conversation's own folder: beside the chat folder, under `chats`, so no file of the shared folder is above it. */
const ownFolder = (r: Rig, id: string): string => join(dirname(r.b.paths.chat), 'chats', id);

/** "Open in"'s entry that continues a chat in a terminal, wired to the chat as the main process wires it (`main.ts`). */
function continuer(r: Rig): { openIn: OpenIn; cwds: (string | undefined)[] } {
    const cwds: (string | undefined)[] = [];
    const deps: Partial<OpenInDeps> = {
        cli: {
            run: (args, given) => {
                if (args[0] === 'session') cwds.push(given?.cwd);
                return Promise.resolve({ exit: 0, stdout: '{}', stderr: '' });
            },
        },
        continuable: (chatId) => r.chat.continuable(chatId),
        chatFolder: () => r.b.paths.chat,
        continueFlags,
    };
    return { openIn: new OpenIn(deps as OpenInDeps), cwds };
}

function put(path: string, text: string): void {
    mkdirSync(dirname(path), { recursive: true });
    writeFileSync(path, text);
}

function textOf(path: string): string | null {
    try {
        return readFileSync(path, 'utf8');
    } catch {
        return null;
    }
}

/** Waits up to ten seconds for what `check` holds, and fails saying `what` when it never does. */
async function settled(check: () => boolean, what: string): Promise<void> {
    const end = Date.now() + 10_000;
    while (!check() && Date.now() < end) await new Promise((resolve) => setTimeout(resolve, 20));
    assert.ok(check(), what);
}

/**
 * A conversation's record as a desktop of 0.49 wrote it, before this desktop starts: no folder, and no mode but Plan
 * only. Its id is returned.
 */
function earlierRecord(r: Rig, extra: Record<string, unknown> = {}): string {
    const id = newConversationId();
    const at = '2026-11-18T10:00:00.000Z';
    const entries = [{ kind: 'user', text: 'hello' }, { kind: 'text', text: 'Hello. What shall we do?', at }];
    put(join(r.b.paths.conversations, `${id}.json`), JSON.stringify({ id, campaign_id: null, title: 'Made before', created_at: at, updated_at: at, sessions: {}, entries, dropped: 0, ...extra }));
    return id;
}

// The mode: Full access unless the record says otherwise.

test('Ask first, Auto-edit or Plan only chosen from the menu is kept on the conversation\'s record, and after a restart the chat opens in it and its next turn runs in it', async (t) => {
    for (const mode of ['ask_first', 'auto_edit', 'plan_only'] as const) {
        const r = rig(t);
        const { id } = await r.chat.create();
        await r.turn(id, 'hello', claude);
        assert.equal((await r.chat.setMode(id, mode, 'claude')).mode, mode);
        assert.equal(recordOf(r, id).mode, mode, `${mode} is on the record`);
        const later = r.restart();
        assert.equal(later.open(id).mode, mode, `${mode} after a restart`);
        assert.equal(later.list().find((each) => each.id === id)?.mode, mode);
        await r.turn(id, 'again', claude);
        assert.equal(r.started.at(-1), mode, `${mode}: the turn after the restart`);
    }
});

test('choosing Full access from the menu takes the mode off the record, so a restart opens the chat in Full access', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.turn(id, 'hello', claude);
    await r.chat.setMode(id, 'plan_only');
    assert.equal(recordOf(r, id).mode, 'plan_only');
    assert.equal((await r.chat.setMode(id, 'full_access', 'claude')).mode, 'full_access');
    assert.equal(recordOf(r, id).mode, undefined, 'the record holds no mode');
    assert.equal(r.restart().open(id).mode, 'full_access');
});

test('a record an earlier desktop wrote with no mode opens in Full access, in the list too, and its next turn runs in it; one it wrote in Plan only opens in Plan only', async (t) => {
    const r = rig(t);
    const plain = earlierRecord(r);
    const planned = earlierRecord(r, { mode: 'plan_only' });
    const chat = r.restart();
    assert.equal(chat.open(plain).mode, 'full_access');
    assert.equal(chat.modeNow(plain), 'full_access');
    assert.equal(chat.list().find((each) => each.id === plain)?.mode, 'full_access');
    assert.equal(chat.open(planned).mode, 'plan_only');
    await r.turn(plain, 'hello', claude);
    assert.deepEqual(r.started, ['full_access']);
});

test('a standing choice of Full access an earlier desktop left is gone once the desktop has started, and changes no mode', async (t) => {
    const r = rig(t);
    const planned = earlierRecord(r, { mode: 'plan_only' });
    // Always on this computer, with new chats left in Ask first: what a desktop of 0.49 kept for its menu.
    put(r.b.paths.fullAccess, `${JSON.stringify({ claude: { consent: 1, given_at: '2026-11-13T08:00:00Z', new_chats: false } }, null, 2)}\n`);
    const chat = r.restart();
    await settled(() => !existsSync(r.b.paths.fullAccess), 'the file is gone once the desktop has started');
    assert.equal(chat.open(planned).mode, 'plan_only');
    assert.equal((await chat.create(undefined, 'claude')).mode, 'full_access', 'a new chat is in Full access, whatever the file said of new chats');
});

test('the Feed opens in Full access as every chat does, its default one named constant: made new, after a restart, and in its first turn', async (t) => {
    const r = rig(t);
    const feed = r.chat.feed([]);
    assert.equal(feed.mode, 'full_access', 'the Feed is made in Full access');
    assert.equal(r.chat.open(feed.id).mode, 'full_access');
    assert.equal(r.restart().open(feed.id).mode, 'full_access', 'after a restart');
    await r.turn(feed.id, 'what came in?', claude);
    assert.deepEqual(r.started, ['full_access']);
    assert.equal(chatModes.feedDefaultMode, 'full_access', 'the Feed\'s default is one named constant');
});

test('the Feed\'s menu offers every mode as any chat\'s does: Auto-edit and Full access are chosen for it with no box, and its next turn runs in the mode chosen', async (t) => {
    const r = rig(t);
    const feed = r.chat.feed([]);
    const choose = (mode: ChatMode) => r.chat.setMode(feed.id, mode, 'claude').then((summary) => summary.mode, (error: Error) => error.message);
    assert.equal(await choose('ask_first'), 'ask_first');
    assert.equal(await choose('auto_edit'), 'auto_edit', 'Auto-edit is the Feed\'s to choose');
    await r.turn(feed.id, 'what came in?', claude);
    assert.equal(await choose('plan_only'), 'plan_only');
    assert.equal(await choose('full_access'), 'full_access', 'Full access is the Feed\'s to choose');
    await r.turn(feed.id, 'and since then?', claude);
    assert.deepEqual(r.started, ['auto_edit', 'full_access']);
    assert.equal(r.boxes, 0, 'no box asked before Full access');
});

// The folder: one per conversation made from now on.

test('every turn of a new conversation runs in its own folder beside the chat folder, under chats, in every mode and on Codex too', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    const own = ownFolder(r, id);
    for (const mode of ['full_access', 'ask_first', 'plan_only', 'auto_edit'] as const) {
        await r.chat.setMode(id, mode, 'claude');
        await r.turn(id, `a turn in ${mode}`, claude);
        const cwd = r.hosts().at(-1)?.cwd;
        assert.ok(same(cwd, own), `${mode}: the turn ran in ${cwd}`);
    }
    await r.chat.setMode(id, 'ask_first');
    await r.turn(id, 'a turn on Codex', { host: 'codex' });
    const cwd = r.hosts().at(-1)?.cwd;
    assert.ok(same(cwd, own), `Codex: the turn ran in ${cwd}`);
    assert.deepEqual(r.started, ['full_access', 'ask_first', 'plan_only', 'auto_edit', 'ask_first']);
    assert.equal(existsSync(join(r.b.paths.chat, id)), false, 'nothing of it is made inside the chat folder');
});

test('Ask first, then Full access, then Ask first again: each turn runs in the one folder, and the second and the third resume the one session', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    const own = ownFolder(r, id);
    await r.chat.setMode(id, 'ask_first');
    await r.turn(id, 'first', claude);
    await r.chat.setMode(id, 'full_access', 'claude');
    await r.turn(id, 'second', claude);
    await r.chat.setMode(id, 'ask_first');
    await r.turn(id, 'third', claude);
    const turns = r.hosts();
    assert.equal(turns.length, 3);
    for (const [at, turn] of turns.entries()) assert.ok(same(turn.cwd, own), `turn ${at + 1} ran in ${turn.cwd}`);
    assert.deepEqual(turns.map((turn) => flag(turn.args, '--resume')), [undefined, 'stub-session-1', 'stub-session-1']);
    assert.deepEqual(r.started, ['ask_first', 'full_access', 'ask_first']);
});

test('Full access makes no folder of a grant: nothing is made under chat-full nor moved to its ended place, by default or chosen from the menu', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.turn(id, 'by default', claude);
    await r.chat.setMode(id, 'ask_first');
    await r.chat.setMode(id, 'full_access', 'claude');
    assert.deepEqual(entriesOf(r.b.paths.chatFull), [], 'nothing under chat-full');
    await r.turn(id, 'chosen from the menu', claude);
    await r.chat.setMode(id, 'ask_first');
    assert.deepEqual(entriesOf(r.b.paths.chatFullEnded), [], 'nothing moved to the ended place');
    assert.deepEqual(entriesOf(r.b.paths.chatFullRecords), [], 'no grant recorded');
    assert.deepEqual(notices(r.events.get(id)), [], 'no folder is said to be kept');
});

test('a new conversation\'s folder holds the instruction files and the recorded skills as the chat folder has them, and is put right before each turn', async (t) => {
    const r = rig(t);
    for (const [folder, record] of [['.claude', 'claude.json'], ['.agents', 'agents.json']] as const) {
        put(join(r.b.paths.chat, folder, 'skills', 'campaign-planning', 'SKILL.md'), 'plan well');
        put(join(r.b.paths.skillsRecords, record), JSON.stringify({ version: 1, packs: [{ files: [{ path: 'campaign-planning/SKILL.md', sha256: sha('plan well') }] }] }));
    }
    const { id } = await r.chat.create();
    await r.turn(id, 'hello', claude);
    const own = ownFolder(r, id);
    assert.notEqual(fence.instructions, null, 'the application carries its instruction text');
    for (const file of ['CLAUDE.md', 'AGENTS.md']) assert.ok(textOf(join(own, file)) === fence.instructions, `${file} in the conversation's folder holds the instruction text`);
    for (const folder of ['.claude', '.agents']) assert.equal(textOf(join(own, folder, 'skills', 'campaign-planning', 'SKILL.md')), 'plan well', `${folder} skills`);
    // What a turn could leave there itself: a skill nobody recorded and a settings file, both gone before the next turn.
    put(join(own, '.claude', 'skills', 'planted', 'SKILL.md'), 'not recorded');
    put(join(own, '.claude', 'settings.json'), '{"disableAllHooks":true}');
    await r.turn(id, 'again', claude);
    assert.equal(existsSync(join(own, '.claude', 'skills', 'planted')), false, 'a skill nobody recorded');
    assert.equal(existsSync(join(own, '.claude', 'settings.json')), false, 'a settings file');
});

test('removing a conversation removes its folder, and leaves the chat folder and every other conversation\'s', async (t) => {
    const r = rig(t);
    const one = await r.chat.create();
    const two = await r.chat.create();
    await r.turn(one.id, 'hello', claude);
    await r.turn(two.id, 'hello', claude);
    assert.ok(existsSync(ownFolder(r, one.id)), 'its folder is there while the conversation is kept');
    r.chat.remove(one.id);
    await settled(() => !existsSync(ownFolder(r, one.id)), 'its folder goes with it');
    assert.ok(existsSync(ownFolder(r, two.id)));
    assert.ok(existsSync(join(r.b.paths.chat, 'CLAUDE.md')));
});

test('a conversation an earlier desktop made, whose record names no folder, keeps running in the shared chat folder in every mode and resumes its session there', async (t) => {
    const r = rig(t);
    const id = earlierRecord(r, { sessions: { claude: 'session-made-before' } });
    const chat = r.restart();
    await chat.setMode(id, 'full_access', 'claude');
    await r.turn(id, 'in Full access', claude);
    await chat.setMode(id, 'ask_first');
    await r.turn(id, 'in Ask first', claude);
    const [full, asked] = r.hosts();
    assert.ok(same(full?.cwd, r.b.paths.chat), `Full access ran in ${full?.cwd}`);
    assert.ok(same(asked?.cwd, r.b.paths.chat), `Ask first ran in ${asked?.cwd}`);
    assert.equal(flag(full?.args ?? [], '--resume'), 'session-made-before');
    assert.equal(flag(asked?.args ?? [], '--resume'), 'session-made-before');
    assert.deepEqual(r.started, ['full_access', 'ask_first']);
    assert.equal(existsSync(ownFolder(r, id)), false, 'no folder of its own is made for it');
});

test('a terminal continues a conversation in its own folder, and one an earlier desktop made in the shared chat folder', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'ask_first');
    await r.turn(id, 'hello', claude);
    const earlier = earlierRecord(r, { sessions: { claude: 'session-made-before' }, plain_sessions: ['claude'], chosen: { host: 'claude', model: null, effort: null } });
    r.restart();
    const { openIn, cwds } = continuer(r);
    assert.equal(await openIn.continueChat(id), null, 'the new conversation is continued');
    assert.equal(await openIn.continueChat(earlier), null, 'the earlier one is continued');
    assert.ok(same(cwds[0], ownFolder(r, id)), `the new conversation was continued in ${cwds[0]}`);
    assert.ok(same(cwds[1], r.b.paths.chat), `the earlier one was continued in ${cwds[1]}`);
});
