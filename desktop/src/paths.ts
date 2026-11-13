/** Pure: where the installation the jason command line uses keeps its files, and which of them are the desktop's own. */

import { join, resolve } from 'node:path';

export interface DesktopPaths {
    /** The data directory: the one the CLI and the runtime use, never one of the desktop's own. */
    data: string;
    /** `<data>/desktop/`: the desktop's settings, its chat folder and its stored provider keys. */
    desktop: string;
    /** `<data>/desktop/settings.json`. */
    settings: string;
    /** `<data>/desktop/mode`: the record of the mode, Live or Demo, one word; read and written in the person's directory only, also during a demo run (`mode.ts`). */
    mode: string;
    /** `<data>/desktop/demo-start-over`: a request that the next demo start remove the demo's two folders first; written in the person's directory only (`demo-over.ts`). */
    demoStartOver: string;
    /** `<data>/desktop/full-access.json`: the standing choice of Full access, the main process's alone (`full-access-standing.ts`). */
    fullAccess: string;
    /** `<data>/desktop/chat/`: the chat's working folder. */
    chat: string;
    /** `<data>/desktop/chat-full/`: one folder per Full access grant, `<id>/` in it, where that conversation's Full access turns run. */
    chatFull: string;
    /** `<data>/desktop/chat-full-ended/`: the grants' folders after Full access ended, kept seven days and then removed (`chat-full-folder.ts`). */
    chatFullEnded: string;
    /** `<data>/desktop/chat-full-records/`: one file per grant naming its conversation, the desktop's alone: no turn reads or writes it. */
    chatFullRecords: string;
    /** `<data>/desktop/conversations/`: one record per conversation of the chat, the desktop's alone. */
    conversations: string;
    /** `<data>/desktop/provider-keys`: the AI providers' keys, encrypted by the operating system. */
    providerKeys: string;
    /**
     * `<data>/desktop/claude-keyed/`: one Claude Code configuration folder per key provider, `<provider id>/` in it, which
     * a turn on that provider's key runs with: no claude.ai login is in it, and it keeps those turns' transcripts.
     */
    claudeKeyed: string;
    /** `<data>/desktop/codex-keyed/`: the Codex home a turn on the OpenAI key runs with: no saved login is in it, and it keeps those turns' transcripts. */
    codexKeyed: string;
    /** `<data>/desktop/turns/`: one MCP configuration per running chat turn, with that turn's secret. */
    turns: string;
    /** `<data>/desktop/models.json`: the models Codex offered when last asked, on the person's sign-in and on the OpenAI key. */
    models: string;
    /**
     * `<data>/desktop/skills-records/`: what the desktop placed in the chat folder's skills folders, outside the chat
     * folder: the record of each skills deployment, and the Reply skills' files with their digests (`chat-agent-folder.ts`).
     */
    skillsRecords: string;
    /** `<data>/desktop/rollback/`: the previous application an update kept aside, and the record of that update. */
    rollback: string;
    /** `<data>/desktop/bundle/`: on an AppImage, the copy of the jason it carries, one folder per version (`bundle-copy.ts`). */
    bundle: string;
    /**
     * `<data>/desktop/electron/`: Electron's own files (its `userData` and `sessionData`), where the page's storage, the
     * single-instance lock and, on Windows, the key the operating system's encryption of `provider-keys` is made with
     * are kept.
     */
    electron: string;
    /** `<data>/desktop/electron/logs/`: what Electron itself logs (its app logs path), never the desktop's own log. */
    electronLogs: string;
    /**
     * `<data>/desktop/window-pairing.json`: whether what an earlier version's page kept on disk was unpaired, and the id
     * of the UI token the window's page holds now, never the token (`window-pairing.ts`).
     */
    windowPairing: string;
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
        mode: join(desktop, 'mode'),
        demoStartOver: join(desktop, 'demo-start-over'),
        fullAccess: join(desktop, 'full-access.json'),
        chat: join(desktop, 'chat'),
        chatFull: join(desktop, 'chat-full'),
        chatFullEnded: join(desktop, 'chat-full-ended'),
        chatFullRecords: join(desktop, 'chat-full-records'),
        conversations: join(desktop, 'conversations'),
        providerKeys: join(desktop, 'provider-keys'),
        claudeKeyed: join(desktop, 'claude-keyed'),
        codexKeyed: join(desktop, 'codex-keyed'),
        turns: join(desktop, 'turns'),
        models: join(desktop, 'models.json'),
        skillsRecords: join(desktop, 'skills-records'),
        rollback: join(desktop, 'rollback'),
        bundle: join(desktop, 'bundle'),
        electron: join(desktop, 'electron'),
        electronLogs: join(desktop, 'electron', 'logs'),
        windowPairing: join(desktop, 'window-pairing.json'),
        logs: join(data, 'logs'),
        installationSettings: join(data, 'config', 'settings.json'),
        runFile: join(data, 'run', 'runtime.json'),
    };
}
