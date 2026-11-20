// `/export <file name>` in a chat: caught before a turn is built, it writes the conversation's words, the person's and
// the AI's, as Markdown into the campaign's folder for a campaign's chat and the harness root otherwise, and says where.
// It never writes over anything, never follows a link at the name, takes a bare file name alone, and is refused while a
// turn runs. Any other leading word that begins with `/` goes to the host as before.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readdirSync, readFileSync, symlinkSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { Chat, type ChatEvent } from '../src/chat.ts';
import { readFence } from '../src/chat-deny.ts';
import { readConversations, writeConversation, type Conversation, type KeptEntry } from '../src/chat-conversations.ts';
import { exportName, exportText, nameRefusal, writeExport } from '../src/chat-export.ts';
import { bench, outsideStandIn, placeSandboxFiles, type Bench } from './harness.ts';

const fence = readFence(join(import.meta.dirname, '..'));
const at = '2026-11-19T12:00:00.000Z';

interface Rig {
    b: Bench;
    chat: Chat;
    events: Map<string, ChatEvent[]>;
    /** Sends and waits for the send's `done`; what was said meanwhile. */
    send(chatId: string, text: string): Promise<ChatEvent[]>;
    until(chatId: string, take: (event: ChatEvent) => boolean): Promise<ChatEvent>;
    hosts(): number;
}

const words: KeptEntry[] = [
    { kind: 'user', text: 'Who should we write to first?' },
    { kind: 'step', text: 'Ran jason campaign get', detail: 'C:\\Users\\someone\\.jason\\desktop\\turns\\x' },
    { kind: 'permission', title: 'Run a command', detail: 'jason campaign start cmp_01ABC', answer: 'allow' },
    { kind: 'text', text: 'Start with the **three** who replied last spring.', at },
    { kind: 'error', text: 'The host stopped.' },
    { kind: 'user', text: 'Thanks.' },
    { kind: 'text', text: 'You are welcome.', at },
];

function conversation(id: string, campaign: string | null, entries: KeptEntry[] = words): Conversation {
    return { id, campaign_id: campaign, title: campaign === null ? 'Planning the spring list' : 'Spring', created_at: at, updated_at: at, sessions: {}, entries, dropped: 0 };
}

const campaignChat = 'conv_01JA0000000000000000000001';
const setupChat = 'conv_01JA0000000000000000000002';
const emptyChat = 'conv_01JA0000000000000000000003';

function rig(t: { after(fn: () => void | Promise<void>): void }): Rig {
    const removals: (() => void)[] = [];
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude', 'codex'] });
    placeSandboxFiles(b);
    mkdirSync(b.paths.conversations, { recursive: true });
    writeConversation(b.paths.conversations, conversation(campaignChat, 'cmp_01ABC'));
    writeConversation(b.paths.conversations, conversation(setupChat, null));
    writeConversation(b.paths.conversations, conversation(emptyChat, null, []));
    const events = new Map<string, ChatEvent[]>();
    const waiters: { chatId: string; take: (event: ChatEvent) => boolean; resolve: (event: ChatEvent) => void }[] = [];
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
        sandboxOutside: outsideStandIn,
        now: () => Date.parse(at),
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
    });
    t.after(async () => {
        chat.closeAll();
        const until = Date.now() + 10_000;
        while (Date.now() < until && [...events.keys()].some((id) => chat.isRunning(id))) await new Promise((resolve) => setTimeout(resolve, 50));
        await chat.idle();
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
        until,
        async send(chatId, text) {
            const from = events.get(chatId)?.length ?? 0;
            const done = until(chatId, (event) => event.kind === 'done');
            await chat.send(chatId, text, { host: 'claude', typed: true });
            await done;
            return (events.get(chatId) ?? []).slice(from);
        },
        hosts: () => b.record().filter((each) => (each.name === 'claude' || each.name === 'codex') && each.args[0] !== '--version').length,
    };
}

/** A campaign folder and a harness root of the rig's own, as the stub runtime reports them. */
function places(r: Rig): { campaign: string; root: string } {
    const campaign = join(r.b.root, 'harness', 'campaigns', 'spring');
    const root = join(r.b.root, 'harness');
    mark(campaign, 'campaign', 'cmp_01ABC');
    mark(root, 'root', null);
    r.b.env.STUB_CAMPAIGN_FOLDER = campaign;
    r.b.env.STUB_HARNESS_ROOT = root;
    return { campaign, root };
}

/** Writes the runtime's marker of that kind and id into a folder, as the runtime marks one. */
function mark(folder: string, kind: string, id: string | null): void {
    mkdirSync(join(folder, '.jason'), { recursive: true });
    writeFileSync(join(folder, '.jason', 'marker.json'), JSON.stringify(id === null ? { marker_version: 1, kind } : { marker_version: 1, kind, id }));
}

const withFolder = 'Give a file name alone, such as notes.md, with no folder in it: the file is written in the campaign’s folder, or the harness root for a chat about no campaign.';
const oneLine = 'Give the file name on one line after /export.';
const theHarnessOwn = 'That name is one the harness keeps for its own files. Choose another.';
const rootUnknown = 'The harness root is not known, or not on this disk, so the file has nowhere to go.';
const campaignUnknown = 'The folder of this chat’s campaign is not known, or not on this disk, so the file has nowhere to go.';
const already = (path: string) => `A file or folder of that name is already there, and the desktop never writes over it: ${path}`;

const expected = [
    '# Planning the spring list',
    '',
    `Written by Jason Desktop at ${at}.`,
    '',
    '## You',
    '',
    'Who should we write to first?',
    '',
    '## AI',
    '',
    'Start with the **three** who replied last spring.',
    '',
    '## You',
    '',
    'Thanks.',
    '',
    '## AI',
    '',
    'You are welcome.',
    '',
].join('\n');

// ---- What a message is, and what is written -------------------------------------------------------------------------

test('a message is an export only when its first word is /export alone', () => {
    assert.equal(exportName('/export notes.md'), 'notes.md');
    assert.equal(exportName('  /export   my notes.md  '), 'my notes.md');
    assert.equal(exportName('/export'), '');
    assert.equal(exportName('/export\tnotes.md'), 'notes.md');
    for (const other of ['/exports notes.md', '/Export notes.md', 'export notes.md', '/compact', '/review /export x.md', 'please /export x.md']) {
        assert.equal(exportName(other), null, other);
    }
});

test('a name is taken only as a bare file name: no folder, no parent, no drive, nothing a system cannot hold', () => {
    for (const bare of ['notes.md', 'my notes.md', 'Spring – week 1.md', 'notes', 'a..b.md']) assert.equal(nameRefusal(bare), null, bare);
    for (const path of ['../notes.md', 'sub/notes.md', 'sub\\notes.md', '/tmp/notes.md', 'C:\\notes.md', 'C:notes.md', '..', '.', '\\\\server\\share\\x.md']) {
        assert.equal(nameRefusal(path), withFolder, path);
    }
    const notAName = 'That is not a file name the desktop writes.';
    const devices = ['CON', 'con.md', 'NUL.txt', 'com1.md', 'LPT9', 'CONIN$', 'conout$.md', 'nul .md', 'con  .txt', 'Aux .x.md'];
    for (const bad of ['no<te>s.md', 'a|b.md', 'what?.md', 'star*.md', 'quote".md', 'nul\u0000.md', 'trailing.', 'trailing ', ...devices, 'x'.repeat(256)]) {
        assert.equal(nameRefusal(bad), notAName, JSON.stringify(bad));
    }
    // The limit is the file systems' own, in UTF-8 bytes: 255 of them, not 255 characters.
    assert.equal(nameRefusal('é'.repeat(127) + 'a'), null);
    assert.equal(nameRefusal('é'.repeat(128)), notAName);
    assert.equal(nameRefusal(''), 'Name the file to write, such as /export notes.md.');
    // The name is the rest of the first line: more after it is refused, saying so, a queued join of two messages too.
    // A line break of any kind ends the line: the ones Unicode adds to the ASCII two too.
    const breaks = ['/export a.md\u0085b', '/export a.md\u2028b', '/export a.md\u2029b', '/export\u0085notes.md', '/export\u2028notes.md'];
    for (const text of ['/export a.md\nmore', '/export a.md\n\nAnd then the next message', '/export\nnotes.md', '/export a.md\r\nb', ...breaks]) {
        assert.equal(nameRefusal(exportName(text) ?? ''), oneLine, JSON.stringify(text));
    }
    // Any other space before the name is not part of it.
    for (const space of ['\u00a0', '\u3000', '\u2003', '\f', '\v', '\ufeff']) assert.equal(exportName(`/export${space}notes.md`), 'notes.md', JSON.stringify(space));
});

test('the harness’s own names are refused, whatever their case: the memory file, every import file the runtime writes, and .jason', () => {
    // The import files are the ones the runtime's table of hosts names beside the memory file, read from its source.
    const table = readFileSync(join(import.meta.dirname, '..', '..', 'dotnet', 'src', 'Jason.Contracts', 'Hosts', 'AgentHosts.cs'), 'utf8');
    const memory = /const string MemoryFile = "([^"]+)"/.exec(table)?.[1] ?? '';
    const imports = [...table.matchAll(/new\("([^"]+)", "@" \+ MemoryFile\)/g)].map((found) => found[1] ?? '');
    assert.equal(memory, 'AGENTS.md');
    assert.deepEqual(imports, ['CLAUDE.md', 'GEMINI.md']);
    // Every host row names its instruction file in that shape or names none, so a row written in another shape is red here.
    const rows = table.match(/new\(AgentHostKind\./g)?.length ?? 0;
    const none = table.match(/new\(AgentHostKind\.\w+, "[^"]*", "[^"]*", "[^"]*", "[^"]*", null,/g)?.length ?? 0;
    assert.ok(rows > 0);
    assert.equal(imports.length + none, rows);
    for (const name of [memory, ...imports, '.jason']) {
        for (const spelled of [name, name.toLowerCase(), name.toUpperCase()]) assert.equal(nameRefusal(spelled), theHarnessOwn, spelled);
    }
    // A name of a host's own file under another extension, and any name that begins with a dot, are the harness's too.
    for (const name of [memory, ...imports]) {
        const stem = name.split('.')[0] ?? '';
        for (const spelled of [`${stem}.local.md`, `${stem.toLowerCase()}.txt`, stem, `${stem.toUpperCase()}.md.md`]) assert.equal(nameRefusal(spelled), theHarnessOwn, spelled);
    }
    for (const name of ['.mcp.json', '.git', '.cursorrules', 'CLAUDE.local.md', 'Gemini.md', 'agents.txt', '.notes.md']) assert.equal(nameRefusal(name), theHarnessOwn, name);
    for (const name of ['my-CLAUDE.md', 'AGENTS-notes.md', 'Claudes.md', 'agentsmd']) assert.equal(nameRefusal(name), null, name);
    // The root keeps projects/ for the projects' folders the runtime makes there; a campaign's folder keeps no such name.
    for (const spelled of ['projects', 'Projects', 'PROJECTS']) {
        assert.equal(nameRefusal(spelled, 'root'), theHarnessOwn, spelled);
        assert.equal(nameRefusal(spelled, 'campaign'), null, spelled);
    }
    assert.equal(nameRefusal('projects.md', 'root'), null);
});

test('the text is the title, the moment, and the person’s and the AI’s words in order: no step, card, error or path', () => {
    const text = exportText(conversation(setupChat, null), new Date(at));
    assert.equal(text, expected);
    for (const hidden of ['Ran jason', 'turns', 'campaign start', 'The host stopped', setupChat]) assert.equal(text.includes(hidden), false, hidden);
});

test('a record that dropped its oldest entries says so where they were', () => {
    const text = exportText({ ...conversation(setupChat, null, [{ kind: 'user', text: 'Hi' }]), dropped: 3 }, new Date(at));
    assert.equal(
        text,
        ['# Planning the spring list', '', `Written by Jason Desktop at ${at}.`, '', 'The earliest messages of this conversation were no longer kept.', '', '## You', '', 'Hi', ''].join('\n'),
    );
});

// ---- The chat -------------------------------------------------------------------------------------------------------

test('a campaign’s chat writes a bare name into the campaign’s folder, says where, and starts no turn', async (t) => {
    const r = rig(t);
    const { campaign } = places(r);
    const said = await r.send(campaignChat, '/export spring.md');
    const path = join(campaign, 'spring.md');
    assert.deepEqual(said, [{ kind: 'notice', text: `Written: ${path}` }, { kind: 'done', error: null }]);
    assert.equal(readFileSync(path, 'utf8'), expected.replace('# Planning the spring list', '# Spring'));
    assert.equal(r.hosts(), 0, 'no host was started');
    // The export is no part of the conversation: its record is as it was.
    const kept = readConversations(r.b.paths.conversations).kept.find((each) => each.id === campaignChat);
    assert.deepEqual(kept?.entries, words);
});

test('a chat about no campaign writes a bare name into the harness root', async (t) => {
    const r = rig(t);
    const { root } = places(r);
    const said = await r.send(setupChat, '/export plan notes.md');
    const path = join(root, 'plan notes.md');
    assert.deepEqual(said, [{ kind: 'notice', text: `Written: ${path}` }, { kind: 'done', error: null }]);
    assert.equal(readFileSync(path, 'utf8'), expected);
    assert.equal(r.hosts(), 0);
});

test('a file already there is never written over, and nothing is written', async (t) => {
    const r = rig(t);
    const { root } = places(r);
    const path = join(root, 'notes.md');
    writeFileSync(path, 'the person’s own');
    const said = await r.send(setupChat, '/export notes.md');
    assert.deepEqual(said, [{ kind: 'done', error: already(path) }]);
    assert.equal(readFileSync(path, 'utf8'), 'the person’s own');
    assert.equal(r.hosts(), 0);
});

test('a link at the name is not followed: it is refused as a file already there, and what it names is not made', async (t) => {
    const r = rig(t);
    const { root } = places(r);
    const outside = join(r.b.root, 'outside.md');
    const link = join(root, 'linked.md');
    // A file link where the account may make one; else, on Windows, a junction, which any account may make.
    try {
        symlinkSync(outside, link, 'file');
    } catch {
        try {
            symlinkSync(outside, link, 'junction');
        } catch {
            t.skip('this account cannot make a link here');
            return;
        }
    }
    const said = await r.send(setupChat, '/export linked.md');
    assert.deepEqual(said, [{ kind: 'done', error: already(link) }]);
    assert.equal(existsSync(outside), false, 'the link was not followed');
});

test('a name with a folder in it is refused and nothing is written anywhere', async (t) => {
    const r = rig(t);
    const { root } = places(r);
    const before = readdirSync(root);
    for (const name of ['../escape.md', 'campaigns/spring/x.md', join(r.b.root, 'abs.md')]) {
        const said = await r.send(setupChat, `/export ${name}`);
        assert.deepEqual(said, [{ kind: 'done', error: withFolder }], name);
    }
    assert.deepEqual(readdirSync(root), before);
    assert.equal(existsSync(join(r.b.root, 'escape.md')), false);
    assert.equal(existsSync(join(r.b.root, 'abs.md')), false);
    assert.equal(r.hosts(), 0);
});

test('an export is refused while a turn of the chat runs, and writes nothing', async (t) => {
    const r = rig(t);
    const { root } = places(r);
    r.b.env.STUB_HOST_PLAN = JSON.stringify([{ wait: join(r.b.root, 'host.pid') }]);
    await r.chat.send(setupChat, 'Wait', { host: 'claude' });
    const refused = r.until(setupChat, (event) => event.kind === 'done' && event.error !== null);
    await r.chat.send(setupChat, '/export during.md', { host: 'claude' });
    assert.deepEqual(await refused, { kind: 'done', error: 'This chat is still answering. Wait for it to finish, or stop it, then send again.' });
    assert.equal(existsSync(join(root, 'during.md')), false);
    const done = r.until(setupChat, (event) => event.kind === 'done' && event.error === null);
    r.chat.stop(setupChat);
    await done;
});

test('a folder the runtime does not name, or that is not on this disk, is said and nothing is written', async (t) => {
    const r = rig(t);
    // No harness root reported, and a campaign with no folder.
    assert.deepEqual(await r.send(setupChat, '/export x.md'), [{ kind: 'done', error: rootUnknown }]);
    assert.deepEqual(await r.send(campaignChat, '/export x.md'), [{ kind: 'done', error: campaignUnknown }]);
    r.b.env.STUB_HARNESS_ROOT = join(r.b.root, 'not-there');
    assert.deepEqual(await r.send(setupChat, '/export x.md'), [{ kind: 'done', error: rootUnknown }]);
    assert.equal(existsSync(join(r.b.root, 'not-there')), false);
});

test('a folder is written into only while it carries the runtime’s marker of its campaign, or of the root', async (t) => {
    const r = rig(t);
    // Folders on the disk, as the runtime names them, with no marker in them.
    const root = join(r.b.root, 'unmarked');
    const campaign = join(root, 'spring');
    mkdirSync(campaign, { recursive: true });
    r.b.env.STUB_CAMPAIGN_FOLDER = campaign;
    r.b.env.STUB_HARNESS_ROOT = root;
    assert.deepEqual(await r.send(campaignChat, '/export x.md'), [{ kind: 'done', error: campaignUnknown }]);
    assert.deepEqual(await r.send(setupChat, '/export x.md'), [{ kind: 'done', error: rootUnknown }]);
    // A marker of another campaign, or of another kind, is no marker of this one.
    mark(campaign, 'campaign', 'cmp_02OTHER');
    mark(root, 'project', 'prj_01STUB');
    assert.deepEqual(await r.send(campaignChat, '/export x.md'), [{ kind: 'done', error: campaignUnknown }]);
    assert.deepEqual(await r.send(setupChat, '/export x.md'), [{ kind: 'done', error: rootUnknown }]);
    assert.deepEqual(readdirSync(campaign), ['.jason']);
    assert.deepEqual(readdirSync(root).sort(), ['.jason', 'spring']);
});

test('a message with more after the /export line, or a name of the harness, is refused saying why, and nothing is written', async (t) => {
    const r = rig(t);
    const { root } = places(r);
    const before = readdirSync(root).sort();
    assert.deepEqual(await r.send(setupChat, '/export a.md\n\nAnd what next?'), [{ kind: 'done', error: oneLine }]);
    assert.deepEqual(await r.send(setupChat, '/export AGENTS.md'), [{ kind: 'done', error: theHarnessOwn }]);
    assert.deepEqual(await r.send(setupChat, '/export Projects'), [{ kind: 'done', error: theHarnessOwn }]);
    assert.deepEqual(readdirSync(root).sort(), before);
    assert.equal(r.hosts(), 0);
});

test('an /export the person did not type in the message box is refused: no file, and the chat says so', async (t) => {
    const r = rig(t);
    const { root } = places(r);
    const before = readdirSync(root).sort();
    for (const options of [{ host: 'claude' }, { host: 'claude', typed: false }, { host: 'claude', typed: 'yes' }]) {
        const from = r.events.get(setupChat)?.length ?? 0;
        const done = r.until(setupChat, (event) => event.kind === 'done');
        await r.chat.send(setupChat, '/export card.md', options);
        await done;
        assert.deepEqual((r.events.get(setupChat) ?? []).slice(from), [{ kind: 'done', error: 'Type /export in the message box yourself.' }], JSON.stringify(options));
    }
    assert.deepEqual(readdirSync(root).sort(), before);
    assert.equal(r.hosts(), 0, 'and nothing went to the host');
    // The words the person typed still export.
    assert.deepEqual(await r.send(setupChat, '/export typed.md'), [{ kind: 'notice', text: `Written: ${join(root, 'typed.md')}` }, { kind: 'done', error: null }]);
});

test('the write checks the name again for the folder it is given: the root’s projects stays refused there', (t) => {
    const r = rig(t);
    const { root, campaign } = places(r);
    assert.throws(() => writeExport(root, 'projects', 'x', 'root'), { message: theHarnessOwn });
    assert.equal(existsSync(join(root, 'projects')), false);
    assert.equal(writeExport(campaign, 'projects', 'x', 'campaign'), join(campaign, 'projects'));
});

test('a campaign’s chat may name its file projects: only the root keeps that name', async (t) => {
    const r = rig(t);
    const { campaign } = places(r);
    const path = join(campaign, 'projects');
    assert.deepEqual(await r.send(campaignChat, '/export projects'), [{ kind: 'notice', text: `Written: ${path}` }, { kind: 'done', error: null }]);
    assert.ok(existsSync(path));
});

test('an export the window closes on says nothing afterwards and writes nothing', async (t) => {
    const r = rig(t);
    const { root } = places(r);
    const sending = r.chat.send(setupChat, '/export late.md', { host: 'claude', typed: true });
    r.chat.closeAll();
    await sending;
    assert.deepEqual(r.events.get(setupChat) ?? [], []);
    assert.equal(existsSync(join(root, 'late.md')), false);
});

test('an export of a chat with nothing said yet, or with no name, is refused', async (t) => {
    const r = rig(t);
    places(r);
    assert.deepEqual(await r.send(emptyChat, '/export x.md'), [{ kind: 'done', error: 'This conversation has nothing to write yet.' }]);
    assert.deepEqual(await r.send(setupChat, '/export'), [{ kind: 'done', error: 'Name the file to write, such as /export notes.md.' }]);
});

test('any other word that begins with a slash goes to the host as before', async (t) => {
    const r = rig(t);
    places(r);
    r.b.env.STUB_HOST_PLAN = JSON.stringify([]);
    for (const text of ['/exports x.md', '/compact']) {
        const said = await r.send(setupChat, text);
        assert.equal(said.at(-1)?.kind, 'done');
    }
    assert.equal(r.hosts(), 2, 'each went to the host');
    const kept = readConversations(r.b.paths.conversations).kept.find((each) => each.id === setupChat);
    assert.deepEqual(kept?.entries.filter((each) => each.kind === 'user').slice(-2), [{ kind: 'user', text: '/exports x.md' }, { kind: 'user', text: '/compact' }]);
});
