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
    DesktopFullAccessStanding,
    DesktopKeptEntry,
    DesktopLink,
    DesktopOpenInAnswer,
    DesktopOpenInContinue,
    DesktopOpenInPlace,
    DesktopRefusal,
    DesktopReplyCopied,
    DesktopReplyDone,
    DesktopReplyOpened,
    DesktopReplyRefusal,
    DesktopSetup,
    JasonDesktop,
} from '../../ui/src/platform/desktop.d.ts';
import type { ChatEvent, ChatSendOptions, ConversationOpened, ConversationSummary, FullAccessStanding } from '../src/chat.ts';
import type { KeptEntry } from '../src/chat-conversations.ts';
import type { ChatRefusal } from '../src/chat-ipc.ts';
import type { LinkTarget } from '../src/deep-link.ts';
import type { applyMode, Mode, ModeAnswer } from '../src/mode.ts';
import type { ContinueOffer, OpenInAnswer, OpenInRefusal, PlaceId } from '../src/open-in.ts';
import type { DesktopPreload } from '../src/preload.ts';
import type { ReplyCopied, ReplyDone, ReplyOpened, ReplyRefusal, SetupState } from '../src/setup.ts';
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
    Holds<Equal<keyof DesktopPreload['links'], keyof JasonDesktop['links']>>,
    Holds<Equal<keyof DesktopPreload['theme'], keyof JasonDesktop['theme']>>,
    Holds<Equal<keyof DesktopPreload['mode'], keyof JasonDesktop['mode']>>,
    Holds<Equal<keyof DesktopPreload['openIn'], keyof JasonDesktop['openIn']>>,
    Holds<Equal<keyof DesktopPreload['pairing'], keyof JasonDesktop['pairing']>>,
    Holds<Equal<keyof DesktopPreload['fullAccess'], keyof JasonDesktop['fullAccess']>>,
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

type DeclaredCalls =
    | Calls<JasonDesktop['chat'], 'chat'>
    | Calls<JasonDesktop['setup'], 'setup'>
    | Calls<JasonDesktop['links'], 'links'>
    | Calls<JasonDesktop['theme'], 'theme'>
    | Calls<JasonDesktop['mode'], 'mode'>
    | Calls<JasonDesktop['openIn'], 'openIn'>
    | Calls<JasonDesktop['pairing'], 'pairing'>
    | Calls<JasonDesktop['fullAccess'], 'fullAccess'>;
type DeclaredSubscriptions =
    | Subscriptions<JasonDesktop, ''>
    | Subscriptions<JasonDesktop['chat'], 'chat.'>
    | Subscriptions<JasonDesktop['setup'], 'setup.'>
    | Subscriptions<JasonDesktop['links'], 'links.'>
    | Subscriptions<JasonDesktop['fullAccess'], 'fullAccess.'>
    | Subscriptions<JasonDesktop['mode'], 'mode.'>;

// ipc.ts registers a channel for every call the page declares, and for nothing else.
type InvokeChannelsAreTheSeam = Holds<Equal<InvokeMember, DeclaredCalls>>;

// Each subscription the page declares listens on one event channel ipc.ts names, and every event channel has one.
const eventChannelOf = {
    onCommand: 'command',
    'chat.onEvent': 'chat.event',
    'setup.onChanged': 'setup.changed',
    'links.onOpen': 'links.open',
    'fullAccess.onChanged': 'fullAccess.changed',
    'mode.onPreparing': 'mode.preparing',
} as const satisfies Record<DeclaredSubscriptions, keyof typeof eventChannels>;
type EventChannelsAreTheSeam = [
    Holds<Equal<keyof typeof eventChannelOf, DeclaredSubscriptions>>,
    Holds<Equal<(typeof eventChannelOf)[keyof typeof eventChannelOf], keyof typeof eventChannels>>,
];

// A conversation, its kept entries and the events of the conversations reach the page exactly as the desktop makes them.
type Conversational = 'attached' | 'fresh' | 'pick' | 'renamed' | 'mode' | 'line' | 'gap' | 'notice';
type ConversationsAreTheSeam = [
    Holds<Equal<ConversationSummary, DesktopConversation>>,
    Holds<Equal<ConversationOpened, DesktopConversationRecord>>,
    Holds<Equal<KeptEntry, DesktopKeptEntry>>,
    Holds<Equal<Extract<ChatEvent, { kind: Conversational }>, Extract<DesktopChatEvent, { kind: Conversational }>>>,
];

// A turn names the conversation and the host, the model, the effort and the mode it was approved for; never a campaign,
// which the conversation's record decides.
type SendIsTheSeam = [Holds<Equal<ChatSendOptions, DesktopChatOptions>>, Holds<Equal<keyof DesktopChatOptions, 'host' | 'model' | 'effort' | 'mode'>>];

// A turn's end reaches the page as the desktop says it, with the mark of a plan.
type DoneIsTheSeam = Holds<Equal<Extract<ChatEvent, { kind: 'done' }>, Extract<DesktopChatEvent, { kind: 'done' }>>>;

// A refusal reaches the page as the value the desktop answers it with.
type RefusalIsTheSeam = Holds<Equal<ChatRefusal, DesktopRefusal>>;

// A Reply act's answer reaches the page as the desktop answers it: that a link was opened, never the link.
type ReplyLinkIsTheSeam = [
    Holds<Equal<ReplyOpened, DesktopReplyOpened>>,
    Holds<Equal<ReplyRefusal, DesktopReplyRefusal>>,
    Holds<Equal<ReplyDone, DesktopReplyDone>>,
    Holds<Equal<ReplyCopied, DesktopReplyCopied>>,
    Holds<Equal<keyof DesktopReplyCopied, 'copied' | 'channel' | 'kind' | 'linkKind' | 'expiresAt' | 'pendingId'>>,
    Holds<Equal<keyof DesktopReplyOpened, 'opened' | 'channel' | 'kind' | 'linkKind' | 'expiresAt' | 'pendingId'>>,
];

// The standing choice of Full access reaches the page as the chat reads it: when it was given and whether new chats start in it.
type StandingIsTheSeam = Holds<Equal<FullAccessStanding, DesktopFullAccessStanding>>;

// A link reaches the page as the page the desktop parsed it to, never as the address.
type LinkIsTheSeam = Holds<Equal<LinkTarget, DesktopLink>>;

// The mode reaches the page as the desktop keeps it, one of two words, with the sentence of a demo that could not be
// opened and whether the demo's sample is still being made, and setting it answers as the desktop does: null once written and the desktop starting again, or the desktop's refusal when the record could not be written.
type ModeIsTheSeam = [
    Holds<Equal<ModeAnswer, Awaited<ReturnType<JasonDesktop['mode']['get']>>>>,
    Holds<Equal<Mode, Parameters<JasonDesktop['mode']['set']>[0]>>,
    Holds<Equal<ReturnType<typeof applyMode>, Awaited<ReturnType<JasonDesktop['mode']['set']>>>>,
];

// Open in: the places, the default and the folder's name reach the page as the desktop makes them, a place is named by
// the one list of words both sides hold, and the page sends a name and a campaign's id, nothing else. A chat to continue in
// a terminal reaches the page as its host's name and whether a turn runs, and the page sends the chat's id alone.
type OpenInIsTheSeam = [
    Holds<Equal<PlaceId, DesktopOpenInPlace>>,
    Holds<Equal<OpenInAnswer, DesktopOpenInAnswer>>,
    Holds<Equal<OpenInRefusal, DesktopRefusal>>,
    Holds<Equal<Parameters<JasonDesktop['openIn']['open']>, [target: DesktopOpenInPlace, campaignId?: string]>>,
    Holds<Equal<Parameters<JasonDesktop['openIn']['targets']>, [campaignId?: string, chatId?: string]>>,
    Holds<Equal<Parameters<JasonDesktop['openIn']['continueChat']>, [chatId: string]>>,
    Holds<Equal<ContinueOffer, DesktopOpenInContinue>>,
    Holds<Equal<keyof DesktopOpenInContinue, 'host' | 'label' | 'running'>>,
];

export type SeamHeld = [
    OpenInIsTheSeam,
    ModeIsTheSeam,
    RefusalIsTheSeam,
    ReplyLinkIsTheSeam,
    StatusIsTheSeam,
    PreloadIsTheSeam,
    InvokeChannelsAreTheSeam,
    EventChannelsAreTheSeam,
    ConversationsAreTheSeam,
    SendIsTheSeam,
    DoneIsTheSeam,
    LinkIsTheSeam,
    StandingIsTheSeam,
];

test('the desktop implements the seam ui/src/platform/desktop.d.ts declares (checked by npm run check)', () => {
    assert.deepEqual(Object.keys(eventChannelOf), ['onCommand', 'chat.onEvent', 'setup.onChanged', 'links.onOpen', 'fullAccess.onChanged', 'mode.onPreparing']);
});
