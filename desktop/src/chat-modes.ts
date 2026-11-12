// Pure: a chat's access mode, chosen per conversation from the menu beside the composer. Ask first is the default of every
// new chat and no setting changes it. Plan only reads and proposes and changes nothing. Full access runs Claude Code in its
// default mode with a hook that lets a command run with no card where Jason reads it plainly, refuses what Ask first
// refuses, and puts anything else to the person as Ask first's card; the deny list, the chat's confinement and the keys
// still hold wherever the desktop can see the attempt, and it is chosen only through a confirmation shown each time, held
// in memory for that conversation alone, and never remembered past the window. Codex has no Full access. Nothing here
// starts a process or reads a file.

import { compareVersions } from './host-version.ts';

export type ChatMode = 'ask_first' | 'plan_only' | 'full_access';

/**
 * Whether the desktop offers Full access. Off: the menu shows it disabled with `fullAccessNotYet`, and the desktop
 * refuses it before any confirmation. Everything else of Full access stays, behind this one switch.
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

export const chatModes: readonly ChatMode[] = ['ask_first', 'plan_only', 'full_access'];

export function isChatMode(value: unknown): value is ChatMode {
    return typeof value === 'string' && (chatModes as readonly string[]).includes(value);
}

/**
 * The oldest Claude Code a turn in Full access runs on: the version on which a hook's `ask` under `-p`, in the default
 * mode, was seen to override an allow rule, reach the permission prompt tool and wait for its answer, which no page
 * states. Below it, Full access would rest on a precedence nobody has seen. It is above the version from which the hook
 * blocks the tool when it fails. Anthropic, https://code.claude.com/docs/en/hooks (read 2026-11-11): "To block the action
 * instead, set `"onFailure": "block"` on a `command` or `http` hook. Requires Claude Code v2.1.295 or later."
 */
export const fullAccessFloor = '2.1.322';

/**
 * Why Full access cannot be chosen on a Claude Code of this version, read as the chat's floor is read (`floorSentence`);
 * null at the floor or above it. A version that could not be read is held as below it.
 */
export function fullAccessSentence(version: string | null): string | null {
    if (version === null) return `Full access needs Claude Code ${fullAccessFloor} or newer, and its version could not be read.`;
    if (compareVersions(version, fullAccessFloor) >= 0) return null;
    return `Full access needs Claude Code ${fullAccessFloor} or newer; this one is ${version}. Update Claude Code, for example by running \`claude update\` in a terminal.`;
}

/** Why Full access cannot be chosen on Codex: its own sandbox and hooks hold none of the chat's refusals. */
export const codexFullAccess = 'Full access runs on Claude Code only.';

/** What a chat in Plan only answers every request it does not take. */
export const planRefusal = 'Plan only is on: nothing is changed. Say what you would do; the person switches the mode to act.';

/** What a chat in Plan only answers the host's own way out of planning: only the person changes the mode. */
export const planExitRefusal = 'Plan only is on, and only the person leaves it, from the menu beside the message box. Give your plan as your answer.';

/**
 * The turn's end when a tool ran that the turn's hook never saw, a policy having filtered the hook out, say: that first
 * unchecked tool ends the turn, and the chat goes back to Ask first.
 */
export const unguardedSentence =
    'Full access was turned off and the turn was stopped: a tool ran without the check Jason makes before each one. Choose Full access again only once that check runs, or go on in Ask first.';

/** What the confirmation shows, as Electron's message box takes it: Cancel is focused, and Escape answers Cancel. */
export interface FullAccessDialog {
    type: 'warning';
    title: string;
    message: string;
    detail: string;
    buttons: string[];
    defaultId: number;
    cancelId: number;
    noLink: true;
}

export const fullAccessButton = 'Give full access';

export function fullAccessDialog(): FullAccessDialog {
    return {
        type: 'warning',
        title: 'Full access',
        message: 'Give this chat full access?',
        detail: [
            'From now on, in this chat, the agent runs commands and changes files on this computer without asking you first; it can read and change what you can on this computer, and a page it reads can steer what it does.',
            'Jason still asks you about a command it cannot read plainly or one that changes what an agent loads, and a question you leave unanswered for thirty minutes is not run.',
            "Jason refuses the agent's commands that approve, reject, answer or clear for you; a program the agent writes and runs can still act as you, including approving.",
            'The risk is yours.',
            'Full access lasts until you change it or close Jason; the agent works in a folder of its own, which Jason keeps for seven days after Full access ends and then removes.',
        ].join(' '),
        buttons: [fullAccessButton, 'Cancel'],
        defaultId: 1,
        cancelId: 1,
        noLink: true,
    };
}
