// Pure: the Dashboard's arithmetic. Every number is one the page computes from reads it already makes; a tile whose
// number no read gives holds no value, which the panel draws as a dash.
import { t } from '../platform/i18n';
import type { Period } from '../router';
import { periodWindow } from '../time/periods';

/** The period the tiles count over. */
export const dashboardPeriod: Period = '30d';

/** The window of the tiles' read: the Performance screen's own window for thirty days. */
export function dashboardWindow(now: Date, zone: string): { since: string; until: string } {
    return periodWindow(dashboardPeriod, now, zone);
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

export interface Tile {
    label: string;
    /** The number, or null where no fact stands behind it. */
    value: number | null;
    sub: string | null;
}

/** The six tiles in order. Interested, Meetings booked and Bounce rate are facts the runtime does not hold. */
export function tilesOf(input: {
    approvals: number;
    questions: number;
    /** Whether the approvals and questions were read. */
    waitingKnown: boolean;
    activity: ActivityRead | null;
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
        { label: t('dash.tile.interested'), value: null, sub: null },
        { label: t('dash.tile.meetings'), value: null, sub: null },
        { label: t('dash.tile.bounce'), value: null, sub: null },
    ];
}
