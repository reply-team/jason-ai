// The page's side of the desktop: `window.jasonDesktop`, and nothing else. A sandboxed preload may require only
// Electron, so the channel names are written here as they are in ipc.ts; a test holds the two lists together. No event
// object, no ipcRenderer and no Node reaches the page: each member passes its own arguments and returns the answer.

import { contextBridge, ipcRenderer, type IpcRendererEvent } from 'electron';

type Handler = (...args: never[]) => void;

function subscribe(channel: string, handler: Handler): () => void {
    const listener = (_event: IpcRendererEvent, ...args: unknown[]) => (handler as (...values: unknown[]) => void)(...args);
    ipcRenderer.on(channel, listener);
    return () => {
        ipcRenderer.removeListener(channel, listener);
    };
}

const platform: 'darwin' | 'win32' | 'linux' = process.platform === 'darwin' || process.platform === 'win32' ? process.platform : 'linux';

const desktop = {
    platform,
    onCommand: (handler: Handler) => subscribe('jason:command', handler),
    chat: {
        send: (chatId: string, text: string, options: unknown) => ipcRenderer.invoke('jason:chat.send', chatId, text, options),
        stop: (chatId: string) => ipcRenderer.invoke('jason:chat.stop', chatId),
        answer: (chatId: string, requestId: string, decision: unknown) => ipcRenderer.invoke('jason:chat.answer', chatId, requestId, decision),
        onEvent: (handler: Handler) => subscribe('jason:chat.event', handler),
        list: () => ipcRenderer.invoke('jason:chat.list'),
        open: (chatId: string) => ipcRenderer.invoke('jason:chat.open', chatId),
        create: (campaignId?: string, host?: string) => ipcRenderer.invoke('jason:chat.create', campaignId, host),
        remove: (chatId: string) => ipcRenderer.invoke('jason:chat.remove', chatId),
        restart: (chatId: string) => ipcRenderer.invoke('jason:chat.restart', chatId),
        attach: (chatId: string, campaignId: string) => ipcRenderer.invoke('jason:chat.attach', chatId, campaignId),
        rename: (chatId: string, title: string) => ipcRenderer.invoke('jason:chat.rename', chatId, title),
        setMode: (chatId: string, mode: string, host: string) => ipcRenderer.invoke('jason:chat.setMode', chatId, mode, host),
        openFolder: (name: string) => ipcRenderer.invoke('jason:chat.openFolder', name),
        feed: (lines: unknown, options?: unknown) => ipcRenderer.invoke('jason:chat.feed', lines, options),
    },
    fullAccess: {
        standing: () => ipcRenderer.invoke('jason:fullAccess.standing'),
        takeBack: () => ipcRenderer.invoke('jason:fullAccess.takeBack'),
        onChanged: (handler: Handler) => subscribe('jason:fullAccess.changed', handler),
    },
    theme: {
        get: () => ipcRenderer.invoke('jason:theme.get'),
        set: (choice: 'system' | 'light' | 'dark') => ipcRenderer.invoke('jason:theme.set', choice),
    },
    dailyTrends: {
        get: () => ipcRenderer.invoke('jason:dailyTrends.get'),
        set: (on: boolean) => ipcRenderer.invoke('jason:dailyTrends.set', on),
    },
    mode: {
        get: () => ipcRenderer.invoke('jason:mode.get'),
        set: (mode: 'live' | 'demo') => ipcRenderer.invoke('jason:mode.set', mode),
        startOver: () => ipcRenderer.invoke('jason:mode.startOver'),
        onPreparing: (handler: Handler) => subscribe('jason:mode.preparing', handler),
    },
    openIn: {
        targets: (campaignId?: string, chatId?: string) => ipcRenderer.invoke('jason:openIn.targets', campaignId, chatId),
        open: (target: string, campaignId?: string) => ipcRenderer.invoke('jason:openIn.open', target, campaignId),
        continueChat: (chatId: string) => ipcRenderer.invoke('jason:openIn.continueChat', chatId),
    },
    pairing: {
        paired: (tokenId: string) => ipcRenderer.invoke('jason:pairing.paired', tokenId),
    },
    links: {
        take: () => ipcRenderer.invoke('jason:links.take'),
        onOpen: (handler: Handler) => subscribe('jason:links.open', handler),
    },
    setup: {
        status: () => ipcRenderer.invoke('jason:setup.status'),
        connectReply: () => ipcRenderer.invoke('jason:setup.connectReply'),
        connectReplyWithKey: (key: string) => ipcRenderer.invoke('jason:setup.connectReplyWithKey', key),
        setProviderKey: (providerId: string, key: string) => ipcRenderer.invoke('jason:setup.setProviderKey', providerId, key),
        installProgram: (programId: string) => ipcRenderer.invoke('jason:setup.installProgram', programId),
        configurePlugin: (pluginId: string, values: unknown) => ipcRenderer.invoke('jason:setup.configurePlugin', pluginId, values),
        disconnectPlugin: (pluginId: string) => ipcRenderer.invoke('jason:setup.disconnectPlugin', pluginId),
        replyConnect: (channel: 'email' | 'linkedin', options: unknown) => ipcRenderer.invoke('jason:setup.replyConnect', channel, options),
        replyReconnect: (channel: 'email' | 'linkedin', accountId: string, options?: unknown) => ipcRenderer.invoke('jason:setup.replyReconnect', channel, accountId, options),
        replyLimits: (accountId: string, daily?: number) => ipcRenderer.invoke('jason:setup.replyLimits', accountId, daily),
        replyPause: (accountId: string) => ipcRenderer.invoke('jason:setup.replyPause', accountId),
        replyResume: (accountId: string) => ipcRenderer.invoke('jason:setup.replyResume', accountId),
        replyUnblock: (accountId: string) => ipcRenderer.invoke('jason:setup.replyUnblock', accountId),
        replyDelete: (channel: 'email' | 'linkedin', accountId: string, options?: unknown) => ipcRenderer.invoke('jason:setup.replyDelete', channel, accountId, options),
        onChanged: (handler: Handler) => subscribe('jason:setup.changed', handler),
    },
};

/** What the preload exposes, for the test that holds it to the page's declaration; a type only, so nothing is exported. */
export type DesktopPreload = typeof desktop;

contextBridge.exposeInMainWorld('jasonDesktop', desktop);
