import { describe, expect, it } from 'vitest';
import type { DesktopSetup, JasonDesktop } from '../../src/platform/desktop';

// The seam is the whole of what the page may ask of the desktop, and the desktop's main process is written to it. A
// member added or dropped is a change of the seam, so each object below names exactly the members of its part:
// `satisfies` refuses a name the type lacks, and `Exactly` refuses a member the object leaves out. `npm run check`
// type-checks this file, so either is a red build.
type Exactly<Named, Declared> = [Named] extends [Declared] ? ([Declared] extends [Named] ? true : false) : false;

const top = {
    platform: true,
    onCommand: true,
    theme: true,
    mode: true,
    openIn: true,
    pairing: true,
    chat: true,
    links: true,
    setup: true,
    fullAccess: true,
} as const satisfies Record<keyof JasonDesktop, true>;
const theme = { get: true, set: true } as const satisfies Record<keyof JasonDesktop['theme'], true>;
// Live or Demo, the installation the desktop runs on: read once as the page loads, and set, which starts it again.
const mode = { get: true, set: true, startOver: true } as const satisfies Record<keyof JasonDesktop['mode'], true>;
// Open in: the places this computer offers for a folder and the act of opening one; the page names a place, never a path.
const openIn = { targets: true, open: true } as const satisfies Record<keyof JasonDesktop['openIn'], true>;
const chat = {
    send: true,
    stop: true,
    answer: true,
    onEvent: true,
    list: true,
    open: true,
    create: true,
    remove: true,
    restart: true,
    attach: true,
    rename: true,
    setMode: true,
    openFolder: true,
    feed: true,
} as const satisfies Record<keyof JasonDesktop['chat'], true>;
const links = { take: true, onOpen: true } as const satisfies Record<keyof JasonDesktop['links'], true>;
// The window's pairing: the page reports its token's id, never the token.
const pairing = { paired: true } as const satisfies Record<keyof JasonDesktop['pairing'], true>;
// The standing choice of Full access: read and taken back by the page, never given by it.
const fullAccess = { standing: true, takeBack: true, onChanged: true } as const satisfies Record<
    keyof JasonDesktop['fullAccess'],
    true
>;
const setup = {
    status: true,
    connectReply: true,
    connectReplyWithKey: true,
    setProviderKey: true,
    installProgram: true,
    configurePlugin: true,
    disconnectPlugin: true,
    replyConnect: true,
    replyReconnect: true,
    replyLimits: true,
    replyPause: true,
    replyResume: true,
    replyUnblock: true,
    replyDelete: true,
    onChanged: true,
} as const satisfies Record<keyof JasonDesktop['setup'], true>;
// What setup.status answers is the seam too: the desktop's main process fills each field.
const status = {
    hosts: true,
    reply: true,
    runtimeVersion: true,
    bundleVersion: true,
    versionsDiffer: true,
    plugins: true,
    settingsRefused: true,
} as const satisfies Record<keyof DesktopSetup, true>;

const held: [
    Exactly<keyof typeof top, keyof JasonDesktop>,
    Exactly<keyof typeof theme, keyof JasonDesktop['theme']>,
    Exactly<keyof typeof mode, keyof JasonDesktop['mode']>,
    Exactly<keyof typeof openIn, keyof JasonDesktop['openIn']>,
    Exactly<keyof typeof chat, keyof JasonDesktop['chat']>,
    Exactly<keyof typeof setup, keyof JasonDesktop['setup']>,
    Exactly<keyof typeof status, keyof DesktopSetup>,
    Exactly<keyof typeof links, keyof JasonDesktop['links']>,
    Exactly<keyof typeof fullAccess, keyof JasonDesktop['fullAccess']>,
    Exactly<keyof typeof pairing, keyof JasonDesktop['pairing']>,
] = [true, true, true, true, true, true, true, true, true, true];

describe('the desktop seam', () => {
    it('is exactly the members the desktop implements', () => {
        expect(held).toEqual([true, true, true, true, true, true, true, true, true, true]);
        expect([
            ...Object.keys(top).filter(
                (name) =>
                    name !== 'theme' &&
                    name !== 'mode' &&
                    name !== 'openIn' &&
                    name !== 'pairing' &&
                    name !== 'chat' &&
                    name !== 'links' &&
                    name !== 'setup' &&
                    name !== 'fullAccess',
            ),
            ...Object.keys(theme).map((name) => `theme.${name}`),
            ...Object.keys(mode).map((name) => `mode.${name}`),
            ...Object.keys(openIn).map((name) => `openIn.${name}`),
            ...Object.keys(pairing).map((name) => `pairing.${name}`),
            ...Object.keys(chat).map((name) => `chat.${name}`),
            ...Object.keys(links).map((name) => `links.${name}`),
            ...Object.keys(setup).map((name) => `setup.${name}`),
            ...Object.keys(fullAccess).map((name) => `fullAccess.${name}`),
        ]).toEqual([
            'platform',
            'onCommand',
            'theme.get',
            'theme.set',
            'mode.get',
            'mode.set',
            'mode.startOver',
            'openIn.targets',
            'openIn.open',
            'pairing.paired',
            'chat.send',
            'chat.stop',
            'chat.answer',
            'chat.onEvent',
            'chat.list',
            'chat.open',
            'chat.create',
            'chat.remove',
            'chat.restart',
            'chat.attach',
            'chat.rename',
            'chat.setMode',
            'chat.openFolder',
            'chat.feed',
            'links.take',
            'links.onOpen',
            'setup.status',
            'setup.connectReply',
            'setup.connectReplyWithKey',
            'setup.setProviderKey',
            'setup.installProgram',
            'setup.configurePlugin',
            'setup.disconnectPlugin',
            'setup.replyConnect',
            'setup.replyReconnect',
            'setup.replyLimits',
            'setup.replyPause',
            'setup.replyResume',
            'setup.replyUnblock',
            'setup.replyDelete',
            'setup.onChanged',
            'fullAccess.standing',
            'fullAccess.takeBack',
            'fullAccess.onChanged',
        ]);
        expect(Object.keys(status)).toEqual([
            'hosts',
            'reply',
            'runtimeVersion',
            'bundleVersion',
            'versionsDiffer',
            'plugins',
            'settingsRefused',
        ]);
    });
});
