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
/** Inside a working folder, this one still configures the host for the next turn, so writing it is never silent. */
const hostSettings = '.claude';
const allowedReply = [['auth', 'status'], ['account', 'list']];

const dataRefusal = 'That path is the installation\'s own data, outside the chat\'s folder: the chat neither reads nor writes it. Nothing was done.';
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

function judgePath(value: unknown, scope: ChatScope, writes: boolean): Judgement {
    const paths = pathsOf(scope.platform);
    const path = located(typeof value === 'string' && value !== '' ? value : '.', scope);
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
    const folder = typeof input['path'] === 'string' && input['path'] !== '' ? input['path'] : scope.cwd;
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

/** True when any jason command inside the text, in any part, form or nesting, is one the deny list names. */
function deniedCommand(command: string, scope: ChatScope): boolean {
    for (const part of command.split(/&&|\|\||\$\(|[;|&`\n\r<>(){}]/)) {
        const tokens = part.trim().split(/\s+/).map(unquoted).filter((token) => token !== '');
        tokens.forEach((token, at) => {
            if (programOf(scope.platform, token) !== 'jason') return;
            const words = tokens.slice(at + 1);
            if (words[0]?.startsWith('-')) throw denial;
            const leading: string[] = [];
            for (const word of words) {
                if (word.startsWith('-')) break;
                leading.push(word);
            }
            for (const entry of scope.denied) {
                const verb = entry.split(' ');
                if (verb.every((word, index) => leading[index] === word)) throw denial;
            }
        });
    }
    for (const entry of scope.denied) {
        const [noun, verb] = entry.split(' ');
        const operation = verb === undefined ? `/v1/${noun}\\.` : `/v1/${noun}\\.${verb.replace(/-/g, '_')}\\b`;
        if (new RegExp(operation, 'i').test(command)) return true;
    }
    return false;
}

const denial = new Error('denied');

function judgeCommand(tool: string, command: string, scope: ChatScope): Judgement {
    try {
        if (deniedCommand(command, scope)) return { verdict: 'deny', message: commandRefusal };
    } catch (error) {
        if (error === denial) return { verdict: 'deny', message: commandRefusal };
        throw error;
    }
    if (!isCompound(command)) {
        const tokens = command.trim().split(/\s+/);
        if (tokens[0] === 'jason') return allow;
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

/** Decides without a person where the answer is already known, and otherwise says what to put in front of them. */
export function judge(tool: string, input: Record<string, unknown>, scope: ChatScope): Judgement {
    if (tool === 'Glob') return judgeGlob(input, scope);
    const reader = readers[tool];
    if (reader !== undefined) return judgePath(input[reader], scope, false);
    const writer = writers[tool];
    if (writer !== undefined) return judgePath(input[writer], scope, true);
    if (shells.has(tool)) return judgeCommand(tool, typeof input['command'] === 'string' ? input['command'] : '', scope);
    if (tool === 'WebFetch') {
        const url = typeof input['url'] === 'string' ? input['url'] : '';
        const host = hostOf(url);
        return { verdict: 'ask', title: 'Open a web page', detail: shorten(url), remember: host === null ? null : `web:${host.toLowerCase()}` };
    }
    return { verdict: 'ask', title: `Use ${tool}`, detail: shorten(JSON.stringify(input)), remember: `tool:${tool}` };
}
