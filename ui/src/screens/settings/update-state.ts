import { signal } from '@preact/signals-react';
import type { DesktopUpdateItem, JasonDesktop } from '../../platform/desktop';
import { mode } from '../../platform/mode';
import { can, currentDesktop } from '../../platform/places';

/**
 * The desktop's update item as the page last read it (`updates.item`), shared by Settings' Update section and the
 * sidebar's Settings row; null where the page has none to read: a tab, a plain browser, a desktop of an earlier version.
 */
export const updateAnswer = signal<DesktopUpdateItem | null>(null);

let followers = 0;
let unfollow: (() => void) | null = null;

/**
 * Reads the desktop's update item, and again whenever the desktop says it changed, while anything that draws it is on
 * the page; the function it returns stops that one follower, and the last one stops the reading.
 */
export function followUpdates(desktop: JasonDesktop | undefined = currentDesktop()): () => void {
    followers += 1;
    if (followers === 1) unfollow = read(desktop);
    let done = false;
    return () => {
        if (done) return;
        done = true;
        followers -= 1;
        if (followers > 0) return;
        unfollow?.();
        unfollow = null;
        updateAnswer.value = null;
    };
}

function read(desktop: JasonDesktop | undefined): () => void {
    if (desktop === undefined || !can('updates.item') || !can('updates.onChanged')) return () => undefined;
    if (typeof desktop.updates?.item !== 'function' || typeof desktop.updates.onChanged !== 'function')
        return () => undefined;
    let live = true;
    // Only the last read stands: an answer that comes back after a later one was asked for is dropped.
    let asked = 0;
    const again = () => {
        const mine = ++asked;
        desktop.updates
            .item()
            .then((answer) => {
                if (live && mine === asked) updateAnswer.value = answer;
            })
            .catch(() => undefined);
    };
    const stop = desktop.updates.onChanged(again);
    again();
    return () => {
        live = false;
        stop();
    };
}

/**
 * The answer Settings' Update section draws: in Live, where the desktop shows an update item. None in the Demo, and none
 * from a desktop that shows no item, as its own Demo run does.
 */
export function updateShown(): (DesktopUpdateItem & { item: NonNullable<DesktopUpdateItem['item']> }) | null {
    const answer = updateAnswer.value;
    if (answer === null || answer.item === null || mode.value === 'demo') return null;
    return answer as DesktopUpdateItem & { item: NonNullable<DesktopUpdateItem['item']> };
}

/** The version to mark Settings with: the one the desktop knows of and has not installed, where Update is shown; else null. */
export function updateMark(): string | null {
    const newer = updateShown()?.newer;
    return typeof newer === 'string' && newer !== '' ? newer : null;
}

/** The deb's item, whose updates come with the system's packages and which has no check of its own. */
export const packageItemLabel = 'Updates Come Through the Package';
