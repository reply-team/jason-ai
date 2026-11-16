import { signal } from '@preact/signals-react';
import type { ChatWait } from './needs-model';

// The chats that wait on a person, for Needs you's "Chats" and for the one count: a conversation whose newest turn of the
// AI ends in a choice nobody has made. They come from the conversations the desktop keeps, not from the runtime, and
// are read by the same pure function that decides whether the chat's own choice card is live, so the count and the
// card never disagree. Until that reading exists the value stays null: no Chats filter is drawn and no count includes a
// chat, rather than a "0" nobody read.

/** The chats that wait, newest first; null while they have not been read. */
export const chatWaits = signal<readonly ChatWait[] | null>(null);
