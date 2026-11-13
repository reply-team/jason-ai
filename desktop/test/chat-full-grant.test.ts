// A Full access grant's own folder through whole turns against stub hosts: each confirmation makes a folder, only that
// conversation's Full access turns run in it, leaving Full access (or closing the window) sends the next turn back to the
// chat folder with the session resuming, the ended folder is kept seven days and said once, and the sweep removes it after.
// Also the added-folder rule: a folder whose `.claude` holds skills, agents or commands is left out and said once.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent, type ChatOptions } from '../src/chat.ts';
import { readFence } from '../src/chat-deny.ts';
import { endedName, keptMs, newGrantId } from '../src/chat-full-folder.ts';
import { feedFullAccess } from '../src/chat-modes.ts';
import { startHost, type HostProcess } from '../src/chat-process.ts';
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
            confirmFullAccess: async () => true,
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
const same = (a: string, b: string) => realpathSync.native(a) === realpathSync.native(b);

async function settled(check: () => boolean): Promise<void> {
    const until = Date.now() + 10_000;
    while (!check() && Date.now() < until) await new Promise((resolve) => setTimeout(resolve, 20));
    assert.ok(check(), 'it did not settle');
}

test('each confirmation makes a folder of its own, and the conversation\'s Full access turns run there and nowhere else, the folder never added', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'full_access', 'claude');
    const made = readdirSync(r.b.paths.chatFull);
    assert.equal(made.length, 1);
    await r.turn(id, 'go', claude);
    const host = r.hosts()[0];
    assert.ok(host !== undefined && same(host.cwd, join(r.b.paths.chatFull, made[0] ?? '')), `the turn ran in ${host?.cwd}`);
    assert.ok(!host.args.includes('--add-dir'), 'the grant\'s folder is never added to a turn');
    assert.ok(!host.args.includes(join(r.b.paths.chatFull, made[0] ?? '')), 'nothing on the command line hands it over as an argument');
    const other = await r.chat.create();
    await r.turn(other.id, 'hello', claude);
    assert.ok(same(r.hosts()[1]?.cwd ?? '', r.b.paths.chat), 'a conversation in Ask first runs in the chat folder');
    await r.chat.setMode(id, 'full_access', 'claude');
    assert.equal(readdirSync(r.b.paths.chatFull).length, 1, 'a chat already in Full access makes no second folder');
});

test('two conversations in Full access have two folders, and a grant given again after it ended has a new one', async (t) => {
    const r = rig(t);
    const one = await r.chat.create();
    const two = await r.chat.create();
    await r.chat.setMode(one.id, 'full_access', 'claude');
    await r.chat.setMode(two.id, 'full_access', 'claude');
    assert.equal(readdirSync(r.b.paths.chatFull).length, 2);
    await r.chat.setMode(one.id, 'ask_first');
    assert.equal(readdirSync(r.b.paths.chatFull).length, 1, 'only the folder of the one that ended moved');
    await r.chat.setMode(one.id, 'full_access', 'claude');
    assert.equal(readdirSync(r.b.paths.chatFull).length, 2);
    assert.equal(readdirSync(r.b.paths.chatFullEnded).length, 1);
});

test('leaving Full access sends the next turn back to the chat folder, the session resuming, and moves the folder to the ended place', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'full_access', 'claude');
    const grant = readdirSync(r.b.paths.chatFull)[0] ?? '';
    writeFileSync(join(r.b.paths.chatFull, grant, 'work.txt'), 'what the agent made');
    await r.turn(id, 'go', claude);
    await r.chat.setMode(id, 'ask_first');
    assert.deepEqual(readdirSync(r.b.paths.chatFull), []);
    const [name] = readdirSync(r.b.paths.chatFullEnded);
    assert.equal(name, endedName(grant, r.clock.now));
    assert.equal(readFileSync(join(r.b.paths.chatFullEnded, name ?? '', 'work.txt'), 'utf8'), 'what the agent made');
    await r.turn(id, 'and now?', claude);
    const second = r.hosts()[1];
    assert.ok(same(second?.cwd ?? '', r.b.paths.chat));
    assert.equal(flag(second?.args ?? [], '--resume'), 'stub-session-1');
});

test('the chat says once, with the folder\'s path and an Open folder button, that the folder is kept seven days; the button opens that folder and no other', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'full_access', 'claude');
    const grant = readdirSync(r.b.paths.chatFull)[0] ?? '';
    await r.chat.setMode(id, 'ask_first');
    const said = notices(r.events.get(id));
    assert.equal(said.length, 1);
    const path = join(r.b.paths.chatFullEnded, endedName(grant, r.clock.now));
    assert.equal(said[0]?.folder, endedName(grant, r.clock.now));
    assert.ok(said[0]?.text.includes(path), said[0]?.text);
    assert.match(said[0]?.text ?? '', /seven days/);
    await r.turn(id, 'again', claude);
    await r.chat.setMode(id, 'plan_only');
    assert.equal(notices(r.events.get(id)).length, 1, 'a mode change that is not leaving Full access says nothing');
    await r.chat.openEndedFolder(said[0]?.folder);
    assert.deepEqual(r.opened, [path]);
    await assert.rejects(r.chat.openEndedFolder('..'), /not a folder/);
    await assert.rejects(r.chat.openEndedFolder(endedName(newGrantId(), r.clock.now)), /not a folder/);
    await assert.rejects(r.chat.openEndedFolder(undefined), /not a folder/);
    assert.deepEqual(r.opened, [path], 'nothing else was opened');
});

test('closing the window ends the grants too: the folder moves to the ended place, and nothing is said to a page that is gone', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'full_access', 'claude');
    r.chat.closeAll();
    assert.deepEqual(readdirSync(r.b.paths.chatFull), []);
    assert.equal(readdirSync(r.b.paths.chatFullEnded).length, 1);
    assert.deepEqual(notices(r.events.get(id)), []);
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

test('a grant folder that could not be made is reported under the step make, with a code, and Full access is not given', async (t) => {
    const steps: string[] = [];
    const r = rig(t, { chat: { fullFolderFailed: (step) => void steps.push(step) } });
    const { id } = await r.chat.create();
    // A file where the folders of grants belong makes the folder impossible to make.
    mkdirSync(r.b.paths.desktop, { recursive: true });
    writeFileSync(r.b.paths.chatFull, 'in the way');
    await assert.rejects(r.chat.setMode(id, 'full_access', 'claude'), { message: 'The folder for Full access could not be made, so Full access was not given.' });
    assert.deepEqual(steps, ['make']);
});

test('a folder a host still holds is left in place and a code is said; the next start moves it', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'full_access', 'claude');
    await r.turn(id, 'go', claude);
    // A file where the ended place should be makes the move fail.
    mkdirSync(r.b.paths.desktop, { recursive: true });
    writeFileSync(r.b.paths.chatFullEnded, 'in the way');
    await r.chat.setMode(id, 'ask_first');
    assert.equal(readdirSync(r.b.paths.chatFull).length, 1);
    assert.equal(r.codes.length, 1);
    assert.match(r.codes[0] ?? '', /^E[A-Z]+$/);
    assert.deepEqual(notices(r.events.get(id)), [], 'nothing is said of a folder that did not move');
    // A file the agent wrote into the folder names nothing: only the desktop's own record, outside it, is read.
    const bystander = await r.chat.create();
    await r.turn(bystander.id, 'hello', claude);
    writeFileSync(join(r.b.paths.chatFull, readdirSync(r.b.paths.chatFull)[0] ?? '', '.grant-conversation'), bystander.id);
    rmSync(r.b.paths.chatFullEnded);
    const next = r.again();
    await settled(() => readdirSync(r.b.paths.chatFull).length === 0);
    assert.equal(readdirSync(r.b.paths.chatFullEnded).length, 1);
    // The start that moved it says so once, when the conversation is opened, with the folder it moved to.
    assert.deepEqual(notices(r.events.get(id)), []);
    next.open(id);
    await settled(() => notices(r.events.get(id)).length === 1);
    const moved = readdirSync(r.b.paths.chatFullEnded)[0];
    assert.equal(notices(r.events.get(id))[0]?.folder, moved);
    assert.ok(notices(r.events.get(id))[0]?.text.includes(join(r.b.paths.chatFullEnded, moved ?? '')));
    next.open(id);
    await new Promise((resolve) => setTimeout(resolve, 100));
    assert.equal(notices(r.events.get(id)).length, 1, 'said once');
    next.open(bystander.id);
    await new Promise((resolve) => setTimeout(resolve, 100));
    assert.deepEqual(notices(r.events.get(bystander.id)), [], 'a forged marker in the folder tells nothing to another conversation');
});

test('the record of whom a grant was for is removed with its folder after the seven days', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'full_access', 'claude');
    const grant = readdirSync(r.b.paths.chatFull)[0] ?? '';
    assert.equal(readFileSync(join(r.b.paths.chatFullRecords, grant), 'utf8'), id);
    assert.ok(!existsSync(join(r.b.paths.chatFull, grant, '.grant-conversation')), 'nothing about it is written inside the folder');
    await r.chat.setMode(id, 'ask_first');
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
    await r.turn(id, 'ask', claude);
    await r.chat.setMode(id, 'plan_only');
    await r.turn(id, 'plan', claude);
    await r.chat.setMode(id, 'full_access', 'claude');
    const during = r.turn(id, 'full', claude);
    await during;
    const [asked, planned, full] = r.hosts();
    assert.equal(JSON.parse(flag(asked?.args ?? [], '--settings') ?? '{}').disableSkillShellExecution, true);
    assert.equal(JSON.parse(flag(planned?.args ?? [], '--settings') ?? '{}').disableSkillShellExecution, true);
    // The Full access turn's file is removed with the turn; its text is the function's (`fullAccessSettings`), tested apart.
    assert.match(flag(full?.args ?? [], '--settings') ?? '', /\.settings\.json$/);
});

test('a Full access turn is refused the chat folder and the other grants\' folders by the host\'s own rules, and may use its own', async (t) => {
    const r = rig(t);
    const { id } = await r.chat.create();
    const other = await r.chat.create();
    await r.chat.setMode(other.id, 'full_access', 'claude');
    await r.chat.setMode(id, 'full_access', 'claude');
    await r.turn(id, 'go', claude);
    const rules = (r.hosts()[0]?.args ?? []).filter((arg) => /^(Read|Edit)\(/.test(arg));
    const mine = readdirSync(r.b.paths.chatFull).filter((name) => same(join(r.b.paths.chatFull, name), r.hosts()[0]?.cwd ?? ''));
    const others = readdirSync(r.b.paths.chatFull).filter((name) => !mine.includes(name));
    assert.equal(mine.length, 1);
    assert.equal(others.length, 1);
    const names = (name: string) => rules.some((rule) => rule.includes(name));
    assert.ok(names(others[0] ?? ''), 'the other grant\'s folder is refused');
    assert.ok(names('chat-full-ended'), 'the ended place is refused');
    assert.ok(!rules.some((rule) => rule.endsWith(`${mine[0]})`) || rule.endsWith(`${mine[0]}/**)`)), 'its own folder is not');
    assert.ok(rules.some((rule) => rule.endsWith('chat)') || rule.endsWith('chat/**)')), 'the shared chat folder is refused to it');
});

test('a campaign folder whose .claude holds skills, agents or commands is left out and said once with its name and the reason; one that holds none is added', async (t) => {
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
        assert.match(left[0]?.text ?? '', /skills, agents or commands/);
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

/** A confirmation the row answers: each box opened is counted, and stays open until the row says yes or no. */
function heldBox() {
    const open: ((yes: boolean) => void)[] = [];
    let opened = 0;
    return {
        confirm: () =>
            new Promise<boolean>((resolve) => {
                opened++;
                open.push(resolve);
            }),
        opened: () => opened,
        answer(yes: boolean) {
            for (const resolve of open.splice(0)) resolve(yes);
        },
    };
}

test('two requests for Full access while the first box is open show one box and give one grant', async (t) => {
    const box = heldBox();
    const r = rig(t, { chat: { confirmFullAccess: box.confirm } });
    const { id } = await r.chat.create();
    const first = r.chat.setMode(id, 'full_access', 'claude');
    await settled(() => box.opened() === 1);
    const second = await r.chat.setMode(id, 'full_access', 'claude');
    assert.equal(second.mode, 'ask_first', 'the second request is answered at once with the mode as it is');
    assert.equal(box.opened(), 1, 'no second box');
    box.answer(true);
    assert.equal((await first).mode, 'full_access');
    assert.equal(box.opened(), 1);
    assert.equal(readdirSync(r.b.paths.chatFull).length, 1, 'one grant, one folder');
    await r.chat.setMode(id, 'ask_first');
    assert.deepEqual(readdirSync(r.b.paths.chatFull), [], 'the one grant ended, and no folder was left behind unended');
    // The mark is gone with the box: a later choice asks again.
    const again = r.chat.setMode(id, 'full_access', 'claude');
    await settled(() => box.opened() === 2);
    box.answer(false);
    assert.equal((await again).mode, 'ask_first');
});

test('a yes that arrives after the window closed opens nothing', async (t) => {
    const box = heldBox();
    const r = rig(t, { chat: { confirmFullAccess: box.confirm } });
    const { id } = await r.chat.create();
    const asking = r.chat.setMode(id, 'full_access', 'claude');
    await settled(() => box.opened() === 1);
    r.chat.closeAll();
    box.answer(true);
    assert.equal((await asking).mode, 'ask_first');
    assert.ok(!existsSync(r.b.paths.chatFull) || readdirSync(r.b.paths.chatFull).length === 0, 'no grant folder was made');
    assert.ok(!existsSync(r.b.paths.chatFullRecords) || readdirSync(r.b.paths.chatFullRecords).length === 0, 'no grant was recorded');
    assert.deepEqual(r.events.get(id)?.filter((event) => event.kind === 'mode') ?? [], []);
});

/**
 * A host whose exit the row lets through: the real stub host runs and a stop ends it as ever, but the chat hears of its
 * exit only once the row opens the gate.
 */
function gatedHosts() {
    let open: () => void = () => {};
    const gate = new Promise<void>((resolve) => {
        open = resolve;
    });
    let exited = false;
    const start: typeof startHost = (launch, args, options) => {
        const host = startHost(launch, args, options);
        const gated: HostProcess = {
            stop: () => host.stop(),
            stderrTail: () => host.stderrTail(),
            exited: Promise.all([host.exited, gate]).then(([exit]) => {
                exited = true;
                return exit;
            }),
        };
        return gated;
    };
    return { start, open: () => open(), exited: () => exited };
}

/** Sends a Full access turn that runs until it is stopped, and resolves once its host has said its first words. */
async function runningTurn(r: Rig, id: string): Promise<void> {
    r.b.env.STUB_HOST_PLAN = JSON.stringify([{ say: 'working' }, { hold: join(r.b.root, 'never-made') }]);
    await r.chat.send(id, 'go', claude);
    await settled(() => (r.events.get(id) ?? []).some((event) => event.kind === 'text' && event.text === 'working'));
}

test('leaving Full access during a turn stops the turn first and moves its folder only once the host has exited, then says so once', async (t) => {
    const hosts = gatedHosts();
    const r = rig(t, { chat: { startHost: hosts.start, grantEndWaitMs: 60_000 } });
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'full_access', 'claude');
    const grant = readdirSync(r.b.paths.chatFull)[0] ?? '';
    await runningTurn(r, id);
    await r.chat.setMode(id, 'ask_first');
    await new Promise((resolve) => setTimeout(resolve, 300));
    assert.equal(hosts.exited(), false);
    assert.deepEqual(readdirSync(r.b.paths.chatFull), [grant], 'no rename before the host has exited');
    assert.deepEqual(notices(r.events.get(id)), [], 'nothing is said of a folder that has not moved');
    hosts.open();
    await settled(() => notices(r.events.get(id)).length === 1);
    assert.deepEqual(readdirSync(r.b.paths.chatFull), []);
    const [name] = readdirSync(r.b.paths.chatFullEnded);
    assert.equal(notices(r.events.get(id))[0]?.folder, name);
    assert.ok(name?.startsWith(`${grant}-`));
    await settled(() => (r.events.get(id) ?? []).some((event) => event.kind === 'done'));
    await new Promise((resolve) => setTimeout(resolve, 100));
    assert.equal(notices(r.events.get(id)).length, 1, 'said once');
    assert.deepEqual(r.codes, []);
});

test('a move that fails at the end of the wait is not forgotten: it is tried again when the host has exited, and said once when the folder moved', async (t) => {
    const hosts = gatedHosts();
    const r = rig(t, { chat: { startHost: hosts.start, grantEndWaitMs: 50 } });
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'full_access', 'claude');
    const grant = readdirSync(r.b.paths.chatFull)[0] ?? '';
    await runningTurn(r, id);
    // A file where the ended place should be makes the first move fail.
    writeFileSync(r.b.paths.chatFullEnded, 'in the way');
    await r.chat.setMode(id, 'ask_first');
    await settled(() => r.codes.length === 1);
    assert.deepEqual(readdirSync(r.b.paths.chatFull), [grant]);
    assert.deepEqual(notices(r.events.get(id)), []);
    rmSync(r.b.paths.chatFullEnded);
    hosts.open();
    await settled(() => notices(r.events.get(id)).length === 1);
    assert.deepEqual(readdirSync(r.b.paths.chatFull), []);
    assert.equal(notices(r.events.get(id))[0]?.folder, readdirSync(r.b.paths.chatFullEnded)[0]);
    await settled(() => (r.events.get(id) ?? []).some((event) => event.kind === 'done'));
    await new Promise((resolve) => setTimeout(resolve, 100));
    assert.equal(notices(r.events.get(id)).length, 1, 'said once');
    assert.equal(r.codes.length, 1);
});

test('closing the window during a Full access turn stops it first and moves the folder once its host has exited, saying nothing', async (t) => {
    const hosts = gatedHosts();
    const r = rig(t, { chat: { startHost: hosts.start, grantEndWaitMs: 60_000 } });
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'full_access', 'claude');
    const grant = readdirSync(r.b.paths.chatFull)[0] ?? '';
    await runningTurn(r, id);
    r.chat.closeAll();
    await new Promise((resolve) => setTimeout(resolve, 300));
    assert.deepEqual(readdirSync(r.b.paths.chatFull), [grant], 'no rename before the host has exited');
    hosts.open();
    await settled(() => readdirSync(r.b.paths.chatFull).length === 0);
    assert.equal(readdirSync(r.b.paths.chatFullEnded).length, 1);
    assert.deepEqual(notices(r.events.get(id)), []);
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

test('a move that failed with no host in the folder is tried again at the daily sweep, and said once when the folder moved', async (t) => {
    const r = rig(t, { chat: { sweepEveryMs: 50 } });
    const { id } = await r.chat.create();
    await r.chat.setMode(id, 'full_access', 'claude');
    mkdirSync(r.b.paths.desktop, { recursive: true });
    writeFileSync(r.b.paths.chatFullEnded, 'in the way');
    await r.chat.setMode(id, 'ask_first');
    assert.equal(r.codes.length >= 1, true);
    assert.deepEqual(notices(r.events.get(id)), []);
    rmSync(r.b.paths.chatFullEnded);
    await settled(() => notices(r.events.get(id)).length === 1);
    assert.deepEqual(readdirSync(r.b.paths.chatFull), []);
    await new Promise((resolve) => setTimeout(resolve, 300));
    assert.equal(notices(r.events.get(id)).length, 1, 'said once');
});

test('Full access is refused for the Feed, in its own sentence, before any box opens', async (t) => {
    const box = heldBox();
    const r = rig(t, { chat: { confirmFullAccess: box.confirm } });
    const feed = r.chat.feed([]);
    await assert.rejects(r.chat.setMode(feed.id, 'full_access', 'claude'), { message: feedFullAccess });
    assert.equal(feedFullAccess, 'Full access is not offered for the Feed: its turns are handed lines that others and providers wrote without you asking for them.');
    assert.equal(box.opened(), 0, 'no box');
    assert.ok(!existsSync(r.b.paths.chatFull) || readdirSync(r.b.paths.chatFull).length === 0, 'no grant');
    assert.equal(r.chat.open(feed.id).mode, 'ask_first');
    await r.chat.setMode(feed.id, 'plan_only');
    assert.equal(r.chat.open(feed.id).mode, 'plan_only', 'the other modes are the Feed\'s to choose');
});
