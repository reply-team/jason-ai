import { computed, signal, type ReadonlySignal } from '@preact/signals-react';
import type { Api } from '../api/client';
import { ApiError } from '../api/client';
import type { JournalEntry } from '../api/types';
import type { DesktopConversation, DesktopSetup, JasonDesktop } from '../platform/desktop';
import type { StreamState } from '../platform/journal-stream';
import type { Route } from '../router';
import type { Opened } from './box-focus';
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
/** Whether the last read of the desktop's setup failed, beside `setupError`, which an act's refusal sets too. */
export const setupReadFailed = signal(false);

/** The chats the console has open, by the id of the conversation the desktop keeps for each. */
export const chats = signal<Readonly<Record<string, Chat>>>({});
/** Every conversation the desktop keeps, newest first, as it listed them last. */
export const conversations = signal<readonly DesktopConversation[]>([]);
/** Whether the desktop has listed its conversations since the console was bound: before that, none is not yet known. */
export const conversationsListed = signal(false);
/**
 * Asks the narrow window's drawer to put itself away, as a click in it does, for an act that shows something in the area
 * from outside the drawer's own controls: a row menu's Open, say. Each ask is one more.
 */
export const sideAway = signal(0);
/** The conversation whose chat is shown, which the sidebar marks in its sections; null while anything else is shown. */
export const currentChat = signal<string | null>(null);
/** The Feed's id, once the desktop answered the first ask; null before. */
export const feedId = signal<string | null>(null);
/** Whether the Feed took a line since the person last chose it: the sidebar marks it. Kept here only. */
export const feedUnseen = signal(false);

/** What the console's one area shows: a panel's name, or `chat:<id>`; none before the console is mounted. */
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

const chatSignals = new Map<string, ReadonlySignal<Chat | undefined>>();

/**
 * One chat's record as a signal of its own: it tells only when that chat changed, so what draws one chat is not drawn
 * again for a word typed in another.
 */
export function chatOf(id: string): ReadonlySignal<Chat | undefined> {
    let held = chatSignals.get(id);
    if (held === undefined) {
        held = computed(() => chats.value[id]);
        chatSignals.set(id, held);
    }
    return held;
}

export const messageOf = (error: unknown): string =>
    error instanceof ApiError || error instanceof Error ? error.message : String(error);

/** The panels the console's one area shows, by their names; `page` holds the browser shell's screens. */
export type PanelName = 'start' | 'dashboard' | 'needs' | 'campaigns' | 'activity' | 'connect' | 'page';

/**
 * The console's one area: it shows exactly one thing at a time, and nothing in it is closed, only chosen. A move made for
 * a person's act that opens a chat, or Start, is given where the focus stood (`opened`, `box-focus.ts`): the box of what
 * it shows takes the focus once drawn. A move given none moves no focus.
 */
export interface Dock {
    show(panel: PanelName, opened?: Opened): void;
    /**
     * Shows a conversation: the campaign's one, made on first use, when a campaign is given, else a new set-up chat.
     */
    chat(campaign?: { id: string; name: string }, opened?: Opened): void;
    /** Shows a chat that is open. */
    showChat(id: string, opened?: Opened): void;
    /** Forgets a chat that was taken away; when it was the one shown, Start is shown. */
    dropChat(id: string): void;
    /** Shows one of the browser shell's screens; an agent's Chat tab is its conversation, as `chat` shows it. */
    page(to: Route, opened?: Opened): void;
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
    conversationsListed.value = false;
    return () => {
        if (current === console) current = null;
    };
}

export function here(): Console {
    if (!current) throw new Error('The console is not mounted.');
    return current;
}

/** Whether a console is mounted, and so a desktop behind it; false for a part drawn on its own. */
export const mounted = (): boolean => current !== null;
