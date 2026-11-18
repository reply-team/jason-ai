/**
 * A terminal's update request: how `jason update apply`, run as the `jason` this application carries, hands the update of
 * the application to it and reads what came of it. The contract is `docs/release-and-update.md`, "Handing the update to
 * Jason Desktop"; this module is the Desktop's side of it, and nothing here needs Electron to be tested.
 *
 * The verb starts the application with `--update-apply --to <version> --request <id>`, and `--feed <url>` where it was
 * given a loopback feed. The start shows no window (on macOS no Dock icon either) and takes the single-instance lock of
 * its data directory; where a running Jason Desktop holds it, the request goes to that one in the lock's additional data
 * and this start exits 3. Whichever process takes it checks the feed for exactly that version, downloads it, stops the
 * runtime it carries, keeps the application aside and hands the update to the installer, silently and with no start
 * after it; or refuses, with a reason. Each step is written to `<data>/update/desktop-<id>.json` before it is taken, so
 * the file is the answer, read after the process has ended, whatever ended it. The file is made by the process that
 * takes the request, never over one already there, and only that process writes it after. A window that takes a request
 * goes as far as the download and stops there unless one switch says it may close to install (`windowOpenUpdate`), and
 * takes none that names a feed.
 *
 * `--await-go` makes a request wait, once the update is downloaded, for the verb's go before anything is stopped, kept or
 * installed: the request's file names `downloaded`, the verb drains and stops the runtime and steps out of the
 * application's folder, and only then makes `<data>/update/desktop-<id>.go`, by an exclusive create. A request that names
 * no `--await-go` never waits, as before. A go that does not come within `goWaitMs` refuses the request as `go_timed_out`,
 * with nothing stopped, kept or installed, and a window keeps the download under its menu's install item; a go file
 * already there when the request is taken, or one made before the request's file said `downloaded`, is no go the verb
 * made, and refuses it as `go_there`.
 *
 * `--update-settle` is the other start: once the new version's runtime answered, it records the update the window-less
 * install left pending as kept (`rollback.ts`), as a window's first start would, under the single-instance lock.
 */

import { closeSync, existsSync, mkdirSync, openSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import type { Timers } from './attention.ts';
import type { Logger } from './log.ts';
import { backLabel } from './mode.ts';
import { demoRuntimeNext, demoRuntimeWords, unansweredWords, type Unanswered } from './own-runtime.ts';
import type { TakenOutcome, TakenRefusal, TakenRequest, TakenStep, Updater } from './updates.ts';

/** A runtime that did not stop for the install: what the person is told to look at. */
type StoppedShort = { kind: 'failed'; exit: number; demo?: true } | Unanswered;

/** The start a terminal's `jason update apply` makes, with no window, to update the application to one version. */
export const updateApplyFlag = '--update-apply';
/** The start that records a window-less update as kept once the new version's runtime answered. */
export const updateSettleFlag = '--update-settle';
export const toFlag = '--to';
export const requestFlag = '--request';
export const feedFlag = '--feed';
/** The request waits for the verb's go once the update is downloaded (`goFile`). */
export const awaitGoFlag = '--await-go';

/**
 * What a window does with a terminal's request once the update is downloaded: `stop` there, leaving it under the menu's
 * install item and refusing the request with `window_open`, or `close` to install as a person's click would. Whether an
 * open Jason Desktop may close itself for an update asked from a terminal is still to be decided; until it is, it stops.
 */
export type WindowOpenUpdate = 'stop' | 'close';
export const windowOpenUpdate: WindowOpenUpdate = 'stop';

/** How long a window-less start waits for its request to end, the install included, before it says so and exits. */
export const headlessLimitMs = 30 * 60_000;

/**
 * How long a request that waits for the go waits for it once the update is downloaded: longer than the verb may take
 * between reading `downloaded` and making the go, ten minutes by the verb's own bound (the drain, the stop and the move),
 * by a margin of five minutes. A window-less start that waits for the go is bounded by `headlessLimitMs` and this together.
 */
export const goWaitMs = 15 * 60_000;

/** How often the go file is looked for while the request waits for it. */
export const goPollMs = 250;

/** The bound of a window-less start: `headlessLimitMs`, and `goWaitMs` beside it where the request waits for the go. */
export function headlessLimitOf(request: UpdateRequest): number {
    return request.awaitGo === true ? headlessLimitMs + goWaitMs : headlessLimitMs;
}

/**
 * The request the verb names: an id of its own, the version it planned for, a loopback feed or none, and whether it waits
 * for the verb's go once the update is downloaded (there only where it does).
 */
export interface UpdateRequest {
    id: string;
    to: string;
    feed: string | null;
    awaitGo?: true;
}

/** What a start's arguments ask: a request, the settling start, or neither read, with the id when that one was. */
export type UpdateStart = { kind: 'apply'; request: UpdateRequest } | { kind: 'settle' } | { kind: 'usage'; id: string | null; why: string };

/** A request's id: a file name's worth of letters, digits, `-` and `_`, never a path. */
export function isRequestId(value: string): boolean {
    return /^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/.test(value);
}

/** A release's version as the feeds name it: three numbers and an optional pre-release part. */
export function isVersion(value: string): boolean {
    return /^(?:0|[1-9]\d{0,8})\.(?:0|[1-9]\d{0,8})\.(?:0|[1-9]\d{0,8})(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/.test(value);
}

/**
 * The feed a request may name: http on the loopback address alone, the address the runtime's own feed allows besides
 * https (`UpdateFeed.IsAllowed`): `localhost`, `127.0.0.0/8` or `[::1]`, with no credentials, query or fragment. Returns
 * the address as the URL parser wrote it, or null for anything else, https included: a feed of another host is the
 * releases' to serve, and this one is for a test's own server.
 */
export function loopbackFeed(raw: string): string | null {
    let url: URL;
    try {
        url = new URL(raw);
    } catch {
        return null;
    }
    if (url.protocol !== 'http:' || url.username !== '' || url.password !== '' || url.search !== '' || url.hash !== '') return null;
    const host = url.hostname;
    const loopback = host === 'localhost' || host === '[::1]' || /^127\.\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(host);
    return loopback ? url.href : null;
}

/** The value after a flag given once, never another flag; null when it is not there, `false` when it is there wrongly. */
function valueOf(argv: readonly string[], flag: string): string | null | false {
    const at = argv.indexOf(flag);
    if (at < 0) return null;
    if (argv.indexOf(flag, at + 1) >= 0) return false;
    const value = argv[at + 1];
    return value === undefined || value.startsWith('--') ? false : value;
}

/**
 * Pure: what a start's arguments ask of the update, or null when they ask nothing of it. Read on every system and in
 * every build, so a start a terminal made never opens a window: what cannot take a request says so in its file.
 */
export function updateStart(argv: readonly string[]): UpdateStart | null {
    const applying = argv.includes(updateApplyFlag);
    const settling = argv.includes(updateSettleFlag);
    if (!applying && !settling) return null;
    const rawId = valueOf(argv, requestFlag);
    const id = typeof rawId === 'string' && isRequestId(rawId) ? rawId : null;
    const usage = (why: string): UpdateStart => ({ kind: 'usage', id, why });
    if (applying && settling) return usage(`${updateApplyFlag} and ${updateSettleFlag} are two starts, not one`);
    if (settling) {
        return [toFlag, requestFlag, feedFlag, awaitGoFlag].some((flag) => argv.includes(flag)) ? usage(`${updateSettleFlag} takes nothing beside it`) : { kind: 'settle' };
    }
    if (argv.indexOf(updateApplyFlag) !== argv.lastIndexOf(updateApplyFlag)) return usage(`${updateApplyFlag} is given once`);
    if (id === null) return usage(`${requestFlag} names the request: letters, digits, - and _, at most 64`);
    const to = valueOf(argv, toFlag);
    if (typeof to !== 'string' || !isVersion(to)) return usage(`${toFlag} names the version to install, such as 0.49.1`);
    const rawFeed = valueOf(argv, feedFlag);
    if (rawFeed === false) return usage(`${feedFlag} names the feed's address once`);
    const feed = rawFeed === null ? null : loopbackFeed(rawFeed);
    if (rawFeed !== null && feed === null) return usage(`${feedFlag} is an http address on the loopback address, and no other`);
    const waits = argv.indexOf(awaitGoFlag);
    if (waits >= 0 && argv.indexOf(awaitGoFlag, waits + 1) >= 0) return usage(`${awaitGoFlag} is given once`);
    return { kind: 'apply', request: waits >= 0 ? { id, to, feed, awaitGo: true } : { id, to, feed } };
}

/** What a start that could not take the lock hands the running application, in the lock's additional data. */
export function handedData(request: UpdateRequest): { update: UpdateRequest } {
    return { update: request.awaitGo === true ? { id: request.id, to: request.to, feed: request.feed, awaitGo: true } : { id: request.id, to: request.to, feed: request.feed } };
}

/**
 * Pure: whether a second start that reaches a window-less request's start asks for a window: no request handed over, no
 * update start of its own, and not a start the computer made at sign-in. Such a start finds the lock held and gets
 * nothing while the request lasts, up to `headlessLimitMs` and `goWaitMs` together, so it is remembered and the
 * application is started once the request ends short of an install (`openAfterRequest`). After an install nothing is
 * started: the installer has the application's folder, and the new version starts at the person's next open.
 */
export function asksForWindow(argv: readonly string[], data: unknown, atLogin: (argv: readonly string[]) => boolean): boolean {
    return requestOf(data) === null && updateStart(argv) === null && !atLogin(argv);
}

/** What the window-less start shows once, to a person who opened Jason Desktop while it holds the lock. */
export const updatingWords = {
    message: 'Jason is updating.',
    // Where the update installs a new version nothing opens it after the installer: that is said, not promised away.
    detail: 'Jason Desktop opens once the update is done; where it installs a new version, that version opens the next time you start Jason.',
};

/** Pure: whether the window-less start opens the application as it exits: a window was asked for, and nothing was installed. */
export function openAfterRequest(windowAsked: boolean, code: number | null): boolean {
    return windowAsked && code !== null;
}

/** The request a second start handed over, read again here and never trusted; null when there is none or it is not one. */
export function requestOf(data: unknown): UpdateRequest | null {
    if (data === null || typeof data !== 'object' || !('update' in data)) return null;
    const raw = (data as { update: unknown }).update;
    if (raw === null || typeof raw !== 'object') return null;
    const { id, to, feed, awaitGo } = raw as Record<string, unknown>;
    if (typeof id !== 'string' || !isRequestId(id) || typeof to !== 'string' || !isVersion(to)) return null;
    if (awaitGo !== undefined && awaitGo !== true) return null;
    const waits = awaitGo === true ? { awaitGo: true as const } : {};
    if (feed === null || feed === undefined) return { id, to, feed: null, ...waits };
    const loopback = typeof feed === 'string' ? loopbackFeed(feed) : null;
    return loopback === null ? null : { id, to, feed: loopback, ...waits };
}

/** Where a request's answer is written: `<data>/update/desktop-<id>.json`. */
export function requestFile(data: string, id: string): string {
    return join(data, 'update', `desktop-${id}.json`);
}

/** Where the verb's go for a request that waits for it is made: `<data>/update/desktop-<id>.go`, beside the request's file. */
export function goFile(data: string, id: string): string {
    return join(data, 'update', `desktop-${id}.go`);
}

/**
 * A request's step while it is taken (`checking`, `downloading`, and `downloaded` where it waits for the go), and its end:
 * `installing`, `current` or `refused`.
 */
export type RequestState = 'checking' | 'downloading' | 'downloaded' | 'installing' | 'current' | 'refused';

/** Why a request was refused: the updater's codes, and the ones a start decides before it reaches the updater. */
export type RequestRefusal = TakenRefusal | 'usage' | 'not_installed' | 'unsupported_platform' | 'window_feed' | 'demo_open' | 'go_there' | 'timed_out' | 'failed';

export const requestRefusals: readonly RequestRefusal[] = [
    'usage',
    'not_installed',
    'unsupported_platform',
    'window_feed',
    'demo_open',
    'go_there',
    'updater_inactive',
    'busy',
    'version_rejected',
    'check_failed',
    'version_mismatch',
    'download_failed',
    'window_open',
    'go_timed_out',
    'runtime_not_stopped',
    'keep_failed',
    'install_failed',
    'timed_out',
    'failed',
];

/** The request's file, as written: snake_case, every member always there. */
export interface RequestRecord {
    request: string;
    state: RequestState;
    /** The version of the application that took the request. */
    from: string;
    /** The version the request named; null only for a request whose version could not be read. */
    to: string | null;
    reason: RequestRefusal | null;
    /** What a person is told of a refusal, in a sentence; null otherwise. */
    message: string | null;
    /** The version the feed offered, where it was read. */
    offered: string | null;
    /** Whether a running window took the request rather than a start of its own. */
    window: boolean;
    /** Whether the runtime this application carries was stopped for it; nothing here starts it again. */
    runtime_stopped: boolean;
    /** The copy of the application kept aside under `<data>/desktop/rollback/<from>/`, where one was. */
    kept: string | null;
    updated_at: string;
}

/** The file operations a request's file is written through; the disk's unless a test names others. */
export interface RequestFileIo {
    write(file: string, text: string): void;
    rename(from: string, to: string): void;
    remove(file: string): void;
    /** Waits `ms` on this thread: the write is synchronous, and a reader holds the file for one read at most. */
    sleep(ms: number): void;
}

const diskIo: RequestFileIo = {
    write: (file, text) => writeFileSync(file, text),
    rename: (from, to) => renameSync(from, to),
    remove: (file) => rmSync(file, { force: true }),
    sleep: (ms) => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms),
};

/**
 * How long a refused rename over the request's file is tried again on Windows before the file is written in place: the
 * verb's poll holds the file for the length of one read, and Windows refuses a rename over a file another process has
 * open, whatever share mode it opened it with (EPERM; EBUSY and EACCES alike).
 */
export const renameTries = 20;
export const renamePauseMs = 25;

const refusedRename = (error: unknown): boolean => ['EPERM', 'EBUSY', 'EACCES'].includes(String((error as NodeJS.ErrnoException).code));

/**
 * Writes a request's file through a file beside it renamed over it, so a reader never meets half of it. On Windows a
 * rename the verb's read refuses is tried again for `renameTries` × `renamePauseMs`, and if still refused the file is
 * written in place: the verb's read lets that through and takes a half-written file for no answer, so the next poll reads
 * the step whole. The rename stays the normal case; the in-place write is the fallback, never the first choice. macOS
 * renames once, as before. Throws where neither way wrote it: the caller decides what an unwritten step means.
 */
export function writeRequestFile(file: string, record: RequestRecord, io: RequestFileIo = diskIo, platform: NodeJS.Platform = process.platform): 'renamed' | 'in_place' {
    mkdirSync(dirname(file), { recursive: true });
    const text = `${JSON.stringify(record, null, 2)}\n`;
    const temporary = `${file}.${process.pid}.tmp`;
    try {
        io.write(temporary, text);
    } catch (error) {
        io.remove(temporary);
        throw error;
    }
    for (let attempt = 1; ; attempt++) {
        try {
            io.rename(temporary, file);
            return 'renamed';
        } catch (error) {
            if (platform !== 'win32' || !refusedRename(error)) {
                io.remove(temporary);
                throw error;
            }
            if (attempt >= renameTries) break;
            io.sleep(renamePauseMs);
        }
    }
    io.remove(temporary);
    io.write(file, text);
    return 'in_place';
}

/**
 * Makes a request's file, its first step in it, only where no file of that name is there: the name is claimed by an
 * exclusive create, then written as `writeRequestFile` writes, so a reader meets nothing, an empty file or a whole one.
 * Returns false, writing nothing, when the file was already there: another request's, or one the verb did not make.
 */
export function createRequestFile(file: string, record: RequestRecord): boolean {
    mkdirSync(dirname(file), { recursive: true });
    try {
        closeSync(openSync(file, 'wx'));
    } catch (error) {
        if ((error as NodeJS.ErrnoException).code === 'EEXIST') return false;
        throw error;
    }
    writeRequestFile(file, record);
    return true;
}

/** A request's file as written, or null where there is none or it cannot be read. */
export function readRequestFile(file: string): RequestRecord | null {
    try {
        return JSON.parse(readFileSync(file, 'utf8')) as RequestRecord;
    } catch {
        return null;
    }
}

/** Where this application cannot take a request at all: a development build, or a system it is not updated on so. */
export function requestSupport(packaged: boolean, platform: NodeJS.Platform): 'not_installed' | 'unsupported_platform' | null {
    if (!packaged) return 'not_installed';
    return platform === 'win32' || platform === 'darwin' ? null : 'unsupported_platform';
}

/** What the person at the terminal is told of a refusal. */
export function refusalMessage(
    reason: RequestRefusal,
    at: { to: string | null; from: string; offered: string | null; runtimeStopped: boolean; kept: string | null; menu: boolean; why?: string; runtime?: StoppedShort; limitMs?: number },
): string {
    const to = at.to ?? 'the version asked for';
    const stopped = at.runtimeStopped ? ' The runtime this application carries was stopped for the update: jason runtime start starts it again.' : '';
    const unchanged = at.runtimeStopped ? stopped : ' Nothing was changed.';
    switch (reason) {
        case 'usage':
            return `Jason Desktop could not read the update request: ${at.why ?? 'its arguments are not the ones it takes'}.`;
        case 'not_installed':
            return 'This Jason Desktop is a development build, which no installer updates, so nothing was changed.';
        case 'unsupported_platform':
            return 'On this system Jason Desktop takes no update request from a terminal: update it from its own menu, or through the package it came in. Nothing was changed.';
        case 'window_feed':
            return 'Jason Desktop is open, and an open window updates from the releases alone, never from a feed a terminal names, so nothing was checked or downloaded. Quit Jason Desktop and run jason update apply again.';
        case 'demo_open':
            return `Jason Desktop is showing the Demo, which takes no update, so nothing was checked, stopped or changed. Choose ${backLabel} in its Jason menu, or quit Jason Desktop, then run jason update apply again.`;
        case 'go_there':
            return 'A go for this request was made before Jason Desktop had downloaded the update and asked for one, so it is no go the verb made: nothing was stopped, kept or installed. Run jason update apply again.';
        case 'go_timed_out':
            return `Jason ${to} was downloaded, and jason update apply did not say go within ${goWaitMs / 60_000} minutes, so nothing was stopped, kept or installed. ${
                at.menu ? "The download waits under the install item of Jason Desktop's menu; run jason update apply again." : "Run jason update apply again, or choose Check for Updates in Jason Desktop's menu once it is open."
            }`;
        case 'updater_inactive':
            return 'Jason Desktop’s updater cannot replace the application where it is installed, so nothing was changed.';
        case 'busy':
            return 'Jason Desktop is already checking for, downloading or installing an update, so this request was not taken. Run jason update apply again once that has finished.';
        case 'version_rejected':
            return `Jason ${to} failed its first start on this machine before, so Jason Desktop does not install it again. Nothing was changed.`;
        case 'check_failed':
            return 'Jason Desktop could not read its update feed, so nothing was changed.';
        case 'version_mismatch':
            return at.offered === null || at.offered === at.from
                ? `The update feed offers no Jason newer than ${at.from}, while Jason ${to} was asked for, so nothing was changed.`
                : `The update feed offers Jason ${at.offered}, not the Jason ${to} that was asked for, so nothing was changed. Run jason update apply again to plan from what the feeds say now.`;
        case 'download_failed':
            return `Jason ${to} could not be downloaded, so nothing was changed.`;
        case 'window_open':
            return at.menu
                ? `Jason Desktop is open, so Jason ${to} was downloaded and waits there: choose Restart Jason and Its Runtime to Install ${to} in its Jason menu, or quit Jason Desktop and run jason update apply again.`
                : `Jason Desktop is open, so Jason ${to} was downloaded and not installed: quit Jason Desktop and run jason update apply again.`;
        case 'runtime_not_stopped': {
            const runtime = at.runtime;
            if (runtime?.demo) return `Jason ${to} was not installed, because ${demoRuntimeWords}. Nothing was replaced. ${demoRuntimeNext}`;
            if (runtime?.kind === 'unanswered') {
                const { why, next } = unansweredWords(runtime);
                return `Jason ${to} was not installed, because ${why}. Nothing was replaced. ${next}`;
            }
            const how = runtime === undefined || runtime.exit === -1 ? 'jason runtime stop did not finish' : `jason runtime stop exited ${runtime.exit}`;
            return `Jason ${to} was not installed, because the runtime this application carries did not stop (${how}). Nothing was replaced. Stop the runtime with jason runtime stop, then run jason update apply again.`;
        }
        case 'keep_failed':
            return `Jason ${to} was not installed, because a copy of this version could not be kept aside to go back to.${unchanged}`;
        case 'install_failed':
            return `The installer stopped before Jason ${to} was installed.${stopped}${at.kept === null ? '' : ` A copy of this version is kept at ${at.kept}.`}`;
        case 'timed_out':
            return `Jason Desktop did not finish the update within ${(at.limitMs ?? headlessLimitMs) / 60_000} minutes and stopped waiting. If it had begun installing, the application may still be replaced once it has ended: jason --version says which version runs.`;
        case 'failed':
            return `Jason Desktop met an error it did not expect while it took the update request, and ended.${stopped}`;
    }
}

/**
 * What a window-less start on Windows sets in its own environment before electron-updater runs anything: `ComSpec`, the
 * command interpreter Node's `execFile` runs when its shell option is on, as the system's own, and the system's folders
 * first on `PATH`. The pinned electron-updater checks an installer's publisher by running PowerShell through that
 * interpreter, and lets the check pass where PowerShell cannot be run at all (`windowsExecutableCodeSignatureVerifier.js`,
 * `handleError`), so the interpreter and the PowerShell it finds are not the caller's to choose. The working directory
 * stays the caller's, and the interpreter looks there before `PATH` unless `NoDefaultCurrentDirectoryInExePath` is set
 * (Microsoft's `NeedCurrentDirectoryForExePath`: "the existence of the NoDefaultCurrentDirectoryInExePath environment
 * variable is checked, and not its value"), so it is set. The system's folder is `SystemRoot`'s, whose value is the
 * caller's too: this closes the working directory and the order of `PATH`, not a `SystemRoot` the caller pointed
 * elsewhere. Null elsewhere, and where `SystemRoot` is not a drive's absolute path.
 */
export function systemShell(env: Readonly<Record<string, string | undefined>>, platform: NodeJS.Platform): Record<string, string> | null {
    if (platform !== 'win32') return null;
    const valueOf = (name: string): string | undefined => env[Object.keys(env).find((key) => key.toLowerCase() === name.toLowerCase()) ?? name];
    const root = valueOf('SystemRoot');
    if (root === undefined || !/^[A-Za-z]:\\[^;"%]*[^;"%\\]$/.test(root)) return null;
    const system = `${root}\\System32`;
    const folders = [system, root, `${system}\\Wbem`, `${system}\\WindowsPowerShell\\v1.0`];
    const rest = valueOf('PATH');
    return {
        ComSpec: `${system}\\cmd.exe`,
        PATH: [...folders, ...(rest === undefined || rest === '' ? [] : [rest])].join(';'),
        NoDefaultCurrentDirectoryInExePath: '1',
    };
}

/**
 * Where requests wait for the run that takes them, one at a time: one that arrives before a run serves them is held, and
 * one that arrives while another is held or taken is refused as `busy`, by `refuseBusy`.
 */
export class UpdateRequests {
    #serve: ((request: UpdateRequest) => Promise<unknown>) | null = null;
    #held: UpdateRequest | null = null;
    #busy = false;
    readonly #refuseBusy: (request: UpdateRequest) => void;

    constructor(refuseBusy: (request: UpdateRequest) => void) {
        this.#refuseBusy = refuseBusy;
    }

    arrived(request: UpdateRequest): void {
        if (this.#busy || this.#held !== null) {
            this.#refuseBusy(request);
            return;
        }
        if (this.#serve === null) this.#held = request;
        else this.#run(request);
    }

    serve(serve: (request: UpdateRequest) => Promise<unknown>): void {
        this.#serve = serve;
        const held = this.#held;
        this.#held = null;
        if (held !== null) this.#run(held);
    }

    #run(request: UpdateRequest): void {
        const serve = this.#serve;
        if (serve === null) return;
        this.#busy = true;
        void serve(request)
            .catch(() => undefined)
            .finally(() => {
                this.#busy = false;
            });
    }
}

export interface TakeDeps {
    /** The data directory the request's file is written under: the one this application runs on. */
    data: string;
    /** This application's version. */
    from: string;
    /**
     * Where the request is taken: by a start of its own, or by a running window, which has an install item or not, and
     * shows the Demo or not. A window that shows the Demo resolves no host name in its run (`demoResolverRule`), so no
     * feed could be read there: it refuses the request as `demo_open` before any updater is made.
     */
    where: 'headless' | { window: { menu: boolean; demo: boolean } };
    /** Why this application takes no request at all (`requestSupport`), or null. */
    unsupported: 'not_installed' | 'unsupported_platform' | null;
    /** The updater the request is taken by, made only when one is needed. */
    updater(): Pick<Updater, 'take'>;
    /** The switch, read by a window only. */
    windowOpen: WindowOpenUpdate;
    log: Logger;
    /** Hears the end an install that failed late wrote (`TakenHow.late`). */
    late?(record: RequestRecord): void;
    /** Bounds the whole request, a window-less start's alone; hears the record it wrote when the bound passed. */
    limit?: { ms: number; timers: Timers; expired(record: RequestRecord): void };
    write?(file: string, record: RequestRecord): void;
    /** Makes the request's file with its first step; false when one was already there (`createRequestFile`). */
    create?(file: string, record: RequestRecord): boolean;
    /** How a request that waits for the go looks for it: the file, `goWaitMs` and `goPollMs` unless a test says. */
    go?: GoWait;
    now?(): Date;
    /**
     * Starts the runtime the application carries again (`jason runtime start`), and says what came of it. Asked only where
     * the updater stopped the runtime and the request's file was then told nothing of it, the refusal unwritten: the verb,
     * whose part restarting it is, never learns it was stopped, so the desktop does it at once. Where the refusal with
     * `runtime_stopped` was written, it stays the verb's.
     */
    startRuntime?(): Promise<'started' | 'answering' | 'not_started'>;
}

/**
 * How the go is waited for: whether its file is there, how long and how often it is looked for, and a pause; and when a
 * file was last written (`made`), where it is read, so that a go older than the request's own `downloaded` is told apart.
 */
export interface GoWait {
    exists(file: string): boolean;
    ms: number;
    pollMs: number;
    sleep(ms: number): Promise<void>;
    made?(file: string): number | null;
}

/** When a file was last written, in milliseconds, or null where it cannot be read. */
function madeAt(file: string): number | null {
    try {
        return statSync(file).mtimeMs;
    } catch {
        return null;
    }
}

const goWait: GoWait = { exists: existsSync, ms: goWaitMs, pollMs: goPollMs, sleep: (ms) => new Promise((resolve) => setTimeout(resolve, ms)), made: madeAt };

/**
 * Waits for the go file of a request: true once it is there, false when the wait ran out or `over` says the request
 * ended meanwhile (its bound passed).
 */
export async function waitForGo(file: string, wait: GoWait, over: () => boolean): Promise<boolean> {
    for (let waited = 0; waited < wait.ms; waited += wait.pollMs) {
        if (over()) return false;
        if (wait.exists(file)) return true;
        await wait.sleep(wait.pollMs);
    }
    return !over() && wait.exists(file);
}

/**
 * Takes one request to its end: its file made with the first step, each step written before it is taken, the end last.
 * Returns the end it wrote, or null, having written and taken nothing, when the request's file was already there: that
 * file is another's, and is never written over. A file that cannot be made refuses the request as `failed`, taken no
 * further and written nowhere; a step that cannot be written after it was made is logged and the request goes on.
 */
export async function takeRequest(request: UpdateRequest, deps: TakeDeps): Promise<RequestRecord | null> {
    const file = requestFile(deps.data, request.id);
    const write = deps.write ?? writeRequestFile;
    const create = deps.create ?? createRequestFile;
    const window = deps.where !== 'headless';
    const menu = deps.where !== 'headless' && deps.where.window.menu;
    let last: RequestRecord = {
        request: request.id,
        state: 'checking',
        from: deps.from,
        to: request.to,
        reason: null,
        message: null,
        offered: null,
        window,
        runtime_stopped: false,
        kept: null,
        updated_at: '',
    };
    let ended = false;
    // Whether the last step put reached the file: a step that did not is never passed over in silence (`progress`).
    let written = true;
    const put = (next: Partial<RequestRecord>): RequestRecord => {
        last = { ...last, ...next, updated_at: (deps.now?.() ?? new Date()).toISOString() };
        try {
            write(file, last);
            written = true;
        } catch {
            written = false;
            deps.log.warn('update_request_unwritten', { State: last.state });
        }
        return last;
    };
    const refuse = (reason: RequestRefusal, at: Partial<RequestRecord> = {}, runtime?: StoppedShort): RequestRecord => {
        const merged = { ...last, ...at };
        const message = refusalMessage(reason, {
            to: merged.to,
            from: merged.from,
            offered: merged.offered,
            runtimeStopped: merged.runtime_stopped,
            kept: merged.kept,
            menu,
            ...(runtime === undefined ? {} : { runtime }),
            ...(deps.limit === undefined ? {} : { limitMs: deps.limit.ms }),
        });
        return put({ ...at, state: 'refused', reason, message });
    };
    const end = (record: RequestRecord): RequestRecord => {
        deps.log.info('update_request_ended', { To: request.to, State: record.state, Code: record.reason ?? 'none' });
        return record;
    };
    last = { ...last, updated_at: (deps.now?.() ?? new Date()).toISOString() };
    try {
        if (!create(file, last)) {
            deps.log.warn('update_request_file_there', { To: request.to });
            return null;
        }
    } catch {
        // No file can answer for this request (`<data>/update` a file, say): it is refused and taken no further, its end
        // written nowhere, and a window-less start exits 1.
        deps.log.warn('update_request_unwritten', { State: last.state });
        const message = refusalMessage('failed', { to: last.to, from: last.from, offered: null, runtimeStopped: false, kept: null, menu });
        return end({ ...last, state: 'refused', reason: 'failed', message });
    }
    deps.log.info('update_request_taken', { To: request.to, Where: window ? 'window' : 'headless' });
    // A window takes the releases' feed alone: a feed a terminal names is refused before anything is checked or set.
    if (window && request.feed !== null) {
        ended = true;
        return end(refuse('window_feed'));
    }
    if (deps.unsupported !== null) {
        ended = true;
        return end(refuse(deps.unsupported));
    }
    // A window that shows the Demo reads no feed (`demoResolverRule`): it says so, and nothing is checked, stopped or kept.
    if (deps.where !== 'headless' && deps.where.window.demo) {
        ended = true;
        return end(refuse('demo_open'));
    }
    // A go that is there before the request was taken is none: the verb makes its go only once it has read `downloaded`.
    const go = goFile(deps.data, request.id);
    const waiting = deps.go ?? goWait;
    if (request.awaitGo === true && waiting.exists(go)) {
        ended = true;
        return end(refuse('go_there'));
    }
    let handle: unknown = null;
    if (deps.limit !== undefined) {
        const { limit } = deps;
        handle = limit.timers.setTimeout(() => {
            if (ended && last.state !== 'installing') return;
            ended = true;
            limit.expired(end(refuse('timed_out')));
        }, limit.ms);
    }
    const clear = (): void => {
        if (handle !== null) deps.limit?.timers.clearTimeout(handle);
        handle = null;
    };
    // When the request's file said `downloaded`, as the file system dates it: the verb makes its go only after it read that,
    // so a go dated before it is one the verb did not make.
    let downloadedAt: number | null = null;
    let early = false;
    // Answers whether the step is on disk. Two steps the verb acts on are dangerous to lose, and the updater goes no
    // further where they are not written: `installing`, which the verb reads before it lets the installer have the
    // application, and `downloaded`, the one the verb gives its go after. `checking` and `downloading` say only where the
    // request stands; a refusal that cannot be written is the end in any case, the verb then seeing no answer.
    const progress = (step: TakenStep): boolean => {
        if (ended) return false;
        if (step.step === 'checking') put({ state: 'checking' });
        else if (step.step === 'downloading') put({ state: 'downloading', offered: step.offered });
        else if (step.step === 'downloaded') {
            put({ state: 'downloaded', offered: request.to });
            if (!written) return false;
            downloadedAt = waiting.made?.(file) ?? null;
        } else {
            put({ state: 'installing', offered: request.to, runtime_stopped: step.runtimeStopped, kept: step.kept });
            return written;
        }
        return true;
    };
    const awaitGo = async (): Promise<boolean> => {
        if (!(await waitForGo(go, waiting, () => ended))) return false;
        const made = waiting.made?.(go) ?? null;
        if (downloadedAt !== null && made !== null && made < downloadedAt) {
            early = true;
            return false;
        }
        return true;
    };
    const taken: TakenRequest = { to: request.to, feed: request.feed };
    let outcome: TakenOutcome;
    try {
        outcome = await deps.updater().take(taken, {
            install: !window || deps.windowOpen === 'close',
            // Only a request that names --await-go waits for the verb's go; every other request installs as before.
            ...(request.awaitGo === true ? { go: awaitGo } : {}),
            late: () => {
                if (ended && last.state !== 'installing') return;
                ended = true;
                clear();
                const record = end(refuse('install_failed'));
                deps.late?.(record);
            },
        }, progress);
    } catch {
        clear();
        ended = true;
        return end(refuse('failed'));
    }
    if (outcome.state === 'installing') {
        // The install's own end is the process quitting, or a late error; the bound keeps running for a window-less start.
        ended = true;
        return end(last.state === 'installing' ? last : put({ state: 'installing', offered: outcome.offered, runtime_stopped: outcome.runtimeStopped, kept: outcome.kept }));
    }
    clear();
    ended = true;
    if (outcome.state === 'current') return end(put({ state: 'current' }));
    // A go older than `downloaded` stopped the request where no go would have: nothing was stopped, kept or installed.
    const reason = early && outcome.reason === 'go_timed_out' ? 'go_there' : outcome.reason === 'step_unwritten' ? 'failed' : outcome.reason;
    const record = refuse(reason, { offered: outcome.offered, runtime_stopped: outcome.runtimeStopped, kept: outcome.kept }, outcome.runtime);
    // One rule, answered before and not after: a runtime the updater stopped is started again by whoever is told it was. The
    // verb is told by the file; where the file could be told nothing, the desktop is the only one that knows, and does it.
    if (!written && outcome.runtimeStopped && deps.startRuntime !== undefined) {
        const started = await deps.startRuntime().catch(() => 'not_started' as const);
        deps.log.warn('update_request_runtime_started', { Outcome: started });
    }
    return end(record);
}

/**
 * The exit of a window-less start once its request ended: none while the installer has it (it quits), 1 for an end it did
 * not expect, 2 for a request whose file was already there, else 0.
 */
export function applyExit(record: RequestRecord | null): number | null {
    if (record === null) return applyExits.usage;
    if (record.state === 'installing') return null;
    return record.reason === 'failed' || record.reason === 'timed_out' ? 1 : 0;
}

/** The exits a start of `--update-apply` makes: the file names the end, the arguments were not read, the request was handed on, or it failed. */
export const applyExits = { answered: 0, failed: 1, usage: 2, handedOver: 3 } as const;

/** The end a start whose arguments were not read writes, where the id was read; it reaches no updater. */
export function usageRecord(id: string, why: string, from: string, now: Date = new Date()): RequestRecord {
    return {
        request: id,
        state: 'refused',
        from,
        to: null,
        reason: 'usage',
        message: refusalMessage('usage', { to: null, from, offered: null, runtimeStopped: false, kept: null, menu: false, why }),
        offered: null,
        window: false,
        runtime_stopped: false,
        kept: null,
        updated_at: now.toISOString(),
    };
}

/** The end of a request a running application refused because it was taking another: written by that application. */
export function busyRecord(request: UpdateRequest, from: string, window: boolean, now: Date = new Date()): RequestRecord {
    return {
        request: request.id,
        state: 'refused',
        from,
        to: request.to,
        reason: 'busy',
        message: refusalMessage('busy', { to: request.to, from, offered: null, runtimeStopped: false, kept: null, menu: false }),
        offered: null,
        window,
        runtime_stopped: false,
        kept: null,
        updated_at: now.toISOString(),
    };
}
