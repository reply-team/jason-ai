import type { ActivityPeriod } from '../../api/types';
import type { ActivityCount, Period } from '../../router';

/**
 * The tiles: each count's one-word name and, for the hover, its definition in the runtime's own words, read across the
 * campaigns asked: "a campaign" where Campaigns, about one, says "the campaign".
 */
export const counts: readonly { key: ActivityCount; label: string; hint: string }[] = [
    {
        key: 'people',
        label: 'People',
        hint: "Something happened to them in the period: they are in at least one of the period's counts.",
    },
    { key: 'added', label: 'Added', hint: 'Added to a campaign, or added back after a removal, in the period.' },
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
    { key: 'replied', label: 'Replied', hint: 'Their first reply in a campaign came in the period.' },
    {
        key: 'opted_out',
        label: 'Opted out',
        hint: 'One of their addresses was put on the do-not-contact register in the period.',
    },
    {
        key: 'removed',
        label: 'Removed',
        hint: 'They were taken out of a campaign in the period, whether added back since or not.',
    },
];

/** What a list of one count is ordered and grouped by, in the words of its head. */
export const byTheDay: Record<ActivityCount, string> = {
    people: 'by the day something happened to them',
    added: 'by the day they were added',
    worked_on: 'by the day they were worked on',
    contacted: 'by the day they were contacted',
    replied: 'by the day they replied',
    opted_out: 'by the day they opted out',
    removed: 'by the day they were removed',
};

export const inThePeriod: Record<Period, string> = {
    today: 'today',
    '7d': 'in the last 7 days',
    '30d': 'in the last 30 days',
    '90d': 'in the last 90 days',
};

export const perBucket: Record<ActivityPeriod, string> = { hour: 'per hour', day: 'per day', week: 'per week' };

/** A person counted on two days stands in two bars, and once in the number above them. */
export const barsAddUp = 'A person counted on two days is in two bars and once in the number above.';

/**
 * Replied or opted out as a share of the period's contacted. The counts cover different people (today's replies may
 * answer last week's messages), so past all of them it says so rather than print a share over a hundred percent.
 */
export function shareOfContacted(value: number, contacted: number): string | null {
    if (contacted === 0) return null;
    if (value > contacted) return 'more than the period’s contacted';
    return `${((value / contacted) * 100).toFixed(1)}% of the period’s contacted`;
}
