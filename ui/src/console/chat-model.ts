// Pure: a chat as the page keeps it, and what each event of the desktop's does to it. The desktop runs the turn and
// keeps the conversation, what the page drew included; the page holds what it shows, and a conversation reopened is
// drawn from what the desktop kept by the same code as a live turn. A message the person sends while a turn runs
// waits here, one at a time, and nowhere else: it becomes the next turn when this one ends well. When the turn fails, or
// the moment the person stops it, the message is given back: into the box when the box is empty, and otherwise kept in
// sight as not sent. Two texts are never merged, and neither is dropped.
import type {
    DesktopChatEvent,
    DesktopConversation,
    DesktopConversationRecord,
    DesktopKeptEntry,
} from '../platform/desktop';

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
    | { kind: 'error'; text: string }
    /** The campaigns a set-up turn made, for the person to pick the one this chat is about. */
    | { kind: 'pick'; campaignIds: string[] }
    /** The set-up chat became the conversation of the campaign named: said in the page, not kept by the desktop. */
    | { kind: 'attached'; title: string }
    /** The turn started a fresh session of the host, and why: said in the page, not kept by the desktop. */
    | { kind: 'fresh'; reason: 'host_changed' | 'not_found' }
    /** The person started the conversation afresh: said in the page, not kept by the desktop. */
    | { kind: 'afresh' };

export interface Chat {
    /** The conversation's id, the desktop's, which every turn of it carries. */
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
    /** How many of the oldest entries the desktop let go to keep the conversation within its size. */
    dropped: number;
    /** Whether the person asked to remove the conversation and has not yet said again. */
    removing: boolean;
}

/** How much of a set-up chat's first message names it, as the desktop names it. */
const titleLength = 60;

/** A set-up chat's title from the person's first message: its first sixty characters, on one line. */
function titleOf(text: string): string {
    const characters = Array.from(text.replace(/\s+/g, ' ').trim());
    return characters.length === 0 ? 'New chat' : characters.slice(0, titleLength).join('');
}

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
        dropped: 0,
        removing: false,
    };
}

/** What the desktop kept of a thing the page drew, as the page draws it; a kept card waits for nothing. */
function entryOf(entry: DesktopKeptEntry): Entry {
    switch (entry.kind) {
        case 'step':
            return {
                kind: 'step',
                step: entry.detail === undefined ? { text: entry.text } : { text: entry.text, detail: entry.detail },
            };
        case 'permission':
            return {
                kind: 'permission',
                request: '',
                title: entry.title,
                detail: entry.detail,
                remember: null,
                answer: entry.answer ?? 'gone',
            };
        case 'pick':
            return { kind: 'pick', campaignIds: [...entry.campaignIds] };
        default:
            return { kind: entry.kind, text: entry.text };
    }
}

/** A conversation the desktop kept, opened: what the page drew before, and no turn running. */
export function fromRecord(record: DesktopConversationRecord, host: string | null): Chat {
    return {
        ...newChat(record.id, host),
        title: record.title,
        campaignId: record.campaignId,
        entries: record.entries.map(entryOf),
        dropped: record.dropped,
    };
}

/** The person picked the campaign a set-up chat is about, and the desktop made it that campaign's conversation. */
export const pickedFor = (chat: Chat, conversation: DesktopConversation): Chat => ({
    ...chat,
    campaignId: conversation.campaignId,
    title: conversation.title,
});

/**
 * The person's words go into the chat and its turn starts; the first words of a chat give it its title. `resent` says
 * the words were the waiting message, which the turn gives back if it fails.
 */
export function said(chat: Chat, text: string, resent = false): Chat {
    const first = chat.campaignId === null && !chat.entries.some((each) => each.kind === 'user');
    return {
        ...chat,
        title: first ? titleOf(text) : chat.title,
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
        case 'attached':
            return {
                ...chat,
                campaignId: event.campaignId,
                title: event.title,
                entries: [...chat.entries, { kind: 'attached', title: event.title }],
            };
        case 'fresh':
            return { ...chat, entries: [...chat.entries, { kind: 'fresh', reason: event.reason }] };
        case 'pick':
            return { ...chat, entries: [...chat.entries, { kind: 'pick', campaignIds: [...event.campaignIds] }] };
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

/** Something the desktop refused, said in the chat in its words; a turn that runs goes on. */
export const told = (chat: Chat, error: string): Chat => ({
    ...chat,
    entries: [...chat.entries, { kind: 'error', text: error }],
});

/** The person asked to start the conversation afresh, and the desktop did. */
export const afresh = (chat: Chat): Chat => ({ ...chat, entries: [...chat.entries, { kind: 'afresh' }] });

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
