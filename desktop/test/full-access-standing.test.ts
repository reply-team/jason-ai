// The standing choice of Full access: the file the main process keeps it in, read as no choice whenever it says anything
// but this build's consent, and the confirmation's three answers that alone may give it.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fullAccessAnswer, fullAccessConsent, fullAccessDialog } from '../src/chat-modes.ts';
import { parseStanding, readStanding, writeStanding } from '../src/full-access-standing.ts';
import { desktopPaths } from '../src/paths.ts';

const given = '2026-01-02T03:04:05.000Z';
const entry = (consent: unknown, extra: Record<string, unknown> = {}) => JSON.stringify({ claude: { consent, given_at: given, new_chats: false, ...extra } });

test('the consent number is one, and the file is the desktop folder\'s own', () => {
    assert.equal(fullAccessConsent, 1);
    assert.equal(desktopPaths(join('/', 'data')).fullAccess, join('/', 'data', 'desktop', 'full-access.json'));
});

test('a choice is read only under this build\'s consent number: a lower or a higher one is no choice', () => {
    assert.deepEqual(parseStanding(entry(fullAccessConsent), 'claude'), { consent: fullAccessConsent, givenAt: given, newChats: false });
    assert.equal(parseStanding(entry(fullAccessConsent - 1), 'claude'), null);
    assert.equal(parseStanding(entry(0), 'claude'), null);
    assert.equal(parseStanding(entry(fullAccessConsent + 1), 'claude'), null);
    assert.equal(parseStanding(entry('1'), 'claude'), null);
});

test('a file that is absent, malformed or says anything wrongly is no choice', () => {
    assert.equal(parseStanding(null, 'claude'), null);
    assert.equal(parseStanding('', 'claude'), null);
    assert.equal(parseStanding('{', 'claude'), null);
    assert.equal(parseStanding('[]', 'claude'), null);
    assert.equal(parseStanding('null', 'claude'), null);
    assert.equal(parseStanding('{"claude":true}', 'claude'), null);
    assert.equal(parseStanding(entry(fullAccessConsent, { new_chats: 'yes' }), 'claude'), null);
    assert.equal(parseStanding(entry(fullAccessConsent, { given_at: 7 }), 'claude'), null);
    assert.equal(parseStanding(entry(fullAccessConsent, { given_at: 'yesterday' }), 'claude'), null);
    assert.deepEqual(parseStanding(entry(fullAccessConsent, { new_chats: true }), 'claude')?.newChats, true);
});

test('the choice is keyed by host program: one for Claude Code is none for Codex', () => {
    assert.equal(parseStanding(entry(fullAccessConsent), 'codex'), null);
    assert.equal(parseStanding(JSON.stringify({ codex: { consent: fullAccessConsent, given_at: given, new_chats: true } }), 'claude'), null);
});

test('writing keeps the other host\'s entry, taking back removes only its own, and the file is the account\'s alone', (t) => {
    const root = mkdtempSync(join(tmpdir(), 'jason-standing-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const file = join(root, 'desktop', 'full-access.json');
    assert.equal(readStanding(file, 'claude'), null);
    writeStanding(file, 'claude', { consent: fullAccessConsent, givenAt: given, newChats: true });
    assert.deepEqual(readStanding(file, 'claude'), { consent: fullAccessConsent, givenAt: given, newChats: true });
    assert.deepEqual(JSON.parse(readFileSync(file, 'utf8')), { claude: { consent: fullAccessConsent, given_at: given, new_chats: true } });
    if (process.platform !== 'win32') assert.equal(statSync(file).mode & 0o777, 0o600);
    writeFileSync(file, JSON.stringify({ claude: { consent: fullAccessConsent, given_at: given, new_chats: false }, codex: { consent: fullAccessConsent, given_at: given, new_chats: false } }));
    writeStanding(file, 'claude', null);
    assert.equal(readStanding(file, 'claude'), null);
    assert.deepEqual(readStanding(file, 'codex'), { consent: fullAccessConsent, givenAt: given, newChats: false });
    writeStanding(file, 'codex', null);
    assert.ok(!existsSync(file), 'a file with no choice in it is removed');
    writeStanding(file, 'claude', null);
    assert.ok(!existsSync(file));
});

test('a write that fails leaves no file half-written and nothing a later read could take, and throws what failed', (t) => {
    const root = mkdtempSync(join(tmpdir(), 'jason-standing-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const folder = join(root, 'desktop');
    const file = join(folder, 'full-access.json');
    // A folder where the file would be: the file beside it is written, and the rename over it fails.
    mkdirSync(file, { recursive: true });
    assert.throws(() => writeStanding(file, 'claude', { consent: fullAccessConsent, givenAt: given, newChats: true }));
    assert.deepEqual(readdirSync(folder), ['full-access.json'], 'the file beside it is removed again');
    assert.equal(readStanding(file, 'claude'), null);
});

test('reading is strict: a key it does not know, a key named twice, a moment that is no date or a file too large is no choice', (t) => {
    const right = { consent: fullAccessConsent, given_at: given, new_chats: true };
    assert.deepEqual(parseStanding(JSON.stringify({ claude: right }), 'claude'), { consent: fullAccessConsent, givenAt: given, newChats: true });
    assert.equal(parseStanding(JSON.stringify({ claude: right, gemini: right }), 'claude'), null, 'a host it does not know');
    assert.equal(parseStanding(JSON.stringify({ claude: right, note: 'x' }), 'claude'), null, 'a key it does not know');
    assert.equal(parseStanding(JSON.stringify({ claude: { ...right, scope: 'all' } }), 'claude'), null, 'a field it does not know');
    const once = JSON.stringify({ claude: { ...right, new_chats: false } });
    assert.equal(parseStanding(`{"claude":${JSON.stringify(right)},${once.slice(1)}`, 'claude'), null, 'a host named twice');
    assert.equal(parseStanding(`{"claude":{"consent":1,"given_at":"${given}","new_chats":false,"new_chats":true}}`, 'claude'), null, 'a field named twice');
    for (const moment of ['2026-99-99T99:99:99Z', '2026-02-30T00:00:00Z', '2026-13-01T00:00:00.000Z']) {
        assert.equal(parseStanding(JSON.stringify({ claude: { ...right, given_at: moment } }), 'claude'), null, moment);
    }
    assert.equal(parseStanding(`${JSON.stringify({ claude: right })}${' '.repeat(5000)}`, 'claude'), null, 'larger than the desktop writes');
    const root = mkdtempSync(join(tmpdir(), 'jason-standing-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const file = join(root, 'full-access.json');
    writeFileSync(file, `${JSON.stringify({ claude: right })}${' '.repeat(5000)}`);
    assert.equal(readStanding(file, 'claude'), null);
});

test('a write goes through a file no one can foretell, made only where nothing is, and leaves nothing beside it', (t) => {
    const root = mkdtempSync(join(tmpdir(), 'jason-standing-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const file = join(root, 'full-access.json');
    // A file put where a predictable name would have been is neither followed nor taken.
    writeFileSync(`${file}.${process.pid}.tmp`, 'planted');
    writeStanding(file, 'claude', { consent: fullAccessConsent, givenAt: given, newChats: false });
    writeStanding(file, 'claude', { consent: fullAccessConsent, givenAt: given, newChats: true });
    assert.deepEqual(readStanding(file, 'claude'), { consent: fullAccessConsent, givenAt: given, newChats: true });
    assert.deepEqual(readdirSync(root).sort(), ['full-access.json', `full-access.json.${process.pid}.tmp`]);
    assert.equal(readFileSync(`${file}.${process.pid}.tmp`, 'utf8'), 'planted');
});

test('the confirmation offers This time, Always and Cancel, Cancel focused and Escape\'s answer, and a checkbox that counts only with Always', () => {
    const dialog = fullAccessDialog();
    assert.deepEqual(dialog.buttons, ['Give full access this time', 'Always on this computer', 'Cancel']);
    assert.equal(dialog.defaultId, 2);
    assert.equal(dialog.cancelId, 2);
    assert.equal(dialog.checkboxLabel, 'With Always, also start new chats in Full access');
    assert.equal(dialog.checkboxChecked, false);
    assert.deepEqual(fullAccessAnswer(0, false), { always: false, newChats: false });
    assert.deepEqual(fullAccessAnswer(0, true), { always: false, newChats: false }, 'with This time the checkbox is ignored');
    assert.deepEqual(fullAccessAnswer(1, false), { always: true, newChats: false });
    assert.deepEqual(fullAccessAnswer(1, true), { always: true, newChats: true });
    assert.equal(fullAccessAnswer(2, true), null);
    assert.equal(fullAccessAnswer(3, false), null);
    assert.equal(fullAccessAnswer(-1, false), null);
});
