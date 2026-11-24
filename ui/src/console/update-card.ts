import type { DesktopUpdateItem } from '../platform/desktop';

// The update card above Settings in Jason Desktop: what it shows, from the desktop's update item and the page's one
// memory of a card put away. Pure: no DOM, no signal. The item is the desktop's to say (`updates.item`); the card reads
// its `step` and its `newer` version and never a step from the label, so a desktop that says no step has no card.

/** The steps the card has words for. */
export type CardStep = 'offered' | 'downloading' | 'downloaded';

/** What the card shows: the step, the version, and the item's own label and whether it can be pressed now. */
export interface Card {
    step: CardStep;
    version: string;
    label: string;
    enabled: boolean;
}

/** A card put away with Not now: the step and the version it showed. Kept in the page's memory only. */
export interface Hidden {
    step: CardStep;
    version: string;
}

const steps: readonly string[] = ['offered', 'downloading', 'downloaded'];

/** Whether `version` is newer than `than`, read as dotted numbers; one that cannot be read is taken as newer when it differs. */
function newerThan(version: string, than: string): boolean {
    if (version === than) return false;
    const a = version.split('.').map((part) => (/^\d+$/.test(part) ? Number(part) : Number.NaN));
    const b = than.split('.').map((part) => (/^\d+$/.test(part) ? Number(part) : Number.NaN));
    if (a.some(Number.isNaN) || b.some(Number.isNaN)) return true;
    for (let i = 0; i < Math.max(a.length, b.length); i += 1) {
        const x = a[i] ?? 0;
        const y = b[i] ?? 0;
        if (x !== y) return x > y;
    }
    return false;
}

/**
 * What the card shows for the item the desktop answered and the card put away, or null: no card. None without an answer
 * or an item, at a step the card has no words for (the desktop sends null at every other step, and nothing at all from
 * a desktop of an earlier version), or without the version; and none while the same step still offers the version the
 * person put away. A later step, or a newer version, shows it again.
 */
export function cardOf(answer: DesktopUpdateItem | null, hidden: Hidden | null): Card | null {
    if (answer === null || answer.item === null) return null;
    const step = answer.step;
    if (typeof step !== 'string' || !steps.includes(step)) return null;
    const version = answer.newer;
    if (typeof version !== 'string' || version === '') return null;
    if (hidden !== null && hidden.step === step && !newerThan(version, hidden.version)) return null;
    return { step: step as CardStep, version, label: answer.item.label, enabled: answer.item.enabled };
}

/**
 * The mark to keep after the item moved: the same while the step and the version stand, and while nothing has been read;
 * none once the step has changed or a newer version is offered, so a card put away comes back by itself and, if the
 * step then returns to the one it was put away at, is offered again.
 */
export function settledMark(hidden: Hidden | null, answer: DesktopUpdateItem | null): Hidden | null {
    if (hidden === null) return null;
    if (answer === null) return hidden;
    if (answer.step !== hidden.step) return null;
    if (typeof answer.newer === 'string' && answer.newer !== '' && newerThan(answer.newer, hidden.version)) return null;
    return hidden;
}
