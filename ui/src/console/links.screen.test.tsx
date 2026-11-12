import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations } from './state';

// A link the desktop parsed, taken by the console: the one a cold start left, asked once the console opens, and one that
// comes while the window is open. Each opens the panel or the screen it names, and nothing else: no turn, no text in a
// chat's box, no answer to what waits.

function renderConsole(desktop: StubDesktop, fake: FakeApi): void {
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
}

const panel = (name: string) => screen.getByRole('heading', { name, level: 1 }).closest<HTMLElement>('.painted')!;
/** The titles of the dock's tabs in front, one a group. */
const inFront = () =>
    Array.from(document.querySelectorAll('.dv-tab.dv-active-tab')).map((tab) => tab.textContent?.trim());
const calls = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name);

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    attention.detail.value = null;
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

describe('a link the desktop hands the console', () => {
    it('left by a cold start is taken once as the console opens, and opens Needs you at that approval, which waits for the person', async () => {
        const desktop = stubDesktop();
        desktop.held = { page: 'approval', id: 'apr_1' };
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/messages-pending')
            .answer('decision.list', 'decision.list/empty')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all')
            .answer('approval.get', 'approval.get/email-message');
        renderConsole(desktop, fake);
        const needs = await waitFor(() => panel('Needs you'));
        await waitFor(() => within(needs).getByText('Notes from the meetup'));
        expect(calls(desktop, 'links.take')).toHaveLength(1);
        expect(fake.calls.filter((each) => each.operation === 'approval.get').map((each) => each.body)).toEqual([
            { approval_id: 'apr_1' },
        ]);
        // Opened, never decided: the approval still waits, and no turn was sent.
        expect(
            fake.calls.some((each) => each.operation === 'approval.approve' || each.operation === 'approval.reject'),
        ).toBe(false);
        expect(calls(desktop, 'chat.send')).toEqual([]);
    });

    it('that comes while the window is open opens the screen it names in the page panel, and puts nothing into a chat', async () => {
        const desktop = stubDesktop();
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending', 'approval.list/by-campaign-pending')
            .answer('decision.list', 'decision.list/pending', 'decision.list/by-campaign-pending')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/some')
            .answer('campaign.get', 'campaign.get/details')
            .answer('workitem.list', 'workitem.list/by-campaign-planned', 'workitem.list/by-campaign-running')
            .answer('journal.list', 'journal.list/by-campaign');
        renderConsole(desktop, fake);
        await waitFor(() => expect(calls(desktop, 'links.take')).toHaveLength(1));
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        desktop.link({ page: 'campaign', id: 'cmp_1' });
        await waitFor(() => expect(document.querySelector('.harness .campaign-tabs')).not.toBeNull());
        expect(route.value).toMatchObject({ screen: 'campaigns', id: 'cmp_1' });
        expect(Object.values(chats.value).map((chat) => [chat.draft, chat.entries.length, chat.running])).toEqual([
            ['', 0, false],
        ]);
        expect(calls(desktop, 'chat.send')).toEqual([]);
        expect(calls(desktop, 'chat.create').filter((each) => each.args[0] === 'cmp_1')).toEqual([]);
        // A panel's link brings that panel forward.
        desktop.link({ page: 'campaigns' });
        await waitFor(() => expect(inFront()).toContain('Campaigns'));
        fireEvent.click(
            within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: 'Activity' }),
        );
        desktop.link({ page: 'needs' });
        await waitFor(() => expect(inFront()).toContain('Needs you'));
    });
});
