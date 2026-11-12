/**
 * Jason Desktop's main process: a window onto the UI the installation's runtime serves at its loopback address, and the
 * Connect cards' actions behind it. It is one more client of the installation the `jason` command line uses: it finds the
 * data directory as the CLI does, brings the runtime up only through `jason runtime start`, and changes the installation
 * only through CLI verbs and the settings file a person would edit.
 */

import { app, BrowserWindow, dialog, ipcMain, nativeTheme, safeStorage, shell, type WebContents } from 'electron';
import { autoUpdater } from 'electron-updater';
import { existsSync, readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { Chat } from './chat.ts';
import { fenceRefusal, readFence, type ChatFence } from './chat-deny.ts';
import { registerChat } from './chat-ipc.ts';
import { loggedModels, loggedTurns } from './chat-log.ts';
import { carriedOf, isOtherCopy, removeOtherCopies } from './bundle-copy.ts';
import { cliAt } from './cli.ts';
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
import { permissionAllowed } from './permissions.ts';
import { pathOf, programsOn, withPath } from './programs.ts';
import { ProviderKeys } from './providers.ts';
import { readSettings, writeSettings, type DesktopSettings } from './settings.ts';
import { applyTheme, darkBackground, isNewInstallation, lightBackground, startingTheme } from './theme.ts';
import { Setup } from './setup.ts';
import { codexAsker, ModelLists } from './model-lists.ts';
import { loginPath } from './shell-path.ts';
import { placedRemoval, removalOffered } from './remove-placed.ts';
import { processesRunFrom, stopRuntimeStart, uninstallStart } from './uninstall.ts';
import { runStopRuntime, runUninstall } from './uninstall-start.ts';

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
 * granted, and only at the runtime's origin; no other permission, no webview.
 */
function lockNavigation(contents: WebContents, origin: () => string | null, log: Logger): void {
    const leave = (url: string) => {
        if (isHttps(url)) {
            void shell.openExternal(url);
            log.info('link_opened', {});
        } else {
            log.warn('link_refused', {});
        }
    };
    const guard = (event: { preventDefault(): void }, url: string) => {
        const here = origin();
        if (here !== null && originOf(url) === here) return;
        event.preventDefault();
        log.warn('navigation_refused', { Origin: here ?? 'none' });
        leave(url);
    };
    contents.on('will-navigate', guard);
    contents.on('will-redirect', guard);
    contents.on('will-attach-webview', (event) => event.preventDefault());
    contents.setWindowOpenHandler(({ url }) => {
        leave(url);
        return { action: 'deny' };
    });
    contents.session.setPermissionRequestHandler((_contents, permission, callback, details) => callback(permissionAllowed(permission, details.requestingUrl, origin())));
    contents.session.setPermissionCheckHandler((_contents, permission, requestingOrigin) => permissionAllowed(permission, requestingOrigin, origin()));
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
        // The links answer to this window alone, on the person's click: never logged, never kept.
        ['setup.replyLinkedInConnect', (name) => setup.replyLinkedInConnect(name)],
        ['setup.replyLinkedInPendingLink', (id) => setup.replyLinkedInPendingLink(id)],
        ['setup.replyLinkedInReconnect', (id) => setup.replyLinkedInReconnect(id)],
        ['setup.replyMailboxLink', (page) => setup.replyMailboxLink(page)],
    ];
    for (const [member, handler] of members) handle(ipcMain, member, origin, handler, refused);
    setup.onChanged(() => window()?.webContents.send(eventChannels['setup.changed']));
}

/** Brings the window forward; the desk's own once it is open. */
let showWindow: () => void = () => bringForward(BrowserWindow.getAllWindows()[0] ?? null);

async function run(newInstallation: boolean): Promise<void> {
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
    lockNavigation(window.webContents, origin, log);

    let settings: DesktopSettings = readSettings(paths.settings);
    // A new installation meets Light, and is told so in its settings, so its second start is not an existing one's.
    if (newInstallation && settings.theme === null) {
        settings = { ...settings, theme: 'light' };
        writeSettings(paths.settings, settings);
    }
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
    const autostart = version === null || !carried.outlives ? null : new RuntimeAutostart({ cli, log });
    // The updater runs only in an installed build: a development build has no installer to hand an update to.
    const rollback = paths.rollback;
    // What an update keeps aside to go back to: the AppImage file on Linux, nothing for the deb.
    const aside = keptAsideOf(process.platform, form, process.execPath);
    const updates = app.isPackaged
        ? new Updater({
              updater: autoUpdater,
              version: app.getVersion(),
              application: aside,
              rollback,
              log,
              stopRuntime: () => stopOwnRuntime(ownRuntimeDeps(process.platform, executable, paths.runFile, cli)),
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
    const command = carried.outlives ? new PathCommand(commandDeps) : null;
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
    // Remove What Jason Placed, on macOS and Linux: the uninstall's own steps, over the same items the menu sets.
    const removal = removalOffered(process.platform)
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
              runsFrom: (folder) => processesRunFrom(folder),
              log,
          })
        : null;
    const desk = new Desk({ window: () => current, origin, cli: jason, log, reload: () => void repairer.reload(), login, autostart, updates, kept: keptCopy, command, removal });
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
    const refusedTheme = (channel: string) => log.warn('ipc_refused', { Channel: channel, Origin: origin() ?? 'none' });
    const themeDeps = {
        setSource: (choice: 'system' | 'light' | 'dark') => {
            nativeTheme.themeSource = choice;
            window.setBackgroundColor(nativeTheme.shouldUseDarkColors ? darkBackground : lightBackground);
        },
        write: (next: DesktopSettings) => writeSettings(paths.settings, next),
    };
    handle(ipcMain, 'theme.get', origin, () => settings.theme, refusedTheme);
    handle(ipcMain, 'theme.set', origin, (choice) => void (settings = applyTheme(choice, settings, themeDeps)), refusedTheme);
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
        }),
        origin,
        log,
        () => current,
    );
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
        emit: (chatId, event) => current?.webContents.send(eventChannels['chat.event'], chatId, event),
        ...loggedTurns(log),
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
            bundle,
            bundleVersion: version,
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
        await window.loadURL(started.link);
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
// Whether this installation is new is read next: before Electron makes its profile under the data directory, before the
// single-instance lock, before anything is written there, on either start below.
const installationWasNew = isNewInstallation(desktopPaths(dataDirectory(process.env, homedir())).data);
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

function startDesktop(): void {
    keepElectronUnderData();

    const uninstalling = uninstallStart(process.argv, { packaged: app.isPackaged, platform: process.platform });
    if (uninstalling !== null) {
        // The nsis uninstaller's start: it takes off what the desktop placed and quits, and opens no window.
        // Its exit code tells the uninstaller whether to go on and remove the files (`uninstallExit`).
        void app.whenReady().then(() => runUninstall(uninstalling.quiet)).then((code) => app.exit(code), () => app.exit(2));
    } else if (stopRuntimeStart(process.argv, { packaged: app.isPackaged, platform: process.platform })) {
        // The nsis installer's and uninstaller's start when a runtime runs from the carried jason.exe: it stops that
        // runtime through jason runtime stop and exits, and opens no window; the installer looks again rather than read
        // the code.
        void app.whenReady().then(runStopRuntime).then((code) => app.exit(code), () => app.exit(2));
    } else if (!app.requestSingleInstanceLock()) {
        app.quit();
    } else {
        // Starting Jason again while it runs, or clicking it in the Dock, brings the window out of the tray.
        app.on('second-instance', (_event, argv) => {
            if (showsOnSecondStart(argv)) showWindow();
        });
        app.on('activate', () => showWindow());
        app.on('window-all-closed', () => app.quit());
        void app.whenReady().then(() => run(installationWasNew));
    }
}
