// The desktop window's pairing with the runtime is kept off the disk: the page runs in a session Electron keeps in
// memory, what an earlier version's page left on disk is unpaired and removed at the first start, and the token the
// page holds now is ended once the next pairing has succeeded.

import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import type { Cli, CliResult } from '../src/cli.ts';
import { desktopPaths } from '../src/paths.ts';
import {
    earlierPageStorage,
    isTokenId,
    loadWindow,
    parseWindowPairing,
    persists,
    readWindowPairing,
    WindowPairing,
    windowPartition,
    writeWindowPairing,
    type WindowPairingState,
} from '../src/window-pairing.ts';
import { memoryLog } from './harness.ts';

const oldId = 'uit_01JD0000000000000000000001';
const newId = 'uit_01JD0000000000000000000002';
/** A UI token's shape: what the runtime hands the page, never an id. */
const plantedToken = 'planted-ui-token-Zx8_q2Lm4Nv7Pw1Rt5Ys9Ua3Bc6Df0Gh2Jk4Mn';

function scratch(t: { after(fn: () => void): void }): string {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-window-pairing-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    return root;
}

/** A CLI that records what it was asked and answers `ui revoke` as the real one does, or with `exit`. */
function revokingCli(exit = 0, revoked = 2): Cli & { asked: string[][] } {
    const asked: string[][] = [];
    return {
        asked,
        async run(args): Promise<CliResult> {
            asked.push([...args]);
            if (exit !== 0) return { exit, stdout: '', stderr: 'No runtime answered.' };
            return { exit: 0, stdout: JSON.stringify({ revoked }), stderr: '' };
        },
    };
}

test('the window runs in a partition Electron keeps in memory: the constant carries no persist: form, and the window is made with it', () => {
    assert.equal(persists(windowPartition), false);
    assert.equal(windowPartition.trim().length > 0, true, 'an empty partition is the default session, which is on disk');
    assert.equal(persists('persist:jason-window'), true);
    assert.equal(persists(' persist:jason-window'.trim()), true);
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    const preferences = main.indexOf('webPreferences: {');
    assert.ok(preferences > 0, 'main.ts makes the window with web preferences');
    const end = main.indexOf('},', preferences);
    assert.ok(main.slice(preferences, end).includes('partition: windowPartition'), 'the window names the in-memory partition');
    assert.equal(main.includes("partition: 'persist:"), false);
    assert.equal(main.includes('fromPartition('), false, 'no other session is made for a page');
});

test('the first start after an update unpairs every UI token before the window loads, then clears and removes what the earlier page left', async (t) => {
    const electron = join(scratch(t), 'electron');
    const steps: string[] = [];
    const cli = revokingCli(0, 3);
    const log = memoryLog();
    const state = await loadWindow(
        { cleared: false, found: true },
        {
            cli: { run: async (args, options) => (steps.push(`cli ${args.join(' ')}`), cli.run(args, options)) },
            clearSession: async () => void steps.push('clear session'),
            remove: (folder) => void steps.push(`remove ${folder}`),
            electron,
            log,
        },
        async () => void steps.push('load'),
    );
    assert.deepEqual(steps, [
        'cli ui revoke --all',
        'clear session',
        ...earlierPageStorage.map((name) => `remove ${join(electron, name)}`),
        'load',
    ]);
    assert.deepEqual(earlierPageStorage, ['Local Storage', 'Session Storage', 'IndexedDB']);
    assert.equal(state.cleared, true);
    assert.deepEqual(log.lines, [{ level: 'info', name: 'page_storage_cleared', fields: { Revoked: 3 } }]);
});

test('a revoke that fails removes nothing and leaves the flag unset, so the next start tries again; the window still loads', async (t) => {
    const electron = join(scratch(t), 'electron');
    const steps: string[] = [];
    const log = memoryLog();
    const state = await loadWindow(
        { cleared: false, found: true },
        { cli: revokingCli(3), clearSession: async () => void steps.push('clear session'), remove: (folder) => void steps.push(`remove ${folder}`), electron, log },
        async () => void steps.push('load'),
    );
    assert.deepEqual(steps, ['load']);
    assert.equal(state.cleared, false);
    assert.deepEqual(log.lines, [{ level: 'warn', name: 'page_storage_clear_failed', fields: { Step: 'revoke', Code: '3' } }]);
});

test('a set flag, or no page storage of an earlier version, revokes nothing', async (t) => {
    const electron = join(scratch(t), 'electron');
    for (const state of [
        { cleared: true, found: true },
        { cleared: true, found: false },
        { cleared: false, found: false },
    ]) {
        const cli = revokingCli();
        const removed: string[] = [];
        let cleared = 0;
        let loaded = 0;
        const after = await loadWindow(
            state,
            { cli, clearSession: async () => void cleared++, remove: (folder) => void removed.push(folder), electron, log: memoryLog() },
            async () => void loaded++,
        );
        assert.deepEqual(cli.asked, [], JSON.stringify(state));
        assert.deepEqual([removed, cleared, loaded], [[], 0, 1]);
        assert.equal(after.cleared, true, 'nothing is left to clear');
    }
});

test('a removal that fails after the revoke still sets the flag, since the tokens are dead, and says where it failed', async (t) => {
    const electron = join(scratch(t), 'electron');
    const log = memoryLog();
    const busy = Object.assign(new Error('busy'), { code: 'EBUSY' });
    const state = await loadWindow(
        { cleared: false, found: true },
        {
            cli: revokingCli(0, 1),
            clearSession: async () => {
                throw new Error('gone');
            },
            remove: (folder) => {
                if (folder.endsWith('Session Storage')) throw busy;
            },
            electron,
            log,
        },
        async () => undefined,
    );
    assert.equal(state.cleared, true);
    assert.deepEqual(log.lines, [
        { level: 'warn', name: 'page_storage_clear_failed', fields: { Step: 'session', Code: 'unknown' } },
        { level: 'warn', name: 'page_storage_clear_failed', fields: { Step: 'remove', Code: 'EBUSY' } },
        { level: 'info', name: 'page_storage_cleared', fields: { Revoked: 1 } },
    ]);
});

test('a token id has the runtime\'s shape, and the token itself never does', () => {
    assert.equal(isTokenId(oldId), true);
    for (const value of [plantedToken, 'uit_1', `${oldId} `, 'cmp_01JD0000000000000000000001', '', null, 7, { id: oldId }]) {
        assert.equal(isTokenId(value), false, String(value));
    }
});

test('the token the window held is ended only after the next pairing has succeeded, and the same id never', async () => {
    const cli = revokingCli(0, 1);
    let state: WindowPairingState = { cleared: true, tokenId: oldId };
    const pairing = new WindowPairing({ cli, log: memoryLog(), state: () => state, save: (next) => void (state = next) });
    assert.deepEqual(cli.asked, [], 'nothing is ended before the page reports a new pairing');
    await pairing.paired(newId);
    assert.deepEqual(cli.asked, [['ui', 'revoke', oldId]]);
    assert.equal(state.tokenId, newId, 'the new id is kept before the old one is ended');
    await pairing.paired(newId);
    assert.deepEqual(cli.asked, [['ui', 'revoke', oldId]], 'a page reporting its own id again ends nothing');
});

test('the member the page reports with cannot carry the token: anything but an id is refused, and nothing is kept or ended', async () => {
    const cli = revokingCli();
    let state: WindowPairingState = { cleared: true, tokenId: oldId };
    let saved = 0;
    const pairing = new WindowPairing({ cli, log: memoryLog(), state: () => state, save: (next) => void (saved++, (state = next)) });
    for (const value of [plantedToken, { token: plantedToken, token_id: newId }, null, 'uit_1']) {
        await assert.rejects(pairing.paired(value), /token id/);
    }
    assert.deepEqual([cli.asked, saved, state.tokenId], [[], 0, oldId]);
});

test('an old token the runtime would not end is logged by the exit alone, and the new id is kept', async () => {
    const log = memoryLog();
    let state: WindowPairingState = { cleared: true, tokenId: oldId };
    const pairing = new WindowPairing({ cli: revokingCli(1), log, state: () => state, save: (next) => void (state = next) });
    await pairing.paired(newId);
    assert.equal(state.tokenId, newId);
    assert.deepEqual(log.lines, [{ level: 'warn', name: 'window_token_revoke_failed', fields: { Exit: 1 } }]);
    assert.equal(JSON.stringify(log.lines).includes(oldId), false, 'the id is not logged');
});

test('the record is the desktop\'s own file beside its settings, with the flag and an id, and anything said wrongly is unset', (t) => {
    const file = desktopPaths(scratch(t)).windowPairing;
    assert.ok(file.endsWith(join('desktop', 'window-pairing.json')));
    assert.deepEqual(readWindowPairing(file), { cleared: false, tokenId: null });
    writeWindowPairing(file, { cleared: true, tokenId: newId });
    assert.deepEqual(JSON.parse(readFileSync(file, 'utf8')), { page_storage_cleared: true, token_id: newId });
    assert.deepEqual(readWindowPairing(file), { cleared: true, tokenId: newId });
    writeFileSync(file, JSON.stringify({ page_storage_cleared: 'yes', token_id: plantedToken }));
    assert.deepEqual(readWindowPairing(file), { cleared: false, tokenId: null });
    assert.deepEqual(parseWindowPairing('{'), { cleared: false, tokenId: null });
});
