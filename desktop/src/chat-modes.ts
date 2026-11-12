// Pure: a chat's access mode, chosen per conversation from the menu beside the composer. Ask first is the default of every
// new chat out of the box. Plan only reads and proposes and changes nothing. Auto-edit is Ask first, except that the six
// file commands run with no card on paths in the chat's own folder, read by Jason's literal reader and never by the host's
// accept-edits mode; it has no confirmation and no standing choice, is held in memory alone, and is chosen on Claude Code
// only, never for the Feed. Full access runs Claude Code in its default mode with a hook that lets a command run with no
// card where Jason reads it plainly, refuses what Ask first refuses, and puts anything else to the person as Ask first's
// card; the deny list, the chat's confinement and the keys still hold wherever the desktop can see the attempt. It is
// chosen through a confirmation that offers this time only or always on this computer, the second with new chats
// starting in Full access as well; a conversation's Full access is held in memory alone and never remembered past the
// window, and the standing choice is the main process's own file (`full-access-standing.ts`), under a consent number.
// Codex has no Full access, nor has the Feed. Nothing here starts a process or reads a file.

import { compareVersions } from './host-version.ts';

export type ChatMode = 'ask_first' | 'plan_only' | 'auto_edit' | 'full_access';

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

/** The modes in the menu's order: the default first, then narrow to wide. */
export const chatModes: readonly ChatMode[] = ['ask_first', 'plan_only', 'auto_edit', 'full_access'];

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

/**
 * Why Full access cannot be chosen for the Feed: its turns are handed journal lines by default, words that others and
 * providers wrote, the widest road for a steer.
 */
export const feedFullAccess = 'Full access is not offered for the Feed: its turns are handed lines that others and providers wrote without you asking for them.';

/** Why Auto-edit cannot be chosen on Codex: it never asks before it acts, so its turns only read. */
export const codexAutoEdit = 'Auto-edit runs on Claude Code only: Codex cannot ask before it acts, so on Codex the chat only reads and proposes.';

/** Why Auto-edit cannot be chosen for the Feed: the reason Full access is not offered there. */
export const feedAutoEdit = 'Auto-edit is not offered for the Feed: its turns are handed lines that others and providers wrote without you asking for them.';

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

/** The same end where the standing choice stood: a check that a policy filtered out would miss every later turn too. */
export const unguardedStandingSentence =
    'Full access was turned off, and Always taken back, and the turn was stopped: a tool ran without the check Jason makes before each one. Choose Full access again only once that check runs, or go on in Ask first.';

/** Said in the chat when Always was answered and its file could not be written: Full access is given this time only. */
export const standingUnkeptSentence = 'Always could not be kept: its file could not be written; Full access holds this time only.';

/**
 * The number the standing choice of Full access is given under (`full-access-standing.ts`): a choice kept under any
 * other number, lower or higher, is no choice, and the confirmation is shown again. It is raised only by a release that
 * widens what Full access reaches; a change of wording alone, the confirmation's or the page's, does not raise it.
 */
export const fullAccessConsent = 1;

/**
 * What the confirmation shows, as Electron's message box takes it: Cancel is focused, and Escape answers Cancel. The
 * checkbox counts only with Always (`fullAccessAnswer`); Electron cannot grey it, so its label says so.
 */
export interface FullAccessDialog {
    type: 'warning';
    title: string;
    message: string;
    detail: string;
    buttons: string[];
    defaultId: number;
    cancelId: number;
    checkboxLabel: string;
    checkboxChecked: false;
    noLink: true;
}

export const fullAccessButton = 'Give full access this time';
export const fullAccessAlwaysButton = 'Always on this computer';

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
            'This time, Full access lasts until you change it or close Jason.',
            'Always lets every Claude Code chat on this computer enter Full access from its menu without this box, until you take it back in that menu or on the System screen, and a program the agent writes and runs could choose it for you too.',
            'The agent works in a folder of its own, which Jason keeps for seven days after Full access ends and then removes.',
        ].join(' '),
        buttons: [fullAccessButton, fullAccessAlwaysButton, 'Cancel'],
        defaultId: 2,
        cancelId: 2,
        checkboxLabel: 'With Always, also start new chats in Full access',
        checkboxChecked: false,
        noLink: true,
    };
}

/**
 * The person's answer to the confirmation, by the button pressed and the checkbox: null for Cancel or anything else;
 * the checkbox is read only with Always.
 */
export interface FullAccessAnswer {
    always: boolean;
    newChats: boolean;
}

export function fullAccessAnswer(response: number, checkboxChecked: boolean): FullAccessAnswer | null {
    if (response === 0) return { always: false, newChats: false };
    if (response === 1) return { always: true, newChats: checkboxChecked === true };
    return null;
}
