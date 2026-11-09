/**
 * The seam between the page and Jason Desktop: what the desktop's preload puts on `window.jasonDesktop`, and all of
 * it. In a browser it is absent, and the page is the classic shell. The page is served from the runtime's loopback
 * origin in both; the desktop's main process checks every call's sender against that origin, so the page checks
 * nothing here. Every promise rejects with an Error whose message the page shows as text.
 */
export {};

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

/** What `chat.send` takes besides the text. */
export interface DesktopChatOptions {
    /** The agent host the turn runs on: the `id` of one of `setup.status().hosts` whose `drivesChat` is true. */
    host: string;
    /** A model the host offers, by the name the host takes; absent, the host's own default. */
    model?: string;
    /** The campaign the chat is about, when it was opened from one: `cmp_` and its ULID. */
    campaignId?: string;
}

/** One thing a running turn says, in the order the host says it. The chat id it belongs to comes beside it. */
export type DesktopChatEvent =
    /** A step the agent took: a tool it ran, in one line, as the host named it. */
    | { kind: 'step'; text: string }
    /** A message of the agent's, whole: shown as its own bubble, as text. */
    | { kind: 'text'; text: string }
    /**
     * The agent asks to do something the permission model does not settle on its own: `request` is the id to answer
     * by `chat.answer`, `title` the act in a few words, `detail` the command or path in full, and `remember` whether
     * "always, in this chat" may be offered for it.
     */
    | { kind: 'permission'; request: string; title: string; detail: string; remember: boolean }
    /**
     * A request no longer waits: it was answered, it timed out, or the desktop no longer holds it. An answer sent for
     * it after this changes nothing.
     */
    | { kind: 'permission_gone'; request: string }
    /** The turn is over: `error` is null when it ended well, else the host's own words for why it did not. */
    | { kind: 'done'; error: string | null };

/** An agent host or a key provider the chat's dropdown lists, found on this machine or not. */
export interface DesktopHost {
    /** What `chat.send`'s `host` names, and `setProviderKey`'s `providerId` for a key provider. */
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
}

/** An account Reply holds, as the Reply CLI lists it. */
export interface DesktopReplyAccount {
    id: string;
    channel: string;
    name: string;
    address: string;
    status: string;
}

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
}

/** A notification plugin the release carries, and whether it is connected. */
export interface DesktopPlugin {
    id: string;
    label: string;
    description: string;
    /** What the token is called on the plugin's side, for the field's label. */
    tokenLabel: string;
    /** Whether a token is set; the token itself never comes back. */
    connected: boolean;
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
    /** The notification plugins the release carries. */
    plugins: DesktopPlugin[];
}

export interface JasonDesktop {
    /** The operating system, as Node names it. */
    platform: 'darwin' | 'win32' | 'linux';
    /** Calls `handler` with each command; the function it returns stops that. */
    onCommand(handler: (command: DesktopCommand) => void): () => void;
    chat: {
        /** Starts a turn of the chat `chatId`, an id the page makes up and keeps for every turn of one chat. */
        send(chatId: string, text: string, options: DesktopChatOptions): Promise<void>;
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
        /** Sets the token of a notification plugin the release carries, and has the runtime read its plugins again. */
        setPluginToken(pluginId: string, token: string): Promise<void>;
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
