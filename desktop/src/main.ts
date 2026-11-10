/**
 * Jason Desktop's main process: a window onto the UI the installation's runtime serves at its loopback address, and the
 * Connect cards' actions behind it. It is one more client of the installation the `jason` command line uses: it finds the
 * data directory as the CLI does, brings the runtime up only through `jason runtime start`, and changes the installation
 * only through CLI verbs and the settings file a person would edit.
 */

import { app, BrowserWindow, ipcMain, safeStorage, shell, type WebContents } from 'electron';
import { autoUpdater } from 'electron-updater';
import { existsSync, readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { Chat } from './chat.ts';
import { denyListFile, parseDenyList, type DenyList } from './chat-deny.ts';
import { registerChat } from './chat-ipc.ts';
import { loggedTurns } from './chat-log.ts';
import { cliAt } from './cli.ts';
import { Desk } from './desk.ts';
import { bringForward, LoginItem, openedAtLogin, RuntimeAutostart, showsOnSecondStart } from './login-item.ts';
import { commandFolder, isProgramFile, PathCommand } from './path-command.ts';
import { atFirstPairing, Repairer } from './repair.ts';
import { applicationFolder, keptLine, keptNotice, readRollback, settleStart } from './rollback.ts';
import { Updater } from './updates.ts';
import { loopbackOrigin, runtimeOrigin } from './descriptor.ts';
import { loggedCli, startInstallation, StartFailed } from './installation.ts';
import { eventChannels, handle, type InvokeMember } from './ipc.ts';
import { fileLogger, limitsFrom, type Logger } from './log.ts';
import { dataDirectory, desktopPaths, type DesktopPaths } from './paths.ts';
import { permissionAllowed } from './permissions.ts';
import { pathOf, programsOn, withPath } from './programs.ts';
import { ProviderKeys } from './providers.ts';
import { readSettings, writeSettings, type DesktopSettings } from './settings.ts';
import { Setup } from './setup.ts';
import { loginPath } from './shell-path.ts';

function readText(file: string): string | null {
    try {
        return readFileSync(file, 'utf8');
    } catch {
        return null;
    }
}

/** The deny list the application carries, read at every turn; a list that cannot be read starts no turn. */
function deniedCommands(): DenyList {
    const text = readText(join(app.getAppPath(), denyListFile));
    if (text === null) throw new Error('The chat’s deny list could not be read.');
    return parseDenyList(text);
}

/** The folder the application carries `jason` and the skills package in: beside the app when packaged, else `bundle/`. */
function bundleDirectory(): string {
    return app.isPackaged ? join(process.resourcesPath, 'bundle') : join(app.getAppPath(), 'bundle');
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
    ];
    for (const [member, handler] of members) handle(ipcMain, member, origin, handler, refused);
    setup.onChanged(() => window()?.webContents.send(eventChannels['setup.changed']));
}

/** Brings the window forward; the desk's own once it is open. */
let showWindow: () => void = () => bringForward(BrowserWindow.getAllWindows()[0] ?? null);

async function run(): Promise<void> {
    const home = homedir();
    // The login shell's PATH is read before anything is started or registered, so every program is found as a terminal
    // finds it.
    const pathRead = loginPath(pathOf(process.env), home);
    process.env.PATH = await pathRead;
    const env = withPath(process.env, process.env.PATH);

    const paths: DesktopPaths = desktopPaths(dataDirectory(process.env, home));
    const log = fileLogger(paths.logs, limitsFrom(readText(paths.installationSettings)));
    const bundle = bundleDirectory();
    const version = bundleVersion(bundle);
    log.info('desktop_started', { Version: version ?? 'unprepared', Platform: process.platform, DataDirectory: paths.data });

    const window = new BrowserWindow({
        width: 1280,
        height: 860,
        minWidth: 900,
        minHeight: 600,
        title: 'Jason',
        // A start the computer made at sign-in stays in the tray until the person opens the window.
        show: !openedAtLogin(process.argv),
        backgroundColor: '#f7f7f5',
        webPreferences: {
            preload: join(__dirname, 'preload.js'),
            contextIsolation: true,
            nodeIntegration: false,
            sandbox: true,
            webSecurity: true,
            spellcheck: false,
        },
    });
    let current: BrowserWindow | null = window;
    window.on('closed', () => {
        current = null;
    });
    window.setMenuBarVisibility(false);
    // The tray keeps the application: closing the window hides it, and Quit in the tray or the menu ends it.
    let quitting = false;
    app.on('before-quit', () => {
        quitting = true;
    });
    window.on('close', (event) => {
        if (quitting) return;
        event.preventDefault();
        window.hide();
    });

    // The origin is read from the run file on every check, because the runtime may come back on another port.
    const origin = () => runtimeOrigin(readText(paths.runFile) ?? '');
    lockNavigation(window.webContents, origin, log);

    let settings: DesktopSettings = readSettings(paths.settings);
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
    const login = new LoginItem({ platform: process.platform, home, executable: process.execPath, electron: app, pathRead, log });
    const autostart = version === null ? null : new RuntimeAutostart({ cli, log });
    // The updater runs only in an installed build: a development build has no installer to hand an update to.
    const rollback = join(paths.desktop, 'rollback');
    const updates = app.isPackaged
        ? new Updater({
              updater: autoUpdater,
              version: app.getVersion(),
              application: applicationFolder(process.platform, process.execPath),
              rollback,
              log,
              async beforeInstall() {
                  quitting = true;
                  repairer.stop();
                  desk.close();
              },
              changed: () => desk.renderMenu(),
          })
        : null;
    const command = new PathCommand({
        platform: process.platform,
        folder: commandFolder(process.platform, process.env, home),
        executable,
        dataDirectory: process.env['JASON_DATA_DIR'] || null,
        path: () => process.env.PATH ?? '',
        pathext: process.env['PATHEXT'],
        isProgram: (file) => isProgramFile(file, process.platform),
        log,
    });
    const keptCopy = () => keptLine(readRollback(rollback), app.getVersion(), existsSync);
    const desk = new Desk({ window: () => current, origin, cli: jason, log, reload: () => void repairer.reload(), login, autostart, updates, kept: keptCopy, command });
    desk.open();
    showWindow = () => desk.show();
    void desk.readAutostart();
    updates?.start();
    let runtimeVersion: string | null = null;
    const keys = new ProviderKeys(paths.providerKeys, safeStorage);
    registerSetup(
        new Setup({
            paths,
            cli,
            programs: programsOn(env),
            keys,
            log,
            bundleVersion: version ?? 'unprepared',
            runtimeVersion: () => runtimeVersion,
            settings: () => settings,
        }),
        origin,
        log,
        () => current,
    );
    const chat = new Chat({
        paths,
        env,
        bundle,
        dataNamed: Boolean(process.env.JASON_DATA_DIR?.trim()),
        cli,
        keyFor: (id) => keys.keyFor(id),
        denied: deniedCommands,
        emit: (chatId, event) => current?.webContents.send(eventChannels['chat.event'], chatId, event),
        ...loggedTurns(log),
    });
    registerChat(ipcMain, chat, origin, (channel) => log.warn('ipc_refused', { Channel: channel, Origin: origin() ?? 'none' }));
    // A turn never outlives the window: closing it stops every host and forgets every "always".
    window.on('closed', () => chat.closeAll());
    app.on('before-quit', () => chat.closeAll());

    await showStarting(window, 'Starting Jason…', false);
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
        const notice = kept === null ? said : keptNotice(said, kept, applicationFolder(process.platform, process.execPath), process.platform);
        if (current !== null) await showStarting(current, notice, true);
    }
}

// Electron's own files follow the data directory, before anything opens: its profile, the page's storage and its own
// logs are under <data>/desktop/electron, never the account's application data, so a second installation (one
// JASON_DATA_DIR names) is a second desktop with its own lock, and a purge of the data directory takes all of it.
const early = desktopPaths(dataDirectory(process.env, homedir()));
app.setPath('userData', early.electron);
app.setPath('sessionData', early.electron);
app.setAppLogsPath(early.electronLogs);

if (!app.requestSingleInstanceLock()) {
    app.quit();
} else {
    // Starting Jason again while it runs, or clicking it in the Dock, brings the window out of the tray.
    app.on('second-instance', (_event, argv) => {
        if (showsOnSecondStart(argv)) showWindow();
    });
    app.on('activate', () => showWindow());
    app.on('window-all-closed', () => app.quit());
    void app.whenReady().then(run);
}
