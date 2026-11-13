import type { ContinueFlags, TerminalFlags } from './open-in.ts';

/**
 * The arguments of `jason session open` for the three terminal entries of "Open in": the one place the form of that verb
 * is written down on the desktop's side. A campaign's id names its folder, none names the harness root; `--host` names the
 * agent host to open, `--shell` a plain terminal. The bundled executable does the opening, as the same words typed by hand.
 */
export const terminalFlags: TerminalFlags = (kind, campaignId) => [
    ...(campaignId === null ? [] : ['--campaign', campaignId]),
    ...(kind === 'terminal' ? ['--shell'] : ['--host', kind === 'claude' ? 'claude_code' : kind]),
];

/**
 * The arguments of `jason session open` that continue a chat in a terminal: `--here` opens at the working folder the
 * desktop starts the CLI in, `--host` names the agent host, and `--resume` the id of that host's own conversation.
 */
export const continueFlags: ContinueFlags = (host, session) => ['--here', '--host', host === 'claude' ? 'claude_code' : host, '--resume', session];
