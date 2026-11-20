// Pure: a chat's access mode, chosen per conversation from the menu beside the composer and kept on its record. A
// conversation whose record names no mode is in Full access, the Feed's included (`feedDefaultMode`), and Full access is
// given with no confirmation. Full access runs Claude Code in its default mode with a hook that lets a command run with no
// card where Jason reads it plainly, refuses what Ask first refuses, and puts anything else to the person as Ask first's
// card; what Ask first refuses and the keys still hold wherever the desktop can see the attempt. Ask first asks before
// anything that changes but the acts of the person's word (`modeFence`). Plan only reads and proposes and changes nothing. Auto-edit is Ask first, except that
// the six file commands run with no card on paths in the chat's own folder, read by Jason's literal reader and never by
// the host's accept-edits mode; it is chosen on Claude Code only. A turn the conversation's mode cannot apply to (Full
// access on Codex, on a Claude Code below its floor, sent from a browser tab or while it is not offered; Auto-edit on
// Codex) runs in Ask first, the chat says why once, and the record keeps the mode. Nothing here starts a process or reads
// a file.

import { compareVersions } from './host-version.ts';

export type ChatMode = 'ask_first' | 'plan_only' | 'auto_edit' | 'full_access';

/**
 * Whether the desktop offers Full access. Off: the menu shows it disabled with `fullAccessNotYet`, the desktop refuses it
 * when it is chosen, and a turn of a conversation in it runs in Ask first. Everything else of Full access stays, behind
 * this one switch.
 */
export const fullAccessOffered = true;

/**
 * Whether Full access runs the PowerShell tool's literal commands unasked. Off: in Full access every command of the
 * PowerShell tool is held or refused, never run unasked, and only the Bash tool's literal commands run. The PowerShell
 * grammar stays behind this switch: two PowerShell generations pass arguments differently, and its built-in functions
 * and providers reach code from literal words, so that surface needs its own reading before it runs anything unasked.
 */
export const powerShellRunsInFullAccess = false;

/** What the menu and the desktop say of Full access while the switch is off. */
export const fullAccessNotYet = 'Full access is not in this version yet.';

/** The modes in the menu's order: Ask first, then narrow to wide, Full access last. */
export const chatModes: readonly ChatMode[] = ['ask_first', 'plan_only', 'auto_edit', 'full_access'];

export function isChatMode(value: unknown): value is ChatMode {
    return typeof value === 'string' && (chatModes as readonly string[]).includes(value);
}

/** The mode the Feed is made in: the one every chat starts in, so the Feed follows the same rule as any chat. */
export const feedDefaultMode: ChatMode = 'full_access';

/**
 * The oldest Claude Code a turn in Full access runs on: the version on which a hook's `ask` under `-p`, in the default
 * mode, was seen to override an allow rule, reach the permission prompt tool and wait for its answer, which no page
 * states. Below it, Full access would rest on a precedence nobody has seen. It is above the version from which the hook
 * blocks the tool when it fails. Anthropic, https://code.claude.com/docs/en/hooks (read 2026-11-11): "To block the action
 * instead, set `"onFailure": "block"` on a `command` or `http` hook. Requires Claude Code v2.1.295 or later."
 */
export const fullAccessFloor = '2.1.322';

/**
 * Why Full access cannot apply on a Claude Code of this version, read as the chat's floor is read (`floorSentence`);
 * null at the floor or above it. A version that could not be read is held as below it.
 */
export function fullAccessSentence(version: string | null): string | null {
    if (version === null) return `Full access needs Claude Code ${fullAccessFloor} or newer, and its version could not be read.`;
    if (compareVersions(version, fullAccessFloor) >= 0) return null;
    return `Full access needs Claude Code ${fullAccessFloor} or newer; this one is ${version}. Update Claude Code, for example by running \`claude update\` in a terminal.`;
}

/**
 * What the chat says, once in a conversation, when Codex warns during a Full access turn or such a turn fails. An
 * administrator's `requirements.toml` that does not allow Codex to run with no sandbox makes it fall back to a sandbox
 * it allows and notify, which `codex exec --json` says as a warning; no event says which sandbox a turn ran in, and a
 * warning's words are no contract, so the desktop cannot tell this warning from another, and a fallback Codex does not
 * say goes unseen. The sentence says what may be so, never that it is.
 */
export const codexFullAccessUnsure =
    'Codex reported a warning or an error in Full access. Where an administrator does not allow Codex to run with no sandbox on this computer, it runs in a sandbox they allow instead, so this chat may not be acting in Full access here.';

/** Why Auto-edit cannot apply on Codex: it never asks before it acts, so its turns only read. */
export const codexAutoEdit = 'Auto-edit runs on Claude Code only: Codex cannot ask before it acts, so on Codex the chat only reads and proposes.';

/**
 * What a conversation in Full access is told, once, when the last listing of the hosts drew its turn in Ask first, having
 * read Claude Code below the floor of Full access or not read its version, and the version a turn reads is at the floor:
 * the turn runs in Full access.
 */
export const fullAccessCurrent = `Claude Code is now ${fullAccessFloor} or newer, so this turn runs in Full access, the chat's mode.`;

/** Why a message a browser tab sent to a chat in Full access runs in Ask first: the default is for the desktop's window. */
export const tabFullAccess = "Full access is for Jason Desktop's own window, and this message came from a browser tab.";

/** Why a turn runs in Ask first while its conversation keeps another mode, by its cause. */
export type AskFirstWhy = 'floor' | 'not_offered' | 'tab' | 'codex_auto_edit';

/** Where a turn is sent from and what is known of the host, which `turnMode` reads beside the mode and the program. */
export interface TurnPlace {
    /** Whether it is sent from a browser tab, which the bridge marks, rather than from the desktop's own window. */
    fromTab: boolean;
    /** Whether this version offers Full access (`fullAccessOffered`). */
    offered: boolean;
    /** Claude Code's version as read, null when it could not be read; undefined when it was not read. */
    claudeVersion: string | null | undefined;
}

/**
 * The mode a turn of a conversation in `recorded` runs in on a host of `program`, sent from `place`, and why when that is
 * not `recorded`: the one place this is decided. A send runs its turn by it (`Chat.send`), and each host in the status
 * hands the page its answers (`Setup.status`, `runs`), so the line under the composer and the menu's check show the
 * mode the next turn runs in, and the page works none of it out. Full access runs in Ask first from a browser tab,
 * while this version does not offer it, and on a Claude Code below its floor of Full access or whose version could not
 * be read, the first of these that holds being the one said; on Codex it runs as itself, with no sandbox. Auto-edit runs
 * in Ask first on Codex. Every other mode runs as itself.
 */
export function turnMode(recorded: ChatMode, program: string, place: TurnPlace): { mode: ChatMode; why: { kind: AskFirstWhy; text: string } | null } {
    const askFirst = (kind: AskFirstWhy, text: string) => ({ mode: 'ask_first' as const, why: { kind, text } });
    if (recorded === 'full_access') {
        if (place.fromTab) return askFirst('tab', tabFullAccess);
        if (!place.offered) return askFirst('not_offered', fullAccessNotYet);
        // Codex runs Full access with no sandbox and holds no floor of it: Claude Code's floor is its own.
        const below = program !== 'claude' || place.claudeVersion === undefined ? null : fullAccessSentence(place.claudeVersion);
        if (below !== null) return askFirst('floor', below);
    } else if (recorded === 'auto_edit' && program === 'codex') return askFirst('codex_auto_edit', codexAutoEdit);
    return { mode: recorded, why: null };
}

/**
 * What a conversation is told, once, when a turn cannot run in its mode: why, and that the turn runs in Ask first while
 * the conversation keeps its mode for the turns that can.
 */
export function askFirstThisTurn(why: string): string {
    return `${why} This turn runs in Ask first, and the chat keeps its mode.`;
}

/**
 * What a conversation is told, once, when it is opened in Ask first because an earlier Jason Desktop wrote its record last:
 * that version keeps no mode on a record but Plan only, so the mode the person chose is not known.
 */
export const earlierDesktopAskFirst =
    "This chat was last used in an earlier version of Jason Desktop, which does not keep a chat's mode, so it is in Ask first now. To give it Full access, choose it from its menu.";

/** What a chat in Plan only answers every request it does not take. */
export const planRefusal = 'Plan only is on: nothing is changed. Say what you would do; the person switches the mode to act.';

/** What a chat in Plan only answers the host's own way out of planning: only the person changes the mode. */
export const planExitRefusal =
    'Plan only is on, and only the person leaves it, from the menu beside the message box or by approving your plan. Give your plan as your answer.';

/**
 * Why a send that names the mode it was approved for is refused: the chat is in another mode now. The plan card's go
 * names Ask first, and a mode chosen from the menu between the approval and the send would otherwise carry it.
 */
export const planModeMoved = "The chat's mode changed before the plan was approved, so nothing was sent.";

/**
 * The turn's end when a tool ran that the turn's hook never saw, a policy having filtered the hook out, say: that first
 * unchecked tool ends the turn, and the chat goes back to Ask first, on its record too.
 */
export const unguardedSentence =
    'Full access was turned off and the turn was stopped: a tool ran without the check Jason makes before each one. Choose Full access again only once that check runs, or go on in Ask first.';
