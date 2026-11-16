import { signal } from '@preact/signals-react';
import { ApiError } from '../api/client';
import { asOf, drawsAs, lastReading } from './card-model';
import { cardLayout, placeOf, readCard, type Card, type CardKind, type PlacedCard } from './cards';
import { act, define } from './actions';
import type { Chat } from './chat-model';
import { html, type Html } from './html';
import { parseRoute } from '../router';
import { chats, here } from './state';

// What every data card the chat's AI asks for shares: its header in the desktop's words, the minute its read was
// answered ("As of"), a Refresh that reads it again and decides nothing, "On record now" over the runtime's facts, and
// the stub an earlier card of a conversation opened again stands as until "Show now" reads it. A card reads once, when
// it is first drawn, and again only on its own Refresh: it never follows the journal, and nothing it read is kept in the
// conversation. What it read is held here, by chat and by the index of the text entry that holds the card, for as long
// as that entry is the one drawn: a conversation opened again draws entries of its own, so it reads afresh.

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

/** What the runtime said when a read failed: its code, when it gave one, and its words. */
export interface Said {
    code: string | null;
    message: string;
}

export const saidOf = (error: unknown): Said =>
    error instanceof ApiError
        ? { code: error.code, message: error.message }
        : { code: null, message: error instanceof Error ? error.message : String(error) };

/** One card's read: the entry it was drawn from, and where the read stands; `at` is the moment it was answered. */
export type Held<T> = { entry: object; serial: number } & (
    { state: 'reading' } | { state: 'read'; at: number; facts: T }
);

/** By chat id, then by the index of the text entry that holds the card: what each data card drawn has read. */
export const cardReads = signal<Readonly<Record<string, Readonly<Record<number, Held<unknown>>>>>>({});

let serials = 0;

function put(chatId: string, index: number, held: Held<unknown>): void {
    cardReads.value = { ...cardReads.value, [chatId]: { ...cardReads.value[chatId], [index]: held } };
}

/** What a card has read, when the entry drawn is the one it read for; null for a card not read in this drawing. */
export function heldOf<T>(chatId: string, index: number, entry: object): Held<T> | null {
    const held = cardReads.value[chatId]?.[index];
    return held !== undefined && held.entry === entry ? (held as Held<T>) : null;
}

/**
 * Reads a card's facts now: "Reading..." until `read` answers, then what it answered and when. `read` says a refusal in
 * its facts and never throws. A read already under way for the card is left to finish.
 */
export function startRead<T>(chatId: string, index: number, entry: object, read: () => Promise<T>): void {
    if (heldOf(chatId, index, entry)?.state === 'reading') return;
    const serial = ++serials;
    put(chatId, index, { entry, serial, state: 'reading' });
    void read().then((facts) => {
        // Only the read the card is waiting for lands; a conversation drawn afresh since has forgotten it.
        if (cardReads.value[chatId]?.[index]?.serial === serial)
            put(chatId, index, { entry, serial, state: 'read', at: Date.now(), facts });
    });
}

/** The cards asked for while the thread was being drawn, read once the drawing is done: a drawing changes nothing. */
const asked = new Set<string>();

/**
 * How a card is drawn now, and its read asked for when it is the conversation's last card that reads and this drawing
 * has not read it: a thread is drawn inside a computed value, so the read starts once the drawing is done.
 */
export function drawn<T>(
    chat: Chat,
    index: number,
    last: number | null,
    read: () => Promise<T>,
): { held: Held<T> | null; stub: boolean } {
    const entry = chat.entries[index]!;
    const held = heldOf<T>(chat.id, index, entry);
    const how = drawsAs(index, last, held !== null);
    if (how === 'read') {
        const key = `${chat.id} ${index}`;
        if (!asked.has(key)) {
            asked.add(key);
            queueMicrotask(() => {
                asked.delete(key);
                if (heldOf(chat.id, index, entry) === null) startRead(chat.id, index, entry, read);
            });
        }
    }
    return { held, stub: how === 'stub' };
}

/** A card block of a chat read where it stands, as the thread reads it. */
const readingOf = (chat: Chat, placed: PlacedCard) =>
    readCard(placed, { place: placeOf(chat), campaignId: chat.campaignId, ended: !placed.lastTurn || !chat.running });

/** The index of the chat's last card that reads, among the cards its turns drew. */
export function lastReadingCard(chat: Chat, cards: ReadonlyMap<number, PlacedCard>): number | null {
    return lastReading(
        [...cards].map(([index, placed]) => {
            const reading = readingOf(chat, placed);
            return { index, kind: reading.state === 'card' ? reading.card.kind : null };
        }),
    );
}

/** The card of a kind a chat's entry holds now, read as the thread reads it, for a control on the card; null for none. */
export function cardAt<K extends CardKind>(
    chatId: unknown,
    index: unknown,
    kind: K,
): { chat: Chat; entry: object; card: Extract<Card, { kind: K }> } | null {
    if (typeof chatId !== 'string' || typeof index !== 'number') return null;
    const chat = chats.value[chatId];
    const placed = chat === undefined ? undefined : cardLayout(chat.entries).cards.get(index);
    if (chat === undefined || placed === undefined) return null;
    const reading = readingOf(chat, placed);
    return reading.state === 'card' && reading.card.kind === kind
        ? { chat, entry: chat.entries[index]!, card: reading.card as Extract<Card, { kind: K }> }
        : null;
}

/** The campaign a data card is about: the one it names, else in an agent's chat that chat's own; none for everyone. */
export const campaignOf = (card: { campaign?: string }, chat: Pick<Chat, 'main' | 'campaignId'>): string | null =>
    card.campaign ?? (placeOf(chat) === 'agent' ? chat.campaignId : null);

/** What a row says beside its name, the parts the runtime gave, in one quiet line; nothing when it gave none. */
export function metaLine(parts: readonly (string | null)[]): Html | null {
    const given = parts.filter((part): part is string => part !== null && part !== '');
    return given.length === 0 ? null : html`<span class="meta card-meta">${given.join(' · ')}</span>`;
}

/** A refusal of the runtime's, as the waiting cards say one: its code, then its words. */
export const saidView = (said: Said): Html =>
    html`<div class="note am card-said" role="alert">
        ${said.code === null ? null : html`<code>${said.code}</code> `}${said.message}
    </div>`;

/**
 * A data card: its header in plain weight (a frame's caption is never bold), "As of" and Refresh in the first row; then what stands outside the facts (`before`: the AI's
 * words, labelled as the AI's); then "On record now" over the runtime's facts, or "Reading..." while a read is under
 * way. A stub is its header and "Show now". `action` is the card's own read, named with the chat and the entry.
 */
export function frame<T>(view: {
    chatId: string;
    index: number;
    header: string;
    action: string;
    held: Held<T> | null;
    stub: boolean;
    before?: Html | null;
    /** The facts read, drawn; `at` is the moment the read was answered. */
    facts: (facts: T, at: number) => Html;
}): Html {
    const { chatId, index, header, held } = view;
    const control = act(view.action, chatId, index);
    if (view.stub && held === null)
        return html`<div class="ask chat-card chat-data stub" role="group" aria-label="${header}">
            <div class="ask-h card-top">
                <span class="card-head">${header}</span>
                <button class="btn sm card-read" type="button" ${control}>Show now</button>
            </div>
        </div>`;
    const read = held?.state === 'read' ? held : null;
    return html`<div class="ask chat-card chat-data" role="group" aria-label="${header}">
        <div class="ask-h card-top">
            <span class="card-head">${header}</span>
            ${read === null ? null : html`<span class="meta card-asof">${asOf(read.at, zone)}</span>`}
            <button class="btn sm ghost card-read" type="button" ${read === null ? html`disabled` : control}>
                Refresh
            </button>
        </div>
        ${view.before ?? null}
        <div class="card-facts">
            <div class="card-rec">On record now</div>
            ${read === null ? html`<div class="card-reading" role="status">Reading...</div>` : view.facts(read.facts, read.at)}
        </div>
    </div>`;
}

/** A control of a data card that opens one of Jason's pages, by its address: navigation only. */
define('card.page', ([hash]) => {
    if (typeof hash === 'string' && hash.startsWith('#/')) here().dock.page(parseRoute(hash));
});

/**
 * "Show what waits" and "Show them": in the agent's own chat, its waiting cards below the conversation, scrolled to;
 * anywhere else, Needs you. It decides nothing.
 */
define('card.waits', ([chatId, campaignId], element) => {
    const chat = typeof chatId === 'string' ? chats.value[chatId] : undefined;
    if (chat !== undefined && placeOf(chat) === 'agent' && chat.campaignId === campaignId) {
        element.closest('.chat-panel')?.querySelector('.chat-waiting-for')?.scrollIntoView({ block: 'start' });
        return;
    }
    here().dock.show('needs');
});
