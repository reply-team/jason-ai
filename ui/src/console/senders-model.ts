import type { Approval, SenderAccount, SenderListRequest, SenderListResponse } from '../api/types';
import type { Said } from './waiting-model';

// Pure: the sender line of an approval card: the sending name the provider holds now for the mailbox a send names, read with
// `sender.list` of the approval's campaign on the send's channel while the card is drawn, and found by the identifier the
// send names. It comes from the runtime's read and the approval's own arguments, never from what the AI wrote; it is a
// fact of the moment it was read, not a promise of what a recipient sees; and it holds no act: an approval is approved
// by its whole read alone (`approvalGate`).

/**
 * The read a card's line needs: the approval's campaign, the channel its send consumes, the account it names, and the
 * plugin that sends it, whose accounts alone the line may speak of.
 */
export interface SenderAsked {
    campaignId: string;
    channel: string;
    externalId: string;
    pluginId: string;
}

const record = (value: unknown): Record<string, unknown> | null =>
    value !== null && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : null;

/**
 * What an approval's line reads: only an approval whose input names a sender, an account by its identifier, on email.
 * What a sending name means is known for a mailbox, so a LinkedIn message or an invitation draws no line, and neither
 * does a reply, a start or an enrolment, which name no account.
 */
export function senderAsked(approval: Approval | undefined): SenderAsked | null {
    if (approval === undefined) return null;
    const args = record(record(approval.subject['input'])?.['args']);
    const externalId = record(args?.['sender'])?.['external_id'];
    if (args?.['channel'] !== 'email') return null;
    if (typeof externalId !== 'string' || externalId === '') return null;
    return { campaignId: approval.campaign_id, channel: 'email', externalId, pluginId: approval.plugin_id };
}

/** How many accounts one page asks for, and how many pages a line reads before it stops looking. */
export const SENDER_PAGE = 100;
export const SENDER_PAGES = 5;

/** What the reads of one campaign's accounts on one channel found, page by page. */
export interface SenderLookup {
    /** When the read began, in the order of drawings and reads: a card drawn after it reads again. */
    started: number;
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
    | { kind: 'unread'; said: Said | null }
    /** The accounts were listed by another plugin than the one that sends: they say nothing of this mailbox. */
    | { kind: 'elsewhere' };

/** The provider by its plugin's id, in the words a person knows it by where Jason ships it; else the id as it is. */
const providers: Readonly<Record<string, string>> = { reply: 'Reply' };
export const providerName = (pluginId: string): string => providers[pluginId] ?? pluginId;

/**
 * Pure: what the line says of one account from what the reads found. Nothing while the account is still being looked for.
 * Accounts another plugin listed than the one that sends say nothing of it. An account found says its name, or that the
 * provider holds none; one the provider's whole list leaves out is not listed now; a page that failed, or a list longer
 * than the pages read, says the name could not be read.
 */
export function senderLine(lookup: SenderLookup | undefined, asked: SenderAsked): SenderLine | null {
    if (lookup === undefined) return null;
    if (lookup.pages > 0 && lookup.provider !== asked.pluginId) return { kind: 'elsewhere' };
    const provider = providerName(lookup.provider);
    const found = lookup.accounts.find((account) => account.external_id === asked.externalId);
    if (found !== undefined)
        return found.name !== null && found.name.trim() !== ''
            ? { kind: 'named', provider, name: found.name }
            : { kind: 'unnamed', provider };
    if (lookup.failed !== null) return { kind: 'unread', said: lookup.failed };
    if (lookup.hasMore) return lookup.pages >= SENDER_PAGES ? { kind: 'unread', said: null } : null;
    return { kind: 'unlisted', provider };
}

/** A line's words: the sentence, the runtime's code and words where it refused, and whether when it was read follows. */
export interface SenderWords {
    text: string;
    code: string | null;
    message: string | null;
    when: boolean;
}

const said = (text: string, when: boolean): SenderWords => ({ text, code: null, message: null, when });

/**
 * The line in the desktop's words, or null for a line that says nothing. Why a name could not be read is said as it is
 * known: the runtime's code and words, or the pages that ran out.
 */
export function senderWords(line: SenderLine): SenderWords | null {
    switch (line.kind) {
        case 'named':
            return said(`Sending name at ${line.provider} now: ${line.name}`, true);
        case 'unnamed':
            return said(`${line.provider} holds no sending name for this mailbox.`, true);
        case 'unlisted':
            return said(`${line.provider} does not list this mailbox now.`, true);
        case 'unread':
            return line.said === null
                ? said(
                      `The sending name could not be read: the provider lists more accounts than the ${SENDER_PAGES} pages read.`,
                      false,
                  )
                : {
                      // Only an answer of the runtime's has a code; a failure the window met itself is said as it was.
                      text:
                          line.said.code === null
                              ? 'The sending name could not be read:'
                              : 'The sending name could not be read. The runtime said:',
                      code: line.said.code,
                      message: line.said.message,
                      when: false,
                  };
        case 'elsewhere':
            return null;
    }
}

/** The sentence under a line that was read. */
export const SENDER_WHEN = 'Read when this card was drawn; it is not part of what you approve.';

export const senderKey = (asked: SenderAsked): string => `${asked.campaignId}/${asked.channel}`;

/**
 * The next page to read for an account, or null once the line is decided: the first page with no offset, each next one
 * at the count of accounts the pages before it returned, since a page may come back short with more to follow; the whole
 * team's accounts on the send's channel.
 */
export function nextSenderRequest(lookup: SenderLookup | undefined, asked: SenderAsked): SenderListRequest | null {
    if (senderLine(lookup, asked) !== null) return null;
    return {
        campaign_id: asked.campaignId,
        owners: 'team',
        sender_channel: asked.channel,
        limit: SENDER_PAGE,
        ...(lookup !== undefined && lookup.pages > 0 ? { offset: lookup.accounts.length } : {}),
    };
}

/** What the reads found once one more page has been read; a first page begins a read marked `started`. */
export const withPage = (
    lookup: SenderLookup | undefined,
    page: SenderListResponse,
    started: number,
): SenderLookup => ({
    started: lookup?.started ?? started,
    provider: page.plugin_id,
    accounts: [...(lookup?.accounts ?? []), ...page.items],
    pages: (lookup?.pages ?? 0) + 1,
    hasMore: page.has_more,
    failed: null,
});

/** What the reads found once a page could not be read: what they found before, and what the runtime said. */
export const withFailure = (lookup: SenderLookup | undefined, said: Said, started: number): SenderLookup => ({
    started: lookup?.started ?? started,
    provider: lookup?.provider ?? '',
    accounts: lookup?.accounts ?? [],
    pages: lookup?.pages ?? 0,
    hasMore: true,
    failed: said,
});
