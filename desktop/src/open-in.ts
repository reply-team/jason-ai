/**
 * "Open in": the places the page's split button offers for the folder the person is working in, and the one act that opens
 * a place. The page sends a place's name and, where a campaign's chat is open, the campaign's id, and never a path: the
 * name is held against the list this module makes for this computer, and the folder is read here from the installation's
 * own record (the campaign's recorded folder, else the harness root). Nothing here starts a process: an editor and the
 * file manager are started by `open-in-start.ts`, the three terminals by the bundled `jason session open`, and nothing is
 * written into the person's folders.
 *
 * One more entry continues the chat in front in a terminal: the page names the chat and nothing else, and the host and the
 * session the host named are read from the desktop's own record of that chat (`continuableOf`), the working folder from
 * the run. It is offered only for a session of Claude Code or Codex that a plain turn named, and it is never the default.
 *
 * And one entry opens the person's own AI on Jason itself, "Change Jason itself": the bundled `jason session open --setup`,
 * with the host its own rule chooses (the preference `agent.interactive_host`, else Claude Code). Only where that rule
 * cannot choose, by the codes in `hostUnchosen`, is it run once more with the host this desktop found, Claude Code, else
 * Codex. It is offered in the person's own installation only, never in a demo run, and it is never the default.
 */

import { posix, win32 } from 'node:path';

/** Every place the menu can offer, in the order it offers them. */
export const placeIds = ['claude', 'codex', 'terminal', 'vscode', 'cursor', 'antigravity', 'windsurf', 'zed', 'files', 'browser', 'copy'] as const;
export type PlaceId = (typeof placeIds)[number];

export const isPlace = (value: unknown): value is PlaceId => typeof value === 'string' && (placeIds as readonly string[]).includes(value);

export const editorIds = ['vscode', 'cursor', 'antigravity', 'windsurf', 'zed'] as const;
export type EditorId = (typeof editorIds)[number];

/** The three places a terminal is opened for, by `jason session open`. */
export type TerminalKind = 'claude' | 'codex' | 'terminal';

const labels: Readonly<Record<PlaceId, string>> = {
    claude: 'Claude Code',
    codex: 'Codex',
    terminal: 'Terminal',
    vscode: 'VS Code',
    cursor: 'Cursor',
    antigravity: 'Antigravity',
    windsurf: 'Windsurf',
    zed: 'Zed',
    files: 'File manager',
    browser: 'Browser',
    copy: 'Copy path',
};

export interface Place {
    id: PlaceId;
    label: string;
}

/** The hosts a chat can be continued in, by the id the chat keeps each one's session under. */
export type ContinueHost = 'claude' | 'codex';

/** The entry that continues the chat in front: its host, by name, and whether a turn of the chat runs now. Never the session's id. */
export interface ContinueOffer {
    host: ContinueHost;
    label: string;
    running: boolean;
}

/** What the menu is drawn from: the places on this computer, the one the main part opens, and the folder's name. */
export interface OpenInAnswer {
    places: Place[];
    default: PlaceId;
    /** The last part of the folder's path, for the button's words; never the path. */
    folder: string;
    /** Present only when the chat the page named may be continued in a terminal. */
    continue?: ContinueOffer;
    /** Present only in the person's own installation: the entry "Change Jason itself". It names no host, which the preference chooses. */
    changeJason?: true;
}

export interface OpenInRefusal {
    refused: string;
}

/** What the system says about this computer, injected so each system's detection is tested with planted answers. */
export interface Probes {
    platform: NodeJS.Platform;
    env: Readonly<Record<string, string | undefined>>;
    home: string;
    /** Where the PATH finds a command, or null. */
    onPath(command: string): string | null;
    /** Whether a file or folder is there. */
    exists(path: string): boolean;
}

/** An editor found: a program to start with the folder, or on macOS an application the system opens it with. */
export interface Found {
    kind: 'program' | 'app';
    path: string;
}

interface Editor {
    command: readonly string[];
    /** The application's name on macOS, `<name>.app` in an Applications folder. */
    app: string;
    /** On Windows, the program's file and the folders it is installed in, under a per-user or per-machine programs folder. */
    exe: string;
    dirs: readonly string[];
}

const editors: Readonly<Record<EditorId, Editor>> = {
    vscode: { command: ['code'], app: 'Visual Studio Code', exe: 'Code.exe', dirs: ['Microsoft VS Code'] },
    cursor: { command: ['cursor'], app: 'Cursor', exe: 'Cursor.exe', dirs: ['cursor', 'Cursor'] },
    antigravity: { command: ['antigravity'], app: 'Antigravity', exe: 'Antigravity.exe', dirs: ['Antigravity'] },
    windsurf: { command: ['windsurf'], app: 'Windsurf', exe: 'Windsurf.exe', dirs: ['Windsurf'] },
    zed: { command: ['zed', 'zeditor'], app: 'Zed', exe: 'Zed.exe', dirs: ['Zed'] },
};

/**
 * The editor as this system registers it. Windows: the program's own file under the per-user or the per-machine programs
 * folder, or beside the `bin` folder a command on the PATH is in (never the batch file itself, which the desktop does not
 * start). macOS: the application in an Applications folder, else a command on the PATH. Linux: a command on the PATH.
 */
export function findEditor(id: EditorId, probes: Probes): Found | null {
    const editor = editors[id];
    if (probes.platform === 'darwin') {
        for (const folder of ['/Applications', posix.join(probes.home, 'Applications')]) {
            const app = posix.join(folder, `${editor.app}.app`);
            if (probes.exists(app)) return { kind: 'app', path: app };
        }
    }
    if (probes.platform === 'win32') {
        const roots = [probes.env.LOCALAPPDATA === undefined ? null : win32.join(probes.env.LOCALAPPDATA, 'Programs'), probes.env.ProgramFiles ?? null];
        for (const root of roots) {
            if (root === null) continue;
            for (const dir of editor.dirs) {
                const exe = win32.join(root, dir, editor.exe);
                if (probes.exists(exe)) return { kind: 'program', path: exe };
            }
        }
        for (const command of editor.command) {
            const found = probes.onPath(command);
            if (found === null) continue;
            const beside = win32.join(win32.dirname(win32.dirname(found)), editor.exe);
            if (probes.exists(beside)) return { kind: 'program', path: beside };
            if (win32.extname(found).toLowerCase() === '.exe') return { kind: 'program', path: found };
        }
        return null;
    }
    for (const command of editor.command) {
        const found = probes.onPath(command);
        if (found !== null) return { kind: 'program', path: found };
    }
    return null;
}

/** What the system calls its file manager; empty where the page's own word for it is used. */
export function fileManagerName(platform: NodeJS.Platform): string {
    return platform === 'darwin' ? 'Finder' : platform === 'win32' ? 'File Explorer' : '';
}

/** The file manager's program, or null where there is none to find. */
export function fileManager(probes: Probes): string | null {
    if (probes.platform === 'win32') return win32.join(probes.env.SystemRoot ?? probes.env.windir ?? 'C:\\Windows', 'explorer.exe');
    if (probes.platform === 'darwin') return '/usr/bin/open';
    return probes.onPath('xdg-open');
}

/** A file and its arguments, as the starter takes them. */
export interface Line {
    file: string;
    args: string[];
}

/**
 * How the file manager shows the folder. On macOS it is `open -a Finder <folder>`, so that a folder named like an
 * application is shown and never launched.
 */
export function fileManagerLine(program: string, folder: string, platform: NodeJS.Platform): Line {
    return platform === 'darwin' ? { file: program, args: ['-a', 'Finder', folder] } : { file: program, args: [folder] };
}

/** How an editor found is started with the folder. */
export function editorLine(found: Found, folder: string): Line {
    return found.kind === 'app' ? { file: '/usr/bin/open', args: ['-a', found.path, folder] } : { file: found.path, args: [folder] };
}

/**
 * The places this computer has, in the menu's order; the three terminal entries need no program of the desktop's. A
 * browser is offered where the desktop can open the chat in one (`browser`): it opens the chat, not the folder.
 */
export function placesFound(probes: Probes, hosts: { claude: boolean; codex: boolean }, browser = false): Place[] {
    const found: PlaceId[] = [];
    if (hosts.claude) found.push('claude');
    if (hosts.codex) found.push('codex');
    found.push('terminal');
    for (const id of editorIds) if (findEditor(id, probes) !== null) found.push(id);
    if (fileManager(probes) !== null) found.push('files');
    if (browser) found.push('browser');
    found.push('copy');
    // The generic places (Terminal, a browser, Copy path, and the file manager where the system has no name for it) carry no
    // label: the page has its own words for them. A product's name (VS Code, Finder, File Explorer, Claude Code) is the
    // desktop's.
    return found.map((id) => ({ id, label: id === 'files' ? fileManagerName(probes.platform) : id === 'terminal' || id === 'browser' || id === 'copy' ? '' : labels[id] }));
}

/** The places that do not open the folder, so are never the main part's: Copy path, and a browser, which opens the chat. */
export const notFolderPlaces: readonly PlaceId[] = ['copy', 'browser'];

/**
 * The place the main part opens: the one used last while it is still here, else Claude Code, else the first place that
 * opens something. Copy path and a browser are never the default; a terminal is always there.
 */
export function defaultPlace(places: readonly Place[], remembered: PlaceId | null): PlaceId {
    const here = (id: PlaceId) => places.some((place) => place.id === id);
    if (remembered !== null && !notFolderPlaces.includes(remembered) && here(remembered)) return remembered;
    if (here('claude')) return 'claude';
    return places.find((place) => !notFolderPlaces.includes(place.id))?.id ?? 'terminal';
}

/** The arguments `jason session open` is given for a terminal; one function holds the form the CLI takes. */
export type TerminalFlags = (kind: TerminalKind, campaignId: string | null) => string[];

/** The arguments `jason session open` is given to continue a host's own conversation of that id, in the working folder. */
export type ContinueFlags = (host: ContinueHost, session: string) => string[];

/** The arguments `jason session open` is given for "Change Jason itself": with no host, the one the command line's own rule chooses. */
export type ChangeJasonFlags = (host: ContinueHost | null) => string[];

/** How "Change Jason itself" was run: by the command line's own choice of host, or by the host this desktop found. */
export type ChangeJasonHost = 'preference' | ContinueHost;

/** What "Change Jason itself" needs from the run. Absent, the entry is not offered, as in a demo run. */
export interface ChangeJasonDeps {
    /** True in the person's own installation, false in a demo run. */
    live(): boolean;
    flags: ChangeJasonFlags;
    log?(event: 'open_in_change_jason' | 'open_in_change_jason_refused', fields: { Host: ChangeJasonHost; Exit: number }): void;
}

export interface OpenInDeps {
    probes: Probes;
    /** The agent hosts the desktop finds. */
    hosts(): { claude: boolean; codex: boolean };
    /** The bundled `jason`. */
    cli: { run(args: readonly string[], options?: { cwd?: string }): Promise<{ exit: number; stdout: string; stderr: string }> };
    /** The harness root of this run's installation: the demo's in a demo run. */
    harnessRoot(): string;
    isDirectory(path: string): boolean;
    remembered(): PlaceId | null;
    remember(id: PlaceId): void;
    /** Starts a program and leaves it running; the sentence it could not be started with, or null. */
    start(line: Line): Promise<string | null>;
    /** Writes to the clipboard, from the main process. */
    copy(text: string): void;
    terminalFlags: TerminalFlags;
    /**
     * Opens the chat in a tab of the system's browser (`browser-bridge.ts`): the sentence why it could not, or null.
     * Absent, a browser is not offered.
     */
    browser?(): Promise<string | null>;
    log?(event: 'open_in' | 'open_in_refused', fields: { Place: PlaceId }): void;
    /**
     * The session a terminal may continue the chat by, read from the desktop's own record, with whether a turn of it runs
     * now; null for a chat the desktop does not keep, an id of no shape, or a chat with no such session (`Chat.continuable`).
     */
    continuable(chatId: unknown): { host: ContinueHost; session: string; running: boolean } | null;
    /** The folder a plain turn of the chat runs in: the working folder of a chat continued in a terminal. */
    chatFolder(): string;
    continueFlags: ContinueFlags;
    logContinue?(event: 'open_in_continued' | 'open_in_continue_refused', fields: { Host: ContinueHost }): void;
    changeJason?: ChangeJasonDeps;
}

/** The refusals of the entry that continues a chat, in the desktop's own words. */
export const notContinuable = 'This chat cannot be continued in a terminal.';
export const continueRunning = 'A turn of this chat is running. Stop it or wait for it to end, then continue it in a terminal.';

/** The refusals of "Change Jason itself", in the desktop's own words. */
export const changeJasonNoHost = 'Change Jason itself needs Claude Code or Codex on this computer.';
export const changeJasonNotInDemo = 'Not in the demo: Change Jason itself is for your own installation.';

/**
 * The codes with which `jason session open --setup` says its own rule could not choose a host, nothing started: the
 * preference names a word this jason does not know as a host (`host_unknown`), the preference could not be read
 * (`preference_unreadable`), or the host chosen, the preference's or with none set Claude Code, is not installed
 * (`host_not_found`). Only these are run once more with the host the desktop found.
 */
export const hostUnchosen: readonly string[] = ['host_unknown', 'preference_unreadable', 'host_not_found'];

const campaignIdShape = /^cmp_[0-9A-Za-z]{1,64}$/;

/** The `error.message` of a CLI answer's JSON, when it has one. */
function cliSentence(stdout: string): string | null {
    try {
        const message = (JSON.parse(stdout) as { error?: { message?: unknown } }).error?.message;
        return typeof message === 'string' && message !== '' ? message : null;
    } catch {
        return null;
    }
}

/** The `error.code` of a CLI answer's JSON, when it has one. */
function cliCode(stdout: string): string | null {
    try {
        const code = (JSON.parse(stdout) as { error?: { code?: unknown } }).error?.code;
        return typeof code === 'string' ? code : null;
    } catch {
        return null;
    }
}

/** A refusal of a terminal the command line was asked to open, in its own sentence, chosen by the exit code. */
function terminalRefusal(answer: { exit: number; stdout: string; stderr: string }): string {
    // A usage error is said on stderr, its first line the sentence; a refusal is the JSON of its error on stdout.
    const usage = answer.exit === 2 ? (answer.stderr.split(/\r?\n/)[0] ?? '').trim() : '';
    return answer.exit === 3 ? "Jason's runtime is not running." : (cliSentence(answer.stdout) ?? (usage === '' ? null : usage) ?? 'A terminal could not be opened.');
}

export class OpenIn {
    readonly #deps: OpenInDeps;

    constructor(deps: OpenInDeps) {
        this.#deps = deps;
    }

    /** The folder the act is about, from the installation's own record, or the sentence why there is none. */
    async #folder(campaignId: unknown): Promise<string | OpenInRefusal> {
        if (campaignId !== undefined && campaignId !== null) {
            if (typeof campaignId !== 'string' || !campaignIdShape.test(campaignId)) return { refused: 'That is not an agent this desktop knows.' };
            const answer = await this.#deps.cli.run(['campaign', 'get', campaignId]);
            if (answer.exit === 3) return { refused: "Jason's runtime is not running." };
            if (answer.exit !== 0 && cliCode(answer.stdout) === 'not_found') return { refused: 'That agent is not in this installation.' };
            let path: unknown;
            try {
                path = answer.exit === 0 ? (JSON.parse(answer.stdout) as { folder_path?: unknown }).folder_path : undefined;
            } catch {
                path = undefined;
            }
            const absolute = this.#deps.probes.platform === 'win32' ? win32.isAbsolute : posix.isAbsolute;
            if (typeof path !== 'string' || !absolute(path) || !this.#deps.isDirectory(path)) {
                return { refused: "This agent's folder is not recorded, or it is not on this computer." };
            }
            return path;
        }
        const root = this.#deps.harnessRoot();
        return this.#deps.isDirectory(root) ? root : { refused: "Jason's folder is not there yet; it is made with the first agent." };
    }

    /**
     * What the menu offers for the folder of the open chat's campaign, or of the harness root with none; and, where the page
     * names the chat in front and that chat may be continued in a terminal, the entry that does it.
     */
    async targets(campaignId?: unknown, chatId?: unknown): Promise<OpenInAnswer | OpenInRefusal> {
        const folder = await this.#folder(campaignId);
        if (typeof folder !== 'string') return folder;
        const places = placesFound(this.#deps.probes, this.#deps.hosts(), this.#deps.browser !== undefined);
        const name = (this.#deps.probes.platform === 'win32' ? win32 : posix).basename(folder);
        const answer: OpenInAnswer = { places, default: defaultPlace(places, this.#deps.remembered()), folder: name };
        const found = chatId === undefined || chatId === null ? null : this.#deps.continuable(chatId);
        if (found !== null) answer.continue = { host: found.host, label: labels[found.host], running: found.running };
        if (this.#deps.changeJason?.live() === true) answer.changeJason = true;
        return answer;
    }

    /**
     * Continues the chat in a terminal: the host and the id of the session it named are read from the desktop's own record
     * of the chat, and the bundled `jason session open --here` opens it in the folder a plain turn runs in. Nothing the page
     * sends but the chat's id is read. Refused, with nothing run, for a chat that may not be continued or whose turn runs;
     * a refusal of the CLI is said in its own sentence. Never kept as the next default.
     */
    async continueChat(chatId: unknown): Promise<OpenInRefusal | null> {
        const deps = this.#deps;
        const found = deps.continuable(chatId);
        if (found === null) return { refused: notContinuable };
        if (found.running) return { refused: continueRunning };
        const answer = await deps.cli.run(['session', 'open', ...deps.continueFlags(found.host, found.session)], { cwd: deps.chatFolder() });
        if (answer.exit === 0) {
            deps.logContinue?.('open_in_continued', { Host: found.host });
            return null;
        }
        deps.logContinue?.('open_in_continue_refused', { Host: found.host });
        return { refused: terminalRefusal(answer) };
    }

    /**
     * "Change Jason itself": the bundled `jason session open --setup` with no host, so the preference chooses as typed by
     * hand. Where the command line says it could not choose (`hostUnchosen`), it is run once more, and only once, with the
     * host this desktop found, Claude Code, else Codex; with neither found, a host not installed is said as the two it
     * needs. Nothing the page sends is read, and nothing is run in a demo run. A refusal is said by its code:
     * `host_not_found` in the desktop's sentence, any other in the command line's own. Never kept as the next default.
     */
    async changeJason(): Promise<OpenInRefusal | null> {
        const deps = this.#deps;
        const own = deps.changeJason;
        if (own === undefined || !own.live()) return { refused: changeJasonNotInDemo };
        let host: ChangeJasonHost = 'preference';
        let answer = await deps.cli.run(['session', 'open', ...own.flags(null)]);
        let code = answer.exit === 0 ? null : cliCode(answer.stdout);
        if (answer.exit === 1 && code !== null && hostUnchosen.includes(code)) {
            const hosts = deps.hosts();
            const found: ContinueHost | null = hosts.claude ? 'claude' : hosts.codex ? 'codex' : null;
            if (found !== null) {
                host = found;
                answer = await deps.cli.run(['session', 'open', ...own.flags(found)]);
                code = answer.exit === 0 ? null : cliCode(answer.stdout);
            }
        }
        if (answer.exit === 0) {
            own.log?.('open_in_change_jason', { Host: host, Exit: 0 });
            return null;
        }
        own.log?.('open_in_change_jason_refused', { Host: host, Exit: answer.exit });
        return { refused: answer.exit === 1 && code === 'host_not_found' ? changeJasonNoHost : terminalRefusal(answer) };
    }

    /** Opens the folder in the place the name says; a name not on this computer's list, or a path, is refused. */
    async open(target: unknown, campaignId?: unknown): Promise<OpenInRefusal | null> {
        const places = placesFound(this.#deps.probes, this.#deps.hosts(), this.#deps.browser !== undefined);
        if (!isPlace(target) || !places.some((place) => place.id === target)) return { refused: 'That is not a place this computer can open the folder in.' };
        // A browser opens the chat, not a folder: none is read, and it is never kept as the place used last.
        if (target === 'browser') {
            const failed = this.#deps.browser === undefined ? 'That is not a place this computer can open the folder in.' : await this.#deps.browser();
            this.#deps.log?.(failed === null ? 'open_in' : 'open_in_refused', { Place: target });
            return failed === null ? null : { refused: failed };
        }
        const folder = await this.#folder(campaignId);
        if (typeof folder !== 'string') return folder;
        const deps = this.#deps;
        let refusal: OpenInRefusal | null = null;
        if (target === 'copy') {
            deps.copy(folder);
        } else if (target === 'claude' || target === 'codex' || target === 'terminal') {
            const scope = campaignId === undefined || campaignId === null ? null : String(campaignId);
            // With no campaign the verb takes its project from the working folder before it falls back to the harness root, so
            // the harness root is also its working folder: it can find no project there.
            const answer = await deps.cli.run(['session', 'open', ...deps.terminalFlags(target, scope)], scope === null ? { cwd: folder } : undefined);
            if (answer.exit !== 0) {
                refusal = { refused: cliSentence(answer.stdout) ?? (answer.exit === 3 ? "Jason's runtime is not running." : 'A terminal could not be opened.') };
            }
        } else if (target === 'files') {
            const program = fileManager(deps.probes);
            // File Explorer reads a comma in its argument as a separator, so a folder whose path holds one is not shown there.
            const failed =
                program === null
                    ? 'The file manager could not be found.'
                    : deps.probes.platform === 'darwin' && /\.app$/i.test(posix.basename(folder))
                      ? 'A folder named like an application might be started rather than shown. Use Copy path.'
                    : deps.probes.platform === 'win32' && folder.includes(',')
                      ? 'File Explorer cannot show a folder whose path holds a comma. Use Copy path.'
                      : await deps.start(fileManagerLine(program, folder, deps.probes.platform));
            if (failed !== null) refusal = { refused: failed };
        } else {
            const found = findEditor(target, deps.probes);
            const failed = found === null ? `${labels[target]} could not be found.` : await deps.start(editorLine(found, folder));
            if (failed !== null) refusal = { refused: failed };
        }
        if (refusal !== null) {
            deps.log?.('open_in_refused', { Place: target });
            return refusal;
        }
        if (!notFolderPlaces.includes(target)) deps.remember(target);
        deps.log?.('open_in', { Place: target });
        return null;
    }
}
