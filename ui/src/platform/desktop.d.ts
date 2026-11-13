/**
 * The seam between the page and Jason Desktop: what the desktop's preload puts on `window.jasonDesktop`, and all of
 * it. In a browser it is absent, and the page is the classic shell. The page is served from the runtime's loopback
 * origin in both; the desktop's main process checks every call's sender against that origin, so the page checks
 * nothing here. Every promise rejects with an Error whose message the page shows as text.
 */
export {};

/**
 * The journal kinds the Feed takes: a reply observed, an approval or a question waiting, an approval approved or
 * rejected, a question answered, a campaign created, started, paused or archived, work that stopped, and work that
 * succeeded where it was a send or the work of one of five roles (`DesktopFeedOperation`, `DesktopFeedRole`). Each is a
 * kind the runtime writes itself.
 */
export type DesktopFeedKind =
    | 'inbound_reply_observed'
    | 'approval_requested'
    | 'decision_raised'
    | 'campaign_created'
    | 'campaign_started'
    | 'campaign_paused'
    | 'workitem_failed'
    | 'workitem_expired'
    | 'provider_blocked'
    | 'approval_approved'
    | 'approval_rejected'
    | 'decision_answered'
    | 'campaign_archived'
    | 'workitem_succeeded';

/** The operations whose success the Feed shows, as a send the provider took. */
export type DesktopFeedOperation = 'message.send' | 'conversation.reply' | 'social_invitation.send';

/** The shipped roles whose finished work the Feed shows. */
export type DesktopFeedRole = 'planner' | 'researcher' | 'copywriter' | 'analyst' | 'deliverability-specialist';

/**
 * A journal line as the page hands it to the Feed: its id, moment, kind, campaign and work item, as `journal.list` and
 * the journal's stream carry them, and nothing else of it.
 */
export interface DesktopFeedLine {
    id: string;
    ts: string;
    kind: DesktopFeedKind;
    campaign_id: string;
    work_item_id: string | null;
    /** On a `workitem_succeeded` line alone: the operation or the role, one of them, from the closed lists; else absent. */
    work_item_operation?: DesktopFeedOperation;
    work_item_role?: DesktopFeedRole;
}

/**
 * One line of the Feed as the desktop keeps it: identifiers, a kind, a moment and a count, never a person's text.
 * `journal_id` and `at` are the newest journal line's it stands for; `count` how many lines of its kind and campaign
 * followed one another into it.
 */
export interface DesktopLineEntry {
    kind: 'line';
    journal_kind: DesktopFeedKind;
    journal_id: string;
    at: string;
    campaign_id: string;
    work_item_id?: string;
    /** On a `workitem_succeeded` line, exactly one of these two; else neither. */
    operation?: DesktopFeedOperation;
    role?: DesktopFeedRole;
    count: number;
    /** Present, and true, once a turn of the Feed was given the line with this count; a fold that raises it removes it. */
    given?: true;
}

/**
 * A page a `jason-desktop://` link opens, as the desktop parsed it from a closed table of addresses: the Feed, one of the
 * console's panels, or a screen at one entity whose id the desktop held to its shape. A link opens that page and does
 * nothing else.
 */
export type DesktopLink =
    | { page: 'feed' }
    | { page: 'needs' }
    | { page: 'campaigns' }
    | { page: 'campaign'; id: string }
    | { page: 'approval'; id: string }
    | { page: 'question'; id: string }
    | { page: 'workitem'; id: string }
    | { page: 'person'; id: string };

/** A command from the desktop's menu, tray or keybindings. The page ignores one it does not know. */
export type DesktopCommand =
    | 'chat.new'
    | 'navigate.chat'
    | 'navigate.attention'
    | 'navigate.campaigns'
    | 'navigate.activity'
    | 'navigate.performance'
    | 'navigate.workitems'
    | 'navigate.people'
    | 'navigate.system'
    | 'navigate.settings'
    | 'navigate.connect';

/**
 * How hard the model thinks, as the host's own flag: one of five levels, the same words on every host. The desktop
 * passes no other word, `ultracode` and `ultra` among them.
 */
export type DesktopEffort = 'low' | 'medium' | 'high' | 'xhigh' | 'max';

/** What `chat.send` takes besides the text: never a campaign, which the conversation the desktop keeps decides. */
export interface DesktopChatOptions {
    /** The agent host the turn runs on: the `id` of one of `setup.status().hosts` whose `drivesChat` is true. */
    host: string;
    /** A model the host offers, by the name the host takes; absent, the host's own default. */
    model?: string;
    /**
     * An effort the model named takes, one of its `DesktopModel.efforts`; absent, the host's own. The desktop refuses a
     * send whose effort the model does not take, and one with an effort and no model, before any host starts.
     */
    effort?: DesktopEffort;
    /**
     * The mode the turn was approved for: the plan card's go names `ask_first`. The desktop refuses the send, with a
     * `done` that says why and before any host starts, when the conversation is in another mode; absent, the turn runs
     * in the mode the conversation is in.
     */
    mode?: DesktopChatMode;
}

/**
 * A chat's access mode, chosen per conversation from the menu beside the composer. `ask_first`, the default of every new
 * chat out of the box: the agent asks on a card before anything the desktop does not settle itself. `plan_only`: it reads
 * and proposes, and changes nothing. `auto_edit`: Ask first, except that the six file commands (mkdir, touch, mv, cp, rm,
 * rmdir) run with no card on paths in the chat's own folder; no confirmation, held for that conversation only until the
 * window closes, Claude Code only, never the Feed. `full_access`: it acts on this computer without asking; the desktop asks the person
 * to confirm each time it is chosen, unless they answered that box with Always (`DesktopFullAccessStanding`), and holds
 * it for that conversation only until the window closes. Codex has no Full access.
 */
export type DesktopChatMode = 'ask_first' | 'plan_only' | 'auto_edit' | 'full_access';

/**
 * The standing choice of Full access, which only the person's answer to the desktop's own box gives: while it stands,
 * the menu enters Full access without the box, and with `newChats` a new chat on a host that can take Full access starts
 * in it. `givenAt` is when it was given, ISO-8601 UTC.
 */
export interface DesktopFullAccessStanding {
    givenAt: string;
    newChats: boolean;
}

/** A model the chat's box offers on a host: `id` the name the host takes, `label` the words to show, and its efforts. */
export interface DesktopModel {
    id: string;
    label: string;
    /** The effort levels it takes; empty when it takes none, and then the box shows no effort for it. */
    efforts: DesktopEffort[];
}

/** What a conversation's last turn was sent with: its host, its model (null: the host's own) and its effort. */
export interface DesktopChosen {
    host: string;
    model: string | null;
    effort: DesktopEffort | null;
}

/**
 * One conversation the desktop keeps under its data directory: a campaign's, one per campaign, or a set-up chat about
 * no campaign yet, any number of them. Its id is the `chatId` every other chat call takes.
 */
export interface DesktopConversation {
    /** `conv_` and 26 Crockford base-32 characters, made by the desktop. */
    id: string;
    /** The campaign it is about: `cmp_` and its ULID; null for a set-up chat and for the Feed. */
    campaignId: string | null;
    /**
     * Present, and true, for the Feed: the person's main chat, one per desktop, into which the journal's lines come. It is
     * never renamed, removed or handed over to a campaign.
     */
    main?: true;
    /**
     * The name a person gave it, one line within sixty characters, which nothing replaces afterwards; else the
     * campaign's name, one line; else for a set-up chat a name of two to five words the desktop asked its model for
     * after the first turn, or the first sixty characters of the person's first message, or "New chat". A person's or a
     * model's text, shown as text.
     */
    title: string;
    /** When it last changed, ISO-8601 UTC. */
    updatedAt: string;
    /** Whether a turn of it runs now, so a page loaded again shows it running, with Stop. */
    running: boolean;
    /**
     * Its mode as the desktop holds it now. The line under the composer that says Full access is on is drawn from this,
     * from `chat.setMode`'s answer and from `mode` events, never from what the page asked for.
     */
    mode: DesktopChatMode;
}

/**
 * A call the desktop refused, answered as a value: `refused` is the desktop's sentence alone, for the page to show as
 * it is. A promise of the seam rejects only for what is no refusal.
 */
export interface DesktopRefusal {
    refused: string;
}

/**
 * One thing the page drew, as the desktop kept it: text only, never markup, drawn by the same code as the live event it
 * stands for. A card keeps the answer it was given: `gone` when it no longer waited, null when it was never answered.
 */
export type DesktopKeptEntry =
    | { kind: 'user'; text: string }
    /** `plan`, when present, marks the last words of a Plan only turn that ended well as its live plan. */
    | { kind: 'text'; text: string; plan?: true }
    | { kind: 'step'; text: string; detail?: string }
    | { kind: 'permission'; title: string; detail: string; answer: 'allow' | 'always' | 'deny' | 'gone' | null }
    | { kind: 'error'; text: string }
    | { kind: 'pick'; campaignIds: string[] }
    /** A line of the Feed, which only the Feed keeps. */
    | DesktopLineEntry
    /** Where the Feed's catch-up read back fewer lines than were written while the window was closed. */
    | { kind: 'gap' };

/** A conversation with what the page drew, oldest first, within 2 MB: `dropped` counts the oldest entries let go. */
export interface DesktopConversationRecord extends DesktopConversation {
    entries: DesktopKeptEntry[];
    dropped: number;
    /** What its last turn was sent with, for the box to show again; null before its first. */
    chosen: DesktopChosen | null;
}

/** One thing a running turn says, in the order the host says it. The chat id it belongs to comes beside it. */
export type DesktopChatEvent =
    /**
     * A step the agent took: `text` is the desktop's own plain words for it ("Ran a jason command", "Read a file"), and
     * `detail` the command, path, query or tool name it acted on, shown beneath them as text.
     */
    | { kind: 'step'; text: string; detail?: string }
    /** A message of the agent's, whole: shown as its own bubble, as text. */
    | { kind: 'text'; text: string }
    /**
     * The agent asks to do something the permission model does not settle on its own: `request` is the id to answer
     * by `chat.answer`, `title` the act in a few words, `detail` the command or path in full, and `remember` what
     * "always, in this chat" would cover, in words to print on its button, or null when it may not be offered.
     */
    | { kind: 'permission'; request: string; title: string; detail: string; remember: string | null }
    /**
     * A request no longer waits: it was answered, it timed out, or the desktop no longer holds it. An answer sent for
     * it after this changes nothing.
     */
    | { kind: 'permission_gone'; request: string }
    /**
     * The set-up chat became the conversation of `campaignId`, named `title`: its turn made exactly that one campaign,
     * or the person picked it (`chat.attach`). Said before the turn's `done`.
     */
    | { kind: 'attached'; campaignId: string; title: string }
    /**
     * The turn starts a fresh session of the host, given a recap of the conversation's earlier messages: the host has
     * not seen this conversation (`host_changed`), or its session could not be resumed (`not_found`).
     */
    | { kind: 'fresh'; reason: 'host_changed' | 'not_found' }
    /**
     * The set-up chat's turn made campaigns the desktop does not hand over by itself: more than one, or while another
     * set-up chat's turn ran. The person picks one of `campaignIds` (`chat.attach`), or none. Said before `done`.
     */
    | { kind: 'pick'; campaignIds: string[] }
    /**
     * The conversation's title changed: a person renamed it (`chat.rename`), or the desktop named a set-up chat by what it
     * is about once its first turn ended well, which may come after that turn's `done`.
     */
    | { kind: 'renamed'; title: string }
    /**
     * The conversation's mode changed: a person chose it (`chat.setMode`), or the desktop put the chat back to Ask first,
     * because its turn ran on Codex, on a Claude Code too old for Full access, or showed a tool the desktop's check never
     * saw.
     */
    | { kind: 'mode'; mode: DesktopChatMode }
    /**
     * The Feed took a line: at its end, or, when `folded`, in place of its last line, which was of the same kind and
     * campaign. A line never starts a turn.
     */
    | { kind: 'line'; line: DesktopLineEntry; folded: boolean }
    /** The Feed's catch-up read back fewer lines than were written while the window was closed; newer ones follow. */
    | { kind: 'gap' }
    /**
     * A sentence the desktop says once, in its own words: a campaign's or project's folder left out of the chat because its
     * `.claude` holds skills, subagents or commands, or, with `folder`, the name of the folder a Full access grant worked in,
     * kept for seven days after Full access ended, which `chat.openFolder` opens. A notice is not kept with the
     * conversation.
     */
    | { kind: 'notice'; text: string; folder?: string }
    /**
     * The turn is over: `error` is null when it ended well, else the host's own words for why it did not. `plan` is
     * present, and true, when a turn in Plan only ended well with its words last, in a chat still in Plan only and not
     * the Feed: those words, the last `text` event, are its plan, which the page offers as a card. Every earlier plan is
     * no card any more once a turn has ended.
     */
    | { kind: 'done'; error: string | null; plan?: true };

/** An agent host or a key provider the chat's dropdown lists, found on this machine or not. */
export interface DesktopHost {
    /**
     * What `chat.send`'s `host` names, and `setProviderKey`'s `providerId` for a key provider. A program's id is the
     * command that runs it, such as `claude`.
     */
    id: string;
    label: string;
    /** `program`: a program on this machine, such as Claude Code; `provider`: a key that runs through Claude Code. */
    kind: 'program' | 'provider';
    /** For a program, whether it was found; for a provider, whether its key is set. */
    found: boolean;
    /** The program's version as it reports it; null for a provider or a program not found. */
    version: string | null;
    /** Whether the chat runs on it. A host that does not is listed as running campaign work, not the chat. */
    drivesChat: boolean;
    /** The `programId` for `setup.installProgram` when the program is not found and npm is there to install it. */
    install: string | null;
    /**
     * When the Claude Code the host runs on is older than the oldest version the chat runs on, or its version could not
     * be read, the desktop's sentence that says so and how to update it; null otherwise. The chat lists such a host and
     * runs no turn on it, and the desktop refuses a turn sent to it whatever the page asked.
     */
    belowFloor: string | null;
    /**
     * Why Full access cannot be chosen for a chat on it, in the desktop's words: "Full access runs on Claude Code only."
     * on Codex, or the version Claude Code needs for it; null where it can be. The menu shows the item disabled with this.
     */
    fullAccess: string | null;
    /**
     * Why Auto-edit cannot be chosen for a chat on it, in the desktop's words, on Codex; null where it can be. The menu
     * shows the item disabled with this.
     */
    autoEdit: string | null;
    /**
     * The models the chat's box offers on it: Claude Code's aliases, the models Codex offered when last asked (kept by
     * the desktop and asked again at most once a day), or a key provider's own; empty for a host that offers none here.
     */
    models: DesktopModel[];
    /** Whether the box also takes a model's full name the person types, with no effort: Claude Code and Anthropic's key. */
    typedModel: boolean;
    /**
     * Where the host's models are asked of it (Codex) and no list was ever read, the desktop's one line saying so, and
     * that the turn uses the host's own default model; null otherwise. The box then offers the host's default alone.
     */
    modelsUnread: string | null;
}

/** An account Reply holds, as the Reply CLI lists it. */
export interface DesktopReplyAccount {
    id: string;
    channel: string;
    name: string;
    address: string;
    /**
     * The state Reply reports for the account, as Reply wrote it (email: `connected`, `disconnected`, `unknown`;
     * LinkedIn: `enabled`, `disabled`, `cookieInvalid`, `dailyLimitReached`; or another word); empty when it reported none.
     */
    status: string;
}

/** A channel Reply lists accounts for. */
export type DesktopReplyChannel = 'email' | 'linkedin';

/** The Reply connection: the Reply CLI, its sign-in, and whether Jason's Reply plugin is set up to use it. */
export interface DesktopReply {
    /** Whether the Reply CLI was found, its version, and the `programId` that installs it when it was not. */
    cli: { found: boolean; version: string | null; install: string | null };
    signedIn: boolean;
    /** The address the CLI is signed in as; null when it is not. */
    user: string | null;
    /** Whether the runtime has the Reply plugin set up with this sign-in. */
    wired: boolean;
    accounts: DesktopReplyAccount[];
    /** The channels the Reply CLI says are not in this account's plan or role. */
    absent: DesktopReplyChannel[];
    /** The channels whose list could not be read, for any other reason. The other channel's rows stay in both cases. */
    unread: DesktopReplyChannel[];
    /**
     * The links made and not used yet (`reply account pending`), read for each channel that was listed; empty for a
     * channel whose read failed. Never with a link, which the Reply CLI does not print.
     */
    pending: DesktopReplyPending[];
    /**
     * Where the Reply CLI is older than the one the account acts and Jason's Reply plugin need, the desktop's sentence
     * naming the update; the card then offers no act. Null otherwise.
     */
    belowMinimum: string | null;
}

/** A link made for connecting or reconnecting an account, and not used yet. */
export interface DesktopReplyPending {
    channel: DesktopReplyChannel;
    id: string;
    /** The name the link was made under; empty where Reply gave none. */
    name: string;
    kind: 'connect' | 'reconnect' | null;
    /** When it stops working, an ISO-8601 moment as Reply wrote it; null where Reply gave none. */
    expiresAt: string | null;
    expired: boolean;
}

/**
 * A link the Reply CLI made, opened in the person's browser by the desktop's main process. The page learns that it was
 * opened, when it stops working and its pending entry, where the CLI said; never the link, which is the person's secret.
 * A `shared_page` link is the account's shared mailbox page, which does not expire.
 */
export interface DesktopReplyOpened {
    opened: true;
    channel: DesktopReplyChannel;
    kind: 'connect' | 'reconnect';
    linkKind: 'connection_link' | 'shared_page';
    expiresAt: string | null;
    pendingId: number | null;
}

/**
 * An act on a Reply account that ended without its effect: the desktop's sentence; the Reply CLI's code where it gave
 * one, which an unknown code carries in the sentence too; what the card may offer next (the shared mailbox page after
 * `email_link.not_available`, a forced delete after `linkedin.account_in_use`, Reconnect after
 * `linkedin.invalid_cookie`); and whether the card reads the list again to show where things stand.
 */
export interface DesktopReplyRefusal {
    refused: string;
    code: string | null;
    offer: 'shared_page' | 'force' | 'reconnect' | null;
    reread: boolean;
}

/**
 * A link the Reply CLI made that expires and connects one account, written to the clipboard by the desktop's main process
 * for the person to hand on to whoever owns the account. The page learns that it was copied, when it stops working and
 * its pending entry; never the link. The shared mailbox page is never copied.
 */
export interface DesktopReplyCopied {
    copied: true;
    channel: DesktopReplyChannel;
    kind: 'connect' | 'reconnect';
    linkKind: 'connection_link';
    expiresAt: string | null;
    pendingId: number | null;
}

/** An act on a Reply account that the CLI did. */
export interface DesktopReplyDone {
    done: true;
}

/**
 * A plugin the release carries that the desktop can configure: the three notification channels (slack, telegram, ntfy)
 * and the journal forwarder. The desktop knows these four from a table of its own and reads no manifest.
 */
export interface DesktopPlugin {
    id: string;
    label: string;
    description: string;
    /**
     * The one value `configurePlugin`'s `target` sets, by the key the plugin reads it under: slack `channel`, telegram
     * `chat` and ntfy `topic` in its notification binding, the forwarder's `url` in its configuration. `label` names
     * the field.
     */
    target: { key: 'channel' | 'chat' | 'topic' | 'url'; label: string };
    /** What the token is called on the plugin's side, for the field's label; null where the desktop sets no token. */
    tokenLabel: string | null;
    /** The label of the server field for the one plugin that takes a server of its own (ntfy); null for the others. */
    serverLabel: string | null;
    /** A sentence for under the fields: what the desktop does not set for this plugin, and where that is set instead. */
    note: string | null;
    /** Whether the settings file holds the desktop's keys for this plugin. Neither the token nor the target comes back. */
    configured: boolean;
    /**
     * What the runtime answered for the plugin when it last read its plugins: whether its package and configuration
     * are valid, and how many grants it holds. Null while the runtime is not running or does not list the plugin.
     */
    runtime: { valid: boolean; grants: number } | null;
    /** Whether the plugin's notification binding is in the settings file; null for the forwarder, which has none. */
    binding: boolean | null;
}

/** `setup.status()`: what the Connect cards show. */
export interface DesktopSetup {
    /** The agent hosts and key providers, found or not. */
    hosts: DesktopHost[];
    reply: DesktopReply;
    /** The version of the runtime the desktop drives; null while it is not running. */
    runtimeVersion: string | null;
    /** The version of the release the desktop carries. */
    bundleVersion: string;
    /**
     * When the runtime's version is not the bundle's, the desktop's sentence saying so, which names `jason update` as
     * the way to bring them together; null otherwise.
     */
    versionsDiffer: string | null;
    /** The plugins the release carries that the desktop can configure. */
    plugins: DesktopPlugin[];
    /**
     * Why the settings file could not be read, in the words of the desktop's parse or of the runtime's reload
     * (`settings_file_unreadable`); null when it could. While it cannot, no plugin is configured from here: the person
     * edits the file by hand and presses Reload.
     */
    settingsRefused: string | null;
}

/**
 * A place "Open in" can open the folder in, by the name the desktop knows it by: Claude Code or Codex as a new session in a
 * terminal, a plain terminal, an editor, the file manager, or Copy path. The page sends one of these names and never a path.
 */
export type DesktopOpenInPlace =
    'claude' | 'codex' | 'terminal' | 'vscode' | 'cursor' | 'antigravity' | 'windsurf' | 'zed' | 'files' | 'copy';

/**
 * What the split button is drawn from: the places this computer has, in the order to show them, the one the button's main
 * part opens (the place used last, else Claude Code), and the folder's name, never its path.
 */
export interface DesktopOpenInAnswer {
    /** A product's name (VS Code, Finder, Claude Code) is the label; a generic place (Terminal, Copy path) has an empty one and the page's own word. */
    places: { id: DesktopOpenInPlace; label: string }[];
    default: DesktopOpenInPlace;
    folder: string;
}

export interface JasonDesktop {
    /** The operating system, as Node names it. */
    platform: 'darwin' | 'win32' | 'linux';
    /** Calls `handler` with each command; the function it returns stops that. */
    onCommand(handler: (command: DesktopCommand) => void): () => void;
    theme: {
        /** The theme the desktop keeps, or null when the person never told it one; the page reads it before its first paint. */
        get(): Promise<'system' | 'light' | 'dark' | null>;
        /**
         * Tells the desktop the theme the person chose, `system`, `light` or `dark`, which it keeps and shows in its own
         * window and starting page. The page calls it as it starts and whenever the choice changes.
         */
        set(choice: 'system' | 'light' | 'dark'): Promise<void>;
    };
    mode: {
        /**
         * Which installation this run of the desktop is on: `live`, the person's own, or `demo`, the second installation
         * of sample data beside it, as the record in the person's installation said when the desktop started. The page
         * reads it once as it loads. `unopened` is the desktop's sentence when the record said Demo and the demo's
         * directory could not be made or used, so this run is on the person's own installation and the record says Live
         * again; null otherwise. `preparing` is true while the demo's sample is still being made behind the page, which
         * `onPreparing` then says false of once it is ready.
         */
        get(): Promise<{ mode: 'live' | 'demo'; unopened: string | null; preparing: boolean }>;
        /**
         * Writes the mode to the record in the person's installation and then starts the desktop again, which runs on
         * the installation it names: null once written and the new start asked for; the desktop's refusal, with nothing
         * started, when the record could not be written. Any other word is rejected.
         */
        set(mode: 'live' | 'demo'): Promise<DesktopRefusal | null>;
        /**
         * In the demo only: asks the person once, then stops the demo installation's runtime and starts the desktop again;
         * the next start removes the demo's two folders and makes the sample again. Answers null once it has begun, and also
         * when the person said no; the desktop's refusal, with the demo left as it was, when the runtime could not be stopped,
         * a folder of the demo's name is not one the demo made, or the request could not be written. Refused in Live, where
         * there is no demo to start over.
         */
        startOver(): Promise<DesktopRefusal | null>;
        /**
         * Whether the demo's sample is being made, said by the desktop when that changes: true as the making begins, false
         * once the sample is ready or its making failed. Until then the desktop refuses a switch and a start over. Answers
         * the way to stop listening.
         */
        onPreparing(handler: (preparing: boolean) => void): () => void;
    };
    openIn: {
        /**
         * The places this computer can open a folder in, for the folder of the campaign `campaignId` names (its recorded
         * folder) or, with none, the harness root; the desktop's refusal, with a sentence, when that folder is not recorded
         * or not on this computer. In the demo the folders are the demo installation's.
         */
        targets(campaignId?: string): Promise<DesktopOpenInAnswer | DesktopRefusal>;
        /**
         * Opens that folder in the place `target` names, which the desktop holds against its own list; `campaignId` is the
         * campaign of the chat that is open, or absent. The page never sends a path. Null once opened (or copied); a refusal
         * for a place not on this computer, a campaign whose folder is not there, or a program that could not be started. The
         * place is kept as the next default, Copy path excepted.
         */
        open(target: DesktopOpenInPlace, campaignId?: string): Promise<DesktopRefusal | null>;
    };
    pairing: {
        /**
         * Tells the desktop the id of the UI token the page holds now, once it has paired: never the token, which the
         * desktop refuses. The desktop then revokes the one the window held before, so the window keeps one pairing.
         */
        paired(tokenId: string): Promise<void>;
    };
    chat: {
        /**
         * Starts a turn of the conversation `chatId`, an id `chat.create` or `chat.list` gave; null once started, or a
         * refusal for a conversation the desktop does not keep. Every other end of a turn, a refused one included, is a
         * `done` event.
         */
        send(chatId: string, text: string, options: DesktopChatOptions): Promise<DesktopRefusal | null>;
        /** Stops the chat's running turn; a `done` event follows. Nothing running is nothing to stop. */
        stop(chatId: string): Promise<void>;
        /**
         * Answers a `permission` event once: `allow` lets the act happen, and `always` with it allows the same act for
         * the rest of this chat, held in memory only and never into the next. A second answer is refused, and an
         * answer to a request no longer held changes nothing; either way a `permission_gone` event says so.
         */
        answer(chatId: string, requestId: string, decision: { allow: boolean; always: boolean }): Promise<void>;
        /** Calls `handler` with every event of every chat; the function it returns stops that. */
        onEvent(handler: (chatId: string, event: DesktopChatEvent) => void): () => void;
        /** The conversations the desktop keeps, the one changed last first; one never sent to is not kept. */
        list(): Promise<DesktopConversation[]>;
        /** One conversation with what the page drew; an id the desktop does not keep is refused. */
        open(chatId: string): Promise<DesktopConversationRecord | DesktopRefusal>;
        /**
         * A conversation to start in: with a campaign, that campaign's own conversation, found or made with its name;
         * without one, a new set-up chat. `host` is the `DesktopHost.id` the next turn runs on: a conversation newly made
         * starts in Full access, its `mode` saying so in this answer, when the standing choice says new chats do and that
         * host can take Full access; otherwise in Ask first, with nothing said.
         */
        create(campaignId?: string, host?: string): Promise<DesktopConversation | DesktopRefusal>;
        /** Removes a conversation and its record: null once done. Refused while its turn runs. */
        remove(chatId: string): Promise<DesktopRefusal | null>;
        /**
         * "Start afresh": the hosts' sessions are dropped, and the entries and the campaign kept; null once done. Refused
         * while its turn runs.
         */
        restart(chatId: string): Promise<DesktopRefusal | null>;
        /**
         * Makes a set-up chat the conversation of a campaign its last `pick` named; its `attached` event comes before
         * this answers. Refused while its turn runs, for a conversation already about a campaign, for a campaign that has
         * a conversation, and for one the pick did not name.
         */
        attach(chatId: string, campaignId: string): Promise<DesktopConversation | DesktopRefusal>;
        /**
         * Renames a conversation, a set-up chat's or a campaign's, to a person's name: one line, with nothing unseen,
         * within sixty characters. Nothing replaces that name afterwards, and the campaign's own name is not changed. Its
         * `renamed` event comes before this answers. Refused for a conversation never sent to, and for a name with
         * nothing that shows.
         */
        rename(chatId: string, title: string): Promise<DesktopConversation | DesktopRefusal>;
        /**
         * Chooses a conversation's mode, on `host`, the `DesktopHost.id` the next turn runs on. `full_access` is refused
         * before anything is asked where it cannot apply (Codex, a host that does not drive the chat, a Claude Code too old
         * for it, the Feed), in the desktop's words; otherwise the desktop asks the person to confirm, every time while no
         * standing choice stands, and their no answers the conversation unchanged. Leaving Full access stops a turn that runs in it. A `mode` event comes before
         * this answers whenever the mode changed. Refused for a conversation the desktop does not keep.
         */
        setMode(chatId: string, mode: DesktopChatMode, host: string): Promise<DesktopConversation | DesktopRefusal>;
        /**
         * The Open folder button of a `notice` that carries a `folder`: shows that ended Full access folder in the system's
         * file manager. Only a folder of that name the desktop keeps is opened; anything else is refused.
         */
        openFolder(name: string): Promise<DesktopRefusal | null>;
        /**
         * The Feed, made on the first ask, with the journal lines the page hands it: each line of a kind the Feed takes,
         * about a campaign, with every id in its shape, and not taken before, goes at its end and is said as a `line`
         * event; any other is left out. No turn starts. An ask with `catchUp`, which the page makes at each load and when
         * its stream started afresh, also reads back the lines written since a few seconds before the Feed's newest. At
         * most 200 lines a call; more is refused.
         */
        feed(lines: DesktopFeedLine[], options?: { catchUp: true }): Promise<DesktopConversation | DesktopRefusal>;
    };
    fullAccess: {
        /** The standing choice of Full access, or null when none stands. */
        standing(): Promise<DesktopFullAccessStanding | null>;
        /**
         * Takes the standing choice back: the box is asked again at the next entry, and new chats start in Ask first. A
         * chat in Full access now stays in it. Null once done, or a refusal when its file could not be changed. No call
         * gives the choice: only the person's answer to the desktop's own box does.
         */
        takeBack(): Promise<DesktopRefusal | null>;
        /** Calls `handler` with the standing choice each time it changes, wherever; the function it returns stops that. */
        onChanged(handler: (standing: DesktopFullAccessStanding | null) => void): () => void;
    };
    links: {
        /**
         * The link the desktop holds for the page, a cold start's or one that came while no page took links; null when
         * none waits. From this call on, while this page is loaded, links come by `onOpen`.
         */
        take(): Promise<DesktopLink | null>;
        /** Calls `handler` with each link that comes while this page is loaded; the function it returns stops that. */
        onOpen(handler: (link: DesktopLink) => void): () => void;
    };
    setup: {
        status(): Promise<DesktopSetup>;
        /** Signs the Reply CLI in through the browser, then sets Jason's Reply plugin up with it. */
        connectReply(): Promise<void>;
        /** The same with a Reply API key the person pasted, for an account the browser sign-in does not reach. */
        connectReplyWithKey(key: string): Promise<void>;
        /** Keeps a key provider's key in the operating system's keychain; an empty key removes it. */
        setProviderKey(providerId: string, key: string): Promise<void>;
        /** Installs a program by a `DesktopHost.install` or `DesktopReply.cli.install` id, with npm. */
        installProgram(programId: string): Promise<void>;
        /**
         * Configures one of `status().plugins` in one edit of the settings file. It writes `Plugins:Config:<id>` (the
         * token where the plugin reads one from there; the forwarder's `url` from `target`), `Plugins:Grants:<id>:Http`,
         * and for the three notification plugins `Notifications:Plugins:<id>:Binding` with `target` under the plugin's
         * own key. Every key the desktop does not own is kept and the file is written whole beside itself and renamed
         * over; a file the desktop cannot parse is refused, never overwritten.
         *
         * A value absent keeps what the file holds; an empty one removes it: `token: ""` removes the token key and
         * `server: ""` the binding's server, putting the grant back to `["*"]`. `target: ""` is refused, since every
         * plugin's binding requires its target. `server` is ntfy's alone (the plugin whose `serverLabel` is set; any
         * other is refused as validation), checked before anything is written against the binding's own pattern and
         * the host rule, `https`, or `http` only to localhost, 127.0.0.1 or ::1; it goes into the binding beside the
         * topic, and the grant becomes the URL's host, with its port when that is not the scheme's default.
         *
         * Then the runtime reads its plugins again and the desktop reads them back. With no values at all the call
         * opens the file for nothing and only does that, which is what Reload is; a refusal the reload answers is what
         * `status().settingsRefused` then says. `status()` says what the runtime answered, which is "configured", never
         * that anything was delivered. No token ever comes back.
         */
        configurePlugin(
            pluginId: string,
            settings: { token?: string; target?: string; server?: string },
        ): Promise<void>;
        /**
         * Disconnects one of the three notification plugins (slack, telegram, ntfy; the journal forwarder and any other
         * id are refused), so that it is no longer a channel that is on: `Plugins:Config:<id>` and the entry under
         * `Notifications:Plugins:<id>` are removed whole, except that an entry whose `Enabled` is false keeps it and loses
         * only its `Binding`, and a section left empty is dropped. Its grant, `Plugins:Grants:<id>`, stays, except the
         * Http grant derived from a server of the plugin's own, which goes with the server; every other key and its
         * package stay. Then the runtime reads its plugins again, as after `configurePlugin`; a settings file the desktop
         * cannot parse or keep is refused, never overwritten.
         */
        disconnectPlugin(pluginId: string): Promise<void>;
        /**
         * Connects a new account by a link the Reply CLI makes, which the desktop opens in the person's browser: a
         * LinkedIn account under `name` (one line, 1 to 100 characters), a mailbox for its `provider`. `sharedPage`
         * asks for the account's shared mailbox page instead, and only after this ask answered the offer
         * `shared_page`; the desktop then asks the person in a box of its own, with the CLI's warning, first.
         * `copy` asks the desktop to put a link that expires on the clipboard instead of opening it, for the person to
         * hand on; the shared mailbox page is never copied, whatever the page asks.
         */
        replyConnect(
            channel: DesktopReplyChannel,
            options: ({ name: string } | { provider: 'gmail' | 'office365'; sharedPage?: boolean }) & {
                copy?: boolean;
            },
        ): Promise<DesktopReplyOpened | DesktopReplyCopied | DesktopReplyRefusal>;
        /** Reconnects an account `accounts` lists, the same way; `sharedPage` for a mailbox only, and `copy`, as above. */
        replyReconnect(
            channel: DesktopReplyChannel,
            accountId: string,
            options?: { sharedPage?: boolean; copy?: boolean },
        ): Promise<DesktopReplyOpened | DesktopReplyCopied | DesktopReplyRefusal>;
        /**
         * A mailbox's daily limit: read without `daily`, set to it with one, after the person's yes in the desktop's
         * own box.
         */
        replyLimits(accountId: string, daily?: number): Promise<{ daily: number | null } | DesktopReplyRefusal>;
        /** Pauses a LinkedIn account `accounts` lists, after the person's yes in the desktop's own box. */
        replyPause(accountId: string): Promise<DesktopReplyDone | DesktopReplyRefusal>;
        /** Resumes a LinkedIn account `accounts` lists, after the person's yes in the desktop's own box. */
        replyResume(accountId: string): Promise<DesktopReplyDone | DesktopReplyRefusal>;
        /** Lets a mailbox its provider stopped send again, after the person's yes in the desktop's own box. */
        replyUnblock(accountId: string): Promise<DesktopReplyDone | DesktopReplyRefusal>;
        /**
         * Deletes an account `accounts` lists from Reply, after the person's yes in the desktop's own box. `force` deletes
         * a LinkedIn account sequences use, only after a delete of it answered the offer `force`, and after a box of
         * its own.
         */
        replyDelete(
            channel: DesktopReplyChannel,
            accountId: string,
            options?: { force?: boolean },
        ): Promise<DesktopReplyDone | DesktopReplyRefusal>;
        /** Calls `handler` whenever what `status()` answers may have changed; the function it returns stops that. */
        onChanged(handler: () => void): () => void;
    };
}

declare global {
    interface Window {
        /** Present only inside the desktop application. */
        jasonDesktop?: JasonDesktop;
    }
}
