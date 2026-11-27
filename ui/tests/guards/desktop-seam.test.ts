import { describe, expect, it } from 'vitest';
import type {
    DesktopAccount,
    DesktopAccounts,
    DesktopReply,
    DesktopReplyTeam,
    DesktopSetup,
    JasonDesktop,
} from '../../src/platform/desktop';

// The seam is the whole of what the page may ask of the desktop, and the desktop's main process is written to it. A
// member added or dropped is a change of the seam, so each object below names exactly the members of its part:
// `satisfies` refuses a name the type lacks, and `Exactly` refuses a member the object leaves out. `npm run check`
// type-checks this file, so either is a red build.
type Exactly<Named, Declared> = [Named] extends [Declared] ? ([Declared] extends [Named] ? true : false) : false;

const top = {
    platform: true,
    onCommand: true,
    theme: true,
    updates: true,
    dailyTrends: true,
    mode: true,
    openIn: true,
    pairing: true,
    chat: true,
    links: true,
    setup: true,
} as const satisfies Record<keyof JasonDesktop, true>;
const theme = { get: true, set: true } as const satisfies Record<keyof JasonDesktop['theme'], true>;
// The update item's key, where the item can act: read by the System screen's notice of an update. The desktop's version
// and its update item as the menu shows it, a press of it as a click, and word that it changed: the foot of Settings.
const updates = { key: true, item: true, press: true, onChanged: true } as const satisfies Record<
    keyof JasonDesktop['updates'],
    true
>;
// Whether the Dashboard shows its daily trends: read as the page loads, told whenever the switch is flipped.
const dailyTrends = { get: true, set: true } as const satisfies Record<keyof JasonDesktop['dailyTrends'], true>;
// Live or Demo, the installation the desktop runs on: read once as the page loads, and set, which starts it again; and
// whether the demo's sample is still being made, said again when it is ready.
const mode = { get: true, set: true, onPreparing: true } as const satisfies Record<keyof JasonDesktop['mode'], true>;
// Open in: the places this computer offers for a folder and the act of opening one; the page names a place, never a path.
const openIn = { targets: true, open: true, continueChat: true, changeJason: true } as const satisfies Record<
    keyof JasonDesktop['openIn'],
    true
>;
const chat = {
    send: true,
    stop: true,
    answer: true,
    onEvent: true,
    list: true,
    open: true,
    create: true,
    // New GTM agent's click: the desktop makes the agent's campaign and its conversation, by the click's own id.
    createAgent: true,
    remove: true,
    restart: true,
    attach: true,
    rename: true,
    setMode: true,
    openFolder: true,
    feed: true,
    // A card the AI asked for that did not draw: its kind and why, for the desktop's log, never what it held.
    cardRefused: true,
    // Where a file dropped on the message box is on this computer, read by the desktop from the file itself, for the box.
    pathOf: true,
} as const satisfies Record<keyof JasonDesktop['chat'], true>;
const links = { take: true, onOpen: true } as const satisfies Record<keyof JasonDesktop['links'], true>;
// The window's pairing: the page reports its token's id, never the token.
const pairing = { paired: true } as const satisfies Record<keyof JasonDesktop['pairing'], true>;
const setup = {
    status: true,
    connectReply: true,
    connectReplyWithKey: true,
    setProviderKey: true,
    installProgram: true,
    useProgram: true,
    configurePlugin: true,
    disconnectPlugin: true,
    replyConnect: true,
    replyReconnect: true,
    replyLimits: true,
    replyPause: true,
    replyResume: true,
    replyUnblock: true,
    replyDelete: true,
    // Make default: the account it names becomes the global default, after the person's yes in the desktop's own box.
    makeDefault: true,
    // A team chosen for an account whose sign-in belongs to more than one, pinned on its profile in the Reply CLI.
    chooseTeam: true,
    // Add Reply account, Sign out and Remove: a profile made, signed out or deleted in the Reply CLI itself.
    addAccount: true,
    signOut: true,
    removeAccount: true,
    onChanged: true,
} as const satisfies Record<keyof JasonDesktop['setup'], true>;
// What setup.status answers is the seam too: the desktop's main process fills each field.
const status = {
    hosts: true,
    reply: true,
    // The Reply accounts, one per profile, which a desktop of an earlier version does not send.
    accounts: true,
    runtimeVersion: true,
    bundleVersion: true,
    versionsDiffer: true,
    plugins: true,
    settingsRefused: true,
} as const satisfies Record<keyof DesktopSetup, true>;
// And what it says of the Reply connection, the optional line about the update at the start among it.
const reply = {
    cli: true,
    signedIn: true,
    user: true,
    wired: true,
    accounts: true,
    absent: true,
    unread: true,
    pending: true,
    pendingUnread: true,
    notRead: true,
    belowMinimum: true,
    startUpdate: true,
    startCheck: true,
} as const satisfies Record<keyof DesktopReply, true>;
// And the accounts, each one profile of the Reply CLI with what was read of it, and what of them could not be read.
const accounts = {
    accounts: true,
    notRead: true,
    noDefault: true,
} as const satisfies Record<keyof DesktopAccounts, true>;
const account = {
    type: true,
    name: true,
    default: true,
    held: true,
    signedIn: true,
    user: true,
    // The team it is bound to, its profile's pin, by its id and name.
    team: true,
    senders: true,
    absent: true,
    unread: true,
    pending: true,
    pendingUnread: true,
    notRead: true,
    // The teams its sign-in can act in, where its profile has none pinned and Reply would not list it without one.
    teams: true,
} as const satisfies Record<keyof DesktopAccount, true>;
const accountTeam = { id: true, name: true } as const satisfies Record<keyof DesktopReplyTeam, true>;

const held: [
    Exactly<keyof typeof top, keyof JasonDesktop>,
    Exactly<keyof typeof theme, keyof JasonDesktop['theme']>,
    Exactly<keyof typeof updates, keyof JasonDesktop['updates']>,
    Exactly<keyof typeof dailyTrends, keyof JasonDesktop['dailyTrends']>,
    Exactly<keyof typeof mode, keyof JasonDesktop['mode']>,
    Exactly<keyof typeof openIn, keyof JasonDesktop['openIn']>,
    Exactly<keyof typeof chat, keyof JasonDesktop['chat']>,
    Exactly<keyof typeof setup, keyof JasonDesktop['setup']>,
    Exactly<keyof typeof status, keyof DesktopSetup>,
    Exactly<keyof typeof reply, keyof DesktopReply>,
    Exactly<keyof typeof accounts, keyof DesktopAccounts>,
    Exactly<keyof typeof account, keyof DesktopAccount>,
    Exactly<keyof typeof accountTeam, keyof DesktopReplyTeam>,
    Exactly<keyof typeof links, keyof JasonDesktop['links']>,
    Exactly<keyof typeof pairing, keyof JasonDesktop['pairing']>,
] = [true, true, true, true, true, true, true, true, true, true, true, true, true, true, true];

describe('the desktop seam', () => {
    it('is exactly the members the desktop implements', () => {
        expect(held).toEqual([
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
        ]);
        expect([
            ...Object.keys(top).filter(
                (name) =>
                    name !== 'theme' &&
                    name !== 'updates' &&
                    name !== 'dailyTrends' &&
                    name !== 'mode' &&
                    name !== 'openIn' &&
                    name !== 'pairing' &&
                    name !== 'chat' &&
                    name !== 'links' &&
                    name !== 'setup',
            ),
            ...Object.keys(theme).map((name) => `theme.${name}`),
            ...Object.keys(updates).map((name) => `updates.${name}`),
            ...Object.keys(dailyTrends).map((name) => `dailyTrends.${name}`),
            ...Object.keys(mode).map((name) => `mode.${name}`),
            ...Object.keys(openIn).map((name) => `openIn.${name}`),
            ...Object.keys(pairing).map((name) => `pairing.${name}`),
            ...Object.keys(chat).map((name) => `chat.${name}`),
            ...Object.keys(links).map((name) => `links.${name}`),
            ...Object.keys(setup).map((name) => `setup.${name}`),
        ]).toEqual([
            'platform',
            'onCommand',
            'theme.get',
            'theme.set',
            'updates.key',
            'updates.item',
            'updates.press',
            'updates.onChanged',
            'dailyTrends.get',
            'dailyTrends.set',
            'mode.get',
            'mode.set',
            'mode.onPreparing',
            'openIn.targets',
            'openIn.open',
            'openIn.continueChat',
            'openIn.changeJason',
            'pairing.paired',
            'chat.send',
            'chat.stop',
            'chat.answer',
            'chat.onEvent',
            'chat.list',
            'chat.open',
            'chat.create',
            'chat.createAgent',
            'chat.remove',
            'chat.restart',
            'chat.attach',
            'chat.rename',
            'chat.setMode',
            'chat.openFolder',
            'chat.feed',
            'chat.cardRefused',
            'chat.pathOf',
            'links.take',
            'links.onOpen',
            'setup.status',
            'setup.connectReply',
            'setup.connectReplyWithKey',
            'setup.setProviderKey',
            'setup.installProgram',
            'setup.useProgram',
            'setup.configurePlugin',
            'setup.disconnectPlugin',
            'setup.replyConnect',
            'setup.replyReconnect',
            'setup.replyLimits',
            'setup.replyPause',
            'setup.replyResume',
            'setup.replyUnblock',
            'setup.replyDelete',
            'setup.makeDefault',
            'setup.chooseTeam',
            'setup.addAccount',
            'setup.signOut',
            'setup.removeAccount',
            'setup.onChanged',
        ]);
        expect(Object.keys(status)).toEqual([
            'hosts',
            'reply',
            'accounts',
            'runtimeVersion',
            'bundleVersion',
            'versionsDiffer',
            'plugins',
            'settingsRefused',
        ]);
        expect(Object.keys(reply)).toEqual([
            'cli',
            'signedIn',
            'user',
            'wired',
            'accounts',
            'absent',
            'unread',
            'pending',
            'pendingUnread',
            'notRead',
            'belowMinimum',
            'startUpdate',
            'startCheck',
        ]);
        expect(Object.keys(accounts)).toEqual(['accounts', 'notRead', 'noDefault']);
        expect(Object.keys(account)).toEqual([
            'type',
            'name',
            'default',
            'held',
            'signedIn',
            'user',
            'team',
            'senders',
            'absent',
            'unread',
            'pending',
            'pendingUnread',
            'notRead',
            'teams',
        ]);
        expect(Object.keys(accountTeam)).toEqual(['id', 'name']);
    });
});
