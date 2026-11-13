// Pure: the Feed as the page sees it. The page hands the desktop each line of the journal's stream whose kind the Feed
// takes, as its ids, moment and kind alone, and for a succeeded work item its operation or its role from the closed lists
// (`toFeedLine`); the desktop checks each again and keeps it. A line the desktop
// kept is drawn as one row (`toFeedRow`): the kind in words, how many lines it stands for, the campaign by the name the
// console read for it, and its age. Nothing here is a person's text, and a line never starts a turn.
import type { JournalEntry } from '../api/types';
import type {
    DesktopFeedKind,
    DesktopFeedLine,
    DesktopFeedOperation,
    DesktopFeedRole,
    DesktopLineEntry,
} from '../platform/desktop';
import type { IconName } from './icons';
import { ageOf } from './live';

const kinds: Readonly<
    Record<Exclude<DesktopFeedKind, 'workitem_succeeded'>, { words: string; icon: IconName; needs: boolean }>
> = {
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

/**
 * Work that succeeded is taken only as one of these, each with its words: a send that went out, by its operation, or the
 * finished work of a shipped role. The tables are the lists, and the desktop holds the same two (`feed.ts` there): a
 * name in neither is no Feed line, and no fallback wording stands for it.
 */
const sends: Readonly<Record<DesktopFeedOperation, string>> = {
    'message.send': 'A message went out',
    'conversation.reply': 'A reply went out',
    'social_invitation.send': 'An invitation went out',
};
const roleWork: Readonly<Record<DesktopFeedRole, string>> = {
    planner: 'A plan is ready',
    researcher: 'Research finished',
    copywriter: 'Copy was written',
    analyst: 'An analysis is ready',
    'deliverability-specialist': 'A deliverability check finished',
};

/** The kinds the Feed takes whole, as the desktop declares them; work that succeeded is taken by operation or role. */
export const feedKinds = Object.keys(kinds) as Exclude<DesktopFeedKind, 'workitem_succeeded'>[];
/** The operations and the roles whose successes the Feed takes. */
export const feedOperations = Object.keys(sends) as DesktopFeedOperation[];
export const feedRoles = Object.keys(roleWork) as DesktopFeedRole[];

const isFeedKind = (kind: string): kind is Exclude<DesktopFeedKind, 'workitem_succeeded'> => Object.hasOwn(kinds, kind);
const isOperation = (value: string | null): value is DesktopFeedOperation =>
    value !== null && Object.hasOwn(sends, value);
const isRole = (value: string | null): value is DesktopFeedRole => value !== null && Object.hasOwn(roleWork, value);

/** A line of the stream as the page hands it to the Feed, or null when the Feed does not take it. */
export function toFeedLine(line: JournalEntry): DesktopFeedLine | null {
    if (line.campaign_id === null) return null;
    if (line.kind === 'workitem_succeeded') {
        // Who it was, from the lists alone: an operation outside them ends it, a role is read only where there is none.
        const { work_item_operation: operation, work_item_role: role } = line;
        const named = (value: string | null | undefined) => value !== null && value !== undefined;
        const who =
            named(operation) && named(role)
                ? null
                : named(operation)
                  ? isOperation(operation ?? null)
                      ? { work_item_operation: operation as DesktopFeedOperation }
                      : null
                  : isRole(role ?? null)
                    ? { work_item_role: role as DesktopFeedRole }
                    : null;
        if (who === null) return null;
        return {
            id: line.id,
            ts: line.ts,
            kind: line.kind,
            campaign_id: line.campaign_id,
            work_item_id: line.work_item_id,
            ...who,
        };
    }
    if (!isFeedKind(line.kind)) return null;
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

/** The words, icon and need of a kept line; a succeeded line holds exactly one of an operation and a role, or it is none of the Feed's. */
function wordsOf(entry: DesktopLineEntry): { words: string; icon: IconName; needs: boolean } {
    if (entry.journal_kind !== 'workitem_succeeded') return kinds[entry.journal_kind];
    if (entry.operation !== undefined && entry.role === undefined && isOperation(entry.operation))
        return { words: sends[entry.operation], icon: 'chat', needs: false };
    if (entry.role !== undefined && entry.operation === undefined && isRole(entry.role))
        return { words: roleWork[entry.role], icon: 'work', needs: false };
    throw new Error('A succeeded line of the Feed names one operation or one role of the lists.');
}

/** One row of the Feed, from the line the desktop kept and the campaigns' names the console read. */
export function toFeedRow(entry: DesktopLineEntry, names: ReadonlyMap<string, string>, now: number): FeedRow {
    // Work that succeeded is worded by its operation or its role, both from the lists; a line of another shape is never kept.
    const said = wordsOf(entry);
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
