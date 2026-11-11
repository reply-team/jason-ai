/**
 * Pure: the agent hosts and the AI providers the desktop shows. Every host found on the machine is listed. Claude Code
 * and Codex drive the chat; every other host runs campaign work, not the chat yet, and is never swapped for another one
 * behind a person's back. The providers that take a key run through Claude Code or Codex with that key.
 */

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
    id: string;
    label: string;
    /** The host whose program runs it. */
    program: 'claude' | 'codex';
    /** The variable the host reads the key from. */
    keyVariable: string;
    /** An endpoint that speaks Anthropic's API, which Claude Code is pointed at instead of Anthropic's own. */
    baseUrl?: string;
    models: readonly string[];
}

const compatible = (id: string, label: string, baseUrl: string, models: readonly string[]): KeyProvider => ({
    id,
    label,
    program: 'claude',
    keyVariable: 'ANTHROPIC_AUTH_TOKEN',
    baseUrl,
    models,
});

export const keyProviders: readonly KeyProvider[] = [
    { id: 'claude-api-key', label: 'Anthropic', program: 'claude', keyVariable: 'ANTHROPIC_API_KEY', models: ['opus', 'sonnet', 'haiku'] },
    { id: 'codex-api-key', label: 'OpenAI', program: 'codex', keyVariable: 'OPENAI_API_KEY', models: [] },
    compatible('deepseek', 'DeepSeek', 'https://api.deepseek.com/anthropic', ['deepseek-v4-pro', 'deepseek-v4-flash']),
    compatible('moonshot', 'Kimi (Moonshot)', 'https://api.moonshot.ai/anthropic', ['kimi-k3']),
    compatible('zai', 'GLM (Z.ai)', 'https://api.z.ai/api/anthropic', []),
    compatible('minimax', 'MiniMax', 'https://api.minimax.io/anthropic', []),
];

export function keyProvider(id: unknown): KeyProvider | undefined {
    return keyProviders.find((provider) => provider.id === id);
}

/**
 * The variables a host's process is started with to use a provider: its key, and for an Anthropic-compatible endpoint
 * where that endpoint is and which of its models stands in for every model Claude Code would otherwise ask for by name.
 * The agent host's environment is the one place a key goes.
 */
export function providerEnvironment(provider: KeyProvider, key: string | undefined, model: string): Record<string, string> {
    if (!key) return {};
    const env: Record<string, string> = { [provider.keyVariable]: key };
    if (!provider.baseUrl) return env;
    env['ANTHROPIC_BASE_URL'] = provider.baseUrl;
    const chosen = model || provider.models[0];
    if (chosen) {
        for (const name of ['ANTHROPIC_MODEL', 'ANTHROPIC_DEFAULT_OPUS_MODEL', 'ANTHROPIC_DEFAULT_SONNET_MODEL', 'ANTHROPIC_DEFAULT_HAIKU_MODEL', 'ANTHROPIC_SMALL_FAST_MODEL']) env[name] = chosen;
    }
    return env;
}
