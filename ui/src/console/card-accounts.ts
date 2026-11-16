import { signal } from '@preact/signals-react';
import { ApiError } from '../api/client';
import type { Plugin, Routes } from '../api/types';
import type { DesktopReply, DesktopReplyAccount, DesktopReplyChannel } from '../platform/desktop';
import { can } from '../platform/places';
import { clock } from '../time/format';
import { act, define } from './actions';
import { html, type Html } from './html';
import { chats, here, inTab } from './state';

// The accounts card the chat's AI asks for. It holds no member, so nothing on it is the AI's: what it shows is read when
// it is first drawn, and again only on its own Refresh, from two readers, each in its own part. Reply's part is the
// desktop's read of the provider now (`setup.status()`, the read the Connect card paints), with each account's state in
// the word Reply wrote; in a browser tab, which reads no provider here, the part is the Accounts page's sentence about
// where connecting is done. The runtime's part, under "On record now", is the plugin the global routes send messages
// through and its state as `plugin.list` answers it. The card follows neither the journal nor the desktop's changes,
// says when it read, and acts on nothing: its buttons are Refresh and Open Accounts.

/** What one reader answered: its value, or why it did not answer, in its own words. */
type Part<T> = { ok: true; value: T } | { ok: false; code: string | null; message: string };

/** A plugin as the card names it: its id, the name its manifest gives (null when none), and its state. */
type Listed = Plugin & { name?: string | null };

/** One drawn card's reads. */
export interface AccountsHeld {
    /** The kept entry the card stands after: a conversation opened again holds new entries, and its card reads afresh. */
    entry: object;
    reading: boolean;
    /** When the last read was answered, by the person's clock; null until one was. */
    at: number | null;
    /** Reply as the desktop read it; null where no desktop's read is made here. */
    reply: Part<DesktopReply> | null;
    routes: Part<Routes> | null;
    plugins: Part<Listed[]> | null;
}

/**
 * By chat id, then by the index of the text entry each card stands after, each drawn accounts card's reads. A chat's
 * own record changes only when one of its cards does, so a read in one chat draws no other chat's thread again.
 */
export const accountsHeld = signal<Readonly<Record<string, Readonly<Record<number, AccountsHeld>>>>>({});

const heldAt = (chatId: string, index: number): AccountsHeld | undefined => accountsHeld.value[chatId]?.[index];
const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

const put = (chatId: string, index: number, held: AccountsHeld): void => {
    accountsHeld.value = { ...accountsHeld.value, [chatId]: { ...accountsHeld.value[chatId], [index]: held } };
};

const failed = (error: unknown): Part<never> =>
    error instanceof ApiError
        ? { ok: false, code: error.code, message: error.message }
        : { ok: false, code: null, message: error instanceof Error ? error.message : String(error) };

const answered = <T>(value: T): Part<T> => ({ ok: true, value });

/** `plugin.list` answers its plugins under `plugins`; only the list is read. */
function listed(answer: unknown): Listed[] {
    const list = (answer as { plugins?: unknown }).plugins;
    return Array.isArray(list) ? (list as Listed[]) : [];
}

/** Whether this place reads the provider through the desktop: Jason Desktop's own window, not a tab. */
const readsProvider = (): boolean => !inTab() && can('setup.status');

/** Reads the card's three answers, together, and keeps them under its key unless another read took its place. */
function readFor(chatId: string, index: number, entry: object): void {
    const before = heldAt(chatId, index);
    const kept = before?.entry === entry ? before : null;
    if (kept?.reading === true) return;
    const reading: AccountsHeld = kept
        ? { ...kept, reading: true }
        : { entry, reading: true, at: null, reply: null, routes: null, plugins: null };
    put(chatId, index, reading);
    const { api, desktop } = here();
    const reply: Promise<Part<DesktopReply> | null> = readsProvider()
        ? desktop.setup.status().then((status) => answered(status.reply), failed)
        : Promise.resolve(null);
    void Promise.all([
        reply,
        api.call<Routes>('route.list').then(answered, failed),
        api.call<unknown>('plugin.list').then((answer) => answered(listed(answer)), failed),
    ]).then(([replyPart, routes, plugins]) => {
        if (heldAt(chatId, index) !== reading) return;
        put(chatId, index, { entry, reading: false, at: Date.now(), reply: replyPart, routes, plugins });
    });
}

/** The cards asked to read while a thread was being drawn, read once the drawing is done: a drawing writes nothing. */
const asked = new Set<string>();
function readSoon(chatId: string, index: number, entry: object): void {
    const key = `${chatId} ${index}`;
    if (asked.has(key)) return;
    asked.add(key);
    queueMicrotask(() => {
        asked.delete(key);
        if (heldAt(chatId, index)?.entry !== entry) readFor(chatId, index, entry);
    });
}

define('card.accounts.refresh', ([chatId, index]) => {
    if (typeof chatId !== 'string' || typeof index !== 'number') return;
    const entry = chats.value[chatId]?.entries[index];
    if (entry !== undefined) readFor(chatId, index, entry);
});

define('card.accounts.open', () => {
    here().dock.page({ screen: 'accounts' });
});

const channelWords: Readonly<Record<DesktopReplyChannel, string>> = { email: 'Email', linkedin: 'LinkedIn' };
const channelOf = (channel: string): string =>
    Object.hasOwn(channelWords, channel) ? channelWords[channel as DesktopReplyChannel] : channel;

const accountsWord = (n: number): string => (n === 0 ? 'No accounts' : n === 1 ? '1 account' : `${n} accounts`);

/** A reader's refusal, in its words: the runtime's code before its message, as the waiting cards say one. */
const refusal = (part: { code: string | null; message: string }): Html =>
    html`${part.code === null ? null : html`<code>${part.code}</code> `}${part.message}`;

function accountRow(account: DesktopReplyAccount): Html {
    return html`<tr>
        <td>${account.address}</td>
        <td>${account.name}</td>
        <td>${channelOf(account.channel)}</td>
        <td class="mono">${account.status === '' ? 'none given' : account.status}</td>
    </tr>`;
}

/** Reply's part: its sign-in, how many accounts it holds, each one, and the channels it did not list, and why. */
function replyPart(part: Part<DesktopReply>): Html {
    if (!part.ok) return html`<div class="ask-w">Reply could not be read: ${part.message}</div>`;
    const reply = part.value;
    const accounts = Array.isArray(reply.accounts) ? reply.accounts : [];
    const absent = Array.isArray(reply.absent) ? reply.absent : [];
    const unread = Array.isArray(reply.unread) ? reply.unread : [];
    return html`<div class="card-line">
            ${reply.cli.found ? null : html`<span>The Reply CLI was not found.</span>`}
            <span
                >${reply.signedIn ? html`Signed in to Reply as <b>${reply.user ?? ''}</b>` : 'Not signed in to Reply'}</span
            >
            <span class="meta"
                >${reply.wired ? 'Jason uses this sign-in' : 'Jason does not use a Reply sign-in yet'}</span
            >
            <span class="meta">${accountsWord(accounts.length)}</span>
        </div>
        ${
            accounts.length === 0
                ? null
                : html`<table class="card-tbl">
                      <thead>
                          <tr>
                              <th>Address</th>
                              <th>Name at Reply</th>
                              <th>Channel</th>
                              <th>Status at Reply</th>
                          </tr>
                      </thead>
                      <tbody>
                          ${accounts.map(accountRow)}
                      </tbody>
                  </table>`
        }
        ${absent.map(
            (channel) =>
                html`<div class="ask-w">
                    ${channelOf(channel)} is not in this Reply account’s plan or role, so Reply lists none.
                </div>`,
        )}
        ${unread.map(
            (channel) => html`<div class="ask-w">${channelOf(channel)} accounts could not be read from Reply.</div>`,
        )}`;
}

/** The plugin a route names, by its manifest's name and its id, and its state as the runtime lists it. */
function routed(label: string, pluginId: string, plugins: readonly Listed[]): Html {
    const plugin = plugins.find((each) => each.id === pluginId);
    const name = typeof plugin?.name === 'string' && plugin.name !== '' ? `${plugin.name} (${pluginId})` : pluginId;
    return html`<div class="card-fact">
        <span class="meta">${label}</span>
        <b>${name}</b>
        <span class="meta">
            ${plugin === undefined ? 'The runtime does not list this plugin.' : `Its state: ${plugin.status}`}
        </span>
    </div>`;
}

/**
 * The runtime's part: the plugin the global routes send messages through, an operation's own route over the default,
 * and any route of a channel beside it.
 */
function runtimePart(routes: Part<Routes>, plugins: Part<Listed[]>): Html {
    const failure = !routes.ok ? routes : !plugins.ok ? plugins : null;
    if (failure !== null) return html`<div class="ask-w">The runtime could not be read: ${refusal(failure)}</div>`;
    if (!routes.ok || !plugins.ok) return html``;
    const global = routes.value.global;
    const sends = global.operations['message.send'] ?? global.default;
    const byChannel = Object.entries(global.channels['message.send'] ?? {});
    return html`${
        sends === null
            ? html`<div class="ask-w">No route sends messages: the runtime routes no plugin to send them.</div>`
            : routed('Plugin that sends messages by default', sends.plugin_id, plugins.value)
    }
    ${byChannel.map(([channel, route]) =>
        routed(`Plugin that sends on ${channelOf(channel)}`, route.plugin_id, plugins.value),
    )}`;
}

/** The card's header, when it read, and its Refresh, which is off while a read is under way. */
function frame(chatId: string, index: number, held: AccountsHeld | undefined, body: Html): Html {
    const reading = held === undefined || held.reading;
    return html`<div class="ask chat-card card-accounts" role="group" aria-label="Accounts">
        <div class="ask-h">
            <span class="card-cap">Accounts</span>
            <button
                class="btn sm card-refresh"
                type="button"
                ${reading ? html`disabled` : act('card.accounts.refresh', chatId, index)}
            >
                Refresh
            </button>
        </div>
        ${held?.at == null || held.reading ? null : html`<div class="meta card-asof">As of ${clock(new Date(held.at), zone)}</div>`}
        ${body}
        <div class="ask-a">
            <button class="btn sm" type="button" ${act('card.accounts.open')}>Open Accounts</button>
        </div>
    </div>`;
}

/** The accounts card after the text entry at `index` of the chat: read once when first drawn, then on Refresh. */
export function accountsView(chatId: string, index: number, entry: object): Html {
    const found = heldAt(chatId, index);
    const held = found?.entry === entry ? found : undefined;
    if (held === undefined) readSoon(chatId, index, entry);
    if (held === undefined || held.reading)
        return frame(chatId, index, held, html`<div class="ask-w card-reading">Reading…</div>`);
    const provider =
        held.reply === null
            ? html`<p class="ask-w">
                  Connecting, reconnecting, pausing and removing a provider’s accounts are done in Jason Desktop; a
                  browser cannot do them.
              </p>`
            : html`<section class="card-part" role="region" aria-label="Read from Reply now">
                  <div class="card-cap">Read from Reply now</div>
                  ${replyPart(held.reply)}
              </section>`;
    const record =
        held.routes === null || held.plugins === null
            ? null
            : html`<section class="card-part" role="region" aria-label="On record now">
                  <div class="card-cap">On record now</div>
                  ${runtimePart(held.routes, held.plugins)}
              </section>`;
    return frame(chatId, index, held, html`${provider}${record}`);
}
