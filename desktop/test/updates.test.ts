// The updater: this repository's stable releases, looked at with the back-off, downloaded and installed only on the
// person's word, and the application's own previous version kept aside until the new one has started once.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { nextCheckDelay, updateCheckInterval } from '../src/backoff.ts';
import { fileLogger } from '../src/log.ts';
import {
    afterFailedStart,
    afterSuccessfulStart,
    applicationFolder,
    isFirstLaunchOfUpdate,
    isRejected,
    keptNotice,
    parseRollbackState,
    readRollback,
    rollbackStateFile,
    settleStart,
    writeRollback,
} from '../src/rollback.ts';
import { feed, firstCheckMs, menuItemOf, Updater, type FeedUpdater, type UpdateState } from '../src/updates.ts';
import { memoryLog, snapshot, written } from './harness.ts';

function scratch(t: { after(fn: () => void): void }) {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-updates-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const data = join(root, 'data');
    const rollback = join(data, 'desktop', 'rollback');
    // The installed application: a folder of the application's own files.
    const application = join(root, 'Programs', 'Jason');
    mkdirSync(join(application, 'resources', 'bundle'), { recursive: true });
    writeFileSync(join(application, 'Jason.exe'), 'the application 0.34.0');
    writeFileSync(join(application, 'resources', 'app.asar'), 'its code');
    writeFileSync(join(application, 'resources', 'bundle', 'jason.exe'), 'the command line it carries');
    mkdirSync(data, { recursive: true });
    return { root, data, rollback, application };
}

function handTimers() {
    const pending: { run: () => void; ms: number }[] = [];
    return {
        pending,
        setTimeout: (run: () => void, ms: number) => {
            const entry = { run, ms };
            pending.push(entry);
            return entry;
        },
        clearTimeout: (entry: unknown) => {
            const at = pending.indexOf(entry as { run: () => void; ms: number });
            if (at >= 0) pending.splice(at, 1);
        },
        async fire() {
            const next = pending.shift();
            assert.ok(next, 'a look was scheduled');
            next.run();
            for (let turn = 0; turn < 4; turn++) await new Promise((resolve) => setImmediate(resolve));
        },
    };
}

/** electron-updater's autoUpdater as far as the desktop uses it, recording what it was told. */
function fakeUpdater(answers: { latest?: string; fails?: () => boolean } = {}) {
    const set: [string, unknown][] = [];
    const calls: string[] = [];
    const feeds: unknown[] = [];
    const fields: Record<string, unknown> = {};
    const updater = {
        calls,
        set,
        feeds,
        latest: answers.latest ?? '0.34.0',
        setFeedURL(options: unknown) {
            feeds.push(options);
        },
        async checkForUpdates() {
            calls.push('check');
            if (answers.fails?.()) throw new Error('planted-update-error https://example.invalid/?token=planted-token');
            return { isUpdateAvailable: updater.latest !== '0.34.0', updateInfo: { version: updater.latest } };
        },
        async downloadUpdate() {
            calls.push('download');
            if (answers.fails?.()) throw new Error('planted-download-error');
            return ['planted-download-path'];
        },
        quitAndInstall() {
            calls.push('quitAndInstall');
        },
    };
    for (const name of ['autoDownload', 'autoInstallOnAppQuit', 'allowPrerelease', 'allowDowngrade', 'channel', 'logger']) {
        Object.defineProperty(updater, name, {
            get: () => fields[name],
            set: (value: unknown) => {
                fields[name] = value;
                set.push([name, value]);
            },
        });
    }
    return updater as typeof updater & FeedUpdater;
}

function updaterOn(s: ReturnType<typeof scratch>, updater: FeedUpdater, extra: { log?: ReturnType<typeof memoryLog> } = {}) {
    const timers = handTimers();
    const states: UpdateState[] = [];
    const order: string[] = [];
    const log = extra.log ?? memoryLog();
    const updates = new Updater({
        updater,
        version: '0.34.0',
        application: s.application,
        rollback: s.rollback,
        log,
        timers,
        beforeInstall: async () => void order.push('beforeInstall'),
        changed: (state) => void states.push(state),
    });
    return { updates, timers, states, order, log };
}

// ---- The rollback record ------------------------------------------------------------------------------------------

test('the rollback record: only the first launch of the version an update installed settles it', () => {
    const pending = { fromVersion: '0.34.0', toVersion: '0.35.0', directory: '/data/desktop/rollback/0.34.0' };
    assert.equal(isFirstLaunchOfUpdate({ pending, rejected: [] }, '0.35.0'), true);
    assert.equal(isFirstLaunchOfUpdate({ pending, rejected: [] }, '0.34.0'), false);
    assert.equal(isFirstLaunchOfUpdate({ rejected: [] }, '0.35.0'), false);
    assert.deepEqual(afterSuccessfulStart({ pending, rejected: ['0.33.9'] }), { rejected: ['0.33.9'] });
    const failed = afterFailedStart({ pending, rejected: [] });
    assert.equal(isRejected(failed, '0.35.0'), true);
    assert.deepEqual(failed.pending, pending, 'the copy stays recorded');
    assert.deepEqual(parseRollbackState('{'), { rejected: [] });
    assert.deepEqual(parseRollbackState(null), { rejected: [] });
    assert.deepEqual(parseRollbackState(JSON.stringify({ rejected: ['1', 2], pending: { fromVersion: 1 } })), { pending: undefined, rejected: ['1'] });
});

test('the application’s own folder is the .app on macOS and the executable’s folder on Windows', () => {
    assert.equal(applicationFolder('darwin', '/Applications/Jason.app/Contents/MacOS/Jason'), '/Applications/Jason.app');
    assert.equal(applicationFolder('win32', 'C:\\Users\\someone\\AppData\\Local\\Programs\\Jason\\Jason.exe'), 'C:\\Users\\someone\\AppData\\Local\\Programs\\Jason');
});

// ---- The updater --------------------------------------------------------------------------------------------------

test('the updater reads this repository’s stable releases only, and downloads and installs nothing by itself', (t) => {
    const s = scratch(t);
    const updater = fakeUpdater();
    const { updates, timers } = updaterOn(s, updater);
    updates.start();
    assert.deepEqual(feed, { provider: 'github', owner: 'reply-team', repo: 'jason-ai' });
    assert.deepEqual(updater.feeds, [feed]);
    const told = Object.fromEntries(updater.set);
    assert.deepEqual(told, { autoDownload: false, autoInstallOnAppQuit: false, allowPrerelease: false, allowDowngrade: false, logger: null });
    assert.equal(updater.set.some(([name]) => name === 'channel'), false, 'no channel is set');
    assert.deepEqual(timers.pending.map((entry) => entry.ms), [firstCheckMs]);
});

test('a newer release is offered and nothing more; the next look is the usual interval later', async (t) => {
    const s = scratch(t);
    const updater = fakeUpdater({ latest: '0.35.0' });
    const { updates, timers, states, log } = updaterOn(s, updater);
    updates.start();
    await timers.fire();
    assert.deepEqual(updater.calls, ['check'], 'nothing is downloaded until the person says so');
    assert.deepEqual(states.at(-1), { step: 'offered', version: '0.35.0' });
    assert.deepEqual(timers.pending.map((entry) => entry.ms), [updateCheckInterval]);
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [['update_offered', { Version: '0.35.0' }]]);
});

test('no newer release is logged as a look, and leaves the menu as it was', async (t) => {
    const s = scratch(t);
    const updater = fakeUpdater();
    const { updates, timers, states, log } = updaterOn(s, updater);
    updates.start();
    await timers.fire();
    assert.deepEqual(states.at(-1), { step: 'idle' });
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [['update_checked', { Available: false }]]);
});

test('a look that fails waits a minute, then twice as long each time, and the usual interval once one succeeds', async (t) => {
    const s = scratch(t);
    let failing = true;
    const updater = fakeUpdater({ latest: '0.34.0', fails: () => failing });
    const { updates, timers, log } = updaterOn(s, updater);
    updates.start();
    await timers.fire();
    assert.deepEqual(timers.pending.map((entry) => entry.ms), [nextCheckDelay(1)]);
    await timers.fire();
    assert.deepEqual(timers.pending.map((entry) => entry.ms), [nextCheckDelay(2)]);
    failing = false;
    await timers.fire();
    assert.deepEqual(timers.pending.map((entry) => entry.ms), [updateCheckInterval]);
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [
        ['update_failed', { Step: 'check', Failures: 1 }],
        ['update_failed', { Step: 'check', Failures: 2 }],
        ['update_checked', { Available: false }],
    ]);
});

test('a version whose first start failed here is not offered again', async (t) => {
    const s = scratch(t);
    writeRollback(s.rollback, { rejected: ['0.35.0'] });
    const updater = fakeUpdater({ latest: '0.35.0' });
    const { updates, timers, states } = updaterOn(s, updater);
    updates.start();
    await timers.fire();
    assert.deepEqual(states.at(-1), { step: 'idle' });
});

test('the download happens on the person’s word, and the install on a second one', async (t) => {
    const s = scratch(t);
    const updater = fakeUpdater({ latest: '0.35.0' });
    const { updates, timers, states, log } = updaterOn(s, updater);
    updates.start();
    await timers.fire();
    await updates.download();
    assert.deepEqual(updater.calls, ['check', 'download']);
    assert.deepEqual(states.at(-1), { step: 'downloaded', version: '0.35.0' });
    assert.equal(updater.calls.includes('quitAndInstall'), false, 'a download installs nothing');
    assert.deepEqual(log.lines.at(-1), { level: 'info', name: 'update_downloaded', fields: { Version: '0.35.0' } });
});

test('a download that fails goes back to the offer, logged by its step', async (t) => {
    const s = scratch(t);
    let failing = false;
    const updater = fakeUpdater({ latest: '0.35.0', fails: () => failing });
    const { updates, timers, states, log } = updaterOn(s, updater);
    updates.start();
    await timers.fire();
    failing = true;
    await updates.download();
    assert.deepEqual(states.at(-1), { step: 'offered', version: '0.35.0' });
    assert.deepEqual(log.lines.at(-1)?.fields, { Step: 'download', Failures: 1 });
});

test('installing copies the application’s own folder aside under <data>/desktop/rollback, records it, then installs', async (t) => {
    const s = scratch(t);
    // A copy an earlier update left is replaced, not kept beside the new one.
    mkdirSync(join(s.rollback, '0.33.0', 'Jason'), { recursive: true });
    writeFileSync(join(s.rollback, '0.33.0', 'Jason', 'Jason.exe'), 'the application 0.33.0');
    const updater = fakeUpdater({ latest: '0.35.0' });
    const { updates, timers, order, log } = updaterOn(s, updater);
    updates.start();
    await timers.fire();
    assert.equal(await updates.install(), false, 'nothing is installed before it is downloaded');
    await updates.download();
    const before = snapshot(s.data);
    const application = snapshot(s.application);
    assert.equal(await updates.install(), true);

    const copy = join(s.rollback, '0.34.0', 'Jason');
    assert.deepEqual(snapshot(copy), application, 'the copy is the application as it is installed');
    assert.deepEqual(readdirSync(s.rollback).sort(), ['0.34.0', 'state.json']);
    assert.deepEqual(readRollback(s.rollback), { pending: { fromVersion: '0.34.0', toVersion: '0.35.0', directory: copy }, rejected: [] });
    for (const path of written(before, snapshot(s.data))) assert.ok(path.startsWith('desktop/rollback/0.34.0/') || path === 'desktop/rollback/state.json', `the install wrote ${path}`);
    assert.deepEqual(snapshot(s.application), application, 'the installed application is not touched before the installer runs');
    assert.deepEqual([...order, ...updater.calls.slice(-1)], ['beforeInstall', 'quitAndInstall']);
    assert.deepEqual(log.lines.at(-1), { level: 'info', name: 'update_installing', fields: { From: '0.34.0', To: '0.35.0' } });
    assert.equal(existsSync(rollbackStateFile(s.rollback)), true);
});

test('the copy holds no file of <data>, even when <data> lies inside the installed application, and its record names no secret', async (t) => {
    const s = scratch(t);
    // A data directory a person put inside the application's folder, holding the run token and a provider key.
    const data = join(s.application, 'data');
    const rollback = join(data, 'desktop', 'rollback');
    mkdirSync(join(data, 'runtime'), { recursive: true });
    mkdirSync(join(data, 'desktop'), { recursive: true });
    writeFileSync(join(data, 'runtime', 'run.json'), '{"token":"planted-run-token-55"}');
    writeFileSync(join(data, 'desktop', 'provider-keys.json'), '{"reply":"planted-provider-key-55"}');
    const application = [...snapshot(s.application).keys()].filter((path) => !path.startsWith('data/')).sort();
    const updater = fakeUpdater({ latest: '0.35.0' });
    const { updates, timers } = updaterOn({ ...s, data, rollback }, updater);
    updates.start();
    await timers.fire();
    await updates.download();
    assert.equal(await updates.install(), true);

    const copy = join(rollback, '0.34.0', 'Jason');
    assert.deepEqual([...snapshot(copy).keys()].sort(), application, 'the copy is the application without the data directory');
    for (const path of snapshot(copy).keys()) assert.doesNotMatch(readFileSync(join(copy, path), 'utf8'), /planted-/, `${path} carries a secret`);
    const record = readFileSync(rollbackStateFile(rollback), 'utf8');
    assert.doesNotMatch(record, /planted-|token|key/i);
    assert.deepEqual(Object.keys(JSON.parse(record)).sort(), ['pending', 'rejected']);
});

test('the starting page, when an update did not start, names the kept copy and says what putting it back does', () => {
    const notice = keptNotice('The runtime did not start.', 'C:/data/desktop/rollback/0.34.0/Jason');
    assert.ok(notice.startsWith('The runtime did not start. '));
    assert.ok(notice.includes('C:/data/desktop/rollback/0.34.0/Jason'));
    assert.ok(notice.includes('This puts back the previous application and the Jason it carried; the data directory stays as the newer version left it.'));
    assert.match(notice, /by hand/);
});

test('the new version’s first start settles the record: a start that worked removes the copy, one that failed keeps it and refuses that version', (t) => {
    const s = scratch(t);
    const copy = join(s.rollback, '0.34.0', 'Jason');
    const pending = { fromVersion: '0.34.0', toVersion: '0.35.0', directory: copy };
    const log = memoryLog();

    mkdirSync(copy, { recursive: true });
    writeRollback(s.rollback, { pending, rejected: [] });
    assert.equal(settleStart(s.rollback, '0.34.0', true, log), null, 'a launch of another version settles nothing');
    assert.equal(existsSync(copy), true);
    assert.equal(settleStart(s.rollback, '0.35.0', true, log), null);
    assert.equal(existsSync(copy), false);
    assert.deepEqual(readRollback(s.rollback), { pending: undefined, rejected: [] });

    mkdirSync(copy, { recursive: true });
    writeRollback(s.rollback, { pending, rejected: [] });
    assert.equal(settleStart(s.rollback, '0.35.0', false, log), copy, 'the person is told where the previous version is');
    assert.equal(existsSync(copy), true);
    assert.equal(isRejected(readRollback(s.rollback), '0.35.0'), true);
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [
        ['update_settled', { Version: '0.35.0', Started: true }],
        ['update_settled', { Version: '0.35.0', Started: false }],
    ]);
});

test('the menu offers one step at a time, and in a build that is not installed none', () => {
    assert.deepEqual(menuItemOf({ step: 'idle' }, true), { label: 'Check for Updates…', enabled: true, act: 'check' });
    assert.deepEqual(menuItemOf({ step: 'checking' }, true), { label: 'Checking for Updates…', enabled: false, act: null });
    assert.deepEqual(menuItemOf({ step: 'offered', version: '0.35.0' }, true), { label: 'Download Jason 0.35.0', enabled: true, act: 'download' });
    assert.deepEqual(menuItemOf({ step: 'downloading', version: '0.35.0' }, true), { label: 'Downloading Jason 0.35.0…', enabled: false, act: null });
    assert.deepEqual(menuItemOf({ step: 'downloaded', version: '0.35.0' }, true), { label: 'Restart to Install Jason 0.35.0', enabled: true, act: 'install' });
    assert.deepEqual(menuItemOf({ step: 'installing', version: '0.35.0' }, true), { label: 'Installing Jason 0.35.0…', enabled: false, act: null });
    assert.deepEqual(menuItemOf({ step: 'idle' }, false), { label: 'Updates Come to an Installed Build', enabled: false, act: null });
});

test('the updater’s log lines, read back, carry versions, steps and counts, never an error’s text or a URL', async (t) => {
    const s = scratch(t);
    const logs = join(s.root, 'logs');
    let failing = true;
    const updater = fakeUpdater({ latest: '0.35.0', fails: () => failing });
    const timers = handTimers();
    const updates = new Updater({ updater, version: '0.34.0', application: s.application, rollback: s.rollback, log: fileLogger(logs), timers, beforeInstall: async () => undefined, changed: () => undefined });
    updates.start();
    await timers.fire();
    failing = false;
    await timers.fire();
    failing = true;
    await updates.download();
    failing = false;
    await updates.download();
    await updates.install();
    const text = readdirSync(logs).map((name) => readFileSync(join(logs, name), 'utf8')).join('');
    for (const planted of ['planted', 'https://', 'token']) assert.equal(text.includes(planted), false, `${planted} reached the log`);
    assert.deepEqual(text.split(/\r?\n/).filter(Boolean).map((line) => (JSON.parse(line) as { EventName: string }).EventName), [
        'update_failed',
        'update_offered',
        'update_failed',
        'update_downloaded',
        'update_installing',
    ]);
});
