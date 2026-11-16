import { signal } from '@preact/signals-react';
import type { Api } from '../api/client';
import type { JournalEntry, Page } from '../api/types';
import { actKinds, personActed, type ActsRead } from './chat-model';

// Whether a person acted on an agent after its chat's last choice was asked: started or paused it, approved or rejected
// one of its approvals, answered one of its questions. Only a chat's last choice can be live at all, so each drawn chat
// holds at most one such check: read once when the chat is drawn, with one `journal.list` of the campaign and those five
// kinds since the moment the desktop kept the choice, and kept current while it stays drawn by the lines of the
// journal's stream the console follows. Nothing is written into the conversation: the lock is worked out, never kept.

/** One chat's check: the campaign and the moment it is about, and what is known. */
export interface ActsHeld {
    campaignId: string;
    at: string;
    read: ActsRead;
}

/** By chat id, the check of each chat drawn whose last choice may still be live. */
export const acts = signal<Readonly<Record<string, ActsHeld>>>({});

/** How many lines one page of the read asks for, and how many pages it reads before it says it does not know. */
const PAGE = 50;
const PAGES = 5;

const put = (chatId: string, held: ActsHeld | null): void => {
    const rest = Object.fromEntries(Object.entries(acts.value).filter(([id]) => id !== chatId));
    acts.value = held === null ? rest : { ...rest, [chatId]: held };
};

/**
 * Reads a person's acts on `campaignId` at or after `at` for the chat's last choice; a line of a person's locks it, and
 * a read that fails, or that pages five times and finds none while more remain, says it is not known. Returns the
 * function that forgets the check, which the chat calls once it is no longer drawn or asks another.
 */
export function readActs(api: Api, chatId: string, campaignId: string, at: string): () => void {
    const held: ActsHeld = { campaignId, at, read: 'reading' };
    put(chatId, held);
    const mine = () => acts.value[chatId]?.campaignId === campaignId && acts.value[chatId]?.at === at;
    const settle = (read: ActsRead) => {
        // A line the stream brought while the read was under way has already said acted; nothing unsays it.
        if (mine() && acts.value[chatId]!.read === 'reading') put(chatId, { campaignId, at, read });
    };
    void (async () => {
        try {
            let cursor: string | null = null;
            for (let page = 0; page < PAGES; page++) {
                const answer: Page<JournalEntry> = await api.call<Page<JournalEntry>>('journal.list', {
                    campaign_id: campaignId,
                    kinds: [...actKinds],
                    since: at,
                    limit: PAGE,
                    ...(cursor === null ? {} : { cursor }),
                });
                if (answer.items.some((line) => personActed(line, campaignId, at))) return settle('acted');
                if (answer.next_cursor === null) return settle('clear');
                cursor = answer.next_cursor;
            }
            settle('unread');
        } catch {
            settle('unread');
        }
    })();
    return () => {
        if (mine()) put(chatId, null);
    };
}

/** A line of the journal's stream: it locks each drawn chat's choice it is a person's act after. */
export function heardLine(line: JournalEntry): void {
    const next = Object.entries(acts.value).filter(
        ([, held]) => held.read !== 'acted' && personActed(line, held.campaignId, held.at),
    );
    if (next.length === 0) return;
    acts.value = { ...acts.value, ...Object.fromEntries(next.map(([id, held]) => [id, { ...held, read: 'acted' }])) };
}
