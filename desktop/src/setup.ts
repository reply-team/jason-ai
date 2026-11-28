/**
 * The Connect cards: what `window.jasonDesktop.setup` answers and does. Every change to the installation goes through
 * the CLI (`jason plugin install`, `jason plugin reload`) or through the settings file a person would edit; a Reply key
 * goes to the Reply CLI on its standard input and is kept by it alone; a provider's key goes to encrypted storage.
 */

import { cp, mkdtemp, readdir, rm } from 'node:fs/promises';
import { mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { deployReplySkills } from './reply-skills.ts';
import {
    accountArguments,
    accountBox,
    accountNameProblem,
    accountsOf,
    accountSentences,
    defaultBinding,
    defaultReason,
    followedProfile,
    signInReason,
    defaultRefusal,
    defaultTeam,
    isAccountRef,
    nonProductionApi,
    readBackend,
    readProfiles,
    readReplyWhoami,
    readRoutes,
    readTeams,
    sameAccount,
    type AccountRef,
    type DefaultFor,
    type ProfileBackend,
    type ReplyWhoami,
    type ReplyTeam,
    withReason,
} from './accounts.ts';
import {
    actsIn,
    asksIn,
    bindingOf,
    namedBy,
    notedOf,
    readAccountNotes,
    sameSignIn,
    restsOnPin,
    sameUser,
    signInKept,
    signInRead,
    noSignInSince,
    whoIsAsked,
    withNote,
    writeAccountNotes,
    type BoundTeam,
    type Change,
    type Noted,
    type NotedSignIn,
    type SignIn,
    type SignInFiles,
} from './account-notes.ts';
import { usableAccount } from './chat-header.ts';
import { AgentFolderUnowned } from './chat-agent-folder.ts';
import { offeredModels, type KeptLists, type ModelOffer } from './chat-models.ts';
import type { Cli, CliResult } from './cli.ts';
import { feedArguments } from './installation.ts';
import type { Logger } from './log.ts';
import { compareVersions, floorSentence, printedVersion, versionLimitMs } from './host-version.ts';
import { chatModes, codexAutoEdit, fullAccessNotYet, fullAccessOffered, fullAccessSentence, turnMode, type Bypass, type ChatMode } from './chat-modes.ts';
import { dataLayout, layoutRefusal, type DataLayout, type DesktopPaths } from './paths.ts';
import { settingsTemporary } from './settings-temporary.ts';
import { isProgramName, type ProgramName, type Programs, type RunResult } from './programs.ts';
import { hosts, keyProvider, keyProviders } from './providers-model.ts';
import { OwnServers, type UnreadKind } from './own-servers.ts';
import {
    hostWords,
    profileProgram,
    profileToUse,
    readDefaultAnswer,
    readProfile,
    readProfilePage,
    roleHostOf,
    roleProfiles,
    rolesNext,
    type ListedProfile,
    type RoleHost,
} from './roles-profile.ts';
import type { ProviderKeys } from './providers.ts';
import {
    actBox,
    belowMinimumSentence,
    dailyLimitMost,
    isOpenableLink,
    isReplyChannel,
    isReplyId,
    isUsageCode,
    linkedInNameProblem,
    refusalOf,
    senderSentences,
    replyActLimitMs,
    type ReplyAct,
    type ReplyBox,
    type ReplyRefusal,
} from './reply-senders.ts';
import {
    connectArguments,
    deleteArguments,
    limitsArguments,
    pendingArguments,
    pendingNoneCodes,
    readDailyLimit,
    readDone,
    readLink,
    readPending,
    readState,
    reconnectArguments,
    replyCliEnding,
    stateArguments,
    unblockArguments,
    type ReplyCliChannel,
    type ReplyCliPending,
} from './reply-cli-account.ts';
import type { DesktopSettings, RolesMark } from './settings.ts';
import {
    bundledPlugin,
    bundledPlugins,
    hasBinding,
    isConfigured,
    parseInstallationSettings,
    parseReplySenders,
    readReplyAuth,
    readReplyChannel,
    type ReplyAuth,
    replyChannels,
    type ReplyChannel,
    pluginRuntimes,
    programInstalls,
    replyMinimumVersion,
    replyPlacement,
    Refused,
    versionAtLeast,
    versionIn,
    withPluginConfigured,
    withPluginDisconnected,
    withReplyGranted,
    type PluginRuntime,
    type PluginValues,
    type ReplySender,
} from './setup-model.ts';
import { npmLine, readUpdateSwitch, replyAtWork, runtimeReplyVersion, startUpdateSentences, versionUnsaid } from './reply-update.ts';

/** What a turn runs in, by the mode a chat's record keeps, where that is another mode, and the desktop's sentence why. */
export type TurnSteps = Partial<Record<ChatMode, { mode: ChatMode; why: string }>>;

export interface HostState {
    id: string;
    label: string;
    kind: 'program' | 'provider';
    found: boolean;
    version: string | null;
    drivesChat: boolean;
    /** The `installProgram` id when the program is missing and npm is there to install it. */
    install: string | null;
    /**
     * When the Claude Code the host runs on is older than the oldest version the chat runs on, or its version could not
     * be read, the sentence that says so, names the floor and the cure: the chat starts no turn on the host. Null
     * otherwise, and for a host that is not Claude Code or a key provider on it.
     */
    belowFloor: string | null;
    /**
     * Why Full access cannot be chosen for a chat on this host, in the desktop's words: while this version does not offer
     * it (`fullAccessNotYet`), and on a Claude Code older than the oldest version a Full access turn runs on, or whose
     * version could not be read (`fullAccessSentence`). Null where it can be chosen, on Codex with no floor of it, and for
     * a host that does not drive the chat.
     */
    fullAccess: string | null;
    /**
     * Why Auto-edit cannot be chosen for a chat on this host, in the desktop's words: Codex cannot ask before it acts
     * (`codexAutoEdit`). Null where it can be chosen, and for a host that does not drive the chat.
     */
    autoEdit: string | null;
    /**
     * What a turn on this host runs in, by the mode a chat's record keeps, where that is another mode, with the desktop's
     * sentence why: sent from the desktop's own window (`window`) and from a browser tab (`tab`, unreachable since 0.49.3).
     * A mode not named runs as itself. Decided as the turn itself is (`turnMode`), with Claude Code's version read at this
     * listing, so the page draws the mode the next turn runs in, and why, and works none of it out. Empty for a host that
     * does not drive the chat.
     */
    runs: { window: TurnSteps; tab: TurnSteps };
    /**
     * The models the chat's box offers on this host, each with the effort levels it takes: Claude Code's aliases at the
     * version read now, the list kept of what Codex offered, or a key provider's table (`offeredModels`).
     */
    models: ModelOffer[];
    /** Whether the box also takes a model's full name typed by the person: on Claude Code and on Anthropic's key. */
    typedModel: boolean;
    /**
     * Where the host's models are asked of it and no list was ever read, the desktop's one line saying so and that the
     * turn uses the host's own default model; null otherwise.
     */
    modelsUnread: string | null;
    /**
     * The names of the person's own MCP servers, for Claude Code and Codex found here (`own-servers.ts`): Claude Code's
     * user servers and the servers of Codex's config.toml, in the file's order, each cut at forty characters. Names and
     * nothing else; empty for every other host, for a host not found, and for a file that is not there or cannot be read.
     */
    ownServers: string[];
    /**
     * The name of the execution profile the agents' roles run on by default, where the default profile the runtime
     * answers in force is an enabled profile whose host is this program and the chat can use this program: it drives the chat, is found here
     * and is at its version floor. Null otherwise, and for every key provider, which runs no role. The Connect row says it
     * in one line.
     */
    runsRoles: string | null;
}

/**
 * A fact of `ReplyState` that was asked and could not be read: `version`, the Reply CLI's, where `reply --version` said
 * none; `auth`, the account's sign-in, where `reply auth status` did not answer, or no account could be read; `route`,
 * whether Jason's Reply plugin is valid and the global default, where the runtime gave no plugin list or no route list.
 */
export type ReplyFact = 'version' | 'auth' | 'route';

/**
 * The Reply connection as one card draws it: the Reply CLI, and of the accounts the default one, or the profile
 * `default`'s where no Reply account is the default. A page of an earlier version reads it, and so does this page's chat
 * until the chat's own part reads the accounts.
 */
export interface ReplyState {
    cli: { found: boolean; version: string | null; install: string | null };
    signedIn: boolean;
    user: string | null;
    wired: boolean;
    accounts: ReplySender[];
    /** The channels the CLI says are not in this account's plan or role (`email`, `linkedin`). */
    absent: ReplyChannel[];
    /** The channels whose list could not be read, for any other reason. The other channel's rows stay in both cases. */
    unread: ReplyChannel[];
    /**
     * The links made and not used yet (`reply account pending`), read for each channel that was listed; never with a
     * link, which the CLI does not print.
     */
    pending: ReplyCliPending[];
    /**
     * The channels that were listed and whose links made and not used yet could not be read: the read did not end done,
     * or printed no list. The channel's accounts stay; the CLI saying the channel has none here (`pendingNoneCodes`) is
     * no failure.
     */
    pendingUnread: ReplyChannel[];
    /**
     * What was asked and could not be read, in the card's order, as closed words. Each leaves its own member as a page of
     * an earlier version draws it: the version null with `belowMinimum` set, so no act is offered; not signed in, so no
     * account is asked for; not wired.
     */
    notRead: ReplyFact[];
    /**
     * Where the Reply CLI is older than the one the account acts and Jason's Reply plugin need, the sentence that says
     * so; the card then offers no act. Null otherwise.
     */
    belowMinimum: string | null;
    /** What came of the update of the Reply CLI at this start of the application, as one sentence; absent where none was made. */
    startUpdate?: string | null;
    /**
     * Where the check of the Reply CLI that this start of the application makes stands, as a word: `pending` from the
     * start until it ends, `ended` once its update was made, whatever came of it, once the desktop decided to make none,
     * or once it waits no longer for the machine set-up, the update still to come. The page is told when it ends, and
     * holds the Reply plugin's line back in the mode strip until then. Always sent; optional as the page reads it.
     */
    startCheck?: 'pending' | 'ended';
}

/** An account's mailboxes and LinkedIn accounts as one look read them, and their links still waiting. */
type Lists = Pick<AccountState, 'senders' | 'absent' | 'unread' | 'pending' | 'pendingUnread'>;

/** A sign-in as the card shows it, without what only the note keeps; null for none or a team picked afresh. */
const shownSignIn = (noted: Noted): SignIn | null => (noted === null || noted === 'fresh' ? null : { user: noted.user, team: noted.team });

/** The user Reply's whoami names. */
const userOf = (said: ReplyWhoami) => ({ user: said.login, id: said.userId });

/** How `Setup.#asked` holds an account and a team. */
const askedKey = (account: AccountRef, team: string): string => JSON.stringify([account.type, account.name, team]);

/** What of the accounts as a whole was asked and could not be read: the Reply CLI's profiles, or the routes. */
export type AccountsFact = 'profiles' | 'routes';

/**
 * One Reply account, one profile of the Reply CLI, as Jason's work acts as it, and what was read of it under its own name:
 * its sign-in and, signed in, its senders (its mailboxes and LinkedIn accounts) and the links still waiting.
 */
export interface AccountState extends AccountRef {
    /** Whether the global default route names this account. */
    default: boolean;
    /**
     * Every other global route to Jason's Reply plugin that names it, as closed data: the operation it routes, and the
     * channel where it is one channel's route; a route that names no profile names the Reply CLI's current one.
     */
    defaultFor: DefaultFor[];
    /** Whether the Reply CLI holds the profile; false for one only a route names, of which nothing was asked. */
    held: boolean;
    /**
     * Whether a route to Jason's Reply plugin that names no profile follows it, it being the Reply CLI's current profile,
     * or might, the routes not read: then it is not removed, since that would move the route to `default`'s login.
     */
    followed: boolean;
    signedIn: boolean;
    user: string | null;
    /**
     * The team the account is bound to (`bindingOf`), by its id and the name Reply gives it, or the note keeps where its
     * teams were not read: empty where Reply gives it none, null where it was not read. Null while it is not signed in, or
     * signed in and bound to none.
     */
    team: BoundTeam | null;
    senders: ReplySender[];
    absent: ReplyChannel[];
    unread: ReplyChannel[];
    pending: ReplyCliPending[];
    pendingUnread: ReplyChannel[];
    /**
     * `auth` where its sign-in could not be read: no sign-in then, and nothing listed; `teams` where the teams of its
     * sign-in could not be read, its other reads standing; `login` where `auth status` did not name the sign-in and the
     * Reply CLI could not say who it is once it was bound.
     */
    notRead: ('auth' | 'teams' | 'login')[];
    /**
     * The teams its sign-in can act in, where it can act in more than one and is bound to none (`bindingOf`): the choice
     * the card offers. Absent otherwise; its channels are unread then, since Reply lists none of them without a team.
     */
    teams?: ReplyTeam[];
    /**
     * Who the account was last seen signed in as, and in which team, as the desktop's note keeps it; null where nothing is
     * noted.
     */
    lastSignIn: SignIn | null;
    /**
     * How this look found its sign-in changed from what the note says (`bindingOf`), until Keep it or a sign-out: another
     * login, the same login no longer in the noted team, the same login in another of its teams, or a login not known
     * before it has a team; null where it is not.
     */
    change: Change | null;
    /**
     * The API address its profile points at, where that is not Reply's production (`nonProductionApi`); absent on
     * production, and where it points could not be read.
     */
    nonProductionApi?: string;
}

/**
 * The accounts, read at each status and kept nowhere: none without a Reply CLI at the minimum; else the default first, then
 * the CLI's own order, then the profiles only a route names.
 */
export interface AccountsState {
    accounts: AccountState[];
    /** What could not be read: no account where the profiles were not, none marked default where the routes were not. */
    notRead: AccountsFact[];
    /** Whether the routes were read and no global default is set at all, whatever its plugin. */
    noDefault: boolean;
}

/**
 * A link the Reply CLI made, opened in the person's browser by the main process: what the page learns of it. The link
 * itself never reaches the page.
 */
export interface ReplyOpened {
    opened: true;
    channel: ReplyCliChannel;
    kind: 'connect' | 'reconnect';
    linkKind: 'connection_link';
    expiresAt: string | null;
    pendingId: number | null;
}

/**
 * A connection link the Reply CLI made, written to the clipboard by the main process for the person to hand on: what the
 * page learns of it. The link itself never reaches the page.
 */
export interface ReplyCopied {
    copied: true;
    channel: ReplyCliChannel;
    kind: 'connect' | 'reconnect';
    linkKind: 'connection_link';
    expiresAt: string | null;
    pendingId: number | null;
}

/** An act on an account that the CLI did. */
export interface ReplyDone {
    done: true;
}

export type { ReplyRefusal } from './reply-senders.ts';

export interface PluginState {
    id: string;
    label: string;
    description: string;
    target: { key: 'channel' | 'chat' | 'topic' | 'url'; label: string };
    tokenLabel: string | null;
    serverLabel: string | null;
    note: string | null;
    configured: boolean;
    runtime: PluginRuntime | null;
    binding: boolean | null;
}

export interface SetupState {
    hosts: HostState[];
    reply: ReplyState;
    /** The Reply accounts, one per profile. Always sent; optional as the page reads it, since an earlier desktop sends none. */
    accounts?: AccountsState;
    runtimeVersion: string | null;
    bundleVersion: string;
    /**
     * When the runtime is another version than the one the desktop carries, the sentence that says so and names the way
     * to one version: Check for Updates when the runtime runs from the jason the desktop carries, which `jason update`
     * refuses to change, and `jason update` when it is another installation's; on the deb the restart item or `jason
     * runtime restart`, or the system's update where this desktop is behind; null when they are one version or no runtime
     * answers.
     */
    versionsDiffer: string | null;
    plugins: PluginState[];
    /**
     * Why the settings file could not be read, in the words of the desktop's parse or of the runtime's last reload
     * (`settings_file_unreadable`); null when it could.
     */
    settingsRefused: string | null;
}

export interface SetupDeps {
    paths: DesktopPaths;
    cli: Cli;
    programs: Programs;
    keys: ProviderKeys;
    log: Logger;
    bundleVersion: string;
    runtimeVersion(): string | null;
    /** Whether the runtime runs from the jason the desktop carries (`own-runtime.ts`), as the start found it; absent, it does not. */
    runtimeIsOurs?(): boolean;
    /** The update item's key as a person reads it where the item can act, null where not (`updateKeyWords`); absent, null. */
    updateKey?(): string | null;
    /** Whether this is the deb, which the system updates and which has no update item of its own; absent, it is not. */
    deb?: boolean;
    settings(): DesktopSettings;
    /**
     * Writes the desktop's own settings whole, and holds them as `settings()` answers from then on: the mark of a profile
     * being made for the roles (`connectRoles`). Absent, no mark can be written, and no profile is made.
     */
    saveSettings?(next: DesktopSettings): void;
    /** The desktop's own environment, where a `JASON_` variable may set the switch of the start's update (`updateSwitch`). */
    environment: Readonly<Record<string, string | undefined>>;
    /**
     * The name of the person signed in to this computer, as the operating system gives it: a change of the global default
     * is journaled as theirs, `human:<name>`. Absent, or a name an actor cannot hold (`usableAccount`), no default is
     * changed.
     */
    person?: string | null;
    clock?: () => number;
    /** Whether Full access is offered; absent, this version's switch (`fullAccessOffered`). */
    fullAccessOffered?: boolean;
    /**
     * Why Claude Code's bypass, which Full access runs in, is not available here, as the chat knows it
     * (`Chat.bypassUnavailable`); absent, nothing is known, and a turn finds it.
     */
    bypass?(): Bypass | null;
    /** The pass that puts the chat folder's top right (`ownAgentFolder`); a test gives one that fails. */
    ownChatFolder?: (chat: string, records: string) => void;
    /**
     * The person's yes to one of the Reply card's acts, asked by the main process in a native box of its own; absent, the
     * answer is no.
     */
    confirm?(box: ReplyBox): Promise<boolean>;
    /** Hands a link to the system opener, the person's browser; absent, no link is opened. */
    openLink?(link: string): Promise<void>;
    /** Writes a link to the system clipboard, where the person asked for a copy; absent, no link is copied. */
    copyLink?(link: string): void;
    /** The models Codex offered when last asked, and the background ask of the ones a day old (`model-lists.ts`). */
    models?: { lists(): KeptLists; refresh(hostIds: readonly string[]): void; unread(hostId: string): 'waiting' | 'failed' | null };
    /**
     * The environment and the home the person's own MCP servers are read under (`own-servers.ts`), which say where Claude
     * Code's and Codex's files are; absent, no file is read and no host names a server.
     */
    ownServers?: { env: Readonly<Record<string, string | undefined>>; home: string; reader?: OwnServers };
}

const signInLimit = 5 * 60_000;
/** How long `jason profile create` is given. */
const profileLimit = 60_000;
/** The reason the runtime journals with the default the desktop names; fixed text, never a person's. */
export const namingReason = 'Jason Desktop named the profile it made for the agents’ roles';
/** The reason the runtime journals with the default a person's Use names; fixed text, never a person's. */
export const usingReason = 'Chosen for the agents’ roles with Use in Jason Desktop';

/**
 * What one call of `connectRoles` came to: already settled in this run, a default already in force, profiles or the
 * default in force it could not read, profiles that are there, no connected host, no mark it could write, a create
 * refused, a default the runtime refused to name, a default not named because the runtime did not answer the look
 * just before the naming or the naming itself, a profile made and named, a profile made beside a default a person named
 * meanwhile, which is kept, or a profile made earlier named now.
 */
export type RolesOutcome =
    | 'settled'
    | 'default_named'
    | 'profiles_unread'
    | 'profiles_exist'
    | 'no_host'
    | 'unmarked'
    | 'create_refused'
    | 'default_unwritten'
    | 'default_unanswered'
    | 'made'
    | 'made_unnamed'
    | 'named';
/** The ends of `connectRoles` that leave it to the next turn: a runtime that did not answer, or no connected host. */
const unsettling: ReadonlySet<RolesOutcome> = new Set(['settled', 'profiles_unread', 'no_host', 'default_unanswered']);
/**
 * The ends of `connectRoles` where the default profile in force, which a listing reads, may have changed: named, or asked
 * for with no answer. The others changed nothing a listing reads.
 */
const rolesDefaultMoved: ReadonlySet<RolesOutcome> = new Set(['made', 'named', 'default_unanswered']);
export const installLimit = 5 * 60_000;

function readText(file: string): string | null {
    try {
        return readFileSync(file, 'utf8');
    } catch (error) {
        if ((error as NodeJS.ErrnoException).code === 'ENOENT') return null;
        throw new Refused('The settings file could not be read, so it was left as it is.');
    }
}

/**
 * Writes a file through one beside it renamed over it, so a reader sees the old text or the new and never half; a rename
 * that fails takes the file beside it away again.
 */
export function writeAtomically(file: string, text: string): void {
    mkdirSync(dirname(file), { recursive: true });
    const temporary = settingsTemporary(file);
    try {
        writeFileSync(temporary, text, { mode: 0o600 });
        renameSync(temporary, file);
    } catch (error) {
        rmSync(temporary, { force: true });
        throw error;
    }
}

/** What the page adds to `settingsRefused`, and what a refusal thrown for the same file ends with. */
const editByHand = 'Edit it by hand, then press Reload.';

function words(result: RunResult, otherwise: string): string {
    return (result.stderr.trim() || result.stdout.trim() || otherwise).slice(-1000);
}

/**
 * A refusal of the Reply CLI's in the desktop's words: the desktop's sentence, then the Reply CLI's own reason as the error
 * it prints last under `--json` gives it, its detail, else its title. Never its hint, which tells the person to run another
 * command (to sign in with no profile named, say), nor its other fields; an ending that does not read as that error is the
 * sentence alone.
 */
function withCliReason(result: RunResult, sentence: string): string {
    const last = result.stderr.trim().split('\n').pop() ?? '';
    let error: { detail?: unknown; title?: unknown } | undefined;
    try {
        error = (JSON.parse(last) as { error?: { detail?: unknown; title?: unknown } } | null)?.error;
    } catch {
        return sentence;
    }
    const reason = [error?.detail, error?.title].find((each): each is string => typeof each === 'string' && each.trim() !== '');
    return reason === undefined ? sentence : withReason(sentence, reason);
}

/**
 * The runtime's own words for a verb it refused: the error's message and the message of each detail it carries (what
 * became of the settings file, say), as the CLI prints them on stdout; the program's words otherwise.
 */
export function runtimeWords(result: CliResult, otherwise: string): string {
    try {
        const error = (JSON.parse(result.stdout) as { error?: { message?: unknown; details?: unknown } } | null)?.error;
        if (typeof error?.message === 'string') {
            const details = Array.isArray(error.details) ? error.details : [];
            const said = details.map((each) => (each as { message?: unknown } | null)?.message).filter((each): each is string => typeof each === 'string');
            return [error.message, ...said].join(' ').slice(0, 1000);
        }
    } catch {
        // Not the CLI's JSON: the program's own words.
    }
    return words(result, otherwise);
}

/** The words of a reload refused because the settings file could not be read, or null for any other answer. */
export function settingsUnreadable(stdout: string): string | null {
    try {
        const raw = JSON.parse(stdout) as { error?: { details?: { code?: unknown; message?: unknown }[] } };
        const details = Array.isArray(raw.error?.details) ? raw.error.details : [];
        const detail = details.find((entry) => entry?.code === 'settings_file_unreadable');
        return typeof detail?.message === 'string' ? detail.message : null;
    } catch {
        return null;
    }
}

/**
 * Pure: what the system screen says when the runtime and the desktop are two versions. A runtime that runs from the jason
 * the desktop carries is brought to one version by the desktop's own update, since `jason update` refuses to change a
 * carried file; another installation's runtime older than this desktop by `jason update`; and one newer than this desktop
 * by the desktop's own update too, since it is this desktop that is behind, and updating the other installation brings
 * nothing. The deb (`deb`) has no such update of its own: the system updates it, and this application's own runtime of an
 * older build is brought up by the restart the start offers (`packageMovedTo`), or the CLI's.
 */
export function versionsDifferSentence(runtimeVersion: string | null, bundleVersion: string, ours: boolean, key: string | null, deb = false): string | null {
    // A build after a plus names the commit, not another version.
    const bare = (version: string) => version.split('+')[0];
    if (runtimeVersion === null || bare(runtimeVersion) === bare(bundleVersion)) return null;
    const desktopBehind = compareVersions(bare(runtimeVersion) ?? runtimeVersion, bare(bundleVersion) ?? bundleVersion) > 0;
    const said = `The runtime is ${runtimeVersion} and this desktop carries ${bundleVersion}, so the UI the runtime serves is shown as it is.`;
    // A Windows or Linux window shows no menu bar, so the tray's menu is named too, and the item's key, which a desktop
    // that shows no tray leaves as the one way in; the key only where the item can act (`updateKeyWords`).
    const menus = "in the desktop's Jason menu, or in the menu of its icon in the tray";
    if (deb && desktopBehind) return `${said} Update Jason Desktop through your system, with Software Updater or \`sudo apt update && sudo apt upgrade\` in a terminal, to bring them to one version.`;
    if (deb && ours) return `${said} Choose Restart Jason and Its Runtime to Finish the Update to ${bundleVersion} ${menus}, or run \`jason runtime restart\`, to bring them to one version.`;
    const menu = `Choose Check for Updates… ${menus}`;
    const way = ours || desktopBehind ? (key === null ? menu : `${menu}, or press ${key},`) : 'Run `jason update`';
    return `${said} ${way} to bring them to one version.`;
}

/** The line a host whose models were never read shows instead of its list. */
export function unreadSentence(label: string, unread: 'waiting' | 'failed' | null): string | null {
    if (unread === 'failed') return `${label}'s models could not be read, so the turn uses its own default model.`;
    if (unread === 'waiting') return `${label}'s models are being read; until then the turn uses its own default model.`;
    return null;
}

/** The account a page of an earlier version knows: the default, else the profile `default`; null where neither is listed. */
const actingOf = (accounts: readonly AccountState[]): AccountState | null =>
    accounts.find((each) => each.default) ?? accounts.find((each) => each.name === 'default') ?? null;

/** An act refused by the desktop itself, before or after the CLI: its sentence, no code and nothing offered. */
function refusal(refused: string, reread = false): ReplyRefusal {
    return { refused, code: null, offer: null, reread };
}

/**
 * An act on a Reply account the Reply CLI refused: the desktop's sentence and the CLI's reason (`withCliReason`), its code,
 * and the accounts read again to show where they stand.
 */
function cliRefusal(result: RunResult, sentence: string): ReplyRefusal {
    const ending = replyCliEnding(result);
    return { refused: withCliReason(result, sentence), code: ending.kind === 'refused' ? ending.code : null, offer: null, reread: true };
}

/**
 * What a placing of Jason's Reply plugin did: nothing, it being in place; a load alone; the grant and a load; the package
 * and a load; or all three.
 */
type Placing = 'in_place' | 'loaded' | 'granted' | 'placed' | 'placed_granted';

/**
 * What came of the look at the Reply CLI at a start: npm was run and it is `updated`, `still_old` or `failed`; or nothing was
 * run, since it was looked at already, `reply` was not found, the switch is off or could not be read, the version was not
 * read or is at the minimum, npm was not found, this minimum was tried before, a plugin that runs it can be at work (`in_use`)
 * or the runtime did not say (`plugins_unread`), or the mark could not be written.
 */
export type StartUpdateOutcome =
    | 'updated' | 'still_old' | 'failed'
    | 'settled' | 'not_found' | 'off' | 'switch_unread' | 'version_unread' | 'current' | 'no_npm' | 'tried' | 'in_use' | 'plugins_unread' | 'unmarked';

/** The outcomes of the start's look at the Reply CLI that ran npm: the others changed nothing a listing reads. */
const npmRan: ReadonlySet<StartUpdateOutcome> = new Set(['updated', 'still_old', 'failed']);

export class Setup {
    #deps: SetupDeps;
    #listeners = new Set<() => void>();
    #versions = new Map<ProgramName, string | null>();
    #queue: Promise<unknown> = Promise.resolve();
    /** The writes of the Reply CLI's profiles, one after another (`#writingConfig`). */
    #configWrites: Promise<unknown> = Promise.resolve();
    /** What the last reload said about the settings file, when it could not read it. */
    #reloadRefusal: string | null = null;
    /** Whether an act on a Reply account is going: one at a time, and a second click meanwhile is refused. */
    #acts = false;
    /**
     * The accounts the next look checks against their note (`bindingOf`) whatever their login and pin, each with the count
     * of marks at its own mark: one signed in on its card in this run; one a look found changed, while that change is not
     * answered, so no look asks Reply for its mailboxes and LinkedIn accounts meanwhile; and one a look left undecided,
     * another look having noted it meanwhile or Reply naming another login once it was decided. Only a look that began
     * after the mark, and found the account bound with nothing changed and nothing unread, clears it.
     */
    #toCheck = new Map<string, number>();
    #marks = 0;
    /**
     * Each account and team `auth whoami` was asked in during this run (`#whoami`): with no sign-in since, a look asks it of
     * an account whose note holds no organization for its team only while it is not here (`whoIsAsked`), once in each run.
     */
    #asked = new Set<string>();
    /**
     * The accounts the last read gave, each with the senders it listed: an act names only one of them and one of its
     * senders, and its box names the sender as the list does.
     */
    #listed: AccountState[] = [];
    /**
     * Each account's sign-in at the read that gave `#listed`: the team its profile pinned, which Choose may write over, and
     * Reply's id of the user the card shows, which Choose and Keep it note beside the login.
     */
    #listedSignIns = new Map<string, { pin: string | null; userId: string | null }>();
    /** The profiles a route named at the last read, at any level; null where the routes were not read, or none was read. */
    #routed: Set<string> | null = null;
    /** How many reads of the setup have begun, and which of them gave `#listed` and `#routed`. */
    #readsBegun = 0;
    #listedBy = 0;
    /** The accounts removed in this run, each with the reads begun by then: a read that began before notes nothing of it. */
    #removed = new Map<string, number>();
    /** The offers a refusal opened, each for the one ask it answered: a forced delete. */
    #doors = new Set<string>();
    /** The reader of the person's own MCP servers, which remembers each file's last answer (`own-servers.ts`). */
    #own: OwnServers;
    /** The kind each program's file was last logged as unread with; absent while it lists. */
    #unread = new Map<string, UnreadKind>();
    /** Claude Code's version as the last listing read it (`handedClaudeVersion`); undefined before one read it. */
    #handedClaude: string | null | undefined = undefined;
    /** Whether the start's update of the Reply CLI was looked at in this run: once, whatever came of it. */
    #startUpdateLooked = false;
    /** What came of the start's update, in this run alone; null where none was made. */
    #startUpdate: { updated: boolean; said: string } | null = null;
    /**
     * Whether the start's check of the Reply CLI has ended in this run; until then the strip holds Reply's line back.
     */
    #startCheckEnded = false;
    /** Whether `connectRoles` came to an end in this run: it then asks nothing more until the next start. */
    #rolesSettled = false;
    /**
     * How many changes have ended, moved only by the doors every change passes through: the setup's queue (`#serial`), an
     * act on a Reply account (`#acting`), and a change the desktop learnt of elsewhere (`announceChange`).
     */
    #ended = 0;
    /** The listing being read now, and how many changes had ended when it began. */
    #reading: { listing: Promise<SetupState>; ended: number } | null = null;

    constructor(deps: SetupDeps) {
        this.#deps = deps;
        this.#own = deps.ownServers?.reader ?? new OwnServers();
    }

    onChanged(listener: () => void): () => void {
        this.#listeners.add(listener);
        return () => this.#listeners.delete(listener);
    }

    #changed(): void {
        for (const listener of this.#listeners) listener();
    }

    /**
     * A change the desktop learnt of outside the setup, the models Codex offers or Full access's bypass: announced to the
     * page as every change is, so a listing asked after it is never answered by a read that began before it.
     */
    announceChange(): void {
        this.#ended += 1;
        this.#changed();
    }

    /**
     * One change at a time: two cards clicked together never write the settings file over each other. Each ends a change,
     * but where `changed` says of what it answered that it changed nothing a listing reads.
     */
    #serial<T>(work: () => Promise<T>, changed: (answer: T) => boolean = () => true): Promise<T> {
        const next = this.#queue.then(work, work).then(
            (answer) => {
                if (changed(answer)) this.#ended += 1;
                return answer;
            },
            (error: unknown) => {
                this.#ended += 1;
                throw error;
            },
        );
        this.#queue = next.catch(() => undefined);
        return next;
    }

    async #run(program: ProgramName, verb: string, args: readonly string[], options?: { stdin?: string; timeoutMs?: number; cwd?: string }): Promise<RunResult> {
        const clock = this.#deps.clock ?? Date.now;
        const started = clock();
        const result = await this.#deps.programs.run(program, args, options);
        this.#deps.log.info('program_exited', { Program: program, Verb: verb, Exit: result.exit, ElapsedMs: clock() - started });
        return result;
    }

    async #version(program: ProgramName): Promise<string | null> {
        if (this.#versions.has(program)) return this.#versions.get(program) ?? null;
        const version = versionIn((await this.#run(program, '--version', ['--version'], { timeoutMs: 15_000 })).stdout);
        this.#versions.set(program, version);
        return version;
    }

    /** The id `installProgram` takes for a program missing here, when the desktop installs it and npm is there to. */
    #installable(program: ProgramName): string | null {
        return programInstalls[program]?.npmPackage && this.#deps.programs.find('npm') !== null ? program : null;
    }

    /**
     * Claude Code's version, read at each listing and never remembered, and the sentences when it is below the floor and
     * below the floor of Full access.
     */
    async #claude(): Promise<{ version: string | null; belowFloor: string | null; fullAccess: string | null }> {
        const version = printedVersion(await this.#run('claude', '--version', ['--version'], { timeoutMs: versionLimitMs }));
        return { version, belowFloor: floorSentence(version), fullAccess: fullAccessSentence(version) };
    }

    /**
     * The names of a program host's own MCP servers; a file that could not be read is logged by program and kind only,
     * once for each change of its kind, not at every listing.
     */
    async #ownServers(program: string, found: boolean): Promise<string[]> {
        const where = this.#deps.ownServers;
        if (where === undefined || !found || (program !== 'claude' && program !== 'codex')) return [];
        const read = await this.#own.read(program, where.env, where.home);
        if ('names' in read) {
            this.#unread.delete(program);
            return read.names;
        }
        if (this.#unread.get(program) !== read.unread) this.#deps.log.info('own_servers_unread', { Program: program, Kind: read.unread });
        this.#unread.set(program, read.unread);
        return [];
    }

    /**
     * Claude Code's version as the last listing of the hosts read it, from which the page drew the mode a turn runs in
     * (`runs`): null when it could not be read, undefined before a listing read it or when Claude Code was not found. A turn
     * holds the version it reads itself to it (`Chat.send`), and asks the page to read the hosts again when the two differ
     * across the floor of Full access. Every page is handed the one version, so a listing whose version stands across that
     * floor from the last listing's asks every page to read the hosts again itself.
     */
    handedClaudeVersion(): string | null | undefined {
        return this.#handedClaude;
    }

    /**
     * What is set up, each host and each Reply account. A listing asked while one is read is answered by that read, so one
     * check reads each account once: a click on Check again that also brings the window forward, or an act's end and the
     * change it announced, both ask. A listing asked after a change ended is never answered by a read that began before
     * it ended. Whether the start's check of the Reply CLI has ended is taken as the read ends, so a window told of that
     * end may be answered by the read already going.
     */
    status(): Promise<SetupState> {
        const going = this.#reading;
        if (going !== null && going.ended === this.#ended) return going.listing;
        const listing = this.#status()
            .then((state): SetupState => ({ ...state, reply: { ...state.reply, startCheck: this.#startCheck() } }))
            .finally(() => {
                if (this.#reading?.listing === listing) this.#reading = null;
            });
        this.#reading = { listing, ended: this.#ended };
        return listing;
    }

    async #status(): Promise<SetupState> {
        this.#readsBegun += 1;
        const begun = this.#readsBegun;
        const { programs, keys } = this.#deps;
        const stored = new Set(keys.stored());
        const claude = programs.find('claude') !== null ? await this.#claude() : null;
        const before = this.#handedClaude;
        this.#handedClaude = claude === null ? undefined : claude.version;
        // A page may have drawn its turns from the last listing, which another page asked for: across the floor of Full
        // access, every page is asked to draw them again.
        if (before !== undefined && claude !== null && (fullAccessSentence(before) === null) !== (claude.fullAccess === null)) this.#changed();
        const kept = this.#deps.models?.lists() ?? new Map();
        const offered = (id: string, label: string) => {
            const { models, typed } = offeredModels(id, kept, claude?.version ?? null);
            return { models, typedModel: typed, modelsUnread: unreadSentence(label, this.#deps.models?.unread(id) ?? null) };
        };
        // Codex has Full access with no floor; Claude Code has it from its floor of Full access. A Claude Code not found offers
        // nothing yet. While this version does not offer it, every host that drives the chat says so.
        const offeredHere = this.#deps.fullAccessOffered ?? fullAccessOffered;
        const bypass = this.#deps.bypass?.() ?? null;
        const fullAccessOn = (program: string, drives: boolean) =>
            !drives
                ? null
                : !offeredHere
                    ? fullAccessNotYet
                    : program === 'claude'
                        ? claude === null
                            ? null
                            : claude.fullAccess
                        : null;
        const autoEditOn = (program: string, drives: boolean) => (drives && program === 'codex' ? codexAutoEdit : null);
        // What a turn on the host runs in, by the record's mode, from the window and from a tab (unreachable since 0.49.3):
        // the turn's own rule.
        const runsOn = (program: string, drives: boolean): HostState['runs'] => {
            const steps = (fromTab: boolean): TurnSteps => {
                const out: TurnSteps = {};
                if (!drives) return out;
                for (const recorded of chatModes) {
                    const turn = turnMode(recorded, program, { fromTab, offered: offeredHere, claudeVersion: program === 'claude' ? claude?.version : undefined, bypass });
                    if (turn.why !== null) out[recorded] = { mode: turn.mode, why: turn.why.text };
                }
                return out;
            };
            return { window: steps(false), tab: steps(true) };
        };

        let settings: unknown = {};
        let settingsRefused = this.#reloadRefusal;
        try {
            settings = parseInstallationSettings(readText(dataLayout(this.#deps.paths.data).userSettingsFile));
        } catch (error) {
            settings = {};
            settingsRefused = (error as Error).message;
        }
        const roles = await this.#rolesDefault();

        const programHosts: HostState[] = [];
        for (const host of hosts) {
            const found = programs.find(host.program) !== null;
            const own = host.program === 'claude' ? claude : null;
            programHosts.push({
                id: host.id,
                label: host.label,
                kind: 'program',
                found,
                version: !found ? null : own !== null ? own.version : await this.#version(host.program),
                drivesChat: host.drivesChat,
                install: found ? null : this.#installable(host.program),
                belowFloor: own?.belowFloor ?? null,
                fullAccess: fullAccessOn(host.program, host.drivesChat),
                autoEdit: autoEditOn(host.program, host.drivesChat),
                runs: runsOn(host.program, host.drivesChat),
                ...offered(host.id, host.label),
                ownServers: await this.#ownServers(host.program, found),
                // Said only under a host the chat can use, as the page counts it connected: found, and at its floor.
                runsRoles: found && host.drivesChat && (own?.belowFloor ?? null) === null && roles !== null && hostWords[host.program] === roles.host ? roles.name : null,
            });
        }
        const providerHosts: HostState[] = keyProviders.map((provider) => ({
            id: provider.id,
            label: provider.label,
            kind: 'provider',
            found: stored.has(provider.id),
            version: null,
            drivesChat: hosts.find((host) => host.program === provider.program)?.drivesChat ?? false,
            install: null,
            belowFloor: provider.program === 'claude' ? (claude?.belowFloor ?? null) : null,
            fullAccess: fullAccessOn(provider.program, hosts.find((host) => host.program === provider.program)?.drivesChat ?? false),
            autoEdit: autoEditOn(provider.program, hosts.find((host) => host.program === provider.program)?.drivesChat ?? false),
            runs: runsOn(provider.program, hosts.find((host) => host.program === provider.program)?.drivesChat ?? false),
            ...offered(provider.id, provider.label),
            ownServers: [],
            runsRoles: null,
        }));
        // Shown from what is kept; Codex is asked again in the background where it is on the PATH or its key is stored.
        this.#deps.models?.refresh([
            ...(programs.find('codex') !== null ? ['codex'] : []),
            ...(stored.has('codex-api-key') ? ['codex-api-key'] : []),
        ]);

        // One question to the runtime answers both the Reply card and the plugins' cards.
        const listed = await this.#deps.cli.run(['plugin', 'list']);
        const answered = listed.exit === 0 ? pluginRuntimes(listed.stdout) : new Map<string, PluginRuntime>();

        const runtimeVersion = this.#deps.runtimeVersion();
        const { reply, accounts } = await this.#reply(begun, replyPlacement(listed)?.valid ?? null, listed.exit === 0 ? runtimeReplyVersion(listed.stdout) : null);
        return {
            hosts: [...programHosts, ...providerHosts],
            reply,
            accounts,
            runtimeVersion,
            bundleVersion: this.#deps.bundleVersion,
            versionsDiffer: versionsDifferSentence(runtimeVersion, this.#deps.bundleVersion, this.#deps.runtimeIsOurs?.() ?? false, this.#deps.updateKey?.() ?? null, this.#deps.deb ?? false),
            plugins: bundledPlugins.map((plugin) => ({
                id: plugin.id,
                label: plugin.label,
                description: plugin.description,
                target: { key: plugin.target.key, label: plugin.target.label },
                tokenLabel: plugin.tokenLabel,
                serverLabel: plugin.serverLabel,
                note: plugin.note,
                configured: isConfigured(settings, plugin),
                runtime: answered.get(plugin.id) ?? null,
                binding: hasBinding(settings, plugin),
            })),
            settingsRefused,
        };
    }

    /**
     * The default profile in force as the runtime answers it, by its name and its host word, where the runtime answers it
     * as an enabled one; null where none is named or none answers.
     */
    async #rolesDefault(): Promise<{ name: string; host: string } | null> {
        const name = await this.#defaultInForce();
        // Only a name the runtime takes reaches the command line, so none is read as an option.
        if (name === null || name === undefined || !/^[a-z][a-z0-9-]{0,63}$/.test(name)) return null;
        const got = await this.#deps.cli.run(['profile', 'get', name]);
        const profile = got.exit === 0 ? readProfile(got.stdout) : null;
        return profile !== null && profile.name === name && !profile.disabled && profile.host !== null ? { name, host: profile.host } : null;
    }

    /**
     * The host word of the profile the agents' roles run on by default, read by the routine the cards' In use is read by
     * (`#rolesDefault`): null where none is named, none answers as an enabled one, or the runtime could not be asked.
     * "Change Jason itself" opens that host where the person has stated none of their own (`OpenIn.changeJason`).
     */
    async rolesHost(): Promise<string | null> {
        return (await this.#rolesDefault())?.host ?? null;
    }

    /**
     * The Reply CLI, and the accounts: the Reply CLI's profiles and every profile a route to Reply names, each read under
     * its own name as Jason's work acts as it. With no Reply CLI at the minimum nothing is asked of an account, and a
     * profile only a route names is asked nothing. `ReplyState` is the default account's, or the profile `default`'s where
     * no Reply account is the default; its `wired` says that Jason's Reply plugin is valid and the global default names
     * that very account, and no team beside it but the one the account is bound to, so the plugin sends as its sign-in.
     * `valid` is null where the runtime gave no plugin list.
     */
    async #reply(begun: number, valid: boolean | null, runtimeVersion: string | null): Promise<{ reply: ReplyState; accounts: AccountsState }> {
        const none: AccountsState = { accounts: [], notRead: [], noDefault: false };
        const found = this.#deps.programs.find('reply') !== null;
        if (!found) {
            this.#keepListing(begun, [], null);
            return {
                reply: { cli: { found, version: null, install: this.#installable('reply') }, signedIn: false, user: null, wired: false, accounts: [], absent: [], unread: [], pending: [], pendingUnread: [], notRead: [], belowMinimum: null, startCheck: this.#startCheck() },
                accounts: none,
            };
        }
        const version = await this.#replyVersion();
        // Older than the acts and Jason's Reply plugin need, or of a version not read, the CLI is offered the update and
        // asked nothing of an account.
        const current = version !== null && versionAtLeast(version, replyMinimumVersion);
        // What came of the start's update, but a failure only while a CLI below the minimum is still in use, here or by the runtime.
        const startUpdate =
            this.#startUpdate !== null && (this.#startUpdate.updated || !current || (runtimeVersion !== null && !versionAtLeast(runtimeVersion, replyMinimumVersion)))
                ? this.#startUpdate.said
                : null;
        const cli = { found, version, install: current ? null : this.#installable('reply') };
        const tail = { belowMinimum: current ? null : belowMinimumSentence(replyMinimumVersion), ...(startUpdate === null ? {} : { startUpdate }), startCheck: this.#startCheck() };
        const nobody = { signedIn: false, user: null, wired: false, accounts: [], absent: [], unread: [], pending: [], pendingUnread: [] };
        if (!current) {
            this.#keepListing(begun, [], null);
            // Nothing was asked of a sign-in: an earlier page says so, never that nobody is signed in.
            return { reply: { cli, ...nobody, notRead: [...(version === null ? ['version' as const] : []), 'auth'], ...tail }, accounts: none };
        }
        const routes = readRoutes(await this.#deps.cli.run(['route', 'list']));
        const profiles = readProfiles(await this.#run('reply', 'profile list', ['profile', 'list', '--json']));
        const listing = profiles === null ? null : accountsOf(profiles, routes);
        const notes = readAccountNotes(this.#deps.paths.replyAccountNotes);
        const read = await Promise.all(
            (listing?.accounts ?? []).map(async (account) => {
                const noted = notedOf(notes, account);
                const { state, pin, userId, backend } = await this.#readAccount(account, profiles?.available.includes(account.name) ?? false, noted);
                const marked = {
                    ...state,
                    default: listing?.default != null && sameAccount(account, listing.default),
                    defaultFor: listing?.defaultFor.get(account.name) ?? [],
                    followed: listing?.followed === account.name,
                };
                return { account: marked, noted, pin, userId, backend };
            }),
        );
        for (const each of read) {
            const elsewhere = nonProductionApi(each.backend);
            if (elsewhere !== null) each.account = { ...each.account, nonProductionApi: elsewhere };
        }
        // Noted in the listing's order once every account is read, so the note is written in one order.
        const accounts = read.map(({ account, noted, userId }) => (begun <= (this.#removed.get(account.name) ?? 0) ? account : this.#noted(account, noted, userId)));
        // Kept once the read is whole: an act clicked while a read is going names the accounts the last one listed.
        this.#keepListing(begun, accounts, listing?.routed ?? null, new Map(read.map(({ account, pin, userId }) => [account.name, { pin, userId }])));
        const acting = actingOf(accounts);
        const routeUnread = valid === null || routes === null ? ['route' as const] : [];
        const sendsIn = defaultTeam(routes);
        const reply: ReplyState =
            acting === null
                ? { cli, ...nobody, notRead: ['auth', ...routeUnread], ...tail }
                : {
                      cli,
                      signedIn: acting.signedIn,
                      user: acting.user,
                      wired: valid === true && acting.default && (sendsIn === null || sendsIn === acting.team?.id),
                      accounts: acting.senders,
                      absent: acting.absent,
                      unread: acting.unread,
                      pending: acting.pending,
                      pendingUnread: acting.pendingUnread,
                      // The teams are this desktop's own fact: a page of an earlier version knows none.
                      notRead: [...acting.notRead.filter((fact) => fact === 'auth'), ...routeUnread],
                      ...tail,
                  };
        return {
            reply,
            accounts: {
                accounts,
                notRead: [...(profiles === null ? ['profiles' as const] : []), ...(routes === null ? ['routes' as const] : [])],
                noDefault: routes !== null && routes.globalDefault === null,
            },
        };
    }

    /**
     * One account's reads, each run naming its profile: the sign-in and its pin and what follows from it (`#readSignIn`);
     * beside it, where the profile points (`profile show`, which names the profile as its argument). A profile the CLI
     * does not hold is asked nothing.
     */
    async #readAccount(account: AccountRef, held: boolean, noted: Noted): Promise<{ state: AccountState; pin: string | null; userId: string | null; backend: ProfileBackend | null }> {
        const [read, backend] = await Promise.all([this.#readSignIn(account, held, noted), held ? this.#backend(account) : null]);
        return { ...read, backend };
    }

    /**
     * An account's sign-in, in three steps. With no sign-in since (`noSignInSince`), the account is its note: its lists are
     * read, and only Reply refusing them in that team gives a sign-in away; where nothing is to be asked of Reply besides
     * (`whoIsAsked`), that is all. Otherwise what Reply and the Reply CLI say of the sign-in is gathered whole: after a
     * sign-in its teams, the lists first where nothing else can decide (`restsOnPin`), and who it is, asked in the team it
     * acts in (`asksIn`); `bindingOf` decides once; and the account is bound where it says, who it is asked in the team
     * pinned where the gathering could not ask there. The pin is the one the account is left with.
     */
    async #readSignIn(account: AccountRef, held: boolean, noted: Noted): Promise<{ state: AccountState; pin: string | null; userId: string | null }> {
        const read: AccountState = { ...account, default: false, defaultFor: [], held, followed: false, signedIn: false, user: null, team: null, senders: [], absent: [], unread: [], pending: [], pendingUnread: [], notRead: [], lastSignIn: shownSignIn(noted), change: null };
        if (!held) return { state: read, pin: null, userId: null };
        const marks = this.#marks;
        // A sign-in that could not be read is named, and is no sign-in: nothing is listed.
        const auth = await this.#pinned(account);
        if (auth === null) return { state: { ...read, notRead: ['auth'] }, pin: null, userId: null };
        if (!auth.signedIn) return { state: read, pin: auth.team, userId: null };
        const signedIn: AccountState = { ...read, signedIn: true, user: auth.user };
        const files: SignInFiles = { pin: auth.team, login: auth.user, userId: auth.userId, keyTeam: auth.keyTeam };
        const asked = (team: string) => this.#asked.has(askedKey(account, team));
        let listed: Lists | null = null;
        let refused = false;
        const sinceNote = noSignInSince(files, noted, this.#toCheck.has(account.name));
        if (sinceNote) {
            const { lists, outOfTeam } = await this.#lists(account);
            if (!outOfTeam && !whoIsAsked(false, noted, asked)) return { state: { ...signedIn, ...lists, user: noted.user, team: noted.team }, pin: auth.team, userId: noted.id ?? auth.userId };
            [listed, refused] = [lists, outOfTeam];
        }
        const signIn = !sinceNote || refused;
        // With no sign-in since, Reply is not asked for the teams: undefined, as the teams are no part of what is decided.
        const teamsRead = signIn ? await this.#teams(account) : undefined;
        const teams = teamsRead ?? null;
        if (listed === null && restsOnPin({ ...files, teams }, noted)) {
            const { lists, outOfTeam } = await this.#lists(account);
            [listed, refused] = [lists, outOfTeam];
        }
        const gathered = { ...files, refused, teams };
        const askedIn = whoIsAsked(signIn, noted, asked) ? asksIn(gathered) : null;
        const who = askedIn === null ? undefined : await this.#whoami(account, askedIn);
        // Who it is: Reply's answer where it names the user the Reply CLI's files hold, by whatever address, or they hold
        // none, or it names another user than noted, which gives the sign-in away; the files' otherwise.
        const fromFiles = files.login === null ? null : { user: files.login, id: files.userId };
        const givenAway = who != null && noted !== null && noted !== 'fresh' && !sameUser(userOf(who), noted);
        const [login, userId] = who != null && (givenAway || fromFiles === null || sameUser(userOf(who), fromFiles)) ? [who.login, who.userId] : [files.login, files.userId];
        const binding = bindingOf({ ...gathered, login, userId }, noted);
        let [team, pin, said] = [binding.team, auth.team, who];
        if (binding.bind !== null) {
            team = await this.#bind(account, binding.bind, auth.team, actsIn(gathered));
            if (team !== null) {
                pin = team.id;
                if (team.id !== askedIn) said = await this.#whoami(account, team.id);
            }
        }
        // Reply's answer for this sign-in's user; one for another user, as a terminal signs in meanwhile, is left to the
        // next look.
        const answer = said != null && (login === null || sameUser(userOf(said), { user: login, id: userId })) ? said : null;
        const later = said != null && answer === null;
        const [user, id] = answer !== null ? [answer.login, answer.userId] : [login, userId];
        const notRead: AccountState['notRead'] = [...(teamsRead === null ? ['teams' as const] : []), ...(user === null && said !== undefined ? ['login' as const] : [])];
        // Checked by a look that began after the mark, that bound it with nothing changed and nothing unread; a change is
        // checked again at every look until it is answered.
        if (binding.change !== null || later) this.#markToCheck(account);
        else if (team !== null && user !== null && teamsRead !== null) this.#checked(account, marks);
        const now: AccountState = { ...signedIn, user, team: team === null ? null : namedBy(team, answer), change: binding.change, notRead };
        if (binding.choose !== null) return { state: { ...now, unread: [...replyChannels], teams: binding.choose }, pin, userId: id };
        if (!binding.lists) return { state: now, pin, userId: id };
        // The account's own read made before it was decided stands for the same pin.
        return { state: { ...now, ...(listed !== null && binding.bind === null ? listed : (await this.#lists(account)).lists) }, pin, userId: id };
    }

    /** Marks the account for the next look to check (`#toCheck`). */
    #markToCheck(account: AccountRef): void {
        this.#marks += 1;
        this.#toCheck.set(account.name, this.#marks);
    }

    /** The account is checked, by a look that began after its mark (`marks`, taken at the look's start). */
    #checked(account: AccountRef, marks: number): void {
        const at = this.#toCheck.get(account.name);
        if (at !== undefined && at <= marks) this.#toCheck.delete(account.name);
    }

    /**
     * An account as the desktop's note says it was (`account-notes.ts`). One signed in and bound, with its login and no
     * change, is noted now, with Reply's id of its user (`userId`), where nothing is noted for it, or its team is picked
     * afresh; the noted sign-in is noted again where this look read of it what the note does not hold (`signInRead`), and
     * shows it as the note keeps it (`signInKept`). Where another look wrote the note meanwhile, this one decides nothing
     * of it: the card names what the note holds now, and the account is marked for the next look, whose `bindingOf` reads
     * it against that note. A team picked afresh is set aside no longer once it is bound, whether or not its login is known.
     */
    #noted(account: AccountState, noted: Noted, userId: string | null): AccountState {
        if (!account.signedIn || account.change !== null || account.team === null) return account;
        if (account.user === null) {
            if (noted === 'fresh') this.#note(account, null, 'look', noted);
            return account;
        }
        let now: NotedSignIn = { user: account.user, ...(userId === null ? {} : { id: userId }), team: account.team };
        if (noted !== null && noted !== 'fresh') {
            if (!sameSignIn(noted, now)) return account;
            const kept = signInKept(noted, now);
            if (!signInRead(noted, now)) return { ...account, user: kept.user, team: kept.team };
            now = kept;
        }
        const kept = this.#note(account, now, 'look', noted);
        const shown = { ...account, user: now.user, team: now.team };
        if (kept === undefined) return shown;
        if (kept === null || kept === 'fresh') return { ...shown, lastSignIn: null };
        if (!sameSignIn(kept, now)) this.#markToCheck(account);
        return { ...shown, lastSignIn: shownSignIn(kept) };
    }

    /**
     * Writes one account's entry in the note, or takes it away (null), on the file as it is at this moment, read and written in
     * one turn of the event loop so no other write comes between. A look passes what it read (`over`), and writes only where
     * the file still holds that; otherwise what another look wrote meanwhile is the answer. What is noted for the account
     * after it; undefined where the note could not be written.
     */
    #note(account: AccountRef, entry: NotedSignIn | 'fresh' | null, moment: 'look' | 'choose' | 'keep' | 'remove', over?: Noted): Noted | undefined {
        const file = this.#deps.paths.replyAccountNotes;
        const notes = readAccountNotes(file);
        const next = withNote(notes, account, entry, over);
        if (next === null) return notedOf(notes, account);
        if (entry === null && notedOf(notes, account) === null) return null;
        try {
            writeAccountNotes(file, next);
        } catch {
            this.#deps.log.warn('reply_account_noted', { Moment: moment, Written: false });
            return undefined;
        }
        this.#deps.log.info('reply_account_noted', { Moment: moment, Written: true });
        return entry;
    }

    /**
     * Where an account's profile points (`reply profile show --json -- <name>`), with no call to Reply; null where not read.
     * The name comes after `--`, so one shaped like a flag of the Reply CLI is read as a name.
     */
    async #backend(account: AccountRef): Promise<ProfileBackend | null> {
        return readBackend(await this.#run('reply', 'profile show', ['profile', 'show', '--json', '--', account.name]));
    }

    /**
     * Who an account's sign-in is (`reply --profile <name> auth whoami --json`), asked in `team`, the one its runs act in
     * now, with the team's name and organization where Reply's answer carries them; null where not said. Kept in this run's
     * `#asked` before it is asked, so two looks at once ask once.
     */
    async #whoami(account: AccountRef, team: string): Promise<ReplyWhoami | null> {
        this.#asked.add(askedKey(account, team));
        return readReplyWhoami(await this.#run('reply', 'auth whoami', [...accountArguments(account), 'auth', 'whoami', '--json'], { timeoutMs: replyActLimitMs }));
    }

    /** The teams an account's sign-in can act in (`reply --profile <name> team list --json`); null where they were not read. */
    async #teams(account: AccountRef): Promise<ReplyTeam[] | null> {
        return readTeams(await this.#run('reply', 'team list', [...accountArguments(account), 'team', 'list', '--json'], { timeoutMs: replyActLimitMs }));
    }

    /**
     * Binds an account to the team `bindingOf` gives it with no question (`reply --profile <name> team use <id>`): its
     * sign-in's one team, where it is bound as a first sign-in is, or its noted team put back; nothing is lost by it. The log
     * says that it was done. The pin is read again in the write's own turn, and one other than the look saw, a terminal's,
     * is left as it is: its team where the sign-in acts in it (`own`, as the look read them), else none. The team bound, or
     * null where none is.
     */
    async #bind(account: AccountRef, team: BoundTeam, seen: string | null, own: readonly BoundTeam[] | null): Promise<BoundTeam | null> {
        return this.#writingConfig(async () => {
            const pinned = (await this.#pinned(account))?.team ?? null;
            if (pinned !== seen) return pinned === team.id ? team : (own?.find((each) => each.id === pinned) ?? null);
            const result = await this.#run('reply', 'team use', [...accountArguments(account), 'team', 'use', team.id, '--json'], { timeoutMs: replyActLimitMs });
            this.#deps.log.info('reply_team_bound', { Exit: result.exit });
            return result.exit === 0 ? team : null;
        });
    }

    /** An account's sign-in as the Reply CLI says it now (`reply --profile <name> auth status --json`), with its pin. */
    async #pinned(account: AccountRef): Promise<ReplyAuth | null> {
        return readReplyAuth(await this.#run('reply', 'auth status', [...accountArguments(account), 'auth', 'status', '--json']));
    }

    /**
     * The profiles as the Reply CLI holds them, read again in a write's own turn (`#writingConfig`), and the profile made
     * there under `name` where it holds none of that name, since an add of a name it holds would replace that profile's
     * settings without a word. Whether it was made; null where the profiles could not be read.
     */
    async #addProfile(name: string): Promise<{ made: boolean; held: boolean; result?: RunResult } | null> {
        return this.#writingConfig(async () => {
            const profiles = readProfiles(await this.#run('reply', 'profile list', ['profile', 'list', '--json']));
            if (profiles === null) return null;
            if (profiles.available.includes(name)) return { made: false, held: true };
            const result = await this.#run('reply', 'profile add', ['profile', 'add', name, '--json']);
            return { made: result.exit === 0, held: false, result };
        });
    }

    /**
     * An account's mailboxes and LinkedIn accounts, each channel read on its own, and the links still waiting of each
     * channel that was listed; and whether Reply refused a channel's read because the sign-in cannot act in the team it
     * was asked in, its pinned one.
     */
    async #lists(account: AccountRef): Promise<{ lists: Lists; outOfTeam: boolean }> {
        const as = accountArguments(account);
        const lists: Lists = { senders: [], absent: [], unread: [], pending: [], pendingUnread: [] };
        let outOfTeam = false;
        // One read per channel: without --channel a channel the plan or role lacks fails the whole command.
        for (const channel of replyChannels) {
            const listed = readReplyChannel(await this.#run('reply', 'account list', [...as, 'account', 'list', '--channel', channel, '--json']));
            if (listed.kind === 'listed') lists.senders.push(...listed.senders);
            else (listed.kind === 'absent' ? lists.absent : lists.unread).push(channel);
            outOfTeam ||= listed.kind === 'outOfTeam';
        }
        // A read that fails lists none and names its channel, unless the CLI's refusal says the channel has none here.
        for (const channel of replyChannels) {
            if (lists.absent.includes(channel) || lists.unread.includes(channel)) continue;
            const ending = replyCliEnding(await this.#run('reply', 'account pending', [...as, ...pendingArguments(channel)], { timeoutMs: replyActLimitMs }));
            if (ending.kind === 'refused' && ending.code !== null && pendingNoneCodes.has(ending.code)) continue;
            const rows = ending.kind === 'done' ? readPending(ending.stdout) : null;
            if (rows === null) lists.pendingUnread.push(channel);
            else lists.pending.push(...rows.filter((row) => row.channel === channel));
        }
        return { lists, outOfTeam };
    }

    /**
     * One write of the Reply CLI's profiles at a time (`team use`, `profile add`, `profile delete`): reply-cli rewrites its
     * configuration whole, so two at once would lose one, and a look binds accounts side by side.
     */
    #writingConfig<T>(work: () => Promise<T>): Promise<T> {
        const next = this.#configWrites.then(work, work);
        this.#configWrites = next.catch(() => undefined);
        return next;
    }

    /**
     * The account an act names, among those the last read listed: the one named, or where none is named, as a page of an
     * earlier version asks, the default, else the profile `default`. Null for a name the last read did not list.
     */
    #accountOf(named: unknown): AccountState | null {
        if (named === undefined) return actingOf(this.#listed);
        return isAccountRef(named) ? (this.#listed.find((each) => sameAccount(each, named)) ?? null) : null;
    }

    /**
     * Keeps a read's accounts as the ones acts are checked against, unless a read that began after it gave them already: an
     * older read that ends last answers its own asker and changes nothing here.
     */
    #keepListing(begun: number, accounts: AccountState[], routed: Set<string> | null, signIns: Map<string, { pin: string | null; userId: string | null }> = new Map()): void {
        if (begun < this.#listedBy) return;
        this.#listedBy = begun;
        this.#listed = accounts;
        this.#routed = routed;
        this.#listedSignIns = signIns;
    }

    #startCheck(): 'pending' | 'ended' {
        return this.#startCheckEnded ? 'ended' : 'pending';
    }

    /**
     * The Reply CLI's version, read again while it is unknown or below the minimum, so an update made outside the
     * desktop is seen at the next read; one at or above it is kept, as every program's is.
     */
    async #replyVersion(): Promise<string | null> {
        const kept = this.#versions.get('reply');
        if (kept === null || (kept !== undefined && !versionAtLeast(kept, replyMinimumVersion))) this.#versions.delete('reply');
        return this.#version('reply');
    }

    /**
     * The account an act names and the sender of it the act is on, as the last read listed both: an act names only an
     * account and a sender that read gave, the sender by its channel and id among that account's own; and nothing of an
     * account whose change of sign-in is not answered yet.
     */
    #named(named: unknown, channel: ReplyCliChannel, id: unknown): { account: AccountState; sender: ReplySender } | ReplyRefusal {
        const account = this.#accountOf(named);
        // With no account named and none to act as, there is no sender: said as one the list did not give.
        if (account === null) return refusal(named === undefined ? senderSentences.badId : accountSentences.badAccount);
        if (account.change !== null) return refusal(accountSentences.unanswered);
        const sender = isReplyId(id) ? account.senders.find((each) => each.channel === channel && each.id === id) : undefined;
        return sender === undefined ? refusal(senderSentences.badId) : { account, sender };
    }

    /**
     * One act on a Reply account at a time, whatever it is: a sign-in, Make default, Choose, Add Reply account, Sign out,
     * Remove, Keep it or an act on a sender. One asked while another goes is `busy`'s answer, and runs nothing; every act
     * that ran ends a change.
     */
    async #oneAct<T>(busy: () => T, work: () => Promise<T>): Promise<T> {
        if (this.#acts) return busy();
        this.#acts = true;
        try {
            return await work();
        } finally {
            this.#acts = false;
            this.#ended += 1;
        }
    }

    /**
     * An act of the Reply CLI's on a Reply account, one at a time (`#oneAct`), at or above the Reply CLI the acts need. The
     * answer is the act's, or the sentence it ended with.
     */
    #acting<T>(work: () => Promise<T | ReplyRefusal>): Promise<T | ReplyRefusal> {
        return this.#oneAct<T | ReplyRefusal>(
            () => refusal(senderSentences.busy),
            async () => {
                const version = await this.#replyVersion();
                if (version === null || !versionAtLeast(version, replyMinimumVersion)) return refusal(belowMinimumSentence(replyMinimumVersion));
                return work();
            },
        );
    }

    /** A sign-in, one act at a time (`#oneAct`), in the setup's queue, since it ends in a write of the settings file. */
    #signingInAct(work: () => Promise<void>): Promise<void> {
        return this.#oneAct(
            () => {
                throw new Refused(senderSentences.busy);
            },
            () => this.#serial(work),
        );
    }

    /** The person's yes in the main process's own box; no box to ask in is a no. */
    async #confirmed(box: ReplyBox): Promise<boolean> {
        return (await this.#deps.confirm?.(box)) ?? false;
    }

    /**
     * Runs one verb as the account it is on. The log says the act, the channel, the exit and the CLI's code, never the
     * account, what the verb printed or what it wrote on stderr: a link's stdout is never logged, recorded or put into an
     * error.
     */
    async #verb(account: AccountRef, act: ReplyAct, channel: ReplyCliChannel, args: string[], options: { write: boolean; force?: boolean }): Promise<{ stdout: string } | ReplyRefusal> {
        const result = await this.#run('reply', `account ${act}`, [...accountArguments(account), ...args], { timeoutMs: replyActLimitMs });
        const ending = replyCliEnding(result);
        const code = ending.kind === 'refused' ? ending.code : ending.kind === 'unknown' ? 'outcome.unknown' : null;
        this.#deps.log.info('reply_account_act', { Act: act, Channel: channel, Exit: result.exit, Code: code ?? 'none', Usage: isUsageCode(code) });
        if (ending.kind === 'done') return { stdout: ending.stdout };
        return refusalOf(ending, { verb: act, channel, write: options.write, force: options.force });
    }

    /**
     * A link verb's ending: the link checked and handed to the system opener, or, where the person asked for a copy,
     * written to the clipboard; the page is told only which, when it expires and its pending entry. The link is kept
     * nowhere and logged nowhere.
     */
    async #open(answer: { stdout: string } | ReplyRefusal, copy: boolean): Promise<ReplyOpened | ReplyCopied | ReplyRefusal> {
        if ('refused' in answer) return answer;
        const printed = readLink(answer.stdout);
        if (printed === null) return refusal(senderSentences.outcomeUnknown, true);
        if (!isOpenableLink(printed.link)) return refusal(senderSentences.badLink, true);
        if (copy) {
            if (this.#deps.copyLink === undefined) return refusal(senderSentences.notCopied, true);
            try {
                this.#deps.copyLink(printed.link);
            } catch {
                return refusal(senderSentences.notCopied, true);
            }
            return { copied: true, channel: printed.channel, kind: printed.kind, linkKind: printed.linkKind, expiresAt: printed.expiresAt, pendingId: printed.pendingId };
        }
        if (this.#deps.openLink === undefined) return refusal(senderSentences.badLink, true);
        try {
            await this.#deps.openLink(printed.link);
        } catch {
            return refusal(senderSentences.notOpened, true);
        }
        return { opened: true, channel: printed.channel, kind: printed.kind, linkKind: printed.linkKind, expiresAt: printed.expiresAt, pendingId: printed.pendingId };
    }

    /** Keeps the door a refusal opened: a forced delete after `account_in_use`. */
    #keepDoor(door: string, answer: { stdout: string } | ReplyRefusal, offer: 'force'): void {
        if ('refused' in answer && answer.offer === offer) this.#doors.add(door);
    }

    /**
     * Connects a new sender by a link the CLI makes (`reply account connect`) in the Reply account named, the default where
     * none is: a LinkedIn account under the name typed for it, a mailbox for its provider. With `copy`, the link is copied instead of
     * opened, as `#open` says. `sharedPage`, the shared mailbox page a desktop of an earlier version offered, is refused, and
     * so is an account whose change of sign-in is not answered yet.
     */
    replyConnect(channel: unknown, options: unknown, named?: unknown): Promise<ReplyOpened | ReplyCopied | ReplyRefusal> {
        const asked = (typeof options === 'object' && options !== null ? options : {}) as Record<string, unknown>;
        const copy = asked['copy'] === true;
        if (asked['sharedPage'] !== undefined) return Promise.resolve(refusal(senderSentences.badRequest));
        const account = this.#accountOf(named);
        if (named !== undefined && account === null) return Promise.resolve(refusal(accountSentences.badAccount));
        if (account !== null && account.change !== null) return Promise.resolve(refusal(accountSentences.unanswered));
        // With no account named and none to act as, the CLI below the minimum is said first, as the card's every act does.
        const connect = (request: Parameters<typeof connectArguments>[0]) =>
            this.#acting(async () => (account === null ? refusal(accountSentences.badAccount) : this.#open(await this.#verb(account, 'connect', request.channel, connectArguments(request), { write: true }), copy)));
        if (channel === 'linkedin') {
            const problem = linkedInNameProblem(asked['name']);
            if (problem !== null) return Promise.resolve(refusal(problem));
            if (asked['provider'] !== undefined) return Promise.resolve(refusal(senderSentences.badRequest));
            return connect({ channel: 'linkedin', name: (asked['name'] as string).trim() });
        }
        const provider = asked['provider'];
        if (channel !== 'email' || (provider !== 'gmail' && provider !== 'office365')) return Promise.resolve(refusal(senderSentences.badRequest));
        return connect({ channel: 'email', provider });
    }

    /** Reconnects a sender the list gave (`reply account reconnect`) by a link the CLI makes; `sharedPage` as above. */
    replyReconnect(channel: unknown, senderId: unknown, options: unknown = {}, named?: unknown): Promise<ReplyOpened | ReplyCopied | ReplyRefusal> {
        const asked = (typeof options === 'object' && options !== null ? options : {}) as Record<string, unknown>;
        const copy = asked['copy'] === true;
        if (!isReplyChannel(channel) || asked['sharedPage'] !== undefined) return Promise.resolve(refusal(senderSentences.badRequest));
        const on = this.#named(named, channel, senderId);
        if ('refused' in on) return Promise.resolve(on);
        return this.#acting(async () => this.#open(await this.#verb(on.account, 'reconnect', channel, reconnectArguments(channel, on.sender.id), { write: true }), copy));
    }

    /**
     * A mailbox's daily limit (`reply account limits --channel email`): read with no limit given, and set to the one
     * given only on the person's yes to the box that names the mailbox.
     */
    replyLimits(senderId: unknown, daily?: unknown, named?: unknown): Promise<{ daily: number | null } | ReplyRefusal> {
        const on = this.#named(named, 'email', senderId);
        if ('refused' in on) return Promise.resolve(on);
        if (daily !== undefined && (typeof daily !== 'number' || !Number.isSafeInteger(daily) || daily < 0 || daily > dailyLimitMost)) {
            return Promise.resolve(refusal(senderSentences.badLimit));
        }
        return this.#acting(async () => {
            if (daily !== undefined && !(await this.#confirmed(actBox('limits', on.sender, daily)))) return refusal(senderSentences.notConfirmed);
            const answer = await this.#verb(on.account, 'limits', 'email', limitsArguments(on.sender.id, daily), { write: daily !== undefined });
            if ('refused' in answer) return answer;
            return readDailyLimit(answer.stdout) ?? refusal(senderSentences.outcomeUnknown, true);
        });
    }

    /** Pauses a LinkedIn account the list gave, on the person's yes (`reply account pause`). */
    replyPause(senderId: unknown, named?: unknown): Promise<ReplyDone | ReplyRefusal> {
        return this.#state('pause', senderId, named);
    }

    /** Resumes a LinkedIn account the list gave, on the person's yes (`reply account resume`). */
    replyResume(senderId: unknown, named?: unknown): Promise<ReplyDone | ReplyRefusal> {
        return this.#state('resume', senderId, named);
    }

    #state(verb: 'pause' | 'resume', senderId: unknown, named: unknown): Promise<ReplyDone | ReplyRefusal> {
        const on = this.#named(named, 'linkedin', senderId);
        if ('refused' in on) return Promise.resolve(on);
        return this.#acting(async () => {
            if (!(await this.#confirmed(actBox(verb, on.sender)))) return refusal(senderSentences.notConfirmed);
            const answer = await this.#verb(on.account, verb, 'linkedin', stateArguments(verb, on.sender.id), { write: true });
            if ('refused' in answer) return answer;
            return readState(answer.stdout) === null ? refusal(senderSentences.outcomeUnknown, true) : { done: true };
        });
    }

    /** Lets a mailbox the list gave send again after its provider stopped it, on the person's yes (`reply account unblock`). */
    replyUnblock(senderId: unknown, named?: unknown): Promise<ReplyDone | ReplyRefusal> {
        const on = this.#named(named, 'email', senderId);
        if ('refused' in on) return Promise.resolve(on);
        return this.#acting(async () => {
            if (!(await this.#confirmed(actBox('unblock', on.sender)))) return refusal(senderSentences.notConfirmed);
            const answer = await this.#verb(on.account, 'unblock', 'email', unblockArguments(on.sender.id), { write: true });
            if ('refused' in answer) return answer;
            return readDone(answer.stdout, 'unblocked') ? { done: true } : refusal(senderSentences.outcomeUnknown, true);
        });
    }

    /**
     * Deletes a sender the list gave (`reply account delete`), passing `--yes` only after the person's yes to the box
     * that names it. With `force`, only after the CLI answered `linkedin.account_in_use` to a delete of that LinkedIn
     * account without it, and only on the person's yes to a box of its own that says sequences use it.
     */
    replyDelete(channel: unknown, senderId: unknown, options: unknown = {}, named?: unknown): Promise<ReplyDone | ReplyRefusal> {
        const asked = (typeof options === 'object' && options !== null ? options : {}) as Record<string, unknown>;
        if (!isReplyChannel(channel)) return Promise.resolve(refusal(senderSentences.badRequest));
        const force = asked['force'] === true;
        if (force && channel !== 'linkedin') return Promise.resolve(refusal(senderSentences.badRequest));
        const on = this.#named(named, channel, senderId);
        if ('refused' in on) return Promise.resolve(on);
        // A door is the one sender's of the one Reply account: two profiles may list the same sender.
        const door = JSON.stringify(['delete', on.account.name, 'linkedin', on.sender.id]);
        return this.#acting(async () => {
            if (force && !this.#doors.has(door)) return refusal(senderSentences.notOffered);
            if (!(await this.#confirmed(actBox(force ? 'force' : 'delete', on.sender)))) return refusal(senderSentences.notConfirmed);
            if (force) this.#doors.delete(door);
            const answer = await this.#verb(on.account, 'delete', channel, deleteArguments(channel, on.sender.id, force), { write: true, force });
            if (channel === 'linkedin') this.#keepDoor(door, answer, 'force');
            if ('refused' in answer) return answer;
            return readDone(answer.stdout, 'deleted') ? { done: true } : refusal(senderSentences.outcomeUnknown, true);
        });
    }

    /**
     * Signs the Reply account named in through the browser, the default where none is named (`reply auth login`, as that
     * account), then places Jason's Reply plugin and, where no default is set, makes it the default (`#signedIn`).
     */
    connectReply(named?: unknown): Promise<void> {
        return this.#signingInAct(async () => {
            // Refused before the sign-in, since the sign-in ends in the plugin's wiring and the skills' deploy.
            this.#writableLayout();
            const account = await this.#signingIn(named);
            const result = await this.#run('reply', 'auth login', [...accountArguments(account), 'auth', 'login', '--json'], { timeoutMs: signInLimit });
            this.#deps.log.info('reply_connected', { Method: 'browser', Exit: result.exit });
            if (result.exit !== 0) throw new Refused(withCliReason(result, 'Reply did not sign in.'));
            await this.#signedIn(account);
        });
    }

    /** The same with a Reply API key the person pasted, on the CLI's standard input and never in an argument. */
    connectReplyWithKey(key: unknown, named?: unknown): Promise<void> {
        return this.#signingInAct(async () => {
            this.#writableLayout();
            if (typeof key !== 'string' || key.trim() === '') throw new Refused('Enter the key.');
            const account = await this.#signingIn(named);
            const result = await this.#run('reply', 'auth login --with-token', [...accountArguments(account), 'auth', 'login', '--with-token', '--json'], { stdin: key.trim(), timeoutMs: signInLimit });
            this.#deps.log.info('reply_connected', { Method: 'key', Exit: result.exit });
            if (result.exit !== 0) throw new Refused(withCliReason(result, 'Reply did not accept that key.'));
            await this.#signedIn(account);
        });
    }

    /**
     * The account a sign-in is for, as the last read listed it, with its profile in the Reply CLI: a profile only a route
     * names is made there first (`reply profile add`), and only where the CLI, asked again now, does not hold it, since an
     * add of a name it holds would replace that profile's settings without a word. An account the Reply CLI, asked now,
     * says is signed in is refused: another login is another Reply account, or a sign-out first; and so is one whose sign-in
     * it could not say then, since a sign-in would replace whatever it holds. With no account named and none listed, as a
     * page of an earlier version asks, the refusal says why there is none.
     */
    async #signingIn(named: unknown): Promise<AccountRef> {
        const account = this.#accountOf(named);
        if (account === null && named !== undefined) throw new Refused(accountSentences.badAccount);
        if (account === null) {
            if (this.#deps.programs.find('reply') === null) throw new Refused(accountSentences.cliMissing);
            const version = await this.#replyVersion();
            if (version === null || !versionAtLeast(version, replyMinimumVersion)) throw new Refused(belowMinimumSentence(replyMinimumVersion));
            throw new Refused(accountSentences.profilesUnread);
        }
        const profile = await this.#addProfile(account.name);
        if (profile === null) throw new Refused(accountSentences.profilesUnread);
        if (!profile.held && !profile.made) throw new Refused(accountSentences.profileNotMade);
        if (profile.held) {
            const auth = await this.#pinned(account);
            if (auth === null) throw new Refused(accountSentences.signInUnread);
            if (auth.signedIn) throw new Refused(accountSentences.signedInAlready);
        }
        return { type: account.type, name: account.name };
    }

    /** After a sign-in: Jason's Reply plugin placed, the first default made, and the Reply skills deployed either way. */
    async #signedIn(account: AccountRef): Promise<void> {
        this.#markToCheck(account);
        try {
            await this.#placeAndMakeFirstDefault(account);
        } finally {
            await this.#deployReplySkills();
            this.#changed();
        }
    }

    /**
     * After a sign-in: places what is missing of Jason's Reply plugin (`#placeMissing`); then, where the routes name no
     * global default at all, has the runtime name the account signed in that default, as Make default does. A default
     * already set, whatever its plugin, is left as it is. Routes that could not be read are said, and so is a placing that
     * failed. The log says what was placed and what came of the default.
     */
    async #placeAndMakeFirstDefault(account: AccountRef): Promise<void> {
        const placed = await this.#placeMissing(accountSentences.notPlacedAtSignIn);
        const routes = readRoutes(await this.#deps.cli.run(['route', 'list']));
        const outcome = routes === null ? 'unread' : routes.globalDefault !== null ? 'kept' : (await this.#setDefault(account, signInReason)) === null ? 'made' : 'refused';
        this.#deps.log.info('reply_placed_after_sign_in', { Placed: placed, Default: outcome });
        if (outcome === 'unread') throw new Refused(accountSentences.routesUnreadAtSignIn);
        if (outcome === 'refused') throw new Refused(accountSentences.notDefaultAtSignIn);
    }

    /**
     * Jason's Reply plugin placed and granted the Reply CLI where either is missing, as `jason plugin list` answers it now:
     * the package fetched with `jason plugin install` only where the runtime lists none, the grant written only where it
     * does not hold `reply`, and the plugins loaded again where either was, or where the plugin is listed and not valid, as
     * after a Reply CLI installed or updated outside the desktop. So an installation where the plugin is listed valid and
     * granted asks no release feed, loads nothing again, and its settings file is neither read nor written by the desktop,
     * one with comments included; where the runtime gives no list, both are placed. A refusal is `sentence`, then the
     * reason of what refused: the fetch, the load, or the desktop's reading of a settings file it cannot parse.
     */
    async #placeMissing(sentence: string): Promise<Placing> {
        const refused = (reason: string) => new Refused(withReason(sentence, reason));
        const readForEdit = () => {
            try {
                return this.#readForEdit();
            } catch (error) {
                throw error instanceof Refused ? refused(error.message) : error;
            }
        };
        const placement = replyPlacement(await this.#deps.cli.run(['plugin', 'list']));
        const placed = placement?.placed === true;
        const granted = placement?.granted === true;
        if (placed && placement?.valid === true && granted) return 'in_place';
        // A file the edit cannot keep is refused before anything is fetched.
        if (!granted) readForEdit();
        if (!placed) {
            const install = await this.#deps.cli.run(['plugin', 'install', 'reply', ...feedArguments(this.#deps.settings())], { timeoutMs: installLimit });
            if (install.exit !== 0) throw refused(words(install, ''));
        }
        if (!granted) {
            const { file, settings } = readForEdit();
            writeAtomically(file, `${JSON.stringify(withReplyGranted(settings), null, 2)}\n`);
        }
        const reload = await this.#reloadPlugins();
        if (reload.exit !== 0) throw refused(this.#reloadRefusal ?? words(reload, ''));
        return placed ? (granted ? 'loaded' : 'granted') : granted ? 'placed' : 'placed_granted';
    }

    /**
     * Has the runtime name the account the global default (`jason route set --global`), which checks the route before it
     * writes it, writes it through its one writer of the settings file, puts the file back on a refusal and journals the
     * change under the person who made it, by this account's name, with the reason of the act that made it. Null where it
     * did, else the desktop's sentence why not.
     */
    async #setDefault(account: AccountRef, reason: string): Promise<string | null> {
        const person = this.#deps.person;
        if (!usableAccount(person)) return accountSentences.noActor;
        const binding = JSON.stringify(defaultBinding(account));
        const set = await this.#deps.cli.run(['route', 'set', '--global', '--plugin', 'reply', '--binding', binding, '--actor', `human:${person}`, '--reason', reason]);
        this.#deps.log.info('reply_default_set', { Exit: set.exit });
        return set.exit === 0 ? null : defaultRefusal(set);
    }

    /**
     * Choose: pins a team on an account's profile in the Reply CLI (`reply --profile <name> team use <id>`, which checks the
     * id against the sign-in's own teams), only a team the last read offered that account, bound to none of them, its
     * change of sign-in, if any, answered. The pin that read saw is written over; one a terminal set since to a team
     * offered is that account's team now, and is refused as bound. The pick is the person's, and is noted at once with the
     * login, the last read's or else Reply's, and the team's name and organization as Reply's whoami names them, asked once
     * the team is pinned: it is never said as a change made outside, and it checks a sign-in made on the card before it,
     * so the next look does not ask Reply again. Nothing is asked of the person first, since nothing is lost by it. The
     * answer is done, or the desktop's sentence and the Reply CLI's reason, but where the CLI refuses the team, whose words
     * number it: the desktop's sentence alone. The page reads the accounts again either way.
     */
    chooseTeam(named: unknown, teamId: unknown): Promise<ReplyDone | ReplyRefusal> {
        const account = named === undefined ? null : this.#accountOf(named);
        if (account === null) return Promise.resolve(refusal(accountSentences.badAccount));
        if (account.change !== null) return Promise.resolve(refusal(accountSentences.unanswered));
        if ((account.team ?? null) !== null) return Promise.resolve(refusal(accountSentences.teamPinned));
        const offered = account.teams ?? [];
        const team = typeof teamId === 'string' ? offered.find((each) => each.id === teamId) : undefined;
        if (team === undefined) return Promise.resolve(refusal(accountSentences.teamNotOffered));
        const seen = this.#listedSignIns.get(account.name) ?? { pin: null, userId: null };
        return this.#acting(async () => {
            const chosen = await this.#writingConfig(async (): Promise<ReplyRefusal | null> => {
                const pinned = (await this.#pinned(account))?.team ?? null;
                if (pinned !== seen.pin && offered.some((each) => each.id === pinned)) return refusal(accountSentences.teamPinned, true);
                const result = await this.#run('reply', 'team use', [...accountArguments(account), 'team', 'use', team.id, '--json'], { timeoutMs: replyActLimitMs });
                this.#deps.log.info('reply_team_chosen', { Exit: result.exit });
                if (result.exit === 0) return null;
                // Where the Reply CLI refuses the team, its words name it by its number: the desktop's sentence is said alone.
                const ending = replyCliEnding(result);
                if (ending.kind === 'refused' && ending.code === 'usage.team') return { refused: accountSentences.teamGone, code: ending.code, offer: null, reread: true };
                return cliRefusal(result, accountSentences.teamNotChosen);
            });
            if (chosen !== null) return chosen;
            const said = await this.#whoami(account, team.id);
            // Reply's answer for the user the card showed, by whatever address, or for a sign-in it showed none for.
            const answer = said !== null && (account.user === null || sameUser(userOf(said), { user: account.user, id: seen.userId })) ? said : null;
            const [user, id] = answer !== null ? [answer.login, answer.userId] : [account.user, seen.userId];
            const noted = this.#note(account, user === null ? null : { user, ...(id === null ? {} : { id }), team: namedBy(team, answer) }, 'choose');
            // Named and noted, the pick is the check of the sign-in made on the card before it: the look after asks Reply
            // nothing of it again.
            if (user !== null && noted !== undefined) this.#checked(account, this.#marks);
            return { done: true };
        });
    }

    /**
     * Add Reply account: the profile made in the Reply CLI under exactly the name given (`reply profile add <name>`), with
     * nothing of an account's own, which the CLI would pin on it; its card is the next read's, not signed in. A name
     * `accountNameProblem` refuses is refused before anything runs, and so is one the Reply CLI, asked now, holds, since an
     * add of it would replace that profile's settings without a word.
     */
    addAccount(name: unknown): Promise<ReplyDone | ReplyRefusal> {
        const problem = accountNameProblem(name);
        if (problem !== null) return Promise.resolve(refusal(problem));
        const asked = name as string;
        return this.#acting(async () => {
            const profile = await this.#addProfile(asked);
            if (profile === null) return refusal(accountSentences.profilesUnreadAtAdd, true);
            if (profile.held) return refusal(accountSentences.nameTaken, true);
            this.#deps.log.info('reply_account_added', { Exit: profile.result?.exit ?? -1 });
            return profile.made ? this.#announced({ done: true }) : cliRefusal(profile.result!, accountSentences.notAdded);
        });
    }

    /**
     * Sign out, on the person's yes to the box that names the Reply account (`accountBox`): its sign-in removed in the Reply
     * CLI itself (`reply --profile <name> auth logout`). No route changes.
     */
    signOut(named: unknown): Promise<ReplyDone | ReplyRefusal> {
        const account = named === undefined ? null : this.#accountOf(named);
        if (account === null) return Promise.resolve(refusal(accountSentences.badAccount));
        return this.#acting(async () => {
            if (!(await this.#confirmed(accountBox('signOut', account.name, this.#routed?.has(account.name) ?? true)))) return refusal(senderSentences.notConfirmed);
            const result = await this.#run('reply', 'auth logout', [...accountArguments(account), 'auth', 'logout', '--json']);
            this.#deps.log.info('reply_signed_out', { Exit: result.exit });
            return result.exit === 0 ? this.#announced({ done: true }) : cliRefusal(result, accountSentences.notSignedOut);
        });
    }

    /**
     * Remove, never for the Reply CLI's own profile `default` nor for an account whose profile it does not hold, on the
     * person's yes to the box that names the Reply account (`accountBox`): its profile and its sign-in deleted in the
     * Reply CLI itself (`reply profile delete <name> --yes`). No route changes, so an account a route names keeps its
     * card, not signed in. Never either for the account a route that names no profile follows, as the last read showed it
     * or the Reply CLI and the routes, asked again before the box, say now: the delete would reset the CLI's current
     * profile, and that route would send as another login. Where the routes could not be read, the current profile is
     * taken as followed.
     */
    removeAccount(named: unknown): Promise<ReplyDone | ReplyRefusal> {
        const account = named === undefined ? null : this.#accountOf(named);
        if (account === null) return Promise.resolve(refusal(accountSentences.badAccount));
        if (account.name === 'default') return Promise.resolve(refusal(accountSentences.defaultKept));
        if (!account.held) return Promise.resolve(refusal(accountSentences.notHeld));
        if (account.followed) return Promise.resolve(refusal(this.#routed === null ? accountSentences.followedUnread : accountSentences.followed));
        return this.#acting(async () => {
            const routes = readRoutes(await this.#deps.cli.run(['route', 'list']));
            const profiles = readProfiles(await this.#run('reply', 'profile list', ['profile', 'list', '--json']));
            if (profiles !== null && followedProfile(profiles, routes) === account.name) return refusal(routes === null ? accountSentences.followedUnread : accountSentences.followed, true);
            if (!(await this.#confirmed(accountBox('remove', account.name, this.#routed?.has(account.name) ?? true)))) return refusal(senderSentences.notConfirmed);
            const result = await this.#writingConfig(() => this.#run('reply', 'profile delete', ['profile', 'delete', account.name, '--yes', '--json']));
            this.#deps.log.info('reply_account_removed', { Exit: result.exit });
            if (result.exit !== 0) return cliRefusal(result, accountSentences.notRemoved);
            this.#removed.set(account.name, this.#readsBegun);
            this.#note(account, null, 'remove');
            return this.#announced({ done: true });
        });
    }

    /**
     * Keep it, where the last read found the account's sign-in changed from what the desktop's note says (`bindingOf`).
     * Bound to another of its sign-in's teams, the account as it now is becomes the noted one. Signed in as another login,
     * no longer in the noted team, or as a login not known, it is bound to no team, and its team is picked afresh: the note
     * says so in place of the sign-in it held, so the pin the profile holds no longer stands, and the next look binds it as
     * a first sign-in is bound, one team by itself and several by Choose, which writes over that pin, and notes it. Nothing is
     * run, and nothing of the Reply CLI's changes.
     */
    keepChange(named: unknown): Promise<ReplyDone | ReplyRefusal> {
        const account = named === undefined ? null : this.#accountOf(named);
        if (account === null) return Promise.resolve(refusal(accountSentences.badAccount));
        const { change, user, team } = account;
        if (change === null || (change === 'team' && (user === null || team === null))) return Promise.resolve(refusal(accountSentences.nothingToKeep));
        const id = this.#listedSignIns.get(account.name)?.userId ?? null;
        const kept: NotedSignIn | 'fresh' = change === 'team' ? { user: user!, ...(id === null ? {} : { id }), team: team! } : 'fresh';
        return this.#oneAct<ReplyDone | ReplyRefusal>(
            () => refusal(senderSentences.busy),
            async () => (this.#note(account, kept, 'keep') === undefined ? refusal(accountSentences.notKept) : this.#announced({ done: true })),
        );
    }

    /** An act's answer, once every page has been told the accounts changed: the acts on a Reply account as a whole. */
    #announced<T>(answer: T): T {
        this.#changed();
        return answer;
    }

    /**
     * Make default, only while Jason has no global default: in the setup's queue the routes are read, and a default set by
     * then, whatever its plugin or account, is left as it is, since a campaign with no route of its own follows the default
     * at every step and moving it would move running campaigns. Otherwise what is missing of the plugin and its grant is
     * placed (`#placeMissing`), the routes are read again, and a default set while it placed is left as it is too; then the
     * runtime names the account (`#setDefault`). Nothing is asked of the person. A refusal is the desktop's sentence, and
     * the runtime writes nothing it refuses.
     */
    async makeDefault(named: unknown): Promise<ReplyDone | ReplyRefusal> {
        const account = named === undefined ? null : this.#accountOf(named);
        if (account === null) return refusal(accountSentences.badAccount);
        const answer = await this.#oneAct(() => refusal(senderSentences.busy), () => this.#makeDefault(account));
        this.#deps.log.info('reply_default_made', { Done: 'done' in answer });
        this.#changed();
        return answer;
    }

    #makeDefault(account: AccountState): Promise<ReplyDone | ReplyRefusal> {
        if (!usableAccount(this.#deps.person)) return Promise.resolve(refusal(accountSentences.noActor));
        return this.#serial(async () => {
            const unset = async () => {
                const routes = readRoutes(await this.#deps.cli.run(['route', 'list']));
                return routes === null ? refusal(accountSentences.routesUnread) : routes.globalDefault !== null ? refusal(accountSentences.defaultExists) : null;
            };
            const before = await unset();
            if (before !== null) return before;
            try {
                await this.#placeMissing(accountSentences.notPlaced);
            } catch (error) {
                if (error instanceof Refused) return refusal(error.message);
                throw error;
            }
            // A placing may fetch the package for minutes: a default set meanwhile is left as it is too.
            const placed = await unset();
            if (placed !== null) return placed;
            const refused = await this.#setDefault(account, defaultReason);
            return refused === null ? { done: true } : refusal(refused);
        });
    }

    /**
     * The data directory's layout as its stamp says it at this moment, for a card that writes. A stamp this build refuses,
     * a layout newer than it runs or a stamp it cannot read, refuses the card before it runs a program, places a plugin
     * or writes a file, in the words `jason runtime start` refuses it in, since the parts may already be where a later
     * layout keeps them.
     */
    #writableLayout(): DataLayout {
        const layout = dataLayout(this.#deps.paths.data);
        const refusal = layoutRefusal(layout, this.#deps.bundleVersion);
        if (refusal !== null) {
            this.#deps.log.warn('settings_layout_refused', { Code: refusal.code });
            throw new Refused(refusal.message);
        }
        return layout;
    }

    /** The settings file a card edits, placed by the layout `#writableLayout` answers. */
    #settingsFile(): string {
        const layout = this.#writableLayout();
        // Every layout this build runs keeps plugins' configuration in this one file; a layout that keeps it apart is
        // newer than this build and refused above, so the cards never write a secret where a later layout keeps none.
        if (!layout.userSettingsFileHoldsSecrets) throw new Error('The Connect cards write one settings file, and this layout keeps two.');
        return layout.userSettingsFile;
    }

    /** The settings file and what it holds, for an edit; one the desktop cannot parse is logged by its path and refused. */
    #readForEdit(): { file: string; settings: Record<string, unknown> } {
        const file = this.#settingsFile();
        try {
            return { file, settings: parseInstallationSettings(readText(file)) };
        } catch (error) {
            this.#deps.log.warn('settings_refused', { Path: file });
            throw new Refused(`${(error as Error).message} ${editByHand}`);
        }
    }

    /**
     * One plugin placed with `jason plugin install` and one edit of the settings file: the edit is tried before the
     * install, so a file or a value it refuses is refused before anything is fetched, and made again on the file as it
     * is just before the write, so an edit made while the package was fetched is not lost.
     */
    async #placeAndEdit(pluginId: string, edit: (settings: Record<string, unknown>) => Record<string, unknown>): Promise<void> {
        edit(this.#readForEdit().settings);
        const placed = await this.#deps.cli.run(['plugin', 'install', pluginId, ...feedArguments(this.#deps.settings())], { timeoutMs: installLimit });
        if (placed.exit !== 0) throw new Refused(words(placed, `jason plugin install ${pluginId} did not place the plugin.`));
        const { file, settings } = this.#readForEdit();
        writeAtomically(file, `${JSON.stringify(edit(settings), null, 2)}\n`);
    }

    /**
     * The Reply skills beside Jason's, in the chat folder's two skills folders, Claude Code's and Codex's. The Reply CLI
     * writes them as plain folders only for hosts other than Claude Code, so they are written to a folder of ours for one
     * of those and the Reply ones copied across; the sales skills it adds with them are an older copy of Jason's own and
     * are left out.
     */
    async #deployReplySkills(): Promise<void> {
        // The routine the start uses too (`reply-skills.ts`); what it copies is recorded outside the chat folder. A chat
        // folder that cannot be put right is said by its code, and the sign-in's own result stands.
        try {
            await deployReplySkills((verb, args, options) => this.#run('reply', verb, args, options), this.#deps.paths, this.#deps.ownChatFolder);
        } catch (error) {
            if (!(error instanceof AgentFolderUnowned)) throw error;
            this.#deps.log.warn('agent_folder_unowned', { Code: error.code });
        }
    }

    /**
     * The agents' roles put on the AI the chat runs on, called at a Live start once the runtime is up, with the host the
     * chat is set to run on, and at each chat turn's start, with the turn's host. Only where the runtime answers no default
     * profile in force and holds no execution profile at all, a disabled one included: the desktop's mark is written, one
     * profile is made with `jason profile create` for the host `roleHostOf` picks (its name, its host word and its
     * program, the verb's own defaults for the rest), and the runtime is asked to name it the default with
     * `jason profile set-default`, which it writes into its settings file and puts in force without a restart; then the
     * mark is taken away. The desktop writes no part of the default itself. The mark holds the profile's name, and once
     * the create succeeded the id it answered: a start that finds the one profile there with that id and no default names
     * the default only. A mark that cannot show the profile there is the desktop's — no id, or another — is let go and
     * nothing is named. A profile or a default a person made is never touched, now or at any later start, a default in
     * force by the look just before the naming included. Each end but a runtime that did not answer and no connected host
     * settles it for this run.
     */
    connectRoles(chosen: string | null): Promise<RolesOutcome> {
        return this.#serial(
            async () => {
                const outcome = await this.#connectRoles(chosen);
                if (!unsettling.has(outcome)) this.#rolesSettled = true;
                this.#deps.log.info('roles_profile', { Outcome: outcome });
                return outcome;
            },
            (outcome) => rolesDefaultMoved.has(outcome),
        );
    }

    async #connectRoles(chosen: string | null): Promise<RolesOutcome> {
        if (this.#rolesSettled) return 'settled';
        const pending = this.#deps.settings().rolesProfilePending ?? null;
        const listed = await this.#deps.cli.run(['profile', 'list', '--include-disabled', '--limit', '2']);
        const page = listed.exit === 0 ? readProfilePage(listed.stdout) : null;
        if (page === null) return 'profiles_unread';
        if (page.defaultInForce !== null) {
            this.#mark(null);
            return 'default_named';
        }
        const next = rolesNext(page.profiles, page.more, pending, page.profiles.length === 0 ? roleHostOf(chosen, await this.#rolesConnected()) : null);
        if (next.kind === 'leave') {
            if (next.why === 'profiles_exist') this.#mark(null);
            return next.why;
        }
        if (next.kind === 'name') {
            const named = await this.#nameDefault(next.name);
            return named === 'written' ? 'named' : named === 'theirs' ? 'default_named' : named === 'unanswered' ? 'default_unanswered' : 'default_unwritten';
        }

        const found = this.#deps.programs.find(next.host);
        if (found === null) return 'no_host';
        const made = await this.#makeRolesProfile(next.host, found);
        if (made.kind !== 'made') return made.kind === 'unmarked' ? 'unmarked' : 'create_refused';
        const { name } = made;
        const named = await this.#nameDefault(name);
        return named === 'written' ? 'made' : named === 'theirs' ? 'made_unnamed' : named === 'unanswered' ? 'default_unanswered' : 'default_unwritten';
    }

    /** Whether Claude Code and Codex are connected as the cards say it: found, and Claude Code at its floor. */
    async #rolesConnected(): Promise<Record<RoleHost, boolean>> {
        const claude = this.#deps.programs.find('claude') !== null && (await this.#claude()).belowFloor === null;
        return { claude, codex: this.#deps.programs.find('codex') !== null };
    }

    /**
     * The desktop's own profile for a host, made with `jason profile create` under its name (`roleProfiles`), its host
     * word and the program found here: the one routine both the fresh installation's link (`connectRoles`) and a person's
     * Use (`useProgram`) make it by. The mark first, so a profile made and never named is finished by the next start, and
     * only then; then the id the create answered, the one proof a later start has that the profile there is the desktop's.
     */
    async #makeRolesProfile(roleHost: RoleHost, found: string): Promise<{ kind: 'made'; name: string } | { kind: 'unmarked' } | { kind: 'refused'; result: CliResult }> {
        const { name, host } = roleProfiles[roleHost];
        if (!this.#mark({ name })) return { kind: 'unmarked' };
        const made = await this.#deps.cli.run(['profile', 'create', name, '--host', host, '--program', profileProgram(roleHost, found)], { timeoutMs: profileLimit });
        if (made.exit !== 0) return { kind: 'refused', result: made };
        const id = readProfile(made.stdout)?.id ?? null;
        if (id !== null) this.#mark({ name, id });
        return { kind: 'made', name };
    }

    /**
     * Every profile the runtime holds, a disabled one included, page after page; the answer of a page not read. A cursor
     * the runtime already gave is not followed again: the pages stop there, so a runtime that answers the same cursor
     * twice cannot keep the act asking.
     */
    async #everyProfile(): Promise<{ profiles: ListedProfile[] } | { unread: CliResult }> {
        const profiles: ListedProfile[] = [];
        const followed = new Set<string>();
        let cursor: string | null = null;
        do {
            const listed = await this.#deps.cli.run(['profile', 'list', '--include-disabled', '--limit', '1000', ...(cursor === null ? [] : ['--cursor', cursor])]);
            const page = listed.exit === 0 ? readProfilePage(listed.stdout) : null;
            if (page === null) return { unread: listed };
            profiles.push(...page.profiles);
            if (cursor !== null) followed.add(cursor);
            cursor = page.cursor !== null && !followed.has(page.cursor) ? page.cursor : null;
        } while (cursor !== null);
        return { profiles };
    }

    /**
     * A person's Use on a program's card: the agents' roles put on Claude Code or Codex by default. The program must be
     * found here, and Claude Code at its floor, as the card's In use needs. The profile is an enabled one on that host —
     * the desktop's own name first, else the one there is; several, none of them the desktop's own, are refused naming
     * them, never guessed between — or, where there is none, one made by the routine the fresh installation's link makes
     * it by. Then the runtime is asked to name it the default, `jason profile set-default`, with the desktop's fixed
     * reason, and the cards are read again: what they show is the default the runtime answers in force, never this act's
     * wish. A refusal of the runtime's, a default a JASON_ variable sets or a Roles section it refuses included, is said in
     * its own words, and nothing is asked again.
     */
    useProgram(programId: unknown): Promise<void> {
        return this.#serial(async () => {
            if (programId !== 'claude' && programId !== 'codex') throw new Refused('The agents’ roles are put on Claude Code or Codex from here, and on no other program.');
            const roleHost: RoleHost = programId;
            const label = roleHost === 'claude' ? 'Claude Code' : 'Codex';
            const outcome = await this.#useProgram(roleHost, label).catch((error: unknown) => {
                this.#deps.log.info('program_used', { Program: roleHost, Outcome: 'refused' });
                this.#changed();
                throw error;
            });
            this.#deps.log.info('program_used', { Program: roleHost, Outcome: outcome });
            this.#changed();
        });
    }

    async #useProgram(roleHost: RoleHost, label: string): Promise<'named' | 'made'> {
        const found = this.#deps.programs.find(roleHost);
        if (found === null) throw new Refused(`${label} is not found on this computer, so the agents’ roles were left where they are.`);
        if (roleHost === 'claude') {
            const below = (await this.#claude()).belowFloor;
            if (below !== null) throw new Refused(below);
        }
        const every = await this.#everyProfile();
        if ('unread' in every) throw new Refused(runtimeWords(every.unread, 'The runtime did not list its execution profiles, so nothing was changed.'));
        const pick = profileToUse(every.profiles, roleHost);
        const nameOne = 'Name the one to use with `jason profile set-default <name>`.';
        if (pick.kind === 'several') throw new Refused(`Several execution profiles run on ${label}: ${pick.names.join(', ')}. ${nameOne}`);
        if (pick.kind === 'disabled') throw new Refused(`The execution profile ${pick.name} runs on ${label} and is disabled; \`jason profile enable ${pick.name}\` puts it back in service.`);
        if (pick.kind === 'taken') {
            const { host: word } = roleProfiles[roleHost];
            throw new Refused(
                `The execution profile ${pick.name} is there and runs another program, so none was made for ${label}. Make one under another name with \`jason profile create <name> --host ${word} --program ${roleHost}\`, then name it with \`jason profile set-default <name>\`.`,
            );
        }
        let name: string;
        let made = false;
        if (pick.kind === 'create') {
            const result = await this.#makeRolesProfile(roleHost, found);
            if (result.kind === 'unmarked') throw new Refused('The desktop could not write its own settings, so no profile was made.');
            if (result.kind === 'refused') throw new Refused(runtimeWords(result.result, `jason profile create ended with code ${result.result.exit}.`));
            name = result.name;
            made = true;
        } else {
            name = pick.name;
        }
        const named = await this.#deps.cli.run(['profile', 'set-default', name, '--reason', usingReason]);
        if (named.exit !== 0) throw new Refused(runtimeWords(named, `jason profile set-default ended with code ${named.exit}.`));
        const answered = readDefaultAnswer(named.stdout);
        if (answered !== name) throw new Refused(`The runtime answered ${answered ?? 'no default'} in force, not ${name}.`);
        if (made) this.#mark(null);
        return made ? 'made' : 'named';
    }

    /**
     * Asks the runtime to name the profile the default, `jason profile set-default`, where the default in force just
     * before names none, then takes the mark away and has the cards read again: `written` where the runtime answered that
     * name in force, or `theirs` where a default was in force by then, which is kept and never named over. The mark is
     * kept otherwise: `unanswered` where the runtime did not answer the look, or the naming exited as a runtime that did
     * not answer (3) or was not finished (-1), which the next turn looks at again; `unwritten` where the runtime refused
     * the naming, a request it took and did not answer in time included, which is never asked again blind: every later
     * look starts with the list, so a naming the runtime did make is read back there. The runtime's naming has no
     * precondition, so a default a person names between the look and the naming is the one it replaces, and journals as
     * it does every change of the default, with the desktop's fixed reason; no second request undoes it.
     */
    async #nameDefault(name: string): Promise<'written' | 'theirs' | 'unwritten' | 'unanswered'> {
        const inForce = await this.#defaultInForce();
        if (inForce === undefined) return 'unanswered';
        if (inForce === null) {
            const named = await this.#deps.cli.run(['profile', 'set-default', name, '--reason', namingReason]);
            if (named.exit === 3 || named.exit === -1) return 'unanswered';
            if (named.exit !== 0 || readDefaultAnswer(named.stdout) !== name) return 'unwritten';
        }
        this.#mark(null);
        this.#changed();
        return inForce === null ? 'written' : 'theirs';
    }

    /** The default profile in force as the runtime answers it: its name, null where none is named, undefined unread. */
    async #defaultInForce(): Promise<string | null | undefined> {
        const listed = await this.#deps.cli.run(['profile', 'list', '--include-disabled', '--limit', '1']);
        const page = listed.exit === 0 ? readProfilePage(listed.stdout) : null;
        return page === null ? undefined : page.defaultInForce;
    }

    /** Writes the mark, or takes it away; false where it could not be written. */
    #mark(mark: RolesMark | null): boolean {
        const save = this.#deps.saveSettings;
        if (save === undefined) return mark === null;
        const settings = this.#deps.settings();
        const held = settings.rolesProfilePending ?? null;
        if (held?.name === mark?.name && held?.id === mark?.id) return true;
        const { rolesProfilePending: _, ...rest } = settings;
        try {
            save(mark === null ? rest : { ...rest, rolesProfilePending: mark });
        } catch {
            this.#deps.log.warn('roles_profile_unmarked', {});
            return false;
        }
        return true;
    }

    setProviderKey(providerId: unknown, key: unknown): Promise<void> {
        return this.#serial(async () => {
            if (typeof providerId !== 'string' || keyProvider(providerId) === undefined) throw new Refused('That is not a provider the desktop keeps a key for.');
            if (typeof key !== 'string') throw new Refused('Enter the key.');
            const done = this.#deps.keys.set(providerId, key);
            this.#deps.log.info(done === 'stored' ? 'provider_key_stored' : 'provider_key_removed', { ProviderId: providerId });
            this.#changed();
        });
    }

    /**
     * Runs the npm install of a program, on a person's click, and only where npm is there. The one program the desktop also
     * updates with no click is an installed Reply CLI below the minimum, at its start (`updateReplyAtStart`). Once the Reply
     * CLI was installed or updated, the runtime is asked to read its plugins again: its Reply plugin holds the CLI it last
     * found to the version its manifest asks for, and says the old one until then.
     */
    installProgram(programId: unknown): Promise<void> {
        return this.#serial(async () => {
            const install = isProgramName(programId) ? programInstalls[programId] : undefined;
            if (!isProgramName(programId) || install === undefined || install.npmPackage === null) throw new Refused('The desktop does not install that.');
            if (this.#deps.programs.find('npm') === null) {
                throw new Refused(`Installing this needs Node.js and npm, which are not on this computer. Install Node.js from ${programInstalls.node?.link}, then try again.`);
            }
            // A click waits its turn behind the start's update, which may have done what it asks for.
            if (programId !== 'reply' || !(await this.#replyCurrent())) {
                const result = await this.#npmInstall(programId, install.npmPackage);
                if (result.exit !== 0) throw new Refused(words(result, `npm ended with code ${result.exit}.`));
            }
            const reload = programId === 'reply' ? await this.#reloadPlugins() : null;
            this.#changed();
            if (reload !== null && reload.exit !== 0) throw this.#reloadRefused(reload);
        });
    }

    /** Whether a Reply CLI is here whose version, read again now, is at the minimum. */
    async #replyCurrent(): Promise<boolean> {
        if (this.#deps.programs.find('reply') === null) return false;
        this.#versions.delete('reply');
        const version = await this.#replyVersion();
        return version !== null && versionAtLeast(version, replyMinimumVersion);
    }

    /** The one npm line of the desktop: the latest version of one of its own packages, and the version kept of it let go. */
    async #npmInstall(program: ProgramName, npmPackage: string): Promise<RunResult> {
        const result = await this.#run('npm', 'install', ['install', '-g', npmPackage], { timeoutMs: installLimit });
        this.#deps.log.info('program_installed', { Program: program, Exit: result.exit });
        this.#versions.delete(program);
        return result;
    }

    /**
     * An installed Reply CLI below the minimum updated with no click, asked once a Live start (`replyUpdateAtStart`),
     * in the queue the clicks wait in: only where `Update:CheckEnabled` is not turned off as the desktop reads it
     * (`updateSwitch`), `reply` is found, its version was read and is below the minimum, npm is found, no try of this
     * minimum stands in the desktop's settings, and no plugin that runs it can be at work (`replyAtWork`). The mark is
     * written before npm starts, and where it cannot be written npm is not run; it is taken away once the CLI in use is
     * at the minimum, so a minimum is tried once and a click is never held back. A missing CLI is installed on a click
     * alone. After npm, however it ended, the version is read again, and where npm ended well or that version is at the
     * minimum the runtime is asked to read its plugins again; what the runtime then says it found is read back, so a
     * CLI updated here and not where the runtime looks is not called updated. The outcome is one line of the card, in
     * memory for this run; npm's words reach that line alone.
     */
    updateReplyAtStart(): Promise<StartUpdateOutcome> {
        return this.#serial(async () => {
            const said = this.#startUpdate;
            try {
                const outcome = await this.#updateReplyAtStart();
                this.#deps.log.info('reply_start_update', { Outcome: outcome });
                return outcome;
            } finally {
                // Where the check had already ended, by the bound on the machine set-up, a sentence set now is told anew.
                if (!this.#endStartCheck() && this.#startUpdate !== said) this.#changed();
            }
        }, (outcome) => npmRan.has(outcome));
    }

    /**
     * Ends the start's check of the Reply CLI before its update ends: a start that asks for none, or one that waits no longer
     * for the machine set-up (`replyStartCheck`). An update that comes after it still runs.
     */
    endStartCheck(): void {
        this.#endStartCheck();
    }

    /** The start's check of the Reply CLI has ended: the page is told once. False where it had already ended. */
    #endStartCheck(): boolean {
        if (this.#startCheckEnded) return false;
        this.#startCheckEnded = true;
        this.#changed();
        return true;
    }

    async #updateReplyAtStart(): Promise<StartUpdateOutcome> {
        if (this.#startUpdateLooked) return 'settled';
        this.#startUpdateLooked = true;
        const npmPackage = programInstalls.reply?.npmPackage ?? null;
        if (npmPackage === null || this.#deps.programs.find('reply') === null) return 'not_found';
        const asked = readUpdateSwitch(this.#deps.environment, dataLayout(this.#deps.paths.data).userSettingsFile);
        if (asked !== 'on') return asked === 'off' ? 'off' : 'switch_unread';
        const before = await this.#replyVersion();
        if (before === null) return 'version_unread';
        if (versionAtLeast(before, replyMinimumVersion)) return 'current';
        if (this.#deps.programs.find('npm') === null) return 'no_npm';
        const tried = this.#deps.settings().replyUpdateTried;
        if (tried !== undefined && versionAtLeast(tried, replyMinimumVersion)) return 'tried';
        // npm replaces the CLI's files under whatever runs it: nothing is run while a plugin the runtime holds as valid may, nor
        // where the runtime does not say.
        const running = await this.#deps.cli.run(['plugin', 'list']);
        const atWork = running.exit === 0 ? replyAtWork(running.stdout) : null;
        if (atWork !== false) return atWork === null ? 'plugins_unread' : 'in_use';
        if (!this.#markReplyTried(replyMinimumVersion)) return 'unmarked';

        const result = await this.#npmInstall('reply', npmPackage);
        // npm that ended badly, or was stopped at its time limit, may have put the new CLI in place before it did.
        const version = await this.#replyVersion();
        if (result.exit !== 0 && (version === null || !versionAtLeast(version, replyMinimumVersion))) {
            return this.#startUpdateEnded('failed', startUpdateSentences.failed(npmLine(result)));
        }
        const reload = await this.#reloadPlugins();
        // Said of the CLI the runtime's reload found; a reload it did not make answers with the one it found before.
        const listed = reload.exit === 0 ? await this.#deps.cli.run(['plugin', 'list']) : null;
        const found = listed !== null && listed.exit === 0 ? runtimeReplyVersion(listed.stdout) : null;
        if (version === null) return this.#startUpdateEnded('failed', startUpdateSentences.failed(versionUnsaid));
        const old = [version, found].find((each) => each !== null && !versionAtLeast(each, replyMinimumVersion));
        if (old !== undefined && old !== null) return this.#startUpdateEnded('still_old', startUpdateSentences.stillOld(old));
        this.#markReplyTried(null);
        return this.#startUpdateEnded('updated', startUpdateSentences.updated(version));
    }

    #startUpdateEnded<Ended extends 'updated' | 'still_old' | 'failed'>(outcome: Ended, sentence: string): Ended {
        this.#startUpdate = { updated: outcome === 'updated', said: sentence };
        return outcome;
    }

    /** Writes the mark of the start's update, or takes it away; false where it could not be written. */
    #markReplyTried(minimum: string | null): boolean {
        const { replyUpdateTried: _, ...rest } = this.#deps.settings();
        try {
            if (this.#deps.saveSettings === undefined) throw new Error('The settings are not written here.');
            this.#deps.saveSettings(minimum === null ? rest : { ...rest, replyUpdateTried: minimum });
        } catch {
            this.#deps.log.warn(minimum === null ? 'reply_start_update_mark_left' : 'reply_start_update_unmarked', {});
            return false;
        }
        return true;
    }

    /**
     * Places one of the four bundled plugins with the CLI, writes its token, its target, ntfy's server and its grant into
     * the installation's settings as a person would, asks the runtime to load its plugins again and reads them back. With
     * no values it only asks for the reload and the read-back, which is what Reload is. A settings file the desktop
     * cannot read is refused and left as it is. The token travels in that file only: never in an argument, an
     * environment or a log.
     */
    configurePlugin(pluginId: unknown, values: unknown): Promise<void> {
        return this.#serial(async () => {
            const plugin = bundledPlugin(pluginId);
            if (plugin === undefined) throw new Refused('That is not a plugin the desktop configures.');
            const asked = (typeof values === 'object' && values !== null ? values : {}) as Record<string, unknown>;
            const wanted: PluginValues = {
                token: typeof asked.token === 'string' ? asked.token : undefined,
                target: typeof asked.target === 'string' ? asked.target : undefined,
                server: typeof asked.server === 'string' ? asked.server : undefined,
            };
            if (wanted.token !== undefined || wanted.target !== undefined || wanted.server !== undefined) {
                await this.#placeAndEdit(plugin.id, (settings) => withPluginConfigured(settings, plugin, wanted));
            }
            await this.#reload(plugin.id, wanted);
        });
    }

    /**
     * Disconnects one of the three notification plugins: its configuration and its notification binding are taken out
     * of the installation's settings, with the Http grant derived from a server of its own; the rest of its grant and
     * its package stay, and the runtime is asked to load its plugins again and, when that reload exits 0, the plugins
     * are read back. The id is checked against the desktop's own table before the settings file is read, and the
     * journal forwarder is refused there too. A settings file the desktop cannot read or keep is refused and left as it
     * is; one the edit would not change is not written.
     */
    disconnectPlugin(pluginId: unknown): Promise<void> {
        return this.#serial(async () => {
            const plugin = bundledPlugin(pluginId);
            if (plugin === undefined || plugin.target.where !== 'binding') throw new Refused('That is not a plugin the desktop disconnects.');
            const { file, settings } = this.#readForEdit();
            let next: Record<string, unknown>;
            try {
                next = withPluginDisconnected(settings, plugin);
            } catch (error) {
                throw error instanceof Refused ? new Refused(`${error.message} ${editByHand}`) : error;
            }
            const written = JSON.stringify(next) !== JSON.stringify(settings);
            if (written) writeAtomically(file, `${JSON.stringify(next, null, 2)}\n`);
            const reload = await this.#reloadPlugins();
            const listed = reload.exit === 0 ? await this.#deps.cli.run(['plugin', 'list']) : null;
            this.#deps.log.info('plugin_disconnected', {
                PluginId: plugin.id,
                Written: written,
                Exit: reload.exit,
                Valid: listed !== null && listed.exit === 0 && (pluginRuntimes(listed.stdout).get(plugin.id)?.valid ?? false),
            });
            this.#changed();
            if (reload.exit !== 0) throw this.#reloadRefused(reload);
        });
    }

    /** Asks the runtime to read its plugins again, and keeps what it said about the settings file for `status()`. */
    async #reloadPlugins(): Promise<CliResult> {
        const reload = await this.#deps.cli.run(['plugin', 'reload']);
        this.#reloadRefusal = reload.exit === 0 ? null : settingsUnreadable(reload.stdout);
        return reload;
    }

    #reloadRefused(reload: CliResult): Refused {
        return new Refused(this.#reloadRefusal ?? words(reload, 'The runtime did not load the plugins again.'));
    }

    /** The reload, then the plugin list read back, so what is logged says whether the plugin is now valid. */
    async #reload(pluginId: string, wanted: PluginValues): Promise<void> {
        const reload = await this.#reloadPlugins();
        const listed = reload.exit === 0 ? await this.#deps.cli.run(['plugin', 'list']) : null;
        this.#deps.log.info('plugin_configured', {
            PluginId: pluginId,
            TokenChanged: wanted.token !== undefined,
            TargetChanged: wanted.target !== undefined,
            ServerChanged: wanted.server !== undefined,
            Exit: reload.exit,
            Valid: listed !== null && listed.exit === 0 && (pluginRuntimes(listed.stdout).get(pluginId)?.valid ?? false),
        });
        this.#changed();
        if (reload.exit !== 0) throw this.#reloadRefused(reload);
    }
}
