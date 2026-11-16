import { createContext, useContext } from 'react';

/** Opens a chat about a campaign, or with none a new chat where a campaign is set up. */
export type ChatAbout = (campaign?: { id: string; name: string }) => void;

/**
 * The one thing a page shown inside the desktop is handed to open a chat: about a campaign, for a campaign's Chat tab,
 * or a new one, for a list that holds nothing yet. The browser hands none, and then a campaign has no Chat tab and an
 * empty list says how to start in words alone.
 */
export const CampaignChat = createContext<ChatAbout | null>(null);

export function useCampaignChat(): ChatAbout | null {
    return useContext(CampaignChat);
}

/**
 * Set by a shell that draws an agent's header itself, above the page, and keeps it while the person moves between the
 * agent's tabs: the page then draws no header of its own, and what the header held that is about the agent's people
 * (how many it holds, the provider campaign it is linked to, Add people) is drawn in its People tab.
 */
export const AgentHeaded = createContext(false);

export function useAgentHeaded(): boolean {
    return useContext(AgentHeaded);
}
