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
 */

import { chmodSync, mkdirSync, readdirSync, statSync } from 'node:fs';
import { homedir } from 'node:os';
import { isAbsolute } from 'node:path';
import { claudeArguments, claudeLine, codexArguments, codexLine, dataRules, hostEnvironment, type HostLine, type KeySource, type Step } from './chat-hosts.ts';
import {
    bounded,
    conversationIdShape,
    ConversationUnwritten,
    type Chosen,
    newConversationId,
    readConversations,
    removeConversation,
    writeConversation,
    type Conversation,
    type KeptEntry,
} from './chat-conversations.ts';
import { header, nameOf, recap, titleOf, turnInput, type About } from './chat-header.ts';
import { currentModel, effortRefusal, isEffort, modelRefusal, modelShape, type Effort, type KeptLists } from './chat-models.ts';
import { PermissionBroker, type BrokerEvent, type Decided } from './chat-permission-broker.ts';
import { openEndpoint, type Endpoint } from './chat-permission-endpoint.ts';
import type { ChatScope, Platform } from './chat-permission-model.ts';
import { startHost, type HostExit, type HostProcess } from './chat-process.ts';
import { realPathOf } from './chat-real-path.ts';
import { newSecret, newTurnId, removeTurnFiles, sweepTurnFiles, writeTurnConfig } from './chat-turn-files.ts';
import type { ChatFence } from './chat-deny.ts';
import { InstructionsUnwritten, placeInstructions } from './chat-instructions.ts';
import type { Cli } from './cli.ts';
import { floorSentence, printedVersion, versionLimitMs } from './host-version.ts';
import type { DesktopPaths } from './paths.ts';
import { programLaunch, type Programs } from './programs.ts';
import { hosts, keyedFolder, keyProvider, providerEnvironment, withoutKey, withoutWholeKey } from './providers-model.ts';

/** Why a turn starts a fresh session of a conversation that had one. */
export type FreshReason = 'host_changed' | 'not_found';

/** What the page is told, by the chat it belongs to: U2's `DesktopChatEvent`. */
export type ChatEvent =
    | ({ kind: 'step' } & Step)
    | { kind: 'text'; text: string }
    | BrokerEvent
    | { kind: 'attached'; campaignId: string; title: string }
    | { kind: 'fresh'; reason: FreshReason }
    | { kind: 'pick'; campaignIds: string[] }
    | { kind: 'done'; error: string | null };

/** What a send names besides its conversation and its words: never a campaign, which the conversation's record decides. */
export interface ChatSendOptions {
    host: string;
    model?: string;
    /** One of the five levels, which the model named must take (`effortRefusal`); absent, the host keeps its own. */
    effort?: Effort;
}

/** A conversation as `chat.list` lists it: `running` whether a turn of it runs now. */
export interface ConversationSummary {
    id: string;
    campaignId: string | null;
    title: string;
    updatedAt: string;
    running: boolean;
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
    cli: Cli;
    /** The program runner, which reads Claude Code's version before each turn on it. */
    programs: Pick<Programs, 'run'>;
    /** The models Codex offered when last asked, by host id (`model-lists.ts`); none when not given. */
    models?(): KeptLists;
    keyFor(providerId: string): string | undefined;
    /** The deny list's commands and operations, the allow list's verbs and the instruction text, read at each turn. */
    fence(): ChatFence;
    emit(chatId: string, event: ChatEvent): void;
    started?(host: string, resumed: boolean): void;
    ended?(turn: TurnEnded): void;
    decided?(decided: Decided): void;
    /** Said once, the first time the application is found to carry no instruction text; the turns run without it. */
    instructionsMissing?(): void;
    /** Said each time a memory file cannot be written, at the start or before a turn, which is then refused. */
    instructionsUnwritten?(file: string, code: string): void;
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
    /** How long a card waits for the person; thirty minutes unless a test shortens it. */
    answerWindowMs?: number;
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

/** A campaign as a turn reads it: what the header says, and the folders a Claude Code turn adds. */
interface CampaignRead {
    about: About;
    added: string[];
}

/** A campaign in `jason campaign list`: when it was created, in milliseconds, and its name. */
type Listed = Map<string, { created: number; name: string }>;

const chatIdShape = /^[A-Za-z0-9_.:-]{1,200}$/;
const campaignIdShape = /^cmp_[0-9A-Za-z]{1,64}$/;
const textLimit = 1_000_000;
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
}

/** The campaigns the conversation's last pick named. */
const candidatesOf = (conversation: Conversation): string[] => {
    for (let i = conversation.entries.length - 1; i >= 0; i--) {
        const entry = conversation.entries[i];
        if (entry?.kind === 'pick') return entry.campaignIds;
    }
    return [];
};

const busy = 'This chat is still answering. Wait for it to finish, or stop it, then send again.';
const notAHost = 'That is not an agent host this desktop knows. Choose Claude Code or Codex for the chat.';
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

    constructor(options: ChatOptions) {
        this.#options = options;
        this.#platform = options.platform ?? currentPlatform();
        sweepTurnFiles(options.paths.turns);
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
        options.conversationsRead?.(read.kept.length, read.unreadable);
        this.#broker = new PermissionBroker({
            emit: (chatId, event) => {
                this.#cardSaid(chatId, event);
                options.emit(chatId, event);
            },
            ...(options.decided ? { decided: options.decided } : {}),
            ...(options.answerWindowMs !== undefined ? { timeoutMs: options.answerWindowMs } : {}),
        });
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

    #summaryOf(conversation: Conversation): ConversationSummary {
        return {
            id: conversation.id,
            campaignId: conversation.campaign_id,
            title: conversation.title,
            updatedAt: conversation.updated_at,
            running: this.#running.has(conversation.id),
        };
    }

    /** A conversation with what the page drew; an id the desktop does not keep is refused. */
    open(id: unknown): ConversationOpened {
        const conversation = this.#own(id);
        return { ...this.#summaryOf(conversation), entries: structuredClone(conversation.entries), dropped: conversation.dropped, chosen: conversation.chosen === undefined ? null : { ...conversation.chosen } };
    }

    /**
     * A conversation to start in: a new set-up chat when no campaign is named, else the campaign's conversation, the
     * one it has or a new one named after it. A new conversation is kept once its first turn has started.
     */
    async create(campaignId?: unknown): Promise<ConversationSummary> {
        if (campaignId === undefined || campaignId === null) return this.#summaryOf(this.#made(null, 'New chat'));
        if (typeof campaignId !== 'string' || !campaignIdShape.test(campaignId)) throw new ChatRefused('That is not a campaign id.');
        const existing = this.#ofCampaign(campaignId);
        if (existing !== undefined) return this.#summaryOf(existing);
        const read = await this.#readCampaign(campaignId, false);
        const name = read.about.campaign?.name;
        return this.#summaryOf(this.#ofCampaign(campaignId) ?? this.#made(campaignId, name ? nameOf(name) || campaignId : campaignId));
    }

    /** Removes a conversation's record; the host's own transcript stays the host's. One whose turn runs is refused. */
    remove(id: unknown): void {
        const conversation = this.#own(id);
        if (this.#running.has(conversation.id)) throw new ChatRefused(stillAnswering('remove it'));
        try {
            removeConversation(this.#options.paths.conversations, conversation.id);
        } catch (error) {
            if (error instanceof ConversationUnwritten) this.#options.conversationUnwritten?.(error.code);
            throw new ChatRefused('The conversation could not be removed. Close anything that has its file open, then try again.');
        }
        this.#conversations.delete(conversation.id);
        this.#kept.delete(conversation.id);
    }

    /** "Start afresh": the hosts' sessions are dropped, and the entries and the campaign kept. One whose turn runs is refused. */
    restart(id: unknown): void {
        const conversation = this.#own(id);
        if (this.#running.has(conversation.id)) throw new ChatRefused(stillAnswering('start it afresh'));
        conversation.sessions = {};
        // One never sent to has no session to drop, and no record to write.
        if (this.#kept.has(conversation.id)) this.#keep(conversation);
    }

    /**
     * Makes a set-up chat the conversation of a campaign the person picked from the ones its last pick named. Refused for
     * a conversation already about a campaign, one whose turn runs, a campaign that has a conversation, and a campaign
     * the pick did not name.
     */
    async attach(id: unknown, campaignId: unknown): Promise<ConversationSummary> {
        const conversation = this.#own(id);
        if (typeof campaignId !== 'string' || !campaignIdShape.test(campaignId)) throw new ChatRefused('That is not a campaign id.');
        const refusal = () => {
            if (this.#running.has(conversation.id)) return stillAnswering('choose its campaign');
            if (conversation.campaign_id !== null) return 'This conversation is already about a campaign.';
            if (this.#ofCampaign(campaignId) !== undefined) return 'That campaign has its conversation already. Open it from the campaign.';
            if (!candidatesOf(conversation).includes(campaignId)) return 'That campaign is not one this chat made. Open it from the campaign.';
            return null;
        };
        const before = refusal();
        if (before !== null) throw new ChatRefused(before);
        const read = await this.#readCampaign(campaignId, false);
        const name = read.about.campaign?.name;
        if (name === null || name === undefined) throw new ChatRefused('That campaign could not be read. Check that Jason is running, then try again.');
        const after = refusal();
        if (after !== null) throw new ChatRefused(after);
        conversation.campaign_id = campaignId;
        conversation.title = nameOf(name) || campaignId;
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
        if (given.model !== undefined && (typeof given.model !== 'string' || !modelShape.test(given.model))) return refuse('That model name is not one the desktop passes to a host.');
        if (given.effort !== undefined && !isEffort(given.effort)) return refuse('That effort is not one the desktop passes to a host.');
        const resolved = this.#resolve(given.host, given.model as string | undefined, given.effort as Effort | undefined);
        if (typeof resolved === 'string') return refuse(resolved);
        const notOffered = modelRefusal(resolved.host, resolved.named, this.#options.models?.() ?? new Map());
        if (notOffered !== null) return refuse(notOffered);
        const launch = programLaunch(this.#options.env, resolved.program);
        if ('refused' in launch) return refuse(`${resolved.label} cannot be started: ${launch.refused}`);

        const running: Running = { stopped: false, process: null, turnId: null, replyTo: chatId, cards: new Map() };
        this.#running.set(conversation.id, running);
        this.#kept.add(conversation.id);
        // Any turn that begins while a set-up turn runs could make the campaign that set-up turn would take for its own.
        for (const [id, each] of this.#setupTurns) if (id !== conversation.id) each.overlapped = true;
        try {
            // The page marks a host below the floor; this is the guard, whatever the page asked. The chat counts as
            // answering while the version is read, so a second send is refused as busy. The version read also says
            // whether the model named takes the effort named.
            const version = resolved.program === 'claude' ? await this.#claudeVersion() : null;
            const below = resolved.program === 'claude' ? floorSentence(version) : null;
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
            await this.#turn(conversation, text, resolved, launch, running);
        } catch (error) {
            this.#running.delete(conversation.id);
            this.#setupTurns.delete(conversation.id);
            if (error instanceof InstructionsUnwritten) refuse(error.message);
            else refuse(`The turn could not be started: ${error instanceof Error ? error.message : String(error)}`);
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
     * running turn's file removed before this returns, since the desktop may quit before a host's exit is seen.
     */
    closeAll(): void {
        for (const running of [...this.#running.values()]) {
            running.stopped = true;
            running.process?.stop();
            if (running.turnId !== null) removeQuietly(this.#options.paths.turns, running.turnId);
        }
        this.#broker.endAll();
    }

    /** Claude Code's version, read before each turn on it and never remembered; null when it cannot be read. */
    async #claudeVersion(): Promise<string | null> {
        return printedVersion(await this.#options.programs.run('claude', ['--version'], { timeoutMs: versionLimitMs }));
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

    #made(campaignId: string | null, title: string): Conversation {
        const now = new Date().toISOString();
        const conversation: Conversation = { id: newConversationId(), campaign_id: campaignId, title, created_at: now, updated_at: now, sessions: {}, entries: [], dropped: 0 };
        this.#conversations.set(conversation.id, conversation);
        return conversation;
    }

    /** Writes the record within its bound; one that cannot be written is said, and the conversation goes on in memory. */
    #keep(conversation: Conversation): void {
        const within = bounded(conversation);
        conversation.entries = within.entries;
        conversation.dropped = within.dropped;
        conversation.updated_at = new Date().toISOString();
        try {
            writeConversation(this.#options.paths.conversations, conversation);
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
            const card: Extract<KeptEntry, { kind: 'permission' }> = { kind: 'permission', title: event.title, detail: event.detail, answer: null };
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

    async #readJson(args: string[]): Promise<Record<string, unknown> | null> {
        const answer = await this.#options.cli.run(args);
        return answer.exit === 0 ? objectOf(answer.stdout) : null;
    }

    /**
     * The campaign as the runtime answers it: its name and status for the header, its folder and its project's, as
     * the runtime recorded them and only when they are on this disk, for a Claude Code turn to add.
     */
    async #readCampaign(campaignId: string, withProject: boolean): Promise<CampaignRead> {
        const campaign = await this.#readJson(['campaign', 'get', campaignId]);
        if (campaign === null) return { about: { campaign: { id: campaignId, name: null, status: null, folder: null } }, added: [] };
        const own = folderThere(campaign.folder_path);
        const added: string[] = own === null ? [] : [own];
        if (withProject && typeof campaign.project_id === 'string' && /^prj_[0-9A-Za-z]{1,64}$/.test(campaign.project_id)) {
            const project = await this.#readJson(['project', 'get', campaign.project_id]);
            const theirs = folderThere(project?.folder_path);
            if (theirs !== null && !added.includes(theirs)) added.push(theirs);
        }
        const name = typeof campaign.name === 'string' ? campaign.name : '';
        const status = typeof campaign.status === 'string' ? campaign.status : null;
        return { about: { campaign: { id: campaignId, name, status, folder: own } }, added: withProject ? added : [] };
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
        conversation.title = nameOf(campaign.name) || campaignId;
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
    #placeInstructions(fence: ChatFence): void {
        if (fence.instructions === null) {
            if (!this.#missingSaid) this.#options.instructionsMissing?.();
            this.#missingSaid = true;
            return;
        }
        try {
            placeInstructions(this.#options.paths.chat, fence.instructions);
        } catch (error) {
            if (error instanceof InstructionsUnwritten) this.#options.instructionsUnwritten?.(error.file, error.code);
            throw error;
        }
    }

    async #turn(conversation: Conversation, text: string, resolved: Resolved, launch: { file: string; prefix: string[] }, running: Running): Promise<void> {
        const { paths } = this.#options;
        const replyTo = running.replyTo;
        const setup = conversation.campaign_id === null;
        let about: About = { campaign: null };
        let added: string[] = [];
        let before: Listed | null = null;
        let since = 0;
        if (conversation.campaign_id !== null) {
            const read = await this.#readCampaign(conversation.campaign_id, resolved.program === 'claude');
            about = read.about;
            added = read.added;
            const name = read.about.campaign?.name;
            if (name) conversation.title = nameOf(name) || conversation.title;
        } else {
            since = Date.now();
            this.#setupBegun(conversation.id);
            before = await this.#listCampaigns();
        }
        const head = header(about);
        // The recap is of what came before this turn; the person's words of this turn follow it.
        const earlier = recap(conversation.entries);
        const resume = conversation.sessions[resolved.host];
        const hostChanged = resume === undefined && Object.keys(conversation.sessions).length > 0;
        mkdirSync(paths.chat, { recursive: true });

        let turnId: string | null = null;
        let endpoint: Endpoint | null = null;
        let argsFor: (resume: string | undefined) => string[];
        let requests = 0;
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
            const fence = this.#options.fence();
            // First of all a turn does: the chat folder is found to be the desktop's own, and its memory files written,
            // before the turn's file, its endpoint or the host that starts in that folder.
            this.#placeInstructions(fence);
            if (resolved.folder !== null) makePrivate(resolved.label, resolved.program === 'claude' ? paths.claudeKeyed : paths.codexKeyed, resolved.folder);
            if (resolved.program === 'claude') {
                const id = newTurnId();
                turnId = id;
                running.turnId = id;
                const scope: ChatScope = {
                    platform: this.#platform,
                    cwd: paths.chat,
                    chat: paths.chat,
                    added,
                    data: paths.data,
                    home: homedir(),
                    protectedFiles: [paths.installationSettings, paths.runFile],
                    denied: fence.commands,
                    deniedOperations: fence.operations,
                    real: realPathOf,
                };
                this.#broker.openTurn(replyTo, id, scope);
                const secret = newSecret();
                endpoint = await openEndpoint({
                    secret,
                    decide: (tool, input, signal) => {
                        requests++;
                        return this.#broker.ask(id, tool, input, signal);
                    },
                });
                const mcpConfig = writeTurnConfig(paths.turns, id, endpoint.url, secret);
                const rules = dataRules(this.#platform, paths.data, entriesOf(paths.data), entriesOf(paths.desktop));
                argsFor = (resumed) => claudeArguments({ mcpConfig, addDirs: added, denied: fence.commands, allowed: fence.allowed, dataRules: rules, model: resolved.model, effort: resolved.effort, resume: resumed });
            } else {
                argsFor = (resumed) => codexArguments({ model: resolved.model, effort: resolved.effort, resume: resumed });
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

        const env = hostEnvironment(this.#options.env, {
            platform: this.#platform,
            bundle: this.#options.bundle,
            data: this.#options.dataNamed ? paths.data : null,
            program: resolved.program,
            keyed: resolved.keyed,
            provider: resolved.provider,
            bundleVersion: this.#options.bundleVersion,
            effortNamed: resolved.effort !== undefined,
        });
        let steps = 0;
        const begun = Date.now();
        // A set-up chat is named by the first sixty characters of its first message, until a campaign names it.
        if (setup && conversation.title === 'New chat' && !conversation.entries.some((entry) => entry.kind === 'user')) conversation.title = titleOf(text);
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
        const attempt = async (resumed: string | undefined, recapped: boolean, fallback: boolean): Promise<Attempted> => {
            let result: string | null | undefined;
            let shown = 0;
            let other = false;
            // The sentence the turn ends with when the desktop stopped the host for its key; nothing it says after counts.
            let stoppedFor: string | null = null;
            // Whether the turn ended on a key the provider refused, in the desktop's own sentence.
            let keyRefused = false;
            // The fallback's session, kept aside until the fallback has done something.
            let pending: string | null = null;
            const seen = new Set<string>();
            let host: HostProcess | null = null;
            const name = (session: string) => {
                if (conversation.sessions[resolved.host] === session) return;
                conversation.sessions[resolved.host] = session;
                this.#keep(conversation);
            };
            const stopFor = (sentence: string) => {
                stoppedFor = sentence;
                result = sentence;
                host?.stop();
            };
            const said = (line: HostLine) => {
                if (other || stoppedFor !== null) return;
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
                if (line.kind === 'session') {
                    if (resumed !== undefined && line.id !== resumed) {
                        other = true;
                        host?.stop();
                        return;
                    }
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
                        const said: Extract<ChatEvent, { kind: 'text' }> = { kind: 'text', text: clean(line.text) };
                        conversation.entries.push({ ...said });
                        this.#options.emit(replyTo, said);
                    }
                }
            };
            let started: HostProcess;
            try {
                started = startHost(launch, argsFor(resumed), {
                    cwd: paths.chat,
                    env,
                    stdin: turnInput(head, recapped ? earlier : null, text),
                    onLine: (line) => {
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
            this.#options.started?.(resolved.host, resumed !== undefined);
            if (running.stopped || stoppedFor !== null) started.stop();
            const exit = await started.exited;
            // Every tool use is said as a step before it runs, so a host that said nothing did nothing.
            const failedSilently = shown === 0 && (exit.code !== 0 || typeof result === 'string');
            // A key that was refused or not taken is the key's, not the session's: a fresh session would fail the same way.
            const refusedKey = stoppedFor !== null || keyRefused;
            const again = resumed !== undefined && !running.stopped && exit.startError === null && !refusedKey && (other || failedSilently);
            return { exit, result, host: started, again, ours: refusedKey };
        };

        void (async () => {
            let last: Attempted = { exit: { code: null, startError: 'unknown' }, result: undefined, host: null, again: false };
            let handed: HandOver | null = null;
            try {
                last = await attempt(resume, hostChanged, false);
                if (last.again) {
                    this.#fresh(replyTo, 'not_found');
                    if (!running.stopped) last = await attempt(undefined, true, true);
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
                const error = this.#ending(resolved.label, last.exit, last.result, running.stopped, last.host, scrub);
                if (error !== null) conversation.entries.push({ kind: 'error', text: error });
                if (handed?.kind === 'pick') conversation.entries.push({ kind: 'pick', campaignIds: [...handed.campaignIds] });
                this.#keep(conversation);
                this.#setupTurns.delete(conversation.id);
                this.#running.delete(conversation.id);
                this.#options.ended?.({ host: resolved.host, exit: last.exit.code ?? -1, durationMs: Date.now() - begun, steps, requests });
                if (handed !== null) this.#options.emit(replyTo, handed);
                this.#options.emit(replyTo, { kind: 'done', error });
            }
        })().catch(() => undefined);
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
