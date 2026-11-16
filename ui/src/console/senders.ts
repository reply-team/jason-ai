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
// approval's campaign on the send's channel, once per campaign and channel while a chat or Needs you is drawn.

/** One drawing's reads of the accounts, by campaign and channel: each read at most once while the drawing lasts. */
export interface SenderReads {
    readonly lookups: Signal<Readonly<Record<string, SenderLookup>>>;
}

export const senderReads = (): SenderReads => ({ lookups: signal({}) });

/** The reads of one drawing under way, by campaign and channel: one waits for the one before it. */
const under = new WeakMap<SenderReads, Map<string, Promise<void>>>();

/**
 * Reads the campaign's accounts on the send's channel, the whole team's, page after page while the account is not found
 * and the provider has more, at most `SENDER_PAGES` pages; a drawing reads each page once, whichever card asks. A failed
 * page is kept with what the runtime said; a pairing that has ended keeps nothing, and the session answers for it.
 */
export function readSender(api: Api, reads: SenderReads, asked: SenderAsked): Promise<void> {
    let chains = under.get(reads);
    if (chains === undefined) {
        chains = new Map();
        under.set(reads, chains);
    }
    const key = senderKey(asked);
    const next = (chains.get(key) ?? Promise.resolve()).then(() => pageUntilFound(api, reads, asked));
    chains.set(key, next);
    return next;
}

async function pageUntilFound(api: Api, reads: SenderReads, asked: SenderAsked): Promise<void> {
    const key = senderKey(asked);
    for (;;) {
        const lookup = reads.lookups.value[key];
        const body = nextSenderRequest(lookup, asked);
        if (body === null) return;
        let kept: SenderLookup;
        try {
            kept = withPage(lookup, await api.call<SenderListResponse>('sender.list', body));
        } catch (error) {
            if (error instanceof Unpaired) return;
            kept = withFailure(
                lookup,
                error instanceof ApiError
                    ? { code: error.code, message: error.message }
                    : { code: null, message: String(error) },
            );
        }
        reads.lookups.value = { ...reads.lookups.value, [key]: kept };
    }
}
