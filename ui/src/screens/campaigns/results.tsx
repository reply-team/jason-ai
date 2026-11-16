import type { ActivityBucket, CampaignActivityRow, CampaignSummaryRow } from '../../api/types';
import { Notice } from '../../layout/notice';
import { t } from '../../platform/i18n';
import type { Period } from '../../router';
import { number, relative } from '../../time/format';
import { bucketOf, periodLabels } from '../../time/periods';
import { Charts } from '../performance/charts';
import type { Read, Review } from './store';
import { activityColumns, summaryColumns, type Column } from './labels';

type Shown = {
    now: CampaignSummaryRow;
    counts: CampaignActivityRow;
    interested: Read<number>;
    series: Read<{ contacted: ActivityBucket[]; replied: ActivityBucket[]; readAt: Date }>;
    review: Read<Review>;
};

type Props = {
    period: Period;
    /** Every answer, or none while they are on their way. */
    shown: Shown | null;
    /** The counts' read failed; why is said above the tab, and no figure is drawn. */
    failed: boolean;
    zone: string;
    now: Date;
};

const column = (columns: readonly Column[], key: string): Column => columns.find((c) => c.key === key)!;

/**
 * The counts, each named and defined as the runtime defines it: who is in the campaign now, and who was contacted,
 * replied, bounced and opted out in the period. Every number is one the runtime answered; the tab divides none by another.
 */
const figures: readonly { column: Column; inPeriod: boolean; of: (shown: Shown) => number }[] = [
    { column: column(summaryColumns, 'people'), inPeriod: false, of: (shown) => shown.now.people },
    { column: column(activityColumns, 'contacted'), inPeriod: true, of: (shown) => shown.counts.contacted },
    { column: column(activityColumns, 'replied'), inPeriod: true, of: (shown) => shown.counts.replied },
    { column: column(activityColumns, 'bounced'), inPeriod: true, of: (shown) => shown.counts.bounced },
    { column: column(activityColumns, 'opted_out'), inPeriod: true, of: (shown) => shown.counts.opted_out },
];

/** The period in words, as a figure's measure reads: today, or in the last 7, 30 or 90 days. */
const over = (period: Period) => (period === 'today' ? 'today' : `in the ${periodLabels[period].toLowerCase()}`);

type Tile = {
    key: string;
    label: string;
    measure: string;
    hint: string;
    /** A number; null while on its way; or that it could not be read, with the runtime's reason when it gave one. */
    value: number | null | { unread: string | null };
    hot?: boolean;
};

function Tiles({ tiles, busy }: { tiles: Tile[]; busy: boolean }) {
    return (
        <div className="tiles" role="group" aria-label="The agent’s results" aria-busy={busy}>
            {tiles.map(({ key, label, measure, hint, value, hot }) => {
                const name = `${label} ${measure}`;
                const unread = typeof value === 'object' && value !== null;
                const written = value === null ? '…' : unread ? '—' : number.format(value);
                return (
                    <div
                        key={key}
                        className="tile"
                        role="figure"
                        aria-label={
                            value === null
                                ? name
                                : unread
                                  ? `${name}, not read${value.unread ? `: ${value.unread}` : ''}`
                                  : `${name}, ${written}`
                        }
                        title={hint}
                    >
                        <b className={hot ? 'hot' : unread ? 'none' : undefined}>{written}</b>
                        <span className="label">{label}</span>
                        <span className="rel">{measure}</span>
                    </div>
                );
            })}
        </div>
    );
}

/** When Jason last finished reviewing the agent, in its own words: never a bare role name. */
function reviewLine(review: Read<Review>, now: Date): string | null {
    if (review.state === 'pending') return null;
    if (review.state === 'failed')
        return `Jason’s last review could not be read${review.reason ? `: ${review.reason}` : '.'}`;
    if (review.value === null) return 'Jason has not reviewed this agent yet';
    return `Jason last reviewed this agent ${relative(new Date(review.value), now)}`;
}

/**
 * The campaign's Results: the counts as tiles, which keep their places, empty, while the numbers are on their way, then
 * the period's contacted and replied by bucket, and when Jason last reviewed the agent. A read that fails alone says so
 * where it would be drawn. Each tile says what it measures over beside its figure, so none reads as one measure.
 */
export function CampaignResults({ period, shown, failed, zone, now }: Props) {
    if (failed) return null;
    const measure = over(period);
    const tiles: Tile[] = [
        ...figures.map(({ column, inPeriod, of }) => ({
            key: column.key,
            label: column.label,
            measure: inPeriod ? measure : 'now',
            hint: column.hint,
            value: shown ? of(shown) : null,
            hot: column.key === 'replied',
        })),
        {
            key: 'interested',
            label: t('dash.tile.interested'),
            measure: `conversations ${measure}`,
            hint: t('dash.tile.interested.hint'),
            value: !shown
                ? null
                : shown.interested.state === 'ok'
                  ? shown.interested.value
                  : shown.interested.state === 'failed'
                    ? { unread: shown.interested.reason }
                    : null,
        },
    ];
    const series = shown?.series.state === 'ok' ? shown.series.value : null;
    const line = shown ? reviewLine(shown.review, now) : null;
    return (
        <div className="campaign-tabs-results">
            <Tiles tiles={tiles} busy={shown === null} />
            {shown?.series.state === 'failed' ? (
                <Notice danger>
                    The charts could not be read{shown.series.reason ? `: ${shown.series.reason}` : '.'}
                </Notice>
            ) : (
                <Charts series={series} period={bucketOf(period)} zone={zone} now={series?.readAt ?? now} chosen={{}} />
            )}
            {shown && shown.counts.contacted === 0 && (
                <p className="review">Results fill in as people get their first messages.</p>
            )}
            {line && <p className="review">{line}</p>}
        </div>
    );
}
