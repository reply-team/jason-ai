// The seam the page declares in ui/src/platform/desktop.d.ts, held against what the desktop implements. Everything
// here is checked by the compiler: `npm run check` type-checks this file, so a member, a field or a channel that drifts
// from the declaration on either side is a red build. The one test below only says so when the file runs.

import test from 'node:test';
import assert from 'node:assert/strict';
import type { DesktopSetup, JasonDesktop } from '../../ui/src/platform/desktop.d.ts';
import type { DesktopPreload } from '../src/preload.ts';
import type { SetupState } from '../src/setup.ts';
import type { eventChannels, InvokeMember } from '../src/ipc.ts';

/** True when the two types are the same type, field by field and all the way down. */
type Equal<A, B> = (<T>() => T extends A ? 1 : 2) extends (<T>() => T extends B ? 1 : 2) ? true : false;
type Holds<Check extends true> = Check;

// setup.status() answers exactly what the page reads.
type StatusIsTheSeam = Holds<Equal<SetupState, DesktopSetup>>;

// The preload puts exactly the declared members on the window, and each one fits the member it stands for.
type PreloadIsTheSeam = [
    Holds<Equal<keyof DesktopPreload, keyof JasonDesktop>>,
    Holds<Equal<keyof DesktopPreload['chat'], keyof JasonDesktop['chat']>>,
    Holds<Equal<keyof DesktopPreload['setup'], keyof JasonDesktop['setup']>>,
    Holds<DesktopPreload extends JasonDesktop ? true : false>,
];

/** The members of one part of the seam that the page calls and the desktop answers: each returns a promise. */
type Calls<Part, Prefix extends string> = {
    [Name in keyof Part & string]: Part[Name] extends (...args: never[]) => Promise<unknown> ? `${Prefix}.${Name}` : never;
}[keyof Part & string];

/** The members that subscribe to what the desktop sends: each returns the function that stops it. */
type Subscriptions<Part, Prefix extends string> = {
    [Name in keyof Part & string]: Part[Name] extends (handler: never) => () => void ? `${Prefix}${Name}` : never;
}[keyof Part & string];

type DeclaredCalls = Calls<JasonDesktop['chat'], 'chat'> | Calls<JasonDesktop['setup'], 'setup'>;
type DeclaredSubscriptions =
    | Subscriptions<JasonDesktop, ''>
    | Subscriptions<JasonDesktop['chat'], 'chat.'>
    | Subscriptions<JasonDesktop['setup'], 'setup.'>;

// ipc.ts registers a channel for every call the page declares, and for nothing else.
type InvokeChannelsAreTheSeam = Holds<Equal<InvokeMember, DeclaredCalls>>;

// Each subscription the page declares listens on one event channel ipc.ts names, and every event channel has one.
const eventChannelOf = {
    onCommand: 'command',
    'chat.onEvent': 'chat.event',
    'setup.onChanged': 'setup.changed',
} as const satisfies Record<DeclaredSubscriptions, keyof typeof eventChannels>;
type EventChannelsAreTheSeam = [
    Holds<Equal<keyof typeof eventChannelOf, DeclaredSubscriptions>>,
    Holds<Equal<(typeof eventChannelOf)[keyof typeof eventChannelOf], keyof typeof eventChannels>>,
];

export type SeamHeld = [StatusIsTheSeam, PreloadIsTheSeam, InvokeChannelsAreTheSeam, EventChannelsAreTheSeam];

test('the desktop implements the seam ui/src/platform/desktop.d.ts declares (checked by npm run check)', () => {
    assert.deepEqual(Object.keys(eventChannelOf), ['onCommand', 'chat.onEvent', 'setup.onChanged']);
});
