/**
 * The chat's three calls from the page, each behind the origin check every call the page makes passes: a send, a stop
 * and a person's answer to a card. Nothing the page sends is trusted further than the chat checks it.
 */

import type { Chat } from './chat.ts';
import { handle, type IpcRegistrar, type Refusal } from './ipc.ts';

export function registerChat(ipc: IpcRegistrar, chat: Pick<Chat, 'send' | 'stop' | 'answer'>, origin: () => string | null, refused: (channel: string, why: Refusal) => void): void {
    handle(ipc, 'chat.send', origin, (chatId, text, options) => chat.send(chatId, text, options), refused);
    handle(ipc, 'chat.stop', origin, (chatId) => chat.stop(chatId), refused);
    handle(ipc, 'chat.answer', origin, (chatId, requestId, decision) => chat.answer(chatId, requestId, decision), refused);
}
