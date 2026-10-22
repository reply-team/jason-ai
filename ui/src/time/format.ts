const dayMs = 24 * 60 * 60 * 1000;

// Fixed English names: the same on every machine, whatever ICU data it carries (en-GB abbreviates September as "Sept").
const weekdays = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const months = [
    'January',
    'February',
    'March',
    'April',
    'May',
    'June',
    'July',
    'August',
    'September',
    'October',
    'November',
    'December',
];
const shortMonths = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

function parts(at: Date, zone: string): Record<string, string> {
    const formatter = new Intl.DateTimeFormat('en-GB', {
        timeZone: zone,
        hourCycle: 'h23',
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
    });
    return Object.fromEntries(formatter.formatToParts(at).map((part) => [part.type, part.value]));
}

/** The wall clock where the person is, 24-hour: `09:12`. */
export function clock(at: Date, zone: string): string {
    const p = parts(at, zone);
    return `${p['hour']}:${p['minute']}`;
}

/** The calendar day where the person is, as `YYYY-MM-DD`: the key lists group by. */
export function dayKey(at: Date, zone: string): string {
    const p = parts(at, zone);
    return `${p['year']}-${p['month']}-${p['day']}`;
}

/** Today, Yesterday, Tomorrow, a weekday with its date inside the last week, else the date; the year only when it is not this one. */
export function dayLabel(key: string, todayKey: string): string {
    if (key === todayKey) return 'Today';
    const day = new Date(key + 'T00:00:00Z');
    const today = new Date(todayKey + 'T00:00:00Z');
    const daysAgo = Math.round((today.getTime() - day.getTime()) / dayMs);
    if (daysAgo === 1) return 'Yesterday';
    if (daysAgo === -1) return 'Tomorrow';
    const sameYear = key.slice(0, 4) === todayKey.slice(0, 4);
    const date = `${day.getUTCDate()} ${months[day.getUTCMonth()]}`;
    if (daysAgo > 1 && daysAgo < 7) return `${weekdays[day.getUTCDay()]} ${date}`;
    return sameYear ? date : `${date} ${key.slice(0, 4)}`;
}

/** How long ago, or how far ahead, in the coarsest unit that still says something. */
export function relative(at: Date, now: Date): string {
    const ms = at.getTime() - now.getTime();
    const ahead = ms > 0;
    const abs = Math.abs(ms);
    const minutes = Math.round(abs / 60000);
    const hours = Math.round(abs / 3600000);
    const days = Math.round(abs / dayMs);
    if (abs < 60000) return 'just now';
    if (minutes < 60) return ahead ? `in ${minutes} min` : `${minutes} min ago`;
    if (hours < 24) return ahead ? `in ${hours} h` : `${hours} h ago`;
    if (!ahead && days === 1) return 'yesterday';
    if (days < 30) return ahead ? `in ${days} d` : `${days} d ago`;
    return `${at.getUTCDate()} ${shortMonths[at.getUTCMonth()]}`;
}

/**
 * Where a person is and what time it is there: the time zone's last part as a place (`America/Mexico_City` is
 * Mexico City) and the clock there now. Null for a zone the browser does not know.
 */
export function place(zone: string, now: Date): { city: string; time: string } | null {
    try {
        const time = clock(now, zone);
        return { city: zone.split('/').at(-1)!.replaceAll('_', ' '), time };
    } catch {
        return null;
    }
}

/** A day and a short month where the person is, `3 Oct`, and the year when it is not this one: `31 Dec 2025`. */
export function shortDate(at: Date, zone: string, now: Date): string {
    const p = parts(at, zone);
    const date = `${Number(p['day'])} ${shortMonths[Number(p['month']) - 1]}`;
    return p['year'] === parts(now, zone)['year'] ? date : `${date} ${p['year']}`;
}

/** A moment outside a list grouped by day: the clock alone today, else the short date before it, `3 Oct, 18:02`. */
export function when(at: Date, now: Date, zone: string): string {
    const time = clock(at, zone);
    return dayKey(at, zone) === dayKey(now, zone) ? time : `${shortDate(at, zone, now)}, ${time}`;
}
