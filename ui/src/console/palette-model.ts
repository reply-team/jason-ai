// The search palette's dense logic, with no page in it: which entries a typed text finds, in what order, and the words
// the shortcut is named with. The palette itself (`palette.tsx`) draws and opens; this file only decides.

/** What an entry is, which the palette says as a small word on its right. */
export type Kind = 'page' | 'settings' | 'action' | 'agent' | 'chat' | 'person';

export interface Entry {
    /** Unique among the entries shown; the list's key. */
    key: string;
    kind: Kind;
    /** What the row says. */
    label: string;
    /** Words the entry is also found by, which no row shows. */
    words: string;
}

/** The most rows the palette draws. */
export const resultLimit = 12;
/** The most people the palette asks for, and shows, from what is typed. */
export const peopleLimit = 5;
/** How long the palette waits after the last key before it asks for people. */
export const typingPause = 250;

/** A text as it is compared: lower case, with accents taken off. */
export function plain(text: string): string {
    return text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();
}

/** The words of a typed text, each of which an entry must hold. */
export function tokens(text: string): string[] {
    return plain(text)
        .split(/\s+/)
        .filter((each) => each.length > 0);
}

/**
 * The entries the text finds, best first, at most `limit`. Every word typed must be in the entry's label, its kind word
 * or its extra words; an entry whose label starts with the text comes first, then one with a word of its label that
 * does, then the rest, each group in the order the entries were given. With nothing typed, every entry stands.
 */
export function search(
    entries: readonly Entry[],
    text: string,
    kindWord: (kind: Kind) => string,
    limit = resultLimit,
): Entry[] {
    const typed = tokens(text);
    if (typed.length === 0) return entries.slice(0, limit);
    const whole = typed.join(' ');
    const ranked: { entry: Entry; rank: number }[] = [];
    for (const entry of entries) {
        const label = plain(entry.label);
        const held = `${label} ${plain(kindWord(entry.kind))} ${plain(entry.words)}`;
        if (!typed.every((word) => held.includes(word))) continue;
        const rank = label.startsWith(whole)
            ? 0
            : label.split(/[\s,.-]+/).some((word) => word.startsWith(whole))
              ? 1
              : typed.every((word) => label.includes(word))
                ? 2
                : 3;
        ranked.push({ entry, rank });
    }
    return ranked
        .sort((a, b) => a.rank - b.rank)
        .slice(0, limit)
        .map((each) => each.entry);
}

/** The key's name on the system the window is on: the Command sign on macOS, Ctrl elsewhere. */
export function hintFor(platform: string): 'mac' | 'other' {
    return platform === 'darwin' ? 'mac' : 'other';
}

/**
 * Whether a key press is the palette's shortcut: the K key with Meta or Ctrl, and no Alt or Shift. The key is taken by
 * the letter it types or by its place on the keyboard, so a layout that types another letter there (Cyrillic, say) works.
 */
export function isShortcut(event: {
    key: string;
    code?: string;
    ctrlKey: boolean;
    metaKey: boolean;
    altKey: boolean;
    shiftKey: boolean;
}): boolean {
    return (
        (event.key.toLowerCase() === 'k' || event.code === 'KeyK') &&
        (event.ctrlKey || event.metaKey) &&
        !event.altKey &&
        !event.shiftKey
    );
}

/** The selection after an arrow key: it wraps at both ends, and stays at the first where there is nothing to select. */
export function moved(index: number, by: 1 | -1, count: number): number {
    if (count === 0) return 0;
    return (index + by + count) % count;
}
