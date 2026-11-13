/**
 * "Open in": the places the page's split button offers for the folder the person is working in, and the one act that opens
 * a place. The page sends a place's name and, where a campaign's chat is open, the campaign's id, and never a path: the
 * name is held against the list this module makes for this computer, and the folder is read here from the installation's
 * own record (the campaign's recorded folder, else the harness root). Nothing here starts a process: an editor and the
 * file manager are started by `open-in-start.ts`, the three terminals by the bundled `jason session open`, and nothing is
 * written into the person's folders.
 */

import { posix, win32 } from 'node:path';

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
}

/** What the menu is drawn from: the places on this computer, the one the main part opens, and the folder's name. */
export interface OpenInAnswer {
    places: Place[];
    default: PlaceId;
    /** The last part of the folder's path, for the button's words; never the path. */
    folder: string;
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
    return found.map((id) => ({ id, label: labels[id] }));
}

/**
 * The place the main part opens: the one used last while it is still here, else Claude Code, else the first place that
 * opens something. Copy path is never the default; a terminal is always there.
 */
export function defaultPlace(places: readonly Place[], remembered: PlaceId | null): PlaceId {
    const here = (id: PlaceId) => places.some((place) => place.id === id);
    if (remembered !== null && remembered !== 'copy' && here(remembered)) return remembered;
    if (here('claude')) return 'claude';
    return places.find((place) => place.id !== 'copy')?.id ?? 'terminal';
}

/** The arguments `jason session open` is given for a terminal; one function holds the form the CLI takes. */
export type TerminalFlags = (kind: TerminalKind, campaignId: string | null) => string[];

export interface OpenInDeps {
    probes: Probes;
    /** The agent hosts the desktop finds. */
    hosts(): { claude: boolean; codex: boolean };
    /** The bundled `jason`. */
    cli: { run(args: readonly string[]): Promise<{ exit: number; stdout: string; stderr: string }> };
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
    log?(event: 'open_in' | 'open_in_refused', fields: { Place: PlaceId }): void;
}

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

    /** What the menu offers for the folder of the open chat's campaign, or of the harness root with none. */
    async targets(campaignId?: unknown): Promise<OpenInAnswer | OpenInRefusal> {
        const folder = await this.#folder(campaignId);
        if (typeof folder !== 'string') return folder;
        const places = placesFound(this.#deps.probes, this.#deps.hosts());
        const name = (this.#deps.probes.platform === 'win32' ? win32 : posix).basename(folder);
        return { places, default: defaultPlace(places, this.#deps.remembered()), folder: name };
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
            const answer = await deps.cli.run(['session', 'open', ...deps.terminalFlags(target, scope)]);
            if (answer.exit !== 0) {
                refusal = { refused: cliSentence(answer.stdout) ?? 'A terminal could not be opened.' };
            }
        } else if (target === 'files') {
            const program = fileManager(deps.probes);
            const failed = program === null ? 'The file manager could not be found.' : await deps.start({ file: program, args: [folder] });
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
        if (target !== 'copy') deps.remember(target);
        deps.log?.('open_in', { Place: target });
        return null;
    }
}
