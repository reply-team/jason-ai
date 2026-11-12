import type { Contact, ContactCampaign } from '../../api/types';
import { summaryColumns, type Column } from '../campaigns/labels';

/** The counts the People page offers as chips: where a person stands in a campaign now, as the runtime counts it. */
export const stages = ['waiting', 'failed', 'working', 'new', 'idle', 'sequence', 'out'] as const;
export type Stage = (typeof stages)[number];

/** The stage an address names, or nothing when it names no count the page offers. */
export function parseStage(value: string | null): Stage | undefined {
    return stages.find((stage) => stage === value);
}

/** A count's one-word name and its definition, as the Campaigns screen words it; nothing for a count this build has no word for. */
export function stageWord(key: string): Column | undefined {
    return summaryColumns.find((column) => column.key === key && column.key !== 'people');
}

/** The counts a membership stands in; none from a runtime that does not say. */
export const standingOf = (campaign: ContactCampaign): readonly string[] => campaign.standing ?? [];

/** Where a membership stands, each count in its one word; empty when no count counts the person there. */
export function stageWords(standing: readonly string[]): Column[] {
    return standing.flatMap((key) => stageWord(key) ?? []);
}

function newest(a: ContactCampaign, b: ContactCampaign): number {
    return Date.parse(b.added_at) - Date.parse(a.added_at) || Date.parse(b.updated_at) - Date.parse(a.updated_at);
}

/**
 * A person's campaigns in the order a row prefers them: the campaign the list is narrowed to, when they are a member
 * there, else the campaign of their latest activity when they are a member there and not excluded; then the newest
 * memberships not excluded, then the excluded ones, newest first.
 */
export function ordered(person: Contact, within?: string): ContactCampaign[] {
    const all = [...(person.campaigns ?? [])].sort(newest);
    const live = all.filter((c) => c.state !== 'excluded');
    const lastIn = person.last_activity?.campaign_id;
    const first = all.find((c) => c.campaign_id === within) ?? live.find((c) => c.campaign_id === lastIn);
    return [
        ...(first ? [first] : []),
        ...live.filter((c) => c !== first),
        ...all.filter((c) => c.state === 'excluded' && c !== first),
    ];
}

/** The campaign a row shows: the first of `ordered`, or under a stage the first whose standing holds it; none for nobody on a campaign. */
export function leading(person: Contact, stage?: Stage, within?: string): ContactCampaign | null {
    return ordered(person, within).find((c) => !stage || standingOf(c).includes(stage)) ?? null;
}

/** The campaigns a row does not show, in the same order. */
export function others(person: Contact, shown: ContactCampaign | null, within?: string): ContactCampaign[] {
    return ordered(person, within).filter((c) => c !== shown);
}
