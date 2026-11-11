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
 * returns false after it was first called dispatches an `error` event first, synchronously (`BaseUpdater.install`,
 * `AppImageUpdater.doInstall`, `DebUpdater.doInstall`). That event is what tells the desktop to come back.
 *
 * On Linux electron-updater is active only where it can replace what runs: the AppImage, which it knows by the
 * `APPIMAGE` variable (`AppImageUpdater.isUpdaterActive`), and the deb. Anywhere else the updater looks for nothing and
 * its item says updates come through the package.
 */

import { nextCheckDelay } from './backoff.ts';
import type { Timers } from './attention.ts';
import type { Logger } from './log.ts';
import { unansweredWords, type StopOutcome, type Unanswered } from './own-runtime.ts';
import { isRejected, keepPrevious, readRollback } from './rollback.ts';

/** The deb a release publishes, as its packages are named. */
export const debFile = 'jason-desktop-linux-x64.deb';

/** Where releases are read: this repository's GitHub releases. */
export const feed = { provider: 'github', owner: 'reply-team', repo: 'jason-ai' } as const;

/** The product's own address in front of the releases, its releases index: every release request passes it. */
const releasesIndex = 'https://get.jasonharness.ai/releases/';

/** A release's page, by its tag: where going back from a deb update finds the previous release's deb. */
export function previousRelease(version: string): string {
    return `${releasesIndex}tag/v${version}`;
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

/** What the person is told when the runtime this application carries did not stop, or did not answer, so the update was not installed. */
export function runtimeRefusal(outcome: { kind: 'failed'; exit: number } | Unanswered): string {
    const again = 'Then choose Restart Jason and Its Runtime to Install again.';
    if (outcome.kind === 'unanswered') {
        const { why, next } = unansweredWords(outcome);
        return `The update was not installed, because ${why}. Jason was left as it was. ${next} ${again}`;
    }
    const how = outcome.exit === -1 ? 'jason runtime stop did not finish' : `jason runtime stop exited ${outcome.exit}`;
    return `The update was not installed, because the runtime this application carries did not stop (${how}). Jason was left as it was. Stop the runtime with jason runtime stop. ${again}`;
}

/** What the person is told when the application could not be kept aside, after its runtime was stopped or with none to stop. */
export function keepRefusal(stopped: boolean): string {
    const why = 'The update was not installed, because a copy of this version could not be kept aside to go back to.';
    return stopped ? `${why} The runtime this application carries was stopped for the update: jason runtime start starts it again.` : `${why} Jason was left as it was.`;
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

    /** One look at the releases; on the cadence, or when the person asks. */
    async check(): Promise<void> {
        if (!this.active) return;
        if (this.state.step === 'downloaded' || this.state.step === 'installing') return;
        if (this.state.step !== 'idle' && this.state.step !== 'offered') {
            this.schedule(nextCheckDelay(this.failures));
            return;
        }
        const before = this.state;
        this.show({ step: 'checking' });
        try {
            const result = await this.deps.updater.checkForUpdates();
            const version = result?.updateInfo.version;
            const offered = result?.isUpdateAvailable === true && version !== undefined && version !== this.deps.version && !isRejected(readRollback(this.deps.rollback), version);
            this.failures = 0;
            if (offered) {
                this.deps.log.info('update_offered', { Version: version });
                this.show({ step: 'offered', version });
            } else {
                this.deps.log.info('update_checked', { Available: false });
                this.show({ step: 'idle' });
            }
        } catch {
            this.failures += 1;
            this.deps.log.warn('update_failed', { Step: 'check', Failures: this.failures });
            this.show(before);
        }
        this.schedule(nextCheckDelay(this.failures));
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
        } catch {
            this.deps.log.error('update_failed', { Step: 'keep', Failures: 1 });
            this.show({ step: 'downloaded', version });
            this.deps.refused(keepRefusal(running.kind === 'stopped'));
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

    private show(state: UpdateState): void {
        this.state = state;
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
