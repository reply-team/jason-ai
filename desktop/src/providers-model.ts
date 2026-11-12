/**
 * Pure: the agent hosts and the AI providers the desktop shows. Every host found on the machine is listed. Claude Code
 * and Codex drive the chat; every other host runs campaign work, not the chat yet, and is never swapped for another one
 * behind a person's back. The providers that take a key run through Claude Code or Codex with that key.
 */

import { join } from 'node:path';
import type { Effort } from './chat-models.ts';
import type { DesktopPaths } from './paths.ts';
import type { ProgramName } from './programs.ts';

export interface Host {
    /** What the page names the host by: the command that runs it. */
    id: ProgramName;
    label: string;
    program: ProgramName;
    /** Whether the chat can be driven by it. */
    drivesChat: boolean;
}

export const hosts: readonly Host[] = [
    { id: 'claude', label: 'Claude Code', program: 'claude', drivesChat: true },
    { id: 'codex', label: 'Codex', program: 'codex', drivesChat: true },
    { id: 'gemini', label: 'Gemini CLI', program: 'gemini', drivesChat: false },
    { id: 'agy', label: 'Antigravity CLI', program: 'agy', drivesChat: false },
    { id: 'cursor-agent', label: 'Cursor CLI', program: 'cursor-agent', drivesChat: false },
    { id: 'copilot', label: 'GitHub Copilot CLI', program: 'copilot', drivesChat: false },
];

/** What the card says of a host found that cannot drive the chat. */
export const notTheChatYet = 'Runs campaign work, not the chat yet.';

/** The one sentence a found host gets about signing in: the desktop never signs in to a host itself. */
export function signInSentence(program: ProgramName): string {
    return `Sign in once by running \`${program}\` in a terminal; the chat uses that sign-in.`;
}

export interface KeyProvider {
    /** The provider's id in this closed table; it also names the provider's Claude Code folder (`keyedFolder`). */
    id: string;
    label: string;
    /** The host whose program runs it. */
    program: 'claude' | 'codex';
    /** The variable the host reads the key from. */
    keyVariable: string;
    /** An endpoint that speaks Anthropic's API, which Claude Code is pointed at instead of Anthropic's own. */
    baseUrl?: string;
    models: readonly string[];
    /**
     * The effort levels every model of the table takes, where the vendor's guide names them; absent where it names none,
     * and for the two providers whose models are Claude Code's or the list kept of Codex's (`offeredModels`).
     */
    efforts?: readonly Effort[];
}

const compatible = (id: string, label: string, baseUrl: string, models: readonly string[], efforts?: readonly Effort[]): KeyProvider => ({
    id,
    label,
    program: 'claude',
    keyVariable: 'ANTHROPIC_AUTH_TOKEN',
    baseUrl,
    models,
    ...(efforts === undefined ? {} : { efforts }),
});

/**
 * The key providers. Where a vendor's own page says what this table takes from it (read 2026-11-11):
 * - OpenAI, https://developers.openai.com/codex/noninteractive (served as
 *   https://learn.chatgpt.com/docs/non-interactive-mode): "To use a different API key for a single run, set
 *   CODEX_API_KEY inline: CODEX_API_KEY=<api-key> codex exec --json ..." and "You can use CODEX_API_KEY with codex exec,
 *   codex review, the TypeScript SDK, and codex exec-server --remote."
 * - Z.ai, https://docs.z.ai/devpack/tool/claude, its Claude Code settings: "ANTHROPIC_DEFAULT_SONNET_MODEL" :
 *   "glm-5.3[1m]", "ANTHROPIC_DEFAULT_OPUS_MODEL" : "glm-5.3[1m]", "ANTHROPIC_DEFAULT_HAIKU_MODEL" : "glm-5.3-flash[1m]".
 * - MiniMax, https://platform.minimax.io/docs/m-plan/claude-code: "ANTHROPIC_MODEL" : "MiniMax-M3.1-Flash[1m]", and
 *   "Set all model names to MiniMax-M3.1-Flash".
 * The `[1m]` suffix asks for a one-million-token window, which both guides set beside `CLAUDE_CODE_AUTO_COMPACT_WINDOW`;
 * the desktop names the models without it.
 * - DeepSeek, https://api-docs.deepseek.com/quick_start/pricing: "Use deepseek-flash as the model name. The legacy names
 *   deepseek-v4-flash and deepseek-v4-flash-vision-exp are still accepted, but the corresponding models have been
 *   retired" (`currentModel` reads a kept `deepseek-v4-flash` as `deepseek-flash`).
 * The effort levels, only where a vendor's guide names them; Claude Code passes them as `--effort`:
 * - MiniMax, the page above: "Choose from low, medium, high, xhigh, or max." and "You can also start with claude --effort
 *   <level>, which applies to that session only."
 * - DeepSeek, https://api-docs.deepseek.com/guides/thinking_mode: `{"output_config": {"effort": "low/high/max"}}`.
 * - Kimi's guide names one value only, "Set to max; lower values may reduce quality on complex tasks", and Z.ai's none,
 *   so neither offers a level.
 */
export const keyProviders: readonly KeyProvider[] = [
    { id: 'claude-api-key', label: 'Anthropic', program: 'claude', keyVariable: 'ANTHROPIC_API_KEY', models: ['opus', 'sonnet', 'haiku', 'fable'] },
    { id: 'codex-api-key', label: 'OpenAI', program: 'codex', keyVariable: 'CODEX_API_KEY', models: [] },
    compatible('deepseek', 'DeepSeek', 'https://api.deepseek.com/anthropic', ['deepseek-v4-pro', 'deepseek-flash'], ['low', 'high', 'max']),
    compatible('moonshot', 'Kimi (Moonshot)', 'https://api.moonshot.ai/anthropic', ['kimi-k3']),
    compatible('zai', 'GLM (Z.ai)', 'https://api.z.ai/api/anthropic', ['glm-5.3', 'glm-5.3-flash']),
    compatible('minimax', 'MiniMax', 'https://api.minimax.io/anthropic', ['MiniMax-M3.1-Flash'], ['low', 'medium', 'high', 'xhigh', 'max']),
];

export function keyProvider(id: unknown): KeyProvider | undefined {
    return keyProviders.find((provider) => provider.id === id);
}

/**
 * The folder a turn on a provider's key runs its host with, the desktop's own and holding no login of the person's, so a
 * key the host does not take is meant to fail the turn instead of running it on the person's own plan (as built; not yet
 * seen with a real host):
 * - Claude Code, `<data>/desktop/claude-keyed/<provider id>/` as `CLAUDE_CONFIG_DIR`. Anthropic,
 *   https://code.claude.com/docs/en/authentication: "Each directory has its own settings, session history, and ...
 *   login or API key." So no claude.ai login is in the folder. The same page goes on: "Separate directories don't keep
 *   two Claude Console sign-ins without an API key apart, because Claude Code stores that kind of sign-in outside the
 *   configuration directory." A Console profile kept under the person's own home is not moved by the folder; the
 *   environment's key ranks above it ("Anthropic profile and federation credentials" are sixth in the page's
 *   precedence, the key in the environment second or third), and the variables that name a profile or a federation are
 *   withheld from a keyed turn (`hostEnvironment`). One folder per provider: a conversation keeps one session per host
 *   id, the provider's id for a key provider (`chat-conversations.ts`), and that session's transcript is in this
 *   provider's folder alone, so the two agree, and a session of one provider is never resumed on another's model.
 * - Codex, `<data>/desktop/codex-keyed/` as `CODEX_HOME`. OpenAI,
 *   https://developers.openai.com/codex/config-file/environment-variables (served as
 *   https://learn.chatgpt.com/docs/config-file/environment-variables): "Sets the root for Codex state, including
 *   config, auth, logs, sessions, skills, and standalone package metadata. If you set it, the directory must already
 *   exist." OpenAI is its one key provider.
 */
export function keyedFolder(paths: Pick<DesktopPaths, 'claudeKeyed' | 'codexKeyed'>, provider: KeyProvider): string {
    return provider.program === 'claude' ? join(paths.claudeKeyed, provider.id) : paths.codexKeyed;
}

/** The word a part of a key is replaced by in a host's error text (`withoutKey`). */
export const keyWord = '[key]';

/** A token shaped as a provider's key, masked or not: a key prefix, a run of asterisks, a JSON web token, a long secret. */
const keyShaped = [/^(?:sk|rk|pk)[-_]/i, /\*{3,}/, /^eyJ[A-Za-z0-9_-]{8,}/, /[A-Za-z0-9_-]{24,}/];

/**
 * A host's error text on a turn on a provider's key, with every token that holds any part of the key replaced by one fixed
 * word (`keyWord`): a token holding the key itself, its first six or its last four characters, or the shape of a
 * provider's key, masked or not (OpenAI's error for a wrong key names it with its last four characters after asterisks,
 * https://developers.openai.com/api/docs/guides/error-codes: "401 - Incorrect API key provided"). A token is a run of
 * characters between spaces, quotes and brackets; one mark that ends a sentence after it is kept. Every error
 * text a keyed turn shows or keeps passes here first; a plain turn's never does.
 */
export function withoutKey(text: string, key: string): string {
    const pieces = key.length >= 10 ? [key, key.slice(0, 6), key.slice(-4)] : key.length > 0 ? [key] : [];
    return text.replace(/[^\s"'`<>()[\]{},;]+/g, (run) => {
        // One mark that ends a sentence is kept; dots in a row are a mask's, part of the token.
        const end = /(?<![.:!?])[.:!?]$/.exec(run);
        const core = end && end.index > 0 ? run.slice(0, end.index) : run;
        const after = run.slice(core.length);
        const holds = pieces.some((piece) => core.includes(piece)) || keyShaped.some((shape) => shape.test(core));
        return holds ? `${keyWord}${after}` : run;
    });
}

/**
 * What a host says while a keyed turn runs, its words and its steps, with the whole key, exactly as it is, replaced by
 * the fixed word (`keyWord`), and nothing else: none of `withoutKey`'s shapes, since an answer's long ids and hashes are
 * its own.
 */
export function withoutWholeKey(text: string, key: string): string {
    return key === '' ? text : text.split(key).join(keyWord);
}

/**
 * The variables a host's process is started with to use a provider: its key, the host's folder of the desktop's own
 * (`keyedFolder`), and for an Anthropic-compatible endpoint where that endpoint is and which of its models stands in for
 * every model Claude Code would otherwise ask for by name. The agent host's environment is the one place a key goes.
 */
export function providerEnvironment(provider: KeyProvider, key: string | undefined, model: string, folder: string): Record<string, string> {
    if (!key) return {};
    const env: Record<string, string> = { [provider.keyVariable]: key, [provider.program === 'claude' ? 'CLAUDE_CONFIG_DIR' : 'CODEX_HOME']: folder };
    if (!provider.baseUrl) return env;
    env['ANTHROPIC_BASE_URL'] = provider.baseUrl;
    const chosen = model || provider.models[0];
    if (chosen) {
        for (const name of ['ANTHROPIC_MODEL', 'ANTHROPIC_DEFAULT_OPUS_MODEL', 'ANTHROPIC_DEFAULT_SONNET_MODEL', 'ANTHROPIC_DEFAULT_HAIKU_MODEL', 'ANTHROPIC_SMALL_FAST_MODEL']) env[name] = chosen;
    }
    return env;
}
