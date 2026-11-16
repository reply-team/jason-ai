import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { chatDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { shownPane } from '../test/area';
import { FakeApi, watch } from '../test/fake-api';
import { fixtures } from '../test/fixtures';
import { nothingWaitsInChats } from '../test/waiting';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { chats, conversations } from './state';

// A campaign's chat carries the agent's state and its one move, Start, Pause or Resume, in the chat's bar: the agent
// page's own control and its own confirmation, drawn from the campaign's state as the runtime lists it. A set-up chat
// carries none until it becomes a campaign's.

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

function consoleApi(): FakeApi {
    return nothingWaitsInChats(new FakeApi())
        .answer('approval.list', 'approval.list/pending', 'approval.list/by-campaign-pending')
        .answer('decision.list', 'decision.list/pending', 'decision.list/by-campaign-pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen');
}

const side = () => document.querySelector<HTMLElement>('.side')!;
const listed = () => within(side()).getByRole('navigation', { name: 'Conversations' });
const calls = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name);
const sent = (api: FakeApi, operation: string) =>
    api.calls.filter((c) => c.operation === operation).map((c) => c.body as Record<string, unknown>);
/** The bar of the chat the area shows. */
const bar = () => shownPane()!.querySelector<HTMLElement>('.chat-bar')!;
/** The agent's part of the bar: its state and its move. */
const agent = () => within(bar()).queryByRole('group', { name: 'This agent' });

/** Opens a conversation the desktop keeps for a campaign, from the sidebar's list. */
async function openKept(desktop: StubDesktop, campaignId: string, title: string): Promise<void> {
    desktop.keep({ id: `conv_01JDKEPT00000000000000000${campaignId.slice(-1)}`, campaignId, title });
    await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
    fireEvent.click(await waitFor(() => within(listed()).getByRole('button', { name: title })));
    await waitFor(() => expect(agent()).not.toBeNull());
}

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

describe('the agent’s move in its chat', () => {
    it('a draft’s chat says Draft and offers Start, which opens the agent page’s own confirmation', async () => {
        const desktop = chatDesktop();
        const fake = consoleApi()
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/unlinked')
            .answer('campaign.get', 'campaign.get/unlinked');
        renderConsole(desktop, fake);
        await openKept(desktop, 'cmp_2', 'Autumn');

        const group = agent()!;
        expect(within(group).getByText('Draft')).toBeTruthy();
        expect(
            within(group)
                .getAllByRole('button')
                .map((each) => each.textContent?.trim()),
        ).toEqual(['Start']);
        // The bar's own buttons are as they were, the agent's move after them.
        expect(
            within(bar())
                .getAllByRole('button')
                .map((each) => each.textContent?.trim()),
        ).toEqual(['Rename', 'Start afresh', 'Remove', 'Start']);

        fireEvent.click(within(group).getByRole('button', { name: 'Start' }));
        const dialog = await screen.findByRole('dialog', { name: 'Start Autumn' });
        // The browser shell's dialog keeps its own stylesheet's scope inside the console.
        expect(dialog.closest('.harness')).not.toBeNull();
        expect(dialog.textContent).toContain("Jason starts releasing this agent's work.");
        const rule = await within(dialog).findByRole('region', { name: 'What starting releases' });
        expect(rule.textContent).toContain('2 people are in this agent.');
        expect(rule.textContent).toContain(
            'People who have not replied yet: Every message waits for you · the default, since nothing is chosen',
        );
        fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
        expect(sent(fake, 'campaign.start')).toEqual([]);
    });

    it('a live agent’s chat says Active and offers Pause, saying what the provider keeps doing, and pauses with the reason', async () => {
        const desktop = chatDesktop();
        const fake = consoleApi()
            .answer('campaign.summary', 'campaign.summary/all')
            .answer('campaign.get', 'campaign.get/details')
            .answer('campaign.pause', 'campaign.pause/with-reason');
        renderConsole(desktop, fake);
        await openKept(desktop, 'cmp_1', 'Spring');

        const group = agent()!;
        expect(within(group).getByText('Active')).toBeTruthy();
        fireEvent.click(within(group).getByRole('button', { name: 'Pause' }));
        const dialog = await screen.findByRole('dialog', { name: 'Pause Spring' });
        // The list does not carry the provider's pin, so the dialog reads the campaign before it says what pausing does.
        await waitFor(() =>
            expect(dialog.textContent).toContain(
                "Jason stops releasing this agent's work. Work already running finishes. Reply's sequence keeps sending until a role pauses it there. Your reason is recorded.",
            ),
        );
        expect(sent(fake, 'campaign.get').at(-1)).toEqual({ campaign_id: 'cmp_1' });
        fireEvent.input(within(dialog).getByRole('textbox', { name: 'Reason' }), {
            target: { value: 'Holding the sends over the holiday.' },
        });
        const listsBefore = sent(fake, 'campaign.list').length;
        fireEvent.click(within(dialog).getByRole('button', { name: 'Pause' }));
        await waitFor(() =>
            expect(sent(fake, 'campaign.pause')).toEqual([
                Object.fromEntries(
                    Object.entries(fixtures['campaign.pause/with-reason'].request).filter(([key]) => key !== 'actor'),
                ),
            ]),
        );
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
        // The state the bar draws is read again from the runtime.
        await waitFor(() => expect(sent(fake, 'campaign.list').length).toBeGreaterThan(listsBefore));
    });

    it('a paused agent’s chat says Paused and offers Resume', async () => {
        const desktop = chatDesktop();
        renderConsole(desktop, consoleApi().answer('campaign.summary', 'campaign.summary/all'));
        await openKept(desktop, 'cmp_3', 'Summer referrals');

        const group = agent()!;
        expect(within(group).getByText('Paused')).toBeTruthy();
        expect(
            within(group)
                .getAllByRole('button')
                .map((each) => each.textContent?.trim()),
        ).toEqual(['Resume']);
    });

    it('a set-up chat carries no agent’s move, and carries it as soon as it becomes a campaign’s', async () => {
        const desktop = chatDesktop();
        renderConsole(desktop, consoleApi().answer('campaign.summary', 'campaign.summary/all'));
        await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), {
            target: { value: 'Help me start a new campaign.' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        const id = calls(desktop, 'chat.send')[0]!.args[0] as string;
        await waitFor(() => expect(shownPane()?.querySelector('.chat-bar')).toBeTruthy());
        expect(agent()).toBeNull();

        desktop.kept.get(id)!.campaignId = 'cmp_2';
        desktop.kept.get(id)!.title = 'Autumn';
        flush(() => {
            desktop.emit(id, { kind: 'attached', campaignId: 'cmp_2', title: 'Autumn' });
            desktop.emit(id, { kind: 'done', error: null });
        });
        await waitFor(() => screen.getByText('This chat is now the conversation of Autumn.'));
        await waitFor(() => expect(agent()).not.toBeNull());
        expect(within(agent()!).getByText('Draft')).toBeTruthy();
        expect(within(agent()!).getByRole('button', { name: 'Start' })).toBeTruthy();
    });
});
