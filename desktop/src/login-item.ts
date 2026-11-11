/**
 * The two things that can happen at the person's sign-in, each off until the person turns it on in the menu.
 *
 * "Open Jason at login" is the desktop's own: on macOS a LaunchAgent under the person's home, kept as the desktop's
 * first version wrote it, on Windows Electron's login item, on Linux an XDG autostart entry (Electron's login item is
 * "macOS Windows" only, `setLoginItemSettings` in the pinned Electron's `electron.d.ts`). Each way the start carries a
 * flag, so a start the computer made stays in the background. Its state is read from the system every time, never kept
 * in a setting of the desktop's: on macOS it is on only when the LaunchAgent starts this application's own executable,
 * on Linux only when the entry's Exec line starts this application's launcher and the entry is not hidden.
 *
 * "Start the runtime at logon" is the CLI's: `jason runtime autostart status|enable|disable`. The desktop writes nothing
 * of its own for it.
 */

import { mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, join, resolve } from 'node:path';
import type { Cli } from './cli.ts';
import type { Logger } from './log.ts';

/** On the command line of a start the computer made, so that start stays in the background. */
export const loginFlag = '--opened-at-login';

const label = 'ai.jasonharness.desktop';

export function launchAgentFile(home: string): string {
    return join(home, 'Library', 'LaunchAgents', `${label}.plist`);
}

function escaped(text: string): string {
    return text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
}

/** The program a LaunchAgent starts, its first ProgramArguments string, or null for a file that names none. */
export function launchAgentProgram(text: string): string | null {
    const match = /<key>ProgramArguments<\/key>\s*<array>\s*<string>([^<]*)<\/string>/.exec(text);
    if (match === null || match[1] === undefined) return null;
    return match[1].replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&amp;', '&');
}

/** What starts the application when this person signs in. */
export function launchAgent(executable: string): string {
    return `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key><string>${label}</string>
    <key>ProgramArguments</key>
    <array>
        <string>${escaped(executable)}</string>
        <string>${loginFlag}</string>
    </array>
    <key>RunAtLoad</key><true/>
    <key>LimitLoadToSessionType</key><string>Aqua</string>
</dict>
</plist>
`;
}

/**
 * Where the Linux entry goes: the user's autostart folder, `$XDG_CONFIG_HOME/autostart`, `~/.config/autostart` when that
 * names no absolute folder. The Desktop Application Autostart Specification
 * (https://specifications.freedesktop.org/autostart-spec/latest/): "If $XDG_CONFIG_HOME is not set the Autostart
 * Directory in the user's home directory is ~/.config/autostart/", and a file there is "more important than" one of the
 * same name under `/etc/xdg/autostart/`.
 */
export function autostartEntryFile(home: string, env: Readonly<Record<string, string | undefined>>): string {
    const config = env['XDG_CONFIG_HOME'];
    return join(config && isAbsolute(config) ? config : join(home, '.config'), 'autostart', `${label}.desktop`);
}

/**
 * The entry's Exec line, or null for a launcher it cannot spell. The Desktop Entry Specification's Exec key
 * (https://specifications.freedesktop.org/desktop-entry-spec/latest/exec-variables.html): "Quoting must be done by
 * enclosing the argument between double quotes and escaping the double quote character, backtick character ("`"),
 * dollar sign ("$") and backslash character ("\") by preceding it with an additional backslash character", and the
 * string value's own escape of a backslash "is applied before the quoting rule", so each of those backslashes is doubled
 * in the file. A per-cent begins a field code, and "Field codes must not be used inside a quoted argument", so a
 * launcher with one, or with a control character, is not offered rather than spelled in a way a session may read
 * otherwise.
 */
export function autostartExec(launcher: string): string | null {
    if (/[%\p{Cc}]/u.test(launcher)) return null;
    const quoted = launcher.replace(/["`$\\]/g, (character) => `\\\\${character === '\\' ? '\\\\' : character}`);
    return `Exec="${quoted}" ${loginFlag}`;
}

/** What starts the application when this person signs in to a Linux session: an XDG autostart entry. */
export function autostartEntry(launcher: string): string {
    return `[Desktop Entry]\n# Written by the Jason desktop application.\nType=Application\nName=Jason\n${autostartExec(launcher) ?? ''}\n`;
}

/** Whether this start was the computer's, at the person's sign-in. */
export function openedAtLogin(argv: readonly string[]): boolean {
    return argv.includes(loginFlag);
}

/**
 * Whether another start of Jason while this one runs brings the window into view: one the person made (a shortcut, the
 * Start menu, the Dock) does; one the computer made at sign-in leaves it in the tray, as the first one would.
 */
export function showsOnSecondStart(argv: readonly string[]): boolean {
    return !openedAtLogin(argv);
}

/**
 * How the window behaves where it is. On Windows and macOS the tray keeps the application: the menu bar is hidden (the
 * tray carries its items off macOS), closing the window hides it, and a start at sign-in stays in the tray. On Linux no
 * tray can be counted on, since a session may show no tray icon at all, so the menu bar stays visible, closing the window
 * quits the application while the runtime keeps running, and a start at sign-in opens the window minimized rather than
 * hidden, on every Linux desktop alike, tray or no tray.
 */
export function windowConduct(platform: NodeJS.Platform): { menuBar: boolean; closeQuits: boolean; atLogin: 'hidden' | 'minimized' } {
    return platform === 'linux' ? { menuBar: true, closeQuits: true, atLogin: 'minimized' } : { menuBar: false, closeQuits: false, atLogin: 'hidden' };
}

/** The part of a window that bringing it forward uses. */
export interface Forwardable {
    isMinimized(): boolean;
    restore(): void;
    show(): void;
    focus(): void;
}

/** Brings the window into view: restored when minimized, shown when hidden in the tray, then focused. */
export function bringForward(window: Forwardable | null): void {
    if (window === null) return;
    if (window.isMinimized()) window.restore();
    window.show();
    window.focus();
}

/** The part of Electron's `app` the Windows login item is read and set through. */
export interface ElectronLogin {
    getLoginItemSettings(options: { args: string[] }): { openAtLogin: boolean };
    setLoginItemSettings(settings: { openAtLogin: boolean; args: string[] }): void;
}

export interface LoginItemDeps {
    platform: NodeJS.Platform;
    home: string;
    /** The application's own executable, as the system is to start it. */
    executable: string;
    electron: ElectronLogin;
    /** Settles once the login shell's PATH has been read; nothing is registered before. */
    pathRead: Promise<unknown>;
    log: Logger;
    /** The environment `XDG_CONFIG_HOME` is read from, on Linux. */
    env?: Readonly<Record<string, string | undefined>>;
}

export class LoginItem {
    private readonly deps: LoginItemDeps;
    readonly supported: boolean;

    constructor(deps: LoginItemDeps) {
        this.deps = deps;
        this.supported = deps.platform === 'darwin' || deps.platform === 'win32' || (deps.platform === 'linux' && autostartExec(deps.executable) !== null);
    }

    private entryFile(): string {
        return autostartEntryFile(this.deps.home, this.deps.env ?? {});
    }

    isOn(): boolean {
        if (this.deps.platform === 'linux') {
            if (!this.supported) return false;
            try {
                const lines = readFileSync(this.entryFile(), 'utf8').split('\n').map((line) => line.trim());
                // Hidden=true is the specification's off; X-GNOME-Autostart-enabled=false is the one GNOME's session reads.
                const off = lines.includes('Hidden=true') || lines.includes('X-GNOME-Autostart-enabled=false');
                return !off && lines.includes(autostartExec(this.deps.executable) ?? '');
            } catch {
                return false;
            }
        }
        if (this.deps.platform === 'darwin') {
            try {
                return launchAgentProgram(readFileSync(launchAgentFile(this.deps.home), 'utf8')) === this.deps.executable;
            } catch {
                return false;
            }
        }
        if (this.deps.platform === 'win32') return this.deps.electron.getLoginItemSettings({ args: [loginFlag] }).openAtLogin;
        return false;
    }

    /**
     * The desktop's own login file, under its own name, when it is there and does not start this application: on macOS a
     * LaunchAgent that starts another path (Jason.app moved, or run from where macOS put it aside), on Linux an entry
     * whose Exec line starts another launcher or that is turned off. Null when there is none, or it is on.
     */
    elsewhere(): { file: string; starts: string | null; hidden: boolean } | null {
        if (this.isOn()) return null;
        if (this.deps.platform === 'linux') {
            const file = this.entryFile();
            let lines: string[];
            try {
                lines = readFileSync(file, 'utf8').split('\n').map((line) => line.trim());
            } catch {
                return null;
            }
            const exec = lines.find((line) => line.startsWith('Exec='));
            const hidden = lines.includes('Hidden=true') || lines.includes('X-GNOME-Autostart-enabled=false');
            return { file, starts: exec === undefined ? null : exec.slice('Exec='.length), hidden };
        }
        if (this.deps.platform === 'darwin') {
            const file = launchAgentFile(this.deps.home);
            try {
                return { file, starts: launchAgentProgram(readFileSync(file, 'utf8')), hidden: false };
            } catch {
                return null;
            }
        }
        return null;
    }

    /** Writes the autostart entry for a launcher by write and rename. */
    private writeEntry(file: string, launcher: string): void {
        mkdirSync(dirname(file), { recursive: true });
        const temporary = `${file}.jason-desktop.tmp`;
        try {
            writeFileSync(temporary, autostartEntry(launcher));
            renameSync(temporary, file);
        } finally {
            rmSync(temporary, { force: true });
        }
    }

    /**
     * On Linux, moves an entry that is on to the file an update installed the AppImage as: the pinned electron-updater
     * installs under the downloaded file's name, and removes the old file, when the old name carries a version
     * (`AppImageUpdater.doInstall`, which then emits appimage-filename-updated). An entry that is off stays off, and a
     * launcher the entry cannot spell leaves it as it is. Whether it moved is returned.
     */
    relocate(launcher: string): boolean {
        if (this.deps.platform !== 'linux' || !this.isOn() || autostartExec(launcher) === null) return false;
        this.writeEntry(this.entryFile(), launcher);
        this.deps.log.info('login_item_moved', { Platform: this.deps.platform });
        return true;
    }

    /** Turns it on or off, on the person's word. */
    async set(on: boolean): Promise<void> {
        if (!this.supported) return;
        await this.deps.pathRead;
        if (this.deps.platform === 'linux') {
            const file = this.entryFile();
            if (on) {
                this.writeEntry(file, this.deps.executable);
            } else {
                rmSync(file, { force: true });
            }
        } else if (this.deps.platform === 'darwin') {
            const file = launchAgentFile(this.deps.home);
            if (on) {
                mkdirSync(dirname(file), { recursive: true });
                writeFileSync(file, launchAgent(this.deps.executable));
            } else {
                rmSync(file, { force: true });
            }
        } else {
            this.deps.electron.setLoginItemSettings({ openAtLogin: on, args: [loginFlag] });
        }
        this.deps.log.info('login_item_set', { On: on, Platform: this.deps.platform });
    }
}

/** What the answer of `jason runtime autostart status` says is registered and what it starts, or null when it says nothing. */
function registrationIn(stdout: string): { registered: boolean; executable: string | null; dataDirectory: string | null } | null {
    try {
        const said = JSON.parse(stdout) as { registered?: unknown; executable?: unknown; data_directory?: unknown };
        if (typeof said.registered !== 'boolean') return null;
        return {
            registered: said.registered,
            executable: typeof said.executable === 'string' ? said.executable : null,
            dataDirectory: typeof said.data_directory === 'string' ? said.data_directory : null,
        };
    } catch {
        return null;
    }
}

/** Whether the answer of `jason runtime autostart` says a registration is there, or null when it says nothing. */
function registeredIn(stdout: string): boolean | null {
    try {
        const registered = (JSON.parse(stdout) as { registered?: unknown }).registered;
        return typeof registered === 'boolean' ? registered : null;
    } catch {
        return null;
    }
}

export class RuntimeAutostart {
    private readonly deps: { cli: Cli; log: Logger };

    constructor(deps: { cli: Cli; log: Logger }) {
        this.deps = deps;
    }

    /** Whether the runtime is registered to start at logon, or null when the CLI did not say. */
    async registered(): Promise<boolean | null> {
        const status = await this.deps.cli.run(['runtime', 'autostart', 'status']);
        return status.exit === 0 ? registeredIn(status.stdout) : null;
    }

    /** Whether the runtime is registered to start at logon and which executable it starts, or null when the CLI did not say. */
    async registration(): Promise<{ registered: boolean; executable: string | null } | null> {
        const read = await this.read();
        return read === null ? null : { registered: read.registered, executable: read.executable };
    }

    /** What `jason runtime autostart status` says, the data directory included, or null when the CLI did not say. */
    private async read(): Promise<{ registered: boolean; executable: string | null; dataDirectory: string | null } | null> {
        const status = await this.deps.cli.run(['runtime', 'autostart', 'status']);
        return status.exit === 0 ? registrationIn(status.stdout) : null;
    }

    /**
     * Registers or unregisters it, on the person's word; what the CLI says is there afterwards. An answer that does not
     * say is followed by a read of what is registered, so the menu shows the system's state rather than an unknown.
     */
    async set(on: boolean): Promise<boolean | null> {
        const result = await this.deps.cli.run(['runtime', 'autostart', on ? 'enable' : 'disable']);
        this.deps.log.info('autostart_set', { On: on, Exit: result.exit });
        const said = result.exit === 0 ? registeredIn(result.stdout) : null;
        return said ?? (await this.registered());
    }

    /**
     * Registers it again from this start's jason when the person registered it from a jason `stale` names, another
     * version's copy, for this desktop's data directory: the same act as enable. One that is not registered, names any
     * other jason or another data directory is left as it is. An enable the CLI refuses is logged as failed with its
     * exit, and the registration then still names the other copy, which therefore stays (`removeOtherCopies`).
     */
    async repoint(stale: (executable: string) => boolean, dataDirectory: string): Promise<boolean> {
        const registration = await this.read();
        if (registration?.registered !== true || registration.executable === null || !stale(registration.executable)) return false;
        if (registration.dataDirectory === null || resolve(registration.dataDirectory) !== resolve(dataDirectory)) return false;
        const result = await this.deps.cli.run(['runtime', 'autostart', 'enable']);
        if (result.exit !== 0) {
            this.deps.log.error('repoint_failed', { What: 'autostart', Exit: result.exit });
            return false;
        }
        this.deps.log.info('autostart_repointed', { Exit: result.exit });
        return true;
    }
}
