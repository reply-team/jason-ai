// Pure: how one turn of the chat runs on Claude Code and on Codex. The command line each host is started with (the
// person's words never among it: they go on standard input), the environment it gets, and what each line the host
// prints means for the page. Nothing here starts a process or reads a file.

import { posix, win32 } from 'node:path';
import { sessionShape } from './chat-conversations.ts';
import { isEffort, type Effort } from './chat-models.ts';
import { allowRules, denyRules } from './chat-deny.ts';
import type { ChatMode } from './chat-modes.ts';
import { promptToolName } from './chat-permission-endpoint.ts';
import { confinedVariable, guardedNames, searchVariables, type Platform } from './chat-permission-model.ts';
import { withReplyClient } from './reply-client.ts';
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
    /** The plan list's verbs: the allow list's reads, which take its place in Plan only. */
    reads?: readonly string[] | undefined;
    /** The chat's mode; absent, Ask first. */
    mode?: ChatMode | undefined;
    /** Full access only: the turn's settings file (`fullAccessSettings`), which carries the turn's secret, so never argv. */
    settingsFile?: string | undefined;
    /** The host's file rules for the installation's data outside the chat folder (`dataRules`). */
    dataRules: readonly string[];
    model?: string | undefined;
    /** The effort the turn names (`--effort`); absent, the host keeps its own. */
    effort?: Effort | undefined;
    /** The session the previous turn of this chat reported; absent on a chat's first turn. */
    resume?: string | undefined;
}

/**
 * The settings every Claude Code turn carries on its command line. Claude Code runs its built-in read-only commands
 * (`cat`, `ls`, `grep` and the like) without asking, wherever the path they name; with reads outside the working folders
 * blocked, such a command on a path outside the chat folder and the added folders asks, and so reaches the permission
 * tool, as does a command whose folder the host cannot trace. It holds no secret, so it travels as inline JSON. The key
 * is read from Claude Code 2.1.257, the floor no turn starts below (`claudeFloor`); an older version would skip it.
 *
 * Every fetch asks too, so that each page reaches the permission model, which opens the documentation hosts' pages with
 * no card (`documentationHosts`) and puts a card in front of any other. Without the rule Claude Code would open a page of
 * its own preapproved set with no question at all. Anthropic,
 * https://code.claude.com/docs/en/tools-reference#webfetch-tool-behavior (read 2026-11-11): "WebFetch prompts before
 * fetching, except for domains your permission rules already allow or deny and a built-in set of preapproved
 * documentation domains that fetch without a prompt." and "An explicit `WebFetch(domain:...)` rule in `deny`, `ask`, or
 * `allow` takes precedence over the preapproved set, so you can block a preapproved domain or require a prompt for it.";
 * https://code.claude.com/docs/en/permissions#webfetch: "`WebFetch(domain:*)` matches every domain", and "Wildcards in
 * `WebFetch` rules require Claude Code v2.1.172 or later to match fetches.", below the floor.
 */
export const claudeTurnSettings = '{"permissions":{"blockReadsOutsideWorkingDirectories":true,"ask":["WebFetch(domain:*)"]}}';

/**
 * The settings of a turn in Plan only: those of every turn, and no classifier in planning, so a command outside Claude
 * Code's read-only set reaches the permission tool, which refuses it. Anthropic, https://code.claude.com/docs/en/settings
 * (read 2026-11-11), `useAutoModeDuringPlan` is honored as "`false` from any managed source, `--settings`, ..."; and
 * https://code.claude.com/docs/en/permission-modes: with auto mode not available, or `useAutoModeDuringPlan` off,
 * "commands outside the built-in read-only set prompt for approval".
 */
export const planTurnSettings = '{"permissions":{"blockReadsOutsideWorkingDirectories":true},"useAutoModeDuringPlan":false}';

/**
 * The settings of a turn in Full access, written to the turn's own file: those of every turn, and a `PreToolUse` hook on
 * every tool that posts the tool use to the turn's endpoint, which answers with the permission model's refusals alone.
 * Claude Code's deny rules hold under bypass but match a command as written only, so the hook reads what they cannot.
 * Anthropic, https://code.claude.com/docs/en/hooks (read 2026-11-11): `"deny"` "prevents the tool call"; a matcher of
 * `"*"` "fires on every occurrence of the event"; "To block the action instead, set `"onFailure": "block"` on a
 * `command` or `http` hook. Requires Claude Code v2.1.295 or later." (`fullAccessFloor`).
 */
export function fullAccessSettings(hookUrl: string, secret: string) {
    return {
        permissions: { blockReadsOutsideWorkingDirectories: true },
        // No project file can switch the hook off or filter it out. Anthropic, https://code.claude.com/docs/en/settings
        // (read 2026-11-12), of command-line settings: "A key you set there overrides the same key in your project and
        // user settings files", and "Lists merge instead of overriding", so the hook's address is always on the list.
        disableAllHooks: false,
        allowedHttpHookUrls: [hookUrl],
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
 * The host's own refusal of any Bash command that names `JASON_CONFINED`, anywhere in it: the variable confines the jason
 * the chat runs (`hostEnvironment`), and a command that names it could clear it. The rule is the exact spelling; the
 * permission model refuses the name in any letter case.
 */
export const confinedRule = `Bash(*${confinedVariable}*)`;

/** A session to resume, refused unless it has the shape a host names one in: it never stands where an option would. */
function resumable(session: string): string {
    if (!sessionShape.test(session)) throw new Error('That session is not one the desktop passes to a host.');
    return session;
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
 * The command line of one Claude Code turn, in the chat's mode. Ask first: no permission mode, the host's default. Plan
 * only: `--permission-mode plan`, the plan list's verbs in place of the allow list's, and no way out of planning. Full
 * access: `--permission-mode bypassPermissions` and the turn's settings file; Anthropic,
 * https://code.claude.com/docs/en/permission-modes (read 2026-11-11): "Deny rules block in every mode, including
 * `bypassPermissions`." and "Allow rules have no effect in `bypassPermissions`.", so every deny rule is passed as in Ask
 * first. Never `--bare`, which drops the subscription sign-in and the skills; always
 * `--strict-mcp-config`, so a person's own MCP servers never enter the chat; reads outside the working folders blocked
 * (`claudeTurnSettings`); each allowed and disallowed rule an argument of its own, the form the CLI reference gives,
 * so a comma in a data path never splits a rule; no prompt argument, so the prompt is read from standard input.
 */
export function claudeArguments(turn: ClaudeTurn): string[] {
    const mode = turn.mode ?? 'ask_first';
    if (mode === 'full_access' && !turn.settingsFile) throw new Error('A turn in Full access needs its settings file.');
    const args = ['-p', '--output-format', 'stream-json', '--verbose'];
    args.push('--allowedTools', ...claudeAllowedTools, ...allowRules(mode === 'plan_only' ? (turn.reads ?? []) : turn.allowed));
    args.push('--disallowedTools', ...claudeRemovedTools, ...(mode === 'plan_only' ? planRemovedTools : []), ...denyRules(turn.denied), confinedRule, ...searchRules, ...turn.dataRules);
    args.push('--setting-sources', 'project');
    args.push('--settings', mode === 'full_access' ? (turn.settingsFile as string) : mode === 'plan_only' ? planTurnSettings : claudeTurnSettings);
    if (mode !== 'ask_first') args.push('--permission-mode', mode === 'plan_only' ? 'plan' : 'bypassPermissions');
    for (const folder of turn.addDirs) args.push('--add-dir', folder);
    args.push('--permission-prompt-tool', promptToolName, '--mcp-config', turn.mcpConfig, '--strict-mcp-config');
    if (turn.model) args.push('--model', turn.model);
    if (turn.effort !== undefined) args.push('--effort', effortWord(turn.effort));
    if (turn.resume) args.push('--resume', resumable(turn.resume));
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

/**
 * The command line of one Codex turn: `codex exec` in its read-only sandbox, emitting JSON events, the prompt read from
 * standard input (`-`), resumed as `codex exec resume <thread>` from the chat's second turn on.
 *
 * Codex is this release's reading host. A Codex turn runs `exec --json --sandbox read-only` and asks no permission, so no
 * card and no fence of the desktop's applies to it: it reads the chat folder, the folders added to it and its skills in
 * `.agents/skills`, and answers. Its sandbox has no network by default and so may keep it from the runtime's loopback
 * address, so a Codex turn reads and advises and acts through nothing.
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
export function codexArguments(turn: { model?: string | undefined; effort?: Effort | undefined; resume?: string | undefined }): string[] {
    const args = ['exec', '--json', '--sandbox', 'read-only', '--skip-git-repo-check', ...codexSearchSettings.flatMap((setting) => ['-c', setting])];
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
 * whether they exist yet or not, so one made while a turn runs is refused as well.
 */
export const knownDataEntries: readonly string[] = ['config', 'run', 'state', 'logs', 'plugins', 'ui', 'work', 'skills', 'update', 'doctor', 'autostart'];

/**
 * The desktop folder's own entries but the chat folder, named the same way; `turns` holds every turn's configuration,
 * `rollback` the application an update kept aside and the record the next start acts on, `bundle` an AppImage's copy of
 * the jason it carries, `conversations` the record of every conversation, `models.json` the models Codex offered when
 * last asked, `claude-keyed` and `codex-keyed` the hosts' folders for the turns on a provider's key with those turns'
 * transcripts, `skills-records` what the desktop placed in the chat folder's skills folders, which these rules keep a Claude Code turn from reading or editing. A Codex turn has no fence of the desktop's: only its own read-only sandbox, which the desktop does
 * not narrow, holds it (`codexArguments`).
 */
export const knownDesktopEntries: readonly string[] = ['settings.json', 'provider-keys', 'electron', 'turns', 'rollback', 'bundle', 'conversations', 'claude-keyed', 'codex-keyed', 'models.json', 'skills-records'];

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
 * The host's own refusal of an edit of the chat's own names directly in the chat folder: its instructions, and its agent
 * settings and skills folders and what lies under them, which the desktop writes and keeps. They hold under bypass and in
 * every mode, beside the permission model's refusal of the same places by every route.
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
 * `CLAUDE_CODE_SUBPROCESS_ENV_SCRUB=1`, `CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1` and
 * `CLAUDE_CODE_DISABLE_BACKGROUND_TASKS=1`. Every host's turn carries `JASON_CONFINED=1`, whatever the desktop
 * inherited, so the jason it runs reads a request's content from standard input only and runs a provider check's
 * program by a bare name only; the desktop's own calls of jason do not carry it. Every host's turn also carries the
 * harness's label for reply-cli, `REPLY_CLIENT`, built from the bundle's version (`withReplyClient`), in place of any the
 * desktop inherited, and none at all when the build carries no bundle version. It is a label the reply-cli the host
 * runs may put in its User-Agent; nothing defends it. A turn that names an effort is started without an inherited
 * `CLAUDE_CODE_EFFORT_LEVEL` (`effortNamed`), which would override it.
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
        if (upper === 'JASON_DATA_DIR' || upper === confinedVariable) continue;
        env[name] = value;
    }
    const separator = options.platform === 'win32' ? ';' : ':';
    env.PATH = path === '' ? options.bundle : `${options.bundle}${separator}${path}`;
    if (options.data !== null) env.JASON_DATA_DIR = options.data;
    for (const [name, value] of Object.entries(options.provider)) env[name] = value;
    env[confinedVariable] = '1';
    if (options.program === 'claude') {
        env[scrub] = '1';
        env[maintainFolder] = '1';
        env[noBackground] = '1';
    }
    return withReplyClient(env, options.bundleVersion);
}

/** What one line a host prints means. */
export type HostLine =
    | { kind: 'session'; id: string }
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
        return [...session, { kind: 'key_source', source: keySourceOf(event.apiKeySource) }];
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
