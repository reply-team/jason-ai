/**
 * The desk around the window: the tray with what waits on the person, its notifications, and the application's menu
 * with its keys. Everything here is Electron's wiring of the pure modules (`attention.ts`, `keybindings.ts`); what it
 * decides is decided and tested there.
 */

import { app, Menu, nativeImage, Notification, Tray, type BrowserWindow, type MenuItemConstructorOptions } from 'electron';
import { join } from 'node:path';
import { AttentionWatcher, trayView, type Attention } from './attention.ts';
import type { Cli } from './cli.ts';
import { eventChannels } from './ipc.ts';
import { bindingsFor, mayCommand, type Binding, type DesktopCommand } from './keybindings.ts';
import type { Logger } from './log.ts';

export interface DeskDeps {
    window(): BrowserWindow | null;
    origin(): string | null;
    /** The CLI, unlogged: the count is read every half minute, and only a change of what the tray shows is logged. */
    cli: Cli;
    log: Logger;
    /** Pairs the window again and loads the page fresh. */
    reload(): void;
    /** Items the Jason menu offers beside About and Quit (the login items, the command line, the updates). */
    jasonMenu?(): MenuItemConstructorOptions[];
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
        this.renderTray();
        this.renderMenu();
    }

    /** Starts reading the count; called only after the start found the runtime up. */
    watch(): void {
        this.watcher.start();
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
            ...(this.deps.jasonMenu?.() ?? []),
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
