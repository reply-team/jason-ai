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
