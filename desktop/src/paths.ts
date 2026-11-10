/** Pure: where the installation the jason command line uses keeps its files, and which of them are the desktop's own. */

import { join, resolve } from 'node:path';

export interface DesktopPaths {
    /** The data directory: the one the CLI and the runtime use, never one of the desktop's own. */
    data: string;
    /** `<data>/desktop/`: the desktop's settings, its chat folder and its stored provider keys. */
    desktop: string;
    /** `<data>/desktop/settings.json`. */
    settings: string;
    /** `<data>/desktop/chat/`: the chat's working folder. */
    chat: string;
    /** `<data>/desktop/provider-keys`: the AI providers' keys, encrypted by the operating system. */
    providerKeys: string;
    /** `<data>/desktop/turns/`: one MCP configuration per running chat turn, with that turn's secret. */
    turns: string;
    /** `<data>/desktop/rollback/`: the previous application an update kept aside, and the record of that update. */
    rollback: string;
    /**
     * `<data>/desktop/electron/`: Electron's own files (its `userData` and `sessionData`), where the page's storage, the
     * single-instance lock and, on Windows, the key the operating system's encryption of `provider-keys` is made with
     * are kept.
     */
    electron: string;
    /** `<data>/desktop/electron/logs/`: what Electron itself logs (its app logs path), never the desktop's own log. */
    electronLogs: string;
    /** `<data>/logs/`, shared with the runtime; the desktop writes only `desktop-*.jsonl` there. */
    logs: string;
    /** `<data>/config/settings.json`, the installation's settings, which a Connect card edits as a person would. */
    installationSettings: string;
    /** `<data>/run/runtime.json`, which the running runtime writes and the desktop only reads. */
    runFile: string;
}

/** `JASON_DATA_DIR` when it is set, otherwise `~/.jason`: the rule the CLI and the runtime follow. */
export function dataDirectory(env: Readonly<Record<string, string | undefined>>, home: string): string {
    const named = env.JASON_DATA_DIR?.trim();
    return named ? resolve(named) : join(home, '.jason');
}

export function desktopPaths(data: string): DesktopPaths {
    const desktop = join(data, 'desktop');
    return {
        data,
        desktop,
        settings: join(desktop, 'settings.json'),
        chat: join(desktop, 'chat'),
        providerKeys: join(desktop, 'provider-keys'),
        turns: join(desktop, 'turns'),
        rollback: join(desktop, 'rollback'),
        electron: join(desktop, 'electron'),
        electronLogs: join(desktop, 'electron', 'logs'),
        logs: join(data, 'logs'),
        installationSettings: join(data, 'config', 'settings.json'),
        runFile: join(data, 'run', 'runtime.json'),
    };
}
