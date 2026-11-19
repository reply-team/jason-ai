/**
 * The updater: electron-updater reading this repository's GitHub releases on the stable channel only (a release with a
 * pre-release part is never offered, no channel is set, nothing is downgraded). It looks on the back-off `backoff.ts`
 * computes, offers what it found, downloads it only when the person says so and installs it only when the person says so
 * again, after the runtime this application carries, when it is the one running, has stopped (`own-runtime.ts`) and the
 * application's own folder has then been kept aside (`rollback.ts`): the installer replaces the executable the runtime
 * runs from, and the first start of the new version starts the runtime again from the new one. A runtime that does not
 * stop, or one that did not answer while the process its run file names still runs, keeps the update from being
 * installed with the copy an earlier update kept left as it was, and the person is told why. electron-updater is handed
 * in, so nothing here needs Electron to be tested.
 *
 * `quitAndInstall` starts the downloaded installer from inside `node_modules` (electron-updater's own spawn), the one
 * start the desktop makes that the process guard cannot see. When its install step fails it does not quit: the pinned
 * electron-updater's `BaseUpdater.quitAndInstall` quits only when `install()` returns true, and every way `install()`
 * returns false dispatches an `error` event first, synchronously (`BaseUpdater.install`, `AppImageUpdater.doInstall`,
 * `DebUpdater.doInstall`), but one: a call after an earlier one returned true (`quitAndInstallCalled` stays set), which only
 * `autoInstallOnAppQuit` or a second `quitAndInstall` makes. This desktop makes neither: the first is off, and it calls
 * `quitAndInstall` once per download, from the one `downloaded` state. That event is what tells the desktop to come back.
 *
 * On Linux electron-updater is active only where it can replace what runs: the AppImage, which it knows by the
 * `APPIMAGE` variable (`AppImageUpdater.isUpdaterActive`), and the deb. Anywhere else the updater looks for nothing and
 * its item says updates come through the package.
 */

import { nextCheckDelay } from './backoff.ts';
import { itemKeys, keyWords } from './keybindings.ts';
import type { Timers } from './attention.ts';
import type { Logger } from './log.ts';
import { demoRuntimeNext, demoRuntimeWords, unansweredWords, type StopOutcome, type Unanswered } from './own-runtime.ts';
import { isRejected, keepPrevious, readRollback } from './rollback.ts';

/** The deb a release publishes, as its packages are named. */
export const debFile = 'jason-desktop-linux-x64.deb';

/** Where releases are read: this repository's GitHub releases. */
export const feed = { provider: 'github', owner: 'reply-team', repo: 'jason-ai' } as const;

/** The product's own address in front of the releases, its releases index: every release request passes it. */
const releasesIndex = 'https://get.jasonharness.ai/releases/';

/** A release's page, by its tag: where its installers and packages are downloaded from. */
export function releasePage(version: string): string {
    return `${releasesIndex}tag/v${version}`;
}

/** The page where going back from a deb update finds the previous release's deb. */
export function previousRelease(version: string): string {
    return releasePage(version);
}

/** The first look waits half a minute after the start, so the start itself is never slowed by it. */
export const firstCheckMs = 30_000;

/** electron-updater's autoUpdater, as far as the desktop uses it. */
export interface FeedUpdater {
    autoDownload: boolean;
    autoInstallOnAppQuit: boolean;
    allowPrerelease: boolean;
    allowDowngrade: boolean;
    logger: unknown;
    setFeedURL(options: typeof feed): void;
    checkForUpdates(): Promise<{ isUpdateAvailable: boolean; updateInfo: { version: string } } | null>;
    downloadUpdate(): Promise<unknown>;
    quitAndInstall(): void;
    /** False where electron-updater cannot replace what runs; then it looks for nothing. */
    isUpdaterActive(): boolean;
    on(event: 'error', listener: (error: Error) => void): unknown;
    removeListener(event: 'error', listener: (error: Error) => void): unknown;
}

export type UpdateState =
    | { step: 'idle' }
    | { step: 'checking' }
    | { step: 'offered'; version: string }
    | { step: 'downloading'; version: string }
    | { step: 'downloaded'; version: string }
    | { step: 'installing'; version: string };

export interface MenuItem {
    label: string;
    enabled: boolean;
    act: 'check' | 'download' | 'install' | null;
}

/**
 * The one menu item the updater shows: the next step the person can take, or what is under way; in a build that is not
 * installed none, and where electron-updater is not active (`FeedUpdater.isUpdaterActive`) none either.
 */
export function menuItemOf(state: UpdateState, installed: boolean, active: boolean): MenuItem {
    if (!installed) return { label: 'Updates Come to an Installed Build', enabled: false, act: null };
    if (!active) return { label: 'Updates Come Through the Package', enabled: false, act: null };
    switch (state.step) {
        case 'idle':
            return { label: 'Check for Updates…', enabled: true, act: 'check' };
        case 'checking':
            return { label: 'Checking for Updates…', enabled: false, act: null };
        case 'offered':
            return { label: `Download Jason ${state.version}`, enabled: true, act: 'download' };
        case 'downloading':
            return { label: `Downloading Jason ${state.version}…`, enabled: false, act: null };
        case 'downloaded':
            return { label: `Restart Jason and Its Runtime to Install ${state.version}`, enabled: true, act: 'install' };
        case 'installing':
            return { label: `Installing Jason ${state.version}…`, enabled: false, act: null };
    }
}

/**
 * A press of the update item, on the act of the step its menu showed. The key acts as a click does but for the install:
 * a hand on the key sees no label, and the install stops the runtime, which ends a running turn and the work in
 * progress, and restarts the desktop, so the key asks first (`installQuestion`; `ask` answers the index of the button
 * chosen) and installs only on yes; a click on the item, whose label says so, installs at once. The question reads the
 * step again, so a press the menu showed before the step moved on asks nothing. A check is the person's: it answers the
 * sentence that says what it found (`checkWords`), null when no look was made; every other act answers null.
 */
export async function pressItem(
    updates: Pick<Updater, 'current' | 'check' | 'download' | 'install'>,
    act: MenuItem['act'],
    byKey: boolean,
    ask: (question: ReturnType<typeof installQuestion>) => Promise<number>,
): Promise<string | null> {
    if (act === 'check') return updates.check(true);
    if (act === 'download') await updates.download();
    else if (act === 'install' && !byKey) await updates.install();
    else if (act === 'install') {
        const state = updates.current;
        if (state.step !== 'downloaded') return null;
        if ((await ask(installQuestion(state.version))) === 0) await updates.install();
    }
    return null;
}

/** Why a check failed, by the kind of failure; the error's own text is never shown or logged. */
export type CheckFailure = 'offline' | 'refused' | 'feed' | 'other';

/** What a check the person asked for found. */
export type CheckFound =
    | { kind: 'latest'; version: string }
    | { kind: 'available'; version: string }
    | { kind: 'held'; version: string }
    | { kind: 'failed'; failure: CheckFailure };

/** Electron's and Node's words for a connection that was not made, or not answered in time. */
const noConnection =
    /net::ERR_(INTERNET_DISCONNECTED|NAME_NOT_RESOLVED|NAME_RESOLUTION_FAILED|CONNECTION_[A-Z_]+|TIMED_OUT|NETWORK_[A-Z_]+|ADDRESS_UNREACHABLE|PROXY_CONNECTION_FAILED|TUNNEL_CONNECTION_FAILED)\b|\b(ENOTFOUND|EAI_AGAIN|ECONNREFUSED|ECONNRESET|ETIMEDOUT|ENETUNREACH|EHOSTUNREACH|ENETDOWN)\b|^Request timed out$/m;

/**
 * Pure: the kind of a check's failure, read from what electron-updater threw: an Electron or Node connection error, also
 * inside electron-updater's own wrapping of a read; an HTTP 403 or 429 (`HttpError`, its `statusCode`, or the same inside
 * a wrapping), a server refusing for now; any other HTTP status or electron-updater's own `ERR_UPDATER_` codes, a list
 * of releases that could not be read; anything else.
 */
export function checkFailureOf(error: unknown): CheckFailure {
    if (typeof error !== 'object' || error === null) return 'other';
    const { message, code, statusCode } = error as { message?: unknown; code?: unknown; statusCode?: unknown };
    const words = typeof message === 'string' ? message : '';
    if (noConnection.test(words) || (typeof code === 'string' && noConnection.test(code))) return 'offline';
    if (statusCode === 403 || statusCode === 429 || /\bHttpError: (403|429)\b/.test(words)) return 'refused';
    if (typeof statusCode === 'number' || (typeof code === 'string' && code.startsWith('ERR_UPDATER_'))) return 'feed';
    return 'other';
}

/** The reason a failed check gives, one per kind of failure. */
const failureWords: Record<CheckFailure, string> = {
    offline: 'there is no connection to where releases are published.',
    refused: 'the release server is refusing requests for now. Try again later.',
    feed: 'the list of releases could not be read.',
    other: 'something unexpected went wrong. Try again later.',
};

/** Pure: the sentence that says what a check the person asked for found. */
export function checkWords(found: CheckFound): string {
    switch (found.kind) {
        case 'latest':
            return `Jason Desktop ${found.version} is the latest version.`;
        case 'available':
            return `Version ${found.version} is available.`;
        case 'held':
            return `Version ${found.version} is out. It is not offered here because you went back from it.`;
        case 'failed':
            return `Could not check for updates: ${failureWords[found.failure]}`;
    }
}

/** Pure: the notification that repeats the sentence when the check was asked for outside Settings, whose menu closes. */
export function updateNotice(said: string): { title: string; body: string } {
    return { title: 'Jason Desktop', body: said };
}

/** Pure: the question the update item's key asks before it installs, naming the version; its default does nothing. */
export function installQuestion(version: string): { type: 'question'; message: string; detail: string; buttons: string[]; defaultId: number; cancelId: number } {
    return {
        type: 'question',
        message: `Restart Jason and its runtime to install Jason ${version}?`,
        detail: 'The runtime stops first, which ends a running turn and the work in progress, and Jason starts again on the new version.',
        buttons: ['Restart and Install', 'Not Now'],
        defaultId: 1,
        cancelId: 1,
    };
}

/**
 * Pure: the update item's key as a person reads it, where the item can act; null where it says "Updates Come to an
 * Installed Build" or "Updates Come Through the Package" (`menuItemOf`), where the key would do nothing, so no sentence
 * names it there.
 */
export function updateKeyWords(installed: boolean, active: boolean, platform: NodeJS.Platform): string | null {
    return installed && active ? keyWords(itemKeys.update, platform) : null;
}

/** What the person is told when the runtime this application carries did not stop, or did not answer, so the update was not installed. */
export function runtimeRefusal(outcome: { kind: 'failed'; exit: number; demo?: true } | Unanswered): string {
    const again = 'Then choose Restart Jason and Its Runtime to Install again.';
    if (outcome.demo) return `The update was not installed, because ${demoRuntimeWords}. Jason was left as it was. ${demoRuntimeNext} ${again}`;
    if (outcome.kind === 'unanswered') {
        const { why, next } = unansweredWords(outcome);
        return `The update was not installed, because ${why}. Jason was left as it was. ${next} ${again}`;
    }
    const how = outcome.exit === -1 ? 'jason runtime stop did not finish' : `jason runtime stop exited ${outcome.exit}`;
    return `The update was not installed, because the runtime this application carries did not stop (${how}). Jason was left as it was. Stop the runtime with jason runtime stop. ${again}`;
}

/**
 * What the person is told when the application could not be kept aside, after its runtime was stopped or with none to
 * stop, and what they can do instead: install the new version from its release's page as they installed this one, which
 * keeps no copy.
 */
export function keepRefusal(stopped: boolean, version: string): string {
    const why = 'The update was not installed, because a copy of this version could not be kept aside to go back to.';
    const left = stopped ? 'The runtime this application carries was stopped for the update: jason runtime start starts it again.' : 'Jason was left as it was.';
    return `${why} ${left} You can install the new version yourself: download it for your system from ${releasePage(version)} and install it as you installed this one.`;
}

/** What the person is told when the installer stopped before it finished, so Jason stayed open. */
export function installRefusal(stopped: boolean, kept: string | null): string {
    return [
        'The update was not installed: the installer stopped before it finished, and Jason stayed open.',
        ...(stopped ? ['The runtime this application carries was stopped for the update: jason runtime start starts it again.'] : []),
        ...(kept === null ? [] : [`A copy of this version is kept at ${kept}.`]),
        'Choose Restart Jason and Its Runtime to Install to try again.',
    ].join(' ');
}

export interface UpdaterDeps {
    updater: FeedUpdater;
    /** The application's own version. */
    version: string;
    /**
     * What is kept aside to go back to: the folder the application is installed as (`applicationFolder`), the AppImage
     * file on Linux, or null for the deb, whose previous release is installed again from its package instead.
     */
    application: string | null;
    /** `<data>/desktop/rollback`. */
    rollback: string;
    log: Logger;
    timers?: Timers;
    /** Stops the runtime this application carries when it is the one running (`stopOwnRuntime`), and says what came of it. */
    stopRuntime(): Promise<StopOutcome>;
    /** Tells the person why the update was not installed. */
    refused(why: string): void;
    /** Lets the window and the tray go before the installer runs. */
    beforeInstall(): Promise<void>;
    /** Brings back what `beforeInstall` let go, when the installer stopped before it finished and Jason stayed open. */
    afterRefused(): Promise<void>;
    changed(state: UpdateState): void;
}

export class Updater {
    private readonly deps: UpdaterDeps;
    private readonly timers: Timers;
    private state: UpdateState = { step: 'idle' };
    /** What the person's last check found, and the state it was said at; in memory only, gone at a restart. */
    private line: { said: string; failed: boolean; at: UpdateState } | null = null;
    private failures = 0;
    private downloadFailures = 0;
    private handle: unknown = null;

    constructor(deps: UpdaterDeps) {
        this.deps = deps;
        this.timers = deps.timers ?? { setTimeout: (run, ms) => setTimeout(run, ms), clearTimeout: (handle) => clearTimeout(handle as NodeJS.Timeout) };
    }

    get current(): UpdateState {
        return this.state;
    }

    /**
     * What the person's last check found, as the item's answer carries it: replaced by their next check, cleared when the
     * item moves to another step, and a failure cleared by a look on the cadence that succeeds. A look on the cadence
     * never writes one.
     */
    get said(): string | null {
        return this.line?.said ?? null;
    }

    /** Whether electron-updater can replace what runs here; read from it each time. */
    get active(): boolean {
        return this.deps.updater.isUpdaterActive();
    }

    start(): void {
        const { updater } = this.deps;
        updater.autoDownload = false;
        updater.autoInstallOnAppQuit = false;
        updater.allowPrerelease = false;
        updater.allowDowngrade = false;
        updater.logger = null;
        updater.setFeedURL(feed);
        if (this.active) this.schedule(firstCheckMs);
    }

    stop(): void {
        if (this.handle !== null) this.timers.clearTimeout(this.handle);
        this.handle = null;
    }

    /**
     * One look at the releases; on the cadence, or when the person asks (`asked`). A look the person asked for answers the
     * sentence that says what it found and keeps it as the item's line (`said`); one on the cadence answers null and writes
     * none. A look that is not made, where electron-updater is not active or while a step is under way, answers null.
     */
    async check(asked = false): Promise<string | null> {
        if (!this.active) return null;
        if (this.state.step === 'downloaded' || this.state.step === 'installing') return null;
        if (this.state.step !== 'idle' && this.state.step !== 'offered') {
            this.schedule(nextCheckDelay(this.failures));
            return null;
        }
        const before = this.state;
        if (asked) this.line = null;
        this.show({ step: 'checking' });
        let found: CheckFound;
        let after: UpdateState;
        try {
            const result = await this.deps.updater.checkForUpdates();
            const version = result?.updateInfo.version;
            const newer = result?.isUpdateAvailable === true && version !== undefined && version !== this.deps.version;
            const held = newer && isRejected(readRollback(this.deps.rollback), version);
            this.failures = 0;
            if (newer && !held) {
                this.deps.log.info('update_offered', { Version: version });
                after = { step: 'offered', version };
                found = { kind: 'available', version };
            } else {
                this.deps.log.info('update_checked', { Available: false });
                after = { step: 'idle' };
                found = held ? { kind: 'held', version } : { kind: 'latest', version: this.deps.version };
            }
            // A look on the cadence that succeeds: "could not check" is no longer true.
            if (!asked && this.line?.failed === true) this.line = null;
        } catch (error) {
            this.failures += 1;
            this.deps.log.warn('update_failed', { Step: 'check', Failures: this.failures });
            after = before;
            found = { kind: 'failed', failure: checkFailureOf(error) };
        }
        const said = asked ? checkWords(found) : null;
        if (said !== null) this.line = { said, failed: found.kind === 'failed', at: after };
        this.show(after);
        this.schedule(nextCheckDelay(this.failures));
        return said;
    }

    /**
     * Downloads what was offered: only on the person's word.
     *
     * electron-updater keeps the download in its own cache under the profile: `%LOCALAPPDATA%\<name>-updater` on
     * Windows, `~/Library/Caches/<name>-updater` on macOS, `$XDG_CACHE_HOME/<name>-updater` or `~/.cache/<name>-updater`
     * on Linux (`ElectronAppAdapter` and `AppAdapter.getAppCacheDir` in the pinned electron-updater), `<name>` being the one electron-builder writes into
     * `app-update.yml`. It is outside `<data>` by electron-updater's construction: its base is the account's cache
     * folder, not `userData`, so the `setPath` that moves Electron's own files under `<data>` does not move it. The
     * desktop does not remove it.
     */
    async download(): Promise<void> {
        if (this.state.step !== 'offered') return;
        const { version } = this.state;
        this.show({ step: 'downloading', version });
        try {
            await this.deps.updater.downloadUpdate();
            this.downloadFailures = 0;
            this.deps.log.info('update_downloaded', { Version: version });
            this.show({ step: 'downloaded', version });
        } catch {
            this.downloadFailures += 1;
            this.deps.log.warn('update_failed', { Step: 'download', Failures: this.downloadFailures });
            this.show({ step: 'offered', version });
        }
    }

    /**
     * Stops the runtime the application carries when that is the one running, keeps the application aside, and installs
     * what was downloaded: only on the person's word. A runtime that does not stop, or does not answer, ends it there,
     * before anything is kept, replaced or closed; a copy that cannot be kept ends it before anything is closed.
     */
    async install(): Promise<boolean> {
        if (this.state.step !== 'downloaded') return false;
        const { version } = this.state;
        this.show({ step: 'installing', version });
        const running = await this.deps.stopRuntime().catch((): StopOutcome => ({ kind: 'failed', exit: -1 }));
        if (running.kind === 'failed' || running.kind === 'unanswered') {
            this.deps.log.error('update_failed', { Step: 'runtime', Failures: 1 });
            this.show({ step: 'downloaded', version });
            this.deps.refused(runtimeRefusal(running));
            return false;
        }
        let kept: string | null;
        try {
            kept = await keepPrevious(this.deps.rollback, this.deps.application, this.deps.version, version);
        } catch (error) {
            const code = (error as { code?: unknown } | null)?.code;
            this.deps.log.warn('update_keep_failed', { Code: typeof code === 'string' ? code : 'unknown' });
            this.deps.log.error('update_failed', { Step: 'keep', Failures: 1 });
            this.show({ step: 'downloaded', version });
            this.deps.refused(keepRefusal(running.kind === 'stopped', version));
            return false;
        }
        this.deps.log.info('update_installing', { From: this.deps.version, To: version });
        this.stop();
        await this.deps.beforeInstall();
        let failed = false;
        const failure = (): void => {
            failed = true;
        };
        this.deps.updater.on('error', failure);
        try {
            this.deps.updater.quitAndInstall();
        } catch {
            failed = true;
        } finally {
            this.deps.updater.removeListener('error', failure);
        }
        if (!failed) return true;
        // electron-updater did not quit: the installer stopped before it finished. The copy stays recorded as pending.
        this.deps.log.error('update_failed', { Step: 'install', Failures: 1 });
        this.show({ step: 'downloaded', version });
        await this.deps.afterRefused();
        this.deps.refused(installRefusal(running.kind === 'stopped', kept));
        return false;
    }

    /** The item's next step: a line said at another step is cleared; a look under way (`checking`) keeps it. */
    private show(state: UpdateState): void {
        this.state = state;
        if (this.line !== null && state.step !== 'checking' && !sameState(state, this.line.at)) this.line = null;
        this.deps.changed(state);
    }

    private schedule(ms: number): void {
        if (this.handle !== null) this.timers.clearTimeout(this.handle);
        this.handle = this.timers.setTimeout(() => {
            this.handle = null;
            void this.check();
        }, ms);
    }
}

/** Whether two states are the same step, of the same version where the step names one. */
function sameState(a: UpdateState, b: UpdateState): boolean {
    return a.step === b.step && ('version' in a ? a.version : null) === ('version' in b ? b.version : null);
}
