import type { TerminalFlags } from './open-in.ts';

/**
 * The arguments of `jason session open` for the three terminal entries of "Open in": the one place the form of that verb
 * is written down on the desktop's side. A campaign's id names its folder, none names the harness root; `--host` names the
 * agent host to open, `--shell` a plain terminal. The bundled executable does the opening, as the same words typed by hand.
 */
export const terminalFlags: TerminalFlags = (kind, campaignId) => [
    ...(campaignId === null ? [] : ['--campaign', campaignId]),
    ...(kind === 'terminal' ? ['--shell'] : ['--host', kind]),
];
