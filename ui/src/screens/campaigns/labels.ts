import type { CampaignView } from '../../router';

/**
 * A count: the runtime's name for it, its one-word name, and its definition for the hover. One the table has no column
 * for is a chip on the People tab only.
 */
export type Column = { key: string; label: string; hint: string; column?: false };

// Each hint is the runtime's own definition of the count, word for word.
export const summaryColumns: readonly Column[] = [
    { key: 'people', label: 'People', hint: 'Everyone in the campaign; removed people are not counted.' },
    { key: 'waiting', label: 'Waiting', hint: 'An approval or a question about their work is waiting for you.' },
    {
        key: 'failed',
        label: 'Failed',
        hint: 'Their latest work failed or expired, and nothing else is planned for them.',
    },
    { key: 'working', label: 'Working', hint: 'Work about them is planned or running.' },
    { key: 'new', label: 'New', hint: 'Nothing has been done or planned for them yet.' },
    {
        key: 'idle',
        label: 'Idle',
        hint: 'Their work finished without failing, nothing is planned, and they were never put into the sequence.',
    },
    { key: 'sequence', label: 'Sequence', hint: "They were put into the provider's sequence and are not out of it." },
    { key: 'out', label: 'Out', hint: 'They replied, opted out, were stopped in the sequence, or hard-bounced.' },
    { key: 'removed', label: 'Removed', hint: 'They were taken out of the campaign.', column: false },
];

export const activityColumns: readonly Column[] = [
    { key: 'added', label: 'Added', hint: 'Added to the campaign, or added back after a removal, in the period.' },
    {
        key: 'worked_on',
        label: 'Worked on',
        hint: 'Work about them finished in the period, whether it succeeded or failed.',
    },
    {
        key: 'contacted',
        label: 'Contacted',
        hint: "Something was sent to them, or they were put into the provider's sequence, in the period.",
    },
    { key: 'replied', label: 'Replied', hint: 'Their first reply in this campaign came in the period.' },
    {
        key: 'opted_out',
        label: 'Opted out',
        hint: 'One of their addresses was put on the do-not-contact register in the period.',
    },
    {
        key: 'removed',
        label: 'Removed',
        hint: 'They were taken out of the campaign in the period, whether added back since or not.',
        column: false,
    },
];

/** What All lists in each view: everyone in the campaign now, or everyone something happened to in the period. */
export const everyone: Record<CampaignView, string> = {
    summary: 'Everyone in the campaign; removed people are not counted.',
    activity: "Something happened to them in the period: they are in at least one of the period's counts.",
};

/** The two counts the provider's sequence decides, which mean nothing until the campaign is linked to one. */
export const atTheProvider: readonly string[] = ['sequence', 'out'];

export const unlinked = 'No sequence linked yet';

export const views: Record<CampaignView, string> = { summary: 'Summary', activity: 'Activity' };

/** The colour a non-zero count is shown in: what waits on the person, what failed, and who answered. */
export const tone: Record<string, string> = { waiting: 'warn', failed: 'bad', replied: 'hot' };
