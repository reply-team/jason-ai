import type { Period } from '../router';

const dayMs = 24 * 60 * 60 * 1000;

/** What each period is called wherever the page names it. */
export const periodLabels: Record<Period, string> = {
    today: 'Today',
    '7d': 'Last 7 days',
    '30d': 'Last 30 days',
    '90d': 'Last 90 days',
};

/** How many whole days before today each period starts: today alone, or the six, twenty-nine or eighty-nine before it as well. */
const daysBefore: Record<Period, number> = { today: 0, '7d': 6, '30d': 29, '90d': 89 };

/**
 * The period's two instants, as the runtime takes them: from the person's local midnight that many days ago, included,
 * to now, not included. The midnight is the zone's own, so a day the clocks change on is as long as it really is.
 */
export function periodWindow(period: Period, now: Date, zone: string): { since: string; until: string } {
    const today = localDay(now, zone);
    const first = new Date(Date.parse(`${today}T00:00:00Z`) - daysBefore[period] * dayMs).toISOString().slice(0, 10);
    return { since: midnight(first, zone).toISOString(), until: now.toISOString() };
}

function fields(at: Date, zone: string): Record<string, number> {
    const formatter = new Intl.DateTimeFormat('en-GB', {
        timeZone: zone,
        hourCycle: 'h23',
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
    });
    return Object.fromEntries(formatter.formatToParts(at).map((part) => [part.type, Number(part.value)]));
}

function localDay(at: Date, zone: string): string {
    const f = fields(at, zone);
    return `${f['year']}-${String(f['month']).padStart(2, '0')}-${String(f['day']).padStart(2, '0')}`;
}

/** How far the zone's wall clock is ahead of UTC at this instant. */
function offset(at: Date, zone: string): number {
    const f = fields(at, zone);
    const wall = Date.UTC(f['year']!, f['month']! - 1, f['day'], f['hour'], f['minute'], f['second']);
    return wall - Math.floor(at.getTime() / 1000) * 1000;
}

/**
 * The instant a calendar day begins in the zone: asked twice, because the first guess may sit across a change of clocks.
 * A day whose clocks go forward at midnight has no 00:00; it begins at the change, which the first guess lands on.
 */
function midnight(day: string, zone: string): Date {
    const wall = Date.parse(`${day}T00:00:00Z`);
    const guess = new Date(wall - offset(new Date(wall), zone));
    const found = new Date(wall - offset(guess, zone));
    return localDay(found, zone) === day ? found : guess;
}
