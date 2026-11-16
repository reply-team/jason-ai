import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { firstCampaignTab, route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { inFront, bringForward } from '../test/area';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations } from './state';

// A link the desktop parsed, taken by the console: the one a cold start left, asked once the console opens, and one that
// comes while the window is open. Each opens the panel or the screen it names, and nothing else: no turn, no text in a
// chat's box, no answer to what waits.

function renderConsole(desktop: StubDesktop, fake: FakeApi): void {
    // Needs you reads the sending name under an email's From (`needs-sender-line.screen.test.tsx`); here it is answered
    // with one recorded list of accounts, which draws one line and holds nothing.
    if (!fake.offers('sender.list')) fake.answerAnyRequest('sender.list', 'sender.list/card-message-sender');
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
const calls = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name);

afterEach(() => {
    firstCampaignTab.value = 'sequence';
});

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
            .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message');
        renderConsole(desktop, fake);
        const needs = await waitFor(() => panel('Needs you'));
        await waitFor(() => within(needs).getByText('Notes from the meetup'));
        expect(calls(desktop, 'links.take')).toHaveLength(1);
        expect(attention.detail.value?.item.id).toBe('apr_1');
        // The link opens the approval it names; each row is read whole once besides, for its Approve.
        const read = (id: string) =>
            fake.calls.filter(
                (each) =>
                    each.operation === 'approval.get' && (each.body as { approval_id?: string }).approval_id === id,
            );
        await waitFor(() => expect(read('apr_2')).toHaveLength(1));
        expect(read('apr_1')).toHaveLength(2);
        expect(fake.calls.filter((each) => each.operation === 'approval.get')).toHaveLength(3);
        // Opened, never decided: the approval still waits, and no turn was sent.
        expect(
            fake.calls.some((each) => each.operation === 'approval.approve' || each.operation === 'approval.reject'),
        ).toBe(false);
        expect(calls(desktop, 'chat.send')).toEqual([]);
    });

    it('that comes while the window is open opens the screen it names in the area, and puts nothing into a chat', async () => {
        const desktop = stubDesktop();
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending', 'approval.list/by-campaign-pending')
            .answer('approval.get', 'approval.get/pending-with-preview', 'approval.get/other-pending')
            .answer('decision.list', 'decision.list/pending', 'decision.list/by-campaign-pending')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/some')
            .answer('campaign.get', 'campaign.get/details')
            .answer('workitem.list', 'workitem.list/by-campaign-planned', 'workitem.list/by-campaign-running')
            .answer('journal.list', 'journal.list/by-campaign')
            // The agent's Chat draws the questions and approvals waiting on it, read and never answered.
            .answerAnyRequest('decision.get', 'decision.get/for-campaign-row')
            .answerAnyRequest('sender.list', 'sender.list/card-message-sender');
        renderConsole(desktop, fake);
        await waitFor(() => expect(calls(desktop, 'links.take')).toHaveLength(1));
        // The window opens on Start, with no chat made.
        await waitFor(() => screen.getByRole('heading', { name: 'Who do you want to reach?' }));
        // In the window an agent opens on its Chat, as the window's mode sets it.
        firstCampaignTab.value = 'chat';
        // An agent's link opens it under its header on its Chat, at its own address, and makes nothing: no conversation is
        // asked of the desktop, none is listed, and nothing is sent; its conversation waits for the person's first act.
        desktop.link({ page: 'campaign', id: 'cmp_1' });
        const header = await waitFor(() => {
            const found = document.querySelector<HTMLElement>('.agent-head');
            expect(found).not.toBeNull();
            return found!;
        });
        await waitFor(() => expect(location.hash).toBe('#/campaigns/cmp_1/chat'));
        expect(within(header).getByRole('tab', { name: 'Chat' }).getAttribute('aria-selected')).toBe('true');
        expect(calls(desktop, 'chat.send')).toEqual([]);
        expect(calls(desktop, 'chat.create')).toEqual([]);
        expect(conversations.value.filter((each) => each.campaignId === 'cmp_1')).toEqual([]);
        // A panel's link brings that panel forward.
        desktop.link({ page: 'campaigns' });
        await waitFor(() => expect(inFront()).toBe('Agents'));
        bringForward('Activity');
        desktop.link({ page: 'needs' });
        await waitFor(() => expect(inFront()).toBe('Needs you'));
    });

    it('to an approval that no longer waits opens Needs you on its own empty card, and nothing else', async () => {
        const desktop = stubDesktop();
        desktop.held = { page: 'approval', id: 'apr_2' };
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/empty')
            .answer('decision.list', 'decision.list/empty')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all')
            .answer('approval.get', 'approval.get/other-pending');
        renderConsole(desktop, fake);
        const needs = await waitFor(() => panel('Needs you'));
        await waitFor(() => expect(fake.calls.some((each) => each.operation === 'approval.get')).toBe(true));
        await waitFor(() => expect(needs.querySelector('.needs-empty')).not.toBeNull());
        // The panel read the item the link names, and it is Needs you that is in front, no page of its own.
        await waitFor(() => expect(attention.detail.value?.item.id).toBe('apr_2'));
        expect(inFront()).toBe('Needs you');
        // What the screen says when nothing waits, and no detail, no button to decide, no turn.
        expect(needs.querySelector('.ask-w')).toBeNull();
        expect(within(needs).queryByRole('button', { name: 'Approve' })).toBeNull();
        expect(
            fake.calls.some((each) => each.operation === 'approval.approve' || each.operation === 'approval.reject'),
        ).toBe(false);
        expect(calls(desktop, 'chat.send')).toEqual([]);
    });

    it('leaves an open chat exactly as it was: its host, model, effort, mode and words in its box', async () => {
        const desktop = stubDesktop();
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending')
            .answer('decision.list', 'decision.list/pending')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all')
            .answer('approval.get', 'approval.get/pending-with-preview', 'approval.get/other-pending')
            .answer('decision.get', 'decision.get/pending-with-options');
        renderConsole(desktop, fake);
        await waitFor(() => expect(calls(desktop, 'links.take')).toHaveLength(1));
        const side = within(document.querySelector<HTMLElement>('.side')!);
        fireEvent.click(side.getByRole('button', { name: 'More to create' }));
        fireEvent.click(side.getByRole('menuitem', { name: /^New chat/ }));
        const box = await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        fireEvent.change(box, { target: { value: 'Half a thought' } });
        const [id] = Object.keys(chats.value);
        const before = structuredClone(chats.value[id!]);
        expect(before?.draft).toBe('Half a thought');
        for (const link of [{ page: 'campaigns' }, { page: 'needs' }, { page: 'question', id: 'dec_1' }] as const)
            desktop.link(link);
        await waitFor(() => expect(inFront()).toBe('Needs you'));
        expect(chats.value[id!]).toEqual(before);
        expect(calls(desktop, 'chat.send')).toEqual([]);
    });

    it("to the Feed opens the Feed, the cold start's link waiting until the desktop has answered for it", async () => {
        const desktop = stubDesktop();
        desktop.held = { page: 'feed' };
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending')
            .answer('decision.list', 'decision.list/pending')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all');
        renderConsole(desktop, fake);
        await waitFor(() => screen.getByRole('heading', { name: 'The Feed' }));
        expect(inFront()).toBe('Feed');
        expect(calls(desktop, 'chat.send')).toEqual([]);
        expect(calls(desktop, 'chat.create')).toEqual([]);
    });
});
