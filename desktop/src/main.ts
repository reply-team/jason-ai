/**
 * Jason Desktop's main process: a window onto the UI the installation's runtime serves at its loopback address, and the
 * Connect cards' actions behind it. It is one more client of the installation the `jason` command line uses: it finds the
 * data directory as the CLI does, brings the runtime up only through `jason runtime start`, and changes the installation
 * only through CLI verbs and the settings file a person would edit.
 */

import { app, BrowserWindow, clipboard, dialog, ipcMain, nativeImage, nativeTheme, protocol, safeStorage, session, shell, type WebContents } from 'electron';
import { autoUpdater } from 'electron-updater';
import { existsSync, readFileSync, rmSync } from 'node:fs';
import { stat } from 'node:fs/promises';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { Chat } from './chat.ts';
import { fenceRefusal, readFence, type ChatFence } from './chat-deny.ts';
import { registerChat } from './chat-ipc.ts';
import { loggedModels, loggedTurns } from './chat-log.ts';
import { fullAccessAnswer, fullAccessDialog } from './chat-modes.ts';
import { carriedOf, isOtherCopy, removeOtherCopies } from './bundle-copy.ts';
import { cliAt, type Cli } from './cli.ts';
import { OpenIn, type PlaceId } from './open-in.ts';
import { IconReader, readRegularFileAtMost } from './open-in-icons.ts';
import { isFolder, systemProbes } from './open-in-probes.ts';
import { editorEnvironment } from './open-in-env.ts';
import { startDetached } from './open-in-start.ts';
import { changeJasonFlags, continueFlags, terminalFlags } from './open-in-terminal.ts';
import { linkIn, LinkPost, linkRegistration, linksTaken, parseLink } from './deep-link.ts';
import { Desk } from './desk.ts';
import { bringForward, LoginItem, openedAtLogin, RuntimeAutostart, showsOnSecondStart, windowConduct } from './login-item.ts';
import { keptAsideOf, launcherOf, linuxPackage, mountOf, withoutMount, type LinuxPackage } from './linux-package.ts';
import { commandFolder, isProgramFile, PathCommand, type PathCommandDeps } from './path-command.ts';
import { ownRuntimeDeps, stopOwnRuntime, whoseRuntime } from './own-runtime.ts';
import { atFirstPairing, Repairer } from './repair.ts';
import { applicationFolder, failedStartNotice, keptLine, readRollback, rollbackVersions, settleFailedStart, settleStart } from './rollback.ts';
import { refuseStart, sandboxRefusal } from './sandbox.ts';
import { debFile, previousRelease, updateKeyWords, Updater } from './updates.ts';
import { loopbackOrigin, runtimeOrigin } from './descriptor.ts';
import { sharingVersionReads } from './host-version.ts';
import { LayoutRefused, loggedCli, startInstallation, StartFailed } from './installation.ts';
import { eventChannels, handle, withMembers, type InvokeMember, type IpcRegistrar } from './ipc.ts';
import { BridgeHolder, BrowserBridge, openOwnPage, openTab } from './browser-bridge.ts';
import { fileLogger, limitsFrom, type Logger } from './log.ts';
import { dataDirectory, dataLayout, desktopPaths, type DesktopPaths } from './paths.ts';
import { applyMode, demoMenu, demoUnshownSentence, keepFirstStart, offersOf, personEnvironmentOf, readMode, relaunchAsPerson, relaunchOptions, startRun, writeMode, type Mode, type ModeAnswer, type PersonEnvironment, type StartRun } from './mode.ts';
// Live still stops a runtime an earlier version's demo left running, before an update or a removal replaces the jason it runs.
import { stopDemoRuntime, stopWithDemo } from './demo-runtime.ts';
import { demoBeforeReady, demoRepository } from './demo-page.ts';
import { openDemo, type DemoViews } from './demo-window.ts';
import { permissionAllowed } from './permissions.ts';
import { pathOf, programsOn, withPath } from './programs.ts';
import { ProviderKeys } from './providers.ts';
import { applyDailyTrends, keptDailyTrends, readSettings, settingsAtStart, writeSettings, type DesktopSettings } from './settings.ts';
import { applyTheme, darkBackground, isNewInstallation, lightBackground, startingTheme } from './theme.ts';
import { Setup } from './setup.ts';
import { rolesMoments } from './roles-moments.ts';
import { SkillsGate } from './skills-gate.ts';
import { askBox } from './reply-accounts.ts';
import { codexAsker, ModelLists } from './model-lists.ts';
import { loginPath } from './shell-path.ts';
import { placedRemoval, removalOffered } from './remove-placed.ts';
import { harnessRoot, processesRunFrom, stopRuntimeStart, uninstallStart } from './uninstall.ts';
import { runStopRuntime, runUninstall } from './uninstall-start.ts';
import { earlierPageFound, loadWindow, readWindowPairing, WindowPairing, windowPartition, writeWindowPairing } from './window-pairing.ts';
import { keepWindowTitle, windowTitle } from './window-title.ts';
import { followTheme, frameOf, keepMovable } from './window-frame.ts';

function readText(file: string): string | null {
    try {
        return readFileSync(file, 'utf8');
    } catch {
        return null;
    }
}

/** The chat's fence the application carries, read at every turn; a file of it that cannot be read starts no turn, and says which. */
function chatFence(): ChatFence {
    try {
        return readFence(app.getAppPath());
    } catch (error) {
        throw new Error(fenceRefusal(error));
    }
}

/** The folder the application carries `jason` and the skills package in: beside the app when packaged, else `bundle/`. */
function bundleDirectory(): string {
    return app.isPackaged ? join(process.resourcesPath, 'bundle') : join(app.getAppPath(), 'bundle');
}

/** The Linux package this application runs as (`linux-package.ts`): a development build is a folder; null off Linux. */
function linuxForm(): LinuxPackage | null {
    if (process.platform !== 'linux') return null;
    if (!app.isPackaged) return { form: 'folder' };
    return linuxPackage(process.env, readText(join(process.resourcesPath, 'package-type')));
}

/** The version the bundle was prepared as: a release's or a candidate's, never `dev`. */
function bundleVersion(bundle: string): string | null {
    try {
        const raw = JSON.parse(readFileSync(join(bundle, 'version.json'), 'utf8')) as { version?: unknown };
        return typeof raw.version === 'string' && raw.version !== '' ? raw.version : null;
    } catch {
        return null;
    }
}

function originOf(url: string): string | null {
    try {
        return loopbackOrigin(new URL(url).origin);
    } catch {
        return null;
    }
}

function isHttps(url: string): boolean {
    try {
        return new URL(url).protocol === 'https:';
    } catch {
        return false;
    }
}

/**
 * The Demo's one way to the system browser: it takes no address and opens the repository's, the constant the page's note
 * names (`demoRepository`). When it is opened is the page's view's to say (`demoLinkOpener`: a navigation of its main
 * frame to exactly that address, within a second of a person's click or Enter there, at most once in two seconds); what
 * is opened is this constant alone.
 */
function openDemoRepository(log: Logger): void {
    shell.openExternal(demoRepository).then(
        () => log.info('demo_link_opened', {}),
        () => log.warn('demo_link_open_failed', {}),
    );
}

/** Shows the desktop's own page while the installation comes up, or why it did not. */
function showStarting(window: BrowserWindow, message: string, failed: boolean): Promise<void> {
    return window.loadFile(join(app.getAppPath(), 'shell', 'starting.html'), { query: { message, state: failed ? 'failed' : 'starting' } });
}

/**
 * Locks the window to the runtime's origin: a navigation elsewhere is refused, and a link or `window.open` that leaves
 * it goes to the system browser only when it is https. Of what the page asks for, only writing to the clipboard is
 * granted, and only at the runtime's origin; no other permission, no webview. Answers the two ways to the system
 * browser: `leave`, which the Reply card's links take too, opens an https URL and refuses any other, logging neither;
 * `ownPage` opens a page of the runtime's own UI at its origin, the chat in a tab, and refuses any other address. The
 * page names neither address: `ownPage` is handed only the link the main process made from what the CLI printed.
 */
function lockNavigation(contents: WebContents, origin: () => string | null, log: Logger): { leave: (url: string) => Promise<void>; ownPage: (url: string) => Promise<void> } {
    const leave = (url: string): Promise<void> => {
        if (!isHttps(url)) {
            log.warn('link_refused', {});
            return Promise.reject(new Error('Only an https link goes to the browser.'));
        }
        return shell.openExternal(url).then(
            () => log.info('link_opened', {}),
            (error: unknown) => {
                log.warn('link_open_failed', {});
                throw error;
            },
        );
    };
    // Held to the runtime's own page as the run file says it at this moment, whatever the caller held (`openOwnPage`).
    const ownPage = (url: string): Promise<void> => openOwnPage(url, { origin, log, open: (held) => shell.openExternal(held) });
    const guard = (event: { preventDefault(): void }, url: string) => {
        const here = origin();
        if (here !== null && originOf(url) === here) return;
        event.preventDefault();
        log.warn('navigation_refused', { Origin: here ?? 'none' });
        leave(url).catch(() => undefined);
    };
    contents.on('will-navigate', guard);
    contents.on('will-redirect', guard);
    contents.on('will-attach-webview', (event) => event.preventDefault());
    contents.setWindowOpenHandler(({ url }) => {
        leave(url).catch(() => undefined);
        return { action: 'deny' };
    });
    contents.session.setPermissionRequestHandler((_contents, permission, callback, details) => callback(permissionAllowed(permission, details.requestingUrl, origin())));
    contents.session.setPermissionCheckHandler((_contents, permission, requestingOrigin) => permissionAllowed(permission, requestingOrigin, origin()));
    return { leave, ownPage };
}

function registerSetup(seam: IpcRegistrar, setup: Setup, origin: () => string | null, log: Logger, changed: () => void): void {
    const refused = (channel: string) => log.warn('ipc_refused', { Channel: channel, Origin: origin() ?? 'none' });
    const members: [InvokeMember, (...args: unknown[]) => unknown][] = [
        ['setup.status', () => setup.status()],
        ['setup.connectReply', () => setup.connectReply()],
        ['setup.connectReplyWithKey', (key) => setup.connectReplyWithKey(key)],
        ['setup.setProviderKey', (id, key) => setup.setProviderKey(id, key)],
        ['setup.installProgram', (id) => setup.installProgram(id)],
        ['setup.useProgram', (id) => setup.useProgram(id)],
        ['setup.configurePlugin', (id, values) => setup.configurePlugin(id, values)],
        ['setup.disconnectPlugin', (id) => setup.disconnectPlugin(id)],
        // The Reply card's acts, each a verb of the Reply CLI on the person's click; a link goes to the browser, never here.
        ['setup.replyConnect', (channel, options) => setup.replyConnect(channel, options)],
        ['setup.replyReconnect', (channel, id, options) => setup.replyReconnect(channel, id, options)],
        ['setup.replyLimits', (id, daily) => setup.replyLimits(id, daily)],
        ['setup.replyPause', (id) => setup.replyPause(id)],
        ['setup.replyResume', (id) => setup.replyResume(id)],
        ['setup.replyUnblock', (id) => setup.replyUnblock(id)],
        ['setup.replyDelete', (channel, id, options) => setup.replyDelete(channel, id, options)],
    ];
    for (const [member, handler] of members) handle(seam, member, origin, handler, refused);
    setup.onChanged(changed);
}

/** Brings the window forward; the desk's own once it is open. */
let showWindow: () => void = () => bringForward(BrowserWindow.getAllWindows()[0] ?? null);

/** The desktop's log before run() makes its own, under the data directory this start was given. */
function startLog(): Logger {
    return fileLogger(desktopPaths(dataDirectory(process.env, homedir())).logs);
}

/**
 * A link that reached the application, from a start's arguments, a second start or the system: parsed, and either held
 * for the page (`links`) with the window brought forward, or refused, which opens nothing. The log says the page or the
 * refusal's code, never the address.
 */
function linkArrived(raw: unknown, links: LinkPost, log: Logger): void {
    if (!linksTaken({ packaged: app.isPackaged, platform: process.platform, form: linuxForm() })) {
        log.warn('deep_link_refused', { Code: 'unregistered' });
        showWindow();
        return;
    }
    const target = parseLink(raw);
    if ('refused' in target) {
        log.warn('deep_link_refused', { Code: target.refused });
        showWindow();
        return;
    }
    log.info('deep_link_opened', { Page: target.page });
    links.arrived(target);
    showWindow();
}

/** The link a second start passed in its `additionalData`, or null; it is parsed again here, never trusted. */
function linkOf(data: unknown): unknown {
    return data !== null && typeof data === 'object' && 'link' in data ? (data as { link: unknown }).link : null;
}

/** The window's run; `links` is where a link waits for the page, made by the start before Electron was ready. */
async function run(links: LinkPost, newInstallation: boolean): Promise<void> {
    // Every member the page may ask for is registered through `seam`, which checks the window's sender and keeps each
    // handler in one registry: the browser bridge reaches the same handlers through it (`browser-bridge.ts`).
    const seam = withMembers(ipcMain);
    const home = homedir();
    // The login shell's PATH is read before anything is started or registered, so every program is found as a terminal
    // finds it.
    const pathRead = loginPath(pathOf(process.env), home);
    process.env.PATH = await pathRead;
    const form = linuxForm();
    // Every child gets the person's PATH and libraries: nothing of an AppImage's mount, which the AppRun put on them.
    const env = withoutMount(withPath(process.env, process.env.PATH), mountOf(form, process.env));

    const paths: DesktopPaths = desktopPaths(dataDirectory(process.env, home));
    const log = fileLogger(paths.logs, limitsFrom(readText(dataLayout(paths.data).userSettingsFile)));
    const packaged = bundleDirectory();
    const version = bundleVersion(packaged);
    // What this start carries (`bundle-copy.ts`): an AppImage's copy under <data>/desktop/bundle/<version>/, the bundle
    // where it is installed for anything else; with no copy, nothing that outlasts the run names the mount's jason.
    const carried = carriedOf({ form, packaged, version, bundles: paths.bundle });
    if (carried.failure !== null) log.error('bundle_copy_failed', { Differs: carried.failure.differs });
    const { bundle, uncopied } = carried;
    log.info('desktop_started', { Version: version ?? 'unprepared', Platform: process.platform, DataDirectory: paths.data });

    const conduct = windowConduct(process.platform);
    const atLogin = openedAtLogin(process.argv);
    // The theme the person chose last, before any page is drawn: the window's first paint and the starting page follow it.
    nativeTheme.themeSource = startingTheme(readSettings(paths.settings).theme, newInstallation);
    const window = new BrowserWindow({
        width: 1280,
        height: 860,
        minWidth: 900,
        minHeight: 600,
        title: windowTitle,
        // No title bar: the page leaves the window's buttons their room and marks where it is dragged by; on Windows and
        // Linux the caption buttons are drawn in the theme in force.
        ...frameOf(process.platform, nativeTheme.shouldUseDarkColors),
        // A start the computer made at sign-in stays in the tray until the person opens the window; on Linux, where no
        // tray can be counted on, it opens minimized instead.
        show: !atLogin || conduct.atLogin === 'minimized',
        backgroundColor: nativeTheme.shouldUseDarkColors ? darkBackground : lightBackground,
        webPreferences: {
            preload: join(__dirname, 'preload.js'),
            // A session Electron keeps in memory: nothing of the page's pairing reaches the disk (`window-pairing.ts`).
            partition: windowPartition,
            contextIsolation: true,
            nodeIntegration: false,
            sandbox: true,
            webSecurity: true,
            spellcheck: false,
            // A packaged build opens no DevTools, whatever the page or a key asks.
            devTools: !app.isPackaged,
        },
    });
    let current: BrowserWindow | null = window;
    window.on('closed', () => {
        current = null;
    });
    // The console's own <title> is the browser tab's, and an older console's says "Jason": the window keeps its name.
    keepWindowTitle(window);
    // With no title bar, every page the window shows is given a band to drag the window by.
    keepMovable(window, process.platform);
    // On Windows and Linux the caption buttons follow each change of the theme in force while the window is open.
    followTheme(window, nativeTheme, process.platform);
    if (atLogin && conduct.atLogin === 'minimized') window.minimize();
    window.setMenuBarVisibility(false);
    // The tray keeps the application: closing the window hides it, and Quit in the tray or the menu ends it. On Linux
    // closing the window quits; the runtime keeps running either way.
    let quitting = false;
    app.on('before-quit', () => {
        quitting = true;
    });
    window.on('close', (event) => {
        if (quitting || conduct.closeQuits) return;
        event.preventDefault();
        window.hide();
    });

    // The origin is read from the run file on every check, because the runtime may come back on another port.
    const origin = () => runtimeOrigin(readText(paths.runFile) ?? '');
    // The bridge a browser tab reaches the chat through: opened only when the person first opens the chat in a browser,
    // closed with the window and at quit.
    // The chat is made further down; the bridge opens only on the person's act, long after it is.
    const bridges = new BridgeHolder(() => BrowserBridge.open({ origin, members: seam.members, modeOf: (chatId) => chat.modeNow(chatId), log }));
    // The one way an event reaches the page: the window, and every tab paired with the bridge for the channels a tab reads.
    const toPage = (channel: keyof typeof eventChannels, ...args: unknown[]) => {
        current?.webContents.send(eventChannels[channel], ...args);
        bridges.publish(channel, args);
    };
    const { leave: toBrowser, ownPage } = lockNavigation(window.webContents, origin, log);
    // A link waits for the page that takes it: a page that goes, by a load or a reload, takes no more until the next asks.
    handle(seam, 'links.take', origin, () => links.take(), (channel) => log.warn('ipc_refused', { Channel: channel, Origin: origin() ?? 'none' }));
    window.webContents.on('did-start-navigation', (details) => {
        if (details.isMainFrame && !details.isSameDocument) links.away();
    });
    // On Windows the scheme is registered at each start, with "--" before the address (`deep-link.ts`).
    const registration = linkRegistration(process.platform, app.isPackaged, process.execPath);
    if (registration !== null && !app.setAsDefaultProtocolClient(registration.scheme, registration.path, registration.args)) log.warn('deep_link_registration_failed', {});

    // Written when missing (a new installation meets Light there): the Windows check of Codex's sandbox reads it.
    let settings: DesktopSettings = settingsAtStart(paths.settings, newInstallation);
    const executable = join(bundle, process.platform === 'win32' ? 'jason.exe' : 'jason');
    const jason = cliAt(executable, env);
    const cli = loggedCli(jason, log);
    // The count and the re-pair loop begin at the first pairing that succeeds: the start's, or a Reload's after a start
    // that failed.
    const begin = atFirstPairing(() => {
        desk.watch();
        repairer.start();
    });
    const repairer = new Repairer({ readRunFile: () => readText(paths.runFile), cli, log, load: async (link) => void (await current?.loadURL(link)), paired: begin });
    // On Linux the session starts the launcher: the AppImage file itself, whose mount lasts only while it runs.
    const login = new LoginItem({ platform: process.platform, home, executable: form === null ? process.execPath : launcherOf(form, process.execPath), electron: app, pathRead, log, env: process.env });
    // This run is Live's (a Demo start runs `runDemo`), which offers every act that outlasts it (`offersOf`).
    const offers = offersOf(runOn.mode);
    const autostart = !offers.autostart || version === null || !carried.outlives ? null : new RuntimeAutostart({ cli, log });
    // The updater runs only in an installed build: a development build has no installer to hand an update to.
    const rollback = paths.rollback;
    // What an update keeps aside to go back to: the AppImage file on Linux, nothing for the deb.
    const aside = keptAsideOf(process.platform, form, process.execPath);
    const updates = app.isPackaged && offers.updates
        ? new Updater({
              updater: autoUpdater,
              version: app.getVersion(),
              application: aside,
              rollback,
              log,
              // A demo runtime left running by a kill or a crash holds the carried executable as well: it is stopped too.
              stopRuntime: () => stopWithDemo(() => stopOwnRuntime(ownRuntimeDeps(process.platform, executable, paths.runFile, cli)), () => stopDemoRuntime({ platform: process.platform, executable, person: paths.data, env })),
              refused: (why) => dialog.showErrorBox(windowTitle, why),
              async beforeInstall() {
                  quitting = true;
                  repairer.stop();
                  desk.close();
              },
              async afterRefused() {
                  quitting = false;
                  desk.open();
                  desk.watch();
                  repairer.start();
                  desk.show();
              },
              changed: () => desk.renderMenu(),
          })
        : null;
    // The update item's key, where the item can act: the page and the versions-differ sentence name it only then.
    const updateKey = () => updateKeyWords(updates !== null, updates?.active ?? false, process.platform);
    const commandDeps: PathCommandDeps = {
        platform: process.platform,
        folder: commandFolder(process.platform, process.env, home),
        executable,
        dataDirectory: process.env['JASON_DATA_DIR'] || null,
        path: () => process.env.PATH ?? '',
        pathext: process.env['PATHEXT'],
        isProgram: (file) => isProgramFile(file, process.platform),
        log,
    };
    const command = offers.command && carried.outlives ? new PathCommand(commandDeps) : null;
    // What the person set up from an earlier version's copy, the PATH command and the start at logon, follows this
    // start's; the copies of other versions are removed only after that, by what then still names them.
    const otherCopy = carried.repoints ? (named: string) => isOtherCopy(named, paths.bundle, executable) : null;
    if (otherCopy !== null && command !== null) {
        try {
            command.repoint(otherCopy);
        } catch {
            log.error('repoint_failed', { What: 'path_command', Exit: -1 });
        }
    }
    const keptCopy = () => keptLine(readRollback(rollback), app.getVersion(), existsSync);
    // Remove What Jason Placed, on macOS and Linux: the uninstall's own steps, over the same items the menu sets. Offered
    // in Live alone (`offersOf`).
    const removal = offers.removal && removalOffered(process.platform)
        ? placedRemoval({
              platform: process.platform,
              home,
              env: process.env,
              form,
              execPath: process.execPath,
              executable,
              version,
              paths,
              command,
              autostart,
              makeCommand: () => new PathCommand(commandDeps),
              makeAutostart: () => new RuntimeAutostart({ cli, log }),
              login,
              stopAs: (carried) => stopOwnRuntime(ownRuntimeDeps(process.platform, carried, paths.runFile, cli)),
              stopDemo: () => stopDemoRuntime({ platform: process.platform, executable, person: paths.data, env }),
              runsFrom: (folder) => processesRunFrom(folder),
              log,
          })
        : null;
    // Live or Demo: the page reads this run's mode once as it loads. A mode set is written to the record in the person's
    // directory, and the desktop then starts again on what it names; a write that fails starts nothing.
    const modeDeps = modeSwitch(log, form);
    const desk = new Desk({ window: () => current, origin, cli: jason, log, reload: () => void repairer.reload(), login, autostart, updates, kept: keptCopy, command, removal, demo: demoMenu(runOn.mode), back: () => backToLive(modeDeps) });
    desk.open();
    showWindow = () => desk.show();
    const repointed = (async () => {
        if (otherCopy !== null) await autostart?.repoint(otherCopy, paths.data).catch(() => log.error('repoint_failed', { What: 'autostart', Exit: -1 }));
        await desk.readAutostart();
    })();
    // An update that installs the AppImage under a new file name moves Open Jason at Login to it.
    autoUpdater.on('appimage-filename-updated', (file) => {
        try {
            login.relocate(file);
        } catch {
            log.error('repoint_failed', { What: 'login_item', Exit: -1 });
        }
    });
    updates?.start();
    let runtimeVersion: string | null = null;
    let runtimeIsOurs = false;
    const keys = new ProviderKeys(paths.providerKeys, safeStorage, process.platform);
    // One runner for the Connect cards and the chat, so a listing and a turn share one read of Claude Code's version.
    const programs = sharingVersionReads(programsOn(env, version));
    const dataNamed = Boolean(process.env.JASON_DATA_DIR?.trim());
    // The models Codex offers: shown from what is kept, asked again in the background when the hosts are listed.
    const models = new ModelLists({
        file: paths.models,
        ask: codexAsker({ paths, env, bundle, dataNamed, bundleVersion: version, keyFor: (id) => keys.keyFor(id) }),
        changed: () => toPage('setup.changed'),
        ...loggedModels(log),
    });
    const refused = (channel: string) => log.warn('ipc_refused', { Channel: channel, Origin: origin() ?? 'none' });
    const themeDeps = {
        setSource: (choice: 'system' | 'light' | 'dark') => {
            nativeTheme.themeSource = choice;
            window.setBackgroundColor(nativeTheme.shouldUseDarkColors ? darkBackground : lightBackground);
        },
        write: (next: DesktopSettings) => writeSettings(paths.settings, next),
    };
    handle(seam, 'theme.get', origin, () => settings.theme, refused);
    handle(seam, 'theme.set', origin, (choice) => void (settings = applyTheme(choice, settings, themeDeps)), refused);
    handle(seam, 'updates.key', origin, updateKey, refused);
    handle(seam, 'dailyTrends.get', origin, () => keptDailyTrends(settings), refused);
    handle(
        seam,
        'dailyTrends.set',
        origin,
        (on) => void (settings = applyDailyTrends(on, settings, (next) => writeSettings(paths.settings, next))),
        refused,
    );
    handle(seam, 'mode.get', origin, (): ModeAnswer => ({ mode: runOn.mode, unopened: runOn.unopened, preparing: false }), refused);
    handle(seam, 'mode.set', origin, (choice) => applyMode(choice, modeDeps), refused);
    // The page reports the id of the UI token it holds, never the token; the one before it is revoked then.
    const windowPairing = new WindowPairing({
        cli,
        log,
        state: () => readWindowPairing(paths.windowPairing),
        save: (next) => writeWindowPairing(paths.windowPairing, next),
    });
    handle(seam, 'pairing.paired', origin, (tokenId) => windowPairing.paired(tokenId), refused);
    const setup = new Setup({
        paths,
        cli,
        programs,
        keys,
        log,
        bundleVersion: version ?? 'unprepared',
        runtimeVersion: () => runtimeVersion,
        runtimeIsOurs: () => runtimeIsOurs,
        updateKey,
        settings: () => settings,
        // The mark of a profile being made for the agents' roles, kept with the rest of this run's own settings.
        saveSettings: (next) => {
            settings = next;
            writeSettings(paths.settings, next);
        },
        models,
        // Claude Code's and Codex's own files are where this process's environment and home say; only names leave them.
        ownServers: { env: process.env, home },
        // A box is closed, and read as a no, when its window is hidden or closed.
        confirm: (box) =>
            askBox(current, (window, options, signal) => dialog.showMessageBox(window, { ...options, buttons: [...options.buttons], signal }), box, (window, abort) => {
                window.once('hide', abort);
                window.once('close', abort);
                return () => {
                    window.removeListener('hide', abort);
                    window.removeListener('close', abort);
                };
            }),
        // A link the Reply CLI made goes from here to the person's browser, the navigation lock's one way out, and is
        // kept nowhere.
        openLink: toBrowser,
        // A link the person asked to hand on goes from here to the clipboard, and is neither kept nor logged.
        copyLink: (link) => clipboard.writeText(link),
    });
    registerSetup(seam, setup, origin, log, () => toPage('setup.changed'));
    // The agents' roles are put on the AI the chat runs on, in Live alone: at a start once the runtime is up, with the host
    // the chat holds, and as each turn starts on its host (`Setup.connectRoles`). Nothing it meets stops the start or the turn.
    const roles = rolesMoments(runOn.mode, {
        connect: (chosen) => setup.connectRoles(chosen),
        chosen: () => chat.chosenHost(),
        failed: () => log.warn('roles_profile', { Outcome: 'failed' }),
    });
    const turns = loggedTurns(log);
    // A start holds it while it deploys skills into the chat folder; a send waits for it (`SkillsGate`).
    const skillsGate = new SkillsGate();
    const chat = new Chat({
        paths,
        env,
        bundle,
        dataNamed,
        bundleVersion: version,
        cli,
        programs,
        models: () => models.lists(),
        keyFor: (id) => keys.keyFor(id),
        fence: chatFence,
        skillsGate,
        // The Open folder button of the notice that says a Full access grant's folder is kept: the file manager shows the
        // folder where it stands. Nothing is run or opened with a program.
        openFolder: (path) => shell.showItemInFolder(path),
        emit: (chatId, event) => toPage('chat.event', chatId, event),
        // The desktop asks the person itself whenever Full access is chosen with no standing choice: the page cannot answer
        // for them, nor give the standing choice. This time and Always say yes; Cancel is the default and Escape answers
        // it. With no window there is no one to ask.
        confirmFullAccess: async () => {
            const parent = current;
            if (parent === null) return null;
            const { response, checkboxChecked } = await dialog.showMessageBox(parent, fullAccessDialog());
            return fullAccessAnswer(response, checkboxChecked);
        },
        ...turns,
        started: (host, resumed, mode) => {
            turns.started(host, resumed, mode);
            roles.atTurn(host);
        },
        // The menu and the System screen follow the standing choice wherever it changed; the log hears it too.
        standingChanged: (standing, why) => {
            turns.standingChanged(standing, why);
            toPage('fullAccess.changed', standing);
        },
    });
    registerChat(seam, chat, origin, (channel) => log.warn('ipc_refused', { Channel: channel, Origin: origin() ?? 'none' }));
    // "Open in": the page names a place and, with a campaign's chat open, the campaign; the folder is read here from the
    // installation's own record, and an editor, the file manager or a terminal is started from here (`open-in.ts`). The
    // place used last is kept in this run's own settings. A chat is continued in a terminal by the session its record keeps,
    // in this run's chat folder, by this run's CLI. "Change Jason itself" is offered in the person's own installation,
    // which this run is. Each application the menu offers is drawn with its own icon where one can be read: the pixels are
    // read here, by the system on Windows and from the bundle's own files on macOS, and kept for the run in memory only.
    const icons = new IconReader({
        platform: process.platform,
        stat: (path) => stat(path),
        readAtMost: (path, cap) => readRegularFileAtMost(path, cap),
        fileIcon: (path) => app.getFileIcon(path, { size: 'normal' }),
        fromBuffer: (png) => nativeImage.createFromBuffer(png),
    });
    const openIn = new OpenIn({
        probes: systemProbes(env),
        hosts: () => ({ claude: programs.find('claude') !== null, codex: programs.find('codex') !== null }),
        cli,
        harnessRoot: () => harnessRoot(process.env, homedir(), dataLayout(runOn.person).userSettingsFile),
        isDirectory: isFolder,
        remembered: () => settings.openIn ?? null,
        remember: (id: PlaceId) => {
            settings = { ...settings, openIn: id };
            try {
                writeSettings(paths.settings, settings);
            } catch {
                log.warn('open_in_remember_failed', {});
            }
        },
        // An editor or the file manager keeps the environment it is started with for its whole life: the person's. The
        // terminals are started by `jason session open` and keep the run's.
        start: (line) => startDetached(line, editorEnvironment(env, personEnvironment, process.platform)),
        copy: (text) => clipboard.writeText(text),
        terminalFlags,
        // The chat in a tab of the system's browser: the bridge is opened on the first ask, and the link is the one the CLI
        // printed for the runtime's own page, with the bridge's code in its fragment (`browser-bridge.ts`).
        browser: () => openTab({ origin, cli, bridge: () => bridges.ensure(), open: ownPage, log }),
        log: (event, fields) => log.info(event, fields),
        continuable: (chatId) => chat.continuable(chatId),
        chatFolder: () => paths.chat,
        continueFlags,
        logContinue: (event, fields) => log.info(event, fields),
        // Which `jason` the PATH an editor is started with finds, read from the PATH command's own look; none told where
        // this run offers no command (the demo, a start that carries no copy).
        ...(command === null ? {} : { jason: () => command.onPath() }),
        icon: (path) => icons.icon(path),
        changeJason: {
            flags: changeJasonFlags,
            log: (event: 'open_in_change_jason' | 'open_in_change_jason_refused', fields: { Host: string; Exit: number; Code?: string }) =>
                event === 'open_in_change_jason' ? log.info(event, { Host: fields.Host, Exit: fields.Exit }) : log.info(event, { Host: fields.Host, Exit: fields.Exit, Code: fields.Code ?? 'none' }),
        },
    });
    handle(seam, 'openIn.targets', origin, (campaignId, chatId) => openIn.targets(campaignId, chatId), refused);
    handle(seam, 'openIn.open', origin, (target, campaignId) => openIn.open(target, campaignId), refused);
    handle(seam, 'openIn.continueChat', origin, (chatId) => openIn.continueChat(chatId), refused);
    handle(seam, 'openIn.changeJason', origin, () => openIn.changeJason(), refused);
    // A turn never outlives the window: closing it stops every host and forgets every "always".
    // Nor does a listing of Codex's models: it is ended the way a turn's host is, and the asks that wait are dropped.
    // Nor does a browser tab's bridge: its sessions end with it.
    window.on('closed', () => {
        chat.closeAll();
        models.close();
        void bridges.close();
    });
    app.on('before-quit', () => {
        chat.closeAll();
        models.close();
        void bridges.close();
    });

    await showStarting(window, 'Starting Jason…', false);
    if (uncopied !== null) {
        desk.notStarted();
        await showStarting(window, uncopied, true);
        return;
    }
    if (version === null) {
        desk.notStarted();
        await showStarting(window, 'This build carries no jason executable. Run npm run prepare:bundle in desktop/, then start it again.', true);
        return;
    }

    try {
        const started = await startInstallation({
            cli,
            paths,
            log,
            programs,
            bundle,
            bundleVersion: version,
            gate: skillsGate,
            settings,
            saveSettings(next) {
                settings = next;
                writeSettings(paths.settings, next);
            },
        });
        runtimeVersion = started.runtimeVersion;
        // Which way to one version the system screen names: the desktop's own update when the runtime runs from the jason
        // it carries, which `jason update` refuses to change.
        if (started.versionsDiffer) runtimeIsOurs = (await whoseRuntime(ownRuntimeDeps(process.platform, executable, paths.runFile, cli))).whose === 'ours';
        // What an earlier version's page kept on disk is unpaired and removed first, once (`window-pairing.ts`).
        const kept = readWindowPairing(paths.windowPairing);
        const { cleared } = await loadWindow(
            { cleared: kept.cleared, found: earlierPageWasFound },
            {
                cli,
                clearSession: async () => {
                    await session.defaultSession.clearStorageData();
                    await session.defaultSession.clearCache();
                },
                remove: (folder) => rmSync(folder, { recursive: true, force: true }),
                electron: paths.electron,
                log,
            },
            () => window.loadURL(started.link),
        );
        if (cleared !== kept.cleared) {
            try {
                writeWindowPairing(paths.windowPairing, { ...readWindowPairing(paths.windowPairing), cleared });
            } catch (error) {
                log.warn('page_storage_clear_failed', { Step: 'record', Code: (error as { code?: string }).code ?? 'unknown' });
            }
        }
        log.info('window_loaded', { Origin: started.origin });
        roles.atStart();
        settleStart(rollback, app.getVersion(), true, log);
        repairer.pairedWith(readText(paths.runFile));
        begin();
    } catch (error) {
        desk.notStarted();
        const step = error instanceof StartFailed ? error.step : 'load';
        const exit = error instanceof StartFailed ? error.exit : -1;
        log.error('window_load_failed', { Step: step, Exit: exit });
        const layoutRefused = error instanceof LayoutRefused;
        const kept = settleFailedStart(rollback, app.getVersion(), layoutRefused, log);
        const said = error instanceof Error ? error.message : String(error);
        const notice = failedStartNotice(
            said,
            layoutRefused,
            kept === null ? null : kept.directory === null ? { release: previousRelease(kept.fromVersion), file: debFile } : { copy: kept.directory },
            aside ?? applicationFolder(process.platform, process.execPath),
            process.platform,
        );
        if (current !== null) await showStarting(current, notice, true);
    }
    if (otherCopy !== null && version !== null) {
        const named = async (): Promise<readonly string[] | null> => {
            const registration = autostart === null ? { registered: false, executable: null } : await autostart.registration();
            if (registration === null) return null;
            return [...(registration.registered && registration.executable !== null ? [registration.executable] : []), ...(command?.named() ?? [])];
        };
        void repointed
            .then(() =>
                removeOtherCopies({
                    bundles: paths.bundle,
                    version,
                    keep: rollbackVersions(readRollback(paths.rollback)),
                    named,
                    whose: async (other) => (await whoseRuntime(ownRuntimeDeps(process.platform, other, paths.runFile, cli))).whose,
                    log,
                }),
            )
            .catch(() => log.error('bundle_copy_removal_failed', {}));
    }
}

/**
 * The Demo's run: the window, its tray and menu, and the page the application carries with our Demo | Live switch over
 * it (`demo-window.ts`, sealed by `demo-page.ts`). It has nothing to do with any installation: it makes no jason, starts
 * and stops no runtime, reads no shell, asks no program anything and writes nothing but the desktop's own log, and the
 * window's own contents have no preload and load nothing but the starting page when the page cannot be shown. A link
 * that arrives waits, as it waits for any page, and only brings the window forward.
 */
async function runDemo(_links: LinkPost): Promise<void> {
    const paths: DesktopPaths = desktopPaths(runOn.person);
    const log = fileLogger(paths.logs);
    const form = linuxForm();
    log.info('desktop_started', { Version: bundleVersion(bundleDirectory()) ?? 'unprepared', Platform: process.platform, DataDirectory: paths.data });
    // The page is drawn light, as it was built; the person's choice of theme is neither read nor written here.
    nativeTheme.themeSource = 'light';
    const conduct = windowConduct(process.platform);
    const atLogin = openedAtLogin(process.argv);
    const window = new BrowserWindow({
        width: 1280,
        height: 860,
        minWidth: 900,
        minHeight: 600,
        title: windowTitle,
        // The window's own frame: on macOS no title bar, its buttons in our band above the page (`demo-window.ts`); on
        // Windows and Linux none either, the caption buttons over the strip's right end, drawn light as the page is.
        ...frameOf(process.platform, nativeTheme.shouldUseDarkColors),
        show: !atLogin || conduct.atLogin === 'minimized',
        backgroundColor: lightBackground,
        webPreferences: {
            // Nothing is put into the window's pages: nothing in the Demo's window reaches the main process.
            partition: windowPartition,
            contextIsolation: true,
            nodeIntegration: false,
            sandbox: true,
            webSecurity: true,
            spellcheck: false,
            devTools: !app.isPackaged,
        },
    });
    let current: BrowserWindow | null = window;
    window.on('closed', () => {
        current = null;
    });
    keepWindowTitle(window);
    if (atLogin && conduct.atLogin === 'minimized') window.minimize();
    window.setMenuBarVisibility(false);
    // The tray keeps the application, as in Live: closing the window hides it; on Linux closing it quits.
    let quitting = false;
    app.on('before-quit', () => {
        quitting = true;
    });
    window.on('close', (event) => {
        if (quitting || conduct.closeQuits) return;
        event.preventDefault();
        window.hide();
    });
    // The window's own contents, under the two views, go nowhere, open nothing and are granted nothing.
    window.webContents.on('will-navigate', (event) => event.preventDefault());
    window.webContents.on('will-attach-webview', (event) => event.preventDefault());
    window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
    window.webContents.session.setPermissionRequestHandler((_contents, _permission, callback) => callback(false));
    window.webContents.session.setPermissionCheckHandler(() => false);

    const modeDeps = modeSwitch(log, form);
    // The Demo runs no jason: the tray's count is never read here, and anything that asked would be refused.
    const noJason: Cli = { run: () => Promise.reject(new Error('The demo runs no jason.')) };
    let views: DemoViews | null = null;
    const login = new LoginItem({ platform: process.platform, home: homedir(), executable: form === null ? process.execPath : launcherOf(form, process.execPath), electron: app, pathRead: Promise.resolve(), log, env: process.env });
    const desk = new Desk({ window: () => current, origin: () => null, cli: noJason, log, reload: () => views?.reload(), login, autostart: null, updates: null, kept: () => null, command: null, removal: null, demo: demoMenu('demo'), back: () => backToLive(modeDeps) });
    desk.open();
    showWindow = () => desk.show();

    const unshown = demoUnshownSentence(process.platform);
    try {
        views = await openDemo(window, {
            appPath: app.getAppPath(),
            platform: process.platform,
            refused: (view, kind) => log.warn('demo_refused', { View: view, Kind: kind }),
            live: () => backToLive(modeDeps),
            open: () => openDemoRepository(log),
            unshown: () => {
                log.error('demo_unshown', {});
                if (current !== null) void showStarting(current, unshown, true);
            },
        });
        log.info('demo_shown', {});
    } catch {
        log.error('demo_unshown', {});
        if (current !== null) await showStarting(current, unshown, true);
    }
}

// Electron's own files follow the data directory, before anything opens: its profile, the page's storage and its own
// logs are under <data>/desktop/electron, never the account's application data, so a second installation (one
// JASON_DATA_DIR names) is a second desktop with its own lock, and a purge of the data directory takes all of it. Every
// start sets them before Electron is ready, a refused one too.
function keepElectronUnderData(): void {
    const early = desktopPaths(dataDirectory(process.env, homedir()));
    app.setPath('userData', early.electron);
    app.setPath('sessionData', early.electron);
    app.setAppLogsPath(early.electronLogs);
}

// Never without Chromium's sandbox: the first thing the main process decides, on every platform. A start that carries
// --no-sandbox, which an AppImage's launcher adds where the system refuses the sandbox, says why once Electron is ready
// and quits, with no lock taken, no copy made, no jason started and no window made (`sandbox.ts`).
const unsandboxed = sandboxRefusal(app.commandLine.hasSwitch('no-sandbox'), process.argv, linuxForm());

// Which installation this run is on, what the person's environment said, whether the installation is new and whether an
// earlier page left its storage: decided once by `chooseInstallation`, after the sandbox and the link, before anything
// else of a start resolves a path, and read only by what runs after it.
let runOn!: StartRun;
let personEnvironment!: PersonEnvironment;
let installationWasNew!: boolean;
let earlierPageWasFound!: boolean;
if (unsandboxed !== null) {
    keepElectronUnderData();
    void refuseStart(unsandboxed, {
        whenReady: () => app.whenReady(),
        logger: () => fileLogger(desktopPaths(dataDirectory(process.env, homedir())).logs),
        showErrorBox: (title, text) => dialog.showErrorBox(title, text),
        exit: (code) => app.exit(code),
    });
} else {
    startDesktop();
}

/**
 * The switch between Live and Demo, the page's and the menu's: the record written in the person's directory, then the
 * desktop started again with the person's environment, which a run no longer changes (`relaunchAsPerson`).
 */
function modeSwitch(log: Logger, form: LinuxPackage | null): { write(mode: Mode): void; relaunch(): void } {
    return {
        write: (mode: Mode) => {
            try {
                writeMode(desktopPaths(runOn.person).mode, mode);
            } catch (error) {
                log.warn('mode_write_failed', { Mode: mode });
                throw error;
            }
            log.info('mode_set', { Mode: mode });
        },
        relaunch: () =>
            relaunchAsPerson({
                env: process.env,
                person: personEnvironment,
                relaunch: () => app.relaunch(relaunchOptions(process.argv, app.isPackaged, form === null ? process.execPath : launcherOf(form, process.execPath))),
                quit: () => app.quit(),
            }),
    };
}

/** Back to Your Own Installation, in a demo run's tray and menu: what Live on the switch does. */
function backToLive(deps: { write(mode: Mode): void; relaunch(): void }): void {
    const refusal = applyMode('live', deps);
    if (refusal !== null) dialog.showErrorBox(windowTitle, refusal.refused);
}

/**
 * Which installation this run is on (`mode.ts`): the record in the person's directory says Live or Demo, and both run on
 * the person's directory, where the desktop keeps its own records (the mode, Electron's profile, its log). Demo is the
 * page the application carries and starts, makes or touches nothing else: no runtime, no folder of the earlier demo. The
 * installer's and the uninstaller's starts ignore the record.
 */
function chooseInstallation(installerStart: boolean): void {
    const personDirectory = dataDirectory(process.env, homedir());
    // Whether this installation is new is read first, of the person's directory: before this start writes anything there,
    // before Electron makes its profile, before the single-instance lock. A new installation's first start opens on the
    // demo (`keepFirstStart`), the record being the one thing written in the person's directory before they choose Live.
    installationWasNew = isNewInstallation(personDirectory);
    keepFirstStart(desktopPaths(personDirectory).mode, installationWasNew, installerStart);
    runOn = startRun(personDirectory, readMode(desktopPaths(personDirectory).mode), installerStart);
    // What the person's environment said, put back before the desktop starts itself again (`relaunchAsPerson`).
    personEnvironment = personEnvironmentOf(process.env);
    // Whether an earlier version's page left its storage on disk, read before Electron is ready (`window-pairing.ts`).
    earlierPageWasFound = earlierPageFound(desktopPaths(personDirectory).electron);
}

function startDesktop(): void {
    // A start that carries a link carries it alone (`linkIn`): one with anything beside it, a flag of the desktop's own
    // or a switch a crafted address put there, does nothing and quits, before any other branch of a start is decided,
    // the choice of installation included.
    const fromArgs = linkIn(process.argv.slice(1), process.platform);
    if (fromArgs.kind === 'refused') {
        keepElectronUnderData();
        startLog().warn('deep_link_refused', { Code: fromArgs.reason });
        app.exit(1);
        return;
    }

    const uninstalling = uninstallStart(process.argv, { packaged: app.isPackaged, platform: process.platform });
    const stoppingRuntime = uninstalling === null && stopRuntimeStart(process.argv, { packaged: app.isPackaged, platform: process.platform });
    chooseInstallation(uninstalling !== null || stoppingRuntime);
    keepElectronUnderData();
    if (uninstalling !== null) {
        // The nsis uninstaller's start: it takes off what the desktop placed and quits, and opens no window.
        // Its exit code tells the uninstaller whether to go on and remove the files (`uninstallExit`).
        void app.whenReady().then(() => runUninstall(uninstalling.quiet)).then((code) => app.exit(code), () => app.exit(2));
    } else if (stoppingRuntime) {
        // The nsis installer's and uninstaller's start when a runtime runs from the carried jason.exe: it stops that
        // runtime through jason runtime stop and exits, and opens no window; the installer looks again rather than read
        // the code.
        void app.whenReady().then(runStopRuntime).then((code) => app.exit(code), () => app.exit(2));
    } else if (!app.requestSingleInstanceLock(fromArgs.kind === 'link' ? { link: fromArgs.raw } : undefined)) {
        app.quit();
    } else {
        const early = startLog();
        // Starting Jason again while it runs, or clicking it in the Dock, brings the window out of the tray. A second
        // start's link comes in its additionalData, which Electron passes as it was given; its argv may be reordered or
        // carry what Chromium added (electron.d.ts, `second-instance`).
        // Where a link waits for the page (`deep-link.ts`): sent to the window's page once that page has taken its links.
        const links = new LinkPost((target) => BrowserWindow.getAllWindows()[0]?.webContents.send(eventChannels['links.open'], target));
        app.on('second-instance', (_event, argv, _directory, data) => {
            const link = linkOf(data);
            if (link !== null) return linkArrived(link, links, early);
            if (showsOnSecondStart(argv)) showWindow();
        });
        // macOS hands a link by open-url, at a cold start before ready as at a running one, so it is listened for now.
        app.on('open-url', (event, url) => {
            event.preventDefault();
            linkArrived(url, links, early);
        });
        if (fromArgs.kind === 'link') linkArrived(fromArgs.raw, links, early);
        app.on('activate', () => showWindow());
        app.on('window-all-closed', () => app.quit());
        // A Demo start, before Electron is ready: its two schemes, its page's and our switch's, are known, and no host name
        // is resolved for the rest of the run (`demo-page.ts`). A Live start is a start of its own and sets neither.
        if (runOn.mode === 'demo') demoBeforeReady(app.commandLine, protocol);
        void app.whenReady().then(() => (runOn.mode === 'demo' ? runDemo(links) : run(links, installationWasNew)));
    }
}
