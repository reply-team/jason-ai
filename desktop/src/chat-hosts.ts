// Pure: how one turn of the chat runs on Claude Code and on Codex. The command line each host is started with (the
// person's words never among it: they go on standard input), the environment it gets, and what each line the host
// prints means for the page. Nothing here starts a process or reads a file.

import { posix, win32 } from 'node:path';
import { denyRules } from './chat-deny.ts';
import { promptToolName } from './chat-permission-endpoint.ts';
import type { Platform } from './chat-permission-model.ts';
import { isSessionMarker } from './session-markers.ts';

/**
 * What Claude Code runs without asking anyone, beside what its own rules already let run: the skills, web search and
 * fetch, subagents (`Agent`, `Task` before the tool was renamed), the task list (`TodoWrite`, and the `Task*` tools that
 * replace it on newer models), every `jason` command the deny list does not name, and of `reply` exactly two read
 * commands. Every other tool use the host would ask about goes to the permission tool.
 */
export const claudeAllowedTools: readonly string[] = [
    'Skill',
    'WebSearch',
    'WebFetch',
    'Agent',
    'Task',
    'TodoWrite',
    'TaskCreate',
    'TaskGet',
    'TaskList',
    'TaskUpdate',
    'Bash(jason:*)',
    'Bash(reply auth status:*)',
    'Bash(reply account list:*)',
];

export interface ClaudeTurn {
    /** The turn's `--mcp-config` file, which names the permission endpoint. */
    mcpConfig: string;
    /** The campaign's and the project's folders, recorded and present. */
    addDirs: readonly string[];
    /** The deny list's commands. */
    denied: readonly string[];
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
 * (`claudeTurnSettings`); no prompt argument, so the prompt is read from standard input.
 */
export function claudeArguments(turn: ClaudeTurn): string[] {
    const args = ['-p', '--output-format', 'stream-json', '--verbose'];
    args.push('--allowedTools', claudeAllowedTools.join(','));
    args.push('--disallowedTools', [...denyRules(turn.denied), ...turn.dataRules].join(','));
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
 * The host's own rules for the installation's data: every entry of the data directory but `desktop`, and every entry of
 * `desktop` but the chat folder (the turn's own configuration among them), refused to its file tools and to the file
 * commands it recognises in Bash (`cat`, `head`, a redirection). Without them a read-only command such as `cat` reads
 * outside the working directories without asking; the permission model refuses the same paths when it is asked.
 */
export function dataRules(platform: Platform, data: string, dataEntries: readonly string[], desktopEntries: readonly string[]): string[] {
    const paths = platform === 'win32' ? win32 : posix;
    const desktop = paths.join(data, 'desktop');
    const targets = [
        ...dataEntries.filter((name) => name.toLowerCase() !== 'desktop').map((name) => paths.join(data, name)),
        ...desktopEntries.filter((name) => name.toLowerCase() !== 'chat').map((name) => paths.join(desktop, name)),
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
 * The host's environment: the desktop's own, with the bundled `jason`'s folder first on PATH, the data directory named
 * when the desktop runs with one, and a key provider's variables. Nothing of a plugin's configuration is passed on, and
 * `CLAUDE_AUTO_BACKGROUND_TASKS`, which would let the host move a waiting permission call to the background, is removed,
 * as are the markers of a Claude Code session the desktop itself was started from.
 */
export function hostEnvironment(
    base: Readonly<Record<string, string | undefined>>,
    options: { platform: Platform; bundle: string; data: string | null; provider: Readonly<Record<string, string>> },
): Record<string, string> {
    const env: Record<string, string> = {};
    let path = '';
    for (const [name, value] of Object.entries(base)) {
        if (value === undefined || withheld(name)) continue;
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
    return env;
}

/** What one line a host prints means. */
export type HostLine =
    | { kind: 'session'; id: string }
    | { kind: 'step'; text: string }
    | { kind: 'text'; text: string }
    /** The host said the turn is over: `error` null when it succeeded, else the host's own words. */
    | { kind: 'result'; error: string | null };

const verbWord = /^[a-z][a-z-]*$/;

/** A command as one step line: `jason <noun> <verb>` for a jason command, never any command's arguments. */
function commandStep(command: unknown): string {
    if (typeof command !== 'string') return 'Ran a command';
    const words = command.trim().split(/\s+/);
    if (words[0] === 'jason' && words.length >= 3 && verbWord.test(words[1] ?? '') && verbWord.test(words[2] ?? '')) return `jason ${words[1]} ${words[2]}`;
    if (words[0] === 'jason' && words.length === 2 && verbWord.test(words[1] ?? '')) return `jason ${words[1]}`;
    return 'Ran a command';
}

const claudeSteps: Readonly<Record<string, string>> = {
    Read: 'Read a file',
    Write: 'Wrote a file',
    Edit: 'Changed a file',
    MultiEdit: 'Changed a file',
    NotebookEdit: 'Changed a notebook',
    Glob: 'Looked through files',
    Grep: 'Looked through files',
    LS: 'Looked through files',
    Skill: 'Used a skill',
    WebSearch: 'Searched the web',
    WebFetch: 'Read a web page',
    Agent: 'Started a subagent',
    Task: 'Started a subagent',
    TodoWrite: 'Updated its task list',
    TaskCreate: 'Updated its task list',
    TaskUpdate: 'Updated its task list',
};

/** A tool use as one step line, in the host's words for what it did. */
export function claudeStep(name: unknown, input: unknown): string {
    if (name === 'Bash' || name === 'PowerShell') return commandStep((input as { command?: unknown } | null)?.command);
    if (typeof name === 'string' && name in claudeSteps) return claudeSteps[name] ?? 'Used a tool';
    return 'Used a tool';
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
            else if (block?.type === 'tool_use') out.push({ kind: 'step', text: claudeStep(block.name, block.input) });
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

const codexSteps: Readonly<Record<string, string>> = {
    file_change: 'Changed files',
    web_search: 'Searched the web',
    mcp_tool_call: 'Used a tool',
    todo_list: 'Updated its task list',
};

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
        const item = event.item as { id?: unknown; type?: unknown; text?: unknown; command?: unknown } | null;
        if (!item || typeof item.type !== 'string') return [];
        if (item.type === 'agent_message') {
            return event.type === 'item.completed' && typeof item.text === 'string' && item.text.trim() !== '' ? [{ kind: 'text', text: item.text }] : [];
        }
        const step = item.type === 'command_execution' ? commandStep(item.command) : codexSteps[item.type];
        if (step === undefined) return [];
        const id = typeof item.id === 'string' ? item.id : null;
        if (id !== null && seen.has(id)) return [];
        if (id !== null) seen.add(id);
        return [{ kind: 'step', text: step }];
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
