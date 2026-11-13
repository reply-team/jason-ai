import { signal } from '@preact/signals-react';

// The mode the page opens in: Live, the installation's own data, or Demo, invented sample data. It is read once, as the
// page loads, here and nowhere else: in the desktop from the desktop's seam, which keeps it in a file of its own; in a
// browser from the address alone. The page never stores it. The rest of the page reads `mode`, and switches through
// `switchMode`, which in either place loads the page again in the other mode.

export type Mode = 'live' | 'demo';

/** The address's query parameter that names the mode in a browser, before the `#` the page routes in: `?mode=demo`. */
export const modeParameter = 'mode';

/** Pure: the mode an address's search part names: Demo for `mode=demo`, Live for anything else or nothing. */
export function modeOfAddress(search: string): Mode {
    return new URLSearchParams(search).get(modeParameter) === 'demo' ? 'demo' : 'live';
}

/** Pure: the address that opens in `to`: the same one, with the parameter set for Demo and taken away for Live. */
export function addressFor(href: string, to: Mode): string {
    const url = new URL(href);
    if (to === 'demo') url.searchParams.set(modeParameter, 'demo');
    else url.searchParams.delete(modeParameter);
    return url.toString();
}

/** The mode this load opened in; Live until `loadMode` has read it. */
export const mode = signal<Mode>('live');

/** Why the last switch did not happen, in the desktop's words; null when none failed. */
export const modeRefusal = signal<string | null>(null);

/** What the desktop offers for the mode; a desktop older than this page offers none of it. */
export interface DesktopModeSeam {
    get?: () => Promise<unknown>;
    set?: (mode: Mode) => Promise<{ refused: string } | null>;
}

/** Where a browser goes to switch; a test stands in for it. */
export const addressBar = { load: (url: string): void => location.assign(url) };

/** The desktop's seam for this load; null in a browser. */
let seam: DesktopModeSeam | null = null;

/**
 * Reads the mode once, as the page loads: from the desktop when there is one (its seam answers the mode it keeps; a
 * desktop without the member, or whose answer fails, is Live), else from the address. Never throws.
 */
export async function loadMode(desktop?: { mode?: DesktopModeSeam }): Promise<Mode> {
    modeRefusal.value = null;
    if (desktop === undefined) {
        seam = null;
        mode.value = modeOfAddress(location.search);
        return mode.value;
    }
    seam = desktop.mode ?? {};
    let kept: unknown;
    try {
        kept = await seam.get?.();
    } catch {
        kept = null;
    }
    mode.value = kept === 'demo' ? 'demo' : 'live';
    return mode.value;
}

/**
 * Switches to `to`, which loads the page again in it: in a browser by the address, in the desktop by its seam, which
 * keeps the mode and then reloads the window. The mode in force asks nothing; a refusal is kept in `modeRefusal`.
 */
export async function switchMode(to: Mode): Promise<void> {
    if (to === mode.value) return;
    modeRefusal.value = null;
    if (seam === null) {
        addressBar.load(addressFor(location.href, to));
        return;
    }
    if (seam.set === undefined) {
        modeRefusal.value = 'This version of the desktop cannot switch modes.';
        return;
    }
    try {
        const answer = await seam.set(to);
        if (answer !== null && typeof answer === 'object' && 'refused' in answer) modeRefusal.value = answer.refused;
    } catch (error) {
        modeRefusal.value = error instanceof Error ? error.message : String(error);
    }
}
