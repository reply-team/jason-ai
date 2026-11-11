import { signal } from '@preact/signals-react';
import type { Api } from '../api/client';
import { ApiError } from '../api/client';
import type { JournalEntry } from '../api/types';
import type { DesktopSetup, JasonDesktop } from '../platform/desktop';
import type { StreamState } from '../platform/journal-stream';
import type { Route } from '../router';
import type { Chat } from './chat-model';

// What the console holds while its window is open: nothing of it is kept anywhere else, and nothing of it is the
// runtime's to know. Every number and every line is the runtime's or the desktop's, as they said it last.

/** The journal's lines the stream brought, newest first; the console keeps the last ones only. */
export const lines = signal<readonly JournalEntry[]>([]);
export const keptLines = 200;
export const streamState = signal<StreamState>({ status: 'connecting', last: null, fresh: false });

export const setup = signal<DesktopSetup | null>(null);
/** What the desktop is doing for a Connect card, in a few words; null while it does nothing. */
export const setupBusy = signal<string | null>(null);
export const setupError = signal<string | null>(null);

export const chats = signal<Readonly<Record<string, Chat>>>({});

/** The moment ages are counted from, moved on by the console every half minute. */
export const clock = signal(Date.now());

export function keepLine(line: JournalEntry): void {
    if (lines.value.some((each) => each.id === line.id)) return;
    lines.value = [line, ...lines.value].slice(0, keptLines);
}

export function changeChat(id: string, change: (chat: Chat) => Chat): void {
    const chat = chats.value[id];
    if (chat) chats.value = { ...chats.value, [id]: change(chat) };
}

export const messageOf = (error: unknown): string =>
    error instanceof ApiError || error instanceof Error ? error.message : String(error);

/** The panels of the dock, by the name the dock knows each by. */
export type PanelName = 'needs' | 'campaigns' | 'activity' | 'connect' | 'page';

export interface Dock {
    show(panel: PanelName): void;
    /** Opens a new chat, about a campaign when it is given one, and shows it. */
    chat(campaign?: { id: string; name: string }): void;
    /** Shows a chat that is open. */
    showChat(id: string): void;
    /** Shows one of the browser shell's screens in the page panel. */
    page(to: Route): void;
}

export interface Console {
    api: Api;
    desktop: JasonDesktop;
    dock: Dock;
}

let current: Console | null = null;

/** The console mounted; the function it returns unmounts it. */
export function bind(console: Console): () => void {
    current = console;
    return () => {
        if (current === console) current = null;
    };
}

export function here(): Console {
    if (!current) throw new Error('The console is not mounted.');
    return current;
}
