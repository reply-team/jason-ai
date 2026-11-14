/**
 * Where the installation the jason command line uses keeps its files, and which of them are the desktop's own: pure,
 * but for the data directory's layout stamp, which `dataLayout` reads at each call to place the parts that move.
 */

import { closeSync, fstatSync, openSync, readSync, statSync } from 'node:fs';
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

// ---- The data directory's layout -------------------------------------------------------------------------------------

/** The paths of the parts a person sets up, which a layout places, relative to the data directory with `/` between names. */
export interface LayoutParts {
    pluginsDirectory: string;
    roleSkillsDirectory: string;
    skillsStagingDirectory: string;
    uiCustomDirectory: string;
    /** The settings file a person may change. */
    userSettingsFile: string;
    /** Where plugins' configuration (`Plugins:Config`), their secrets among it, is kept. */
    secretsFile: string;
    /** Whether the settings file a person may change holds plugins' configuration too: one file in layout 1. */
    userSettingsFileHoldsSecrets: boolean;
}

/**
 * The data directory's table as `JasonPaths` composes it, for the parts that move between layouts: the stamp's name, the
 * layout this build runs, the names a purge removes, and each layout's places. `desktop/data-layouts.json` is written from
 * `JasonPaths`, and a test holds this table to it member for member, and holds every path of that file this table does
 * not carry to one place in every layout, so a part JasonPaths starts to move turns that test red here.
 */
export const layoutTable = {
    stamp: 'layout.json',
    thisBuildRuns: 1,
    ownEntries: ['state', 'config', 'plugins', 'run', 'logs', 'work', 'skills', 'update', 'autostart', 'ui', 'desktop', 'layout.json', 'setup'],
    layouts: {
        1: {
            pluginsDirectory: 'plugins',
            roleSkillsDirectory: 'skills/roles',
            skillsStagingDirectory: 'skills/.staging',
            uiCustomDirectory: 'ui/custom',
            userSettingsFile: 'config/settings.json',
            secretsFile: 'config/settings.json',
            userSettingsFileHoldsSecrets: true,
        },
        2: {
            pluginsDirectory: 'setup/plugins',
            roleSkillsDirectory: 'setup/roles',
            skillsStagingDirectory: 'setup/.staging',
            uiCustomDirectory: 'setup/ui',
            userSettingsFile: 'setup/settings.json',
            secretsFile: 'config/secrets.json',
            userSettingsFileHoldsSecrets: false,
        },
    },
} as const satisfies { stamp: string; thisBuildRuns: number; ownEntries: readonly string[]; layouts: Record<1 | 2, LayoutParts> };

/** A layout the table has places for. */
export type LayoutNumber = keyof typeof layoutTable.layouts;

/** The newest layout the table places the parts by; a stamp naming a later one is answered by this one. */
export const newestLayout: LayoutNumber = 2;

/** A stamp is a few dozen bytes; a file larger than this is not one. */
export const stampMaxBytes = 64 * 1024;

/** The refusal of a stamp naming a layout later than the one this build runs. */
export const layoutNewerCode = 'layout_newer';

/** The refusal of a stamp that is there and cannot be read. */
export const layoutUnreadableCode = 'layout_unreadable';

/**
 * What a data directory's `layout.json` was found to say, read by the rules `LayoutStamp` reads it by: nothing (layout 1,
 * the layout every Jason before the stamp kept), a layout with the version that wrote it, or that it cannot be read,
 * with why. Members this build does not know are ignored; the number alone decides.
 */
export type LayoutStamp =
    | { state: 'missing'; path: string }
    | { state: 'read'; path: string; layout: number; writtenBy: string; at: string | null }
    | { state: 'unreadable'; path: string; problem: string };

/** How the stamp is found on disk; a test gives its own. */
export interface StampDisk {
    /** What is at the path, following links; anything that cannot be asked is nothing, as `File.Exists` answers. */
    kind(path: string): 'file' | 'folder' | 'none';
    /** The file's bytes, or `larger` when it holds more than `limit`; throws when it cannot be read. */
    read(path: string, limit: number): Uint8Array | 'larger';
}

const onDisk: StampDisk = {
    kind(path) {
        try {
            return statSync(path).isDirectory() ? 'folder' : 'file';
        } catch {
            return 'none';
        }
    },
    read(path, limit) {
        const descriptor = openSync(path, 'r');
        try {
            const size = fstatSync(descriptor).size;
            if (size > limit) return 'larger';
            const bytes = Buffer.alloc(size);
            let read = 0;
            while (read < size) {
                const got = readSync(descriptor, bytes, read, size - read, read);
                if (got === 0) break;
                read += got;
            }
            return bytes.subarray(0, read);
        } finally {
            closeSync(descriptor);
        }
    },
};

/** The stamp of the data directory at `data`, as found. Never throws on what is there: what it finds is recorded. */
export function readLayoutStamp(data: string, disk: StampDisk = onDisk): LayoutStamp {
    const path = join(data, layoutTable.stamp);
    const kind = disk.kind(path);
    if (kind === 'folder') return { state: 'unreadable', path, problem: 'it is a folder' };
    if (kind === 'none') return { state: 'missing', path };
    let bytes: Uint8Array | 'larger';
    try {
        bytes = disk.read(path, stampMaxBytes);
    } catch {
        return { state: 'unreadable', path, problem: 'it could not be read' };
    }
    if (bytes === 'larger') return { state: 'unreadable', path, problem: 'it is larger than a stamp' };
    return parseLayoutStamp(path, bytes);
}

/**
 * The stamp `bytes` hold. Where `LayoutStamp` says a file that is not JSON by the line and byte its reader stopped at,
 * this says only that it is not JSON: Node's parser stops elsewhere, and a position that is not the runtime's would be
 * worse than none.
 */
export function parseLayoutStamp(path: string, bytes: Uint8Array): LayoutStamp {
    const unreadable = (problem: string): LayoutStamp => ({ state: 'unreadable', path, problem });
    let text: string;
    let root: unknown;
    try {
        // A byte-order mark is dropped, as the runtime drops it; bytes that are not UTF-8 are not JSON.
        text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
        root = JSON.parse(text);
    } catch {
        return unreadable('it is not JSON');
    }
    if (typeof root !== 'object' || root === null || Array.isArray(root)) return unreadable('it is not a JSON object');
    const members = topLevelMembers(text);
    if (new Set(members.map((member) => member.name)).size !== members.length) return unreadable('it names a member twice');
    const raw = (name: string) => members.find((member) => member.name === name)?.raw;
    const read = root as Record<string, unknown>;

    const layoutText = raw('layout');
    // A whole number as written and within 32 bits, as `TryGetInt32` takes one: `2.0`, `2e0` and a string are none.
    const layout = layoutText !== undefined && /^-?(?:0|[1-9][0-9]*)$/.test(layoutText) ? Number(layoutText) : Number.NaN;
    if (!Number.isInteger(layout) || layout > 2 ** 31 - 1 || layout < -(2 ** 31)) return unreadable('it has no whole-number layout');
    // `-0` reads as 0, and says so.
    if (layout < 1) return unreadable(`its layout is ${layout}, and layouts count from 1`);

    const writtenBy = read.written_by;
    if (typeof writtenBy !== 'string' || !isVersion(writtenBy)) return unreadable('it names no version that wrote it');

    let at: string | null = null;
    if (Object.hasOwn(read, 'at')) {
        if (typeof read.at !== 'string') return unreadable('its at is not text');
        at = read.at;
    }
    return { state: 'read', path, layout, writtenBy, at };
}

/** The names of a JSON object's own members, each with its value's text as written: the text is JSON already. */
function topLevelMembers(text: string): { name: string; raw: string }[] {
    let at = 0;
    const space = () => {
        while (at < text.length && ' \t\n\r'.includes(text[at] ?? '')) at++;
    };
    const string = () => {
        const start = at;
        at++;
        while (text[at] !== '"') at += text[at] === '\\' ? 2 : 1;
        at++;
        return text.slice(start, at);
    };
    const value = () => {
        const start = at;
        let depth = 0;
        while (at < text.length) {
            const character = text[at];
            if (character === '"') {
                string();
                continue;
            }
            if (character === '{' || character === '[') depth++;
            else if (character === '}' || character === ']') {
                if (depth === 0) break;
                depth--;
            } else if (character === ',' && depth === 0) break;
            at++;
        }
        return text.slice(start, at).trim();
    };
    const members: { name: string; raw: string }[] = [];
    space();
    at++;
    space();
    if (text[at] === '}') return members;
    for (;;) {
        space();
        const name = JSON.parse(string()) as string;
        space();
        at++;
        space();
        members.push({ name, raw: value() });
        space();
        if (text[at] !== ',') return members;
        at++;
    }
}

/** A version as the runtime's `SemanticVersion` reads one: major.minor.patch with an optional pre-release and build. */
function isVersion(text: string): boolean {
    let span = text.trim();
    if (span === '') return false;
    const plus = span.indexOf('+');
    if (plus >= 0) span = span.slice(0, plus);
    const dash = span.indexOf('-');
    if (dash >= 0) {
        const identifiers = span.slice(dash + 1).split('.');
        if (!identifiers.every((identifier) => /^[0-9A-Za-z-]+$/.test(identifier) && !/^0[0-9]+$/.test(identifier))) return false;
        span = span.slice(0, dash);
    }
    return /^(?:0|[1-9][0-9]{0,8})\.(?:0|[1-9][0-9]{0,8})\.(?:0|[1-9][0-9]{0,8})$/.test(span);
}

/** Whether a build that runs `layoutTable.thisBuildRuns` refuses this stamp: a later layout, or a stamp it cannot read. */
export function stampRefuses(stamp: LayoutStamp): boolean {
    return stamp.state === 'unreadable' || (stamp.state === 'read' && stamp.layout > layoutTable.thisBuildRuns);
}

/** The data directory's parts as one layout places them, and the stamp that layout was read from. */
export interface DataLayout {
    /** The layout the parts are placed by: the stamp's, 1 where there is none or it cannot be read, or the one given. */
    layout: LayoutNumber;
    /** What the stamp was found to say; null when the layout was given. */
    stamp: LayoutStamp | null;
    pluginsDirectory: string;
    roleSkillsDirectory: string;
    skillsStagingDirectory: string;
    uiCustomDirectory: string;
    userSettingsFile: string;
    secretsFile: string;
    userSettingsFileHoldsSecrets: boolean;
}

/**
 * The parts of the data directory at `data`, placed by the layout its stamp names at this moment: the stamp is read at
 * every call, never kept, because the desktop outlives a stamp a later build writes. Given `layout`, nothing is read and
 * nothing is refused, as `JasonPaths(root, layout)`: a test's, never production's.
 */
export function dataLayout(data: string, layout?: LayoutNumber, disk: StampDisk = onDisk): DataLayout {
    const stamp = layout === undefined ? readLayoutStamp(data, disk) : null;
    const answered: LayoutNumber = layout ?? (stamp?.state === 'read' ? (Math.min(stamp.layout, newestLayout) as LayoutNumber) : 1);
    const parts: LayoutParts = layoutTable.layouts[answered];
    const at = (relative: string) => join(data, ...relative.split('/'));
    return {
        layout: answered,
        stamp,
        pluginsDirectory: at(parts.pluginsDirectory),
        roleSkillsDirectory: at(parts.roleSkillsDirectory),
        skillsStagingDirectory: at(parts.skillsStagingDirectory),
        uiCustomDirectory: at(parts.uiCustomDirectory),
        userSettingsFile: at(parts.userSettingsFile),
        secretsFile: at(parts.secretsFile),
        userSettingsFileHoldsSecrets: parts.userSettingsFileHoldsSecrets,
    };
}

/** The sentence of a data directory a newer Jason set up, as `LayoutStamp.NewerSentence` says it. */
export function layoutNewerSentence(writtenBy: string, current: string): string {
    return `This data directory was set up by Jason ${writtenBy}, newer than this jason (${current}): update this jason, or use the one that set it up.`;
}

/** The sentence of a stamp that cannot be read, as `LayoutStamp.RefusalFor` says it. */
export function layoutUnreadableSentence(path: string, problem: string): string {
    return `This data directory's layout stamp at ${path} cannot be read (${problem}), so this jason cannot tell how the directory is laid out: use the Jason that set it up, or update this jason.`;
}

/**
 * The refusal a point that touches a part that moves gives on `layout`, said for a jason of version `current`; null where
 * it goes on. A layout that was given rather than read is never refused.
 */
export function layoutRefusal(layout: DataLayout, current: string): { code: string; message: string } | null {
    const stamp = layout.stamp;
    if (stamp === null || !stampRefuses(stamp)) return null;
    if (stamp.state === 'unreadable') return { code: layoutUnreadableCode, message: layoutUnreadableSentence(stamp.path, stamp.problem) };
    if (stamp.state === 'read') return { code: layoutNewerCode, message: layoutNewerSentence(stamp.writtenBy, current) };
    return null;
}
