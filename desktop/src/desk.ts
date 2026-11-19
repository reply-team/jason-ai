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
import { bindingsFor, itemKeys, mayCommand, type Binding, type DesktopCommand } from './keybindings.ts';
import type { Logger } from './log.ts';
import { bringForward, type LoginItem, type RuntimeAutostart } from './login-item.ts';
import { commandMenuOf, commandQuestion, type CommandStatus, type PathCommand } from './path-command.ts';
import { quietWatcher, RemovalAct, removeLabel, type RemovalDeps } from './remove-placed.ts';
import type { DemoMenu } from './mode.ts';
import { menuItemOf, pressItem, updateNotice, type MenuItem, type Updater } from './updates.ts';
import { windowTitle } from './window-title.ts';

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
    /** Where the previous version is kept while a copy of it is there (`keptLine`), shown under the update item. */
    kept(): string | null;
    /**
     * The jason command on the PATH: placed and removed by an installed build only, each on the person's word; null where
     * this start carries no jason that outlasts it (an AppImage whose copy was not made), when none is offered.
     */
    command: PathCommand | null;
    /**
     * Remove What Jason Placed, on macOS and Linux, offered by an installed build only; null where it is not offered:
     * Windows keeps its uninstaller.
     */
    removal: Pick<RemovalDeps, 'scope' | 'remove'> | null;
    /**
     * In a demo run, the way back (`demoMenu`), and nothing where the update, the start at logon, the jason command and
     * Remove What Jason Placed would be; null in Live.
     */
    demo: DemoMenu | null;
    /** Back to Your Own Installation: switches to Live and starts the desktop again. */
    back(): void;
    /**
     * Told each time the menu is drawn again, so the page's copy of the update item (`updates.onChanged`) follows the
     * menu's; absent where no page asks for it.
     */
    menuChanged?(): void;
}

/** The Command Line submenu's one line where this start carries no jason that outlasts it. */
export const uncarriedLine = 'No jason command is offered: this start carries no copy of jason to name, as the starting page says.';

function resource(...parts: string[]): string {
    return join(app.getAppPath(), 'resources', ...parts);
}

/** A black outline the menu bar recolours on macOS; the mark in colour, small, where a template is not read. */
function trayIcon(): Electron.NativeImage {
    if (process.platform === 'darwin') {
        const icon = nativeImage.createFromPath(resource('tray', 'trayTemplate.png'));
        icon.setTemplateImage(true);
        return icon;
    }
    return nativeImage.createFromPath(resource('tray', 'tray.png')).resize({ width: 16, height: 16 });
}

export class Desk {
    private readonly deps: DeskDeps;
    private tray: Tray | null = null;
    private attention: Attention;
    private autostartOn: boolean | null = null;
    private readonly removal: RemovalAct | null;
    private readonly watcher: AttentionWatcher;
    private readonly demo: DemoMenu | null;

    constructor(deps: DeskDeps) {
        this.deps = deps;
        this.demo = deps.demo;
        // The Demo reads no count: the tray says it is the demo from the first.
        this.attention = deps.demo === null ? { state: 'reading' } : { state: 'demo' };
        const removal = deps.removal;
        this.removal =
            removal === null
                ? null
                : new RemovalAct({
                      scope: () => removal.scope(),
                      remove: () => removal.remove(),
                      ask: (question) => this.box({ type: 'question', title: windowTitle, ...question }),
                      tell: (closing) => this.box({ type: closing.quitId === null ? 'warning' : 'info', title: windowTitle, message: closing.message, detail: closing.detail, buttons: closing.buttons, defaultId: closing.defaultId, cancelId: closing.cancelId }),
                      quit: () => app.quit(),
                      log: deps.log,
                      changed: () => this.renderMenu(),
                      ...quietWatcher(() => this.watcher),
                  });
        this.watcher = new AttentionWatcher({
            cli: deps.cli,
            log: deps.log,
            show: (attention) => {
                this.attention = attention;
                this.renderTray();
            },
            notify: (text) => this.notify(text, () => this.send('navigate.attention')),
        });
    }

    /** Shows the window, brought forward. */
    show(): void {
        bringForward(this.deps.window());
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
                enabled: opens && !this.removing(),
                checked: opens && login.isOn(),
                click: (item) => void login.set(item.checked).finally(() => this.renderMenu()),
            },
            ...(this.demo !== null
                ? []
                : [
                      {
                          label: 'Start the Runtime at Logon',
                          type: 'checkbox' as const,
                          enabled: autostart !== null && this.autostartOn !== null && !this.removing(),
                          checked: this.autostartOn === true,
                          click: (item: Electron.MenuItem) =>
                              void autostart?.set(item.checked).then((now) => {
                                  this.autostartOn = now;
                                  this.renderMenu();
                              }),
                      },
                  ]),
        ];
    }

    /**
     * The update item, and under it, unclickable, where the previous version is kept while it is. A press in the tray's
     * menu is always a click: the tray has no key, and on Linux its menu reports a click with no mouse button, which
     * Electron counts as a key's (`triggeredByAccelerator`).
     */
    private updateItems(inTray: boolean): MenuItemConstructorOptions[] {
        const item = this.updateItem();
        if (item === null) return [];
        let kept: string | null = null;
        try {
            kept = this.deps.kept();
        } catch {
            // A record that cannot be read names no copy.
        }
        return [
            {
                label: item.label,
                accelerator: itemKeys.update,
                enabled: item.enabled,
                click: (_menuItem, _window, event) => this.pressUpdate(item.act, !inTray && event.triggeredByAccelerator === true, 'menu'),
            },
            ...(kept === null ? [] : [{ label: kept, enabled: false }]),
        ];
    }

    /**
     * The update item as the menu shows it now, disabled while Remove What Jason Placed runs; null in a demo run, whose
     * menu has none. The menu draws it, and the page's Settings shows it through the seam (`updates.item`).
     */
    updateItem(): MenuItem | null {
        if (this.demo !== null) return null;
        const { updates } = this.deps;
        const item = menuItemOf(updates?.current ?? { step: 'idle' }, updates !== null, updates?.active ?? true);
        return { ...item, enabled: item.enabled && !this.removing() };
    }

    /**
     * A press of the update item, on the act of the step it showed: the menu's click and key, and the page's press in
     * Settings (`updates.press`, a click on the label the page drew, `actShown`), the one way either reaches `pressItem`. While Remove What Jason Placed runs the
     * item is disabled, and a press does nothing either. What a check found is in the item's answer wherever it was
     * asked from; asked `from` the menu, the tray or the key, whose menu closes or shows no label, it comes as a
     * notification too, whose click shows the window.
     */
    pressUpdate(act: MenuItem['act'], byKey: boolean, from: 'menu' | 'page'): void {
        if (this.removing()) return;
        const { updates } = this.deps;
        if (updates === null) return;
        void pressItem(updates, act, byKey, (question) => this.box(question)).then((said) => {
            if (said !== null && from === 'menu') this.notify(updateNotice(said), () => this.show());
        });
    }

    /**
     * The Command Line submenu: the bundled executable's full path always, where the command stands, and the act the
     * state allows. A development build shows it and places nothing: its executable is the checkout's, not an install's.
     */
    private commandItems(): MenuItemConstructorOptions[] {
        if (this.demo !== null) return [];
        const command = this.deps.command;
        if (command === null) return [{ label: 'Command Line', submenu: [{ label: uncarriedLine, enabled: false }] }];
        let status: CommandStatus;
        try {
            status = command.status();
        } catch {
            status = { state: 'unsupported', executable: '' };
        }
        const menu = commandMenuOf(status);
        const items: MenuItemConstructorOptions[] = menu.lines.map((line) => ({ label: line, enabled: false }));
        items.push({ label: 'Copy the Executable’s Path', click: () => clipboard.writeText(status.executable) });
        if (menu.acts.length > 0) items.push({ type: 'separator' });
        for (const act of menu.acts) {
            items.push({ label: act.label, enabled: app.isPackaged && !this.removing(), click: () => void this.commandAct(act.act, status) });
        }
        return [{ label: 'Command Line', submenu: items }];
    }

    /** Asks, then places or removes the command; what went wrong is said in a box of its own. */
    private async commandAct(act: 'place' | 'remove', status: CommandStatus): Promise<void> {
        const question = commandQuestion(act, status);
        const window = this.deps.window();
        const options = { type: 'question' as const, buttons: [question.button, 'Cancel'], defaultId: 0, cancelId: 1, message: question.message, detail: question.detail };
        const answer = window === null ? await dialog.showMessageBox(options) : await dialog.showMessageBox(window, options);
        if (answer.response !== 0) return;
        try {
            if (act === 'place') {
                // Where the act wrote the PATH lines, or could not, it says so in the installer's own words.
                const said = this.deps.command?.place().said ?? null;
                if (said !== null) {
                    const told = { type: said.added ? ('info' as const) : ('warning' as const), buttons: ['OK'], message: `The ${'name' in status ? status.name : 'jason'} command is placed.`, detail: said.words };
                    if (window === null) await dialog.showMessageBox(told);
                    else await dialog.showMessageBox(window, told);
                }
            } else this.deps.command?.remove();
        } catch (error) {
            dialog.showErrorBox('The jason command', error instanceof Error ? error.message : String(error));
        }
        this.renderMenu();
    }

    /** Whether Remove What Jason Placed runs: the installation's other acts are off meanwhile. */
    private removing(): boolean {
        return this.removal?.busy ?? false;
    }

    /** A message box, on the window when there is one; the index of the button chosen. */
    private async box(options: Electron.MessageBoxOptions): Promise<number> {
        const window = this.deps.window();
        return (window === null ? await dialog.showMessageBox(options) : await dialog.showMessageBox(window, options)).response;
    }

    /** Remove What Jason Placed: in the Jason menu and the tray's, beside the other acts on the installation. */
    /** In a demo run, the way back to the person's own installation, which needs no page: none in Live. */
    private backItems(): MenuItemConstructorOptions[] {
        const demo = this.demo;
        if (demo === null) return [];
        return [
            { type: 'separator' },
            { label: demo.back.label, accelerator: itemKeys.back, enabled: demo.back.enabled, click: () => this.deps.back() },
        ];
    }

    private removalItems(): MenuItemConstructorOptions[] {
        if (this.demo !== null) return [];
        if (this.removal === null) return [];
        return [{ label: removeLabel, enabled: app.isPackaged && !this.removing(), click: () => void this.removeAct() }];
    }

    /** Runs the act, then reads again what the menu shows of the installation, which the act changed. */
    private async removeAct(): Promise<void> {
        if (this.removal === null) return;
        try {
            await this.removal.run();
        } catch (error) {
            dialog.showErrorBox(windowTitle, error instanceof Error ? error.message : String(error));
        }
        await this.readAutostart().catch(() => undefined);
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
            ...this.updateItems(false),
            ...this.commandItems(),
            ...this.removalItems(),
            ...this.backItems(),
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
        // The tray carries the same items, since a Windows or Linux window shows no menu bar.
        this.renderTray();
        this.deps.menuChanged?.();
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
                ...this.updateItems(true),
                ...this.commandItems(),
                ...this.removalItems(),
                ...this.backItems(),
                { type: 'separator' },
                { label: 'Quit Jason', click: () => app.quit() },
            ]),
        );
    }

    /** A system notification, where the system supports one; its click does what `clicked` says. */
    private notify(text: { title: string; body: string }, clicked: () => void): void {
        if (!Notification.isSupported()) return;
        const notification = new Notification({ title: text.title, body: text.body });
        notification.on('click', clicked);
        notification.show();
    }
}
