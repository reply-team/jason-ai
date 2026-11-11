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
    cutMark,
    keptTextLimit,
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
        [`${newConversationId()}.json`]: serialize({ ...record(), entries: [{ kind: 'user' } as never] }),
        [`${newConversationId()}.json`]: JSON.stringify({ ...record(), campaign_id: '../cmp_1' }),
        [`${newConversationId()}.json`]: JSON.stringify({ ...record(), sessions: { '../claude': 'ses-1' } }),
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
});

test('an entry too large is cut to fit, keeping its end after the desktop\'s mark, and the newest entries always stay', () => {
    const bytes = (value: string) => Buffer.byteLength(value, 'utf8');
    const cut = (text: string, end: string) => text.startsWith(cutMark) && text.endsWith(end) && bytes(JSON.stringify(text)) <= keptTextLimit;
    const older = [{ kind: 'user' as const, text: 'First' }, { kind: 'text' as const, text: 'An answer' }];
    // Over the bound by itself: as one character a byte, as three bytes a character, and as characters JSON writes twice.
    for (const text of ['y'.repeat(recordLimit) + 'END', '字'.repeat(700_000) + 'END', '"\\'.repeat(500_000) + 'END']) {
        const kept = bounded(record({ entries: [...older, { kind: 'user', text }] }));
        assert.ok(bytes(serialize(kept)) <= recordLimit);
        assert.equal(kept.dropped, 0, 'nothing older goes for one large entry');
        assert.deepEqual(kept.entries.slice(0, 2), older);
        const last = kept.entries.at(-1);
        assert.ok(last?.kind === 'user' && cut(last.text, 'END'), 'cut to fit, its end kept after the mark');
    }
    const step = bounded(record({ entries: [{ kind: 'step', text: 'Ran a command', detail: 'z'.repeat(recordLimit) + 'END' }] })).entries[0];
    assert.ok(step?.kind === 'step' && step.text === 'Ran a command' && cut(step.detail ?? '', 'END'), 'a step\'s detail too');
    const card = bounded(record({ entries: [{ kind: 'permission', title: 'Run a command', detail: 'c'.repeat(recordLimit) + 'END', answer: 'deny' }] })).entries[0];
    assert.ok(card?.kind === 'permission' && card.answer === 'deny' && cut(card.detail, 'END'), 'and a card\'s');
    // Many large entries: each is cut, the oldest go to keep the whole within the bound, and the newest stay.
    const many = bounded(record({ entries: Array.from({ length: 40 }, (_, i) => ({ kind: 'text' as const, text: `${'w'.repeat(recordLimit / 4)} ${i}` })) }));
    assert.ok(bytes(serialize(many)) <= recordLimit);
    assert.ok(many.entries.length >= 8, String(many.entries.length));
    assert.equal(many.dropped, 40 - many.entries.length);
    const newest = many.entries.at(-1);
    assert.ok(newest?.kind === 'text' && cut(newest.text, ' 39'));
    assert.equal(bounded(record({ entries: [{ kind: 'user', text: 'small' }] })).entries[0]?.kind, 'user');
});

test('an entry of a kind this desktop does not know is skipped and counted as dropped, and the rest of the record is kept', () => {
    const kept = record({ dropped: 2, entries: [{ kind: 'user', text: 'Hello' }, { kind: 'chart', series: [1, 2] } as never, { kind: 'text', text: 'Hi' }, { kind: 'later' } as never] });
    const read = parseConversation(serialize(kept), kept.id);
    assert.deepEqual(read?.entries, [{ kind: 'user', text: 'Hello' }, { kind: 'text', text: 'Hi' }]);
    assert.equal(read?.dropped, 4);
    // A known kind that is malformed is no record of this desktop's, nor is an entry with no kind or one that is no object.
    for (const bad of [{ kind: 'user' }, { kind: 'user', text: 3 }, { text: 'no kind' }, { kind: 7 }, 'text', null]) {
        const planted = record({ entries: [bad as never] });
        assert.equal(parseConversation(serialize(planted), planted.id), null, JSON.stringify(bad));
    }
});

test('a session that does not begin with a letter or a digit is read as no session for that host, and the record is kept', () => {
    const kept = record({ sessions: { claude: '--dangerously-skip-permissions', codex: '0199a213-81c0-7800-8aa1-bbab2a035a53', other: '-x', more: 'a b', dot: '.x' } });
    const read = parseConversation(serialize(kept), kept.id);
    assert.deepEqual(read, { ...kept, sessions: { codex: '0199a213-81c0-7800-8aa1-bbab2a035a53' } });
});

test('a record whose id is not its file\'s name, or not a conversation id, is not read', () => {
    const kept = record();
    assert.deepEqual(parseConversation(serialize(kept), kept.id), kept);
    assert.equal(parseConversation(serialize(kept), newConversationId()), null);
    assert.equal(parseConversation(serialize({ ...kept, id: 'conv_../../x' }), 'conv_../../x'), null);
    assert.throws(() => writeConversation(tmpdir(), { ...kept, id: '../escape' }), ConversationUnwritten);
});
