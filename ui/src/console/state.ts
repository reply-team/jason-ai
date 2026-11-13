import { signal } from '@preact/signals-react';
import type { Api } from '../api/client';
import { ApiError } from '../api/client';
import type { JournalEntry } from '../api/types';
import type { DesktopConversation, DesktopSetup, JasonDesktop } from '../platform/desktop';
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

/** The chats the console has open, by the id of the conversation the desktop keeps for each. */
export const chats = signal<Readonly<Record<string, Chat>>>({});
/** Every conversation the desktop keeps, newest first, as it listed them last. */
export const conversations = signal<readonly DesktopConversation[]>([]);
/** The conversation whose chat was brought to the front last, which the sidebar marks; null when none is open. */
export const currentChat = signal<string | null>(null);
/** The Feed's id, once the desktop answered the first ask; null before. */
export const feedId = signal<string | null>(null);
/** Whether the Feed took a line since the person last brought it to the front: the sidebar marks it. Kept here only. */
export const feedUnseen = signal(false);

/** The id of the dock's panel in front: a panel's name, or `chat:<id>`; none before the dock is ready. */
export const activePanel = signal<string | null>(null);
/** Why the conversations could not be listed or one opened, in the desktop's words; null when nothing failed. */
export const conversationsFailure = signal<string | null>(null);

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
export type PanelName = 'start' | 'dashboard' | 'needs' | 'campaigns' | 'activity' | 'connect' | 'page';

export interface Dock {
    show(panel: PanelName): void;
    /** Takes a panel out of the dock. */
    close(panel: PanelName): void;
    /**
     * Shows a conversation: the campaign's one, made on first use, when a campaign is given, else a new set-up chat.
     */
    chat(campaign?: { id: string; name: string }): void;
    /** Shows a chat that is open. */
    showChat(id: string): void;
    /** Takes a chat's tab out of the dock. */
    closeChat(id: string): void;
    /** Shows one of the browser shell's screens in the page panel. */
    page(to: Route): void;
}

export interface Console {
    api: Api;
    desktop: JasonDesktop;
    dock: Dock;
    /** Set when the console is bound in a browser tab over the desktop's bridge: what only the desktop's window may do is not offered. */
    tab?: boolean;
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

/** Whether the console was bound in a browser tab. The one fact every control a tab may not use is hidden by. */
export function inTab(): boolean {
    return current?.tab === true;
}
