import type { JournalEntry } from '../api/types';
import type {
    DesktopChatEvent,
    DesktopCommand,
    DesktopConversation,
    DesktopConversationRecord,
    DesktopSetup,
    JasonDesktop,
} from '../platform/desktop';
import { JournalStream, type StreamSocket } from '../platform/journal-stream';
import { fixtures } from './fixtures';

// A stand-in for Jason Desktop's seam, and for the journal stream, as the console's screen tests want them: every call
// recorded, every event the test's to send.

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

export interface StubDesktop extends JasonDesktop {
    calls: { name: string; args: unknown[] }[];
    emit(chatId: string, event: DesktopChatEvent): void;
    /** Sends a command from the desktop's menu or shortcuts, as the main process does. */
    command(name: DesktopCommand): void;
    changed(): void;
    status: DesktopSetup;
    /** Calls whose promise the test settles: by name, the reason they reject with. */
    refuse: Map<string, string>;
    /** The conversations the stand-in keeps, by id, as the desktop's records hold them; the newest last. */
    kept: Map<string, DesktopConversationRecord>;
    /** A campaign's name, as the desktop reads it from the runtime when it makes that campaign's conversation. */
    names: Map<string, string>;
    /** Keeps a conversation as a desktop that kept it before this window opened. */
    keep(record: Partial<DesktopConversationRecord> & { id: string }): DesktopConversationRecord;
}

export function stubDesktop(status: DesktopSetup = setupFound): StubDesktop {
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
    const answering =
        <T>(name: string, value: (...args: unknown[]) => T) =>
        (...args: unknown[]): Promise<T> => {
            calls.push({ name, args });
            const reason = refuse.get(name);
            if (reason !== undefined) return Promise.reject(new Error(reason));
            try {
                return Promise.resolve(value(...args));
            } catch (error) {
                return Promise.reject(error);
            }
        };
    // The conversations, kept as the desktop keeps them: one per campaign, any number of set-up chats.
    const kept = new Map<string, DesktopConversationRecord>();
    const names = new Map<string, string>([['cmp_1', 'Spring']]);
    let made = 0;
    const summaryOf = ({ id, campaignId, title, updatedAt }: DesktopConversationRecord): DesktopConversation => ({
        id,
        campaignId,
        title,
        updatedAt,
    });
    const keep = (record: Partial<DesktopConversationRecord> & { id: string }): DesktopConversationRecord => {
        const full: DesktopConversationRecord = {
            campaignId: null,
            title: 'New chat',
            updatedAt: '2026-11-11T00:00:00.000Z',
            entries: [],
            dropped: 0,
            ...record,
        };
        kept.set(full.id, full);
        return full;
    };
    const own = (id: unknown): DesktopConversationRecord => {
        const record = typeof id === 'string' ? kept.get(id) : undefined;
        if (!record) throw new Error('That conversation is not one this desktop keeps.');
        return record;
    };
    const stub: StubDesktop = {
        platform: 'win32',
        calls,
        refuse,
        status,
        kept,
        names,
        keep,
        onCommand(handler) {
            commandHandlers.add(handler);
            return () => commandHandlers.delete(handler);
        },
        chat: {
            send: call('chat.send'),
            stop: call('chat.stop'),
            answer: call('chat.answer'),
            onEvent(handler) {
                chatHandlers.add(handler);
                return () => chatHandlers.delete(handler);
            },
            list: answering('chat.list', (): DesktopConversation[] => [...kept.values()].reverse().map(summaryOf)),
            open: answering('chat.open', (id): DesktopConversationRecord => structuredClone(own(id))),
            create: answering('chat.create', (campaignId): DesktopConversation => {
                const campaign = typeof campaignId === 'string' ? campaignId : null;
                const existing = [...kept.values()].find((each) => campaign !== null && each.campaignId === campaign);
                if (existing) return summaryOf(existing);
                const id = `conv_01JD${String(++made).padStart(22, '0')}`;
                const title = campaign === null ? 'New chat' : (names.get(campaign) ?? campaign);
                return summaryOf(keep({ id, campaignId: campaign, title }));
            }),
            remove: answering('chat.remove', (id) => {
                own(id);
                kept.delete(id as string);
            }),
            restart: answering('chat.restart', (id) => {
                own(id);
            }),
            attach: answering('chat.attach', (id, campaignId): DesktopConversation => {
                const record = own(id);
                record.campaignId = campaignId as string;
                record.title = names.get(campaignId as string) ?? (campaignId as string);
                return summaryOf(record);
            }),
        },
        setup: {
            status: () => {
                calls.push({ name: 'setup.status', args: [] });
                return Promise.resolve(stub.status);
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
        emit: (chatId, event) => chatHandlers.forEach((handler) => handler(chatId, event)),
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
