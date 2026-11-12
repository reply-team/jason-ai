import type { JournalEntry } from '../api/types';
import type {
    DesktopChatEvent,
    DesktopChatOptions,
    DesktopCommand,
    DesktopConversation,
    DesktopConversationRecord,
    DesktopRefusal,
    DesktopSetup,
    JasonDesktop,
} from '../platform/desktop';
import { JournalStream, type StreamSocket } from '../platform/journal-stream';
import { unseen } from '../console/chat-model';
import { fixtures } from './fixtures';

// A stand-in for Jason Desktop's seam, and for the journal stream, as the console's screen tests want them: every call
// recorded, every event the test's to send. Its conversations behave as the desktop's do: one per campaign, kept from
// their first turn, listed newest first, refused in the desktop's sentences and answered as a value, a turn running
// from its send to its `done`, and a pick taken only from the conversation's last one.

/** Every effort level, as Claude Code's aliases take them. */
const every = ['low', 'medium', 'high', 'xhigh', 'max'] as const;

export const setupFound: DesktopSetup = {
    hosts: [
        {
            id: 'claude',
            label: 'Claude Code',
            kind: 'program',
            found: true,
            version: '2.1.300',
            drivesChat: true,
            install: null,
            belowFloor: null,
            fullAccess: null,
            models: [
                { id: 'opus', label: 'Opus', efforts: [...every] },
                { id: 'sonnet', label: 'Sonnet', efforts: [...every] },
                { id: 'haiku', label: 'Haiku', efforts: [] },
            ],
            typedModel: true,
            modelsUnread: null,
        },
        {
            id: 'codex',
            label: 'Codex',
            kind: 'program',
            found: false,
            version: null,
            drivesChat: true,
            install: 'codex',
            belowFloor: null,
            fullAccess: 'Full access runs on Claude Code only.',
            models: [{ id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', efforts: ['low', 'high'] }],
            typedModel: false,
            modelsUnread: null,
        },
        {
            id: 'gemini',
            label: 'Gemini CLI',
            kind: 'program',
            found: true,
            version: '0.9.0',
            drivesChat: false,
            install: null,
            belowFloor: null,
            fullAccess: null,
            models: [],
            typedModel: false,
            modelsUnread: null,
        },
        {
            id: 'deepseek',
            label: 'DeepSeek',
            kind: 'provider',
            found: false,
            version: null,
            drivesChat: true,
            install: null,
            belowFloor: null,
            fullAccess: null,
            models: [
                { id: 'deepseek-v4-pro', label: 'deepseek-v4-pro', efforts: ['low', 'high', 'max'] },
                { id: 'deepseek-flash', label: 'deepseek-flash', efforts: ['low', 'high', 'max'] },
            ],
            typedModel: false,
            modelsUnread: null,
        },
    ],
    reply: {
        cli: { found: true, version: '0.19.0', install: null },
        signedIn: true,
        user: 'person@example.test',
        wired: true,
        accounts: [
            { id: 'acc_1', channel: 'email', name: 'Sales inbox', address: 'sales@example.test', status: 'connected' },
        ],
    },
    runtimeVersion: '0.34.0',
    bundleVersion: '0.34.0',
    plugins: [
        {
            id: 'telegram',
            label: 'Telegram',
            description: 'Tells you when something waits.',
            target: { key: 'chat', label: 'Chat id' },
            tokenLabel: 'Bot token',
            serverLabel: null,
            note: null,
            configured: false,
            runtime: { valid: true, grants: 0 },
            binding: false,
        },
        {
            id: 'ntfy',
            label: 'ntfy',
            description: 'Tells a topic you follow when something waits.',
            target: { key: 'topic', label: 'Topic' },
            tokenLabel: null,
            serverLabel: 'Server (optional)',
            note: 'A server that needs a token takes it as the plugin README says.',
            configured: true,
            runtime: { valid: true, grants: 1 },
            binding: true,
        },
        {
            id: 'journal-forwarder',
            label: 'Journal forwarder',
            description: 'Sends each journal line to an address of yours.',
            target: { key: 'url', label: 'Receiver URL' },
            tokenLabel: 'Bearer token',
            serverLabel: null,
            note: null,
            configured: true,
            runtime: { valid: false, grants: 1 },
            binding: null,
        },
    ],
    versionsDiffer: null,
    settingsRefused: null,
};

const efforts: readonly string[] = ['low', 'medium', 'high', 'xhigh', 'max'];
const modelShape = /^[A-Za-z0-9][A-Za-z0-9._:[\]/-]{0,99}$/;

/**
 * Why the desktop refuses a send's model or effort before any host starts, in its own sentences; null when it takes
 * them: an effort outside the five words, a model the host neither lists nor takes typed, an effort the model named
 * does not take.
 */
function refusalOf(status: DesktopSetup, { host, model, effort }: DesktopChatOptions): string | null {
    if (effort !== undefined && !efforts.includes(effort))
        return 'That effort is not one the desktop passes to a host.';
    const listed = status.hosts.find((each) => each.id === host);
    const offer = listed?.models.find((each) => each.id === model);
    if (model !== undefined && offer === undefined && !(listed?.typedModel === true && modelShape.test(model)))
        return 'That model is not one this host offers.';
    if (effort !== undefined && !offer?.efforts.includes(effort)) return 'That effort is not one this model takes.';
    return null;
}

export interface StubDesktop extends JasonDesktop {
    calls: { name: string; args: unknown[] }[];
    emit(chatId: string, event: DesktopChatEvent): void;
    /** Sends a command from the desktop's menu or shortcuts, as the main process does. */
    command(name: DesktopCommand): void;
    changed(): void;
    status: DesktopSetup;
    /**
     * Calls the test refuses, by name, with the sentence: a chat call answers it as a value (`{ refused }`), as the
     * desktop does; a setup call rejects with it.
     */
    refuse: Map<string, string>;
    /** The conversations the stand-in has, by id, as the desktop's records hold them. */
    kept: Map<string, DesktopConversationRecord>;
    /** A campaign's name, as the desktop reads it from the runtime when it makes that campaign's conversation. */
    names: Map<string, string>;
    /** Keeps a conversation as a desktop that kept it before this window opened. */
    keep(record: Partial<DesktopConversationRecord> & { id: string }): DesktopConversationRecord;
    /** What the person answers the desktop's own confirmation of Full access with; yes unless a test says no. */
    confirmFullAccess: boolean;
}

/**
 * A stand-in for the desktop, as the window really is: it opens on the start screen and sends no command by itself.
 * `newChat` has it send the menu's New chat command as the console starts, which opens a chat; `chatDesktop` is that.
 */
export function stubDesktop(status: DesktopSetup = setupFound, options: { newChat?: boolean } = {}): StubDesktop {
    const chatHandlers = new Set<(chatId: string, event: DesktopChatEvent) => void>();
    const changeHandlers = new Set<() => void>();
    const commandHandlers = new Set<(command: DesktopCommand) => void>();
    const calls: { name: string; args: unknown[] }[] = [];
    const refuse = new Map<string, string>();
    const call =
        (name: string) =>
        (...args: unknown[]): Promise<void> => {
            calls.push({ name, args });
            const reason = refuse.get(name);
            return reason === undefined ? Promise.resolve() : Promise.reject(new Error(reason));
        };
    /** A chat call: a refusal the test set, or one the call itself makes, answered as a value. */
    const answering =
        <T>(name: string, value: (...args: unknown[]) => T | DesktopRefusal) =>
        (...args: unknown[]): Promise<T | DesktopRefusal> => {
            calls.push({ name, args });
            const reason = refuse.get(name);
            return Promise.resolve(reason !== undefined ? { refused: reason } : value(...args));
        };
    // The conversations, as the desktop keeps them: one per campaign, any number of set-up chats.
    const kept = new Map<string, DesktopConversationRecord>();
    /** The conversations sent to, or kept before the window opened: the ones the list says. */
    const listed = new Set<string>();
    /** By conversation, the campaigns its last pick named. */
    const picks = new Map<string, string[]>();
    const names = new Map<string, string>([['cmp_1', 'Spring']]);
    /** The conversations a person renamed, whose name a hand-over keeps. */
    const namedByPerson = new Set<string>();
    let made = 0;
    let moment = Date.parse('2026-11-11T10:00:00.000Z');
    const now = () => new Date((moment += 1000)).toISOString();
    const notKept = { refused: 'That conversation is not one this desktop keeps.' };
    const answeringStill = (act: string) => ({
        refused: `This conversation is still answering. Wait for it to finish, or stop it, then ${act}.`,
    });
    const summaryOf = ({
        id,
        campaignId,
        title,
        updatedAt,
        running,
        mode,
    }: DesktopConversationRecord): DesktopConversation => ({
        id,
        campaignId,
        title,
        updatedAt,
        running,
        mode,
    });
    const keep = (record: Partial<DesktopConversationRecord> & { id: string }): DesktopConversationRecord => {
        const full: DesktopConversationRecord = {
            campaignId: null,
            title: 'New chat',
            updatedAt: '2026-11-11T00:00:00.000Z',
            running: false,
            mode: 'ask_first',
            entries: [],
            dropped: 0,
            chosen: null,
            ...record,
        };
        kept.set(full.id, full);
        listed.add(full.id);
        const pick = [...full.entries].reverse().find((entry) => entry.kind === 'pick');
        if (pick?.kind === 'pick') picks.set(full.id, pick.campaignIds);
        return full;
    };
    const own = (id: unknown): DesktopConversationRecord | undefined =>
        typeof id === 'string' ? kept.get(id) : undefined;
    const stub: StubDesktop = {
        platform: 'win32',
        calls,
        refuse,
        status,
        kept,
        names,
        keep,
        confirmFullAccess: true,
        onCommand(handler) {
            commandHandlers.add(handler);
            if (options.newChat === true) queueMicrotask(() => handler('chat.new'));
            return () => commandHandlers.delete(handler);
        },
        chat: {
            send: answering('chat.send', (id, _text, options) => {
                const record = own(id);
                if (!record) return notKept;
                // The desktop refuses what it does not pass to a host with a `done` that says why, and keeps nothing.
                const { host, model, effort } = options as DesktopChatOptions;
                const why = refusalOf(stub.status, options as DesktopChatOptions);
                if (why !== null) {
                    chatHandlers.forEach((handler) => handler(record.id, { kind: 'done', error: why }));
                    return null;
                }
                // What the desktop keeps of a send: the host, the model and the effort it named.
                record.chosen = { host, model: model ?? null, effort: effort ?? null };
                record.running = true;
                record.updatedAt = now();
                listed.add(record.id);
                return null;
            }),
            stop: call('chat.stop'),
            answer: call('chat.answer'),
            onEvent(handler) {
                chatHandlers.add(handler);
                return () => chatHandlers.delete(handler);
            },
            // The list refuses nothing: one that fails, as a test sets in `refuse`, rejects.
            list: (): Promise<DesktopConversation[]> => {
                calls.push({ name: 'chat.list', args: [] });
                const reason = refuse.get('chat.list');
                if (reason !== undefined) return Promise.reject(new Error(reason));
                return Promise.resolve(
                    [...kept.values()]
                        .filter((each) => listed.has(each.id))
                        .sort((a, b) =>
                            a.updatedAt < b.updatedAt ? 1 : a.updatedAt > b.updatedAt ? -1 : a.id < b.id ? 1 : -1,
                        )
                        .map(summaryOf),
                );
            },
            open: answering('chat.open', (id) => {
                const record = own(id);
                return record ? structuredClone(record) : notKept;
            }),
            create: answering('chat.create', (campaignId): DesktopConversation => {
                const campaign = typeof campaignId === 'string' ? campaignId : null;
                const existing = [...kept.values()].find((each) => campaign !== null && each.campaignId === campaign);
                if (existing) return summaryOf(existing);
                const id = `conv_01JD${String(++made).padStart(22, '0')}`;
                const title = campaign === null ? 'New chat' : (names.get(campaign) ?? campaign);
                const record = keep({ id, campaignId: campaign, title, updatedAt: now() });
                // Made and never sent to: the window's that made it, not yet listed.
                listed.delete(id);
                return summaryOf(record);
            }),
            remove: answering('chat.remove', (id) => {
                const record = own(id);
                if (!record) return notKept;
                if (record.running) return answeringStill('remove it');
                kept.delete(record.id);
                listed.delete(record.id);
                return null;
            }),
            restart: answering('chat.restart', (id) => {
                const record = own(id);
                if (!record) return notKept;
                if (record.running) return answeringStill('start it afresh');
                if (listed.has(record.id)) record.updatedAt = now();
                return null;
            }),
            attach: answering('chat.attach', (id, campaignId) => {
                const record = own(id);
                if (!record) return notKept;
                if (record.running) return answeringStill('choose its campaign');
                if (record.campaignId !== null) return { refused: 'This conversation is already about a campaign.' };
                if ([...kept.values()].some((each) => each.campaignId === campaignId))
                    return { refused: 'That campaign has its conversation already. Open it from the campaign.' };
                if (!(picks.get(record.id) ?? []).includes(campaignId as string))
                    return { refused: 'That campaign is not one this chat made. Open it from the campaign.' };
                record.campaignId = campaignId as string;
                // A name a person gave stays, as the desktop keeps it.
                if (!namedByPerson.has(record.id))
                    record.title = names.get(campaignId as string) ?? (campaignId as string);
                record.updatedAt = now();
                // The desktop says the hand-over before it answers.
                stub.emit(record.id, { kind: 'attached', campaignId: record.campaignId, title: record.title });
                return summaryOf(record);
            }),
            rename: answering('chat.rename', (id, title) => {
                const record = own(id);
                if (!record) return notKept;
                if (!listed.has(record.id))
                    return { refused: 'Send this chat a message first; then it can be renamed.' };
                if (typeof title === 'string' && title.length > 10_000)
                    return { refused: 'That name is too long. Keep it to a short line.' };
                // As the desktop cleans it: one line, nothing unseen, cut at sixty characters, never in one.
                const line =
                    typeof title === 'string'
                        ? title.replace(/\s+/g, ' ').replace(unseen, '').replace(/\s+/g, ' ').trim()
                        : '';
                const name = Array.from(line).slice(0, 60).join('');
                if (name === '') return { refused: 'A name needs at least one character that shows.' };
                record.title = name;
                namedByPerson.add(record.id);
                record.updatedAt = now();
                // The desktop says the new title before it answers.
                stub.emit(record.id, { kind: 'renamed', title: name });
                return summaryOf(record);
            }),
            setMode: answering('chat.setMode', (id, mode, host) => {
                const record = own(id);
                if (!record) return notKept;
                if (mode !== 'ask_first' && mode !== 'plan_only' && mode !== 'full_access')
                    return { refused: 'That is not a mode of the chat.' };
                if (mode === record.mode) return summaryOf(record);
                // Full access is refused before anything is asked on a host where it cannot apply, as the desktop does.
                const listed = stub.status.hosts.find((each) => each.id === host);
                if (mode === 'full_access' && (listed === undefined || listed.fullAccess !== null))
                    return { refused: listed?.fullAccess ?? 'That is not an agent host this desktop knows.' };
                // Full access waits for the person's yes to the desktop's own box; a no answers the conversation as it was.
                if (mode === 'full_access' && !stub.confirmFullAccess) return summaryOf(record);
                record.mode = mode;
                // The desktop says the change before it answers.
                stub.emit(record.id, { kind: 'mode', mode });
                return summaryOf(record);
            }),
        },
        setup: {
            status: () => {
                calls.push({ name: 'setup.status', args: [] });
                const reason = refuse.get('setup.status');
                return reason === undefined ? Promise.resolve(stub.status) : Promise.reject(new Error(reason));
            },
            connectReply: call('setup.connectReply'),
            connectReplyWithKey: call('setup.connectReplyWithKey'),
            setProviderKey: call('setup.setProviderKey'),
            installProgram: call('setup.installProgram'),
            configurePlugin: call('setup.configurePlugin'),
            disconnectPlugin: call('setup.disconnectPlugin'),
            onChanged(handler) {
                changeHandlers.add(handler);
                return () => changeHandlers.delete(handler);
            },
        },
        emit: (chatId, event) => {
            // What the desktop keeps of an event: the turn's end, and the pick its turn made.
            const record = kept.get(chatId);
            if (record && event.kind === 'pick') picks.set(chatId, [...event.campaignIds]);
            if (record && event.kind === 'done') {
                record.running = false;
                record.updatedAt = now();
            }
            chatHandlers.forEach((handler) => handler(chatId, event));
        },
        changed: () => changeHandlers.forEach((handler) => handler()),
        command: (name) => commandHandlers.forEach((handler) => handler(name)),
    };
    return stub;
}

export interface StubStream {
    stream: JournalStream;
    /** The address and protocols of every connection the stream opened. */
    opened: { url: string; protocols: string[] }[];
    open(): void;
    send(line: JournalEntry): void;
}

export function stubStream(): StubStream {
    let socket: StreamSocket | null = null;
    const opened: { url: string; protocols: string[] }[] = [];
    const stream = new JournalStream({
        origin: 'http://127.0.0.1:7781',
        token: () => 'tok',
        open: (url, protocols) => {
            opened.push({ url, protocols });
            socket = { onopen: null, onmessage: null, onclose: null, close: () => undefined };
            return socket;
        },
        later: (run, delay) => setTimeout(run, delay),
        cancel: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
        reachable: () => Promise.resolve(true),
    });
    return {
        stream,
        opened,
        open: () => socket?.onopen?.(),
        send: (line) => socket?.onmessage?.({ data: JSON.stringify(line) }),
    };
}

/**
 * A line as the stream sends it, which is an entry of `journal.list`: a recorded one, with only the fields a row needs
 * changed.
 */
export const journalLine = (fields: Partial<JournalEntry>): JournalEntry => ({
    ...fixtures['journal.list/oli'].response.items[0]!,
    ...fields,
});

/** A stand-in for a desktop whose person chose New chat as the window opened: the tests of a chat start from one. */
export const chatDesktop = (status: DesktopSetup = setupFound): StubDesktop => stubDesktop(status, { newChat: true });
