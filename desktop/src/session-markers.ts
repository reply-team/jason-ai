// Pure: the markers a Claude Code session sets for the processes it starts. A desktop started from inside such a session
// inherits them, and every process the desktop starts would carry them on: an agent host would take itself for that
// session's child, run bare or at its effort, and the session's messaging token would ride down through jason to the
// runtime and its plugins. So no process the desktop starts is given them: the chat's host (chat-hosts.ts), the programs
// (programs.ts) and the bundled jason (cli.ts) all take their environment through this one list.

/**
 * The markers, a closed list. The rest of `CLAUDE_CODE_*` is a person's configuration (a cloud provider, the Git Bash
 * path Windows needs, a token, the configuration folder) and is kept.
 */
export const sessionMarkers: readonly string[] = [
    'CLAUDECODE',
    'CLAUDE_PID',
    'CLAUDE_EFFORT',
    'CLAUDE_CODE_ENTRYPOINT',
    'CLAUDE_CODE_SSE_PORT',
    'CLAUDE_CODE_SESSION_ID',
    'CLAUDE_CODE_CHILD_SESSION',
    'CLAUDE_CODE_BRIDGE_SESSION_ID',
    'CLAUDE_CODE_SESSION_ATTENDED',
    'CLAUDE_CODE_MESSAGING_SOCKET',
    'CLAUDE_CODE_MESSAGING_TOKEN',
    'CLAUDE_CODE_EXECPATH',
    'CLAUDE_CODE_REMOTE',
    'CLAUDE_CODE_REMOTE_SESSION_ID',
    'CLAUDE_CODE_SIMPLE',
];

const markers: ReadonlySet<string> = new Set(sessionMarkers);

/** True when the variable is one of a Claude Code session's markers, whatever case its name is written in. */
export function isSessionMarker(name: string): boolean {
    return markers.has(name.toUpperCase());
}

/** The environment less a Claude Code session's markers, and less any variable with no value. */
export function withoutSessionMarkers(env: Readonly<Record<string, string | undefined>>): Record<string, string> {
    const next: Record<string, string> = {};
    for (const [name, value] of Object.entries(env)) if (value !== undefined && !isSessionMarker(name)) next[name] = value;
    return next;
}
