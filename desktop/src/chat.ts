/**
 * The Live chat. One turn is one host process: Claude Code, or Codex, started in the chat folder with the person's words
 * on its standard input, its output mapped to the page's events in the order the host says them, and its end always
 * said with a `done`. Claude Code asks what its rules leave open through the permission endpoint the turn opens; the
 * broker decides what the permission model settles and puts the rest on the page as a card. A host that cannot drive
 * the chat is never swapped for one that can, and the desktop never signs in to a host.
 *
 * A chat is a conversation the desktop keeps (`chat-conversations.ts`): one per campaign, and any number of set-up chats
 * about no campaign yet. The conversation's record decides which campaign a turn is about, and a header in the
 * desktop's words says so before the person's words, every turn. The record keeps what the page drew and the session
 * each host named, so a conversation reopens after a restart and its host resumes it; a host that has not seen it, or
 * whose session is gone, starts afresh with a recap of the earlier messages. A set-up chat whose turn made exactly one
 * new campaign becomes that campaign's conversation. A conversation is how the work is ordered, not a fence: a turn
 * acts for the person across the installation, as the permission model and the allow list let it.
 *
 * Each conversation has an access mode (`chat-modes.ts`), read when a person's send starts a turn and kept on its record:
 * Full access where the record names none, which every new chat starts in with no box, and Ask first, Auto-edit or Plan
 * only once the person chose one from the menu, through a restart and Start fresh alike. A turn the mode cannot apply to
 * (Full access while this version does not offer it, on a Claude Code below its floor or sent from a browser tab;
 * Auto-edit on Codex) runs in Ask
 * first, the chat says why once, and the record keeps the mode. Leaving Full access stops the turn that runs in it. The
 * first tool of a Full access turn that runs without its hook having seen it ends the turn, and the chat goes back to Ask
 * first. Every turn of a conversation runs in its own folder, `<data>/desktop/chats/<id>/` beside the shared chat folder,
 * and one an earlier desktop made in the shared chat folder (`Conversation.folder`); before every turn that folder's agent
 * settings are put right (`chat-agent-folder.ts`). No turn starts without a person's send; the naming run of a set-up
 * chat (`chat-naming.ts`) is no turn: it runs with no tool at all, in any mode.
 *
 * One conversation is of its own kind, the Feed (`feed.ts`): the person's main chat, about no campaign, made on the
 * page's first ask, into which the page hands the journal's lines of a closed table of kinds. It is never renamed,
 * removed or handed over; a campaign one of its turns makes keeps its own conversation. A line never starts a turn: the
 * lines reach the agent only with the person's next message, after the header, set apart as records.
 */

import { chmodSync, closeSync, existsSync, lstatSync, mkdirSync, openSync, readdirSync, rmSync, statSync, unlinkSync, writeSync } from 'node:fs';
import { homedir, hostname } from 'node:os';
import { dirname, isAbsolute, join } from 'node:path';
import { AgentFolderUnowned, ownAgentFolder, ownConversationFolder } from './chat-agent-folder.ts';
import { endConversationFolder, endedNameShape, moveLeftovers, moveOwnLeftovers, sweepCap, sweepEnded } from './chat-full-folder.ts';
import { chatRules, claudeArguments, claudeLine, claudeRemovedToolsOf, claudeToolWord, codexArguments, codexLine, dataRules, hostEnvironment, pinnedEnvironment, type HostLine, type KeySource, type Step } from './chat-hosts.ts';
import {
    bounded,
    conversationIdShape,
    continuableOf,
    takenLimit,
    ConversationUnwritten,
    type Chosen,
    type ContinuableHost,
    memoryGivenTo,
    nameSession,
    newConversationId,
    noteMemoryGiven,
    readConversations,
    recordShape,
    removeConversation,
    writeConversation,
    type Conversation,
    type KeptEntry,
} from './chat-conversations.ts';
import { ExportRefused, exportInto, exportName, exportNotTyped, exportStart } from './chat-export.ts';
import { feedBlock, givenTitle, header, nameOf, recap, titleOf, turnInput, type About } from './chat-header.ts';
import { digestsOf, memoryBlock, memoryReadLimitMs, memorySecondLookMs, memoryToGive, type GivenDigests, type MemoryGiving, type MemoryLevel, type MemoryPlace, type MemoryRead } from './chat-memory.ts';
import { readMemoryFile, readMemoryWithin } from './chat-memory-files.ts';
import { currentModel, effortRefusal, haikuEffortFrom, isEffort, modelRefusal, modelShape, type Effort, type KeptLists } from './chat-models.ts';
import {
    claudeNamingArguments,
    codexNamingArguments,
    codexNamingModel,
    namingInput,
    namingLimitMs,
    namingModelOf,
    runNaming,
    type NamingOutcome,
} from './chat-naming.ts';
import { PermissionBroker, type BrokerEvent, type Decided } from './chat-permission-broker.ts';
import { openEndpoint, type Endpoint } from './chat-permission-endpoint.ts';
import type { ChatScope, Platform } from './chat-permission-model.ts';
import { startHost, type HostExit, type HostProcess } from './chat-process.ts';
import { entryOf, identityOf, realPathOf, textOf } from './chat-real-path.ts';
import type { SkillsGate } from './skills-gate.ts';
import { namingFolderPath, newSecret, newServerName, newTurnId, removeTurnFiles, sweepTurnFiles, turnSettingsPath, writeTurnConfig, writeTurnSettings } from './chat-turn-files.ts';
import { helpNouns, modeFence, type ChatFence } from './chat-deny.ts';
import { InstructionsUnwritten, placeInstructions } from './chat-instructions.ts';
import {
    askFirstThisTurn,
    bypassHeldMs,
    bypassLeadsTo,
    codexAutoEdit,
    codexFullAccessRefused,
    followsCodexRefusal,
    codexFullAccessNarrowed,
    earlierDesktopAskFirst,
    feedDefaultMode,
    forcedDefault,
    fullAccessCurrent,
    fullAccessNotYet,
    fullAccessOffered,
    fullAccessSentence,
    hookFilteredSentence,
    isChatMode,
    namedMode,
    planModeMoved,
    turnMode,
    unguardedSentence,
    type AskFirstWhy,
    type Bypass,
    type BypassWhy,
    type ChatMode,
    type NamedMode,
} from './chat-modes.ts';
import type { Cli } from './cli.ts';
import { feedKinds, feedTitle, lineOf, linesWaiting, markGiven, succeededKind, take, trimmed, type LineEntry } from './feed.ts';
import { codexFloorSentence, compareVersions, floorSentence, printedCodexVersion, printedVersion, versionLimitMs } from './host-version.ts';
import { settingsFilesOf, type DesktopPaths } from './paths.ts';
import { programLaunch, type Programs, type RunResult } from './programs.ts';
import {
    checkingSandbox,
    checkRuns,
    runSandboxCheck,
    sandboxCheck,
    sandboxKey,
    sandboxKeyedSentence,
    sandboxLimitMs,
    sandboxNoSettingsSentence,
    sandboxPathSentence,
    sandboxSentence,
    sandboxUncheckedSentence,
    SandboxUnproven,
    type SandboxCheck,
} from './codex-sandbox.ts';
import { hosts, keyedFolder, keyProvider, providerEnvironment, withoutKey, withoutWholeKey } from './providers-model.ts';
import { lastChosenHost } from './roles-profile.ts';

/** Why a turn starts a fresh session of a conversation that had one. */
export type FreshReason = 'host_changed' | 'not_found';

/** The kinds of card the page draws, and "unknown" for a block it could not read a kind from: what the log may name. */
export const refusedCardKinds: ReadonlySet<string> = new Set(['choice', 'people_list', 'agent_status', 'needs', 'accounts', 'ai_setup', 'reply_setup', 'plan', 'own_list', 'unknown']);
/** Why a card the AI asked for did not draw, as the page says it: the log's closed list. */
export const cardFailures: ReadonlySet<string> = new Set(['kind_unknown', 'too_long', 'malformed', 'schema']);

/** What the page is told, by the chat it belongs to: U2's `DesktopChatEvent`. */
export type ChatEvent =
    | ({ kind: 'step' } & Step)
    /** `at`: the moment the desktop kept the words, ISO-8601 UTC, as the record keeps it beside them. */
    | { kind: 'text'; text: string; at: string }
    | BrokerEvent
    | { kind: 'attached'; campaignId: string; title: string }
    | { kind: 'fresh'; reason: FreshReason }
    | { kind: 'pick'; campaignIds: string[] }
    /** The conversation's title changed: a person renamed it, or the desktop named a set-up chat by what it is about. */
    | { kind: 'renamed'; title: string }
    /** The conversation's mode changed: a person chose it, or the desktop put the chat back to Ask first. */
    | { kind: 'mode'; mode: ChatMode }
    /** A line the Feed took: at its end, or folded into its last line, which it then stands in for. */
    | { kind: 'line'; line: LineEntry; folded: boolean }
    /** The lines written while the window was closed were more than one read brings back: the older ones are not shown. */
    | { kind: 'gap' }
    /**
     * A sentence the desktop says to the person, once: a folder left out of the chat, why a turn runs in Ask first, or the
     * folder a Full access grant of an earlier desktop worked in, now kept seven days. `folder` is present for the last,
     * the name of that ended folder, for the Open folder button (`openEndedFolder`).
     */
    | { kind: 'notice'; text: string; folder?: string }
    /**
     * The turn is over. `plan` is present, and true, when a turn in Plan only ended well with its words last: those words,
     * the conversation's last entry, are its plan, marked so on the kept entry too (`#markPlan`).
     */
    | { kind: 'done'; error: string | null; plan?: true };

/** What a send names besides its conversation and its words: never a campaign, which the conversation's record decides. */
export interface ChatSendOptions {
    host: string;
    model?: string;
    /** One of the five levels, which the model named must take (`effortRefusal`); absent, the host keeps its own. */
    effort?: Effort;
    /**
     * The mode the turn was approved for: the plan card's go names Ask first. The send is refused, before anything is
     * built, when the conversation is in another mode as it is read; absent, the turn runs in the mode it is in.
     */
    mode?: ChatMode;
    /**
     * Present, and true, when the words are the ones the person typed in the message box: only such words are an
     * `/export` (`chat-export.ts`). The page sends it with an export alone.
     */
    typed?: true;
    /**
     * Present, and true, on a send a browser tab made, which the bridge marks (`browser-bridge.ts`): a turn of a
     * conversation in Full access then runs in Ask first.
     */
    fromTab?: boolean;
}

/** A conversation as `chat.list` lists it: `running` whether a turn of it runs now. */
export interface ConversationSummary {
    id: string;
    campaignId: string | null;
    /** Present, and true, for the Feed. */
    main?: true;
    title: string;
    updatedAt: string;
    running: boolean;
    /** The conversation's access mode, as the desktop holds it now. */
    mode: ChatMode;
    /**
     * Present, and true, when its turns run in a folder of its own (`folder: 'own'` on its record), which removing it
     * moves to the ended place, kept seven days.
     */
    ownFolder?: true;
}

/**
 * A call the chat refuses, in the desktop's own sentence for the page to show. It is answered to the page as a value
 * (`chat-ipc.ts`), never thrown across the seam; a throw of anything else is no refusal and stays a throw.
 */
export class ChatRefused extends Error {
    constructor(sentence: string) {
        super(sentence);
        this.name = 'ChatRefused';
    }
}

/** A conversation as `chat.open` answers it: what the page drew, kept as text, and what its last turn was sent with. */
export interface ConversationOpened extends ConversationSummary {
    entries: KeptEntry[];
    dropped: number;
    chosen: Chosen | null;
}

/** What one turn did, for the log: counts and a duration, never its words. */
export interface TurnEnded {
    host: string;
    exit: number;
    durationMs: number;
    steps: number;
    requests: number;
}

export interface ChatOptions {
    paths: DesktopPaths;
    /** The desktop's environment, PATH included: where hosts are found, and the base of a host's environment. */
    env: Readonly<Record<string, string | undefined>>;
    /** The folder the bundled `jason` is in, first on the host's PATH. */
    bundle: string;
    /** Whether the desktop runs with a data directory `JASON_DATA_DIR` names, which the host is then given too. */
    dataNamed: boolean;
    /** The bundle's version, from which a host's label for reply-cli is built (`hostEnvironment`); null when there is none. */
    bundleVersion: string | null;
    /**
     * This account's name as the operating system gives it, which the main process reads (`os.userInfo().username`) and
     * the carried `jason` writes a person's acts under (`Environment.UserName`): every turn's header names the person by it,
     * and asks the chat to ask where it is null, not read. Absent, as in a row about something else, the header says
     * nothing of the person.
     */
    account?: string | null;
    cli: Cli;
    /** The program runner, which reads Claude Code's version before each turn on it. */
    programs: Pick<Programs, 'run'>;
    /** The models Codex offered when last asked, by host id (`model-lists.ts`); none when not given. */
    models?(): KeptLists;
    keyFor(providerId: string): string | undefined;
    /** The deny list's commands and operations, the allow list's verbs and the instruction text, read at each turn. */
    fence(): ChatFence;
    emit(chatId: string, event: ChatEvent): void;
    /** Said each time a conversation's mode changes, by the two modes alone. */
    modeChanged?(from: ChatMode, to: ChatMode): void;
    /** Said when a Full access turn is stopped because a tool ran that its hook never saw, or such a tool ran after a stop. */
    unchecked?(): void;
    /**
     * Said once in a turn of Ask first or Auto-edit, by its mode and the tool's word (`claudeToolWord`), when a tool ran or
     * was refused that its hook never saw: the turn runs on under its cards and the host's own rules.
     */
    hookFiltered?(mode: ChatMode, tool: string): void;
    /**
     * Said once as the chat is made where the desktop runs as root, and each time a turn finds Claude Code's bypass not
     * available where no earlier finding still holds: its cause, and the mode Claude Code named instead, a word of its
     * closed table (`namedMode`), null where it named none.
     */
    bypassGone?(why: BypassWhy, named: NamedMode | null): void;
    /** How long a cause a turn found holds, in milliseconds (`bypassLeadsTo`); absent, `bypassHeldMs`, a minute. A row shortens it. */
    bypassHeldMs?: number;
    /**
     * Whether the desktop runs as root, as which Claude Code does not run in its bypass mode on macOS and Linux; absent,
     * the system's answer. A row gives its own.
     */
    root?(): boolean;
    /** Whether Full access is offered; absent, this version's switch (`fullAccessOffered`). Tests of its code turn it on. */
    fullAccessOffered?: boolean;
    /**
     * Claude Code's version as the last listing of the hosts read it (`Setup.handedClaudeVersion`), from which the page
     * drew the mode a turn runs in: null when it could not be read, undefined before a listing read it.
     */
    handedClaudeVersion?(): string | null | undefined;
    /**
     * Asks the page to read the hosts again (`setup.changed`): a turn found Claude Code on the other side of the floor of
     * Full access from the last listing, or found Claude Code's bypass not available here.
     */
    setupChanged?(): void;
    /** How a conversation's record is written: `writeConversation` unless a row makes a write fail. */
    writeRecord?(folder: string, conversation: Conversation): void;
    /** Held by a start while it deploys skills into the chat folder: a send waits for it before it builds a turn. */
    skillsGate?: Pick<SkillsGate, 'held' | 'wait'>;
    /** The clock, in milliseconds; absent, the system's. A test gives its own to age a folder. */
    now?(): number;
    /** How many entries one pass of the sweep of the ended grants' folders takes; absent, `sweepCap`. */
    sweepCap?: number;
    /** How often the ended place is swept while the desktop runs; absent, `sweepEveryMs`, a day. A row shortens it. */
    sweepEveryMs?: number;
    /** Opens a folder for the person to see (the Open folder button); absent, nothing is opened. */
    openFolder?(path: string): void | Promise<void>;
    /** Starts a turn's host; absent, `startHost`. A row gives one whose exit it lets through when it chooses. */
    startHost?: typeof startHost;
    /**
     * Said when a folder could not be moved to the ended place or swept, by the system's error code: a grant's folder an
     * earlier desktop left, or a conversation's own folder.
     */
    fullFolderFailed?(step: 'move' | 'sweep', code: string): void;
    started?(host: string, resumed: boolean, mode: ChatMode): void;
    ended?(turn: TurnEnded): void;
    decided?(decided: Decided): void;
    /** Said once, the first time the application is found to carry no instruction text; the turns run without it. */
    instructionsMissing?(): void;
    /** Said each time a memory file cannot be written, at the start or before a turn, which is then refused. */
    instructionsUnwritten?(file: string, code: string): void;
    /**
     * Said for each level of the person's memory files a host was started with, by the level and how it stood (given,
     * changed, gone or skipped): never its path, its digest or a word of it (`chat-memory.ts`).
     */
    memoryGiven?(level: MemoryLevel, how: MemoryGiving): void;
    /** How a level's memory file is read: `readMemoryFile` unless a test reads it otherwise. */
    memoryReader?(place: MemoryPlace, platform: NodeJS.Platform): Promise<MemoryRead | null>;
    /** The longest a level's read may take, in milliseconds: `memoryReadLimitMs` unless a test says otherwise. */
    memoryReadLimitMs?: number;
    /** Said once at the start: how many conversations were read, and how many files there were not records. */
    conversationsRead?(kept: number, unreadable: number): void;
    /** Said each time a conversation's record could not be written or removed, by the system's error code. */
    conversationUnwritten?(code: string): void;
    /** Said at the end of a set-up chat's turn: how many new campaigns it found, and whether it handed over. */
    setupEnded?(found: number | null, handedOver: boolean): void;
    /** Said when a turn starts a fresh session of a conversation that had one. */
    fresh?(reason: FreshReason): void;
    /**
     * Said when a turn on Anthropic's key starts and Claude Code names where its key came from by a value that does not
     * say it is the environment's, nor that it is not: the word only (`KeySource`), never what the host printed.
     */
    keySource?(source: Exclude<KeySource, 'environment' | 'elsewhere'>): void;
    /** Said at the end of each naming run of a set-up chat, or when one is held: how it ended, as a closed word only. */
    named?(outcome: NamingOutcome): void;
    /** How long a naming run may take; thirty seconds unless a test shortens it. */
    namingLimitMs?: number;
    /** How long the Feed's catch-up read may take; thirty seconds unless a test shortens it. */
    feedReadLimitMs?: number;
    /** How long a card waits for the person; thirty minutes unless a test shortens it. */
    answerWindowMs?: number;
    /** How long the Windows check of Codex's sandbox may take; thirty seconds unless a test shortens it. */
    sandboxLimitMs?: number;
    /**
     * How the Windows check runs its line outside any sandbox, its control: cmd by its full path (`checkRuns`) unless a
     * test gives a stand-in, since a test may run where there is no cmd.
     */
    sandboxOutside?: (check: SandboxCheck) => Promise<RunResult>;
    /**
     * Said each time the Windows check of Codex's sandbox ends, unless the turn was stopped meanwhile: its answer, and
     * whether a command inside opened the desktop's settings file, which decides nothing; `unknown` where no run inside
     * decided. Two closed words, never a path or what was read.
     */
    sandboxChecked?(answer: 'holds' | 'reads' | 'unproven', settings: 'read' | 'unread' | 'unknown'): void;
    /**
     * Said when the page says a card the AI asked for in a chat this desktop holds did not draw: its kind and why, each one
     * of a closed list (`refusedCardKinds`, `cardFailures`), never what the block held.
     */
    cardRefused?(kind: string, failure: string): void;
    platform?: Platform;
}

interface Running {
    stopped: boolean;
    process: HostProcess | null;
    /** The turn's id while it has a file under `<data>/desktop`; a Codex turn has none. */
    turnId: string | null;
    /** The chat id the turn's events go to: the one its send named. */
    replyTo: string;
    /** The cards of this turn the page holds, by request id, so the answer is kept with the card. */
    cards: Map<string, Extract<KeptEntry, { kind: 'permission' }>>;
    /** The mode the turn runs in, read when the person's send started it: Ask first where the conversation's cannot apply. */
    mode: ChatMode;
    /** Stops the Windows check of Codex's sandbox while it runs. */
    checking?: AbortController;
}

interface Resolved {
    host: string;
    label: string;
    program: 'claude' | 'codex';
    provider: Record<string, string>;
    /** Whether the turn runs on a key provider, which the inherited provider and credentials never reach. */
    keyed: boolean;
    /** The model named on the command line; an Anthropic-compatible endpoint is given its model by the environment instead. */
    model: string | undefined;
    /** The model the send named, by the name the vendor gives it now (`currentModel`), wherever it goes. */
    named: string | undefined;
    /** The effort the send named, which the model named takes. */
    effort: Effort | undefined;
    /** A key provider's host folder of the desktop's own (`keyedFolder`), made before the turn; null on a plain turn. */
    folder: string | null;
    /** Whether the host's word on where its key came from is read: on Anthropic's own key alone, whose variable the page names. */
    checksKeySource: boolean;
    /** The key a keyed turn runs on, which no error text of the host reaches the page or the record with (`withoutKey`). */
    key: string | null;
}

/** The turn's end when a provider refused its key: one sentence naming where the key is changed. */
function refusedSentence(label: string): string {
    return `${label} refused the key. Change it on the ${label} card in Connect, then send again.`;
}

/** The turn's end when the provider answered 403 alone, which a proxy or a denied resource answers as well. */
function forbiddenSentence(label: string): string {
    return `${label} refused the key or the request (403). Check the key on the ${label} card in Connect, then send again.`;
}

/** The turn's end when Claude Code said the key it uses is not the one in its environment. */
function notTakenSentence(label: string): string {
    return `Claude Code did not use the ${label} key, so the turn was stopped before it ran on another account. Check the key on the ${label} card in Connect, then send again.`;
}

/**
 * Makes a key provider's host folder, and its parent of the desktop's own, closed to every account but this one where
 * the system has modes: the folder keeps the transcripts of that provider's turns.
 */
export function makePrivate(label: string, parent: string, folder: string): void {
    try {
        mkdirSync(folder, { recursive: true, mode: 0o700 });
        if (process.platform === 'win32') return;
        for (const each of new Set([parent, folder])) chmodSync(each, 0o700);
    } catch (error) {
        // The system's code, never its text, which names the path; the turn is refused and no host starts.
        const code = (error as NodeJS.ErrnoException | null)?.code;
        throw new Error(`the folder for ${label} turns could not be made (${typeof code === 'string' ? code : 'unknown'}).`);
    }
}

/**
 * A campaign as a turn reads it: what the header says, the folders a Claude Code turn adds, and where the campaign's and
 * its project's memory files are, as the runtime records their folders, for a turn on any host.
 */
interface CampaignRead {
    about: About;
    added: string[];
    memory: MemoryPlace[];
}

/** A campaign in `jason campaign list`: when it was created, in milliseconds, and its name. */
type Listed = Map<string, { created: number; name: string }>;

const chatIdShape = /^[A-Za-z0-9_.:-]{1,200}$/;
const campaignIdShape = /^cmp_[0-9A-Za-z]{1,64}$/;
const textLimit = 1_000_000;
/** The most lines one `feed` call takes: as many as the runtime's stream sends at one look. */
const feedBatch = 200;
/** How many lines a catch-up reads back, in one page; more than that leaves a gap. */
const catchUpLimit = 100;
/** How many lines handed while the catch-up reads wait for it; the older ones beyond are let go and said as a gap. */
const feedWaitingLimit = 200;
/** How long the catch-up's read may take: a slow runtime keeps the Feed waiting no longer. */
const feedReadLimitMs = 30_000;
/** How far before the newest kept line the catch-up reads, since lines are committed out of the order of their moments. */
const catchUpLookbackMs = 5_000;
const notAFeedBatch = 'That is not a list of journal lines the desktop takes.';
const feedKept = 'The Feed is always kept; it cannot be removed or renamed.';
/** The longest text a rename reads before it makes it one line and cuts it to the title's cap. */
const renameLimit = 10_000;
const wordsKept = 600;
/** One page of `campaign list` at the runtime's largest, and how many pages a hand-over reads before it gives up. */
const listPage = 1000;
const listPages = 100;

/** What a set-up turn's end says of the campaigns it made: the one it handed over to, or the ones to pick from. */
type HandOver = Extract<ChatEvent, { kind: 'attached' | 'pick' }>;

/** How one start of the host ended; `host` null when it could not be started, `again` when a fresh session follows. */
interface Attempted {
    exit: HostExit;
    result: string | null | undefined;
    host: HostProcess | null;
    again: boolean;
    /** Whether the result is the desktop's own sentence for a refused key, which no scrub touches. */
    ours?: boolean;
    /** Whether Claude Code refused its bypass mode before it started (`BypassWhy`). */
    bypassRefused?: boolean;
    /** The mode Claude Code started in instead of its bypass, where it named one and this start was stopped at its first line. */
    bypassOff?: Bypass | null;
    /** Whether the host said any line on its standard output before it exited. */
    heard?: boolean;
}

/** Whether this process runs as root on macOS or Linux, as which Claude Code does not run in its bypass mode. */
function runsAsRoot(): boolean {
    return process.platform !== 'win32' && process.getuid?.() === 0;
}

/** Every cause of Claude Code's bypass not being available (`BypassWhy`), each told once in each hold of it. */
const bypassKinds = Object.keys({ root: true, bypass_off: true, forced_default: true, bypass_refused: true, unguarded: true } satisfies Record<BypassWhy, true>) as BypassWhy[];

/** The campaigns the conversation's last pick named. */
const candidatesOf = (conversation: Conversation): string[] => {
    for (let i = conversation.entries.length - 1; i >= 0; i--) {
        const entry = conversation.entries[i];
        if (entry?.kind === 'pick') return entry.campaignIds;
    }
    return [];
};

const busy = 'This chat is still answering. Wait for it to finish, or stop it, then send again.';

/** How often the ended place is swept while the desktop runs, beside the sweep at each start: once a day. */
export const sweepEveryMs = 24 * 60 * 60 * 1000;

const notAHost = 'That is not an AI host this desktop knows. Choose Claude Code or Codex for the chat.';

/** The program a host the page names runs: a key provider's, or a host that drives the chat; undefined for any other. */
function programOf(host: unknown): string | undefined {
    return keyProvider(host)?.program ?? hosts.find((each) => each.id === host && each.drivesChat)?.program;
}

/** How much each mode lets a turn do without asking: a change to a narrower mode stops the turn running in a wider one. */
const breadth: Readonly<Record<ChatMode, number>> = { plan_only: 0, ask_first: 1, auto_edit: 2, full_access: 3 };

const notAConversation = 'That conversation is not one this desktop keeps.';
const stillAnswering = (act: string) => `This conversation is still answering. Wait for it to finish, or stop it, then ${act}.`;

function currentPlatform(): Platform {
    return process.platform === 'win32' || process.platform === 'darwin' ? process.platform : 'linux';
}

/** The last words a host wrote to its standard error, whole lines, short enough for the page. */
function lastWords(tail: string): string {
    const lines = tail.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
    let words = '';
    for (let i = lines.length - 1; i >= 0; i--) {
        const next = words === '' ? (lines[i] ?? '') : `${lines[i]} ${words}`;
        if (next.length > wordsKept) break;
        words = next;
    }
    return words === '' ? (lines.at(-1) ?? '').slice(-wordsKept) : words;
}

function folderThere(path: unknown): string | null {
    if (typeof path !== 'string' || !isAbsolute(path)) return null;
    try {
        return statSync(path).isDirectory() ? path : null;
    } catch {
        return null;
    }
}

/** Removes a turn's file; one that cannot be removed now is left for the next start's sweep. */
function removeQuietly(turns: string, turnId: string): void {
    try {
        removeTurnFiles(turns, turnId);
    } catch {
        // Left for the next start.
    }
}

function entriesOf(folder: string): string[] {
    try {
        return readdirSync(folder);
    } catch {
        return [];
    }
}

/** Takes every plan's mark off a conversation's entries; whether one had it. */
function unmarkPlans(entries: KeptEntry[]): boolean {
    let had = false;
    for (const entry of entries) {
        if (entry.kind === 'text' && entry.plan === true) {
            delete entry.plan;
            had = true;
        }
    }
    return had;
}

/** Whether anything stands at a name, by `lstat`: one that cannot be looked at counts as standing. */
function standing(path: string): boolean {
    try {
        lstatSync(path);
        return true;
    } catch (error) {
        return (error as NodeJS.ErrnoException | null)?.code !== 'ENOENT';
    }
}

/** Whether a conversation's own folder stands at its place: a folder itself, never a link or a junction to one. */
function ownFolderStands(path: string): boolean {
    try {
        const stat = lstatSync(path);
        return stat.isDirectory() && !stat.isSymbolicLink();
    } catch {
        return false;
    }
}

function objectOf(text: string): Record<string, unknown> | null {
    try {
        const value: unknown = JSON.parse(text);
        return value !== null && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : null;
    } catch {
        return null;
    }
}


export class Chat {
    #options: ChatOptions;
    #platform: Platform;
    #broker: PermissionBroker;
    /** By conversation id, the turn that runs. */
    #running = new Map<string, Running>();
    #conversations = new Map<string, Conversation>();
    /** The conversations kept: read from a record, or sent to. One made and never sent to is not listed or written. */
    #kept = new Set<string>();
    /**
     * A chat id the page made up, before the page knew the desktop's conversations, and the conversation its first send
     * found or made: that campaign's for a send that named one, a new set-up chat for one that named none.
     */
    /** The set-up turns that run, by conversation id, and whether another set-up turn ran beside each. */
    #setupTurns = new Map<string, { overlapped: boolean }>();
    /** Whether the missing instruction text was said already. */
    #missingSaid = false;
    /** The naming runs of set-up chats, one after another across the desktop: the last one asked for, or none. */
    #naming: Promise<void> = Promise.resolve();
    /** Whether the window closed: a naming run that waits is then not started. */
    #closed = false;
    /** The naming run that runs now, its stop and its folder; null while none runs. */
    #namingNow: { stop: AbortController; folder: string } | null = null;
    /**
     * How many times the person chose a mode for each conversation, by conversation id, counting every call of
     * `setMode`, a choice of the mode it is in and one refused included: a choice of Full access that waited on the
     * host's version is given only when no other choice came meanwhile.
     */
    #choices = new Map<string, number>();
    /**
     * Why turns of each conversation ran in Ask first while it was in another mode, by conversation id: each said once, and
     * a cause of Claude Code's bypass once in each hold of it, so a cause that comes to hold again is said again
     * (`#bypassGone`).
     */
    #toldAskFirst = new Map<string, Set<AskFirstWhy>>();
    /** The conversations already told that Codex ran a Full access turn in a narrower sandbox (`codexFullAccessNarrowed`). */
    #toldNarrowed = new Set<string>();
    /** The conversations told that a turn runs in Full access since Claude Code is current, where a listing drew Ask first. */
    #toldCurrent = new Set<string>();
    /** The conversations told that the check before each tool did not run in a turn of Ask first or Auto-edit. */
    #toldFiltered = new Set<string>();
    /**
     * Why Claude Code's bypass, which Full access runs in, is not available here, as the machine or a turn of this run
     * showed it, and until when on the clock (`#now`) that holds: a minute after a turn found it, or null for the rest of
     * this run (`bypassLeadsTo`); null while nothing that holds showed it. Never on a record.
     */
    #bypass: { bypass: Bypass; until: number | null } | null;
    /**
     * Asks the page to read the hosts again when a cause that holds a minute ends, so the menu draws the chat's own mode
     * again with no turn sent; never keeps the process alive, and cleared when the window closes.
     */
    #bypassEnds: NodeJS.Timeout | null = null;
    /**
     * Whether a Claude Code start in Full access was honoured in this run, since it began or since the last time the bypass
     * was found not available (`#bypassGone`): it named its session in Full access. Until one is, a resumed Full access
     * start forks (`--fork-session`), so a start refused its bypass leaves the session it resumed as it was; once one is,
     * resumed starts no longer fork. Never on a record.
     */
    #fullAccessHonoured = false;
    /** The daily sweep's timer, which never keeps the process alive; cleared when the window closes. */
    #daily: NodeJS.Timeout;
    /** The campaign and project folders already said to be left out, by path, so each is said once. */
    #leftOut = new Set<string>();
    /** Whether the records are known to carry their shape (`recordShape`): the desktop's own file says so, or this run wrote it. */
    #stamped = false;
    /** Whether this start found no record and no stamp: then the first record written writes the stamp first. */
    #stampAtFirstWrite = false;
    /** The records with no shape this start read once the records were stamped: each written again when it is opened. */
    #unshaped = new Set<string>();
    /** Of those, the ones put in Ask first for it, which are told why once, when each is opened. */
    #earlier = new Set<string>();
    /** Folders an earlier desktop's grants left that this start moved, by conversation: said once, when it is opened. */
    #movedAtStart = new Map<string, string>();
    /**
     * What the Windows check of Codex's sandbox found, for this run of the desktop, by Codex's file, its version and the
     * Codex home the turn names: true where the sandbox held, false where a command in it read the run file. A check
     * that proved neither is not kept, and runs again at the next send. A setup taken away while the desktop runs is
     * found at its next start.
     */
    #sandboxChecked = new Map<string, boolean>();
    /** The Feed's id, once it is made or read. */
    #feedId: string | null = null;
    /** The journal ids the Feed took, oldest first, at most `takenLimit`, kept on its record too (`taken`). */
    #feedSeen = new Set<string>();
    /** The lines handed while a catch-up reads, checked already, which wait for it; null while none runs. */
    #feedWaiting: LineEntry[] | null = null;
    /** Whether lines waiting for the catch-up were let go for want of room, which the catch-up then says as a gap. */
    #feedOverflow = false;
    /** The catch-up's read, settled once it has put its lines in. */
    #feedRead: Promise<void> = Promise.resolve();

    constructor(options: ChatOptions) {
        this.#options = options;
        this.#platform = options.platform ?? currentPlatform();
        const root = options.root?.() ?? runsAsRoot();
        this.#bypass = root ? { bypass: { why: 'root', named: null }, until: null } : null;
        // Running as root holds for the whole run, and is said to the log once, now, as a cause a turn finds is when it does.
        if (root) options.bypassGone?.('root', null);
        sweepTurnFiles(options.paths.turns);
        // The standing choice of Full access an earlier desktop kept, and its mark of a take-back owed, mean nothing now:
        // every chat starts in Full access with no box. Both files are unlinked, and neither ever changes a mode.
        for (const left of [options.paths.fullAccess, join(dirname(options.paths.fullAccess), 'full-access-taken-back')]) {
            try {
                unlinkSync(left);
            } catch {
                // Not there, or left for the next start; nothing reads it.
            }
        }
        // The chat folder carries the instruction text from the start; a fence that cannot be read now is the first
        // turn's to refuse, and a file that cannot be written now is said and written again before that turn. The start
        // never stops for either.
        try {
            this.#placeInstructions(options.fence());
        } catch {
            // Nothing more to do until a turn starts.
        }
        const read = readConversations(options.paths.conversations);
        for (const conversation of read.kept) {
            this.#conversations.set(conversation.id, conversation);
            this.#kept.add(conversation.id);
        }
        this.#settleShapes(read.kept, read.unstamped, read.listed && read.unreadable === 0);
        this.#settleFeed();
        options.conversationsRead?.(read.kept.length, read.unreadable);
        this.#broker = new PermissionBroker({
            emit: (chatId, event) => {
                this.#cardSaid(chatId, event);
                options.emit(chatId, event);
            },
            ...(options.decided ? { decided: options.decided } : {}),
            ...(options.answerWindowMs !== undefined ? { timeoutMs: options.answerWindowMs } : {}),
        });
        // A conversation's own folder no record names is known only once every record was read: one that cannot be read
        // names no folder.
        this.#sweepAtStart(read.listed && read.unreadable === 0 ? new Set(read.kept.map((each) => each.id)) : null);
        // A desktop left running sweeps each day too, so an ended folder goes within a day of its seven days, start or no
        // start.
        this.#daily = setInterval(() => this.#sweepLater(), options.sweepEveryMs ?? sweepEveryMs);
        this.#daily.unref();
    }

    #now(): number {
        return this.#options.now?.() ?? Date.now();
    }

    /**
     * The records' shape (`recordShape`), which an earlier desktop drops when it writes a record again, with the mode but
     * Plan only and the folder. A conversation's own folder that such a write dropped from its record is the
     * conversation's again wherever that folder stands, so its turns run there once more. The first start that finds
     * records with no shape takes each as it is, a record with no mode in Full access, and writes it again with its shape;
     * then it writes its own file that says so (`conversationsStamped`), which no earlier desktop reads or writes, only once
     * every one of them was written again, and only where the folder was listed and every file in it was read (`whole`):
     * a start that could not write one, list the folder or read a file leaves the file unwritten, through every later
     * write too, so the next start is a first start again. With no record at all in a folder that was listed, the first
     * record written writes that file first. A start that finds the file is no first start: a record with no shape is one an earlier desktop wrote
     * last, and it opens in Ask first, unless it is in Plan only, which that desktop keeps; it is told why once, and
     * written again with its mode and its shape, when it is opened. A file that is lost makes the next start a first start
     * again.
     */
    #settleShapes(kept: readonly Conversation[], unstamped: readonly string[], whole: boolean): void {
        const { paths } = this.#options;
        for (const conversation of kept) {
            if (conversation.folder === undefined && ownFolderStands(join(paths.chats, conversation.id))) conversation.folder = 'own';
        }
        const unshaped = new Set(unstamped);
        this.#stamped = standing(paths.conversationsStamped);
        if (this.#stamped) {
            for (const conversation of kept) {
                if (!unshaped.has(conversation.id)) continue;
                this.#unshaped.add(conversation.id);
                if (conversation.mode === 'plan_only') continue;
                conversation.mode = 'ask_first';
                this.#earlier.add(conversation.id);
            }
            return;
        }
        if (kept.length === 0) {
            this.#stampAtFirstWrite = whole;
            return;
        }
        let all = true;
        for (const conversation of kept) if (unshaped.has(conversation.id)) all = this.#rewrite(conversation) && all;
        // A record that could not be written again, or read, is taken up by the next start, which is a first start still.
        if (all && whole) this.#stamp();
    }

    /** Writes the desktop's own file that says the records carry their shape, never through anything at its name. */
    #stamp(): void {
        const { conversationsStamped } = this.#options.paths;
        try {
            mkdirSync(dirname(conversationsStamped), { recursive: true });
            const handle = openSync(conversationsStamped, 'wx', 0o600);
            try {
                writeSync(handle, `${recordShape}\n`);
            } finally {
                closeSync(handle);
            }
            this.#stamped = true;
        } catch (error) {
            this.#options.conversationUnwritten?.((error as NodeJS.ErrnoException | null)?.code ?? 'unknown');
        }
    }

    /** Writes a conversation's record, through the writer a row gives or `writeConversation`. */
    #write(conversation: Conversation): void {
        (this.#options.writeRecord ?? writeConversation)(this.#options.paths.conversations, conversation);
    }

    /** Writes a record again as it stands, its time untouched; whether it was written. */
    #rewrite(conversation: Conversation): boolean {
        try {
            this.#write(conversation);
            return true;
        } catch (error) {
            this.#options.conversationUnwritten?.(error instanceof ConversationUnwritten ? error.code : 'unknown');
            return false;
        }
    }

    #fullFolders() {
        const { chatFull, chatFullEnded, chatFullRecords } = this.#options.paths;
        return { full: chatFull, ended: chatFullEnded, records: chatFullRecords };
    }

    /**
     * A grant's folder an earlier desktop left is a leftover (no grant is made now): it moves to the ended place, its
     * seven days counted from now, and the conversation it was for is told so once, when it is opened. So does a
     * conversation's own folder that no record names, when `recorded` names every record (a removal that found the folder
     * held by a terminal leaves it for a start); while a record cannot be read, `recorded` is null and no such folder
     * moves. Then the ended place is swept, off the send path and bounded; a failure leaves the folder where it is and
     * says a code, and a turn is never refused for it.
     */
    #sweepAtStart(recorded: ReadonlySet<string> | null): void {
        const said = (code: string) => this.#options.fullFolderFailed?.('move', code);
        try {
            for (const each of moveLeftovers(this.#fullFolders(), this.#now(), said)) {
                if (each.conversation !== null && conversationIdShape.test(each.conversation)) this.#movedAtStart.set(each.conversation, each.name);
            }
        } catch {
            // Nothing to move.
        }
        if (recorded !== null) moveOwnLeftovers(this.#options.paths.chats, this.#fullFolders(), recorded, this.#now(), said);
        this.#sweepLater();
    }

    #sweepLater(): void {
        const timer = setTimeout(() => {
            try {
                sweepEnded(this.#fullFolders(), { now: this.#now(), cap: this.#options.sweepCap ?? sweepCap, said: (code) => this.#options.fullFolderFailed?.('sweep', code) });
            } catch (error) {
                this.#options.fullFolderFailed?.('sweep', (error as NodeJS.ErrnoException | null)?.code ?? 'unknown');
            }
        }, 0);
        timer.unref();
    }

    /** The folder every turn of a conversation runs in: its own, below `chats`, or the shared chat folder for one an earlier desktop made. */
    #folderOf(conversation: Conversation): string {
        const { paths } = this.#options;
        return conversation.folder === 'own' ? join(paths.chats, conversation.id) : paths.chat;
    }

    /** The Open folder button of that notice: opens the ended folder of that name, and no other path. */
    async openEndedFolder(name: unknown): Promise<void> {
        const { chatFullEnded } = this.#options.paths;
        const notAFolder = 'That is not a folder the desktop keeps.';
        if (typeof name !== 'string' || !endedNameShape.test(name)) throw new ChatRefused(notAFolder);
        const path = join(chatFullEnded, name);
        try {
            if (!lstatSync(path).isDirectory()) throw new ChatRefused(notAFolder);
        } catch {
            throw new ChatRefused(notAFolder);
        }
        await this.#options.openFolder?.(path);
    }

    /**
     * A folder a campaign or project names is added to a Claude Code turn only when its .claude holds no skills, agents
     * or commands, which the host would load from there into every turn that adds it. A folder left out is said once, by
     * its name and the reason.
     */
    #addable(folders: readonly string[], replyTo: string): string[] {
        const kept: string[] = [];
        for (const folder of folders) {
            const holds = ['skills', 'agents', 'commands'].some((name) => {
                try {
                    lstatSync(join(folder, '.claude', name));
                    return true;
                } catch (error) {
                    return (error as NodeJS.ErrnoException).code !== 'ENOENT';
                }
            });
            if (!holds) {
                kept.push(folder);
                continue;
            }
            if (this.#leftOut.has(folder)) continue;
            this.#leftOut.add(folder);
            const name = folder.split(/[\\/]/).filter(Boolean).at(-1) ?? folder;
            this.#options.emit(replyTo, {
                kind: 'notice',
                text: `Left out of this chat: the folder “${name}” has skills, subagents or commands of its own in its .claude folder, and a chat does not load those.`,
            });
        }
        return kept;
    }

    /**
     * The session a terminal may continue this chat by (`continuableOf`), with whether a turn of it runs now and the
     * folder its turns run in, where the host finds that session; null for a chat the desktop does not keep, an id of no
     * shape, or a chat with no such session.
     */
    continuable(chatId: unknown): { host: ContinuableHost; session: string; running: boolean; folder: string } | null {
        const conversation = typeof chatId === 'string' && conversationIdShape.test(chatId) ? this.#conversations.get(chatId) : undefined;
        const found = conversation === undefined ? null : continuableOf(conversation);
        return conversation === undefined || found === null ? null : { ...found, running: this.#running.has(conversation.id), folder: this.#folderOf(conversation) };
    }

    /** Whether a turn of this chat is running. */
    isRunning(chatId: string): boolean {
        const conversation = this.#conversationOf(chatId);
        return conversation !== undefined && this.#running.has(conversation.id);
    }

    /**
     * The conversations kept, newest first: one read from its record, or one whose first turn has started. One made
     * and never sent to is the window's that made it, and is not listed.
     */
    list(): ConversationSummary[] {
        return [...this.#conversations.values()]
            .filter((conversation) => this.#kept.has(conversation.id))
            .sort((a, b) => (a.updated_at < b.updated_at ? 1 : a.updated_at > b.updated_at ? -1 : a.id < b.id ? 1 : -1))
            .map((conversation) => this.#summaryOf(conversation));
    }

    /**
     * The host the chat is set to run on, from the conversations this run holds, read once at its start: the one the
     * newest conversation's last turn ran on (`lastChosenHost`); null before any turn.
     */
    chosenHost(): string | null {
        return lastChosenHost([...this.#conversations.values()]);
    }

    #summaryOf(conversation: Conversation): ConversationSummary {
        return {
            id: conversation.id,
            campaignId: conversation.campaign_id,
            ...(conversation.main === true ? { main: true as const } : {}),
            title: conversation.title,
            updatedAt: conversation.updated_at,
            running: this.#running.has(conversation.id),
            mode: this.#modeOf(conversation),
            ...(conversation.folder === 'own' ? { ownFolder: true as const } : {}),
        };
    }

    #fullAccessOffered(): boolean {
        return this.#options.fullAccessOffered ?? fullAccessOffered;
    }

    /**
     * A conversation's mode as the desktop holds it now, kept or not, by its id; null for an id it does not hold. A read
     * alone: unlike `open`, it says nothing a conversation waits to be told, and it marks nothing.
     */
    modeNow(id: unknown): ChatMode | null {
        const conversation = typeof id === 'string' ? this.#conversations.get(id) : undefined;
        return conversation === undefined ? null : this.#modeOf(conversation);
    }

    #modeOf(conversation: Conversation): ChatMode {
        return conversation.mode ?? 'full_access';
    }

    /**
     * A person's choice of a conversation's mode, from the menu beside the composer, on the host the page will send to,
     * kept on its record. Full access is given with no confirmation, Codex's with no floor, and refused where it cannot
     * apply: while this version does not offer it, on a host that does not drive the chat, and on a Claude Code older than
     * its floor or whose version cannot be read, read now as a turn reads it. Auto-edit is refused on Codex and on a host that does not drive the chat. A change to a
     * narrower mode stops a turn that runs in a wider one.
     */
    async setMode(id: unknown, mode: unknown, host?: unknown): Promise<ConversationSummary> {
        const conversation = this.#own(id);
        // Every choice counts, whatever it is: the latest one the person made is the one that stands.
        const choice = (this.#choices.get(conversation.id) ?? 0) + 1;
        this.#choices.set(conversation.id, choice);
        if (!isChatMode(mode)) throw new ChatRefused('That is not a mode of the chat.');
        const before = this.#modeOf(conversation);
        if (mode === before) return this.#summaryOf(conversation);
        if (mode === 'auto_edit') {
            const program = programOf(host);
            if (program === 'codex') throw new ChatRefused(codexAutoEdit);
            if (program !== 'claude') throw new ChatRefused(notAHost);
        }
        if (mode === 'full_access') {
            if (!this.#fullAccessOffered()) throw new ChatRefused(fullAccessNotYet);
            const program = programOf(host);
            if (program !== 'claude' && program !== 'codex') throw new ChatRefused(notAHost);
            // Codex takes Full access with no floor of it; Claude Code from its floor.
            const below = program === 'claude' ? fullAccessSentence(await this.#claudeVersion()) : null;
            if (below !== null) throw new ChatRefused(below);
            // A conversation removed or a window closed meanwhile is given nothing, and a choice made meanwhile stands, even
            // one that left the chat in the mode it was in, as does a step down the desktop took.
            if (this.#closed || this.#conversations.get(conversation.id) !== conversation || this.#choices.get(conversation.id) !== choice || this.#modeOf(conversation) !== before) {
                return this.#summaryOf(conversation);
            }
        }
        this.#changeMode(conversation, mode, true);
        return this.#summaryOf(conversation);
    }

    /**
     * Puts a conversation in a mode, keeps it on its record, and says so to its page and to the log. A change to a mode
     * narrower than the running turn's stops that turn, unless the caller stops it in its own words (`stopTurn` false).
     * Nothing enters Auto-edit here but the person's choice from the menu (`setMode`): Plan only's approval of a plan goes
     * to Ask first.
     */
    #changeMode(conversation: Conversation, to: ChatMode, stopTurn: boolean): void {
        const from = this.#modeOf(conversation);
        if (from === to) return;
        // Leaving Plan only ends its plan's card, kept so: an approved plan is never offered again, after a reload or a
        // restart, nor one the person left by the menu.
        if (from === 'plan_only') unmarkPlans(conversation.entries);
        // A record that names no mode is in Full access, so a step back up to it clears the field.
        if (to === 'full_access') delete conversation.mode;
        else conversation.mode = to;
        if (this.#kept.has(conversation.id)) this.#keep(conversation);
        this.#options.modeChanged?.(from, to);
        this.#options.emit(conversation.id, { kind: 'mode', mode: to });
        const running = this.#running.get(conversation.id);
        // A change to a narrower mode than the running turn's stops it: leaving Full access, or leaving Auto-edit for Ask
        // first or Plan only, stops the turn that runs in it, while Auto-edit to Full access lets it finish; a change to
        // Plan only stops any other, so the menu never says a mode while the running turn could still do more than it lets.
        const stops = running !== undefined && breadth[to] < breadth[running.mode];
        if (stopTurn && stops && running !== undefined) {
            running.stopped = true;
            running.process?.stop();
            running.checking?.abort();
        }
    }

    /**
     * Why Claude Code's bypass, which Full access runs in, is not available here, as this run of the desktop knows it
     * (`Bypass`), while that holds; null while it is, or once what a turn found has held its minute, when the next Full
     * access turn asks for the bypass again. The listing of the hosts draws a turn's mode by it, as the turn does.
     */
    bypassUnavailable(): Bypass | null {
        const held = this.#bypass;
        if (held === null) return null;
        if (held.until !== null && this.#now() >= held.until) {
            this.#bypass = null;
            return null;
        }
        return held.bypass;
    }

    /**
     * Claude Code's bypass is not available here, as a turn showed it: held for as long as `bypassLeadsTo` says, a minute
     * or the rest of this run, while the turns after it run in Ask first and say why once in each conversation for this
     * hold, and the page is asked to read the hosts again, now and when a minute that holds ends. A cause that still holds
     * stands, unless it holds a minute and this one the rest of the run; the log is told each cause that comes to hold, by
     * its cause and the mode Claude Code named.
     */
    #bypassGone(bypass: Bypass): void {
        // Every refusal is seen, a cause that already holds included: a start begun before the hold may have been honoured
        // within it, and the next resumed Full access start forks again.
        this.#fullAccessHonoured = false;
        const holding = this.bypassUnavailable() === null ? null : this.#bypass;
        const { held } = bypassLeadsTo(bypass);
        if (holding !== null && (holding.until === null || held === 'one_minute')) return;
        // A new hold: what each conversation was told of an earlier one is told again.
        for (const told of this.#toldAskFirst.values()) for (const kind of bypassKinds) told.delete(kind);
        if (this.#bypassEnds !== null) clearTimeout(this.#bypassEnds);
        this.#bypassEnds = null;
        if (held === 'this_run') this.#bypass = { bypass, until: null };
        else {
            const heldMs = this.#options.bypassHeldMs ?? bypassHeldMs;
            const until = this.#now() + heldMs;
            this.#bypass = { bypass, until };
            this.#bypassEnds = setTimeout(() => {
                this.#bypassEnds = null;
                if (this.#bypass?.until === until) this.#bypass = null;
                this.#options.setupChanged?.();
            }, heldMs);
            this.#bypassEnds.unref();
        }
        this.#options.bypassGone?.(bypass.why, bypass.named);
        this.#options.setupChanged?.();
    }

    /**
     * Tells a conversation, once for each cause, why a turn runs in Ask first while it keeps another mode: a cause of Claude
     * Code's bypass once for each hold of it (`#toldAskFirst`).
     */
    #tellAskFirst(conversationId: string, chatId: string, kind: AskFirstWhy, text: string, kept: ChatMode): void {
        const told = this.#toldAskFirst.get(conversationId) ?? new Set<AskFirstWhy>();
        if (told.has(kind)) return;
        told.add(kind);
        this.#toldAskFirst.set(conversationId, told);
        this.#options.emit(chatId, { kind: 'notice', text: askFirstThisTurn(text, kept) });
    }

    /** A conversation with what the page drew; an id the desktop does not keep is refused. */
    open(id: unknown): ConversationOpened {
        const conversation = this.#own(id);
        if (this.#unshaped.delete(conversation.id)) {
            if (this.#earlier.delete(conversation.id)) queueMicrotask(() => this.#options.emit(conversation.id, { kind: 'notice', text: earlierDesktopAskFirst }));
            this.#rewrite(conversation);
        }
        const moved = this.#movedAtStart.get(conversation.id);
        if (moved !== undefined) {
            this.#movedAtStart.delete(conversation.id);
            const path = join(this.#options.paths.chatFullEnded, moved);
            queueMicrotask(() =>
                this.#options.emit(conversation.id, {
                    kind: 'notice',
                    text: `Full access has ended. The folder the AI worked in is kept for seven days and then removed: ${path}`,
                    folder: moved,
                }),
            );
        }
        return { ...this.#summaryOf(conversation), entries: structuredClone(conversation.entries), dropped: conversation.dropped, chosen: conversation.chosen === undefined ? null : { ...conversation.chosen } };
    }

    /**
     * A conversation to start in: a new set-up chat when no campaign is named, else the campaign's conversation, the
     * one it has or a new one named after it. A new conversation is in Full access, works in a folder of its own, and is
     * kept once its first turn has started.
     */
    async create(campaignId?: unknown): Promise<ConversationSummary> {
        if (campaignId === undefined || campaignId === null) return this.#summaryOf(this.#made(null, 'New chat'));
        if (typeof campaignId !== 'string' || !campaignIdShape.test(campaignId)) throw new ChatRefused("That is not an agent's id.");
        const existing = this.#ofCampaign(campaignId);
        if (existing !== undefined) return this.#summaryOf(existing);
        const read = await this.#readCampaign(campaignId, false);
        const name = read.about.campaign?.name;
        const again = this.#ofCampaign(campaignId);
        if (again !== undefined) return this.#summaryOf(again);
        return this.#summaryOf(this.#made(campaignId, name ? nameOf(name) || campaignId : campaignId));
    }

    /**
     * Removes a conversation's record, and moves its own folder, with what its turns left there, to the ended place, where
     * the sweep removes it seven days later; the host's own transcript stays the host's. A folder that cannot be moved now
     * (a terminal "Continue in" opened holds it, on Windows) stays where it is, nothing in it removed, for a later start to
     * move. One whose turn runs is refused.
     */
    remove(id: unknown): void {
        const conversation = this.#own(id);
        if (conversation.main === true) throw new ChatRefused(feedKept);
        if (this.#running.has(conversation.id)) throw new ChatRefused(stillAnswering('remove it'));
        try {
            removeConversation(this.#options.paths.conversations, conversation.id);
        } catch (error) {
            if (error instanceof ConversationUnwritten) this.#options.conversationUnwritten?.(error.code);
            throw new ChatRefused('The conversation could not be removed. Close anything that has its file open, then try again.');
        }
        this.#conversations.delete(conversation.id);
        this.#kept.delete(conversation.id);
        this.#toldAskFirst.delete(conversation.id);
        this.#toldNarrowed.delete(conversation.id);
        this.#toldCurrent.delete(conversation.id);
        this.#toldFiltered.delete(conversation.id);
        this.#choices.delete(conversation.id);
        this.#unshaped.delete(conversation.id);
        this.#earlier.delete(conversation.id);
        if (conversation.folder === 'own') endConversationFolder(this.#options.paths.chats, this.#fullFolders(), conversation.id, this.#now(), (code) => this.#options.fullFolderFailed?.('move', code));
    }

    /**
     * "Start fresh": the hosts' sessions are dropped, and the entries, the campaign and the mode kept. One whose turn runs
     * is refused.
     */
    restart(id: unknown): void {
        const conversation = this.#own(id);
        if (this.#running.has(conversation.id)) throw new ChatRefused(stillAnswering('start it fresh'));
        conversation.sessions = {};
        delete conversation.plain_sessions;
        // A fresh session is given the memory files again.
        delete conversation.memory_given;
        // A plan the host no longer remembers is no card to approve.
        unmarkPlans(conversation.entries);
        // One never sent to has no session to drop, and no record to write.
        if (this.#kept.has(conversation.id)) this.#keep(conversation);
    }

    /**
     * A person's name for a conversation, a set-up chat's or a campaign's: one line, with nothing unseen, within the
     * title's cap. It is kept with the mark that a person gave it, and nothing replaces it afterwards: not a hand-over,
     * not the campaign's name, not a name the desktop asks a model for. The campaign's own name in the runtime is not
     * changed. Refused while nothing is kept of the conversation, and for a name with nothing that shows. A turn that
     * runs goes on; its end keeps the name.
     */
    rename(id: unknown, title: unknown): ConversationSummary {
        const conversation = this.#own(id);
        if (conversation.main === true) throw new ChatRefused(feedKept);
        if (!this.#kept.has(conversation.id)) throw new ChatRefused('Send this chat a message first; then it can be renamed.');
        if (typeof title === 'string' && title.length > renameLimit) throw new ChatRefused('That name is too long. Keep it to a short line.');
        const name = givenTitle(title);
        if (name === '') throw new ChatRefused('A name needs at least one character that shows.');
        conversation.title = name;
        conversation.named_by_person = true;
        this.#keep(conversation);
        this.#options.emit(conversation.id, { kind: 'renamed', title: name });
        return this.#summaryOf(conversation);
    }

    /**
     * The page could not draw a card the AI asked for in a conversation this desktop holds: the log hears its kind and
     * why, each held to its closed list, and nothing else of it. Anything outside those lists, and a conversation the
     * desktop does not hold, is said nowhere; the call answers nothing either way, and refuses nothing.
     */
    cardRefused(chatId: unknown, kind: unknown, failure: unknown): null {
        if (typeof kind !== 'string' || !refusedCardKinds.has(kind)) return null;
        if (typeof failure !== 'string' || !cardFailures.has(failure)) return null;
        if (typeof chatId !== 'string' || this.#conversationOf(chatId) === undefined) return null;
        this.#options.cardRefused?.(kind, failure);
        return null;
    }

    /** Resolves once no naming run of a set-up chat runs or waits, and the Feed's catch-up has put its lines in. */
    idle(): Promise<void> {
        return Promise.all([this.#naming, this.#feedRead]).then(() => undefined);
    }

    /**
     * The Feed, made and written on the first ask, then listed with the others. Each of `lines` is made a Feed line
     * (`lineOf`) or left out, a journal line it took already is left out, and the rest go at its end (`take`), each said
     * as a `line` event. Nothing else happens: no turn starts, in any mode, and a turn that runs goes on; the lines reach
     * the agent with the person's next message. An ask that carries `catchUp`, which the page makes at each load and when
     * its stream started afresh, also reads back two pages of the lines written since a few seconds before the newest kept
     * one (`jason journal list`: the Feed's kinds, and the succeeded work apart), when the Feed kept one and no read runs already; those go in before the lines handed
     * while it reads, which are checked at once and wait, the newest two hundred of them, with a gap for the rest. A page
     * that was not all of them leaves a gap before them too.
     */
    feed(lines: unknown, options?: unknown): ConversationSummary {
        if (!Array.isArray(lines) || lines.length > feedBatch) throw new ChatRefused(notAFeedBatch);
        const feed = this.#feedConversation();
        const catchUp = options !== null && typeof options === 'object' && (options as { catchUp?: unknown }).catchUp === true;
        if (catchUp && this.#feedWaiting === null) {
            const newest = feed.entries.reduce<string | null>((found, entry) => (entry.kind === 'line' && (found === null || entry.at > found) ? entry.at : found), null);
            if (newest !== null) {
                this.#feedWaiting = [];
                this.#feedOverflow = false;
                this.#feedRead = this.#catchUp(feed, new Date(Date.parse(newest) - catchUpLookbackMs).toISOString());
            }
        }
        const checked = lines.map(lineOf).filter((line): line is LineEntry => line !== null);
        if (this.#feedWaiting === null) this.#takeLines(feed, checked, false);
        else {
            this.#feedWaiting.push(...checked);
            const over = this.#feedWaiting.length - feedWaitingLimit;
            if (over > 0) {
                this.#feedWaiting.splice(0, over);
                this.#feedOverflow = true;
            }
        }
        return this.#summaryOf(feed);
    }

    /** The Feed, as read, or made now and written at once. */
    #feedConversation(): Conversation {
        const known = this.#feedId === null ? undefined : this.#conversations.get(this.#feedId);
        if (known !== undefined) return known;
        const feed = this.#made(null, feedTitle);
        feed.main = true;
        // The Feed starts in the mode every chat starts in, named once (`feedDefaultMode`).
        if (feedDefaultMode !== 'full_access') feed.mode = feedDefaultMode;
        this.#feedId = feed.id;
        this.#kept.add(feed.id);
        this.#keep(feed);
        return feed;
    }

    /**
     * The one Feed among the records read: the oldest marked main. Any other so marked is read as a set-up chat, its
     * lines and gaps let go and counted, and is written so when it is next kept.
     */
    #settleFeed(): void {
        const marked = [...this.#conversations.values()]
            .filter((each) => each.main === true)
            .sort((a, b) => (a.created_at < b.created_at ? -1 : a.created_at > b.created_at ? 1 : a.id < b.id ? -1 : 1));
        const [feed, ...others] = marked;
        for (const other of others) {
            delete other.main;
            const kept = other.entries.filter((entry) => entry.kind !== 'line' && entry.kind !== 'gap');
            other.dropped += other.entries.length - kept.length;
            other.entries = kept;
        }
        if (feed === undefined) return;
        this.#feedId = feed.id;
        for (const id of feed.taken ?? []) this.#remember(id);
        for (const entry of feed.entries) if (entry.kind === 'line') this.#remember(entry.journal_id);
    }

    #remember(id: string): void {
        this.#feedSeen.delete(id);
        this.#feedSeen.add(id);
        if (this.#feedSeen.size > takenLimit) {
            const oldest = this.#feedSeen.values().next();
            if (!oldest.done) this.#feedSeen.delete(oldest.value);
        }
    }

    /** Puts the lines new to the Feed at its end, says each, and keeps the record, with the ids taken, when any went in. */
    #takeLines(feed: Conversation, lines: readonly LineEntry[], gap: boolean): void {
        let changed = false;
        if (gap) {
            feed.entries.push({ kind: 'gap' });
            this.#options.emit(feed.id, { kind: 'gap' });
            changed = true;
        }
        for (const line of lines) {
            if (this.#feedSeen.has(line.journal_id)) continue;
            this.#remember(line.journal_id);
            const taken = take(feed.entries, line);
            this.#options.emit(feed.id, { kind: 'line', line: { ...taken.line }, folded: taken.folded });
            changed = true;
        }
        if (!changed) return;
        feed.dropped += trimmed(feed.entries);
        feed.taken = [...this.#feedSeen];
        this.#keep(feed);
    }

    /**
     * Reads back the lines written since `since`, a few seconds before the newest kept line's moment, at or after which
     * the runtime answers (the lines already taken among them, left out as such): one page, newest first, put in oldest
     * first, within its own time limit; beside it, in the same time, one page of the succeeded work since then, of which
     * the sends and the listed roles' work go in among the rest by their ids. Then the lines handed meanwhile go in, after
     * a gap when some of them were let go. A failed or timed out read of the Feed's kinds puts nothing in, the succeeded
     * work with it, and leaves no gap of its own, so the next catch-up reads the same span again; a failed read of the
     * succeeded work leaves only its sends and role work out of this catch-up.
     */
    async #catchUp(feed: Conversation, since: string): Promise<void> {
        try {
            const limitMs = this.#options.feedReadLimitMs ?? feedReadLimitMs;
            const args = ['journal', 'list', ...feedKinds.flatMap((kind) => ['--kind', kind]), '--since', since, '--limit', String(catchUpLimit)];
            // A second request, for succeeded work alone and one page of its own, so that a campaign's reads, which succeed
            // every cadence, cannot crowd the other kinds out of the first. The journal cannot filter by operation or
            // role, so the desktop does, in `lineOf`: of the newest `catchUpLimit` succeeded items since `since`, those
            // that are a send or the work of a listed role are kept. Sends older than that page are not caught up, and
            // no gap is said for them: a gap on every catch-up of a busy campaign would say nothing.
            const [answer, succeeded] = await Promise.all([
                this.#readJson(args, limitMs),
                this.#readJson(['journal', 'list', '--kind', succeededKind, '--since', since, '--limit', String(catchUpLimit)], limitMs),
            ]);
            const more = answer !== null && typeof answer.next_cursor === 'string' && answer.next_cursor !== '';
            // Each line read is made a Feed line by the same check as a line the page hands, or left out.
            const lines = (page: Record<string, unknown> | null): LineEntry[] =>
                page !== null && Array.isArray(page.items) ? (page.items as unknown[]).map(lineOf).filter((line): line is LineEntry => line !== null) : [];
            // The gate is the first read's alone: the succeeded page goes in beneath it, never by itself, or the Feed's newest
            // moment would move past a span of the other kinds that was not read, with no gap said for it.
            if (answer !== null && Array.isArray(answer.items)) {
                // Newest first within each page; merged by journal id (a ULID sorts by moment) and put in oldest first.
                const read = [...lines(answer), ...lines(succeeded)].sort((a, b) => (a.journal_id < b.journal_id ? 1 : a.journal_id > b.journal_id ? -1 : 0));
                this.#takeLines(feed, read.reverse(), more);
            }
        } catch {
            // Nothing read is nothing put in.
        } finally {
            const waiting = this.#feedWaiting ?? [];
            const overflow = this.#feedOverflow;
            this.#feedWaiting = null;
            this.#feedOverflow = false;
            this.#takeLines(feed, waiting, overflow);
        }
    }

    /**
     * Makes a set-up chat the conversation of a campaign the person picked from the ones its last pick named. Refused for
     * a conversation already about a campaign, one whose turn runs, a campaign that has a conversation, and a campaign
     * the pick did not name.
     */
    async attach(id: unknown, campaignId: unknown): Promise<ConversationSummary> {
        const conversation = this.#own(id);
        if (typeof campaignId !== 'string' || !campaignIdShape.test(campaignId)) throw new ChatRefused("That is not an agent's id.");
        const refusal = () => {
            if (conversation.main === true) return "The Feed is about no agent. Open the agent's own conversation from the agent.";
            if (this.#running.has(conversation.id)) return stillAnswering('choose its agent');
            if (conversation.campaign_id !== null) return 'This conversation is already about an agent.';
            if (this.#ofCampaign(campaignId) !== undefined) return 'That agent has its conversation already. Open it from the agent.';
            if (!candidatesOf(conversation).includes(campaignId)) return 'That agent is not one this chat made. Open it from the agent.';
            return null;
        };
        const before = refusal();
        if (before !== null) throw new ChatRefused(before);
        const read = await this.#readCampaign(campaignId, false);
        const name = read.about.campaign?.name;
        if (name === null || name === undefined) throw new ChatRefused('That agent could not be read. Check that Jason is running, then try again.');
        const after = refusal();
        if (after !== null) throw new ChatRefused(after);
        conversation.campaign_id = campaignId;
        if (conversation.named_by_person !== true) conversation.title = nameOf(name) || campaignId;
        this.#keep(conversation);
        this.#options.emit(conversation.id, { kind: 'attached', campaignId, title: conversation.title });
        return this.#summaryOf(conversation);
    }

    /**
     * Starts a turn. It resolves once the host has started, or once the send was refused; a refusal, like every end, is
     * a `done` with an error in plain words, and no host was started: on Claude Code, one below the floor
     * (`claudeFloor`) or whose version cannot be read is refused after `claude --version` and before anything else, and
     * so is an effort the model named does not take at that version. An effort that is not one of the five words, or one
     * the model named does not take by the table or the list kept, is refused the same way, and so is a model the host
     * does not offer (`modelRefusal`). A chat id the desktop does
     * not take, or a conversation it does not keep, has no chat to say `done` to, so that send rejects with an Error the
     * page shows.
     */
    async send(chatId: unknown, text: unknown, options: unknown): Promise<void> {
        if (typeof chatId !== 'string' || !chatIdShape.test(chatId)) throw new ChatRefused('That is not a chat id the desktop takes.');
        const conversation = this.#conversationOf(chatId);
        if (conversation === undefined) throw new ChatRefused(notAConversation);
        const refuse = (error: string) => this.#options.emit(chatId, { kind: 'done', error });
        if (this.#running.has(conversation.id)) return refuse(busy);
        if (typeof text !== 'string' || text.trim() === '') return refuse('There is nothing to send.');
        if (text.length > textLimit) return refuse('That message is too long to send in one turn.');
        const given = (options ?? {}) as Partial<Record<keyof ChatSendOptions, unknown>>;
        // `/export <file name>` writes the conversation to a file and starts no turn (`chat-export.ts`), and only from
        // words the person typed in the message box, as the page says with `typed`: a card's option or field, or a page
        // that does not say the words were typed, writes nothing.
        const exported = exportName(text);
        if (exported !== null) return given.typed === true ? this.#export(conversation, chatId, exported) : refuse(exportNotTyped);
        if (given.model !== undefined && (typeof given.model !== 'string' || !modelShape.test(given.model))) return refuse('That model name is not one the desktop passes to a host.');
        if (given.effort !== undefined && !isEffort(given.effort)) return refuse('That effort is not one the desktop passes to a host.');
        // A go approved for one mode never runs in another: a mode the person chose from the menu meanwhile refuses it.
        if (given.mode !== undefined && !isChatMode(given.mode)) return refuse('That is not a mode of the chat.');
        if (given.mode !== undefined && given.mode !== this.#modeOf(conversation)) return refuse(planModeMoved);
        const resolved = this.#resolve(given.host, given.model as string | undefined, given.effort as Effort | undefined);
        if (typeof resolved === 'string') return refuse(resolved);
        const notOffered = modelRefusal(resolved.host, resolved.named, this.#options.models?.() ?? new Map());
        if (notOffered !== null) return refuse(notOffered);
        const launch = programLaunch(this.#options.env, resolved.program);
        if ('refused' in launch) return refuse(`${resolved.label} cannot be started: ${launch.refused}`);
        // A turn its conversation's mode cannot apply to runs in Ask first, and the record keeps the mode for the turns that
        // can (`turnMode`, which the status hands the page too): Full access sent from a browser tab or while this version
        // does not offer it, Full access on Claude Code where its bypass is not available, and Auto-edit on Codex. Full
        // access on a Claude Code below its floor is found once the version is read, below; on Codex Full access runs as
        // itself.
        const recorded = this.#modeOf(conversation);
        const place = { fromTab: given.fromTab === true, offered: this.#fullAccessOffered(), bypass: this.bypassUnavailable() };
        const first = turnMode(recorded, resolved.program, { ...place, claudeVersion: undefined });
        let why: { kind: AskFirstWhy; text: string } | null = first.why;

        const running: Running = { stopped: false, process: null, turnId: null, replyTo: chatId, cards: new Map(), mode: first.mode };
        this.#running.set(conversation.id, running);
        this.#kept.add(conversation.id);
        // Any turn that begins while a set-up turn runs could make the campaign that set-up turn would take for its own.
        for (const [id, each] of this.#setupTurns) if (id !== conversation.id) each.overlapped = true;
        try {
            // A start that deploys skills into the chat folder holds the gate: the turn is built after it lets go.
            if (this.#options.skillsGate?.held) await this.#options.skillsGate.wait();
            // The page marks a host below the floor; this is the guard, whatever the page asked. The chat counts as
            // answering while the version is read, so a second send is refused as busy. The version read also says
            // whether the model named takes the effort named.
            // Codex has a floor of its own, the first version that leaves the person's configuration unread.
            const hostVersion = resolved.program === 'claude' ? await this.#claudeVersion() : await this.#codexVersion();
            const version = resolved.program === 'claude' ? hostVersion : null;
            const below = resolved.program === 'claude' ? floorSentence(hostVersion) : codexFloorSentence(hostVersion);
            // Full access runs only on a Claude Code at its floor, where the hook blocks the tool when it fails and has been
            // seen to stand in front of the host's rules, and where the bypass is available: elsewhere the turn runs in Ask
            // first.
            if (below === null && running.mode === 'full_access') {
                const read = turnMode(recorded, resolved.program, { ...place, claudeVersion: version });
                if (read.why !== null) {
                    running.mode = read.mode;
                    why = read.why;
                }
            }
            // The page drew this turn's mode from the last listing of the hosts (`handedClaudeVersion`). Where the version
            // read now puts a turn on the other side of the floor of Full access from it, either way, a version that could
            // not be read included, the page is asked to read the hosts again; a turn wider than the page drew is said
            // once, below.
            const handed = resolved.program === 'claude' ? this.#options.handedClaudeVersion?.() : undefined;
            const drewFloor = handed !== undefined && turnMode(recorded, 'claude', { ...place, claudeVersion: handed }).why?.kind === 'floor';
            if (handed !== undefined && drewFloor !== (turnMode(recorded, 'claude', { ...place, claudeVersion: version }).why?.kind === 'floor')) this.#options.setupChanged?.();
            const held = below ?? (resolved.effort === undefined ? null : effortRefusal(resolved.host, resolved.named, resolved.effort, this.#options.models?.() ?? new Map(), version));
            // A stop or a window close while the version was read: its answer is dropped and nothing of the turn is built.
            if (running.stopped) {
                this.#running.delete(conversation.id);
                return this.#options.emit(chatId, { kind: 'done', error: null });
            }
            if (held !== null) {
                this.#running.delete(conversation.id);
                return refuse(held);
            }
            // Said once per conversation for each reason, before the turn's first words.
            if (why !== null) this.#tellAskFirst(conversation.id, chatId, why.kind, why.text, recorded);
            // The page drew Ask first from a listing that read Claude Code below the floor of Full access or could not read
            // its version; read now, it is at the floor, and the turn runs in Full access: the conversation is told once.
            if (drewFloor && running.mode === 'full_access' && !this.#toldCurrent.has(conversation.id)) {
                this.#toldCurrent.add(conversation.id);
                this.#options.emit(chatId, { kind: 'notice', text: fullAccessCurrent });
            }
            await this.#turn(conversation, text, resolved, launch, running, hostVersion ?? '');
        } catch (error) {
            this.#running.delete(conversation.id);
            this.#setupTurns.delete(conversation.id);
            if (error instanceof InstructionsUnwritten || error instanceof AgentFolderUnowned || error instanceof SandboxUnproven) refuse(error.message);
            else refuse(`The turn could not be started: ${error instanceof Error ? error.message : String(error)}`);
        }
    }

    /**
     * Writes the conversation's words to a new file named `name`: in the campaign's folder for a campaign's chat, in the
     * harness root otherwise, as the runtime reports them. Says where, or why not, and starts no turn.
     */
    async #export(conversation: Conversation, chatId: string, name: string): Promise<void> {
        // A window that closed meanwhile is told nothing, and nothing is written.
        const say = (event: ChatEvent) => {
            if (!this.#closed) this.#options.emit(chatId, event);
        };
        try {
            const text = exportStart(conversation, name, new Date(this.#now()));
            const campaign = conversation.campaign_id;
            const folder = campaign === null ? null : ((await this.#readCampaign(campaign, false)).about.campaign?.folder ?? null);
            const place: MemoryPlace | null = campaign === null ? await this.#rootPlace() : folder === null ? null : { level: 'campaign', folder, id: campaign };
            if (this.#closed) return;
            const said = await exportInto(place, campaign === null ? 'root' : 'campaign', name, text, () => !this.#closed);
            if (said === null) return;
            say({ kind: 'notice', text: said });
            say({ kind: 'done', error: null });
        } catch (error) {
            say({ kind: 'done', error: error instanceof ExportRefused ? error.message : `The file could not be written: ${error instanceof Error ? error.message : String(error)}` });
        }
    }

    /** Ends the chat's running turn and everything its host started; its `done` follows. */
    stop(chatId: unknown): void {
        if (typeof chatId !== 'string') return;
        const conversation = this.#conversationOf(chatId);
        const running = conversation === undefined ? undefined : this.#running.get(conversation.id);
        if (running === undefined) return;
        running.stopped = true;
        running.process?.stop();
        running.checking?.abort();
    }

    /** A person's answer to a card; anything but the asking chat's first answer to a request it holds changes nothing. */
    answer(chatId: unknown, requestId: unknown, decision: unknown): void {
        if (typeof chatId !== 'string' || typeof requestId !== 'string') return;
        const given = (decision ?? {}) as { allow?: unknown; always?: unknown };
        const allow = given.allow === true;
        const always = given.always === true;
        const conversation = this.#conversationOf(chatId);
        const running = conversation === undefined ? undefined : this.#running.get(conversation.id);
        // Only the chat the card was put to answers it (the broker's rule): another page chat of the conversation keeps nothing.
        const card = running?.replyTo === chatId ? running.cards.get(requestId) : undefined;
        if (card !== undefined && card.answer === null) card.answer = !allow ? 'deny' : always ? 'always' : 'allow';
        this.#broker.answer(chatId, requestId, { allow, always });
    }

    /**
     * The window closed: every turn is stopped, every waiting request refused, every "always" forgotten, and every
     * running turn's file removed before this returns, since the desktop may quit before a host's exit is seen. A naming
     * run that runs is ended as a stop ends a turn's host and its folder removed when it can be now (else when its host
     * has let go of it, or at the next start); one that waits never starts.
     */
    closeAll(): void {
        this.#closed = true;
        clearInterval(this.#daily);
        if (this.#bypassEnds !== null) clearTimeout(this.#bypassEnds);
        this.#bypassEnds = null;
        for (const running of [...this.#running.values()]) {
            running.stopped = true;
            running.process?.stop();
            running.checking?.abort();
            if (running.turnId !== null) removeQuietly(this.#options.paths.turns, running.turnId);
        }
        const naming = this.#namingNow;
        if (naming !== null) {
            naming.stop.abort();
            try {
                rmSync(naming.folder, { recursive: true, force: true });
            } catch {
                // The host still holds it: removed once it lets go (`runNaming`), or at the next start.
            }
        }
        this.#broker.endAll();
    }

    /** Claude Code's version, read before each turn on it and never remembered; null when it cannot be read. */
    async #claudeVersion(): Promise<string | null> {
        return printedVersion(await this.#options.programs.run('claude', ['--version'], { timeoutMs: versionLimitMs }));
    }

    /** Codex's version, read before each turn on it and never remembered; null when it cannot be read. */
    async #codexVersion(): Promise<string | null> {
        return printedCodexVersion(await this.#options.programs.run('codex', ['--version'], { timeoutMs: versionLimitMs }));
    }

    /**
     * Returns when Codex's sandbox on Windows has been seen to hold for this Codex, or the turn was stopped meanwhile;
     * throws `SandboxUnproven` with the sentence the person reads otherwise (`codex-sandbox.ts`). The check runs at the
     * first send to a Codex in this run of the desktop, never at the Connect card, since naming the elevated sandbox may
     * ask an administrator; it is started as the turn's host would be, with the turn's environment, in the turn's own
     * folder (`cwd`).
     */
    async #codexSandboxHolds(launch: { file: string; prefix: string[] }, version: string, env: Record<string, string>, running: Running, keyed: boolean, cwd: string): Promise<void> {
        const named = (wanted: string) => Object.entries(env).find(([name]) => name.toUpperCase() === wanted)?.[1] ?? '';
        const key = sandboxKey(launch, version, named('CODEX_HOME'));
        // A keyed turn runs under a Codex home of the desktop's own, which the setup command may not reach.
        const refused = keyed ? sandboxKeyedSentence : sandboxSentence;
        const known = this.#sandboxChecked.get(key);
        if (known === true) return;
        if (known === false) throw new SandboxUnproven(refused);
        const { paths } = this.#options;
        // Nothing to read is nothing proven: checked again at the next send.
        if (!existsSync(paths.runFile)) throw new SandboxUnproven(sandboxUncheckedSentence);
        if (!existsSync(paths.settings)) throw new SandboxUnproven(sandboxNoSettingsSentence);
        const check = sandboxCheck([paths.runFile, paths.settings], named('SYSTEMROOT'));
        if (check === 'path') throw new SandboxUnproven(sandboxPathSentence);
        if (check === 'root') throw new SandboxUnproven(refused);
        this.#options.emit(running.replyTo, { kind: 'step', text: checkingSandbox });
        const checking = new AbortController();
        running.checking = checking;
        let answer: 'holds' | 'reads' | 'unproven';
        let opened: readonly boolean[] | null;
        try {
            const runs = checkRuns(launch, check, { env, cwd, limitMs: this.#options.sandboxLimitMs ?? sandboxLimitMs, signal: checking.signal });
            const outside = this.#options.sandboxOutside;
            ({ answer, opened } = await runSandboxCheck(check, outside === undefined ? runs : { ...runs, cmd: () => outside(check) }, checking.signal));
        } finally {
            delete running.checking;
        }
        if (running.stopped) return;
        // The settings file, the check's second, is a fact the check reports: the verdict rests on the run file.
        const settingsOpened = opened?.[1];
        this.#options.sandboxChecked?.(answer, settingsOpened === undefined ? 'unknown' : settingsOpened ? 'read' : 'unread');
        if (answer !== 'unproven') this.#sandboxChecked.set(key, answer === 'holds');
        if (answer !== 'holds') throw new SandboxUnproven(refused);
    }

    #conversationOf(chatId: string): Conversation | undefined {
        return this.#conversations.get(chatId);
    }

    #own(id: unknown): Conversation {
        const conversation = typeof id === 'string' && conversationIdShape.test(id) ? this.#conversations.get(id) : undefined;
        if (conversation === undefined) throw new ChatRefused(notAConversation);
        return conversation;
    }

    #ofCampaign(campaignId: string): Conversation | undefined {
        for (const conversation of this.#conversations.values()) if (conversation.campaign_id === campaignId) return conversation;
        return undefined;
    }

    /** A conversation made now: in Full access, which its record names by naming no mode, and with a folder of its own. */
    #made(campaignId: string | null, title: string): Conversation {
        const now = new Date().toISOString();
        const conversation: Conversation = { id: newConversationId(), campaign_id: campaignId, title, created_at: now, updated_at: now, sessions: {}, entries: [], dropped: 0, folder: 'own' };
        this.#conversations.set(conversation.id, conversation);
        return conversation;
    }

    /**
     * At a turn's end: every earlier plan's mark is taken off, and the turn's last words are marked its plan when the turn
     * ran in Plan only and ended well, not stopped, the chat is in Plan only still, it is not the Feed, and those words are
     * the conversation's last entry. The Feed has no plan card: its turns carry lines others wrote. Keeping the record
     * takes the mark off again where it cuts the words to fit.
     */
    #markPlan(conversation: Conversation, running: Running, endedWell: boolean): void {
        unmarkPlans(conversation.entries);
        if (!endedWell || running.stopped || running.mode !== 'plan_only' || conversation.main === true) return;
        if (this.#modeOf(conversation) !== 'plan_only') return;
        const last = conversation.entries.at(-1);
        if (last?.kind === 'text') last.plan = true;
    }

    /** Writes the record within its bound; one that cannot be written is said, and the conversation goes on in memory. */
    #keep(conversation: Conversation): void {
        const within = bounded(conversation);
        conversation.entries = within.entries;
        conversation.dropped = within.dropped;
        conversation.updated_at = new Date().toISOString();
        // The first record this desktop writes, where the start found none, says first that the records carry their shape.
        if (this.#stampAtFirstWrite && !this.#stamped) this.#stamp();
        try {
            this.#write(conversation);
        } catch (error) {
            this.#options.conversationUnwritten?.(error instanceof ConversationUnwritten ? error.code : 'unknown');
        }
    }

    /** A card the broker put on the page, or took off it, kept in its conversation's record. */
    #cardSaid(chatId: string, event: BrokerEvent): void {
        const conversation = this.#conversationOf(chatId);
        const running = conversation === undefined ? undefined : this.#running.get(conversation.id);
        // What the broker tells another page chat of the conversation (a card it may not answer is gone for it) is not the card's.
        if (conversation === undefined || running === undefined || running.replyTo !== chatId) return;
        if (event.kind === 'permission') {
            const card: Extract<KeptEntry, { kind: 'permission' }> = { kind: 'permission', title: event.title, detail: event.detail, answer: null, ...(event.why === undefined ? {} : { why: event.why }) };
            conversation.entries.push(card);
            running.cards.set(event.request, card);
        } else {
            const card = running.cards.get(event.request);
            if (card !== undefined && card.answer === null) card.answer = 'gone';
            running.cards.delete(event.request);
        }
    }

    #resolve(host: unknown, given: string | undefined, effort: Effort | undefined): Resolved | string {
        const provider = keyProvider(host);
        if (provider !== undefined) {
            const key = this.#options.keyFor(provider.id);
            if (!key) return `No key is stored for ${provider.label}. Add it on the Connect card, then send again.`;
            const folder = keyedFolder(this.#options.paths, provider);
            const model = currentModel(provider.id, given);
            const provided = providerEnvironment(provider, key, model ?? '', folder);
            return {
                host: provider.id,
                label: provider.label,
                program: provider.program,
                provider: provided,
                keyed: true,
                model: provider.baseUrl ? undefined : model,
                named: model,
                effort,
                folder,
                checksKeySource: provider.keyVariable === 'ANTHROPIC_API_KEY',
                key,
            };
        }
        const known = hosts.find((each) => each.id === host);
        if (known === undefined) return notAHost;
        if (!known.drivesChat || (known.program !== 'claude' && known.program !== 'codex')) {
            return `${known.label} runs campaign work, not the chat yet. Choose Claude Code or Codex for the chat.`;
        }
        const model = currentModel(known.id, given);
        return { host: known.id, label: known.label, program: known.program, provider: {}, keyed: false, model, named: model, effort, folder: null, checksKeySource: false, key: null };
    }

    async #readJson(args: string[], timeoutMs?: number): Promise<Record<string, unknown> | null> {
        const answer = await this.#options.cli.run(args, timeoutMs === undefined ? undefined : { timeoutMs });
        return answer.exit === 0 ? objectOf(answer.stdout) : null;
    }

    /**
     * The campaign as the runtime answers it: its name and status for the header, its folder and its project's, as
     * the runtime recorded them and only when they are on this disk, for a Claude Code turn to add.
     */
    async #readCampaign(campaignId: string, withProject: boolean, withMemory = false): Promise<CampaignRead> {
        const campaign = await this.#readJson(['campaign', 'get', campaignId]);
        if (campaign === null) return { about: { campaign: { id: campaignId, name: null, status: null, folder: null } }, added: [], memory: [] };
        const own = folderThere(campaign.folder_path);
        const added: string[] = own === null ? [] : [own];
        // A turn reads the project on every host, for its memory file; its folder is added to a Claude Code turn alone.
        const project = typeof campaign.project_id === 'string' && /^prj_[0-9A-Za-z]{1,64}$/.test(campaign.project_id) ? campaign.project_id : null;
        const projectRead = project === null || !(withProject || withMemory) ? null : await this.#readJson(['project', 'get', project]);
        const theirs = folderThere(projectRead?.folder_path);
        if (withProject && theirs !== null && !added.includes(theirs)) added.push(theirs);
        const memory: MemoryPlace[] = [];
        if (project !== null && typeof projectRead?.folder_path === 'string') memory.push({ level: 'project', folder: projectRead.folder_path, id: project });
        if (withMemory && typeof campaign.folder_path === 'string') memory.push({ level: 'campaign', folder: campaign.folder_path, id: campaignId });
        const name = typeof campaign.name === 'string' ? campaign.name : '';
        const status = typeof campaign.status === 'string' ? campaign.status : null;
        return { about: { campaign: { id: campaignId, name, status, folder: own } }, added: withProject ? added : [], memory };
    }

    /**
     * The harness root as the runtime reports it, when it reports it marked: where the client's memory file is. Never a
     * root worked out here; null when the runtime does not answer, names none or names one that is not marked.
     */
    async #rootPlace(): Promise<MemoryPlace | null> {
        const status = await this.#readJson(['runtime', 'status']);
        const harness = status?.harness;
        if (harness === null || typeof harness !== 'object' || Array.isArray(harness)) return null;
        const { root, root_state: state } = harness as Record<string, unknown>;
        return typeof root === 'string' && state === 'marked' ? { level: 'client', folder: root } : null;
    }

    /**
     * The person's memory files that apply to a turn, in the order of the levels: the client's for every chat, and the
     * project's and the campaign's for a campaign's. A level with no read is left out. A file found absent that the
     * session was given (`given`) is looked at once more, a moment later, so an editor's save that leaves the name bare
     * for an instant is not said to be gone.
     */
    async #memoryFiles(places: readonly MemoryPlace[], given: GivenDigests | null): Promise<MemoryRead[]> {
        const root = await this.#rootPlace();
        const limit = this.#options.memoryReadLimitMs ?? memoryReadLimitMs;
        const reader = this.#options.memoryReader ?? readMemoryFile;
        const all = [...(root === null ? [] : [root]), ...places];
        const read = await Promise.all(all.map((place) => readMemoryWithin(place, limit, this.#platform, reader)));
        if (read.some((each) => each?.kind === 'absent' && given?.[each.level] !== undefined)) {
            await new Promise((resolve) => setTimeout(resolve, memorySecondLookMs));
            for (const [i, each] of read.entries()) {
                if (each?.kind === 'absent' && given?.[each.level] !== undefined) read[i] = await readMemoryWithin(all[i]!, limit, this.#platform, reader);
            }
        }
        return read.filter((each): each is MemoryRead => each !== null);
    }

    /** Every campaign of the installation, archived ones included, read page by page; null when any page was not. */
    async #listCampaigns(): Promise<Listed | null> {
        const found: Listed = new Map();
        let cursor: string | null = null;
        for (let page = 0; page < listPages; page++) {
            const args = ['campaign', 'list', '--all-projects', '--include-archived', '--limit', String(listPage)];
            if (cursor !== null) args.push('--cursor', cursor);
            const answer = await this.#readJson(args);
            if (answer === null || !Array.isArray(answer.items)) return null;
            for (const item of answer.items as unknown[]) {
                const each = (item ?? {}) as Record<string, unknown>;
                const created = typeof each.created_at === 'string' ? Date.parse(each.created_at) : Number.NaN;
                if (typeof each.id !== 'string' || !campaignIdShape.test(each.id) || Number.isNaN(created)) return null;
                found.set(each.id, { created, name: typeof each.name === 'string' ? each.name : '' });
            }
            if (answer.next_cursor === null || answer.next_cursor === undefined) return found;
            if (typeof answer.next_cursor !== 'string') return null;
            cursor = answer.next_cursor;
        }
        return null;
    }

    /**
     * At the end of a set-up chat's turn: the campaign the turn made, when it made exactly one and no other turn, of any
     * conversation, ran beside it; otherwise the campaigns it made, for the person to pick from. A campaign counts when it was absent before the turn, is present after it, was created at or after
     * the turn's start, and has no conversation of its own. Every page is read, or nothing is handed over.
     */
    async #handOver(conversation: Conversation, before: Listed | null, since: number): Promise<HandOver | null> {
        const after = before === null ? null : await this.#listCampaigns();
        const overlapped = this.#setupTurns.get(conversation.id)?.overlapped ?? true;
        if (before === null || after === null) {
            this.#options.setupEnded?.(null, false);
            return null;
        }
        const taken = new Set([...this.#conversations.values()].map((each) => each.campaign_id));
        const made = [...after].filter(([id, campaign]) => !before.has(id) && campaign.created >= since && !taken.has(id));
        const only = made.length === 1 ? made[0] : undefined;
        if (only === undefined || overlapped || conversation.campaign_id !== null) {
            this.#options.setupEnded?.(made.length, false);
            // More than one, or a turn that ran beside another: the person picks from what this turn made.
            return made.length === 0 || conversation.campaign_id !== null ? null : { kind: 'pick', campaignIds: made.map(([id]) => id).sort() };
        }
        const [campaignId, campaign] = only;
        conversation.campaign_id = campaignId;
        if (conversation.named_by_person !== true) conversation.title = nameOf(campaign.name) || campaignId;
        this.#options.setupEnded?.(1, true);
        return { kind: 'attached', campaignId, title: conversation.title };
    }

    /**
     * A set-up turn begins: it ran beside another when any other turn runs, of any conversation; a turn that begins
     * later marks it too (`send`). A campaign a person makes by hand meanwhile cannot be told apart this way.
     */
    #setupBegun(id: string): void {
        const beside = [...this.#running.keys()].some((each) => each !== id);
        this.#setupTurns.set(id, { overlapped: beside });
    }

    /**
     * Writes the instruction text into the chat folder where a memory file does not hold it. With no text in the
     * application, nothing is written and the gap is said once; a file that cannot be written is said and thrown, and
     * the turn it was written for is refused in the desktop's words.
     */
    #placeInstructions(fence: ChatFence, folder: string = this.#options.paths.chat): void {
        if (fence.instructions === null) {
            if (!this.#missingSaid) this.#options.instructionsMissing?.();
            this.#missingSaid = true;
            return;
        }
        try {
            placeInstructions(folder, fence.instructions);
        } catch (error) {
            if (error instanceof InstructionsUnwritten) this.#options.instructionsUnwritten?.(error.file, error.code);
            throw error;
        }
    }

    async #turn(conversation: Conversation, text: string, resolved: Resolved, launch: { file: string; prefix: string[] }, running: Running, version: string): Promise<void> {
        const { paths } = this.#options;
        const replyTo = running.replyTo;
        const main = conversation.main === true;
        const setup = conversation.campaign_id === null && !main;
        let about: About = main ? { main: true, campaign: null } : { campaign: null };
        let added: string[] = [];
        let places: MemoryPlace[] = [];
        let before: Listed | null = null;
        let since = 0;
        if (conversation.campaign_id !== null) {
            const read = await this.#readCampaign(conversation.campaign_id, resolved.program === 'claude', true);
            about = read.about;
            added = this.#addable(read.added, replyTo);
            places = read.memory;
            const name = read.about.campaign?.name;
            if (name && conversation.named_by_person !== true) conversation.title = nameOf(name) || conversation.title;
        } else if (setup) {
            since = Date.now();
            this.#setupBegun(conversation.id);
            before = await this.#listCampaigns();
        }
        // A turn of the Feed is given the lines no turn was given yet, after the header, set apart as records. They are
        // marked given once its host ran (`markGiven`); a line that lands from here on, or folds into one of them, waits for
        // the next turn, whatever its journal id.
        const given = main ? linesWaiting(conversation.entries) : null;
        const block = given === null ? null : feedBlock(given.lines, given.more);
        // The recap is of what came before this turn; the person's words of this turn follow it.
        const earlier = recap(conversation.entries);
        const resume = conversation.sessions[resolved.host];
        // The person's memory files follow the header: all of them to a session that is new, and to one this conversation
        // resumes only those that changed since it was given them, a level really absent now, and one that did not answer
        // in time. A level not read this turn is kept by no record, so the next turn that reads it gives it again.
        const memory = await this.#memoryFiles(places, memoryGivenTo(conversation, resolved.host, resume));
        const held = digestsOf(memory);
        const giving = (resumed: string | undefined) => memoryToGive(memory, memoryGivenTo(conversation, resolved.host, resumed));
        const headFor = (resumed: string | undefined): string =>
            [header(about, this.#options.account), memoryBlock(giving(resumed)), block].filter((part): part is string => part !== null).join('\n\n');
        // Every turn of a conversation runs in its folder, in every mode and on every host: its own, or the shared chat
        // folder for one an earlier desktop made.
        const own = conversation.folder === 'own';
        const folder = this.#folderOf(conversation);
        const hostChanged = resume === undefined && Object.keys(conversation.sessions).length > 0;
        mkdirSync(paths.chat, { recursive: true });

        let turnId: string | null = null;
        /** A Claude Code turn: the folder its judgement reads a `~` against, which a Full access turn names to its host as HOME. */
        let turnHome: string | undefined;
        /**
         * Whether the turn's hook rides it: in Full access, and in Ask first and Auto-edit on a Claude Code at the floor of
         * Full access; never in Plan only, whose list of verbs takes no request on standard input.
         */
        let hooked = false;
        /** Writes the turn's settings again for Ask first, once Claude Code refused its bypass before it started. */
        let narrowSettings: ((mode: 'ask_first') => void) | null = null;
        let endpoint: Endpoint | null = null;
        let argsFor: (resume: string | undefined, fork: boolean) => string[];
        let requests = 0;
        /** Whether the log was told that a tool of this turn ran or was refused unseen by its hook (`hookFiltered`). */
        let filteredLogged = false;
        // A start's environment is built as it starts, from the mode it starts in, as its command line is (`argsFor`), never
        // from the conversation's record: a Full access start carries no scrub (`hostEnvironment`), and a start in Ask first,
        // the one a turn is narrowed to before it starts or started again in after Claude Code's bypass was found not
        // available, carries Ask first's. The settings of a turn the hook rides set the same values (`pinnedEnvironment`),
        // written for the mode the start runs in as its environment is.
        const environmentOptions = () => ({
            platform: this.#platform,
            bundle: this.#options.bundle,
            data: this.#options.dataNamed ? paths.data : null,
            program: resolved.program,
            keyed: resolved.keyed,
            provider: resolved.provider,
            bundleVersion: this.#options.bundleVersion,
            effortNamed: resolved.effort !== undefined,
            mode: running.mode,
            ...(running.mode === 'full_access' && turnHome !== undefined ? { home: turnHome } : {}),
        });
        const environmentNow = () => hostEnvironment(this.#options.env, environmentOptions());
        const pinsNow = () => pinnedEnvironment(environmentNow(), environmentOptions());
        // Closes what the turn opened. The file's removal runs whatever came before it, and a removal that fails (a scanner
        // holding the file on Windows) leaves the file for the next start to remove.
        const finishFiles = async () => {
            try {
                if (turnId !== null) this.#broker.endTurn(turnId);
                await endpoint?.close();
            } finally {
                if (turnId !== null) removeQuietly(paths.turns, turnId);
            }
        };
        try {
            // Outside Plan only a turn refuses nothing of the deny list (`modeFence`).
            const fence = modeFence(this.#options.fence(), running.mode);
            // First of all a turn does: the chat folder is found to be the desktop's own, and its memory files written,
            // before the turn's file, its endpoint or the host that starts in that folder.
            this.#placeInstructions(fence);
            // The chat folder's agent settings hold only what the desktop placed (`chat-agent-folder.ts`), and so does the
            // top of a conversation's own folder, which gets the memory files and the recorded skills too.
            ownAgentFolder(paths.chat, paths.skillsRecords);
            if (own) {
                ownConversationFolder(paths.chats, folder, paths.chat, paths.skillsRecords);
                this.#placeInstructions(fence, folder);
            }
            if (resolved.folder !== null) makePrivate(resolved.label, resolved.program === 'claude' ? paths.claudeKeyed : paths.codexKeyed, resolved.folder);
            if (resolved.program === 'claude') {
                turnHome = Object.entries(this.#options.env).find(([name, value]) => (this.#platform === 'win32' ? name.toUpperCase() : name) === 'HOME' && value !== undefined && value !== '')?.[1] ?? homedir();
                const id = newTurnId();
                turnId = id;
                running.turnId = id;
                // A hook rides the turn in Full access, and in Ask first and Auto-edit from Claude Code's floor of Full
                // access on, and the permission model then judges by what the hook refuses (`ChatScope.hooked`).
                const hookedMode = running.mode === 'plan_only' ? null : running.mode;
                hooked = hookedMode !== null && (hookedMode === 'full_access' || fullAccessSentence(version) === null);
                const scope: ChatScope = {
                    platform: this.#platform,
                    cwd: folder,
                    chat: folder,
                    added,
                    data: paths.data,
                    home: homedir(),
                    // The host's environment is built from this one (`hostEnvironment`), so its shell's `~` is this HOME;
                    // a Full access turn names it to the host explicitly (`turnHome`), so the two cannot differ.
                    shellHome: turnHome,
                    // Every layout's settings files and the run file, whatever the stamp says, by every route a path can take.
                    protectedFiles: [...settingsFilesOf(paths.data), paths.runFile],
                    // The names a network path may give this computer by, beside localhost and the loopback addresses.
                    machine: [hostname()],
                    denied: fence.commands,
                    deniedOperations: fence.operations,
                    help: helpNouns(fence.allowed, fence.reads),
                    // The host's allow rules of Ask first and Auto-edit, whose plain forms the model runs with no card where
                    // the host is given no rule for them (`withoutRequestFiles`).
                    ...(running.mode === 'ask_first' || running.mode === 'auto_edit' ? { allowed: fence.allowed } : {}),
                    hooked,
                    real: realPathOf,
                    entry: entryOf,
                    text: textOf,
                    identity: identityOf,
                };
                this.#broker.openTurn(replyTo, id, scope, running.mode);
                const secret = newSecret();
                endpoint = await openEndpoint({
                    secret,
                    decide: (tool, input, signal) => {
                        requests++;
                        return this.#broker.ask(id, tool, input, signal);
                    },
                    ...(hooked ? { hook: (tool: string, input: Record<string, unknown>, use: string | null) => this.#broker.hook(id, tool, input, use) } : {}),
                });
                // The prompt tool's server is named for this turn alone, so no server of the person's own, which a turn
                // in Full access loads beside it, is ever the one the host asks (`turnServerName`).
                const server = newServerName();
                const mcpConfig = writeTurnConfig(paths.turns, id, endpoint.url, secret, server);
                const hookUrl = endpoint.hookUrl;
                let settingsFile = hooked && hookedMode !== null ? writeTurnSettings(paths.turns, id, hookUrl, secret, hookedMode, { env: pinsNow(), keyed: resolved.keyed }) : undefined;
                narrowSettings = (mode) => {
                    rmSync(turnSettingsPath(paths.turns, id), { force: true });
                    settingsFile = writeTurnSettings(paths.turns, id, hookUrl, secret, mode, { env: pinsNow(), keyed: resolved.keyed });
                };
                // The host's data rules name the conversations' folders present at this turn's start; one made later is
                // refused by the permission model's data rule alone, since a deny wins over an allow and no rule says "all of
                // chats but mine".
                const data = dataRules(this.#platform, paths.data, entriesOf(paths.data), entriesOf(paths.desktop), own ? { id: conversation.id, present: entriesOf(paths.chats) } : undefined);
                // In Full access the turn's folder is its own, its instructions and agent folders included, the shared chat
                // folder's as a conversation's own: before the next turn the desktop writes its instructions there afresh and
                // leaves its agent folders holding only what it placed (`ownAgentFolder`). Every other mode keeps the rules
                // over them, and so does a turn narrowed to Ask first.
                const rulesNow = () => [...data, ...(running.mode === 'full_access' ? [] : chatRules(this.#platform, folder))];
                argsFor = (resumed, fork) =>
                    claudeArguments({ mcpConfig, server, addDirs: added, denied: fence.commands, allowed: fence.allowed, reads: fence.reads, mode: running.mode, settingsFile, dataRules: rulesNow(), model: resolved.model, effort: resolved.effort, resume: resumed, fork, version, keyed: resolved.keyed });
            } else {
                argsFor = (resumed) =>
                    codexArguments({ platform: this.#platform, mode: running.mode, model: resolved.model, effort: resolved.effort, resume: resumed, environment: environmentNow() });
            }
        } catch (error) {
            // Whatever the turn had opened is closed again: its endpoint, its broker turn and its file.
            await finishFiles();
            throw error;
        }

        if (running.stopped) {
            await finishFiles();
            this.#setupTurns.delete(conversation.id);
            this.#running.delete(conversation.id);
            this.#options.emit(replyTo, { kind: 'done', error: null });
            return;
        }

        // On Windows a read-only Codex turn runs only where its sandbox has been seen to keep a command from the runtime's run
        // file; a Full access turn runs with no sandbox, so there is nothing to check.
        if (resolved.program === 'codex' && this.#platform === 'win32' && running.mode !== 'full_access') {
            await this.#codexSandboxHolds(launch, version, environmentNow(), running, resolved.keyed, folder);
            if (running.stopped) {
                this.#setupTurns.delete(conversation.id);
                this.#running.delete(conversation.id);
                this.#options.emit(replyTo, { kind: 'done', error: null });
                return;
            }
        }
        let steps = 0;
        const begun = Date.now();
        // A set-up chat is named by the first sixty characters of its first message, until a campaign names it; after its
        // first turn ends well the desktop asks its model for a better name (`#nameLater`).
        const firstTurn = setup && !conversation.entries.some((entry) => entry.kind === 'user');
        if (firstTurn && conversation.title === 'New chat') conversation.title = titleOf(text);
        conversation.entries.push({ kind: 'user', text });
        conversation.chosen = { host: resolved.host, model: resolved.named ?? null, effort: resolved.effort ?? null };
        if (hostChanged) this.#fresh(replyTo, 'host_changed');

        /**
         * One start of the host. A resume the host answers with another session, or one that ends in a failure before
         * anything was said, falls to a fresh session with the recap: the host is started again, once. A session that
         * differs is stopped the moment its id is read, and nothing after it is shown. The record gives up the session
         * it had only once the fresh session has done something; a fresh session that also ends with nothing said
         * leaves the record's session as it was, since such a failure (an account signed out, a limit, no network) is
         * not the session's.
         *
         * What the vendors state, and all this leans on:
         * - Claude Code, https://code.claude.com/docs/en/headless: "json: structured JSON with result, session ID, and
         *   metadata", and its example resumes a captured id: `claude -p "Continue that review" --resume "$session_id"`.
         * - Claude Code, https://code.claude.com/docs/en/sessions: "If the session depended on --mcp-config, --settings,
         *   --plugin-dir, --fallback-model, or directories added with --add-dir, pass them again when you resume", so
         *   every turn passes them, resumed or not; and "If no stored session matches the ID, Claude Code reports
         *   `No conversation found with session ID: <session-id>`."
         * - Codex, https://developers.openai.com/codex/noninteractive (served as
         *   https://learn.chatgpt.com/docs/non-interactive-mode): "You can also target a specific session ID with codex
         *   exec resume <SESSION_ID>." and "Event types include thread.started, turn.started, turn.completed,
         *   turn.failed, item.*, and error."
         * The Codex page does not say what a resume of a session that is not found does, nor that the id to resume is the
         * `thread_id` of `thread.started`; so a resume that fails, is not found or answers with another id is never
         * trusted, on either host, and falls to a fresh session with the recap.
         *
         * A start that throws, before any child exists, is an attempt that could not be started: it never rejects.
         */
        const attempt = async (resumed: string | undefined, recapped: boolean, fallback: boolean, fork = false): Promise<Attempted> => {
            let result: string | null | undefined;
            let shown = 0;
            let other = false;
            // The sentence the turn ends with when the desktop stopped the host for its key; nothing it says after counts.
            let stoppedFor: string | null = null;
            // Whether the turn ended on a key the provider refused, in the desktop's own sentence.
            let keyRefused = false;
            // The fallback's session, kept aside until the fallback has done something.
            let pending: string | null = null;
            // Whether the host said any line at all: one that refused its bypass mode says none.
            let spoke = false;
            // Whether this start ran in another mode than the bypass it asked for, and was stopped at its first line.
            let bypassOff: Bypass | null = null;
            const seen = new Set<string>();
            // Whether the host said anything on its standard output: a Codex that refuses to start says no event.
            let heard = false;
            // The tools this start used, by the id Claude Code gave each use.
            const uses = new Map<string, string>();
            let host: HostProcess | null = null;
            // Beside the session the record keeps whether this turn was plain: the person's own login, in any mode. Only
            // such a session is one a terminal may continue (`continuableOf`).
            // The session this start was given the memory files in holds, from then on, the files read for this turn.
            const name = (session: string) => {
                const named = nameSession(conversation, resolved.host, session, !resolved.keyed);
                if (noteMemoryGiven(conversation, resolved.host, session, held) || named) this.#keep(conversation);
            };
            const stopFor = (sentence: string) => {
                stoppedFor = sentence;
                result = sentence;
                host?.stop();
            };
            const said = (line: HostLine) => {
                spoke = true;
                if (other || stoppedFor !== null || bypassOff !== null) return;
                if (line.kind === 'permission_mode') {
                    // A turn that asked for Claude Code's bypass names another mode on its init line, read first: Claude Code
                    // did not start in its bypass here. What is kept is the mode it named, as a word of the closed table
                    // (`namedMode`); why is read once the start has ended, from Claude Code's own words that it forced its
                    // default mode alone (`forcedDefault`). What that leads to is decided in one place (`bypassLeadsTo`):
                    // this start is stopped there, what it still asks judged and carded as that mode's and nothing more it
                    // says read, and the turn is started again in that mode, as one whose bypass was refused is: with that
                    // mode's own tools, settings, rules and environment. The record keeps Full access.
                    if (running.mode !== 'full_access' || line.mode === 'bypassPermissions') return;
                    bypassOff = { why: 'bypass_off', named: namedMode(line.mode) };
                    running.mode = bypassLeadsTo(bypassOff).mode;
                    if (running.turnId !== null) this.#broker.narrowTurn(running.turnId, running.mode);
                    host?.stop();
                    return;
                }
                if (line.kind === 'key_source') {
                    if (!resolved.checksKeySource || line.source === 'environment') return;
                    if (line.source === 'elsewhere') stopFor(notTakenSentence(resolved.label));
                    else this.#options.keySource?.(line.source);
                    return;
                }
                if (line.kind === 'refused') {
                    // On a plain turn the host's own sign-in is the person's, and so are its retries and its words.
                    if (resolved.keyed) stopFor(line.forbidden === true ? forbiddenSentence(resolved.label) : refusedSentence(resolved.label));
                    return;
                }
                if (line.kind === 'tool_use') {
                    uses.set(line.id, line.name);
                    return;
                }
                if (line.kind === 'sandbox_narrowed') {
                    // An administrator's requirements kept a Full access turn in a sandbox: the turn goes on there, the chat
                    // keeps its mode, and the conversation is told once.
                    if (running.mode !== 'full_access' || this.#toldNarrowed.has(conversation.id)) return;
                    this.#toldNarrowed.add(conversation.id);
                    this.#options.emit(replyTo, { kind: 'notice', text: codexFullAccessNarrowed });
                    return;
                }
                if (line.kind === 'tool_result') {
                    // Every tool use of a turn the hook rides passes it first. One whose result comes back unseen by it ran,
                    // or was refused, without the hook, which a policy can filter out; EndConversation is the one tool
                    // Claude Code fires no hook for. Full access is then not available here for the rest of this run of the
                    // desktop, and the records keep their modes. A Full access turn ends there, said in its own sentence;
                    // one already stopped, by Stop or by a change of mode, ends as its stop ends it, its next turn saying
                    // why it runs in Ask first. A turn of Ask first or Auto-edit runs on, its cards and Claude Code's own
                    // rules deciding as before; the log is told once in the turn, and the conversation once. A tool this
                    // start was given taken out (`claudeRemovedToolsOf`), which the conversation still names from a turn of
                    // another mode, Claude Code refuses as one it does not have before any hook fires: an error result of
                    // such a tool ran nothing, and is no tool the hook missed. One of its results that is no error did run.
                    const tool = uses.get(line.id);
                    if (!hooked || tool === 'EndConversation') return;
                    if (running.turnId !== null && this.#broker.hooked(running.turnId, line.id)) return;
                    if (line.error === true && tool !== undefined && claudeRemovedToolsOf(running.mode).includes(tool)) return;
                    this.#bypassGone({ why: 'unguarded', named: null });
                    if (running.mode === 'full_access') {
                        this.#options.unchecked?.();
                        if (running.stopped) return;
                        const told = this.#toldAskFirst.get(conversation.id) ?? new Set<AskFirstWhy>();
                        told.add('unguarded');
                        this.#toldAskFirst.set(conversation.id, told);
                        stopFor(unguardedSentence);
                        return;
                    }
                    if (!filteredLogged) {
                        filteredLogged = true;
                        this.#options.hookFiltered?.(running.mode, claudeToolWord(tool));
                    }
                    if (this.#toldFiltered.has(conversation.id)) return;
                    this.#toldFiltered.add(conversation.id);
                    this.#options.emit(replyTo, { kind: 'notice', text: hookFilteredSentence });
                    return;
                }
                if (line.kind === 'session') {
                    // A forked resume answers with a session of its own, the fork: it is named only here, once the start
                    // was not stopped for its mode, so a refused start's fork is never recorded.
                    if (resumed !== undefined && line.id !== resumed && !fork) {
                        other = true;
                        host?.stop();
                        return;
                    }
                    // Set at the session line; a start stopped later by a tool_result its hook never saw is un-set by `#bypassGone`.
                    if (resolved.program === 'claude' && running.mode === 'full_access') this.#fullAccessHonoured = true;
                    if (fallback && shown === 0) pending = line.id;
                    else name(line.id);
                } else if (line.kind === 'result') {
                    keyRefused = resolved.keyed && line.refused === true;
                    result = !keyRefused ? line.error : line.forbidden === true ? forbiddenSentence(resolved.label) : refusedSentence(resolved.label);
                } else {
                    if (pending !== null) {
                        name(pending);
                        pending = null;
                    }
                    shown++;
                    // On a keyed turn the whole key, exactly as it is, never reaches the page or the record in what the
                    // host says while it runs; only the exact key, since an answer's long ids and hashes are its own.
                    const key = resolved.key;
                    const clean = key === null ? (text: string) => text : (text: string) => withoutWholeKey(text, key);
                    if (line.kind === 'step') {
                        steps++;
                        const step: Extract<ChatEvent, { kind: 'step' }> =
                            line.detail === undefined ? { kind: 'step', text: clean(line.text) } : { kind: 'step', text: clean(line.text), detail: clean(line.detail) };
                        conversation.entries.push({ ...step });
                        this.#options.emit(replyTo, step);
                    } else {
                        const said: Extract<ChatEvent, { kind: 'text' }> = { kind: 'text', text: clean(line.text), at: new Date(this.#now()).toISOString() };
                        conversation.entries.push({ ...said });
                        this.#options.emit(replyTo, said);
                    }
                }
            };
            let started: HostProcess;
            try {
                started = (this.#options.startHost ?? startHost)(launch, argsFor(resumed, fork), {
                    cwd: folder,
                    env: environmentNow(),
                    stdin: turnInput(headFor(resumed), recapped ? earlier : null, text),
                    onLine: (line) => {
                        heard = true;
                        for (const each of resolved.program === 'claude' ? claudeLine(line) : codexLine(line, seen)) said(each);
                    },
                });
            } catch (error) {
                // The system's code when it gave one, never its text, which may carry what the start was given.
                const code = (error as NodeJS.ErrnoException | null)?.code;
                const why = typeof code === 'string' ? code : error instanceof Error ? error.message : 'unknown';
                return { exit: { code: null, startError: why }, result: undefined, host: null, again: false };
            }
            host = started;
            running.process = started;
            this.#options.started?.(resolved.host, resumed !== undefined, running.mode);
            for (const { read, how } of giving(resumed)) this.#options.memoryGiven?.(read.level, how);
            if (running.stopped || stoppedFor !== null || bypassOff !== null) started.stop();
            const exit = await started.exited;
            // The lines this turn was given count as given once its host ran: a start the system refused gave none.
            if (exit.startError === null && given !== null) markGiven(conversation.entries, given.waiting);
            // Every tool use is said as a step before it runs, so a host that said nothing did nothing.
            const failedSilently = shown === 0 && (exit.code !== 0 || typeof result === 'string');
            // A key that was refused or not taken is the key's, not the session's: a fresh session would fail the same way.
            const refusedKey = stoppedFor !== null || keyRefused;
            // Claude Code refused its bypass mode before it started, in its restricted mode or under a policy: it said no
            // line, failed, and its standard error names the mode.
            const bypassRefused =
                resolved.program === 'claude' && running.mode === 'full_access' && !spoke && !running.stopped && exit.startError === null && exit.code !== 0 && /\bbypassPermissions\b/.test(started.stderrTail());
            const again = bypassOff === null && resumed !== undefined && !running.stopped && exit.startError === null && !refusedKey && (other || failedSilently);
            return { exit, result, host: started, again, ours: refusedKey, heard, bypassRefused, bypassOff };
        };

        /**
         * The verdict every start of the turn meets, the first and the fresh session after a resume that was lost alike: a
         * start whose bypass was refused, or was not in force, is started again at once, as `again` starts it, in the mode
         * that leads to (`bypassLeadsTo`), its settings written for it and its command line and its environment that mode's
         * own, the cause held as long as that says, and the conversation told once in the hold why: by the cause that holds,
         * which a cause this start found within it does not replace (`#bypassGone`). Any other start stands as it ended.
         */
        const unlessNarrowed = async (ended: Attempted, again: () => Promise<Attempted>): Promise<Attempted> => {
            // A start that named another mode, whose standard error, read to its end, holds Claude Code's own words that it
            // forced its default mode, is that cause; nothing else of what it wrote is read (`forcedDefault`).
            const off = ended.bypassOff ?? null;
            const named: Bypass | null = off !== null && forcedDefault(ended.host?.stderrTail() ?? '') ? { why: 'forced_default', named: off.named } : off;
            const narrowed: Bypass | null = named ?? (ended.bypassRefused === true ? { why: 'bypass_refused', named: null } : null);
            if (narrowed === null || narrowSettings === null) return ended;
            const leads = bypassLeadsTo(narrowed);
            running.mode = leads.mode;
            if (running.turnId !== null) this.#broker.narrowTurn(running.turnId, leads.mode);
            narrowSettings(leads.mode);
            this.#bypassGone(narrowed);
            const told = bypassLeadsTo(this.bypassUnavailable() ?? narrowed).why;
            this.#tellAskFirst(conversation.id, replyTo, told.kind, told.text, this.#modeOf(conversation));
            return running.stopped ? ended : again();
        };

        // A resumed Claude Code start in Full access forks while no Full access start was honoured in this run
        // (`#fullAccessHonoured`): one refused its bypass then wrote the person's message into the fork alone, so the turn
        // is started again in the session it resumed, untouched, with no recap. A resumed start that did not fork and was
        // stopped for its mode may already have written the message into the session it resumed, so it is started again
        // in a fresh session given the recap, never in that session again: the message is held once, and the record takes
        // the fresh session's id as a fallback's (`attempt`).
        const forks = resolved.program === 'claude' && resume !== undefined && running.mode === 'full_access' && !this.#fullAccessHonoured;
        const startAgain =
            resume === undefined ? () => attempt(undefined, hostChanged, false) : forks ? () => attempt(resume, hostChanged, false) : () => attempt(undefined, true, true);

        void (async () => {
            let last: Attempted = { exit: { code: null, startError: 'unknown' }, result: undefined, host: null, again: false };
            let handed: HandOver | null = null;
            try {
                last = await unlessNarrowed(await attempt(resume, hostChanged, false, forks), startAgain);
                if (last.again) {
                    this.#fresh(replyTo, 'not_found');
                    if (!running.stopped) last = await unlessNarrowed(await attempt(undefined, true, true), () => attempt(undefined, true, true));
                }
            } catch {
                // An attempt never rejects; were one to, the turn still ends below, as one that could not be started.
            }
            try {
                await finishFiles();
                if (setup) handed = await this.#handOver(conversation, before, since);
            } catch {
                // The turn ends whatever closing its files or reading the campaigns did.
            } finally {
                // The turn ends: its record kept, it is no longer running, its end is told, and its done sent.
                // On a keyed turn the host's own words pass `withoutKey` before the page or the record has them.
                const key = resolved.key;
                const scrub = key === null || last.ours ? (text: string) => text : (text: string) => withoutKey(text, key);
                const ended = this.#ending(resolved.label, last.exit, last.result, running.stopped, last.host, scrub);
                // A Full access turn on Codex that ended before Codex said any event, with a failing exit: Codex refused to
                // start. At openai/codex 9f2b87495b, read and not seen, `codex exec` forces `approval_policy = "never"`
                // (exec/src/lib.rs), so an administrator's requirements that forbid `danger-full-access` make building its
                // configuration fail (core/src/config/mod.rs) with Codex's words on standard error. Those words are not
                // read: one sentence of ours follows them, every time, and nothing is remembered.
                const refusedAtStart = followsCodexRefusal({
                    program: resolved.program,
                    mode: running.mode,
                    ended,
                    stopped: running.stopped,
                    heard: last.heard,
                    startError: last.exit.startError,
                    code: last.exit.code,
                    ours: last.ours,
                });
                const error = refusedAtStart ? `${ended}\n\n${codexFullAccessRefused}` : ended;
                if (error !== null) conversation.entries.push({ kind: 'error', text: error });
                if (handed?.kind === 'pick') conversation.entries.push({ kind: 'pick', campaignIds: [...handed.campaignIds] });
                this.#markPlan(conversation, running, error === null && handed === null);
                this.#keep(conversation);
                // The mark as kept: a plan too long to keep whole is cut, and loses it.
                const kept = conversation.entries.at(-1);
                const planned = kept?.kind === 'text' && kept.plan === true;
                this.#setupTurns.delete(conversation.id);
                this.#running.delete(conversation.id);
                this.#options.ended?.({ host: resolved.host, exit: last.exit.code ?? -1, durationMs: Date.now() - begun, steps, requests });
                if (handed !== null) this.#options.emit(replyTo, handed);
                this.#options.emit(replyTo, planned ? { kind: 'done', error, plan: true } : { kind: 'done', error });
                if (firstTurn && error === null && !running.stopped) this.#nameLater(conversation, text, resolved);
            }
        })().catch(() => undefined);
    }

    /** Whether a set-up chat is still named by the first words of its first message, and still kept: what a naming run may replace. */
    #namedByFirstWords(conversation: Conversation, firstWords: string): boolean {
        return (
            !this.#closed &&
            this.#conversations.get(conversation.id) === conversation &&
            this.#kept.has(conversation.id) &&
            conversation.campaign_id === null &&
            conversation.named_by_person !== true &&
            conversation.title === firstWords
        );
    }

    /**
     * Asks, once, after a set-up chat's first turn ended without an error, for a name by what it is about: one naming run
     * on the conversation's own host or provider (`chat-naming.ts`), after every naming run asked for before it, so one
     * runs at a time across the desktop. Never for a chat a person named or a campaign's.
     */
    #nameLater(conversation: Conversation, first: string, resolved: Resolved): void {
        const firstWords = titleOf(first);
        if (!this.#namedByFirstWords(conversation, firstWords)) return;
        this.#naming = this.#naming.then(() => this.#nameNow(conversation, first, firstWords, resolved)).catch(() => undefined);
    }

    async #nameNow(conversation: Conversation, first: string, firstWords: string, resolved: Resolved): Promise<void> {
        const model = namingModelOf(resolved.host);
        if (model === null || !this.#namedByFirstWords(conversation, firstWords)) return;
        const said = (outcome: NamingOutcome) => this.#options.named?.(outcome);
        let effort = model.effort === 'always';
        if (resolved.program === 'claude') {
            // The floor holds a naming run as it holds a turn: read again now, never remembered.
            const version = printedVersion(await this.#options.programs.run('claude', ['--version'], { timeoutMs: versionLimitMs }));
            if (version === null || floorSentence(version) !== null) return said('held');
            if (model.effort === 'haiku') effort = compareVersions(version, haikuEffortFrom) >= 0;
        }
        if (!this.#namedByFirstWords(conversation, firstWords)) return;
        const launch = programLaunch(this.#options.env, resolved.program);
        if ('refused' in launch) return said('not_started');
        const provider = keyProvider(resolved.host);
        // A keyed run has its provider's environment as a turn has it, with the fast model in place of the turn's.
        const provided = provider !== undefined && resolved.key !== null && resolved.folder !== null ? providerEnvironment(provider, resolved.key, model.model ?? '', resolved.folder) : {};
        const env = hostEnvironment(this.#options.env, {
            platform: this.#platform,
            bundle: this.#options.bundle,
            data: this.#options.dataNamed ? this.#options.paths.data : null,
            program: resolved.program,
            keyed: resolved.keyed,
            provider: provided,
            bundleVersion: this.#options.bundleVersion,
        });
        // An inherited effort would stand over the run's own (Claude Code reads the variable over `--effort`).
        for (const name of Object.keys(env)) if (name.toUpperCase() === 'CLAUDE_CODE_EFFORT_LEVEL') delete env[name];
        const onCommandLine = provider?.baseUrl ? null : model.model;
        const now = { stop: new AbortController(), folder: namingFolderPath(this.#options.paths.turns, newTurnId()) };
        this.#namingNow = now;
        const answer = await runNaming({
            program: resolved.program,
            launch,
            args: resolved.program === 'claude' ? claudeNamingArguments(onCommandLine, effort) : codexNamingArguments(codexNamingModel(this.#options.models?.().get(resolved.host)?.models ?? null)),
            env,
            stdin: namingInput(resolved.program, first),
            folder: now.folder,
            checksKeySource: resolved.checksKeySource,
            limitMs: this.#options.namingLimitMs ?? namingLimitMs,
            signal: now.stop.signal,
        }).finally(() => {
            this.#namingNow = null;
        });
        if ('outcome' in answer) return said(answer.outcome);
        // A person who renamed the chat, a hand-over or a removal while the run ran: their name stands.
        if (!this.#namedByFirstWords(conversation, firstWords)) return said('overtaken');
        conversation.title = answer.name;
        this.#keep(conversation);
        this.#options.emit(conversation.id, { kind: 'renamed', title: answer.name });
        said('named');
    }

    #fresh(replyTo: string, reason: FreshReason): void {
        this.#options.fresh?.(reason);
        this.#options.emit(replyTo, { kind: 'fresh', reason });
    }

    /** The turn's end in words: the host's own (its result, or the tail of its standard error) through `scrub`, else the desktop's. */
    #ending(label: string, exit: HostExit, result: string | null | undefined, stopped: boolean, host: HostProcess | null, scrub: (text: string) => string): string | null {
        if (stopped) return null;
        if (exit.startError !== null) return `${label} could not be started: ${exit.startError}`;
        if (typeof result === 'string') return scrub(result);
        const words = scrub(lastWords(host?.stderrTail() ?? ''));
        if (result === null && exit.code === 0) return null;
        if (result === null) return words || `${label} stopped with exit code ${exit.code ?? 'none'}.`;
        if (exit.code === 0) return words || `${label} ended without an answer.`;
        return words || `${label} stopped with exit code ${exit.code ?? 'none'} before it answered.`;
    }
}
