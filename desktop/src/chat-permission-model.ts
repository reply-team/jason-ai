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
/**
 * The most of a card's detail the page is given: a longer detail is cut with an ellipsis (`shorten`), and a longer command
 * is refused (`judgeCommand`), since its card could not show what Allow would run.
 */
const detailLimit = 4000;
const readers: Readonly<Record<string, string>> = { Read: 'file_path', Glob: 'path', Grep: 'path', LS: 'path' };
const writers: Readonly<Record<string, string>> = { Write: 'file_path', Edit: 'file_path', MultiEdit: 'file_path', NotebookEdit: 'notebook_path' };
const shells = new Set(['Bash', 'PowerShell']);
/**
 * The tools an "always, in this chat" may cover whole: those that run nothing. None is named today, so every other tool's
 * card covers its one request.
 */
const runsNothing: ReadonlySet<string> = new Set<string>();
interface GuardedName {
    /** The name as its owner spells it: one path component, or a run of them joined by `/`; compared folded (`foldName`). */
    name: string;
    /** The card's title for a write under it, or a command that names a place under it. */
    title: string;
    /**
     * What the name is directly in the chat folder, where a write of it, or under it, is refused: the chat's own
     * instructions, which the desktop writes before each turn or a host reads beside them, or the chat's own agent
     * settings and skills, which the desktop deploys and the host reads at every turn.
     */
    chatsOwn?: 'instructions' | 'configuration';
}

const instructionsTitle = 'Change instructions an agent reads';
const loadsTitle = 'Change the skills or settings an agent loads';

/**
 * The names whose files act on what runs next, as any component of a path, or a run of components: `.claude` configures
 * Claude Code for the next turn; `.git` holds a repository's hooks and configuration, which git runs; `.jason` holds the
 * marker a folder is known by; `.agents` holds the skills Codex and the other hosts load, and `.codex`, `.gemini`,
 * `.cursor` and `.copilot` the hosts' own settings; the instruction files are what a host reads as its instructions, the
 * person's memory file among them, which every role session in a campaign's folder loads. A write under any, anywhere on
 * the machine, is a card each time, never covered by "always"; a read in a working folder is not. Directly in the chat
 * folder, the four instruction files the hosts read and the three folders of the chat's agent configuration are the
 * chat's own (`chatsOwn`): a write of one, or under one, is refused there. One declaration, which the .NET suite holds to
 * the names the runtime and the hosts' table use.
 */
export const guardedNames: readonly GuardedName[] = [
    { name: '.claude', title: 'Change the agent\'s own settings', chatsOwn: 'configuration' },
    { name: '.git', title: 'Change a repository\'s own files' },
    { name: '.jason', title: 'Change a folder\'s Jason marker' },
    { name: '.agents', title: loadsTitle, chatsOwn: 'configuration' },
    { name: '.codex', title: loadsTitle, chatsOwn: 'configuration' },
    { name: '.gemini', title: loadsTitle },
    { name: '.cursor', title: loadsTitle },
    { name: '.copilot', title: loadsTitle },
    { name: 'CLAUDE.md', title: instructionsTitle, chatsOwn: 'instructions' },
    { name: 'CLAUDE.local.md', title: instructionsTitle, chatsOwn: 'instructions' },
    { name: 'AGENTS.md', title: instructionsTitle, chatsOwn: 'instructions' },
    { name: 'AGENTS.override.md', title: instructionsTitle, chatsOwn: 'instructions' },
    { name: 'GEMINI.md', title: instructionsTitle },
    { name: '.cursorrules', title: instructionsTitle },
    { name: '.github/copilot-instructions.md', title: instructionsTitle },
];

/**
 * The extensions a file named jason may carry and still run as `jason`: those Windows' default PATHEXT lists, and
 * PowerShell's script.
 */
const programExtensions = 'exe com bat cmd vbs vbe js jse wsf wsh msc ps1'.split(' ');
const jasonFileTitle = 'Write or run a jason that is not the desktop\'s';
const searchTitle = 'Change where the shell finds programs';
const windowsNameTitle = 'Use a path Windows reads as another name';
/** The titles of a card the guard gives: one of them is said as itself, never as a spelling's card. */
const guardTitles: ReadonlySet<string> = new Set([...guardedNames.map((guarded) => guarded.title), jasonFileTitle]);
const allowedReply = [['auth', 'status'], ['account', 'list']];

const dataRefusal = 'That path is the installation\'s own data, outside the chat\'s folder: the chat neither reads nor writes it. Nothing was done.';
const deviceRefusal = 'That path names a device or a volume, not a file in a folder: the chat neither reads nor writes it. Nothing was done.';
const networkRefusal = 'That path is on another computer, or reaches this one as another computer would: the chat never writes there. Nothing was done.';
const climbRefusal = 'That pattern climbs out of its folder with ..: the chat does not take it. Name the folder as the path instead. Nothing was done.';
const braceRefusal = 'That pattern has a brace group that can name another folder: the chat does not take it. Name the folder as the path instead. Nothing was done.';
const ownToolRefusal = 'That tool answers the chat\'s own permission requests: only the person answers them. Nothing was done.';
const searchRefusal = 'That folder holds the installation\'s own data, so a search or a listing of it would walk that data: the chat does not take it. Name a narrower folder, one that does not hold the data directory. Nothing was done.';
const lengthRefusal = 'That path is longer than the system takes: the chat neither reads nor writes it. Nothing was done.';
/** The longest path each platform takes: the `\\?\` form's limit on Windows, `PATH_MAX` on macOS and Linux. */
const pathLimit = (platform: Platform) => (platform === 'win32' ? 32_767 : 4_096);
const replyRefusal = 'The chat runs only reply auth status and reply account list of reply: provider work goes through jason, and the connection is made on the Connect card. Nothing was run.';
const commandRefusal = 'That command is a person\'s own act or changes the installation, so the chat never runs it: the person runs it themselves. Nothing was run.';
const confinedRefusal = 'That command names JASON_CONFINED, which confines the jason the chat runs: the chat never reads, sets or clears it. Nothing was run.';
const lookupRefusal = 'That command changes where the shell finds a program, so the jason it would start may not be the desktop\'s: the chat never runs it. Nothing was run.';
const instructionsRefusal = 'That file is the chat\'s own instructions, which the desktop writes before each turn: the chat never changes it. Nothing was done.';
const configurationRefusal = 'That folder is the chat\'s own agent settings and skills, which the desktop keeps: the chat never changes it. Nothing was done.';
const longRefusal = 'That command is longer than the card can show whole, so nobody could read what they would allow: the chat never runs it. '
    + 'Put the content in a file in the chat\'s folder and give it as --file - < <file>, or split the work. Nothing was run.';

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

/** The working folder a located path lies in, the chat folder first, and whether it is the chat folder; null when none. */
function workingFolderOf(scope: ChatScope, path: string): { folder: string; chat: boolean } | null {
    const chat = located(scope.chat, scope);
    if (inside(scope, chat, path)) return { folder: chat, chat: true };
    const added = scope.added.map((folder) => located(folder, scope)).find((folder) => inside(scope, folder, path));
    return added === undefined ? null : { folder: added, chat: false };
}

/**
 * The components of a located path below its working folder, the folder's own name never among them, compared as the
 * platform folds case, so that a path spelled in another case than its folder is still read below it.
 */
function partsBelow(scope: ChatScope, folder: string, path: string): string[] {
    const paths = pathsOf(scope.platform);
    return paths.relative(keyOf(scope.platform, folder), keyOf(scope.platform, path)).split(paths.sep).filter((part) => part !== '');
}

/**
 * A path component as the volume compares names. On Windows, without a stream suffix and without trailing dots and
 * spaces, which Windows drops when it opens the file. On Windows and macOS, through compatibility decomposition, with
 * combining marks and default-ignorable characters dropped, and with case folded both ways, so that a long s is an s
 * and a dotless i an i. Linux compares names exactly.
 */
function foldName(platform: Platform, part: string): string {
    if (platform === 'linux') return part;
    const name = platform === 'win32' ? part.replace(/:[\s\S]*$/, '').replace(/[. ]+$/, '') : part;
    return name.normalize('NFKD').replace(/[\p{M}\p{Default_Ignorable_Code_Point}]/gu, '').toUpperCase().toLowerCase();
}

/**
 * The first guarded name a run of path components holds, nearest the start first, and the component it starts at; each
 * component compared folded.
 */
function guardAt(scope: ChatScope, parts: readonly string[]): { guarded: GuardedName; at: number } | undefined {
    const folded = parts.map((part) => foldName(scope.platform, part));
    for (let at = 0; at < folded.length; at++) {
        const guarded = guardedNames.find((each) => each.name.split('/').every((name, step) => folded[at + step] === foldName(scope.platform, name)));
        if (guarded !== undefined) return { guarded, at };
    }
    return undefined;
}

function guardIn(scope: ChatScope, parts: readonly string[]): GuardedName | undefined {
    return guardAt(scope, parts)?.guarded;
}

/** True when a file name, compared folded, is jason with or without a program's extension. */
function isJasonFile(scope: ChatScope, name: string): boolean {
    const match = /^(.*?)(?:\.([^.]*))?$/.exec(foldName(scope.platform, name));
    if (match === null || match[1] !== 'jason') return false;
    return match[2] === undefined || programExtensions.includes(match[2]);
}

/**
 * The components a guard reads of a located path: those below its working folder, the folder's own path never among
 * them, or every component of a path outside them.
 */
function guardedParts(scope: ChatScope, path: string): { parts: string[]; working: { folder: string; chat: boolean } | null } {
    const working = workingFolderOf(scope, path);
    if (working !== null) return { parts: partsBelow(scope, working.folder, path), working };
    return { parts: path.split(/[\\/]/).filter((part) => part !== ''), working: null };
}

/**
 * What a write of a located path is: refused when it is, or lies under, one of the chat's own names directly in the chat
 * folder; a card titled by the first guarded name among its components, wherever it lies; a card of its own for a file
 * named jason directly in a working folder; otherwise allowed in a working folder and a card outside them.
 */
function judgeWrite(scope: ChatScope, path: string): Judgement {
    const { parts, working } = guardedParts(scope, path);
    const found = guardAt(scope, parts);
    const guarded = found?.guarded;
    if (working?.chat === true && found?.at === 0 && guarded?.chatsOwn !== undefined) {
        return { verdict: 'deny', message: guarded.chatsOwn === 'instructions' ? instructionsRefusal : configurationRefusal };
    }
    if (guarded !== undefined) return { verdict: 'ask', title: guarded.title, detail: shorten(path), remember: null };
    if (working !== null && parts.length === 1 && isJasonFile(scope, parts[0] ?? '')) return { verdict: 'ask', title: jasonFileTitle, detail: shorten(path), remember: null };
    if (working !== null) return allow;
    return { verdict: 'ask', title: 'Change a file outside the chat\'s folders', detail: shorten(path), remember: null };
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
    return differs ? { verdict: 'ask', title: windowsNameTitle, detail: shorten(value), remember: null } : null;
}

/**
 * A write whose path Windows reads as another name is judged as that name too: a refusal, or a guard's card, is said as
 * itself, and anything else is the spelling's card.
 */
function judgePath(value: string, scope: ChatScope, writes: boolean): Judgement {
    const spelling = judgeSpelling(value, scope);
    if (spelling === null) return judgeLocated(value, scope, writes);
    if (!(writes && spelling.verdict === 'ask' && spelling.title === windowsNameTitle)) return spelling;
    const judged = judgeLocated(value, scope, writes);
    return judged.verdict === 'deny' || (judged.verdict === 'ask' && guardTitles.has(judged.title)) ? judged : spelling;
}

function judgeLocated(value: string, scope: ChatScope, writes: boolean): Judgement {
    if (writes && network(scope.platform, spelled(value, scope))) return { verdict: 'deny', message: networkRefusal };
    const paths = pathsOf(scope.platform);
    const path = located(value, scope);
    const key = keyOf(scope.platform, path);
    if (scope.protectedFiles.some((file) => keyOf(scope.platform, located(file, scope)) === key)) return { verdict: 'deny', message: dataRefusal };
    const chat = located(scope.chat, scope);
    if (inside(scope, located(scope.data, scope), path) && !inside(scope, chat, path)) return { verdict: 'deny', message: dataRefusal };
    if (writes) return judgeWrite(scope, path);
    if (workingFolderOf(scope, path) !== null) return allow;
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
 * True when a folder holds the data directory, read as a walk would most likely reach it: a leading `~` as the home
 * folder, and on Windows each component without its trailing dots and spaces, as Windows reads the name.
 */
function holdsData(value: string, scope: ChatScope): boolean {
    let text = /^~([\\/]|$)/.test(value) ? `${scope.home}${value.slice(1)}` : value;
    if (scope.platform === 'win32') text = text.split(/[\\/]/).map((part) => (part === '.' || part === '..' ? part : part.replace(/[. ]+$/, ''))).join('\\');
    return inside(scope, located(text, scope), located(scope.data, scope));
}

/**
 * A folder a search or a listing walks: one that holds the data directory is refused, since the walk would read what the
 * chat never reads, and that comes before a card for its spelling; any other is judged as a read of it. Its card is
 * remembered by the folder it names and by the tool's own kind, so a grant covers that tool's walks of that folder only:
 * never a Read, a sibling, a folder under it or another tool's walk, and a Read's grant never covers it.
 */
function judgeFolder(tool: string, value: string, scope: ChatScope): Judgement {
    const judged = judgePath(value, scope, false);
    if (judged.verdict === 'deny') return judged;
    if (holdsData(value, scope)) return { verdict: 'deny', message: searchRefusal };
    if (judgeSpelling(value, scope) !== null) return judged;
    const folder = located(value, scope);
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
    // A brace group that can name a place reaches where the fixed leading folders do not show, whether or not the host
    // expands it.
    if (/\{[^{}]*[\\/:~][^{}]*\}/.test(pattern)) return { verdict: 'deny', message: braceRefusal };
    const parts = pattern.split(/[\\/]/);
    const fixed: string[] = [];
    for (const part of parts) {
        if (/[*?[{]/.test(part)) break;
        fixed.push(part);
    }
    const separator = pathsOf(scope.platform).sep;
    if (pattern.startsWith('~')) {
        // A walk of a ~ folder that holds the data directory is refused; any other is a card each time.
        const home: Judgement = { verdict: 'ask', title: 'Use a path that begins with ~', detail: shorten(pattern), remember: null };
        return worse(worse(fixed.length === 0 ? home : judgeFolder('Glob', fixed.join(separator), scope), home), judged);
    }
    if (fixed.length === 0) return judged;
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
 * cover anything: shells and the wrappers that run a command they are given, and the interpreters, package scripts and
 * build tools that run a file the agent may write with no card. It is a closed list: a program it does not name is
 * remembered by its first word. They are names to recognise, never to run, and are written as one string so the process
 * guard, which refuses a quoted shell name in any module but the login-shell lookup, keeps holding this one as it holds
 * the others. An interpreter is also matched by its stem followed by a version (`python3.12`, `node22`).
 */
const runsAnother = new Set(
    ('cmd sh bash zsh pwsh powershell env sudo xargs npx wsl start python python3 py node deno bun ruby perl php npm pnpm yarn make '
        + 'timeout nohup time command exec watch nice stdbuf setsid doas').split(' '),
);
const interpreterStem = /^(python|pythonw|node|nodejs|ruby|perl|php)[0-9.]*$/;

/** True when a program's name, without a folder or an executable's extension, is one that runs another program or a script. */
function runsAnotherProgram(name: string): boolean {
    const key = name.toLowerCase();
    return runsAnother.has(key) || interpreterStem.test(key);
}

/**
 * What "always, in this chat" covers of a command: the program and its first word. Nothing for a compound command, a
 * program named by a path, or a shell or exec wrapper, whose card covers its one request; and nothing for a command any
 * of whose words the shell would rewrite before it runs (a quote, a backslash, a variable, a `~`, a glob or a brace),
 * or that climbs by a `..` segment, because a later word is covered unread: such a command is a card each time, and the
 * person sees its text. Two dots that are not a segment (`main..feature`, `./...`) are not a climb. Nothing either for a
 * command any of whose words is reply (`namesReply`): reply after a program the chat does not know as a runner is not
 * refused, so an "always" for `caffeinate reply` would cover every reply command after it.
 */
export function commandPrefix(command: string): string | null {
    if (isCompound(command)) return null;
    const words = command.trim().split(/\s+/);
    if (!words.every((word) => /^[A-Za-z0-9._/:@+=,%-]+$/.test(word) && !/(^|[^.])\.\.([\\/]|$)/.test(word))) return null;
    if (words.some(namesReply)) return null;
    const [program, first] = words;
    if (program === undefined || first === undefined || first.startsWith('-')) return null;
    if (/[\\/:]/.test(program) || runsAnotherProgram(program.toLowerCase().replace(/\.(exe|cmd|bat|com|ps1)$/, ''))) return null;
    return `${program} ${first}`;
}

/**
 * True when a word, wherever it stands, is reply or its package `reply-cli` as a whole: by any folder, with any version,
 * executable extension or case. A word that only holds the letters (`replies.md`, `reply-later`) is not.
 */
function namesReply(word: string): boolean {
    const name = (word.split(/[\\/]/).pop() ?? '').toLowerCase().replace(/\.(exe|cmd|bat|com|ps1)$/, '');
    return name === 'reply' || /^reply-cli(@.*)?$/.test(name);
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

/** True when a token names reply: the program itself, or its package `reply-cli`, with or without a version. */
function isReply(platform: Platform, token: string): boolean {
    const program = programOf(platform, token);
    return program === 'reply' || /^reply-cli(@.*)?$/i.test(program);
}

/**
 * True when any command inside the text, in any part, form or nesting, runs reply with words other than the two the chat
 * runs: `auth status` and `account list`. reply is read as a program only where a program stands: the first token of a
 * part after any `NAME=value` assignments, or a later token of a part in which an earlier token runs another program (a
 * shell, an exec wrapper, a package runner). A quoted word is one token, and one given to such a program is read as a
 * command of its own. So a draft, a message, a search pattern or a folder's name that holds the word is not reply.
 */
function replyRefused(command: string, scope: ChatScope): boolean {
    for (const part of command.split(/&&|\|\||\$\(|[;|&`\n\r<>(){}]/)) {
        const tokens = (part.match(/"[^"]*"|'[^']*'|\S+/g) ?? []).map(unquoted).filter((token) => token !== '');
        const first = tokens.findIndex((token) => !/^[A-Za-z_][A-Za-z0-9_]*=/.test(token));
        let runner = false;
        for (const [at, token] of tokens.entries()) {
            if (runner && /\s/.test(token) && replyRefused(token, scope)) return true;
            if (at !== first && !runner) continue;
            if (isReply(scope.platform, token)) {
                const words = tokens.slice(at + 1);
                if (!allowedReply.some((pair) => pair.every((word, index) => words[index] === word))) return true;
            }
            if (runsAnotherProgram(programOf(scope.platform, token))) runner = true;
        }
    }
    return false;
}

/** A text as paths in it are compared: one slash for any run of slashes or backslashes, case folded where the platform folds it. */
function pathText(platform: Platform, text: string): string {
    return keyOf(platform, text.replace(/[\\/]+/g, '/'));
}

/**
 * The variable the host's environment carries the data directory in. It is refused by its name as a whole word anywhere
 * in the command, whatever wraps it (`$`, `${...:-}`, `%...%`, `!...!`, `$env:`, an assignment, `printenv`), even where
 * it leads to the chat folder; the text is compared case folded where the platform folds it. A longer name that only
 * begins or ends like it is not it.
 */
const dataVariable = 'JASON_DATA_DIR';

function namesDataVariable(command: string, scope: ChatScope): boolean {
    return new RegExp(`(?<![A-Za-z0-9_])${keyOf(scope.platform, dataVariable)}(?![A-Za-z0-9_])`).test(keyOf(scope.platform, command));
}

/**
 * The variable every host's environment carries so that the jason it runs is confined: an option that reads a file into
 * a request takes standard input only, and the provider check runs a bare name only (`hostEnvironment`). A command that
 * names it could clear it for the jason it runs, so it is refused by its name as a whole word, in any letter case on
 * every platform, whatever wraps it; the host's own deny rule (`confinedRule`) refuses the exact spelling first. A longer
 * name that only begins or ends like it is not it. A spelling that builds the name, as `JASON_CONF''INED`,
 * `${!JASON_CONF@}` or `env -i`, is not refused by this check and reaches a card; what holds then is the CLI's
 * own rule.
 */
export const confinedVariable = 'JASON_CONFINED';

function namesConfinedVariable(command: string): boolean {
    return new RegExp(`(?<![A-Za-z0-9_])${confinedVariable}(?![A-Za-z0-9_])`, 'i').test(command);
}

/**
 * The variables a shell finds a program by: PATH, and PATHEXT, which Windows' own search reads. A command that assigns
 * either could start a `jason` the agent wrote in place of the desktop's, and the confined CLI never runs to say so; the
 * host's own deny rules (`searchRules`) refuse the plain assignments first.
 */
export const searchVariables = ['PATH', 'PATHEXT'] as const;

const searchName = `(?:${searchVariables.join('|')})`;
/**
 * What changes where the shell finds a program, read over the whole text in any letter case: an assignment to a search
 * variable in any shell's form (`NAME=`, `NAME+=`, `${NAME:=}`, `NAME[0]=`; leading, or after `export`, `declare`,
 * `env` or `set`, the name quoted or not), `unset` and `env -u`, `read` and `printf -v` into it, cmd's `path` command
 * as the command handed to cmd by `/c` or `/k` (the chat's shell is Bash, where a word path is no such command) and
 * cmd's `setx`, PowerShell's environment drive and .NET's setter; and a function, an alias or a hash
 * entry for jason. A longer name that only ends like one (`MYPATH=`, `PYTHONPATH=`), a read of `$PATH`, an option named
 * path (`--path=`) and a prompt that holds the word are not it. A spelling that builds the name is not refused here; a
 * command that names either variable in capitals otherwise is a card of its own (`namesSearch`).
 */
const searchChanges: readonly RegExp[] = [
    new RegExp(String.raw`(?<![A-Za-z0-9_$.-])["']?${searchName}["']?(?:\[[^\]]*\])?(?:\+|:)?=`, 'i'),
    new RegExp(String.raw`env:[\\/]?${searchName}(?![A-Za-z0-9_])`, 'i'),
    new RegExp(String.raw`setenvironmentvariable\s*\(\s*['"]?${searchName}['"]?\s*,`, 'i'),
    new RegExp(String.raw`(?<![A-Za-z0-9_.-])setx(?:\s+/\S+)*\s+["']?${searchName}(?![A-Za-z0-9_])`, 'i'),
    new RegExp(String.raw`(?<![A-Za-z0-9_.-])unset\b[^;&|\n]*?(?<![A-Za-z0-9_$./-])["']?${searchName}["']?(?![A-Za-z0-9_./-])`, 'i'),
    new RegExp(String.raw`(?<![A-Za-z0-9_.-])env\b[^;&|\n]*?(?:\s-u\s*|\s--unset[=\s]\s*)["']?${searchName}(?![A-Za-z0-9_])`, 'i'),
    new RegExp(String.raw`(?<![A-Za-z0-9_.-])(?:read|readarray|mapfile)\b[^;&|\n<>]*?\s["']?${searchName}["']?(?=[\s;&|<>]|$)`, 'i'),
    new RegExp(String.raw`(?<![A-Za-z0-9_.-])printf\s+(?:-\S+\s+)*?-v\s*["']?${searchName}(?![A-Za-z0-9_])`, 'i'),
    /(?<![A-Za-z0-9_.-])cmd(?:\.exe)?['"]?(?:\s+\/[a-z])*?\s+\/[ck]\s+['"]?path(?![A-Za-z0-9_.-])/i,
    /(?<![A-Za-z0-9_.-])jason(?:\.[a-z]+)?\s*\(\s*\)/i,
    /(?<![A-Za-z0-9_.-])function\s+jason(?![A-Za-z0-9_-])/i,
    /(?<![A-Za-z0-9_.-])(?:alias|doskey)\s+(?:-\S+\s+)*['"]?jason(?:\.[a-z]+)?['"]?=/i,
    /(?<![A-Za-z0-9_.-])(?:set-alias|new-alias|sal|nal)\s+(?:-name\s+)?['"]?jason(?![A-Za-z0-9_-])/i,
    /(?<![A-Za-z0-9_.-])hash\s+(?:-\S+\s+)*(?:\S+\s+)?['"]?jason(?![A-Za-z0-9_.-])/i,
];

function changesSearch(command: string): boolean {
    return searchChanges.some((change) => change.test(command));
}

/** True when the command names a search variable in capitals as a whole word, however else it uses it. */
function namesSearch(command: string): boolean {
    return new RegExp(`(?<![A-Za-z0-9_])${searchName}(?![A-Za-z0-9_])`).test(command);
}

/**
 * The first guarded name a place the command's words name holds, below its working folder or anywhere outside them, so
 * that its card says what it may change and covers that request alone. A shell word is never refused by it: a quoted
 * text that names one is located as a path too.
 */
function guardedPlace(places: readonly string[], scope: ChatScope): GuardedName | undefined {
    for (const path of places) {
        const guarded = guardIn(scope, guardedParts(scope, path).parts);
        if (guarded !== undefined) return guarded;
    }
    return undefined;
}

/**
 * True when a word of the command names a file called jason, by a folder or with a program's extension, directly in a
 * working folder: the shell, or cmd, which looks in the current folder first, would run it in place of the desktop's.
 * The bare program name is the desktop's own and is not it.
 */
function namesLocalJason(command: string, scope: ChatScope): boolean {
    for (const word of command.split(/[\s;|&<>()`]+/).map(unquoted)) {
        for (const piece of new Set([word, ...word.split(/[=@,]/)])) {
            if (piece === '' || piece.length > pathLimit(scope.platform) || /^~|[$%]/.test(piece)) continue;
            const name = piece.split(/[\\/]/).pop() ?? '';
            if (!isJasonFile(scope, name) || !(/[\\/]/.test(piece) || /\./.test(name))) continue;
            const { parts, working } = guardedParts(scope, located(piece, scope));
            if (working !== null && parts.length === 1) return true;
        }
    }
    return false;
}

/**
 * True when the command's text names the data directory, or anything in it outside the chat folder, in any literal
 * spelling a shell takes: the native path, its forward-slash form, Git Bash's and the host rules' `/c/...` on Windows,
 * and `~/.jason`, `$HOME/.jason` or `.jason` under the home folder's variable in any shell's spelling (`$USERPROFILE`
 * among them, since Git Bash is the Windows shell) when the data directory is the default one. A name that only begins
 * like it is not it.
 */
function namesData(command: string, scope: ChatScope): boolean {
    const paths = pathsOf(scope.platform);
    const text = pathText(scope.platform, command);
    const data = pathText(scope.platform, paths.resolve(scope.data)).replace(/\/$/, '');
    const chat = pathText(scope.platform, paths.resolve(scope.chat));
    const chatRest = chat.startsWith(`${data}/`) ? chat.slice(data.length) : null;
    const spellings = [data];
    const drive = /^([a-z]):\//i.exec(data);
    if (drive) spellings.push(`/${drive[1]}${data.slice(2)}`);
    if (data === `${pathText(scope.platform, paths.resolve(scope.home)).replace(/\/$/, '')}/.jason`) {
        for (const home of ['~', '$HOME', '${HOME}', '%USERPROFILE%', '$env:USERPROFILE', '$USERPROFILE', '${USERPROFILE}']) spellings.push(pathText(scope.platform, `${home}/.jason`));
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
 * The places a command's words name, each located as a Read's path is: read against the folder the command runs in, with
 * links, junctions and Windows' short names followed, and Git Bash's `/c/...` read as the drive's path. A word is split
 * on `=`, `@` and `,` as on spaces, so a value joined to an option, a file a program reads by `@`, or one item of a list
 * is located on its own; a piece that begins with one dash and a letter is also located without those two characters,
 * as a one-dash option's joined value (`-T../x`). A piece the shell would expand first (a variable, a `~`) is left to the
 * variable's name and the text's spellings.
 */
function placesOf(command: string, scope: ChatScope): string[] {
    const places: string[] = [];
    for (const word of command.split(/[\s;|&<>()`]+/).map(unquoted)) {
        const pieces = new Set([word, ...word.split(/[=@,]/)]);
        for (const piece of [...pieces]) if (/^-[A-Za-z]/.test(piece)) pieces.add(piece.slice(2));
        for (const value of pieces) {
            if (value === '' || value.length > pathLimit(scope.platform) || /^~|[$%]/.test(value)) continue;
            // Git Bash's /c/... is the drive's own path.
            const drive = scope.platform === 'win32' ? /^[\\/]+([a-z])(?=[\\/]|$)/i.exec(value) : null;
            places.push(located(drive ? `${drive[1]}:${value.slice(drive[0].length) || '\\'}` : value, scope));
        }
    }
    return places;
}

/** True when a place the command's words name is in the data directory outside the chat folder. */
function wordNamesData(places: readonly string[], scope: ChatScope): boolean {
    const data = located(scope.data, scope);
    const chat = located(scope.chat, scope);
    return places.some((path) => inside(scope, data, path) && !inside(scope, chat, path));
}

function judgeCommand(tool: string, command: string, scope: ChatScope): Judgement {
    if (namesConfinedVariable(command)) return { verdict: 'deny', message: confinedRefusal };
    if (changesSearch(command)) return { verdict: 'deny', message: lookupRefusal };
    const places = placesOf(command, scope);
    if (namesDataVariable(command, scope) || namesData(command, scope) || wordNamesData(places, scope)) return { verdict: 'deny', message: dataRefusal };
    const jason = jasonIn(command, scope);
    if (jason === 'deny') return { verdict: 'deny', message: commandRefusal };
    if (replyRefused(command, scope)) return { verdict: 'deny', message: replyRefusal };
    // After every refusal and before any card: a card cuts its detail at the limit, so Allow would run what nobody saw.
    // A grant is looked at only for a card, so none allows it either.
    if (command.length > detailLimit) return { verdict: 'deny', message: longRefusal };
    // A word under a guarded name in a working folder: that guard's card, never covered by a grant.
    // A command that names a search variable otherwise, a word under a guarded name, or a jason of the agent's own: each
    // is its own card, never covered by a grant.
    if (namesSearch(command)) return { verdict: 'ask', title: searchTitle, detail: shorten(command), remember: null };
    const guarded = guardedPlace(places, scope);
    if (guarded !== undefined) return { verdict: 'ask', title: guarded.title, detail: shorten(command), remember: null };
    if (namesLocalJason(command, scope)) return { verdict: 'ask', title: jasonFileTitle, detail: shorten(command), remember: null };
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
    // A command that names a folder holding the data directory may walk it: a card each time, never covered by a grant.
    const data = located(scope.data, scope);
    const prefix = places.some((path) => inside(scope, path, data)) ? null : commandPrefix(command);
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

/**
 * The hosts a page of which the chat's agent opens with no card: the documentation of the two agent hosts and of the
 * provider, each published by its own vendor and holding no one else's pages, so a page there is the vendor's word and a
 * fetch there reaches only the vendor. Claude Code's documentation (`code.claude.com`), Codex's (`developers.openai.com`
 * and `learn.chatgpt.com`, where OpenAI publishes it too), and Reply's API reference (`docs.reply.io`). Each is one exact
 * host; none is a wildcard, since a vendor's other subdomains may serve what others wrote. Claude Code does not follow a
 * redirect to another host: Anthropic, https://code.claude.com/docs/en/tools-reference#webfetch-tool-behavior (read
 * 2026-11-11), "When a URL redirects to a different host, WebFetch returns a text result that names the original URL
 * and the redirect target instead of following it. Claude then fetches the new URL with a second WebFetch call.", so a
 * listed page that leads elsewhere is a card at the next fetch.
 */
export const documentationHosts: readonly string[] = ['code.claude.com', 'developers.openai.com', 'learn.chatgpt.com', 'docs.reply.io'];

/**
 * True when a URL is a page of a documentation host as written: `https://`, then the host exactly as listed (in any
 * letter case), then the end or `/`, `?` or `#`. Anything else in its place, a user, a port (the default one spelled
 * included), a trailing dot, an escape or a character a parser would turn into a dot, is not; nor is a URL with a space
 * or a control character anywhere, which a parser drops or trims. A URL parser must read the same host from it, so no
 * reading of it can reach another.
 */
function isDocumentationPage(url: string): boolean {
    if (/[\s\x00-\x1f\x7f]/.test(url)) return false;
    const spelled = /^https:\/\/([A-Za-z0-9.-]+)(?:[/?#]|$)/.exec(url)?.[1]?.toLowerCase();
    if (spelled === undefined || !documentationHosts.includes(spelled)) return false;
    try {
        const parsed = new URL(url);
        return parsed.protocol === 'https:' && parsed.username === '' && parsed.password === '' && parsed.port === '' && parsed.hostname === spelled;
    } catch {
        return false;
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
        // A documentation host's page opens with no card; any other page, a lookalike of one included, keeps its card.
        if (isDocumentationPage(url)) return allow;
        const host = hostOf(url);
        if (host === null) return { verdict: 'ask', title: 'Open a web page', detail: shorten(url), remember: null };
        return { verdict: 'ask', title: 'Open a web page', detail: shorten(url), remember: `web:${host.toLowerCase()}`, covers: `pages on ${host.toLowerCase()}` };
    }
    const card = { verdict: 'ask', title: `Use ${tool}`, detail: shorten(JSON.stringify(input)) } as const;
    return runsNothing.has(tool) ? { ...card, remember: `tool:${tool}`, covers: `every use of ${tool}` } : { ...card, remember: null };
}
