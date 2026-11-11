// The conversations' records on disk: written beside their names and renamed over them, never followed through a link,
// bounded by dropping the oldest entries, and a file that is not a record left as it was.

import assert from 'node:assert/strict';
import { lstatSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import {
    bounded,
    conversationIdShape,
    ConversationUnwritten,
    newConversationId,
    parseConversation,
    readConversations,
    recordLimit,
    removeConversation,
    serialize,
    writeConversation,
    type Conversation,
} from '../src/chat-conversations.ts';

function folderOf(t: { after(fn: () => void): void }): { root: string; folder: string } {
    const root = mkdtempSync(join(tmpdir(), 'jason-conversations-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    return { root, folder: join(root, 'conversations') };
}

const record = (fields: Partial<Conversation> = {}): Conversation => ({
    id: newConversationId(),
    campaign_id: null,
    title: 'New chat',
    created_at: '2026-11-11T10:00:00.000Z',
    updated_at: '2026-11-11T10:00:00.000Z',
    sessions: {},
    entries: [],
    dropped: 0,
    ...fields,
});

test('a conversation id is conv_ and 26 Crockford characters, the moment first, so ids sort as they were made', () => {
    const early = newConversationId(1_700_000_000_000, Buffer.alloc(10, 0xff));
    const late = newConversationId(1_700_000_000_001, Buffer.alloc(10, 0));
    assert.match(early, conversationIdShape);
    assert.match(late, conversationIdShape);
    assert.ok(early < late);
    assert.notEqual(newConversationId(), newConversationId());
});

test('a record is written and read back field for field, and nothing is left beside it', (t) => {
    const { folder } = folderOf(t);
    const kept = record({
        campaign_id: 'cmp_01ABC',
        title: 'Spring',
        sessions: { claude: 'stub-session-1', codex: '0199a213-81c0-7800-8aa1-bbab2a035a53' },
        entries: [
            { kind: 'user', text: 'Which campaigns wait on me?' },
            { kind: 'step', text: 'Ran a jason command', detail: 'jason campaign list' },
            { kind: 'step', text: 'Read its task list' },
            { kind: 'permission', title: 'Run a command', detail: 'git status', answer: 'deny' },
            { kind: 'permission', title: 'Read a file', detail: '/x', answer: null },
            { kind: 'text', text: '<b>not markup</b>' },
            { kind: 'error', text: 'Claude Code stopped.' },
        ],
    });
    writeConversation(folder, kept);
    assert.deepEqual(readdirSync(folder), [`${kept.id}.json`]);
    assert.deepEqual(readConversations(folder), { kept: [kept], unreadable: 0 });
    if (process.platform !== 'win32') assert.equal(statSync(join(folder, `${kept.id}.json`)).mode & 0o777, 0o600, 'readable by this account alone');
});

test('a second write is a new file renamed over the name, never a write into the old one', (t) => {
    const { folder } = folderOf(t);
    const kept = record();
    writeConversation(folder, kept);
    const before = statSync(join(folder, `${kept.id}.json`), { bigint: true });
    writeConversation(folder, { ...kept, title: 'Renamed' });
    assert.notEqual(statSync(join(folder, `${kept.id}.json`), { bigint: true }).ino, before.ino);
    assert.equal(readConversations(folder).kept[0]?.title, 'Renamed');
    assert.deepEqual(readdirSync(folder), [`${kept.id}.json`]);
});

test('a link at a record\'s name is replaced by a regular file, and what it named is left as it was', (t) => {
    const { root, folder } = folderOf(t);
    mkdirSync(folder);
    const kept = record();
    const elsewhere = join(root, 'someone-else.json');
    writeFileSync(elsewhere, 'not the desktop\'s\n');
    try {
        symlinkSync(elsewhere, join(folder, `${kept.id}.json`), 'file');
    } catch (error) {
        t.skip(`this platform made no link here (${(error as NodeJS.ErrnoException).code})`);
        return;
    }
    assert.deepEqual(readConversations(folder), { kept: [], unreadable: 1 }, 'a link is never read as a record');
    writeConversation(folder, kept);
    assert.ok(lstatSync(join(folder, `${kept.id}.json`)).isFile());
    assert.equal(readFileSync(elsewhere, 'utf8'), 'not the desktop\'s\n');
    removeConversation(folder, kept.id);
    assert.deepEqual(readdirSync(folder), []);
    assert.equal(readFileSync(elsewhere, 'utf8'), 'not the desktop\'s\n');
});

test('a conversations folder that is a link to another folder is refused, and nothing is written there', (t) => {
    const { root, folder } = folderOf(t);
    const elsewhere = join(root, 'harness');
    mkdirSync(elsewhere);
    symlinkSync(elsewhere, folder, 'junction');
    assert.throws(() => writeConversation(folder, record()), (error) => error instanceof ConversationUnwritten && error.code === 'ENOTDIR');
    assert.deepEqual(readdirSync(elsewhere), []);
    assert.deepEqual(readConversations(folder), { kept: [], unreadable: 0 }, 'nor is it read');
});

test('a file that is not a record is counted, never read as one, and left byte for byte as it was', (t) => {
    const { folder } = folderOf(t);
    mkdirSync(folder);
    const good = record();
    writeConversation(folder, good);
    const other = newConversationId();
    const planted: Record<string, string> = {
        [`${newConversationId()}.json`]: '{ not json',
        [`${other}.json`]: serialize({ ...record(), id: newConversationId() }),
        [`${newConversationId()}.json`]: serialize({ ...record(), entries: [{ kind: 'html', text: '<img>' } as never] }),
        [`${newConversationId()}.json`]: JSON.stringify({ ...record(), campaign_id: '../cmp_1' }),
        [`${newConversationId()}.json`]: JSON.stringify({ ...record(), sessions: { claude: 'a b' } }),
        'notes.txt': 'not a record, and not counted',
    };
    for (const [name, text] of Object.entries(planted)) writeFileSync(join(folder, name), text);
    assert.deepEqual(readConversations(folder), { kept: [good], unreadable: 5 });
    for (const [name, text] of Object.entries(planted)) assert.equal(readFileSync(join(folder, name), 'utf8'), text, name);
});

test('a record within the bound is kept whole, and one over it drops its oldest entries and counts them', () => {
    const small = record({ entries: [{ kind: 'user', text: 'hello' }] });
    assert.equal(bounded(small), small);
    const big = 'x'.repeat(100_000);
    const entries = Array.from({ length: 30 }, (_, i) => ({ kind: 'text' as const, text: `${i} ${big}` }));
    const kept = bounded(record({ entries, dropped: 3 }));
    assert.ok(Buffer.byteLength(serialize(kept), 'utf8') <= recordLimit);
    assert.ok(kept.entries.length < 30 && kept.entries.length >= 19, String(kept.entries.length));
    assert.equal(kept.dropped, 3 + 30 - kept.entries.length);
    const newest = kept.entries.at(-1);
    assert.ok(newest?.kind === 'text' && newest.text.startsWith('29 '), 'the newest stays');
    const huge = bounded(record({ entries: [{ kind: 'user', text: 'y'.repeat(recordLimit) }] }));
    assert.deepEqual([huge.entries, huge.dropped], [[], 1], 'an entry over the bound by itself goes as well');
});

test('a record whose id is not its file\'s name, or not a conversation id, is not read', () => {
    const kept = record();
    assert.deepEqual(parseConversation(serialize(kept), kept.id), kept);
    assert.equal(parseConversation(serialize(kept), newConversationId()), null);
    assert.equal(parseConversation(serialize({ ...kept, id: 'conv_../../x' }), 'conv_../../x'), null);
    assert.throws(() => writeConversation(tmpdir(), { ...kept, id: '../escape' }), ConversationUnwritten);
});
