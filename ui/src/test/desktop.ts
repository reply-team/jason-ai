import type { JournalEntry } from '../api/types';
import type { DesktopChatEvent, DesktopCommand, DesktopSetup, JasonDesktop } from '../platform/desktop';
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
            version: '2.1.0',
            drivesChat: true,
            install: null,
        },
        {
            id: 'codex',
            label: 'Codex',
            kind: 'program',
            found: false,
            version: null,
            drivesChat: true,
            install: 'codex',
        },
        {
            id: 'gemini',
            label: 'Gemini CLI',
            kind: 'program',
            found: true,
            version: '0.9.0',
            drivesChat: false,
            install: null,
        },
        {
            id: 'deepseek',
            label: 'DeepSeek',
            kind: 'provider',
            found: false,
            version: null,
            drivesChat: true,
            install: null,
        },
    ],
    reply: {
        cli: { found: true, version: '0.19.0', install: null },
        signedIn: true,
        user: 'person@example.test',
        wired: true,
        accounts: [
            { id: 'acc_1', channel: 'email', name: 'Sales inbox', address: 'sales@example.test', status: 'active' },
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
    const stub: StubDesktop = {
        platform: 'win32',
        calls,
        refuse,
        status,
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
