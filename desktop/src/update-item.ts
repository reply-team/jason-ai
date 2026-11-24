/**
 * The update item as the page's Settings reads and presses it: the item the menu draws, answered without its act, and a
 * press that acts only on the item the person saw. Pure: the desk draws the item (`Desk.updateItem`) and presses it
 * (`Desk.pressUpdate`).
 */

import type { MenuItem, UpdateState } from './updates.ts';

/**
 * What the seam's `updates.item` answers: the desktop's version, the update item as the menu shows it, or none, what
 * the person's last check found (`Updater.said`), or null, and the version the desktop knows of and has not installed
 * (`Desk.updateNewer`), or null, which the page marks Settings with. This desktop always sends `said` and `newer`; they
 * are optional because the type is held equal to the page's, which an earlier desktop answers without them.
 */
export interface UpdateItemAnswer {
    version: string;
    item: { label: string; enabled: boolean } | null;
    said?: string | null;
    newer?: string | null;
    step?: UpdateStep | null;
}

/** The three steps of an update the page's card has words for; every other step of the updater is sent as null. */
export type UpdateStep = 'offered' | 'downloading' | 'downloaded';

/**
 * The answer of `updates.item`: the item's label and whether it is enabled, never its act, which the press reads again,
 * the sentence of the person's last check, wherever it was asked from, and the version known and not installed.
 */
export function updateAnswerOf(version: string, item: MenuItem | null, said: string | null, newer: string | null, step: UpdateStep | null = null): UpdateItemAnswer {
    return { version, item: item === null ? null : { label: item.label, enabled: item.enabled }, said, newer, step };
}

/**
 * The step the page's card shows: the updater's own step when the item is the updater's and the step is one of the three
 * the card has words for, else null (idle, checking, installing, the deb's restart, a build that is not installed, a demo
 * run). `item` is the item the menu shows now: where it has no act of the updater's, there is no step.
 */
export function updateStepOf(state: UpdateState, item: MenuItem | null): UpdateStep | null {
    if (item === null || item.act === 'restart') return null;
    return state.step === 'offered' || state.step === 'downloading' || state.step === 'downloaded' ? state.step : null;
}

/**
 * Whether a press from the page asks first: only a press that names `ask` and whose act is the install, which stops the
 * runtime and restarts the desktop. The question is the desktop's own (`installQuestion`), the one the key gets; any
 * other act asks nothing here.
 */
export function pressAsks(act: MenuItem['act'], options: unknown): boolean {
    if (act !== 'install' || typeof options !== 'object' || options === null) return false;
    return (options as { ask?: unknown }).ask === true;
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
