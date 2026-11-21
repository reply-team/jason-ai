// Pure: what happens when the chat's agent asks to use a tool its allowed list does not settle. The host asks only for
// what its own rules leave open; this decides without a person where the answer is already known, and otherwise says
// what to put on the person's card. The platform is passed in, so both platforms' path rules are tested on either.

import { posix, win32 } from 'node:path';
import { fileCommandsOf, fileGrammars } from './chat-auto-edit.ts';
import { askingCardedForms } from './chat-deny.ts';
import { followsLinksIn, linkOperands, programName, readLiteral, systemLaunchers, type LiteralCommand } from './chat-literal.ts';
import { serverName } from './chat-permission-endpoint.ts';

export type Platform = 'win32' | 'darwin' | 'linux';

export interface ChatScope {
    platform: Platform;
    /** The folder a relative path is read against: the turn's chat folder. */
    cwd: string;
    /**
     * The turn's chat folder: the conversation's own, `<data>/desktop/chats/<id>`, or for a conversation an earlier desktop
     * made the shared one, `<data>/desktop/chat`.
     */
    chat: string;
    /** The campaign and project folders added to the turn. */
    added: readonly string[];
    /** The data directory: nothing under it but the turn's chat folder is the chat's. */
    data: string;
    /** The person's home folder, for `~/.jason` when the data directory is the default one. */
    home: string;
    /**
     * On Windows, the names this computer has, which a network path may name it by as another computer would: its own
     * name, say. `localhost` and the loopback addresses are always among them (`thisComputer`). Absent, those alone.
     */
    machine?: readonly string[];
    /**
     * The home folder the host's shell resolves `~` to: the `HOME` of the host's environment, or the profile's folder
     * when it carries none. Absent, taken to be `home`. Where the two differ, a command's `~` is read by its text alone.
     */
    shellHome?: string;
    /**
     * In a demo run, the person's own installation's directories: its data directory and its harness root, each refused
     * whole in every mode, Full access included, with no folder in them that the chat may use. Absent or empty in the
     * person's own installation, where the data directory is the one fenced folder.
     */
    personsOwn?: readonly string[];
    /** The files refused first: the installation's settings file and the runtime's run file. */
    protectedFiles: readonly string[];
    /** The deny list's commands. */
    denied: readonly string[];
    /** The deny list's operations: a command whose text names one is refused, however it would reach the runtime. */
    deniedOperations: readonly string[];
    /**
     * The nouns whose help runs with no card (`helpNouns`): exactly `jason <noun> --help`, the host's own help rules.
     * Absent, none.
     */
    help?: readonly string[] | undefined;
    /**
     * The forms of the host's allow rules in Ask first and Auto-edit (`allowRules`), whose plain form runs here with no
     * card as the host's rule would run it (`plainForm`): below Claude Code's floor of Full access the host is given no
     * rule for a verb that reads a request's content (`withoutRequestFiles`), so its command reaches the model. Absent,
     * none: every jason command that reaches the model is a card.
     */
    allowed?: readonly string[] | undefined;
    /**
     * Whether the turn's hook rides it: Full access, and Ask first and Auto-edit from Claude Code's floor of Full access on,
     * where the turn's settings block no read (`hookedSettings`) and the hook refuses the three kinds wherever a tool names
     * them. There a read anywhere else runs with no card, a listing and the host's own read-only commands among them
     * (`hookedNoCard`), a change outside the chat's folders is one card, a walk of a folder that holds the data directory is
     * refused by the hook and a card each time here (`walksRoot`), and every jason verb runs with no card but the forms of
     * `askingCardedForms`, jason's own help and version among them. Absent or false, below the floor and in Plan only: as
     * it was.
     */
    hooked?: boolean;
    /** The real path of an existing path, links, junctions and short names followed; null when nothing is there. */
    real: (path: string) => string | null;
    /**
     * What is at a path, its last component not followed: null when nothing is there, `unresolved` for a link or a
     * junction that leads nowhere or an entry that cannot be read, `present` otherwise. Auto-edit reads it, and so does a
     * plain form of the allow list (`plainForm`); absent, neither runs unasked.
     */
    entry?: (path: string) => 'present' | 'unresolved' | null;
    /**
     * The text of an existing file, so an edit is judged by the file as it would read after it (`writtenText`); null when
     * nothing readable is there. Absent, an edit is judged by its replacement alone.
     */
    text?: (path: string) => string | null;
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
     * settings and skills, which the desktop deploys and the host reads at every turn, or a file that would name MCP
     * servers for the chat's own agent, which the desktop never writes.
     */
    chatsOwn?: 'instructions' | 'configuration' | 'servers';
}

const instructionsTitle = 'Change instructions the AI reads';
/** The card's title for a tool of an MCP server of the person's own: one title, whatever the server or the tool. */
export const personsToolTitle = 'Use one of your own tools';
/** The card's title for a command that changes the MCP servers an agent host loads (`claude mcp`, `codex mcp`). */
export const serversTitle = 'Change the MCP servers the AI loads';
const loadsTitle = 'Change the skills or settings the AI loads';

/**
 * The names whose files act on what runs next, as any component of a path, or a run of components: `.claude` configures
 * Claude Code for the next turn; `.git` holds a repository's hooks and configuration, which git runs; `.jason` holds the
 * marker a folder is known by; `.agents` holds the skills Codex and the other hosts load, and `.codex`, `.gemini`,
 * `.cursor` and `.copilot` the hosts' own settings; the instruction files are what a host reads as its instructions, the
 * person's memory file among them, which every role session in a campaign's folder loads; `.mcp.json` names the MCP
 * servers of the project at the folder Claude Code runs in, and `.claude.json` those of the user's and the local scopes,
 * so a write of either could put a server under a name the person chose. A write under any, anywhere on the machine, is
 * a card each time, never covered by "always"; a read in a working folder is not. Directly in the chat folder, the four
 * instruction files the hosts read, the three folders of the chat's agent configuration and the project's server file
 * are the chat's own (`chatsOwn`): a write of one, or under one, is refused there. One declaration, which the .NET suite holds to
 * the names the runtime and the hosts' table use.
 */
export const guardedNames: readonly GuardedName[] = [
    { name: '.claude', title: 'Change the AI\'s own settings', chatsOwn: 'configuration' },
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
    { name: '.mcp.json', title: serversTitle, chatsOwn: 'servers' },
    { name: '.claude.json', title: serversTitle },
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

/**
 * The reply commands the chat runs with no card, exactly: `reply auth status` and `reply account list`, each with at most
 * `--json`, and the account list with at most `--channel email` or `--channel linkedin`; nothing else may follow. The
 * host's allow rules name the same forms (`claudeAllowedTools`), and any other word, option or value is a card.
 */
export const replyNoCardForms: readonly string[] = (() => {
    const forms = ['reply auth status', 'reply auth status --json', 'reply account list', 'reply account list --json'];
    for (const channel of ['email', 'linkedin']) {
        forms.push(`reply account list --channel ${channel}`, `reply account list --channel ${channel} --json`, `reply account list --json --channel ${channel}`);
    }
    return forms;
})();

/**
 * The three kinds the turn's hook refuses in every mode it rides, and the only refusals of Full access (`hookRefusal`):
 * Jason's settings files and run file, its data directory outside the turn's folder, and what Jason itself sets for the
 * turn: its variables, with where the shell finds a program, and its permission tool. Each sentence, which the agent
 * passes on, says what is closed, that it is Jason's own and not one of the person's files, and the `jason` verb that does
 * what was asked where one does, never that the chat cannot, the permission tool's that only the person answers it; Ask
 * first, Auto-edit and Plan only refuse the same places by the same sentences.
 */
export const settingsRefusal =
    'That is Jason\'s settings file or its run file: the installation\'s own data, Jason\'s own and not one of the person\'s files, so the chat does not open it. '
    + '`jason status` says whether Jason runs and can work, `jason route list` and `jason plugin list` show its routes and plugins, `jason route set` and '
    + '`jason plugin grant` change them, and a plugin\'s configuration is the person\'s to write. Nothing was done.';
export const dataRefusal =
    'That path is in Jason\'s data directory, outside the chat\'s folder: the installation\'s own data, Jason\'s own and not one of the person\'s files, so the '
    + 'chat does not open it. Jason answers for what is there, with `jason campaign list`, `jason workitem list`, `jason journal list` or `jason logs`. Nothing was done.';
export const personsRefusal =
    'That path is the person\'s own installation, and this chat runs in the demo, which does not open it: the demo\'s own `jason` answers for its data, '
    + 'such as `jason campaign list`. Nothing was done.';
/**
 * The hook's refusal of a path whose end it does not read (`hookPlace`): on Windows one in a device's or a volume's
 * namespace, and one through a share of this computer that names no drive.
 */
export const unplacedRefusal =
    'Jason could not place where that path leads: it is in a device\'s or a volume\'s namespace, or on a share of this computer that names no drive, and '
    + 'the chat does not open such a path. Name the file by its drive\'s own path instead, such as `C:\\Users\\you\\Documents\\notes.md`. Nothing was done.';
const deviceRefusal = 'That path names a device or a volume, not a file in a folder: the chat neither reads nor writes it. Nothing was done.';
const networkRefusal = 'That path is on another computer, or reaches this one as another computer would: the chat neither reads nor writes there. Nothing was done.';
const climbRefusal = 'That pattern climbs out of its folder with ..: the chat does not take it. Name the folder as the path instead. Nothing was done.';
export const braceRefusal = 'That pattern has a brace group that can name another folder: the chat does not take it. Name the folder as the path instead. Nothing was done.';
export const ownToolRefusal =
    'That tool is Jason\'s own, set for this turn: it carries the chat\'s own permission requests, and only the person answers them, so the chat does not use it. Nothing was done.';
/**
 * The refusal of a walk of a folder that holds Jason's data directory, or in a demo run one of the person's own
 * directories: a search of it by the Grep or Glob tool, and where the hook rides the turn a command that may walk it
 * (`walksRoot`), with how to go on. A walk reads the data without naming it, so it is the data's kind. A plain listing
 * of such a folder is no walk where the hook rides the turn; a listing of the data directory itself is a read of the data
 * (`dataRefusal`).
 */
export const searchRefusal =
    'That folder holds Jason\'s data directory, the installation\'s own data and not one of the person\'s files, so a search, a walk, a copy or an archive of '
    + 'it would pass through Jason\'s own data: the chat does not take it, and runs on such a folder only a plain listing, such as `ls ~`. Name a narrower '
    + 'folder that does not hold it, such as Documents or Desktop. Nothing was done.';
/**
 * Where the hook rides the turn, the refusal of a jq program that reads what its input does not hold: the environment
 * (`env`, `$ENV`), a module or a data file by its own words (`import`, `include`), or a program from a file (`-f`).
 */
export const jqRefusal =
    'That jq program reads the environment or a file by its own words (`env`, `$ENV`, `import`, `include`, or a program from a file), which the chat does not run: '
    + 'give jq what it reads on standard input, such as `jason campaign list | jq \'.items\'`. Nothing was run.';
/**
 * Where the hook rides the turn, the refusal of a command told the files it reads on its input or by another file
 * (`--files0-from`), whose names no word of it holds.
 */
export const filesFromRefusal =
    'That command reads files whose names come on its input or from another file, so no word of it says which files it opens, and one may be Jason\'s own '
    + 'data and not one of the person\'s files: the chat does not take it. Name each file in the command instead, such as `wc -l notes.txt`. Nothing was run.';
const lengthRefusal = 'That path is longer than the system takes: the chat neither reads nor writes it. Nothing was done.';
/** The longest path each platform takes: the `\\?\` form's limit on Windows, `PATH_MAX` on macOS and Linux. */
const pathLimit = (platform: Platform) => (platform === 'win32' ? 32_767 : 4_096);
const replyRefusal = 'The chat runs only reply auth status and reply account list of reply: provider work goes through jason, and the connection is made on the Connect card. Nothing was run.';
export const commandRefusal = 'That command is a person\'s own act or changes the installation, so the chat never runs it: the person runs it themselves. Nothing was run.';
/** A jason that starts with an option, or carries a word that begins with @: refused in every mode, by the form to use. */
export const optionRefusal = 'That command starts jason with an option, or gives it a word that begins with @, which the chat never runs: write the verb first and its options after it, such as `jason approval approve <id> --actor human:<name>`. Nothing was run.';
export const rootHelpRefusal = 'That command starts jason with an option, which the chat never runs, so nothing was run: to read a noun\'s verbs and options, run `jason <noun> --help`, such as `jason campaign --help`.';
/**
 * A command that names or clears Jason's own variables (`ownVariables`), in every mode: Jason alone sets them, and a jason
 * started without the chat's environment may act on another installation than the one this window shows; and
 * `JASON_CHAT_TURN` is what keeps a jason the chat runs from reading a file an option names, so the sentence names the
 * form that gives a request's content instead.
 */
export const confinedRefusal =
    'That command names or clears JASON_CONFINED or JASON_CHAT_TURN, Jason\'s own variables for this turn and not the person\'s, which Jason alone sets and the '
    + 'chat does not open or change: a jason started without them may act on another installation than the one this window shows. Run the `jason` verb you '
    + 'need as it is, such as `jason campaign list`, with a request\'s content on standard input, as `--file - < file`. Nothing was run.';
export const attemptRefusal =
    'That command names JASON_ATTEMPT_ID, one of Jason\'s own variables and not the person\'s, which says a role\'s attempt runs: the chat is never one, and '
    + 'does not open or change it. `jason workitem get` reads an item and its attempts. Nothing was run.';
export const lookupRefusal =
    'That command changes where the shell finds a program, Jason\'s own for this turn and not the person\'s, which Jason itself sets and the chat does not '
    + 'open or change, so that the jason it starts stays the desktop\'s: run the `jason` verb you need as it is, such as `jason campaign list`. Nothing was run.';
/** What the hook answers where reading a tool use failed: it refuses, since a judgement that failed never runs anything. */
const unreadRefusal = 'The desktop could not read what this tool would do, so it was not run.';
const instructionsRefusal = 'That file is the chat\'s own instructions, which the desktop writes before each turn: the chat never changes it. Nothing was done.';
const configurationRefusal = 'That folder is the chat\'s own AI settings and skills, which the desktop keeps: the chat never changes it. Nothing was done.';
const serversRefusal = 'That file would name MCP servers for the chat\'s own AI, which only the person chooses: the chat never changes it. Nothing was done.';

/** The refusal of a write of, or under, one of the chat's own names directly in the chat folder. */
function ownRefusal(kind: NonNullable<GuardedName['chatsOwn']>): string {
    return kind === 'instructions' ? instructionsRefusal : kind === 'servers' ? serversRefusal : configurationRefusal;
}
/** What a command that names one of them is told after the write's sentence: the file tools read it. */
const ownByCommand = ' A command that names it is refused, since it cannot be told apart as a read or a write: read it with the file tools, Read, Grep and Glob. Nothing was run.';
const longRefusal ='That command is longer than the card can show whole, so nobody could read what they would allow: the chat never runs it. '
    + 'Put the content in a file in the chat\'s folder and give it as --file - < <file>, or split the work. Nothing was run.';

const pathsOf = (platform: Platform) => (platform === 'win32' ? win32 : posix);
const keyOf = (platform: Platform, path: string) => (platform === 'linux' ? path : path.toLowerCase());

function shorten(text: string): string {
    return text.length > detailLimit ? `${text.slice(0, detailLimit)}…` : text;
}

/**
 * The home folder's spellings a path may begin with, each followed by a separator or nothing: `~` and the variables a
 * shell reads it from, in the spelling of each shell the chat may run (Git Bash, PowerShell, the command prompt). On
 * Windows a variable's name is read in any case, as Windows reads it.
 */
const homeSpellings = ['~', '$HOME', '${HOME}', '%USERPROFILE%', '$env:USERPROFILE', '$USERPROFILE', '${USERPROFILE}'];

/** A path with a leading home spelling (`homeSpellings`) replaced by the home folder; any other path as it is. */
function homeExpanded(value: string, scope: ChatScope): string {
    for (const spelling of homeSpellings) {
        const head = value.slice(0, spelling.length);
        const same = scope.platform === 'win32' && spelling !== '~' ? head.toLowerCase() === spelling.toLowerCase() : head === spelling;
        if (same && /^([\\/]|$)/.test(value.slice(spelling.length))) return `${scope.home}${value.slice(spelling.length)}`;
    }
    return value;
}

/**
 * A `file:` URL's path: a host other than none is kept as a network path's host, `localhost` among them on Windows, where
 * it names the machine as another computer would. Any other value as it is.
 */
function urlPath(value: string, platform: Platform): string {
    const url = /^file:(\/\/([^/\\]*))?([\\/].*)?$/i.exec(value);
    if (url === null) return value;
    let path = url[3] ?? '/';
    try {
        path = decodeURIComponent(path);
    } catch {
        // A malformed escape is read as written.
    }
    const host = url[2] ?? '';
    if (host !== '' && !(platform !== 'win32' && host.toLowerCase() === 'localhost')) return `//${host}${path}`;
    // `file:///C:/x` is the drive's own path on Windows.
    return platform === 'win32' && /^[\\/][A-Za-z]:([\\/]|$)/.test(path) ? path.slice(1) : path;
}

/** The prefix under which macOS also shows the data volume's folders (a firmlink): `/System/Volumes/Data/Users` is `/Users`. */
const dataVolume = '/system/volumes/data';

/**
 * A path in the one spelling every check of the chat compares, before anything is looked up. Every site that matches a
 * path against the chat's folders, the data directory or the person's own directories reaches it through this function,
 * directly or through `located`:
 * - a leading home spelling (`homeExpanded`) is the home folder, at any depth below it;
 * - a `file:` URL is its path (`urlPath`);
 * - on Windows, slashes are backslashes, a `\\?\` or `\\.\` form of a drive's path is the plain path, `\\?\UNC\` is a
 *   network path, any other `\\?\` or `\\.\` form is a device path, kept as it is spelled (`device`), and each component
 *   is read as Windows opens it: without a stream after a colon and without trailing dots and spaces;
 * - on macOS, a path under the data volume's prefix is the same path without it;
 * - `..` and a relative path are settled against the folder the path is read in (`scope.cwd`, which a literal directory
 *   change sets for the commands after it).
 */
function spelled(value: string, scope: ChatScope): string {
    let text = urlPath(homeExpanded(value, scope), scope.platform);
    if (scope.platform === 'win32') {
        text = text.replace(/\//g, '\\');
        if (/^\\\\[?.]\\UNC\\/i.test(text)) text = `\\\\${text.slice(8)}`;
        else if (/^\\\\[?.]\\[a-z]:\\/i.test(text)) text = text.slice(4);
        // A device's or a volume's namespace names no folder this resolves: the path is kept as spelled, a device path.
        else if (/^\\\\[?.]\\/.test(text) || /^\\\\[?.]$/.test(text)) return text;
    }
    const paths = pathsOf(scope.platform);
    let path = paths.resolve(scope.cwd, text);
    if (scope.platform === 'win32') {
        const root = win32.parse(path).root;
        const parts = path.slice(root.length).split('\\').map((part) => part.replace(/:[\s\S]*$/, '').replace(/[. ]+$/, '')).filter((part) => part !== '');
        path = `${root}${parts.join('\\')}`;
    }
    return volumeFolded(scope.platform, path);
}

/** On macOS, a path under the data volume's prefix as the same path without it (`dataVolume`); any other as it is. */
function volumeFolded(platform: Platform, path: string): string {
    if (platform !== 'darwin') return path;
    const key = path.toLowerCase();
    return key === dataVolume || key.startsWith(`${dataVolume}/`) ? path.slice(dataVolume.length) || '/' : path;
}

/**
 * A path that names another computer, whatever the computer, or on Windows a device path (`device`): never looked up,
 * since a lookup can send the machine's credentials, and never matched against a folder, since it names none.
 */
function network(platform: Platform, path: string): boolean {
    return platform === 'win32' ? path.startsWith('\\\\') : path.startsWith('/net/');
}

/** On Windows, a path in a device's or a volume's namespace (`\\.\GLOBALROOT\...`, `\\?\Volume{...}\...`), as `spelled` keeps it. */
function device(platform: Platform, path: string): boolean {
    return platform === 'win32' && /^\\\\[?.](\\|$)/.test(path);
}

/** The refusal of a path that names another computer or a device (`network`); null for any other. */
function remoteRefusal(platform: Platform, path: string): string | null {
    if (device(platform, path)) return deviceRefusal;
    return network(platform, path) ? networkRefusal : null;
}

/** The computer a network path (`network`) names. */
function computerOf(platform: Platform, path: string): string {
    return platform === 'win32' ? (path.slice(2).split('\\')[0] ?? '') : (path.slice('/net/'.length).split('/')[0] ?? '');
}

/**
 * Where a path really is: its one spelling (`spelled`), then its nearest existing parent's real path with the rest of the
 * path after it, links, junctions and short names followed. A network path is never looked up.
 */
function located(value: string, scope: ChatScope): string {
    const paths = pathsOf(scope.platform);
    const path = spelled(value, scope);
    if (network(scope.platform, path)) return path;
    const rest: string[] = [];
    let at = path;
    for (;;) {
        const real = scope.real(at);
        // A link may lead to the data volume's spelling of a folder: the real path is folded as the spelling was.
        if (real !== null) return volumeFolded(scope.platform, rest.length === 0 ? real : paths.join(real, ...rest.reverse()));
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

/** The person's own directories a demo run fences off whole; none in the person's own installation. */
function personsOwnOf(scope: ChatScope): readonly string[] {
    return scope.personsOwn ?? [];
}

/** True when a located path is in one of the person's own directories (`ChatScope.personsOwn`). */
function inPersonsOwn(scope: ChatScope, path: string): boolean {
    return personsOwnOf(scope).some((folder) => inside(scope, located(folder, scope), path));
}

/**
 * The fenced folders as located paths: the data directory, and in a demo run the person's own directories. A place that
 * holds one of them, or is one, may walk into it.
 */
function fencedFolders(scope: ChatScope): string[] {
    return [scope.data, ...personsOwnOf(scope)].map((folder) => located(folder, scope));
}

/** True when a located place is a fenced folder or a folder holding one, so a walk of it may reach the fenced data. */
function holdsFenced(scope: ChatScope, place: string): boolean {
    return fencedFolders(scope).some((folder) => inside(scope, place, folder));
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
        return { verdict: 'deny', message: ownRefusal(guarded.chatsOwn) };
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
 * A path whose spelling is a card (one that begins with `~`, or that Windows reads as another name) is judged in its one
 * spelling (`spelled`) too, read or write: a refusal there is said as itself, and so is a guard's card for a write with a
 * Windows name; anything else is the spelling's card.
 */
function judgePath(value: string, scope: ChatScope, writes: boolean): Judgement {
    const spelling = judgeSpelling(value, scope);
    if (spelling === null) return judgeLocated(value, scope, writes);
    if (spelling.verdict !== 'ask') return spelling;
    const judged = judgeLocated(value, scope, writes);
    if (judged.verdict === 'deny') return judged;
    // Where the hook rides the turn a path is judged where its one spelling leads, as the hook judges it (`pathKind`): a
    // `~` of the home folder is no card of its own there, since the file tools take it for the home folder this judges it
    // against, so a change outside the chat's folders is its one card; and for a read, nor is a name Windows reads as
    // another. A `~user` keeps its card.
    if (scope.hooked === true && (homeTilde(value) || (!writes && spelling.title === windowsNameTitle))) return judged;
    return writes && spelling.title === windowsNameTitle && judged.verdict === 'ask' && guardTitles.has(judged.title) ? judged : spelling;
}

/** True when a path begins with the home folder's own `~`: alone, or followed by a separator. */
function homeTilde(value: string): boolean {
    return /^~([\\/]|$)/.test(value);
}

function judgeLocated(value: string, scope: ChatScope, writes: boolean): Judgement {
    // A path that names another computer, or this one as another computer would, is outside every chat's folders: never
    // read and never written, in every mode.
    const remote = remoteRefusal(scope.platform, spelled(value, scope));
    if (remote !== null) return { verdict: 'deny', message: remote };
    const paths = pathsOf(scope.platform);
    const path = located(value, scope);
    const key = keyOf(scope.platform, path);
    if (inPersonsOwn(scope, path)) return { verdict: 'deny', message: personsRefusal };
    if (scope.protectedFiles.some((file) => keyOf(scope.platform, located(file, scope)) === key)) return { verdict: 'deny', message: settingsRefusal };
    const chat = located(scope.chat, scope);
    if (inside(scope, located(scope.data, scope), path) && !inside(scope, chat, path)) return { verdict: 'deny', message: dataRefusal };
    if (writes) return judgeWrite(scope, path);
    if (workingFolderOf(scope, path) !== null) return allow;
    // Where the hook rides the turn a read anywhere else runs with no card: the refusals above are the hook's own kinds.
    if (scope.hooked === true) return allow;
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
 * True when a folder holds the data directory or, in a demo run, one of the person's own directories, read in its one
 * spelling (`spelled`), as a walk would most likely reach it.
 */
function holdsData(value: string, scope: ChatScope): boolean {
    return holdsFenced(scope, located(value, scope));
}

/**
 * A folder a search or a listing walks: one that holds the data directory is refused, since the walk would read what the
 * chat never reads, and that comes before a card for its spelling; where the hook rides the turn, which refuses such a
 * walk itself (`walkKind`), it is the person's card each time, never covered by a grant. Any other is judged as a read of
 * it. Its card is remembered by the folder it names and by the tool's own kind, so a grant covers that tool's walks of
 * that folder only: never a Read, a sibling, a folder under it or another tool's walk, and a Read's grant never covers it.
 */
function judgeFolder(tool: string, value: string, scope: ChatScope): Judgement {
    const judged = judgePath(value, scope, false);
    if (judged.verdict === 'deny') return judged;
    if (holdsData(value, scope)) {
        return scope.hooked === true
            ? { verdict: 'ask', title: walkTitles[tool] ?? 'Run a command', detail: shorten(located(value, scope)), remember: null }
            : { verdict: 'deny', message: searchRefusal };
    }
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
    // Where the hook rides the turn a pattern under the home folder's own `~` is a walk of its fixed folders, judged as a
    // folder's walk is, with no card of its own (`homeTilde`).
    if (scope.hooked === true && homeTilde(pattern)) return worse(judgeFolder('Glob', fixed.join(separator), scope), judged);
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
        + 'timeout nohup time command exec watch nice stdbuf setsid doas alacritty gdb lldb').split(' '),
);
const interpreterStem = /^(python|pythonw|node|nodejs|ruby|perl|php)[0-9.]*$/;

/**
 * True when a program's name, without a folder or an executable's extension, is one that runs another program or a script,
 * or hands a file or a command to the system's own opener or scheduler (`systemLaunchers`).
 */
function runsAnotherProgram(name: string): boolean {
    const key = name.toLowerCase();
    return runsAnother.has(key) || interpreterStem.test(key) || systemLaunchers.has(key);
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
    // A link a command makes leads where no later command's words say: each is a card of its own.
    if (linkOperands(words) !== null || followsLinksIn(words)) return null;
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
 * The first word is held in place, so `contact update ... update` is not the `update` entry. The words are compared in
 * any letter case (an entry is lower case by the file's own rule): the CLI takes a mis-cased verb for a usage error, and
 * the fence does not lean on that.
 */
function holdsEntry(words: readonly string[], entry: string): boolean {
    const [first, ...rest] = entry.split(' ');
    const folded = words.map((word) => word.toLowerCase());
    if (folded[0] !== first) return false;
    let at = 1;
    for (const word of rest) {
        while (at < folded.length && folded[at] !== word) at++;
        if (at === words.length) return false;
        at++;
    }
    return true;
}

/**
 * What the text holds of jason: `option` when any jason command inside it, in any part, form or nesting, starts with an
 * option or carries a word that begins with `@` (a file of words); `deny` when one holds a deny-list entry, or when the
 * text names a denied operation however it would reach the runtime, which only Plan only's fence names; `help` when no such command or operation is there but a jason
 * command starts with `--help`, which is refused as `deny` is, by a sentence that names the help the chat runs instead;
 * `jason` when it runs jason otherwise; null when it does not.
 */
function jasonIn(command: string, scope: ChatScope): 'deny' | 'option' | 'help' | 'jason' | null {
    let found: 'jason' | null = null;
    let help = false;
    for (const part of command.split(/&&|\|\||\$\(|[;|&`\n\r<>(){}]/)) {
        const tokens = part.trim().split(/\s+/).map(unquoted).filter((token) => token !== '');
        for (const [at, token] of tokens.entries()) {
            if (programOf(scope.platform, token) !== 'jason') continue;
            found = 'jason';
            const words = tokens.slice(at + 1);
            if (words.some((word) => word.startsWith('@'))) return 'option';
            // Where the hook rides the turn jason's own help and version, each alone, are jason's like any verb.
            if (scope.hooked === true && words.length === 1 && rootForms.includes(words[0] as string)) continue;
            if (words[0]?.toLowerCase() === '--help') help = true;
            else if (words[0]?.startsWith('-')) return 'option';
            if (scope.denied.some((entry) => holdsEntry(words, entry))) return 'deny';
        }
    }
    const text = command.toLowerCase();
    if (scope.deniedOperations.some((operation) => new RegExp(`(?<![a-z0-9_])${operation.replace('.', '\\.')}(?![a-z0-9_])`).test(text))) return 'deny';
    return help ? 'help' : found;
}

/**
 * jason's own help and version, each the one word after `jason`: where the hook rides the turn they run with no card, and
 * elsewhere a jason that starts with an option is refused (`jasonIn`).
 */
const rootForms: readonly string[] = ['--help', '-h', '--version'];

/** The refusal of what `jasonIn` found, or null where it found nothing refused. */
function jasonRefusal(found: ReturnType<typeof jasonIn>): string | null {
    return found === 'deny' ? commandRefusal : found === 'option' ? optionRefusal : found === 'help' ? rootHelpRefusal : null;
}

/**
 * True when the Bash tool's command is exactly `jason <noun> --help` for a noun whose help runs (`ChatScope.help`): one
 * command, those three words and no other, apart by spaces and tabs alone, as the host's help rule names it. Any other
 * tool's command, and any other separator, is judged as it was before.
 */
function isNounHelp(tool: string, command: string, scope: ChatScope): boolean {
    if (tool !== 'Bash' || isCompound(command)) return false;
    const words = command.replace(/^[ \t]+|[ \t]+$/g, '').split(/[ \t]+/);
    return words.length === 3 && words[0] === 'jason' && words[2] === '--help' && (scope.help ?? []).includes(words[1] as string);
}

/**
 * True when the Bash tool's command is a plain form of the host's allow rules (`ChatScope.allowed`), read literally with
 * no `~` taken (`readLiteral`): one command whose program is the desktop's own `jason` and whose next words are a form's;
 * whose standard input is nothing, a here-document, or one file in a working folder (`workingFolderOf`) given by its only
 * redirection, and which otherwise redirects only to `/dev/null`; with no file named jason in the folder the shell runs
 * in (`shadowed`) and no guard's card. It is judged after every refusal, so a redirection from the installation's
 * settings file, its run file or its data is refused before it is looked at.
 */
function plainForm(tool: string, command: string, places: readonly string[], scope: ChatScope): boolean {
    const forms = scope.allowed ?? [];
    if (tool !== 'Bash' || forms.length === 0) return false;
    // Whatever goes wrong while the command is read, it keeps its card: a reading that failed never runs anything.
    try {
        const reading = readLiteral('Bash', command, { platform: scope.platform, home: null });
        if (reading.held || reading.commands.length !== 1) return false;
        const { words, targets, input, cwd } = reading.commands[0] as LiteralCommand;
        if (cwd !== null || words[0] !== 'jason' || !forms.some((form) => form.split(' ').every((word, at) => words[at + 1] === word))) return false;
        if (input === 'file' ? targets.length !== 1 : targets.some((target) => target !== '/dev/null')) return false;
        if (input === 'file' && workingFolderOf(scope, located(targets[0] as string, scope)) === null) return false;
        if (shadowed(scope, new Set(['jason']))) return false;
        return !changesServers(command, scope) && !namesSearch(command) && guardedPlace(places, scope) === undefined && !namesLocalJason(command, scope);
    } catch {
        return false;
    }
}

/**
 * The programs that run with no card beside jason where the hook rides an Ask first or Auto-edit turn, alone or in a
 * pipeline or a sequence with it (`hookedNoCard`): a closed list of the host's read-only commands and `jq`, each of which
 * reads its input and prints, and writes nothing, starts nothing and runs nothing it is given. Each is named bare, as the
 * shell finds it, and takes only the options of its own table (`optionTables`); in a command of more than one part each
 * reads its standard input alone and names no file (`noCardFilter`), but `ls`, which lists the folders it names, and
 * `echo`, which prints its words. Any other program is a card, as before.
 */
export const noCardFilters: readonly string[] = ['jq', 'grep', 'head', 'tail', 'wc', 'sort', 'uniq', 'cut', 'tr', 'cat', 'ls', 'echo'];

/**
 * A program's options in the forms the chat knows read no folder below the ones they name and no file they are told of
 * on their input or by another file: letters alone (`flags`), letters that take a value, the rest of their word or the
 * next word (`valued`), long options alone (`long`), long options that take a value after `=` or as the next word
 * (`longValued`), and long options that take two (`longPairs`, jq's `--arg NAME VALUE`). Each is exact, since a program
 * that takes an abbreviated long option reads a prefix as the option it begins; nothing else is taken. `operands` says
 * what its other words are: files, a pattern and then files, a program and then files, folders it lists, or the sets of
 * letters `tr` reads.
 */
interface OptionTable {
    readonly flags: string;
    readonly valued?: string;
    readonly long?: readonly string[];
    readonly longValued?: readonly string[];
    readonly longPairs?: readonly string[];
    /** A count as an option of its own, as `head -5`. */
    readonly count?: boolean;
    readonly operands: 'files' | 'pattern' | 'program' | 'folders' | 'sets';
}

const colorOptions = ['--color', '--color=never', '--color=auto', '--color=always'];
const grepTable: OptionTable = {
    flags: 'ivcnlLhHoqswxEFGPabzZ',
    valued: 'emABC',
    long: [
        '--ignore-case', '--no-ignore-case', '--invert-match', '--count', '--line-number', '--files-with-matches', '--files-without-match', '--no-filename',
        '--with-filename', '--only-matching', '--quiet', '--silent', '--no-messages', '--word-regexp', '--line-regexp', '--extended-regexp',
        '--fixed-strings', '--basic-regexp', '--perl-regexp', '--text', '--byte-offset', '--null-data', '--null', ...colorOptions,
        ...colorOptions.map((option) => option.replace('color', 'colour')),
    ],
    longValued: ['--regexp', '--max-count', '--after-context', '--before-context', '--context'],
    operands: 'pattern',
};
const headTable: OptionTable = { flags: 'qvz', valued: 'nc', count: true, long: ['--quiet', '--silent', '--verbose', '--zero-terminated'], longValued: ['--lines', '--bytes'], operands: 'files' };

/**
 * The options each program of `noCardFilters` takes, and the plain forms of a walk (`plainWalkForm`), GNU's and BSD's
 * in common use, none that descends into a folder, reads a file it is told of on its input or by another file, writes a
 * file or starts a program: `ls` and `grep` with no recursion and no `-d`, `sort` with no `-o`, `-T`, `--files0-from` or
 * `--compress-program`, `wc` with no `--files0-from`, `tail` with no `-f`, and `jq` with only `-r`, `-c`, `-e`, `-s`,
 * `-j`, `-S`, `-M`, `-a`, `--arg` and `--argjson`.
 */
const optionTables: Readonly<Record<string, OptionTable>> = {
    ls: {
        flags: 'aAlhd1FptrSsiognGcuUXvkQmxC',
        long: ['--all', '--almost-all', '--human-readable', '--directory', '--classify', '--reverse', '--size', '--inode', '--numeric-uid-gid',
            '--group-directories-first', '--full-time', '--literal', '--si', '--kibibytes', '--no-group', ...colorOptions],
        operands: 'folders',
    },
    grep: grepTable,
    egrep: grepTable,
    fgrep: grepTable,
    cat: { flags: 'AbeEnstTuv', long: ['--show-all', '--number-nonblank', '--show-ends', '--number', '--squeeze-blank', '--show-tabs', '--show-nonprinting'], operands: 'files' },
    head: headTable,
    tail: headTable,
    wc: { flags: 'cmlwL', long: ['--bytes', '--chars', '--lines', '--words', '--max-line-length'], operands: 'files' },
    sort: {
        flags: 'bdfgiMhnrVsuz',
        valued: 'kt',
        long: ['--ignore-leading-blanks', '--dictionary-order', '--ignore-case', '--general-numeric-sort', '--ignore-nonprinting', '--month-sort',
            '--human-numeric-sort', '--numeric-sort', '--reverse', '--version-sort', '--stable', '--unique', '--zero-terminated'],
        longValued: ['--key', '--field-separator'],
        operands: 'files',
    },
    uniq: {
        flags: 'cduiz',
        valued: 'fsw',
        long: ['--count', '--repeated', '--unique', '--ignore-case', '--zero-terminated'],
        longValued: ['--skip-fields', '--skip-chars', '--check-chars'],
        operands: 'files',
    },
    cut: {
        flags: 'snz',
        valued: 'bcfd',
        long: ['--complement', '--only-delimited', '--zero-terminated'],
        longValued: ['--bytes', '--characters', '--fields', '--delimiter', '--output-delimiter'],
        operands: 'files',
    },
    tr: { flags: 'cCdst', long: ['--complement', '--delete', '--squeeze-repeats', '--truncate-set1'], operands: 'sets' },
    jq: { flags: 'rcesjSMa', longPairs: ['--arg', '--argjson'], operands: 'program' },
};

/**
 * A program's words that are no option, read by its table (`OptionTable`), and whether an option gave a grep its
 * pattern; null where a word is an option the table does not take, or one that needs a value it is not given. Every word
 * that begins with a dash before `--` is an option, wherever it stands, as GNU's programs read it.
 */
function operandsOf(table: OptionTable, args: readonly string[]): { operands: string[]; patternGiven: boolean } | null {
    const operands: string[] = [];
    let patternGiven = false;
    for (let at = 0; at < args.length; at++) {
        const word = args[at] as string;
        if (word === '--') {
            operands.push(...args.slice(at + 1));
            break;
        }
        if (word === '-' || !word.startsWith('-')) {
            operands.push(word);
            continue;
        }
        if (word.startsWith('--')) {
            const equals = word.indexOf('=');
            const name = equals < 0 ? word : word.slice(0, equals);
            if (table.long?.includes(word) === true) continue;
            if (table.longValued?.includes(name) === true) {
                if (equals < 0 && ++at >= args.length) return null;
                if (name === '--regexp') patternGiven = true;
                continue;
            }
            if (equals < 0 && table.longPairs?.includes(word) === true) {
                at += 2;
                if (at >= args.length) return null;
                continue;
            }
            return null;
        }
        if (table.count === true && /^-[0-9]+$/.test(word)) continue;
        for (let letter = 1; letter < word.length; letter++) {
            const flag = word[letter] as string;
            if (table.flags.includes(flag)) continue;
            if (table.valued?.includes(flag) !== true) return null;
            if (letter === word.length - 1 && ++at >= args.length) return null;
            if (table.operands === 'pattern' && flag === 'e') patternGiven = true;
            break;
        }
    }
    return { operands, patternGiven };
}

/** The files a program reads among its words that are no option, by what its table says they are; `-` is its input. */
function filesOf(table: OptionTable, read: { operands: string[]; patternGiven: boolean }): string[] {
    const named = table.operands === 'sets' ? [] : table.operands === 'program' || (table.operands === 'pattern' && !read.patternGiven) ? read.operands.slice(1) : read.operands;
    return named.filter((word) => word !== '-');
}

/**
 * True when a program of `noCardFilters` runs with these words with no card where the hook rides the turn: `echo` with
 * any; any other with only the options its table takes (`optionTables`), `uniq` with one file at most, since its second
 * is the file it writes; and in a command of more than one part (`compound`) none but `ls` names a file, so each reads its
 * standard input alone.
 */
function noCardFilter(program: string, args: readonly string[], compound: boolean): boolean {
    if (!noCardFilters.includes(program)) return false;
    if (program === 'echo') return true;
    const table = optionTables[program];
    const read = table === undefined ? null : operandsOf(table, args);
    if (table === undefined || read === null) return false;
    const files = filesOf(table, read);
    if (program === 'uniq' && files.length > 1) return false;
    return !compound || table.operands === 'folders' || files.length === 0;
}

/** True when a word gives an option: its name in any letter case, alone or with its value after `=` or `:`. */
function namesOption(word: string, option: string): boolean {
    const folded = word.toLowerCase();
    return folded === option || folded.startsWith(`${option}=`) || folded.startsWith(`${option}:`);
}

/**
 * True when the words after `jason` run with no card where the hook rides an Ask first or Auto-edit turn: any verb in any
 * form but the forms that keep a card (`askingCardedForms`), whose verb is told in any letter case and with anything
 * between its words (`holdsEntry`) and whose option is told anywhere among them (`namesOption`); the help of a carded
 * form, a `--help` or `-h` anywhere before a `--`, which runs nothing; and jason's own help and version, each alone
 * (`rootForms`). Nothing else that starts with an option.
 */
function noCardJason(rest: readonly string[]): boolean {
    const first = rest[0];
    if (first === undefined) return false;
    if (first.startsWith('-')) return rest.length === 1 && rootForms.includes(first);
    const carded = askingCardedForms.some(
        ({ verb, options }) => holdsEntry(rest, verb) && (options === undefined || rest.some((word) => options.some((option) => namesOption(word, option)))),
    );
    if (!carded) return true;
    const end = rest.indexOf('--');
    return (end < 0 ? rest : rest.slice(0, end)).some((word) => word === '--help' || word === '-h');
}

/**
 * Where the hook rides an Ask first or Auto-edit turn, true when the Bash tool's command runs with no card: read
 * literally (`readLiteral`, a `~` taken only where the shell's home is the judgement's, and a letter outside ASCII read in
 * a single-quoted body), one command or a pipeline or a sequence of them, each the desktop's own `jason` in a form that
 * runs with no card (`noCardJason`) or a program of `noCardFilters` as it runs with none (`noCardFilter`); no directory
 * change; standard input nothing, a pipe, a here-document or one file given by its only redirection, and every other
 * redirection to `/dev/null` alone, so nothing is written into a file; no file named like one of its programs in the
 * folder the shell runs in (`shadowed`) and no guard's card. It is judged after every refusal, and its own reading is held
 * to them again (`refusalOver`), since the readings they read do not take a letter outside ASCII. Anything else, compound
 * or not, is the card it was.
 */
function hookedNoCard(tool: string, command: string, places: readonly string[], scope: ChatScope): boolean {
    if (scope.hooked !== true || tool !== 'Bash') return false;
    // Whatever goes wrong while the command is read, it keeps its card: a reading that failed never runs anything.
    try {
        const reading = readLiteral('Bash', command, { platform: scope.platform, home: shellHomeOf(scope), lettersInSingleQuotes: true });
        if (reading.held) return false;
        const read = reading.commands.flatMap(({ words, targets }) => placesOfWords([...words, ...targets], scope, true));
        const text = reading.commands.map(({ words, targets }) => [...words, ...targets].join(' ')).join('\n');
        if (refusalOver(text, read, scope) !== null || read.some((path) => isProtected(scope, path)) || guardedPlace(read, scope) !== undefined) return false;
        const programs = new Set<string>();
        const compound = reading.commands.length > 1;
        for (const { words, targets, input, cwd } of reading.commands) {
            if (cwd !== null) return false;
            if (input === 'file' ? targets.length !== 1 : targets.some((target) => target !== '/dev/null')) return false;
            const [program = '', ...rest] = words;
            if (program === 'jason' ? !noCardJason(rest) : !noCardFilter(program, rest, compound)) return false;
            programs.add(program);
        }
        if (shadowed(scope, programs)) return false;
        return !changesServers(command, scope) && !namesSearch(command) && guardedPlace(places, scope) === undefined && !namesLocalJason(command, scope);
    } catch {
        return false;
    }
}

/**
 * The programs the chat knows do not walk a folder they are given, whatever their words: they print their words
 * (`echo`, `printf`), change or say the folder the shell runs in (`cd`, `pushd`, `popd`, `pwd`), or look at a path and no
 * further (`test`, `[`, `[[`).
 */
const neverWalks: ReadonlySet<string> = new Set(['echo', 'printf', 'cd', 'chdir', 'pushd', 'popd', 'pwd', 'test', '[', '[[']);
/** PowerShell's kinds of `neverWalks`. */
const powerShellNeverWalks: ReadonlySet<string> = new Set([
    'set-location', 'sl', 'cd', 'chdir', 'push-location', 'pushd', 'pop-location', 'popd', 'get-location', 'gl', 'pwd', 'write-output', 'write', 'echo',
    'write-host', 'test-path',
]);
/** PowerShell's listing, which walks only with a parameter its table does not take (`powerShellListingParameters`). */
const powerShellListing: ReadonlySet<string> = new Set(['get-childitem', 'gci', 'ls', 'dir']);
const powerShellListingParameters: readonly string[] = ['-force', '-name', '-directory', '-file', '-hidden', '-path', '-literalpath', '-filter', '-erroraction', '-ea'];
const powerShellValued: readonly string[] = ['-path', '-literalpath', '-filter', '-erroraction', '-ea'];
/** The PowerShell commands that print or reshape what a pipe gives them, and open no path it gives. */
const powerShellPrints: ReadonlySet<string> = new Set([
    'write-output', 'write', 'echo', 'write-host', 'out-host', 'out-string', 'out-null', 'measure-object', 'measure', 'select-object', 'select',
    'sort-object', 'sort', 'format-table', 'ft', 'format-list', 'fl', 'format-wide', 'fw', 'where-object', 'where', '?', 'foreach-object', '%',
    'foreach', 'group-object', 'group', 'convertto-json', 'convertfrom-json',
]);

/** The plain forms whose programs have an option that descends into a folder: a listing and a search. */
const descends: ReadonlySet<string> = new Set(['ls', 'grep', 'egrep', 'fgrep']);

/**
 * True when a program with these words is one of the few forms the chat knows does not walk a folder it is given: a
 * program of `neverWalks`, whatever its words; one of `optionTables` with only the options its table takes, `uniq` with
 * one file at most, and, for a listing or a search (`descends`), no word the shell builds (`unknown`) and none that
 * begins with a pattern, either of which may be an option that descends; in PowerShell the kinds of `neverWalks`, and
 * its listing with only the parameters its table takes, each named whole, and no splatted word, the one way a value
 * becomes a parameter there.
 */
function plainWalkForm(program: string, args: readonly string[], powershell: boolean, unknown: (at: number) => boolean): boolean {
    if (powershell) {
        if (powerShellNeverWalks.has(program)) return true;
        if (!powerShellListing.has(program)) return false;
        for (let at = 0; at < args.length; at++) {
            const word = (args[at] as string).toLowerCase();
            if (word.startsWith('@')) return false;
            if (!word.startsWith('-')) continue;
            if (!powerShellListingParameters.includes(word)) return false;
            if (powerShellValued.includes(word)) at++;
        }
        return true;
    }
    if (neverWalks.has(program)) return true;
    const table = optionTables[program];
    if (table === undefined) return false;
    if (descends.has(program) && args.some((arg, at) => unknown(at) || /^[*?[]/.test(arg))) return false;
    const read = operandsOf(table, args);
    return read !== null && (program !== 'uniq' || filesOf(table, read).length <= 1);
}

/**
 * True when a program never runs a word it is given, so a quoted word or a here-document's body given to it is its data
 * and never a command (`walkedFrom`): the desktop's own jason, whose words are a request, and the programs of
 * `noCardFilters` but `sort`, and of `neverWalks`; in PowerShell its kinds of `neverWalks` and its listing.
 */
function takesTextOnly(program: string, powershell: boolean): boolean {
    if (program === 'jason') return true;
    if (powershell) return powerShellNeverWalks.has(program) || powerShellListing.has(program);
    return (program !== 'sort' && noCardFilters.includes(program)) || neverWalks.has(program);
}

/**
 * True when a command's text could make a program of the plain forms another one: an alias, a function, `hash` or
 * `enable` in a shell, and the variable BSD grep reads its options from; an alias, a function, a filter or a default
 * parameter in PowerShell. Such a command has no plain form.
 */
function redefines(command: string, powershell: boolean): boolean {
    return powershell
        ? /(?<![\w-])(?:function|filter|set-alias|new-alias|sal|nal|import-alias)(?![\w-])|\$PSDefaultParameterValues|(?:alias|function):/i.test(command)
        : /(?:^|[\s;&|(){}`])(?:alias|function|hash|enable)(?=[\s;&|()]|$)|[A-Za-z_][\w.:-]*[ \t]*\([ \t]*\)|GREP_OPTIONS/.test(command);
}

/**
 * A word as the walk reads it: its text, its quotes and escapes taken; whether it holds a space or a separator; whether
 * the shell builds part of it while it runs, by a variable, a substitution or an expression; and whether a pattern
 * outside quotes is in it.
 */
interface WrittenWord {
    text: string;
    spaced: boolean;
    built: boolean;
    pattern: boolean;
}

/**
 * One command as the walk reads it (`writtenCommands`): its words; the texts of the substitutions and expressions in
 * them, which run where it runs; the bodies of its here-documents, each with whether its tag was quoted; and whether a
 * pipe feeds it.
 */
interface WrittenCommand {
    words: WrittenWord[];
    inner: string[];
    bodies: { text: string; quoted: boolean }[];
    piped: boolean;
}

/** Where the parenthesis that closes the one at `open` stands, quoted text skipped; the text's end when none does. */
function closing(text: string, open: number, powershell: boolean): number {
    const escape = powershell ? '`' : '\\';
    let depth = 0;
    for (let at = open; at < text.length; at++) {
        const c = text[at] as string;
        if (c === escape) {
            at++;
        } else if (c === '\'') {
            const end = text.indexOf('\'', at + 1);
            if (end < 0) return text.length;
            at = end;
        } else if (c === '"') {
            let end = at + 1;
            while (end < text.length && text[end] !== '"') end += text[end] === escape ? 2 : 1;
            at = end;
        } else if (c === '(') {
            depth++;
        } else if (c === ')' && --depth === 0) {
            return at;
        }
    }
    return text.length;
}

/** Where the backquote that closes the one at `open` stands, an escaped one skipped; the text's end when none does. */
function backquoteEnd(text: string, open: number): number {
    for (let at = open + 1; at < text.length; at++) {
        if (text[at] === '\\') at++;
        else if (text[at] === '`') return at;
    }
    return text.length;
}

/** The texts of the substitutions a text holds where the shell runs them, `$(...)` and backquotes in a POSIX shell. */
function substitutions(text: string, powershell: boolean): string[] {
    const found: string[] = [];
    for (let at = 0; at < text.length; at++) {
        if (text[at] === '$' && text[at + 1] === '(') {
            const end = closing(text, at + 1, powershell);
            found.push(text.slice(at + 2, end));
            at = end;
        } else if (!powershell && text[at] === '`') {
            const end = backquoteEnd(text, at);
            found.push(text.slice(at + 1, end));
            at = end;
        }
    }
    return found;
}

/**
 * The commands of a text as its shell splits them, whatever is not literal in it: on every separator outside quotes, a
 * quoted text part of its word with its quotes taken and an escaped character taken as it is, as the POSIX grammar of the
 * Bash tool reads them and, with its backtick, its expressions and its blocks, PowerShell's does. A substitution, a
 * backquote and an expression stay in the word they stand in, their texts kept beside the command; a here-document's
 * body, and a here-string's, kept beside the command that reads it. A reading for refusals: it reads more commands than
 * run, never fewer.
 */
function writtenCommands(text: string, powershell: boolean): WrittenCommand[] {
    const commands: WrittenCommand[] = [];
    const fresh = (piped: boolean): WrittenCommand => ({ words: [], inner: [], bodies: [], piped });
    let current = fresh(false);
    let word: WrittenWord | null = null;
    const documents: { tag: string; quoted: boolean; dash: boolean; owner: WrittenCommand }[] = [];
    const escape = powershell ? '`' : '\\';
    const separating = powershell ? /[\s;&|()<>{}]/ : /[\s;&|()<>`]/;
    // A kept substitution is read through its text (`inner`), so it does not make its word one to read again.
    const append = (piece: string, built = false, pattern = false, kept = false) => {
        const each: WrittenWord = word ?? { text: '', spaced: false, built: false, pattern: false };
        each.text += piece;
        if (!kept && separating.test(piece)) each.spaced = true;
        if (built) each.built = true;
        if (pattern) each.pattern = true;
        word = each;
    };
    const endWord = () => {
        if (word !== null) current.words.push(word);
        word = null;
    };
    const inWord = () => word !== null;
    const endCommand = (piped = false) => {
        endWord();
        if (current.words.length > 0 || current.inner.length > 0 || current.bodies.length > 0) commands.push(current);
        current = fresh(piped);
    };
    // A substitution or an expression the shell runs where it stands: kept in its word, its text read as commands.
    const kept = (from: number, open: number): number => {
        const end = closing(text, open, powershell);
        current.inner.push(text.slice(open + 1, end));
        append(text.slice(from, end + 1), true, false, true);
        return end + 1;
    };
    let at = 0;
    while (at < text.length) {
        const c = text[at] as string;
        if (c === escape) {
            const next = text[at + 1];
            if (next !== '\n' && next !== undefined) append(next);
            at += 2;
            continue;
        }
        if (powershell && c === '@' && (text[at + 1] === '\'' || text[at + 1] === '"') && /^[ \t]*\r?\n/.test(text.slice(at + 2))) {
            const quote = text[at + 1] as string;
            const close = text.indexOf(`\n${quote}@`, at + 2);
            const body = text.slice(at + 2, close < 0 ? text.length : close);
            if (quote === '"') current.inner.push(...substitutions(body, true));
            append(body, quote === '"');
            at = close < 0 ? text.length : close + 3;
            continue;
        }
        if ((c === '$' || (powershell && c === '@')) && text[at + 1] === '(') {
            at = kept(at, at + 1);
            continue;
        }
        if (c === '(' && (inWord() || current.words.length > 0)) {
            at = kept(at, at);
            continue;
        }
        if (!powershell && c === '`') {
            const end = backquoteEnd(text, at);
            current.inner.push(text.slice(at + 1, end));
            append(text.slice(at, end + 1), true, false, true);
            at = end + 1;
            continue;
        }
        if (!powershell && c === '$' && text[at + 1] === '\'') {
            let end = at + 2;
            while (end < text.length && text[end] !== '\'') end += text[end] === '\\' ? 2 : 1;
            append(ansiC(text.slice(at + 2, end)));
            at = end + 1;
            continue;
        }
        if (!powershell && c === '$' && text[at + 1] === '"') {
            at++;
            continue;
        }
        if (c === '$') {
            append('$', true);
            at++;
            continue;
        }
        if (c === '\'') {
            let end = at + 1;
            let body = '';
            for (; end < text.length; end++) {
                if (text[end] !== '\'') {
                    body += text[end];
                    continue;
                }
                // PowerShell writes a quote inside a single-quoted text as two.
                if (powershell && text[end + 1] === '\'') {
                    body += '\'';
                    end++;
                    continue;
                }
                break;
            }
            append(body);
            at = end + 1;
            continue;
        }
        if (c === '"') {
            let end = at + 1;
            let body = '';
            let built = false;
            while (end < text.length) {
                const each = text[end] as string;
                if (each === '"') {
                    // PowerShell writes a quote inside a double-quoted text as two.
                    if (powershell && text[end + 1] === '"') {
                        body += '"';
                        end += 2;
                        continue;
                    }
                    break;
                }
                if (each === escape && end + 1 < text.length) {
                    body += text[end + 1];
                    end += 2;
                    continue;
                }
                if (each === '$' && text[end + 1] === '(') {
                    const close = closing(text, end + 1, powershell);
                    current.inner.push(text.slice(end + 2, close));
                    body += text.slice(end, close + 1);
                    built = true;
                    end = close + 1;
                    continue;
                }
                if (!powershell && each === '`') {
                    const close = backquoteEnd(text, end);
                    current.inner.push(text.slice(end + 1, close));
                    body += text.slice(end, close + 1);
                    built = true;
                    end = close + 1;
                    continue;
                }
                if (each === '$') built = true;
                body += each;
                end++;
            }
            append(body, built);
            at = end + 1;
            continue;
        }
        if (!powershell && c === '<' && text[at + 1] === '<' && text[at + 2] !== '<') {
            // A here-document: its tag, quoted or not, whose body follows the line.
            endWord();
            let from = at + 2;
            const dash = text[from] === '-';
            if (dash) from++;
            while (text[from] === ' ' || text[from] === '\t') from++;
            let tag = '';
            let quoted = false;
            while (from < text.length && !/[\s;&|()<>]/.test(text[from] as string)) {
                const each = text[from] as string;
                if (each === '\'' || each === '"' || each === '\\') quoted = true;
                else tag += each;
                from++;
            }
            documents.push({ tag, quoted, dash, owner: current });
            at = from;
            continue;
        }
        if (c === '<' || c === '>') {
            endWord();
            at++;
            while (text[at] === '<' || text[at] === '>') at++;
            if (text[at] === '&') at++;
            continue;
        }
        if (c === ' ' || c === '\t') {
            endWord();
            at++;
            continue;
        }
        if (c === '\n' || c === '\r') {
            endCommand();
            at++;
            if (c !== '\n') continue;
            while (documents.length > 0) {
                const document = documents.shift() as (typeof documents)[number];
                const lines: string[] = [];
                while (at < text.length) {
                    const end = text.indexOf('\n', at);
                    const line = text.slice(at, end < 0 ? text.length : end).replace(/\r$/, '');
                    at = end < 0 ? text.length : end + 1;
                    if ((document.dash ? line.replace(/^\t+/, '') : line) === document.tag) break;
                    lines.push(line);
                }
                document.owner.bodies.push({ text: lines.join('\n'), quoted: document.quoted });
            }
            continue;
        }
        if (c === '|') {
            const or = text[at + 1] === '|';
            endCommand(!or);
            at += or ? 2 : text[at + 1] === '&' ? 2 : 1;
            continue;
        }
        if (';&()'.includes(c) || (powershell && '{}'.includes(c))) {
            endCommand();
            at++;
            continue;
        }
        append(c, false, !powershell && (c === '*' || c === '?'));
        at++;
    }
    endCommand();
    for (const document of documents) document.owner.bodies.push({ text: '', quoted: document.quoted });
    return commands;
}

/** The words that stand before a program and are none: a shell's reserved words, a group's braces and a negation. */
const beforeProgram: ReadonlySet<string> = new Set(
    'if then elif else fi do done case esac while until for select function coproc time in ! { } noglob nocorrect repeat foreach'.split(' '),
);

/** Where a command's program stands among its words, after the words of `beforeProgram` and the assignments before it. */
function programIndex(texts: readonly string[], powershell: boolean): { at: number; assigned: boolean } {
    let assigned = false;
    for (let at = 0; at < texts.length; at++) {
        const word = texts[at] as string;
        if (beforeProgram.has(word)) continue;
        if (!powershell && /^[A-Za-z_][A-Za-z0-9_]*(?:\[[^\]]*\])?\+?=/.test(word)) {
            assigned = true;
            continue;
        }
        if (powershell && word.startsWith('$') && /^[+\-*/]?=$/.test(texts[at + 1] ?? '')) {
            assigned = true;
            at++;
            continue;
        }
        return { at, assigned };
    }
    return { at: texts.length, assigned };
}

/** A program's name as the walk compares it: as the platform finds it (`programOf`), and in PowerShell in any case. */
function programKey(platform: Platform, word: string, powershell: boolean): string {
    const name = programOf(platform, word);
    return powershell ? name.toLowerCase() : name;
}

/** The programs a word after which may be a folder change of the shell's own: `builtin cd` and `command cd`. */
const changeRunners: ReadonlySet<string> = new Set(['builtin', 'command']);
const posixChanges: ReadonlySet<string> = new Set(['cd', 'chdir', 'pushd']);
const powerShellChanges: ReadonlySet<string> = new Set(['set-location', 'sl', 'cd', 'chdir', 'push-location', 'pushd']);
/** The commands that go back to a folder the shell was in before: `popd`, and PowerShell's `Pop-Location`. */
const goesBack: ReadonlySet<string> = new Set(['popd', 'pop-location']);
/** The programs that run another with words they read on their standard input. */
const inputRunners: ReadonlySet<string> = new Set(['xargs', 'parallel']);
/**
 * On Windows, the programs whose short slash words are options (`/s`), not a drive's own path. Written as one string, as
 * `runsAnother` is, so the process guard keeps holding the quoted shell names of this module.
 */
const slashPrograms: ReadonlySet<string> = new Set('cmd robocopy xcopy findstr where attrib icacls'.split(' '));
/** How deep the walk reads a text inside a text; deeper, a text is taken to walk where the command names such a folder. */
const walkDepth = 6;

/** What a reading of a command's words takes: the judgement's scope, and whether the words are PowerShell's. */
interface ShellReading {
    scope: ChatScope;
    powershell: boolean;
}

/** What the walk carries through a command: the judgement's scope, the shell, and what the whole command's text says. */
interface WalkReading extends ShellReading {
    /** The command could make a program of the plain forms another one (`redefines`). */
    redefined: boolean;
    /** Some word of the command names a folder that holds the data directory, or may (`wordNamesRoot`). */
    named: boolean;
}

/**
 * Where the shell may run while the walk reads a command: every folder it may be in now, every one it may have been in,
 * and whether a change led where the walk could not follow.
 */
interface WalkPlace {
    folders: string[];
    seen: string[];
    unknown: boolean;
}

/** True when a located place is a folder that holds the data directory, read where it leads (`hookPlace`). */
function isRoot(place: string, scope: ChatScope): boolean {
    const lead = hookPlace(place, scope);
    return typeof lead === 'string' && !network(scope.platform, lead) && holdsFenced(scope, lead);
}

/** The folders a word's path is fixed to before the first part the shell builds, with the separator after them. */
function fixedPart(value: string, platform: Platform): string | null {
    const at = value.search(/[*?[{$`%(]/);
    if (at < 0) return null;
    const head = value.slice(0, at);
    const cut = Math.max(head.lastIndexOf('/'), platform === 'win32' ? head.lastIndexOf('\\') : -1);
    return cut < 0 ? '' : head.slice(0, cut + 1);
}

/**
 * True when a word the shell builds while it runs may name a folder that holds the data directory: a `~name`, which is
 * a home folder the walk does not read, and a word whose folders before its first pattern, variable or substitution are
 * such a folder, its home spelling taken (`homeExpanded`), read from the folder it is read in.
 */
function builtRoot(word: string, scope: ChatScope): boolean {
    const value = nativeSpelling(homeExpanded(word, scope), scope);
    if (/^~[^\\/]/.test(value)) return true;
    const fixed = fixedPart(value, scope.platform);
    return fixed !== null && fixed !== '' && isRoot(located(fixed, scope), scope);
}

/**
 * True when a word names a folder that holds the data directory (`isRoot`), read from `cwd`: its places as `placesOfWords`
 * reads them, in PowerShell a parameter's value joined by a colon (`-Path:~`) among them, or, read as written, a word the
 * shell builds that may name one (`builtRoot`).
 */
function wordNamesRoot(text: string, cwd: string, reading: WalkReading, literal: boolean): boolean {
    const at = { ...reading.scope, cwd };
    const joined = reading.powershell ? /^-[A-Za-z][\w-]*:(.+)$/.exec(text) : null;
    const words = joined === null ? [text] : [text, joined[1] as string];
    if (placesOfWords(words, at, literal).some((place) => isRoot(place, reading.scope))) return true;
    return !literal && words.some((word) => word.split(/[=@,]/).some((piece) => builtRoot(piece, at)));
}

/** True when a word the walk reads is one the shell builds and no home spelling: a variable, a substitution, an expression. */
function unknownWord(word: WrittenWord, scope: ChatScope): boolean {
    return word.built && /[$`(]/.test(homeExpanded(word.text, scope));
}

/** A command as the walk reads it: its words, their texts, its program and the words after it. */
interface ReadCommand {
    command: WrittenCommand;
    assigned: boolean;
    word: string | undefined;
    args: WrittenWord[];
    program: string | null;
}

/** The commands of a text, each with its program, and whether every program of them takes text only (`takesTextOnly`). */
function readCommands(text: string, reading: ShellReading): { commands: ReadCommand[]; pure: boolean } {
    const commands = writtenCommands(text, reading.powershell).map((command) => {
        const texts = command.words.map((each) => each.text);
        const { at, assigned } = programIndex(texts, reading.powershell);
        const word = texts[at];
        return { command, assigned, word, args: command.words.slice(at + 1), program: word === undefined ? null : programKey(reading.scope.platform, word, reading.powershell) };
    });
    // A text whose every program takes text only runs none of its words, so a quoted word or a body is data there; where
    // any other program reads it, a shell or an interpreter among them, it may be run.
    return { commands, pure: commands.every(({ program }) => program === null || takesTextOnly(program, reading.powershell)) };
}

/**
 * The texts the walk reads again as commands of their own: the substitutions and expressions a command holds; where a
 * program that does not take text only reads the text, every word that holds a space or a separator and every body of a
 * here-document, and `eval`'s words as one command; and otherwise a body's substitutions where its tag is not quoted.
 * `pure` false reads every one of them again, whatever reads the text.
 */
function readAgain(each: ReadCommand, pure: boolean, powershell: boolean): string[] {
    const text = pure && each.program !== null && takesTextOnly(each.program, powershell);
    return [
        ...each.command.inner,
        ...(text ? [] : each.command.words.filter((word) => word.spaced).map((word) => word.text)),
        ...(each.program === 'eval' ? [each.args.map((arg) => arg.text).join(' ')] : []),
        ...each.command.bodies.flatMap((body) => (!text ? [body.text] : body.quoted ? [] : substitutions(body.text, powershell))),
    ];
}

/**
 * True when some word of a text names such a folder, read as `writtenCommands` reads it and again wherever any of it may
 * be run, data or not (`readAgain`), each from the folder the turn runs in; a folder change with no target names the
 * home folder. A short slash option of a program that takes them (`slashPrograms`) is no folder.
 */
function namesAnyRoot(text: string, reading: WalkReading, depth: number): boolean {
    if (depth > walkDepth) return false;
    const { scope, powershell } = reading;
    const cwd = located(scope.cwd, scope);
    const { commands } = readCommands(text, reading);
    return commands.some((each) => {
        const slashes = scope.platform === 'win32' && each.program !== null && slashPrograms.has(each.program);
        const options = new Set(slashes ? each.args.filter((arg) => optionWord(arg.text, scope.platform)) : []);
        if (each.command.words.some((word) => !options.has(word) && wordNamesRoot(word.text, cwd, reading, false))) return true;
        const change = each.program !== null && (powershell ? powerShellChanges : posixChanges).has(each.program);
        if (change && each.args.every((arg) => arg.text.startsWith('-')) && isRoot(located(scope.home, scope), scope)) return true;
        return readAgain(each, false, powershell).some((inner) => namesAnyRoot(inner, reading, depth + 1));
    });
}

/**
 * Where a folder change of the shell's own leads: its target, read as `placesOfWords` reads it from each folder the shell
 * may be in; with none, the home folder, and `-`, `+`, `popd` and `Pop-Location` any folder it has been in. A target the
 * shell builds while it runs leads below the folders it is fixed to (`fixedPart`), and where it leads is not followed.
 */
function changedPlace(program: string, args: readonly string[], place: WalkPlace, reading: WalkReading): WalkPlace {
    const { scope, powershell } = reading;
    const runner = changeRunners.has(program) && args[0] !== undefined;
    const name = runner ? programKey(scope.platform, args[0] as string, powershell) : program;
    const words = runner ? args.slice(1) : args;
    const back = (): WalkPlace => ({ ...place, folders: [...place.seen] });
    if (goesBack.has(name)) return back();
    if (!(powershell ? powerShellChanges : posixChanges).has(name)) return place;
    let target: string | undefined;
    for (let at = 0; at < words.length; at++) {
        const word = words[at] as string;
        const folded = word.toLowerCase();
        if (powershell && folded.startsWith('-')) {
            const [parameter = '', joined] = folded.split(/:(.*)/s);
            const named = (full: string, least: number) => parameter.length >= least && full.startsWith(parameter);
            if (named('-path', 4) || named('-literalpath', 2) || named('-pspath', 3) || parameter === '-lp') {
                target = joined !== undefined ? word.slice(word.indexOf(':') + 1) : words[at + 1];
                break;
            }
            if (named('-stackname', 2) && joined === undefined) at++;
            continue;
        }
        if (word === '-' || word === '+') return back();
        if (word === '--') {
            target = words[at + 1];
            break;
        }
        if (word.startsWith('-')) continue;
        target = word;
        break;
    }
    const moved = (folders: string[], unknown: boolean): WalkPlace => ({ folders, seen: [...place.seen, ...folders], unknown });
    if (target === undefined) return name === 'pushd' || name === 'push-location' ? back() : moved([located(scope.home, scope)], false);
    const value = nativeSpelling(homeExpanded(target, scope), scope);
    const fixed = /^~/.test(value) ? '' : fixedPart(value, scope.platform);
    if (fixed !== null) return moved(fixed === '' ? place.folders : place.folders.map((folder) => located(fixed, { ...scope, cwd: folder })), true);
    const absolute = pathsOf(scope.platform).isAbsolute(value);
    return moved(place.folders.map((folder) => located(value, { ...scope, cwd: folder })), place.unknown && !absolute);
}

/**
 * True when a command walks a folder that holds the data directory: it names one as a word, read from each folder the
 * shell may be in, or runs in one; or, where some word of the whole command names one, it runs where a change led that
 * the walk did not follow, takes a word the shell builds, takes words from its standard input (`inputRunners`), runs
 * another program (`runsAnotherProgram`), a shell that reads a pipe among them, or in PowerShell is a command a pipe feeds
 * paths to that does not only print them (`powerShellPrints`).
 */
function walksThere(each: ReadCommand, place: WalkPlace, reading: WalkReading): boolean {
    const { scope, powershell } = reading;
    const program = each.program ?? '';
    const slashes = scope.platform === 'win32' && slashPrograms.has(program);
    const named = each.args.some((arg) => !(slashes && optionWord(arg.text, scope.platform)) && place.folders.some((folder) => wordNamesRoot(arg.text, folder, reading, false)));
    if (named || place.folders.some((folder) => isRoot(folder, scope))) return true;
    if (!reading.named) return false;
    return (
        place.unknown
        || inputRunners.has(program)
        || runsAnotherProgram(program)
        || each.args.some((arg) => unknownWord(arg, scope))
        || (powershell && each.command.piped && !powerShellPrints.has(program))
    );
}

/**
 * The walk of the commands a text holds (`readCommands`), from where the shell may run on: true when one of them walks a
 * folder that holds the data directory (`walksThere`) in any form but the plain ones the chat knows do not descend
 * (`plainWalkForm`): an assignment before the program, a program named by a path and a command that could redefine a
 * program (`redefines`) have none. What may run of a command is read again, as commands of their own, from the same place
 * (`readAgain`). A folder change of the shell's own (`changedPlace`) sets where the commands after it run, whatever joins
 * them, which reads more than runs where a group or a pipe keeps the change to itself; `eval` keeps the change its words
 * make. Read deeper than `walkDepth`, a text walks where the command names such a folder.
 */
function walkedFrom(text: string, start: WalkPlace, reading: WalkReading, depth: number): { walks: boolean; place: WalkPlace } {
    const { scope, powershell } = reading;
    if (depth > walkDepth) return { walks: reading.named || start.unknown || start.folders.some((folder) => isRoot(folder, scope)), place: start };
    const { commands, pure } = readCommands(text, reading);
    let place = start;
    for (const each of commands) {
        if (each.word !== undefined && each.program !== null) {
            const args = each.args.map((arg) => arg.text);
            const unknown = (at: number) => unknownWord(each.args[at] as WrittenWord, scope);
            const plain = !reading.redefined && !each.assigned && !/[\\/]/.test(each.word) && plainWalkForm(each.program, args, powershell, unknown);
            if (!plain && walksThere(each, place, reading)) return { walks: true, place };
        }
        for (const inner of readAgain(each, pure, powershell)) {
            const read = walkedFrom(inner, place, reading, depth + 1);
            if (read.walks) return { walks: true, place };
            if (each.program === 'eval') place = read.place;
        }
        if (each.program !== null) place = changedPlace(each.program, each.args.map((arg) => arg.text), place, reading);
    }
    return { walks: false, place };
}

/** A word that is an option and no folder: one that begins with a dash, or on Windows a short slash option (`/s`). */
function optionWord(word: string, platform: Platform): boolean {
    return word.startsWith('-') || (platform === 'win32' && /^\/[A-Za-z?]{1,4}(:.*)?$/.test(word));
}

/**
 * Where the hook rides the turn, true when a command may walk a folder that holds the installation's data
 * (`holdsFenced`): the home folder, a drive's top, the data directory's parents, and in a demo run the person's own
 * directories'. With no read blocked such a walk would read the data without naming it, so a command that names such a
 * folder, or runs in one, passes only in a form the chat knows does not descend (`plainWalkForm`): a plain listing, a
 * search of what it names alone, a print, a folder change. The commands are read as their shell splits them
 * (`walkedFrom`), and again from the literal reading, each from the folder a literal directory change sets for it.
 */
function walksRoot(tool: string, command: string, scope: ChatScope): boolean {
    const powershell = tool === 'PowerShell';
    const base = { scope, powershell, redefined: redefines(command, powershell), named: false };
    const reading: WalkReading = { ...base, named: namesAnyRoot(command, base, 0) };
    const start = located(scope.cwd, scope);
    if (walkedFrom(command, { folders: [start], seen: [start], unknown: false }, reading, 0).walks) return true;
    const literal = readLiteral(tool, command, { platform: scope.platform, home: shellHomeOf(scope), relativeDirectories: true });
    if (literal.held) return false;
    return literal.commands.some(({ words, cwd }) => {
        const at = cwd === null ? start : located(nativeSpelling(cwd, scope), scope);
        const [word = '', ...args] = words;
        const program = programKey(scope.platform, word, powershell);
        if (!reading.redefined && !/[\\/]/.test(word) && plainWalkForm(program, args, powershell, () => false)) return false;
        const slashes = scope.platform === 'win32' && slashPrograms.has(program);
        return isRoot(at, scope) || args.some((arg) => !(slashes && optionWord(arg, scope.platform)) && wordNamesRoot(arg, at, reading, true));
    });
}

/** A word that names jq's environment, or reads a module or a data file by jq's own words. */
const jqReads = /(?<![A-Za-z0-9_$])(?:env|import|include|modulemeta)(?![A-Za-z0-9_])|\$ENV(?![A-Za-z0-9_])/;

/** The programs whose `--files0-from` reads the names of the files they read from their input or another file. */
const namesFromInput: ReadonlySet<string> = new Set(['sort', 'wc', 'du']);

/** True when a word is `--files0-from` or a prefix of it GNU's programs take for it, alone or with its value. */
function filesFromInput(word: string): boolean {
    const name = (word.split('=')[0] ?? '').toLowerCase();
    return name.length >= 3 && '--files0-from'.startsWith(name);
}

/**
 * The words of every command a text holds as the walk reads them (`readCommands`), and of what may run of each, read
 * again (`readAgain`): a reading for refusals, which reads more than runs. Read deeper than `walkDepth`, a text's words
 * are read as one command a runner starts, `env` (`runsAnother`), so that each of them is looked at as a program.
 */
function everyCommand(text: string, reading: ShellReading, depth: number): WrittenWord[][] {
    if (depth > walkDepth) {
        return [['env', ...text.split(/[\s'"`;|&()<>]+/)].filter((word) => word !== '').map((word) => ({ text: word, spaced: false, built: false, pattern: false }))];
    }
    const { commands } = readCommands(text, reading);
    return commands.flatMap((each) => [each.command.words, ...readAgain(each, false, reading.powershell).flatMap((inner) => everyCommand(inner, reading, depth + 1))]);
}

/**
 * Where the hook rides the turn, the refusal of a command that reads what no word of it shows: a jq program that reads
 * the environment or a file by its own words, `env`, `$ENV`, `import`, `include` or `modulemeta`, takes its program from a
 * file, or is given a word the shell builds (`jqRefusal`); and `sort`, `wc` or `du` told the files they read by
 * `--files0-from` or a prefix of it, and `find` by `-files0-from` (`filesFromRefusal`). A program is looked for where a
 * command's program stands and at every word after one that runs another (`runsAnotherProgram`), in every command as the
 * walk reads them (`everyCommand`) and in the literal reading. Null for anything else.
 */
function unseenReadRefusal(tool: string, command: string, scope: ChatScope): string | null {
    const powershell = tool === 'PowerShell';
    const commands = everyCommand(command, { scope, powershell }, 0);
    const literal = readLiteral(tool, command, { platform: scope.platform, home: shellHomeOf(scope) });
    if (!literal.held) commands.push(...literal.commands.map(({ words }) => words.map((text) => ({ text, spaced: false, built: false, pattern: false }))));
    for (const words of commands) {
        const start = programIndex(words.map((word) => word.text), powershell).at;
        let runner = false;
        for (let at = start; at < words.length; at++) {
            if (at !== start && !runner) continue;
            const program = programOf(scope.platform, (words[at] as WrittenWord).text).toLowerCase();
            const rest = words.slice(at + 1);
            if (program === 'jq' && rest.some(({ text, built, pattern }) => built || pattern || jqReads.test(text) || text === '--from-file' || text.startsWith('--from-file=') || /^-[A-Za-z]*f/.test(text))) return jqRefusal;
            if (namesFromInput.has(program) && rest.some(({ text }) => filesFromInput(text))) return filesFromRefusal;
            if (program === 'find' && rest.some(({ text }) => text === '-files0-from')) return filesFromRefusal;
            if (runsAnotherProgram(program)) runner = true;
        }
    }
    return null;
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
 * The CLI's confining variable, which a Plan only turn's environment carries (`hostEnvironment`): under it the jason the
 * turn runs refuses the verbs of a person's word and of the installation, and holds its file half as the chat's variable
 * does. No turn in Ask first, Auto-edit or Full access sets it.
 */
export const confinedVariable = 'JASON_CONFINED';

/**
 * The chat's own variable, which every turn's environment carries, in every mode and on every host (`hostEnvironment`):
 * under it an option of the jason the turn runs that reads a file into a request takes standard input only, and the
 * provider check runs a bare name only. It refuses no verb.
 */
export const chatTurnVariable = 'JASON_CHAT_TURN';

/**
 * Jason's own variables of a turn. A command that names one could clear or set it for the jason it runs, so it is refused
 * by its name as a whole word, in any letter case on every platform, whatever wraps it; the host's own deny rules
 * (`confinedRule`, `chatTurnRule`) refuse the exact spellings first. A longer name that only begins or ends like one is
 * not it. A spelling that builds the name, as `JASON_CONF''INED`, `${!JASON_CONF@}` or `env -i`, is not refused by this
 * check and reaches a card; what holds then is the CLI's own rule.
 */
const ownVariables = [confinedVariable, chatTurnVariable] as const;

function namesConfinedVariable(command: string): boolean {
    return ownVariables.some((name) => new RegExp(`(?<![A-Za-z0-9_])${name}(?![A-Za-z0-9_])`, 'i').test(command));
}

/**
 * The variable a role's attempt is launched with, naming the attempt the jason it runs works inside; the CLI sends it
 * with every request, and the runtime takes the request as that attempt's. The chat is never an attempt, so a command
 * that names it could only claim one: it is refused by its name as a whole word, in any letter case on every platform,
 * whatever wraps it, as the confining variable is, and the host's own deny rule (`attemptRule`) refuses the exact
 * spelling first. A longer name that only begins or ends like it is not it. A spelling that builds the name is not
 * refused by this check and reaches a card.
 */
export const attemptVariable = 'JASON_ATTEMPT_ID';

function namesAttemptVariable(command: string): boolean {
    return new RegExp(`(?<![A-Za-z0-9_])${attemptVariable}(?![A-Za-z0-9_])`, 'i').test(command);
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
 * spelling a shell takes: the native path, its forward-slash form, Git Bash's and the host rules' `/c/...` on Windows and
 * this computer's share of the drive (`\\localhost\C$\...`, `thisComputerNames`), the long-path forms holding the native
 * one, and `~/.jason`, `$HOME/.jason` or `.jason` under the home folder's variable in any shell's spelling (`$USERPROFILE`
 * among them, since Git Bash is the Windows shell) when the data directory is the default one, or in a demo run when it
 * is any folder directly in the home folder. A name that only begins like it is not it.
 */
function namesData(command: string, scope: ChatScope): boolean {
    return namesFolder(command, scope, scope.data, scope.chat, personsOwnOf(scope).length > 0 ? null : '.jason');
}

/**
 * True when the command's text names one of the person's own directories (`ChatScope.personsOwn`), or anything in it, in
 * the spellings `namesData` reads, the home folder's spellings for one directly in the home folder. A name that only
 * begins like it is not it: `~/.jason` never names `~/.jason-demo`, nor `~/Jason` `~/Jason-demo`.
 */
function namesPersonsOwn(command: string, scope: ChatScope): boolean {
    return personsOwnOf(scope).some((folder) => namesFolder(command, scope, folder, null, null));
}

/**
 * True when the command's text names a folder, or anything in it outside the one folder it excepts, in any literal
 * spelling a shell takes; the home folder's spellings when the folder is directly in the home folder and, where a name is
 * given, has that name.
 */
function namesFolder(command: string, scope: ChatScope, folder: string, except: string | null, homeName: string | null): boolean {
    const paths = pathsOf(scope.platform);
    const text = pathText(scope.platform, command);
    const named = pathText(scope.platform, paths.resolve(folder)).replace(/\/$/, '');
    const excepted = except === null ? null : pathText(scope.platform, paths.resolve(except));
    const exceptRest = excepted !== null && excepted.startsWith(`${named}/`) ? excepted.slice(named.length) : null;
    const spellings = [named];
    const drive = /^([a-z]):\//i.exec(named);
    if (drive) spellings.push(`/${drive[1]}${named.slice(2)}`);
    // On Windows, this computer named as another computer would, through the drive's share (`hookPlace`).
    if (drive && scope.platform === 'win32') for (const computer of thisComputerNames(scope)) spellings.push(`/${computer}/${drive[1]}$${named.slice(2)}`);
    const home = pathText(scope.platform, paths.resolve(scope.home)).replace(/\/$/, '');
    const name = named.startsWith(`${home}/`) ? named.slice(home.length + 1) : null;
    if (name !== null && name !== '' && !name.includes('/') && (homeName === null || name === keyOf(scope.platform, homeName))) {
        for (const variable of ['~', '$HOME', '${HOME}', '%USERPROFILE%', '$env:USERPROFILE', '$USERPROFILE', '${USERPROFILE}']) spellings.push(pathText(scope.platform, `${variable}/${name}`));
    }
    for (const spelling of spellings) {
        for (let at = text.indexOf(spelling); at >= 0; at = text.indexOf(spelling, at + 1)) {
            const rest = text.slice(at + spelling.length);
            if (/^[A-Za-z0-9._-]/.test(rest)) continue;
            const under = posix.normalize(`/${/^[^\s"'`;|&<>()]*/.exec(rest)?.[0] ?? ''}`);
            if (exceptRest !== null && (under === exceptRest || under.startsWith(`${exceptRest}/`))) continue;
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
 * as a one-dash option's joined value (`-T../x`). A piece that begins with a home spelling is the home folder's
 * (`homeExpanded`); any other piece the shell would expand first (a variable, `~user`) is left to the variable's name and
 * the text's spellings.
 */
function placesOf(command: string, scope: ChatScope): string[] {
    const links = command
        .split(/&&|\|\||[;|&\n\r()`]/)
        .flatMap((part) => commandPlaces(part.trim().split(/\s+/).map(unquoted).filter((word) => word !== ''), scope, false));
    return [...placesOfWords(command.split(/[\s;|&<>()`]+/).map(unquoted), scope, false), ...links];
}

/**
 * The places a link a command makes may lead to (`linkOperands`): each of its words located from the folder the link is
 * made in as well as from the command's own, since a link's relative target is read from the link's own folder, not from
 * the folder the command runs in. Every word is taken for a target, and every word and its parent for the folder the
 * link may be made in: more places than the link names, which only adds refusals.
 */
function commandPlaces(words: readonly string[], scope: ChatScope, literal: boolean): string[] {
    return [...linkPlaces(words, scope, literal), ...directoryPlaces(words, scope, literal)];
}

/** The programs whose own option names the folder the words after it are read from: `-C` and `--directory`. */
const directoryPrograms: ReadonlySet<string> = new Set(['tar', 'bsdtar', 'gtar', 'git', 'make', 'gmake', 'ninja']);

/**
 * The places a program's words name when its own option sets the folder they are read from (`tar -C`, `git -C`,
 * `make --directory=`): every word after the program located from each such folder, as words after a literal `cd` are.
 */
function directoryPlaces(words: readonly string[], scope: ChatScope, literal: boolean): string[] {
    const at = words.findIndex((word) => directoryPrograms.has(programName(word)));
    if (at < 0) return [];
    const rest = words.slice(at + 1);
    const folders: string[] = [];
    rest.forEach((word, index) => {
        const next = rest[index + 1];
        if ((word === '--directory' || /^-[A-Za-z]*C$/.test(word)) && next !== undefined) folders.push(next);
        else if (word.startsWith('--directory=')) folders.push(word.slice('--directory='.length));
        else if (/^-C./.test(word)) folders.push(word.slice(2));
    });
    return folders
        .map((folder) => nativeSpelling(folder, scope))
        .filter((folder) => folder !== '' && folder.length <= pathLimit(scope.platform) && (literal || !/^~[^\\/]|[$%]/.test(homeExpanded(folder, scope))))
        .flatMap((folder) => {
            const cwd = located(folder, scope);
            return network(scope.platform, cwd) ? [] : placesOfWords(rest, { ...scope, cwd }, literal);
        });
}

function linkPlaces(words: readonly string[], scope: ChatScope, literal: boolean): string[] {
    const operands = (linkOperands(words) ?? [])
        .map((word) => nativeSpelling(word, scope))
        .filter((word) => word.length <= pathLimit(scope.platform) && (literal || !/^~|[$%]/.test(homeExpanded(word, scope))));
    const paths = pathsOf(scope.platform);
    const folders = new Set<string>();
    for (const word of operands) {
        const at = located(word, scope);
        if (network(scope.platform, at)) continue;
        folders.add(at);
        folders.add(paths.dirname(at));
    }
    return [...folders].flatMap((folder) => operands.map((word) => located(word, { ...scope, cwd: folder })));
}

/**
 * The places words name, located as `placesOf` locates them. Resolved words (`literal`) are what the shell passes on, so
 * none of them is left to a variable's name or a `~`.
 */
function placesOfWords(words: readonly string[], scope: ChatScope, literal: boolean): string[] {
    const places: string[] = [];
    for (const word of words) {
        const pieces = new Set([word, ...word.split(/[=@,]/)]);
        for (const piece of [...pieces]) if (/^-[A-Za-z]/.test(piece)) pieces.add(piece.slice(2));
        for (const value of pieces) {
            if (value === '' || value.length > pathLimit(scope.platform)) continue;
            if (!literal && /^~|[$%]/.test(homeExpanded(value, scope))) {
                // A path names its host before anything the shell expands after it: such a word is that host's, whatever
                // the rest becomes.
                const path = spelled(nativeSpelling(value, scope), scope);
                if (network(scope.platform, path) && !/[$%`~]/.test(computerOf(scope.platform, path))) places.push(path);
                continue;
            }
            places.push(located(nativeSpelling(value, scope), scope));
        }
    }
    return places;
}

/** A path word as the platform spells it: Git Bash's /c/... is the drive's own path. */
function nativeSpelling(value: string, scope: ChatScope): string {
    const drive = scope.platform === 'win32' ? /^[\\/]+([a-z])(?=[\\/]|$)/i.exec(value) : null;
    return drive ? `${drive[1]}:${value.slice(drive[0].length) || '\\'}` : value;
}

/** True when a place the command's words name is in the data directory outside the chat folder. */
function wordNamesData(places: readonly string[], scope: ChatScope): boolean {
    const data = located(scope.data, scope);
    const chat = located(scope.chat, scope);
    return places.some((path) => inside(scope, data, path) && !inside(scope, chat, path));
}

/**
 * The places a command's words name as the shell passes them on: each command of the literal reading (`readLiteral`),
 * its quoted words as the shell reads them, read from the folder a literal directory change, absolute or relative, sets
 * for it. None when the command is not read literally. A reading for refusals only, so that Ask first refuses what Full
 * access's reading refuses.
 */
function literalPlaces(tool: string, command: string, scope: ChatScope): string[] {
    const reading = readLiteral(tool, command, { platform: scope.platform, home: shellHomeOf(scope), relativeDirectories: true });
    if (reading.held) return [];
    return reading.commands.flatMap((each) => {
        const at = each.cwd === null ? scope : { ...scope, cwd: located(nativeSpelling(each.cwd, scope), scope) };
        return [...placesOfWords([...each.words, ...each.targets], at, true), ...commandPlaces(each.words, at, true)];
    });
}

/**
 * The refusal of a place a command's words name on another computer, or reaching this one as another computer would, or
 * in a device's namespace (`remoteRefusal`); null when none does.
 */
function wordNamesHost(places: readonly string[], scope: ChatScope): string | null {
    for (const path of places) {
        const refused = remoteRefusal(scope.platform, path);
        if (refused !== null) return refused;
    }
    return null;
}

/** True when a place the command's words name is in one of the person's own directories (`ChatScope.personsOwn`). */
function wordNamesPersonsOwn(places: readonly string[], scope: ChatScope): boolean {
    return places.some((path) => inPersonsOwn(scope, path));
}

/** The programs whose `mcp` command adds, removes or lists the MCP servers that host loads: Claude Code and Codex. */
const serverHosts: ReadonlySet<string> = new Set(['claude', 'claude-code', 'codex']);

/**
 * True when a command part, as written or as a shell would most likely read it (`readingsOf`), runs Claude Code or Codex,
 * by any folder or package name, and holds the word `mcp` after it, whatever options stand between: the command that
 * adds a server to the person's own configuration, which a later turn of theirs may load. A part that only names the
 * word in a text is a card too: the card shows what it is.
 */
function changesServers(command: string, scope: ChatScope): boolean {
    for (const text of new Set([command, ...readingsOf(command)])) {
        for (const part of text.split(/&&|\|\||\$\(|[;|&`\n\r<>(){}]/)) {
            const tokens = part.trim().split(/\s+/).map(unquoted).filter((token) => token !== '');
            const at = tokens.findIndex((token) => serverHosts.has(programOf(scope.platform, token).toLowerCase()));
            if (at >= 0 && tokens.slice(at + 1).some((token) => token.toLowerCase() === 'mcp')) return true;
        }
    }
    return false;
}

function judgeCommand(tool: string, command: string, scope: ChatScope): Judgement {
    if (namesConfinedVariable(command)) return { verdict: 'deny', message: confinedRefusal };
    if (namesAttemptVariable(command)) return { verdict: 'deny', message: attemptRefusal };
    if (changesSearch(command)) return { verdict: 'deny', message: lookupRefusal };
    const places = [...placesOf(command, scope), ...literalPlaces(tool, command, scope)];
    const remote = wordNamesHost(places, scope);
    if (remote !== null) return { verdict: 'deny', message: remote };
    if (namesPersonsOwn(command, scope) || wordNamesPersonsOwn(places, scope)) return { verdict: 'deny', message: personsRefusal };
    if (places.some((path) => isProtected(scope, path))) return { verdict: 'deny', message: settingsRefusal };
    if (namesDataVariable(command, scope) || namesData(command, scope) || wordNamesData(places, scope)) return { verdict: 'deny', message: dataRefusal };
    const jason = jasonIn(command, scope);
    const jasonRefused = jasonRefusal(jason);
    if (jasonRefused !== null) return { verdict: 'deny', message: jasonRefused };
    const respelled = respelledRefusal(command, scope);
    if (respelled !== null) return { verdict: 'deny', message: respelled };
    const own = chatsOwnPlace(places, scope);
    if (own !== null) return { verdict: 'deny', message: own };
    if (replyRefused(command, scope)) return { verdict: 'deny', message: replyRefusal };
    // Where the hook rides the turn no read is blocked: a command that reads what no word of it shows is refused, as the
    // hook refuses it (`unseenReadRefusal`), and one that may walk a folder that holds the data directory without naming
    // it, which the hook refuses too, is the person's to answer each time, never covered by a grant (`walksRoot`).
    if (scope.hooked === true) {
        let unseen: string | null;
        let walks: boolean;
        try {
            unseen = unseenReadRefusal(tool, command, scope);
            walks = walksRoot(tool, command, scope);
        } catch {
            unseen = unreadRefusal;
            walks = false;
        }
        if (unseen !== null) return { verdict: 'deny', message: unseen };
        if (walks) return command.length > detailLimit ? { verdict: 'deny', message: longRefusal } : { verdict: 'ask', title: 'Run a command', detail: shorten(command), remember: null };
    }
    // A plain form of the host's own allow rules runs with no card, as the host's rule would run it (`plainForm`), and so
    // does what runs with no card where the hook rides the turn (`hookedNoCard`): no card shows either, so neither is held
    // to a card's length.
    if (jason === 'jason' && plainForm(tool, command, places, scope)) return allow;
    if (hookedNoCard(tool, command, places, scope)) return allow;
    // After every refusal and before any card: a card cuts its detail at the limit, so Allow would run what nobody saw.
    // A grant is looked at only for a card, so none allows it either.
    if (command.length > detailLimit) return { verdict: 'deny', message: longRefusal };
    if (changesServers(command, scope)) return { verdict: 'ask', title: serversTitle, detail: shorten(command), remember: null };
    // A word under a guarded name in a working folder: that guard's card, never covered by a grant.
    // A command that names a search variable otherwise, a word under a guarded name, or a jason of the agent's own: each
    // is its own card, never covered by a grant.
    if (namesSearch(command)) return { verdict: 'ask', title: searchTitle, detail: shorten(command), remember: null };
    const guarded = guardedPlace(places, scope);
    if (guarded !== undefined) return { verdict: 'ask', title: guarded.title, detail: shorten(command), remember: null };
    if (namesLocalJason(command, scope)) return { verdict: 'ask', title: jasonFileTitle, detail: shorten(command), remember: null };
    // A noun's help, exactly as the host's help rule names it, runs here too: it prints and changes nothing.
    if (isNounHelp(tool, command, scope)) return allow;
    // A jason command in no plain form of the allow list is the person's to answer, each time; where the hook rides the
    // turn that is a form of `askingCardedForms`, or a jason with something that is no filter of `noCardFilters` as it
    // runs with no card beside it, a directory change or a redirection into a file.
    if (jason === 'jason') return { verdict: 'ask', title: 'Run a command', detail: shorten(command), remember: null };
    // Only the Bash tool's commands are fenced by the host and may be allowed or remembered; any other tool's command is
    // the person's to answer, each time.
    if (tool !== 'Bash') return { verdict: 'ask', title: 'Run a command', detail: shorten(command), remember: null };
    if (!isCompound(command) && replyNoCardForms.includes(command.trim().split(/\s+/).join(' '))) return allow;
    // A command that names a folder holding the data directory, or in a demo run one of the person's own directories, may
    // walk it: a card each time, never covered by a grant.
    const prefix = places.some((path) => holdsFenced(scope, path)) ? null : commandPrefix(command);
    if (prefix === null) return { verdict: 'ask', title: 'Run a command', detail: shorten(command), remember: null };
    return { verdict: 'ask', title: 'Run a command', detail: shorten(command), remember: `${tool}:${prefix}`, covers: `${prefix} \u2026` };
}

/**
 * The text a writer tool would put in a file: a whole file's content, or each replacement of an edit, and for an edit
 * the whole file as it would read after it, where the file can be read (`ChatScope.text`): a replacement can complete a
 * word the file already begins.
 */
function writtenText(tool: string, input: Record<string, unknown>, scope: ChatScope): string[] {
    const texts: unknown[] = [];
    if (tool === 'Write') texts.push(input['content']);
    if (tool === 'Edit') texts.push(input['new_string']);
    if (tool === 'NotebookEdit') texts.push(input['new_source']);
    const edits = tool === 'Edit' ? [input] : tool === 'MultiEdit' && Array.isArray(input['edits']) ? (input['edits'] as unknown[]) : [];
    if (tool === 'MultiEdit') for (const edit of edits) texts.push((edit as { new_string?: unknown } | null)?.new_string);
    const path = input['file_path'];
    if (edits.length > 0 && typeof path === 'string' && scope.text !== undefined) {
        const before = scope.text(located(path, scope));
        const after = before === null ? null : edited(before, edits);
        if (after !== null) texts.push(after);
    }
    return texts.filter((text): text is string => typeof text === 'string');
}

/** A file's text after its edits, applied in order as the writer tool applies them; null where one does not apply. */
function edited(text: string, edits: readonly unknown[]): string | null {
    let after = text;
    for (const edit of edits) {
        const { old_string: from, new_string: to, replace_all: all } = (edit ?? {}) as { old_string?: unknown; new_string?: unknown; replace_all?: unknown };
        if (typeof from !== 'string' || typeof to !== 'string' || from === '') return null;
        const at = after.indexOf(from);
        if (at < 0) return null;
        after = all === true ? after.split(from).join(to) : `${after.slice(0, at)}${to}${after.slice(at + from.length)}`;
    }
    return after;
}

/**
 * The kinds a script the chat writes would show as a command (`kindOver`): its lines read as a shell reads them,
 * the script whole and each line alone, from the file's own folder, a literal `cd` in it setting the folder for what
 * follows, so a relative path and a directory change are located as a command's are. Null when none.
 */
function scriptRefusal(kind: ScriptKind, text: string, path: string, scope: ChatScope): string | null {
    const at = { ...scope, cwd: pathsOf(scope.platform).dirname(located(path, scope)) };
    const tool = /\.(ps1|psm1|psd1)$/i.test(path) ? 'PowerShell' : 'Bash';
    for (const commands of scriptCommands(kind, text)) {
        const lines = commands.split(/\r?\n/).map((line) => (kind === 'make' ? line.replace(/^\s+/, '').replace(/^[@+-]+/, '') : line)).filter((line) => line.trim() !== '');
        const whole = lines.join('\n');
        // A script's lines run one after another in the folder the lines before them left, as commands joined by `&&` do.
        const chained = lines.join(' && ');
        const places = [...placesOf(whole, at), ...literalPlaces(tool, whole, at), ...literalPlaces(tool, chained, at), ...lines.flatMap((line) => literalPlaces(tool, line, at))];
        const refused = kindOver(whole, places, at);
        if (refused !== null) return refused;
    }
    return null;
}

/** What a written file is run as, by its name or its first line: a shell's or PowerShell's script, a makefile, a package's scripts, a notebook. */
type ScriptKind = 'shell' | 'make' | 'package' | 'notebook';

const scriptExtension = /\.(sh|bash|zsh|ksh|dash|fish|csh|tcsh|command|tool|ps1|psm1|psd1|bat|cmd|btm)$/;
const makeNames: ReadonlySet<string> = new Set(['makefile', 'gnumakefile', 'bsdmakefile', 'justfile']);
const shellStartupNames: ReadonlySet<string> = new Set(['.bashrc', '.bash_profile', '.bash_login', '.profile', '.zshrc', '.zprofile', '.zshenv', '.zlogin', '.kshrc', '.envrc']);

function scriptKind(tool: string, path: string, text: string): ScriptKind | null {
    const parts = path.toLowerCase().split(/[\\/]+/);
    const name = parts.at(-1) ?? '';
    if (tool === 'NotebookEdit' || name.endsWith('.ipynb')) return 'notebook';
    if (name === 'package.json') return 'package';
    if (makeNames.has(name) || /\.(mk|mak|make)$/.test(name)) return 'make';
    const hooks = (parts.at(-2) === 'hooks' && parts.at(-3) === '.git') || parts.at(-2) === '.husky';
    return scriptExtension.test(name) || shellStartupNames.has(name) || hooks || text.startsWith('#!') ? 'shell' : null;
}

/** The command texts a written file of a kind runs: the file itself, a package's script strings, a notebook's shell lines. */
function scriptCommands(kind: ScriptKind, text: string): string[] {
    if (kind === 'package') {
        const strings: string[] = [];
        // Each value, a script's command among them; a key, which a colon follows, is a name.
        for (const match of text.matchAll(/"((?:[^"\\]|\\.)*)"(\s*:)?/g)) {
            if (match[2] !== undefined) continue;
            try {
                strings.push(JSON.parse(`"${match[1] ?? ''}"`) as string);
            } catch {
                strings.push(match[1] ?? '');
            }
        }
        return strings;
    }
    if (kind === 'notebook') {
        if (/^\s*%%(bash|sh|zsh|script|cmd|powershell|pwsh|system)\b/.test(text)) return [text];
        return text.split(/\r?\n/).filter((line) => /^\s*(!|%(system|sx|sc)\b)/.test(line)).map((line) => line.replace(/^\s*(!|%(system|sx|sc)\b)/, ''));
    }
    return [text];
}

/**
 * True when a text, read as a command's text is read, plainly and a second time over its normalised form, runs a
 * deny-list command or names a deny-list operation by its dot, names the confining variable, the attempt's variable
 * (`JASON_ATTEMPT_ID`), the data directory's variable or the installation's data in any literal spelling, or clears the
 * environment around a jason.
 */
function textRefused(text: string, scope: ChatScope): boolean {
    if (namesConfinedVariable(text) || namesAttemptVariable(text) || namesDataVariable(text, scope) || namesData(text, scope) || namesPersonsOwn(text, scope) || jasonRefusal(jasonIn(text, scope)) !== null) return true;
    return respelledRefusal(text, scope) !== null;
}

/** True for a tool of an MCP server other than the chat's own: `mcp__<server>__<tool>`. */
function isPersonsTool(tool: string): boolean {
    return tool.startsWith('mcp__') && !tool.startsWith(`mcp__${serverName}__`);
}

const personsInputRefusal = 'That tool\'s input names one of Jason\'s own variables, the installation\'s own data, or a command this mode refuses: the chat never sends it. Nothing was sent.';
const personsLongRefusal = 'That tool\'s input is longer than the card can show whole, so nobody could read what they would allow: the chat never sends it. Nothing was sent.';

/** Every string of a tool's input at any depth, the keys of its objects among them. */
function stringsOf(input: unknown): string[] {
    const found: string[] = [];
    const pending: unknown[] = [input];
    while (pending.length > 0) {
        const value = pending.pop();
        if (typeof value === 'string') found.push(value);
        else if (Array.isArray(value)) pending.push(...value);
        else if (value !== null && typeof value === 'object') {
            for (const [key, each] of Object.entries(value)) {
                found.push(key);
                pending.push(each);
            }
        }
    }
    return found;
}

/** Every string value of a tool's input, at any depth, in the order it stands; keys are not values. */
function valuesInOrder(input: unknown): string[] {
    const found: string[] = [];
    const pending: unknown[] = [input];
    while (pending.length > 0) {
        const value = pending.shift();
        if (typeof value === 'string') found.push(value);
        else if (Array.isArray(value)) pending.unshift(...value);
        else if (value !== null && typeof value === 'object') pending.unshift(...Object.values(value));
    }
    return found;
}

/**
 * A tool of an MCP server of the person's own, whatever its arguments are named: never read as a shell command or a
 * file tool, since the server, not a shell, does what it is sent. Its card's detail is the server and the tool, then the
 * input exactly as the host sent it (`JSON.stringify`), never cut: an input whose detail would be longer than the card
 * shows whole is refused, since Allow would send what nobody saw. Before that, every string of the input at any depth is
 * read as a command's text is (`textRefused`), and one that shows a refusal is refused with no card. Its card is never
 * covered by "always".
 */
function judgePersonsTool(tool: string, input: Record<string, unknown>, scope: ChatScope): Judgement {
    const rest = tool.slice('mcp__'.length);
    const cut = rest.indexOf('__');
    const detail = `${cut < 0 ? rest : rest.slice(0, cut)} · ${cut < 0 ? '' : rest.slice(cut + 2)}\n${JSON.stringify(input)}`;
    if (detail.length > detailLimit) return { verdict: 'deny', message: personsLongRefusal };
    if (stringsOf(input).some((text) => textRefused(text, scope))) return { verdict: 'deny', message: personsInputRefusal };
    // A server may join what it was sent, an argv array or a program and its arguments in fields of their own, into one
    // command: the input's string values are judged once more, joined by a space in the order they stand.
    if (textRefused(valuesInOrder(input).join(' '), scope)) return { verdict: 'deny', message: personsInputRefusal };
    return { verdict: 'ask', title: personsToolTitle, detail, remember: null };
}

/**
 * The body of a Bash `$'...'` string as the shell reads it: its escapes decoded. An `\x`, `\u` or `\U` with no digit after
 * it, or one past the last code point, is kept as written, as the shell keeps it.
 */
function ansiC(body: string): string {
    const simple: Readonly<Record<string, string>> = { n: '\n', t: '\t', r: '\r', '\\': '\\', "'": "'", '"': '"', a: '', b: '', e: '', E: '', f: '', v: '' };
    return body.replace(/\\(x[0-9A-Fa-f]{1,2}|u[0-9A-Fa-f]{1,4}|U[0-9A-Fa-f]{1,8}|[0-7]{1,3}|.)/g, (_, escape: string) => {
        if (/^[xuU]/.test(escape)) {
            const point = Number.parseInt(escape.slice(1), 16);
            return Number.isNaN(point) || point > 0x10ffff ? `\\${escape}` : String.fromCodePoint(point);
        }
        if (/^[0-7]/.test(escape)) return String.fromCodePoint(Number.parseInt(escape, 8));
        return simple[escape] ?? escape;
    });
}

/** A word's brace expansions, as Bash makes them from `{a,b}`; the word itself when it has none; null past `braceLimit`. */
const braceLimit = 64;
function braces(word: string): string[] | null {
    const group = /\{([^{}]*,[^{}]*)\}/.exec(word);
    if (group === null) return [word];
    const before = word.slice(0, group.index);
    const after = word.slice(group.index + group[0].length);
    const out: string[] = [];
    for (const choice of (group[1] ?? '').split(',')) {
        const each = braces(`${before}${choice}${after}`);
        if (each === null || out.length + each.length > braceLimit) return null;
        out.push(...each);
    }
    return out;
}

/** True when a word of the text has more brace expansions than the second look reads (`braceLimit`). */
function overBraced(text: string): boolean {
    return text.split(/\s+/).some((word) => braces(word) === null);
}

const bracesRefusal = 'That command expands into more words than the chat reads, so it cannot tell what would run: the chat never runs it. Write the words out. Nothing was run.';

/**
 * A text as a shell would most likely run it once it has taken its quoting away: a line continuation joined, an empty
 * `$''` or `$""` inside a word dropped and a `$'...'` string decoded, each brace group expanded into its words, a
 * backslash before a character inside a word and every quote removed, and a bracket, a comma or a parenthesis read as a
 * space. So `appro\ve`, `approv''e`, `appro$''ve`, `$'\x61'pprove`, `appro\` and a new line then `ve`, `{approve,}` and
 * a list literal (`['jason','approval','approve']`) read as the plain words they run.
 */
export function normalised(text: string): string {
    return readingsOf(text)[0] ?? text;
}

/**
 * The readings a refusal is looked for in: the text normalised (`normalised`), and the same with `\n`, `\t` and `\r` read
 * as the space they stand for in a `printf` or `echo -e` string rather than dropped.
 */
function readingsOf(text: string): string[] {
    const joined = text
        .replace(/\\\r?\n/g, '')
        .replace(/\$(['"])\1/g, '')
        .replace(/\$'((?:[^'\\]|\\[\s\S])*)'/g, (_, body: string) => ansiC(body));
    const expanded = joined.split(/(\s+)/).map((part) => (/\s/.test(part) ? part : (braces(part) ?? [part]).join(' '))).join('');
    const rest = (value: string) => value.replace(/\\(?=\S)/g, '').replace(/['"]/g, '').replace(/[[\](),]/g, ' ');
    return [rest(expanded), rest(expanded.replace(/\\[ntr]/g, ' '))];
}

/**
 * The environment cleared around a jason: in one command part that begins with `env`, its option `-i`, `-`,
 * `--ignore-environment`, `-u` or `--unset`, alone or among other short options; or Bash's `exec -c`; and that same part
 * names jason. Jason's own variables go with the rest without being named.
 */
function clearsEnvironment(text: string): boolean {
    const jasonWord = /(?<![A-Za-z0-9_.-])jason(?![A-Za-z0-9_-])/i;
    for (const part of text.split(/&&|\|\||[;|&\n\r]/)) {
        const tokens = part.trim().split(/\s+/).map(unquoted).filter((token) => token !== '');
        const first = tokens.findIndex((token) => !/^[A-Za-z_][A-Za-z0-9_]*=/.test(token));
        const program = (tokens[first] ?? '').split(/[\\/]/).pop()?.toLowerCase().replace(/\.exe$/, '');
        if (program !== 'env' && program !== 'exec') continue;
        let clears = false;
        for (const token of tokens.slice(first + 1)) {
            if (!token.startsWith('-')) {
                if (program === 'env' && /^[A-Za-z_][A-Za-z0-9_]*=/.test(token)) continue;
                break;
            }
            if (program === 'env' && (token === '-' || token === '--ignore-environment' || /^--unset(=|$)/.test(token) || /^-[A-Za-z0-9]*[iu]/.test(token))) clears = true;
            if (program === 'exec' && /^-[A-Za-z]*c/.test(token)) clears = true;
        }
        if (clears && jasonWord.test(normalised(part))) return true;
    }
    return false;
}

/**
 * The refusals a text earns read a second time, over its normalised form (`normalised`): a deny-list command or an
 * operation by its dot, the confining variable, the attempt's variable (`JASON_ATTEMPT_ID`), the data directory's
 * variable or its spellings; and the environment cleared around a jason. A hit is refused in every mode, as the plain
 * spelling is: a re-spelled verb is never a card.
 */
function respelledRefusal(text: string, scope: ChatScope): string | null {
    if (clearsEnvironment(text)) return confinedRefusal;
    // More expansions than are read is never judged on the part that is.
    if (overBraced(text)) return bracesRefusal;
    for (const plain of readingsOf(text)) {
        if (plain === text) continue;
        if (namesConfinedVariable(plain)) return confinedRefusal;
        if (namesAttemptVariable(plain)) return attemptRefusal;
        if (namesPersonsOwn(plain, scope)) return personsRefusal;
        if (namesDataVariable(plain, scope) || namesData(plain, scope)) return dataRefusal;
        const jasonRefused = jasonRefusal(jasonIn(plain, scope));
        if (jasonRefused !== null) return jasonRefused;
    }
    return null;
}

/**
 * The refusal a command earns when one of its places is the chat's own: its instructions, or its agent settings and
 * skills, directly in the chat folder. Refused in every mode but Full access, as the file tools refuse a write there: a
 * command cannot be told apart as a read or a write, so its sentence says that the file tools read it. In Full access
 * the folder is the turn's own, and the desktop puts back what it placed there before the next turn.
 */
function chatsOwnPlace(places: readonly string[], scope: ChatScope): string | null {
    for (const path of places) {
        const { parts, working } = guardedParts(scope, path);
        if (working?.chat !== true) continue;
        const found = guardAt(scope, parts);
        if (found?.at === 0 && found.guarded.chatsOwn !== undefined) return ownRefusal(found.guarded.chatsOwn).replace(/ Nothing was done\.$/, ownByCommand);
    }
    return null;
}

const separators = /&&|\|\||\$\(|[;|&`\n\r<>(){}]/;
const shellNames = '(?:sh|bash|zsh|dash|ksh|fish|pwsh|powershell|cmd)';
/** A shell given a script by a process substitution: it reads the script as commands. */
const pipedToShell = new RegExp(String.raw`(?<![A-Za-z0-9_.-])(?:\S*[\\/])?${shellNames}(?:\.exe)?\s+(?:-\S+\s+)*<\(`, 'i');
/** A program a script is piped into on its standard input: a shell, an interpreter, or `xargs`, which runs what it reads. */
const intoShell = new RegExp(String.raw`\|\s*(?:\S*[\\/])?(?:${shellNames}|python[0-9.]*|node|perl|ruby|php|xargs)(?:\.exe)?(?=\s|$|[;&|])`, 'i');

/** The text without the bodies of its here-documents, and whether one of them feeds a shell or an interpreter. */
function withoutHereDocuments(command: string): { text: string; fedToShell: boolean } {
    const lines = command.split('\n');
    const kept: string[] = [];
    let fedToShell = false;
    for (let at = 0; at < lines.length; at++) {
        const line = lines[at] ?? '';
        kept.push(line);
        const opened = /<<(-?)\s*(['"]?)([A-Za-z_][A-Za-z0-9_]*)\2/.exec(line);
        if (opened === null) continue;
        const before = line.slice(0, opened.index);
        if (new RegExp(String.raw`(?<![A-Za-z0-9_.-])(?:\S*[\\/])?(?:${shellNames}|python[0-9.]*|node|perl|ruby|php)(?:\.exe)?(?:\s+-\S+)*\s*$`, 'i').test(before)) fedToShell = true;
        const tag = opened[3];
        while (at + 1 < lines.length) {
            at++;
            const body = lines[at] ?? '';
            if ((opened[1] === '-' ? body.replace(/^\t+/, '') : body) === tag) {
                kept.push(body);
                break;
            }
        }
    }
    return { text: kept.join('\n'), fedToShell };
}

/**
 * Whether a piece of a word could expand into the data directory: one that begins with `~` other than the home folder's
 * own, one with a variable and a glob, and a glob whose fixed beginning is the data directory, a folder holding it or a
 * name it begins with, or a place in it outside the chat folder. A `~` folder is read as the home folder. In a demo run
 * each of the person's own directories is read the same way, whole.
 */
function couldReachData(piece: string, scope: ChatScope): boolean {
    let value = piece;
    if (value.startsWith('~')) {
        if (!/^~([\\/]|$)/.test(value)) return true;
        value = `${scope.home}${value.slice(1)}`;
    }
    const glob = value.search(/[*?[]/);
    if (glob < 0) {
        if (!piece.startsWith('~')) return false;
        const path = spelled(value, scope);
        return fencedSpellings(scope).some(({ folder, except }) => inside(scope, folder, path) && !(except !== null && inside(scope, except, path)));
    }
    const fixed = value.slice(0, glob);
    if (/[$%]/.test(fixed)) return true;
    const drive = scope.platform === 'win32' ? /^[\\/]+([a-z])(?=[\\/]|$)/i.exec(fixed) : null;
    const leading = drive ? `${drive[1]}:${fixed.slice(drive[0].length) || '\\'}` : fixed;
    const partial = leading === '' || /[\\/]$/.test(leading) ? '' : (leading.split(/[\\/]/).pop() ?? '');
    const folder = spelled(partial === '' ? leading || '.' : leading.slice(0, leading.length - partial.length) || '.', scope);
    const start = keyOf(scope.platform, pathsOf(scope.platform).join(folder, partial));
    return fencedSpellings(scope).some(({ folder: fenced, except }) => {
        const key = keyOf(scope.platform, fenced);
        if (key === start || key.startsWith(start)) return true;
        return inside(scope, key, folder) && !(except !== null && inside(scope, except, folder));
    });
}

/** The fenced folders as spelled: the data directory with the chat folder excepted, and the person's own directories whole. */
function fencedSpellings(scope: ChatScope): { folder: string; except: string | null }[] {
    return [
        { folder: spelled(scope.data, scope), except: spelled(scope.chat, scope) },
        ...personsOwnOf(scope).map((folder) => ({ folder: spelled(folder, scope), except: null })),
    ];
}

/**
 * Under Full access, whether a command is one the model cannot read plainly: a `jason` that is not the program of a
 * plain `jason <verb>` (an argument of `xargs`, a word in an interpreter's one-liner or a list literal, a quoted text, a
 * wrapper's argument), a variable, an expansion, `eval` or `xargs` as a program, a script handed to a shell or an
 * interpreter on its standard input or by a here-document, and a piece of a word that could expand into the data
 * directory. A here-document's body that feeds a program other than a shell is the program's input, never read here.
 */
export function unreadableCommand(command: string, scope: ChatScope): boolean {
    const { text, fedToShell } = withoutHereDocuments(command);
    if (fedToShell || pipedToShell.test(text) || intoShell.test(text)) return true;
    const jasonWord = /(?<![A-Za-z0-9_.-])jason(?![A-Za-z0-9_-])/;
    for (const part of text.split(separators)) {
        const tokens = (part.match(/"[^"]*"|'[^']*'|\S+/g) ?? []).filter((token) => token !== '');
        const first = tokens.findIndex((token) => !/^[A-Za-z_][A-Za-z0-9_]*=/.test(token));
        for (const [at, token] of tokens.entries()) {
            const word = unquoted(token);
            if (at === first && (/^[$`]|\$[({]|`/.test(token) || ['eval', 'xargs'].includes(programOf(scope.platform, word)))) return true;
            for (const piece of new Set([word, ...word.split(/[=@,]/)])) if (piece !== '' && couldReachData(piece, scope)) return true;
            if (programOf(scope.platform, word) === 'jason' && token === word) {
                if (at !== first || !/^[a-z][a-z-]*$/.test(tokens[at + 1] ?? '')) return true;
                continue;
            }
            if (jasonWord.test(normalised(word))) return true;
        }
    }
    return false;
}

/**
 * A Full access judgement: refused, by one of the three kinds' sentences (`hookRefusal`), or it runs, the host's own
 * bypass deciding the rest. Nothing is held for a card.
 */
export type FullAccessJudgement = { outcome: 'refused'; message: string } | { outcome: 'runs' };

/**
 * The one judgement of the turn's hook, in every mode it rides, and all of Full access's: refused where `hookRefusal`
 * refuses, and otherwise it runs, or in Ask first and Auto-edit the cards and the host's rules decide it. A judgement
 * that failed refuses, since it never runs anything.
 */
export function fullAccessJudgement(tool: string, input: Record<string, unknown>, scope: ChatScope): FullAccessJudgement {
    let refused: string | null;
    try {
        refused = hookRefusal(tool, input, scope);
    } catch {
        refused = unreadRefusal;
    }
    return refused === null ? { outcome: 'runs' } : { outcome: 'refused', message: refused };
}

/**
 * What the turn's hook refuses, in every mode it rides, and the only refusals of Full access: three kinds, each by its
 * own sentence. Jason's settings files and run file (`protectedFiles`); its data directory outside the turn's folder,
 * the desktop's records, the turns' files and the other conversations' folders among it, as the host's data rules name
 * it (`dataRules`), and in a demo run the person's own directories too; and what Jason itself sets for the turn: its
 * variables, the confining one, the attempt's and the data directory's, with where the shell finds a program, and its
 * permission tool, which only the person answers. They are read the ways a tool's input names them: a file tool's path
 * and a pattern's fixed folders, a command's text as written and its words as the literal reading resolves them, what a
 * writer tool would write, a script read as its lines, and every string of an MCP tool's input; on Windows a path that
 * names this computer as another computer would, through a drive's share, as that drive's path (`hookPlace`). A command
 * whose words are not literal is read by its text alone, and a path a program builds while it runs is not seen. No read
 * is blocked where the hook rides the turn, so a walk of a folder that holds the data directory is the data's kind too:
 * a search of it by the Grep or Glob tool (`walkKind`), or a command that may walk it, in any form but those known not to
 * descend (`walksRoot`), said by the sentence that says how to go on (`searchRefusal`); a plain listing of such a folder
 * is no walk. So is a command that reads what no word of it shows (`unseenReadRefusal`): a jq program that reads the
 * environment or a file by its own words, and a command told the files it reads on its input. Null for anything else:
 * the turn's folder is its own, the chat's instructions and agent folders in it among it, which the desktop puts back
 * before the next turn.
 */
export function hookRefusal(tool: string, input: Record<string, unknown>, scope: ChatScope): string | null {
    if (tool.startsWith(`mcp__${serverName}__`)) return ownToolRefusal;
    if (tool.startsWith('mcp__')) {
        for (const text of [...stringsOf(input), valuesInOrder(input).join(' ')]) {
            const refused = textKind(text, scope);
            if (refused !== null) return refused;
        }
        return null;
    }
    if (tool === 'Glob') return globKind(input, scope);
    const reader = readers[tool];
    if (reader !== undefined) {
        const value = input[reader];
        if (tool === 'Grep') return pathKind(named(value) ? value : scope.cwd, scope) ?? walkKind(named(value) ? value : scope.cwd, scope);
        return named(value) ? pathKind(value, scope) : null;
    }
    const writer = writers[tool];
    if (writer !== undefined) {
        const value = input[writer];
        const refused = named(value) ? pathKind(value, scope) : null;
        return refused ?? writtenKind(tool, input, scope);
    }
    const command = input['command'];
    return typeof command === 'string' ? commandKind(tool, command, scope) : null;
}

/**
 * The data's kind of a folder a search walks, where no read is blocked: a folder that holds the data directory, or in a
 * demo run one of the person's own directories, read where it leads (`hookPlace`), is refused by the sentence that says
 * how to go on (`searchRefusal`), since the walk would reach the data without naming it. Null for any other.
 */
function walkKind(value: string, scope: ChatScope): string | null {
    const place = hookPlace(located(value, scope), scope);
    return place !== null && !network(scope.platform, place) && holdsFenced(scope, place) ? searchRefusal : null;
}

/** Whether a located path is one of the installation's settings files or its run file. */
function isProtected(scope: ChatScope, path: string): boolean {
    const key = keyOf(scope.platform, path);
    return scope.protectedFiles.some((file) => keyOf(scope.platform, located(file, scope)) === key);
}

/**
 * The kind a path a file tool names is: the person's own installation in a demo, whose settings and run files among it
 * are the person's; a settings or run file; the data directory. It is read where it leads (`hookPlace`), and a path whose
 * end is not read is refused by the sentence that says so (`unplacedRefusal`).
 */
function pathKind(value: string, scope: ChatScope): string | null {
    const path = hookPlace(located(value, scope), scope);
    if (path === null) return unplacedRefusal;
    if (inPersonsOwn(scope, path)) return personsRefusal;
    if (isProtected(scope, path)) return settingsRefusal;
    if (inside(scope, located(scope.data, scope), path) && !inside(scope, located(scope.chat, scope), path)) return dataRefusal;
    return null;
}

/**
 * The names a network path may give this computer by (`ChatScope.machine`): `localhost`, the loopback addresses in the
 * spellings a path takes, and its own names; compared folded, as Windows compares them.
 */
function thisComputerNames(scope: ChatScope): string[] {
    return ['localhost', '127.0.0.1', '::1', '[::1]', '0--1.ipv6-literal.net', ...(scope.machine ?? [])].map((name) => name.toLowerCase());
}

/** Whether a network path's computer is this one: one of its names, any loopback address, or its own name with a domain after it. */
function thisComputer(computer: string, scope: ChatScope): boolean {
    const name = computer.toLowerCase();
    if (/^127(\.\d{1,3}){3}$/.test(name)) return true;
    const names = thisComputerNames(scope);
    return names.includes(name) || (scope.machine ?? []).some((own) => !own.includes('.') && name.startsWith(`${own.toLowerCase()}.`));
}

/**
 * On Windows, where a located path the hook reads leads, by Ask first's reading of it (`spelled`): one that names this
 * computer as another computer would (`thisComputer`), through a drive's administrative share (`C$`), is that drive's
 * path, located again; one through any other share of this computer, and one in a device's or a volume's namespace
 * (`device`), is null, since where it leads is not read. Any other path, another computer's among them, as it is.
 */
function hookPlace(path: string, scope: ChatScope): string | null {
    if (scope.platform !== 'win32') return path;
    // The namespace's prefix alone, as a pattern's `\\.` is, names nothing.
    if (device(scope.platform, path)) return /^\\\\[?.]\\[^\\]/.test(path) ? null : path;
    if (!network(scope.platform, path)) return path;
    const [computer = '', share = '', ...rest] = path.slice(2).split('\\');
    if (!thisComputer(computer, scope)) return path;
    const drive = /^([a-z])\$$/i.exec(share);
    return drive === null ? null : located(`${drive[1]}:\\${rest.join('\\')}`, scope);
}

/**
 * A search's folder, and the folder its pattern walks, read as paths are, each refused where it is or holds the data
 * (`walkKind`): the fixed folders the pattern begins with, climbed once more for each `..` after them, since a wildcard and
 * a `..` after it lead back to the folder the wildcard was in, and nothing climbs higher than that. A brace group that can
 * name a place is not read, and is refused by the sentence that says how to go on.
 */
function globKind(input: Record<string, unknown>, scope: ChatScope): string | null {
    const folder = named(input['path']) ? input['path'] : scope.cwd;
    const atFolder = pathKind(folder, scope) ?? walkKind(folder, scope);
    if (atFolder !== null) return atFolder;
    const pattern = typeof input['pattern'] === 'string' ? input['pattern'] : '';
    if (/\{[^{}]*[\\/:~][^{}]*\}/.test(pattern)) return braceRefusal;
    const parts = pattern.split(/[\\/]/);
    const fixed: string[] = [];
    for (const part of parts) {
        if (/[*?[{]/.test(part)) break;
        fixed.push(part);
    }
    const climbs = parts.slice(fixed.length).join('/').split(/[\\/{},]/).filter((part) => part === '..').length;
    if (fixed.length === 0 && climbs === 0) return null;
    const paths = pathsOf(scope.platform);
    const separator = paths.sep;
    const leading = pattern.startsWith('/') && fixed[0] === '' ? `${separator}${fixed.slice(1).join(separator)}` : fixed.join(separator);
    let walked = pattern.startsWith('~') ? leading || '~' : paths.resolve(spelled(folder, scope), leading || '.');
    for (let up = 0; up < climbs; up++) walked = paths.dirname(located(walked, scope));
    return pathKind(walked, scope) ?? walkKind(walked, scope);
}

/**
 * The kinds a text shows wherever it stands, in a file or an MCP tool's input: Jason's own variables and its data, read
 * as written and again over its normalised forms (`readingsOf`), as Ask first reads a name re-spelled by a quote or a
 * backslash.
 */
function textKind(text: string, scope: ChatScope): string | null {
    for (const plain of [text, ...readingsOf(text)]) {
        if (namesConfinedVariable(plain)) return confinedRefusal;
        if (namesAttemptVariable(plain)) return attemptRefusal;
        if (namesPersonsOwn(plain, scope)) return personsRefusal;
        if (namesDataVariable(plain, scope) || namesData(plain, scope)) return dataRefusal;
    }
    return null;
}

/**
 * The kinds a command's text and the places its words name show, in the order the sentences are said, each place read
 * where it leads (`hookPlace`), one whose end is not read refused by the sentence that says so (`unplacedRefusal`).
 */
function kindOver(text: string, places: readonly string[], scope: ChatScope): string | null {
    if (namesConfinedVariable(text)) return confinedRefusal;
    if (namesAttemptVariable(text)) return attemptRefusal;
    if (changesSearch(text)) return lookupRefusal;
    const leads = places.map((path) => hookPlace(path, scope));
    const read = leads.filter((path): path is string => path !== null);
    if (namesPersonsOwn(text, scope) || wordNamesPersonsOwn(read, scope)) return personsRefusal;
    if (read.some((path) => isProtected(scope, path))) return settingsRefusal;
    if (namesDataVariable(text, scope) || namesData(text, scope) || wordNamesData(read, scope)) return dataRefusal;
    return leads.includes(null) ? unplacedRefusal : null;
}

/**
 * A command's kinds: over its text as written and the places its words name, then over its normalised forms, then, where
 * every word is literal, over the words the literal reading resolves (`readLiteral`), each read from the folder a literal
 * directory change sets, a URL's percent-escapes decoded; and an environment cleared around a jason, which takes the
 * confining variable with it.
 */
function commandKind(tool: string, command: string, scope: ChatScope): string | null {
    const asWritten = kindOver(command, [...placesOf(command, scope), ...literalPlaces(tool, command, scope)], scope);
    if (asWritten !== null) return asWritten;
    const respelled = textKind(command, scope);
    if (respelled !== null) return respelled;
    if (command.split(/&&|\|\||[;|&\n\r]/).some((part) => clearsConfinement(part.trim().split(/\s+/).map(unquoted)))) return confinedRefusal;
    const reading = readLiteral(tool, command, { platform: scope.platform, home: shellHomeOf(scope) });
    if (!reading.held) {
        const commands = reading.commands.map((each) => ({ command: each, scope: each.cwd === null ? scope : { ...scope, cwd: located(nativeSpelling(each.cwd, scope), scope) } }));
        const places = commands.flatMap(({ command: each, scope: at }) => [...placesOfWords([...each.words, ...each.targets], at, true), ...commandPlaces(each.words, at, true)]);
        const resolved = reading.commands.map((each) => [...each.words, ...each.targets].join(' ')).join('\n');
        for (const text of new Set([resolved, percentDecoded(resolved)])) {
            const refused = kindOver(text, places, scope);
            if (refused !== null) return refused;
        }
        if (reading.commands.some((each) => clearsConfinement(each.words))) return confinedRefusal;
    }
    // No read is blocked where the hook rides the turn, so a read no word shows, and a walk of a folder that holds the
    // data, reach it without naming it: the data's kind too.
    const unseen = unseenReadRefusal(tool, command, scope);
    if (unseen !== null) return unseen;
    return walksRoot(tool, command, scope) ? searchRefusal : null;
}

/**
 * What a writer tool would write, read for the kinds: every text it would put in a file (`writtenText`), and a file run
 * as a script read as its lines are (`scriptRefusal`), so a script the agent writes to run later is refused where the
 * attempt shows.
 */
function writtenKind(tool: string, input: Record<string, unknown>, scope: ChatScope): string | null {
    const texts = writtenText(tool, input, scope);
    for (const text of texts) {
        const refused = textKind(text, scope);
        if (refused !== null) return refused;
    }
    const path = input['file_path'] ?? input['notebook_path'];
    if (typeof path !== 'string') return null;
    for (const text of texts) {
        const kind = scriptKind(tool, path, text);
        if (kind === null) continue;
        const refused = scriptRefusal(kind, text, path, scope);
        if (refused !== null) return refused;
    }
    return null;
}

/**
 * The card for a request the host asks about itself in Full access, where the hook said nothing: Ask first's card for
 * it where Ask first shows one, and otherwise a card of its own, never an allow and never a refusal. The host asked, so
 * the person answers.
 */
export function askedCard(tool: string, input: Record<string, unknown>, scope: ChatScope): Extract<Judgement, { verdict: 'ask' }> {
    try {
        const judged = judge(tool, input, scope);
        if (judged.verdict === 'ask') return judged;
    } catch {
        // A card of its own, below.
    }
    const command = input['command'];
    if (typeof command === 'string') return { verdict: 'ask', title: 'Run a command', detail: shorten(command), remember: null };
    return { verdict: 'ask', title: `Use ${tool}`, detail: shorten(JSON.stringify(input)), remember: null };
}

/**
 * What a `~` resolves to in the host's shell, which is the profile's home folder when the shell's is the same folder,
 * however spelled, and null, so a command with a `~` is read by its text alone, when it is another.
 */
function shellHomeOf(scope: ChatScope): string | null {
    if (scope.shellHome === undefined) return scope.home;
    const shell = keyOf(scope.platform, located(nativeSpelling(scope.shellHome, scope), scope));
    return shell === keyOf(scope.platform, located(scope.home, scope)) ? scope.home : null;
}

/** The refusals of a command's text and of the places its words name, by Ask first's own checks. */
function refusalOver(text: string, places: readonly string[], scope: ChatScope): string | null {
    if (namesConfinedVariable(text)) return confinedRefusal;
    if (namesAttemptVariable(text)) return attemptRefusal;
    if (changesSearch(text)) return lookupRefusal;
    const remote = wordNamesHost(places, scope);
    if (remote !== null) return remote;
    if (namesPersonsOwn(text, scope) || wordNamesPersonsOwn(places, scope)) return personsRefusal;
    if (namesDataVariable(text, scope) || namesData(text, scope) || wordNamesData(places, scope)) return dataRefusal;
    const jasonRefused = jasonRefusal(jasonIn(text, scope));
    if (jasonRefused !== null) return jasonRefused;
    const own = chatsOwnPlace(places, scope);
    if (own !== null) return own;
    // reply with words other than `auth status` or `account list` is refused here as Ask first refuses it.
    return replyRefused(text, scope) ? replyRefusal : null;
}

/**
 * The one judgement of Auto-edit: Ask first's (`judge`), except that a Bash command Ask first would put on a card runs
 * with no card where it is only the six file commands (`fileCommandsOf`) on paths in the chat's own folder. It is never
 * wider than that: a refusal, an allow and every other card are Ask first's own, word for word.
 */
export function autoEditJudgement(tool: string, input: Record<string, unknown>, scope: ChatScope): Judgement {
    const judged = judge(tool, input, scope);
    const command = input['command'];
    if (judged.verdict !== 'ask' || tool !== 'Bash' || typeof command !== 'string') return judged;
    // Whatever goes wrong while the command is read, it keeps its card: a reading that failed never runs anything.
    try {
        return editsOnlyTheChatFolder(command, scope) ? allow : judged;
    } catch {
        return judged;
    }
}

/**
 * True when a command is read literally, with no `~` taken (`readLiteral`), as file commands alone, every operand of
 * which lies strictly inside the chat folder (`chatFolderPlace`), and so does the path a `mv` or a `cp` writes when its
 * last operand is a folder: that folder's path with each source's name after it, judged for every copy and move as one
 * more operand whatever the last operand is now, since an earlier part of the same command may make it a folder. No
 * operand of a later command lies at or under one an earlier `mv` or `cp` of it named or wrote, nothing under a guarded
 * name, no `mv` to a name that begins a guarded path of two parts (`.github`, whatever the folder moved there holds), no
 * file named like a program the shell may run from the chat folder in place of the system's, and none of Ask first's
 * refusals over the words as the shell passes them on. The judgement is made before the command runs: a later command
 * is judged against the folder as it is now, and a move or a copy could put a link where it looks, so what follows one
 * may not look there.
 */
function editsOnlyTheChatFolder(command: string, scope: ChatScope): boolean {
    if (scope.entry === undefined) return false;
    const reading = readLiteral('Bash', command, { platform: scope.platform, home: null });
    if (reading.held) return false;
    const files = fileCommandsOf(reading.commands);
    if (files === null) return false;
    const chat = { spelled: spelled(scope.chat, scope), located: located(scope.chat, scope) };
    const moved: string[] = [];
    const places: string[] = [];
    const placeOf = (operand: string): string | null => {
        const place = chatFolderPlace(operand, scope, chat);
        return place === null || moved.some((earlier) => inside(scope, earlier, place)) ? null : place;
    };
    for (const file of files) {
        const named: string[] = [];
        for (const operand of file.operands) {
            const place = placeOf(operand);
            if (place === null) return false;
            named.push(place);
        }
        if (file.program === 'mv' || file.program === 'cp') {
            const destination = (file.operands.at(-1) ?? '').replace(/[\\/]+$/, '');
            const into: string[] = [named.at(-1) ?? ''];
            for (const source of file.operands.slice(0, -1)) {
                const name = source.replace(/[\\/]+$/, '').split(/[\\/]/).pop() ?? '';
                const written = destination === '' || name === '' ? null : placeOf(`${destination}/${name}`);
                if (written === null) return false;
                named.push(written);
                into.push(written);
            }
            if (file.program === 'mv' && into.some((place) => beginsGuardedPath(scope, guardedParts(scope, place).parts))) return false;
            moved.push(...named);
        }
        places.push(...named);
    }
    if (places.some((place) => namesProgramHere(scope, chat.located, place))) return false;
    if (shadowed(scope, new Set(files.map((file) => file.program)))) return false;
    const resolved = reading.commands.map((each) => each.words.join(' ')).join('\n');
    if (refusalOver(resolved, places, scope) !== null) return false;
    if (guardedPlace(places, scope) !== undefined || namesLocalJason(resolved, scope)) return false;
    return true;
}

/** True when the last component of a path is the first of a guarded name of more than one part. */
function beginsGuardedPath(scope: ChatScope, parts: readonly string[]): boolean {
    const last = parts.at(-1);
    if (last === undefined) return false;
    return guardedNames.some(({ name }) => name.includes('/') && foldName(scope.platform, name.split('/')[0] ?? '') === foldName(scope.platform, last));
}

/**
 * True when a file name is one the shell may run as a program from the chat folder: one of the six, compared as the
 * platform folds names, with one of Windows' program extensions on Windows; or jason, as `namesLocalJason` reads it.
 */
function isProgramFile(scope: ChatScope, name: string): boolean {
    if (isJasonFile(scope, name)) return true;
    const match = /^(.*?)(?:\.([^.]*))?$/.exec(foldName(scope.platform, name));
    if (match === null || !Object.hasOwn(fileGrammars, match[1] ?? '')) return false;
    return match[2] === undefined || (scope.platform === 'win32' && programExtensions.includes(match[2]));
}

/** True when a located path lies directly in the chat folder under a program's name (`isProgramFile`). */
function namesProgramHere(scope: ChatScope, chat: string, place: string): boolean {
    const parts = partsBelow(scope, chat, place);
    return parts.length === 1 && isProgramFile(scope, parts[0] ?? '');
}

/**
 * True when a file named like one of the programs the command runs is already in the folder the shell runs in, where a
 * search path that holds the current folder would run it in place of the system's: the bare name, and on Windows each
 * program extension. The volume folds the name where the platform does.
 */
function shadowed(scope: ChatScope, programs: ReadonlySet<string>): boolean {
    const paths = pathsOf(scope.platform);
    for (const program of programs) {
        const names = [program, ...(scope.platform === 'win32' ? programExtensions.map((extension) => `${program}.${extension}`) : [])];
        if (names.some((name) => scope.entry?.(paths.join(scope.cwd, name)) !== null)) return true;
    }
    return false;
}

/** A name Windows opens as a device in any folder, with any extension: `nul`, `con.txt`, `COM1`. */
const deviceName = /^(con|prn|aux|nul|com[0-9]|lpt[0-9]|conin\$|conout\$)(\..*)?$/i;

/**
 * Where an operand lies when it lies strictly inside the chat folder, the folder itself never; null otherwise. No `..`
 * component is taken, since the system reads `link/..` as the parent of where the link leads and a spelling as the
 * parent of the link. On Windows, a name the system reads as another (a trailing dot or space, a stream, a device form)
 * and a device's name are not taken either. Inside twice over: as spelled, and as located, through links, junctions and
 * short names, compared as the platform folds case. A link or a junction that leads nowhere, at the path or at any
 * folder above it in the chat folder, is not taken: the real path reader cannot follow it, so where it leads is not
 * known, and a `touch` or a `cp` through it would write there.
 */
function chatFolderPlace(operand: string, scope: ChatScope, chat: { spelled: string; located: string }): string | null {
    const parts = operand.split(/[\\/]/);
    if (parts.includes('..') || judgeSpelling(operand, scope) !== null) return null;
    if (scope.platform === 'win32' && parts.some((part) => deviceName.test(part))) return null;
    const native = nativeSpelling(operand, scope);
    const lexical = spelled(native, scope);
    if (!strictlyInside(scope, chat.spelled, lexical)) return null;
    const paths = pathsOf(scope.platform);
    for (let at = lexical; strictlyInside(scope, chat.spelled, at); at = paths.dirname(at)) {
        const state = scope.entry?.(at);
        if (state !== null && state !== 'present') return null;
    }
    const place = located(native, scope);
    return strictlyInside(scope, chat.located, place) ? place : null;
}

function strictlyInside(scope: ChatScope, folder: string, path: string): boolean {
    return inside(scope, folder, path) && keyOf(scope.platform, folder) !== keyOf(scope.platform, path);
}

/** A text with each percent-escape decoded, as a URL word is read. */
function percentDecoded(text: string): string {
    return text.replace(/%[0-9A-Fa-f]{2}/g, (escape) => String.fromCharCode(Number.parseInt(escape.slice(1), 16)));
}

/**
 * True when a resolved command is env clearing or unsetting the environment, or exec clearing it, around a jason: the
 * variables Jason sets on a turn would go with the rest without being named.
 */
function clearsConfinement(words: readonly string[]): boolean {
    const program = programName(words[0] ?? '');
    if (program !== 'env' && program !== 'exec') return false;
    const options = words.slice(1).filter((word) => word.startsWith('-'));
    const clears = program === 'env'
        ? options.some((word) => word === '-' || word === '--ignore-environment' || /^--unset(=|$)/.test(word) || /^-[A-Za-z0-9]*[iu]/.test(word))
        : options.some((word) => /^-[A-Za-z]*c/.test(word));
    return clears && words.slice(1).some((word) => programName(word) === 'jason');
}

const packageRunners = new Set(['npx', 'bunx', 'pnpx', 'pnpm', 'yarn', 'npm']);

function hostOf(url: string): string | null {
    try {
        return new URL(url).host || null;
    } catch {
        return null;
    }
}

/**
 * The documentation pages the chat's agent opens with no card: each a host and the path its documentation lives under,
 * as the vendor's own index of its pages gives it (read 2026-11-11).
 * - Claude Code's, https://code.claude.com/docs/llms.txt: "# Claude Code Docs", "Official documentation for Claude Code,
 *   Anthropic's agentic coding tool", every page listed under `https://code.claude.com/docs/`.
 * - OpenAI's, https://developers.openai.com/llms.txt: "[Codex developer tools](https://developers.openai.com/codex/llms.txt)";
 *   https://developers.openai.com/api/llms.txt: "[OpenAI API guides](https://developers.openai.com/api/docs/llms.txt)" and
 *   "[OpenAI API endpoint reference](https://developers.openai.com/api/reference/llms.txt)"; and the host the Codex pages
 *   redirect to, https://learn.chatgpt.com/docs/llms.txt: "Each page has a Markdown twin at `/docs/<slug>.md`".
 * - Reply's, https://docs.reply.io/llms.txt: "# Reply API Documentation", the host open whole.
 * The same hosts serve pages others wrote or a platform's own files beside them: https://developers.openai.com/llms.txt
 * lists "[OpenAI developer showcase](https://developers.openai.com/showcase/llms.txt): Projects, demos, games, and
 * applications built with OpenAI APIs and Codex.", and https://docs.reply.io/ says "This documentation is built and
 * hosted on Mintlify, a developer documentation platform", whose files stand under a path segment that begins with an
 * underscore (`/_llms/`, `/_next/`). So no host is open beyond its documentation's path, and no path below a segment that
 * begins with an underscore opens, on any of them. Each host is exact; none is a wildcard, since a vendor's other
 * subdomains may serve what others wrote. Claude Code does not follow a redirect to another host: Anthropic,
 * https://code.claude.com/docs/en/tools-reference#webfetch-tool-behavior (read 2026-11-11), "When a URL redirects to a
 * different host, WebFetch returns a text result that names the original URL and the redirect target instead of
 * following it. Claude then fetches the new URL with a second WebFetch call.", so a listed page that leads to another
 * host is judged again at the next fetch.
 */
export const documentationPages: readonly { host: string; prefix: string }[] = [
    { host: 'code.claude.com', prefix: '/docs/' },
    { host: 'developers.openai.com', prefix: '/codex/' },
    { host: 'developers.openai.com', prefix: '/api/docs/' },
    { host: 'developers.openai.com', prefix: '/api/reference/' },
    { host: 'learn.chatgpt.com', prefix: '/docs/' },
    { host: 'docs.reply.io', prefix: '/' },
];

/**
 * The longest address of a documentation page that opens with no card: a page's own address is far shorter, and a longer
 * one would carry more than the name of a page to the vendor.
 */
export const documentationAddressLimit = 300;

/**
 * True when a URL is a documentation page as written: at most `documentationAddressLimit` characters, no space or
 * control character anywhere (a parser drops or trims them), `https://`, the host exactly as listed (in any letter case),
 * then a path of letters, digits and `-._~/` alone under one of that host's paths, and at most a fragment of the same
 * characters but the slash. No query string, not even an empty one; no escape, no empty segment, no `.` or `..`
 * segment, and no segment that begins with an underscore. Anything else in the host's place, a user, a port (the default
 * one spelled included), a trailing dot, an escape or a character a parser would turn into a dot, is not one. A URL
 * parser then reads the same host and path from it; no address that passes the spelling reads otherwise, and the parser's
 * reading stays as a second one.
 */
function isDocumentationPage(url: string): boolean {
    if (url.length > documentationAddressLimit || /[\s\x00-\x1f\x7f]/.test(url)) return false;
    const match = /^https:\/\/([A-Za-z0-9.-]+)(\/[A-Za-z0-9._~/-]*)?(?:#[A-Za-z0-9._~-]*)?$/.exec(url);
    const host = match?.[1]?.toLowerCase();
    const path = match?.[2] ?? '/';
    if (host === undefined) return false;
    const segments = path.split('/').slice(1);
    if (segments.some((segment, at) => (segment === '' && at < segments.length - 1) || segment === '.' || segment === '..' || segment.startsWith('_'))) return false;
    const under = documentationPages.some((page) => page.host === host && (path.startsWith(page.prefix) || `${path}/` === page.prefix));
    if (!under) return false;
    try {
        const parsed = new URL(url);
        return parsed.protocol === 'https:' && parsed.username === '' && parsed.password === '' && parsed.port === '' && parsed.hostname === host && parsed.pathname === path && parsed.search === '';
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
    // Before any table and before the command check: an argument named `command` or `file_path` is the server's to read.
    if (isPersonsTool(tool)) return judgePersonsTool(tool, input, scope);
    if (tool === 'Glob') return judgeGlob(input, scope);
    const reader = readers[tool];
    if (reader !== undefined) {
        const value = input[reader];
        if (tool === 'Read') return named(value) ? judgePath(value, scope, false) : unnamed(tool, input);
        // Where the hook rides the turn a listing reads one folder's names and is judged as a read of it; a search walks.
        if (tool === 'LS' && scope.hooked === true) return judgePath(named(value) ? value : '.', scope, false);
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
        // A documentation page opens with no card; any other page, a lookalike of one included, keeps its card.
        if (isDocumentationPage(url)) return allow;
        const host = hostOf(url);
        if (host === null) return { verdict: 'ask', title: 'Open a web page', detail: shorten(url), remember: null };
        return { verdict: 'ask', title: 'Open a web page', detail: shorten(url), remember: `web:${host.toLowerCase()}`, covers: `pages on ${host.toLowerCase()}` };
    }
    const card = { verdict: 'ask', title: `Use ${tool}`, detail: shorten(JSON.stringify(input)) } as const;
    return runsNothing.has(tool) ? { ...card, remember: `tool:${tool}`, covers: `every use of ${tool}` } : { ...card, remember: null };
}
