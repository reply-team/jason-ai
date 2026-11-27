import { effect, signal } from '@preact/signals-react';
import type {
    DesktopAccountRef,
    DesktopDefaultFor,
    DesktopHost,
    DesktopPlugin,
    DesktopReplySender,
    DesktopReplyChannel,
    DesktopReplyCopied,
    DesktopReplyDone,
    DesktopReplyOpened,
    DesktopReplyPending,
    DesktopReplyRefusal,
    DesktopReplySignIn,
    DesktopReplyTeam,
    DesktopSetup,
} from '../platform/desktop';
import { t } from '../platform/i18n';
import { mode } from '../platform/mode';
import { act, argsOf, define, type Action } from './actions';
import { withModels } from './chat-model';
import { html, type Html } from './html';
import { signInSentence } from './live';
import { earlier } from '../platform/window-desktop';
import { here, messageOf, mounted, setup, setupBusy, setupError, setupReadFailed } from './state';

// Connect: what the desktop found and set up, and the acts that set the rest up. The desktop does each act; the card
// says where things stand afterwards, from what `setup.status()` answers.

/** Keys and tokens being typed, held only until they are handed to the desktop. */
const typed = new Map<string, string>();

// In the demo the acts that connect or change a real account are off: the accounts there are the demo provider's, and a
// real one is connected in the person's own installation. A control drawn for such an act is disabled and the card says
// why in one sentence; the act itself also refuses to run, so a stale control cannot reach a real account.

/** Whether this window is the demo. */
const inDemo = (): boolean => mode.value === 'demo';

/** Defines an act that connects or changes a real account: in the demo it does nothing. */
function defineReal(name: string, action: Action): void {
    define(name, (args, element, event) => {
        if (!inDemo()) action(args, element, event);
    });
}

/** The one sentence a card says in the demo about its acts being off. */
const notInDemo = (): Html | null =>
    inDemo() ? html`<div class="setrow meta demo-off" role="note">${t('mode.notDemo')}</div>` : null;

/** `busy`, or disabled in the demo, for a control that connects or changes a real account. */
const realBusy = (busy: Html | null): Html | null => (inDemo() ? html`disabled` : busy);

/** `realBusy`, and disabled too while an act on a Reply account is asked: one act at a time, as the desktop holds it. */
const asking = (busy: Html | null): Html | null =>
    realBusy(busy ?? (actDialog.value?.asking === true ? html`disabled` : null));

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

// ---- The Reply accounts: one card per profile of the Reply CLI ------------------------------------------------------
//
// The desktop reads the accounts at each status and keeps none: every profile the Reply CLI holds and every profile a
// route to Jason's Reply plugin names, the default first. A desktop of an earlier version sends none, and its one Reply
// state is drawn as one account whose name it cannot give, with the acts of its version, which name no account.

/** One Reply account as the page draws it, from either desktop. */
interface AccountView {
    /** How the page names it to the desktop; null for an earlier desktop's one account, whose name it cannot give. */
    ref: DesktopAccountRef | null;
    isDefault: boolean;
    /** Every other global route to Reply that names it, each a mark of its card. */
    defaultFor: readonly DesktopDefaultFor[];
    /** Whether the Reply CLI holds its profile; false for one only a route names. */
    held: boolean;
    /** Whether a route that names no profile follows it as the Reply CLI's current profile, so it is not removed. */
    followed: boolean;
    signedIn: boolean;
    user: string | null;
    /** The team its sign-in is bound to; null where it is bound to none, or the desktop does not say. */
    team: DesktopReplyTeam | null;
    /** Its sign-in could not be read: said as that, and no sign-in. */
    authUnread: boolean;
    /** The teams of its sign-in could not be read: said as that, its other reads standing. */
    teamsUnread: boolean;
    senders: readonly DesktopReplySender[];
    absent: readonly DesktopReplyChannel[];
    unread: readonly DesktopReplyChannel[];
    pending: readonly DesktopReplyPending[];
    pendingUnread: readonly DesktopReplyChannel[];
    /** The teams its sign-in can act in, where Reply would not list it for want of one; null where there is no choice. */
    teams: readonly DesktopReplyTeam[] | null;
    /** Who it was last seen signed in as, as the desktop's note keeps it; null where nothing is noted. */
    lastSignIn: DesktopReplySignIn | null;
    /** Whether the last read found another login or team than the note says. */
    changed: boolean;
}

const listOf = <T>(value: readonly T[] | undefined): readonly T[] => (Array.isArray(value) ? value : []);

/** Whether the desktop sent the accounts at all, which a desktop of an earlier version does not. */
const accountsSent = (state: DesktopSetup): boolean => Array.isArray(state.accounts?.accounts);

/**
 * The accounts the page draws: the desktop's, in its order; or, from a desktop of an earlier version, its one Reply state
 * as one account, where its Reply CLI was found.
 */
function accountViews(state: DesktopSetup): AccountView[] {
    if (accountsSent(state))
        return state.accounts!.accounts.map((each) => ({
            ref: { type: each.type, name: each.name },
            isDefault: each.default === true,
            defaultFor: listOf(each.defaultFor),
            held: each.held === true,
            followed: each.followed === true,
            signedIn: each.signedIn === true,
            user: each.user,
            team: each.team ?? null,
            authUnread: listOf(each.notRead).includes('auth'),
            teamsUnread: listOf(each.notRead).includes('teams'),
            senders: listOf(each.senders),
            absent: listOf(each.absent),
            unread: listOf(each.unread),
            pending: listOf(each.pending),
            pendingUnread: listOf(each.pendingUnread),
            teams: Array.isArray(each.teams) && each.teams.length > 0 ? each.teams : null,
            lastSignIn: each.lastSignIn ?? null,
            changed: each.changed === true,
        }));
    const reply = state.reply;
    if (!reply.cli.found) return [];
    return [
        {
            ref: null,
            isDefault: false,
            defaultFor: [],
            held: true,
            followed: false,
            signedIn: reply.signedIn,
            user: reply.user,
            team: null,
            authUnread: listOf(reply.notRead).includes('auth'),
            teamsUnread: false,
            senders: listOf(reply.accounts),
            absent: listOf(reply.absent),
            unread: listOf(reply.unread),
            pending: listOf(reply.pending),
            pendingUnread: listOf(reply.pendingUnread),
            teams: null,
            lastSignIn: null,
            changed: false,
        },
    ];
}

/**
 * The account drawn wherever the page offers Reply outside the Accounts page: the default, else the profile `default`;
 * from a desktop of an earlier version, its one. Null where there is none to draw.
 */
function defaultAccount(state: DesktopSetup): AccountView | null {
    const views = accountViews(state);
    return (
        views.find((each) => each.isDefault) ??
        views.find((each) => each.ref === null || each.ref.name === 'default') ??
        null
    );
}

/** The Accounts page's place of an account's card: its own, so a dialog asked on one card never shows on another. */
const accountPlace = (view: AccountView): Place =>
    view.ref === null ? accountsPlace : `${accountsPlace}:${JSON.stringify(view.ref.name)}`;

/** The account a card at `place` is: on the Accounts page, the one its place names; anywhere else, the default. */
function accountAt(place: Place, state: DesktopSetup | null = setup.value): AccountView | null {
    if (state === null) return null;
    if (place === accountsPlace || place.startsWith(`${accountsPlace}:`))
        return accountViews(state).find((each) => accountPlace(each) === place) ?? null;
    return defaultAccount(state);
}

/** How an account is told from the others, an earlier desktop's one account included. */
const accountKey = (view: AccountView): string =>
    view.ref === null ? '' : JSON.stringify([view.ref.type, view.ref.name]);

/** An account's sign-in as a read shows it: signed out, or signed in and as whom; null where it could not be read. */
const signInOf = (view: AccountView): string | null =>
    view.authUnread ? null : view.signedIn ? `in ${view.user ?? ''}` : 'out';

/** Whether the desktop has Make default, which one of an earlier version does not: asked before it is offered. */
const makesDefault = (): boolean => mounted() && typeof earlier(here().desktop).setup?.makeDefault === 'function';

/** Whether the desktop can pin a team on a profile, which one of an earlier version cannot: asked before a choice is offered. */
const choosesTeam = (): boolean => mounted() && typeof earlier(here().desktop).setup?.chooseTeam === 'function';

/** Whether the desktop has Add Reply account, Sign out and Remove, which one of an earlier version does not. */
const addsAccount = (): boolean => mounted() && typeof earlier(here().desktop).setup?.addAccount === 'function';
const signsOut = (): boolean => mounted() && typeof earlier(here().desktop).setup?.signOut === 'function';
const removesAccount = (): boolean => mounted() && typeof earlier(here().desktop).setup?.removeAccount === 'function';
const keepsChange = (): boolean => mounted() && typeof earlier(here().desktop).setup?.keepChange === 'function';

/** Whether the Reply CLI is found, at the version the acts need and of a version that was read. */
const usableCli = (reply: DesktopSetup['reply']): boolean =>
    reply.cli.found && typeof reply.belowMinimum !== 'string' && !listOf(reply.notRead).includes('version');

/** The account a card's act names, after the act's own arguments; none for an earlier desktop's one account. */
const named = (view: AccountView): [] | [DesktopAccountRef] => (view.ref === null ? [] : [view.ref]);

/** A team as the card names it: by the name Reply gives it, else by its id. */
const teamWords = (team: DesktopReplyTeam): string => (team.name === '' ? `team ${team.id}` : team.name);

/** A sign-in as the card says it: the login, and the team it is bound to. */
const signInWords = (signIn: DesktopReplySignIn): Html =>
    html`<b>${signIn.user}</b> in <b>${teamWords(signIn.team)}</b>`;

/**
 * What the desktop's note of an account says: on a card not signed in, who it was last signed in as; on one the last read
 * found as another login or team, what the account was and what it is now, and that campaigns sending through it now send
 * as the new one, with Keep it and Sign out.
 */
function noteView(view: AccountView, place: Place, real: Html | null): Html | null {
    const last = view.lastSignIn;
    if (last === null || view.authUnread) return null;
    if (!view.signedIn)
        return html`<div class="setrow meta acct-last">
            <div class="grow">Last signed in as ${signInWords(last)}.</div>
        </div>`;
    if (!view.changed || view.user === null || view.team === null || !keepsChange()) return null;
    return html`<div class="note am acct-changed" role="note">
        <div>
            This Reply account was ${signInWords(last)}, and is now
            ${signInWords({ user: view.user, team: view.team })}: campaigns sending through it now send as the new one.
        </div>
        <div class="setrow">
            <button class="btn sm" type="button" ${real} ${act('connect.keepChange', place)}>Keep it</button>
            ${signsOut() ? html`<button class="btn sm" type="button" ${real} ${act('connect.signOut', place)}>Sign out</button>` : null}
        </div>
    </div>`;
}

/**
 * The one line for each read the desktop did not wholly make, from its lists of closed words: the teams of the sign-in
 * that could not be read, the channels the CLI says are not in the plan or role, the channels that could not be read,
 * then the listed channels whose links still waiting could not be read. A channel read and empty has none: an error is
 * said, an empty state is not. Only the card's own sentences, never the CLI's.
 */
const unlisted = (view: AccountView): string[] => [
    ...(view.signedIn && view.teamsUnread ? ['The teams of this sign-in could not be read. Check again.'] : []),
    ...view.absent.map((channel) => `This Reply account has no ${channel === 'linkedin' ? 'LinkedIn' : 'email'}.`),
    ...view.unread.map(
        (channel) => `${channel === 'linkedin' ? 'LinkedIn accounts' : 'Mailboxes'} could not be read. Check again.`,
    ),
    ...view.pendingUnread.map(
        (channel) =>
            `${channel === 'linkedin' ? 'LinkedIn' : 'Email'} links still waiting could not be read. Check again.`,
    ),
];

function stateOf(sender: DesktopReplySender): readonly [string, Tone] {
    if (!sender.status) return ['State unknown', 'gray'];
    const known = Object.hasOwn(states, sender.channel) ? states[sender.channel]! : {};
    return Object.hasOwn(known, sender.status) ? known[sender.status]! : [cap(sender.status), 'gray'];
}

/**
 * The accounts by channel: the known channels in their order, then any other under its own name, as it first comes;
 * each with the links made for it and not used yet, which make its heading even with no sender.
 */
function grouped(
    senders: readonly DesktopReplySender[],
    pending: readonly DesktopReplyPending[],
): [string, string, DesktopReplySender[], readonly DesktopReplyPending[]][] {
    const others = [...new Set(senders.map((each) => each.channel))].filter(
        (channel) => !channels.some(([known]) => known === channel),
    );
    return [...channels, ...others.map((channel) => [channel, cap(channel)] as const)]
        .map(([channel, heading]): [string, string, DesktopReplySender[], readonly DesktopReplyPending[]] => [
            channel,
            heading,
            senders.filter((each) => each.channel === channel),
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

const needsReconnect = (sender: DesktopReplySender): boolean =>
    Object.hasOwn(good, sender.channel) && !good[sender.channel]!.includes(sender.status);

// ---- The acts: a verb of the Reply CLI each, which the desktop runs; Make default asks Jason's runtime -----------------
//
// A link the Reply CLI makes for connecting or reconnecting a sender goes from the desktop straight to the person's
// browser: the card learns only that it was opened, when it stops working and its pending entry. A limit's change, a
// pause, a resume, an unblock and a delete ask the person first in a box of the desktop's own, as do a forced delete, a
// sign-out and a removal; the card can only ask for the act.

/**
 * Which of the cards a dialog or a choice belongs to: the Connect panel's, a chat's, or an account's on the Accounts page.
 */
type Place = string;

type Channel = DesktopReplyChannel;

/** What the card may do next after a refusal offered it: the same ask again, forced. */
type Again = () => Promise<DesktopReplyOpened | DesktopReplyCopied | DesktopReplyDone | DesktopReplyRefusal>;

/** A link act's answer: opened in the browser, copied for the person to hand on, or refused. */
type LinkAnswer = DesktopReplyOpened | DesktopReplyCopied | DesktopReplyRefusal;

interface ActDialog {
    place: Place;
    title: string;
    asking: boolean;
    /** What the card says while the desktop has not answered. */
    waiting: string;
    opened: DesktopReplyOpened | null;
    /** A link the desktop put on the clipboard for the person to hand on; the card never holds the link itself. */
    copied: DesktopReplyCopied | null;
    done: boolean;
    refused: DesktopReplyRefusal | null;
    /** The sender the act was on, for a Reconnect a signed-out refusal offers; null for a new one. */
    sender: { channel: Channel; id: string } | null;
    /** The ask the refusal's offer makes: the forced delete; null for none. */
    again: Again | null;
    /** Whether it goes with the sign-in it was asked under, as an act on the account's rows does; Make default's does not. */
    signedIn: boolean;
}

const actDialog = signal<ActDialog | null>(null);
/** The place whose Add account choices are open; null when none is. */
const adding = signal<Place | null>(null);
/** The place whose LinkedIn name field is open; null when none is. */
const naming = signal<Place | null>(null);
/** The mailbox whose daily limit is shown for a change, at its place; null when none is. */
const limiting = signal<{ place: Place; id: string; daily: number | null } | null>(null);
/** Whether a new link is copied to hand on instead of opened here. */
const copying = signal(false);
/** The LinkedIn accounts the CLI said are signed out, by id: Pause and Resume are not offered while that stands. */
const signedOut = signal<readonly string[]>([]);
/**
 * A sign-in's or a key's refusal, said on the card it was asked on while that card draws the account it was said of,
 * until the next act, or a read that shows that account's sign-in changed; null when none is. `read` is the number of
 * reads begun when it was said, and `signIn` the account's sign-in as the first read begun after it showed it, since the
 * act may have signed it in before it was refused.
 */
const cardRefused = signal<{ place: Place; account: string; said: string; read: number; signIn?: string } | null>(null);
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
    if (cardRefused.peek()?.place === place) cardRefused.value = null;
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
    cardRefused.value = null;
    addingAccount.value = { asking: false, refused: null };
}

/** The card's account, and the sender of it a control names by channel and id, as the last read listed them. */
function listedSender(
    place: unknown,
    channel: unknown,
    id: unknown,
): { view: AccountView; sender: DesktopReplySender } | null {
    const view = typeof place === 'string' ? accountAt(place) : null;
    const sender = view?.senders.find((each) => each.channel === channel && each.id === id);
    return view && sender ? { view, sender } : null;
}

const senderTitle = (sender: DesktopReplySender): string =>
    cap(sender.name || sender.address || (sender.channel === 'linkedin' ? 'the LinkedIn account' : 'the mailbox'));

/** What a link connects, by its channel: a mailbox or a LinkedIn account. */
const linkedSender = (channel: DesktopReplyChannel): string =>
    channel === 'linkedin' ? 'LinkedIn account' : 'mailbox';

/**
 * Opens a dialog that asks the desktop for one act and says how it ended. A refusal that says the list may have changed
 * reads it again, as does an act done; nothing is ever repeated without the person's click.
 */
async function ask(
    place: Place,
    title: string,
    sender: ActDialog['sender'],
    run: () => Promise<
        DesktopReplyOpened | DesktopReplyCopied | DesktopReplyDone | DesktopReplyRefusal | { daily: number | null }
    >,
    again: (refusal: DesktopReplyRefusal) => Again | null = () => null,
    signedIn = true,
    waiting = 'Asking Reply…',
): Promise<void> {
    asked += 1;
    const mine = asked;
    adding.value = null;
    naming.value = null;
    limiting.value = null;
    cardRefused.value = null;
    actDialog.value = {
        place,
        title,
        asking: true,
        waiting,
        opened: null,
        copied: null,
        done: false,
        refused: null,
        sender,
        again: null,
        signedIn,
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
        if (answer.offer === 'reconnect' && sender !== null && !signedOut.value.includes(sender.id))
            signedOut.value = [...signedOut.value, sender.id];
        actDialog.value = { ...actDialog.value, asking: false, refused: answer, again: again(answer) };
        if (answer.reread) void reload();
        return;
    }
    if ('daily' in answer) {
        // A limit read: shown in its field for a change, and the dialog that read it closed.
        actDialog.value = null;
        if (sender !== null) {
            typed.set(`limit:${sender.id}`, answer.daily === null ? '' : String(answer.daily));
            limiting.value = { place, id: sender.id, daily: answer.daily };
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

/** A link act: connect or reconnect, copied instead of opened where the person asked so. */
function askLink(
    place: Place,
    title: string,
    sender: ActDialog['sender'],
    run: (how: { copy?: true }) => Promise<LinkAnswer>,
): Promise<void> {
    const copy = copying.value;
    return ask(place, title, sender, () => run(copy ? { copy } : {}));
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
    const view = typeof place === 'string' ? accountAt(place) : null;
    if (typeof place !== 'string' || !view || !isProvider(provider)) return;
    const name = mailboxProviders.find(([each]) => each === provider)![1];
    void askLink(place, `Connect a ${name} mailbox`, null, (how) =>
        here().desktop.setup.replyConnect('email', { provider, ...how }, ...named(view)),
    );
});

defineReal('connect.addLinkedIn', ([place]) => {
    if (typeof place === 'string') naming.value = place;
});

defineReal('connect.makeLinkedIn', ([place]) => {
    const name = typedOf('linkedin-name');
    const view = typeof place === 'string' ? accountAt(place) : null;
    if (typeof place !== 'string' || !view || !name) return;
    const copy = copying.value;
    void ask(place, 'Connect a LinkedIn account', null, async () => {
        const answer = await here().desktop.setup.replyConnect(
            'linkedin',
            { name, ...(copy ? { copy } : {}) },
            ...named(view),
        );
        if (!('refused' in answer)) forget('linkedin-name');
        return answer;
    });
});

defineReal('connect.reconnect', ([place, channel, id]) => {
    const on = listedSender(place, channel, id);
    if (typeof place !== 'string' || !isChannel(channel) || !on) return;
    const { view, sender } = on;
    signedOut.value = signedOut.value.filter((each) => each !== sender.id);
    void askLink(place, `Reconnect ${senderTitle(sender)}`, { channel, id: sender.id }, (how) =>
        here().desktop.setup.replyReconnect(channel, sender.id, how.copy ? how : undefined, ...named(view)),
    );
});

defineReal('connect.pause', ([place, id]) => {
    const on = listedSender(place, 'linkedin', id);
    if (typeof place === 'string' && on)
        void ask(place, `Pause ${senderTitle(on.sender)}`, { channel: 'linkedin', id: on.sender.id }, () =>
            here().desktop.setup.replyPause(on.sender.id, ...named(on.view)),
        );
});

defineReal('connect.resume', ([place, id]) => {
    const on = listedSender(place, 'linkedin', id);
    if (typeof place === 'string' && on)
        void ask(place, `Resume ${senderTitle(on.sender)}`, { channel: 'linkedin', id: on.sender.id }, () =>
            here().desktop.setup.replyResume(on.sender.id, ...named(on.view)),
        );
});

defineReal('connect.unblock', ([place, id]) => {
    const on = listedSender(place, 'email', id);
    if (typeof place === 'string' && on)
        void ask(place, `Unblock ${senderTitle(on.sender)}`, { channel: 'email', id: on.sender.id }, () =>
            here().desktop.setup.replyUnblock(on.sender.id, ...named(on.view)),
        );
});

defineReal('connect.delete', ([place, channel, id]) => {
    const on = listedSender(place, channel, id);
    if (typeof place !== 'string' || !isChannel(channel) || !on) return;
    const { view, sender } = on;
    void ask(
        place,
        `Delete ${senderTitle(sender)}`,
        { channel, id: sender.id },
        () => here().desktop.setup.replyDelete(channel, sender.id, undefined, ...named(view)),
        (refusal) =>
            refusal.offer === 'force'
                ? () => here().desktop.setup.replyDelete(channel, sender.id, { force: true }, ...named(view))
                : null,
    );
});

defineReal('connect.limits', ([place, id]) => {
    const on = listedSender(place, 'email', id);
    if (typeof place !== 'string' || !on) return;
    if (limiting.value?.place === place && limiting.value.id === on.sender.id) {
        limiting.value = null;
        return;
    }
    void ask(place, `Daily limit of ${senderTitle(on.sender)}`, { channel: 'email', id: on.sender.id }, () =>
        here().desktop.setup.replyLimits(on.sender.id, undefined, ...named(on.view)),
    );
});

defineReal('connect.setLimit', ([place, id]) => {
    const on = listedSender(place, 'email', id);
    const text = typedOf(`limit:${typeof id === 'string' ? id : ''}`);
    if (typeof place !== 'string' || !on || !/^\d{1,10}$/.test(text)) return;
    void ask(place, `Daily limit of ${senderTitle(on.sender)}`, { channel: 'email', id: on.sender.id }, () =>
        here()
            .desktop.setup.replyLimits(on.sender.id, Number(text), ...named(on.view))
            .then((answer) => ('refused' in answer ? answer : { done: true as const })),
    );
});

defineReal('connect.again', () => {
    const shown = actDialog.value;
    if (shown?.again != null) void ask(shown.place, shown.title, shown.sender, shown.again);
});

// A team for an account whose sign-in belongs to several and whose profile pins none: one the desktop offered, pinned on
// the profile in the Reply CLI with no question, since nothing is lost by it; the card reads as that team after.
defineReal('connect.chooseTeam', ([place, teamId]) => {
    const view = typeof place === 'string' ? accountAt(place) : null;
    if (typeof place !== 'string' || view?.ref == null || !choosesTeam()) return;
    if (typeof teamId !== 'string' || !(view.teams ?? []).some((team) => team.id === teamId)) return;
    const ref = view.ref;
    void ask(place, 'Choose a team', null, () => here().desktop.setup.chooseTeam(ref, teamId));
});

// Make default, offered only while Jason has no default, and said on the card as it ended. It asks Jason's runtime, never
// Reply, so meanwhile the card says the default is being made.
defineReal('connect.makeDefault', ([place]) => {
    const view = typeof place === 'string' ? accountAt(place) : null;
    if (typeof place !== 'string' || view?.ref == null || !makesDefault()) return;
    const ref = view.ref;
    void ask(
        place,
        'Make default',
        null,
        () => here().desktop.setup.makeDefault(ref),
        () => null,
        false,
        'Making it the default…',
    );
});

// Sign out and Remove, said on the card as they ended. Each asks the person in the desktop's own box first, so meanwhile the
// card says what it waits on.
defineReal('connect.signOut', ([place]) => {
    const view = typeof place === 'string' ? accountAt(place) : null;
    if (typeof place !== 'string' || view?.ref == null || !signsOut()) return;
    const ref = view.ref;
    void ask(
        place,
        'Sign out',
        null,
        () => here().desktop.setup.signOut(ref),
        () => null,
        false,
        'Signing it out…',
    );
});

defineReal('connect.removeAccount', ([place]) => {
    const view = typeof place === 'string' ? accountAt(place) : null;
    if (typeof place !== 'string' || view?.ref == null || !removesAccount()) return;
    const ref = view.ref;
    void ask(
        place,
        'Remove',
        null,
        () => here().desktop.setup.removeAccount(ref),
        () => null,
        false,
        'Removing it…',
    );
});

// Keep it: the account as it now is becomes the one the desktop's note keeps, and the card no longer says the change.
defineReal('connect.keepChange', ([place]) => {
    const view = typeof place === 'string' ? accountAt(place) : null;
    if (typeof place !== 'string' || view?.ref == null || !keepsChange()) return;
    const ref = view.ref;
    void ask(
        place,
        'Keep it',
        null,
        () => here().desktop.setup.keepChange(ref),
        () => null,
        true,
        'Keeping it…',
    );
});

/** Where the name of a new Reply account is kept until the desktop takes it. */
const accountNameField = 'reply-account-name';

/** Add Reply account's ask: whether one is going, and what the desktop refused the last one with, said under its field. */
const addingAccount = signal<{ asking: boolean; refused: string | null }>({ asking: false, refused: null });

// Add Reply account: the name typed, trimmed, asked of the desktop; its card is the next read's, and a refusal is said under
// the field, which keeps the name to be corrected.
defineReal('connect.addReplyAccount', () => {
    const name = typedOf(accountNameField);
    if (name === '' || !addsAccount() || addingAccount.value.asking) return;
    addingAccount.value = { asking: true, refused: null };
    void here()
        .desktop.setup.addAccount(name)
        .catch((error: unknown): DesktopReplyRefusal => ({
            refused: messageOf(error),
            code: null,
            offer: null,
            reread: false,
        }))
        .then((answer) => {
            if ('refused' in answer) {
                addingAccount.value = { asking: false, refused: answer.refused };
                if (answer.reread) void reload();
                return;
            }
            forget(accountNameField);
            addingAccount.value = { asking: false, refused: null };
            void reload();
        });
});

define('connect.closeLink', () => closeDialog());
define('connect.checkNow', () => void reload());

// What a card holds open goes with the sign-in it was asked under, and with the card: Make default's dialog stays while
// the account is drawn, signed in or not.
effect(() => {
    const state = setup.value;
    if (state === null) return;
    const gone = (place: Place) => accountAt(place, state) === null;
    const signedOutAt = (place: Place) => !(accountAt(place, state)?.signedIn ?? false);
    const shown = actDialog.peek();
    if (shown !== null && (gone(shown.place) || (shown.signedIn && signedOutAt(shown.place)))) closeDialog();
    if (adding.peek() !== null && signedOutAt(adding.peek()!)) adding.value = null;
    if (naming.peek() !== null && signedOutAt(naming.peek()!)) naming.value = null;
    if (limiting.peek() !== null && signedOutAt(limiting.peek()!.place)) limiting.value = null;
    const refused = cardRefused.peek();
    if (refused === null) return;
    const view = accountAt(refused.place, state);
    if (view === null || accountKey(view) !== refused.account) cardRefused.value = null;
    else if (shownRead > refused.read) {
        const signIn = signInOf(view);
        if (signIn === null) return;
        if (refused.signIn === undefined) cardRefused.value = { ...refused, signIn };
        else if (refused.signIn !== signIn) cardRefused.value = null;
    }
});

/** A moment Reply wrote, as a person reads it: `2026-11-19 04:59 UTC`; null for one that is not a moment. */
function momentOf(text: string | null): string | null {
    if (text === null) return null;
    const at = new Date(/(?:Z|[+-]\d{2}:\d{2})$/.test(text) ? text : `${text}Z`);
    return Number.isNaN(at.getTime()) ? null : `${at.toISOString().slice(0, 16).replace('T', ' ')} UTC`;
}

const offers: Record<NonNullable<DesktopReplyRefusal['offer']>, string> = {
    force: 'Delete anyway…',
    reconnect: 'Reconnect',
};

function dialogView(shown: ActDialog, busy: Html | null): Html {
    const opened = shown.opened;
    const until = momentOf(opened?.expiresAt ?? null);
    const offer = shown.refused?.offer ?? null;
    return html`<div class="note act-dialog" role="dialog" aria-label="${shown.title}">
        <b>${shown.title}</b>
        ${shown.asking ? html`<div class="meta" role="status"><span class="spin"></span>${shown.waiting}</div>` : null}
        ${shown.refused !== null ? html`<div class="note am" role="alert">${shown.refused.refused}</div>` : null}
        ${
            offer === 'reconnect' && shown.sender !== null
                ? html`<div class="setrow">
                      <button
                          class="btn sm"
                          type="button"
                          ${busy}
                          ${act('connect.reconnect', shown.place, shown.sender.channel, shown.sender.id)}
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
                          It connects one ${linkedSender(opened.channel)} until ${until ?? 'it expires'}.
                      </p>
                      <div class="setrow">
                          <div class="grow meta">
                              The ${linkedSender(opened.channel)} appears in the list when the sign-in there is
                              finished.
                          </div>
                          <button class="btn sm" type="button" ${act('connect.checkNow')}>Check now</button>
                      </div>`
                : null
        }
        ${
            shown.copied !== null
                ? html`<div class="meta" role="status">The link was copied.</div>
                      <p class="meta link-note">
                          Hand it to whoever owns the ${linkedSender(shown.copied.channel)}: it connects one
                          ${linkedSender(shown.copied.channel)} to this Reply account until
                          ${momentOf(shown.copied.expiresAt) ?? 'it expires'}, so send it to no one else.
                      </p>
                      <div class="setrow">
                          <div class="grow meta">
                              The ${linkedSender(shown.copied.channel)} appears in the list when they finish.
                          </div>
                          <button class="btn sm" type="button" ${act('connect.checkNow')}>Check now</button>
                      </div>`
                : null
        }
        ${shown.done ? html`<div class="meta" role="status">Done.</div>` : null}
        <div class="setrow"><button class="btn sm" type="button" ${act('connect.closeLink')}>Close</button></div>
    </div>`;
}

/** Add account's choices: Gmail and Outlook where email is not absent, LinkedIn where it is not, and Cancel. */
function addView(view: AccountView, place: Place, busy: Html | null): Html {
    const absent = view.absent;
    return html`<div class="setrow add-account">
            ${
                absent.includes('email')
                    ? null
                    : mailboxProviders.map(
                          ([provider, name]) =>
                              html`<button
                                  class="btn sm"
                                  type="button"
                                  ${busy}
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
                      <button class="btn sm" type="button" ${busy} ${act('connect.makeLinkedIn', place)}>
                          Make the link
                      </button>
                  </div>`
                : null
        }`;
}

/** The acts a sender's row offers, by its channel and state; Pause and Resume not while it is signed out. */
function rowActs(sender: DesktopReplySender, place: Place, busy: Html | null): Html[] {
    const button = (label: string, name: string, ...args: string[]) =>
        html`<button class="btn sm" type="button" ${busy} ${act(name, place, ...args)}>${label}</button>`;
    const acts: Html[] = [];
    if (needsReconnect(sender)) acts.push(button('Reconnect', 'connect.reconnect', sender.channel, sender.id));
    if (sender.channel === 'email') {
        acts.push(button('Limits', 'connect.limits', sender.id), button('Unblock', 'connect.unblock', sender.id));
    } else if (
        sender.channel === 'linkedin' &&
        sender.status !== 'cookieInvalid' &&
        !signedOut.value.includes(sender.id)
    ) {
        // Reply's one call flips the state, so Pause is offered only from enabled and Resume only from disabled; a
        // LinkedIn account at its daily limit is on and offers neither.
        if (sender.status === 'disabled') acts.push(button('Resume', 'connect.resume', sender.id));
        else if (sender.status === 'enabled') acts.push(button('Pause', 'connect.pause', sender.id));
    }
    if (sender.channel === 'email' || sender.channel === 'linkedin')
        acts.push(button('Delete', 'connect.delete', sender.channel, sender.id));
    return acts;
}

/** A mailbox's daily limit, open for a change: the field, and Set, which the desktop asks the person about first. */
function limitView(sender: DesktopReplySender, place: Place, busy: Html | null): Html | null {
    const open = limiting.value;
    if (open === null || open.place !== place || open.id !== sender.id) return null;
    return html`<div class="setrow acct-limit">
        <div class="grow meta">Emails a day${open.daily === null ? '' : `, now ${open.daily}`}:</div>
        ${field(`limit:${sender.id}`, 'Daily limit', 'text')}
        <button class="btn sm" type="button" ${busy} ${act('connect.setLimit', place, sender.id)}>Set</button>
    </div>`;
}

function pendingView(row: DesktopReplyPending): Html {
    const until = momentOf(row.expiresAt);
    return html`<div class="setrow acct-pending">
        <div class="grow">
            <b>${cap(row.name || (row.kind === 'reconnect' ? 'A reconnect' : `A new ${linkedSender(row.channel)}`))}</b>
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
/** How many reads have begun, and which of them gave the state the page shows. */
let begun = 0;
let shownRead = 0;
/** Whether the error the page says is a read's that failed, which the next read that succeeds takes away; an act's stays. */
let readSaid = false;

/** The window taking the focus back reads again no more often than this. */
const focusReadSpacingMs = 30_000;

export async function reload(): Promise<void> {
    reading += 1;
    lastRead = Date.now();
    begun += 1;
    const read = begun;
    try {
        const status = await here().desktop.setup.status();
        shownRead = read;
        setup.value = { ...status, hosts: withModels(status.hosts) };
        setupReadFailed.value = false;
        if (readSaid) setupError.value = null;
        readSaid = false;
    } catch (error) {
        setupError.value = messageOf(error);
        setupReadFailed.value = true;
        readSaid = true;
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

/**
 * Runs an act, with its busy line. Its refusal is said on the card at `place` of the account it was asked as where it has
 * one (a sign-in, a key), else in the error line of the section or panel the act is on.
 */
async function doing(
    what: string,
    run: () => Promise<void>,
    card: { place: Place; view: AccountView } | null = null,
): Promise<void> {
    if (setupBusy.value !== null) return;
    setupBusy.value = what;
    setupError.value = null;
    readSaid = false;
    cardRefused.value = null;
    try {
        await run();
    } catch (error) {
        if (card === null) setupError.value = messageOf(error);
        else
            cardRefused.value = {
                place: card.place,
                account: accountKey(card.view),
                said: messageOf(error),
                read: begun,
            };
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
    // The Reply CLI is the Reply account's program: placing or updating it is off in the demo, an AI's program is not.
    if (inDemo() && program === setup.value?.reply.cli.install) return;
    if (typeof program === 'string')
        void doing(`Installing ${program}…`, () => here().desktop.setup.installProgram(program));
});

// A sign-in is the card's account's: the desktop makes its profile in the Reply CLI where only a route names it, and makes
// it the default where none is set.
defineReal('connect.reply', ([place]) => {
    const view = typeof place === 'string' ? accountAt(place) : null;
    if (view)
        void doing('Signing in to Reply…', () => here().desktop.setup.connectReply(...named(view)), {
            place: place as Place,
            view,
        });
});

/** Where a card's typed key is kept until the desktop takes it: one per card. */
const keyField = (place: Place): string => `reply:${place}`;

defineReal('connect.replyKey', ([place]) => {
    const view = typeof place === 'string' ? accountAt(place) : null;
    const key = typeof place === 'string' ? typedOf(keyField(place)) : '';
    if (view && key)
        void doing(
            'Connecting Reply with the key…',
            async () => {
                await here().desktop.setup.connectReplyWithKey(key, ...named(view));
                forget(keyField(place as string));
            },
            { place: place as Place, view },
        );
});

define('connect.providerKey', ([provider, remove]) => {
    if (typeof provider !== 'string') return;
    const key = remove === true ? '' : typedOf(`provider:${provider}`);
    if (key || remove === true)
        void doing(remove === true ? 'Removing the key…' : 'Saving the key…', async () => {
            await here().desktop.setup.setProviderKey(provider, key);
            forget(`provider:${provider}`);
        });
});

defineReal('connect.plugin', ([id]) => {
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

/** How many of a host's own MCP servers its line names before it counts the rest. */
const ownServersShown = 5;

/**
 * The one line under Claude Code or Codex naming the person's own MCP servers, as the desktop read them from that
 * program's own settings: the first five in the file's order, then how many more, and where they work: in the chat in
 * Full access, the one mode whose turns load the person's own configuration on either program, and when Jason is opened
 * in that program from Open in. Nothing for a host with none, a host not found, or a file the desktop could not read.
 * The names are the person's own text, drawn as text and nothing else.
 */
export function ownServersLine(host: DesktopHost): string | null {
    const names = Array.isArray(host.ownServers) ? host.ownServers : [];
    if (host.kind !== 'program' || !host.found || names.length === 0) return null;
    const first = names.slice(0, ownServersShown).join(', ');
    const more = names.length - ownServersShown;
    return t('connect.ownServers', {
        names: more > 0 ? t('connect.ownServers.more', { names: first, count: String(more) }) : first,
        host: host.label,
    });
}

/**
 * The one line under the program the agents' roles run on by default, where the chat can use it — it drives the chat, is
 * found here and is at its version floor: the host and the profile's name, as the desktop read them from the
 * installation's settings and the runtime. Nothing for any other host.
 */
export function runsRolesLine(host: DesktopHost): string | null {
    if (
        host.kind !== 'program' ||
        !host.found ||
        !host.drivesChat ||
        host.belowFloor !== null ||
        typeof host.runsRoles !== 'string'
    )
        return null;
    return t('connect.runsRoles', { host: host.label, profile: host.runsRoles });
}

/**
 * Whether a host is drawn as a card of its own: found here, or carrying a sentence about its version. Every other host is
 * named on the one line of the hosts not found.
 */
export const hostRow = (host: DesktopHost): boolean => host.found || host.belowFloor !== null;

/**
 * Whether a program is the one in use: the one the agents' roles run on by default, the fact its runs-roles line says.
 * At most one program answers it, since the default profile is on one program.
 */
export const inUse = (host: DesktopHost): boolean => runsRolesLine(host) !== null;

/**
 * Whether a program's card offers Use: a program found here that could carry "In use" — it drives the chat and is at its
 * version floor, as the runs-roles line needs — and does not carry it now. Never a key provider, which runs no role.
 */
export const usable = (host: DesktopHost): boolean =>
    host.kind === 'program' && host.found && host.drivesChat && host.belowFloor === null && !inUse(host);

/** The program a person last put the agents' roles on with Use, while the page is open; null before any. */
const used = signal<string | null>(null);

/**
 * The line a program's card says after a person's Use, while the card reads that program in use again: what the act did
 * not change. What it changed, that the roles run on this program, is the runs-roles line the card already draws, since
 * In use and that line are one fact, so it is never said twice.
 */
export function usedLine(host: DesktopHost): string | null {
    return used.value === host.id && inUse(host) ? t('connect.used') : null;
}

define('connect.use', ([id]) => {
    if (typeof id !== 'string') return;
    const host = setup.value?.hosts.find((each) => each.id === id);
    if (host === undefined || !usable(host)) return;
    used.value = null;
    void doing(t('connect.using', { host: host.label }), async () => {
        await here().desktop.setup.useProgram(id);
        used.value = id;
    });
});

/** A program found here: a card, its name, its version as its state and "In use" on one line, its notes under it. */
function program(host: DesktopHost, busy: Html | null): Html {
    const sentence = signInSentence(host);
    const own = ownServersLine(host);
    const roles = runsRolesLine(host);
    const after = usedLine(host);
    const state = host.found ? (host.version ?? 'Found') : 'Not found';
    const tone = host.belowFloor !== null ? 'amber' : host.found ? 'green' : 'gray';
    const how = host.belowFloor !== null || sentence !== null || own !== null || roles !== null || after !== null;
    return html`<div class="setrow host-row host-card">
        <div class="grow">
            <div class="row-hd">
                <b>${host.label}</b> <span class="pillc ${tone} host-state">${state}</span>
                ${inUse(host) ? html`<span class="pillc green in-use">In use</span>` : null}
                ${
                    usable(host)
                        ? html`<button class="btn sm use-program" type="button" ${busy} ${act('connect.use', host.id)}>
                              Use
                          </button>`
                        : null
                }
                ${
                    !host.found && host.install
                        ? html`<button class="btn sm" type="button" ${busy} ${act('connect.install', host.install)}>
                              Install
                          </button>`
                        : null
                }
            </div>
            ${
                host.found
                    ? html`<div class="host-does">
                          ${host.drivesChat ? 'Drives the chat' : 'Runs campaign work, not the chat yet'}
                      </div>`
                    : null
            }
            ${
                how
                    ? html`<div class="host-how">
                          ${host.belowFloor !== null ? html`<div class="meta">${withCode(host.belowFloor)}</div>` : null}
                          ${sentence ? html`<div class="meta sign-in">${withCode(sentence)}</div>` : null}
                          ${own !== null ? html`<div class="meta own-servers">${own}</div>` : null}
                          ${roles !== null ? html`<div class="meta runs-roles">${roles}</div>` : null}
                          ${after !== null ? html`<div class="meta used-program" role="status">${after}</div>` : null}
                      </div>`
                    : null
            }
        </div>
    </div>`;
}

/**
 * The hosts not found, on one quiet line that names them: those that can be installed first, each followed by its own
 * Install button, then the others.
 */
function notFound(hosts: readonly DesktopHost[], busy: Html | null): Html | null {
    if (hosts.length === 0) return null;
    const ordered = [...hosts.filter((each) => each.install), ...hosts.filter((each) => !each.install)];
    return html`<div class="meta hosts-absent">
        <span>Not found</span>:
        ${ordered.map(
            (host, at) =>
                html`${at > 0 ? html`<span class="sep">·</span>` : null}<span class="absent-host"
                        >${host.label}${
                            host.install
                                ? html` <button
                                      class="btn sm"
                                      type="button"
                                      ${busy}
                                      ${act('connect.install', host.install)}
                                  >
                                      Install
                                  </button>`
                                : null
                        }</span
                    >`,
        )}
    </div>`;
}

/** The key provider the person picked in the panel; null until they pick one. */
const chosenProvider = signal<string | null>(null);

define('connect.provider', (_args, element) => {
    if (!(element instanceof HTMLSelectElement)) return;
    // A key typed and not saved is not kept behind the person's back under the provider they leave.
    for (const key of [...typed.keys()]) if (key.startsWith('provider:')) typed.delete(key);
    chosenProvider.value = element.value;
});

/** The provider the panel shows: the one picked, else the first with no key, else the first. */
export function shownProvider(providers: readonly DesktopHost[], picked: string | null): DesktopHost | undefined {
    return providers.find((each) => each.id === picked) ?? providers.find((each) => !each.found) ?? providers[0];
}

/**
 * The key providers whose key is set, as one row of what is on this computer: "Model keys" and each such provider as a
 * pill; nothing when none is.
 */
function keysSet(providers: readonly DesktopHost[]): Html | null {
    const set = providers.filter((each) => each.found);
    if (set.length === 0) return null;
    return html`<div class="setrow host-card keys-set">
        <div class="row-hd">
            <b>Model keys</b>
            ${set.map((each) => html`<span class="pillc green key-held">${each.label}</span>`)}
        </div>
    </div>`;
}

/**
 * The model keys as one panel: a Provider select over the key providers, each with a key saying so; under it, for the
 * provider shown, the API key field and Save key when it has no key, or Key set and Remove the key when it has one.
 */
function keyPanel(providers: readonly DesktopHost[], busy: Html | null): Html | null {
    const shown = shownProvider(providers, chosenProvider.value);
    if (!shown) return null;
    const key = `provider:${shown.id}`;
    return html`<div class="key-panel">
        <div class="key-hd">
            <b>Model keys</b>
            <span class="meta">Run through Claude Code; kept in the system keychain.</span>
        </div>
        <label class="key-fld">
            <span>Provider</span>
            <select class="inp" aria-label="Provider" data-change="connect.provider">
                ${providers.map(
                    (each) =>
                        html`<option value="${each.id}" ${each.id === shown.id ? html`selected` : null}>
                            ${each.label}${each.found ? ' · key set' : ''}
                        </option>`,
                )}
            </select>
        </label>
        ${
            shown.found
                ? html`<div class="key-act" data-id="${shown.id}">
                      <span class="pillc green key-state">Key set</span>
                      <button class="btn sm" type="button" ${busy} ${act('connect.providerKey', shown.id, true)}>
                          Remove the key
                      </button>
                  </div>`
                : html`<label class="key-fld">
                          <span>API key</span>
                          <input
                              class="inp"
                              type="password"
                              autocomplete="off"
                              aria-label="API key"
                              placeholder="Enter your API key"
                              value="${typed.get(key) ?? ''}"
                              data-input="connect.type"
                              ${argsOf(key)}
                          />
                      </label>
                      <div class="key-act" data-id="${shown.id}">
                          <button class="btn p sm" type="button" ${busy} ${act('connect.providerKey', shown.id)}>
                              Save key
                          </button>
                      </div>`
        }
    </div>`;
}

/** What Reply is, as the head of the Reply section and of the one card drawn outside the Accounts page. */
const replyHead = (): Html =>
    html`<div class="reply-name"><b>Reply</b> <span class="pillc green">Recommended</span></div>
        <p class="meta reply-why">
            Reply.io is the service Jason sends through. You connect your Gmail, Outlook or other mailbox and your
            LinkedIn account to it, and Jason sends from them once you approve.
        </p>`;

/**
 * The Reply CLI's state, only where there is something to say: not found and Install, or what to install where npm is
 * missing too; what came of the update at the start; a version not read and Check again; below the minimum and Update.
 */
function cliLines(reply: DesktopSetup['reply'], real: Html | null): Html {
    const below = typeof reply.belowMinimum === 'string' ? reply.belowMinimum : null;
    const notRead = listOf(reply.notRead);
    const missing = reply.cli.found
        ? null
        : reply.cli.install
          ? html`<span class="meta">The Reply CLI was not found.</span>
                <button class="btn sm" type="button" ${real} ${act('connect.install', reply.cli.install)}>
                    Install
                </button>`
          : // Nothing installs it from here where npm is missing too: what to install, and from where, is said instead.
            html`<div class="meta">
                The Reply CLI was not found. It is a Node.js program installed with npm, and npm was not found on this
                computer. Install Node.js from nodejs.org, which brings npm, then quit Jason and open it again: this
                card will offer to install the Reply CLI.
            </div>`;
    // What came of the update the desktop made at its start: its own sentence, and npm's words in it, as text.
    const startUpdate = typeof reply.startUpdate === 'string' ? reply.startUpdate : null;
    return html`${missing === null ? null : html`<div class="setrow"><div class="grow">${missing}</div></div>`}
    ${startUpdate === null ? null : html`<div class="setrow start-update" role="note"><div class="grow meta">${startUpdate}</div></div>`}
    ${
        reply.cli.found && notRead.includes('version')
            ? html`<div class="setrow version-unread">
                  <div class="grow meta">The Reply CLI’s version could not be read.</div>
                  <button class="btn sm" type="button" ${act('connect.check')}>Check again</button>
              </div>`
            : reply.cli.found && below !== null
              ? html`<div class="setrow below-minimum">
                    <div class="grow meta">${below}</div>
                    ${
                        reply.cli.install
                            ? html`<button
                                  class="btn sm"
                                  type="button"
                                  ${real}
                                  ${act('connect.install', reply.cli.install)}
                              >
                                  Update
                              </button>`
                            : null
                    }
                </div>`
              : null
    }`;
}

/**
 * What of the accounts as a whole the desktop could not read, said as that and never as a state; and that no account is
 * the default yet where the routes were read and name none: from a desktop of this version alone.
 */
function accountsLines(state: DesktopSetup): Html | null {
    if (!accountsSent(state)) return null;
    const notRead = listOf(state.accounts!.notRead);
    return html`${
        notRead.includes('profiles')
            ? html`<div class="setrow accounts-unread">
                  <div class="grow">The Reply accounts could not be read.</div>
                  <button class="btn sm" type="button" ${act('connect.check')}>Check again</button>
              </div>`
            : null
    }
    ${notRead.includes('routes') ? html`<div class="setrow meta default-unread">Which Reply account is the default could not be read.</div>` : null}
    ${
        state.accounts!.noDefault
            ? html`<div class="setrow meta no-default" role="note">
                  No Reply account is the default yet. Campaigns have nothing to send through until you make one the
                  default.
              </div>`
            : null
    }`;
}

/** Where what the card does not do is managed. */
const manageInReply = (): Html =>
    html`<div class="setrow">
        <div class="grow meta">
            Other mailboxes (SMTP/IMAP), signatures and a LinkedIn account’s limits are set in Reply.
            <a href="${replyAccountsAddress}" target="_blank" rel="noreferrer"
                >Manage mailboxes and LinkedIn accounts in Reply</a
            >
        </div>
    </div>`;

/** A channel as a mark names it. */
const channelWords = (channel: string): string => (channel === 'linkedin' ? 'LinkedIn' : cap(channel));

/**
 * The mark of a global route other than the default: "Default for LinkedIn" or "Default for email" for a channel's sends,
 * else the work's name as Jason has it, with its channel where the route is one channel's.
 */
function defaultForWords(mark: DesktopDefaultFor): string {
    if (mark.kind !== 'channel') return `Default for ${cap(mark.operation)}`;
    if (mark.operation === 'message.send') return `Default for ${channelWords(mark.channel)}`;
    return `Default for ${cap(mark.operation)} on ${channelWords(mark.channel)}`;
}

/**
 * An account's head: Reply and its name exactly as the Reply CLI has it, the default mark and a mark for each other global
 * route that names it, or Make default where the desktop has it and the routes were read and name no default at all. None
 * for an earlier desktop's account, which has no name.
 */
function accountHead(state: DesktopSetup, view: AccountView, place: Place, real: Html | null): Html | null {
    if (view.ref === null) return null;
    const offered = state.accounts?.noDefault === true && makesDefault();
    return html`<div class="acct-head">
        <span class="acct-type">Reply</span> <b class="acct-name">${view.ref.name}</b>
        ${view.isDefault ? html`<span class="pillc green acct-default">Default</span>` : null}
        ${view.defaultFor.map((mark) => html` <span class="pillc green acct-default">${defaultForWords(mark)}</span>`)}
        ${offered ? html`<button class="btn sm" type="button" ${real} ${act('connect.makeDefault', place)}>Make default</button>` : null}
    </div>`;
}

/**
 * The teams of a sign-in that belongs to more than one, in place of its account's senders, where the profile pins none
 * and Reply would not list it without one: each by its name, its id beside it, with Choose.
 */
function teamChoice(teams: readonly DesktopReplyTeam[], place: Place, asks: Html | null): Html {
    return html`<div class="acct-group acct-teams">
        <div class="setrow meta acct-teams-why">
            This sign-in belongs to more than one Reply team. Choose the one this profile works in.
        </div>
        ${teams.map(
            (team) =>
                html`<div class="setrow acct-team">
                    <div class="grow">
                        ${team.name === '' ? null : html`<b>${team.name}</b>`} <span class="meta">team ${team.id}</span>
                    </div>
                    <button class="btn sm" type="button" ${asks} ${act('connect.chooseTeam', place, team.id)}>
                        Choose
                    </button>
                </div>`,
        )}
    </div>`;
}

/**
 * An account's body: its sign-in line, the sign-in and the key for it, its senders (its mailboxes and LinkedIn accounts)
 * with their acts, what of them could not be read, its Add account and Check again, and the dialog an act on it opened.
 */
function accountBody(reply: DesktopSetup['reply'], view: AccountView, busy: Html | null, place: Place): Html {
    const shown = actDialog.value?.place === place ? actDialog.value : null;
    // Below the Reply CLI the acts need, the card offers no act: only a desktop of an earlier version draws an account then.
    const usable = typeof reply.belowMinimum !== 'string' && !listOf(reply.notRead).includes('version');
    const offering = view.signedIn && usable;
    // A sign-in in several teams, its profile pinning none: the teams to choose from, in place of its senders and acts.
    const choosing = view.signedIn && view.teams !== null && choosesTeam();
    // Every act is off while one is being asked for.
    const asks = asking(busy);
    const real = realBusy(busy);
    const shownRefusal = cardRefused.value;
    const refused =
        shownRefusal?.place === place && shownRefusal.account === accountKey(view) ? shownRefusal.said : null;
    return html`<div class="setrow reply-state">
            ${
                view.authUnread
                    ? html`<div class="grow">The Reply sign-in could not be read.</div>
                          <button class="btn sm" type="button" ${act('connect.check')}>Check again</button>`
                    : html`<div class="grow">
                          ${
                              view.signedIn
                                  ? html`Signed in as <b>${view.user ?? ''}</b>${
                                            view.team === null ? null : html` in <b>${teamWords(view.team)}</b>`
                                        }`
                                  : 'Not signed in'
                          }
                      </div>`
            }
        </div>
        ${noteView(view, place, asks)}
        ${
            view.signedIn
                ? null
                : html`<div class="reply-key">
                          ${secret(keyField(place), 'Or paste a Reply API key')}
                          <button class="btn" type="button" ${real} ${act('connect.replyKey', place)}>Use key</button>
                      </div>
                      <div class="meta reply-key-why">For a Reply account the browser sign-in does not reach.</div>`
        }
        ${refused === null ? null : html`<div class="note am" role="alert">${refused}</div>`}
        ${choosing ? teamChoice(view.teams!, place, asks) : null}
        ${(choosing ? [] : grouped(view.senders, view.pending)).map(
            ([, heading, members, waiting]) =>
                html`<div class="acct-group">
                    <div class="setrow meta acct-hd">${heading}</div>
                    ${members.map((sender) => {
                        const [words, tone] = stateOf(sender);
                        return html`<div class="setrow acct">
                                <div class="grow">
                                    <b>${sender.name}</b> <span class="meta">${sender.address}</span>
                                </div>
                                <span class="pillc ${tone}">${words}</span>
                                ${offering ? rowActs(sender, place, asks) : null}
                            </div>
                            ${offering ? limitView(sender, place, asks) : null}`;
                    })}
                    ${waiting.map((row) => pendingView(row))}
                    ${
                        waiting.length > 0
                            ? html`<div class="setrow meta acct-pending-hint">
                                  A link here is not shown again: connect the mailbox or LinkedIn account again to make
                                  a new link.
                              </div>`
                            : null
                    }
                </div>`,
        )}
        ${(choosing ? [] : unlisted(view)).map((line) => html`<div class="setrow meta acct-none">${line}</div>`)}
        ${
            offering && !choosing
                ? html`<div class="setrow">
                          <div class="grow meta">
                              Mailboxes and LinkedIn accounts are read again when this window takes the focus back.
                          </div>
                          <button class="btn sm" type="button" ${asks} ${act('connect.addAccount', place)}>
                              Connect a mailbox or LinkedIn
                          </button>
                          <button class="btn sm" type="button" ${act('connect.check')}>Check again</button>
                      </div>
                      <label class="setrow meta link-copy">
                          <input
                              type="checkbox"
                              data-change="connect.copyLinks"
                              ${copying.value ? html`checked` : null}
                          />
                          Copy a new link to hand on to whoever owns the mailbox or LinkedIn account, instead of opening
                          it here
                      </label>
                      ${adding.value === place ? addView(view, place, asks) : null}`
                : null
        }
        ${shown !== null ? dialogView(shown, asks) : null}`;
}

/**
 * The acts on the card's account as a whole, beside its name: Sign in with Reply while it is not signed in, Sign out once it
 * is, and Remove where the Reply CLI holds its profile, it is not the Reply CLI's own `default` and no route that names no
 * profile follows it; each only where the desktop has it.
 */
function accountActs(view: AccountView, place: Place, real: Html | null): Html[] {
    const signIn = signInButton(view, place, real);
    const acts = signIn === null ? [] : [signIn];
    if (view.ref === null) return acts;
    if (view.signedIn && signsOut())
        acts.push(html`<button class="btn" type="button" ${real} ${act('connect.signOut', place)}>Sign out</button>`);
    if (view.held && !view.followed && view.ref.name !== 'default' && removesAccount())
        acts.push(
            html`<button class="btn" type="button" ${real} ${act('connect.removeAccount', place)}>Remove</button>`,
        );
    return acts;
}

/** Add Reply account, where the accounts were read: a name asked, and the desktop's refusal of it under the field. */
function addAccountView(state: DesktopSetup, real: Html | null): Html | null {
    if (
        !addsAccount() ||
        !accountsSent(state) ||
        listOf(state.accounts!.notRead).includes('profiles') ||
        !usableCli(state.reply)
    )
        return null;
    const { asking, refused } = addingAccount.value;
    return html`<div class="reply-add">
        <div class="setrow">
            ${field(accountNameField, 'Name for a new Reply account', 'text')}
            <button class="btn sm" type="button" ${asking ? html`disabled` : real} ${act('connect.addReplyAccount')}>
                Add Reply account
            </button>
        </div>
        ${refused === null ? null : html`<div class="note am" role="alert">${refused}</div>`}
    </div>`;
}

/**
 * "Sign in with Reply" for the card's account, only while it is not signed in, since another login is another Reply
 * account: the card's first act where its sign-in was read, a plain one where it could not be.
 */
const signInButton = (view: AccountView, place: Place, real: Html | null): Html | null =>
    view.signedIn
        ? null
        : html`<button
              class="${view.authUnread ? 'btn' : 'btn p'}"
              type="button"
              ${real}
              ${act('connect.reply', place)}
          >
              Sign in with Reply
          </button>`;

/**
 * The one Reply card drawn outside the Accounts page, on the Connect panel and in a chat: what Reply is, the Reply CLI's
 * state and what of the accounts could not be read, then the default account, or the profile `default` where no Reply
 * account is the default, as its card on the Accounts page draws it. With the busy line and the error where `notes`.
 */
function replyCard(
    state: DesktopSetup,
    busy: Html | null,
    label?: string,
    place: Place = 'connect',
    notes = false,
): Html {
    const view = defaultAccount(state);
    const real = realBusy(busy);
    const failure = setupError.value;
    return html`<div class="card reply-card" ${label ? html`role="region" aria-label="${label}"` : null}>
        <div class="bd">
            <div class="reply-top">
                <div class="grow">${replyHead()}</div>
                ${view === null ? null : accountActs(view, place, asking(busy))}
            </div>
            ${notInDemo()}
            ${notes && setupBusy.value ? html`<div class="note" role="status"><span class="spin"></span>${setupBusy.value}</div>` : null}
            ${notes && failure ? html`<div class="note am" role="alert">${failure}</div>` : null}
            ${cliLines(state.reply, real)} ${accountsLines(state)}
            ${view === null ? null : html`${accountHead(state, view, place, asking(busy))}${accountBody(state.reply, view, busy, place)}`}
            ${manageInReply()}
        </div>
    </div>`;
}

/** The Accounts page's Reply section: what Reply is, the CLI's state, what of the accounts could not be read. */
function replySection(state: DesktopSetup, busy: Html | null): Html {
    const failure = setupError.value;
    return html`<div class="card reply-card reply-section" role="region" aria-label="Reply">
        <div class="bd">
            <div class="reply-top"><div class="grow">${replyHead()}</div></div>
            ${notInDemo()}
            ${setupBusy.value ? html`<div class="note" role="status"><span class="spin"></span>${setupBusy.value}</div>` : null}
            ${failure ? html`<div class="note am" role="alert">${failure}</div>` : null}
            ${cliLines(state.reply, realBusy(busy))} ${accountsLines(state)} ${addAccountView(state, realBusy(busy))}
            ${manageInReply()}
        </div>
    </div>`;
}

/** One account's card on the Accounts page, at a place of its own. */
function accountCard(state: DesktopSetup, view: AccountView, busy: Html | null): Html {
    const place = accountPlace(view);
    const asks = asking(busy);
    const label = view.ref === null ? 'Reply account' : `Reply account ${view.ref.name}`;
    return html`<div class="card reply-card reply-account" role="region" aria-label="${label}">
        <div class="bd">
            <div class="reply-top">
                <div class="grow">
                    ${accountHead(state, view, place, asks) ?? html`<div class="acct-head"><span class="acct-type">Reply</span></div>`}
                </div>
                ${accountActs(view, place, asks)}
            </div>
            ${accountBody(state.reply, view, busy, place)}
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
            ${notInDemo()}
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
 * asked for, so a region painting it is painted once. It draws the default account, as the Connect panel does.
 */
export function replyCardFor(chatId: string): () => Html {
    let view = chatCards.get(chatId);
    if (view === undefined) {
        view = () => {
            const state = setup.value;
            if (!state) return html``;
            return replyCard(
                state,
                setupBusy.value === null ? null : html`disabled`,
                'Connect Reply',
                chatPlace(chatId),
                true,
            );
        };
        chatCards.set(chatId, view);
    }
    return view;
}

/** The Accounts page's place, and the prefix of each of its cards' places. */
export const accountsPlace: Place = 'accounts';

/**
 * The Accounts page's Reply accounts: the Reply section, then one card per account, the default first, each at a place of
 * its own. One function, so the region painting it is painted once.
 */
export function accountsReplyView(): Html {
    const state = setup.value;
    if (!state) {
        const failure = setupError.value;
        return failure
            ? html`<div class="note am" role="alert">${failure}</div>`
            : html`<div class="empty">Reading what is set up…</div>`;
    }
    const busy = setupBusy.value === null ? null : html`disabled`;
    return html`<div class="reply-accounts" role="region" aria-label="Reply accounts">
        ${replySection(state, busy)} ${accountViews(state).map((view) => accountCard(state, view, busy))}
    </div>`;
}

/** The Accounts page stopped painting its cards: a dialog or a choice one of them held open is dropped. */
export function accountsReplyGone(): void {
    const ours = (place: Place | null | undefined) =>
        place === accountsPlace || (place ?? '').startsWith(`${accountsPlace}:`);
    if (ours(actDialog.peek()?.place)) closeDialog();
    if (ours(adding.peek())) adding.value = null;
    if (ours(naming.peek())) naming.value = null;
    if (ours(limiting.peek()?.place)) limiting.value = null;
    if (ours(cardRefused.peek()?.place)) cardRefused.value = null;
    addingAccount.value = { ...addingAccount.peek(), refused: null };
}

/**
 * The AI card, what Connect and Settings' Models hold for the AI, in two groups. What is on this computer: each program
 * found, a card, the one in use marked, then the key providers whose key is set, in one row. Under it, in a panel of its
 * own, what can be added or changed: the programs not found, with Install where it can be done, and the model keys' panel.
 */
function aiCards(
    programs: readonly DesktopHost[],
    providers: readonly DesktopHost[],
    disabled: Html | null,
    heading = true,
): Html {
    const found = programs.filter(hostRow);
    const set = keysSet(providers);
    const absent = notFound(
        programs.filter((each) => !hostRow(each)),
        disabled,
    );
    const keys = keyPanel(providers, disabled);
    return html`<div class="card ai-card">
        ${heading ? html`<div class="hd"><b>AI</b></div>` : null}
        <div class="bd">
            ${
                found.length > 0 || set !== null
                    ? html`<div class="ai-here">
                          <div class="group-label">On this computer</div>
                          ${found.map((each) => program(each, disabled))}${set}
                      </div>`
                    : null
            }
            ${
                absent !== null || keys !== null
                    ? html`<div class="ai-add">
                          <div class="group-label">Add or change</div>
                          ${absent}${keys}
                      </div>`
                    : null
            }
        </div>
    </div>`;
}

/**
 * The AI hosts and the model keys alone, for Settings' Models section: the same cards, with the same acts, that Connect
 * draws, read from the same `setup.status()` answer, without the card's own "AI" heading: the section says "Models".
 * Connect keeps them as well, with the heading.
 */
export function modelsView(): Html {
    const state = setup.value;
    const busy = setupBusy.value;
    const failure = setupError.value;
    const disabled = busy === null ? null : html`disabled`;
    return html`${busy ? html`<div class="note" role="status"><span class="spin"></span>${busy}</div>` : null}
    ${failure ? html`<div class="note am" role="alert">${failure}</div>` : null}
    ${
        state
            ? aiCards(
                  state.hosts.filter((each) => each.kind === 'program'),
                  state.hosts.filter((each) => each.kind === 'provider'),
                  disabled,
                  false,
              )
            : failure
              ? null
              : html`<div class="empty">Reading what is set up…</div>`
    }`;
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
        ${aiCards(programs, providers, disabled)} ${replyCard(state, disabled)}
        ${state.plugins.length ? plugins(state, disabled) : null}
        <div class="meta versions">
            Jason Desktop ${state.bundleVersion} · runtime ${state.runtimeVersion ?? 'not running'}
        </div>
        ${state.versionsDiffer === null ? null : html`<div class="meta versions-differ">${state.versionsDiffer}</div>`}
    </div>`;
}
