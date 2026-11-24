import { effect, signal } from '@preact/signals-react';
import { windowDesktop } from '../platform/window-desktop';
import { updateAnswer, updateShown } from '../screens/settings/update-state';
import { cardOf, settledMark, type Card, type Hidden } from './update-card';

/**
 * The card put away with Not now, in this page's memory and nowhere else: no file, no preference, nothing the runtime or
 * the desktop could know. A new start of the application makes a new page, and so brings the card back.
 */
const hidden = signal<Hidden | null>(null);

// The mark follows the desktop's item: it is forgotten when the step changes or a newer version is offered
// (`settledMark`), which is what brings a card put away back by itself.
effect(() => {
    const answer = updateAnswer.value;
    const kept = settledMark(hidden.peek(), answer);
    if (kept !== hidden.peek()) hidden.value = kept;
});

/** What the card above Settings shows now, or null: none. Only in Live, where the desktop shows its update item. */
export function updateCard(): Card | null {
    const shown = updateShown();
    return shown === null ? null : cardOf(shown, hidden.value);
}

/** Puts the card away until the next start of the application; only an offered card has a Not now. */
export function putAway(): void {
    const card = updateCard();
    if (card === null || card.step !== 'offered') return;
    hidden.value = { step: card.step, version: card.version };
}

/**
 * The card's press: the item's own label as the desktop answered it, never one the page composed. At the downloaded step
 * the press asks the desktop to ask first (`ask`), in its own words; at the others it presses as Settings does.
 */
export function pressCard(): void {
    const card = updateCard();
    const desktop = windowDesktop();
    if (card === null || !card.enabled || desktop === undefined || card.step === 'downloading') return;
    const done =
        card.step === 'downloaded'
            ? desktop.updates.press(card.label, { ask: true })
            : desktop.updates.press(card.label);
    done.catch(() => undefined);
}
