import { computed } from '@preact/signals-react';
import { route, type CampaignTab, type Route } from '../router';
import type { Chat } from './chat-model';
import { activePanel, chats } from './state';

// An agent is one place in the console: its chat and its page's tabs under one header, each tab an address of its own.
// The area shows an agent while it shows that agent's chat (a conversation about a campaign, never the Feed) or the page
// at one of that agent's tabs. Everything else the area shows has no address of its own: it stands at the chat's address.

/** The address of an agent's Chat tab. */
export const agentChatRoute = (id: string): Extract<Route, { screen: 'campaigns' }> => ({
    screen: 'campaigns',
    id,
    tab: 'chat',
    view: 'summary',
    period: '7d',
    filters: {},
});

/** The agent a chat is about: its campaign, for any chat but the Feed and a set-up chat. */
export const agentOfChat = (chat: Chat | undefined): string | null =>
    chat === undefined || chat.main ? null : chat.campaignId;

/** The agent's Chat tab an address names, or null. */
export const agentChatOf = (to: Route): string | null =>
    to.screen === 'campaigns' && to.id !== undefined && to.tab === 'chat' ? to.id : null;

/**
 * Whether an address names a place of its own. The chat's address is the console's neutral place, and the runtime's
 * bare default (`#/attention`, which the window opens at and the console never writes) names none either.
 */
export const namesPlace = (to: Route): boolean =>
    to.screen !== 'chat' && !(to.screen === 'attention' && !(to.kind && to.id));

/** The agent the area shows and the tab it shows it on; null while the area shows anything else. */
export const shownAgent = computed<{ id: string; tab: CampaignTab } | null>(() => {
    const pane = activePanel.value;
    if (pane === null) return null;
    if (pane.startsWith('chat:')) {
        const id = agentOfChat(chats.value[pane.slice('chat:'.length)]);
        return id === null ? null : { id, tab: 'chat' };
    }
    if (pane !== 'page') return null;
    const at = route.value;
    return at.screen === 'campaigns' && at.id !== undefined ? { id: at.id, tab: at.tab } : null;
});
