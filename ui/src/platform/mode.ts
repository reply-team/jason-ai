import { signal } from '@preact/signals-react';
import { t } from './i18n';

// The mode is which installation the desktop runs on: Live, the person's own, or Demo, a second installation of sample
// data beside it. The page learns it from the desktop alone, once, as it loads, here and nowhere else; a browser shows
// whatever runtime it is paired with, which is Live to the page. The rest of the page reads `mode`, and switches through
// `switchMode`, which asks the desktop to keep the other mode and start again on it.

export type Mode = 'live' | 'demo';

/** The mode this load opened in; Live until `loadMode` has read it, and in a browser. */
export const mode = signal<Mode>('live');

/** Why the last switch did not happen, in the desktop's words; null when none failed. */
export const modeRefusal = signal<string | null>(null);

/**
 * The desktop's sentence when the record said Demo and the demo could not be opened, so this load is the person's own
 * installation; null otherwise, and in a browser.
 */
export const modeUnopened = signal<string | null>(null);

/**
 * Whether the demo's sample is still being made behind the page: a quiet mark in the bar, and the switch off until it
 * is ready. Only ever true in the demo, and only when the desktop says so.
 */
export const modePreparing = signal(false);

/** What the desktop offers for the mode; a desktop older than this page offers none of it. */
export interface DesktopModeSeam {
    get?: () => Promise<unknown>;
    set?: (mode: Mode) => Promise<{ refused: string } | null>;
    onPreparing?: (handler: (preparing: boolean) => void) => () => void;
}

/** The desktop's seam for this load; null in a browser. */
let seam: DesktopModeSeam | null = null;
/** The way to stop following the desktop's word on the sample, for the load before this one. */
let unfollow: (() => void) | null = null;

/**
 * Reads the mode once, as the page loads, from the desktop's seam: `{ mode, unopened }`, or a bare word from a desktop
 * that answers one. A desktop without the member, or whose answer fails or is no mode, is Live, and so is a browser.
 * Never throws.
 */
export async function loadMode(desktop?: { mode?: DesktopModeSeam }): Promise<Mode> {
    modeRefusal.value = null;
    modeUnopened.value = null;
    modePreparing.value = false;
    unfollow?.();
    unfollow = null;
    seam = desktop === undefined ? null : (desktop.mode ?? {});
    // The mark is followed before the mode is read, so a word the desktop sends between the two is not lost; a word
    // heard before the answer outranks the answer's.
    let heard: boolean | null = null;
    try {
        unfollow =
            seam?.onPreparing?.((on) => {
                heard = on === true;
                if (mode.value === 'demo') modePreparing.value = heard;
            }) ?? null;
    } catch {
        unfollow = null;
    }
    let kept: unknown;
    try {
        kept = await seam?.get?.();
    } catch {
        kept = null;
    }
    const answer =
        kept !== null && typeof kept === 'object'
            ? (kept as { mode?: unknown; unopened?: unknown; preparing?: unknown })
            : { mode: kept };
    mode.value = answer.mode === 'demo' ? 'demo' : 'live';
    if (mode.value === 'live' && typeof answer.unopened === 'string' && answer.unopened !== '')
        modeUnopened.value = answer.unopened;
    if (mode.value === 'demo') modePreparing.value = heard ?? answer.preparing === true;
    else {
        unfollow?.();
        unfollow = null;
    }
    return mode.value;
}

/**
 * Asks the desktop to switch to `to`: it keeps the mode and starts again on that installation. The mode in force, and a
 * browser, ask nothing; a refusal is kept in `modeRefusal`.
 */
export async function switchMode(to: Mode): Promise<void> {
    if (to === mode.value || seam === null || modePreparing.value) return;
    modeRefusal.value = null;
    if (seam.set === undefined) {
        modeRefusal.value = t('mode.unsupported');
        return;
    }
    try {
        const answer = await seam.set(to);
        if (answer !== null && typeof answer === 'object' && 'refused' in answer) modeRefusal.value = answer.refused;
    } catch (error) {
        modeRefusal.value = error instanceof Error ? error.message : String(error);
    }
}
