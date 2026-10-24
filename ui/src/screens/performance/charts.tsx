import type { ActivityBucket, ActivityPeriod } from '../../api/types';
import { bucketLabel, dayRange, labelEvery, labelled } from '../../time/buckets';
import { number } from '../../time/format';
import { barsAddUp, perBucket } from './labels';

type Charted = 'contacted' | 'replied';

/** The first day to the last the buckets cover, in the person's zone: `10–16 Oct`. */
function span(buckets: readonly ActivityBucket[], zone: string, now: Date): string {
    if (buckets.length === 0) return '';
    return dayRange(new Date(buckets[0]!.start), new Date(buckets.at(-1)!.end), zone, now);
}

/** One count's bars: each the share of the highest, the highest and the bucket running now written above. */
function Chart({
    count,
    title,
    buckets,
    period,
    zone,
    now,
    chosen,
    onBar,
}: {
    count: Charted;
    title: string;
    buckets: readonly ActivityBucket[];
    period: ActivityPeriod;
    zone: string;
    now: Date;
    chosen: { count?: string | undefined; from?: string | undefined; to?: string | undefined };
    onBar: (count: Charted, bucket: ActivityBucket) => void;
}) {
    const max = Math.max(0, ...buckets.map((bucket) => bucket.value));
    const name = `${title} ${perBucket[period]}`;
    const range = span(buckets, zone, now);
    const every = labelEvery(period, buckets.length);
    const busy = buckets.length === 0;
    return (
        <div class="card chart" role="group" aria-label={range ? `${name}, ${range}` : name} aria-busy={busy}>
            <h2 title={barsAddUp}>
                {name} <span class="hint">{range}</span>
            </h2>
            <div class="bars">
                {buckets.map((bucket, i) => {
                    const label = bucketLabel(bucket, period, zone, now, buckets.length);
                    const on = chosen.count === count && chosen.from === bucket.start && chosen.to === bucket.end;
                    const written = max > 0 && (bucket.value === max || label.current);
                    return (
                        <button
                            key={bucket.start}
                            type="button"
                            class={`bar${label.current ? ' today' : ''}${on ? ' on' : ''}${
                                labelled(i, buckets.length, every.wide) ? '' : ' unlabelled'
                            }${labelled(i, buckets.length, every.narrow) ? '' : ' unlabelled-narrow'}`}
                            style={`--v: ${max > 0 ? (bucket.value / max).toFixed(3) : 0}`}
                            title={`${label.long} · ${number.format(bucket.value)}`}
                            aria-label={`${label.long}, ${number.format(bucket.value)}`}
                            aria-pressed={on}
                            onClick={() => onBar(count, bucket)}
                        >
                            <i />
                            {written && <span class="v">{number.format(bucket.value)}</span>}
                            <span class="d">{label.short}</span>
                        </button>
                    );
                })}
                {max === 0 && !busy && (
                    <div class="none">
                        <span>Nobody yet</span>
                    </div>
                )}
            </div>
        </div>
    );
}

/** The two charts, or their frames while the series are on their way. */
export function Charts(props: {
    series: { contacted: ActivityBucket[]; replied: ActivityBucket[] } | null;
    period: ActivityPeriod;
    zone: string;
    now: Date;
    chosen: { count?: string | undefined; from?: string | undefined; to?: string | undefined };
    onBar: (count: Charted, bucket: ActivityBucket) => void;
}) {
    const { series, ...rest } = props;
    return (
        <div class="charts">
            <Chart count="contacted" title="Contacted" buckets={series?.contacted ?? []} {...rest} />
            <Chart count="replied" title="Replied" buckets={series?.replied ?? []} {...rest} />
        </div>
    );
}
