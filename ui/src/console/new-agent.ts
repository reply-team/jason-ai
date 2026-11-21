import { signal } from '@preact/signals-react';
import { earlier } from '../platform/places';
import { reload as reloadCampaigns } from './campaigns';
import { isRefusal } from './chat';
import { conversations, here, messageOf } from './state';

// New GTM agent: the click makes the agent. The desktop makes a campaign named "New agent N" and its conversation, kept
// from that moment (`chat.createAgent`), and the agent's place opens on its Chat, where the empty chat asks Start's
// question. A desktop older than that member has none: the click shows Start, as it did there.

/** Whether an agent is being made now: the button takes no second click meanwhile. */
export const agentMaking = signal(false);

/**
 * A click that made no agent: the desktop's sentence and the click's own id, which Try again sends once more, so a
 * create that went through before its answer was lost gives the same agent back. Null when none waits.
 */
export const agentFailure = signal<{ clickId: string; said: string } | null>(null);

/** A click's own id, as the desktop takes it. */
const clickId = (): string => crypto.randomUUID();

/**
 * Makes the agent of a click and opens its place; a click made while one is being made is not taken. `again` is a
 * failed click's id, sent once more by Try again; a new click forgets a failed one.
 */
export async function newAgent(again?: string): Promise<void> {
    if (agentMaking.value) return;
    const { desktop, dock } = here();
    if (earlier(desktop).chat?.createAgent === undefined) return dock.show('start');
    const id = again ?? clickId();
    agentFailure.value = null;
    agentMaking.value = true;
    try {
        const made = await desktop.chat.createAgent(id);
        if (isRefusal(made)) {
            agentFailure.value = { clickId: id, said: made.refused };
            return;
        }
        // The conversation is kept from now: it is listed at once, so the agent's place opens on it.
        const { conversation, campaign } = made;
        conversations.value = [conversation, ...conversations.value.filter((each) => each.id !== conversation.id)];
        void reloadCampaigns();
        dock.chat({ id: campaign.id, name: campaign.name });
    } catch (error) {
        agentFailure.value = { clickId: id, said: messageOf(error) };
    } finally {
        agentMaking.value = false;
    }
}

/** Sends the failed click once more. */
export function tryAgentAgain(): void {
    const failed = agentFailure.value;
    if (failed !== null) void newAgent(failed.clickId);
}

/** Closes the failure line: its click is forgotten. */
export function forgetAgentFailure(): void {
    agentFailure.value = null;
}
