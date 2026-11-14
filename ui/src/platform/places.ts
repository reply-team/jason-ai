import { bridgeMembers, notInTab, type Bridge } from './bridge';
import type { DesktopRefusal, DesktopReplyRefusal, JasonDesktop } from './desktop';
import { t } from './i18n';
import { windowDesktop } from './window-desktop';

// One console, run in places that differ by what each can do, never by a name: Jason Desktop's own window (everything),
// a browser tab the desktop opened, which holds a bridge answering one table of members, and a browser tab with no
// desktop (nothing of the desktop). The console is always handed an object of the desktop's whole shape; before it draws
// a control it asks one question, `can(member)`: can this place do this member? What the place cannot do is drawn
// disabled, with a sentence, and the member itself answers a refusal.

type Member = (...args: never[]) => unknown;

/** Every member of the desktop's object that is a function, by its path in the seam: `chat.send`, `onCommand`. */
export type DesktopMember = {
    [P in keyof JasonDesktop & string]: JasonDesktop[P] extends Member
        ? P
        : JasonDesktop[P] extends object
          ? {
                [M in keyof JasonDesktop[P] & string]: JasonDesktop[P][M] extends Member ? `${P}.${M}` : never;
            }[keyof JasonDesktop[P] & string]
          : never;
}[keyof JasonDesktop & string];

/** Where a person gets Jason Desktop. */
export const releasesPage = 'https://github.com/reply-team/jason-ai/releases';

export type PlaceKind = 'window' | 'tab' | 'none';

/** The subscriptions the bridge's event stream feeds: not calls of the table, but a tab has them. */
const streamed: readonly string[] = ['chat.onEvent', 'setup.onChanged', 'fullAccess.onChanged'];

/** Whether a place of this kind can do the member. */
export function canIn(kind: PlaceKind, member: DesktopMember): boolean {
    if (kind === 'window') return true;
    if (kind === 'none') return false;
    return (bridgeMembers as readonly string[]).includes(member) || streamed.includes(member);
}

// Until the page says where it runs nothing is done: a place that was never named can do nothing of the desktop.
let kind: PlaceKind = 'none';
let held: JasonDesktop | undefined;

/** Says which place this page runs in, and the object it hands the console; `windowDesktop()` in the window. */
export function setPlace(next: PlaceKind, desktop?: JasonDesktop): void {
    kind = next;
    held = desktop;
}

export const placeKind = (): PlaceKind => kind;

/** The object the page was handed: the window's, the tab's, or none in a plain browser. */
export const currentDesktop = (): JasonDesktop | undefined => held ?? windowDesktop();

/** The desktop as one of an earlier version may be: a part of it may be missing, and the page asks before it reads one. */
export const earlier = (desktop: JasonDesktop): Partial<JasonDesktop> => desktop;

/** The one question: can this place do this member? */
export const can = (member: DesktopMember): boolean => canIn(kind, member);

/** `act` as it stands where the place can do the member, and nothing where it cannot. */
export function gated<Args extends unknown[]>(
    member: DesktopMember,
    act: (...args: Args) => void,
): (...args: Args) => void {
    return (...args) => {
        if (can(member)) act(...args);
    };
}

/** The one sentence a control says when its place cannot do its member. */
export const cannotHere = (): string => t(kind === 'none' ? 'place.needs' : 'place.elsewhere');

/** The platform of the machine the page runs on, as the browser says it; never asked of a desktop. */
export function browserPlatform(): JasonDesktop['platform'] {
    const data = (navigator as { userAgentData?: { platform?: string } }).userAgentData?.platform;
    const text = `${data ?? ''} ${navigator.platform ?? ''} ${navigator.userAgent ?? ''}`.toLowerCase();
    if (/mac|iphone|ipad/.test(text)) return 'darwin';
    if (/win/.test(text)) return 'win32';
    return 'linux';
}

const refused = (): DesktopRefusal => ({ refused: notInTab });
const replyRefused = (): DesktopReplyRefusal => ({ refused: notInTab, code: null, offer: null, reread: false });
const rejected = (): Promise<never> => Promise.reject(new Error(notInTab));
const none = (): (() => void) => () => undefined;

/**
 * The object of a tab with a bridge, of the desktop's whole shape: the members of the bridge's table are carried over it,
 * the rest are present and answer the refusal their own type has. `platform` is the browser's own.
 */
export function tabDesktop(bridge: Bridge): JasonDesktop {
    const seam = bridge.seam();
    return {
        platform: browserPlatform(),
        onCommand: none,
        theme: { get: seam.theme.get, set: rejected },
        dailyTrends: { get: async () => null, set: rejected },
        mode: {
            get: seam.mode.get,
            set: async () => refused(),
            startOver: async () => refused(),
            onPreparing: none,
        },
        openIn: {
            targets: async () => refused(),
            open: async () => refused(),
            continueChat: async () => refused(),
            changeJason: async () => refused(),
        },
        pairing: { paired: rejected },
        chat: {
            send: seam.chat.send,
            stop: seam.chat.stop,
            answer: rejected,
            onEvent: seam.chat.onEvent,
            list: seam.chat.list,
            open: seam.chat.open,
            create: seam.chat.create,
            remove: async () => refused(),
            restart: async () => refused(),
            attach: async () => refused(),
            rename: seam.chat.rename,
            setMode: async () => refused(),
            openFolder: async () => refused(),
            feed: async () => refused(),
        },
        fullAccess: {
            standing: seam.fullAccess.standing,
            takeBack: async () => refused(),
            onChanged: seam.fullAccess.onChanged,
        },
        links: { take: async () => null, onOpen: none },
        setup: {
            status: seam.setup.status,
            connectReply: rejected,
            connectReplyWithKey: rejected,
            setProviderKey: rejected,
            installProgram: rejected,
            configurePlugin: rejected,
            disconnectPlugin: rejected,
            replyConnect: async () => replyRefused(),
            replyReconnect: async () => replyRefused(),
            replyLimits: async () => replyRefused(),
            replyPause: async () => replyRefused(),
            replyResume: async () => replyRefused(),
            replyUnblock: async () => replyRefused(),
            replyDelete: async () => replyRefused(),
            onChanged: seam.setup.onChanged,
        },
    };
}

const needs = (): Promise<never> => Promise.reject(new Error(t('place.needs')));
const needsRefusal = (): DesktopRefusal => ({ refused: t('place.needs') });
const needsReply = (): DesktopReplyRefusal => ({ refused: t('place.needs'), code: null, offer: null, reread: false });

/**
 * The object of a tab with no desktop, of the desktop's whole shape: every act refuses in one sentence, and a read says
 * what a page with no desktop can truthfully say: the person's own installation, no conversations, nothing standing, no
 * link held. What the desktop alone knows, the setup, is refused with the sentence, never made up. `platform` is the
 * browser's own.
 */
export function noDesktop(): JasonDesktop {
    return {
        platform: browserPlatform(),
        onCommand: none,
        theme: { get: async () => null, set: needs },
        dailyTrends: { get: async () => null, set: needs },
        mode: {
            get: async () => ({ mode: 'live', unopened: null, preparing: false }),
            set: async () => needsRefusal(),
            startOver: async () => needsRefusal(),
            onPreparing: none,
        },
        openIn: {
            targets: async () => needsRefusal(),
            open: async () => needsRefusal(),
            continueChat: async () => needsRefusal(),
            changeJason: async () => needsRefusal(),
        },
        pairing: { paired: needs },
        chat: {
            send: async () => needsRefusal(),
            stop: needs,
            answer: needs,
            onEvent: none,
            list: async () => [],
            open: async () => needsRefusal(),
            create: async () => needsRefusal(),
            remove: async () => needsRefusal(),
            restart: async () => needsRefusal(),
            attach: async () => needsRefusal(),
            rename: async () => needsRefusal(),
            setMode: async () => needsRefusal(),
            openFolder: async () => needsRefusal(),
            feed: async () => needsRefusal(),
        },
        fullAccess: { standing: async () => null, takeBack: async () => needsRefusal(), onChanged: none },
        links: { take: async () => null, onOpen: none },
        setup: {
            status: needs,
            connectReply: needs,
            connectReplyWithKey: needs,
            setProviderKey: needs,
            installProgram: needs,
            configurePlugin: needs,
            disconnectPlugin: needs,
            replyConnect: async () => needsReply(),
            replyReconnect: async () => needsReply(),
            replyLimits: async () => needsReply(),
            replyPause: async () => needsReply(),
            replyResume: async () => needsReply(),
            replyUnblock: async () => needsReply(),
            replyDelete: async () => needsReply(),
            onChanged: none,
        },
    };
}
