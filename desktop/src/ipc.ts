/**
 * The IPC the page may use, and the one place a handler for it is registered. Every call is answered only when it came
 * from the top frame of a page at the runtime's loopback origin; a subframe, another origin, a `file:` page or a frame
 * already gone is refused before the handler runs.
 */

/** The calls the page makes, by the member of `window.jasonDesktop` that makes them. */
export const invokeChannels = {
    'setup.status': 'jason:setup.status',
    'setup.connectReply': 'jason:setup.connectReply',
    'setup.connectReplyWithKey': 'jason:setup.connectReplyWithKey',
    'setup.setProviderKey': 'jason:setup.setProviderKey',
    'setup.installProgram': 'jason:setup.installProgram',
    'setup.configurePlugin': 'jason:setup.configurePlugin',
    'setup.disconnectPlugin': 'jason:setup.disconnectPlugin',
    'setup.replyConnect': 'jason:setup.replyConnect',
    'setup.replyReconnect': 'jason:setup.replyReconnect',
    'setup.replyLimits': 'jason:setup.replyLimits',
    'setup.replyPause': 'jason:setup.replyPause',
    'setup.replyResume': 'jason:setup.replyResume',
    'setup.replyUnblock': 'jason:setup.replyUnblock',
    'setup.replyDelete': 'jason:setup.replyDelete',
    'theme.get': 'jason:theme.get',
    'theme.set': 'jason:theme.set',
    'mode.get': 'jason:mode.get',
    'mode.set': 'jason:mode.set',
    'chat.send': 'jason:chat.send',
    'chat.stop': 'jason:chat.stop',
    'chat.answer': 'jason:chat.answer',
    'chat.list': 'jason:chat.list',
    'chat.open': 'jason:chat.open',
    'chat.create': 'jason:chat.create',
    'chat.remove': 'jason:chat.remove',
    'chat.restart': 'jason:chat.restart',
    'chat.attach': 'jason:chat.attach',
    'chat.rename': 'jason:chat.rename',
    'chat.setMode': 'jason:chat.setMode',
    'chat.openFolder': 'jason:chat.openFolder',
    'chat.feed': 'jason:chat.feed',
    'fullAccess.standing': 'jason:fullAccess.standing',
    'fullAccess.takeBack': 'jason:fullAccess.takeBack',
    'links.take': 'jason:links.take',
    'pairing.paired': 'jason:pairing.paired',
} as const;

/** What the desktop sends the page. */
export const eventChannels = {
    command: 'jason:command',
    'chat.event': 'jason:chat.event',
    'setup.changed': 'jason:setup.changed',
    'links.open': 'jason:links.open',
    'fullAccess.changed': 'jason:fullAccess.changed',
} as const;

export type InvokeMember = keyof typeof invokeChannels;

/** The part of a sender's frame the check reads: Electron's `WebFrameMain` has both. */
export interface SenderFrame {
    url: string;
    parent: unknown;
}

export type Refusal = 'no_frame' | 'subframe' | 'other_origin';

/** Why a sender is refused, or null when it is the runtime's page in the top frame. */
export function refusalOf(frame: SenderFrame | null | undefined, origin: string | null): Refusal | null {
    if (!frame) return 'no_frame';
    if (frame.parent !== null && frame.parent !== undefined) return 'subframe';
    let url: URL;
    try {
        url = new URL(frame.url);
    } catch {
        return 'other_origin';
    }
    // A file: page's origin is the string "null", and an origin the runtime never had is null here: neither matches.
    if (origin === null || url.protocol !== 'http:' || url.origin !== origin) return 'other_origin';
    return null;
}

/** The registration surface of Electron's `ipcMain` that this module uses. */
export interface IpcRegistrar {
    handle(channel: string, listener: (event: { senderFrame: SenderFrame | null }, ...args: unknown[]) => unknown): void;
}

export class RefusedSender extends Error {
    constructor() {
        super('This window may not ask that.');
    }
}

/**
 * Registers the handler for one member. `origin` is read on every call, because the runtime can restart on another
 * port; `refused` is told the channel, so the refusal can be logged without anything the sender sent.
 */
export function handle(
    ipc: IpcRegistrar,
    member: InvokeMember,
    origin: () => string | null,
    handler: (...args: unknown[]) => unknown,
    refused: (channel: string, why: Refusal) => void,
): void {
    const channel = invokeChannels[member];
    ipc.handle(channel, (event, ...args) => {
        const why = refusalOf(event.senderFrame, origin());
        if (why !== null) {
            refused(channel, why);
            throw new RefusedSender();
        }
        return handler(...args);
    });
}
