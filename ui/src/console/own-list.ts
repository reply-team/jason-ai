// Pure: what a list card the chat's AI wrote turns into. The request a press sends is built from the card alone, every
// row as one payload of `campaign.add_contacts`; the runtime judges each value and answers row by row. And the page's
// own comparison: each row's address looked for in the text of the person's own messages, with no parser and nothing
// read from those messages but whether an address stands in them.
import type { AddContactsResult, ListAddRequest, ListContact } from '../api/types';
import { cardLayout, optionsOf, placeOf, readCard, type OwnListCard } from './cards';
import { locksChoice, type Chat } from './chat-model';

/** The reason a list's press gives, so what happened says where the people came from. */
export const listReason = 'From a list given in the chat.';

/** The channels a column may fill: each becomes one of the person's channels, in the columns' order. */
const channelFields: readonly string[] = ['email', 'phone', 'linkedin'];

const fieldWords: Readonly<Record<string, string>> = {
    first_name: 'First name',
    last_name: 'Last name',
    company: 'Company',
    title: 'Title',
    time_zone: 'Time zone',
    email: 'Email',
    phone: 'Phone',
    linkedin: 'LinkedIn',
};

/** A column's field in the desktop's words; a custom field by its own name. */
export const fieldWord = (field: string): string =>
    field.startsWith('custom:') ? field.slice('custom:'.length) : (fieldWords[field] ?? field);

/** How each column was read, in one line: the person's header and the field it fills, then what was left out. */
export function mappingLine(card: OwnListCard): string {
    const mapped = card.columns
        .map((column) =>
            column.from === undefined ? fieldWord(column.field) : `${column.from} → ${fieldWord(column.field)}`,
        )
        .join(', ');
    return card.left_out.length === 0 ? mapped : `${mapped}; left out: ${card.left_out.join(', ')}`;
}

/** One row as one payload: its empty cells left out, its channels in order, its custom fields a string each. */
function contactOf(card: OwnListCard, row: readonly string[]): ListContact {
    const contact: Record<string, unknown> = {};
    const channels: { channel: string; value: string }[] = [];
    const custom: Record<string, string> = {};
    card.columns.forEach((column, at) => {
        const value = (row[at] ?? '').trim();
        if (value === '') return;
        if (channelFields.includes(column.field)) channels.push({ channel: column.field, value });
        else if (column.field.startsWith('custom:')) custom[column.field.slice('custom:'.length)] = value;
        else contact[column.field] = value;
    });
    if (channels.length > 0) contact['channels'] = channels;
    if (Object.keys(custom).length > 0) contact['custom'] = custom;
    return contact as ListContact;
}

/** The one request a press sends: every row of the card, matched by its key, with the fixed reason. */
export const listRequest = (card: OwnListCard, campaignId: string): ListAddRequest => ({
    campaign_id: campaignId,
    match_by: card.match_by,
    contacts: card.rows.map((row) => contactOf(card, row)),
    reason: listReason,
});

/**
 * The person's own messages of a chat: every entry of theirs but an answer a click on a choice sent, whose words the AI
 * wrote. An answer is known as the chat knows one (`choiceState`): the first entry after a choice's card that locks it is
 * the person's, and its words are one of the choice's labels.
 */
export function ownMessages(chat: Pick<Chat, 'entries' | 'campaignId' | 'main' | 'running'>): string[] {
    const clicked = new Set<number>();
    for (const [index, placed] of cardLayout(chat.entries).cards) {
        const reading = readCard(placed, {
            place: placeOf(chat),
            campaignId: chat.campaignId,
            ended: !placed.lastTurn || !chat.running,
        });
        if (reading.state !== 'card') continue;
        const labels = optionsOf(reading.card).map((option) => option.label.trim());
        const next = chat.entries.findIndex((entry, at) => at > index && locksChoice(entry.kind));
        const entry = chat.entries[next];
        if (entry?.kind === 'user' && labels.includes(entry.text.trim())) clicked.add(next);
    }
    return chat.entries.flatMap((entry, at) => (entry.kind === 'user' && !clicked.has(at) ? [entry.text] : []));
}

/** What may stand right before an address and still be part of a longer one. */
const mailbox = /[a-z0-9._%+-]/;
/** What may stand right after one and still be part of a longer one: a letter or digit, `_` or `-`, or `.` before one. */
const domain = /[a-z0-9_-]/;

/**
 * Whether an address stands whole in a text, both lowercased: a hit counts only where the character before it could not
 * begin a longer mailbox and the character after it could not go on with a longer domain.
 */
function standsIn(text: string, address: string): boolean {
    for (let at = text.indexOf(address); at !== -1; at = text.indexOf(address, at + 1)) {
        const end = at + address.length;
        if (at > 0 && mailbox.test(text[at - 1]!)) continue;
        const after = text[end] ?? '';
        if (domain.test(after)) continue;
        if (after === '.' && /[a-z0-9]/.test(text[end + 1] ?? '')) continue;
        return true;
    }
    return false;
}

/**
 * The card's addresses against the person's own messages: how many rows carry an address, how many of those addresses
 * stand in one of the messages, trimmed and without regard to case, and the index of each row whose address does not.
 */
export function addressesIn(
    card: OwnListCard,
    messages: readonly string[],
): { total: number; found: number; missing: number[] } {
    const column = card.columns.findIndex((each) => each.field === 'email');
    if (column === -1) return { total: 0, found: 0, missing: [] };
    const said = messages.map((message) => message.toLowerCase());
    let total = 0;
    const missing: number[] = [];
    card.rows.forEach((row, index) => {
        const address = (row[column] ?? '').trim().toLowerCase();
        if (address === '') return;
        total += 1;
        if (!said.some((message) => standsIn(message, address))) missing.push(index);
    });
    return { total, found: total - missing.length, missing };
}

type Item = AddContactsResult['items'][number];

/** What the runtime answered of one row, in words, and whether it added the person. */
export function rowOutcome(item: Item | undefined): { added: boolean; words: string } {
    if (item === undefined) return { added: false, words: 'The runtime said nothing of this row.' };
    switch (item.status) {
        case 'added':
            return {
                added: true,
                words:
                    item.contact_created === true
                        ? 'Added, as a new person'
                        : 'Added, as the person Jason already held',
            };
        case 'already_member':
            return {
                added: false,
                words:
                    item.state === 'excluded'
                        ? 'Taken out of this agent before; not added back'
                        : 'Already in this agent',
            };
        case 'rejected':
            return {
                added: false,
                words: `Not added: ${item.error?.details?.[0]?.message ?? item.error?.message ?? 'the runtime gave no reason.'}`,
            };
        default:
            return { added: false, words: item.status };
    }
}
