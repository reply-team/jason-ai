// Pure: how one turn of the chat runs on Claude Code and on Codex. The command line each host is started with (the
// person's words never among it: they go on standard input), the environment it gets, and what each line the host
// prints means for the page. Nothing here starts a process or reads a file.

import { posix, win32 } from 'node:path';
import { allowRules, denyRules } from './chat-deny.ts';
import { promptToolName } from './chat-permission-endpoint.ts';
import type { Platform } from './chat-permission-model.ts';
import { isSessionMarker } from './session-markers.ts';

/**
 * What Claude Code runs without asking anyone, beside what its own rules already let run and the allow list's jason verbs
 * (`allowRules`): the skills, web search, subagents (`Agent`, `Task` before the tool was renamed), the task list
 * (`TodoWrite`, and the `Task*` tools that replace it on newer models), and of `reply` exactly two read commands. Every
 * other tool use the host would ask about goes to the permission tool, a jason command none of the verbs begins included,
 * and so does opening a web page: the agent reads text outsiders wrote, and a fetch could carry what it sees out in a URL.
 */
export const claudeAllowedTools: readonly string[] = [
    'Skill',
    'WebSearch',
    'Agent',
    'Task',
    'TodoWrite',
    'TaskCreate',
    'TaskGet',
    'TaskList',
    'TaskUpdate',
    'Bash(reply auth status:*)',
    'Bash(reply account list:*)',
];

/**
 * The tools taken out of every turn by a bare rule, which removes a tool from the model's context. PowerShell is on by
 * default on Windows for claude.ai accounts with Git Bash, and Monitor runs a command in the background; the fence (the
 * deny rules and the `jason`/`reply` allow rules) names the Bash tool, so with both gone the one shell a turn has is the
 * fenced one. Git Bash is Claude Code's own prerequisite on Windows. The permission model still reads any tool input that
 * carries a command as a shell command.
 */
export const claudeRemovedTools: readonly string[] = ['PowerShell', 'Monitor'];

export interface ClaudeTurn {
    /** The turn's `--mcp-config` file, which names the permission endpoint. */
    mcpConfig: string;
    /** The campaign's and the project's folders, recorded and present. */
    addDirs: readonly string[];
    /** The deny list's commands. */
    denied: readonly string[];
    /** The allow list's verbs. */
    allowed: readonly string[];
    /** The host's file rules for the installation's data outside the chat folder (`dataRules`). */
    dataRules: readonly string[];
    model?: string | undefined;
    /** The session the previous turn of this chat reported; absent on a chat's first turn. */
    resume?: string | undefined;
}

/**
 * The settings every Claude Code turn carries on its command line. Claude Code runs its built-in read-only commands
 * (`cat`, `ls`, `grep` and the like) without asking, wherever the path they name; with reads outside the working folders
 * blocked, such a command on a path outside the chat folder and the added folders asks, and so reaches the permission
 * tool, as does a command whose folder the host cannot trace. It holds no secret, so it travels as inline JSON. The key
 * is read from Claude Code 2.1.257; an older version skips it without a word.
 */
export const claudeTurnSettings = '{"permissions":{"blockReadsOutsideWorkingDirectories":true}}';

/**
 * The command line of one Claude Code turn. Never `--bare`, which drops the subscription sign-in and the skills; always
 * `--strict-mcp-config`, so a person's own MCP servers never enter the chat; reads outside the working folders blocked
 * (`claudeTurnSettings`); each allowed and disallowed rule an argument of its own, the form the CLI reference gives,
 * so a comma in a data path never splits a rule; no prompt argument, so the prompt is read from standard input.
 */
export function claudeArguments(turn: ClaudeTurn): string[] {
    const args = ['-p', '--output-format', 'stream-json', '--verbose'];
    args.push('--allowedTools', ...claudeAllowedTools, ...allowRules(turn.allowed));
    args.push('--disallowedTools', ...claudeRemovedTools, ...denyRules(turn.denied), ...turn.dataRules);
    args.push('--setting-sources', 'project');
    args.push('--settings', claudeTurnSettings);
    for (const folder of turn.addDirs) args.push('--add-dir', folder);
    args.push('--permission-prompt-tool', promptToolName, '--mcp-config', turn.mcpConfig, '--strict-mcp-config');
    if (turn.model) args.push('--model', turn.model);
    if (turn.resume) args.push('--resume', turn.resume);
    return args;
}

/**
 * The command line of one Codex turn: `codex exec` in its read-only sandbox, emitting JSON events, the prompt read from
 * standard input (`-`), resumed as `codex exec resume <thread>` from the chat's second turn on.
 *
 * Codex is this release's reading host. A Codex turn runs `exec --json --sandbox read-only` and asks no permission, so no
 * card and no fence of the desktop's applies to it: it reads the chat folder, the folders added to it and its skills in
 * `.agents/skills`, and answers. Its sandbox has no network by default and so may keep it from the runtime's loopback
 * address, so a Codex turn reads and advises and acts through nothing.
 */
export function codexArguments(turn: { model?: string | undefined; resume?: string | undefined }): string[] {
    const args = ['exec', '--json', '--sandbox', 'read-only', '--skip-git-repo-check'];
    if (turn.model) args.push('--model', turn.model);
    if (turn.resume) args.push('resume', turn.resume);
    args.push('-');
    return args;
}

const gitignoreSpecial = /[\\[\]*?!#]/g;

/** A path as a Claude Code file rule spells an absolute one: `//` and the path from the root, `//c/...` on Windows. */
export function rulePath(platform: Platform, path: string): string {
    let spelled: string;
    if (platform === 'win32') {
        const resolved = win32.resolve(path);
        const drive = /^([a-zA-Z]):\\/.exec(resolved);
        spelled = drive ? `/${drive[1]?.toLowerCase()}/${resolved.slice(3).replaceAll('\\', '/')}` : resolved.replaceAll('\\', '/');
    } else {
        spelled = posix.resolve(path);
    }
    const trimmed = spelled.replace(/\/+$/, '');
    return `/${trimmed.replace(gitignoreSpecial, (character) => `\\${character}`)}`;
}

/**
 * The data directory's own entries, which the runtime, the command line and the desktop make: named by the data rules
 * whether they exist yet or not, so one made while a turn runs is refused as well.
 */
export const knownDataEntries: readonly string[] = ['config', 'run', 'state', 'logs', 'plugins', 'ui', 'work', 'skills', 'update', 'doctor', 'autostart'];

/**
 * The desktop folder's own entries but the chat folder, named the same way; `turns` holds every turn's configuration,
 * `rollback` the application an update kept aside and the record the next start acts on.
 */
export const knownDesktopEntries: readonly string[] = ['settings.json', 'provider-keys', 'electron', 'turns', 'rollback'];

/** The known names, then every name that is there and is not one of them, spelled as it is. */
function namesOf(known: readonly string[], present: readonly string[], kept: string): string[] {
    return [...new Set([...known, ...present])].filter((name) => name.toLowerCase() !== kept);
}

/**
 * The host's own rules for the installation's data: every entry of the data directory but `desktop`, and every entry of
 * `desktop` but the chat folder (the turns' configurations among them), the known ones whether they exist or not,
 * refused to its file tools and to the file commands it recognises in Bash (`cat`, `head`, a redirection). Without them
 * a read-only command such as `cat` reads outside the working directories without asking; the permission model refuses
 * the same paths when it is asked.
 */
export function dataRules(platform: Platform, data: string, dataEntries: readonly string[], desktopEntries: readonly string[]): string[] {
    const paths = platform === 'win32' ? win32 : posix;
    const desktop = paths.join(data, 'desktop');
    const targets = [
        ...namesOf(knownDataEntries, dataEntries, 'desktop').map((name) => paths.join(data, name)),
        ...namesOf(knownDesktopEntries, desktopEntries, 'chat').map((name) => paths.join(desktop, name)),
    ];
    return targets.flatMap((target) => {
        const rule = rulePath(platform, target);
        return [`Read(${rule})`, `Read(${rule}/**)`, `Edit(${rule})`, `Edit(${rule}/**)`];
    });
}

/**
 * The variables a host never inherits from the desktop: a plugin's configuration, backgrounding of tool calls, and a
 * parent Claude Code session's markers (`sessionMarkers`).
 */
function withheld(name: string): boolean {
    const upper = name.toUpperCase();
    return upper.startsWith('JASON_PLUGINS__') || upper === 'CLAUDE_AUTO_BACKGROUND_TASKS' || isSessionMarker(name);
}

/**
 * What a person's environment says about which provider and credentials each host uses. A turn on a key provider is
 * started without them, so the provider the person picked is the one the turn reaches: an inherited
 * `CLAUDE_CODE_USE_BEDROCK` or `OPENAI_BASE_URL` would otherwise send it elsewhere. A plain Claude Code or Codex turn
 * keeps them, as the person's own configuration.
 */
const inheritedProvider: Readonly<Record<'claude' | 'codex', ReadonlySet<string>>> = {
    claude: new Set([
        'ANTHROPIC_API_KEY',
        'ANTHROPIC_AUTH_TOKEN',
        'ANTHROPIC_BASE_URL',
        'ANTHROPIC_MODEL',
        'ANTHROPIC_DEFAULT_OPUS_MODEL',
        'ANTHROPIC_DEFAULT_SONNET_MODEL',
        'ANTHROPIC_DEFAULT_HAIKU_MODEL',
        'ANTHROPIC_SMALL_FAST_MODEL',
        'CLAUDE_CODE_USE_BEDROCK',
        'CLAUDE_CODE_USE_VERTEX',
        'CLAUDE_CODE_USE_FOUNDRY',
        'CLAUDE_CODE_OAUTH_TOKEN',
    ]),
    codex: new Set(['OPENAI_API_KEY', 'OPENAI_BASE_URL']),
};

/**
 * Set on every Claude Code turn: the host strips credentials from the environment of the commands, hooks and stdio
 * servers it starts (a key provider's key among them), so the key reaches the host's own process and not a read-only
 * `echo` that runs without a card.
 */
const scrub = 'CLAUDE_CODE_SUBPROCESS_ENV_SCRUB';

/**
 * Set on every Claude Code turn: the host returns to the folder the turn started in, the chat folder, after each Bash
 * command, so a `cd` in one command does not move the next, and the permission model reads a relative word where the
 * command really runs.
 */
const maintainFolder = 'CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR';

/**
 * Set on every Claude Code turn: the host runs nothing in the background. A turn is one process that ends when the agent
 * answers, so a command left running behind it would outlive the turn, and its answer would reach no one.
 */
const noBackground = 'CLAUDE_CODE_DISABLE_BACKGROUND_TASKS';

/**
 * The host's environment: the desktop's own, with the bundled `jason`'s folder first on PATH, the data directory named
 * when the desktop runs with one, and a key provider's variables. Nothing of a plugin's configuration is passed on, and
 * `CLAUDE_AUTO_BACKGROUND_TASKS`, which would let the host move a waiting permission call to the background, is removed,
 * as are the markers of a Claude Code session the desktop itself was started from. A key provider's turn is started
 * without the provider and credentials the desktop inherited (`inheritedProvider`); a Claude Code turn always carries
 * `CLAUDE_CODE_SUBPROCESS_ENV_SCRUB=1`, `CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1` and
 * `CLAUDE_CODE_DISABLE_BACKGROUND_TASKS=1`.
 */
export function hostEnvironment(
    base: Readonly<Record<string, string | undefined>>,
    options: {
        platform: Platform;
        bundle: string;
        data: string | null;
        program: 'claude' | 'codex';
        /** Whether the turn runs on a key provider. */
        keyed: boolean;
        provider: Readonly<Record<string, string>>;
    },
): Record<string, string> {
    const env: Record<string, string> = {};
    let path = '';
    const replaced = options.keyed ? inheritedProvider[options.program] : new Set<string>();
    for (const [name, value] of Object.entries(base)) {
        if (value === undefined || withheld(name)) continue;
        const upper = name.toUpperCase();
        if (replaced.has(upper) || upper === scrub || upper === maintainFolder || upper === noBackground) continue;
        if (name.toUpperCase() === 'PATH') {
            path = value;
            continue;
        }
        if (name.toUpperCase() === 'JASON_DATA_DIR') continue;
        env[name] = value;
    }
    const separator = options.platform === 'win32' ? ';' : ':';
    env.PATH = path === '' ? options.bundle : `${options.bundle}${separator}${path}`;
    if (options.data !== null) env.JASON_DATA_DIR = options.data;
    for (const [name, value] of Object.entries(options.provider)) env[name] = value;
    if (options.program === 'claude') {
        env[scrub] = '1';
        env[maintainFolder] = '1';
        env[noBackground] = '1';
    }
    return env;
}

/** What one line a host prints means. */
export type HostLine =
    | { kind: 'session'; id: string }
    | ({ kind: 'step' } & Step)
    | { kind: 'text'; text: string }
    /** The host said the turn is over: `error` null when it succeeded, else the host's own words. */
    | { kind: 'result'; error: string | null };

/**
 * One tool use as the page shows it: `text`, fixed words of the desktop's own for what the host did, and `detail`, the
 * command, path, query or tool name it acted on, which the page draws as text beneath them. A tool the tables below do
 * not name is "Used a tool", with its name as the detail.
 */
export interface Step {
    text: string;
    detail?: string;
}

/** The longest detail a step carries; a longer one is cut and ends with an ellipsis. */
const detailLimit = 300;

/**
 * A value a step can show beneath its words: a non-blank string, cut to the limit; anything else is no detail. The cut
 * falls between two characters: a character written as two code units is kept whole or left out whole.
 */
function detailOf(value: unknown): string | undefined {
    if (typeof value !== 'string' || value.trim() === '') return undefined;
    const text = value.trim();
    if (text.length <= detailLimit) return text;
    let end = detailLimit - 1;
    const last = text.charCodeAt(end - 1);
    if (last >= 0xd800 && last <= 0xdbff) end--;
    return `${text.slice(0, end)}…`;
}

function step(text: string, detail: unknown): Step {
    const shown = detailOf(detail);
    return shown === undefined ? { text } : { text, detail: shown };
}

/** A command as a step: a jason command says so, and the command itself is the detail. */
function commandStep(command: unknown): Step {
    const first = typeof command === 'string' ? command.trim().split(/\s+/)[0] : undefined;
    return step(first === 'jason' ? 'Ran a jason command' : 'Ran a command', command);
}

type Field = (input: Record<string, unknown>) => unknown;
const field = (name: string): Field => (input) => input[name];

/**
 * Claude Code's tools other than its two shells, by name: the words for what each did, and the input field shown
 * beneath them. A Map, so a name
 * that only an object inherits (`constructor`, `__proto__`) is a tool it does not know.
 */
const claudeSteps: ReadonlyMap<string, { text: string; detail?: Field }> = new Map([
    ['Read', { text: 'Read a file', detail: field('file_path') }],
    ['Write', { text: 'Wrote a file', detail: field('file_path') }],
    ['Edit', { text: 'Changed a file', detail: field('file_path') }],
    ['MultiEdit', { text: 'Changed a file', detail: field('file_path') }],
    ['NotebookEdit', { text: 'Changed a notebook', detail: field('notebook_path') }],
    ['Glob', { text: 'Looked through files', detail: field('pattern') }],
    ['Grep', { text: 'Looked through files', detail: field('pattern') }],
    ['LS', { text: 'Looked through files', detail: field('path') }],
    ['Skill', { text: 'Used a skill', detail: field('skill') }],
    ['WebSearch', { text: 'Searched the web', detail: field('query') }],
    ['WebFetch', { text: 'Read a web page', detail: field('url') }],
    ['Agent', { text: 'Started a subagent', detail: field('description') }],
    ['Task', { text: 'Started a subagent', detail: field('description') }],
    ['TodoWrite', { text: 'Updated its task list' }],
    ['TaskCreate', { text: 'Updated its task list' }],
    ['TaskUpdate', { text: 'Updated its task list' }],
    ['TaskGet', { text: 'Read its task list' }],
    ['TaskList', { text: 'Read its task list' }],
]);

/** A tool use as a step, in the desktop's words for what it did. */
export function claudeStep(name: unknown, input: unknown): Step {
    const fields = input !== null && typeof input === 'object' && !Array.isArray(input) ? (input as Record<string, unknown>) : {};
    if (name === 'Bash' || name === 'PowerShell') return commandStep(fields.command);
    const known = typeof name === 'string' ? claudeSteps.get(name) : undefined;
    if (known === undefined) return step('Used a tool', name);
    return step(known.text, known.detail?.(fields));
}

function parsed(line: string): Record<string, unknown> | null {
    try {
        const value: unknown = JSON.parse(line);
        return value !== null && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : null;
    } catch {
        return null;
    }
}

const sessionShape = /^[A-Za-z0-9._:-]{1,200}$/;

/**
 * One line of Claude Code's `stream-json` output: the session it runs as (`system`/`init`), the agent's words and tool
 * uses (`assistant`), and the end (`result`). A tool's own output (`user`) and anything unreadable say nothing.
 */
export function claudeLine(line: string): HostLine[] {
    const event = parsed(line);
    if (event === null) return [];
    if (event.type === 'system' && event.subtype === 'init') {
        return typeof event.session_id === 'string' && sessionShape.test(event.session_id) ? [{ kind: 'session', id: event.session_id }] : [];
    }
    if (event.type === 'assistant') {
        const content = (event.message as { content?: unknown } | null)?.content;
        if (!Array.isArray(content)) return [];
        const out: HostLine[] = [];
        for (const block of content as { type?: unknown; text?: unknown; name?: unknown; input?: unknown }[]) {
            if (block?.type === 'text' && typeof block.text === 'string' && block.text.trim() !== '') out.push({ kind: 'text', text: block.text });
            else if (block?.type === 'tool_use') out.push({ kind: 'step', ...claudeStep(block.name, block.input) });
        }
        return out;
    }
    if (event.type === 'result') {
        const failed = event.is_error === true || (typeof event.subtype === 'string' && event.subtype !== 'success');
        if (!failed) return [{ kind: 'result', error: null }];
        const words = typeof event.result === 'string' && event.result.trim() !== '' ? event.result.trim() : `Claude Code ended the turn: ${String(event.subtype ?? 'error')}.`;
        return [{ kind: 'result', error: words }];
    }
    return [];
}

/** Codex's tool items, by type, as steps: a Map, like Claude Code's, so an inherited name is an item it does not know. */
const codexSteps: ReadonlyMap<string, (item: Record<string, unknown>) => Step> = new Map([
    ['command_execution', (item: Record<string, unknown>) => commandStep(item.command)],
    ['file_change', (item: Record<string, unknown>) => step('Changed files', pathsOf(item.changes))],
    ['web_search', (item: Record<string, unknown>) => step('Searched the web', item.query)],
    ['mcp_tool_call', (item: Record<string, unknown>) => step('Used a tool', item.tool)],
    ['todo_list', () => step('Updated its task list', undefined)],
]);

function pathsOf(changes: unknown): string | undefined {
    if (!Array.isArray(changes)) return undefined;
    const paths = changes.flatMap((change: unknown) => {
        const path = (change as { path?: unknown } | null)?.path;
        return typeof path === 'string' && path !== '' ? [path] : [];
    });
    return paths.length ? paths.join(', ') : undefined;
}

/**
 * One line of `codex exec --json`: the thread it runs as (`thread.started`), the agent's message and its tool items
 * (`item.started`, `item.completed`), and the end (`turn.completed`, `turn.failed`, `error`). A tool item is one step,
 * said when it starts; `seen` holds the items already said, so the item's completion says nothing again.
 */
export function codexLine(line: string, seen: Set<string>): HostLine[] {
    const event = parsed(line);
    if (event === null) return [];
    if (event.type === 'thread.started') {
        return typeof event.thread_id === 'string' && sessionShape.test(event.thread_id) ? [{ kind: 'session', id: event.thread_id }] : [];
    }
    if (event.type === 'item.started' || event.type === 'item.completed') {
        const item = event.item as ({ id?: unknown; type?: unknown; text?: unknown } & Record<string, unknown>) | null;
        if (!item || typeof item.type !== 'string') return [];
        if (item.type === 'agent_message') {
            return event.type === 'item.completed' && typeof item.text === 'string' && item.text.trim() !== '' ? [{ kind: 'text', text: item.text }] : [];
        }
        const stepOf = codexSteps.get(item.type);
        if (stepOf === undefined) return [];
        const id = typeof item.id === 'string' ? item.id : null;
        if (id !== null && seen.has(id)) return [];
        if (id !== null) seen.add(id);
        return [{ kind: 'step', ...stepOf(item) }];
    }
    if (event.type === 'turn.completed') return [{ kind: 'result', error: null }];
    if (event.type === 'turn.failed') {
        const message = (event.error as { message?: unknown } | null)?.message;
        return [{ kind: 'result', error: typeof message === 'string' && message.trim() !== '' ? message.trim() : 'Codex ended the turn with an error.' }];
    }
    if (event.type === 'error') {
        return [{ kind: 'result', error: typeof event.message === 'string' && event.message.trim() !== '' ? event.message.trim() : 'Codex ended the turn with an error.' }];
    }
    return [];
}
