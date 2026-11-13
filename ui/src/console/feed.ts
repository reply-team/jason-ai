// Pure: the Feed as the page sees it. The page hands the desktop each line of the journal's stream whose kind the Feed
// takes, as its ids, moment and kind alone (`toFeedLine`); the desktop checks each again and keeps it. A line the desktop
// kept is drawn as one row (`toFeedRow`): the kind in words, how many lines it stands for, the campaign by the name the
// console read for it, and its age. Nothing here is a person's text, and a line never starts a turn.
import type { JournalEntry } from '../api/types';
import type { DesktopFeedKind, DesktopFeedLine, DesktopLineEntry } from '../platform/desktop';
import type { IconName } from './icons';
import { ageOf } from './live';

const kinds: Readonly<Record<DesktopFeedKind, { words: string; icon: IconName; needs: boolean }>> = {
    inbound_reply_observed: { words: 'A reply arrived', icon: 'chat', needs: false },
    approval_requested: { words: 'An approval waits', icon: 'att', needs: true },
    decision_raised: { words: 'A question waits', icon: 'att', needs: true },
    campaign_created: { words: 'An agent was created', icon: 'camp', needs: false },
    campaign_started: { words: 'Agent started', icon: 'camp', needs: false },
    campaign_paused: { words: 'Agent paused', icon: 'camp', needs: false },
    workitem_failed: { words: 'Work stopped', icon: 'work', needs: false },
    workitem_expired: { words: 'Work stopped', icon: 'work', needs: false },
    provider_blocked: { words: 'A provider block holds work', icon: 'shield', needs: false },
    approval_approved: { words: 'Approved', icon: 'att', needs: false },
    approval_rejected: { words: 'Rejected', icon: 'att', needs: false },
    decision_answered: { words: 'A question was answered', icon: 'att', needs: false },
    campaign_archived: { words: 'Agent archived', icon: 'camp', needs: false },
};

/** The kinds the Feed takes, as the desktop declares them. */
export const feedKinds = Object.keys(kinds) as DesktopFeedKind[];

const isFeedKind = (kind: string): kind is DesktopFeedKind => Object.hasOwn(kinds, kind);

/** A line of the stream as the page hands it to the Feed, or null when the Feed does not take it. */
export function toFeedLine(line: JournalEntry): DesktopFeedLine | null {
    if (!isFeedKind(line.kind) || line.campaign_id === null) return null;
    return {
        id: line.id,
        ts: line.ts,
        kind: line.kind,
        campaign_id: line.campaign_id,
        work_item_id: line.work_item_id,
    };
}

export interface FeedRow {
    id: string;
    kind: DesktopFeedKind;
    words: string;
    icon: IconName;
    /** "x3" when the row stands for more than one line; null for one. */
    count: string | null;
    campaignId: string;
    /** The campaign's name as the console read it, else its id. */
    campaign: string;
    age: string;
    /** Whether the row leads to Needs you too: an approval or a question waits. */
    needs: boolean;
}

/** One row of the Feed, from the line the desktop kept and the campaigns' names the console read. */
export function toFeedRow(entry: DesktopLineEntry, names: ReadonlyMap<string, string>, now: number): FeedRow {
    const said = kinds[entry.journal_kind];
    return {
        id: entry.journal_id,
        kind: entry.journal_kind,
        words: said.words,
        icon: said.icon,
        count: entry.count > 1 ? `x${entry.count}` : null,
        campaignId: entry.campaign_id,
        campaign: names.get(entry.campaign_id) ?? entry.campaign_id,
        age: ageOf(entry.at, now),
        needs: said.needs,
    };
}
