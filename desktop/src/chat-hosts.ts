// Pure: how one turn of the chat runs on Claude Code and on Codex. The command line each host is started with (the
// person's words never among it: they go on standard input), the environment it gets, and what each line the host
// prints means for the page. Nothing here starts a process or reads a file.

import { posix, win32 } from 'node:path';
import { sessionShape } from './chat-conversations.ts';
import { isEffort, type Effort } from './chat-models.ts';
import { allowRules, denyRules, helpNouns, helpRules, withoutRequestFiles } from './chat-deny.ts';
import type { ChatMode } from './chat-modes.ts';
import { isTurnServerName, promptToolName } from './chat-permission-endpoint.ts';
import { attemptVariable, chatTurnVariable, confinedVariable, guardedNames, replyNoCardForms, searchVariables, type Platform } from './chat-permission-model.ts';
import { compareVersions, noChromeFrom } from './host-version.ts';
import { replyClientVariable, withReplyClient } from './reply-client.ts';
import { isSessionMarker, sessionMarkers } from './session-markers.ts';

/**
 * What Claude Code runs without asking anyone, beside what its own rules already let run and the allow list's jason verbs
 * (`allowRules`): the skills, web search, subagents (`Agent`, `Task` before the tool was renamed), the task list
 * (`TodoWrite`, and the `Task*` tools that replace it on newer models), and of `reply` two read commands in their exact forms. Every
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
    // Of reply, the exact forms alone (`replyNoCardForms`): a rule with no `:*` matches the command as written and no
    // longer one.
    ...replyNoCardForms.map((form) => `Bash(${form})`),
];

/**
 * The tools taken out of a turn by a bare rule, which removes a tool from the model's context. PowerShell is on by
 * default on Windows for claude.ai accounts with Git Bash, and Monitor runs a command in the background; the fence (the
 * deny rules and the `jason`/`reply` allow rules) names the Bash tool, so with both gone the one shell a turn has is the
 * fenced one. Git Bash is Claude Code's own prerequisite on Windows. A turn in Full access, the host's own bypass, keeps
 * the PowerShell tool as a terminal's session does (`fullAccessRemovedTools`): the turn's hook reads its commands as it
 * reads Bash's. The permission model reads any tool input that carries a command as a shell command.
 */
export const claudeRemovedTools: readonly string[] = ['PowerShell', 'Monitor'];

/** The tools taken out of a turn in Full access: Monitor alone, which runs a command in the background. */
export const fullAccessRemovedTools: readonly string[] = ['Monitor'];

/**
 * The permission mode each mode names to Claude Code, by name every time, so that a `defaultMode` of the person's or a
 * project's never decides it: Full access the host's own bypass, Ask first and Auto-edit its default, Plan only plan.
 */
export const permissionModes: Readonly<Record<ChatMode, string>> = { full_access: 'bypassPermissions', ask_first: 'default', auto_edit: 'default', plan_only: 'plan' };

export interface ClaudeTurn {
    /** The turn's `--mcp-config` file, which names the permission endpoint. */
    mcpConfig: string;
    /** The turn's own name for its permission server, the one its MCP file names (`newServerName`): the prompt tool is on it. */
    server: string;
    /** The campaign's and the project's folders, recorded and present. */
    addDirs: readonly string[];
    /** The deny list's commands. */
    denied: readonly string[];
    /** The allow list's verbs. */
    allowed: readonly string[];
    /** The plan list's verbs: the allow list's reads, which take its place in Plan only. */
    reads?: readonly string[] | undefined;
    /** The chat's mode; absent, Ask first. */
    mode?: ChatMode | undefined;
    /**
     * A turn the hook rides: its settings file (`hookedSettings`), which carries the turn's secret, so never argv. Full
     * access always has one; Ask first and Auto-edit have one from the floor of Full access on, and the inline settings
     * below it.
     */
    settingsFile?: string | undefined;
    /** The host's file rules for the installation's data outside the chat folder (`dataRules`). */
    dataRules: readonly string[];
    model?: string | undefined;
    /** The effort the turn names (`--effort`); absent, the host keeps its own. */
    effort?: Effort | undefined;
    /** The session the previous turn of this chat reported; absent on a chat's first turn. */
    resume?: string | undefined;
    /**
     * Whether the resume forks (`--fork-session`): Claude Code then writes this start into a session of a new id and leaves
     * the one resumed as it was. Read only with `resume`.
     */
    fork?: boolean | undefined;
    /** The host's version as read before the turn, at or above the floor (`claudeFloor`): it decides a sealed turn's `--no-chrome`. */
    version: string;
    /** A turn on a provider's key, which keeps the sealed line in every mode, Full access included. */
    keyed?: boolean | undefined;
}

/**
 * What the settings of every turn that does not run in Full access carry so that nothing of the person's own tools
 * reaches it: the claude.ai connectors off, the browser's built-in server denied by name, and the shell commands a skill
 * carries not run. They stand beside `--setting-sources project`, `--strict-mcp-config` and `--no-chrome`
 * (`claudeArguments`), and the first two are the only defences of their kind on a Claude Code older than `noChromeFrom`,
 * which skips a key it does not know.
 * Anthropic, https://code.claude.com/docs/en/settings-reference (read 2026-11-12): `disableClaudeAiConnectors` "Turn off
 * claude.ai connectors so Claude Code doesn't fetch them" and `deniedMcpServers` "Block specific MCP servers by URL,
 * command, or name", both set in "Any file"; https://code.claude.com/docs/en/settings: `disableClaudeAiConnectors` is
 * honored as "`true` from any scope", and `--settings` "can set any key your user settings file can set". The browser's
 * server is `claude-in-chrome`, the name its tools are called by. https://code.claude.com/docs/en/skills (read
 * 2026-11-12): with `disableSkillShellExecution` set, "each command is replaced with `[shell command execution disabled by
 * policy]`". A turn that runs in Full access on the person's own sign-in carries none of them: it loads the person's own
 * configuration as a terminal does. A turn on a provider's key carries them in every mode.
 */
function serverDefences() {
    return { disableClaudeAiConnectors: true, deniedMcpServers: [{ serverName: 'claude-in-chrome' }], disableSkillShellExecution: true };
}

/**
 * The settings an Ask first or Auto-edit turn carries on its command line where the hook does not ride it, below Claude
 * Code's floor of Full access; a turn the hook rides carries its own (`hookedSettings`). Claude Code runs its built-in
 * read-only commands (`cat`, `ls`, `grep` and the like) without asking, wherever the path they name; with reads outside
 * the working folders blocked, such a command on a path outside the chat folder and the added folders asks, and so
 * reaches the permission tool, as does a command whose folder the host cannot trace. It holds no secret, so it travels as
 * inline JSON. The key is read from Claude Code 2.1.257, the floor no turn starts below (`claudeFloor`); an older version
 * would skip it.
 *
 * Every fetch asks too, so that each page reaches the permission model, which opens the documentation hosts' pages with
 * no card (`documentationHosts`) and puts a card in front of any other. Without the rule Claude Code would open a page of
 * its own preapproved set with no question at all. Anthropic,
 * https://code.claude.com/docs/en/tools-reference#webfetch-tool-behavior (read 2026-11-11): "WebFetch prompts before
 * fetching, except for domains your permission rules already allow or deny and a built-in set of preapproved
 * documentation domains that fetch without a prompt." and "An explicit `WebFetch(domain:...)` rule in `deny`, `ask`, or
 * `allow` takes precedence over the preapproved set, so you can block a preapproved domain or require a prompt for it.";
 * https://code.claude.com/docs/en/permissions#webfetch: "`WebFetch(domain:*)` matches every domain", and "Wildcards in
 * `WebFetch` rules require Claude Code v2.1.172 or later to match fetches.", below the floor. These are the settings of
 * an Ask first or Auto-edit turn the hook does not ride, below the floor of Full access; they carry the server defences
 * (`serverDefences`), as the settings of every turn that does not run in Full access do.
 */
export const claudeTurnSettings = JSON.stringify({ permissions: { blockReadsOutsideWorkingDirectories: true, ask: ['WebFetch(domain:*)'] }, ...serverDefences() });

/**
 * The settings of a turn in Plan only: reads outside the working folders blocked, as every turn's the hook does not ride
 * are; no classifier in planning, so a command outside Claude Code's read-only set reaches the permission tool, which
 * refuses it; and the server defences (`serverDefences`), as every turn's that does not run in Full access. Anthropic, https://code.claude.com/docs/en/settings
 * (read 2026-11-11), `useAutoModeDuringPlan` is honored as "`false` from any managed source, `--settings`, ..."; and
 * https://code.claude.com/docs/en/permission-modes: with auto mode not available, or `useAutoModeDuringPlan` off,
 * "commands outside the built-in read-only set prompt for approval".
 */
export const planTurnSettings = JSON.stringify({ permissions: { blockReadsOutsideWorkingDirectories: true }, useAutoModeDuringPlan: false, ...serverDefences() });

/**
 * The settings of a turn the hook rides, written to the turn's own file: in Ask first and Auto-edit the rule that every
 * page asks (`claudeTurnSettings`) and the server defences (`serverDefences`), in Full access no rule, and no server
 * defence but on a provider's key (`keyed`), whose turn is sealed in every mode; and a `PreToolUse` hook on every tool that posts the tool use to the turn's endpoint, which answers a refusal
 * of the three kinds or nothing (`HookAnswer`). Reads outside the working folders are not blocked: the hook refuses the
 * three kinds wherever a tool names them, so a read anywhere else runs in Full access, and in Ask first and Auto-edit
 * reaches the prompt tool, which runs it with no card, and a change outside the working folders is the one card
 * (`ChatScope.hooked`). With the block, Claude Code refused such a read before it asked anyone, and an edit, which needs
 * a read of the file first, died with it. Claude Code's deny rules match a command as written only, so the hook reads
 * what they cannot, and a refusal of it stands in front of an allow rule. Anthropic,
 * https://code.claude.com/docs/en/hooks (read 2026-11-11): `"deny"` "prevents the tool call", and "Deny and ask rules are
 * still evaluated regardless of what the hook returns"; a matcher of `"*"` "fires on every occurrence of the event"; "To
 * block the action instead, set `"onFailure": "block"` on a `command` or `http` hook. Requires Claude Code v2.1.295 or
 * later."
 *
 * A turn that runs in Full access loads the person's own configuration, their hooks among it, which run beside this one;
 * a refusal of this one stands whatever theirs answer. Anthropic, https://code.claude.com/docs/en/hooks (read
 * 2026-11-20): "When multiple PreToolUse hooks return different decisions, precedence is `deny` > `defer` > `ask` >
 * `allow`." The hook sees the tool use as the model asked for it; a hook of the person's own that rewrites the input is
 * their configuration acting. An Ask first or Auto-edit turn the hook rides is sealed as Plan only's (`claudeArguments`).
 *
 * The file also sets, as `env`, what the desktop decides of the turn's environment (`pinnedEnvironment`), given as `env`,
 * in every mode the hook rides: a settings file of the person's, which a Full access turn loads, or a project's, which
 * every turn loads, could otherwise put its own PATH or its own value of a turn's variable in place of the desktop's.
 */
export function hookedSettings(mode: Exclude<ChatMode, 'plan_only'>, hookUrl: string, secret: string, turn: { env?: Readonly<Record<string, string>>; keyed?: boolean } = {}) {
    return {
        ...(mode === 'full_access' ? {} : { permissions: { ask: ['WebFetch(domain:*)'] } }),
        ...(turn.env === undefined ? {} : { env: { ...turn.env } }),
        // No settings file of the person's or a project's can switch the hook off or filter it out. Anthropic,
        // https://code.claude.com/docs/en/settings (read 2026-11-12), of command-line settings: "A key you set there
        // overrides the same key in your project and user settings files", and "Lists merge instead of overriding", so the
        // hook's address is always on the list.
        disableAllHooks: false,
        allowedHttpHookUrls: [hookUrl],
        ...(mode === 'full_access' && turn.keyed !== true ? {} : serverDefences()),
        hooks: {
            PreToolUse: [
                {
                    matcher: '*',
                    hooks: [{ type: 'http', url: hookUrl, headers: { Authorization: `Bearer ${secret}` }, timeout: 30, onFailure: 'block' }],
                },
            ],
        },
    };
}

/**
 * The host's own refusal of any Bash command that names `JASON_CONFINED`, anywhere in it: Jason alone sets its own
 * variables (`hostEnvironment`), and a command that names one could set or clear it. The rule is the exact spelling; the
 * permission model refuses the name in any letter case.
 */
export const confinedRule = `Bash(*${confinedVariable}*)`;

/** The host's own refusal of any Bash command that names `JASON_CHAT_TURN`, anywhere in it, as `confinedRule` is. */
export const chatTurnRule = `Bash(*${chatTurnVariable}*)`;

/**
 * The host's own refusal of any Bash command that names `JASON_ATTEMPT_ID`, anywhere in it: the variable names the
 * role's attempt a jason runs inside, and a chat turn is never one, so a command that names it could only claim one. The
 * rule is the exact spelling; the permission model refuses the name in any letter case.
 */
export const attemptRule = `Bash(*${attemptVariable}*)`;

/** A session to resume, refused unless it has the shape a host names one in: it never stands where an option would. */
function resumable(session: string): string {
    if (!sessionShape.test(session)) throw new Error('That session is not one the desktop passes to a host.');
    return session;
}

/**
 * The turn's own name for its permission server, refused unless it is one the desktop gives a turn's server
 * (`turnServerName`): never the fixed name an earlier version used, which a server of the person's own may carry, nor
 * anything that would stand where an option would.
 */
function ownServer(server: string): string {
    if (!isTurnServerName(server)) throw new Error('That server is not one the desktop names a turn\'s permission tool on.');
    return server;
}

/**
 * An effort, refused unless it is one of the five words (`efforts`): never `ultracode` nor `ultra`, nor anything that
 * would stand where an option would.
 */
function effortWord(effort: Effort): string {
    if (!isEffort(effort)) throw new Error('That effort is not one the desktop passes to a host.');
    return effort;
}

/**
 * The host's own refusal of any Bash command that assigns a variable the shell finds a program by (`searchVariables`),
 * anywhere in it, plainly or by `+=`: such a command could start a `jason` the agent wrote. The host's documentation
 * says such an assignment asks; these rules refuse it whether or not it would. They also refuse a longer name that ends
 * the same way (`PYTHONPATH=`), with no card; the permission model refuses the other spellings.
 */
export const searchRules: readonly string[] = searchVariables.flatMap((name) => [`Bash(*${name}=*)`, `Bash(*${name}+=*)`]);

/**
 * Taken out of a turn in Plan only by a bare rule: the host's own way out of planning, so only the person leaves Plan only.
 * Anthropic, https://code.claude.com/docs/en/permission-modes (read 2026-11-11): approving a plan "switches the session to
 * the permission mode each approve option describes".
 */
export const planRemovedTools: readonly string[] = ['ExitPlanMode'];

/**
 * The command line of one Claude Code turn, in the chat's mode, its permission mode named every time
 * (`permissionModes`). Ask first: the host's default, with the turn's settings file, whose hook refuses three kinds
 * before the host's own rules (`hookedSettings`), from the floor of Full access; below it, the inline settings and no
 * hook. Auto-edit: exactly Ask first's, never the host's accept-edits mode; its six file commands reach the prompt tool
 * as any other command does, and the desktop's judgement lets them run there (`autoEditJudgement`). Plan only: plan,
 * which on macOS and Windows Claude Code meets by starting in its default mode, since the turn carries the scrub
 * (`scrub`); the plan list's verbs in place of the allow list's, its inline settings, and no way out of planning. In every mode the
 * nouns' help runs too, exactly as written (`helpRules`). Full access: the host's own bypass, with the turn's settings
 * file and its hook, and the PowerShell tool kept (`fullAccessRemovedTools`); what the host asks about in that mode
 * itself reaches the prompt tool. Below the floor, where no hook rides an Ask first or Auto-edit turn to read where a
 * request's content comes from, the host is given no allow rule for a form of a verb that reads one from a file or from
 * standard input (`withoutRequestFiles`), so each reaches the prompt tool, which refuses a redirect from the
 * installation's own data and runs a plain form with no card, as the rule would. Every deny rule is passed in every mode; Anthropic,
 * https://code.claude.com/docs/en/permission-modes (read 2026-11-11): "Deny rules block in every mode". The rule against
 * a jason that starts with an option is passed only where no hook rides the turn (`denyRules`): where one does, such a
 * command reaches the prompt tool, which runs jason's own help and version and refuses any other. Never `--bare`, which
 * drops the subscription sign-in and the skills.
 *
 * A turn that runs in Full access on the person's own sign-in loads the person's own Claude Code configuration, as a
 * terminal does: no `--setting-sources`, no `--strict-mcp-config`, no `--no-chrome`, so their settings, rules and hooks,
 * their skills, their MCP servers, their claude.ai connectors and the browser integration reach it; the permission mode
 * named every time overrides their `defaultMode` (https://code.claude.com/docs/en/cli-reference, read 2026-11-20:
 * `--permission-mode` "Overrides `defaultMode` from settings files"). Every other turn keeps the sealed command line whole:
 * a turn on a provider's key in every mode, Full access included, beside the desktop's own Claude Code folder
 * (`keyedFolder`); Plan only, Ask first and Auto-edit, and the turn of a Full access chat that runs in Ask first, below the floor of Full access, from a
 * browser tab, as root, started again after Claude Code refused its bypass or started the turn in another mode, or once
 * the hook was found filtered out: which line a turn gets follows the mode it runs in (`turnMode`, and `running.mode`
 * once a start is narrowed), never the one the chat shows. The sealed line is `--setting-sources project`,
 * `--strict-mcp-config`, and from `noChromeFrom` on `--no-chrome`, so none of the person's settings, servers, connectors
 * or browser reaches the turn (its settings' `serverDefences` hold below that version). In every mode the turn's own
 * `--mcp-config` names the prompt tool's server, under the turn's own name for it (`ownServer`), beside the person's
 * servers in Full access, so a question the host asks reaches the prompt tool and never hangs a `-p` turn; reads outside
 * the working folders blocked where no hook rides the turn (`claudeTurnSettings`, `planTurnSettings`), and not where one
 * does (`hookedSettings`); each allowed and disallowed rule an argument of its own, the form the CLI reference gives, so
 * a comma in a data path never splits a rule; no prompt argument, so the prompt is read from standard input.
 */
export function claudeArguments(turn: ClaudeTurn): string[] {
    const mode = turn.mode ?? 'ask_first';
    if (mode === 'full_access' && !turn.settingsFile) throw new Error('A turn in Full access needs its settings file.');
    const sealed = mode !== 'full_access' || turn.keyed === true;
    const args = ['-p', '--output-format', 'stream-json', '--verbose'];
    // The hook rides every turn given a settings file of its own, and Plan only, which carries its inline settings, never.
    const hooked = mode !== 'plan_only' && turn.settingsFile !== undefined;
    const allowed = mode === 'plan_only' ? (turn.reads ?? []) : hooked ? turn.allowed : withoutRequestFiles(turn.allowed);
    args.push('--allowedTools', ...claudeAllowedTools, ...allowRules(allowed), ...helpRules(helpNouns(turn.allowed, turn.reads ?? [])));
    const removed = mode === 'full_access' ? fullAccessRemovedTools : claudeRemovedTools;
    args.push('--disallowedTools', ...removed, ...(mode === 'plan_only' ? planRemovedTools : []), ...denyRules(turn.denied, hooked), confinedRule, ...searchRules, attemptRule, chatTurnRule, ...turn.dataRules);
    if (sealed) args.push('--setting-sources', 'project');
    args.push('--settings', mode === 'plan_only' ? planTurnSettings : (turn.settingsFile ?? claudeTurnSettings));
    args.push('--permission-mode', permissionModes[mode]);
    for (const folder of turn.addDirs) args.push('--add-dir', folder);
    args.push('--permission-prompt-tool', promptToolName(ownServer(turn.server)), '--mcp-config', turn.mcpConfig);
    if (sealed) {
        args.push('--strict-mcp-config');
        if (compareVersions(turn.version, noChromeFrom) >= 0) args.push('--no-chrome');
    }
    if (turn.model) args.push('--model', turn.model);
    if (turn.effort !== undefined) args.push('--effort', effortWord(turn.effort));
    if (turn.resume) {
        args.push('--resume', resumable(turn.resume));
        if (turn.fork === true) args.push('--fork-session');
    }
    return args;
}

/**
 * What every Codex chat turn sets for its search: the top-level key at `cached`, and beside it the two legacy toggles
 * the reference gives a meaning next to that key, both off, so that a Codex older than the key, which the person's own
 * legacy setting would otherwise send searching live, searches no more than the cache. A current Codex ignores both
 * while the key is set. OpenAI, https://developers.openai.com/codex/config-reference (read 2026-11-11):
 * `features.web_search_cached` "Deprecated legacy toggle. When `web_search` is unset, true maps to `web_search =
 * "cached"`." and `features.web_search_request` "Deprecated legacy toggle. When `web_search` is unset, true maps to
 * `web_search = "live"`." Its other two keys are left alone: `features.web_search`, "Deprecated legacy toggle; prefer the
 * top-level `web_search` setting.", says nothing of how it stands beside the key, and `tools.web_search`, "Optional web
 * search tool configuration", is no toggle of the mode. No floor is held for Codex instead, since OpenAI's changelog,
 * https://developers.openai.com/codex/changelog (read 2026-11-11), names no version for the key: its entry of 2026-01-28,
 * "Web search is now enabled by default", says "To disable this behavior or switch modes, use the web_search
 * configuration option" and names none.
 */
export const codexSearchSettings: readonly string[] = ['web_search=cached', 'features.web_search_request=false', 'features.web_search_cached=false'];

/** The setting every Windows Codex turn runs under (`codexArguments`): the elevated sandbox, set for the run. */
export const codexWindowsSandbox = 'windows.sandbox="elevated"';

/**
 * The settings every Codex turn on a platform sets for the run by `-c`, before the model and the effort: on Windows the
 * sandbox's kind, then the search settings, then the person's ChatGPT connectors off (`codexConnectorsOff`). The
 * Windows sandbox check runs under the same set (`codex-sandbox.ts`).
 */
export function codexTurnSettings(platform: Platform): string[] {
    return [...(platform === 'win32' ? [codexWindowsSandbox] : []), ...codexSearchSettings, codexConnectorsOff];
}

/**
 * The person's ChatGPT apps (connectors) off for the run: they are no part of config.toml, so `--ignore-user-config`
 * leaves them on, and a Codex turn asks no one before one acts. The Codex counterpart of Claude Code's
 * `disableClaudeAiConnectors`, and what the naming run sets too (`codexToolsOff`). OpenAI,
 * https://developers.openai.com/codex/config-reference: `features.apps` "Enable app (connector) integrations (stable;
 * on by default)."
 */
export const codexConnectorsOff = 'features.apps=false';

/**
 * The settings a Full access Codex turn sets for the run by `-c`: the search and the connectors as every turn has them
 * (`codexTurnSettings`), and no sandbox's kind: with `danger-full-access` no sandbox runs, so there is none to choose.
 */
export const codexFullAccessSettings: readonly string[] = [...codexSearchSettings, codexConnectorsOff];

/**
 * The variables a Codex turn that reads the person's config.toml pins for the commands it runs, so that a
 * `shell_environment_policy` there (`inherit = "core"` or `"none"`, or an exclude) cannot keep from a command the data
 * directory the desktop runs on or the turn's mark, and the `jason` it runs act on another installation or read a file
 * an option names. PATH is never pinned: `set` would replace the person's whole PATH. openai/codex 9f2b87495b, read and
 * not seen: session flags are merged over config.toml table by table (config/src/merge.rs,
 * `merge_toml_values_at_path`), so `-c shell_environment_policy.set.<NAME>=<value>` adds a name to the person's `set`;
 * `populate_env` (protocol/src/shell_environment.rs) applies `set` after `inherit`, the default excludes and `exclude`,
 * and before `include_only`, so a policy that keeps only listed names, by `include_only` or an include filter, still
 * removes them, as it removes the bundle's folder from PATH.
 */
export const codexPinnedVariables: readonly string[] = ['JASON_DATA_DIR', 'JASON_CHAT_TURN'];

/** The `-c` settings that pin `codexPinnedVariables` the environment holds, each value a TOML basic string. */
function codexPinned(environment: Readonly<Record<string, string>>): string[] {
    return codexPinnedVariables.flatMap((name) => {
        const value = environment[name];
        return value === undefined ? [] : [`shell_environment_policy.set.${name}=${JSON.stringify(value)}`];
    });
}

/**
 * The command line of one Codex turn: `codex exec` emitting JSON events, the prompt read from standard input (`-`),
 * resumed as `codex exec resume <thread>` from the chat's second turn on. In Full access it runs with no sandbox
 * (`--sandbox danger-full-access`) and reads the person's own `config.toml`, as a terminal's Codex does; in every other
 * mode it runs in its read-only sandbox, as below. Codex in `exec` never asks a person, so it has no Ask first: a turn
 * either only reads or acts unasked, and nothing of the desktop's fence or refusals holds on it.
 *
 * A Full access turn runs `exec --json --sandbox danger-full-access` and asks no permission: no card, no fence and no
 * refusal of the desktop's applies to it, the runtime's run file and the turn settings included, and it acts with the
 * person's own rights and network. It reads the person's `config.toml`, its MCP servers, providers and profiles with
 * it, as Codex in a terminal does; the search and the connectors are still set for the run (`codexFullAccessSettings`).
 *
 * A turn in any other mode runs `exec --json --sandbox read-only` and asks no permission, so no card and no fence of the
 * desktop's applies to it either: it reads the chat folder, the folders added to it and its skills in `.agents/skills`,
 * and answers. Its sandbox has no network by default and so may keep it from the runtime's loopback address, so such a
 * turn reads and proposes and acts through nothing.
 *
 * Such a turn leaves the person's `config.toml` unread (`--ignore-user-config`), so none of the MCP servers it names,
 * and none of its providers or profiles, reach the chat: Codex asks no one before a tool of a server runs. Its sign-in
 * is still read from `CODEX_HOME`. The flag is Codex's from `codexFloor` on, and the chat runs no turn on an older one
 * (`codexFloorSentence`).
 *
 * On Windows such a turn names its sandbox's kind for the run, `elevated` (`codexWindowsSandbox`), never the default and
 * never `unelevated`: the person's own choice is in the file the turn leaves unread, and a command in the unelevated
 * sandbox can read the installation's data directory. OpenAI, https://learn.chatgpt.com/docs/windows/windows-sandbox:
 * "`elevated`: ... Requires administrator-approved setup." and "`unelevated`: ... doesn't support denied read paths."
 * On macOS and Linux no kind is named.
 *
 * An effort is the configuration key `model_reasoning_effort`, set for the run by `-c`. OpenAI,
 * https://developers.openai.com/codex/cli/reference, `exec`: "-c, --config key=value Inline configuration override for
 * the non-interactive run (repeatable)." and "Values parse as TOML if possible; otherwise the literal string is used."
 *
 * Every turn searches the web, from OpenAI's cached index and never live: a Codex turn asks no permission, so a page it
 * opened live would be one no card was shown for, and the cached index opens none. Set for the run, so the person's own
 * configuration neither turns the search off nor makes it live. OpenAI
 * (read 2026-11-11), https://developers.openai.com/codex/config-reference: `web_search` "disabled | cached | indexed |
 * live" "Web search mode (default: `"cached"`; cached uses an OpenAI-maintained index without external web access;
 * indexed permits external access only when gated by the search index; if you use `--yolo` or another full access
 * sandbox setting, it defaults to `"live"`). Use `"live"` for unrestricted live retrieval, or `"disabled"` to remove the
 * tool.";
 * https://developers.openai.com/codex/web-search: "Cached mode uses an OpenAI-maintained index instead of fetching
 * arbitrary pages live, which lowers—but doesn’t remove—prompt injection risk. Web search is a hosted tool, separate
 * from sandboxed local command networking. It does not use the permission profile’s network proxy or domain allowlist,
 * and it can remain available when command network access is disabled." A search is a step (`codexLine`); the item
 * types https://developers.openai.com/codex/noninteractive names, "agent messages, reasoning, command executions, file
 * changes, MCP tool calls, web searches, and plan updates", hold no fetch of a page. The naming run keeps its search off
 * (`codexToolsOff`). The settings are `codexSearchSettings`.
 */
export function codexArguments(turn: {
    platform: Platform;
    mode?: ChatMode | undefined;
    model?: string | undefined;
    effort?: Effort | undefined;
    resume?: string | undefined;
    /** The turn's environment, whose `codexPinnedVariables` a turn that reads the person's config.toml pins. */
    environment?: Readonly<Record<string, string>> | undefined;
}): string[] {
    const args =
        turn.mode === 'full_access'
            ? [
                  'exec',
                  '--json',
                  '--sandbox',
                  'danger-full-access',
                  '--skip-git-repo-check',
                  ...[...codexFullAccessSettings, ...codexPinned(turn.environment ?? {})].flatMap((setting) => ['-c', setting]),
              ]
            : ['exec', '--json', '--sandbox', 'read-only', '--skip-git-repo-check', '--ignore-user-config', ...codexTurnSettings(turn.platform).flatMap((setting) => ['-c', setting])];
    if (turn.model) args.push('--model', turn.model);
    if (turn.effort !== undefined) args.push('-c', `model_reasoning_effort=${effortWord(turn.effort)}`);
    if (turn.resume) args.push('resume', resumable(turn.resume));
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
 * whether they exist yet or not, so one made while a turn runs is refused as well. `setup` is where a later layout keeps
 * the roles, the plugins and the settings a person sets up, and `layout.json` the stamp that says so: both are refused
 * before any build makes them. A test holds this list to `JasonPaths.OwnEntries` as `desktop/data-layouts.json` carries
 * it, with `doctor` added and `desktop` taken out.
 */
export const knownDataEntries: readonly string[] = ['config', 'run', 'state', 'logs', 'plugins', 'ui', 'work', 'skills', 'update', 'doctor', 'autostart', 'setup', 'layout.json'];

/**
 * The desktop folder's own entries but the chat folder, named the same way; `turns` holds every turn's configuration,
 * `rollback` the application an update kept aside and the record the next start acts on, `bundle` an AppImage's copy of
 * the jason it carries, `conversations` the record of every conversation, `models.json` the models Codex offered when
 * last asked, `claude-keyed` and `codex-keyed` the hosts' folders for the turns on a provider's key with those turns'
 * transcripts, `skills-records` what the desktop placed in the chat folder's skills folders, `chats` every
 * conversation's own folder, `full-access.json` and `full-access-taken-back` what an earlier desktop kept of a standing
 * choice of Full access, removed at each start, and `mode` the record of which installation the desktop runs on, which
 * these rules keep a Claude Code turn from reading or editing. A Codex turn has no fence of the desktop's: outside Full
 * access only its own read-only sandbox, which the desktop does not narrow, holds it, and in Full access nothing does
 * (`codexArguments`).
 */
export const knownDesktopEntries: readonly string[] = ['settings.json', 'provider-keys', 'electron', 'turns', 'rollback', 'bundle', 'conversations', 'conversations-stamped', 'claude-keyed', 'codex-keyed', 'models.json', 'skills-records', 'chats', 'chat-full', 'chat-full-ended', 'chat-full-records', 'full-access.json', 'full-access-taken-back', 'window-pairing.json', 'mode', 'demo-start-over', 'machine-setup.json'];

/** The known names, then every name that is there and is not one of them, spelled as it is. */
function namesOf(known: readonly string[], present: readonly string[], kept: string): string[] {
    return [...new Set([...known, ...present])].filter((name) => name.toLowerCase() !== kept);
}

/**
 * The host's own rules for the installation's data: every entry of the data directory but `desktop`, and every entry of
 * `desktop` but the turn's chat folder (the turns' configurations among them), the known ones whether they exist or not,
 * refused to its file tools. The host's maker documents that it checks a redirection's target, and a file command it
 * recognizes in Bash (`cat`, `head`), against these rules as if its own file tools read or wrote that file (Anthropic,
 * https://code.claude.com/docs/en/permissions, read 2026-11-20). Where no hook rides the turn, in Plan only and, below
 * Claude Code's floor of Full access, for a command an allow rule lets run with no card (the allow list's verbs and the
 * forms of the person's word), the rules' hold of a redirect rests on that documentation: it is documented by the host's
 * maker and not observed by us, since no test of the real host has seen it. A command an allow rule matches is never put to the permission model. The turn's `PreToolUse` hook
 * holds it (`hookedSettings`): in Full access, and in Ask first and Auto-edit from Claude Code's floor of Full access on,
 * it refuses such a command before the host's allow rules let it run. The permission model refuses the same paths when
 * it is asked. In a demo run
 * the person's own directories (`whole`: the person's data directory and harness root) are refused whole, the folder and
 * everything under it, with no entry left open.
 */
export function dataRules(
    platform: Platform,
    data: string,
    dataEntries: readonly string[],
    desktopEntries: readonly string[],
    own?: { id: string; present: readonly string[] },
    whole: readonly string[] = [],
): string[] {
    const paths = platform === 'win32' ? win32 : posix;
    const desktop = paths.join(data, 'desktop');
    // A conversation with a folder of its own runs its turns there, below `chats`: the shared chat folder is then closed
    // to it as every other entry is, and so is every other conversation's folder; `chats` itself is the one entry left
    // open, for its own folder. A conversation an earlier desktop made runs in the shared chat folder, and `chats` is
    // closed to it whole.
    const targets = [
        ...namesOf(knownDataEntries, dataEntries, 'desktop').map((name) => paths.join(data, name)),
        ...namesOf(knownDesktopEntries, desktopEntries, own === undefined ? 'chat' : 'chats').map((name) => paths.join(desktop, name)),
        ...(own === undefined ? [] : [paths.join(desktop, 'chat'), ...own.present.filter((name) => name !== own.id).map((name) => paths.join(desktop, 'chats', name))]),
        ...whole,
    ];
    // On macOS each folder is also named under the data volume's prefix, which shows the same folder (a firmlink).
    const spellings = platform === 'darwin' ? targets.flatMap((target) => [target, posix.join('/System/Volumes/Data', posix.resolve(target))]) : targets;
    return spellings.flatMap((target) => {
        const rule = rulePath(platform, target);
        return [`Read(${rule})`, `Read(${rule}/**)`, `Edit(${rule})`, `Edit(${rule}/**)`];
    });
}

/**
 * The host's own refusal of an edit of the chat's own names directly in the chat folder: its instructions, and its agent
 * settings and skills folders and what lies under them, which the desktop writes and keeps. They hold in every mode but
 * Full access, where the turn's folder is its own, a conversation's own and the shared chat folder alike, and is put
 * right again before the next turn: its instructions written afresh and its agent folders holding only what the desktop
 * placed (`ownAgentFolder`).
 */
export function chatRules(platform: Platform, chat: string): string[] {
    const paths = platform === 'win32' ? win32 : posix;
    return guardedNames
        .filter((guarded) => guarded.chatsOwn !== undefined)
        .flatMap((guarded) => {
            const rule = rulePath(platform, paths.join(chat, guarded.name));
            return guarded.chatsOwn === 'configuration' ? [`Edit(${rule})`, `Edit(${rule}/**)`] : [`Edit(${rule})`];
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
 * What a person's environment says about which provider and credentials each host uses, and where the host keeps its
 * sign-in. A turn on a key provider is started without them, so the provider the person picked is the one the turn
 * reaches: an inherited `CLAUDE_CODE_USE_BEDROCK` or `OPENAI_BASE_URL` would otherwise send it elsewhere, and an
 * inherited `CLAUDE_CONFIG_DIR` or `CODEX_HOME` would stand where the desktop's own folder goes (`keyedFolder`). A plain
 * Claude Code or Codex turn keeps them, as the person's own configuration.
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
        'CLAUDE_CONFIG_DIR',
        'CLAUDE_CODE_PROJECT_DIR_NAME',
        // Anthropic, https://code.claude.com/docs/en/authentication#anthropic-profiles-and-federation-credentials (read
        // 2026-11-11): a named profile, "`ANTHROPIC_PROFILE`", and the federation variables, "`ANTHROPIC_FEDERATION_RULE_ID`
        // and `ANTHROPIC_ORGANIZATION_ID`, both set", and "the other variables in the WIF reference", which
        // https://platform.claude.com/docs/en/manage-claude/wif-reference#environment-variables lists.
        'ANTHROPIC_PROFILE',
        'ANTHROPIC_FEDERATION_RULE_ID',
        'ANTHROPIC_ORGANIZATION_ID',
        'ANTHROPIC_SERVICE_ACCOUNT_ID',
        'ANTHROPIC_IDENTITY_TOKEN_FILE',
        'ANTHROPIC_IDENTITY_TOKEN',
        'ANTHROPIC_WORKSPACE_ID',
    ]),
    // OpenAI, https://developers.openai.com/codex/config-file/environment-variables, "Authentication and network" (read
    // 2026-11-11): its credential and identity variables are CODEX_API_KEY, CODEX_ACCESS_TOKEN ("Provides a ChatGPT or
    // Codex access token for trusted automation."), OPENAI_FEDERATION_RULE_ID, OPENAI_IDENTITY_TOKEN_FILE and
    // OPENAI_WORKLOAD_IDENTITY_CONTEXT (workload identity); the CA bundle variables there are no credential. Beside them,
    // OPENAI_API_KEY and OPENAI_BASE_URL, and CODEX_HOME and CODEX_SQLITE_HOME, where Codex keeps its state.
    codex: new Set([
        'OPENAI_API_KEY',
        'OPENAI_BASE_URL',
        'CODEX_API_KEY',
        'CODEX_ACCESS_TOKEN',
        'OPENAI_FEDERATION_RULE_ID',
        'OPENAI_IDENTITY_TOKEN_FILE',
        'OPENAI_WORKLOAD_IDENTITY_CONTEXT',
        'CODEX_HOME',
        'CODEX_SQLITE_HOME',
    ]),
};

/**
 * Set on every Claude Code turn on macOS and Windows but a Full access turn: the host strips credentials from the
 * environment of the commands, hooks and stdio servers it starts (a key provider's key among them), so the key reaches the
 * host's own process and not a read-only `echo` that runs without a card. Never set in Full access, on any system, nor
 * passed on there when the desktop inherited it: while the variable is set, Claude Code starts in its default mode
 * whatever mode the start asks for, its bypass included, and says so on its standard error ("Permission mode forced to
 * default — CLAUDE_CODE_SUBPROCESS_ENV_SCRUB is set", seen on 2.1.329), so no Full access turn would run in its bypass.
 * Plan only keeps it, so on macOS and Windows a Plan only turn asks for Claude Code's plan mode and starts in its default
 * mode, the same line said (seen on 2.1.329), and Jason's own rules hold the turn: the plan list, the deny rules, no way
 * out of planning, `JASON_CONFINED` and the desktop's refusal of every other request.
 * Never set on Linux either, nor passed on there when the desktop inherited it:
 * there the variable runs every command inside Claude Code's sandbox (https://code.claude.com/docs/en/sandboxing, read
 * 2026-11-16: "A Linux session with `CLAUDE_CODE_SUBPROCESS_ENV_SCRUB` set: every command runs sandboxed"), and the
 * sandbox shuts `jason` off from the runtime twice. Under `blockReadsOutsideWorkingDirectories`, which every turn the hook
 * does not ride sets (`claudeTurnSettings`, `planTurnSettings`), it denies reads of the home directory (the settings
 * reference, "Sandboxed commands under the block"), so Jason's data directory and the runtime's descriptor in it are
 * hidden; and "a sandboxed command's
 * `localhost` is private to that command", so the runtime's loopback address is out of reach too. `socat` is only the
 * sandbox's network relay, which stock Ubuntu lacks, and the PID namespace the variable also brings on Linux is a
 * separate effect. In Full access, and on Linux in every mode, a key and the credentials the turn carries reach the
 * commands the agent runs, as in a terminal session.
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
 * Withheld from a turn that names an effort: Anthropic, https://code.claude.com/docs/en/env-vars (read 2026-11-11), of
 * `CLAUDE_CODE_EFFORT_LEVEL`: "Takes precedence over `--effort`, `/effort`, and the `modelSettings` and `effortLevel`
 * settings.", so an inherited one would stand for the person's choice.
 */
const effortLevel = 'CLAUDE_CODE_EFFORT_LEVEL';

/**
 * The host's environment: the desktop's own, with the bundled `jason`'s folder first on PATH, the data directory named
 * when the desktop runs with one, and a key provider's variables. Nothing of a plugin's configuration is passed on, and
 * `CLAUDE_AUTO_BACKGROUND_TASKS`, which would let the host move a waiting permission call to the background, is removed,
 * as are the markers of a Claude Code session the desktop itself was started from. A key provider's turn is started
 * without the provider and credentials the desktop inherited (`inheritedProvider`); a Claude Code turn always carries
 * `CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1` and `CLAUDE_CODE_DISABLE_BACKGROUND_TASKS=1`, and off Linux outside Full
 * access `CLAUDE_CODE_SUBPROCESS_ENV_SCRUB=1` (`scrub`, which says why not in Full access nor on Linux). Every host's turn carries
 * `JASON_CHAT_TURN=1`, in every mode, whatever the desktop inherited, so the jason it runs reads a request's content from
 * standard input only and runs a provider check's program by a bare name only; every turn that can only read and propose
 * carries `JASON_CONFINED=1` beside it, Plan only on any host and every Codex turn outside Full access (Ask first, and
 * Auto-edit, which runs as it), so its jason also refuses the verbs of a person's word and of the installation, and no
 * other turn carries `JASON_CONFINED`, not even one the desktop inherited. The desktop's own calls of jason carry neither. No host's turn
 * carries a `JASON_ATTEMPT_ID` the desktop inherited: a chat turn is never a role's attempt. Every
 * host's turn also carries the harness's label for reply-cli, `REPLY_CLIENT`, built from the bundle's version (`withReplyClient`), in place of any the
 * desktop inherited, and none at all when the build carries no bundle version. It is a label the reply-cli the host
 * runs may put in its User-Agent; nothing defends it. A turn that names an effort is started without an inherited
 * `CLAUDE_CODE_EFFORT_LEVEL` (`effortNamed`), which would override it. What it decides holds against a settings file's
 * `env` too: a Claude Code turn the hook rides sets the same values in its own settings file (`pinnedEnvironment`), which
 * outranks the person's and a project's, and every other Claude Code turn loads no settings file of the person's
 * (`claudeArguments`).
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
        /** The bundle's version, from which the label for reply-cli is built; null when the build carries none. */
        bundleVersion: string | null;
        /** Whether the turn names an effort, which an inherited `CLAUDE_CODE_EFFORT_LEVEL` would override. */
        effortNamed?: boolean;
        /**
         * The turn's mode, which the start it is given to runs in: a turn that can only read and propose sets
         * `JASON_CONFINED`, Plan only on any host and every Codex turn outside Full access, and a Full access turn of Claude
         * Code carries no `CLAUDE_CODE_SUBPROCESS_ENV_SCRUB` (`scrub`). Absent, a run that is no turn of a mode: no
         * `JASON_CONFINED`, and the scrub as in every mode but Full access.
         */
        mode?: ChatMode;
        /**
         * A Full access turn: the folder its judgement reads a `~` against (`ChatScope.shellHome`), set as `HOME` in place
         * of any spelling of it the base carries, so the host's shell and the judgement take the same home. That folder is
         * the desktop's own environment's HOME, or the profile's home folder (`homedir()`) when the environment names none.
         * Where the environment's HOME is another folder than the profile's, the judgement holds every `~`.
         */
        home?: string;
    },
): Record<string, string> {
    const env: Record<string, string> = {};
    let path = '';
    const replaced = options.keyed ? inheritedProvider[options.program] : new Set<string>();
    for (const [name, value] of Object.entries(base)) {
        if (value === undefined || withheld(name)) continue;
        const upper = name.toUpperCase();
        if (replaced.has(upper) || upper === scrub || upper === maintainFolder || upper === noBackground) continue;
        if (options.effortNamed === true && upper === effortLevel) continue;
        if (name.toUpperCase() === 'PATH') {
            path = value;
            continue;
        }
        if (upper === 'JASON_DATA_DIR' || upper === confinedVariable || upper === chatTurnVariable || upper === attemptVariable) continue;
        if (options.home !== undefined && (options.platform === 'win32' ? upper : name) === 'HOME') continue;
        env[name] = value;
    }
    if (options.home !== undefined) env.HOME = options.home;
    const separator = options.platform === 'win32' ? ';' : ':';
    env.PATH = path === '' ? options.bundle : `${options.bundle}${separator}${path}`;
    if (options.data !== null) env.JASON_DATA_DIR = options.data;
    for (const [name, value] of Object.entries(options.provider)) env[name] = value;
    env[chatTurnVariable] = '1';
    const readsOnly = options.mode === 'plan_only' || (options.program === 'codex' && options.mode !== undefined && options.mode !== 'full_access');
    if (readsOnly) env[confinedVariable] = '1';
    if (options.program === 'claude') {
        // The one place the scrub is decided, by the system and the turn's mode: never on Linux, never in Full access.
        if (options.platform !== 'linux' && options.mode !== 'full_access') env[scrub] = '1';
        env[maintainFolder] = '1';
        env[noBackground] = '1';
    }
    return withReplyClient(env, options.bundleVersion);
}

/**
 * What the settings file of a Claude Code turn the hook rides sets of the turn's environment (`hookedSettings`): for each
 * name the desktop decides for the turn (`hostEnvironment`), the value the turn's environment holds, or an empty string for
 * a name it withholds. Claude Code writes a settings file's `env` over the environment it was started with, and the
 * turn's own file outranks the person's user, project and local files name by name, so no file of the person's or a
 * project's puts another PATH, another `JASON_CHAT_TURN` or a session's marker in place of the desktop's. Anthropic,
 * https://code.claude.com/docs/en/env-vars (read 2026-11-21): "When the same variable is set in both your shell and a
 * settings file `env` block, the settings file value applies in most sessions. Claude Code writes each `env` entry into
 * the process environment, replacing the value inherited from the shell."; https://code.claude.com/docs/en/settings-reference:
 * "when more than one settings file sets a variable, the highest-precedence one applies", and of command-line settings,
 * https://code.claude.com/docs/en/settings: "A key you set there overrides the same key in your project and user settings
 * files". The names: PATH, the data directory, Jason's three variables of a turn, the folder and background variables,
 * the backgrounding of tool calls and a session's markers it withholds, the label for reply-cli, `HOME` where the turn
 * names one, and `CLAUDE_CODE_EFFORT_LEVEL` where the turn names an effort: every name `hostEnvironment` sets or
 * withholds by name but the two below, which a row holds to what it does. `CLAUDE_CODE_SUBPROCESS_ENV_SCRUB` is left out: where a file
 * of the person's sets it in Full access, Claude Code starts in another mode and the turn is started again in Ask first,
 * which carries it. A provider's variables are left out too, so no key is written to a file: a turn on a provider's key
 * loads none of the person's settings (`claudeArguments`). An empty value is no value to the jason the turn runs, which
 * reads a blank data directory as none.
 */
export function pinnedEnvironment(environment: Readonly<Record<string, string>>, options: { home?: string; effortNamed?: boolean }): Record<string, string> {
    const named = (name: string) => Object.entries(environment).find(([each]) => each.toUpperCase() === name)?.[1] ?? '';
    const names = [
        'PATH',
        'JASON_DATA_DIR',
        chatTurnVariable,
        confinedVariable,
        attemptVariable,
        maintainFolder,
        noBackground,
        'CLAUDE_AUTO_BACKGROUND_TASKS',
        ...sessionMarkers,
        replyClientVariable,
        ...(options.home !== undefined ? ['HOME'] : []),
        ...(options.effortNamed === true ? [effortLevel] : []),
    ];
    return Object.fromEntries(names.map((name) => [name, named(name)]));
}

/** What one line a host prints means. */
export type HostLine =
    | { kind: 'session'; id: string }
    /** The permission mode Claude Code says the turn runs in, on its `system/init` line, as one word. */
    | { kind: 'permission_mode'; mode: string }
    /** Claude Code began to use a tool: its id and name, said before the step that shows it. */
    | { kind: 'tool_use'; id: string; name: string }
    /** A tool's result came back to Claude Code, by the id of its use: the tool ran, or was refused. */
    | { kind: 'tool_result'; id: string }
    /** Where Claude Code says the turn's key came from (`keySourceOf`). */
    | { kind: 'key_source'; source: KeySource }
    /**
     * Claude Code said a request was refused for its credential: a retry of a 401 or 403, or a failed authentication;
     * `forbidden` when it was a 403 alone, which a proxy or a denied resource answers as well.
     */
    | { kind: 'refused'; forbidden?: true }
    | ({ kind: 'step' } & Step)
    | { kind: 'text'; text: string }
    /** Codex said an administrator's requirements put its sandbox below the one the turn asked for (`codexNarrowed`). */
    | { kind: 'sandbox_narrowed' }
    /**
     * The host said the turn is over: `error` null when it succeeded, else the host's own words; `refused` when it ended
     * on a 401 or 403, and `forbidden` too when on a 403.
     */
    | { kind: 'result'; error: string | null; refused?: true; forbidden?: true };

/**
 * Where Claude Code says a turn's key came from, the `apiKeySource` of its `system/init` line, as a closed word. The
 * page, https://code.claude.com/docs/en/agent-sdk/typescript#apikeysource (read 2026-11-11): "Where the API key for the
 * session's requests came from, reported as `apiKeySource` on the `SDKSystemMessage` init message." `type ApiKeySource =
 * "ANTHROPIC_API_KEY" | "apiKeyHelper" | "/login managed key" | "none" | "user" | "project" | "org" | "temporary" |
 * "oauth";` and "Claude Code reports one of four values:"
 *   | `ANTHROPIC_API_KEY`  | The key in the `ANTHROPIC_API_KEY` environment variable |
 *   | `apiKeyHelper`       | The key returned by your `apiKeyHelper` command |
 *   | `/login managed key` | The key Claude Code stored when you logged in with a Claude Console account |
 *   | `none`               | No API key. The session authenticates another way, such as a claude.ai login, a bearer token, or a cloud provider |
 * and "The type also keeps `user`, `project`, `org`, `temporary`, and `oauth` so older code still compiles, and Claude
 * Code doesn't report them."
 * - `environment`: `ANTHROPIC_API_KEY`, the variable the desktop puts the key in.
 * - `elsewhere`: a value whose row says the requests do not go on that key: a helper's key, a Console login's key, or
 *   `none`, whose row says the session authenticates another way; and `oauth`, kept for older code, whose name says a
 *   login.
 * - `unexplained`: `user`, `project`, `org`, `temporary`, kept for older code and not reported, with no row of their own.
 * - `unlisted`: a string the page does not list; `absent`: no string at all.
 * Only `elsewhere` stops a turn; the rest are logged by their word and the turn goes on, since the turn's folder holds no
 * claude.ai login to fall back to (`keyedFolder`): a check that refused every key turn because one string changed would
 * be worse than none.
 */
export type KeySource = 'environment' | 'elsewhere' | 'unexplained' | 'unlisted' | 'absent';

const keySources: ReadonlyMap<string, KeySource> = new Map([
    ['ANTHROPIC_API_KEY', 'environment'],
    ['apiKeyHelper', 'elsewhere'],
    ['/login managed key', 'elsewhere'],
    ['none', 'elsewhere'],
    ['oauth', 'elsewhere'],
    ['user', 'unexplained'],
    ['project', 'unexplained'],
    ['org', 'unexplained'],
    ['temporary', 'unexplained'],
]);

export function keySourceOf(value: unknown): KeySource {
    if (typeof value !== 'string') return 'absent';
    return keySources.get(value) ?? 'unlisted';
}


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

/**
 * One line of Claude Code's `stream-json` output: the session it runs as and where its key came from (`system`/`init`),
 * a retry of a refused credential (`system`/`api_retry`), the agent's words and tool uses (`assistant`), and the end
 * (`result`). Of a tool's own output (`user`), only which tool use it answers, by its id; anything unreadable says nothing.
 *
 * What a refused credential looks like, as Anthropic states it (read 2026-11-11):
 * - https://code.claude.com/docs/en/headless#handle-api-retries: "When an API request fails with a retryable error,
 *   Claude Code emits a `system/api_retry` event before retrying", with `error_status`, "HTTP status code of the failed
 *   attempt", and `error`, a category among which is `authentication_failed`.
 * - https://code.claude.com/docs/en/agent-sdk/typescript#sdkresultmessage: `api_error_status`, "the HTTP status code of
 *   the API error that terminated the conversation"; and an assistant message's `error`, one of which is
 *   `'authentication_failed'`.
 */
export function claudeLine(line: string): HostLine[] {
    const event = parsed(line);
    if (event === null) return [];
    if (event.type === 'system' && event.subtype === 'init') {
        const session: HostLine[] = typeof event.session_id === 'string' && sessionShape.test(event.session_id) ? [{ kind: 'session', id: event.session_id }] : [];
        // The mode the turn runs in: a Full access turn whose host turned its bypass off says so here alone, as seen on 2.1.329.
        // It comes first, so a start stopped for it names no session.
        const mode: HostLine[] = typeof event.permissionMode === 'string' && /^[A-Za-z]{1,32}$/.test(event.permissionMode) ? [{ kind: 'permission_mode', mode: event.permissionMode }] : [];
        return [...mode, ...session, { kind: 'key_source', source: keySourceOf(event.apiKeySource) }];
    }
    if (event.type === 'system' && event.subtype === 'api_retry') {
        if (event.error === 'authentication_failed' || event.error_status === 401) return [{ kind: 'refused' }];
        return event.error_status === 403 ? [{ kind: 'refused', forbidden: true }] : [];
    }
    if (event.type === 'assistant') {
        const content = (event.message as { content?: unknown } | null)?.content;
        const out: HostLine[] = event.error === 'authentication_failed' ? [{ kind: 'refused' }] : [];
        if (!Array.isArray(content)) return out;
        for (const block of content as { type?: unknown; text?: unknown; name?: unknown; input?: unknown }[]) {
            if (block?.type === 'text' && typeof block.text === 'string' && block.text.trim() !== '') out.push({ kind: 'text', text: block.text });
            else if (block?.type === 'tool_use') {
                const id = (block as { id?: unknown }).id;
                if (typeof id === 'string' && typeof block.name === 'string') out.push({ kind: 'tool_use', id, name: block.name });
                out.push({ kind: 'step', ...claudeStep(block.name, block.input) });
            }
        }
        return out;
    }
    if (event.type === 'user') {
        const content = (event.message as { content?: unknown } | null)?.content;
        if (!Array.isArray(content)) return [];
        return (content as { type?: unknown; tool_use_id?: unknown }[]).flatMap((block) =>
            block?.type === 'tool_result' && typeof block.tool_use_id === 'string' ? [{ kind: 'tool_result' as const, id: block.tool_use_id }] : [],
        );
    }
    if (event.type === 'result') {
        const failed = event.is_error === true || (typeof event.subtype === 'string' && event.subtype !== 'success');
        if (!failed) return [{ kind: 'result', error: null }];
        const words = typeof event.result === 'string' && event.result.trim() !== '' ? event.result.trim() : `Claude Code ended the turn: ${String(event.subtype ?? 'error')}.`;
        if (event.api_error_status === 403) return [{ kind: 'result', error: words, refused: true, forbidden: true }];
        return [event.api_error_status === 401 ? { kind: 'result', error: words, refused: true } : { kind: 'result', error: words }];
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
 * Codex's warning that an administrator's requirements (`requirements.toml`) do not allow the sandbox the turn asked for,
 * so it runs one they allow. openai/codex at 9f2b87495b, read and not seen: core/src/config/mod.rs,
 * `apply_requirement_constrained_value`, says "Configured value for `{field_name}` is disallowed by requirements; falling
 * back to required value {fallback_value:?}. Details: {err}", the sandbox's field being `permission_profile` there and
 * `sandbox_mode` in an earlier Codex; exec/src/event_processor_with_jsonl_output.rs says such a configuration warning as
 * an `item.completed` whose item is of type `error`, with that `message`. `codex exec` forces `approval_policy = "never"`
 * (exec/src/lib.rs), so a forbidden `danger-full-access` makes building the configuration fail instead, and Codex
 * refuses to start (`codexFullAccessRefused`); this narrowing happens only under an automatic reviewer (AutoReview).
 * Every other warning of Codex's is dropped.
 */
const codexNarrowed = /^Configured value for `(?:permission_profile|sandbox_mode)` is disallowed by requirements/;

/**
 * One line of `codex exec --json`: the thread it runs as (`thread.started`), the agent's message and its tool items
 * (`item.started`, `item.completed`), and the end (`turn.completed`, `turn.failed`, `error`). A tool item is one step,
 * said when it starts; `seen` holds the items already said, so the item's completion says nothing again.
 *
 * No line of Codex's is read as a refused key. OpenAI, https://developers.openai.com/codex/noninteractive (read
 * 2026-11-11), says only "Event types include thread.started, turn.started, turn.completed, turn.failed, item.*, and
 * error."; it names no status, code or type an event carries for a refused key. So a keyed Codex turn ends in Codex's own
 * words, which pass `withoutKey` before the page or the record has them (`chat.ts`).
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
        if (item.type === 'error') {
            return event.type === 'item.completed' && typeof item.message === 'string' && codexNarrowed.test(item.message) ? [{ kind: 'sandbox_narrowed' }] : [];
        }
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
