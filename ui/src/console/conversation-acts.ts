import { can } from '../platform/places';
import { openConversation, restartChat } from './chat';
import { conversationItems, type ConversationAct, type ConversationFacts, type Gates } from './conversation-items';
import { changeChat, chats, conversations, inTab } from './state';

// The conversation's acts as they are taken, wherever they are offered: the agent's "…" and the menu on a row of Recent.
// Each shows the conversation first and begins there, so the person sees what is renamed, started fresh or removed.

/** What the place lets the person do, read now. */
export function placeGates(): Gates {
    return {
        create: can('chat.create'),
        rename: can('chat.rename'),
        restart: can('chat.restart'),
        remove: can('chat.remove'),
        tab: inTab(),
    };
}

/** A listed conversation as the console knows it now: kept, and whether its chat runs or is being removed. */
export function factsOf(id: string): ConversationFacts {
    const listed = conversations.value.find((each) => each.id === id && each.main !== true);
    const chat = chats.value[id];
    return {
        kept: listed?.id,
        id,
        running: chat?.running ?? listed?.running ?? false,
        removing: chat?.removing === true,
    };
}

/**
 * Shows the conversation, then begins one of its acts there: the box to rename it, its fresh start, or the question
 * before removing it. Whether the act may be taken is decided again once the conversation is shown, by the rule its menu
 * holds it by, so nothing is started fresh or removed while its turn runs, even where that was not known before.
 */
export async function begin(conversation: string, act: ConversationAct, show: (held: string) => void): Promise<void> {
    const held = await openConversation(conversation);
    if (held === null) return;
    show(held);
    const item = conversationItems(factsOf(held), placeGates()).find((each) => each.kind === act);
    if (item === undefined || !('disabled' in item) || item.disabled) return;
    if (act === 'fresh') restartChat(held);
    else
        changeChat(held, (each) =>
            act === 'rename' ? { ...each, renaming: each.title } : { ...each, removing: true },
        );
}
