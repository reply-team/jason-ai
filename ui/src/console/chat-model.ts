// Pure: a chat as the page keeps it, and what each event of the desktop's does to it. The desktop runs the turn; the
// page keeps what was said, in memory, for as long as the window is open.
import type { DesktopChatEvent } from '../platform/desktop';

export type Answer = 'allow' | 'always' | 'deny' | 'gone';

export type Entry =
    | { kind: 'user'; text: string }
    | { kind: 'text'; text: string }
    | { kind: 'step'; text: string }
    | {
          kind: 'permission';
          request: string;
          title: string;
          detail: string;
          /** What "always, in this chat" would cover, as the button prints it; null when it is not offered. */
          remember: string | null;
          answer: Answer | null;
      }
    | { kind: 'error'; text: string };

export interface Chat {
    /** The id the page made up for the chat, and every turn of it carries. */
    id: string;
    title: string;
    campaignId: string | null;
    /** The host the next turn runs on: a `DesktopHost.id`, or null when none that drives the chat was found. */
    host: string | null;
    entries: Entry[];
    running: boolean;
}

const titleLength = 48;

export function newChat(id: string, host: string | null, campaign?: { id: string; name: string }): Chat {
    return {
        id,
        title: campaign ? campaign.name : 'New chat',
        campaignId: campaign?.id ?? null,
        host,
        entries: [],
        running: false,
    };
}

/** The person's words go into the chat and its turn starts; the first words of a chat give it its title. */
export function said(chat: Chat, text: string): Chat {
    const first = chat.campaignId === null && !chat.entries.some((each) => each.kind === 'user');
    const title = text.length > titleLength ? `${text.slice(0, titleLength - 1)}…` : text;
    return {
        ...chat,
        title: first ? title : chat.title,
        entries: [...chat.entries, { kind: 'user', text }],
        running: true,
    };
}

/** What an event of the desktop's does to the chat it belongs to. */
export function received(chat: Chat, event: DesktopChatEvent): Chat {
    switch (event.kind) {
        case 'step':
        case 'text':
            return { ...chat, entries: [...chat.entries, { kind: event.kind, text: event.text }] };
        case 'permission':
            return {
                ...chat,
                entries: [
                    ...chat.entries,
                    {
                        kind: 'permission',
                        request: event.request,
                        title: event.title,
                        detail: event.detail,
                        remember: event.remember,
                        answer: null,
                    },
                ],
            };
        case 'permission_gone':
            return answered(chat, event.request, 'gone');
        case 'done': {
            const over = ended(chat);
            return event.error === null
                ? over
                : { ...over, entries: [...over.entries, { kind: 'error', text: event.error }] };
        }
    }
}

/** A turn that ended: every request it still held is gone with it. */
function ended(chat: Chat): Chat {
    return {
        ...chat,
        running: false,
        entries: chat.entries.map((each) =>
            each.kind === 'permission' && each.answer === null ? { ...each, answer: 'gone' } : each,
        ),
    };
}

/**
 * A request answered, or no longer held: the first of the two stands, so an answer the person gave stays shown when
 * the desktop says afterwards that the request no longer waits.
 */
export function answered(chat: Chat, request: string, answer: Answer): Chat {
    return {
        ...chat,
        entries: chat.entries.map((each) =>
            each.kind === 'permission' && each.request === request && each.answer === null ? { ...each, answer } : each,
        ),
    };
}

/** A request the desktop refused an answer to: whatever the page showed, it no longer waits. */
export const lost = (chat: Chat, request: string): Chat => ({
    ...chat,
    entries: chat.entries.map((each) =>
        each.kind === 'permission' && each.request === request ? { ...each, answer: 'gone' } : each,
    ),
});

/** A turn the desktop refused to start: the chat says why, in the desktop's words, and waits for the person again. */
export const failed = (chat: Chat, error: string): Chat => ({
    ...chat,
    running: false,
    entries: [...chat.entries, { kind: 'error', text: error }],
});

/** The steps between two messages fold into one line; the last group stays open while the turn runs. */
export type Shown = { kind: 'steps'; steps: string[]; open: boolean } | Exclude<Entry, { kind: 'step' }>;

export function shown(chat: Chat): Shown[] {
    const out: Shown[] = [];
    let steps: string[] = [];
    const flush = (open: boolean) => {
        if (steps.length) out.push({ kind: 'steps', steps, open });
        steps = [];
    };
    for (const entry of chat.entries) {
        if (entry.kind === 'step') {
            steps.push(entry.text);
            continue;
        }
        flush(false);
        out.push(entry);
    }
    flush(chat.running);
    return out;
}
