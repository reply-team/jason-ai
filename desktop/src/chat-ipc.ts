/**
 * The chat's calls from the page, each behind the origin check every call the page makes passes: a send, a stop, a
 * person's answer to a card, and the conversations the desktop keeps (list, open, create, remove, start afresh, and
 * attach a set-up chat to a campaign it made). Nothing the page sends is trusted further than the chat checks it.
 */

import type { Chat } from './chat.ts';
import { handle, type IpcRegistrar, type Refusal } from './ipc.ts';

export type ChatCalls = Pick<Chat, 'send' | 'stop' | 'answer' | 'list' | 'open' | 'create' | 'remove' | 'restart' | 'attach'>;

export function registerChat(ipc: IpcRegistrar, chat: ChatCalls, origin: () => string | null, refused: (channel: string, why: Refusal) => void): void {
    handle(ipc, 'chat.send', origin, (chatId, text, options) => chat.send(chatId, text, options), refused);
    handle(ipc, 'chat.stop', origin, (chatId) => chat.stop(chatId), refused);
    handle(ipc, 'chat.answer', origin, (chatId, requestId, decision) => chat.answer(chatId, requestId, decision), refused);
    handle(ipc, 'chat.list', origin, () => chat.list(), refused);
    handle(ipc, 'chat.open', origin, (chatId) => chat.open(chatId), refused);
    handle(ipc, 'chat.create', origin, (campaignId) => chat.create(campaignId), refused);
    handle(ipc, 'chat.remove', origin, (chatId) => chat.remove(chatId), refused);
    handle(ipc, 'chat.restart', origin, (chatId) => chat.restart(chatId), refused);
    handle(ipc, 'chat.attach', origin, (chatId, campaignId) => chat.attach(chatId, campaignId), refused);
}
