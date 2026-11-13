import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { chatDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { chats, conversations } from './state';

// A campaign's Chat tab, in the page panel of the desktop: it opens the campaign's one conversation the desktop keeps,
// made on first use, as the Campaigns list's Chat does.

function renderConsole(fake: FakeApi, desktop: StubDesktop = chatDesktop()): StubDesktop {
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
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    return desktop;
}

const creates = (desktop: StubDesktop, campaignId: string) =>
    desktop.calls.filter((each) => each.name === 'chat.create' && each.args[0] === campaignId).length;

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

async function openSpring(): Promise<HTMLElement> {
    await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
    // The sidebar brings the Campaigns panel forward, since a chat opened from it takes the front.
    const spring = async () => {
        fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: 'Agents' }));
        return (await waitFor(() => within(painted('Agents')).getByText('Spring'))).closest('tr')!;
    };
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
    conversations.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});
afterEach(() => {
    vi.useRealTimers();
});

describe('a campaign’s Chat tab in the desktop', () => {
    it('opens the campaign’s one conversation, made on first use, and a second click brings that one forward', async () => {
        const desktop = renderConsole(springApi());
        const tabs = await openSpring();
        expect(about('cmp_1')).toEqual([]);

        fireEvent.click(within(tabs).getByRole('button', { name: 'Chat' }));
        await waitFor(() => expect(about('cmp_1')).toHaveLength(1));
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));

        fireEvent.click(within(tabs).getByRole('button', { name: 'Chat' }));
        await waitFor(() => expect(creates(desktop, 'cmp_1')).toBe(2));
        expect(about('cmp_1')).toHaveLength(1);
        expect(Object.keys(chats.value)).toHaveLength(2); // the set-up chat the window opened on, and Spring's
    });

    it('opens the conversation the desktop keeps for the campaign with what the page drew, from the tab and the list alike', async () => {
        const desktop = chatDesktop();
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000S1',
            campaignId: 'cmp_1',
            title: 'Spring',
            entries: [{ kind: 'user', text: 'The kept words about Spring' }],
        });
        renderConsole(springApi(), desktop);
        // The list's Chat first: the campaign's one conversation, opened with its kept words.
        await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
        fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: 'Agents' }));
        const row = (await waitFor(() => within(painted('Agents')).getByText('Spring'))).closest('tr')!;
        fireEvent.click(within(row).getByRole('button', { name: 'Chat' }));
        expect(await screen.findByText('The kept words about Spring')).toBeTruthy();
        expect(about('cmp_1').map((chat) => chat.id)).toEqual(['conv_01JDKEPT0000000000000000S1']);
        const tabs = await openSpring();

        fireEvent.click(within(tabs).getByRole('button', { name: 'Chat' }));
        await waitFor(() => expect(creates(desktop, 'cmp_1')).toBe(2));
        expect(about('cmp_1')).toHaveLength(1);
        expect(desktop.calls.filter((each) => each.name === 'chat.open')).toHaveLength(1);
    });
});
