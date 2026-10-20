import { dayKey } from './format';

export type DayGroup<T> = { day: string; items: T[] };

/** Groups by the person's calendar day, newest day first unless asked to keep the input's order; inside a day the input order stands. */
export function groupByDay<T>(items: T[], at: (item: T) => Date, zone: string, newestFirst = true): DayGroup<T>[] {
    const byDay = new Map<string, T[]>();
    for (const item of items) {
        const key = dayKey(at(item), zone);
        const group = byDay.get(key);
        if (group) group.push(item);
        else byDay.set(key, [item]);
    }
    const days = [...byDay.entries()];
    if (newestFirst) days.sort(([a], [b]) => (a < b ? 1 : a > b ? -1 : 0));
    return days.map(([day, grouped]) => ({ day, items: grouped }));
}
