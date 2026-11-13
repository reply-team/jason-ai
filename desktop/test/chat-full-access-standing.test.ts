// Full access answered once for good, through the chat against stub hosts: Always kept by the main process in its own
// file, the menu entering Full access without the box while it stands and only where every other check passes, new chats
// starting in Full access when the person also asked for that, the take-back, and the one way the desktop takes it back
// itself. Out of the box nothing changes.

import assert from 'node:assert/strict';
import fs, { existsSync, mkdirSync, mkdtempSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { syncBuiltinESMExports } from 'node:module';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent, type ChatOptions } from '../src/chat.ts';
import { readFence } from '../src/chat-deny.ts';
import { codexFullAccess, feedFullAccess, fullAccessConsent, fullAccessSentence, newChatAskFirstSentence, standingUnkeptSentence, standingUnremovedSentence, unguardedSentence, unguardedStandingSentence, type FullAccessAnswer } from '../src/chat-modes.ts';
import { readStanding, takenBackMark, writeStanding } from '../src/full-access-standing.ts';
import { bench, outsideStandIn, placeSandboxFiles, type Bench } from './harness.ts';

const fence = readFence(join(import.meta.dirname, '..'));
const claude = { host: 'claude' };
const given = '2026-01-02T03:04:05.000Z';

interface Rig {
    b: Bench;
    chat: Chat;
    events: Map<string, ChatEvent[]>;
    confirms: number;
    standings: string[];
    turn(id: string, text: string): Promise<ChatEvent[]>;
    until(id: string, take: (event: ChatEvent) => boolean): Promise<ChatEvent>;
    again(): Chat;
}

function rig(t: { after(fn: () => void | Promise<void>): void }, options: { answer?: boolean | FullAccessAnswer | null; chat?: Partial<ChatOptions>; env?: Record<string, string> } = {}): Rig {
    const removals: (() => void)[] = [];
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude', 'codex'], ...(options.env ? { env: options.env } : {}) });
    placeSandboxFiles(b);
    const bundle = join(b.root, 'bundle');
    mkdirSync(bundle, { recursive: true });
    const events = new Map<string, ChatEvent[]>();
    const waiters: { id: string; take: (event: ChatEvent) => boolean; resolve: (event: ChatEvent) => void }[] = [];
    const made: Chat[] = [];
    const r: Rig = {
        b,
        chat: undefined as unknown as Chat,
        events,
        confirms: 0,
        standings: [],
        until: (id, take) =>
            new Promise<ChatEvent>((resolve, reject) => {
                const timer = setTimeout(() => reject(new Error(`chat ${id} never said what was waited for; it said ${JSON.stringify(events.get(id) ?? [])}`)), 20_000);
                waiters.push({ id, take, resolve: (event) => { clearTimeout(timer); resolve(event); } });
            }),
        async turn(id, text) {
            const from = events.get(id)?.length ?? 0;
            const done = r.until(id, (event) => event.kind === 'done');
            await r.chat.send(id, text, claude);
            await done;
            return (events.get(id) ?? []).slice(from);
        },
        again: () => make(),
    };
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
            sandboxOutside: outsideStandIn,
            fullAccessOffered: true,
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
            confirmFullAccess: async () => {
                r.confirms++;
                return options.answer === undefined ? { always: true, newChats: false } : options.answer;
            },
            standingChanged: (standing, why) => void r.standings.push(`${why}:${standing === null ? 'none' : standing.newChats ? 'new chats' : 'menu'}`),
            ...options.chat,
        });
        made.push(chat);
        return chat;
    };
    r.chat = make();
    t.after(async () => {
        for (const chat of made) chat.closeAll();
        const until = Date.now() + 10_000;
        while (made.some((chat) => chat.list().some((each) => each.running)) && Date.now() < until) await new Promise((resolve) => setTimeout(resolve, 50));
        for (const chat of made) await chat.idle();
        for (const remove of removals) remove();
    });
    return r;
}

const plan = (r: Rig, steps: unknown[]) => {
    r.b.env.STUB_HOST_PLAN = JSON.stringify(steps);
};
const stand = (r: Rig, newChats: boolean, consent = fullAccessConsent) => writeStanding(r.b.paths.fullAccess, 'claude', { consent, givenAt: given, newChats });
const grants = (r: Rig) => (existsSync(r.b.paths.chatFull) ? readdirSync(r.b.paths.chatFull) : []);
const done = (events: ChatEvent[]) => events.at(-1);
const notices = (events: ChatEvent[]) => events.flatMap((event) => (event.kind === 'notice' ? [event.text] : []));
const tick = () => new Promise((resolve) => setTimeout(resolve, 20));

/**
 * The file of the standing choice will not be removed, as when another program holds it open on Windows: removing it
 * throws EBUSY until the function returned is called. Every other path is removed as before.
 */
function holdOpen(t: { after(fn: () => void): void }, file: string, also: { replace?: boolean } = {}): () => void {
    const original = fs.rmSync;
    const rename = fs.renameSync;
    const busy = () => Object.assign(new Error(`EBUSY: resource busy or locked, '${file}'`), { code: 'EBUSY' });
    fs.rmSync = ((path: fs.PathLike, options?: fs.RmOptions) => {
        if (String(path) === file) throw busy();
        original(path, options);
    }) as typeof fs.rmSync;
    // With replace, a file renamed over it fails the same way.
    if (also.replace === true)
        fs.renameSync = ((from: fs.PathLike, to: fs.PathLike) => {
            if (String(to) === file) throw busy();
            rename(from, to);
        }) as typeof fs.renameSync;
    syncBuiltinESMExports();
    let held = true;
    const release = () => {
        if (!held) return;
        held = false;
        fs.rmSync = original;
        fs.renameSync = rename;
        syncBuiltinESMExports();
    };
    t.after(release);
    return release;
}
const modes = (events: ChatEvent[]) => events.flatMap((event) => (event.kind === 'mode' ? [event.mode] : []));

test('out of the box there is no standing choice: no file, Ask first, and the box each time', async (t) => {
    const r = rig(t, { answer: true });
    assert.equal(r.chat.fullAccessStanding(), null);
    const { id, mode } = await r.chat.create(undefined, 'claude');
    assert.equal(mode, 'ask_first', 'a new chat starts in Ask first');
    assert.equal((await r.chat.setMode(id, 'full_access', 'claude')).mode, 'full_access');
    await r.chat.setMode(id, 'ask_first');
    await r.chat.setMode(id, 'full_access', 'claude');
    assert.equal(r.confirms, 2);
    assert.ok(!existsSync(r.b.paths.fullAccess), '"This time" keeps nothing');
});

test('"This time" with the checkbox ticked keeps nothing: the checkbox counts only with Always', async (t) => {
    const r = rig(t, { answer: { always: false, newChats: true } });
    const { id } = await r.chat.create();
    assert.equal((await r.chat.setMode(id, 'full_access', 'claude')).mode, 'full_access');
    assert.ok(!existsSync(r.b.paths.fullAccess));
    assert.equal(r.chat.fullAccessStanding(), null);
    assert.equal((await r.chat.create(undefined, 'claude')).mode, 'ask_first');
});

test('Always is kept by the main process under this build\'s consent number, and the menu then enters Full access without the box', async (t) => {
    const r = rig(t);
    const first = await r.chat.create();
    assert.equal((await r.chat.setMode(first.id, 'full_access', 'claude')).mode, 'full_access');
    assert.equal(r.confirms, 1);
    const kept = readStanding(r.b.paths.fullAccess, 'claude');
    assert.equal(kept?.consent, fullAccessConsent);
    assert.equal(kept?.newChats, false);
    assert.deepEqual(r.chat.fullAccessStanding(), { givenAt: kept?.givenAt, newChats: false });
    assert.deepEqual(r.standings, ['given:menu']);

    const second = await r.chat.create(undefined, 'claude');
    assert.equal(second.mode, 'ask_first', 'without "start new chats" a new chat is in Ask first');
    assert.equal((await r.chat.setMode(second.id, 'full_access', 'claude')).mode, 'full_access');
    assert.equal(r.confirms, 1, 'no box while the choice stands');
    assert.equal(grants(r).length, 2, 'each chat has its own grant folder, the box or not');
});

test('after a restart a kept chat is in Ask first whatever the choice says, and with Always its menu enters Full access without the box', async (t) => {
    const r = rig(t, { answer: { always: true, newChats: true } });
    const { id } = await r.chat.create(undefined, 'claude');
    await r.chat.setMode(id, 'full_access', 'claude');
    await r.turn(id, 'hello');
    const later = r.again();
    assert.equal(later.open(id).mode, 'ask_first', 'an existing chat after a restart is Ask first as today');
    const before = r.confirms;
    assert.equal((await later.setMode(id, 'full_access', 'claude')).mode, 'full_access');
    assert.equal(r.confirms, before, 'no box');
});

test('a choice kept under a lower or a higher consent number is no choice: the box comes back', async (t) => {
    for (const consent of [fullAccessConsent - 1, fullAccessConsent + 1]) {
        const r = rig(t, { answer: true });
        stand(r, true, consent);
        assert.equal(r.chat.fullAccessStanding(), null);
        assert.equal((await r.chat.create(undefined, 'claude')).mode, 'ask_first', `consent ${consent}: a new chat is Ask first`);
        const { id } = await r.chat.create();
        await r.chat.setMode(id, 'full_access', 'claude');
        assert.equal(r.confirms, 1, `consent ${consent}: the box is shown`);
    }
});

test('with Always standing the Feed is still refused Full access, and so are Codex and a Claude Code below the floor, before any box', async (t) => {
    const r = rig(t);
    stand(r, true);
    const feed = r.chat.feed([]);
    assert.equal(feed.mode, 'ask_first', 'the Feed never starts in Full access');
    await assert.rejects(r.chat.setMode(feed.id, 'full_access', 'claude'), { message: feedFullAccess });
    const { id } = await r.chat.create();
    await assert.rejects(r.chat.setMode(id, 'full_access', 'codex'), { message: codexFullAccess });
    assert.equal(r.confirms, 0);
    assert.equal(r.chat.open(id).mode, 'ask_first');

    const versionFile = join(mkdtempSync(join(tmpdir(), 'jason-version-')), 'version.json');
    t.after(() => rmSync(dirname(versionFile), { recursive: true, force: true }));
    writeFileSync(versionFile, JSON.stringify({ stdout: '2.1.300 (Claude Code)' }));
    const old = rig(t, { env: { STUB_CLAUDE_VERSION_FILE: versionFile } });
    stand(old, true);
    const made = await old.chat.create(undefined, 'claude');
    assert.equal(made.mode, 'ask_first', 'a new chat on a host below the floor starts in Ask first');
    assert.deepEqual(old.events.get(made.id) ?? [], [], 'with no notice');
    await assert.rejects(old.chat.setMode(made.id, 'full_access', 'claude'), { message: fullAccessSentence('2.1.300') });
    assert.equal(old.confirms, 0);
});

test('"start new chats in Full access": a new chat on Claude Code is in Full access from its first moment, and on any other host in Ask first with no notice', async (t) => {
    const r = rig(t);
    stand(r, true);
    const made = await r.chat.create(undefined, 'claude');
    assert.equal(made.mode, 'full_access', 'the summary the page draws first already says Full access');
    assert.equal(r.chat.open(made.id).mode, 'full_access');
    assert.equal(r.confirms, 0);
    assert.equal(grants(r).length, 1, 'its grant folder is made before anything is sent');
    const said = await r.turn(made.id, 'hello');
    assert.deepEqual(done(said), { kind: 'done', error: null });

    const onCodex = await r.chat.create(undefined, 'codex');
    assert.equal(onCodex.mode, 'ask_first');
    assert.deepEqual(r.events.get(onCodex.id) ?? [], [], 'no notice');
    assert.equal((await r.chat.create()).mode, 'ask_first', 'a host not named is no host that can take it');
    assert.equal((await r.chat.create(undefined, 'gemini')).mode, 'ask_first');
});

test('"start new chats" reaches only a conversation newly made: a campaign\'s existing conversation keeps its mode', async (t) => {
    const r = rig(t);
    const first = await r.chat.create('cmp_01ABC', 'claude');
    assert.equal(first.mode, 'ask_first');
    stand(r, true);
    assert.equal((await r.chat.create('cmp_01ABC', 'claude')).mode, 'ask_first', 'the one it has is no new chat');
    assert.equal((await r.chat.create('cmp_01DEF', 'claude')).mode, 'full_access', 'a campaign\'s first conversation is a new chat');
});

test('a take-back removes the choice and leaves the chats in Full access now as they are; the next entry asks', async (t) => {
    const r = rig(t);
    stand(r, true);
    const open = await r.chat.create(undefined, 'claude');
    assert.equal(open.mode, 'full_access');
    const other = await r.chat.create();
    assert.equal(r.chat.takeBackFullAccess(), null);
    assert.ok(!existsSync(r.b.paths.fullAccess));
    assert.equal(r.chat.fullAccessStanding(), null);
    assert.deepEqual(r.standings, ['taken_back:none']);
    assert.equal(r.chat.open(open.id).mode, 'full_access', 'a chat in Full access stays in it');
    assert.deepEqual(modes(r.events.get(open.id) ?? []), ['full_access'], 'no mode event takes it out');
    // A take-back with nothing standing changes nothing and says nothing.
    r.chat.takeBackFullAccess();
    assert.deepEqual(r.standings, ['taken_back:none']);
    assert.equal((await r.chat.create(undefined, 'claude')).mode, 'ask_first', 'a new chat is in Ask first again');
    await r.chat.setMode(other.id, 'full_access', 'claude');
    assert.equal(r.confirms, 1, 'the next entry asks');
    assert.deepEqual(r.standings, ['taken_back:none', 'given:menu']);
});

test('a tool that ran unchecked takes Always back as well, and says so; without Always the sentence is the one it was', async (t) => {
    const r = rig(t);
    stand(r, true);
    const { id } = await r.chat.create(undefined, 'claude');
    plan(r, [{ unhooked: { tool: 'Bash', input: { command: 'git status' } } }]);
    const said = await r.turn(id, 'go');
    assert.deepEqual(done(said), { kind: 'done', error: unguardedStandingSentence });
    assert.equal(r.chat.open(id).mode, 'ask_first');
    assert.equal(r.chat.fullAccessStanding(), null);
    assert.ok(!existsSync(r.b.paths.fullAccess));
    assert.deepEqual(r.standings, ['unchecked:none']);

    const plain = rig(t, { answer: true });
    const made = await plain.chat.create();
    await plain.chat.setMode(made.id, 'full_access', 'claude');
    plan(plain, [{ unhooked: { tool: 'Bash', input: { command: 'git status' } } }]);
    assert.deepEqual(done(await plain.turn(made.id, 'go')), { kind: 'done', error: unguardedSentence });
    assert.deepEqual(plain.standings, []);
});

test('an unchecked tool whose take-back the file will not take: Always is out of force for this run, the chat says so in one line, and the next start removes it', async (t) => {
    const unwritten: string[] = [];
    const r = rig(t, { answer: { always: false, newChats: false }, chat: { standingUnwritten: (code) => void unwritten.push(code) } });
    stand(r, true);
    const { id } = await r.chat.create(undefined, 'claude');
    const release = holdOpen(t, r.b.paths.fullAccess);
    plan(r, [{ unhooked: { tool: 'Bash', input: { command: 'git status' } } }]);
    const said = await r.turn(id, 'go');
    assert.deepEqual(done(said), { kind: 'done', error: unguardedStandingSentence });
    assert.deepEqual(notices(said).filter((text) => !text.startsWith('Full access has ended.')), [standingUnremovedSentence], 'one line, beside the line of the grant folder');
    assert.deepEqual(unwritten, ['EBUSY']);
    assert.ok(readStanding(r.b.paths.fullAccess, 'claude') !== null, 'the file still says Always');
    assert.ok(existsSync(takenBackMark(r.b.paths.fullAccess)), 'the take-back is marked for the next start');
    assert.equal(r.chat.fullAccessStanding(), null, 'the menu and the System screen hear none');
    assert.deepEqual(r.standings, ['unchecked:none']);
    // The box comes back, and a new chat with "start new chats" in the file stays in Ask first.
    assert.equal((await r.chat.create(undefined, 'claude')).mode, 'ask_first');
    const other = await r.chat.create();
    await r.chat.setMode(other.id, 'full_access', 'claude');
    assert.equal(r.confirms, 1);

    // A start while the file is still held keeps it out of force; the next one that can remove it does, before any chat.
    const still = r.again();
    assert.equal(still.fullAccessStanding(), null);
    assert.equal((await still.create(undefined, 'claude')).mode, 'ask_first');
    release();
    const next = r.again();
    assert.ok(!existsSync(r.b.paths.fullAccess), 'removed at the start');
    assert.ok(!existsSync(takenBackMark(r.b.paths.fullAccess)));
    assert.equal(next.fullAccessStanding(), null);
});

test('Always given again after a take-back was owed clears the mark first, so the next start keeps it', async (t) => {
    const r = rig(t);
    stand(r, false);
    writeFileSync(takenBackMark(r.b.paths.fullAccess), '');
    const held = r.again();
    assert.equal(held.fullAccessStanding(), null, 'the start removed it');
    assert.ok(!existsSync(r.b.paths.fullAccess));
    const { id } = await held.create();
    writeFileSync(takenBackMark(r.b.paths.fullAccess), '');
    await held.setMode(id, 'full_access', 'claude');
    assert.ok(!existsSync(takenBackMark(r.b.paths.fullAccess)));
    assert.notEqual(r.again().fullAccessStanding(), null, 'Always given after the mark stands at the next start');
});

test('Always answered while a take-back is owed and the file will not be replaced: the mark stays, so the next start shows the box', async (t) => {
    const r = rig(t);
    stand(r, true);
    writeFileSync(takenBackMark(r.b.paths.fullAccess), '');
    holdOpen(t, r.b.paths.fullAccess, { replace: true });
    const held = r.again();
    const { id } = await held.create();
    assert.equal((await held.setMode(id, 'full_access', 'claude')).mode, 'full_access');
    assert.equal(r.confirms, 1);
    assert.deepEqual(notices(r.events.get(id) ?? []), [standingUnkeptSentence]);
    assert.ok(existsSync(takenBackMark(r.b.paths.fullAccess)), 'the mark goes only after the new choice is written');
    const next = r.again();
    assert.equal(next.fullAccessStanding(), null, 'the old Always is not back');
    assert.equal((await next.create(undefined, 'claude')).mode, 'ask_first', 'nor its new chats');
    const other = await next.create();
    await next.setMode(other.id, 'full_access', 'claude');
    assert.equal(r.confirms, 2, 'the box');
});

test('Always written while the mark of a take-back owed will not go answers as this time only, and the next start removes it', async (t) => {
    const r = rig(t);
    const mark = takenBackMark(r.b.paths.fullAccess);
    writeFileSync(mark, '');
    holdOpen(t, mark);
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'full_access', 'claude');
    assert.deepEqual(notices(r.events.get(id) ?? []), [standingUnkeptSentence]);
    assert.equal(r.chat.fullAccessStanding(), null);
    assert.deepEqual(r.standings, [], 'nothing given is said to the page');
});

test('a start whose folder takes no new file holds a standing choice as taken back for the run', async (t) => {
    const r = rig(t);
    stand(r, true);
    const original = fs.writeFileSync;
    fs.writeFileSync = ((path: fs.PathOrFileDescriptor, data: string | NodeJS.ArrayBufferView, options?: fs.WriteFileOptions) => {
        if (String(path).endsWith('.probe')) throw Object.assign(new Error('EACCES: permission denied'), { code: 'EACCES' });
        original(path, data, options);
    }) as typeof fs.writeFileSync;
    syncBuiltinESMExports();
    t.after(() => {
        fs.writeFileSync = original;
        syncBuiltinESMExports();
    });
    const held = r.again();
    assert.equal(held.fullAccessStanding(), null);
    assert.equal((await held.create(undefined, 'claude')).mode, 'ask_first');
    fs.writeFileSync = original;
    syncBuiltinESMExports();
    assert.notEqual(r.again().fullAccessStanding(), null, 'a folder that takes files again lets it stand');
    assert.deepEqual(readdirSync(dirname(r.b.paths.fullAccess)).filter((name) => name.endsWith('.probe')), [], 'no probe is left');
});

test('the menu\'s take-back the file will not take is refused with what to do, and Always still stands', async (t) => {
    const r = rig(t);
    stand(r, false);
    holdOpen(t, r.b.paths.fullAccess);
    assert.throws(() => r.chat.takeBackFullAccess(), { message: 'Always could not be taken back: its file could not be changed. Close anything that has it open, then try again.' });
    assert.notEqual(r.chat.fullAccessStanding(), null);
    assert.deepEqual(r.standings, []);
});

test('a take-back while a new chat waits for the version read reaches that chat: it starts in Ask first', async (t) => {
    const r = rig(t);
    stand(r, true);
    const made = r.chat.create(undefined, 'claude');
    r.chat.takeBackFullAccess();
    assert.equal((await made).mode, 'ask_first');
    assert.equal(grants(r).length, 0);
});

test('a new chat whose Full access folder cannot be made starts in Ask first and says why in one line, once the page can draw it', async (t) => {
    const r = rig(t);
    stand(r, true);
    mkdirSync(dirname(r.b.paths.chatFull), { recursive: true });
    writeFileSync(r.b.paths.chatFull, 'not a folder');
    const setUp = await r.chat.create(undefined, 'claude');
    assert.equal(setUp.mode, 'ask_first');
    assert.deepEqual(notices(r.events.get(setUp.id) ?? []), [], 'nothing before the page has the chat');
    await tick();
    assert.deepEqual(notices(r.events.get(setUp.id) ?? []), [newChatAskFirstSentence]);
    const campaign = await r.chat.create('cmp_01GHI', 'claude');
    assert.equal(campaign.mode, 'ask_first');
    await tick();
    assert.deepEqual(notices(r.events.get(campaign.id) ?? []), [], 'a campaign\'s chat is told when the page opens it');
    r.chat.open(campaign.id);
    await tick();
    assert.deepEqual(notices(r.events.get(campaign.id) ?? []), [newChatAskFirstSentence]);
    r.chat.open(campaign.id);
    await tick();
    assert.equal(notices(r.events.get(campaign.id) ?? []).length, 1, 'once');
});

test('with Always and "start new chats" standing, a held card in Full access still offers no Always', async (t) => {
    const r = rig(t);
    stand(r, true);
    const { id } = await r.chat.create(undefined, 'claude');
    plan(r, [{ use: { tool: 'Bash', input: { command: 'ls *.md' } } }]);
    const from = r.events.get(id)?.length ?? 0;
    const finished = r.until(id, (event) => event.kind === 'done');
    await r.chat.send(id, 'go', claude);
    const card = await r.until(id, (event) => event.kind === 'permission');
    assert.equal(card.kind === 'permission' ? card.title : '', 'Full access asks about this one');
    assert.equal(card.kind === 'permission' ? card.remember : 'x', null);
    if (card.kind === 'permission') r.chat.answer(id, card.request, { allow: false, always: true });
    await finished;
    assert.ok((r.events.get(id) ?? []).slice(from).some((event) => event.kind === 'text' && event.text.startsWith('tool refused')));
});

test('a choice that cannot be written still gives Full access this time, says so in one line, and the box comes back next time', async (t) => {
    const unwritten: string[] = [];
    const s = rig(t, { chat: { standingUnwritten: (code) => void unwritten.push(code) } });
    // A folder where the file would be: the write fails.
    mkdirSync(s.b.paths.fullAccess, { recursive: true });
    const { id } = await s.chat.create();
    assert.equal((await s.chat.setMode(id, 'full_access', 'claude')).mode, 'full_access');
    assert.equal(unwritten.length, 1);
    assert.equal(s.chat.fullAccessStanding(), null);
    const said = s.events.get(id) ?? [];
    assert.deepEqual(said.filter((event) => event.kind === 'notice'), [{ kind: 'notice', text: standingUnkeptSentence }]);
    assert.deepEqual(modes(said), ['full_access'], 'the line follows the mode it was given');
    assert.ok(said.findIndex((event) => event.kind === 'mode') < said.findIndex((event) => event.kind === 'notice'));
    assert.deepEqual(readdirSync(dirname(s.b.paths.fullAccess)).filter((name) => name.endsWith('.tmp')), [], 'no file is left half-written beside it');
    const other = await s.chat.create();
    await s.chat.setMode(other.id, 'full_access', 'claude');
    assert.equal(s.confirms, 2);
});
