// What is left of Full access grants through whole turns against stub hosts: no grant and no folder of one is made now,
// and a grant's folder an earlier desktop left is moved to the ended place at the next start, said once when its
// conversation is opened, kept seven days and then swept. Also the host's own rules for a conversation's folder, the
// settings every turn carries, the added-folder rule (a folder whose `.claude` holds skills, subagents or commands is left
// out and said once) and the deploy a start makes.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent, type ChatOptions } from '../src/chat.ts';
import { readFence } from '../src/chat-deny.ts';
import { hookedSettings } from '../src/chat-hosts.ts';
import { endedName, keptMs, newGrantId } from '../src/chat-full-folder.ts';
import { SkillsGate } from '../src/skills-gate.ts';
import { bench, type Bench, type Recorded } from './harness.ts';

const fence = readFence(join(import.meta.dirname, '..'));
const day = 24 * 60 * 60 * 1000;
const claude = { host: 'claude' };

interface Rig {
    b: Bench;
    chat: Chat;
    events: Map<string, ChatEvent[]>;
    clock: { now: number };
    opened: string[];
    codes: string[];
    turn(id: string, text: string, options: unknown): Promise<ChatEvent[]>;
    hosts(): Recorded[];
    again(): Chat;
}

function rig(t: { after(fn: () => void | Promise<void>): void }, options: { chat?: Partial<ChatOptions>; env?: Record<string, string> } = {}): Rig {
    const removals: (() => void)[] = [];
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude', 'codex'], ...(options.env ? { env: options.env } : {}) });
    const bundle = join(b.root, 'bundle');
    mkdirSync(bundle, { recursive: true });
    const events = new Map<string, ChatEvent[]>();
    const waiters: { id: string; take: (event: ChatEvent) => boolean; resolve: (event: ChatEvent) => void }[] = [];
    const made: Chat[] = [];
    const clock = { now: Date.UTC(2026, 10, 12, 8, 0, 0) };
    const opened: string[] = [];
    const codes: string[] = [];
    const until = (id: string, take: (event: ChatEvent) => boolean) =>
        new Promise<ChatEvent>((resolve, reject) => {
            const timer = setTimeout(() => reject(new Error(`chat ${id} never said what was waited for; it said ${JSON.stringify(events.get(id) ?? [])}`)), 20_000);
            waiters.push({ id, take, resolve: (event) => { clearTimeout(timer); resolve(event); } });
        });
    const make = () => {
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
            fullAccessOffered: true,
            now: () => clock.now,
            openFolder: (path) => void opened.push(path),
            fullFolderFailed: (_step, code) => void codes.push(code),
            ...options.chat,
        });
        made.push(chat);
        return chat;
    };
    const r: Rig = {
        b,
        chat: make(),
        events,
        clock,
        opened,
        codes,
        async turn(id, text, sendOptions) {
            const from = events.get(id)?.length ?? 0;
            const done = until(id, (event) => event.kind === 'done');
            await r.chat.send(id, text, sendOptions);
            await done;
            return (events.get(id) ?? []).slice(from);
        },
        hosts: () => b.record().filter((each) => (each.name === 'claude' || each.name === 'codex') && each.args[0] !== '--version'),
        again: make,
    };
    t.after(async () => {
        for (const chat of made) chat.closeAll();
        const until = Date.now() + 10_000;
        while (made.some((chat) => chat.list().some((each) => each.running)) && Date.now() < until) await new Promise((resolve) => setTimeout(resolve, 50));
        for (const chat of made) await chat.idle();
        for (const remove of removals) remove();
    });
    return r;
}

const flag = (args: string[], name: string) => (args.includes(name) ? args[args.indexOf(name) + 1] : undefined);
const notices = (events: ChatEvent[] | undefined) => (events ?? []).filter((event): event is Extract<ChatEvent, { kind: 'notice' }> => event.kind === 'notice');

async function settled(check: () => boolean): Promise<void> {
    const until = Date.now() + 10_000;
    while (!check() && Date.now() < until) await new Promise((resolve) => setTimeout(resolve, 20));
    assert.ok(check(), 'it did not settle');
}

/** A grant's folder and the record of whom it was for, as an earlier desktop left them; the grant's id. */
function leftGrant(r: Rig, conversation: string): string {
    const grant = newGrantId(r.clock.now - 20 * day);
    mkdirSync(join(r.b.paths.chatFull, grant), { recursive: true });
    writeFileSync(join(r.b.paths.chatFull, grant, 'work.txt'), 'what the agent made');
    mkdirSync(r.b.paths.chatFullRecords, { recursive: true });
    writeFileSync(join(r.b.paths.chatFullRecords, grant), conversation);
    return grant;
}

test('a grant\'s folder an earlier desktop left is moved at the next start, and the chat says once, with its path and an Open folder button, that it is kept seven days; the button opens that folder and no other', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.turn(id, 'hello', claude);
    const grant = leftGrant(r, id);
    const next = r.again();
    await settled(() => readdirSync(r.b.paths.chatFull).length === 0);
    const name = endedName(grant, r.clock.now);
    assert.deepEqual(readdirSync(r.b.paths.chatFullEnded), [name]);
    assert.equal(readFileSync(join(r.b.paths.chatFullEnded, name, 'work.txt'), 'utf8'), 'what the agent made');
    next.open(id);
    await settled(() => notices(r.events.get(id)).length === 1);
    const said = notices(r.events.get(id));
    const path = join(r.b.paths.chatFullEnded, name);
    assert.equal(said[0]?.folder, name);
    assert.ok(said[0]?.text.includes(path), said[0]?.text);
    assert.match(said[0]?.text ?? '', /seven days/);
    next.open(id);
    await new Promise((resolve) => setTimeout(resolve, 100));
    assert.equal(notices(r.events.get(id)).length, 1, 'said once');
    await next.openEndedFolder(said[0]?.folder);
    assert.deepEqual(r.opened, [path]);
    await assert.rejects(next.openEndedFolder('..'), /not a folder/);
    await assert.rejects(next.openEndedFolder(endedName(newGrantId(), r.clock.now)), /not a folder/);
    await assert.rejects(next.openEndedFolder(undefined), /not a folder/);
    assert.deepEqual(r.opened, [path], 'nothing else was opened');
});

test('a start moves a folder a closed window left, and removes an ended folder eight days old and keeps one six days old', async (t) => {
    const r = rig(t);
    const old = join(r.b.paths.chatFullEnded, endedName(newGrantId(r.clock.now - 8 * day), r.clock.now - 8 * day));
    const recent = join(r.b.paths.chatFullEnded, endedName(newGrantId(r.clock.now - 6 * day), r.clock.now - 6 * day));
    for (const folder of [old, recent]) {
        mkdirSync(join(folder, 'deep'), { recursive: true });
        writeFileSync(join(folder, 'deep', 'file.txt'), 'x');
    }
    const left = join(r.b.paths.chatFull, newGrantId(r.clock.now - 20 * day));
    mkdirSync(left, { recursive: true });
    r.again();
    await settled(() => !existsSync(old));
    assert.ok(existsSync(recent));
    assert.ok(!existsSync(left), 'the leftover was moved');
    const names = readdirSync(r.b.paths.chatFullEnded);
    assert.ok(names.includes(endedName(left.slice(-26), r.clock.now)), 'its seven days count from this start');
});

test('the sweep takes only as many entries as the cap lets it in a pass and finishes at the next start', async (t) => {
    const r = rig(t, { chat: { sweepCap: 3 } });
    const old = join(r.b.paths.chatFullEnded, endedName(newGrantId(r.clock.now - 8 * day), r.clock.now - 8 * day));
    for (const name of ['a', 'b', 'c', 'd']) {
        mkdirSync(join(old, 'deep'), { recursive: true });
        writeFileSync(join(old, 'deep', `${name}.txt`), 'x');
    }
    r.again();
    await settled(() => readdirSync(join(old, 'deep')).length < 4);
    assert.ok(existsSync(old), 'one pass of three entries does not finish it');
    for (let pass = 0; pass < 6 && existsSync(old); pass++) {
        r.again();
        await new Promise((resolve) => setTimeout(resolve, 100));
    }
    assert.ok(!existsSync(old));
});

test('a leftover that cannot be moved is left in place and a code is said; the next start moves it, and a marker inside it tells nothing to another conversation', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.turn(id, 'hello', claude);
    const bystander = await r.chat.create();
    await r.turn(bystander.id, 'hello', claude);
    const grant = leftGrant(r, id);
    // A file the agent wrote into the folder names nothing: only the desktop's own record, outside it, is read.
    writeFileSync(join(r.b.paths.chatFull, grant, '.grant-conversation'), bystander.id);
    // A file where the ended place should be makes the move fail.
    writeFileSync(r.b.paths.chatFullEnded, 'in the way');
    const held = r.again();
    await settled(() => r.codes.length === 1);
    assert.match(r.codes[0] ?? '', /^E[A-Z]+$/);
    assert.deepEqual(readdirSync(r.b.paths.chatFull), [grant]);
    held.open(id);
    await new Promise((resolve) => setTimeout(resolve, 100));
    assert.deepEqual(notices(r.events.get(id)), [], 'nothing is said of a folder that did not move');
    rmSync(r.b.paths.chatFullEnded);
    const next = r.again();
    await settled(() => readdirSync(r.b.paths.chatFull).length === 0);
    next.open(id);
    await settled(() => notices(r.events.get(id)).length === 1);
    assert.equal(notices(r.events.get(id))[0]?.folder, endedName(grant, r.clock.now));
    next.open(bystander.id);
    await new Promise((resolve) => setTimeout(resolve, 100));
    assert.deepEqual(notices(r.events.get(bystander.id)), [], 'a forged marker in the folder tells nothing to another conversation');
});

test('the record of whom a grant was for is removed with its folder after the seven days', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.turn(id, 'hello', claude);
    const grant = leftGrant(r, id);
    r.again();
    await settled(() => readdirSync(r.b.paths.chatFull).length === 0);
    assert.equal(readFileSync(join(r.b.paths.chatFullRecords, grant), 'utf8'), id, 'kept while the folder is');
    r.clock.now += 8 * day;
    r.again();
    await settled(() => readdirSync(r.b.paths.chatFullEnded).length === 0);
    await settled(() => !existsSync(join(r.b.paths.chatFullRecords, grant)));
});

test('the ended place is kept for exactly seven days', () => {
    assert.equal(keptMs, 7 * day);
});

test('every turn\'s settings, in all three modes, carry disableSkillShellExecution', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'ask_first');
    await r.turn(id, 'ask', claude);
    await r.chat.setMode(id, 'plan_only');
    await r.turn(id, 'plan', claude);
    await r.chat.setMode(id, 'full_access', 'claude');
    const during = r.turn(id, 'full', claude);
    await during;
    const [asked, planned, full] = r.hosts();
    assert.equal(JSON.parse(flag(planned?.args ?? [], '--settings') ?? '{}').disableSkillShellExecution, true);
    // A turn the hook rides has its settings in a file of its own, removed with the turn; their text is the function's.
    for (const hooked of [asked, full]) assert.match(flag(hooked?.args ?? [], '--settings') ?? '', /\.settings\.json$/);
    for (const mode of ['ask_first', 'auto_edit', 'full_access'] as const) {
        assert.equal(hookedSettings(mode, 'http://127.0.0.1:4100/hook', 'the-secret').disableSkillShellExecution, true, mode);
    }
});

test('a conversation\'s turn is refused the shared chat folder and the other conversations\' folders by the host\'s own rules, and may use its own', async (t) => {
    const r = rig(t);
    const other = await r.chat.create();
    await r.turn(other.id, 'hello', claude);
    const { id } = await r.chat.create();
    await r.turn(id, 'go', claude);
    const host = r.hosts()[1];
    assert.equal(host?.cwd, join(r.b.paths.chats, id));
    const rules = (host?.args ?? []).filter((arg) => /^(Read|Edit)\(/.test(arg));
    const names = (name: string) => rules.some((rule) => rule.includes(name));
    assert.ok(names(other.id), 'the other conversation\'s folder is refused');
    assert.ok(names('chat-full-ended'), 'the ended place is refused');
    assert.ok(!rules.some((rule) => rule.endsWith(`${id})`) || rule.endsWith(`${id}/**)`)), 'its own folder is not');
    assert.ok(rules.some((rule) => rule.endsWith('chat)') || rule.endsWith('chat/**)')), 'the shared chat folder is refused to it');
});

test('a campaign folder whose .claude holds skills, subagents or commands is left out and said once with its name and the reason; one that holds none is added', async (t) => {
    const r = rig(t);
    const plain = join(r.b.root, 'harness-copy', 'plain');
    mkdirSync(join(plain, '.claude'), { recursive: true });
    writeFileSync(join(plain, '.claude', 'settings.local.json'), '{}');
    for (const [folder, what] of [['skilled', 'skills'], ['agents-only', 'agents'], ['commands-only', 'commands']] as const) {
        mkdirSync(join(r.b.root, 'harness-copy', folder, '.claude', what), { recursive: true });
    }
    r.b.env.STUB_PROJECT_FOLDER = plain;
    for (const folder of ['skilled', 'agents-only', 'commands-only']) {
        r.b.env.STUB_CAMPAIGN_FOLDER = join(r.b.root, 'harness-copy', folder);
        const { id } = await r.chat.create(`cmp_${folder.replace(/\W/g, '')}`);
        const said = await r.turn(id, 'hello', claude);
        const added = r.hosts().at(-1)?.args.flatMap((arg, i, all) => (all[i - 1] === '--add-dir' ? [arg] : []));
        assert.deepEqual(added, [plain], `${folder} is left out and the project's plain folder added`);
        const left = notices(said);
        assert.equal(left.length, 1);
        assert.ok(left[0]?.text.includes(`“${folder}”`) || left[0]?.text.includes(`"${folder}"`), left[0]?.text);
        assert.match(left[0]?.text ?? '', /skills, subagents or commands/);
        assert.equal(left[0]?.folder, undefined, 'a notice that is no ended folder carries no button');
        await r.turn(id, 'again', claude);
        assert.equal(notices(r.events.get(id)).length, 1, 'said once for the folder, not at each turn');
    }
});

test('a send during the deploy a start makes into the chat folder builds its turn only after the deploy ends', async (t) => {
    const gate = new SkillsGate();
    const r = rig(t, { chat: { skillsGate: gate } });
    const { id } = await r.chat.create();
    const release = gate.hold();
    const said = (kind: string) => (r.events.get(id) ?? []).filter((event) => event.kind === kind);
    const sending = r.chat.send(id, 'hello', claude);
    await new Promise((resolve) => setTimeout(resolve, 300));
    assert.deepEqual(r.hosts(), [], 'no host started while the deploy was going');
    assert.equal(said('done').length, 0);
    await r.chat.send(id, 'second', claude);
    assert.equal(said('done').length, 1, 'a second send while one waits is refused as busy');
    release();
    await sending;
    await settled(() => said('done').length === 2);
    assert.equal(r.hosts().length, 1);
});

test('a desktop left running sweeps the ended place each day, so a folder goes once its seven days are over without a new start', async (t) => {
    const r = rig(t, { chat: { sweepEveryMs: 50 } });
    const ended = join(r.b.paths.chatFullEnded, endedName(newGrantId(r.clock.now - 6 * day), r.clock.now - 6 * day));
    mkdirSync(join(ended, 'deep'), { recursive: true });
    writeFileSync(join(ended, 'deep', 'file.txt'), 'x');
    await new Promise((resolve) => setTimeout(resolve, 300));
    assert.ok(existsSync(ended), 'six days old: kept');
    r.clock.now += 2 * day;
    await settled(() => !existsSync(ended));
});

test('the Feed is given Full access as any chat is: no folder of a grant, and the other modes stay its to choose', async (t) => {
    const r = rig(t);
    const feed = r.chat.feed([]);
    await r.chat.setMode(feed.id, 'ask_first');
    const given = await r.chat.setMode(feed.id, 'full_access', 'claude').then((summary) => summary.mode, (error: Error) => error.message);
    assert.equal(given, 'full_access', 'Full access is the Feed\'s to choose');
    assert.ok(!existsSync(r.b.paths.chatFull) || readdirSync(r.b.paths.chatFull).length === 0, 'no grant');
    assert.equal(r.chat.open(feed.id).mode, 'full_access', 'the Feed is in Full access');
    await r.chat.setMode(feed.id, 'plan_only');
    assert.equal(r.chat.open(feed.id).mode, 'plan_only', 'the other modes are the Feed\'s to choose');
});
