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
 * And one entry opens the person's own AI on Jason itself, "Change Jason itself": the bundled `jason session open --setup`.
 * Where the person has stated a host of their own, the preference `agent.interactive_host`, the command line's own rule
 * chooses it; where they have stated none, the desktop names the program the cards mark In use, Claude Code or Codex found
 * here; else the command line's rule chooses, Claude Code. Only where the host could not be opened, by the codes in
 * `hostUnchosen`, is it run once more with a host this desktop found that was not tried, Claude Code, else Codex. It is
 * offered in the person's own installation only, and it is never the default.
 */

import { posix, win32 } from 'node:path';
import type { PlaceIcon } from './open-in-icons.ts';
import type { JasonOnPath } from './path-command.ts';

/** Every place the menu can offer, in the order it offers them. */
export const placeIds = ['claude', 'codex', 'terminal', 'vscode', 'cursor', 'antigravity', 'windsurf', 'zed', 'files', 'copy'] as const;
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
    copy: 'Copy path',
};

export interface Place {
    id: PlaceId;
    label: string;
    /**
     * The application's own icon, as pixels, where the desktop could read it (`open-in-icons.ts`); absent, the page draws
     * its own glyph for the place. Never a path or a file.
     */
    icon?: PlaceIcon;
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
    /**
     * Which `jason` the PATH an editor is started with finds by name, and the way out the Jason menu offers
     * (`PathCommand.onPath`). Absent where the desktop cannot tell, or offers no PATH command: in the demo, say.
     */
    jason?: JasonOnPath;
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

/** Where macOS keeps the file manager and the terminal, whose own icons the menu draws for them. */
export const finderApp = '/System/Library/CoreServices/Finder.app';
export const terminalApp = '/System/Applications/Utilities/Terminal.app';

/**
 * The application whose own icon a place draws, by the desktop's own detection and never by anything the page sends; null
 * for a place with no application of its own (Claude Code, Codex, Copy path) and for every place on Linux.
 * Windows: an editor's program and File Explorer; its Terminal is the command line's to choose, so not known here. macOS:
 * an editor's application, Finder and Terminal.
 */
export function iconPath(id: PlaceId, probes: Probes): string | null {
    if (probes.platform === 'win32') {
        if (id === 'files') return fileManager(probes);
        if (!(editorIds as readonly string[]).includes(id)) return null;
        const found = findEditor(id as EditorId, probes);
        return found !== null && win32.extname(found.path).toLowerCase() === '.exe' ? found.path : null;
    }
    if (probes.platform === 'darwin') {
        if (id === 'files') return finderApp;
        if (id === 'terminal') return terminalApp;
        if (!(editorIds as readonly string[]).includes(id)) return null;
        const found = findEditor(id as EditorId, probes);
        return found !== null && found.kind === 'app' ? found.path : null;
    }
    return null;
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

/** The places this computer has, in the menu's order; the three terminal entries need no program of the desktop's. */
export function placesFound(probes: Probes, hosts: { claude: boolean; codex: boolean }): Place[] {
    const found: PlaceId[] = [];
    if (hosts.claude) found.push('claude');
    if (hosts.codex) found.push('codex');
    found.push('terminal');
    for (const id of editorIds) if (findEditor(id, probes) !== null) found.push(id);
    if (fileManager(probes) !== null) found.push('files');
    found.push('copy');
    // The generic places (Terminal, Copy path, and the file manager where the system has no name for it) carry no label: the
    // page has its own words for them. A product's name (VS Code, Finder, File Explorer, Claude Code) is the desktop's.
    return found.map((id) => ({ id, label: id === 'files' ? fileManagerName(probes.platform) : id === 'terminal' || id === 'copy' ? '' : labels[id] }));
}

/** The places that do not open the folder, so are never the main part's: Copy path. */
export const notFolderPlaces: readonly PlaceId[] = ['copy'];

/**
 * The place the main part opens: the one used last while it is still here, else Claude Code, else the first place that
 * opens something. Copy path is never the default; a terminal is always there.
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

/**
 * Where the host of "Change Jason itself" came from, as its log lines say it: the command line's own rule (the preference,
 * else Claude Code), the program the cards mark In use where no preference is stated, or the host this desktop found and
 * named for the second run.
 */
export type ChangeJasonHost = 'preference' | 'in_use_claude' | 'in_use_codex' | 'desktop_claude' | 'desktop_codex';

/**
 * The command line's error codes a log line of "Change Jason itself" names, a closed table of the codes its act answers;
 * any other is `other`, and an answer with no code `none`, so no text the command line printed reaches the log.
 */
export const changeJasonLoggedCodes: readonly string[] = ['host_unknown', 'preference_unreadable', 'host_not_found', 'folder_missing', 'terminal_not_found', 'terminal_start_failed'];

/** What "Change Jason itself" needs from the run. Absent, the entry is not offered. */
export interface ChangeJasonDeps {
    flags: ChangeJasonFlags;
    /**
     * The host word of the profile the agents' roles run on by default, as the cards' In use reads it (`Setup.rolesHost`);
     * null where it names none or could not be read. Asked at the click only, never when the menu is built.
     */
    inUse(): Promise<string | null>;
    log?(event: 'open_in_change_jason', fields: { Host: ChangeJasonHost; Exit: number }): void;
    log?(event: 'open_in_change_jason_refused', fields: { Host: ChangeJasonHost; Exit: number; Code: string }): void;
}

export interface OpenInDeps {
    probes: Probes;
    /** The agent hosts the desktop finds. */
    hosts(): { claude: boolean; codex: boolean };
    /** The bundled `jason`. */
    cli: { run(args: readonly string[], options?: { cwd?: string }): Promise<{ exit: number; stdout: string; stderr: string }> };
    /** The harness root of this run's installation. */
    harnessRoot(): string;
    isDirectory(path: string): boolean;
    remembered(): PlaceId | null;
    remember(id: PlaceId): void;
    /** Starts a program and leaves it running; the sentence it could not be started with, or null. */
    start(line: Line): Promise<string | null>;
    /** Writes to the clipboard, from the main process. */
    copy(text: string): void;
    terminalFlags: TerminalFlags;
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
    /** Which `jason` the PATH finds by name (`PathCommand.onPath`); absent, or undefined, where that is not told. */
    jason?(): JasonOnPath | undefined;
    /**
     * The own icon of the application at a path `iconPath` gave, or null (`IconReader.icon`): it never waits long, and a
     * place without one keeps the page's glyph. Absent, no place carries an icon.
     */
    icon?(path: string): Promise<PlaceIcon | null>;
}

/** The refusals of the entry that continues a chat, in the desktop's own words. */
export const notContinuable = 'This chat cannot be continued in a terminal.';
export const continueRunning = 'A turn of this chat is running. Stop it or wait for it to end, then continue it in a terminal.';

/** The refusals of "Change Jason itself", in the desktop's own words. */
export const changeJasonNoHost = 'Change Jason itself needs Claude Code or Codex on this computer.';
export const changeJasonUnoffered = 'Change Jason itself is not offered here.';

/**
 * The codes with which `jason session open --setup` says its own rule could not choose a host, nothing started: the
 * preference names a word this jason does not know as a host (`host_unknown`), the preference could not be read
 * (`preference_unreadable`), or the host chosen, the preference's or with none set Claude Code, is not installed
 * (`host_not_found`). Only these are run once more with the host the desktop found.
 */
export const hostUnchosen: readonly string[] = ['host_unknown', 'preference_unreadable', 'host_not_found'];

/** The preference that names the host of a person's own session, and the host the command line takes with none set. */
const hostPreference = 'agent.interactive_host';
const unsetHost = 'claude_code';

/** The host preference as `jason preference list` answers it (`statedPreference`). */
type StatedPreference = 'unread' | 'none' | { value: unknown };

/**
 * The host preference as `jason preference list` answers it: `unread` where the list cannot be read, `none` where the
 * person has stated no host (the key is listed with no value, or not listed), else the value they stated.
 */
function statedPreference(answer: { exit: number; stdout: string }): StatedPreference {
    if (answer.exit !== 0) return 'unread';
    try {
        const items = (JSON.parse(answer.stdout) as { items?: unknown }).items;
        if (!Array.isArray(items)) return 'unread';
        const entry = items.find((item) => typeof item === 'object' && item !== null && (item as { key?: unknown }).key === hostPreference) as { value?: unknown } | undefined;
        return entry?.value === undefined || entry.value === null ? 'none' : { value: entry.value };
    } catch {
        return 'unread';
    }
}

/**
 * The host word the command line tried with no host named, as it read the preference: the preference's word, else Claude
 * Code's; null when the list could not be read, so that which host was tried is not known.
 */
function triedHost(preference: StatedPreference): string | null {
    if (preference === 'unread') return null;
    const word = preference === 'none' ? undefined : (preference.value as { host?: unknown } | null | undefined)?.host;
    return typeof word === 'string' ? word : unsetHost;
}

/**
 * The host "Change Jason itself" names where no preference is stated: the program the cards mark In use, by its profile's
 * host word, where that is Claude Code or Codex and the desktop finds it here, as the card's In use needs; null for any
 * other word, a host not found, or none, so the command line's own rule chooses as it does with no host named. Claude
 * Code's version floor, which the card also needs, is not read here: below it Claude Code is named all the same, the host
 * the command line's own rule takes with no preference stated, so the act opens what it opened before.
 */
export function inUseHost(word: string | null, hosts: { claude: boolean; codex: boolean }): ContinueHost | null {
    if (word === 'claude_code') return hosts.claude ? 'claude' : null;
    if (word === 'codex') return hosts.codex ? 'codex' : null;
    return null;
}

/** A host's word on the command line. */
const hostWord = (host: ContinueHost): string => (host === 'claude' ? 'claude_code' : host);

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
        const places = await this.#withIcons(placesFound(this.#deps.probes, this.#deps.hosts()));
        const name = (this.#deps.probes.platform === 'win32' ? win32 : posix).basename(folder);
        const answer: OpenInAnswer = { places, default: defaultPlace(places, this.#deps.remembered()), folder: name };
        const found = chatId === undefined || chatId === null ? null : this.#deps.continuable(chatId);
        if (found !== null) answer.continue = { host: found.host, label: labels[found.host], running: found.running };
        if (this.#deps.changeJason !== undefined) answer.changeJason = true;
        let jason: JasonOnPath | undefined;
        try {
            jason = this.#deps.jason?.();
        } catch {
            jason = undefined;
        }
        if (jason !== undefined) answer.jason = jason;
        return answer;
    }

    /** The places, each with its application's own icon where one was read; a read that fails or is slow leaves the glyph. */
    async #withIcons(places: Place[]): Promise<Place[]> {
        const deps = this.#deps;
        if (deps.icon === undefined) return places;
        return Promise.all(
            places.map(async (place) => {
                const path = iconPath(place.id, deps.probes);
                if (path === null) return place;
                const icon = await Promise.resolve()
                    .then(() => deps.icon?.(path) ?? null)
                    .catch(() => null);
                return icon === null ? place : { ...place, icon };
            }),
        );
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
     * "Change Jason itself": the bundled `jason session open --setup`. At the click, and only then, the host preference is
     * read with `jason preference list` and the program the cards mark In use with `inUse`, the two side by side. A stated
     * preference is the person's own word: the command is run with no host, so it chooses as typed by hand. With none
     * stated, the program In use is named with `--host` (`inUseHost`); where that names none, or either read fails, the
     * command is run with no host, as before. Where the command line says the host could not be opened (`hostUnchosen`),
     * it is run once more, and only once, with a host this desktop found that was not tried, Claude Code, else Codex
     * (`#secondHost`). A host not installed where the desktop finds no other is said as the two the entry needs; every
     * other refusal, the second run's included, in the command line's own sentence. Nothing the page sends is read, and
     * nothing is run where the entry is not offered. Never kept as the next default.
     */
    async changeJason(): Promise<OpenInRefusal | null> {
        const deps = this.#deps;
        const own = deps.changeJason;
        if (own === undefined) return { refused: changeJasonUnoffered };
        // Both reads are awaited together, so which of them lands first decides nothing.
        const [listed, rolesWord] = await Promise.all([deps.cli.run(['preference', 'list']), own.inUse().catch(() => null)]);
        const preference = statedPreference(listed);
        const named = preference === 'none' ? inUseHost(rolesWord, deps.hosts()) : null;
        let host: ChangeJasonHost = named === null ? 'preference' : named === 'claude' ? 'in_use_claude' : 'in_use_codex';
        let answer = await deps.cli.run(['session', 'open', ...own.flags(named)]);
        let code = answer.exit === 0 ? null : cliCode(answer.stdout);
        let second: ContinueHost | 'none' | 'unknown' | null = null;
        if (answer.exit === 1 && code !== null && hostUnchosen.includes(code)) {
            second = this.#secondHost(code, named === null ? triedHost(preference) : hostWord(named));
            if (second === 'claude' || second === 'codex') {
                host = second === 'claude' ? 'desktop_claude' : 'desktop_codex';
                answer = await deps.cli.run(['session', 'open', ...own.flags(second)]);
                code = answer.exit === 0 ? null : cliCode(answer.stdout);
            }
        }
        if (answer.exit === 0) {
            own.log?.('open_in_change_jason', { Host: host, Exit: 0 });
            return null;
        }
        own.log?.('open_in_change_jason_refused', { Host: host, Exit: answer.exit, Code: code === null ? 'none' : changeJasonLoggedCodes.includes(code) ? code : 'other' });
        // The two hosts the entry needs are said only where the desktop found none the command line had not tried.
        return { refused: answer.exit === 1 && code === 'host_not_found' && second === 'none' ? changeJasonNoHost : terminalRefusal(answer) };
    }

    /**
     * The host the second run names: the first of Claude Code and Codex this desktop finds that was not tried already. A
     * preference the command line could not read or does not know tried no host. A host not installed was `tried`: the
     * host the first run named, else the preference's word, else Claude Code's, as the click's `jason preference list`
     * read them; a list that could not be read leaves it null, `unknown`, and there is no second run. `none` where the
     * desktop finds no host it may name.
     */
    #secondHost(code: string, tried: string | null): ContinueHost | 'none' | 'unknown' {
        const deps = this.#deps;
        if (code !== 'host_not_found') tried = null;
        else if (tried === null) return 'unknown';
        const hosts = deps.hosts();
        const found = (['claude', 'codex'] as const).filter((each) => hosts[each] && hostWord(each) !== tried);
        return found[0] ?? 'none';
    }

    /** Opens the folder in the place the name says; a name not on this computer's list, or a path, is refused. */
    async open(target: unknown, campaignId?: unknown): Promise<OpenInRefusal | null> {
        const places = placesFound(this.#deps.probes, this.#deps.hosts());
        if (!isPlace(target) || !places.some((place) => place.id === target)) return { refused: 'That is not a place this computer can open the folder in.' };
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
