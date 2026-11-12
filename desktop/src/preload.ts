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
        create: (campaignId?: string) => ipcRenderer.invoke('jason:chat.create', campaignId),
        remove: (chatId: string) => ipcRenderer.invoke('jason:chat.remove', chatId),
        restart: (chatId: string) => ipcRenderer.invoke('jason:chat.restart', chatId),
        attach: (chatId: string, campaignId: string) => ipcRenderer.invoke('jason:chat.attach', chatId, campaignId),
        rename: (chatId: string, title: string) => ipcRenderer.invoke('jason:chat.rename', chatId, title),
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
        onChanged: (handler: Handler) => subscribe('jason:setup.changed', handler),
    },
};

/** What the preload exposes, for the test that holds it to the page's declaration; a type only, so nothing is exported. */
export type DesktopPreload = typeof desktop;

contextBridge.exposeInMainWorld('jasonDesktop', desktop);
