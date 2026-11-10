// Pure: the runtime's answers and the desktop's, in the shape the console's panels draw. Nothing here is a view the
// runtime knows of, and nothing here is markup: the panels escape what they draw.
import type { ApprovalSummary, CampaignSummary, CampaignSummaryRow, DecisionSummary, JournalEntry } from '../api/types';
import type { DesktopHost } from '../platform/desktop';

export type Tone = 'green' | 'amber' | 'gray' | 'blue' | 'red' | 'violet' | 'teal';

export function ageOf(moment: string | null | undefined, now: number): string {
    if (!moment) return '';
    const minutes = Math.floor((now - Date.parse(moment)) / 60_000);
    if (!Number.isFinite(minutes) || minutes < 1) return 'now';
    if (minutes < 60) return `${minutes} min`;
    if (minutes < 60 * 24) return `${Math.floor(minutes / 60)} h`;
    return `${Math.floor(minutes / (60 * 24))} d`;
}

const states: Readonly<Record<string, [string, Tone]>> = {
    active: ['Active', 'green'],
    paused: ['Paused', 'amber'],
    archived: ['Archived', 'gray'],
    draft: ['Draft', 'blue'],
};

export interface CampaignRow {
    id: string;
    name: string;
    state: string;
    tone: Tone;
    people: number;
    waiting: number;
    failed: number;
}

export function toCampaignRow(campaign: CampaignSummary, counts: CampaignSummaryRow | undefined): CampaignRow {
    const [state, tone] = states[campaign.status] ?? states['draft']!;
    return {
        id: campaign.id,
        name: campaign.name,
        state,
        tone,
        people: counts?.people ?? 0,
        waiting: counts?.waiting ?? 0,
        failed: counts?.failed ?? 0,
    };
}

const operations: Readonly<Record<string, string>> = {
    'message.send': 'Message',
    'conversation.reply': 'Reply',
    'social_invitation.send': 'LinkedIn invite',
    'campaign.activate': 'Start sequence',
    'campaign.enroll': 'Add to sequence',
    'enrollment.stop': 'Stop sequence',
};

/** The operations that reach their person by a message; a row of any other names its person with no word before. */
const byMessage: ReadonlySet<string> = new Set(['message.send', 'conversation.reply', 'social_invitation.send']);

/** An operation's code in words; a code with no words here, the names every object has included, is shown as given. */
export const operationWords = (operation: string): string =>
    Object.hasOwn(operations, operation) ? operations[operation]! : operation;

const channels: Readonly<Record<string, string>> = {
    email: 'Email',
    linkedin: 'LinkedIn',
};

/** A channel's code in words; a code with no words here, the names every object has included, is shown as given. */
export const channelWords = (code: string): string => (Object.hasOwn(channels, code) ? channels[code]! : code);

export interface ApprovalRow {
    id: string;
    what: string;
    who: string | null;
    /** Whether the operation reaches that person by a message, so the row says the message goes to them. */
    byMessage: boolean;
    campaign: string;
    via: string;
    age: string;
}

/**
 * One row of what waits: who it reaches is the contact the row carries, else the address the preview highlights when
 * that approval is already open; a row is never read again for it.
 */
export function toApprovalRow(row: ApprovalSummary, now: number, preview?: Record<string, unknown>): ApprovalRow {
    const highlighted = preview ? text(record(preview['highlights'])?.['/contacts/0/channels/0/value']) : null;
    return {
        id: row.id,
        what: operationWords(row.operation),
        who: row.contact_name ?? highlighted,
        byMessage: byMessage.has(row.operation),
        campaign: row.campaign_name,
        via: row.plugin_id,
        age: ageOf(row.requested_at, now),
    };
}

export interface ApprovalDetail {
    intent: string | null;
    to: string | null;
    /** The account the message goes from, as the preview highlights it: its address, else its identifier. */
    from: string | null;
    /** The channel it goes on, in words. */
    channel: string | null;
    subject: string | null;
    body: string | null;
    facts: string[];
}

const record = (value: unknown): Record<string, unknown> | null =>
    value !== null && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : null;
const text = (value: unknown): string | null => (typeof value === 'string' && value ? value : null);

const factNames: readonly [string, string][] = [
    ['reach', 'Reach'],
    ['reversibility', 'Reversibility'],
    ['cost', 'Cost'],
];

/**
 * What an approval's preview says the act would do: its intent, its recipient, the account and the channel it goes
 * by, its message and its properties. A value the preview does not carry is null, never a word in its place.
 */
export function toApprovalDetail(preview: Record<string, unknown>): ApprovalDetail {
    const highlights = record(preview['highlights']);
    const message = record(highlights?.['/args/message']);
    const sender = record(highlights?.['/args/sender']);
    const channel = text(highlights?.['/args/channel']);
    const contact = record(preview['contact']);
    const value = text(contact?.['value']) ?? text(highlights?.['/contacts/0/channels/0/value']);
    const name = text(contact?.['name']);
    const facts: string[] = [];
    for (const [key, label] of factNames) {
        const fact = record(preview[key]);
        const shown = text(fact?.['value']);
        if (!shown) continue;
        const detail = text(fact?.['detail']);
        facts.push(detail ? `${label}: ${shown} (${detail})` : `${label}: ${shown}`);
    }
    return {
        intent: text(preview['intent']),
        to: value ? (name ? `${name}, ${value}` : value) : null,
        from: text(sender?.['address']) ?? text(sender?.['external_id']),
        channel: channel ? channelWords(channel) : null,
        subject: text(message?.['subject']),
        body: text(message?.['body']),
        facts,
    };
}

export interface QuestionRow {
    id: string;
    question: string;
    campaign: string;
    age: string;
}

export function toQuestionRow(row: DecisionSummary, now: number): QuestionRow {
    return { id: row.id, question: row.question, campaign: row.campaign_name, age: ageOf(row.raised_at, now) };
}

export interface ActivityLine {
    id: string;
    age: string;
    what: string;
    campaign: string | null;
    contact: string | null;
    who: string;
}

const sentence = (kind: string): string => {
    const words = kind.replace(/_/g, ' ');
    return words.charAt(0).toUpperCase() + words.slice(1);
};

/** One line of the activity list: when, what, the campaign, the person and who wrote it. */
export function toActivity(line: JournalEntry, now: number): ActivityLine {
    return {
        id: line.id,
        age: ageOf(line.ts, now),
        what: sentence(line.kind),
        campaign: line.campaign_name,
        contact: line.contact_name,
        who: line.actor.id ?? line.actor.type,
    };
}

/** The hosts the chat can run on, and the ones found that run campaign work but not the chat yet. */
export function chatHosts(hosts: readonly DesktopHost[]): { drive: DesktopHost[]; other: DesktopHost[] } {
    const found = hosts.filter((each) => each.found);
    return { drive: found.filter((each) => each.drivesChat), other: found.filter((each) => !each.drivesChat) };
}

/** How a program found on this machine is signed in to: once, in a terminal, by its own command. */
export function signInSentence(host: DesktopHost): string | null {
    if (host.kind !== 'program' || !host.found) return null;
    return `Sign in once by running \`${host.id}\` in a terminal; the chat uses that sign-in.`;
}

/** Whether a turn's error, in the host's own words, says the host is not signed in. */
export const isSignInFailure = (error: string): boolean =>
    /\b(not logged in|log ?in|sign(ed)? ?in|unauthori[sz]ed|authenticat\w*|api key|credentials?|401)\b|\/login/i.test(
        error,
    );
