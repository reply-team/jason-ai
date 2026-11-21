import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { StrictMode } from 'react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { mode } from '../platform/mode';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { shown, shownPane, bringForward } from '../test/area';
import { stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import { nothingWaitsInChats } from '../test/waiting';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { resetRecent } from './recent';
import { chats, conversations, here } from './state';

// The console's one area beside the sidebar: no tab strip and no right column, exactly one thing shown at a time, chosen
// from the menu, Recent, the palette, a desktop command or a link, and marked in the sidebar. Read against the recorded
// exchanges of the runtime's own tests.

async function renderConsole(
    seed: (desktop: StubDesktop) => void = () => undefined,
    waits: 'pending' | 'empty' = 'pending',
    strict = false,
): Promise<StubDesktop> {
    const desktop = stubDesktop();
    seed(desktop);
    const fake = nothingWaitsInChats(new FakeApi())
        .answer('approval.list', `approval.list/${waits}`)
        .answer('decision.list', `decision.list/${waits}`)
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        // A link opens the first; Needs you reads each approval it lists whole, for its row's Approve.
        .answer('approval.get', 'approval.get/email-message', 'approval.get/other-pending');
    // Needs you reads the sending name under an email's From (`needs-sender-line.screen.test.tsx`); here it is answered
    // with one recorded list of accounts, which draws one line and holds nothing.
    if (!fake.offers('sender.list')) fake.answerAnyRequest('sender.list', 'sender.list/card-message-sender');
    watch(agentHeads(fake));
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
    const shell = <Console api={fake} desktop={desktop} stream={stubStream().stream} />;
    render(strict ? <StrictMode>{shell}</StrictMode> : shell);
    await waitFor(() => expect(attention.loaded.value).toBe(true));
    return desktop;
}

const side = () => within(document.querySelector<HTMLElement>('.side')!);
const menu = () => within(side().getByRole('navigation', { name: 'Menu' }));
const recentRow = (name: string) =>
    within(side().getByRole('navigation', { name: 'Conversations' })).getByRole('button', { name });
/** The sidebar's controls that say they stand for what is shown. */
const marked = () =>
    Array.from(document.querySelectorAll<HTMLElement>('.side [aria-current="true"]'), (each) =>
        each.textContent?.replace(/\s+/g, ' ').trim(),
    );
const box = () => screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' });
const type = (text: string) => fireEvent.change(box(), { target: { value: text } });
const sends = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'chat.send');

const seedSpring = (desktop: StubDesktop) => {
    desktop.keep({ id: 'conv_s', campaignId: 'cmp_1', title: 'Q4 push', updatedAt: new Date().toISOString() });
};

const fourTitles = ['Q4 push', 'North list', 'Renewals', 'Old leads'] as const;
const seedFour = (desktop: StubDesktop) => {
    fourTitles.forEach((title, at) =>
        desktop.keep({
            id: `conv_${at + 1}`,
            // The first is a campaign's chat, so its first turn does not rename it.
            campaignId: at === 0 ? 'cmp_1' : null,
            title,
            updatedAt: new Date(Date.now() - at * 60_000).toISOString(),
        }),
    );
};
/** The ids the document carries more than once. */
const repeatedIds = () => {
    const ids = Array.from(document.querySelectorAll('[id]'), (each) => each.id);
    return ids.filter((each, at) => ids.indexOf(each) !== at);
};

beforeEach(() => {
    mode.value = 'live';
    resetRecent();
    chats.value = {};
    conversations.value = [];
    attention.loaded.value = false;
    attention.approvals.value = [];
    attention.questions.value = [];
    attention.detail.value = null;
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

describe('the one area', () => {
    it('opens on Start, with no tab strip and no right column, and marks no row of the sidebar', async () => {
        await renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        expect(shown()).toBe('start');
        expect(document.querySelectorAll('.area > .pane:not([hidden])')).toHaveLength(1);
        expect(document.querySelector('[class*="dv-"]')).toBeNull();
        // Needs you, Agents and Activity are no column beside it: none of them is drawn until it is chosen.
        expect(screen.queryByRole('heading', { name: 'Needs you', level: 1 })).toBeNull();
        expect(screen.queryByRole('heading', { name: 'Agents', level: 1 })).toBeNull();
        expect(screen.queryByRole('heading', { name: 'Activity', level: 1 })).toBeNull();
        expect(screen.queryByText('Page')).toBeNull();
        // New GTM agent makes an agent at the click: it is no row that brings Start.
        expect(marked()).toEqual([]);
    });

    it('shows exactly the one thing chosen from the menu, and marks its row alone', async () => {
        // The demo's sidebar draws every row; Live's draws Needs you and Settings alone.
        mode.value = 'demo';
        await renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        fireEvent.click(menu().getByRole('button', { name: /^Needs you/ }));
        await waitFor(() => expect(shown()).toBe('needs'));
        expect(within(shownPane()!).getByRole('heading', { name: 'Needs you', level: 1 })).toBeTruthy();
        expect(marked()).toHaveLength(1);
        expect(marked()[0]).toMatch(/^Needs you/);
        bringForward('Agents');
        await waitFor(() => expect(shown()).toBe('campaigns'));
        expect(marked()).toEqual(['Agents']);
        bringForward('Activity');
        await waitFor(() => expect(shown()).toBe('activity'));
        expect(marked()).toEqual(['More']);
        fireEvent.click(side().getByRole('button', { name: 'Settings' }));
        await waitFor(() => expect(route.value.screen).toBe('settings'));
        expect(shown()).toBe('page');
        expect(marked()).toEqual(['Settings']);
        // Whatever was shown before, one pane alone is in sight.
        expect(document.querySelectorAll('.area > .pane:not([hidden])')).toHaveLength(1);
    });

    it('keeps a chat left and chosen again from Recent as it was: the words typed, what arrived meanwhile and the message that waits', async () => {
        const desktop = await renderConsole(seedSpring);
        fireEvent.click(await waitFor(() => recentRow('Q4 push')));
        await waitFor(() => expect(shown()).toBe('chat:conv_s'));
        expect(recentRow('Q4 push').getAttribute('aria-current')).toBe('true');
        expect(marked()).toHaveLength(1);
        type('Go');
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(sends(desktop)).toHaveLength(1));
        type('And then?');
        fireEvent.click(screen.getByRole('button', { name: 'Send when this turn ends' }));
        await waitFor(() => screen.getByRole('group', { name: 'Queued message' }));
        type('Half a thought');
        // The person goes to Needs you: the chat is out of sight, and Recent marks nothing.
        fireEvent.click(menu().getByRole('button', { name: /^Needs you/ }));
        await waitFor(() => expect(shown()).toBe('needs'));
        expect(recentRow('Q4 push').getAttribute('aria-current')).toBeNull();
        expect(screen.queryByRole('textbox', { name: 'Your message' })).toBeNull();
        flush(() => desktop.emit('conv_s', { kind: 'text', text: 'Arrived while you were away' }));
        // Chosen again, it has all of it.
        fireEvent.click(recentRow('Q4 push'));
        await waitFor(() => expect(shown()).toBe('chat:conv_s'));
        expect(box().value).toBe('Half a thought');
        expect(within(shownPane()!).getByText('Arrived while you were away')).toBeTruthy();
        expect(screen.getByRole('group', { name: 'Queued message' }).textContent).toContain('And then?');
        expect(sends(desktop)).toHaveLength(1);
    });

    it('keeps drawn only the chat shown and the two before it, and a chat chosen again after that still has all it had', async () => {
        const desktop = await renderConsole(seedFour);
        fireEvent.click(await waitFor(() => recentRow('Q4 push')));
        await waitFor(() => expect(shown()).toBe('chat:conv_1'));
        type('Go');
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(sends(desktop)).toHaveLength(1));
        type('And then?');
        fireEvent.click(screen.getByRole('button', { name: 'Send when this turn ends' }));
        await waitFor(() => screen.getByRole('group', { name: 'Queued message' }));
        type('Half a thought');
        for (const [at, title] of fourTitles.slice(1).entries()) {
            fireEvent.click(recentRow(title));
            await waitFor(() => expect(shown()).toBe(`chat:conv_${at + 2}`));
            flush(() => desktop.emit('conv_1', { kind: 'text', text: `Arrived while ${title} was shown` }));
        }
        // Four chats were shown: the first is no longer drawn, the last three are, one of them in sight.
        expect(document.querySelector('[data-pane="chat:conv_1"]')).toBeNull();
        expect(document.querySelectorAll('.area > .pane[data-pane^="chat:"]')).toHaveLength(3);
        expect(repeatedIds()).toEqual([]);
        fireEvent.click(recentRow('Q4 push'));
        await waitFor(() => expect(shown()).toBe('chat:conv_1'));
        expect(box().value).toBe('Half a thought');
        for (const title of fourTitles.slice(1))
            expect(within(shownPane()!).getByText(`Arrived while ${title} was shown`)).toBeTruthy();
        expect(screen.getByRole('group', { name: 'Queued message' }).textContent).toContain('And then?');
        expect(sends(desktop)).toHaveLength(1);
        // Chosen again, it is drawn again, and the one shown longest ago goes in its place.
        expect(document.querySelector('[data-pane="chat:conv_2"]')).toBeNull();
        expect(document.querySelectorAll('.area > .pane[data-pane^="chat:"]')).toHaveLength(3);
        expect(repeatedIds()).toEqual([]);
    });

    it('stays bound when React mounts it twice, as StrictMode does', async () => {
        await renderConsole(() => undefined, 'pending', true);
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        expect(() => here()).not.toThrow();
        expect(shown()).toBe('start');
        expect(document.querySelectorAll('.area > .pane')).toHaveLength(1);
        fireEvent.click(menu().getByRole('button', { name: /^Needs you/ }));
        await waitFor(() => expect(shown()).toBe('needs'));
        expect(marked()[0]).toMatch(/^Needs you/);
    });

    it('shows Start when the chat shown is taken away', async () => {
        const desktop = await renderConsole(seedSpring);
        fireEvent.click(await waitFor(() => recentRow('Q4 push')));
        await waitFor(() => expect(shown()).toBe('chat:conv_s'));
        // An agent's conversation is removed from the "…" of the agent's header over it.
        const header = await waitFor(() => document.querySelector<HTMLElement>('.agent-head')!);
        fireEvent.click(within(header).getByRole('button', { name: "More for this agent's conversation" }));
        fireEvent.click(await waitFor(() => within(header).getByRole('menuitem', { name: 'Remove conversation' })));
        fireEvent.click(await waitFor(() => within(shownPane()!).getByRole('button', { name: 'Remove it' })));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.remove')).toBe(true));
        await waitFor(() => expect(shown()).toBe('start'));
        expect(screen.getByRole('heading', { name: 'Who do you want to reach?' })).toBeTruthy();
        expect(marked()).toEqual([]);
        expect(document.querySelector('[data-pane="chat:conv_s"]')).toBeNull();
    });

    it('opens Needs you at the one approval a link names, in the one area', async () => {
        const desktop = await renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        desktop.link({ page: 'approval', id: 'apr_1' });
        await waitFor(() => expect(shown()).toBe('needs'));
        await waitFor(() => expect(attention.detail.value?.item.id).toBe('apr_1'));
        expect(marked()[0]).toMatch(/^Needs you/);
    });

    it('shows on Needs you how many approvals and questions wait while that is above zero, and nothing at zero', async () => {
        await renderConsole();
        const waiting = attention.approvals.value.length + attention.questions.value.length;
        expect(waiting).toBeGreaterThan(0);
        const needs = await waitFor(() => menu().getByRole('button', { name: /^Needs you/ }));
        await waitFor(() => expect(needs.querySelector('.count')?.textContent).toBe(String(waiting)));
        expect(needs.querySelector('.count')?.getAttribute('aria-label')).toBe(`${waiting} waiting`);
    });

    it('draws no number on Needs you when nothing waits', async () => {
        await renderConsole(() => undefined, 'empty');
        const needs = menu().getByRole('button', { name: /^Needs you/ });
        expect(needs.querySelector('.count')).toBeNull();
    });
});
