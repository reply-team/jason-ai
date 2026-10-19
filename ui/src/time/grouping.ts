import { dayKey } from './format';

export type DayGroup<T> = { day: string; items: T[] };

/** Groups by the person's calendar day, newest day first; inside a day the input order stands. */
export function groupByDay<T>(items: T[], at: (item: T) => Date, zone: string): DayGroup<T>[] {
    const byDay = new Map<string, T[]>();
    for (const item of items) {
        const key = dayKey(at(item), zone);
        const group = byDay.get(key);
        if (group) group.push(item);
        else byDay.set(key, [item]);
    }
    return [...byDay.entries()]
        .sort(([a], [b]) => (a < b ? 1 : a > b ? -1 : 0))
        .map(([day, grouped]) => ({ day, items: grouped }));
}
