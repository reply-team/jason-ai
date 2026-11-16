import type { Plugin, Routes } from '../api/types';
import type { DesktopReply, DesktopReplyAccount, DesktopReplyChannel, DesktopSetup } from '../platform/desktop';
import { can } from '../platform/places';
import { act, define } from './actions';
import { cardAt, cardHead, drawn, frame, type Said, saidOf, saidView, startRead } from './card-frame';
import type { Chat } from './chat-model';
import { html, type Html } from './html';
import { here, inTab } from './state';

// The accounts card the chat's AI asks for. It holds no member, so nothing on it is the AI's: what it shows is read by
// the shared frame's rule (when it is the conversation's last card that reads, and again on its Refresh), from two
// readers, each in its own part. Reply's part is the desktop's read of the provider now (`setup.status()`, the read the
// Connect card paints), with each account's state in the word Reply wrote; in a browser tab, which reads no provider
// here, the part is the Accounts page's sentence about where connecting is done. The runtime's part, under "On record
// now", is the plugin the global routes name for messages and its state as `plugin.list` answers it. The card follows
// neither the journal nor the desktop's changes, and acts on nothing: its buttons are Refresh and Open Accounts.

/** What one reader answered: its value, or what it said when it did not. */
type Part<T> = { ok: true; value: T } | { ok: false; said: Said };

/** A plugin as the card names it: its id, the name its manifest gives (null when none), and its state. */
type Listed = Plugin & { name?: string | null };

/** The card's read: Reply as the desktop read it (null where no desktop's read is made here), and the runtime's two. */
interface AccountsRead {
    reply: Part<DesktopReply> | null;
    routes: Part<Routes>;
    plugins: Part<Listed[]>;
}

const answered = <T>(value: T): Part<T> => ({ ok: true, value });
const failed = (error: unknown): Part<never> => ({ ok: false, said: saidOf(error) });

/** `plugin.list` answers its plugins under `plugins`; only the list is read. */
function listed(answer: unknown): Listed[] {
    const list = (answer as { plugins?: unknown }).plugins;
    return Array.isArray(list) ? (list as Listed[]) : [];
}

/** Whether this place reads the provider through the desktop: Jason Desktop's own window, not a tab. */
const readsProvider = (): boolean => !inTab() && can('setup.status');

/** The card's three reads, together; a refusal is said in its part and nothing throws. */
async function readAccounts(): Promise<AccountsRead> {
    const { api, desktop } = here();
    // A desktop that throws rather than refuses is a read that failed, like one that refuses.
    const reply: Promise<Part<DesktopReply> | null> = readsProvider()
        ? new Promise<DesktopSetup>((resolve) => resolve(desktop.setup.status())).then(
              (status) => answered(status.reply),
              failed,
          )
        : Promise.resolve(null);
    const [replyPart, routes, plugins] = await Promise.all([
        reply,
        api.call<Routes>('route.list').then(answered, failed),
        api.call<unknown>('plugin.list').then((answer) => answered(listed(answer)), failed),
    ]);
    return { reply: replyPart, routes, plugins };
}

define('card.accounts', ([chatId, index]) => {
    const at = cardAt(chatId, index, 'accounts');
    if (at !== null) startRead(at.chat.id, index as number, at.now, readAccounts);
});

define('card.accounts.open', () => {
    here().dock.page({ screen: 'accounts' });
});

const channelWords: Readonly<Record<DesktopReplyChannel, string>> = { email: 'Email', linkedin: 'LinkedIn' };
const channelOf = (channel: string): string =>
    Object.hasOwn(channelWords, channel) ? channelWords[channel as DesktopReplyChannel] : channel;

const accountsWord = (n: number): string => (n === 0 ? 'No accounts' : n === 1 ? '1 account' : `${n} accounts`);

function accountRow(account: DesktopReplyAccount): Html {
    return html`<tr>
        <td>${account.address}</td>
        <td>${account.name}</td>
        <td>${channelOf(account.channel)}</td>
        <td class="mono">${account.status === '' ? 'none given' : account.status}</td>
    </tr>`;
}

/** Reply's sign-in, how many accounts it listed, each one, and the channels it did not list, and why. */
function replyFacts(part: Part<DesktopReply>): Html {
    if (!part.ok) return html`<div class="ask-w">Reply could not be read: ${part.said.message}</div>`;
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
                >${
                    reply.wired
                        ? 'The runtime’s Reply plugin uses this sign-in'
                        : 'The runtime’s Reply plugin does not use a Reply sign-in yet'
                }</span
            >
            <span class="meta"
                >${
                    unread.length === 0
                        ? accountsWord(accounts.length)
                        : `${accountsWord(accounts.length)} listed; ${unread.map(channelOf).join(' and ')} not read`
                }</span
            >
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

/**
 * Reply's part, under its own label above the runtime's facts; in a place that reads no provider, the Accounts page's
 * sentence about where connecting is done.
 */
function outside(read: AccountsRead): Html {
    if (read.reply === null)
        return html`<p class="ask-w card-provider">
            Connecting, reconnecting, pausing and removing a provider’s accounts are done in Jason Desktop; a browser
            cannot do them.
        </p>`;
    return html`<div class="card-facts card-provider">
        <div class="card-rec">Read from Reply now</div>
        ${replyFacts(read.reply)}
    </div>`;
}

/**
 * The plugin a route for messages names, by its manifest's name and its id, and its state as the runtime lists it. It
 * is said to send only where the list names `message.send` among its operations.
 */
function routed(where: string, pluginId: string, plugins: readonly Listed[]): Html {
    const plugin = plugins.find((each) => each.id === pluginId);
    const name = typeof plugin?.name === 'string' && plugin.name !== '' ? `${plugin.name} (${pluginId})` : pluginId;
    const sends = Array.isArray(plugin?.operations) && plugin.operations.includes('message.send');
    const label = sends ? `Plugin that sends messages ${where}` : `Plugin the route for messages names ${where}`;
    return html`<div class="card-fact">
        <span class="meta">${label}</span>
        <b>${name}</b>
        <span class="meta">
            ${
                plugin === undefined
                    ? 'The runtime does not list this plugin.'
                    : sends
                      ? `Its state: ${plugin.status}`
                      : `Its state: ${plugin.status}. Its operations do not include message.send.`
            }
        </span>
    </div>`;
}

/**
 * The runtime's facts: the plugin the global routes name for messages, an operation's own route over the default, and
 * any route of a channel beside it; each read that failed is said.
 */
function runtimeFacts(read: AccountsRead): Html {
    return html`${routeFacts(read)}
        <div class="ask-a">
            <button class="btn sm" type="button" ${act('card.accounts.open')}>Open Accounts</button>
        </div>`;
}

function routeFacts(read: AccountsRead): Html {
    const { routes, plugins } = read;
    if (!routes.ok || !plugins.ok)
        return html`${
            routes.ok
                ? null
                : html`<div class="ask-w">The runtime’s routes could not be read:</div>
                      ${saidView(routes.said)}`
        }${
            plugins.ok
                ? null
                : html`<div class="ask-w">The runtime’s plugins could not be read:</div>
                      ${saidView(plugins.said)}`
        }`;
    const global = routes.value.global;
    const sends = global.operations['message.send'] ?? global.default;
    const byChannel = Object.entries(global.channels['message.send'] ?? {});
    return html`${
        sends === null
            ? html`<div class="ask-w">No route sends messages: the runtime routes no plugin to send them.</div>`
            : routed('by default', sends.plugin_id, plugins.value)
    }
    ${byChannel.map(([channel, route]) => routed(`on ${channelOf(channel)}`, route.plugin_id, plugins.value))}`;
}

/** The accounts card, read when it is drawn as the conversation's last card that reads, and on its Refresh. */
export function accountsCard(chat: Chat, index: number, last: number | null, header: string): Html {
    const { held, stub } = drawn(chat, index, last, readAccounts);
    return frame<AccountsRead>({
        chatId: chat.id,
        index,
        head: cardHead(header),
        action: 'card.accounts',
        held,
        stub,
        outside,
        facts: runtimeFacts,
    });
}
