/**
 * The update item as the page's Settings reads and presses it: the item the menu draws, answered without its act, and a
 * press that acts only on the item the person saw. Pure: the desk draws the item (`Desk.updateItem`) and presses it
 * (`Desk.pressUpdate`).
 */

import type { MenuItem } from './updates.ts';

/**
 * What the seam's `updates.item` answers: the desktop's version, the update item as the menu shows it, or none, and what
 * the person's last check found (`Updater.said`), or null. This desktop always sends `said`; it is optional because the
 * type is held equal to the page's, which an earlier desktop answers without it.
 */
export interface UpdateItemAnswer {
    version: string;
    item: { label: string; enabled: boolean } | null;
    said?: string | null;
}

/**
 * The answer of `updates.item`: the item's label and whether it is enabled, never its act, which the press reads again,
 * and the sentence of the person's last check, wherever it was asked from.
 */
export function updateAnswerOf(version: string, item: MenuItem | null, said: string | null): UpdateItemAnswer {
    return { version, item: item === null ? null : { label: item.label, enabled: item.enabled }, said };
}

/**
 * The act of a press from the page, which sends the label it drew: the item's act only where the item is enabled now and
 * its label is that very text, so a click lands only on the step the person saw. Where the item moved on before the
 * page drew it again, none: nothing is done, and the page is drawn again by the word of the change it was about to get.
 */
export function actShown(item: MenuItem | null, label: unknown): MenuItem['act'] {
    if (item === null || !item.enabled || typeof label !== 'string' || label !== item.label) return null;
    return item.act;
}
