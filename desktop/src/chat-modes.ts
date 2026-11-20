// Pure: a chat's access mode, chosen per conversation from the menu beside the composer and kept on its record. A
// conversation whose record names no mode is in Full access, the Feed's included (`feedDefaultMode`), and Full access is
// given with no confirmation. Full access runs Claude Code in its own bypass mode, with Jason's hook in front of every
// tool use refusing three kinds alone: the installation's settings files and run file, its data directory outside the
// chat's folder, and what Jason itself sets for the turn: its variables and its permission tool. What Claude Code asks
// about in that mode itself is a card. On Codex, Full access runs Codex with no sandbox, as a terminal's Codex runs, and
// none of the desktop's refusals holds on it (`codexArguments`). Ask first asks before anything that changes outside the
// allow list's verbs and the acts of the person's word (`modeFence`). Plan only reads and proposes and changes nothing.
// Auto-edit is Ask first, except that the six file commands run with no card on paths in the chat's own folder, read by
// Jason's literal reader and never by the host's accept-edits mode; it is chosen on Claude Code only. A turn the
// conversation's mode cannot apply to (Full access on a Claude Code below its floor, sent from a browser tab, while it is
// not offered, or where Claude Code's bypass is not available; Auto-edit on Codex) runs in Ask first, the chat says why
// once, and the record keeps the mode. Nothing here starts a process or reads a file.

import { compareVersions } from './host-version.ts';

export type ChatMode = 'ask_first' | 'plan_only' | 'auto_edit' | 'full_access';

/**
 * Whether the desktop offers Full access. Off: the menu shows it disabled with `fullAccessNotYet`, the desktop refuses it
 * when it is chosen, and a turn of a conversation in it runs in Ask first. Everything else of Full access stays, behind
 * this one switch.
 */
export const fullAccessOffered = true;

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
 * The oldest Claude Code a turn in Full access runs on, and the oldest an Ask first or Auto-edit turn carries the hook on:
 * the version on which the turn's hook under `-p` was seen to stand in front of an allow rule and reach the permission
 * prompt tool, which no page states. It is above the version from which the hook blocks the tool when it fails.
 * Anthropic, https://code.claude.com/docs/en/hooks (read 2026-11-11): "To block the action instead, set `"onFailure":
 * "block"` on a `command` or `http` hook. Requires Claude Code v2.1.295 or later."
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
 * What the chat says, once in a conversation, when Codex ran a Full access turn in a narrower sandbox because an
 * administrator's requirements do not allow it to run with none (`codexLine`, `sandbox_narrowed`).
 */
export const codexFullAccessNarrowed =
    'Codex could not act in Full access here: an administrator does not allow it to run with no sandbox on this computer, so it ran in the sandbox they allow.';

/**
 * What the chat says after Codex's own words, every time, when a Full access turn on Codex ends before Codex said any
 * event, with a failing exit: Codex refused to start, and an administrator's requirements that forbid running with no
 * sandbox are one reason it does. The desktop does not read Codex's words to tell which; the sentence leaves that to the
 * person, and nothing of it is remembered.
 */
export const codexFullAccessRefused =
    'If these words say Full access is not allowed on this computer, choose Ask first for this chat, where Codex reads and proposes.';

/**
 * Whether `codexFullAccessRefused` follows a turn's end: a Full access turn on Codex that ended in words, not stopped,
 * with no line on standard output, a failing exit and no start error, and whose words are not the desktop's own sentence
 * for a refused key (`ours`), which no sentence of Codex's precedes.
 */
export function followsCodexRefusal(turn: {
    program: string;
    mode: ChatMode;
    ended: string | null;
    stopped: boolean;
    heard: boolean | undefined;
    startError: string | null;
    code: number | null;
    ours: boolean | undefined;
}): boolean {
    return (
        turn.program === 'codex' &&
        turn.mode === 'full_access' &&
        turn.ended !== null &&
        !turn.stopped &&
        turn.heard === false &&
        turn.startError === null &&
        turn.code !== 0 &&
        turn.ours !== true
    );
}

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

/**
 * Why Claude Code's bypass, which Full access runs in, is not available here: the desktop runs as root, which Claude Code
 * refuses its bypass under on macOS and Linux; Claude Code started a turn that asked for its bypass in another mode, which
 * its init line names, and said on its standard error that it forced its default mode (`forced_default`, read from those
 * words alone, `forcedDefault`), or said nothing of why (`bypass_off`, why not being read); it refuses the mode before it
 * starts, in its restricted mode or under a policy ("Error: bypassPermissions not supported in restricted mode", seen on
 * 2.1.329); or a tool ran that Jason's hook never saw, a policy having filtered the hook out.
 */
export type BypassWhy = 'root' | 'bypass_off' | 'forced_default' | 'bypass_refused' | 'unguarded';

/**
 * Claude Code's own words on its standard error when it starts in its default mode whatever mode the start asked for, as
 * 2.1.329 does while `CLAUDE_CODE_SUBPROCESS_ENV_SCRUB` is set: "⚠ Permission mode forced to default —
 * CLAUDE_CODE_SUBPROCESS_ENV_SCRUB is set (allowed_non_write_users hardening). ...". A closed match on these words, and
 * nothing else of what it wrote is read: what is said and logged of it is the cause's word, `forced_default`.
 */
export const forcedDefaultWords = 'Permission mode forced to default';

/** Whether what Claude Code wrote on its standard error holds its own words that it forced its default mode. */
export function forcedDefault(stderr: string): boolean {
    return stderr.includes(forcedDefaultWords);
}

/**
 * The words Claude Code names its modes by on its init line, other than its bypass, that Jason knows: a closed table, so
 * what is said and logged of a mode is one of these words or `another`, never a word Claude Code printed.
 */
export const claudeModeWords = ['default', 'acceptEdits', 'plan', 'auto', 'dontAsk'] as const;

/** The mode Claude Code named instead of its bypass: a word of its table (`claudeModeWords`), or `another`. */
export type NamedMode = (typeof claudeModeWords)[number] | 'another';

/** The mode a word names, read from the closed table: any word outside it is `another`. */
export function namedMode(word: string): NamedMode {
    return claudeModeWords.find((known) => known === word) ?? 'another';
}

/** Why Claude Code's bypass is not available here, and the mode Claude Code named instead where it named one. */
export interface Bypass {
    why: BypassWhy;
    named: NamedMode | null;
}

/**
 * The fixed causes' sentences, of the one shape: "Full access is not available here: <why>." A cause a turn found holds a
 * minute (`bypassLeadsTo`), and its sentence says so: "... right now: <why>."; a tool that ran unchecked holds for the rest
 * of this run of the desktop, and its sentence says "... until Jason Desktop starts again: <why>." A start that named
 * another mode and said nothing of why is said by `bypassSentence`, which names that mode.
 */
export const bypassSentences: Readonly<Record<Exclude<BypassWhy, 'bypass_off'>, string>> = {
    root: 'Full access is not available here: Claude Code does not run in its bypass mode as root or under sudo.',
    forced_default: 'Full access is not available here right now: Claude Code said it forced its permission mode to default.',
    bypass_refused: 'Full access is not available here right now: Claude Code refused its bypass mode, in its restricted mode or under a policy.',
    unguarded: 'Full access is not available here until Jason Desktop starts again: a tool ran without the check Jason makes before each one, which a policy can filter out.',
};

/**
 * A cause's sentence: the fixed causes' own (`bypassSentences`), and for a start that named another mode and said nothing
 * of why what was read, that Claude Code did not start in its bypass mode and which mode it named, never why.
 */
export function bypassSentence(bypass: Bypass): string {
    if (bypass.why !== 'bypass_off') return bypassSentences[bypass.why];
    const named = bypass.named === null || bypass.named === 'another' ? 'a mode Jason does not know' : `its ${bypass.named} mode`;
    return `Full access is not available here right now: Claude Code did not start in its bypass mode, and named ${named} instead.`;
}

/** How long a cause a turn found holds (`bypassLeadsTo`): a minute, after which a Full access turn asks for the bypass again. */
export const bypassHeldMs = 60_000;

/**
 * What Full access on Claude Code leads to where its bypass is not available: the one place it is decided, for the turn
 * that found the cause and for the turns after it. The turn that found it is started again in the mode this names
 * (`Chat.send`), and the turns after it run in that mode (`turnMode`), which the status hands the page for the menu
 * (`Setup.status`), for as long as the cause holds: a minute where a turn found it (`bypassHeldMs`), so each Full access
 * turn after that asks for the bypass again and a cause that went away needs no restart of the desktop, while the minute
 * spares a second start of the host on every turn while the cause stays; for the rest of this run where the desktop runs
 * as root, and where a tool ran that the hook never saw, which only a tool that ran unchecked shows. Ask first, the cause
 * said.
 */
export function bypassLeadsTo(bypass: Bypass): { mode: 'ask_first'; why: { kind: AskFirstWhy; text: string }; held: 'one_minute' | 'this_run' } {
    const held = bypass.why === 'root' || bypass.why === 'unguarded' ? 'this_run' : 'one_minute';
    return { mode: 'ask_first', why: { kind: bypass.why, text: bypassSentence(bypass) }, held };
}

/**
 * The end of a Full access turn stopped where a tool ran that the turn's hook never saw: said once, the conversation
 * keeps its mode, and its later turns run in Ask first (`unguarded`).
 */
export const unguardedSentence = `${bypassSentences.unguarded} The turn was stopped there; the chat keeps its mode, and its next turns run in Ask first.`;

/**
 * What an Ask first or Auto-edit turn says once where a tool ran that the turn's hook never saw: the turn runs on, the
 * cards and Claude Code's own rules deciding as before, without Jason's refusals in front of them.
 */
export const hookFilteredSentence =
    "Jason's check before each tool did not run in this turn, which a policy can filter out: the cards and Claude Code's own rules still decide what runs, without Jason's refusals in front of them.";

/** Why a turn runs in Ask first while its conversation keeps another mode, by its cause. */
export type AskFirstWhy = 'floor' | 'not_offered' | 'tab' | 'codex_auto_edit' | BypassWhy;

/** Where a turn is sent from and what is known of the host, which `turnMode` reads beside the mode and the program. */
export interface TurnPlace {
    /** Whether it is sent from a browser tab, which the bridge marks, rather than from the desktop's own window. */
    fromTab: boolean;
    /** Whether this version offers Full access (`fullAccessOffered`). */
    offered: boolean;
    /** Claude Code's version as read, null when it could not be read; undefined when it was not read. */
    claudeVersion: string | null | undefined;
    /**
     * Why Claude Code's bypass is not available here, as the machine or an earlier turn showed it, while that holds
     * (`bypassLeadsTo`); absent or null, it is, and a Full access turn asks for it.
     */
    bypass?: Bypass | null;
}

/**
 * The mode a turn of a conversation in `recorded` runs in on a host of `program`, sent from `place`, and why when that is
 * not `recorded`: the one place this is decided. A send runs its turn by it (`Chat.send`), and each host in the status
 * hands the page its answers (`Setup.status`, `runs`), so the line under the composer and the menu's check show the
 * mode the next turn runs in, and the page works none of it out. Full access runs in Ask first from a browser tab,
 * while this version does not offer it, where Claude Code's bypass is not available (`bypass`, as `bypassLeadsTo`
 * decides it), and on a Claude Code below its floor of Full access or whose version could not be read, the first of
 * these that holds being the one said;
 * on Codex it runs as itself, with no sandbox, held to neither Claude Code's bypass nor its floor. Auto-edit runs in Ask
 * first on Codex. Every other mode runs as itself.
 */
export function turnMode(recorded: ChatMode, program: string, place: TurnPlace): { mode: ChatMode; why: { kind: AskFirstWhy; text: string } | null } {
    const askFirst = (kind: AskFirstWhy, text: string) => ({ mode: 'ask_first' as const, why: { kind, text } });
    if (recorded === 'full_access') {
        if (place.fromTab) return askFirst('tab', tabFullAccess);
        if (!place.offered) return askFirst('not_offered', fullAccessNotYet);
        // Codex runs Full access with no sandbox and is held to neither Claude Code's bypass nor its floor; every other
        // program is held to both.
        if (program !== 'codex' && place.bypass !== undefined && place.bypass !== null) {
            const { mode, why } = bypassLeadsTo(place.bypass);
            return { mode, why };
        }
        const below = program === 'codex' || place.claudeVersion === undefined ? null : fullAccessSentence(place.claudeVersion);
        if (below !== null) return askFirst('floor', below);
    } else if (recorded === 'auto_edit' && program === 'codex') return askFirst('codex_auto_edit', codexAutoEdit);
    return { mode: recorded, why: null };
}

/** The modes by the names the menu gives them. */
const modeNames: Readonly<Record<ChatMode, string>> = { ask_first: 'Ask first', plan_only: 'Plan only', auto_edit: 'Auto-edit', full_access: 'Full access' };

/**
 * What a conversation is told, once, when a turn cannot run in its mode: why, and what the menu draws, that the turn runs
 * in Ask first and the menu shows the mode each next turn runs in, while the record keeps the chat's own mode, `kept`.
 */
export function askFirstThisTurn(why: string, kept: ChatMode): string {
    return `${why} This turn runs in Ask first, and the menu shows the mode each next turn runs in; the chat stays set to ${modeNames[kept]}.`;
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
