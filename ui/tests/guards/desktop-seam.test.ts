import { describe, expect, it } from 'vitest';
import type { JasonDesktop } from '../../src/platform/desktop';

// The seam is the whole of what the page may ask of the desktop, and the desktop's main process is written to it. A
// member added or dropped is a change of the seam, so each object below names exactly the members of its part:
// `satisfies` refuses a name the type lacks, and `Exactly` refuses a member the object leaves out. `npm run check`
// type-checks this file, so either is a red build.
type Exactly<Named, Declared> = [Named] extends [Declared] ? ([Declared] extends [Named] ? true : false) : false;

const top = { platform: true, onCommand: true, chat: true, setup: true } as const satisfies Record<
    keyof JasonDesktop,
    true
>;
const chat = { send: true, stop: true, answer: true, onEvent: true } as const satisfies Record<
    keyof JasonDesktop['chat'],
    true
>;
const setup = {
    status: true,
    connectReply: true,
    connectReplyWithKey: true,
    setProviderKey: true,
    installProgram: true,
    setPluginToken: true,
    onChanged: true,
} as const satisfies Record<keyof JasonDesktop['setup'], true>;

const held: [
    Exactly<keyof typeof top, keyof JasonDesktop>,
    Exactly<keyof typeof chat, keyof JasonDesktop['chat']>,
    Exactly<keyof typeof setup, keyof JasonDesktop['setup']>,
] = [true, true, true];

describe('the desktop seam', () => {
    it('is exactly the members the desktop implements', () => {
        expect(held).toEqual([true, true, true]);
        expect([
            ...Object.keys(top).filter((name) => name !== 'chat' && name !== 'setup'),
            ...Object.keys(chat).map((name) => `chat.${name}`),
            ...Object.keys(setup).map((name) => `setup.${name}`),
        ]).toEqual([
            'platform',
            'onCommand',
            'chat.send',
            'chat.stop',
            'chat.answer',
            'chat.onEvent',
            'setup.status',
            'setup.connectReply',
            'setup.connectReplyWithKey',
            'setup.setProviderKey',
            'setup.installProgram',
            'setup.setPluginToken',
            'setup.onChanged',
        ]);
    });
});
