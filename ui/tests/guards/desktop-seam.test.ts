import { describe, expect, it } from 'vitest';
import type { DesktopSetup, JasonDesktop } from '../../src/platform/desktop';

// The seam is the whole of what the page may ask of the desktop, and the desktop's main process is written to it. A
// member added or dropped is a change of the seam, so each object below names exactly the members of its part:
// `satisfies` refuses a name the type lacks, and `Exactly` refuses a member the object leaves out. `npm run check`
// type-checks this file, so either is a red build.
type Exactly<Named, Declared> = [Named] extends [Declared] ? ([Declared] extends [Named] ? true : false) : false;

const top = { platform: true, onCommand: true, theme: true, chat: true, setup: true } as const satisfies Record<
    keyof JasonDesktop,
    true
>;
const theme = { get: true, set: true } as const satisfies Record<keyof JasonDesktop['theme'], true>;
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
    feed: true,
} as const satisfies Record<keyof JasonDesktop['chat'], true>;
const setup = {
    status: true,
    connectReply: true,
    connectReplyWithKey: true,
    setProviderKey: true,
    installProgram: true,
    configurePlugin: true,
    disconnectPlugin: true,
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
    Exactly<keyof typeof chat, keyof JasonDesktop['chat']>,
    Exactly<keyof typeof setup, keyof JasonDesktop['setup']>,
    Exactly<keyof typeof status, keyof DesktopSetup>,
] = [true, true, true, true, true];

describe('the desktop seam', () => {
    it('is exactly the members the desktop implements', () => {
        expect(held).toEqual([true, true, true, true, true]);
        expect([
            ...Object.keys(top).filter((name) => name !== 'theme' && name !== 'chat' && name !== 'setup'),
            ...Object.keys(theme).map((name) => `theme.${name}`),
            ...Object.keys(chat).map((name) => `chat.${name}`),
            ...Object.keys(setup).map((name) => `setup.${name}`),
        ]).toEqual([
            'platform',
            'onCommand',
            'theme.get',
            'theme.set',
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
            'chat.feed',
            'setup.status',
            'setup.connectReply',
            'setup.connectReplyWithKey',
            'setup.setProviderKey',
            'setup.installProgram',
            'setup.configurePlugin',
            'setup.disconnectPlugin',
            'setup.onChanged',
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
