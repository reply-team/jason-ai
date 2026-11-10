import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { judge } from '../src/chat-permission-model.ts';
import { realPathOf } from '../src/chat-real-path.ts';
import { PermissionBroker } from '../src/chat-permission-broker.ts';
import { hostCallLimitMs, newSecret, newTurnId, removeTurnFiles, turnConfig, turnConfigPath, writeTurnConfig } from '../src/chat-turn-files.ts';
import { desktopPaths } from '../src/paths.ts';

function data(): string {
    const root = mkdtempSync(join(tmpdir(), 'jason-turn-'));
    mkdirSync(join(root, 'desktop', 'chat'), { recursive: true });
    return root;
}

test('the turn\'s MCP configuration names one HTTP server with the turn\'s secret as a bearer token and a time limit above the broker\'s, and nothing else', () => {
    assert.deepEqual(turnConfig('http://127.0.0.1:50123/mcp', 's3cret'), {
        mcpServers: { jason_desktop: { type: 'http', url: 'http://127.0.0.1:50123/mcp', headers: { Authorization: 'Bearer s3cret' }, timeout: 2_100_000 } },
    });
    assert.ok(hostCallLimitMs > PermissionBroker.unanswered, 'the host must never give up on a call before the broker answers it');
});

test('a turn\'s id and secret are fresh and long enough that nobody guesses them', () => {
    const ids = new Set(Array.from({ length: 50 }, newTurnId));
    assert.equal(ids.size, 50);
    for (const id of ids) assert.match(id, /^[0-9a-f]{24}$/);
    const secrets = new Set(Array.from({ length: 50 }, newSecret));
    assert.equal(secrets.size, 50);
    for (const secret of secrets) assert.match(secret, /^[A-Za-z0-9_-]{43}$/);
});

test('the configuration is written under <data>/desktop/turns/, made when it is not there, outside the chat folder, readable by this user alone', (t) => {
    const root = data();
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const paths = desktopPaths(root);
    const id = newTurnId();
    assert.equal(existsSync(paths.turns), false);
    const file = writeTurnConfig(paths.turns, id, 'http://127.0.0.1:50123/mcp', 's3cret');
    assert.equal(file, turnConfigPath(paths.turns, id));
    assert.equal(dirname(file), join(root, 'desktop', 'turns'));
    assert.deepEqual(JSON.parse(readFileSync(file, 'utf8')), turnConfig('http://127.0.0.1:50123/mcp', 's3cret'));
    if (process.platform !== 'win32') assert.equal(statSync(file).mode & 0o777, 0o600);
});

test('the chat\'s agent is refused a read or a write of the configuration', (t) => {
    const root = data();
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const paths = desktopPaths(root);
    const file = writeTurnConfig(paths.turns, newTurnId(), 'http://127.0.0.1:50123/mcp', 's3cret');
    const platform = process.platform === 'win32' || process.platform === 'darwin' ? process.platform : 'linux';
    const scope = { platform, cwd: paths.chat, chat: paths.chat, added: [], data: root, protectedFiles: [], denied: [], deniedOperations: [], home: root, real: realPathOf } as const;
    assert.equal(judge('Read', { file_path: file }, scope).verdict, 'deny');
    assert.equal(judge('Read', { file_path: join('..', 'turn-x.mcp.json') }, scope).verdict, 'deny');
    assert.equal(judge('Read', { file_path: join('..', 'turns', 'turn-x.mcp.json') }, scope).verdict, 'deny');
    assert.equal(judge('Write', { file_path: file, content: '{}' }, scope).verdict, 'deny');
});

test('a turn\'s file is never written twice, and only for an id the desktop made', (t) => {
    const root = data();
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const desktop = desktopPaths(root).turns;
    const id = newTurnId();
    writeTurnConfig(desktop, id, 'http://127.0.0.1:1/mcp', 'a');
    assert.throws(() => writeTurnConfig(desktop, id, 'http://127.0.0.1:1/mcp', 'b'));
    for (const bad of ['../x', 'ABC', '', `${id}/..`]) assert.throws(() => turnConfigPath(desktop, bad), /turn id/);
});

test('the turn\'s file is removed when the turn ends, and removing it twice is no error', (t) => {
    const root = data();
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const desktop = desktopPaths(root).turns;
    const id = newTurnId();
    const file = writeTurnConfig(desktop, id, 'http://127.0.0.1:1/mcp', 'a');
    removeTurnFiles(desktop, id);
    assert.equal(existsSync(file), false);
    removeTurnFiles(desktop, id);
});
