import { t } from './platform/i18n';
import './own-place.css';

/** What a plain browser tab at the runtime's `/ui/` draws: one sentence, with nothing to press or follow. */
export function showOwnPlace(element: HTMLElement): void {
    const words = document.createElement('p');
    words.className = 'own-place';
    words.textContent = t('ownPlace.message');
    element.replaceChildren(words);
}
