/**
 * The chat's calls from the page, each behind the origin check every call the page makes passes: a send, a stop, a
 * person's answer to a card, the conversations the desktop keeps (list, open, create, remove, start afresh, attach a
 * set-up chat to a campaign it made, rename one, and choose its mode, whose Full access the desktop confirms with the
 * person itself), the Feed with the journal lines the page hands it, and the standing choice of Full access, which the
 * page may read and take back and never give: only the person's answer to the desktop's own box gives it. Nothing the
 * page sends is trusted further than the chat checks it.
 *
 * A refusal is answered as a value, `{ refused }` with the desktop's sentence alone, and a call that did what it was
 * asked and has nothing to say answers null. A refusal thrown across the seam would reach the page wrapped: Electron
 * 44.10.2 replies to a handler that throws with `event._replyChannel.sendReply({ error: error.toString() })`
 * (lib/browser/ipc-dispatch.ts), and the page's `ipcRenderer.invoke` rejects with
 * `new Error(`Error invoking remote method '${channel}': ${error}`)` (lib/renderer/api/ipc-renderer.ts), so the page
 * would show "Error invoking remote method 'jason:chat.remove': ChatRefused: …" for the desktop's sentence. A throw of
 * anything that is no refusal stays a throw.
 */

import { ChatRefused, type Chat } from './chat.ts';
import { handle, type IpcRegistrar, type Refusal } from './ipc.ts';

export type ChatCalls = Pick<
    Chat,
    'send' | 'stop' | 'answer' | 'list' | 'open' | 'create' | 'remove' | 'restart' | 'attach' | 'rename' | 'setMode' | 'openEndedFolder' | 'feed' | 'fullAccessStanding' | 'takeBackFullAccess'
>;

/** What the page is answered with when the chat refused a call: `DesktopRefusal` on the page's side. */
export interface ChatRefusal {
    refused: string;
}

/** The call's answer, a refusal as a value, and null for a call with nothing to answer. */
async function answered(call: () => unknown): Promise<unknown> {
    try {
        const value = await call();
        return value === undefined ? null : value;
    } catch (error) {
        if (error instanceof ChatRefused) return { refused: error.message } satisfies ChatRefusal;
        throw error;
    }
}

export function registerChat(ipc: IpcRegistrar, chat: ChatCalls, origin: () => string | null, refused: (channel: string, why: Refusal) => void): void {
    handle(ipc, 'chat.send', origin, (chatId, text, options) => answered(() => chat.send(chatId, text, options)), refused);
    handle(ipc, 'chat.stop', origin, (chatId) => chat.stop(chatId), refused);
    handle(ipc, 'chat.answer', origin, (chatId, requestId, decision) => chat.answer(chatId, requestId, decision), refused);
    handle(ipc, 'chat.list', origin, () => chat.list(), refused);
    handle(ipc, 'chat.open', origin, (chatId) => answered(() => chat.open(chatId)), refused);
    handle(ipc, 'chat.create', origin, (campaignId, host) => answered(() => chat.create(campaignId, host)), refused);
    handle(ipc, 'chat.remove', origin, (chatId) => answered(() => chat.remove(chatId)), refused);
    handle(ipc, 'chat.restart', origin, (chatId) => answered(() => chat.restart(chatId)), refused);
    handle(ipc, 'chat.attach', origin, (chatId, campaignId) => answered(() => chat.attach(chatId, campaignId)), refused);
    handle(ipc, 'chat.rename', origin, (chatId, title) => answered(() => chat.rename(chatId, title)), refused);
    handle(ipc, 'chat.setMode', origin, (chatId, mode, host) => answered(() => chat.setMode(chatId, mode, host)), refused);
    handle(ipc, 'chat.openFolder', origin, (name) => answered(() => chat.openEndedFolder(name)), refused);
    handle(ipc, 'chat.feed', origin, (lines, options) => answered(() => chat.feed(lines, options)), refused);
    // The page reads the standing choice and takes it back; neither call takes an argument, so neither can give it.
    handle(ipc, 'fullAccess.standing', origin, () => chat.fullAccessStanding(), refused);
    handle(ipc, 'fullAccess.takeBack', origin, () => answered(() => chat.takeBackFullAccess()), refused);
}
