import { effect, signal } from '@preact/signals-react';
import type {
    DesktopHost,
    DesktopPlugin,
    DesktopReplyAccount,
    DesktopReplyChannel,
    DesktopReplyCopied,
    DesktopReplyDone,
    DesktopReplyOpened,
    DesktopReplyPending,
    DesktopReplyRefusal,
    DesktopSetup,
} from '../platform/desktop';
import { t } from '../platform/i18n';
import { mode } from '../platform/mode';
import { can, cannotHere, type DesktopMember } from '../platform/places';
import { act, argsOf, define, type Action } from './actions';
import { withModels } from './chat-model';
import { html, type Html } from './html';
import { signInSentence } from './live';
import { here, messageOf, setup, setupBusy, setupError } from './state';

// Connect: what the desktop found and set up, and the acts that set the rest up. The desktop does each act; the card
// says where things stand afterwards, from what `setup.status()` answers.

/** Keys and tokens being typed, held only until they are handed to the desktop. */
const typed = new Map<string, string>();

// In the demo the acts that connect or change a real account are off: the accounts there are the demo provider's, and a
// real one is connected in the person's own installation. A control drawn for such an act is disabled and the card says
// why in one sentence; the act itself also refuses to run, so a stale control cannot reach a real account.

/** Whether this window is the demo. */
const inDemo = (): boolean => mode.value === 'demo';

/** Whether this place cannot do the acts of a card: a browser tab, which only reads what the desktop found. */
const actsOff = (): boolean => !can('setup.connectReply');

/** Defines an act that connects or changes a real account: in the demo it does nothing. */
function defineReal(name: string, action: Action): void {
    define(name, (args, element, event) => {
        if (!inDemo()) action(args, element, event);
    });
}

/** The one sentence a card says in the demo about its acts being off. */
const notInDemo = (): Html | null =>
    inDemo()
        ? html`<div class="setrow meta demo-off" role="note">${t('mode.notDemo')}</div>`
        : actsOff()
          ? html`<div class="setrow meta demo-off" role="note">${cannotHere()}</div>`
          : null;

/** The one sentence a card whose acts this place cannot do says; nothing where it can. */
const elsewhere = (): Html | null =>
    actsOff() ? html`<div class="setrow meta demo-off" role="note">${cannotHere()}</div>` : null;

/** `busy`, or disabled in the demo, for a control that connects or changes a real account. */
const realBusy = (busy: Html | null): Html | null => (inDemo() ? html`disabled` : busy);

/** `busy`, or off where this place cannot do the member the control calls; a control that only changes the page is not asked. */
const unlessCan = (busy: Html | null, member: DesktopMember): Html | null => (can(member) ? busy : html`disabled`);

/** The plugin whose Disconnect waits on the person's confirmation; null when none does. */
const confirming = signal<string | null>(null);

// A confirmation is forgotten once the plugin it names is no longer configured, so configuring it again asks afresh.
effect(() => {
    const id = confirming.value;
    if (id !== null && !setup.value?.plugins.some((each) => each.id === id && each.configured)) confirming.value = null;
});

/**
 * Reply's web app, where what the card does not do is managed: a mailbox other than Gmail or Outlook (SMTP/IMAP),
 * signatures, and a LinkedIn account's limits. The card links there for those.
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
 * each with the links made for it and not used yet, which make its heading even with no account.
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
            pending.filter((row) => row.channel === channel),
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

// ---- The acts: each one verb of the Reply CLI, which the desktop runs ------------------------------------------------
//
// A link the Reply CLI makes for connecting or reconnecting an account goes from the desktop straight to the person's
// browser: the card learns only that it was opened, when it stops working and its pending entry. A limit's change, a
// pause, a resume, an unblock and a delete ask the person first in a box of the desktop's own, as do the shared mailbox
// page and a forced delete; the card can only ask for the act.

/** Which of the card's places a dialog or a choice belongs to: the Connect panel's card, or the chat's. */
type Place = string;

type Channel = DesktopReplyChannel;

/** What the card may do next after a refusal offered it: the same ask again with the shared page, or forced. */
type Again = () => Promise<DesktopReplyOpened | DesktopReplyCopied | DesktopReplyDone | DesktopReplyRefusal>;

/** A link act's answer: opened in the browser, copied for the person to hand on, or refused. */
type LinkAnswer = DesktopReplyOpened | DesktopReplyCopied | DesktopReplyRefusal;

interface ActDialog {
    place: Place;
    title: string;
    asking: boolean;
    opened: DesktopReplyOpened | null;
    /** A link the desktop put on the clipboard for the person to hand on; the card never holds the link itself. */
    copied: DesktopReplyCopied | null;
    done: boolean;
    refused: DesktopReplyRefusal | null;
    /** The account the act was on, for a Reconnect a signed-out refusal offers; null for a new account. */
    account: { channel: Channel; id: string } | null;
    /** The ask the refusal's offer makes: the shared page or the forced delete; null for none. */
    again: Again | null;
}

const actDialog = signal<ActDialog | null>(null);
/** The place whose Add account choices are open; null when none is. */
const adding = signal<Place | null>(null);
/** The place whose LinkedIn name field is open; null when none is. */
const naming = signal<Place | null>(null);
/** The mailbox whose daily limit is shown for a change, at its place; null when none is. */
const limiting = signal<{ place: Place; id: string; daily: number | null } | null>(null);
/**
 * Whether a new link is copied to hand on instead of opened here. The desktop copies only a link that expires, never the
 * shared mailbox page, which is always opened here.
 */
const copying = signal(false);
/** The LinkedIn accounts the CLI said are signed out, by id: Pause and Resume are not offered while that stands. */
const signedOut = signal<readonly string[]>([]);
/** Moves on at each ask and each close, so an answer that comes after its dialog was closed or replaced is dropped. */
let asked = 0;

const mailboxProviders: readonly (readonly ['gmail' | 'office365', string])[] = [
    ['gmail', 'Gmail'],
    ['office365', 'Outlook'],
];

const isProvider = (value: unknown): value is 'gmail' | 'office365' =>
    mailboxProviders.some(([each]) => each === value);
const isChannel = (value: unknown): value is Channel => value === 'email' || value === 'linkedin';

function closeDialog(): void {
    asked += 1;
    actDialog.value = null;
}

/**
 * Drops the dialog, the choices, the limit field and the name field a card at `place` has open: called when that card is
 * removed; a tab that is only hidden keeps its open dialog.
 */
export function dropDialogs(place: Place): void {
    if (actDialog.peek()?.place === place) closeDialog();
    if (adding.peek() === place) adding.value = null;
    if (naming.peek() === place) naming.value = null;
    if (limiting.peek()?.place === place) limiting.value = null;
}

/** The place whose dialog the card holds open; null when none is. */
export const heldDialog = (): Place | null => actDialog.peek()?.place ?? null;

/** Drops every dialog, choice and field the card has open: what a console that goes away leaves behind. */
export function resetDialogs(): void {
    closeDialog();
    adding.value = null;
    naming.value = null;
    limiting.value = null;
    signedOut.value = [];
    copying.value = false;
}

const listedAccount = (channel: unknown, id: unknown): DesktopReplyAccount | undefined =>
    setup.value?.reply.accounts.find((each) => each.channel === channel && each.id === id);

const accountTitle = (account: DesktopReplyAccount): string => cap(account.name || account.address || 'the account');

/**
 * Opens a dialog that asks the desktop for one act and says how it ended. A refusal that says the list may have changed
 * reads it again, as does an act done; nothing is ever repeated without the person's click.
 */
async function ask(
    place: Place,
    title: string,
    account: ActDialog['account'],
    run: () => Promise<
        DesktopReplyOpened | DesktopReplyCopied | DesktopReplyDone | DesktopReplyRefusal | { daily: number | null }
    >,
    again: (refusal: DesktopReplyRefusal) => Again | null = () => null,
): Promise<void> {
    asked += 1;
    const mine = asked;
    adding.value = null;
    naming.value = null;
    limiting.value = null;
    actDialog.value = {
        place,
        title,
        asking: true,
        opened: null,
        copied: null,
        done: false,
        refused: null,
        account,
        again: null,
    };
    let answer:
        DesktopReplyOpened | DesktopReplyCopied | DesktopReplyDone | DesktopReplyRefusal | { daily: number | null };
    try {
        answer = await run();
    } catch (error) {
        answer = { refused: messageOf(error), code: null, offer: null, reread: false };
    }
    if (mine !== asked || actDialog.value === null) {
        // The dialog went while the act was asked; the list is still read again where the act was done or said to.
        if (!('refused' in answer) || answer.reread) void reload();
        return;
    }
    if ('refused' in answer) {
        if (answer.offer === 'reconnect' && account !== null && !signedOut.value.includes(account.id))
            signedOut.value = [...signedOut.value, account.id];
        actDialog.value = { ...actDialog.value, asking: false, refused: answer, again: again(answer) };
        if (answer.reread) void reload();
        return;
    }
    if ('daily' in answer) {
        // A limit read: shown in its field for a change, and the dialog that read it closed.
        actDialog.value = null;
        if (account !== null) {
            typed.set(`limit:${account.id}`, answer.daily === null ? '' : String(answer.daily));
            limiting.value = { place, id: account.id, daily: answer.daily };
        }
        return;
    }
    actDialog.value = {
        ...actDialog.value,
        asking: false,
        opened: 'opened' in answer ? answer : null,
        copied: 'copied' in answer ? answer : null,
        done: 'done' in answer,
    };
    void reload();
}

/**
 * A link act: connect or reconnect, copied instead of opened where the person asked so, whose refusal may offer the
 * shared page, asked again with it only on a click and always to open: the shared page is never copied.
 */
function askLink(
    place: Place,
    title: string,
    account: ActDialog['account'],
    run: (how: { sharedPage?: true; copy?: true }) => Promise<LinkAnswer>,
): Promise<void> {
    const copy = copying.value;
    return ask(
        place,
        title,
        account,
        () => run(copy ? { copy } : {}),
        (refusal) => (refusal.offer === 'shared_page' ? () => run({ sharedPage: true }) : null),
    );
}

define('connect.copyLinks', (_args, element) => {
    if (element instanceof HTMLInputElement) copying.value = element.checked;
});

defineReal('connect.addAccount', ([place]) => {
    if (typeof place !== 'string') return;
    adding.value = adding.value === place ? null : place;
    naming.value = null;
});

define('connect.addCancel', () => {
    adding.value = null;
    naming.value = null;
});

defineReal('connect.addMailbox', ([place, provider]) => {
    if (!can('setup.replyConnect')) return;
    if (typeof place !== 'string' || !isProvider(provider)) return;
    const name = mailboxProviders.find(([each]) => each === provider)![1];
    void askLink(place, `Connect a ${name} mailbox`, null, (how) =>
        here().desktop.setup.replyConnect('email', { provider, ...how }),
    );
});

defineReal('connect.addLinkedIn', ([place]) => {
    if (typeof place === 'string') naming.value = place;
});

defineReal('connect.makeLinkedIn', ([place]) => {
    if (!can('setup.replyConnect')) return;
    const name = typedOf('linkedin-name');
    if (typeof place !== 'string' || !name) return;
    const copy = copying.value;
    void ask(place, 'Connect a LinkedIn account', null, async () => {
        const answer = await here().desktop.setup.replyConnect('linkedin', { name, ...(copy ? { copy } : {}) });
        if (!('refused' in answer)) forget('linkedin-name');
        return answer;
    });
});

defineReal('connect.reconnect', ([place, channel, id]) => {
    if (!can('setup.replyReconnect')) return;
    const account = listedAccount(channel, id);
    if (typeof place !== 'string' || !isChannel(channel) || !account) return;
    signedOut.value = signedOut.value.filter((each) => each !== account.id);
    void askLink(place, `Reconnect ${accountTitle(account)}`, { channel, id: account.id }, (how) =>
        here().desktop.setup.replyReconnect(channel, account.id, how.sharedPage || how.copy ? how : undefined),
    );
});

defineReal('connect.pause', ([place, id]) => {
    if (!can('setup.replyPause')) return;
    const account = listedAccount('linkedin', id);
    if (typeof place === 'string' && account)
        void ask(place, `Pause ${accountTitle(account)}`, { channel: 'linkedin', id: account.id }, () =>
            here().desktop.setup.replyPause(account.id),
        );
});

defineReal('connect.resume', ([place, id]) => {
    if (!can('setup.replyResume')) return;
    const account = listedAccount('linkedin', id);
    if (typeof place === 'string' && account)
        void ask(place, `Resume ${accountTitle(account)}`, { channel: 'linkedin', id: account.id }, () =>
            here().desktop.setup.replyResume(account.id),
        );
});

defineReal('connect.unblock', ([place, id]) => {
    if (!can('setup.replyUnblock')) return;
    const account = listedAccount('email', id);
    if (typeof place === 'string' && account)
        void ask(place, `Unblock ${accountTitle(account)}`, { channel: 'email', id: account.id }, () =>
            here().desktop.setup.replyUnblock(account.id),
        );
});

defineReal('connect.delete', ([place, channel, id]) => {
    if (!can('setup.replyDelete')) return;
    const account = listedAccount(channel, id);
    if (typeof place !== 'string' || !isChannel(channel) || !account) return;
    void ask(
        place,
        `Delete ${accountTitle(account)}`,
        { channel, id: account.id },
        () => here().desktop.setup.replyDelete(channel, account.id),
        (refusal) =>
            refusal.offer === 'force'
                ? () => here().desktop.setup.replyDelete(channel, account.id, { force: true })
                : null,
    );
});

defineReal('connect.limits', ([place, id]) => {
    if (!can('setup.replyLimits')) return;
    const account = listedAccount('email', id);
    if (typeof place !== 'string' || !account) return;
    if (limiting.value?.place === place && limiting.value.id === account.id) {
        limiting.value = null;
        return;
    }
    void ask(place, `Daily limit of ${accountTitle(account)}`, { channel: 'email', id: account.id }, () =>
        here().desktop.setup.replyLimits(account.id),
    );
});

defineReal('connect.setLimit', ([place, id]) => {
    if (!can('setup.replyLimits')) return;
    const account = listedAccount('email', id);
    const text = typedOf(`limit:${typeof id === 'string' ? id : ''}`);
    if (typeof place !== 'string' || !account || !/^\d{1,10}$/.test(text)) return;
    void ask(place, `Daily limit of ${accountTitle(account)}`, { channel: 'email', id: account.id }, () =>
        here()
            .desktop.setup.replyLimits(account.id, Number(text))
            .then((answer) => ('refused' in answer ? answer : { done: true as const })),
    );
});

defineReal('connect.again', () => {
    const shown = actDialog.value;
    if (shown?.again != null) void ask(shown.place, shown.title, shown.account, shown.again);
});

define('connect.closeLink', () => closeDialog());
define('connect.checkNow', () => void reload());

// A dialog is dropped with the sign-in it was asked under.
effect(() => {
    if (setup.value !== null && !setup.value.reply.signedIn) {
        if (actDialog.peek() !== null) closeDialog();
        adding.value = null;
        naming.value = null;
        limiting.value = null;
    }
});

/** A moment Reply wrote, as a person reads it: `2026-11-19 04:59 UTC`; null for one that is not a moment. */
function momentOf(text: string | null): string | null {
    if (text === null) return null;
    const at = new Date(/(?:Z|[+-]\d{2}:\d{2})$/.test(text) ? text : `${text}Z`);
    return Number.isNaN(at.getTime()) ? null : `${at.toISOString().slice(0, 16).replace('T', ' ')} UTC`;
}

const offers: Record<NonNullable<DesktopReplyRefusal['offer']>, string> = {
    shared_page: 'Open the shared mailbox page…',
    force: 'Delete anyway…',
    reconnect: 'Reconnect',
};

function dialogView(shown: ActDialog, busy: Html | null): Html {
    const opened = shown.opened;
    const until = momentOf(opened?.expiresAt ?? null);
    const offer = shown.refused?.offer ?? null;
    return html`<div class="note act-dialog" role="dialog" aria-label="${shown.title}">
        <b>${shown.title}</b>
        ${shown.asking ? html`<div class="meta" role="status"><span class="spin"></span>Asking Reply…</div>` : null}
        ${shown.refused !== null ? html`<div class="note am" role="alert">${shown.refused.refused}</div>` : null}
        ${
            offer === 'reconnect' && shown.account !== null
                ? html`<div class="setrow">
                      <button
                          class="btn sm"
                          type="button"
                          ${busy}
                          ${act('connect.reconnect', shown.place, shown.account.channel, shown.account.id)}
                      >
                          ${offers.reconnect}
                      </button>
                  </div>`
                : offer !== null && shown.again !== null
                  ? html`<div class="setrow">
                        <button class="btn sm" type="button" ${busy} ${act('connect.again')}>${offers[offer]}</button>
                    </div>`
                  : null
        }
        ${
            opened !== null
                ? html`<div class="meta" role="status">The link opened in your browser.</div>
                      <p class="meta link-note">
                          ${
                              opened.linkKind === 'shared_page'
                                  ? 'It is Reply’s shared mailbox page, which does not expire: close it once the mailbox is connected.'
                                  : `It connects one account until ${until ?? 'it expires'}.`
                          }
                      </p>
                      <div class="setrow">
                          <div class="grow meta">
                              The account appears in the list when the sign-in there is finished.
                          </div>
                          <button class="btn sm" type="button" ${act('connect.checkNow')}>Check now</button>
                      </div>`
                : null
        }
        ${
            shown.copied !== null
                ? html`<div class="meta" role="status">The link was copied.</div>
                      <p class="meta link-note">
                          Hand it to whoever owns the account: it connects one account to this Reply account until
                          ${momentOf(shown.copied.expiresAt) ?? 'it expires'}, so send it to no one else.
                      </p>
                      <div class="setrow">
                          <div class="grow meta">The account appears in the list when they finish.</div>
                          <button class="btn sm" type="button" ${act('connect.checkNow')}>Check now</button>
                      </div>`
                : null
        }
        ${shown.done ? html`<div class="meta" role="status">Done.</div>` : null}
        <div class="setrow"><button class="btn sm" type="button" ${act('connect.closeLink')}>Close</button></div>
    </div>`;
}

/** Add account's choices: Gmail and Outlook where email is not absent, LinkedIn where it is not, and Cancel. */
function addView(state: DesktopSetup['reply'], place: Place, busy: Html | null): Html {
    const absent = Array.isArray(state.absent) ? state.absent : [];
    return html`<div class="setrow add-account">
            ${
                absent.includes('email')
                    ? null
                    : mailboxProviders.map(
                          ([provider, name]) =>
                              html`<button
                                  class="btn sm"
                                  type="button"
                                  ${unlessCan(busy, 'setup.replyConnect')}
                                  ${act('connect.addMailbox', place, provider)}
                              >
                                  ${name}
                              </button>`,
                      )
            }
            ${
                absent.includes('linkedin')
                    ? null
                    : html`<button class="btn sm" type="button" ${busy} ${act('connect.addLinkedIn', place)}>
                          LinkedIn
                      </button>`
            }
            <button class="btn sm" type="button" ${act('connect.addCancel')}>Cancel</button>
        </div>
        ${
            naming.value === place
                ? html`<div class="setrow">
                      ${field('linkedin-name', 'Name for the LinkedIn account', 'text')}
                      <button
                          class="btn sm"
                          type="button"
                          ${unlessCan(busy, 'setup.replyConnect')}
                          ${act('connect.makeLinkedIn', place)}
                      >
                          Make the link
                      </button>
                  </div>`
                : null
        }`;
}

/** The acts a row offers, by its channel and state; Pause and Resume not while the account is signed out. */
function rowActs(account: DesktopReplyAccount, place: Place, busy: Html | null): Html[] {
    const button = (label: string, name: string, member: DesktopMember, ...args: string[]) =>
        html`<button class="btn sm" type="button" ${unlessCan(busy, member)} ${act(name, place, ...args)}>
            ${label}
        </button>`;
    const acts: Html[] = [];
    if (needsReconnect(account))
        acts.push(button('Reconnect', 'connect.reconnect', 'setup.replyReconnect', account.channel, account.id));
    if (account.channel === 'email') {
        acts.push(
            button('Limits', 'connect.limits', 'setup.replyLimits', account.id),
            button('Unblock', 'connect.unblock', 'setup.replyUnblock', account.id),
        );
    } else if (
        account.channel === 'linkedin' &&
        account.status !== 'cookieInvalid' &&
        !signedOut.value.includes(account.id)
    ) {
        // Reply's one call flips the state, so Pause is offered only from enabled and Resume only from disabled; an
        // account at its daily limit is on and offers neither.
        if (account.status === 'disabled')
            acts.push(button('Resume', 'connect.resume', 'setup.replyResume', account.id));
        else if (account.status === 'enabled')
            acts.push(button('Pause', 'connect.pause', 'setup.replyPause', account.id));
    }
    if (account.channel === 'email' || account.channel === 'linkedin')
        acts.push(button('Delete', 'connect.delete', 'setup.replyDelete', account.channel, account.id));
    return acts;
}

/** A mailbox's daily limit, open for a change: the field, and Set, which the desktop asks the person about first. */
function limitView(account: DesktopReplyAccount, place: Place, busy: Html | null): Html | null {
    const open = limiting.value;
    if (open === null || open.place !== place || open.id !== account.id) return null;
    return html`<div class="setrow acct-limit">
        <div class="grow meta">Emails a day${open.daily === null ? '' : `, now ${open.daily}`}:</div>
        ${field(`limit:${account.id}`, 'Daily limit', 'text')}
        <button
            class="btn sm"
            type="button"
            ${unlessCan(busy, 'setup.replyLimits')}
            ${act('connect.setLimit', place, account.id)}
        >
            Set
        </button>
    </div>`;
}

function pendingView(row: DesktopReplyPending): Html {
    const until = momentOf(row.expiresAt);
    return html`<div class="setrow acct-pending">
        <div class="grow">
            <b>${cap(row.name || (row.kind === 'reconnect' ? 'A reconnect' : 'A new account'))}</b>
            <span class="meta"
                >${row.expired ? 'Its link expired' : until ? `Link valid until ${until}` : 'Waiting to be finished'}</span
            >
        </div>
        <span class="pillc ${row.expired ? 'gray' : 'amber'}">${row.expired ? 'Expired' : 'Waiting'}</span>
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
    if (!can('setup.installProgram')) return;
    // The Reply CLI is the Reply account's program: placing or updating it is off in the demo, an AI's program is not.
    if (inDemo() && program === setup.value?.reply.cli.install) return;
    if (typeof program === 'string')
        void doing(`Installing ${program}…`, () => here().desktop.setup.installProgram(program));
});

defineReal('connect.reply', () => {
    if (!can('setup.connectReply')) return;
    void doing('Signing in to Reply…', () => here().desktop.setup.connectReply());
});

defineReal('connect.replyKey', () => {
    if (!can('setup.connectReplyWithKey')) return;
    const key = typedOf('reply');
    if (key)
        void doing('Connecting Reply with the key…', async () => {
            await here().desktop.setup.connectReplyWithKey(key);
            forget('reply');
        });
});

define('connect.providerKey', ([provider, remove]) => {
    if (!can('setup.setProviderKey')) return;
    if (typeof provider !== 'string') return;
    const key = remove === true ? '' : typedOf(`provider:${provider}`);
    if (key || remove === true)
        void doing('Keeping the key…', async () => {
            await here().desktop.setup.setProviderKey(provider, key);
            forget(`provider:${provider}`);
        });
});

defineReal('connect.plugin', ([id]) => {
    if (!can('setup.configurePlugin')) return;
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

defineReal('connect.pluginServerClear', ([id]) => {
    if (!can('setup.configurePlugin')) return;
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

defineReal('connect.disconnectConfirm', ([id]) => {
    if (!can('setup.disconnectPlugin')) return;
    const plugin = setup.value?.plugins.find((each) => each.id === id);
    confirming.value = null;
    if (plugin) void doing(`Disconnecting ${plugin.label}…`, () => here().desktop.setup.disconnectPlugin(plugin.id));
});

// Reload is the configure call with no values: it writes nothing, has the runtime read its plugins again, and reads
// them back. The reload is the installation's, so any of the plugins names it.
define('connect.reload', () => {
    if (!can('setup.configurePlugin')) return;
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
                ? html`<button
                      class="btn sm"
                      type="button"
                      ${unlessCan(busy, 'setup.installProgram')}
                      ${act('connect.install', host.install)}
                  >
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
                ? html`<button
                      class="btn sm"
                      type="button"
                      ${unlessCan(busy, 'setup.setProviderKey')}
                      ${act('connect.providerKey', host.id, true)}
                  >
                      Remove the key
                  </button>`
                : html`${secret(`provider:${host.id}`, `${host.label} key`)}
                      <button
                          class="btn sm"
                          type="button"
                          ${unlessCan(busy, 'setup.setProviderKey')}
                          ${act('connect.providerKey', host.id)}
                      >
                          Keep
                      </button>`
        }
    </div>`;
}

function reply(state: DesktopSetup['reply'], busy: Html | null, label?: string, place: Place = 'connect'): Html {
    const shown = actDialog.value?.place === place ? actDialog.value : null;
    const pending = Array.isArray(state.pending) ? state.pending : [];
    // Below the Reply CLI the acts need, the card says so and offers no act.
    const below = typeof state.belowMinimum === 'string' ? state.belowMinimum : null;
    const offering = state.signedIn && state.cli.found && below === null;
    // Every act is off while one is being asked for.
    const asks = realBusy(busy ?? (actDialog.value?.asking === true ? html`disabled` : null));
    const real = realBusy(busy);
    const cli = state.cli.found
        ? html`<span class="meta">Reply CLI ${state.cli.version ?? ''}</span>`
        : html`<span class="meta">The Reply CLI was not found.</span> ${
                  state.cli.install
                      ? html`<button
                            class="btn sm"
                            type="button"
                            ${unlessCan(real, 'setup.installProgram')}
                            ${act('connect.install', state.cli.install)}
                        >
                            Install
                        </button>`
                      : null
              }`;
    return html`<div class="card" ${label ? html`role="region" aria-label="${label}"` : null}>
        <div class="hd"><b>Reply</b></div>
        <div class="bd">
            ${notInDemo()}
            <div class="setrow"><div class="grow">${cli}</div></div>
            ${
                state.cli.found && below !== null
                    ? html`<div class="setrow below-minimum">
                          <div class="grow meta">${below}</div>
                          ${
                              state.cli.install
                                  ? html`<button
                                        class="btn sm"
                                        type="button"
                                        ${unlessCan(real, 'setup.installProgram')}
                                        ${act('connect.install', state.cli.install)}
                                    >
                                        Update
                                    </button>`
                                  : null
                          }
                      </div>`
                    : null
            }
            <div class="setrow">
                <div class="grow">
                    ${state.signedIn ? html`Signed in as <b>${state.user ?? ''}</b>` : 'Not signed in'}
                    <span class="meta">${state.wired ? 'Jason uses this sign-in' : 'Jason does not use it yet'}</span>
                </div>
                ${state.cli.found ? html`<button class="btn sm p" type="button" ${unlessCan(real, 'setup.connectReply')} ${act('connect.reply')}>Sign in</button>` : null}
            </div>
            ${
                state.cli.found
                    ? html`<div class="setrow">
                          <div class="grow meta">Or an API key, for an account the browser sign-in does not reach:</div>
                          ${secret('reply', 'Reply API key')}
                          <button
                              class="btn sm"
                              type="button"
                              ${unlessCan(real, 'setup.connectReplyWithKey')}
                              ${act('connect.replyKey')}
                          >
                              Connect
                          </button>
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
                                    ${offering ? rowActs(account, place, asks) : null}
                                </div>
                                ${offering ? limitView(account, place, asks) : null}`;
                        })}
                        ${waiting.map((row) => pendingView(row))}
                        ${
                            waiting.length > 0
                                ? html`<div class="setrow meta acct-pending-hint">
                                      A link here is not shown again: add the account again to make a new link.
                                  </div>`
                                : null
                        }
                    </div>`,
            )}
            ${unlisted(state).map((line) => html`<div class="setrow meta acct-none">${line}</div>`)}
            ${
                offering
                    ? html`<div class="setrow">
                              <div class="grow meta">
                                  Accounts are read again when this window takes the focus back.
                              </div>
                              <button class="btn sm" type="button" ${asks} ${act('connect.addAccount', place)}>
                                  Add account
                              </button>
                              <button class="btn sm" type="button" ${act('connect.check')}>Check again</button>
                          </div>
                          <label class="setrow meta link-copy">
                              <input
                                  type="checkbox"
                                  data-change="connect.copyLinks"
                                  ${copying.value ? html`checked` : null}
                              />
                              Copy a new link to hand on to whoever owns the account, instead of opening it here
                          </label>
                          ${adding.value === place ? addView(state, place, asks) : null}
                          ${shown !== null ? dialogView(shown, asks) : null}`
                    : null
            }
            <div class="setrow">
                <div class="grow meta">
                    Other mailboxes (SMTP/IMAP), signatures and a LinkedIn account’s limits are set in Reply.
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

function plugin(each: DesktopPlugin, busyReal: Html | null, refused: boolean): Html {
    const busy = realBusy(busyReal);
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
                                    ${unlessCan(busy, 'setup.configurePlugin')}
                                    ${act('connect.pluginServerClear', each.id)}
                                >
                                    Use the public server
                                </button>`
                              : null
                      }
                      <button
                          class="btn sm"
                          type="button"
                          ${unlessCan(busy, 'setup.configurePlugin')}
                          ${act('connect.plugin', each.id)}
                      >
                          Configure
                      </button>
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
        <button
            class="btn sm"
            type="button"
            ${unlessCan(busy, 'setup.disconnectPlugin')}
            ${act('connect.disconnectConfirm', each.id)}
        >
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
            ${notInDemo()}
            ${
                refused
                    ? html`<div class="note am" role="alert">
                          ${state.settingsRefused} Edit it by hand, then press Reload.
                          <button
                              class="btn sm"
                              type="button"
                              ${unlessCan(busy, 'setup.configurePlugin')}
                              ${act('connect.reload')}
                          >
                              Reload
                          </button>
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
            ${failure ? html`<div class="note am" role="alert">${failure}</div>` : null} ${elsewhere()}
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

/** The place of the Accounts page's Reply card: its own, so a link asked there never shows on the Connect panel. */
export const accountsPlace: Place = 'accounts';

/**
 * The Reply card alone for the Accounts page: the same card, with the same acts, at a place of its own. One function, so
 * the region painting it is painted once.
 */
export function accountsReplyView(): Html {
    const state = setup.value;
    if (!state) {
        const failure = setupError.value;
        return failure
            ? html`<div class="note am" role="alert">${failure}</div>`
            : html`<div class="empty">Reading what is set up…</div>`;
    }
    return reply(state.reply, setupBusy.value === null ? null : html`disabled`, 'Reply accounts', accountsPlace);
}

/** The Accounts page stopped painting the card: a dialog it held open is dropped. */
export const accountsReplyGone = (): void => dropDialogs(accountsPlace);

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
            <div class="bd">${elsewhere()}${programs.map((each) => program(each, disabled))}</div>
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
