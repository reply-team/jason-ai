import { effect, signal } from '@preact/signals-react';
import type {
    DesktopHost,
    DesktopMailboxPage,
    DesktopPlugin,
    DesktopRefusal,
    DesktopReplyAccount,
    DesktopReplyLink,
    DesktopReplyPending,
    DesktopSetup,
} from '../platform/desktop';
import { act, argsOf, define } from './actions';
import { withModels } from './chat-model';
import { html, type Html } from './html';
import { signInSentence } from './live';
import { here, messageOf, setup, setupBusy, setupError } from './state';

// Connect: what the desktop found and set up, and the acts that set the rest up. The desktop does each act; the card
// says where things stand afterwards, from what `setup.status()` answers.

/** Keys and tokens being typed, held only until they are handed to the desktop. */
const typed = new Map<string, string>();

/** The plugin whose Disconnect waits on the person's confirmation; null when none does. */
const confirming = signal<string | null>(null);

// A confirmation is forgotten once the plugin it names is no longer configured, so configuring it again asks afresh.
effect(() => {
    const id = confirming.value;
    if (id !== null && !setup.value?.plugins.some((each) => each.id === id && each.configured)) confirming.value = null;
});

/**
 * Reply's web app, where an account's limits, signature and pauses are managed. The desktop manages none of them; the
 * card links there instead.
 */
export const replyAccountsAddress = 'https://run.reply.io/';

/** The channels the Reply CLI lists accounts for, in the order the card shows them, with their headings. */
const channels: readonly (readonly [string, string])[] = [
    ['email', 'Email'],
    ['linkedin', 'LinkedIn'],
];

type Tone = 'green' | 'amber' | 'red' | 'gray';

/**
 * The state Reply reports for an account (`reply_status` in `reply account list --json`, see the Reply CLI's
 * `src/operations/accounts.ts`), in words, by channel. A word not here is shown as Reply wrote it, in a neutral pill.
 */
const states: Record<string, Record<string, readonly [string, Tone]>> = {
    email: {
        connected: ['Connected', 'green'],
        disconnected: ['Disconnected', 'red'],
        unknown: ['Unknown', 'gray'],
    },
    linkedin: {
        enabled: ['Enabled', 'green'],
        disabled: ['Disabled', 'gray'],
        cookieInvalid: ['Signed out', 'red'],
        dailyLimitReached: ['Daily limit reached', 'amber'],
    },
};

/** Text the card did not write, cut to forty characters so a long one cannot crowd the row; never inside one. */
const cap = (text: string): string => {
    const characters = [...text];
    return characters.length > 40 ? `${characters.slice(0, 40).join('')}…` : text;
};

/**
 * The one line for each channel the desktop did not list, from its two lists of closed words: the channels the CLI says
 * are not in the plan or role, then the channels that could not be read. Only the card's own sentences, never the CLI's.
 */
const unlisted = (state: DesktopSetup['reply']): string[] => [
    ...(Array.isArray(state.absent) ? state.absent : []).map(
        (channel) => `This Reply account has no ${channel === 'linkedin' ? 'LinkedIn' : 'email'}.`,
    ),
    ...(Array.isArray(state.unread) ? state.unread : []).map(
        (channel) => `${channel === 'linkedin' ? 'LinkedIn' : 'Email'} accounts could not be read. Check again.`,
    ),
];

function stateOf(account: DesktopReplyAccount): readonly [string, Tone] {
    if (!account.status) return ['State unknown', 'gray'];
    const known = Object.hasOwn(states, account.channel) ? states[account.channel]! : {};
    return Object.hasOwn(known, account.status) ? known[account.status]! : [cap(account.status), 'gray'];
}

/**
 * The accounts by channel: the known channels in their order, then any other under its own name, as it first comes;
 * LinkedIn with the connections that wait, which make its heading even with no account.
 */
function grouped(
    accounts: readonly DesktopReplyAccount[],
    pending: readonly DesktopReplyPending[],
): [string, string, DesktopReplyAccount[], readonly DesktopReplyPending[]][] {
    const others = [...new Set(accounts.map((each) => each.channel))].filter(
        (channel) => !channels.some(([known]) => known === channel),
    );
    return [...channels, ...others.map((channel) => [channel, cap(channel)] as const)]
        .map(([channel, heading]): [string, string, DesktopReplyAccount[], readonly DesktopReplyPending[]] => [
            channel,
            heading,
            accounts.filter((each) => each.channel === channel),
            channel === 'linkedin' ? pending : [],
        ])
        .filter(([, , members, waiting]) => members.length > 0 || waiting.length > 0);
}

/**
 * The states a reconnect does not change, by channel: a mailbox connected, a LinkedIn account enabled, disabled by its
 * owner or at its daily limit. Every other state of these two channels, an unknown one included, offers Reconnect.
 */
const good: Record<string, readonly string[]> = {
    email: ['connected'],
    linkedin: ['enabled', 'disabled', 'dailyLimitReached'],
};

const needsReconnect = (account: DesktopReplyAccount): boolean =>
    Object.hasOwn(good, account.channel) && !good[account.channel]!.includes(account.status);

// ---- Links: connecting and reconnecting an account by Reply's external link ------------------------------------------
//
// Reply answers the address of a page where an account is connected with no sign-in to Reply, so whoever owns the
// mailbox or the LinkedIn account can open it in any browser. Whoever holds the address can connect an account to this
// Reply account: it is asked of the desktop on the person's click, shown in this dialog alone, and dropped when the
// dialog closes. It is never kept anywhere else.

/** Which of the card's places a dialog or a choice belongs to: the Connect panel's card, or the chat's. */
type Place = string;

interface LinkDialog {
    place: Place;
    title: string;
    kind: 'linkedin' | 'mailbox';
    /** A mailbox's reconnect, whose provider the person picks before anything is asked. */
    choosing: boolean;
    asking: boolean;
    link: DesktopReplyLink | null;
    refused: string | null;
    copied: 'copied' | 'failed' | null;
}

const linkDialog = signal<LinkDialog | null>(null);
/** The place whose Add account choices are open; null when none is. */
const adding = signal<Place | null>(null);
/** The place whose LinkedIn name field is open; null when none is. */
const naming = signal<Place | null>(null);
/** Moves on at each ask and each close, so an answer that comes after its dialog was closed or replaced is dropped. */
let asked = 0;

const mailboxTitles: Record<DesktopMailboxPage, string> = {
    gmail: 'Connect a Gmail mailbox',
    outlook: 'Connect an Outlook mailbox',
    hosted: 'Add another mailbox on Reply’s page',
};

const pageNames: readonly (readonly [DesktopMailboxPage, string])[] = [
    ['gmail', 'Gmail'],
    ['outlook', 'Outlook'],
    ['hosted', 'Another mailbox'],
];

const isPage = (value: unknown): value is DesktopMailboxPage => pageNames.some(([page]) => page === value);

function closeDialog(): void {
    asked += 1;
    linkDialog.value = null;
}

/**
 * Drops the link, the choices and the name field a card at `place` has open: called when that card is no longer drawn,
 * so a link the person stopped seeing is not held.
 */
export function dropLinks(place: Place): void {
    if (linkDialog.peek()?.place === place) closeDialog();
    if (adding.peek() === place) adding.value = null;
    if (naming.peek() === place) naming.value = null;
}

/** The link the card holds, in its open dialog; null once the dialog is closed. */
export const heldLink = (): string | null => linkDialog.peek()?.link?.link ?? null;

/** Drops every link, choice and name field the card has open: what a console that goes away leaves behind. */
export function resetLinks(): void {
    closeDialog();
    adding.value = null;
    naming.value = null;
}

async function copyLink(): Promise<void> {
    const shown = linkDialog.value;
    if (shown?.link == null) return;
    let copied: 'copied' | 'failed' = 'copied';
    try {
        await navigator.clipboard.writeText(shown.link.link);
    } catch {
        copied = 'failed';
    }
    const now = linkDialog.value;
    if (now !== null && now.link === shown.link) linkDialog.value = { ...now, copied };
}

/**
 * Opens a dialog that asks the desktop for one link. `then` copies it or opens it once it comes, for a waiting
 * connection's row; `reread` reads the card again afterwards, for an ask that makes a waiting connection in Reply.
 */
async function askLink(
    place: Place,
    title: string,
    kind: 'linkedin' | 'mailbox',
    run: () => Promise<DesktopReplyLink | DesktopRefusal>,
    options: { then?: 'copy' | 'open'; reread?: boolean } = {},
): Promise<void> {
    asked += 1;
    const mine = asked;
    adding.value = null;
    naming.value = null;
    linkDialog.value = { place, title, kind, choosing: false, asking: true, link: null, refused: null, copied: null };
    let answer: DesktopReplyLink | DesktopRefusal;
    try {
        answer = await run();
    } catch (error) {
        answer = { refused: messageOf(error) };
    }
    if (mine !== asked || linkDialog.value === null) return;
    if ('refused' in answer) {
        linkDialog.value = { ...linkDialog.value, asking: false, refused: answer.refused };
    } else {
        linkDialog.value = { ...linkDialog.value, asking: false, link: answer };
        if (options.then === 'copy') await copyLink();
        // The desktop hands an https link from the window to the system browser, and refuses any other.
        if (options.then === 'open' && answer.link.startsWith('https://'))
            window.open(answer.link, '_blank', 'noopener,noreferrer');
    }
    if (options.reread === true) void reload();
}

const askMailbox = (place: Place, page: DesktopMailboxPage, title = mailboxTitles[page]): Promise<void> =>
    askLink(place, title, 'mailbox', () => here().desktop.setup.replyMailboxLink(page));

define('connect.addAccount', ([place]) => {
    if (typeof place !== 'string') return;
    adding.value = adding.value === place ? null : place;
    naming.value = null;
});

define('connect.addCancel', () => {
    adding.value = null;
    naming.value = null;
});

define('connect.addMailbox', ([place, page]) => {
    if (typeof place === 'string' && isPage(page)) void askMailbox(place, page);
});

define('connect.addLinkedIn', ([place]) => {
    if (typeof place === 'string') naming.value = place;
});

define('connect.makeLinkedIn', ([place]) => {
    const name = typedOf('linkedin-name');
    if (typeof place !== 'string' || !name) return;
    void askLink(
        place,
        'Connect a LinkedIn account',
        'linkedin',
        async () => {
            const answer = await here().desktop.setup.replyLinkedInConnect(name);
            if (!('refused' in answer)) forget('linkedin-name');
            return answer;
        },
        { reread: true },
    );
});

define('connect.reconnect', ([place, channel, id]) => {
    if (typeof place !== 'string' || typeof id !== 'string') return;
    const account = setup.value?.reply.accounts.find((each) => each.channel === channel && each.id === id);
    if (!account) return;
    const title = `Reconnect ${cap(account.name || account.address || 'the account')}`;
    if (channel === 'linkedin') {
        void askLink(place, title, 'linkedin', () => here().desktop.setup.replyLinkedInReconnect(id));
    } else if (channel === 'email') {
        asked += 1;
        adding.value = null;
        naming.value = null;
        linkDialog.value = {
            place,
            title,
            kind: 'mailbox',
            choosing: true,
            asking: false,
            link: null,
            refused: null,
            copied: null,
        };
    }
});

define('connect.reconnectMailbox', ([page]) => {
    const shown = linkDialog.value;
    if (shown?.choosing === true && isPage(page)) void askMailbox(shown.place, page, shown.title);
});

define('connect.pendingLink', ([place, id, then]) => {
    if (typeof place !== 'string' || typeof id !== 'string' || (then !== 'copy' && then !== 'open')) return;
    const row = setup.value?.reply.linkedinPending.find((each) => each.id === id);
    if (!row) return;
    void askLink(
        place,
        `Finish ${cap(row.name || 'the LinkedIn account')}`,
        'linkedin',
        () => here().desktop.setup.replyLinkedInPendingLink(id),
        { then },
    );
});

define('connect.copyLink', () => void copyLink());
define('connect.closeLink', () => closeDialog());
define('connect.checkNow', () => void reload());

// A dialog is dropped with the sign-in it was asked under.
effect(() => {
    if (setup.value !== null && !setup.value.reply.signedIn) {
        if (linkDialog.peek() !== null) closeDialog();
        adding.value = null;
        naming.value = null;
    }
});

/** A moment Reply wrote, as a person reads it: `2026-11-19 04:59 UTC`; null for one that is not a moment. */
function momentOf(text: string | null): string | null {
    if (text === null) return null;
    const at = new Date(/(?:Z|[+-]\d{2}:\d{2})$/.test(text) ? text : `${text}Z`);
    return Number.isNaN(at.getTime()) ? null : `${at.toISOString().slice(0, 16).replace('T', ' ')} UTC`;
}

const linkedinNote =
    'No Reply sign-in is needed, so you can send this link to whoever owns the account. Anyone who has it can connect an account to this Reply account: send it only to them.';
const mailboxNote =
    "No Reply sign-in is needed, so you can send this link to whoever owns the mailbox. Anyone who has it can add and manage this Reply account's mailboxes, and it does not expire: send it only to someone you trust.";

function dialogView(shown: LinkDialog, busy: Html | null): Html {
    const link = shown.link;
    const until = momentOf(link?.expiresAtUtc ?? null);
    return html`<div class="note link-dialog" role="dialog" aria-label="${shown.title}">
        <b>${shown.title}</b>
        ${
            shown.choosing
                ? html`<div class="meta">
                          Going through Gmail’s or Outlook’s page again with the same address reconnects that mailbox
                          and makes no second one; a mailbox by SMTP or IMAP is put right on Reply’s page.
                      </div>
                      <div class="setrow">
                          ${pageNames.map(
                              ([page, name]) =>
                                  html`<button
                                      class="btn sm"
                                      type="button"
                                      ${busy}
                                      ${act('connect.reconnectMailbox', page)}
                                  >
                                      ${name}
                                  </button>`,
                          )}
                      </div>`
                : null
        }
        ${shown.asking ? html`<div class="meta" role="status"><span class="spin"></span>Asking Reply for the link…</div>` : null}
        ${shown.refused !== null ? html`<div class="note am" role="alert">${shown.refused}</div>` : null}
        ${
            link !== null && link.link.startsWith('https://')
                ? html`<div class="meta">Opens on <b class="link-host">${link.host}</b></div>
                      <input class="inp link-text" type="text" readonly aria-label="Link" value="${link.link}" />
                      <div class="setrow">
                          <button class="btn sm" type="button" ${act('connect.copyLink')}>Copy link</button>
                          <a class="btn sm" href="${link.link}" target="_blank" rel="noreferrer">Open in the browser</a>
                          ${
                              shown.copied === 'copied'
                                  ? html`<span class="meta" role="status">Copied.</span>`
                                  : shown.copied === 'failed'
                                    ? html`<span class="meta" role="status"
                                          >The clipboard did not take it; select the link and copy it.</span
                                      >`
                                    : null
                          }
                      </div>
                      <p class="meta link-note">${link.kind === 'mailbox' ? mailboxNote : linkedinNote}</p>
                      ${
                          link.kind === 'linkedin'
                              ? html`<p class="meta">
                                    Valid for a limited time; if it stopped working, make a new
                                    one.${until ? ` Valid until ${until}.` : ''}
                                </p>`
                              : null
                      }`
                : null
        }
        <div class="setrow">
            <div class="grow meta">The account appears in the list when they finish.</div>
            <button class="btn sm" type="button" ${act('connect.checkNow')}>Check now</button>
            <button class="btn sm" type="button" ${act('connect.closeLink')}>Close</button>
        </div>
    </div>`;
}

/** Add account's choices: a mailbox's three pages where email is not absent, LinkedIn where it is not, and Cancel. */
function addView(state: DesktopSetup['reply'], place: Place, busy: Html | null, linkedinOff: Html | null): Html {
    const absent = Array.isArray(state.absent) ? state.absent : [];
    return html`<div class="setrow add-account">
            ${
                absent.includes('email')
                    ? null
                    : pageNames.map(
                          ([page, name]) =>
                              html`<button
                                  class="btn sm"
                                  type="button"
                                  ${busy}
                                  ${act('connect.addMailbox', place, page)}
                              >
                                  ${name}
                              </button>`,
                      )
            }
            ${
                absent.includes('linkedin')
                    ? null
                    : html`<button
                          class="btn sm"
                          type="button"
                          ${busy ?? linkedinOff}
                          ${act('connect.addLinkedIn', place)}
                      >
                          LinkedIn
                      </button>`
            }
            <button class="btn sm" type="button" ${act('connect.addCancel')}>Cancel</button>
        </div>
        ${
            naming.value === place
                ? html`<div class="setrow">
                      ${field('linkedin-name', 'Name for the LinkedIn account', 'text')}
                      <button class="btn sm" type="button" ${busy ?? linkedinOff} ${act('connect.makeLinkedIn', place)}>
                          Make the link
                      </button>
                  </div>`
                : null
        }`;
}

function pendingView(row: DesktopReplyPending, place: Place, busy: Html | null): Html {
    const until = momentOf(row.expiresAtUtc);
    return html`<div class="setrow acct-pending">
        <div class="grow">
            <b>${cap(row.name)}</b>
            <span class="meta"
                >${row.isExpired ? 'Its link expired' : until ? `Link valid until ${until}` : 'Waiting to be finished'}</span
            >
        </div>
        <span class="pillc ${row.isExpired ? 'gray' : 'amber'}">${row.isExpired ? 'Expired' : 'Waiting'}</span>
        <button class="btn sm" type="button" ${busy} ${act('connect.pendingLink', place, row.id, 'copy')}>
            Copy link
        </button>
        <button class="btn sm" type="button" ${busy} ${act('connect.pendingLink', place, row.id, 'open')}>Open</button>
    </div>`;
}

/** How many reads of the setup are going, and when the last one began. */
let reading = 0;
let lastRead = Number.NEGATIVE_INFINITY;

/** The window taking the focus back reads again no more often than this. */
const focusReadSpacingMs = 30_000;

export async function reload(): Promise<void> {
    reading += 1;
    lastRead = Date.now();
    try {
        const status = await here().desktop.setup.status();
        setup.value = { ...status, hosts: withModels(status.hosts) };
    } catch (error) {
        setupError.value = messageOf(error);
    } finally {
        reading -= 1;
    }
}

/** The window took the focus back: read again, unless a read is going or the last began under thirty seconds ago. */
export function reloadOnFocus(): void {
    if (reading > 0 || Date.now() - lastRead < focusReadSpacingMs) return;
    void reload();
}

define('connect.check', () => void reload());

async function doing(what: string, run: () => Promise<void>): Promise<void> {
    if (setupBusy.value !== null) return;
    setupBusy.value = what;
    setupError.value = null;
    try {
        await run();
    } catch (error) {
        setupError.value = messageOf(error);
    } finally {
        setupBusy.value = null;
    }
    await reload();
}

/** What is typed under `key`, trimmed. It stays typed until the desktop accepts the act, so a refusal is corrected. */
const typedOf = (key: string): string => (typed.get(key) ?? '').trim();

/** Forgets what was typed under `keys`, once the desktop has accepted what it was typed for. */
function forget(...keys: string[]): void {
    for (const key of keys) typed.delete(key);
}

define('connect.type', (args, element) => {
    if (typeof args[0] === 'string' && element instanceof HTMLInputElement) typed.set(args[0], element.value);
});

define('connect.install', ([program]) => {
    if (typeof program === 'string')
        void doing(`Installing ${program}…`, () => here().desktop.setup.installProgram(program));
});

define('connect.reply', () => void doing('Signing in to Reply…', () => here().desktop.setup.connectReply()));

define('connect.replyKey', () => {
    const key = typedOf('reply');
    if (key)
        void doing('Connecting Reply with the key…', async () => {
            await here().desktop.setup.connectReplyWithKey(key);
            forget('reply');
        });
});

define('connect.providerKey', ([provider, remove]) => {
    if (typeof provider !== 'string') return;
    const key = remove === true ? '' : typedOf(`provider:${provider}`);
    if (key || remove === true)
        void doing('Keeping the key…', async () => {
            await here().desktop.setup.setProviderKey(provider, key);
            forget(`provider:${provider}`);
        });
});

define('connect.plugin', ([id]) => {
    const plugin = setup.value?.plugins.find((each) => each.id === id);
    if (!plugin) return;
    const keys = ['token', 'target', 'server'].map((name) => `plugin:${plugin.id}:${name}`);
    const [token, target, typedServer] = keys.map(typedOf);
    const server = plugin.serverLabel ? typedServer : '';
    // A blank field is left out, which keeps what the file holds; removing a server is a button of its own.
    const values = { ...(token ? { token } : {}), ...(target ? { target } : {}), ...(server ? { server } : {}) };
    if (Object.keys(values).length === 0) return;
    void doing(`Configuring ${plugin.label}…`, async () => {
        await here().desktop.setup.configurePlugin(plugin.id, values);
        forget(...keys);
    });
});

define('connect.pluginServerClear', ([id]) => {
    const plugin = setup.value?.plugins.find((each) => each.id === id);
    if (plugin?.serverLabel)
        void doing(`Configuring ${plugin.label}…`, () =>
            here().desktop.setup.configurePlugin(plugin.id, { server: '' }),
        );
});

define('connect.disconnect', ([id]) => {
    if (typeof id === 'string') confirming.value = id;
});

define('connect.disconnectCancel', () => {
    confirming.value = null;
});

define('connect.disconnectConfirm', ([id]) => {
    const plugin = setup.value?.plugins.find((each) => each.id === id);
    confirming.value = null;
    if (plugin) void doing(`Disconnecting ${plugin.label}…`, () => here().desktop.setup.disconnectPlugin(plugin.id));
});

// Reload is the configure call with no values: it writes nothing, has the runtime read its plugins again, and reads
// them back. The reload is the installation's, so any of the plugins names it.
define('connect.reload', () => {
    const first = setup.value?.plugins[0];
    if (first) void doing('Reading the settings again…', () => here().desktop.setup.configurePlugin(first.id, {}));
});

const field = (key: string, label: string, kind: 'text' | 'password'): Html =>
    html`<input
        class="inp"
        type="${kind}"
        autocomplete="off"
        aria-label="${label}"
        placeholder="${label}"
        value="${typed.get(key) ?? ''}"
        data-input="connect.type"
        ${argsOf(key)}
    />`;

const secret = (key: string, label: string): Html => field(key, label, 'password');

/** A desktop sentence with each command it names, between backticks, drawn as code. */
const withCode = (sentence: string): Html[] =>
    sentence.split('`').map((part, at) => (at % 2 ? html`<code>${part}</code>` : html`${part}`));

function program(host: DesktopHost, busy: Html | null): Html {
    const sentence = signInSentence(host);
    return html`<div class="setrow">
        <div class="grow">
            <b>${host.label}</b>
            <span class="meta">${host.found ? (host.version ?? 'Found') : 'Not found'}</span>
            ${
                host.found
                    ? html`<div class="meta">
                          ${host.drivesChat ? 'Drives the chat' : 'Runs campaign work, not the chat yet'}
                      </div>`
                    : null
            }
            ${host.belowFloor !== null ? html`<div class="meta">${withCode(host.belowFloor)}</div>` : null}
            ${sentence ? html`<div class="meta sign-in">${withCode(sentence)}</div>` : null}
        </div>
        ${
            !host.found && host.install
                ? html`<button class="btn sm" type="button" ${busy} ${act('connect.install', host.install)}>
                      Install
                  </button>`
                : null
        }
    </div>`;
}

function provider(host: DesktopHost, busy: Html | null): Html {
    return html`<div class="setrow">
        <div class="grow"><b>${host.label}</b> <span class="meta">${host.found ? 'Key set' : 'No key'}</span></div>
        ${
            host.found
                ? html`<button class="btn sm" type="button" ${busy} ${act('connect.providerKey', host.id, true)}>
                      Remove the key
                  </button>`
                : html`${secret(`provider:${host.id}`, `${host.label} key`)}
                      <button class="btn sm" type="button" ${busy} ${act('connect.providerKey', host.id)}>Keep</button>`
        }
    </div>`;
}

function reply(state: DesktopSetup['reply'], busy: Html | null, label?: string, place: Place = 'connect'): Html {
    const shown = linkDialog.value?.place === place ? linkDialog.value : null;
    const pending = Array.isArray(state.linkedinPending) ? state.linkedinPending : [];
    // Every ask is off while a link is being asked for, and LinkedIn's below the Reply CLI that can make one.
    const asks = busy ?? (linkDialog.value?.asking === true ? html`disabled` : null);
    const floor = typeof state.linkedinBelowFloor === 'string' ? state.linkedinBelowFloor : null;
    const linkedinOff = floor === null ? null : html`disabled`;
    const cli = state.cli.found
        ? html`<span class="meta">Reply CLI ${state.cli.version ?? ''}</span>`
        : html`<span class="meta">The Reply CLI was not found.</span> ${
                  state.cli.install
                      ? html`<button class="btn sm" type="button" ${busy} ${act('connect.install', state.cli.install)}>
                            Install
                        </button>`
                      : null
              }`;
    return html`<div class="card" ${label ? html`role="region" aria-label="${label}"` : null}>
        <div class="hd"><b>Reply</b></div>
        <div class="bd">
            <div class="setrow"><div class="grow">${cli}</div></div>
            <div class="setrow">
                <div class="grow">
                    ${state.signedIn ? html`Signed in as <b>${state.user ?? ''}</b>` : 'Not signed in'}
                    <span class="meta">${state.wired ? 'Jason uses this sign-in' : 'Jason does not use it yet'}</span>
                </div>
                ${state.cli.found ? html`<button class="btn sm p" type="button" ${busy} ${act('connect.reply')}>Sign in</button>` : null}
            </div>
            ${
                state.cli.found
                    ? html`<div class="setrow">
                          <div class="grow meta">Or an API key, for an account the browser sign-in does not reach:</div>
                          ${secret('reply', 'Reply API key')}
                          <button class="btn sm" type="button" ${busy} ${act('connect.replyKey')}>Connect</button>
                      </div>`
                    : null
            }
            ${grouped(state.accounts, pending).map(
                ([, heading, members, waiting]) =>
                    html`<div class="acct-group">
                        <div class="setrow meta acct-hd">${heading}</div>
                        ${members.map((account) => {
                            const [words, tone] = stateOf(account);
                            return html`<div class="setrow acct">
                                <div class="grow">
                                    <b>${account.name}</b> <span class="meta">${account.address}</span>
                                </div>
                                <span class="pillc ${tone}">${words}</span>
                                ${
                                    state.signedIn && needsReconnect(account)
                                        ? html`<button
                                              class="btn sm"
                                              type="button"
                                              ${asks ?? (account.channel === 'linkedin' ? linkedinOff : null)}
                                              ${act('connect.reconnect', place, account.channel, account.id)}
                                          >
                                              Reconnect
                                          </button>`
                                        : null
                                }
                            </div>`;
                        })}
                        ${waiting.map((row) => pendingView(row, place, asks ?? linkedinOff))}
                    </div>`,
            )}
            ${unlisted(state).map((line) => html`<div class="setrow meta acct-none">${line}</div>`)}
            ${
                state.signedIn && state.cli.found
                    ? html`<div class="setrow">
                              <div class="grow meta">
                                  Accounts are read again when this window takes the focus back.
                              </div>
                              <button class="btn sm" type="button" ${asks} ${act('connect.addAccount', place)}>
                                  Add account
                              </button>
                              <button class="btn sm" type="button" ${act('connect.check')}>Check again</button>
                          </div>
                          ${floor !== null ? html`<div class="setrow meta link-floor">${floor}</div>` : null}
                          ${adding.value === place ? addView(state, place, asks, linkedinOff) : null}
                          ${shown !== null ? dialogView(shown, asks) : null}`
                    : null
            }
            <div class="setrow">
                <div class="grow meta">
                    Limits, signatures and pauses are set in Reply.
                    <a href="${replyAccountsAddress}" target="_blank" rel="noreferrer">Manage accounts in Reply</a>
                </div>
            </div>
        </div>
    </div>`;
}

function runtimeWords(plugin: DesktopPlugin): string {
    if (plugin.runtime === null) return 'The runtime has not read it.';
    if (!plugin.runtime.valid) return 'The runtime does not take its package or configuration as they stand.';
    const grants = plugin.runtime.grants === 1 ? '1 grant' : `${plugin.runtime.grants} grants`;
    return `The runtime reads it as valid, with ${grants}.`;
}

function plugin(each: DesktopPlugin, busy: Html | null, refused: boolean): Html {
    return html`<div class="setrow plugin" data-id="${each.id}">
        <div class="grow">
            <b>${each.label}</b> <span class="meta">${each.description}</span>
            <div class="meta">
                ${runtimeWords(each)}
                ${each.binding === false ? html` It has no binding yet: set its ${each.target.label.toLowerCase()}.` : null}
            </div>
            ${each.note ? html`<div class="meta">${each.note}</div>` : null}
        </div>
        ${
            each.configured
                ? html`<span class="pillc green">Configured</span>`
                : html`<span class="pillc gray">Not configured</span>`
        }
        ${
            refused
                ? null
                : html`${field(`plugin:${each.id}:target`, each.target.label, 'text')}
                      ${each.serverLabel ? field(`plugin:${each.id}:server`, each.serverLabel, 'text') : null}
                      ${each.tokenLabel ? secret(`plugin:${each.id}:token`, each.tokenLabel) : null}
                      ${
                          each.serverLabel
                              ? html`<button
                                    class="btn sm"
                                    type="button"
                                    ${busy}
                                    ${act('connect.pluginServerClear', each.id)}
                                >
                                    Use the public server
                                </button>`
                              : null
                      }
                      <button class="btn sm" type="button" ${busy} ${act('connect.plugin', each.id)}>Configure</button>
                      ${disconnect(each, busy)}`
        }
    </div>`;
}

/**
 * Disconnect, on a configured notification plugin only: the forwarder (the plugin with no binding) has none, since
 * without its receiver it would fail every journal line it is still granted. It asks once before the desktop acts.
 */
function disconnect(each: DesktopPlugin, busy: Html | null): Html | null {
    if (each.binding === null || !each.configured) return null;
    if (confirming.value !== each.id)
        return html`<button class="btn sm" type="button" ${busy} ${act('connect.disconnect', each.id)}>
            Disconnect
        </button>`;
    return html`<div class="note am" role="alertdialog" aria-label="Disconnect ${each.label}">
        Disconnect ${each.label}? Every value under its configuration in the settings file and its notification target
        are removed. Jason stops sending notifications there. The plugin stays installed.
        <button class="btn sm" type="button" ${busy} ${act('connect.disconnectConfirm', each.id)}>
            Disconnect ${each.label}
        </button>
        <button class="btn sm" type="button" ${act('connect.disconnectCancel')}>Cancel</button>
    </div>`;
}

function plugins(state: DesktopSetup, busy: Html | null): Html {
    const refused = state.settingsRefused !== null;
    return html`<div class="card">
        <div class="hd">
            <b>Plugins</b>
            <span class="meta">Where Jason tells you something waits, and where the journal goes.</span>
        </div>
        <div class="bd">
            ${
                refused
                    ? html`<div class="note am" role="alert">
                          ${state.settingsRefused} Edit it by hand, then press Reload.
                          <button class="btn sm" type="button" ${busy} ${act('connect.reload')}>Reload</button>
                      </div>`
                    : null
            }
            ${state.plugins.map((each) => plugin(each, busy, refused))}
        </div>
    </div>`;
}

/** Chats whose Reply card the person dismissed, by conversation id; kept for this window only. */
export const replyDismissed = signal<readonly string[]>([]);

/** The AI card alone, for a screen that needs an AI before it goes on: the hosts, found or not, and what they ask. */
export function aiCardView(): Html {
    const state = setup.value;
    const busy = setupBusy.value;
    const failure = setupError.value;
    const disabled = busy === null ? null : html`disabled`;
    return html`<div class="card" role="region" aria-label="Connect an AI">
        <div class="hd"><b>AI</b></div>
        <div class="bd">
            ${busy ? html`<div class="note" role="status"><span class="spin"></span>${busy}</div>` : null}
            ${failure ? html`<div class="note am" role="alert">${failure}</div>` : null}
            ${
                state
                    ? state.hosts.filter((each) => each.kind === 'program').map((each) => program(each, disabled))
                    : failure
                      ? null
                      : html`<div class="empty">Reading what is set up…</div>`
            }
        </div>
    </div>`;
}

/** The place of a chat's Reply card: its own, so a link asked in one chat never shows in another. */
export const chatPlace = (chatId: string): Place => `chat:${chatId}`;

const chatCards = new Map<string, () => Html>();

/**
 * The Reply card alone, for the moment a chat's work needs a way to send: one view per chat, the same each time it is
 * asked for, so a region painting it is painted once.
 */
export function replyCardFor(chatId: string): () => Html {
    let view = chatCards.get(chatId);
    if (view === undefined) {
        view = () => {
            const state = setup.value;
            if (!state) return html``;
            return reply(
                state.reply,
                setupBusy.value === null ? null : html`disabled`,
                'Connect Reply',
                chatPlace(chatId),
            );
        };
        chatCards.set(chatId, view);
    }
    return view;
}

export function connectView(): Html {
    const state = setup.value;
    const busy = setupBusy.value;
    const failure = setupError.value;
    const disabled = busy === null ? null : html`disabled`;
    const header = html`<div class="ph">
        <div>
            <h1>Connect</h1>
            <p>The AI that does the work, and the accounts it works through.</p>
        </div>
    </div>`;
    if (!state)
        return html`<div class="panel-body">
            ${header}${failure ? html`<div class="note am" role="alert">${failure}</div>` : html`<div class="empty">Reading what is set up…</div>`}
        </div>`;
    const programs = state.hosts.filter((each) => each.kind === 'program');
    const providers = state.hosts.filter((each) => each.kind === 'provider');
    return html`<div class="panel-body">
        ${header} ${busy ? html`<div class="note" role="status"><span class="spin"></span>${busy}</div>` : null}
        ${failure ? html`<div class="note am" role="alert">${failure}</div>` : null}
        <div class="card">
            <div class="hd"><b>AI</b></div>
            <div class="bd">${programs.map((each) => program(each, disabled))}</div>
        </div>
        ${
            providers.length
                ? html`<div class="card">
                      <div class="hd">
                          <b>Model keys</b>
                          <span class="meta">Run through Claude Code; kept in the system keychain.</span>
                      </div>
                      <div class="bd">${providers.map((each) => provider(each, disabled))}</div>
                  </div>`
                : null
        }
        ${reply(state.reply, disabled)} ${state.plugins.length ? plugins(state, disabled) : null}
        <div class="meta versions">
            Jason Desktop ${state.bundleVersion} · runtime ${state.runtimeVersion ?? 'not running'}
        </div>
        ${state.versionsDiffer === null ? null : html`<div class="meta versions-differ">${state.versionsDiffer}</div>`}
    </div>`;
}
