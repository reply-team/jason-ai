// What a desktop started from inside a Claude Code session inherits, planted: the markers that session sets for the
// processes it starts, each of which no process the desktop starts may carry, and a person's own configuration, which
// every one of them keeps. Written out here rather than read from the source, so a name dropped there turns a row red.

export const plantedMarkers: Readonly<Record<string, string>> = {
    CLAUDECODE: '1',
    CLAUDE_PID: '4242',
    CLAUDE_EFFORT: 'max',
    CLAUDE_CODE_ENTRYPOINT: 'cli',
    CLAUDE_CODE_SSE_PORT: '41000',
    CLAUDE_CODE_SESSION_ID: 'planted-session',
    CLAUDE_CODE_CHILD_SESSION: '1',
    CLAUDE_CODE_BRIDGE_SESSION_ID: 'planted-bridge',
    CLAUDE_CODE_SESSION_ATTENDED: '1',
    CLAUDE_CODE_MESSAGING_SOCKET: '/tmp/planted.sock',
    CLAUDE_CODE_MESSAGING_TOKEN: 'planted-messaging-token',
    CLAUDE_CODE_EXECPATH: '/opt/claude/planted',
    CLAUDE_CODE_REMOTE: 'true',
    CLAUDE_CODE_REMOTE_SESSION_ID: 'planted-remote',
    CLAUDE_CODE_SIMPLE: '1',
    CLAUDE_JOB_DIR: '/h/.claude/jobs/planted',
    CLAUDE_CODE_SAFE_MODE: '1',
};

export const plantedConfiguration: Readonly<Record<string, string>> = {
    CLAUDE_CODE_GIT_BASH_PATH: 'C:\\Git\\bin\\bash.exe',
    CLAUDE_CODE_USE_BEDROCK: '1',
    CLAUDE_CODE_USE_VERTEX: '1',
    CLAUDE_CODE_USE_FOUNDRY: '1',
    CLAUDE_CODE_OAUTH_TOKEN: 'planted-oauth',
    CLAUDE_CONFIG_DIR: '/h/.claude-other',
    CLAUDE_CODE_AUTO_COMPACT_WINDOW: '100000',
};

/** The names of the markers an environment still carries, in any case: none, for every process the desktop starts. */
export function markersIn(names: readonly string[]): string[] {
    const planted = new Set(Object.keys(plantedMarkers));
    return names.filter((name) => planted.has(name.toUpperCase()));
}
