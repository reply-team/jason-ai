import { act, define } from './actions';
import { cardHead } from './card-frame';
import { html, type Html } from './html';
import { here } from './state';

// The two cards the chat's AI asks for that only lead to a place of the window. Each holds no member, so nothing on it is
// the AI's, and it reads nothing, so it says no fact: its header, one sentence of the window's that is true wherever the
// card stands, and one button that opens the place. The AI card opens Connect, where the AI hosts and the model keys
// are; the Reply card opens the Accounts page, where Reply's sign-in and its accounts are. What is done there is done
// with that place's own controls, which say themselves what a browser tab cannot do; the card holds no key, no login, no
// form and no link, and acts on nothing.

export type PlaceKind = 'ai_setup' | 'reply_setup';

/** The window's sentence on each card: where the place is, never what stands there now. */
const sentences: Readonly<Record<PlaceKind, string>> = {
    ai_setup: 'The AI hosts and the model keys are in Connect.',
    reply_setup: 'Reply’s sign-in and its accounts are on the Accounts page.',
};

define('card.places.connect', () => {
    here().dock.show('connect');
});

define('card.places.accounts', () => {
    here().dock.page({ screen: 'accounts' });
});

/** A card that leads to a place: drawn the same in every conversation and every place, reading nothing. */
export function placeCard(kind: PlaceKind, header: string): Html {
    const head = cardHead(header);
    return html`<div class="ask chat-card chat-place" role="group" aria-label="${head.words}">
        <div class="ask-h card-top">${head.view}</div>
        <p class="ask-w">${sentences[kind]}</p>
        <div class="ask-a">
            ${
                kind === 'ai_setup'
                    ? html`<button class="btn sm" type="button" ${act('card.places.connect')}>Open Connect</button>`
                    : html`<button class="btn sm" type="button" ${act('card.places.accounts')}>Open Accounts</button>`
            }
        </div>
    </div>`;
}
