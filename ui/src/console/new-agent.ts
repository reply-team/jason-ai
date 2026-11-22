import { signal } from '@preact/signals-react';
import { earlier } from '../platform/places';
import { reload as reloadCampaigns } from './campaigns';
import { holdOpening, isRefusal } from './chat';
import { startWaiting } from './start';
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
 * The id of the last click that made no agent, kept after its line is closed: until a click makes its agent, a new click
 * is that one once more, so a create that went through before its answer was lost gives the same agent back.
 */
let unsettled: string | null = null;

/**
 * Makes the agent of a click and opens its place; a click made while one is being made is not taken. `again` is a
 * failed click's id, sent once more by Try again; a new click sends it too, whether or not its line was closed.
 */
export async function newAgent(again?: string): Promise<void> {
    if (agentMaking.value) return;
    const { desktop, dock } = here();
    if (earlier(desktop).chat?.createAgent === undefined) return dock.show('start');
    // Until a click made its agent, a new click is that click once more: if its create went through after all, the same
    // agent comes back, never a second.
    const id = again ?? unsettled ?? clickId();
    unsettled = id;
    agentFailure.value = null;
    agentMaking.value = true;
    // A message Start holds for an AI to be connected is taken at the click, so an AI that connects while the agent is
    // made never sends it into a set-up chat. It moves with the person into the agent's opening: one message in one
    // place, shown there with Take back, and sent into this agent once an AI is there. A click that made nothing puts it
    // back on Start.
    const waiting = startWaiting.value;
    startWaiting.value = null;
    const putBack = () => {
        if (waiting !== null && startWaiting.value === null) startWaiting.value = waiting;
    };
    try {
        const made = await desktop.chat.createAgent(id);
        if (isRefusal(made)) {
            putBack();
            agentFailure.value = { clickId: id, said: made.refused };
            return;
        }
        unsettled = null;
        // The conversation is kept from now: it is listed at once, so the agent's place opens on it.
        const { conversation, campaign } = made;
        if (waiting !== null) holdOpening(conversation.id, waiting);
        conversations.value = [conversation, ...conversations.value.filter((each) => each.id !== conversation.id)];
        void reloadCampaigns();
        dock.chat({ id: campaign.id, name: campaign.name });
    } catch (error) {
        putBack();
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

/** Closes the failure line. Its click is kept: the next click sends it once more, until one makes its agent. */
export function forgetAgentFailure(): void {
    agentFailure.value = null;
}
