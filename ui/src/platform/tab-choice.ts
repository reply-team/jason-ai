// What the person chose in this tab with the Demo | Live switch, kept in the tab's own session storage so that a reload
// of that tab keeps it and no other tab or later visit does. Every access is wrapped: a browser that refuses storage
// changes nothing but the memory, and the tab opens as it would on a first load.

export type TabChoice = 'live' | 'demo';

const key = 'jason.ui.tab-choice';

/**
 * Whether this load of the tab has already been opened on its page or its Demo. What a tab opens on is decided once per
 * load, from the person's own choice or, with none, from what waits; an address that comes back to the page later in the
 * same load, Back from the Demo say, is the person leaving it on purpose and is not sent there again.
 */
export const opening = { done: false };

/** The tab's own choice, or null when the person has made none (or the browser would not say). */
export function chosen(): TabChoice | null {
    try {
        const value = sessionStorage.getItem(key);
        return value === 'live' || value === 'demo' ? value : null;
    } catch {
        return null;
    }
}

/** Keeps the person's choice for this tab. */
export function choose(choice: TabChoice): void {
    try {
        sessionStorage.setItem(key, choice);
    } catch {
        // A refusal changes nothing.
    }
}
