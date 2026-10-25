import type { ActivityBucket, ActivityPeriod } from '../api/types';
import { clock, dayKey, shortDate } from './format';

const weekdays = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/**
 * A bucket by name, in the person's zone: `16 Oct, 10:00`, `Mon 12 Oct`, `week of 20 Jul`; a week the window cuts short
 * at its start by its own days, `19 Jul` or `1–3 Aug`, since no week begins on them.
 */
export function bucketName(bucket: ActivityBucket, period: ActivityPeriod, zone: string, now: Date): string {
    const start = new Date(bucket.start);
    const date = shortDate(start, zone, now);
    if (period === 'hour') return `${date}, ${clock(start, zone)}`;
    if (period === 'week') {
        return weekdayOf(start, zone) === 'Mon' ? `week of ${date}` : dayRange(start, new Date(bucket.end), zone, now);
    }
    return `${weekdayOf(start, zone)} ${date}`;
}

/** Where a bucket's people were counted, as a list's head says it: `at 16 Oct, 10:00`, `on Mon 12 Oct`, `in the week of 20 Jul`. */
export function bucketPhrase(bucket: ActivityBucket, period: ActivityPeriod, zone: string, now: Date): string {
    const name = bucketName(bucket, period, zone, now);
    if (period === 'hour') return `at ${name}`;
    return name.startsWith('week of ') ? `in the ${name}` : `on ${name}`;
}

/** The days from one instant to another, not included, in the person's zone: `12 Oct`, `10–16 Oct`, `26 Jul – 23 Oct`. */
export function dayRange(start: Date, end: Date, zone: string, now: Date): string {
    const first = shortDate(start, zone, now);
    const last = shortDate(new Date(end.getTime() - 1), zone, now);
    if (first === last) return first;
    const [d1, m1] = first.split(' ');
    const [d2, m2] = last.split(' ');
    return m1 === m2 && first.split(' ').length === 2 ? `${d1}–${d2} ${m2}` : `${first} – ${last}`;
}

/**
 * What a bar says under it (`short`) and on hover (`long`): an hour by its clock, a day by its weekday, or by its date
 * in a chart of more than a week of `bars`, a week by the day it starts. The bucket running now says so instead: `now`,
 * `today`, `this week`.
 */
export function bucketLabel(
    bucket: ActivityBucket,
    period: ActivityPeriod,
    zone: string,
    now: Date,
    bars: number,
): { short: string; long: string; current: boolean } {
    const start = new Date(bucket.start);
    const current = start.getTime() <= now.getTime() && now.getTime() <= new Date(bucket.end).getTime();
    const long = bucketName(bucket, period, zone, now);
    if (period === 'hour') return { short: current ? 'now' : clock(start, zone).slice(0, 2), long, current };
    const date = shortDate(start, zone, now);
    if (period === 'week') return { short: current ? 'this week' : date, long, current };
    return { short: current ? 'today' : bars > 7 ? date : weekdayOf(start, zone), long, current };
}

function weekdayOf(moment: Date, zone: string): string {
    return weekdays[new Date(dayKey(moment, zone) + 'T00:00:00Z').getUTCDay()]!;
}

/** How many labels fit under a chart, wide and narrow: an hour's is two digits, a day's or a week's a word or a date. */
const fitting: Record<ActivityPeriod, { wide: number; narrow: number }> = {
    hour: { wide: 24, narrow: 8 },
    day: { wide: 10, narrow: 7 },
    week: { wide: 10, narrow: 7 },
};

/** Every how many bars a label is written, so that a chart's labels fit it, wide and narrow. */
export function labelEvery(period: ActivityPeriod, bars: number): { wide: number; narrow: number } {
    const fit = fitting[period];
    return { wide: Math.ceil(bars / fit.wide), narrow: Math.ceil(bars / fit.narrow) };
}

/** Whether a bar's label is written, every `every` bars counted back from the last, which is the bar running now. */
export function labelled(index: number, bars: number, every: number): boolean {
    return (bars - 1 - index) % every === 0;
}
