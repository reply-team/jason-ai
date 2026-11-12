// The seam the page declares in ui/src/platform/desktop.d.ts, held against what the desktop implements. Everything
// here is checked by the compiler: `npm run check` type-checks this file, so a member, a field or a channel that drifts
// from the declaration on either side is a red build. The one test below only says so when the file runs.

import test from 'node:test';
import assert from 'node:assert/strict';
import type {
    DesktopChatEvent,
    DesktopChatOptions,
    DesktopConversation,
    DesktopConversationRecord,
    DesktopKeptEntry,
    DesktopRefusal,
    DesktopReplyLink,
    DesktopSetup,
    JasonDesktop,
} from '../../ui/src/platform/desktop.d.ts';
import type { ChatEvent, ChatSendOptions, ConversationOpened, ConversationSummary } from '../src/chat.ts';
import type { KeptEntry } from '../src/chat-conversations.ts';
import type { ChatRefusal } from '../src/chat-ipc.ts';
import type { DesktopPreload } from '../src/preload.ts';
import type { LinkRefusal, ReplyLink, SetupState } from '../src/setup.ts';
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
    Holds<Equal<keyof DesktopPreload['theme'], keyof JasonDesktop['theme']>>,
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

type DeclaredCalls = Calls<JasonDesktop['chat'], 'chat'> | Calls<JasonDesktop['setup'], 'setup'> | Calls<JasonDesktop['theme'], 'theme'>;
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

// A conversation, its kept entries and the events of the conversations reach the page exactly as the desktop makes them.
type Conversational = 'attached' | 'fresh' | 'pick' | 'renamed' | 'line' | 'gap';
type ConversationsAreTheSeam = [
    Holds<Equal<ConversationSummary, DesktopConversation>>,
    Holds<Equal<ConversationOpened, DesktopConversationRecord>>,
    Holds<Equal<KeptEntry, DesktopKeptEntry>>,
    Holds<Equal<Extract<ChatEvent, { kind: Conversational }>, Extract<DesktopChatEvent, { kind: Conversational }>>>,
];

// A turn names the conversation and the host, the model and the effort; never a campaign, which the conversation's
// record decides.
type SendIsTheSeam = [Holds<Equal<ChatSendOptions, DesktopChatOptions>>, Holds<Equal<keyof DesktopChatOptions, 'host' | 'model' | 'effort'>>];

// A refusal reaches the page as the value the desktop answers it with.
type RefusalIsTheSeam = Holds<Equal<ChatRefusal, DesktopRefusal>>;

// A Reply link, or the sentence its ask ended with, reaches the page as the desktop answers it.
type LinkIsTheSeam = [Holds<Equal<ReplyLink, DesktopReplyLink>>, Holds<Equal<LinkRefusal, DesktopRefusal>>];

export type SeamHeld = [
    RefusalIsTheSeam,
    LinkIsTheSeam,
    StatusIsTheSeam,
    PreloadIsTheSeam,
    InvokeChannelsAreTheSeam,
    EventChannelsAreTheSeam,
    ConversationsAreTheSeam,
    SendIsTheSeam,
];

test('the desktop implements the seam ui/src/platform/desktop.d.ts declares (checked by npm run check)', () => {
    assert.deepEqual(Object.keys(eventChannelOf), ['onCommand', 'chat.onEvent', 'setup.onChanged']);
});
