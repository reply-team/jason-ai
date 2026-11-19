// The updater: this repository's stable releases, looked at with the back-off, downloaded and installed only on the
// person's word, and the application's own previous version kept aside until the next update replaces it.

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
    failedStartNotice,
    isFirstLaunchOfUpdate,
    isRejected,
    keptLine,
    keepPrevious,
    keptNotice,
    parseRollbackState,
    readRollback,
    rollbackStateFile,
    settleByRequest,
    settleFailedStart,
    settleStart,
    writeRollback,
} from '../src/rollback.ts';
import type { StopOutcome } from '../src/own-runtime.ts';
import { debFile, feed, firstCheckMs, installQuestion, installRefusal, keepRefusal, menuItemOf, packageMovedTo, pressItem, previousRelease, requestUnderWayRefusal, restartForPackage, restartOnce, restartQuestion, runtimeRefusal, updateKeyWords, Updater, type FeedUpdater, type TakenStep, type UpdateState } from '../src/updates.ts';
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
function fakeUpdater(answers: { latest?: string; fails?: () => boolean; inactive?: boolean; installFails?: boolean } = {}) {
    const set: [string, unknown][] = [];
    const calls: string[] = [];
    const feeds: unknown[] = [];
    const fields: Record<string, unknown> = {};
    const listeners: ((error: Error) => void)[] = [];
    const updater = {
        calls,
        listeners,
        isUpdaterActive: () => answers.inactive !== true,
        on(event: 'error', listener: (error: Error) => void) {
            if (event === 'error') listeners.push(listener);
            return updater;
        },
        removeListener(event: 'error', listener: (error: Error) => void) {
            const at = listeners.indexOf(listener);
            if (event === 'error' && at >= 0) listeners.splice(at, 1);
            return updater;
        },
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
        quitAndInstall(...args: unknown[]) {
            // The person's click calls it with nothing; a terminal's request names the silent install and no start after it.
            calls.push(args.length === 0 ? 'quitAndInstall' : `quitAndInstall(${args.join(', ')})`);
            // electron-updater's install() returning false: it dispatches the error synchronously and does not quit.
            if (answers.installFails) for (const listener of [...listeners]) listener(new Error('planted-install-error'));
        },
    };
    for (const name of ['autoDownload', 'autoInstallOnAppQuit', 'allowPrerelease', 'allowDowngrade', 'channel', 'logger', 'autoRunAppAfterInstall']) {
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

function updaterOn(
    s: ReturnType<typeof scratch>,
    updater: FeedUpdater,
    extra: { log?: ReturnType<typeof memoryLog>; stop?: () => Promise<StopOutcome>; application?: string | null } = {},
) {
    const timers = handTimers();
    const states: UpdateState[] = [];
    const order: string[] = [];
    const told: string[] = [];
    const log = extra.log ?? memoryLog();
    const updates = new Updater({
        updater,
        version: '0.34.0',
        application: extra.application === undefined ? s.application : extra.application,
        rollback: s.rollback,
        log,
        timers,
        stopRuntime: async () => {
            // What had been kept aside when the runtime was stopped: nothing yet, since the keep comes after the stop.
            order.push(existsSync(join(s.rollback, '0.34.0')) ? 'stopRuntime after the keep' : 'stopRuntime');
            return extra.stop ? extra.stop() : { kind: 'none' };
        },
        refused: (why) => void told.push(why),
        beforeInstall: async () => void order.push('beforeInstall'),
        afterRefused: async () => void order.push('afterRefused'),
        changed: (state) => void states.push(state),
    });
    return { updates, timers, states, order, told, log };
}

// ---- The rollback record ------------------------------------------------------------------------------------------

test('the rollback record: only the first launch of the version an update installed settles it', () => {
    const pending = { fromVersion: '0.34.0', toVersion: '0.35.0', directory: '/data/desktop/rollback/0.34.0' };
    assert.equal(isFirstLaunchOfUpdate({ pending, rejected: [] }, '0.35.0'), true);
    assert.equal(isFirstLaunchOfUpdate({ pending, rejected: [] }, '0.34.0'), false);
    assert.equal(isFirstLaunchOfUpdate({ rejected: [] }, '0.35.0'), false);
    assert.deepEqual(afterSuccessfulStart({ pending, rejected: ['0.33.9'] }), { kept: { version: '0.34.0', directory: pending.directory }, rejected: ['0.33.9'] });
    const failed = afterFailedStart({ pending, rejected: [] });
    assert.equal(isRejected(failed, '0.35.0'), true);
    assert.deepEqual(failed.pending, pending, 'the copy stays recorded');
    assert.deepEqual(parseRollbackState('{'), { rejected: [] });
    assert.deepEqual(parseRollbackState(null), { rejected: [] });
    assert.deepEqual(parseRollbackState(JSON.stringify({ rejected: ['1', 2], pending: { fromVersion: 1 } })), { pending: undefined, rejected: ['1'] });
    assert.deepEqual(parseRollbackState(JSON.stringify({ rejected: [], kept: { version: '0.34.0', directory: '/d' } })), { pending: undefined, kept: { version: '0.34.0', directory: '/d' }, rejected: [] });
    assert.deepEqual(parseRollbackState(JSON.stringify({ rejected: [], kept: { version: 1 } })), { pending: undefined, rejected: [] });
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
    // A copy an earlier update left, kept since that update started, is replaced, not kept beside the new one.
    mkdirSync(join(s.rollback, '0.33.0', 'Jason'), { recursive: true });
    writeFileSync(join(s.rollback, '0.33.0', 'Jason', 'Jason.exe'), 'the application 0.33.0');
    writeRollback(s.rollback, { kept: { version: '0.33.0', directory: join(s.rollback, '0.33.0', 'Jason') }, rejected: [] });
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
    assert.deepEqual([...order, ...updater.calls.slice(-1)], ['stopRuntime', 'beforeInstall', 'quitAndInstall'], 'the runtime the application carries is stopped before anything closes');
    assert.deepEqual(log.lines.at(-1), { level: 'info', name: 'update_installing', fields: { From: '0.34.0', To: '0.35.0' } });
    assert.equal(existsSync(rollbackStateFile(s.rollback)), true);
});

test('a runtime the application carries that does not stop keeps the update from being installed, the kept copy as it was, and the person is told why', async (t) => {
    const stops: (() => Promise<StopOutcome>)[] = [
        async () => ({ kind: 'failed', exit: 1 }),
        async () => ({ kind: 'failed', exit: -1 }),
        async () => Promise.reject(new Error('planted-stop-error')),
        async () => ({ kind: 'unanswered', pid: 4242, runFile: 'C:/data/run/runtime.json' }),
        async () => ({ kind: 'unanswered', pid: null, runFile: 'C:/data/run/runtime.json' }),
        async () => ({ kind: 'unanswered', pid: null, runFile: 'C:/data/run/runtime.json', unreadable: true }),
    ];
    for (const stop of stops) {
        const s = scratch(t);
        // The copy an earlier update kept, which a refused install leaves where it is, with its record.
        const earlier = join(s.rollback, '0.33.0', 'Jason');
        mkdirSync(earlier, { recursive: true });
        writeFileSync(join(earlier, 'Jason.exe'), 'the application 0.33.0');
        writeRollback(s.rollback, { kept: { version: '0.33.0', directory: earlier }, rejected: [] });
        const kept = snapshot(s.rollback);
        const updater = fakeUpdater({ latest: '0.35.0' });
        const { updates, timers, order, told, log } = updaterOn(s, updater, { stop });
        updates.start();
        await timers.fire();
        await updates.download();
        const application = snapshot(s.application);
        assert.equal(await updates.install(), false);

        assert.deepEqual(order, ['stopRuntime'], 'nothing is closed');
        assert.equal(updater.calls.includes('quitAndInstall'), false, 'the installer is not started');
        assert.deepEqual(updates.current, { step: 'downloaded', version: '0.35.0' }, 'the person can try again');
        assert.deepEqual(snapshot(s.application), application);
        assert.deepEqual(snapshot(s.rollback), kept, 'the copy kept by the earlier update and its record are as they were');
        assert.deepEqual(readRollback(s.rollback), { pending: undefined, kept: { version: '0.33.0', directory: earlier }, rejected: [] });
        assert.equal(told.length, 1);
        assert.match(
            told[0]!,
            /^The update was not installed, because (the runtime this application carries did not stop \(jason runtime stop (exited 1|did not finish)\)\. Jason was left as it was\. Stop the runtime with jason runtime stop|a runtime may be running( that did not answer(: its run file names process 4242, which is running|, and its run file names no process)|: its run file, [^,]+, is there and could not be read)\. Jason was left as it was\. jason runtime status answers once the runtime does[^]*)\. Then choose Restart Jason and Its Runtime to Install again\.$/,
        );
        assert.doesNotMatch(told[0]!, /planted/);
        assert.deepEqual(log.lines.at(-1), { level: 'error', name: 'update_failed', fields: { Step: 'runtime', Failures: 1 } });
    }
    assert.match(runtimeRefusal({ kind: 'failed', exit: 1 }), /jason runtime stop exited 1/);
    assert.match(runtimeRefusal({ kind: 'failed', exit: -1 }), /jason runtime stop did not finish/);
    assert.equal(
        runtimeRefusal({ kind: 'unanswered', pid: 4242, runFile: 'C:/data/run/runtime.json' }),
        'The update was not installed, because a runtime may be running that did not answer: its run file names process 4242, which is running. Jason was left as it was. jason runtime status answers once the runtime does. jason runtime start says whether process 4242 is the runtime: it names it if it is, and starts one in place of the run file if it is not. If it is the runtime and never answers, end process 4242. If no Jason runtime is running, deleting C:/data/run/runtime.json lets this go on. Then choose Restart Jason and Its Runtime to Install again.',
    );
});

test('a copy that cannot be kept aside says so, how to start a stopped runtime again, and where the new version’s installer is', async (t) => {
    for (const stopped of [true, false]) {
        const s = scratch(t);
        // A file where the rollback folder goes: nothing can be kept there.
        mkdirSync(join(s.rollback, '..'), { recursive: true });
        writeFileSync(s.rollback, 'not a folder');
        const updater = fakeUpdater({ latest: '0.35.0' });
        const { updates, timers, order, told, log } = updaterOn(s, updater, { stop: async () => ({ kind: stopped ? 'stopped' : 'none' }) });
        updates.start();
        await timers.fire();
        await updates.download();
        assert.equal(await updates.install(), false);

        assert.deepEqual(order, ['stopRuntime'], 'the runtime is stopped first, and nothing is closed');
        assert.equal(updater.calls.includes('quitAndInstall'), false);
        assert.deepEqual(updates.current, { step: 'downloaded', version: '0.35.0' });
        assert.deepEqual(log.lines.slice(-2), [
            { level: 'warn', name: 'update_keep_failed', fields: { Code: 'ENOTDIR' } },
            { level: 'error', name: 'update_failed', fields: { Step: 'keep', Failures: 1 } },
        ]);
        assert.deepEqual(told, [keepRefusal(stopped, '0.35.0')]);
    }
    assert.equal(
        keepRefusal(false, '0.35.0'),
        'The update was not installed, because a copy of this version could not be kept aside to go back to. Jason was left as it was. You can install the new version yourself: download it for your system from https://get.jasonharness.ai/releases/tag/v0.35.0 and install it as you installed this one.',
    );
    assert.equal(
        keepRefusal(true, '0.35.0'),
        'The update was not installed, because a copy of this version could not be kept aside to go back to. The runtime this application carries was stopped for the update: jason runtime start starts it again. You can install the new version yourself: download it for your system from https://get.jasonharness.ai/releases/tag/v0.35.0 and install it as you installed this one.',
    );
});

test('a copy that fails part way leaves nothing of itself, and the record no longer names the earlier copy it replaced', async (t) => {
    for (const asFile of [false, true]) {
        for (const before of ['kept', 'pending', 'none'] as const) {
            const s = scratch(t);
            const application = asFile ? join(s.root, 'Jason.AppImage') : s.application;
            if (asFile) writeFileSync(application, 'the AppImage 0.34.0');
            const earlier = join(s.rollback, '0.33.0', 'Jason');
            mkdirSync(earlier, { recursive: true });
            writeFileSync(join(earlier, 'Jason.exe'), 'the application 0.33.0');
            if (before === 'kept') writeRollback(s.rollback, { kept: { version: '0.33.0', directory: earlier }, rejected: ['0.33.1'] });
            if (before === 'pending') writeRollback(s.rollback, { pending: { fromVersion: '0.33.0', toVersion: '0.34.0', directory: earlier }, rejected: ['0.33.1'] });
            const failing = async (_source: string, target: string): Promise<void> => {
                // Some of the copy is written before it fails, as a copy that runs out of room or meets a file it cannot read.
                mkdirSync(join(target, 'resources'), { recursive: true });
                writeFileSync(join(target, 'resources', 'app.asar'), 'half');
                throw Object.assign(new Error('planted copy failure'), { code: 'ENOSPC' });
            };
            await assert.rejects(keepPrevious(s.rollback, application, '0.34.0', '0.35.0', failing), { code: 'ENOSPC' });
            assert.deepEqual(readdirSync(s.rollback), before === 'none' ? [] : ['state.json'], `no part of either copy is left (${before})`);
            const record = readRollback(s.rollback);
            assert.equal(record.kept, undefined, `the record names no kept copy (${before})`);
            assert.equal(record.pending, undefined, `nor a pending one (${before})`);
            assert.deepEqual(record.rejected, before === 'none' ? [] : ['0.33.1'], 'what was refused stays refused');
            assert.equal(keptLine(record, '0.34.0', existsSync), null);
        }
    }
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

test('the starting page, when an update did not start, names the kept copy and the installed folder, and says what putting it back does', () => {
    const ruled = 'This puts back the previous application and the Jason it carried; the data directory stays as the newer version left it.';
    const programs = 'The system’s list of installed programs keeps showing the newer version’s number after that.';
    const windows = keptNotice('The runtime did not start.', { copy: 'C:/data/desktop/rollback/0.34.0/Jason' }, 'C:/Users/someone/AppData/Local/Programs/Jason', 'win32');
    assert.equal(
        windows,
        [
            'The runtime did not start.',
            'If this version is the cause, the version of Jason it was updated from is kept at C:/data/desktop/rollback/0.34.0/Jason.',
            'To go back, quit Jason and put that folder back in the place of the installed one, C:/Users/someone/AppData/Local/Programs/Jason, by hand: nothing does it for you.',
            ruled,
            programs,
        ].join(' '),
    );
    const mac = keptNotice('The runtime did not start.', { copy: '/data/desktop/rollback/0.34.0/Jason.app' }, '/Applications/Jason.app', 'darwin');
    assert.ok(mac.includes('in the place of the installed one, /Applications/Jason.app, by hand'));
    assert.ok(mac.endsWith(ruled), 'only Windows keeps a list of programs with a version number');
});

test('a start that failed says where the replaced version is only after an update, and a refused layout is said in its own words alone', () => {
    const said = 'The runtime did not start.';
    const copy = { copy: '/data/desktop/rollback/0.34.0/Jason.app' };
    assert.equal(failedStartNotice(said, false, null, '/Applications/Jason.app', 'darwin'), said, 'not an update');
    assert.equal(failedStartNotice(said, false, copy, '/Applications/Jason.app', 'darwin'), keptNotice(said, copy, '/Applications/Jason.app', 'darwin'));
    const refused = 'This data directory was set up by Jason 0.50.0, newer than this jason (0.41.0): update this jason, or use the one that set it up.';
    for (const platform of ['win32', 'darwin', 'linux'] as const) {
        assert.equal(failedStartNotice(refused, true, copy, '/Applications/Jason.app', platform), refused, `${platform}: the version it was updated from is not offered`);
        assert.equal(failedStartNotice(refused, true, { release: previousRelease('0.34.0'), file: debFile }, '/opt/Jason', platform), refused);
        assert.equal(failedStartNotice(refused, true, null, '/opt/Jason', platform), refused);
    }
});

test('a first start refused for the data directory’s layout refuses no version: the record stays, and the next start settles it', (t) => {
    const s = scratch(t);
    const copy = join(s.rollback, '0.34.0', 'Jason');
    const pending = { fromVersion: '0.34.0', toVersion: '0.35.0', directory: copy };
    const log = memoryLog();
    mkdirSync(copy, { recursive: true });
    writeRollback(s.rollback, { pending, rejected: ['0.33.0'] });
    const record = readFileSync(rollbackStateFile(s.rollback), 'utf8');

    assert.equal(settleFailedStart(s.rollback, '0.35.0', true, log), null, 'no going back is offered for a directory the version did not cause');
    assert.equal(readFileSync(rollbackStateFile(s.rollback), 'utf8'), record, 'the record is as it was, byte for byte');
    assert.equal(isRejected(readRollback(s.rollback), '0.35.0'), false);
    assert.deepEqual(log.lines, []);

    // Once the directory is put right, the start that works settles the same record.
    assert.equal(settleStart(s.rollback, '0.35.0', true, log), null);
    assert.deepEqual(readRollback(s.rollback), { pending: undefined, kept: { version: '0.34.0', directory: copy }, rejected: ['0.33.0'] });

    // Any other failure is settled as a failed start is.
    writeRollback(s.rollback, { pending, rejected: [] });
    assert.deepEqual(settleFailedStart(s.rollback, '0.35.0', false, log), pending);
    assert.equal(isRejected(readRollback(s.rollback), '0.35.0'), true);
});

test('the starting page’s failed start is wired to the layout refusal and the newer database’s: no version refused, and the CLI’s sentence alone', () => {
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8').split(/\s+/).join(' ');
    const from = main.indexOf("log.error('window_load_failed'");
    assert.ok(from >= 0);
    const failed = main.slice(from, main.indexOf('startInFlight = false;', from));
    assert.ok(failed.includes('const layoutRefused = error instanceof LayoutRefused || error instanceof DataNewerRefused;'));
    assert.ok(failed.includes('const kept = settleFailedStart(rollback, app.getVersion(), layoutRefused, log);'));
    assert.ok(failed.includes('const notice = failedStartNotice( said, layoutRefused,'));
    assert.ok(failed.includes('await showStarting(current, notice, true);'));
    assert.equal(failed.includes('settleStart('), false, 'a failed start is settled through settleFailedStart alone');
    assert.equal(failed.includes('keptNotice('), false, 'the notice is failedStartNotice’s alone');
});

test('the new version’s first start settles the record: a start that worked keeps the copy and says so, one that failed keeps it and refuses that version', (t) => {
    const s = scratch(t);
    const copy = join(s.rollback, '0.34.0', 'Jason');
    const pending = { fromVersion: '0.34.0', toVersion: '0.35.0', directory: copy };
    const log = memoryLog();

    mkdirSync(copy, { recursive: true });
    writeRollback(s.rollback, { pending, rejected: [] });
    assert.equal(settleStart(s.rollback, '0.34.0', true, log), null, 'a launch of another version settles nothing');
    assert.equal(existsSync(copy), true);
    assert.equal(settleStart(s.rollback, '0.35.0', true, log), null);
    assert.equal(existsSync(copy), true, 'the copy stays until the next update replaces it');
    assert.deepEqual(readRollback(s.rollback), { pending: undefined, kept: { version: '0.34.0', directory: copy }, rejected: [] });
    assert.equal(settleStart(s.rollback, '0.35.0', true, log), null, 'a later start settles nothing again');
    assert.equal(existsSync(copy), true);

    mkdirSync(copy, { recursive: true });
    writeRollback(s.rollback, { pending, rejected: [] });
    assert.deepEqual(settleStart(s.rollback, '0.35.0', false, log), pending, 'the person is told where the previous version is');
    assert.equal(existsSync(copy), true);
    assert.equal(isRejected(readRollback(s.rollback), '0.35.0'), true);
    assert.deepEqual(readRollback(s.rollback), { pending, rejected: ['0.35.0'] }, 'a failed start is settled as before');
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [
        ['update_settled', { Version: '0.35.0', Started: true }],
        ['update_settled', { Version: '0.35.0', Started: false }],
    ]);
});

test('while a copy of the previous version is kept, the Jason menu says where, under the update item', () => {
    const copy = '/data/desktop/rollback/0.34.0/Jason.app';
    const line = 'The previous version 0.34.0 is kept at /data/desktop/rollback/0.34.0/Jason.app; put it back by hand.';
    const there = (path: string) => path === copy;
    assert.equal(keptLine({ kept: { version: '0.34.0', directory: copy }, rejected: [] }, '0.35.0', there), line);
    // A first start that failed keeps the copy under its pending record.
    const pending = { fromVersion: '0.34.0', toVersion: '0.35.0', directory: copy };
    assert.equal(keptLine({ pending, rejected: ['0.35.0'] }, '0.35.0', there), line);
    assert.equal(keptLine({ kept: { version: '0.34.0', directory: copy }, rejected: [] }, '0.35.0', () => false), null, 'a copy no longer there is not named');
    assert.equal(keptLine({ kept: { version: '0.34.0', directory: copy }, rejected: [] }, '0.34.0', there), null, 'the copy of the version that runs is not a previous one');
    assert.equal(keptLine({ pending, rejected: [] }, '0.34.0', there), null, 'before the update has started, nothing is previous yet');
    assert.equal(keptLine({ rejected: [] }, '0.35.0', there), null);
});

test('the menu offers one step at a time, and in a build that is not installed none', () => {
    assert.deepEqual(menuItemOf({ step: 'idle' }, true, true), { label: 'Check for Updates…', enabled: true, act: 'check' });
    assert.deepEqual(menuItemOf({ step: 'checking' }, true, true), { label: 'Checking for Updates…', enabled: false, act: null });
    assert.deepEqual(menuItemOf({ step: 'offered', version: '0.35.0' }, true, true), { label: 'Download Jason 0.35.0', enabled: true, act: 'download' });
    assert.deepEqual(menuItemOf({ step: 'downloading', version: '0.35.0' }, true, true), { label: 'Downloading Jason 0.35.0…', enabled: false, act: null });
    assert.deepEqual(menuItemOf({ step: 'downloaded', version: '0.35.0' }, true, true), { label: 'Restart Jason and Its Runtime to Install 0.35.0', enabled: true, act: 'install' });
    assert.deepEqual(menuItemOf({ step: 'installing', version: '0.35.0' }, true, true), { label: 'Installing Jason 0.35.0…', enabled: false, act: null });
    assert.deepEqual(menuItemOf({ step: 'idle' }, false, true), { label: 'Updates Come to an Installed Build', enabled: false, act: null });
});

test('where the package updates itself, the item says so, and once the system installed a new version under the running window it offers the restart', () => {
    assert.deepEqual(menuItemOf({ step: 'idle' }, true, false), { label: 'Updates Come Through the Package', enabled: false, act: null });
    // Pressable: an item that names the restart and does nothing leaves the person to find Quit and the menu themselves. It
    // names the runtime, which it stops, as the install's item does.
    assert.deepEqual(menuItemOf({ step: 'idle' }, true, false, '0.49.2'), { label: 'Restart Jason and Its Runtime to Finish the Update to 0.49.2', enabled: true, act: 'restart' });
    assert.deepEqual(menuItemOf({ step: 'idle' }, true, true, '0.49.2'), { label: 'Check for Updates…', enabled: true, act: 'check' }, 'electron-updater’s own steps stand where it installs');
});

test('the deb’s restart stops the runtime the old build runs before Jason starts again, and leaves Jason as it is when that runtime does not stop', async () => {
    // Without the stop the new start finds the old build's runtime up and uses it, and the installation stays a version
    // behind its own package until a person restarts the runtime by hand.
    const run = async (outcome: StopOutcome) => {
        const calls: string[] = [];
        await restartForPackage({
            stopRuntime: async () => {
                calls.push('stop');
                return outcome;
            },
            relaunch: () => calls.push('relaunch'),
            refused: (why) => calls.push(`refused: ${why}`),
        });
        return calls;
    };
    assert.deepEqual(await run({ kind: 'stopped' }), ['stop', 'relaunch']);
    assert.deepEqual(await run({ kind: 'none' }), ['stop', 'relaunch']);
    assert.deepEqual(await run({ kind: 'another' }), ['stop', 'relaunch']);
    assert.deepEqual(await run({ kind: 'failed', exit: 1 }), [
        'stop',
        'refused: Jason was not restarted, because the runtime this application carries did not stop (jason runtime stop exited 1). Jason was left as it was. Stop the runtime with jason runtime stop. Then choose Restart Jason and Its Runtime to Finish the Update again.',
    ]);
    const unanswered = await run({ kind: 'unanswered', pid: 4242, runFile: '/data/run/runtime.json' });
    assert.equal(unanswered.length, 2);
    assert.match(unanswered[1]!, /^refused: Jason was not restarted, because .+\. Jason was left as it was\. .+ Then choose Restart Jason and Its Runtime to Finish the Update again\.$/);
});

test('the deb has moved when its version.json names another version than the window runs, and nothing else has', () => {
    // The one comparison the window's watch makes once a minute: wrong, the notice and the item never come, or come for
    // the version already running.
    assert.equal(packageMovedTo({ form: 'deb' }, '0.49.1', '0.49.2'), '0.49.2');
    assert.equal(packageMovedTo({ form: 'deb' }, '0.49.1', '0.49.1'), null, 'the version running');
    assert.equal(packageMovedTo({ form: 'deb' }, '0.49.1', null), null, 'a version.json that could not be read');
    assert.equal(packageMovedTo({ form: 'deb' }, null, '0.49.2'), null, 'a window that knows no version of its own');
    assert.equal(packageMovedTo({ form: 'appimage', file: '/home/a/Jason.AppImage' }, '0.49.1', '0.49.2'), null, 'an AppImage updates itself');
    assert.equal(packageMovedTo(null, '0.49.1', '0.49.2'), null, 'off Linux');
});

test('the deb’s restart runs once at a time: a second press while one is under way does nothing, and a refused one lets the next go', async () => {
    // A click on the notice and then the item, or two clicks, would stop the runtime twice and relaunch twice, the second
    // stop racing the first into a refusal while Jason in fact restarts.
    const calls: string[] = [];
    let outcome: StopOutcome = { kind: 'failed', exit: 1 };
    let release: () => void = () => undefined;
    const press = restartOnce({
        stopRuntime: () => new Promise<StopOutcome>((resolve) => {
            calls.push('stop');
            release = () => resolve(outcome);
        }),
        relaunch: () => calls.push('relaunch'),
        refused: () => calls.push('refused'),
    });
    const first = press();
    await press();
    release();
    await first;
    assert.deepEqual(calls, ['stop', 'refused']);
    outcome = { kind: 'stopped' };
    const again = press();
    release();
    await again;
    await press();
    assert.deepEqual(calls, ['stop', 'refused', 'stop', 'relaunch'], 'refused, the next press goes; relaunched, none does');
    // The key's question names what the restart ends, as the install's does.
    assert.equal(restartQuestion('0.49.2').message, 'Restart Jason and its runtime to finish the update to Jason 0.49.2?');
    assert.equal(restartQuestion('0.49.2').cancelId, 1);
});

test('the item’s key asks before it installs, which stops the runtime, and installs only on yes; a click on the item, and every other step, act at once', async (t) => {
    const asked: ReturnType<typeof installQuestion>[] = [];
    const answer = (button: number) => async (question: ReturnType<typeof installQuestion>) => {
        asked.push(question);
        return button;
    };
    const installs = (updater: { calls: string[] }) => updater.calls.filter((call) => call === 'quitAndInstall').length;
    const updater = fakeUpdater({ latest: '0.35.0' });
    const { updates, timers } = updaterOn(scratch(t), updater);
    updates.start();
    await timers.fire();
    // Checking and downloading act at once, by the key or by a click.
    await pressItem(updates, 'check', true, answer(0));
    await pressItem(updates, 'download', true, answer(0));
    assert.deepEqual(updater.calls, ['check', 'check', 'download']);
    assert.deepEqual(updates.current, { step: 'downloaded', version: '0.35.0' });
    await pressItem(updates, null, true, answer(0));
    assert.equal(asked.length, 0, 'nothing but the install asks');
    // The key at the install asks, naming the version and what installing it stops; its default does nothing.
    await pressItem(updates, 'install', true, answer(1));
    const [question] = asked;
    assert.equal(asked.length, 1);
    assert.ok(question !== undefined);
    assert.match(question.message, /Jason 0\.35\.0/);
    assert.match(question.detail, /runtime stops/);
    assert.deepEqual(question.buttons, ['Restart and Install', 'Not Now']);
    assert.equal(question.defaultId, 1);
    assert.equal(question.cancelId, 1);
    assert.equal(installs(updater), 0, 'Not Now installs nothing');
    assert.deepEqual(updates.current, { step: 'downloaded', version: '0.35.0' });
    await pressItem(updates, 'install', true, answer(0));
    assert.equal(asked.length, 2);
    assert.equal(installs(updater), 1, 'yes installs');
    // A press the menu showed before the step moved on asks nothing.
    await pressItem(updates, 'install', true, answer(0));
    assert.equal(asked.length, 2);
    assert.equal(installs(updater), 1);

    // A click on the item, whose label says so, installs at once.
    const clicked = fakeUpdater({ latest: '0.35.0' });
    const other = updaterOn(scratch(t), clicked);
    other.updates.start();
    await other.timers.fire();
    await pressItem(other.updates, 'download', false, answer(1));
    await pressItem(other.updates, 'install', false, answer(1));
    assert.equal(asked.length, 2, 'a click asks nothing');
    assert.equal(installs(clicked), 1);
});

test('the update item’s key is named only where the item can act', () => {
    assert.equal(updateKeyWords(true, true, 'linux'), 'Ctrl+Shift+D');
    assert.equal(updateKeyWords(true, true, 'darwin'), 'Cmd+Shift+D');
    // Updates Come to an Installed Build, and Updates Come Through the Package.
    assert.equal(updateKeyWords(false, true, 'linux'), null);
    assert.equal(updateKeyWords(true, false, 'linux'), null);
});

test('an updater electron-updater says is not active looks for nothing, and its item says updates come through the package', async (t) => {
    const s = scratch(t);
    const updater = fakeUpdater({ latest: '0.35.0', inactive: true });
    const { updates, timers } = updaterOn(s, updater);
    updates.start();
    assert.equal(updates.active, false);
    assert.deepEqual(timers.pending, [], 'no look is scheduled');
    await updates.check();
    assert.deepEqual(updater.calls, [], 'nor made when asked');
    assert.deepEqual(menuItemOf(updates.current, true, false), { label: 'Updates Come Through the Package', enabled: false, act: null });
    assert.deepEqual(menuItemOf({ step: 'idle' }, false, false), { label: 'Updates Come to an Installed Build', enabled: false, act: null });
    assert.equal(updaterOn(s, fakeUpdater()).updates.active, true);
});

test('an install electron-updater refuses without quitting brings the desk back, keeps the copy, and tells the person', async (t) => {
    for (const stopped of [true, false]) {
        const s = scratch(t);
        const updater = fakeUpdater({ latest: '0.35.0', installFails: true });
        const { updates, timers, order, told, log } = updaterOn(s, updater, { stop: async () => ({ kind: stopped ? 'stopped' : 'none' }) });
        updates.start();
        await timers.fire();
        await updates.download();
        assert.equal(await updates.install(), false);

        const copy = join(s.rollback, '0.34.0', 'Jason');
        assert.deepEqual(order, ['stopRuntime', 'beforeInstall', 'afterRefused'], 'what closed for the installer comes back');
        assert.deepEqual(updates.current, { step: 'downloaded', version: '0.35.0' }, 'the person can try again');
        assert.deepEqual(updater.listeners, [], 'the listener does not outlive the install');
        assert.deepEqual(told, [installRefusal(stopped, copy)]);
        assert.doesNotMatch(told[0]!, /planted/);
        assert.equal(existsSync(copy), true, 'the copy kept for the install stays');
        assert.deepEqual(log.lines.at(-1), { level: 'error', name: 'update_failed', fields: { Step: 'install', Failures: 1 } });
    }
    assert.equal(
        installRefusal(true, '/data/desktop/rollback/0.34.0/Jason.AppImage'),
        'The update was not installed: the installer stopped before it finished, and Jason stayed open. The runtime this application carries was stopped for the update: jason runtime start starts it again. A copy of this version is kept at /data/desktop/rollback/0.34.0/Jason.AppImage. Choose Restart Jason and Its Runtime to Install to try again.',
    );
    assert.equal(
        installRefusal(false, null),
        'The update was not installed: the installer stopped before it finished, and Jason stayed open. Choose Restart Jason and Its Runtime to Install to try again.',
    );
});

test('on Linux the AppImage file is what is kept aside, and the deb keeps nothing but the record', async (t) => {
    const s = scratch(t);
    const image = join(s.root, 'Applications', 'Jason.AppImage');
    mkdirSync(join(s.root, 'Applications'), { recursive: true });
    writeFileSync(image, 'the AppImage 0.34.0');
    const updater = fakeUpdater({ latest: '0.35.0' });
    const { updates, timers } = updaterOn(s, updater, { application: image });
    updates.start();
    await timers.fire();
    await updates.download();
    assert.equal(await updates.install(), true);
    const copy = join(s.rollback, '0.34.0', 'Jason.AppImage');
    assert.equal(readFileSync(copy, 'utf8'), 'the AppImage 0.34.0', 'the copy is the file itself');
    assert.deepEqual(readRollback(s.rollback), { pending: { fromVersion: '0.34.0', toVersion: '0.35.0', directory: copy }, rejected: [] });

    const deb = scratch(t);
    mkdirSync(join(deb.rollback, '0.33.0'), { recursive: true });
    writeFileSync(join(deb.rollback, '0.33.0', 'Jason.AppImage'), 'an earlier copy');
    const debUpdater = fakeUpdater({ latest: '0.35.0' });
    const run = updaterOn(deb, debUpdater, { application: null });
    run.updates.start();
    await run.timers.fire();
    await run.updates.download();
    assert.equal(await run.updates.install(), true);
    assert.deepEqual(readdirSync(deb.rollback), ['state.json'], 'no copy, and the earlier one is gone');
    assert.deepEqual(readRollback(deb.rollback), { pending: { fromVersion: '0.34.0', toVersion: '0.35.0', directory: null }, rejected: [] });
    assert.equal(debUpdater.calls.at(-1), 'quitAndInstall');
});

test('a record with no copy settles into nothing kept, and the menu names no copy', (t) => {
    const s = scratch(t);
    const pending = { fromVersion: '0.34.0', toVersion: '0.35.0', directory: null };
    assert.deepEqual(parseRollbackState(JSON.stringify({ rejected: [], pending })), { pending, rejected: [] });
    assert.deepEqual(afterSuccessfulStart({ pending, rejected: [] }), { rejected: [] });
    assert.equal(keptLine({ pending, rejected: ['0.35.0'] }, '0.35.0', () => true), null);
    writeRollback(s.rollback, { pending, rejected: [] });
    assert.deepEqual(settleStart(s.rollback, '0.35.0', false, memoryLog()), pending, 'a failed start is told the version it came from');
});

test('on Linux the starting page names the kept AppImage file, or the previous release’s deb and the apt line', () => {
    const ruled = 'This puts back the previous application and the Jason it carried; the data directory stays as the newer version left it.';
    // Through the product's own address, which every release request passes: its release page, by the release's tag.
    assert.equal(previousRelease('0.34.0'), 'https://get.jasonharness.ai/releases/tag/v0.34.0');
    assert.equal(debFile, 'jason-desktop-linux-x64.deb');
    assert.equal(
        keptNotice('The runtime did not start.', { copy: '/data/desktop/rollback/0.34.0/Jason.AppImage' }, '/home/someone/Applications/Jason.AppImage', 'linux'),
        [
            'The runtime did not start.',
            'If this version is the cause, the version of Jason it was updated from is kept at /data/desktop/rollback/0.34.0/Jason.AppImage.',
            'To go back, quit Jason and put that file back in the place of the installed one, /home/someone/Applications/Jason.AppImage, by hand: nothing does it for you.',
            ruled,
        ].join(' '),
    );
    assert.equal(
        keptNotice('The runtime did not start.', { release: previousRelease('0.34.0'), file: debFile }, '/opt/Jason', 'linux'),
        [
            'The runtime did not start.',
            'If this version is the cause, the version of Jason it was updated from is published at https://get.jasonharness.ai/releases/tag/v0.34.0.',
            'To go back, quit Jason, download jason-desktop-linux-x64.deb from that page, and in the folder it is in run sudo apt install ./jason-desktop-linux-x64.deb: nothing does it for you.',
            ruled,
        ].join(' '),
    );
});

test('the updater’s log lines, read back, carry versions, steps and counts, never an error’s text or a URL', async (t) => {
    const s = scratch(t);
    const logs = join(s.root, 'logs');
    let failing = true;
    const updater = fakeUpdater({ latest: '0.35.0', fails: () => failing });
    const timers = handTimers();
    const updates = new Updater({ updater, version: '0.34.0', application: s.application, rollback: s.rollback, log: fileLogger(logs), timers, stopRuntime: async () => ({ kind: 'none' }), refused: () => undefined, beforeInstall: async () => undefined, afterRefused: async () => undefined, changed: () => undefined });
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

// ---- A terminal's request (`update-request.ts`): `take` ------------------------------------------------------------

/** Runs `take` on an updater that never started its cadence, as a window-less start's, recording each step heard. */
async function taking(s: ReturnType<typeof scratch>, updater: ReturnType<typeof fakeUpdater>, request: { to: string; feed: string | null }, how: { install: boolean; late?: () => void }, extra: Parameters<typeof updaterOn>[2] = {}) {
    const on = updaterOn(s, updater, extra);
    on.updates.prepare();
    const steps: TakenStep[] = [];
    const outcome = await on.updates.take(request, how, (step) => void steps.push(step));
    return { ...on, steps, outcome, updater };
}

test('a request installs the version it names silently, with no start after it: the runtime stopped, the copy kept, then the installer', async (t) => {
    const s = scratch(t);
    const updater = fakeUpdater({ latest: '0.35.0' });
    const { outcome, steps, order, told, updates } = await taking(s, updater, { to: '0.35.0', feed: null }, { install: true });
    const copy = join(s.rollback, '0.34.0', 'Jason');
    assert.deepEqual(outcome, { state: 'installing', offered: '0.35.0', runtimeStopped: false, kept: copy });
    assert.deepEqual(steps, [{ step: 'checking' }, { step: 'downloading', offered: '0.35.0' }, { step: 'installing', runtimeStopped: false, kept: copy }]);
    // electron-updater's quitAndInstall(isSilent, isForceRunAfter): on Windows --updated /S and no --force-run; on macOS
    // autoRunAppAfterInstall off quits the application once Squirrel has the update, and starts nothing.
    assert.deepEqual(updater.calls, ['check', 'download', 'quitAndInstall(true, false)']);
    assert.equal(updater.autoRunAppAfterInstall, false);
    assert.deepEqual(order, ['stopRuntime', 'beforeInstall'], 'the runtime is stopped before anything closes');
    assert.deepEqual(readRollback(s.rollback), { pending: { fromVersion: '0.34.0', toVersion: '0.35.0', directory: copy }, rejected: [] });
    assert.deepEqual(told, [], 'nothing is shown to a person: the request file is the answer');
    assert.deepEqual(updates.current, { step: 'installing', version: '0.35.0' });
});

test('a request names its own feed for its look, and the releases are read again after a refusal', async (t) => {
    const s = scratch(t);
    const updater = fakeUpdater({ latest: '0.35.0' });
    const loop = 'http://127.0.0.1:8123/';
    const { outcome } = await taking(s, updater, { to: '0.36.0', feed: loop }, { install: true });
    assert.deepEqual(updater.feeds, [feed, loop, feed], 'the feed is the request one for the look, and the releases after it');
    assert.deepEqual(outcome, { state: 'refused', reason: 'version_mismatch', offered: '0.35.0', runtimeStopped: false, kept: null });
    assert.deepEqual(updater.calls, ['check'], 'nothing is downloaded when the feed offers another version');
});

test('a feed that offers no newer version, a request for the version that runs, and a version that failed here before', async (t) => {
    const none = await taking(scratch(t), fakeUpdater({ latest: '0.34.0' }), { to: '0.35.0', feed: null }, { install: true });
    assert.deepEqual(none.outcome, { state: 'refused', reason: 'version_mismatch', offered: '0.34.0', runtimeStopped: false, kept: null });
    const current = await taking(scratch(t), fakeUpdater({ latest: '0.35.0' }), { to: '0.34.0', feed: null }, { install: true });
    assert.deepEqual(current.outcome, { state: 'current' });
    assert.deepEqual(current.steps, []);
    const s = scratch(t);
    writeRollback(s.rollback, { rejected: ['0.35.0'] });
    const rejected = await taking(s, fakeUpdater({ latest: '0.35.0' }), { to: '0.35.0', feed: null }, { install: true });
    assert.deepEqual(rejected.outcome, { state: 'refused', reason: 'version_rejected', offered: null, runtimeStopped: false, kept: null });
    const inactive = await taking(scratch(t), fakeUpdater({ latest: '0.35.0', inactive: true }), { to: '0.35.0', feed: null }, { install: true });
    assert.deepEqual(inactive.outcome, { state: 'refused', reason: 'updater_inactive', offered: null, runtimeStopped: false, kept: null });
});

test('a look or a download that fails refuses the request, logged by its step and never by its text', async (t) => {
    const look = await taking(scratch(t), fakeUpdater({ latest: '0.35.0', fails: () => true }), { to: '0.35.0', feed: null }, { install: true });
    assert.deepEqual(look.outcome, { state: 'refused', reason: 'check_failed', offered: null, runtimeStopped: false, kept: null });
    assert.deepEqual(look.updates.current, { step: 'idle' });
    let calls = 0;
    const updater = fakeUpdater({ latest: '0.35.0', fails: () => (calls += 1) > 1 });
    const download = await taking(scratch(t), updater, { to: '0.35.0', feed: null }, { install: true });
    assert.deepEqual(download.outcome, { state: 'refused', reason: 'download_failed', offered: '0.35.0', runtimeStopped: false, kept: null });
    assert.deepEqual(download.updates.current, { step: 'offered', version: '0.35.0' });
    for (const line of [...look.log.lines, ...download.log.lines]) assert.doesNotMatch(JSON.stringify(line), /planted|https?:/);
});

test('a window that may not close stops at the download: the update waits under its install item, and nothing is stopped or kept', async (t) => {
    const s = scratch(t);
    const updater = fakeUpdater({ latest: '0.35.0' });
    const { outcome, order, updates } = await taking(s, updater, { to: '0.35.0', feed: null }, { install: false });
    assert.deepEqual(outcome, { state: 'refused', reason: 'window_open', offered: '0.35.0', runtimeStopped: false, kept: null });
    assert.deepEqual(order, [], 'the runtime is not stopped and nothing closes');
    assert.equal(existsSync(s.rollback), false, 'nothing is kept aside');
    assert.deepEqual(updates.current, { step: 'downloaded', version: '0.35.0' });
    assert.deepEqual(menuItemOf(updates.current, true, true), { label: 'Restart Jason and Its Runtime to Install 0.35.0', enabled: true, act: 'install' });
    // The person's click then installs as it always did: no silent switch, the application started again after it.
    assert.equal(await updates.install(), true);
    assert.equal(updater.calls.at(-1), 'quitAndInstall');
});

test('a request for the version already downloaded takes the download as it is', async (t) => {
    const updater = fakeUpdater({ latest: '0.35.0' });
    const again = updaterOn(scratch(t), updater);
    again.updates.start();
    await again.timers.fire();
    await again.updates.download();
    const outcome = await again.updates.take({ to: '0.35.0', feed: null }, { install: true }, () => undefined);
    assert.equal(outcome.state, 'installing');
    assert.deepEqual(updater.calls, ['check', 'download', 'quitAndInstall(true, false)']);
});

test('a window cadence is held while a request is taken and looks again after a refusal; a request meets a step under way as busy', async (t) => {
    const s = scratch(t);
    const updater = fakeUpdater({ latest: '0.35.0' });
    const { updates, timers } = updaterOn(s, updater);
    updates.start();
    assert.deepEqual(timers.pending.map((entry) => entry.ms), [firstCheckMs]);
    const outcome = await updates.take({ to: '0.36.0', feed: null }, { install: true }, () => undefined);
    assert.equal(outcome.state, 'refused');
    assert.deepEqual(timers.pending.map((entry) => entry.ms), [updateCheckInterval], 'one look scheduled again, none left from before');
    let release!: () => void;
    const slow = fakeUpdater({ latest: '0.35.0' });
    slow.downloadUpdate = () => new Promise<string[]>((resolve) => (release = () => resolve([])));
    const busy = updaterOn(scratch(t), slow);
    busy.updates.prepare();
    const first = busy.updates.take({ to: '0.35.0', feed: null }, { install: false }, () => undefined);
    await new Promise((resolve) => setImmediate(resolve));
    assert.deepEqual(await busy.updates.take({ to: '0.35.0', feed: null }, { install: true }, () => undefined), { state: 'refused', reason: 'busy', offered: null, runtimeStopped: false, kept: null });
    release();
    assert.equal((await first).state, 'refused');
});

test('a runtime that does not stop, a copy that cannot be kept, and an installer that stops refuse a request with what was done', async (t) => {
    const runtime = await taking(scratch(t), fakeUpdater({ latest: '0.35.0' }), { to: '0.35.0', feed: null }, { install: true }, { stop: async () => ({ kind: 'unanswered', pid: 4242, runFile: '/data/run/runtime.json' }) });
    assert.deepEqual(runtime.outcome, { state: 'refused', reason: 'runtime_not_stopped', offered: '0.35.0', runtimeStopped: false, kept: null, runtime: { kind: 'unanswered', pid: 4242, runFile: '/data/run/runtime.json' } });
    assert.equal(runtime.updater.calls.some((call) => call.startsWith('quitAndInstall')), false);

    const s = scratch(t);
    mkdirSync(join(s.rollback, '..'), { recursive: true });
    writeFileSync(s.rollback, 'a file where the rollback folder goes');
    const keep = await taking(s, fakeUpdater({ latest: '0.35.0' }), { to: '0.35.0', feed: null }, { install: true }, { stop: async () => ({ kind: 'stopped' }) });
    assert.deepEqual(keep.outcome, { state: 'refused', reason: 'keep_failed', offered: '0.35.0', runtimeStopped: true, kept: null });

    const install = await taking(scratch(t), fakeUpdater({ latest: '0.35.0', installFails: true }), { to: '0.35.0', feed: null }, { install: true }, { stop: async () => ({ kind: 'stopped' }) });
    assert.equal(install.outcome.state === 'refused' && install.outcome.reason, 'install_failed');
    assert.equal(install.outcome.state === 'refused' && install.outcome.runtimeStopped, true);
    assert.deepEqual(install.order, ['stopRuntime', 'beforeInstall', 'afterRefused'], 'what closed for the installer comes back');
    assert.equal(install.updater.autoRunAppAfterInstall, true, 'a later click starts the application after its install, as it always did');
    assert.deepEqual(install.updater.listeners, []);
});

test('a throw out of a request puts the releases’ feed back, leaves no step under way and looks again', async (t) => {
    const updater = fakeUpdater({ latest: '0.35.0' });
    const timers = handTimers();
    const throwing = new Updater({
        updater,
        version: '0.34.0',
        application: null,
        rollback: scratch(t).rollback,
        log: memoryLog(),
        timers,
        stopRuntime: async () => ({ kind: 'stopped' }),
        refused: () => undefined,
        beforeInstall: async () => {
            throw new Error('planted-before-install');
        },
        afterRefused: async () => undefined,
        changed: () => undefined,
    });
    throwing.start();
    assert.deepEqual(timers.pending.map((entry) => entry.ms), [firstCheckMs]);
    const loop = 'http://127.0.0.1:8123/';
    await assert.rejects(throwing.take({ to: '0.35.0', feed: loop }, { install: true }, () => undefined), /planted-before-install/);
    assert.equal(updater.feeds.at(-1), feed, 'the releases’ feed is put back');
    assert.deepEqual(throwing.current, { step: 'downloaded', version: '0.35.0' }, 'not installing: the download waits');
    assert.deepEqual(timers.pending.map((entry) => entry.ms), [updateCheckInterval], 'the cadence looks again');
    // A request that ends in the installer puts the releases' feed back too.
    const handed = fakeUpdater({ latest: '0.35.0' });
    const ok = await taking(scratch(t), handed, { to: '0.35.0', feed: loop }, { install: true });
    assert.equal(ok.outcome.state, 'installing');
    assert.deepEqual(handed.feeds, [feed, loop, feed]);
});

test('an install that fails after quitAndInstall returned, as Squirrel says it on macOS, is heard once', async (t) => {
    const updater = fakeUpdater({ latest: '0.35.0' });
    let heard = 0;
    const { outcome, log } = await taking(scratch(t), updater, { to: '0.35.0', feed: null }, { install: true, late: () => void (heard += 1) });
    assert.equal(outcome.state, 'installing');
    assert.equal(updater.listeners.length, 1, 'one listener waits for an error after the install began');
    for (const listener of [...updater.listeners]) listener(new Error('planted-squirrel-error'));
    for (const listener of [...updater.listeners]) listener(new Error('planted-squirrel-error'));
    assert.equal(heard, 1);
    assert.deepEqual(updater.listeners, []);
    assert.deepEqual(log.lines.at(-1), { level: 'error', name: 'update_failed', fields: { Step: 'install', Failures: 1 } });
});

test('the terminal settle records the update that installed this version as kept, once, and leaves any other record as it is', (t) => {
    const s = scratch(t);
    const log = memoryLog();
    const copy = join(s.rollback, '0.34.0', 'Jason');
    writeRollback(s.rollback, { pending: { fromVersion: '0.34.0', toVersion: '0.35.0', directory: copy }, rejected: ['0.33.9'] });
    assert.equal(settleByRequest(s.rollback, '0.34.0', log), false, 'the version an update came from settles nothing');
    assert.equal(settleByRequest(s.rollback, '0.35.0', log), true);
    assert.deepEqual(readRollback(s.rollback), { pending: undefined, kept: { version: '0.34.0', directory: copy }, rejected: ['0.33.9'] });
    assert.equal(settleByRequest(s.rollback, '0.35.0', log), false, 'a second settle changes nothing');
    assert.equal(settleStart(s.rollback, '0.35.0', true, log), null, 'nor does the first window start after it');
    assert.deepEqual(log.lines, [{ level: 'info', name: 'update_settled_by_request', fields: { Version: '0.35.0' } }]);
    assert.equal(keptLine(readRollback(s.rollback), '0.35.0', () => true), `The previous version 0.34.0 is kept at ${copy}; put it back by hand.`);
    // A record with no copy (the deb's) settles into nothing kept.
    writeRollback(s.rollback, { pending: { fromVersion: '0.35.0', toVersion: '0.36.0', directory: null }, rejected: [] });
    assert.equal(settleByRequest(s.rollback, '0.36.0', log), true);
    assert.deepEqual(readRollback(s.rollback), { pending: undefined, rejected: [] });
});

test('under the close switch, the menu’s install refuses while a terminal’s request that installs is under way; under stop, the request never installs and the menu is the person’s', async (t) => {
    // A window that already holds the download, and a request for it that installs: its runtime stop held open.
    const updater = fakeUpdater({ latest: '0.35.0' });
    let release!: () => void;
    const on = updaterOn(scratch(t), updater, { stop: () => new Promise<StopOutcome>((resolve) => (release = () => resolve({ kind: 'none' }))) });
    on.updates.start();
    await on.timers.fire();
    await on.updates.download();
    const request = on.updates.take({ to: '0.35.0', feed: null }, { install: true }, () => undefined);
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(await on.updates.install(), false);
    assert.deepEqual(on.told, [requestUnderWayRefusal]);
    release();
    assert.equal((await request).state, 'installing');
    assert.deepEqual(updater.calls.filter((call) => call.startsWith('quitAndInstall')), ['quitAndInstall(true, false)'], 'the request’s install alone');
    // A request that does not install (a window under `stop`) leaves the menu's install as it was once it has ended.
    const stopped = updaterOn(scratch(t), fakeUpdater({ latest: '0.35.0' }));
    stopped.updates.prepare();
    assert.equal((await stopped.updates.take({ to: '0.35.0', feed: null }, { install: false }, () => undefined)).state, 'refused');
    assert.equal(await stopped.updates.install(), true);
    assert.deepEqual(stopped.told, []);
    // A window's request installs only under the close switch: the switch is what makes `how.install` true there.
    const requests = readFileSync(new URL('../src/update-request.ts', import.meta.url), 'utf8');
    assert.ok(requests.includes("install: !window || deps.windowOpen === 'close',"));
});
