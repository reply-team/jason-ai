/**
 * Jason Desktop's main process: a window onto the UI the installation's runtime serves at its loopback address, and the
 * Connect cards' actions behind it. It is one more client of the installation the `jason` command line uses: it finds the
 * data directory as the CLI does, brings the runtime up only through `jason runtime start`, and changes the installation
 * only through CLI verbs and the settings file a person would edit.
 */

import { app, BrowserWindow, clipboard, dialog, ipcMain, nativeTheme, safeStorage, session, shell, type WebContents } from 'electron';
import { autoUpdater } from 'electron-updater';
import { existsSync, readFileSync, rmSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { Chat } from './chat.ts';
import { fenceRefusal, readFence, type ChatFence } from './chat-deny.ts';
import { registerChat } from './chat-ipc.ts';
import { loggedModels, loggedTurns } from './chat-log.ts';
import { fullAccessAnswer, fullAccessDialog } from './chat-modes.ts';
import { carriedOf, isOtherCopy, removeOtherCopies } from './bundle-copy.ts';
import { cliAt } from './cli.ts';
import { linkIn, LinkPost, linkRegistration, linksTaken, parseLink } from './deep-link.ts';
import { Desk } from './desk.ts';
import { bringForward, LoginItem, openedAtLogin, RuntimeAutostart, showsOnSecondStart, windowConduct } from './login-item.ts';
import { keptAsideOf, launcherOf, linuxPackage, mountOf, withoutMount, type LinuxPackage } from './linux-package.ts';
import { commandFolder, isProgramFile, PathCommand, type PathCommandDeps } from './path-command.ts';
import { ownRuntimeDeps, stopOwnRuntime, whoseRuntime } from './own-runtime.ts';
import { atFirstPairing, Repairer } from './repair.ts';
import { applicationFolder, keptLine, keptNotice, readRollback, rollbackVersions, settleStart } from './rollback.ts';
import { refuseStart, sandboxRefusal } from './sandbox.ts';
import { debFile, previousRelease, Updater } from './updates.ts';
import { loopbackOrigin, runtimeOrigin } from './descriptor.ts';
import { sharingVersionReads } from './host-version.ts';
import { loggedCli, startInstallation, StartFailed } from './installation.ts';
import { eventChannels, handle, type InvokeMember } from './ipc.ts';
import { fileLogger, limitsFrom, type Logger } from './log.ts';
import { dataDirectory, desktopPaths, type DesktopPaths } from './paths.ts';
import { demoPlaces, placesProblem, preparingPage, readyTheSample, seedDepsOnDisk, startDemoOver, type DemoPlaces } from './demo-seed.ts';
import { carryOutOnDisk, CarryOutRefused, carryOutStartOver, requestStartOver } from './demo-over.ts';
import { demoRuntimeAlive } from './demo-alive.ts';
import { processIsRunning } from './own-runtime.ts';
import { applyMode, demoDirectory, demoMenu, demoUnopened, enterRun, offersOf, readMode, relaunchAsPerson, relaunchOptions, startOn, writeMode, type Mode, type ModeAnswer, type PersonEnvironment, type StartRun } from './mode.ts';
import { holdDemoQuit, stopDemoRuntime, stopWithDemo } from './demo-runtime.ts';
import { carriedDemoPlugin, placeDemoPlugin } from './demo-plugin.ts';
import { claimDemoFolder, hasDemoMarker } from './demo-folders.ts';
import { openDemoDirectory, prepareDemo, writeDemoRoute } from './demo-start.ts';
import { writeDemoSettings } from './demo-settings.ts';
import { permissionAllowed } from './permissions.ts';
import { pathOf, programsOn, withPath } from './programs.ts';
import { ProviderKeys } from './providers.ts';
import { readSettings, settingsAtStart, writeSettings, type DesktopSettings } from './settings.ts';
import { applyTheme, darkBackground, isNewInstallation, lightBackground, startingTheme } from './theme.ts';
import { Setup } from './setup.ts';
import { SkillsGate } from './skills-gate.ts';
import { askBox } from './reply-accounts.ts';
import { codexAsker, ModelLists } from './model-lists.ts';
import { loginPath } from './shell-path.ts';
import { placedRemoval, removalOffered } from './remove-placed.ts';
import { harnessRoot, processesRunFrom, stopRuntimeStart, uninstallStart } from './uninstall.ts';
import { runStopRuntime, runUninstall } from './uninstall-start.ts';
import { earlierPageFound, loadWindow, readWindowPairing, WindowPairing, windowPartition, writeWindowPairing } from './window-pairing.ts';

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

/** Shows the desktop's own page while the installation comes up, or why it did not. */
function showStarting(window: BrowserWindow, message: string, failed: boolean): Promise<void> {
    return window.loadFile(join(app.getAppPath(), 'shell', 'starting.html'), { query: { message, state: failed ? 'failed' : 'starting' } });
}

/**
 * Locks the window to the runtime's origin: a navigation elsewhere is refused, and a link or `window.open` that leaves
 * it goes to the system browser only when it is https. Of what the page asks for, only writing to the clipboard is
 * granted, and only at the runtime's origin; no other permission, no webview. Answers the one way to the system
 * browser, which the Reply card's links take too: it opens an https URL and refuses any other, logging neither.
 */
function lockNavigation(contents: WebContents, origin: () => string | null, log: Logger): (url: string) => Promise<void> {
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
    return leave;
}

function registerSetup(setup: Setup, origin: () => string | null, log: Logger, window: () => BrowserWindow | null): void {
    const refused = (channel: string) => log.warn('ipc_refused', { Channel: channel, Origin: origin() ?? 'none' });
    const members: [InvokeMember, (...args: unknown[]) => unknown][] = [
        ['setup.status', () => setup.status()],
        ['setup.connectReply', () => setup.connectReply()],
        ['setup.connectReplyWithKey', (key) => setup.connectReplyWithKey(key)],
        ['setup.setProviderKey', (id, key) => setup.setProviderKey(id, key)],
        ['setup.installProgram', (id) => setup.installProgram(id)],
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
    for (const [member, handler] of members) handle(ipcMain, member, origin, handler, refused);
    setup.onChanged(() => window()?.webContents.send(eventChannels['setup.changed']));
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
    const home = homedir();
    // The login shell's PATH is read before anything is started or registered, so every program is found as a terminal
    // finds it.
    const pathRead = loginPath(pathOf(process.env), home);
    process.env.PATH = await pathRead;
    const form = linuxForm();
    // Every child gets the person's PATH and libraries: nothing of an AppImage's mount, which the AppRun put on them.
    const env = withoutMount(withPath(process.env, process.env.PATH), mountOf(form, process.env));

    const paths: DesktopPaths = desktopPaths(dataDirectory(process.env, home));
    const log = fileLogger(paths.logs, limitsFrom(readText(paths.installationSettings)));
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
        title: 'Jason',
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
    if (atLogin && conduct.atLogin === 'minimized') window.minimize();
    window.setMenuBarVisibility(conduct.menuBar);
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
    const toBrowser = lockNavigation(window.webContents, origin, log);
    // A link waits for the page that takes it: a page that goes, by a load or a reload, takes no more until the next asks.
    handle(ipcMain, 'links.take', origin, () => links.take(), (channel) => log.warn('ipc_refused', { Channel: channel, Origin: origin() ?? 'none' }));
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
    // A demo run's quit, the switch back to Live included, first stops the demo's runtime, by the demo's run file and the
    // existing stop; the person's runtime is never stopped. A live run's quit leaves its runtime running, as ever.
    // The stop is waited for at most `demoStopCapMs`; the quit then goes on all the same.
    holdDemoQuit(app, runOn.mode, {
        stop: async () => {
            const outcome = await stopDemoRuntime({ platform: process.platform, executable, person: runOn.person, env });
            log.info('demo_runtime_stopped', { Outcome: outcome.kind });
        },
        timedOut: () => log.warn('demo_runtime_stop_timed_out', {}),
    });
    if (runOn.unopened !== null) log.warn('demo_unopened', {});
    if (startOverNotCarriedOut !== null) log.warn('demo_start_over_not_carried_out', { Cause: startOverNotCarriedOut });
    // The count and the re-pair loop begin at the first pairing that succeeds: the start's, or a Reload's after a start
    // that failed.
    const begin = atFirstPairing(() => {
        desk.watch();
        repairer.start();
    });
    const repairer = new Repairer({ readRunFile: () => readText(paths.runFile), cli, log, load: async (link) => void (await current?.loadURL(link)), paired: begin });
    // On Linux the session starts the launcher: the AppImage file itself, whose mount lasts only while it runs.
    const login = new LoginItem({ platform: process.platform, home, executable: form === null ? process.execPath : launcherOf(form, process.execPath), electron: app, pathRead, log, env: process.env });
    // A demo run writes nothing of the demo into what outlasts it: no update, no start at logon, no PATH command, no
    // Remove What Jason Placed; the menu says to go back to the person's own installation for them, and offers the way
    // back (`offersOf`, `demoMenu`).
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
              refused: (why) => dialog.showErrorBox('Jason', why),
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
    // Remove What Jason Placed, on macOS and Linux: the uninstall's own steps, over the same items the menu sets. Never in
    // a demo run: an act that removes an installation never runs with the demo's directory in this process's environment.
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
    // directory, also from a demo run, and the desktop then starts again on the installation it names; a write that fails
    // starts nothing.
    const modeDeps = {
        write: (mode: Mode) => {
            try {
                writeMode(desktopPaths(runOn.person).mode, mode);
            } catch (error) {
                log.warn('mode_write_failed', { Mode: mode });
                throw error;
            }
            log.info('mode_set', { Mode: mode });
        },
        // The new start inherits this process's environment: the person's is put back first (`relaunchAsPerson`).
        relaunch: () =>
            relaunchAsPerson({
                env: process.env,
                person: personEnvironment,
                relaunch: () => app.relaunch(relaunchOptions(process.argv, app.isPackaged, form === null ? process.execPath : launcherOf(form, process.execPath))),
                quit: () => app.quit(),
            }),
    };
    // Back to Your Own Installation, in a demo run's tray and menu: the way back that needs no page, for a demo whose
    // start failed.
    const back = () => {
        const refusal = applyMode('live', modeDeps);
        if (refusal !== null) dialog.showErrorBox('Jason', refusal.refused);
    };
    // Start the Demo Over: one question first (everything done in the demo goes), then the demo's runtime is stopped, a
    // request is left in the person's own desktop folder and the desktop starts again. This run lives inside the demo's
    // folders, so it removes none: the next start removes them before anything names the demo's directory
    // (`carryOutStartOver` in `chooseInstallation`). Refused in Live; the person's own installation is never touched.
    // True from the window opening until this run's own start of the runtime has answered: a stop in that window finds no run
    // file and says none, though a runtime is booting, so Start the Demo Over waits for the start.
    let startInFlight = true;
    const startOver = async (): Promise<{ refused: string } | null> => {
        if (runOn.mode !== 'demo') return { refused: 'Start the Demo Over is only in the demo.' };
        if (startInFlight) return { refused: 'The demo is still starting. Try again in a moment.' };
        const parent = current;
        const options = { type: 'question' as const, title: 'Jason', message: 'Start the demo over?', detail: 'Everything you did in the demo is removed, and the demo starts again from the beginning. Your own installation is not touched.', buttons: ['Start Over', 'Cancel'], defaultId: 1, cancelId: 1 };
        const { response } = parent === null ? await dialog.showMessageBox(options) : await dialog.showMessageBox(parent, options);
        if (response !== 0) return null;
        const outcome = await startDemoOver({
            places: demoWhere,
            exists: (path) => existsSync(path),
            marked: hasDemoMarker,
            stopRuntime: async () => {
                const stopped = await stopDemoRuntime({ platform: process.platform, executable, person: runOn.person, env });
                log.info('demo_runtime_stopped', { Outcome: stopped.kind });
                return stopped.kind === 'stopped' || stopped.kind === 'none';
            },
            request: () => requestStartOver(desktopPaths(runOn.person).demoStartOver),
            relaunch: () => modeDeps.relaunch(),
        });
        if (outcome.kind === 'refused') {
            log.warn('demo_start_over_refused', { Cause: outcome.reason });
            return { refused: outcome.why };
        }
        log.info('demo_start_over', {});
        return null;
    };
    const startOverFromMenu = () => {
        void startOver().then(
            (refusal) => {
                if (refusal !== null) dialog.showErrorBox('Jason', refusal.refused);
            },
            () => {
                log.warn('demo_start_over_refused', { Cause: 'threw' });
                dialog.showErrorBox('Jason', 'The demo could not be started over.');
            },
        );
    };
    const desk = new Desk({ window: () => current, origin, cli: jason, log, reload: () => void repairer.reload(), login, autostart, updates, kept: keptCopy, command, removal, demo: demoMenu(runOn.mode, removalOffered(process.platform)), back, startOver: startOverFromMenu });
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
        changed: () => current?.webContents.send(eventChannels['setup.changed']),
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
    handle(ipcMain, 'theme.get', origin, () => settings.theme, refused);
    handle(ipcMain, 'theme.set', origin, (choice) => void (settings = applyTheme(choice, settings, themeDeps)), refused);
    handle(ipcMain, 'mode.get', origin, (): ModeAnswer => ({ mode: runOn.mode, unopened: runOn.unopened }), refused);
    handle(ipcMain, 'mode.set', origin, (choice) => applyMode(choice, modeDeps), refused);
    handle(ipcMain, 'mode.startOver', origin, () => startOver(), refused);
    // The page reports the id of the UI token it holds, never the token; the one before it is revoked then.
    const windowPairing = new WindowPairing({
        cli,
        log,
        state: () => readWindowPairing(paths.windowPairing),
        save: (next) => writeWindowPairing(paths.windowPairing, next),
    });
    handle(ipcMain, 'pairing.paired', origin, (tokenId) => windowPairing.paired(tokenId), refused);
    registerSetup(
        new Setup({
            paths,
            cli,
            programs,
            keys,
            log,
            bundleVersion: version ?? 'unprepared',
            runtimeVersion: () => runtimeVersion,
            runtimeIsOurs: () => runtimeIsOurs,
            settings: () => settings,
            models,
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
        }),
        origin,
        log,
        () => current,
    );
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
        emit: (chatId, event) => current?.webContents.send(eventChannels['chat.event'], chatId, event),
        // The desktop asks the person itself whenever Full access is chosen with no standing choice: the page cannot answer
        // for them, nor give the standing choice. This time and Always say yes; Cancel is the default and Escape answers
        // it. With no window there is no one to ask.
        confirmFullAccess: async () => {
            const parent = current;
            if (parent === null) return null;
            const { response, checkboxChecked } = await dialog.showMessageBox(parent, fullAccessDialog());
            return fullAccessAnswer(response, checkboxChecked);
        },
        ...loggedTurns(log),
        // The menu and the System screen follow the standing choice wherever it changed; the log hears it too.
        standingChanged: (standing, why) => {
            loggedTurns(log).standingChanged(standing, why);
            current?.webContents.send(eventChannels['fullAccess.changed'], standing);
        },
    });
    registerChat(ipcMain, chat, origin, (channel) => log.warn('ipc_refused', { Channel: channel, Origin: origin() ?? 'none' }));
    // A turn never outlives the window: closing it stops every host and forgets every "always".
    // Nor does a listing of Codex's models: it is ended the way a turn's host is, and the asks that wait are dropped.
    window.on('closed', () => {
        chat.closeAll();
        models.close();
    });
    app.on('before-quit', () => {
        chat.closeAll();
        models.close();
    });

    await showStarting(window, 'Starting Jason…', false);
    if (uncopied !== null) {
        startInFlight = false;
        desk.notStarted();
        await showStarting(window, uncopied, true);
        return;
    }
    if (version === null) {
        startInFlight = false;
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
        // A demo's runtime is up: a demo with no sample gets it made, behind the preparing page, and every start cancels the
        // open AI-role reviews, because no AI role runs in the demo.
        if (runOn.mode === 'demo') {
            const ready = await readyTheSample(
                seedDepsOnDisk({ cli, places: demoWhere, scratch: join(paths.data, 'seed-scratch'), progress: (step) => log.info('demo_seed_step', { Step: step }) }),
                () => window.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(preparingPage())}`),
            );
            if (ready.kind === 'failed') {
                log.error('demo_seed_failed', { Step: ready.step ?? 'marker', Code: ready.code ?? 'none' });
                throw new Error(ready.why);
            }
        }
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
        settleStart(rollback, app.getVersion(), true, log);
        repairer.pairedWith(readText(paths.runFile));
        begin();
    } catch (error) {
        desk.notStarted();
        const step = error instanceof StartFailed ? error.step : 'load';
        const exit = error instanceof StartFailed ? error.exit : -1;
        log.error('window_load_failed', { Step: step, Exit: exit });
        const kept = settleStart(rollback, app.getVersion(), false, log);
        const said = error instanceof Error ? error.message : String(error);
        const notice =
            kept === null
                ? said
                : keptNotice(said, kept.directory === null ? { release: previousRelease(kept.fromVersion), file: debFile } : { copy: kept.directory }, aside ?? applicationFolder(process.platform, process.execPath), process.platform);
        if (current !== null) await showStarting(current, notice, true);
    } finally {
        startInFlight = false;
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
let demoWhere!: DemoPlaces;
/** Why the start over the run before asked for was not carried out at this start, as a code; null when nothing was refused. */
let startOverNotCarriedOut: string | null = null;
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
 * Which installation this run is on (`mode.ts`): the record in the person's directory says Live or Demo, and for Demo
 * the demo's directory becomes this run's data directory, so the profile, the lock, the logs, the runtime it starts, the
 * chat's hosts and every jason verb are the demo installation's by the rule they already follow. The installer's and
 * the uninstaller's starts ignore the record. A demo directory that cannot be made, used or prepared makes this run the
 * person's own: the record goes back to Live, and the first screen says the demo could not be opened. Before the demo's
 * runtime is ever started, its settings are written into its own settings file (`prepare`, the seam for whatever else
 * the demo installation must hold before it runs); the harness root is a sibling of the person's.
 */
function chooseInstallation(installerStart: boolean): void {
    const personDirectory = dataDirectory(process.env, homedir());
    const personHarness = harnessRoot(process.env, homedir(), desktopPaths(personDirectory).installationSettings);
    demoWhere = demoPlaces(personDirectory, personHarness);
    // Before the demo's directory is claimed, and before anything of this run names it, a start over asked by the run
    // before is carried out (`demo-over.ts`), and a demo whose places break the rule is refused. Either makes this run the
    // person's own, as an unusable directory does; a failed removal keeps its request for the next demo start.
    let refused = false;
    if (readMode(desktopPaths(personDirectory).mode) === 'demo' && !installerStart) {
        try {
            const problem = placesProblem(demoWhere, personDirectory, personHarness);
            if (problem !== null) throw new CarryOutRefused('places', problem);
            carryOutStartOver({
                ...carryOutOnDisk(desktopPaths(personDirectory).demoStartOver, demoWhere, processIsRunning),
                runtimeAlive: () => demoRuntimeAlive(desktopPaths(demoWhere.data).runFile),
                exists: (folder) => existsSync(folder),
                marked: hasDemoMarker,
            });
        } catch (error) {
            refused = true;
            startOverNotCarriedOut = error instanceof CarryOutRefused ? error.reason : 'failed';
            try {
                writeMode(desktopPaths(personDirectory).mode, 'live');
            } catch {
                // The record stays as it was; this start is the person's all the same.
            }
        }
    }
    runOn = startOn(personDirectory, readMode(desktopPaths(personDirectory).mode), installerStart, {
        open: openDemoDirectory,
        // The demo's start-up order in one place (`demo-start.ts`): the provider and its route, then the fixed settings. It runs
        // after the directory is made and before any runtime start, and a step that fails makes this run the person's own.
        prepare: (demo) =>
            prepareDemo(demo, {
                harnessRoot: () => claimDemoFolder(demoDirectory(harnessRoot(process.env, homedir(), desktopPaths(personDirectory).installationSettings))),
                place: (data) => placeDemoPlugin(carriedDemoPlugin(bundleDirectory()), data, personDirectory),
                route: writeDemoRoute,
                fixedSettings: (data) => writeDemoSettings(data, harnessRoot(process.env, homedir(), desktopPaths(personDirectory).installationSettings)),
                runtimeAlive: (data) => demoRuntimeAlive(desktopPaths(data).runFile),
            }),
        keep: (mode) => writeMode(desktopPaths(personDirectory).mode, mode),
    });
    if (refused && runOn.mode === 'live') runOn = { ...runOn, unopened: demoUnopened };
    // What the person's environment said, put back before the desktop starts itself again (`relaunchAsPerson`).
    personEnvironment = enterRun(process.env, runOn);
    // Whether this installation is new is read next: before Electron makes its profile under the data directory, before
    // the single-instance lock, before anything is written there.
    installationWasNew = isNewInstallation(desktopPaths(dataDirectory(process.env, homedir())).data);
    // Whether an earlier version's page left its storage on disk, read before Electron is ready (`window-pairing.ts`).
    earlierPageWasFound = earlierPageFound(desktopPaths(dataDirectory(process.env, homedir())).electron);
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
    const early = startLog();
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
        void app.whenReady().then(() => run(links, installationWasNew));
    }
}
