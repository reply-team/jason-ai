import { signal } from '@preact/signals-react';
import { can, earlier } from './places';
import type { DesktopFullAccessStanding, DesktopRefusal, JasonDesktop } from './desktop';

// The standing choice of Full access as the desktop last said it: the mode menu offers its take-back while it stands,
// and the System screen shows it. The page only reads it and takes it back; only the person's answer to the desktop's own
// box gives it. Nothing of it is kept in the page's storage.

/** The standing choice, or null when none stands or the desktop has not said yet. */
export const fullAccessStanding = signal<DesktopFullAccessStanding | null>(null);

/** What the menu offers, after the modes, while the standing choice stands: its take-back. */
export const takeBackWords = 'Full access: ask each time again';

let known: Promise<void> = Promise.resolve();

/** Reads the standing choice and follows its changes; the function it returns stops that. */
export function followStanding(desktop: JasonDesktop): () => void {
    // A desktop of an earlier version has no standing choice to read or take back.
    if (!can('fullAccess.standing') || !earlier(desktop).fullAccess) {
        fullAccessStanding.value = null;
        return () => undefined;
    }
    known = desktop.fullAccess
        .standing()
        .then((standing) => {
            fullAccessStanding.value = standing;
        })
        .catch(() => {
            fullAccessStanding.value = null;
        });
    return desktop.fullAccess.onChanged((standing) => {
        fullAccessStanding.value = standing;
    });
}

/**
 * Reads the standing choice again where the page is about to offer or show it, the mode menu as it opens, since a file
 * changed by anything but the desktop's own acts is said to no one. A read that fails changes nothing.
 */
export function readStandingAgain(desktop: JasonDesktop): void {
    if (!can('fullAccess.standing') || !earlier(desktop).fullAccess) return;
    void desktop.fullAccess
        .standing()
        .then((standing) => {
            fullAccessStanding.value = standing;
        })
        .catch(() => undefined);
}

/** Settles once the desktop's first answer about the standing choice is in. */
export const standingKnown = (): Promise<void> => known;

/** Takes the standing choice back: null once done, or the desktop's sentence when it could not. */
export async function takeBackStanding(desktop: JasonDesktop): Promise<string | null> {
    if (!can('fullAccess.takeBack') || !earlier(desktop).fullAccess) return null;
    const answer: DesktopRefusal | null = await desktop.fullAccess.takeBack();
    if (answer !== null) return answer.refused;
    fullAccessStanding.value = null;
    return null;
}
