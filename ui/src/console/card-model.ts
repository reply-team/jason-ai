// Pure: what the chat's data cards say of what they read. A data card takes only an id from the AI; every count, row,
// status and mode it shows is the runtime's answer, and every sentence around them is the desktop's, chosen from that
// answer alone. The words never name the AI: a card's facts say "it", "this agent", "the runtime".
import type { CampaignActivityRow, CampaignSummaryRow, Contact, ModeInForce } from '../api/types';
import { summaryColumns } from '../screens/campaigns/labels';
import { modeWords } from '../screens/send-modes/model';
import { t } from '../platform/i18n';
import { clock } from '../time/format';
import type { CardKind, Standing } from './cards';
import { channelWords } from './live';

/**
 * The kinds whose card reads the runtime (or the desktop) when it is drawn: every kind but a choice. A plan's second
 * part reads, so a plan counts.
 */
export const readingKinds: readonly CardKind[] = ['people_list', 'agent_status', 'needs', 'accounts', 'plan'];

/**
 * The index of the conversation's last card that reads, among the cards its turns drew (`kind` null for a block that
 * did not read as a card); null when none reads.
 */
export function lastReading(cards: readonly { index: number; kind: CardKind | null }[]): number | null {
    let last: number | null = null;
    for (const card of cards) if (card.kind !== null && readingKinds.includes(card.kind)) last = card.index;
    return last;
}

/**
 * How a card that reads is drawn: as it was read when this drawing already read it; else as a read, when it is the
 * conversation's last card that reads; else as a stub that reads only when asked. A card that arrives while the chat is
 * drawn is the last when it first draws, so it reads; a conversation opened again reads its last card alone.
 */
export const drawsAs = (index: number, last: number | null, held: boolean): 'held' | 'read' | 'stub' =>
    held ? 'held' : index === last ? 'read' : 'stub';

/** The minute a card's last read was answered, on the person's own clock. */
export const asOf = (at: number, zone: string): string => `As of ${clock(new Date(at), zone)}`;

/** The status card's sentence, from the campaign's status alone: what the dispatcher's eligibility rule says of it. */
export function statusSentence(status: string): string {
    switch (status) {
        case 'draft':
            return 'It is a draft: none of its work starts until it is started.';
        case 'active':
            return 'It is active.';
        case 'paused':
            return 'It is paused: none of its work starts but what stops it.';
        case 'archived':
            return 'It is archived.';
        default:
            return `Its status is ${status}.`;
    }
}

/** One count a card shows: the runtime's number under the desktop's word for it. */
export interface Count {
    key: string;
    label: string;
    value: number;
}

/**
 * The counts of now, from `campaign.summary`: its people, the people waiting on the person (people, never approvals or
 * questions), working, failed, in the provider's sequence (only once one is linked, before which it counts nothing),
 * and out.
 */
export function nowCounts(row: CampaignSummaryRow): Count[] {
    return [
        { key: 'people', label: 'People', value: row.people },
        { key: 'waiting', label: 'People waiting on you', value: row.waiting },
        { key: 'working', label: 'Working', value: row.working },
        { key: 'failed', label: 'Failed', value: row.failed },
        ...(row.sequence_linked ? [{ key: 'sequence', label: 'In the sequence', value: row.sequence }] : []),
        { key: 'out', label: 'Out', value: row.out },
    ];
}

/** The counts of the last thirty days, from `campaign.activity` over the Results tab's own window. */
export const monthCounts = (row: CampaignActivityRow): Count[] => [
    { key: 'contacted', label: 'Contacted', value: row.contacted },
    { key: 'replied', label: 'Replied', value: row.replied },
    { key: 'bounced', label: 'Bounced', value: row.bounced },
    { key: 'opted_out', label: 'Opted out', value: row.opted_out },
];

/** How a send to each kind of person is decided now, in the send-modes screen's words; none from a runtime that does not say. */
export function modesInForce(
    inForce: { cold: ModeInForce; warm: ModeInForce } | undefined,
): { kind: string; mode: string }[] {
    if (inForce === undefined) return [];
    return [
        { kind: t('modes.kind.cold'), mode: modeWords(inForce.cold.mode).name },
        { kind: t('modes.kind.warm'), mode: modeWords(inForce.warm.mode).name },
    ];
}

const people = (count: number): string => (count === 1 ? '1 person' : `${count} people`);

/** A count's one word, as the Campaigns screen words it. */
const standingWord = (key: string): string => summaryColumns.find((column) => column.key === key)?.label ?? key;

/**
 * A people list's footer: with a campaign and no search, how many people the agent holds (or how many stand so, under
 * a standing); otherwise, when the runtime has a further page, a pointer to the People page; else nothing.
 */
export function peopleFooter(list: {
    campaign: boolean;
    search: boolean;
    standing?: Standing | undefined;
    count: number | null;
    more: boolean;
}): string | null {
    if (list.campaign && !list.search && list.count !== null)
        return list.standing === undefined
            ? `${people(list.count)} in this agent`
            : `${people(list.count)} in this agent ${list.count === 1 ? 'is' : 'are'} counted under ${standingWord(list.standing)}`;
    return list.more ? 'More on the People page' : null;
}

/** What an empty people list says: nobody matched what was asked, nobody is in the agent, or nobody is on record. */
export function peopleEmpty(list: { campaign: boolean; search: boolean; standing?: Standing | undefined }): string {
    if (list.search || list.standing !== undefined) return 'Nobody matches.';
    return list.campaign ? 'Nobody is in this agent yet.' : 'No people yet.';
}

/** One row of a people list: what the runtime holds of the person, and the primary channel's kind, never its value. */
export interface PersonRow {
    id: string;
    name: string;
    company: string | null;
    title: string | null;
    channel: string | null;
    /** Where the person stands in the card's campaign, in the People tab's words; none without a campaign. */
    standing: string[];
}

export function personRow(contact: Contact, campaignId?: string): PersonRow {
    const name = [contact.first_name?.trim(), contact.last_name?.trim()].filter(Boolean).join(' ');
    const primary = contact.channels.find((each) => each.primary) ?? contact.channels[0];
    const membership =
        campaignId === undefined ? undefined : contact.campaigns?.find((each) => each.campaign_id === campaignId);
    return {
        id: contact.id,
        name: name === '' ? 'No name on record' : name,
        company: contact.company,
        title: contact.title,
        channel: primary === undefined ? null : channelWords(primary.channel),
        standing: (membership?.standing ?? []).map(standingWord),
    };
}

/** One list's count: how many items it holds, and whether the runtime has a further page of them. */
export interface Listed {
    count: number;
    more: boolean;
}

const items = (listed: Listed, one: string, many: string): string =>
    listed.more ? `${listed.count} or more ${many}` : `${listed.count} ${listed.count === 1 ? one : many}`;

/** The needs card's count line: items waiting, never people. */
export const needsLine = (approvals: Listed, questions: Listed): string =>
    `${items(approvals, 'approval', 'approvals')} and ${items(questions, 'question', 'questions')} waiting for your decision`;
