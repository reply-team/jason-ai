import { signal, type Signal } from '@preact/signals-react';
import { ApiError, Unpaired, type Api } from '../api/client';
import type { SenderListResponse } from '../api/types';
import {
    nextSenderRequest,
    senderKey,
    withFailure,
    withPage,
    type SenderAsked,
    type SenderLookup,
} from './senders-model';

// The reads behind an approval card's sender line (`senders-model.ts` says what the line says): `sender.list` of the
// approval's campaign on the send's channel. Cards drawn together share one read of a campaign's accounts; a card drawn
// after a read began (a new approval, the next item, a re-ask) reads again, so no line is older than its card. Each read
// is kept beside the later ones, so a card keeps its own answer whatever a later card's read finds or fails to find.

/** The reads of a chat's cards or of a panel, by campaign and channel, in the order they began. */
export interface SenderReads {
    readonly lookups: Signal<Readonly<Record<string, readonly SenderLookup[]>>>;
}

/** How many reads of one campaign and channel are kept: a card drawn before the oldest kept one shows a later one. */
const KEPT = 16;

export const senderReads = (): SenderReads => ({ lookups: signal({}) });

/** The order of drawings and reads: a card takes a mark when it is drawn, a read when it begins. */
let marks = 0;
export const senderMark = (): number => ++marks;

/** The lookup a card drawn at `drawn` shows: the first begun after it was drawn, never an older one. */
export function lookupFor(reads: SenderReads, asked: SenderAsked, drawn: number): SenderLookup | undefined {
    return (reads.lookups.value[senderKey(asked)] ?? []).find((lookup) => lookup.started > drawn);
}

/** The reads of a campaign and channel with `kept` in the place of the read begun when it began. */
function keeping(lookups: readonly SenderLookup[], kept: SenderLookup): readonly SenderLookup[] {
    const others = lookups.filter((lookup) => lookup.started !== kept.started);
    return [...others, kept].sort((a, b) => a.started - b.started).slice(-KEPT);
}

/** The reads under way, by holder and by campaign and channel: one waits for the one before it. */
const under = new WeakMap<SenderReads, Map<string, Promise<void>>>();

/**
 * Reads the campaign's accounts on the send's channel for a card drawn at `drawn`, the whole team's, page after page
 * while the account is not found and the provider has more, at most `SENDER_PAGES` pages. A read begun after the card
 * was drawn is shared; an older one is read again. A failed page is kept with what the runtime said; a pairing that
 * has ended keeps nothing, and the session answers for it.
 */
export function readSender(api: Api, reads: SenderReads, asked: SenderAsked, drawn: number): Promise<void> {
    let chains = under.get(reads);
    if (chains === undefined) {
        chains = new Map();
        under.set(reads, chains);
    }
    const key = senderKey(asked);
    const next = (chains.get(key) ?? Promise.resolve()).then(() => pageUntilFound(api, reads, asked, drawn));
    chains.set(key, next);
    return next;
}

async function pageUntilFound(api: Api, reads: SenderReads, asked: SenderAsked, drawn: number): Promise<void> {
    const key = senderKey(asked);
    for (;;) {
        const lookup = lookupFor(reads, asked, drawn);
        const body = nextSenderRequest(lookup, asked);
        if (body === null) return;
        const started = lookup?.started ?? senderMark();
        let kept: SenderLookup;
        try {
            kept = withPage(lookup, await api.call<SenderListResponse>('sender.list', body), started);
        } catch (error) {
            if (error instanceof Unpaired) return;
            kept = withFailure(
                lookup,
                error instanceof ApiError
                    ? { code: error.code, message: error.message }
                    : { code: null, message: String(error) },
                started,
            );
        }
        reads.lookups.value = { ...reads.lookups.value, [key]: keeping(reads.lookups.value[key] ?? [], kept) };
    }
}
