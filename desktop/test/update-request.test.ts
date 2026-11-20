// A terminal's update request (`update-request.ts`): the arguments `jason update apply` starts the application with, the
// loopback feed, the request's file and its steps, the switch a window obeys, and how the main module starts it.

import assert from 'node:assert/strict';
import { closeSync, existsSync, mkdirSync, mkdtempSync, openSync, readdirSync, readFileSync, renameSync, rmSync, statSync, utimesSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { demoMenu } from '../src/mode.ts';
import {
    applyExit,
    applyExits,
    asksForWindow,
    awaitGoFlag,
    busyRecord,
    createRequestFile,
    goFile,
    goWaitMs,
    handedData,
    headlessLimitMs,
    headlessLimitOf,
    isRequestId,
    isVersion,
    loopbackFeed,
    openAfterRequest,
    readRequestFile,
    refusalMessage,
    renamePauseMs,
    renameTries,
    requestFile,
    requestOf,
    requestRefusals,
    requestSupport,
    systemShell,
    takeRequest,
    UpdateRequests,
    updateStart,
    updatingWords,
    usageRecord,
    waitForGo,
    windowOpenUpdate,
    writeRequestFile,
    type GoWait,
    type RequestFileIo,
    type RequestRecord,
    type TakeDeps,
    type TakeDepsCommon,
    type UpdateRequest,
} from '../src/update-request.ts';
import { Updater, type TakenHow, type TakenOutcome, type TakenRequest, type TakenStep } from '../src/updates.ts';
import { memoryLog } from './harness.ts';

function scratch(t: { after(fn: () => void): void }): string {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-request-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    return join(root, 'data');
}

const request: UpdateRequest = { id: 'req_01JA', to: '0.49.1', feed: null };

// ---- The arguments ------------------------------------------------------------------------------------------------

test('the start reads --update-apply with the version, the request and a loopback feed, and --update-settle alone', () => {
    assert.equal(updateStart(['Jason.exe']), null);
    assert.equal(updateStart(['Jason.exe', '--opened-at-login']), null);
    assert.deepEqual(updateStart(['Jason.exe', '--update-apply', '--to', '0.49.1', '--request', 'req_01JA']), { kind: 'apply', request });
    assert.deepEqual(updateStart(['Jason', '--update-apply', '--request', 'a-b_c', '--to', '0.49.1-ci.7', '--feed', 'http://127.0.0.1:8123/feed']), {
        kind: 'apply',
        request: { id: 'a-b_c', to: '0.49.1-ci.7', feed: 'http://127.0.0.1:8123/feed' },
    });
    assert.deepEqual(updateStart(['Jason', '--update-settle']), { kind: 'settle' });
});

test('arguments it cannot read are a usage refusal, with the id where the id was read', () => {
    const usage = (argv: string[]) => {
        const start = updateStart(['Jason', ...argv]);
        assert.equal(start?.kind, 'usage', argv.join(' '));
        return start?.kind === 'usage' ? start : null;
    };
    assert.equal(usage(['--update-apply', '--to', '0.49.1'])?.id, null, 'no request');
    assert.equal(usage(['--update-apply', '--to', '0.49.1', '--request', '../../evil'])?.id, null, 'a request id is never a path');
    assert.equal(usage(['--update-apply', '--to', 'latest', '--request', 'r1'])?.id, 'r1');
    assert.equal(usage(['--update-apply', '--request', 'r1'])?.id, 'r1', 'no version');
    assert.equal(usage(['--update-apply', '--to', '--request', 'r1'])?.id, 'r1', 'a flag is no value');
    assert.equal(usage(['--update-apply', '--to', '0.49.1', '--to', '0.49.2', '--request', 'r1'])?.id, 'r1', 'a flag given twice');
    assert.equal(usage(['--update-apply', '--update-apply', '--to', '0.49.1', '--request', 'r1'])?.id, 'r1');
    for (const feed of ['https://127.0.0.1/', 'http://example.com/', 'http://127.0.0.1.example.com/', 'http://user:pw@127.0.0.1/', 'http://127.0.0.1/?x=1', 'nonsense']) {
        assert.match(usage(['--update-apply', '--to', '0.49.1', '--request', 'r1', '--feed', feed])?.why ?? '', /loopback/, feed);
    }
    usage(['--update-apply', '--update-settle', '--to', '0.49.1', '--request', 'r1']);
    usage(['--update-settle', '--request', 'r1']);
});

test('the feed is http on the loopback address alone, as the runtime’s own feed allows besides https', () => {
    for (const ok of ['http://127.0.0.1:8123/', 'http://127.5.6.7/x/', 'http://localhost:9000', 'http://[::1]:8000/', 'http://LOCALHOST/']) assert.notEqual(loopbackFeed(ok), null, ok);
    for (const refused of ['https://127.0.0.1/', 'http://0.0.0.0/', 'http://10.0.0.1/', 'http://localhost.example/', 'http://127.0.0.1#x', 'file:///tmp/feed', 'ftp://127.0.0.1/', '']) {
        assert.equal(loopbackFeed(refused), null, refused);
    }
    assert.equal(isRequestId('a'.repeat(64)), true);
    assert.equal(isRequestId('a'.repeat(65)), false);
    for (const bad of ['', '-x', 'a/b', 'a\\b', 'a.b', 'a b']) assert.equal(isRequestId(bad), false, bad);
    for (const version of ['0.49.1', '1.0.0-rc.1', '0.49.0-ci.12']) assert.equal(isVersion(version), true, version);
    for (const version of ['v0.49.1', '0.49', '01.2.3', '0.49.1+build', '0.49.1-']) assert.equal(isVersion(version), false, version);
});

test('what a start hands the running application is read again there and never trusted', () => {
    assert.deepEqual(requestOf(handedData(request)), request);
    const fed = { ...request, feed: 'http://127.0.0.1:8123/' };
    assert.deepEqual(requestOf(handedData(fed)), fed);
    for (const data of [null, undefined, 'x', {}, { link: 'jason-desktop://needs' }, { update: null }, { update: { ...request, id: '../x' } }, { update: { ...request, to: 'latest' } }, { update: { ...request, feed: 'https://example.com/' } }, { update: { ...request, feed: 7 } }]) {
        assert.equal(requestOf(data), null, JSON.stringify(data));
    }
});

test('a request is taken only by an installed application on Windows or macOS', () => {
    assert.equal(requestSupport(true, 'win32'), null);
    assert.equal(requestSupport(true, 'darwin'), null);
    assert.equal(requestSupport(true, 'linux'), 'unsupported_platform');
    assert.equal(requestSupport(false, 'win32'), 'not_installed');
});

// ---- The file -----------------------------------------------------------------------------------------------------

test('the request’s file is <data>/update/desktop-<id>.json, written whole, every member there', (t) => {
    const data = scratch(t);
    const file = requestFile(data, 'req_01JA');
    assert.equal(file, join(data, 'update', 'desktop-req_01JA.json'));
    const record = usageRecord('req_01JA', 'its arguments are not the ones it takes', '0.49.0', new Date('2026-11-17T10:00:00Z'));
    writeRequestFile(file, record);
    assert.deepEqual(readdirSync(join(data, 'update')), ['desktop-req_01JA.json'], 'no temporary file is left');
    assert.deepEqual(readRequestFile(file), record);
    assert.deepEqual(Object.keys(record).sort(), ['from', 'kept', 'message', 'offered', 'reason', 'request', 'runtime_stopped', 'state', 'to', 'updated_at', 'window']);
    assert.equal(readRequestFile(join(data, 'none.json')), null);
    const busy = busyRecord(request, '0.49.0', true);
    assert.equal(busy.state, 'refused');
    assert.equal(busy.reason, 'busy');
    assert.equal(busy.window, true);
});

test('the request’s file is made only where none is there, and a file already there is never written over', (t) => {
    const data = scratch(t);
    const file = requestFile(data, 'req_01JA');
    const first = usageRecord('req_01JA', 'its arguments are not the ones it takes', '0.49.0', new Date('2026-11-17T10:00:00Z'));
    assert.equal(createRequestFile(file, first), true);
    assert.deepEqual(readRequestFile(file), first);
    assert.deepEqual(readdirSync(join(data, 'update')), ['desktop-req_01JA.json'], 'no temporary file is left');
    // A busy or usage refusal for the same id, made by a second start, finds the file of the request under way.
    assert.equal(createRequestFile(file, busyRecord(request, '0.49.0', true)), false);
    assert.deepEqual(readRequestFile(file), first, 'the request under way keeps its file');
});

test('a request whose file is already there is not taken: nothing is written, no updater is made, and the start exits 2', async (t) => {
    const data = scratch(t);
    const file = requestFile(data, request.id);
    mkdirSync(join(data, 'update'), { recursive: true });
    writeFileSync(file, 'a file the verb did not make');
    const u = planted({ state: 'installing', offered: '0.49.1', runtimeStopped: true, kept: null });
    const { all, log, written } = deps(data, u.updater);
    const end = await takeRequest(request, all);
    assert.equal(end, null);
    assert.equal(applyExit(end), applyExits.usage);
    assert.equal(u.made(), 0, 'no updater is made');
    assert.equal(readFileSync(file, 'utf8'), 'a file the verb did not make');
    assert.equal(written.length, 1, 'only the exclusive create was tried');
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [['update_request_file_there', { To: '0.49.1' }]]);
});

test('a request whose file cannot be made is refused as failed: nothing is taken or written, no updater is made, and the start exits 1', async (t) => {
    const data = scratch(t);
    mkdirSync(data, { recursive: true });
    writeFileSync(join(data, 'update'), 'a file where the folder goes');
    const u = planted({ state: 'installing', offered: '0.49.1', runtimeStopped: true, kept: null });
    const { all, log, written } = deps(data, u.updater);
    const end = await takeRequest(request, all);
    assert.ok(end !== null);
    assert.equal(end.state, 'refused');
    assert.equal(end.reason, 'failed');
    assert.equal(applyExit(end), applyExits.failed);
    assert.equal(u.made(), 0, 'no updater is made');
    assert.equal(written.length, 1, 'only the exclusive create was tried');
    assert.equal(readFileSync(join(data, 'update'), 'utf8'), 'a file where the folder goes');
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [
        ['update_request_unwritten', { State: 'checking' }],
        ['update_request_ended', { To: '0.49.1', State: 'refused', Code: 'failed' }],
    ]);
});

test('every refusal has a sentence for the person, naming the versions where it knows them', () => {
    for (const reason of requestRefusals) {
        const message = refusalMessage(reason, { to: '0.49.1', from: '0.49.0', offered: '0.49.2', runtimeStopped: true, kept: '/data/desktop/rollback/0.49.0/Jason.app', menu: true });
        assert.ok(message.length > 20, reason);
        assert.doesNotMatch(message, /undefined|null|\[object/, reason);
    }
    assert.match(refusalMessage('version_mismatch', { to: '0.49.1', from: '0.49.0', offered: '0.49.2', runtimeStopped: false, kept: null, menu: false }), /offers Jason 0\.49\.2, not the Jason 0\.49\.1/);
    assert.match(refusalMessage('version_mismatch', { to: '0.49.1', from: '0.49.0', offered: null, runtimeStopped: false, kept: null, menu: false }), /offers no Jason newer than 0\.49\.0/);
    assert.match(refusalMessage('window_open', { to: '0.49.1', from: '0.49.0', offered: null, runtimeStopped: false, kept: null, menu: true }), /Restart Jason and Its Runtime to Install 0\.49\.1/);
    assert.doesNotMatch(refusalMessage('window_open', { to: '0.49.1', from: '0.49.0', offered: null, runtimeStopped: false, kept: null, menu: false }), /Restart Jason/, 'the Demo has no install item');
    assert.match(refusalMessage('runtime_not_stopped', { to: '0.49.1', from: '0.49.0', offered: null, runtimeStopped: false, kept: null, menu: false, runtime: { kind: 'unanswered', pid: 4242, runFile: '/d/run/runtime.json' } }), /process 4242/);
    assert.match(refusalMessage('keep_failed', { to: '0.49.1', from: '0.49.0', offered: null, runtimeStopped: true, kept: null, menu: false }), /jason runtime start starts it again/);
});

test('the page’s table of reasons is the reasons a request is refused for, each once', () => {
    const page = readFileSync(new URL('../../docs/release-and-update.md', import.meta.url), 'utf8');
    const head = '| Reason | What happened |';
    assert.equal(page.split(head).length - 1, 1);
    const rows = page.slice(page.indexOf(head)).split('\n').slice(2);
    const codes: string[] = [];
    for (const row of rows) {
        if (!row.startsWith('|')) break;
        const code = /^\| `([a-z_]+)` \|/.exec(row);
        assert.ok(code !== null, row);
        codes.push(code[1]!);
    }
    assert.equal(new Set(codes).size, codes.length, 'each reason once');
    assert.deepEqual([...codes].sort(), [...requestRefusals].sort());
});

// ---- Taking one ---------------------------------------------------------------------------------------------------

/** An updater whose `take` answers as planted, hearing the steps it is given and recording how it was asked. */
function planted(outcome: TakenOutcome, steps: TakenStep[] = [], extra: { throws?: boolean } = {}) {
    const asked: { request: TakenRequest; how: TakenHow }[] = [];
    let made = 0;
    return {
        asked,
        made: () => made,
        updater: () => {
            made += 1;
            return {
                async take(taken: TakenRequest, how: TakenHow, progress: (step: TakenStep) => void): Promise<TakenOutcome> {
                    asked.push({ request: taken, how });
                    if (extra.throws) throw new Error('planted-error');
                    for (const step of steps) progress(step);
                    return outcome;
                },
            };
        },
    };
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
    };
}

/** What every taker is given, the test's own writes and clock among them. */
function common(data: string, extra: Partial<TakeDepsCommon>) {
    const log = memoryLog();
    const written: RequestRecord[] = [];
    const base: TakeDepsCommon = {
        data,
        from: '0.49.0',
        unsupported: null,
        windowOpen: 'stop',
        log,
        write: (file, record) => {
            written.push(record);
            writeRequestFile(file, record);
        },
        create: (file, record) => {
            written.push(record);
            return createRequestFile(file, record);
        },
        now: () => new Date('2026-11-17T10:00:00Z'),
        ...extra,
    };
    return { base, log, written };
}

/** A start of its own, or a Live window where `where` says so, taking the request with `updater`. */
function deps(data: string, updater: () => Pick<Updater, 'take'>, extra: Partial<TakeDepsCommon> & { where?: Exclude<TakeDeps['where'], 'demo'> } = {}) {
    const { where = 'headless', ...rest } = extra;
    const { base, log, written } = common(data, rest);
    const all: TakeDeps = { ...base, where, updater };
    return { all, log, written };
}

/** A window that shows the Demo, which holds no updater. */
function demoDeps(data: string, extra: Partial<TakeDepsCommon> = {}) {
    const { base, log, written } = common(data, extra);
    const all: TakeDeps = { ...base, where: 'demo' };
    return { all, log, written };
}

/** `takeRequest` where the request's file was not there before: it always ends with a record. */
async function taken(request: UpdateRequest, all: TakeDeps): Promise<RequestRecord> {
    const end = await takeRequest(request, all);
    assert.ok(end !== null, 'the request was taken');
    return end;
}

/** electron-updater's autoUpdater as far as `Updater` uses it, recording every feed it is given and every call. */
function recordingUpdater(version: string) {
    const calls: string[] = [];
    const fields: Record<string, unknown> = {};
    const electron = {
        ...fields,
        autoDownload: false, autoInstallOnAppQuit: false, allowPrerelease: false, allowDowngrade: false, logger: null as unknown, autoRunAppAfterInstall: true,
        isUpdaterActive: () => true,
        on: () => electron,
        removeListener: () => electron,
        setFeedURL: (url: unknown) => void calls.push(`setFeedURL(${String(url)})`),
        async checkForUpdates() {
            calls.push('check');
            return { isUpdateAvailable: true, updateInfo: { version } };
        },
        async downloadUpdate() {
            calls.push('download');
            return [];
        },
        quitAndInstall: (...args: unknown[]) => void calls.push(`quitAndInstall(${args.join(', ')})`),
    };
    return { electron, calls };
}

test('a window-less start writes each step before it is taken and ends at installing, the copy and the stop named; it exits as the installer quits it', async (t) => {
    const data = scratch(t);
    const kept = '/data/desktop/rollback/0.49.0/Jason.app';
    const u = planted({ state: 'installing', offered: '0.49.1', runtimeStopped: true, kept }, [{ step: 'checking' }, { step: 'downloading', offered: '0.49.1' }, { step: 'installing', runtimeStopped: true, kept }]);
    const { all, written, log } = deps(data, u.updater);
    const end = await taken(request, all);
    assert.deepEqual(written.map((record) => record.state), ['checking', 'checking', 'downloading', 'installing']);
    assert.deepEqual(end, { request: 'req_01JA', state: 'installing', from: '0.49.0', to: '0.49.1', reason: null, message: null, offered: '0.49.1', window: false, runtime_stopped: true, kept, updated_at: '2026-11-17T10:00:00.000Z' });
    assert.deepEqual(readRequestFile(requestFile(data, 'req_01JA')), end);
    assert.deepEqual(u.asked[0]?.request, { to: '0.49.1', feed: null });
    assert.equal(u.asked[0]?.how.install, true, 'a window-less start installs');
    assert.equal(applyExit(end), null, 'no exit of its own: electron-updater quits it');
    // The log says the version, where and how it ended, never the request's id or its feed.
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [
        ['update_request_taken', { To: '0.49.1', Where: 'headless' }],
        ['update_request_ended', { To: '0.49.1', State: 'installing', Code: 'none' }],
    ]);
});

test('the switch: a window that takes a request stops at the download by default, and installs only where it is set to close', async (t) => {
    assert.equal(windowOpenUpdate, 'stop', 'whether an open window may close for a terminal is not decided: it stops');
    for (const windowOpen of ['stop', 'close'] as const) {
        const data = scratch(t);
        const u = planted({ state: 'refused', reason: 'window_open', offered: '0.49.1', runtimeStopped: false, kept: null });
        const { all } = deps(data, u.updater, { where: { window: { menu: true } }, windowOpen });
        const end = await taken(request, all);
        assert.equal(u.asked[0]?.how.install, windowOpen === 'close', windowOpen);
        assert.equal(end.window, true);
        assert.equal(end.reason, 'window_open');
        assert.match(end.message ?? '', /Restart Jason and Its Runtime to Install 0\.49\.1/);
    }
    // And the real updater with each setting: stopped at the download, or installed silently.
    for (const windowOpen of ['stop', 'close'] as const) {
        const data = scratch(t);
        const calls: string[] = [];
        const fields: Record<string, unknown> = {};
        const electron = {
            ...fields,
            autoDownload: false, autoInstallOnAppQuit: false, allowPrerelease: false, allowDowngrade: false, logger: null as unknown, autoRunAppAfterInstall: true,
            isUpdaterActive: () => true,
            on: () => electron,
            removeListener: () => electron,
            setFeedURL: () => undefined,
            async checkForUpdates() {
                calls.push('check');
                return { isUpdateAvailable: true, updateInfo: { version: '0.49.1' } };
            },
            async downloadUpdate() {
                calls.push('download');
                return [];
            },
            quitAndInstall: (...args: unknown[]) => void calls.push(`quitAndInstall(${args.join(', ')})`),
        };
        const updater = new Updater({ updater: electron, version: '0.49.0', application: null, rollback: join(data, 'desktop', 'rollback'), log: memoryLog(), stopRuntime: async () => ({ kind: 'none' }), refused: () => undefined, beforeInstall: async () => undefined, afterRefused: async () => undefined, changed: () => undefined });
        updater.prepare();
        const { all } = deps(data, () => updater, { where: { window: { menu: true } }, windowOpen });
        const end = await taken(request, all);
        if (windowOpen === 'stop') {
            assert.equal(end.reason, 'window_open');
            assert.deepEqual(calls, ['check', 'download']);
        } else {
            assert.equal(end.state, 'installing');
            assert.deepEqual(calls, ['check', 'download', 'quitAndInstall(true, false)']);
        }
    }
});

test('a window refuses a request that names a feed before it checks anything: no feed set, no look, no download', async (t) => {
    const fed: UpdateRequest = { ...request, feed: 'http://127.0.0.1:8123/feed/' };
    for (const windowOpen of ['stop', 'close'] as const) {
        for (const menu of [true, false]) {
            const u = planted({ state: 'installing', offered: '0.49.1', runtimeStopped: true, kept: null });
            const end = await taken(fed, deps(scratch(t), u.updater, { where: { window: { menu } }, windowOpen }).all);
            assert.equal(end.state, 'refused');
            assert.equal(end.reason, 'window_feed');
            assert.equal(end.window, true);
            assert.match(end.message ?? '', /never from a feed a terminal names/);
            assert.equal(u.made(), 0, 'no updater is made, its own included');
        }
    }
    // The window's own updater, the Live one, which a window would otherwise take the request with.
    const data = scratch(t);
    const { electron, calls } = recordingUpdater('0.49.1');
    const updater = new Updater({ updater: electron, version: '0.49.0', application: null, rollback: join(data, 'desktop', 'rollback'), log: memoryLog(), stopRuntime: async () => ({ kind: 'none' }), refused: () => undefined, beforeInstall: async () => undefined, afterRefused: async () => undefined, changed: () => undefined });
    updater.prepare();
    const prepared = [...calls];
    const end = await taken(fed, deps(data, () => updater, { where: { window: { menu: true } }, windowOpen: 'stop' }).all);
    assert.equal(end.reason, 'window_feed');
    assert.deepEqual(calls, prepared, 'no setFeedURL, no check, no download');
    assert.deepEqual(updater.current, { step: 'idle' }, 'nothing waits under the install item');
    // A window-less start takes the same request; the log says the version and the step, never the feed or the id.
    const headless = scratch(t);
    const own = recordingUpdater('0.49.1');
    const ownUpdater = new Updater({ updater: own.electron, version: '0.49.0', application: null, rollback: join(headless, 'desktop', 'rollback'), log: memoryLog(), stopRuntime: async () => ({ kind: 'none' }), refused: () => undefined, beforeInstall: async () => undefined, afterRefused: async () => undefined, changed: () => undefined });
    ownUpdater.prepare();
    const { all, log } = deps(headless, () => ownUpdater);
    const installed = await taken(fed, all);
    assert.equal(installed.state, 'installing');
    assert.ok(own.calls.includes('setFeedURL(http://127.0.0.1:8123/feed/)'), 'the feed is read by a start of its own');
    assert.ok(log.lines.length > 0);
    assert.doesNotMatch(JSON.stringify(log.lines), /127\.0\.0\.1|8123|feed\/|req_01JA/);
});

test('a Demo window, which holds no updater, refuses a terminal’s request with its own reason: nothing checked, stopped or kept', async (t) => {
    for (const windowOpen of ['stop', 'close'] as const) {
        const { all, written, log } = demoDeps(scratch(t), { windowOpen });
        assert.equal('updater' in all, false, 'the Demo is given no updater to take it with');
        const end = await taken(request, all);
        assert.equal(end.state, 'refused');
        assert.equal(end.reason, 'demo_open');
        assert.equal(end.window, true);
        assert.equal(end.offered, null, 'no feed was read');
        assert.equal(end.runtime_stopped, false, 'no runtime was stopped');
        assert.equal(end.kept, null, 'no copy was kept');
        assert.match(end.message ?? '', /^Jason Desktop is showing the Demo, which takes no update, so nothing was checked, stopped or changed\./);
        assert.match(end.message ?? '', /Choose Back to Your Own Installation in its Jason menu, or quit Jason Desktop, then run jason update apply again\.$/);
        assert.ok((end.message ?? '').includes(`Choose ${demoMenu('demo')!.back.label} in its Jason menu`), 'the item the Demo’s menu draws');
        assert.deepEqual(written.map((record) => record.state), ['checking', 'refused']);
        assert.ok(log.lines.some((line) => line.name === 'update_request_ended' && line.fields.Code === 'demo_open'));
    }
});

test('a Demo window decides the Demo after a feed and a build it cannot take, and before a go found there', async (t) => {
    const fed: UpdateRequest = { ...request, feed: 'http://127.0.0.1:8123/feed/' };
    const byFeed = await taken(fed, demoDeps(scratch(t)).all);
    assert.equal(byFeed.reason, 'window_feed');
    assert.equal(byFeed.window, true);
    for (const unsupported of ['not_installed', 'unsupported_platform'] as const) {
        const end = await taken(request, demoDeps(scratch(t), { unsupported }).all);
        assert.equal(end.reason, unsupported);
    }
    const there: GoWait = { exists: () => true, ms: 10, pollMs: 5, sleep: async () => undefined };
    const waiting = await taken({ ...request, awaitGo: true }, demoDeps(scratch(t), { go: there }).all);
    assert.equal(waiting.reason, 'demo_open');
});

test('a Live window still takes the request with its updater: the Demo’s reason is the Demo’s alone', async (t) => {
    for (const menu of [true, false]) {
        const u = planted({ state: 'current' });
        const end = await taken(request, deps(scratch(t), u.updater, { where: { window: { menu } } }).all);
        assert.equal(end.state, 'current');
        assert.equal(u.made(), 1);
        assert.equal(u.asked.length, 1);
    }
});

test('an application that takes no request says why and makes no updater', async (t) => {
    for (const unsupported of ['not_installed', 'unsupported_platform'] as const) {
        const u = planted({ state: 'current' });
        const { all } = deps(scratch(t), u.updater, { unsupported });
        const end = await taken(request, all);
        assert.equal(end.state, 'refused');
        assert.equal(end.reason, unsupported);
        assert.equal(u.made(), 0);
        assert.equal(applyExit(end), applyExits.answered);
    }
});

test('a refusal ends with its reason and a sentence, the version the feed offered, and exit 0; current is no refusal', async (t) => {
    const u = planted({ state: 'refused', reason: 'version_mismatch', offered: '0.49.2', runtimeStopped: false, kept: null });
    const end = await taken(request, deps(scratch(t), u.updater).all);
    assert.equal(end.state, 'refused');
    assert.equal(end.reason, 'version_mismatch');
    assert.equal(end.offered, '0.49.2');
    assert.match(end.message ?? '', /offers Jason 0\.49\.2/);
    assert.equal(applyExit(end), 0);
    const current = await taken(request, deps(scratch(t), planted({ state: 'current' }).updater).all);
    assert.equal(current.state, 'current');
    assert.equal(current.reason, null);
    assert.equal(applyExit(current), 0);
});

test('an updater that throws ends as failed, exit 1, its text in neither the file nor the log', async (t) => {
    const data = scratch(t);
    const { all, log } = deps(data, planted({ state: 'current' }, [], { throws: true }).updater);
    const end = await taken(request, all);
    assert.equal(end.reason, 'failed');
    assert.equal(applyExit(end), 1);
    assert.doesNotMatch(readFileSync(requestFile(data, request.id), 'utf8'), /planted/);
    assert.doesNotMatch(JSON.stringify(log.lines), /planted|req_01JA/);
});

test('an install that fails late is written over installing, and heard once', async (t) => {
    const data = scratch(t);
    let late: (() => void) | undefined;
    const updater = () => ({
        async take(_taken: TakenRequest, how: TakenHow, progress: (step: TakenStep) => void): Promise<TakenOutcome> {
            late = how.late;
            progress({ step: 'installing', runtimeStopped: false, kept: null });
            return { state: 'installing', offered: '0.49.1', runtimeStopped: false, kept: null };
        },
    });
    const heard: RequestRecord[] = [];
    const { all } = deps(data, updater, { late: (record) => void heard.push(record) });
    assert.equal((await taken(request, all)).state, 'installing');
    late?.();
    late?.();
    assert.equal(heard.length, 1);
    assert.equal(heard[0]?.reason, 'install_failed');
    assert.equal(readRequestFile(requestFile(data, request.id))?.reason, 'install_failed');
});

test('a window-less start is bounded: past the bound while installing it writes timed_out and exits 1; a request that ended is not touched', async (t) => {
    assert.equal(headlessLimitMs, 30 * 60_000);
    const data = scratch(t);
    const timers = handTimers();
    const expired: RequestRecord[] = [];
    const u = planted({ state: 'installing', offered: '0.49.1', runtimeStopped: false, kept: null });
    const { all } = deps(data, u.updater, { limit: { ms: headlessLimitMs, timers, expired: (record) => void expired.push(record) } });
    await taken(request, all);
    assert.deepEqual(timers.pending.map((entry) => entry.ms), [headlessLimitMs]);
    timers.pending[0]?.run();
    assert.equal(expired[0]?.reason, 'timed_out');
    assert.equal(applyExit(expired[0]!), 1);
    const ended = handTimers();
    await taken(request, deps(scratch(t), planted({ state: 'current' }).updater, { limit: { ms: headlessLimitMs, timers: ended, expired: () => assert.fail('a request that ended is not timed out') } }).all);
    assert.deepEqual(ended.pending, [], 'the bound is cleared once the request ended short of an install');
});

test('on Windows a window-less start runs the system’s command interpreter, the system’s folders first on its PATH, and never the working directory’s', () => {
    const root = 'C:\\Windows';
    assert.deepEqual(systemShell({ SystemRoot: root, Path: 'D:\\planted;C:\\Tools', ComSpec: 'D:\\planted\\cmd.exe' }, 'win32'), {
        ComSpec: 'C:\\Windows\\System32\\cmd.exe',
        PATH: 'C:\\Windows\\System32;C:\\Windows;C:\\Windows\\System32\\Wbem;C:\\Windows\\System32\\WindowsPowerShell\\v1.0;D:\\planted;C:\\Tools',
        NoDefaultCurrentDirectoryInExePath: '1',
    });
    assert.deepEqual(systemShell({ SYSTEMROOT: root }, 'win32')?.PATH, 'C:\\Windows\\System32;C:\\Windows;C:\\Windows\\System32\\Wbem;C:\\Windows\\System32\\WindowsPowerShell\\v1.0', 'no PATH: the system’s alone');
    for (const bad of [undefined, '', 'Windows', '\\\\server\\share', 'C:\\Win;D:\\x', 'C:\\%x%', 'C:\\Windows\\']) {
        assert.equal(systemShell({ SystemRoot: bad, PATH: 'x' }, 'win32'), null, String(bad));
    }
    assert.equal(systemShell({ SystemRoot: root }, 'darwin'), null);
    assert.equal(systemShell({ SystemRoot: root }, 'linux'), null);
    const start = readFileSync(new URL('../src/update-start.ts', import.meta.url), 'utf8');
    const apply = start.slice(start.indexOf('export function runUpdateApply('), start.indexOf('export function runUpdateUsage('));
    assert.ok(apply.indexOf('Object.assign(process.env, shell);') > 0 && apply.indexOf('Object.assign(process.env, shell);') < apply.indexOf('requests.serve('), 'set before any updater is made');
});

test('requests wait for the run that serves them, one at a time; one that comes meanwhile is refused as busy', async () => {
    const busy: string[] = [];
    const served: string[] = [];
    let finish!: () => void;
    const requests = new UpdateRequests((refused) => void busy.push(refused.id));
    requests.arrived({ ...request, id: 'first' });
    requests.arrived({ ...request, id: 'second' });
    assert.deepEqual(busy, ['second'], 'one is held for the run; the next is busy');
    requests.serve(async (taken) => {
        served.push(taken.id);
        await new Promise<void>((resolve) => (finish = resolve));
    });
    assert.deepEqual(served, ['first']);
    requests.arrived({ ...request, id: 'third' });
    assert.deepEqual(busy, ['second', 'third']);
    finish();
    await new Promise((resolve) => setImmediate(resolve));
    requests.arrived({ ...request, id: 'fourth' });
    assert.deepEqual(served, ['first', 'fourth']);
    finish();
});

// ---- The main module ----------------------------------------------------------------------------------------------

// What runs around Electron's own start cannot be run here: how the main module starts a request is held on its text.
test('the main module decides the update starts after the uninstaller’s, as an installer start, and they open no window', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const start = main.slice(main.indexOf('function startDesktop('), main.indexOf('function startUpdate('));
    const stop = start.indexOf('const stoppingRuntime = ');
    const updating = start.indexOf('const updating = uninstalling === null && !stoppingRuntime ? updateStart(process.argv) : null;');
    const chosen = start.indexOf('chooseInstallation(uninstalling !== null || stoppingRuntime || updating !== null);');
    const branch = start.indexOf('} else if (updating !== null) {\n        // A terminal');
    const lock = start.indexOf('} else if (!app.requestSingleInstanceLock(');
    assert.ok(stop > 0 && updating > stop && chosen > updating && branch > chosen && lock > branch, 'decided after --uninstall and --stop-runtime, before the window’s lock');
    // A window's second start hands a request to the run that takes it, after a link and before the window is shown.
    assert.ok(start.includes('windowRequests = new UpdateRequests((request) => refuseBusy(request, true));'));
    const second = start.slice(start.indexOf("app.on('second-instance'"));
    assert.ok(second.indexOf('const request = requestOf(data);') > second.indexOf('linkArrived(link, links, early)'));
    assert.ok(second.indexOf('if (request !== null) return windowRequests?.arrived(request);') < second.indexOf('if (showsOnSecondStart(argv)) showWindow();'));

    const update = main.slice(main.indexOf('function startUpdate('));
    // No window, no run, no runtime started by the start itself.
    for (const never of ['new BrowserWindow(', 'run(links', 'runDemo(', 'startInstallation(', "'runtime', 'start'"]) assert.equal(update.includes(never), false, never);
    // macOS: no Dock icon, the policy set before Electron is ready and again when it is.
    const policy = update.indexOf("if (process.platform === 'darwin') app.setActivationPolicy('accessory');");
    assert.ok(policy > 0 && update.indexOf('headless();') > policy && update.indexOf('headless();') < update.indexOf('app.whenReady()'));
    assert.ok(update.includes('const ready = app.whenReady().then(headless);'));
    // The lock is taken with the request as its additional data, and a start that does not get it exits 3.
    assert.ok(update.includes('if (!app.requestSingleInstanceLock(handedData(request))) {'));
    assert.ok(update.indexOf('app.exit(applyExits.handedOver);') > update.indexOf('requestSingleInstanceLock(handedData(request))'));
    assert.equal(applyExits.handedOver, 3);
    assert.equal(applyExits.usage, 2);
    // While it holds the lock, a second start's request is refused as busy.
    assert.ok(update.includes("app.on('second-instance', (_event, argv, _directory, data) => {\n        const other = requestOf(data);\n        if (other !== null) return requests.arrived(other);"));
    assert.ok(update.includes('const requests = new UpdateRequests((other) => refuseBusy(other, false));'));
});

test('the settling start takes the single-instance lock, and leaves the record to a Jason Desktop that holds it', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const update = main.slice(main.indexOf('function startUpdate('));
    const settle = update.slice(update.indexOf("if (updating.kind === 'settle') {"), update.indexOf('const { request } = updating;'));
    const lock = settle.indexOf('if (!app.requestSingleInstanceLock()) {');
    assert.ok(lock > 0, 'the settle takes the lock');
    assert.ok(settle.indexOf("startLog().info('update_settle_left', {});") > lock);
    assert.ok(settle.indexOf('app.exit(0);') > lock && settle.indexOf('app.exit(0);') < settle.indexOf('ready.then(runUpdateSettle)'), 'exits 0 before it settles anything');
    // A window that holds the lock is not brought forward by it, or by any start of the update.
    const start = main.slice(main.indexOf('function startDesktop('), main.indexOf('function startUpdate('));
    const second = start.slice(start.indexOf("app.on('second-instance'"));
    assert.ok(second.indexOf('if (updateStart(argv) !== null) return;') > second.indexOf('return windowRequests?.arrived(request);'));
    assert.ok(second.indexOf('if (updateStart(argv) !== null) return;') < second.indexOf('if (showsOnSecondStart(argv)) showWindow();'));
    // A busy or usage refusal is made only where no file is there: never over a request under way with the same id.
    const starts = readFileSync(new URL('../src/update-start.ts', import.meta.url), 'utf8');
    assert.equal(starts.includes('writeRequestFile('), false);
    assert.equal(starts.split('createRequestFile(requestFile(data, ').length - 1, 2);
});

test('a Live window’s run takes requests with its own updater, and the Demo’s says it is the Demo', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const live = main.slice(main.indexOf('async function run('), main.indexOf('async function runDemo('));
    assert.ok(live.includes('serveRequests(log, paths.data, { menu: updates !== null, updater: () => updates ?? requestUpdater({ data: paths.data, log }) });'));
    const demo = main.slice(main.indexOf('async function runDemo('), main.indexOf('function keepElectronUnderData('));
    assert.ok(demo.includes("serveRequests(log, runOn.person, 'demo');"), 'the Demo’s window refuses as the Demo');
    assert.equal(demo.includes('requestUpdater('), false, 'and holds no updater');
    const served = main.slice(main.indexOf('function serveRequests('), main.indexOf('\n}\n', main.indexOf('function serveRequests(')));
    for (const member of ['data,', "...(window === 'demo' ? { where: 'demo' as const } : { where: { window: { menu: window.menu } }, updater: window.updater }),", 'unsupported: requestSupport(app.isPackaged, process.platform),', 'windowOpen: windowOpenUpdate,', 'log,']) {
        assert.ok(served.includes(member), member);
    }
    assert.equal(existsSync(new URL('../src/update-start.ts', import.meta.url)), true);
});

// ---- The go -------------------------------------------------------------------------------------------------------

/** A go wait of the test's own: the file looked for as it is, a few short pauses, and what happened at each look. */
function goWaitOf(looks: number, onLook: (look: number) => void = () => undefined): GoWait & { looked: () => number } {
    let looked = 0;
    return {
        exists: (file) => {
            looked += 1;
            onLook(looked);
            return existsSync(file);
        },
        ms: looks * 5,
        pollMs: 5,
        sleep: (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
        looked: () => looked,
    };
}

/** The real updater over a recording electron-updater, its runtime stop and its keep heard in order. */
function realUpdater(data: string) {
    const { electron, calls } = recordingUpdater('0.49.1');
    const order: string[] = [];
    const updater = new Updater({
        updater: electron,
        version: '0.49.0',
        application: null,
        rollback: join(data, 'desktop', 'rollback'),
        log: memoryLog(),
        stopRuntime: async () => {
            order.push('stopRuntime');
            return { kind: 'stopped' };
        },
        refused: () => undefined,
        beforeInstall: async () => void order.push('beforeInstall'),
        afterRefused: async () => undefined,
        changed: () => undefined,
    });
    updater.prepare();
    return { updater, calls, order };
}

const waits: UpdateRequest = { ...request, awaitGo: true };

test('--await-go is read once beside a request, handed to a running window and read again there, and the settle takes none', () => {
    assert.equal(awaitGoFlag, '--await-go');
    assert.deepEqual(updateStart(['Jason', '--update-apply', '--to', '0.49.1', '--request', 'req_01JA', '--await-go']), { kind: 'apply', request: waits });
    assert.equal(updateStart(['Jason', '--update-apply', '--to', '0.49.1', '--request', 'r1', '--await-go', '--await-go'])?.kind, 'usage');
    assert.equal(updateStart(['Jason', '--update-settle', '--await-go'])?.kind, 'usage');
    assert.deepEqual(requestOf(handedData(waits)), waits);
    assert.deepEqual(handedData(request), { update: { id: 'req_01JA', to: '0.49.1', feed: null } }, 'a request that waits for nothing hands nothing more than before');
    for (const awaitGo of [false, 'yes', 1, null]) assert.equal(requestOf({ update: { ...request, awaitGo } }), null, String(awaitGo));
    assert.equal(goFile('/data', 'req_01JA'), join('/data', 'update', 'desktop-req_01JA.go'));
});

test('the go is waited for longer than the verb may take to give it, by a stated margin, and a start that waits for it is bounded by both', () => {
    // The verb gives the go within ten minutes of reading `downloaded` (docs/release-and-update.md §7), or gives none.
    const verbGoBoundMs = 10 * 60_000;
    assert.equal(goWaitMs, 15 * 60_000);
    assert.ok(goWaitMs - verbGoBoundMs >= 5 * 60_000, 'a margin of five minutes over the verb’s own bound');
    assert.equal(headlessLimitOf(request), headlessLimitMs, 'a request that waits for nothing is bounded as before');
    assert.equal(headlessLimitOf(waits), headlessLimitMs + goWaitMs);
});

test('a request that names no --await-go never waits: no downloaded step, no go looked for, installed as before', async (t) => {
    const data = scratch(t);
    const { updater, calls, order } = realUpdater(data);
    const never: GoWait = { exists: () => assert.fail('a request that waits for nothing looked for a go'), ms: 10, pollMs: 5, sleep: async () => undefined };
    const { all, written } = deps(data, () => updater, { go: never });
    const end = await taken(request, all);
    assert.equal(end.state, 'installing');
    assert.deepEqual(written.map((record) => record.state), ['checking', 'checking', 'downloading', 'installing']);
    assert.deepEqual(calls.filter((call) => !call.startsWith('setFeedURL')), ['check', 'download', 'quitAndInstall(true, false)']);
    assert.deepEqual(order, ['stopRuntime', 'beforeInstall']);
    const planted_ = planted({ state: 'current' });
    await taken(request, deps(scratch(t), planted_.updater).all);
    assert.equal(planted_.asked[0]?.how.go, undefined, 'the updater is handed no go to wait for');
});

test('a request that waits for the go says downloaded and stops, keeps and installs nothing until the go is there', async (t) => {
    const data = scratch(t);
    const { updater, calls, order } = realUpdater(data);
    // The go appears at the third look, as the verb makes it once it has stopped the runtime and stepped out.
    const wait = goWaitOf(100, (look) => {
        assert.deepEqual(order, [], 'the runtime was stopped before the go');
        // The first look is the one before the request is taken; every later one is the wait's, at `downloaded`.
        if (look > 1) assert.equal(readRequestFile(requestFile(data, request.id))?.state, 'downloaded');
        if (look === 4) writeFileSync(goFile(data, request.id), '');
    });
    const { all, written } = deps(data, () => updater, { go: wait });
    const end = await taken(waits, all);
    assert.equal(end.state, 'installing');
    assert.deepEqual(written.map((record) => record.state), ['checking', 'checking', 'downloading', 'downloaded', 'installing']);
    assert.equal(written[3]?.offered, '0.49.1');
    // One look before the request was taken, then the three of the wait.
    assert.equal(wait.looked(), 4);
    assert.deepEqual(order, ['stopRuntime', 'beforeInstall']);
    assert.deepEqual(calls.filter((call) => !call.startsWith('setFeedURL')), ['check', 'download', 'quitAndInstall(true, false)']);
});

test('no go in time: go_timed_out, nothing stopped, kept or installed, and the download waits under the install item', async (t) => {
    const data = scratch(t);
    const { updater, calls, order } = realUpdater(data);
    const { all, written, log } = deps(data, () => updater, { go: goWaitOf(4) });
    const end = await taken(waits, all);
    assert.equal(end.state, 'refused');
    assert.equal(end.reason, 'go_timed_out');
    assert.equal(end.runtime_stopped, false);
    assert.equal(end.kept, null);
    assert.match(end.message ?? '', /did not say go within 15 minutes, so nothing was stopped, kept or installed/);
    // A start of its own has no menu: the sentence names none, and the window's own Check for Updates instead.
    assert.doesNotMatch(end.message ?? '', /install item/);
    assert.match(end.message ?? '', /Check for Updates in Jason Desktop's menu once it is open/);
    assert.equal(applyExit(end), applyExits.answered);
    assert.deepEqual(written.map((record) => record.state), ['checking', 'checking', 'downloading', 'downloaded', 'refused']);
    assert.deepEqual(order, [], 'nothing stopped or let go');
    assert.equal(existsSync(join(data, 'desktop', 'rollback', '0.49.0')), false, 'nothing kept');
    assert.ok(!calls.some((call) => call.startsWith('quitAndInstall')), 'nothing installed');
    assert.deepEqual(updater.current, { step: 'downloaded', version: '0.49.1' }, 'the menu keeps the download');
    assert.ok(log.lines.some((line) => line.name === 'update_request_ended' && line.fields['Code'] === 'go_timed_out'));
});

test('a go that is there before the request is taken is no go: refused as go_there, with no updater made', async (t) => {
    const data = scratch(t);
    mkdirSync(join(data, 'update'), { recursive: true });
    writeFileSync(goFile(data, request.id), '');
    const u = planted({ state: 'installing', offered: '0.49.1', runtimeStopped: true, kept: null });
    const end = await taken(waits, deps(data, u.updater).all);
    assert.equal(end.reason, 'go_there');
    assert.equal(u.made(), 0);
    assert.match(end.message ?? '', /no go/);
    // A request that waits for nothing does not look at a go file at all.
    writeFileSync(goFile(data, 'req_plain'), '');
    assert.equal((await taken({ ...request, id: 'req_plain' }, deps(data, planted({ state: 'current' }).updater).all)).state, 'current');
});

test('a window that would install waits for the go too, and its menu stays at the download when none comes; one that may not close refuses first', async (t) => {
    for (const windowOpen of ['close', 'stop'] as const) {
        const data = scratch(t);
        const { updater, order } = realUpdater(data);
        const wait = goWaitOf(3);
        const end = await taken(waits, deps(data, () => updater, { where: { window: { menu: true } }, windowOpen, go: wait }).all);
        assert.equal(end.window, true);
        assert.equal(end.reason, windowOpen === 'close' ? 'go_timed_out' : 'window_open', windowOpen);
        if (windowOpen === 'close') assert.match(end.message ?? '', /The download waits under the install item of Jason Desktop's menu/);
        // The look before the request is taken, and for a window that would install, the wait's own looks.
        assert.equal(wait.looked() > 1, windowOpen === 'close', windowOpen);
        assert.deepEqual(order, []);
        assert.deepEqual(updater.current, { step: 'downloaded', version: '0.49.1' }, windowOpen);
    }
});

test('a go made before the request said downloaded is no go the verb made: refused as go_there, nothing stopped, kept or installed', async (t) => {
    const data = scratch(t);
    const { updater, calls, order } = realUpdater(data);
    // A go planted while the update downloads: there at the wait's first look, dated before the request's `downloaded`.
    const wait = goWaitOf(100, (look) => {
        if (look === 1) return;
        const go = goFile(data, request.id);
        if (!existsSync(go)) {
            writeFileSync(go, '');
            const before = new Date(Date.now() - 60_000);
            utimesSync(go, before, before);
        }
    });
    const end = await taken(waits, deps(data, () => updater, { go: { ...wait, made: (file) => statSync(file).mtimeMs } }).all);
    assert.equal(end.state, 'refused');
    assert.equal(end.reason, 'go_there');
    assert.match(end.message ?? '', /no go the verb made: nothing was stopped, kept or installed/);
    assert.deepEqual(order, [], 'nothing stopped or let go');
    assert.ok(!calls.some((call) => call.startsWith('quitAndInstall')), 'nothing installed');

    // The verb's own go, made after the request said downloaded, is taken.
    const fresh = scratch(t);
    const second = realUpdater(fresh);
    const own = goWaitOf(100, (look) => {
        if (look === 3) writeFileSync(goFile(fresh, request.id), '');
    });
    const taken_ = await taken(waits, deps(fresh, () => second.updater, { go: { ...own, made: (file) => statSync(file).mtimeMs } }).all);
    assert.equal(taken_.state, 'installing');
});

test('the wait for the go ends when the request has ended meanwhile, its bound passed', async () => {
    let over = false;
    const wait: GoWait = {
        exists: () => false,
        ms: 1_000,
        pollMs: 5,
        sleep: async () => {
            over = true;
        },
    };
    assert.equal(await waitForGo('/nowhere.go', wait, () => over), false);
    assert.equal(await waitForGo('/nowhere.go', { ...wait, exists: () => true }, () => false), true);
});

test('a person who opens Jason Desktop while a window-less request holds the lock is remembered, and the application opens as the request ends short of an install', () => {
    const atLogin = (argv: readonly string[]) => argv.includes('--opened-at-login');
    // A plain start, a link's start: a window asked for. A request handed over, an update start, a start at sign-in: not.
    assert.equal(asksForWindow(['Jason'], undefined, atLogin), true);
    assert.equal(asksForWindow(['Jason'], { link: 'jason://open' }, atLogin), true);
    assert.equal(asksForWindow(['Jason', '--update-apply'], handedData({ id: 'r2', to: '0.49.0', feed: null }), atLogin), false);
    assert.equal(asksForWindow(['Jason', '--update-settle'], undefined, atLogin), false);
    assert.equal(asksForWindow(['Jason', '--opened-at-login'], undefined, atLogin), false);
    // Opened as the start exits with any code; never where nothing exits because the installer has the application.
    assert.equal(openAfterRequest(true, applyExits.answered), true);
    assert.equal(openAfterRequest(true, applyExits.failed), true);
    assert.equal(openAfterRequest(false, applyExits.answered), false);
    assert.equal(openAfterRequest(true, applyExit({ request: 'r', state: 'installing' } as never)), false);
    // main: the window-less start remembers it on second-instance and relaunches only on its own exit path.
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const start = main.slice(main.indexOf('function startUpdate('), main.indexOf('\n}\n', main.indexOf('function startUpdate(')));
    assert.ok(start.includes('if (!asksForWindow(argv, data, openedAtLogin)) return;'));
    // (f) The holding start says once that Jason is updating, waits for no answer, and promises no window after an install.
    const said = start.indexOf("if (!windowAsked) void dialog.showMessageBox({ type: 'info', title: windowTitle, ...updatingWords, buttons: ['OK'] }).catch(() => undefined);");
    assert.ok(said > 0 && said < start.indexOf('windowAsked = true;'));
    assert.equal(start.split('showMessageBox(').length - 1, 1);
    assert.equal(updatingWords.message, 'Jason is updating.');
    assert.match(updatingWords.detail, /^Jason Desktop opens once the update is done; where it installs a new version, that version opens the next time you start Jason\.$/);
    assert.ok(start.includes('if (openAfterRequest(windowAsked, code)) {'));
    assert.ok(start.indexOf('app.relaunch(') < start.indexOf('app.exit(code);'));
    assert.equal(start.split('app.relaunch(').length - 1, 1);
});

// ---- A step the verb's poll holds, and a step that cannot be written ------------------------------------------------

const refusal = (code: string): Error => Object.assign(new Error(`${code}: operation not permitted, rename`), { code });

/**
 * The disk, but a rename over a file a reader holds is refused as Windows refuses it (EPERM), however the reader opened
 * it. On Windows the reader's handle is a real one and the refusal is the system's own; elsewhere the seam says it.
 */
function heldDisk() {
    let held: number | null = null;
    const seen = { refused: 0, renamed: 0, inPlace: 0, slept: 0 };
    const io: RequestFileIo = {
        write: (file, text) => {
            if (!file.endsWith('.tmp')) seen.inPlace += 1;
            writeFileSync(file, text);
        },
        rename: (from, to) => {
            try {
                if (held !== null && process.platform !== 'win32') throw refusal('EPERM');
                renameSync(from, to);
                seen.renamed += 1;
            } catch (error) {
                seen.refused += 1;
                throw error;
            }
        },
        remove: (file) => rmSync(file, { force: true }),
        sleep: () => void (seen.slept += 1),
    };
    return {
        io,
        seen,
        hold: (file: string) => {
            held = existsSync(file) ? openSync(file, 'r') : null;
        },
        letGo: () => {
            if (held !== null) closeSync(held);
            held = null;
        },
    };
}

test('a reader holding the request’s file while each step is written: the rename is refused, tried again, then written in place and read whole', async (t) => {
    assert.ok(renameTries * renamePauseMs >= 250, 'the moment tried again outlasts one read of a small file');
    assert.ok(renameTries * renamePauseMs <= 1_000, 'and is short beside the verb’s poll');
    const data = scratch(t);
    const disk = heldDisk();
    const { updater, calls } = realUpdater(data);
    const ways: string[] = [];
    const { all, written } = deps(data, () => updater, {
        go: goWaitOf(100, (look) => {
            if (look === 3) writeFileSync(goFile(data, request.id), '');
        }),
        write: (file, record) => {
            written.push(record);
            // The verb's poll has the file open at the moment the step is written, for longer than the rename is tried.
            disk.hold(file);
            const before = disk.seen.refused;
            try {
                ways.push(writeRequestFile(file, record, disk.io, 'win32'));
            } finally {
                disk.letGo();
            }
            assert.equal(disk.seen.refused - before, renameTries, `${record.state}: every rename refused while the reader holds the file`);
            // The next poll reads the step whole: the in-place write is the record, not a part of it.
            assert.deepEqual(readRequestFile(file), record, `${record.state}: read whole`);
        },
    });
    const end = await taken(waits, all);
    assert.equal(end.state, 'installing');
    assert.deepEqual(written.map((record) => record.state), ['checking', 'checking', 'downloading', 'downloaded', 'installing']);
    // The first record makes the file (`createRequestFile`); every step after it is written over a file the reader holds.
    assert.deepEqual(ways, ['in_place', 'in_place', 'in_place', 'in_place'], 'the fallback taken for every step');
    assert.equal(disk.seen.inPlace, ways.length);
    assert.equal(disk.seen.slept, ways.length * (renameTries - 1), 'a pause between tries, none after the last');
    assert.deepEqual(readdirSync(join(data, 'update')).filter((name) => name.endsWith('.tmp')), [], 'no file beside it is left');
    assert.ok(calls.includes('quitAndInstall(true, false)'), '`installing` was on disk, so the installer was started');
});

test('the rename stays the normal case: a reader that lets go is waited for, and macOS renames once and is refused as before', (t) => {
    const data = scratch(t);
    const file = requestFile(data, 'req_01JA');
    const record = usageRecord('req_01JA', 'why', '0.49.0', new Date('2026-11-17T10:00:00Z'));
    // Nobody holds it: renamed at once, on both systems, with no pause.
    const free = heldDisk();
    assert.equal(writeRequestFile(file, record, free.io, 'win32'), 'renamed');
    assert.equal(writeRequestFile(file, record, free.io, 'darwin'), 'renamed');
    assert.deepEqual(free.seen, { refused: 0, renamed: 2, inPlace: 0, slept: 0 });
    // A reader that lets go after two refusals: the third rename goes through, and nothing is written in place.
    let refusals = 0;
    const brief: RequestFileIo = {
        ...free.io,
        rename: (from, to) => {
            if (refusals < 2) {
                refusals += 1;
                throw refusal('EBUSY');
            }
            renameSync(from, to);
        },
    };
    assert.equal(writeRequestFile(file, { ...record, message: 'second' }, brief, 'win32'), 'renamed');
    assert.equal(readRequestFile(file)?.message, 'second');
    assert.equal(free.seen.inPlace, 0);
    // macOS is unchanged: one rename, its refusal thrown, the file beside it removed, nothing written in place.
    refusals = 0;
    assert.throws(() => writeRequestFile(file, { ...record, message: 'third' }, brief, 'darwin'), /EBUSY/);
    assert.equal(refusals, 1);
    assert.equal(readRequestFile(file)?.message, 'second');
    // A refusal of another kind is never taken for a reader's handle, on Windows either.
    const full: RequestFileIo = {
        ...free.io,
        rename: () => {
            throw refusal('ENOSPC');
        },
    };
    assert.throws(() => writeRequestFile(file, record, full, 'win32'), /ENOSPC/);
    assert.equal(free.seen.inPlace, 0);
    assert.deepEqual(readdirSync(join(data, 'update')), ['desktop-req_01JA.json'], 'no file beside it is left');
});

/** A write of the request's file that neither way can do for the states named: the disk refuses, the in-place write too. */
function unwritable(states: string[], written: RequestRecord[]): TakeDeps['write'] {
    return (file, record) => {
        written.push(record);
        if (states.includes(record.state)) throw refusal('EPERM');
        writeRequestFile(file, record);
    };
}

test('`installing` that cannot be written: no installer is started, and the request ends refused where that can still be written', async (t) => {
    const data = scratch(t);
    const { updater, calls, order } = realUpdater(data);
    const written: RequestRecord[] = [];
    const { all, log } = deps(data, () => updater, { write: unwritable(['installing'], written) });
    const end = await taken(request, all);
    assert.ok(!calls.some((call) => call.startsWith('quitAndInstall')), 'no installer started');
    assert.deepEqual(order, ['stopRuntime'], 'the runtime was stopped, and nothing was let go to the installer');
    // The first `checking` made the file; these are the steps written over it.
    assert.deepEqual(written.map((record) => record.state), ['checking', 'downloading', 'installing', 'refused']);
    assert.equal(end.state, 'refused');
    assert.equal(end.reason, 'failed');
    assert.equal(end.runtime_stopped, true);
    assert.match(end.message ?? '', /jason runtime start starts it again/);
    assert.deepEqual(readRequestFile(requestFile(data, request.id)), end, 'the end is on disk for the verb');
    assert.deepEqual(updater.current, { step: 'downloaded', version: '0.49.1' }, 'the download waits under the install item');
    assert.ok(log.lines.some((line) => line.name === 'update_request_unwritten' && line.fields['State'] === 'installing'));
});

test('neither `installing` nor the refusal can be written: it ends without installing, and the verb sees no answer of either', async (t) => {
    const data = scratch(t);
    const { updater, calls, order } = realUpdater(data);
    const written: RequestRecord[] = [];
    const { all, log } = deps(data, () => updater, { write: unwritable(['installing', 'refused'], written) });
    await takeRequest(request, all);
    assert.ok(!calls.some((call) => call.startsWith('quitAndInstall')), 'no installer started');
    assert.deepEqual(order, ['stopRuntime']);
    assert.equal(readRequestFile(requestFile(data, request.id))?.state, 'downloading', 'the file still says the last step it was told');
    assert.deepEqual(
        log.lines.filter((line) => line.name === 'update_request_unwritten').map((line) => line.fields['State']),
        ['installing', 'refused'],
    );
});

test('`downloaded` that cannot be written: refused at once, no go waited for, nothing stopped, kept or installed', async (t) => {
    const data = scratch(t);
    const { updater, calls, order } = realUpdater(data);
    const written: RequestRecord[] = [];
    const wait = goWaitOf(100, (look) => assert.equal(look, 1, 'only the look before the request was taken'));
    const { all } = deps(data, () => updater, { go: wait, write: unwritable(['downloaded'], written) });
    const end = await taken(waits, all);
    assert.equal(end.state, 'refused');
    assert.equal(end.reason, 'failed');
    assert.equal(end.runtime_stopped, false);
    assert.equal(end.kept, null);
    assert.deepEqual(written.map((record) => record.state), ['checking', 'downloading', 'downloaded', 'refused']);
    assert.deepEqual(order, []);
    assert.equal(existsSync(join(data, 'desktop', 'rollback', '0.49.0')), false, 'nothing kept');
    assert.ok(!calls.some((call) => call.startsWith('quitAndInstall')));
    assert.equal(wait.looked(), 1);
});

test('R1: the runtime the updater stopped is started again by the desktop where the file could be told nothing, and left to the verb where `failed` was written', async (t) => {
    // Neither `installing` nor the refusal written: the verb never learns the runtime was stopped, so the desktop starts it.
    const data = scratch(t);
    const { updater, calls } = realUpdater(data);
    const starts: string[] = [];
    const { all, log } = deps(data, () => updater, {
        write: unwritable(['installing', 'refused'], []),
        startRuntime: async () => (starts.push('runtime start'), 'started'),
    });
    await takeRequest(request, all);
    assert.ok(!calls.some((call) => call.startsWith('quitAndInstall')));
    assert.deepEqual(starts, ['runtime start'], 'started again at once, once');
    assert.ok(log.lines.some((line) => line.name === 'update_request_runtime_started' && line.fields['Outcome'] === 'started'));
    // The refusal written, `runtime_stopped` in it: the verb is told, and restarting it stays the verb's.
    const told = scratch(t);
    const second = realUpdater(told);
    const none: string[] = [];
    const end = await taken(request, deps(told, () => second.updater, {
        write: unwritable(['installing'], []),
        startRuntime: async () => (none.push('runtime start'), 'started'),
    }).all);
    assert.equal(end.runtime_stopped, true);
    assert.deepEqual(none, [], 'the desktop starts nothing where the verb was told');
    // Nothing stopped, nothing to start: a `downloaded` it could not write stops nothing.
    const early = scratch(t);
    const third = realUpdater(early);
    const nothing: string[] = [];
    await takeRequest(waits, deps(early, () => third.updater, {
        go: goWaitOf(100),
        write: unwritable(['downloaded', 'refused'], []),
        startRuntime: async () => (nothing.push('runtime start'), 'started'),
    }).all);
    assert.deepEqual(nothing, []);
    // Both doors give it the carried jason's start: a window's requests and a window-less start's.
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const start = readFileSync(new URL('../src/update-start.ts', import.meta.url), 'utf8');
    assert.ok(main.includes('startRuntime: carriedRuntimeStart(data, log),'));
    assert.ok(start.includes('startRuntime: carriedRuntimeStart(data, log),'));
    assert.ok(start.includes('return (await startAgain(runtimeDoor(cli, desktopPaths(data).runFile))).act;'));
});
