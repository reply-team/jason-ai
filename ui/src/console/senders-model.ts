import type { Approval, SenderAccount, SenderListRequest, SenderListResponse } from '../api/types';
import type { Said } from './waiting-model';

// Pure: the sender line of an approval card: the sending name the provider holds now for the mailbox a send names, read with
// `sender.list` of the approval's campaign on the send's channel while the card is drawn, and found by the identifier the
// send names. It comes from the runtime's read and the approval's own arguments, never from what the AI wrote; it is a
// fact of the moment it was read, not a promise of what a recipient sees; and it holds no act: an approval is approved
// by its whole read alone (`approvalGate`).

/** The read a card's line needs: the approval's campaign, the channel its send consumes and the account it names. */
export interface SenderAsked {
    campaignId: string;
    channel: string;
    externalId: string;
}

/** The channels `sender.list` reads one at a time. */
const channels = new Set(['email', 'linkedin']);

const record = (value: unknown): Record<string, unknown> | null =>
    value !== null && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : null;

/**
 * What an approval's line reads: only an approval whose input names a sender, an account by its identifier, on a channel
 * of its own. A reply, a start and an enrolment name none, so they draw no line.
 */
export function senderAsked(approval: Approval | undefined): SenderAsked | null {
    if (approval === undefined) return null;
    const args = record(record(approval.subject['input'])?.['args']);
    const channel = args?.['channel'];
    const externalId = record(args?.['sender'])?.['external_id'];
    if (typeof channel !== 'string' || !channels.has(channel)) return null;
    if (typeof externalId !== 'string' || externalId === '') return null;
    return { campaignId: approval.campaign_id, channel, externalId };
}

/** How many accounts one page asks for, and how many pages a line reads before it stops looking. */
export const SENDER_PAGE = 100;
export const SENDER_PAGES = 5;

/** What the reads of one campaign's accounts on one channel found, page by page. */
export interface SenderLookup {
    /** The plugin that answered: the provider the line names. */
    provider: string;
    accounts: readonly SenderAccount[];
    pages: number;
    hasMore: boolean;
    /** What the runtime said when a page could not be read. */
    failed: Said | null;
}

export type SenderLine =
    | { kind: 'named'; provider: string; name: string }
    | { kind: 'unnamed'; provider: string }
    | { kind: 'unlisted'; provider: string }
    | { kind: 'unread'; said: Said | null };

/** The provider by its plugin's id, in the words a person knows it by where Jason ships it; else the id as it is. */
const providers: Readonly<Record<string, string>> = { reply: 'Reply' };
export const providerName = (pluginId: string): string => providers[pluginId] ?? pluginId;

/**
 * Pure: what the line says of one account from what the reads found. Nothing while the account is still being looked for.
 * An account found says its name, or that the provider holds none; one the provider's whole list leaves out is not
 * listed now; a page that failed, or a list longer than the pages read, says the name could not be read.
 */
export function senderLine(lookup: SenderLookup | undefined, externalId: string): SenderLine | null {
    if (lookup === undefined) return null;
    const provider = providerName(lookup.provider);
    const found = lookup.accounts.find((account) => account.external_id === externalId);
    if (found !== undefined)
        return found.name !== null && found.name.trim() !== ''
            ? { kind: 'named', provider, name: found.name }
            : { kind: 'unnamed', provider };
    if (lookup.failed !== null) return { kind: 'unread', said: lookup.failed };
    if (lookup.hasMore) return lookup.pages >= SENDER_PAGES ? { kind: 'unread', said: null } : null;
    return { kind: 'unlisted', provider };
}

/** The line in the desktop's words, and whether the sentence that says when it was read stands under it. */
export function senderWords(line: SenderLine): { text: string; code: string | null; when: boolean } {
    switch (line.kind) {
        case 'named':
            return { text: `Sending name at ${line.provider} now: ${line.name}`, code: null, when: true };
        case 'unnamed':
            return { text: `${line.provider} holds no sending name for this mailbox.`, code: null, when: true };
        case 'unlisted':
            return { text: `${line.provider} does not list this mailbox now.`, code: null, when: true };
        case 'unread':
            return { text: 'The sending name could not be read now.', code: line.said?.code ?? null, when: false };
    }
}

/** The sentence under a line that was read. */
export const SENDER_WHEN = 'Read when this card was drawn; it is not part of what you approve.';

export const senderKey = (asked: SenderAsked): string => `${asked.campaignId}/${asked.channel}`;

/**
 * The next page to read for an account, or null once the line is decided: the first page with no offset, each next one
 * at the offset of the pages read, the whole team's accounts on the send's channel.
 */
export function nextSenderRequest(lookup: SenderLookup | undefined, asked: SenderAsked): SenderListRequest | null {
    if (senderLine(lookup, asked.externalId) !== null) return null;
    const pages = lookup?.pages ?? 0;
    return {
        campaign_id: asked.campaignId,
        owners: 'team',
        sender_channel: asked.channel,
        limit: SENDER_PAGE,
        ...(pages > 0 ? { offset: pages * SENDER_PAGE } : {}),
    };
}

/** What the reads found once one more page has been read. */
export const withPage = (lookup: SenderLookup | undefined, page: SenderListResponse): SenderLookup => ({
    provider: page.plugin_id,
    accounts: [...(lookup?.accounts ?? []), ...page.items],
    pages: (lookup?.pages ?? 0) + 1,
    hasMore: page.has_more,
    failed: null,
});

/** What the reads found once a page could not be read: what they found before, and what the runtime said. */
export const withFailure = (lookup: SenderLookup | undefined, said: Said): SenderLookup => ({
    provider: lookup?.provider ?? '',
    accounts: lookup?.accounts ?? [],
    pages: lookup?.pages ?? 0,
    hasMore: true,
    failed: said,
});
