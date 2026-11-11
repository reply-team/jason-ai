// Pure: a chat as the page keeps it, and what each event of the desktop's does to it. The desktop runs the turn; the
// page keeps what was said, in memory, for as long as the window is open. A message the person sends while a turn runs
// waits here, one at a time, and nowhere else: it becomes the next turn when this one ends well. When the turn fails, or
// the moment the person stops it, the message is given back: into the box when the box is empty, and otherwise kept in
// sight as not sent. Two texts are never merged, and neither is dropped.
import type { DesktopChatEvent } from '../platform/desktop';

export type Answer = 'allow' | 'always' | 'deny' | 'gone';

/** A step of the agent's: the desktop's plain words for it, and what it acted on, when it said. */
export interface Step {
    text: string;
    detail?: string;
}

export type Entry =
    | { kind: 'user'; text: string }
    | { kind: 'text'; text: string }
    | { kind: 'step'; step: Step }
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
    /** What is typed in the message box. */
    draft: string;
    /** The message sent while the turn runs, which waits for its end; null when none waits. */
    waiting: string | null;
    /** Whether the waiting message is held, not sent: given back while the box held other words. It is never sent. */
    held: boolean;
    /** Whether the person asked the running turn to stop. */
    stopping: boolean;
    /** The message the turn's end made the next turn, until the page has handed it to the desktop. */
    outbox: string | null;
    /** The running turn's words when they were the waiting message, given back if that turn fails. */
    resent: string | null;
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
        draft: '',
        waiting: null,
        held: false,
        stopping: false,
        outbox: null,
        resent: null,
    };
}

/**
 * The person's words go into the chat and its turn starts; the first words of a chat give it its title. `resent` says
 * the words were the waiting message, which the turn gives back if it fails.
 */
export function said(chat: Chat, text: string, resent = false): Chat {
    const first = chat.campaignId === null && !chat.entries.some((each) => each.kind === 'user');
    const title = text.length > titleLength ? `${text.slice(0, titleLength - 1)}…` : text;
    return {
        ...chat,
        title: first ? title : chat.title,
        entries: [...chat.entries, { kind: 'user', text }],
        running: true,
        resent: resent ? text : null,
    };
}

/** What an event of the desktop's does to the chat it belongs to. */
export function received(chat: Chat, event: DesktopChatEvent): Chat {
    switch (event.kind) {
        case 'step': {
            const step: Step =
                event.detail === undefined ? { text: event.text } : { text: event.text, detail: event.detail };
            return { ...chat, entries: [...chat.entries, { kind: 'step', step }] };
        }
        case 'text':
            return { ...chat, entries: [...chat.entries, { kind: 'text', text: event.text }] };
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
        // The conversations the desktop keeps: this page does not draw them yet.
        case 'attached':
        case 'fresh':
        case 'pick':
            return chat;
        case 'done': {
            const over = ended(chat);
            if (event.error === null)
                return chat.waiting !== null && !chat.held ? { ...over, waiting: null, outbox: chat.waiting } : over;
            return givenBackAfterFailure(chat, {
                ...over,
                entries: [...over.entries, { kind: 'error', text: event.error }],
            });
        }
    }
}

/**
 * Words given back to the person: into the box when it is empty, else held in sight as not sent while nothing else
 * is. When both places are taken the words are the failed turn's own, shown in the thread, and stay there alone.
 */
function givenBack(chat: Chat, text: string): Chat {
    if (!chat.draft.trim()) return { ...chat, draft: text };
    if (chat.waiting === null) return { ...chat, waiting: text, held: true };
    return chat;
}

/** The waiting message, when one waits for the turn's end, taken out of waiting and given back. */
function released(chat: Chat): Chat {
    if (chat.waiting === null || chat.held) return chat;
    return givenBack({ ...chat, waiting: null }, chat.waiting);
}

/** A failed turn gives back the waiting message first, which is nowhere else, then the turn's own words if resent. */
function givenBackAfterFailure(before: Chat, after: Chat): Chat {
    const back = released({ ...after, waiting: before.waiting, held: before.held });
    return before.resent === null ? back : givenBack(back, before.resent);
}

/** What is typed in the box. */
export const typed = (chat: Chat, text: string): Chat => ({ ...chat, draft: text });

/**
 * The box's text sent while the turn runs: it waits for the turn's end and the box is cleared. While one already
 * waits, or is held, or the person asked the turn to stop, nothing changes and the text stays in the box, so a second
 * message replaces the first only by Replace.
 */
export function kept(chat: Chat): Chat {
    const text = chat.draft.trim();
    if (!chat.running || chat.stopping || chat.waiting !== null || !text) return chat;
    return { ...chat, waiting: text, draft: '' };
}

/** The box's text takes the waiting message's place, which goes into the box: the two are swapped, never merged. */
export function replaced(chat: Chat): Chat {
    const text = chat.draft.trim();
    if (chat.waiting === null || !text) return chat;
    return { ...chat, waiting: text, draft: chat.waiting };
}

/** The waiting message taken back into the box; only into an empty one, so no word typed there is overwritten. */
export function takenBack(chat: Chat): Chat {
    if (chat.waiting === null || chat.draft.trim()) return chat;
    return { ...chat, waiting: null, held: false, draft: chat.waiting };
}

/**
 * The person asked the running turn to stop. A stopped turn ends with the same `done` as a good one, so the waiting
 * message is given back now, before the desktop is asked, and the end finds nothing to send.
 */
export const stopAsked = (chat: Chat): Chat => (chat.running ? released({ ...chat, stopping: true }) : chat);

/** The page handed the turn's end's message to the desktop. */
export const handed = (chat: Chat): Chat => ({ ...chat, outbox: null });

/** A turn that ended: every request it still held is gone with it. */
function ended(chat: Chat): Chat {
    return {
        ...chat,
        running: false,
        stopping: false,
        resent: null,
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

/**
 * A turn the desktop refused to start, or a stop it refused: the chat says why, in the desktop's words, and waits for
 * the person again, with what waited given back as a failed turn gives it back.
 */
export function failed(chat: Chat, error: string): Chat {
    const over = ended(chat);
    return givenBackAfterFailure(chat, {
        ...over,
        outbox: null,
        entries: [...over.entries, { kind: 'error', text: error }],
    });
}

/** The steps between two messages fold into one line; the last group stays open while the turn runs. */
export type Shown = { kind: 'steps'; steps: Step[]; open: boolean } | Exclude<Entry, { kind: 'step' }>;

export function shown(chat: Chat): Shown[] {
    const out: Shown[] = [];
    let steps: Step[] = [];
    const flush = (open: boolean) => {
        if (steps.length) out.push({ kind: 'steps', steps, open });
        steps = [];
    };
    for (const entry of chat.entries) {
        if (entry.kind === 'step') {
            steps.push(entry.step);
            continue;
        }
        flush(false);
        out.push(entry);
    }
    flush(chat.running);
    return out;
}
