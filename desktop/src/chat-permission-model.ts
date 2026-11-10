// Pure: what happens when the chat's agent asks to use a tool its allowed list does not settle. The host asks only for
// what its own rules leave open; this decides without a person where the answer is already known, and otherwise says
// what to put on the person's card. The platform is passed in, so both platforms' path rules are tested on either.

import { posix, win32 } from 'node:path';
import { serverName } from './chat-permission-endpoint.ts';

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
    /** The person's home folder, for `~/.jason` when the data directory is the default one. */
    home: string;
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
    /** A card whose answer can only ever cover this request. */
    | { verdict: 'ask'; title: string; detail: string; remember: null }
    /**
     * A card "always, in this chat" may answer: `remember` is the key it would keep, and `covers` what that covers, as
     * the page prints it on the button, so the person sees what they grant.
     */
    | { verdict: 'ask'; title: string; detail: string; remember: string; covers: string };

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
/**
 * Inside a working folder, these still act on what runs next: `.claude` configures the host for the next turn, and
 * `.git` holds a repository's hooks and configuration, which git runs. Writing under either is never silent.
 */
const guardedFolders: Readonly<Record<string, string>> = {
    '.claude': 'Change the agent\'s own settings',
    '.git': 'Change a repository\'s own files',
};
const allowedReply = [['auth', 'status'], ['account', 'list']];

const dataRefusal = 'That path is the installation\'s own data, outside the chat\'s folder: the chat neither reads nor writes it. Nothing was done.';
const deviceRefusal = 'That path names a device or a volume, not a file in a folder: the chat neither reads nor writes it. Nothing was done.';
const networkRefusal = 'That path is on another computer, or reaches this one as another computer would: the chat never writes there. Nothing was done.';
const climbRefusal = 'That pattern climbs out of its folder with ..: the chat does not take it. Name the folder as the path instead. Nothing was done.';
const ownToolRefusal = 'That tool answers the chat\'s own permission requests: only the person answers them. Nothing was done.';
const searchRefusal = 'That folder holds the installation\'s own data, so a search or a listing of it would walk that data: the chat does not take it. Name a narrower folder, one that does not hold the data directory. Nothing was done.';
const lengthRefusal ='That path is longer than the system takes: the chat neither reads nor writes it. Nothing was done.';
/** The longest path each platform takes: the `\\?\` form's limit on Windows, `PATH_MAX` on macOS and Linux. */
const pathLimit = (platform: Platform) => (platform === 'win32' ? 32_767 : 4_096);
const replyRefusal = 'The chat runs only reply auth status and reply account list of reply: provider work goes through jason, and the connection is made on the Connect card. Nothing was run.';
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
        const guarded = paths.relative(root, path).split(paths.sep).map((part) => guardedFolders[keyOf(scope.platform, part)]).find((title) => title !== undefined);
        if (!(writes && guarded !== undefined)) return allow;
        return { verdict: 'ask', title: guarded, detail: shorten(path), remember: null };
    }
    if (writes) return { verdict: 'ask', title: 'Change a file outside the chat\'s folders', detail: shorten(path), remember: null };
    return {
        verdict: 'ask',
        title: 'Read a file outside the chat\'s folders',
        detail: shorten(path),
        remember: `read:${keyOf(scope.platform, paths.dirname(path))}`,
        covers: `files in ${shorten(paths.dirname(path))}`,
    };
}

const walkTitles: Readonly<Record<string, string>> = {
    Grep: 'Search a folder outside the chat\'s folders',
    Glob: 'Find files in a folder outside the chat\'s folders',
    LS: 'List a folder outside the chat\'s folders',
};

/**
 * A folder a search or a listing walks: one that holds the data directory is refused, since the walk would read what the
 * chat never reads; any other is judged as a read of it. Its card is remembered by the folder it names and by the tool's
 * own kind, so a grant covers that tool's walks of that folder only: never a Read, a sibling, a folder under it or
 * another tool's walk, and a Read's grant never covers it.
 */
function judgeFolder(tool: string, value: string, scope: ChatScope): Judgement {
    const judged = judgePath(value, scope, false);
    if (judged.verdict === 'deny' || judgeSpelling(value, scope) !== null) return judged;
    const folder = located(value, scope);
    if (inside(scope, folder, located(scope.data, scope))) return { verdict: 'deny', message: searchRefusal };
    if (judged.verdict !== 'ask' || judged.remember === null) return judged;
    return {
        verdict: 'ask',
        title: walkTitles[tool] ?? judged.title,
        detail: shorten(folder),
        remember: `${tool.toLowerCase()}:${keyOf(scope.platform, folder)}`,
        covers: `everything under ${shorten(folder)}`,
    };
}

/** The worse of two judgements: a refusal over a card, a card over an allowance. */
function worse(one: Judgement, other: Judgement): Judgement {
    const rank = (judged: Judgement) => (judged.verdict === 'deny' ? 2 : judged.verdict === 'ask' ? 1 : 0);
    return rank(other) > rank(one) ? other : one;
}

/**
 * Glob also reaches where its pattern climbs or points: the pattern's fixed leading folders are walked too, and its card
 * is remembered by the folder it walks.
 */
function judgeGlob(input: Record<string, unknown>, scope: ChatScope): Judgement {
    const folder = named(input['path']) ? input['path'] : scope.cwd;
    const judged = judgeFolder('Glob', folder, scope);
    const pattern = typeof input['pattern'] === 'string' ? input['pattern'] : '';
    // A .. anywhere, a brace's alternative included, climbs where the fixed leading folders do not show.
    if (pattern.split(/[\\/{},]/).includes('..')) return { verdict: 'deny', message: climbRefusal };
    if (pattern.startsWith('~')) return worse(judged, { verdict: 'ask', title: 'Use a path that begins with ~', detail: shorten(pattern), remember: null });
    const parts = pattern.split(/[\\/]/);
    const fixed: string[] = [];
    for (const part of parts) {
        if (/[*?[{]/.test(part)) break;
        fixed.push(part);
    }
    if (fixed.length === 0) return judged;
    const separator = pathsOf(scope.platform).sep;
    const leading = pattern.startsWith('/') && fixed[0] === '' ? `${separator}${fixed.slice(1).join(separator)}` : fixed.join(separator);
    // The walked folder's card comes first, so that a tie keeps its key rather than the path's.
    return worse(judgeFolder('Glob', pathsOf(scope.platform).resolve(spelled(folder, scope), leading || '.'), scope), judged);
}

/** True when a shell could make the command more than one command, or send its output somewhere. */
export function isCompound(command: string): boolean {
    return /&&|\|\||[;|&`\n\r<>]|\$\(/.test(command);
}

/**
 * The programs whose first word is another program or a whole script, so that "the program and its first word" would
 * cover anything: shells and the wrappers that run a command they are given. They are names to recognise, never to run,
 * and are written as one string so the process guard, which refuses a quoted shell name in any module but the login-shell
 * lookup, keeps holding this one as it holds the others.
 */
const runsAnother = new Set('cmd sh bash zsh pwsh powershell env sudo xargs npx wsl start'.split(' '));

/**
 * What "always, in this chat" covers of a command: the program and its first word. Nothing for a compound command, a
 * program named by a path, or a shell or exec wrapper, whose card covers its one request.
 */
export function commandPrefix(command: string): string | null {
    if (isCompound(command)) return null;
    const [program, first] = command.trim().split(/\s+/);
    const plain = /^[A-Za-z0-9._/\\:@+-]+$/;
    if (program === undefined || first === undefined || !plain.test(program) || !plain.test(first) || first.startsWith('-')) return null;
    if (/[\\/:]/.test(program) || runsAnother.has(program.toLowerCase().replace(/\.(exe|cmd|bat|com|ps1)$/, ''))) return null;
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

/**
 * True when any command inside the text, in any part, form or nesting, runs reply with words other than the two the chat
 * runs: `auth status` and `account list`. Read as jason is read, by every token whose program is reply.
 */
function replyRefused(command: string, scope: ChatScope): boolean {
    for (const part of command.split(/&&|\|\||\$\(|[;|&`\n\r<>(){}]/)) {
        const tokens = part.trim().split(/\s+/).map(unquoted).filter((token) => token !== '');
        for (const [at, token] of tokens.entries()) {
            if (programOf(scope.platform, token) !== 'reply') continue;
            const words = tokens.slice(at + 1);
            if (!allowedReply.some((pair) => pair.every((word, index) => words[index] === word))) return true;
        }
    }
    return false;
}

/** A text as paths in it are compared: one slash for any run of slashes or backslashes, case folded where the platform folds it. */
function pathText(platform: Platform, text: string): string {
    return keyOf(platform, text.replace(/[\\/]+/g, '/'));
}

/**
 * The variable the host's environment carries the data directory in, as each shell spells it. The text is compared case
 * folded where the platform folds it, so on Windows any letter case of the name is the variable.
 */
const dataVariables = ['$JASON_DATA_DIR', '${JASON_DATA_DIR}', '%JASON_DATA_DIR%', '$env:JASON_DATA_DIR'];

/**
 * True when the command's text names the data directory, or anything in it outside the chat folder, in any spelling a
 * shell takes: the native path, its forward-slash form, Git Bash's and the host rules' `/c/...` on Windows, the data
 * directory's variable, and `~/.jason` or `$HOME/.jason` when the data directory is the default one. A name that only
 * begins like it is not it.
 */
function namesData(command: string, scope: ChatScope): boolean {
    const paths = pathsOf(scope.platform);
    const text = pathText(scope.platform, command);
    const data = pathText(scope.platform, paths.resolve(scope.data)).replace(/\/$/, '');
    const chat = pathText(scope.platform, paths.resolve(scope.chat));
    const chatRest = chat.startsWith(`${data}/`) ? chat.slice(data.length) : null;
    const spellings = [data, ...dataVariables.map((variable) => pathText(scope.platform, variable))];
    const drive = /^([a-z]):\//i.exec(data);
    if (drive) spellings.push(`/${drive[1]}${data.slice(2)}`);
    if (data === `${pathText(scope.platform, paths.resolve(scope.home)).replace(/\/$/, '')}/.jason`) {
        for (const home of ['~', '$HOME', '${HOME}', '%USERPROFILE%', '$env:USERPROFILE']) spellings.push(pathText(scope.platform, `${home}/.jason`));
    }
    for (const spelling of spellings) {
        for (let at = text.indexOf(spelling); at >= 0; at = text.indexOf(spelling, at + 1)) {
            const rest = text.slice(at + spelling.length);
            if (/^[A-Za-z0-9._-]/.test(rest)) continue;
            const named = posix.normalize(`/${/^[^\s"'`;|&<>()]*/.exec(rest)?.[0] ?? ''}`);
            if (chatRest !== null && (named === chatRest || named.startsWith(`${chatRest}/`))) continue;
            return true;
        }
    }
    return false;
}

/**
 * True when a word of the command, or an option's value after its `=`, names a place in the data directory outside the
 * chat folder, located as a Read's path is: read against the folder the command runs in, with links, junctions and
 * Windows' short names followed, and Git Bash's `/c/...` read as the drive's path. A word the shell would expand first
 * (a variable, a `~`) is left to the text's spellings.
 */
function wordNamesData(command: string, scope: ChatScope): boolean {
    const data = located(scope.data, scope);
    const chat = located(scope.chat, scope);
    for (const word of command.split(/[\s;|&<>()`]+/).map(unquoted)) {
        for (const value of new Set([word, word.slice(word.indexOf('=') + 1)])) {
            if (value === '' || value.length > pathLimit(scope.platform) || /^~|[$%]/.test(value)) continue;
            // Git Bash's /c/... is the drive's own path.
            const drive = scope.platform === 'win32' ? /^[\\/]+([a-z])(?=[\\/]|$)/i.exec(value) : null;
            const path = located(drive ? `${drive[1]}:${value.slice(drive[0].length) || '\\'}` : value, scope);
            if (inside(scope, data, path) && !inside(scope, chat, path)) return true;
        }
    }
    return false;
}

function judgeCommand(tool: string, command: string, scope: ChatScope): Judgement {
    if (namesData(command, scope) || wordNamesData(command, scope)) return { verdict: 'deny', message: dataRefusal };
    const jason = jasonIn(command, scope);
    if (jason === 'deny') return { verdict: 'deny', message: commandRefusal };
    // The host runs the allow list's verbs itself; a jason command that reaches the model is none of them, so it is the
    // person's to answer, each time.
    if (jason === 'jason') return { verdict: 'ask', title: 'Run a command', detail: shorten(command), remember: null };
    if (replyRefused(command, scope)) return { verdict: 'deny', message: replyRefusal };
    // Only the Bash tool's commands are fenced by the host and may be allowed or remembered; any other tool's command is
    // the person's to answer, each time.
    if (tool !== 'Bash') return { verdict: 'ask', title: 'Run a command', detail: shorten(command), remember: null };
    if (!isCompound(command)) {
        const tokens = command.trim().split(/\s+/);
        if (tokens[0] === 'reply' && allowedReply.some((words) => words.every((word, index) => tokens[index + 1] === word))) return allow;
    }
    const prefix = commandPrefix(command);
    if (prefix === null) return { verdict: 'ask', title: 'Run a command', detail: shorten(command), remember: null };
    return { verdict: 'ask', title: 'Run a command', detail: shorten(command), remember: `${tool}:${prefix}`, covers: `${prefix} \u2026` };
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

/** The page prints "always" as this label on a button that does not wrap, so a label longer than it can show is not offered. */
const alwaysLabel = (covers: string) => `Allow for this chat: ${covers}`;
const alwaysLabelLimit = 80;

/**
 * Decides without a person where the answer is already known, and otherwise says what to put in front of them. A card
 * whose "always" label would print longer than the button shows covers its one request.
 */
export function judge(tool: string, input: Record<string, unknown>, scope: ChatScope): Judgement {
    const judged = judgeAsked(tool, input, scope);
    if (judged.verdict !== 'ask' || judged.remember === null || alwaysLabel(judged.covers).length <= alwaysLabelLimit) return judged;
    return { verdict: 'ask', title: judged.title, detail: judged.detail, remember: null };
}

function judgeAsked(tool: string, input: Record<string, unknown>, scope: ChatScope): Judgement {
    if (tool.startsWith(`mcp__${serverName}__`)) return { verdict: 'deny', message: ownToolRefusal };
    if (tool === 'Glob') return judgeGlob(input, scope);
    const reader = readers[tool];
    if (reader !== undefined) {
        const value = input[reader];
        if (tool === 'Read') return named(value) ? judgePath(value, scope, false) : unnamed(tool, input);
        return judgeFolder(tool, named(value) ? value : '.', scope);
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
        if (host === null) return { verdict: 'ask', title: 'Open a web page', detail: shorten(url), remember: null };
        return { verdict: 'ask', title: 'Open a web page', detail: shorten(url), remember: `web:${host.toLowerCase()}`, covers: `pages on ${host.toLowerCase()}` };
    }
    const card = { verdict: 'ask', title: `Use ${tool}`, detail: shorten(JSON.stringify(input)) } as const;
    return runsNothing.has(tool) ? { ...card, remember: `tool:${tool}`, covers: `every use of ${tool}` } : { ...card, remember: null };
}
