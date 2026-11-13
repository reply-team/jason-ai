// Pure: the Dashboard's arithmetic. Every number is one the page computes from reads it already makes; a tile whose
// number no read gives holds no value, which the panel draws as a dash.
import { t } from '../platform/i18n';
import { dayOf, monthsBefore, periodWindow, windowBetweenDays, windowFromDay } from '../time/periods';

/** What the Dashboard's one period can be: the whole page counts over it. */
export type DashboardPeriod = '7d' | '30d' | '3m' | '6m' | 'year' | 'custom';

export const dashboardPeriods: readonly DashboardPeriod[] = ['7d', '30d', '3m', '6m', 'year', 'custom'];

/** The period the Dashboard opens on, every time. */
export const defaultDashboardPeriod: DashboardPeriod = '30d';

export function isDashboardPeriod(value: unknown): value is DashboardPeriod {
    return dashboardPeriods.some((each) => each === value);
}

/** A custom range: two calendar days of the person's zone, `YYYY-MM-DD`, both included; empty while one is not given. */
export interface CustomRange {
    from: string;
    to: string;
}

/**
 * The window the tiles and cards count over, in the person's zone: seven and thirty days are the calendar days back
 * from today (today included), three and six months begin on the same day of the month that many months back, this
 * year on 1 January, each ending now. A custom range begins at its first day's midnight and ends where its last day
 * does, or now. Null for a custom range that is incomplete, backwards or not yet begun: nothing is asked for it.
 */
export function dashboardWindow(
    choice: DashboardPeriod,
    now: Date,
    zone: string,
    custom: CustomRange | null = null,
): { since: string; until: string } | null {
    const today = dayOf(now, zone);
    switch (choice) {
        case '7d':
        case '30d':
            return periodWindow(choice, now, zone);
        case '3m':
            return windowFromDay(monthsBefore(today, 3), now, zone);
        case '6m':
            return windowFromDay(monthsBefore(today, 6), now, zone);
        case 'year':
            return windowFromDay(`${today.slice(0, 4)}-01-01`, now, zone);
        case 'custom':
            return custom && custom.from && custom.to ? windowBetweenDays(custom.from, custom.to, now, zone) : null;
    }
}

/** The range a person who picks Custom begins with: the thirty days up to today, so there is something to read. */
export function startingRange(now: Date, zone: string): CustomRange {
    const window = periodWindow('30d', now, zone);
    return { from: dayOf(new Date(window.since), zone), to: dayOf(now, zone) };
}

/** How the period is named in a sentence: "the last 7 days", "this year", "the chosen dates". */
export function periodWords(choice: DashboardPeriod): string {
    return t(`dash.period.words.${choice}`);
}

/** "Good morning" before 12:00, "Good afternoon" before 18:00, "Good evening" otherwise and before 05:00. */
export function greetingFor(hour: number): string {
    if (hour >= 5 && hour < 12) return t('dash.morning');
    if (hour >= 12 && hour < 18) return t('dash.afternoon');
    return t('dash.evening');
}

const counted = (
    count: number,
    one: 'dash.approval.one' | 'dash.question.one',
    many: 'dash.approval.many' | 'dash.question.many',
): string => t(count === 1 ? one : many, { n: String(count) });

/** "2 approvals · 1 question": a part only above zero; null when nothing waits. */
export function needsSubline(approvals: number, questions: number): string | null {
    const parts = [
        ...(approvals > 0 ? [counted(approvals, 'dash.approval.one', 'dash.approval.many')] : []),
        ...(questions > 0 ? [counted(questions, 'dash.question.one', 'dash.question.many')] : []),
    ];
    return parts.length === 0 ? null : parts.join(' · ');
}

/** Replied over contacted as a percentage to one decimal, dropped when whole; null when nobody was contacted. */
export function rateOf(replied: number, contacted: number): string | null {
    if (contacted <= 0) return null;
    return `${Math.round((replied / contacted) * 1000) / 10}%`;
}

/** Contacted over people, between 0 and 1; null for an agent with no people. */
export function progressOf(contacted: number, people: number): number | null {
    return people > 0 ? Math.min(1, contacted / people) : null;
}

/** A text cut to `max` characters, the last one an ellipsis when it was cut. */
export function cut(text: string, max: number): string {
    return text.length <= max ? text : `${text.slice(0, max - 1)}…`;
}

/** The thirty-day counts across the agents that are not archived. */
export interface ActivityRead {
    contacted: number;
    replied: number;
}

/** What `reply.count` says over the window: replies a role judged, by the word of the verdict. */
export interface ReplyCountRead {
    total: { replies: number; threads: number; categories: CategoryCount[] };
}

export interface CategoryCount {
    category: string;
    replies: number;
    threads: number;
}

/** The words of the verdicts the two tiles read; compared exactly, as the skills write them. */
export const interestedWord = 'interested';
export const meetingBookedWord = 'meeting_booked';

/** The conversations judged to be in one category, or null when the answer holds no entry of that word. */
export function threadsIn(read: ReplyCountRead | null, word: string): number | null {
    return read?.total.categories.find((each) => each.category === word)?.threads ?? null;
}

/** A category's word for a person: the underscores as spaces and a capital first, nothing else assumed about it. */
export function categoryLabel(word: string): string {
    const spaced = word.replace(/_/g, ' ');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

export interface Tile {
    label: string;
    /** The number, or null where no fact stands behind it. */
    value: number | null;
    sub: string | null;
    /** What the number is, said on hover; absent for a tile that needs none. */
    hint?: string;
}

/** The six tiles in order. Interested and Meetings booked count conversations judged so; Bounce rate has no fact yet. */
export function tilesOf(input: {
    approvals: number;
    questions: number;
    /** Whether the approvals and questions were read. */
    waitingKnown: boolean;
    activity: ActivityRead | null;
    replies?: ReplyCountRead | null;
}): Tile[] {
    const { activity } = input;
    const rate = activity ? rateOf(activity.replied, activity.contacted) : null;
    return [
        {
            label: t('dash.tile.needs'),
            value: input.waitingKnown ? input.approvals + input.questions : null,
            sub: input.waitingKnown ? needsSubline(input.approvals, input.questions) : null,
        },
        { label: t('dash.tile.contacted'), value: activity?.contacted ?? null, sub: null },
        {
            label: t('dash.tile.replied'),
            value: activity?.replied ?? null,
            sub: rate === null ? null : t('dash.rate', { rate }),
        },
        {
            label: t('dash.tile.interested'),
            value: threadsIn(input.replies ?? null, interestedWord),
            sub: null,
            hint: t('dash.tile.interested.hint'),
        },
        {
            label: t('dash.tile.meetings'),
            value: threadsIn(input.replies ?? null, meetingBookedWord),
            sub: null,
            hint: t('dash.tile.meetings.hint'),
        },
        { label: t('dash.tile.bounce'), value: null, sub: null },
    ];
}
