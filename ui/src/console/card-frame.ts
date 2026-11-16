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
// conversation. What it read is held here, by chat and by the index of the text entry that holds the card, matched by
// that entry's words and the chat's scope, until the conversation is opened afresh (`forgetCards`).

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

/**
 * What a card's read was made for: the words of the entry that holds the card, and the chat's place and campaign then.
 * The page builds an entry afresh whenever a turn ends or the chat's mode changes, so a read is matched by the words,
 * never by the object; and a chat given a campaign since reads under its new scope, never shows the old read.
 */
export interface ReadFor {
    text: string;
    scope: string;
}

/** What a card in a chat's entry is read for now. */
export function readFor(chat: Chat, index: number): ReadFor {
    const entry = chat.entries[index];
    return { text: entry?.kind === 'text' ? entry.text : '', scope: `${placeOf(chat)} ${chat.campaignId ?? ''}` };
}

/** One card's read: what it was made for, and where it stands; `at` is the moment it was answered. */
export type Held<T> = ReadFor & { serial: number } & ({ state: 'reading' } | { state: 'read'; at: number; facts: T });

/** By chat id, then by the index of the text entry that holds the card: what each data card drawn has read. */
export const cardReads = signal<Readonly<Record<string, Readonly<Record<number, Held<unknown>>>>>>({});

let serials = 0;

function put(chatId: string, index: number, held: Held<unknown>): void {
    cardReads.value = { ...cardReads.value, [chatId]: { ...cardReads.value[chatId], [index]: held } };
}

/**
 * Forgets what a chat's cards read: a conversation opened afresh reads its last card again and draws the earlier ones
 * as stubs. The chat calls it when it holds a conversation from the desktop's record.
 */
export function forgetCards(chatId: string): void {
    if (cardReads.value[chatId] === undefined) return;
    cardReads.value = Object.fromEntries(Object.entries(cardReads.value).filter(([id]) => id !== chatId));
}

/** What a card has read for what it is now; null for a card not read since the conversation was opened. */
export function heldOf<T>(chatId: string, index: number, now: ReadFor): Held<T> | null {
    const held = cardReads.value[chatId]?.[index];
    return held !== undefined && held.text === now.text && held.scope === now.scope ? (held as Held<T>) : null;
}

/**
 * Reads a card's facts now: "Reading..." until `read` answers, then what it answered and when. `read` says a refusal in
 * its facts and never throws. A read started again (Refresh, while one is under way) fences the earlier one: only the
 * last read started lands.
 */
export function startRead<T>(chatId: string, index: number, now: ReadFor, read: () => Promise<T>): void {
    const serial = ++serials;
    put(chatId, index, { ...now, serial, state: 'reading' });
    void read().then((facts) => {
        if (cardReads.value[chatId]?.[index]?.serial === serial)
            put(chatId, index, { ...now, serial, state: 'read', at: Date.now(), facts });
    });
}

/** The cards asked for while the thread was being drawn, read once the drawing is done: a drawing changes nothing. */
const asked = new Set<string>();

/**
 * How a card is drawn now, and its read asked for when it is the conversation's last card that reads and has not been
 * read for what it is now: a thread is drawn inside a computed value, so the read starts once the drawing is done.
 */
export function drawn<T>(
    chat: Chat,
    index: number,
    last: number | null,
    read: () => Promise<T>,
): { held: Held<T> | null; stub: boolean } {
    const now = readFor(chat, index);
    const held = heldOf<T>(chat.id, index, now);
    const how = drawsAs(index, last, held !== null);
    if (how === 'read') {
        const key = `${chat.id} ${index}`;
        if (!asked.has(key)) {
            asked.add(key);
            queueMicrotask(() => {
                asked.delete(key);
                if (heldOf(chat.id, index, now) === null) startRead(chat.id, index, now, read);
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
): { chat: Chat; now: ReadFor; card: Extract<Card, { kind: K }> } | null {
    if (typeof chatId !== 'string' || typeof index !== 'number') return null;
    const chat = chats.value[chatId];
    const placed = chat === undefined ? undefined : cardLayout(chat.entries).cards.get(index);
    if (chat === undefined || placed === undefined) return null;
    const reading = readingOf(chat, placed);
    return reading.state === 'card' && reading.card.kind === kind
        ? { chat, now: readFor(chat, index), card: reading.card as Extract<Card, { kind: K }> }
        : null;
}

/** The campaign a data card is about: the one it names, else in an agent's chat that chat's own; none for everyone. */
export const campaignOf = (card: { campaign?: string }, chat: Pick<Chat, 'main' | 'campaignId'>): string | null =>
    card.campaign ?? (placeOf(chat) === 'agent' ? chat.campaignId : null);

/**
 * The caption that names the AI over words of its own on any card it asked for ("Jason asks", "Jason's proposal"): the
 * first row of the AI's frame, in plain weight, never in the look of a fact.
 */
export const aiCaption = (words: string): Html => html`<div class="card-who">${words}</div>`;

/**
 * A card's header in the desktop's words, in plain weight: the first thing in its top row, and its name for a screen
 * reader. It is made only here, and it is what a frame takes: a card of facts names itself by its header, so it can
 * never stand under the AI's caption alone.
 */
export interface CardHead {
    readonly words: string;
    readonly view: Html;
}
export const cardHead = (words: string): CardHead =>
    Object.freeze({ words, view: html`<span class="card-head">${words}</span>` });

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
    head: CardHead;
    action: string;
    held: Held<T> | null;
    stub: boolean;
    before?: Html | null;
    /**
     * Facts read from outside the runtime (a provider, as the desktop reads it), drawn once read above "On record now"
     * under their own label, so nothing the runtime did not say stands under it.
     */
    outside?: (facts: T, at: number) => Html | null;
    /** The facts read, drawn; `at` is the moment the read was answered. */
    facts: (facts: T, at: number) => Html;
}): Html {
    const { chatId, index, head, held } = view;
    const control = act(view.action, chatId, index);
    if (view.stub && held === null)
        return html`<div class="ask chat-card chat-data stub" role="group" aria-label="${head.words}">
            <div class="ask-h card-top">
                ${head.view}
                <button class="btn sm card-read" type="button" ${control}>Show now</button>
            </div>
        </div>`;
    const read = held?.state === 'read' ? held : null;
    return html`<div class="ask chat-card chat-data" role="group" aria-label="${head.words}">
        <div class="ask-h card-top">
            ${head.view} ${read === null ? null : html`<span class="meta card-asof">${asOf(read.at, zone)}</span>`}
            <button class="btn sm ghost card-read" type="button" ${control}>Refresh</button>
        </div>
        ${view.before ?? null} ${read === null || view.outside === undefined ? null : view.outside(read.facts, read.at)}
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
    // The waiting cards are drawn only while something waits: with none below, Needs you is where to look.
    const below =
        chat !== undefined && placeOf(chat) === 'agent' && chat.campaignId === campaignId
            ? element.closest('.chat-panel')?.querySelector('.chat-waiting-for')
            : null;
    if (below) below.scrollIntoView({ block: 'start' });
    else here().dock.show('needs');
});
