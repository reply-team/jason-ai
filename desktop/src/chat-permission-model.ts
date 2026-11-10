// Pure: what happens when the chat's agent asks to use a tool its allowed list does not settle. The host asks only for
// what its own rules leave open; this decides without a person where the answer is already known, and otherwise says
// what to put on the person's card. The platform is passed in, so both platforms' path rules are tested on either.

import { posix, win32 } from 'node:path';

export type Platform = 'win32' | 'darwin' | 'linux';

export interface ChatScope {
    platform: Platform;
    /** The folder a relative path is read against: the chat folder. */
    cwd: string;
    /** The chat folder, `<data>/desktop/chat`. */
    chat: string;
    /** The campaign and project folders added to the turn. */
    added: readonly string[];
    /** The data directory: nothing under it but the chat folder is the chat's. */
    data: string;
    /** The files refused first: the installation's settings file and the runtime's run file. */
    protectedFiles: readonly string[];
    /** The deny list's commands. */
    denied: readonly string[];
    /** The deny list's operations: a command whose text names one is refused, however it would reach the runtime. */
    deniedOperations: readonly string[];
    /** The real path of an existing path, links, junctions and short names followed; null when nothing is there. */
    real: (path: string) => string | null;
}

export type Judgement =
    | { verdict: 'allow' }
    | { verdict: 'deny'; message: string }
    /** `remember` is what "always, in this chat" would cover; null when an answer can only ever cover this request. */
    | { verdict: 'ask'; title: string; detail: string; remember: string | null };

const allow: Judgement = { verdict: 'allow' };
const detailLimit = 4000;
const readers: Readonly<Record<string, string>> = { Read: 'file_path', Glob: 'path', Grep: 'path', LS: 'path' };
const writers: Readonly<Record<string, string>> = { Write: 'file_path', Edit: 'file_path', MultiEdit: 'file_path', NotebookEdit: 'notebook_path' };
const shells = new Set(['Bash', 'PowerShell']);
/**
 * The tools an "always, in this chat" may cover whole: those that run nothing. None is named today, so every other tool's
 * card covers its one request.
 */
const runsNothing: ReadonlySet<string> = new Set<string>();
/** Inside a working folder, this one still configures the host for the next turn, so writing it is never silent. */
const hostSettings = '.claude';
const allowedReply = [['auth', 'status'], ['account', 'list']];

const dataRefusal = 'That path is the installation\'s own data, outside the chat\'s folder: the chat neither reads nor writes it. Nothing was done.';
const deviceRefusal = 'That path names a device or a volume, not a file in a folder: the chat neither reads nor writes it. Nothing was done.';
const networkRefusal = 'That path is on another computer, or reaches this one as another computer would: the chat never writes there. Nothing was done.';
const lengthRefusal = 'That path is longer than the system takes: the chat neither reads nor writes it. Nothing was done.';
/** The longest path each platform takes: the `\\?\` form's limit on Windows, `PATH_MAX` on macOS and Linux. */
const pathLimit = (platform: Platform) => (platform === 'win32' ? 32_767 : 4_096);
const commandRefusal = 'That command is a person\'s own act or changes the installation, so the chat never runs it: the person runs it themselves. Nothing was run.';

const pathsOf = (platform: Platform) => (platform === 'win32' ? win32 : posix);
const keyOf = (platform: Platform, path: string) => (platform === 'linux' ? path : path.toLowerCase());

function shorten(text: string): string {
    return text.length > detailLimit ? `${text.slice(0, detailLimit)}…` : text;
}

/** A path as the platform spells it whole: slashes, the `\\?\` and `\\?\UNC\` forms, `..` and relative paths settled. */
function spelled(value: string, scope: ChatScope): string {
    let text = value;
    if (scope.platform === 'win32') {
        text = text.replace(/\//g, '\\');
        if (/^\\\\[?.]\\UNC\\/i.test(text)) text = `\\\\${text.slice(8)}`;
        else if (/^\\\\[?.]\\/.test(text)) text = text.slice(4);
    }
    return pathsOf(scope.platform).resolve(scope.cwd, text);
}

/** A path that could reach another computer: never looked up, since a lookup can send the machine's credentials. */
function network(platform: Platform, path: string): boolean {
    return platform === 'win32' ? path.startsWith('\\\\') : path.startsWith('/net/');
}

/** Where a path really is: its nearest existing parent's real path, with the rest of the path after it. */
function located(value: string, scope: ChatScope): string {
    const paths = pathsOf(scope.platform);
    const path = spelled(value, scope);
    if (network(scope.platform, path)) return path;
    const rest: string[] = [];
    let at = path;
    for (;;) {
        const real = scope.real(at);
        if (real !== null) return rest.length === 0 ? real : paths.join(real, ...rest.reverse());
        const up = paths.dirname(at);
        if (up === at) return path;
        rest.push(paths.basename(at));
        at = up;
    }
}

function inside(scope: ChatScope, root: string, path: string): boolean {
    const separator = pathsOf(scope.platform).sep;
    const folder = keyOf(scope.platform, root);
    const key = keyOf(scope.platform, path);
    return key === folder || key.startsWith(folder.endsWith(separator) ? folder : `${folder}${separator}`);
}

/**
 * What the spelling alone says, before anything is looked up; null when the path may be looked up. A path too long for
 * the platform is refused. One that begins with `~` is a card, since the shell and the host may expand it where this
 * cannot follow. On Windows, after `\\?\` or `\\.\` only a drive or `UNC\` may follow: any other device form is refused.
 * A component that ends in a dot or a space, or carries a colon (a stream), is a card wherever it lies, since Windows
 * reads it as another name than the one spelled.
 */
function judgeSpelling(value: string, scope: ChatScope): Judgement | null {
    if (value.length > pathLimit(scope.platform)) return { verdict: 'deny', message: lengthRefusal };
    if (value.startsWith('~')) return { verdict: 'ask', title: 'Use a path that begins with ~', detail: shorten(value), remember: null };
    if (scope.platform !== 'win32') return null;
    let rest = value.replace(/\//g, '\\');
    if (/^\\\\[?.]\\/.test(rest)) {
        rest = rest.slice(4);
        if (!/^[a-z]:\\/i.test(rest) && !/^unc\\/i.test(rest)) return { verdict: 'deny', message: deviceRefusal };
    }
    if (/^[a-z]:\\/i.test(rest)) rest = rest.slice(2);
    const differs = rest.split('\\').some((part) => part !== '.' && part !== '..' && (/[. ]$/.test(part) || part.includes(':')));
    return differs ? { verdict: 'ask', title: 'Use a path Windows reads as another name', detail: shorten(value), remember: null } : null;
}

function judgePath(value: string, scope: ChatScope, writes: boolean): Judgement {
    const spelling = judgeSpelling(value, scope);
    if (spelling !== null) return spelling;
    if (writes && network(scope.platform, spelled(value, scope))) return { verdict: 'deny', message: networkRefusal };
    const paths = pathsOf(scope.platform);
    const path = located(value, scope);
    const key = keyOf(scope.platform, path);
    if (scope.protectedFiles.some((file) => keyOf(scope.platform, located(file, scope)) === key)) return { verdict: 'deny', message: dataRefusal };
    const chat = located(scope.chat, scope);
    if (inside(scope, located(scope.data, scope), path) && !inside(scope, chat, path)) return { verdict: 'deny', message: dataRefusal };
    const root = [chat, ...scope.added.map((folder) => located(folder, scope))].find((folder) => inside(scope, folder, path));
    if (root !== undefined) {
        const settings = paths.relative(root, path).split(paths.sep).some((part) => keyOf(scope.platform, part) === hostSettings);
        if (!(writes && settings)) return allow;
        return { verdict: 'ask', title: 'Change the agent\'s own settings', detail: shorten(path), remember: null };
    }
    if (writes) return { verdict: 'ask', title: 'Change a file outside the chat\'s folders', detail: shorten(path), remember: null };
    return { verdict: 'ask', title: 'Read a file outside the chat\'s folders', detail: shorten(path), remember: `read:${keyOf(scope.platform, paths.dirname(path))}` };
}

/** The worse of two judgements: a refusal over a card, a card over an allowance. */
function worse(one: Judgement, other: Judgement): Judgement {
    const rank = (judged: Judgement) => (judged.verdict === 'deny' ? 2 : judged.verdict === 'ask' ? 1 : 0);
    return rank(other) > rank(one) ? other : one;
}

/** Glob also reaches where its pattern climbs or points: the pattern's fixed leading folders are judged too. */
function judgeGlob(input: Record<string, unknown>, scope: ChatScope): Judgement {
    const folder = named(input['path']) ? input['path'] : scope.cwd;
    const judged = judgePath(folder, scope, false);
    const pattern = typeof input['pattern'] === 'string' ? input['pattern'] : '';
    const parts = pattern.split(/[\\/]/);
    const fixed: string[] = [];
    for (const part of parts) {
        if (/[*?[{]/.test(part)) break;
        fixed.push(part);
    }
    if (fixed.length === 0) return judged;
    const separator = pathsOf(scope.platform).sep;
    const leading = pattern.startsWith('/') && fixed[0] === '' ? `${separator}${fixed.slice(1).join(separator)}` : fixed.join(separator);
    return worse(judged, judgePath(pathsOf(scope.platform).resolve(spelled(folder, scope), leading || '.'), scope, false));
}

/** True when a shell could make the command more than one command, or send its output somewhere. */
export function isCompound(command: string): boolean {
    return /&&|\|\||[;|&`\n\r<>]|\$\(/.test(command);
}

/** What "always, in this chat" covers of a command: the program and its first word; nothing for a compound command. */
export function commandPrefix(command: string): string | null {
    if (isCompound(command)) return null;
    const [program, first] = command.trim().split(/\s+/);
    const plain = /^[A-Za-z0-9._/\\:@+-]+$/;
    if (program === undefined || first === undefined || !plain.test(program) || !plain.test(first) || first.startsWith('-')) return null;
    return `${program} ${first}`;
}

function unquoted(token: string): string {
    return token.replace(/^['"{]+|['"}]+$/g, '');
}

/** The program a token names, as the platform finds it: its file name without a folder or an executable's extension. */
function programOf(platform: Platform, token: string): string {
    const name = token.split(/[\\/]/).pop() ?? '';
    const bare = platform === 'win32' ? name.replace(/\.(exe|cmd|bat|com|ps1)$/i, '') : name;
    return keyOf(platform, bare);
}

/**
 * True when a jason command's words hold a deny-list entry: its first word is the command's first, and its other words
 * follow in order with anything between, so options before a verb (`approval --actor human:x approve`) do not hide it.
 * The first word is held in place, so `contact update ... update` is not the `update` entry.
 */
function holdsEntry(words: readonly string[], entry: string): boolean {
    const [first, ...rest] = entry.split(' ');
    if (words[0] !== first) return false;
    let at = 1;
    for (const word of rest) {
        while (at < words.length && words[at] !== word) at++;
        if (at === words.length) return false;
        at++;
    }
    return true;
}

/**
 * What the text holds of jason: `deny` when any jason command inside it, in any part, form or nesting, starts with an
 * option, carries a word that begins with `@` (a file of words), or holds a deny-list entry, or when the text names a
 * denied operation however it would reach the runtime; `jason` when it runs jason otherwise; null when it does not.
 */
function jasonIn(command: string, scope: ChatScope): 'deny' | 'jason' | null {
    let found: 'jason' | null = null;
    for (const part of command.split(/&&|\|\||\$\(|[;|&`\n\r<>(){}]/)) {
        const tokens = part.trim().split(/\s+/).map(unquoted).filter((token) => token !== '');
        for (const [at, token] of tokens.entries()) {
            if (programOf(scope.platform, token) !== 'jason') continue;
            found = 'jason';
            const words = tokens.slice(at + 1);
            if (words[0]?.startsWith('-') || words.some((word) => word.startsWith('@'))) return 'deny';
            if (scope.denied.some((entry) => holdsEntry(words, entry))) return 'deny';
        }
    }
    const text = command.toLowerCase();
    if (scope.deniedOperations.some((operation) => new RegExp(`(?<![a-z0-9_])${operation.replace('.', '\\.')}(?![a-z0-9_])`).test(text))) return 'deny';
    return found;
}

function judgeCommand(tool: string, command: string, scope: ChatScope): Judgement {
    const jason = jasonIn(command, scope);
    if (jason === 'deny') return { verdict: 'deny', message: commandRefusal };
    // The host runs the allow list's verbs itself; a jason command that reaches the model is none of them, so it is the
    // person's to answer, each time.
    if (jason === 'jason') return { verdict: 'ask', title: 'Run a command', detail: shorten(command), remember: null };
    // Only the Bash tool's commands are fenced by the host and may be allowed or remembered; any other tool's command is
    // the person's to answer, each time.
    if (tool !== 'Bash') return { verdict: 'ask', title: 'Run a command', detail: shorten(command), remember: null };
    if (!isCompound(command)) {
        const tokens = command.trim().split(/\s+/);
        if (tokens[0] === 'reply' && allowedReply.some((words) => words.every((word, index) => tokens[index + 1] === word))) return allow;
    }
    const prefix = commandPrefix(command);
    return { verdict: 'ask', title: 'Run a command', detail: shorten(command), remember: prefix === null ? null : `${tool}:${prefix}` };
}

function hostOf(url: string): string | null {
    try {
        return new URL(url).host || null;
    } catch {
        return null;
    }
}

function named(value: unknown): value is string {
    return typeof value === 'string' && value !== '';
}

/** Read and the writers name the file they act on; one that names none is a card, never the chat folder by default. */
function unnamed(tool: string, input: Record<string, unknown>): Judgement {
    return { verdict: 'ask', title: `Use ${tool} with no file named`, detail: shorten(JSON.stringify(input)), remember: null };
}

/** Decides without a person where the answer is already known, and otherwise says what to put in front of them. */
export function judge(tool: string, input: Record<string, unknown>, scope: ChatScope): Judgement {
    if (tool === 'Glob') return judgeGlob(input, scope);
    const reader = readers[tool];
    if (reader !== undefined) {
        const value = input[reader];
        if (named(value)) return judgePath(value, scope, false);
        return tool === 'Read' ? unnamed(tool, input) : judgePath('.', scope, false);
    }
    const writer = writers[tool];
    if (writer !== undefined) {
        const value = input[writer];
        return named(value) ? judgePath(value, scope, true) : unnamed(tool, input);
    }
    const command = input['command'];
    if (typeof command === 'string') return judgeCommand(tool, command, scope);
    if (shells.has(tool)) return judgeCommand(tool, '', scope);
    if (tool === 'WebFetch') {
        const url = typeof input['url'] === 'string' ? input['url'] : '';
        const host = hostOf(url);
        return { verdict: 'ask', title: 'Open a web page', detail: shorten(url), remember: host === null ? null : `web:${host.toLowerCase()}` };
    }
    return { verdict: 'ask', title: `Use ${tool}`, detail: shorten(JSON.stringify(input)), remember: runsNothing.has(tool) ? `tool:${tool}` : null };
}
