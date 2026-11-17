/**
 * The updater: electron-updater reading this repository's GitHub releases on the stable channel only (a release with a
 * pre-release part is never offered, no channel is set, nothing is downgraded). It looks on the back-off `backoff.ts`
 * computes, offers what it found, downloads it only when the person says so and installs it only when the person says so
 * again, after the runtime this application carries, when it is the one running, has stopped (`own-runtime.ts`) and the
 * application's own folder has then been kept aside (`rollback.ts`): the installer replaces the executable the runtime
 * runs from, and the first start of the new version starts the runtime again from the new one. The one other word it acts
 * on is a terminal's: `jason update apply` asks for one version by a request (`update-request.ts`, `take`), which is
 * checked, downloaded and installed as one act, silently and with no start after it, or refused with a reason. A runtime that does not
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
    /**
     * Whether the installer starts the application after it installs where it is not silent; on macOS whether Squirrel's
     * downloaded update quits into a start of the new version or only quits (`MacUpdater.handleUpdateDownloaded`).
     */
    autoRunAppAfterInstall: boolean;
    /** The releases above, or a generic feed's address, which a request's loopback feed is (`update-request.ts`). */
    setFeedURL(options: typeof feed | string): void;
    checkForUpdates(): Promise<{ isUpdateAvailable: boolean; updateInfo: { version: string } } | null>;
    downloadUpdate(): Promise<unknown>;
    quitAndInstall(isSilent?: boolean, isForceRunAfter?: boolean): void;
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
 * step again, so a press the menu showed before the step moved on asks nothing.
 */
export async function pressItem(
    updates: Pick<Updater, 'current' | 'check' | 'download' | 'install'>,
    act: MenuItem['act'],
    byKey: boolean,
    ask: (question: ReturnType<typeof installQuestion>) => Promise<number>,
): Promise<void> {
    if (act === 'check') await updates.check();
    else if (act === 'download') await updates.download();
    else if (act === 'install' && !byKey) await updates.install();
    else if (act === 'install') {
        const state = updates.current;
        if (state.step !== 'downloaded') return;
        if ((await ask(installQuestion(state.version))) === 0) await updates.install();
    }
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

/** What the person is told when the application could not be kept aside, after its runtime was stopped or with none to stop. */
export function keepRefusal(stopped: boolean): string {
    const why = 'The update was not installed, because a copy of this version could not be kept aside to go back to.';
    return stopped ? `${why} The runtime this application carries was stopped for the update: jason runtime start starts it again.` : `${why} Jason was left as it was.`;
}

/** What the person is told when the menu's install is chosen while a terminal's request installs the update itself. */
export const requestUnderWayRefusal =
    'An update asked for from a terminal is under way and installs itself: Jason closes for it once it is ready. Nothing was done from the menu.';

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

/** A terminal's request as the updater takes it: the one version it names, and a loopback feed or null for the releases. */
export interface TakenRequest {
    to: string;
    feed: string | null;
}

/**
 * How a request is taken: whether it goes on to install once the update is downloaded, which a window that takes it does
 * only where the switch says it may close (`windowOpenUpdate` in `update-request.ts`); and `late`, which hears an install
 * that failed after `quitAndInstall` returned, as Squirrel says it on macOS only once it has looked.
 */
export interface TakenHow {
    install: boolean;
    /**
     * Where the request waits for the verb's go (`--await-go`): heard once the update is downloaded and before anything is
     * stopped, kept or installed; true once the go is there, false when it did not come in time.
     */
    go?: () => Promise<boolean>;
    late?: () => void;
}

/** A step of a request under way, heard before it is taken. */
export type TakenStep = { step: 'checking' } | { step: 'downloading'; offered: string } | { step: 'downloaded' } | { step: 'installing'; runtimeStopped: boolean; kept: string | null };

/** How `take` words a refusal: the reason, the version the feed offered, and what was done before it. */
type Refuse = (reason: TakenRefusal, offered?: string | null, done?: { runtimeStopped?: boolean; kept?: string | null; runtime?: { kind: 'failed'; exit: number; demo?: true } | Unanswered }) => TakenOutcome;

/** Why the updater refused a request: codes of the request's file (`update-request.ts`). */
export type TakenRefusal =
    | 'updater_inactive'
    | 'busy'
    | 'version_rejected'
    | 'check_failed'
    | 'version_mismatch'
    | 'download_failed'
    | 'window_open'
    | 'go_timed_out'
    | 'runtime_not_stopped'
    | 'keep_failed'
    | 'install_failed';

/** What came of a request: nothing to install, the update handed to the installer, or a refusal and what was done before it. */
export type TakenOutcome =
    | { state: 'current' }
    | { state: 'installing'; offered: string; runtimeStopped: boolean; kept: string | null }
    | { state: 'refused'; reason: TakenRefusal; offered: string | null; runtimeStopped: boolean; kept: string | null; runtime?: { kind: 'failed'; exit: number; demo?: true } | Unanswered };

/** What came of an install: handed to the installer, or stopped at one of its three steps with what was done before it. */
type InstallResult =
    | { ok: true; runtimeStopped: boolean; kept: string | null }
    | { ok: false; step: 'runtime'; outcome: { kind: 'failed'; exit: number; demo?: true } | Unanswered }
    | { ok: false; step: 'keep'; runtimeStopped: boolean }
    | { ok: false; step: 'install'; runtimeStopped: boolean; kept: string | null };

export class Updater {
    private readonly deps: UpdaterDeps;
    private readonly timers: Timers;
    private state: UpdateState = { step: 'idle' };
    private failures = 0;
    private downloadFailures = 0;
    private handle: unknown = null;
    /** Whether the cadence was started (`start`): a window's updater. One built for a request alone never looks by itself. */
    private looking = false;
    /**
     * Whether a terminal's request that installs is under way (`take` with `how.install`): in a window only under the
     * `close` switch (`windowOpenUpdate`), where the menu's install would race the request's own; refused while it lasts.
     */
    private requestInstalls = false;

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
        this.prepare();
        this.looking = true;
        if (this.active) this.schedule(firstCheckMs);
    }

    /** What every use of electron-updater is set up with: nothing downloaded or installed by itself, the releases read. */
    prepare(): void {
        const { updater } = this.deps;
        updater.autoDownload = false;
        updater.autoInstallOnAppQuit = false;
        updater.allowPrerelease = false;
        updater.allowDowngrade = false;
        updater.logger = null;
        updater.setFeedURL(feed);
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
        if (this.requestInstalls) {
            this.deps.refused(requestUnderWayRefusal);
            return false;
        }
        if (this.state.step !== 'downloaded') return false;
        const result = await this.installing(this.state.version, null);
        if (result.ok) return true;
        if (result.step === 'runtime') this.deps.refused(runtimeRefusal(result.outcome));
        else if (result.step === 'keep') this.deps.refused(keepRefusal(result.runtimeStopped));
        else this.deps.refused(installRefusal(result.runtimeStopped, result.kept));
        return false;
    }

    /**
     * A terminal's request (`update-request.ts`): the version it names, looked for on the feed it names or the releases,
     * downloaded, and, where `how` says so, installed as `install` installs, but silently and with no start of the
     * application after it: `quitAndInstall(true, false)`, which the pinned electron-updater turns on Windows into the
     * installer's `--updated /S` with no `--force-run` (`BaseUpdater.quitAndInstall`, `NsisUpdater.doInstall`), with
     * `autoRunAppAfterInstall` off, which on macOS makes Squirrel's downloaded update quit the application rather than
     * start it again (`MacUpdater.handleUpdateDownloaded`), ShipIt installing once it has quit. `progress` hears each step
     * before it is taken, the install's before the installer is handed anything. A feed that offers another version, or
     * none, refuses the request: it names the version the terminal planned for. So does a version whose first start failed
     * here, which the cadence never offers either. Nothing is shown to a person: the reason is the answer.
     */
    async take(request: TakenRequest, how: TakenHow, progress: (step: TakenStep) => void): Promise<TakenOutcome> {
        const refused: Refuse = (reason, offered = null, done = {}) => ({
            state: 'refused',
            reason,
            offered,
            runtimeStopped: done.runtimeStopped ?? false,
            kept: done.kept ?? null,
            ...(done.runtime === undefined ? {} : { runtime: done.runtime }),
        });
        if (!this.active) return refused('updater_inactive');
        if (request.to === this.deps.version) return { state: 'current' };
        const step = this.state.step;
        if (step === 'checking' || step === 'downloading' || step === 'installing') return refused('busy');
        if (isRejected(readRollback(this.deps.rollback), request.to)) return refused('version_rejected');
        // No look of the cadence runs while the request is under way; one is scheduled again when it ends short of an install.
        this.stop();
        const before = this.state;
        const back: UpdateState = before.step === 'downloaded' || before.step === 'offered' ? before : { step: 'idle' };
        let handed = false;
        this.requestInstalls = how.install;
        try {
            const outcome = await this.taking(request, how, progress, back, refused);
            handed = outcome.state === 'installing';
            return outcome;
        } finally {
            // On every path, a throw included: the releases' feed put back, and short of the installer no step left under
            // way and the cadence looking again.
            this.deps.updater.setFeedURL(feed);
            this.requestInstalls = false;
            if (!handed) {
                const now = this.current;
                if (now.step === 'installing') this.show({ step: 'downloaded', version: now.version });
                else if (now.step === 'downloading') this.show({ step: 'offered', version: now.version });
                else if (now.step === 'checking') this.show(back);
                if (this.looking) this.schedule(nextCheckDelay(this.failures));
            }
        }
    }

    /** `take` once the cadence is held: its look, its download and its install, the feed and the cadence being `take`'s. */
    private async taking(request: TakenRequest, how: TakenHow, progress: (step: TakenStep) => void, back: UpdateState, refused: Refuse): Promise<TakenOutcome> {
        const ready = this.state.step === 'downloaded' && this.state.version === request.to && request.feed === null;
        if (!ready) {
            progress({ step: 'checking' });
            this.show({ step: 'checking' });
            this.deps.updater.setFeedURL(request.feed ?? feed);
            let seen: string | null;
            let offered: string | null;
            try {
                const result = await this.deps.updater.checkForUpdates();
                seen = result?.updateInfo.version ?? null;
                offered = result?.isUpdateAvailable === true ? seen : null;
            } catch {
                this.deps.log.warn('update_failed', { Step: 'check', Failures: 1 });
                this.show(back);
                return refused('check_failed');
            }
            if (offered !== request.to) {
                this.deps.log.info('update_checked', { Available: offered !== null });
                this.show(back);
                return refused('version_mismatch', seen);
            }
            this.deps.log.info('update_offered', { Version: offered });
            progress({ step: 'downloading', offered });
            this.show({ step: 'downloading', version: offered });
            try {
                await this.deps.updater.downloadUpdate();
            } catch {
                this.downloadFailures += 1;
                this.deps.log.warn('update_failed', { Step: 'download', Failures: this.downloadFailures });
                this.show({ step: 'offered', version: offered });
                return refused('download_failed', offered);
            }
            this.downloadFailures = 0;
            this.deps.log.info('update_downloaded', { Version: offered });
            this.show({ step: 'downloaded', version: offered });
        }
        // A window that may not close stops here: the update waits under its menu's install item.
        if (!how.install) return refused('window_open', request.to);
        // A request that waits for the go says it is downloaded and waits: no go in time stops nothing, keeps nothing and
        // installs nothing, and the download waits under the install item.
        if (how.go !== undefined) {
            progress({ step: 'downloaded' });
            if (!(await how.go())) {
                this.deps.log.info('update_go_missed', { Version: request.to });
                return refused('go_timed_out', request.to);
            }
        }
        const result = await this.installing(request.to, { late: how.late, progress });
        if (result.ok) return { state: 'installing', offered: request.to, runtimeStopped: result.runtimeStopped, kept: result.kept };
        if (result.step === 'runtime') return refused('runtime_not_stopped', request.to, { runtime: result.outcome });
        if (result.step === 'keep') return refused('keep_failed', request.to, { runtimeStopped: result.runtimeStopped });
        return refused('install_failed', request.to, { runtimeStopped: result.runtimeStopped, kept: result.kept });
    }

    /**
     * The install both words share: the runtime stopped, the application kept aside, the desk let go and the installer
     * handed the update. `request` is null for the person's, whose installer runs as it always did, else the terminal's.
     */
    private async installing(version: string, request: { late: (() => void) | undefined; progress: (step: TakenStep) => void } | null): Promise<InstallResult> {
        this.show({ step: 'installing', version });
        const running = await this.deps.stopRuntime().catch((): StopOutcome => ({ kind: 'failed', exit: -1 }));
        if (running.kind === 'failed' || running.kind === 'unanswered') {
            this.deps.log.error('update_failed', { Step: 'runtime', Failures: 1 });
            this.show({ step: 'downloaded', version });
            return { ok: false, step: 'runtime', outcome: running };
        }
        const runtimeStopped = running.kind === 'stopped';
        let kept: string | null;
        try {
            kept = await keepPrevious(this.deps.rollback, this.deps.application, this.deps.version, version);
        } catch {
            this.deps.log.error('update_failed', { Step: 'keep', Failures: 1 });
            this.show({ step: 'downloaded', version });
            return { ok: false, step: 'keep', runtimeStopped };
        }
        this.deps.log.info('update_installing', { From: this.deps.version, To: version });
        request?.progress({ step: 'installing', runtimeStopped, kept });
        this.stop();
        await this.deps.beforeInstall();
        let failed = false;
        const failure = (): void => {
            failed = true;
        };
        const { updater } = this.deps;
        updater.on('error', failure);
        try {
            if (request === null) updater.quitAndInstall();
            else {
                updater.autoRunAppAfterInstall = false;
                updater.quitAndInstall(true, false);
            }
        } catch {
            failed = true;
        } finally {
            updater.removeListener('error', failure);
        }
        if (!failed) {
            // An error electron-updater says after this, Squirrel's on macOS, means the update was not installed after all.
            const late = request?.late;
            if (late !== undefined) {
                const heard = (): void => {
                    updater.removeListener('error', heard);
                    this.deps.log.error('update_failed', { Step: 'install', Failures: 1 });
                    late();
                };
                updater.on('error', heard);
            }
            return { ok: true, runtimeStopped, kept };
        }
        if (request !== null) updater.autoRunAppAfterInstall = true;
        // electron-updater did not quit: the installer stopped before it finished. The copy stays recorded as pending.
        this.deps.log.error('update_failed', { Step: 'install', Failures: 1 });
        this.show({ step: 'downloaded', version });
        await this.deps.afterRefused();
        return { ok: false, step: 'install', runtimeStopped, kept };
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
