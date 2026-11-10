/**
 * The desk around the window: the tray with what waits on the person, its notifications, and the application's menu
 * with its keys. Everything here is Electron's wiring of the pure modules (`attention.ts`, `keybindings.ts`); what it
 * decides is decided and tested there.
 */

import { app, clipboard, dialog, Menu, nativeImage, Notification, Tray, type BrowserWindow, type MenuItemConstructorOptions } from 'electron';
import { join } from 'node:path';
import { AttentionWatcher, trayView, type Attention } from './attention.ts';
import type { Cli } from './cli.ts';
import { eventChannels } from './ipc.ts';
import { bindingsFor, mayCommand, type Binding, type DesktopCommand } from './keybindings.ts';
import type { Logger } from './log.ts';
import type { LoginItem, RuntimeAutostart } from './login-item.ts';
import { commandMenuOf, type CommandStatus, type PathCommand } from './path-command.ts';
import { menuItemOf, type Updater } from './updates.ts';

export interface DeskDeps {
    window(): BrowserWindow | null;
    origin(): string | null;
    /** The CLI, unlogged: the count is read every half minute, and only a change of what the tray shows is logged. */
    cli: Cli;
    log: Logger;
    /** Pairs the window again and loads the page fresh. */
    reload(): void;
    /** Open Jason at login: offered only by a packaged application, whose executable is the one to start. */
    login: LoginItem;
    /** Start the runtime at logon, through the CLI; offered once the CLI has said what is registered. */
    autostart: RuntimeAutostart | null;
    /** The updater, in an installed build; null in a development build. */
    updates: Updater | null;
    /** The jason command on the PATH: placed and removed by an installed build only, each on the person's word. */
    command: PathCommand;
}

function resource(...parts: string[]): string {
    return join(app.getAppPath(), 'resources', ...parts);
}

/** A black outline the menu bar recolours on macOS; the application's icon, small, where a template is not read. */
function trayIcon(): Electron.NativeImage {
    if (process.platform === 'darwin') {
        const icon = nativeImage.createFromPath(resource('tray', 'trayTemplate.png'));
        icon.setTemplateImage(true);
        return icon;
    }
    return nativeImage.createFromPath(resource('icon.png')).resize({ width: 16, height: 16 });
}

export class Desk {
    private readonly deps: DeskDeps;
    private tray: Tray | null = null;
    private attention: Attention = { state: 'reading' };
    private autostartOn: boolean | null = null;
    private readonly watcher: AttentionWatcher;

    constructor(deps: DeskDeps) {
        this.deps = deps;
        this.watcher = new AttentionWatcher({
            cli: deps.cli,
            log: deps.log,
            show: (attention) => {
                this.attention = attention;
                this.renderTray();
            },
            notify: (text) => this.notify(text),
        });
    }

    /** Shows the window, brought forward. */
    show(): void {
        const window = this.deps.window();
        if (window === null) return;
        if (window.isMinimized()) window.restore();
        window.show();
        window.focus();
    }

    /** Sends the page one of the commands it declares, only while the window shows the runtime's page. */
    send(command: DesktopCommand): void {
        const window = this.deps.window();
        if (window === null) return;
        this.show();
        if (mayCommand(window.webContents.getURL(), this.deps.origin())) window.webContents.send(eventChannels.command, command);
    }

    private act(binding: Binding): void {
        if ('command' in binding.does) {
            this.send(binding.does.command);
        } else if (binding.does.window === 'reload') {
            this.deps.reload();
        } else {
            this.deps.window()?.webContents.toggleDevTools();
        }
    }

    /** The tray and the menu; the count is read once the installation is up (`watch`). */
    open(): void {
        this.tray = new Tray(trayIcon());
        this.tray.on('click', () => {
            if (process.platform !== 'darwin') this.show();
        });
        this.renderMenu();
    }

    /** Reads what the CLI says is registered to start at logon, and shows it in the menu. */
    async readAutostart(): Promise<void> {
        if (this.deps.autostart === null) return;
        this.autostartOn = await this.deps.autostart.registered();
        this.renderMenu();
    }

    private loginItems(): MenuItemConstructorOptions[] {
        const { login, autostart } = this.deps;
        const opens = login.supported && app.isPackaged;
        return [
            { type: 'separator' },
            {
                label: 'Open Jason at Login',
                type: 'checkbox',
                enabled: opens,
                checked: opens && login.isOn(),
                click: (item) => void login.set(item.checked).finally(() => this.renderMenu()),
            },
            {
                label: 'Start the Runtime at Logon',
                type: 'checkbox',
                enabled: autostart !== null && this.autostartOn !== null,
                checked: this.autostartOn === true,
                click: (item) =>
                    void autostart?.set(item.checked).then((now) => {
                        this.autostartOn = now;
                        this.renderMenu();
                    }),
            },
        ];
    }

    private updateItem(): MenuItemConstructorOptions {
        const { updates } = this.deps;
        const item = menuItemOf(updates?.current ?? { step: 'idle' }, updates !== null);
        return {
            label: item.label,
            enabled: item.enabled,
            click: () => {
                if (item.act === 'check') void updates?.check();
                else if (item.act === 'download') void updates?.download();
                else if (item.act === 'install') void updates?.install();
            },
        };
    }

    /**
     * The Command Line submenu: the bundled executable's full path always, where the command stands, and the act the
     * state allows. A development build shows it and places nothing: its executable is the checkout's, not an install's.
     */
    private commandItem(): MenuItemConstructorOptions {
        let status: CommandStatus;
        try {
            status = this.deps.command.status();
        } catch {
            status = { state: 'unsupported', executable: '' };
        }
        const menu = commandMenuOf(status);
        const items: MenuItemConstructorOptions[] = menu.lines.map((line) => ({ label: line, enabled: false }));
        items.push({ label: 'Copy the Executable’s Path', click: () => clipboard.writeText(status.executable) });
        if (menu.acts.length > 0) items.push({ type: 'separator' });
        for (const act of menu.acts) {
            items.push({ label: act.label, enabled: app.isPackaged, click: () => void this.commandAct(act.act, status) });
        }
        return { label: 'Command Line', submenu: items };
    }

    /** Asks, then places or removes the command; what went wrong is said in a box of its own. */
    private async commandAct(act: 'place' | 'remove', status: CommandStatus): Promise<void> {
        const file = 'file' in status ? status.file : '';
        const detail = act === 'place' ? `This writes ${file}, which runs ${status.executable}.` : `This removes ${file}.`;
        const window = this.deps.window();
        const options = { type: 'question' as const, buttons: [act === 'place' ? 'Place It' : 'Remove It', 'Cancel'], defaultId: 0, cancelId: 1, message: act === 'place' ? 'Put the jason command on the PATH?' : 'Remove the jason command from the PATH?', detail };
        const answer = window === null ? await dialog.showMessageBox(options) : await dialog.showMessageBox(window, options);
        if (answer.response !== 0) return;
        try {
            if (act === 'place') this.deps.command.place();
            else this.deps.command.remove();
        } catch (error) {
            dialog.showErrorBox('The jason command', error instanceof Error ? error.message : String(error));
        }
        this.renderMenu();
    }

    /** Starts reading the count; called at the first pairing that succeeded (`atFirstPairing`). */
    watch(): void {
        this.watcher.start();
    }

    /** The start failed: the tray says the runtime is not answering until a pairing succeeds. */
    notStarted(): void {
        this.watcher.unreachable();
    }

    close(): void {
        this.watcher.stop();
        this.tray?.destroy();
        this.tray = null;
    }

    renderMenu(): void {
        const keys: MenuItemConstructorOptions[] = bindingsFor(app.isPackaged).map((binding) => ({
            label: binding.label,
            accelerator: binding.accelerator,
            click: () => this.act(binding),
        }));
        const jason: MenuItemConstructorOptions[] = [
            { role: 'about' },
            ...this.loginItems(),
            { type: 'separator' },
            this.updateItem(),
            this.commandItem(),
            { type: 'separator' },
            ...(process.platform === 'darwin' ? ([{ role: 'hide' }, { role: 'hideOthers' }, { role: 'unhide' }, { type: 'separator' }] as MenuItemConstructorOptions[]) : []),
            { label: 'Quit Jason', accelerator: 'CmdOrCtrl+Q', click: () => app.quit() },
        ];
        Menu.setApplicationMenu(
            Menu.buildFromTemplate([
                { label: 'Jason', submenu: jason },
                { role: 'editMenu' },
                { label: 'View', submenu: [...keys, { type: 'separator' }, { role: 'togglefullscreen' }] },
                { role: 'windowMenu' },
            ]),
        );
        // The tray carries the same items, since a Windows window shows no menu bar.
        this.renderTray();
    }

    private renderTray(): void {
        if (this.tray === null) return;
        const view = trayView(this.attention);
        this.tray.setToolTip(view.tooltip);
        if (process.platform === 'darwin') this.tray.setTitle(view.title);
        this.tray.setContextMenu(
            Menu.buildFromTemplate([
                { label: view.status, enabled: false },
                { type: 'separator' },
                { label: 'Open Jason', click: () => this.show() },
                { label: 'New Chat', click: () => this.send('chat.new') },
                { label: 'What Needs You', click: () => this.send('navigate.attention') },
                ...this.loginItems(),
                { type: 'separator' },
                this.updateItem(),
                this.commandItem(),
                { type: 'separator' },
                { label: 'Quit Jason', click: () => app.quit() },
            ]),
        );
    }

    private notify(text: { title: string; body: string }): void {
        if (!Notification.isSupported()) return;
        const notification = new Notification({ title: text.title, body: text.body });
        notification.on('click', () => this.send('navigate.attention'));
        notification.show();
    }
}
