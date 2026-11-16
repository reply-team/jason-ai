import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { firstCampaignTab, route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { chatDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { nothingWaitsInChats } from '../test/waiting';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { chats, conversations } from './state';
import { bringForward } from '../test/area';

// A campaign's Chat tab, in the desktop's one area: the first tab of the agent's header, it opens the campaign's one
// conversation the desktop keeps, made on first use, as the Campaigns list's Chat does.

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

/** The console's reads, with Spring's page and the agent's header answered. */
function springApi(): FakeApi {
    return nothingWaitsInChats(new FakeApi())
        .answer('approval.list', 'approval.list/pending', 'approval.list/by-campaign-pending')
        .answer('decision.list', 'decision.list/pending', 'decision.list/by-campaign-pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/some')
        .answer('campaign.get', 'campaign.get/details')
        .answer('workitem.list', 'workitem.list/by-campaign-planned', 'workitem.list/by-campaign-running')
        .answer('journal.list', 'journal.list/by-campaign')
        .answer('campaign.list_contacts', 'campaign.list_contacts/everyone');
}

const painted = (name: string) => screen.getByRole('heading', { name, level: 1 }).closest<HTMLElement>('.painted')!;

async function openSpring(): Promise<HTMLElement> {
    await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
    // The sidebar brings the Campaigns panel forward, since a chat opened from it takes the front.
    const spring = async () => {
        bringForward('Agents');
        return (await waitFor(() => within(painted('Agents')).getByText('Spring'))).closest('tr')!;
    };
    fireEvent.click(within(await spring()).getByRole('button', { name: 'Open' }));
    return waitFor(() => {
        const header = document.querySelector<HTMLElement>('.agent-head');
        expect(header).not.toBeNull();
        return header!;
    });
}

/** Chooses one of the agent's tabs in its header. */
const choose = (header: HTMLElement, name: RegExp | string) =>
    fireEvent.click(within(header).getByRole('tab', { name }));

const about = (id: string) => Object.values(chats.value).filter((chat) => chat.campaignId === id);

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-16T09:00:00Z'));
    // The window's Live opens an agent on its Chat, as the console's mount learns from the desktop (loadMode).
    firstCampaignTab.value = 'chat';
    chats.value = {};
    conversations.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});
afterEach(() => {
    vi.useRealTimers();
    firstCampaignTab.value = 'sequence';
});

describe('a campaign’s Chat tab in the desktop', () => {
    it('opens the campaign’s one conversation, made on first use, and choosing Chat again brings that one forward', async () => {
        const desktop = renderConsole(springApi());
        // Open opens the agent on its first tab, its Chat: the conversation is made then.
        const header = await openSpring();
        await waitFor(() => expect(about('cmp_1')).toHaveLength(1));
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
        expect(within(header).getByRole('tab', { name: 'Chat' }).getAttribute('aria-selected')).toBe('true');

        // Another tab, then Chat again: the one conversation is brought forward, and nothing is made again.
        choose(header, /^People/);
        await waitFor(() => expect(route.value).toMatchObject({ screen: 'campaigns', id: 'cmp_1', tab: 'people' }));
        choose(header, 'Chat');
        await waitFor(() => expect(route.value).toMatchObject({ screen: 'campaigns', id: 'cmp_1', tab: 'chat' }));
        expect(creates(desktop, 'cmp_1')).toBe(1);
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
        bringForward('Agents');
        const row = (await waitFor(() => within(painted('Agents')).getByText('Spring'))).closest('tr')!;
        fireEvent.click(within(row).getByRole('button', { name: 'Chat' }));
        expect(await screen.findByText('The kept words about Spring')).toBeTruthy();
        expect(about('cmp_1').map((chat) => chat.id)).toEqual(['conv_01JDKEPT0000000000000000S1']);
        // Open shows the agent on its Chat tab: the same conversation, opened once and never made again.
        const header = await openSpring();
        expect(within(header).getByRole('tab', { name: 'Chat' }).getAttribute('aria-selected')).toBe('true');
        expect(about('cmp_1')).toHaveLength(1);
        expect(desktop.calls.filter((each) => each.name === 'chat.open')).toHaveLength(1);
    });
});
