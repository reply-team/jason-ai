// The folder of a Full access grant an earlier desktop left, which no desktop makes now, and a conversation's own folder:
// moved to the ended place, never out of a place or into one that is a link, and the bounded sweep that removes either
// seven days after the move: a link in it is unlinked and never walked, a folder swapped after it was looked at is not
// entered, a failure leaves the folder and says a code, and a pass takes only as many entries as it is given.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { newConversationId } from '../src/chat-conversations.ts';
import {
    endConversationFolder,
    endGrantFolder,
    endedName,
    endedNameShape,
    grantIdShape,
    keptMs,
    moveLeftovers,
    moveOwnLeftovers,
    newGrantId,
    sweepEnded,
    sweepOps,
    type FullFolders,
    type SweepOps,
} from '../src/chat-full-folder.ts';

const day = 24 * 60 * 60 * 1000;
const t0 = Date.UTC(2026, 10, 12, 8, 0, 0);

function rig(t: { after(fn: () => void): void }) {
    const root = mkdtempSync(join(tmpdir(), 'jason-full-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const folders: FullFolders = { full: join(root, 'chat-full'), ended: join(root, 'chat-full-ended'), records: join(root, 'chat-full-records') };
    return { root, folders };
}

/** A grant's folder as an earlier desktop left it. */
function left(folders: FullFolders, id: string): void {
    mkdirSync(join(folders.full, id), { recursive: true });
}

function put(path: string, text = 'x'): void {
    mkdirSync(dirname(path), { recursive: true });
    writeFileSync(path, text);
}

/** An ended folder of a made-up grant, with the moment it moved. */
function ended(folders: FullFolders, at: number, id = newGrantId(at)): string {
    const path = join(folders.ended, endedName(id, at));
    mkdirSync(path, { recursive: true });
    return path;
}

test('a grant id is 26 characters, and an ended name is the id, a dash and the moment it moved', () => {
    const id = newGrantId(t0);
    assert.match(id, grantIdShape);
    assert.notEqual(id, newGrantId(t0));
    assert.deepEqual(endedNameShape.exec(endedName(id, t0))?.slice(1), [id, String(t0)]);
});

test('ending a grant moves its folder to the ended place at once, named with the moment, and the other grants\' folders stay', (t) => {
    const { folders } = rig(t);
    const one = newGrantId(t0);
    const two = newGrantId(t0 + 1);
    left(folders, one);
    left(folders, two);
    put(join(folders.full, one, 'work.txt'), 'the agent\'s file');
    const name = endGrantFolder(folders, one, t0 + 5);
    assert.equal(name, `${one}-${t0 + 5}`);
    assert.equal(readFileSync(join(folders.ended, name ?? '', 'work.txt'), 'utf8'), 'the agent\'s file');
    assert.deepEqual(readdirSync(folders.full), [two]);
    assert.equal(endGrantFolder(folders, one, t0 + 6), null, 'a folder already moved is nothing to move');
});

test('a folder that cannot be moved is left and a code said; the next start moves every leftover, its seven days counted from then', (t) => {
    const { folders } = rig(t);
    const id = newGrantId(t0);
    left(folders, id);
    mkdirSync(folders.ended, { recursive: true });
    // A file where the ended place should be makes the move fail.
    rmSync(folders.ended, { recursive: true });
    writeFileSync(folders.ended, 'in the way');
    const codes: string[] = [];
    assert.equal(endGrantFolder(folders, id, t0, (code) => codes.push(code)), null);
    assert.equal(codes.length, 1);
    assert.match(codes[0] ?? '', /^E[A-Z]+$/);
    assert.ok(existsSync(join(folders.full, id)));
    rmSync(folders.ended);
    moveLeftovers(folders, t0 + day);
    assert.deepEqual(readdirSync(folders.full), []);
    assert.deepEqual(readdirSync(folders.ended), [`${id}-${t0 + day}`]);
});

test('a folder six days old is kept and one eight days old is removed, the age counted from the move', (t) => {
    const { folders } = rig(t);
    const six = ended(folders, t0 - 6 * day);
    const eight = ended(folders, t0 - 8 * day);
    const exactly = ended(folders, t0 - keptMs);
    for (const folder of [six, eight, exactly]) put(join(folder, 'a', 'b.txt'));
    const swept = sweepEnded(folders, { now: t0, cap: 1000 });
    assert.deepEqual(swept, { removed: 2, capped: false });
    assert.ok(existsSync(six));
    assert.ok(!existsSync(eight));
    assert.ok(!existsSync(exactly), 'seven days to the millisecond is old enough');
});

test('a name that is not one the desktop gave is left, and said', (t) => {
    const { folders } = rig(t);
    mkdirSync(join(folders.ended, 'somebody-else'), { recursive: true });
    const codes: string[] = [];
    sweepEnded(folders, { now: t0, cap: 1000, said: (code) => codes.push(code) });
    assert.ok(existsSync(join(folders.ended, 'somebody-else')));
    assert.deepEqual(codes, ['ENAME']);
});

test('a junction or link inside an ended folder is unlinked and what it names is left whole', (t) => {
    const { folders, root } = rig(t);
    const outside = join(root, 'outside');
    put(join(outside, 'precious.txt'), 'mine');
    const old = ended(folders, t0 - 8 * day);
    put(join(old, 'plain.txt'));
    symlinkSync(outside, join(old, 'door'), 'junction');
    const codes: string[] = [];
    const swept = sweepEnded(folders, { now: t0, cap: 1000, said: (code) => codes.push(code) });
    assert.deepEqual(codes, []);
    assert.equal(swept.removed, 1);
    assert.ok(!existsSync(old));
    assert.equal(readFileSync(join(outside, 'precious.txt'), 'utf8'), 'mine');
});

test('a checked folder swapped for a link before it is entered is not walked: the link is unlinked and its target left', (t) => {
    const { folders, root } = rig(t);
    const outside = join(root, 'outside');
    put(join(outside, 'precious.txt'), 'mine');
    const old = ended(folders, t0 - 8 * day);
    put(join(old, 'inner', 'file.txt'));
    const inner = join(old, 'inner');
    // The first look finds a folder; before the sweep enters it, it is replaced by a junction.
    let looked = 0;
    const ops: SweepOps = {
        ...sweepOps,
        lstat: (path) => {
            const stat = sweepOps.lstat(path);
            if (path === inner && ++looked === 1) {
                rmSync(inner, { recursive: true });
                symlinkSync(outside, inner, 'junction');
            }
            return stat;
        },
    };
    const codes: string[] = [];
    sweepEnded(folders, { now: t0, cap: 1000, ops, said: (code) => codes.push(code) });
    assert.equal(readFileSync(join(outside, 'precious.txt'), 'utf8'), 'mine', 'what the link named was never walked');
    assert.ok(!existsSync(inner), 'the link itself is gone');
    assert.ok(!existsSync(old));
    assert.deepEqual(codes, []);
});

test('a checked folder swapped for another folder (a different device and inode) is not entered: the folder is left and a code said', (t) => {
    const { folders } = rig(t);
    const old = ended(folders, t0 - 8 * day);
    put(join(old, 'inner', 'file.txt'));
    const inner = join(old, 'inner');
    let looked = 0;
    const ops: SweepOps = {
        ...sweepOps,
        lstat: (path) => {
            const stat = sweepOps.lstat(path);
            if (path === inner && ++looked === 2) {
                // The second look finds another file system object at the name.
                return Object.assign(Object.create(Object.getPrototypeOf(stat) as object) as typeof stat, stat, { ino: stat.ino + 1n });
            }
            return stat;
        },
    };
    const codes: string[] = [];
    const swept = sweepEnded(folders, { now: t0, cap: 1000, ops, said: (code) => codes.push(code) });
    assert.deepEqual(codes, ['ESWAPPED']);
    assert.equal(swept.removed, 0);
    assert.ok(existsSync(join(inner, 'file.txt')), 'nothing below the swapped folder was touched');
});

test('an entry that cannot be read or removed leaves its folder, says a code and throws nothing; the next folder is still swept', (t) => {
    const { folders } = rig(t);
    const stuck = ended(folders, t0 - 9 * day);
    const fine = ended(folders, t0 - 8 * day);
    put(join(stuck, 'locked.txt'));
    put(join(stuck, 'other.txt'));
    put(join(fine, 'a.txt'));
    const ops: SweepOps = {
        ...sweepOps,
        unlink: (path) => {
            if (path.endsWith('locked.txt')) throw Object.assign(new Error('no'), { code: 'EACCES' });
            sweepOps.unlink(path);
        },
    };
    const codes: string[] = [];
    const swept = sweepEnded(folders, { now: t0, cap: 1000, ops, said: (code) => codes.push(code) });
    assert.deepEqual(codes, ['EACCES']);
    assert.deepEqual(swept, { removed: 1, capped: false });
    assert.ok(existsSync(join(stuck, 'locked.txt')));
    assert.ok(!existsSync(fine));
    const unreadable: SweepOps = {
        ...sweepOps,
        lstat: (path) => {
            if (path.endsWith('locked.txt')) throw Object.assign(new Error('no'), { code: 'EPERM' });
            return sweepOps.lstat(path);
        },
    };
    const more: string[] = [];
    assert.doesNotThrow(() => sweepEnded(folders, { now: t0, cap: 1000, ops: unreadable, said: (code) => more.push(code) }));
    assert.deepEqual(more, ['EPERM']);
});

test('a small tree is removed over two passes with a small cap, and the first pass stops where it is told', (t) => {
    const { folders } = rig(t);
    const old = ended(folders, t0 - 8 * day);
    for (const name of ['a', 'b', 'c']) put(join(old, 'dir', `${name}.txt`));
    put(join(old, 'top.txt'));
    // The folder, dir, three files and the top file are six entries.
    const first = sweepEnded(folders, { now: t0, cap: 4 });
    assert.deepEqual(first, { removed: 0, capped: true });
    assert.ok(existsSync(old), 'what the first pass could not finish waits for the next');
    assert.ok(readdirSync(old).length > 0 || existsSync(old));
    const second = sweepEnded(folders, { now: t0, cap: 4 });
    const third = second.capped ? sweepEnded(folders, { now: t0, cap: 4 }) : second;
    assert.deepEqual(third, { removed: 1, capped: false });
    assert.ok(!existsSync(old));
});

test('the cap is a parameter: a larger one takes the same tree in one pass', (t) => {
    const { folders } = rig(t);
    const old = ended(folders, t0 - 8 * day);
    for (const name of ['a', 'b', 'c']) put(join(old, 'dir', `${name}.txt`));
    assert.deepEqual(sweepEnded(folders, { now: t0, cap: 100 }), { removed: 1, capped: false });
    assert.ok(!existsSync(old));
});

// A conversation's own folder, `<data>/desktop/chats/<id>/`, goes to the same ended place when the conversation is
// removed, or at a start when no record names it, and the same sweep takes it after seven days.

test('a conversation\'s own folder is moved to the ended place under its id and the moment it moved, kept seven days, and then removed by the sweep', (t) => {
    const { root, folders } = rig(t);
    const chats = join(root, 'chats');
    const id = newConversationId(t0);
    const other = newConversationId(t0 + 1);
    put(join(chats, id, 'work.txt'), 'the agent\'s file');
    put(join(chats, other, 'work.txt'), 'another chat\'s file');
    const name = endConversationFolder(chats, folders, id, t0 + 5);
    assert.equal(name, `${id}-${t0 + 5}`);
    assert.deepEqual(endedNameShape.exec(name ?? '')?.slice(1), [id, String(t0 + 5)]);
    assert.equal(existsSync(join(chats, id)), false, 'gone from its place');
    assert.equal(readFileSync(join(folders.ended, name ?? '', 'work.txt'), 'utf8'), 'the agent\'s file', 'whole in the ended place');
    assert.deepEqual(readdirSync(chats), [other], 'the other conversation\'s folder stays');
    assert.equal(endConversationFolder(chats, folders, id, t0 + 6), null, 'a folder already moved is nothing to move');
    assert.deepEqual(sweepEnded(folders, { now: t0 + 5 + keptMs - 1, cap: 1000 }), { removed: 0, capped: false }, 'kept for seven days');
    assert.ok(existsSync(join(folders.ended, name ?? '')));
    const codes: string[] = [];
    assert.deepEqual(sweepEnded(folders, { now: t0 + 5 + keptMs, cap: 1000, said: (code) => codes.push(code) }), { removed: 1, capped: false });
    assert.deepEqual(codes, [], 'its name is one the desktop gave');
    assert.equal(existsSync(join(folders.ended, name ?? '')), false);
});

test('a conversation\'s folder that cannot be moved is left where it is with everything in it, and a code said', (t) => {
    const { root, folders } = rig(t);
    const chats = join(root, 'chats');
    const id = newConversationId(t0);
    put(join(chats, id, 'work.txt'), 'the agent\'s file');
    // A file where the ended place should be makes the move fail, as a terminal holding the folder does on Windows.
    writeFileSync(folders.ended, 'in the way');
    const codes: string[] = [];
    assert.equal(endConversationFolder(chats, folders, id, t0, (code) => codes.push(code)), null);
    assert.equal(codes.length, 1);
    assert.match(codes[0] ?? '', /^E[A-Z]+$/);
    assert.equal(readFileSync(join(chats, id, 'work.txt'), 'utf8'), 'the agent\'s file');
});

test('nothing is moved out of a conversations\' place that is a link, nor into an ended place that is one: what each names is left whole', (t) => {
    const { root, folders } = rig(t);
    const id = newConversationId(t0);
    const outside = join(root, 'outside');
    put(join(outside, id, 'precious.txt'), 'mine');
    const chats = join(root, 'chats');
    symlinkSync(outside, chats, 'junction');
    const codes: string[] = [];
    assert.equal(endConversationFolder(chats, folders, id, t0, (code) => codes.push(code)), null);
    assert.equal(moveOwnLeftovers(chats, folders, new Set(), t0, (code) => codes.push(code)).length, 0);
    assert.equal(readFileSync(join(outside, id, 'precious.txt'), 'utf8'), 'mine', 'the folder the link names is left');
    assert.equal(existsSync(folders.ended), false, 'nothing reached the ended place');
    assert.deepEqual(codes, ['ENOTDIR', 'ENOTDIR']);
    // A real conversations' place, and an ended place that is a link elsewhere: nothing is moved into it.
    const real = join(root, 'chats-real');
    put(join(real, id, 'work.txt'), 'the agent\'s file');
    const elsewhere = join(root, 'elsewhere');
    mkdirSync(elsewhere);
    symlinkSync(elsewhere, folders.ended, 'junction');
    assert.equal(endConversationFolder(real, folders, id, t0), null);
    assert.deepEqual(readdirSync(elsewhere), [], 'nothing went through the link');
    assert.equal(readFileSync(join(real, id, 'work.txt'), 'utf8'), 'the agent\'s file');
});

test('every conversation\'s folder no record names is moved to the ended place; one a record names, and a name that is no conversation\'s, stay', (t) => {
    const { root, folders } = rig(t);
    const chats = join(root, 'chats');
    const kept = newConversationId(t0);
    const orphan = newConversationId(t0 + 1);
    put(join(chats, kept, 'work.txt'));
    put(join(chats, orphan, 'work.txt'));
    put(join(chats, 'not-a-conversation', 'work.txt'));
    const moved = moveOwnLeftovers(chats, folders, new Set([kept]), t0 + 9);
    assert.deepEqual(moved, [`${orphan}-${t0 + 9}`]);
    assert.deepEqual(readdirSync(chats).sort(), [kept, 'not-a-conversation'].sort());
    assert.deepEqual(readdirSync(folders.ended), [`${orphan}-${t0 + 9}`]);
});
