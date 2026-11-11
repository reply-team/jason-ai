import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { stubDesktop, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { said } from './chat-model';
import { changeChat, chats } from './state';

// A campaign's Chat tab, in the page panel of the desktop: it brings forward the newest chat about the campaign the
// console holds, and opens one only when there is none.

function renderConsole(fake: FakeApi) {
    watch(fake);
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'attention' };
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    fake.answer('system.info', 'system.info/with-ui');
    render(<Console api={fake} desktop={stubDesktop()} stream={stubStream().stream} />);
}

/** The console, and Spring opened in the page panel on its Sequence, as the Campaigns panel's Open does. */
function springApi(): FakeApi {
    return new FakeApi()
        .answer('approval.list', 'approval.list/pending', 'approval.list/by-campaign-pending')
        .answer('decision.list', 'decision.list/pending', 'decision.list/by-campaign-pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/some')
        .answer('campaign.get', 'campaign.get/details')
        .answer('workitem.list', 'workitem.list/by-campaign-planned', 'workitem.list/by-campaign-running')
        .answer('journal.list', 'journal.list/by-campaign');
}

const painted = (name: string) => screen.getByRole('heading', { name, level: 1 }).closest<HTMLElement>('.painted')!;

async function openSpring(chatsFirst = 0): Promise<HTMLElement> {
    await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
    // The sidebar brings the Campaigns panel forward each time, since a chat opened from it takes the front.
    const spring = async () => {
        fireEvent.click(
            within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: 'Campaigns' }),
        );
        return (await waitFor(() => within(painted('Campaigns')).getByText('Spring'))).closest('tr')!;
    };
    for (let n = 0; n < chatsFirst; n++) fireEvent.click(within(await spring()).getByRole('button', { name: 'Chat' }));
    fireEvent.click(within(await spring()).getByRole('button', { name: 'Open' }));
    return waitFor(() => {
        const tabs = document.querySelector<HTMLElement>('.harness .campaign-tabs');
        expect(tabs).not.toBeNull();
        return tabs!;
    });
}

const about = (id: string) => Object.values(chats.value).filter((chat) => chat.campaignId === id);

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-16T09:00:00Z'));
    chats.value = {};
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});
afterEach(() => {
    vi.useRealTimers();
});

describe('a campaign’s Chat tab in the desktop', () => {
    it('opens a chat about a campaign that has none, and a second click brings that one forward', async () => {
        renderConsole(springApi());
        const tabs = await openSpring();
        expect(about('cmp_1')).toEqual([]);

        fireEvent.click(within(tabs).getByRole('button', { name: 'Chat' }));
        await waitFor(() => expect(about('cmp_1')).toHaveLength(1));
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));

        fireEvent.click(within(tabs).getByRole('button', { name: 'Chat' }));
        expect(about('cmp_1')).toHaveLength(1);
    });

    it('brings forward a chat about the campaign the console already holds, and opens none', async () => {
        renderConsole(springApi());
        // Two chats about Spring from the Campaigns list's own Chat, which opens a new one each time.
        const tabs = await openSpring(2);
        expect(about('cmp_1')).toHaveLength(2);
        const held = Object.keys(chats.value);
        const [older, newest] = about('cmp_1');
        changeChat(older!.id, (chat) => said(chat, 'The older chat about Spring'));
        changeChat(newest!.id, (chat) => said(chat, 'The newest chat about Spring'));

        fireEvent.click(within(tabs).getByRole('button', { name: 'Chat' }));

        expect(Object.keys(chats.value)).toEqual(held);
        expect(await screen.findByText('The newest chat about Spring')).toBeTruthy();
        expect(screen.queryByText('The older chat about Spring')).toBeNull();
    });
});
