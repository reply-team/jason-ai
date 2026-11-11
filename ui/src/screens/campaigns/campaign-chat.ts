import { createContext, useContext } from 'react';

/** Opens a chat about a campaign. */
export type ChatAbout = (campaign: { id: string; name: string }) => void;

/**
 * The one thing a page shown inside the desktop is handed for a campaign's Chat tab: a function that opens a chat about
 * the campaign. The browser hands none, and then a campaign has no Chat tab.
 */
export const CampaignChat = createContext<ChatAbout | null>(null);

export function useCampaignChat(): ChatAbout | null {
    return useContext(CampaignChat);
}
