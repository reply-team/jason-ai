// The person's memory files the chat is given: read by the desktop from the folders the runtime records, each a regular
// file of one name in its own marked folder, never through a link, bounded in size and in time; handed to a host session
// after the header once, and again only when one changes; a level really absent said once, a level not read said
// nothing of, a file the person can mend named; given on every host and in every mode; never added as a folder; in the
// record as a digest and in the log as a closed word.

import assert from 'node:assert/strict';
import { linkSync, mkdirSync, readdirSync, readFileSync, rmSync, symlinkSync, unlinkSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent, type ChatOptions } from '../src/chat.ts';
import { readFence } from '../src/chat-deny.ts';
import { claudeTurnSettings, planTurnSettings } from '../src/chat-hosts.ts';
import { loggedTurns } from '../src/chat-log.ts';
import { digestShape, memoryBlock, memoryFileLimit, memoryToGive, type MemoryFile, type MemoryRead } from '../src/chat-memory.ts';
import { readMemoryFile, readMemoryWithin, readNoFollow } from '../src/chat-memory-files.ts';
import { fileLogger } from '../src/log.ts';
import { bench, outsideStandIn, placeSandboxFiles, type Bench } from './harness.ts';
import { NamedChat } from './named-chat.ts';

const fence = readFence(join(import.meta.dirname, '..'));
const campaignId = 'cmp_01ABC';
const projectId = 'prj_01STUB';

/** A folder made one of Jason's: its marker of that kind and id, and its memory file when there is text for it. */
function marked(folder: string, kind: 'root' | 'project' | 'campaign', id: string | null, text?: string): string {
    mkdirSync(join(folder, '.jason'), { recursive: true });
    writeFileSync(join(folder, '.jason', 'marker.json'), id === null ? `{"marker_version":1,"kind":"${kind}"}` : `{"marker_version":1,"kind":"${kind}","id":"${id}"}`);
    if (text !== undefined) writeFileSync(join(folder, 'AGENTS.md'), text);
    return folder;
}

const flagValue = (args: string[], flag: string) => args[args.indexOf(flag) + 1] ?? '';
const added = (args: string[]) => args.flatMap((arg, i) => (args[i - 1] === '--add-dir' ? [arg] : []));
const block = (stdin: string) => /<memory-files>\n([\s\S]*?)\n<\/memory-files>/.exec(stdin)?.[1] ?? null;
const fileOf = (read: MemoryRead | null): MemoryFile | null => (read?.kind === 'file' ? read : null);
const names = {
    client: "The client's memory file, the person's standing rules for all of their work",
    project: "The project's memory file, the person's standing rules for this campaign's project",
    campaign: "The campaign's memory file, the person's standing rules for this campaign",
};

interface Rig {
    b: Bench;
    chat: NamedChat;
    raw: () => Chat;
    root: string;
    project: string;
    campaign: string;
    keys: Record<string, string>;
    turn(name: string, text: string, options: unknown): Promise<ChatEvent[]>;
    turnById(id: string, text: string, options: unknown): Promise<ChatEvent[]>;
    hosts(): ReturnType<Bench['record']>;
    record(name: string): Record<string, { session: string; files: Record<string, string> }> | undefined;
}

/** A chat on stub hosts, with a harness root, a project and a campaign the runtime records, each marked and with its file. */
function rig(t: { after(fn: () => void | Promise<void>): void }, chatOptions: (b: Bench) => Partial<ChatOptions> = () => ({})): Rig {
    const removals: (() => void)[] = [];
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude', 'codex'] });
    placeSandboxFiles(b);
    const bundle = join(b.root, 'bundle');
    mkdirSync(bundle, { recursive: true });
    // The root the runtime reports, not the one the environment names: a turn takes the runtime's.
    const root = marked(join(b.root, 'reported-root'), 'root', null, 'Client rule: sign every message as Sam.\n');
    const project = marked(join(root, 'projects', 'default'), 'project', projectId, 'Project rule: mention the pilot.\n');
    const campaign = marked(join(project, 'campaigns', 'spring'), 'campaign', campaignId, 'Campaign rule: Tuesday only.\n');
    b.env.STUB_HARNESS_ROOT = root;
    b.env.STUB_PROJECT_FOLDER = project;
    b.env.STUB_CAMPAIGN_FOLDER = campaign;
    const keys: Record<string, string> = {};
    const events = new Map<string, ChatEvent[]>();
    const waiters: { id: string; resolve: () => void }[] = [];
    // Each conversation is put in Ask first, whose settings travel inline, which the fence rows read.
    const named: NamedChat = new NamedChat(() => chat, { mode: 'ask_first' });
    const chat: Chat = new Chat({
        paths: b.paths,
        env: b.env,
        bundle,
        dataNamed: true,
        bundleVersion: null,
        cli: b.cli,
        programs: b.programs,
        keyFor: (id) => keys[id],
        fence: () => fence,
        emit(id, event) {
            const said = events.get(id) ?? [];
            said.push(event);
            events.set(id, said);
            if (event.kind === 'done') for (const waiter of waiters.filter((each) => each.id === id)) waiters.splice(waiters.indexOf(waiter), 1) && waiter.resolve();
        },
        sandboxOutside: outsideStandIn,
        fullAccessOffered: true,
        ...chatOptions(b),
    });
    t.after(async () => {
        chat.closeAll();
        await chat.idle();
        for (const remove of removals) remove();
    });
    const turnById = async (id: string, text: string, options: unknown) => {
        const from = events.get(id)?.length ?? 0;
        const done = new Promise<void>((resolve) => waiters.push({ id, resolve }));
        await chat.send(id, text, options);
        await done;
        return (events.get(id) ?? []).slice(from);
    };
    return {
        b,
        chat: named,
        raw: () => chat,
        root,
        project,
        campaign,
        keys,
        async turn(name, text, options) {
            const id = await named.made(name, (options as { campaignId?: unknown } | null)?.campaignId);
            const { campaignId: _campaign, ...rest } = (options ?? {}) as Record<string, unknown>;
            return turnById(id, text, rest);
        },
        turnById,
        hosts: () => b.record().filter((each) => (each.name === 'claude' || each.name === 'codex') && each.args[0] !== '--version' && each.args[0] !== 'sandbox'),
        record: (name) =>
            (JSON.parse(readFileSync(join(b.paths.conversations, `${named.idOf(name) as string}.json`), 'utf8')) as { memory_given?: Record<string, { session: string; files: Record<string, string> }> })
                .memory_given,
    };
}

const done = (events: ChatEvent[]) => events.at(-1);

// ---- Reading -------------------------------------------------------------------------------------------------------

test('a level is read only from a regular file in a folder carrying the marker of that level and that id', async (t) => {
    const b = bench(t);
    const root = marked(join(b.root, 'root'), 'root', null, '# Mine\nAlways be brief.\n');
    const read = fileOf(await readMemoryFile({ level: 'client', folder: root }));
    assert.equal(read?.text, '# Mine\nAlways be brief.\n');
    assert.equal(read?.path, join(root, 'AGENTS.md'));
    assert.equal(read?.cut, false);
    assert.match(read?.digest ?? '', digestShape);

    const project = marked(join(b.root, 'project'), 'project', projectId, 'words');
    assert.ok(fileOf(await readMemoryFile({ level: 'project', folder: project, id: projectId })));
    // A folder that is not the level's own has no read: nothing is said of it.
    assert.equal(await readMemoryFile({ level: 'project', folder: project, id: 'prj_01OTHER' }), null, 'a marker of another project');
    assert.equal(await readMemoryFile({ level: 'campaign', folder: project, id: projectId }), null, 'a marker of another level');
    assert.equal(await readMemoryFile({ level: 'client', folder: project }), null, 'a project folder is no root');
    const bare = join(b.root, 'bare');
    mkdirSync(bare, { recursive: true });
    writeFileSync(join(bare, 'AGENTS.md'), 'words');
    assert.equal(await readMemoryFile({ level: 'client', folder: bare }), null, 'a folder without a marker');
    assert.equal(await readMemoryFile({ level: 'client', folder: 'relative/root' }), null, 'a folder that is no absolute path');
    assert.equal(await readMemoryFile({ level: 'client', folder: join(b.root, 'not-there') }), null);
    const badMarker = marked(join(b.root, 'bad-marker'), 'root', null, 'words');
    writeFileSync(join(badMarker, '.jason', 'marker.json'), '{"marker_version":2,"kind":"root"}');
    assert.equal(await readMemoryFile({ level: 'client', folder: badMarker }), null, 'a marker of another version');
    const folderAtName = marked(join(b.root, 'folder-at-name'), 'root', null);
    mkdirSync(join(folderAtName, 'AGENTS.md'));
    assert.equal(await readMemoryFile({ level: 'client', folder: folderAtName }), null, 'a folder at the file name');

    // The marked folder read, and its file empty or not there: a real absence.
    const empty = marked(join(b.root, 'empty'), 'root', null, ' \n\n');
    assert.deepEqual(await readMemoryFile({ level: 'client', folder: empty }), { kind: 'absent', level: 'client' }, 'an empty file');
    const none = marked(join(b.root, 'none'), 'root', null);
    assert.deepEqual(await readMemoryFile({ level: 'client', folder: none }), { kind: 'absent', level: 'client' }, 'no file');
    // Not text: not given, and named so the person can mend it.
    const binary = marked(join(b.root, 'binary'), 'root', null, 'text\0more');
    assert.deepEqual(pick(await readMemoryFile({ level: 'client', folder: binary })), { kind: 'skipped', why: 'not_text' });
    const notUtf8 = marked(join(b.root, 'utf16'), 'root', null);
    writeFileSync(join(notUtf8, 'AGENTS.md'), Buffer.from('﻿Rules\n', 'utf16le'));
    assert.deepEqual(pick(await readMemoryFile({ level: 'client', folder: notUtf8 })), { kind: 'skipped', why: 'not_text' });
});

const pick = (read: MemoryRead | null) => (read?.kind === 'skipped' ? { kind: read.kind, why: read.why } : read);

test('a file the session was given and is found absent is looked at again a moment later: an editor’s save is no absence', async (t) => {
    let bare = 0;
    const r = rig(t, () => ({
        memoryReader: async (place, platform) => {
            if (place.level !== 'client' || bare === 0) return readMemoryFile(place, platform);
            bare--;
            return { kind: 'absent', level: 'client' };
        },
    }));
    await r.turn('c1', 'first', { host: 'claude', campaignId });
    // The next turn's first look finds the name bare, its second the file as it was.
    bare = 1;
    await r.turn('c1', 'second', { host: 'claude' });
    assert.equal(bare, 0, 'it was looked at');
    assert.equal(block(r.hosts()[1]?.stdin ?? ''), null, 'neither said gone nor given again unchanged');
    // Absent at both looks: gone.
    bare = 2;
    await r.turn('c1', 'third', { host: 'claude' });
    assert.deepEqual((block(r.hosts()[2]?.stdin ?? '') ?? '').split('\n'), [`${names.client} is now empty or no longer there: what Jason gave of it earlier in this conversation no longer holds.`]);
});

test('no link is followed: a linked folder or .jason has no read, and a file that is a link or a hard link is named, not given', async (t) => {
    const b = bench(t);
    const real = marked(join(b.root, 'real'), 'root', null, 'words');
    // A junction needs no privilege on Windows; elsewhere it is a symbolic link to a folder.
    const linkedFolder = join(b.root, 'linked-folder');
    symlinkSync(real, linkedFolder, 'junction');
    assert.equal(await readMemoryFile({ level: 'client', folder: linkedFolder }), null, 'the folder is a link');
    const linkedMarker = join(b.root, 'linked-marker');
    mkdirSync(linkedMarker);
    symlinkSync(join(real, '.jason'), join(linkedMarker, '.jason'), 'junction');
    writeFileSync(join(linkedMarker, 'AGENTS.md'), 'words');
    assert.equal(await readMemoryFile({ level: 'client', folder: linkedMarker }), null, 'its .jason is a link');

    // A file kept outside the marked folder, reached by a second name inside it: a hard link is a regular file, of the
    // same identity when opened, and is still not read.
    const outside = join(b.root, 'outside.md');
    writeFileSync(outside, 'words from elsewhere');
    const hard = marked(join(b.root, 'hard-link'), 'root', null);
    linkSync(outside, join(hard, 'AGENTS.md'));
    const read = await readMemoryFile({ level: 'client', folder: hard });
    assert.deepEqual(pick(read), { kind: 'skipped', why: 'linked' });
    assert.equal(read?.kind === 'skipped' ? read.path : null, join(hard, 'AGENTS.md'));
    assert.equal(await readNoFollow(join(hard, 'AGENTS.md'), 100), 'linked');
    assert.equal(JSON.stringify(read).includes('words from elsewhere'), false);

    const linkedFile = marked(join(b.root, 'linked-file'), 'root', null);
    try {
        symlinkSync(outside, join(linkedFile, 'AGENTS.md'), 'file');
    } catch (error) {
        // Windows makes a file link only with Developer Mode or an administrator's right.
        if ((error as NodeJS.ErrnoException).code === 'EPERM') return t.skip('this account may not make a file link');
        throw error;
    }
    assert.deepEqual(pick(await readMemoryFile({ level: 'client', folder: linkedFile })), { kind: 'skipped', why: 'linked' }, 'the file is a link to one outside');
    assert.equal(await readNoFollow(join(linkedFile, 'AGENTS.md'), 100), 'linked');
});

test('a file over the limit gives its first 32 KiB, a character the cut split left out, and says it was cut', async (t) => {
    const b = bench(t);
    // A three-byte character straddles the limit: 32767 bytes of a, then the character, then more.
    const text = `${'a'.repeat(memoryFileLimit - 1)}€ and the rest`;
    const root = marked(join(b.root, 'root'), 'root', null, text);
    const read = fileOf(await readMemoryFile({ level: 'client', folder: root }));
    assert.equal(read?.cut, true);
    assert.equal(read?.text, 'a'.repeat(memoryFileLimit - 1));
    const shown = memoryBlock(read === null ? [] : [{ read }]) ?? '';
    assert.ok(
        shown.includes(`That file is longer than Jason gives: these are its first 32 KiB. The whole file is at ${JSON.stringify(join(root, 'AGENTS.md'))}; tell the person, who can shorten it.`),
        shown.slice(-300),
    );
    // Exactly at the limit is whole.
    writeFileSync(join(root, 'AGENTS.md'), 'b'.repeat(memoryFileLimit));
    assert.equal(fileOf(await readMemoryFile({ level: 'client', folder: root }))?.cut, false);
});

test('a read that does not answer in time is said not to have answered, and the turn goes on', async () => {
    const place = { level: 'project' as const, folder: join(import.meta.dirname, 'nowhere-slow'), id: projectId };
    const started = Date.now();
    let release: (read: MemoryRead | null) => void = () => {};
    const read = await readMemoryWithin(place, 40, process.platform, () => new Promise((resolve) => (release = resolve)));
    assert.ok(Date.now() - started < 2000);
    assert.deepEqual(read, { kind: 'skipped', level: 'project', path: join(place.folder, 'AGENTS.md'), why: 'slow', digest: null });
    release(null);
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(await readMemoryWithin(place, 40, process.platform, async () => ({ kind: 'absent', level: 'project' })).then((each) => each?.kind), 'absent');
});

test('a place whose read is still pending is not read again: it is said not to have answered at once, until that read returns', async () => {
    const place = { level: 'client' as const, folder: join(import.meta.dirname, 'nowhere-pending') };
    let started = 0;
    let release: (read: MemoryRead | null) => void = () => {};
    const hung = () => {
        started++;
        return new Promise<MemoryRead | null>((resolve) => (release = resolve));
    };
    const slow = { kind: 'skipped', level: 'client', path: join(place.folder, 'AGENTS.md'), why: 'slow', digest: null };
    assert.deepEqual(await readMemoryWithin(place, 40, process.platform, hung), slow);
    // The limit is long, and the answer comes at once: no second read of the place was started.
    const before = Date.now();
    for (let turn = 0; turn < 5; turn++) assert.deepEqual(await readMemoryWithin(place, 60_000, process.platform, hung), slow);
    assert.ok(Date.now() - before < 2000);
    assert.equal(started, 1, 'one read of the place, however many turns ask');
    // Another place, or the same folder at another level, is read as ever.
    assert.equal((await readMemoryWithin({ ...place, folder: join(place.folder, 'other') }, 40, process.platform, async () => ({ kind: 'absent', level: 'client' })))?.kind, 'absent');
    // Once the hung read returns, the place is read again.
    release(null);
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal((await readMemoryWithin(place, 40, process.platform, async () => ({ kind: 'absent', level: 'client' })))?.kind, 'absent');
});

// ---- What a session is given ---------------------------------------------------------------------------------------

test('a session is given every file once, then only one that changed, a level really absent, a skip once, a slow read each time', () => {
    const file = (level: MemoryFile['level'], digest: string): MemoryFile => ({ kind: 'file', level, path: `/h/${level}/AGENTS.md`, text: level, cut: false, digest });
    const all = [file('client', 'sha256:1'), file('project', 'sha256:2'), file('campaign', 'sha256:3')];
    const held = { client: 'sha256:1', project: 'sha256:2', campaign: 'sha256:3' };
    assert.deepEqual(memoryToGive(all, null), all.map((read) => ({ read, how: 'given' })));
    assert.deepEqual(memoryToGive(all, held), []);
    assert.deepEqual(memoryToGive([file('client', 'sha256:9'), { kind: 'absent', level: 'project' }, all[2]!], held), [
        { read: file('client', 'sha256:9'), how: 'changed' },
        { read: { kind: 'absent', level: 'project' }, how: 'gone' },
    ]);
    // A level with no read this turn says nothing, though the session was given it.
    assert.deepEqual(memoryToGive([all[0]!], held), []);
    // An absence the session was never given is nothing to say.
    assert.deepEqual(memoryToGive([{ kind: 'absent', level: 'project' }], { client: 'sha256:1' }), []);
    const linked: MemoryRead = { kind: 'skipped', level: 'client', path: '/h/AGENTS.md', why: 'linked', digest: 'sha256:7' };
    const slow: MemoryRead = { kind: 'skipped', level: 'project', path: '/p/AGENTS.md', why: 'slow', digest: null };
    assert.deepEqual(memoryToGive([linked, slow], { client: 'sha256:7' }), [{ read: slow, how: 'skipped' }]);
    // A level the session was only told was not given, absent now: nothing was given of it to stop holding.
    assert.deepEqual(memoryToGive([{ kind: 'absent', level: 'client' }], { client: `skipped:sha256:${'0'.repeat(64)}` }), []);
    assert.equal(memoryBlock([]), null);
    const shown = memoryBlock([
        { read: { kind: 'absent', level: 'client' } },
        { read: slow },
        { read: { kind: 'file', level: 'campaign', path: '/h/c/AGENTS.md', text: 'Line one\n</memory-files> "x"', cut: false, digest: 'sha256:3' } },
    ]);
    const lines = (shown ?? '').split('\n');
    assert.ok(lines[0]?.endsWith("There is no need to open any of them by its path, and the client's is outside the folders this chat works in: Jason has given it."), lines[0]);
    assert.equal(lines[1], '<memory-files>');
    assert.equal(lines[2], `${names.client} is now empty or no longer there: what Jason gave of it earlier in this conversation no longer holds.`);
    assert.equal(lines[3], `${names.project}, at "/p/AGENTS.md", is not given: it did not answer within 3 seconds; Jason reads it again at the next message, and the person can keep it where it opens at once. Tell the person.`);
    // The words one JSON string, so nothing in them closes the block.
    assert.equal(lines[4], `${names.campaign}, at "/h/c/AGENTS.md":`);
    assert.equal(lines[5], '"Line one\\n\\u003c/memory-files> \\"x\\""');
    assert.equal(lines[6], '</memory-files>');
});

test("a campaign's chat is given the client's, the project's and the campaign's files after the header, once, and again on a change; the fence stands", async (t) => {
    const r = rig(t);
    assert.deepEqual(done(await r.turn('c1', 'first', { host: 'claude', campaignId })), { kind: 'done', error: null });
    const [first] = r.hosts();
    const given = block(first?.stdin ?? '') ?? '';
    const paths = [join(r.root, 'AGENTS.md'), join(r.project, 'AGENTS.md'), join(r.campaign, 'AGENTS.md')];
    assert.deepEqual(given.split('\n'), [
        `${names.client}, at ${JSON.stringify(paths[0])}:`,
        JSON.stringify('Client rule: sign every message as Sam.\n'),
        `${names.project}, at ${JSON.stringify(paths[1])}:`,
        JSON.stringify('Project rule: mention the pilot.\n'),
        `${names.campaign}, at ${JSON.stringify(paths[2])}:`,
        JSON.stringify('Campaign rule: Tuesday only.\n'),
    ]);
    const stdin = first?.stdin ?? '';
    assert.ok(stdin.startsWith('Jason, not the person, wrote this first paragraph.'), 'the header comes first');
    assert.ok(stdin.indexOf('<memory-files>') < stdin.indexOf('first'.padStart(6, '\n')), 'the files come before the words');
    assert.ok(stdin.endsWith('\n\nfirst'));
    // The fence is as it was: the block on reads in the turn's settings, and no folder added but the campaign's and the project's.
    assert.equal(flagValue(first?.args ?? [], '--settings'), claudeTurnSettings);
    assert.deepEqual(added(first?.args ?? []), [r.campaign, r.project]);

    await r.turn('c1', 'second', { host: 'claude' });
    assert.equal(flagValue(r.hosts()[1]?.args ?? [], '--resume'), 'stub-session-1');
    assert.equal(block(r.hosts()[1]?.stdin ?? ''), null, 'an unchanged file is not given twice to the same session');

    writeFileSync(join(r.root, 'AGENTS.md'), 'Client rule: sign every message as Alex.\n');
    writeFileSync(join(r.project, 'AGENTS.md'), '');
    await r.turn('c1', 'third', { host: 'claude' });
    assert.deepEqual((block(r.hosts()[2]?.stdin ?? '') ?? '').split('\n'), [
        `${names.client}, at ${JSON.stringify(paths[0])}:`,
        JSON.stringify('Client rule: sign every message as Alex.\n'),
        `${names.project} is now empty or no longer there: what Jason gave of it earlier in this conversation no longer holds.`,
    ]);

    // Start fresh: the next turn is a new session, given every file again.
    r.raw().restart(r.chat.idOf('c1'));
    await r.turn('c1', 'fourth', { host: 'claude' });
    assert.ok(!r.hosts()[3]?.args.includes('--resume'));
    assert.deepEqual(
        (block(r.hosts()[3]?.stdin ?? '') ?? '').split('\n').filter((line) => !line.startsWith('"')),
        [`${names.client}, at ${JSON.stringify(paths[0])}:`, `${names.campaign}, at ${JSON.stringify(paths[2])}:`],
    );

    // The record keeps a digest per level for the session, never the words.
    const record = readFileSync(join(r.b.paths.conversations, `${r.chat.idOf('c1') as string}.json`), 'utf8');
    for (const words of ['Client rule', 'Campaign rule', 'Project rule']) assert.equal(record.includes(words), false, words);
    const kept = r.record('c1');
    assert.deepEqual(Object.keys(kept ?? {}), ['claude']);
    assert.equal(kept?.claude?.session, 'stub-session-1');
    assert.deepEqual(Object.keys(kept?.claude?.files ?? {}), ['client', 'campaign']);
    for (const digest of Object.values(kept?.claude?.files ?? {})) assert.match(digest, digestShape);
});

test('a level the turn could not read says nothing, not "no longer holds", and is given again at the next turn that reads it', async (t) => {
    const r = rig(t);
    await r.turn('c1', 'first', { host: 'claude', campaignId });
    // The runtime does not answer for the project: its folder is not known this turn.
    r.b.env.STUB_PROJECT_GET_FAILS = 'yes';
    await r.turn('c1', 'second', { host: 'claude' });
    assert.equal(block(r.hosts()[1]?.stdin ?? ''), null, 'nothing is said of a level not read');
    assert.deepEqual(Object.keys(r.record('c1')?.claude?.files ?? {}), ['client', 'campaign'], 'and no record keeps it');
    delete r.b.env.STUB_PROJECT_GET_FAILS;
    await r.turn('c1', 'third', { host: 'claude' });
    assert.deepEqual((block(r.hosts()[2]?.stdin ?? '') ?? '').split('\n'), [`${names.project}, at ${JSON.stringify(join(r.project, 'AGENTS.md'))}:`, JSON.stringify('Project rule: mention the pilot.\n')]);
    // The folder no longer marked as the project's: no read, so nothing said either.
    rmSync(join(r.project, '.jason'), { recursive: true });
    await r.turn('c1', 'fourth', { host: 'claude' });
    assert.equal(block(r.hosts()[3]?.stdin ?? ''), null);
});

test('a read that does not answer in time is named to the agent each turn, and the file is given at the first turn that reads it', async (t) => {
    let hang = true;
    let reads = 0;
    let release: (read: MemoryRead | null) => void = () => {};
    const r = rig(t, () => ({
        memoryReadLimitMs: 50,
        memoryReader: (place, platform) => {
            if (place.level !== 'project' || !hang) return readMemoryFile(place, platform);
            reads++;
            return new Promise((resolve) => (release = resolve));
        },
    }));
    const slowLine = `${names.project}, at ${JSON.stringify(join(r.project, 'AGENTS.md'))}, is not given: it did not answer within 3 seconds; Jason reads it again at the next message, and the person can keep it where it opens at once. Tell the person.`;
    await r.turn('c1', 'first', { host: 'claude', campaignId });
    const lines = (block(r.hosts()[0]?.stdin ?? '') ?? '').split('\n').filter((line) => !line.startsWith('"'));
    assert.deepEqual(lines, [`${names.client}, at ${JSON.stringify(join(r.root, 'AGENTS.md'))}:`, slowLine, `${names.campaign}, at ${JSON.stringify(join(r.campaign, 'AGENTS.md'))}:`]);
    assert.equal(r.record('c1')?.claude?.files.project, undefined);
    // Still hung: said again, and no second read of it started.
    await r.turn('c1', 'second', { host: 'claude' });
    assert.deepEqual((block(r.hosts()[1]?.stdin ?? '') ?? '').split('\n'), [slowLine]);
    assert.equal(reads, 1);
    // It returns; the next turn reads it and gives it.
    hang = false;
    release(null);
    await r.turn('c1', 'third', { host: 'claude' });
    assert.deepEqual((block(r.hosts()[2]?.stdin ?? '') ?? '').split('\n'), [`${names.project}, at ${JSON.stringify(join(r.project, 'AGENTS.md'))}:`, JSON.stringify('Project rule: mention the pilot.\n')]);
});

test('a file the session was only told was not given, and that then goes absent, is not said to have stopped holding', async (t) => {
    const r = rig(t);
    const clientFile = join(r.root, 'AGENTS.md');
    unlinkSync(clientFile);
    const kept = join(r.b.root, 'kept-elsewhere.md');
    writeFileSync(kept, 'Rules kept elsewhere.\n');
    linkSync(kept, clientFile);
    await r.turn('c1', 'first', { host: 'claude', campaignId });
    assert.match(block(r.hosts()[0]?.stdin ?? '') ?? '', /is not given: it is a link/);
    assert.match(r.record('c1')?.claude?.files.client ?? '', /^skipped:sha256:[0-9a-f]{64}$/);
    unlinkSync(clientFile);
    await r.turn('c1', 'second', { host: 'claude' });
    assert.equal(block(r.hosts()[1]?.stdin ?? ''), null, 'nothing was given of it, so nothing is said to hold no longer');
    assert.equal(r.record('c1')?.claude?.files.client, undefined);
});

test('a file the person can mend is named once to the session, with what to do, and given once mended', async (t) => {
    const r = rig(t);
    const clientFile = join(r.root, 'AGENTS.md');
    unlinkSync(clientFile);
    const kept = join(r.b.root, 'kept-elsewhere.md');
    writeFileSync(kept, 'Rules kept elsewhere.\n');
    linkSync(kept, clientFile);
    writeFileSync(join(r.campaign, 'AGENTS.md'), Buffer.from('﻿Campaign rule\n', 'utf16le'));
    await r.turn('c1', 'first', { host: 'claude', campaignId });
    const first = r.hosts()[0]?.stdin ?? '';
    assert.deepEqual((block(first) ?? '').split('\n').filter((line) => !line.startsWith('"')), [
        `${names.client}, at ${JSON.stringify(clientFile)}, is not given: it is a link, or a name with more than one link, and Jason reads no memory file through one; the person can put the file itself there. Tell the person.`,
        `${names.project}, at ${JSON.stringify(join(r.project, 'AGENTS.md'))}:`,
        `${names.campaign}, at ${JSON.stringify(join(r.campaign, 'AGENTS.md'))}, is not given: it is not UTF-8 text; the person can save it as UTF-8. Tell the person.`,
    ]);
    assert.equal(first.includes('Rules kept elsewhere'), false);
    await r.turn('c1', 'second', { host: 'claude' });
    assert.equal(block(r.hosts()[1]?.stdin ?? ''), null, 'named once to the session');
    unlinkSync(clientFile);
    writeFileSync(clientFile, 'Client rule, mended.\n');
    await r.turn('c1', 'third', { host: 'claude' });
    assert.deepEqual((block(r.hosts()[2]?.stdin ?? '') ?? '').split('\n'), [`${names.client}, at ${JSON.stringify(clientFile)}:`, JSON.stringify('Client rule, mended.\n')]);
});

test('a set-up chat and the Feed are given the client’s file alone', async (t) => {
    const r = rig(t);
    await r.turn('setup', 'hello', { host: 'claude' });
    const feed = r.raw().feed([]);
    await r.turnById(feed.id, 'hello', { host: 'claude' });
    for (const host of r.hosts()) {
        const lines = (block(host.stdin) ?? '').split('\n');
        assert.equal(lines.length, 2, host.stdin);
        assert.match(lines[0]!, /^The client's memory file/);
        assert.deepEqual(added(host.args), [], 'no folder is added, the root least of all');
    }
});

test('a Codex turn, a turn on a provider key, Plan only and Full access are each given the files', async (t) => {
    const r = rig(t);
    r.keys.deepseek = 'sk-planted-deepseek';
    // A campaign has one conversation: each host keeps its own session in it, and Start fresh makes the next one new.
    const id = await r.chat.made('c1', campaignId);
    await r.turnById(id, 'hello', { host: 'codex' });
    await r.turnById(id, 'hello', { host: 'deepseek' });
    await r.raw().setMode(id, 'plan_only');
    await r.turnById(id, 'hello', { host: 'claude' });
    r.raw().restart(id);
    await r.raw().setMode(id, 'full_access', 'claude');
    await r.turnById(id, 'hello', { host: 'claude' });
    const hosts = r.hosts();
    assert.equal(hosts.length, 4);
    for (const host of hosts) {
        assert.ok(!host.args.includes('--resume') && !host.args.includes('resume'), 'each a new session');
        const lines = (block(host.stdin) ?? '').split('\n').filter((line) => !line.startsWith('"'));
        assert.deepEqual(lines.map((line) => line.split(',')[0]), ["The client's memory file", "The project's memory file", "The campaign's memory file"], host.name);
    }
    assert.ok(hosts[3]?.args.includes('--settings') && !hosts[3].args.includes(claudeTurnSettings), 'the last turn ran in Full access, on its settings file');
    // Codex is added no folder; the Plan only turn keeps its own settings, the block among them.
    assert.equal(hosts[0]?.name, 'codex');
    assert.ok(!hosts[0]?.args.includes('--add-dir'));
    assert.equal(flagValue(hosts[2]?.args ?? [], '--settings'), planTurnSettings);
});

test('the root the runtime does not report as marked gives no client file, and the environment’s root is never read', async (t) => {
    const r = rig(t);
    r.b.env.STUB_HARNESS_STATE = 'unmarked';
    // The root the environment names holds a file of its own, which a turn never reads.
    marked(r.b.env.JASON_Harness__Root!, 'root', null, 'Environment root rule.\n');
    await r.turn('c1', 'hello', { host: 'claude', campaignId });
    const shown = block(r.hosts()[0]?.stdin ?? '') ?? '';
    assert.equal(shown.includes("The client's memory file"), false);
    assert.equal(shown.includes('Environment root rule'), false);
    assert.ok(shown.includes("The campaign's memory file"));
});

test('the log says a level and how it stood, and never a word, a path or a digest of a file', async (t) => {
    const r = rig(t, (b) => ({ memoryGiven: loggedTurns(fileLogger(join(b.root, 'test-logs'))).memoryGiven }));
    writeFileSync(join(r.root, 'AGENTS.md'), 'planted-client-words\n');
    await r.turn('c1', 'hello', { host: 'claude', campaignId });
    writeFileSync(join(r.root, 'AGENTS.md'), 'planted-client-words, changed\n');
    writeFileSync(join(r.project, 'AGENTS.md'), '');
    await r.turn('c1', 'again', { host: 'claude' });
    const logs = join(r.b.root, 'test-logs');
    const text = readdirSync(logs).map((name) => readFileSync(join(logs, name), 'utf8')).join('');
    const lines = text.split('\n').filter(Boolean).map((line) => JSON.parse(line) as Record<string, unknown>);
    const given = lines.filter((line) => line.EventName === 'chat_memory_given');
    assert.deepEqual(
        given.map((line) => [line.Level, line.How]),
        [['client', 'given'], ['project', 'given'], ['campaign', 'given'], ['client', 'changed'], ['project', 'gone']],
    );
    for (const planted of ['planted-client-words', 'Project rule', 'Campaign rule', r.root, r.campaign, 'AGENTS.md', 'sha256:']) assert.equal(text.includes(planted), false, planted);
});
