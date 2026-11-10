/**
 * The updater: electron-updater reading this repository's GitHub releases on the stable channel only (a release with a
 * pre-release part is never offered, no channel is set, nothing is downgraded). It looks on the back-off `backoff.ts`
 * computes, offers what it found, downloads it only when the person says so and installs it only when the person says so
 * again, after the application's own folder has been kept aside (`rollback.ts`). electron-updater is handed in, so
 * nothing here needs Electron to be tested.
 */

import { nextCheckDelay } from './backoff.ts';
import type { Timers } from './attention.ts';
import type { Logger } from './log.ts';
import { isRejected, keepPrevious, readRollback } from './rollback.ts';

/** Where releases are read: this repository's GitHub releases. */
export const feed = { provider: 'github', owner: 'reply-team', repo: 'jason-ai' } as const;

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

/** The one menu item the updater shows: the next step the person can take, or what is under way. */
export function menuItemOf(state: UpdateState, installed: boolean): MenuItem {
    if (!installed) return { label: 'Updates Come to an Installed Build', enabled: false, act: null };
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
            return { label: `Restart to Install Jason ${state.version}`, enabled: true, act: 'install' };
        case 'installing':
            return { label: `Installing Jason ${state.version}…`, enabled: false, act: null };
    }
}

export interface UpdaterDeps {
    updater: FeedUpdater;
    /** The application's own version. */
    version: string;
    /** The folder the application is installed as (`applicationFolder`). */
    application: string;
    /** `<data>/desktop/rollback`. */
    rollback: string;
    log: Logger;
    timers?: Timers;
    /** Lets the window and the tray go before the installer runs. */
    beforeInstall(): Promise<void>;
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

    start(): void {
        const { updater } = this.deps;
        updater.autoDownload = false;
        updater.autoInstallOnAppQuit = false;
        updater.allowPrerelease = false;
        updater.allowDowngrade = false;
        updater.logger = null;
        updater.setFeedURL(feed);
        this.schedule(firstCheckMs);
    }

    stop(): void {
        if (this.handle !== null) this.timers.clearTimeout(this.handle);
        this.handle = null;
    }

    /** One look at the releases; on the cadence, or when the person asks. */
    async check(): Promise<void> {
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

    /** Downloads what was offered: only on the person's word. */
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

    /** Keeps the application aside and installs what was downloaded: only on the person's word. */
    async install(): Promise<boolean> {
        if (this.state.step !== 'downloaded') return false;
        const { version } = this.state;
        this.show({ step: 'installing', version });
        try {
            await keepPrevious(this.deps.rollback, this.deps.application, this.deps.version, version);
        } catch {
            this.deps.log.error('update_failed', { Step: 'keep', Failures: 1 });
            this.show({ step: 'downloaded', version });
            return false;
        }
        this.deps.log.info('update_installing', { From: this.deps.version, To: version });
        this.stop();
        await this.deps.beforeInstall();
        this.deps.updater.quitAndInstall();
        return true;
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
