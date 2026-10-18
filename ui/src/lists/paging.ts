/** The next cursor page appended to what is shown; an item seen again takes the newer copy's place. */
export function mergePage<T>(existing: T[], next: T[], key: (item: T) => string): T[] {
    const merged = new Map<string, T>();
    for (const item of existing) merged.set(key(item), item);
    for (const item of next) merged.set(key(item), item);
    return [...merged.values()];
}
