// The instruction text placed in a folder of its own: what stands at a memory file's name is never followed, never read
// unless it is a regular file, and replaced only by a file written beside it and renamed over the name.

import assert from 'node:assert/strict';
import { linkSync, lstatSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { InstructionsUnwritten, placeInstructions } from '../src/chat-instructions.ts';

const text = 'Keep to these rules.\n';

function chatFolder(t: { after(fn: () => void): void }): { root: string; chat: string } {
    const root = mkdtempSync(join(tmpdir(), 'jason-instructions-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const chat = join(root, 'chat');
    mkdirSync(chat);
    return { root, chat };
}

test('a memory file that is a link is replaced by a regular file, and what the link named is left as it was', (t) => {
    const { root, chat } = chatFolder(t);
    const other = join(root, 'someone-else.md');
    const same = join(root, 'same-text.md');
    writeFileSync(other, 'Not the chat’s.\n');
    writeFileSync(same, text);
    try {
        symlinkSync(other, join(chat, 'CLAUDE.md'), 'file');
        symlinkSync(same, join(chat, 'AGENTS.md'), 'file');
    } catch (error) {
        t.skip(`this platform made no link here (${(error as NodeJS.ErrnoException).code})`);
        return;
    }
    assert.deepEqual(placeInstructions(chat, text), ['CLAUDE.md', 'AGENTS.md'], 'a link is replaced even when what it names holds the text');
    for (const name of ['CLAUDE.md', 'AGENTS.md']) {
        assert.ok(lstatSync(join(chat, name)).isFile(), name);
        assert.equal(readFileSync(join(chat, name), 'utf8'), text, name);
    }
    assert.equal(readFileSync(other, 'utf8'), 'Not the chat’s.\n');
    assert.equal(readFileSync(same, 'utf8'), text);
    assert.deepEqual(readdirSync(chat).sort(), ['AGENTS.md', 'CLAUDE.md'], 'nothing left beside them');
});

test('a memory file that is a hard link to another file of another text is replaced, and the other file is left as it was', (t) => {
    const { root, chat } = chatFolder(t);
    const other = join(root, 'someone-else.md');
    writeFileSync(other, 'Not the chat’s.\n');
    linkSync(other, join(chat, 'CLAUDE.md'));
    assert.deepEqual(placeInstructions(chat, text), ['CLAUDE.md', 'AGENTS.md']);
    assert.equal(readFileSync(join(chat, 'CLAUDE.md'), 'utf8'), text);
    assert.equal(readFileSync(other, 'utf8'), 'Not the chat’s.\n');
    assert.equal(statSync(other).nlink, 1, 'the name is no longer the other file');
});

test('a memory file of another text is replaced by one written beside it and renamed over the name, and one already equal is not written', (t) => {
    const { chat } = chatFolder(t);
    writeFileSync(join(chat, 'CLAUDE.md'), 'Run everything in the background.\n');
    writeFileSync(join(chat, 'AGENTS.md'), text);
    // A file id read whole: on Windows it is 64 bits, more than a Number holds exactly.
    const stat = (name: string) => statSync(join(chat, name), { bigint: true });
    const replaced = stat('CLAUDE.md');
    const kept = stat('AGENTS.md');
    assert.deepEqual(placeInstructions(chat, text), ['CLAUDE.md']);
    assert.equal(readFileSync(join(chat, 'CLAUDE.md'), 'utf8'), text);
    assert.notEqual(stat('CLAUDE.md').ino, replaced.ino, 'a new file at the name, never a write into the old one');
    assert.equal(stat('AGENTS.md').ino, kept.ino);
    assert.equal(stat('AGENTS.md').mtimeNs, kept.mtimeNs);
    assert.deepEqual(placeInstructions(chat, text), []);
    assert.deepEqual(readdirSync(chat).sort(), ['AGENTS.md', 'CLAUDE.md'], 'nothing left beside them');
});

test('a directory at a memory file’s name is a failed write that names the file, and the directory is left as it was', (t) => {
    const { chat } = chatFolder(t);
    mkdirSync(join(chat, 'CLAUDE.md'));
    writeFileSync(join(chat, 'CLAUDE.md', 'notes.md'), 'kept\n');
    assert.throws(
        () => placeInstructions(chat, text),
        (error) => error instanceof InstructionsUnwritten && error.file === 'CLAUDE.md' && /^E[A-Z]+$/.test(error.code),
    );
    assert.ok(statSync(join(chat, 'CLAUDE.md')).isDirectory());
    assert.equal(readFileSync(join(chat, 'CLAUDE.md', 'notes.md'), 'utf8'), 'kept\n');
    assert.deepEqual(readdirSync(chat), ['CLAUDE.md'], 'no file left behind from the write that failed');
});

test('a failed write says so in the desktop’s words, naming the file and nothing of the error', () => {
    const error = new InstructionsUnwritten('AGENTS.md', 'EPERM');
    assert.equal(
        error.message,
        'The chat’s instruction file AGENTS.md could not be written, so the turn was not started. Close anything that has it open and send again.',
    );
    assert.equal(new InstructionsUnwritten('CLAUDE.md', 'not a code: C:\\Users\\someone').code, 'unknown');
});

test('a chat folder that is a link to another folder is a failed write, and nothing is written into that folder', (t) => {
    const { root, chat } = chatFolder(t);
    rmSync(chat, { recursive: true });
    const elsewhere = join(root, 'harness');
    mkdirSync(elsewhere);
    const own = Buffer.from('The person’s own memory file.\n', 'utf8');
    writeFileSync(join(elsewhere, 'CLAUDE.md'), own);
    // A junction on Windows, which any account may make; elsewhere the type is ignored and the link is symbolic.
    symlinkSync(elsewhere, chat, 'junction');
    assert.throws(
        () => placeInstructions(chat, text),
        (error) => error instanceof InstructionsUnwritten && error.file === 'CLAUDE.md' && error.code === 'ENOTDIR',
    );
    assert.ok(readFileSync(join(elsewhere, 'CLAUDE.md')).equals(own), 'the file there is byte for byte as it was');
    assert.deepEqual(readdirSync(elsewhere), ['CLAUDE.md'], 'nothing written beside it, no file left from a write');
});
