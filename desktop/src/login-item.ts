/**
 * The two things that can happen at the person's sign-in, each off until the person turns it on in the menu.
 *
 * "Open Jason at login" is the desktop's own: on macOS a LaunchAgent under the person's home, kept as the desktop's
 * first version wrote it, on Windows Electron's login item. Either way the start carries a flag, so a start the
 * computer made stays in the background. Its state is read from the system every time, never kept in a setting of the
 * desktop's: on macOS it is on only when the LaunchAgent starts this application's own executable.
 *
 * "Start the runtime at logon" is the CLI's: `jason runtime autostart status|enable|disable`. The desktop writes nothing
 * of its own for it.
 */

import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
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
}

export class LoginItem {
    private readonly deps: LoginItemDeps;
    readonly supported: boolean;

    constructor(deps: LoginItemDeps) {
        this.deps = deps;
        this.supported = deps.platform === 'darwin' || deps.platform === 'win32';
    }

    isOn(): boolean {
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

    /** Turns it on or off, on the person's word. */
    async set(on: boolean): Promise<void> {
        if (!this.supported) return;
        await this.deps.pathRead;
        if (this.deps.platform === 'darwin') {
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
function registrationIn(stdout: string): { registered: boolean; executable: string | null } | null {
    try {
        const said = JSON.parse(stdout) as { registered?: unknown; executable?: unknown };
        if (typeof said.registered !== 'boolean') return null;
        return { registered: said.registered, executable: typeof said.executable === 'string' ? said.executable : null };
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
}
