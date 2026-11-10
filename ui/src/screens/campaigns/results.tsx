import type { CampaignActivityRow, CampaignSummaryRow } from '../../api/types';
import type { Period } from '../../router';
import { number } from '../../time/format';
import { periodLabels } from '../../time/periods';
import { activityColumns, summaryColumns, type Column } from './labels';

type Props = {
    period: Period;
    /** Both answers, or none while they are on their way. */
    shown: { now: CampaignSummaryRow; counts: CampaignActivityRow } | null;
};

const column = (columns: readonly Column[], key: string): Column => columns.find((c) => c.key === key)!;

/**
 * The four figures, each named and defined as the runtime defines it: who is in the campaign now, and who was contacted,
 * replied and opted out in the period. Every number is one the runtime answered; the tab divides none by another.
 */
const figures: readonly { column: Column; inPeriod: boolean; of: (shown: NonNullable<Props['shown']>) => number }[] = [
    { column: column(summaryColumns, 'people'), inPeriod: false, of: (shown) => shown.now.people },
    { column: column(activityColumns, 'contacted'), inPeriod: true, of: (shown) => shown.counts.contacted },
    { column: column(activityColumns, 'replied'), inPeriod: true, of: (shown) => shown.counts.replied },
    { column: column(activityColumns, 'opted_out'), inPeriod: true, of: (shown) => shown.counts.opted_out },
];

/** The period in words, as a figure's measure reads: today, or in the last 7, 30 or 90 days. */
const over = (period: Period) => (period === 'today' ? 'today' : `in the ${periodLabels[period].toLowerCase()}`);

/**
 * The campaign's Results: four tiles, which keep their places, empty, while the numbers are on their way. Each says what
 * it measures over beside its figure, the people now and the other three in the period, so none reads as one measure.
 */
export function CampaignResults({ period, shown }: Props) {
    return (
        <div className="campaign-tabs-results">
            <div className="tiles" role="group" aria-label="The campaign’s results" aria-busy={shown === null}>
                {figures.map(({ column, inPeriod, of }) => {
                    const value = shown ? of(shown) : null;
                    const measure = inPeriod ? over(period) : 'now';
                    const name = `${column.label} ${measure}`;
                    return (
                        <div
                            key={column.key}
                            className="tile"
                            role="figure"
                            aria-label={value === null ? name : `${name}, ${number.format(value)}`}
                            title={column.hint}
                        >
                            <b className={column.key === 'replied' ? 'hot' : undefined}>
                                {value === null ? '…' : number.format(value)}
                            </b>
                            <span className="label">{column.label}</span>
                            <span className="rel">{measure}</span>
                        </div>
                    );
                })}
            </div>
        </div>
    );
}
