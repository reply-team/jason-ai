import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { registry } from '../platform/slots';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations } from './state';

// The sidebar: the button that makes an agent and its caret's menu, the rows in their order, the number on Needs you,
// More and what is under it, the conversations under Recent, and Settings in the foot. Read against the recorded
// exchanges of the runtime's own tests.

function renderConsole(desktop: StubDesktop = stubDesktop()): StubDesktop {
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
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

const side = () => within(document.querySelector<HTMLElement>('.side')!);
const menu = () => side().getByRole('navigation', { name: 'Menu' });
/** The titles of the dock's tabs in front, one a group. */
const inFront = () =>
    Array.from(document.querySelectorAll('.dv-tab.dv-active-tab')).map((tab) => tab.textContent?.trim());
const calls = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name);
const caret = () => side().getByRole('button', { name: 'More to create' });
const more = () => side().getByRole('button', { name: 'More' });
const entries = (name: string) => within(side().getByRole('menu', { name })).getAllByRole('menuitem');

beforeEach(() => {
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

describe('the sidebar', () => {
    it('lists Feed, Needs you, People, Agents, Accounts and More in that order, then Recent, with Settings in the foot', async () => {
        renderConsole();
        await waitFor(() => within(menu()).getByRole('button', { name: /^Feed/ }));
        const names = within(menu())
            .getAllByRole('button')
            .map((each) => each.textContent?.replace(/\s+/g, ' ').trim().replace(/ \d+$/, ''));
        expect(names).toEqual(['Feed', 'Needs you', 'People', 'Agents', 'Accounts', 'More']);
        // The two groups are gone; the conversations sit under their header.
        expect(side().queryByRole('navigation', { name: 'Console' })).toBeNull();
        expect(side().queryByRole('navigation', { name: 'Pages' })).toBeNull();
        const recent = side().getByText('Recent');
        const conversationsNav = side().getByRole('navigation', { name: 'Conversations' });
        expect(recent.compareDocumentPosition(conversationsNav) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        // The foot holds Settings alone.
        const settings = side().getByRole('button', { name: 'Settings' });
        const version = document.querySelector<HTMLElement>('.sfoot')!;
        expect(version.textContent?.trim()).toBe('Settings');
        expect(version.contains(settings)).toBe(true);
    });

    it('opens Start from New GTM agent, bringing it forward, and makes no chat', async () => {
        const desktop = renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        fireEvent.click(side().getByRole('button', { name: 'Agents' }));
        await waitFor(() => expect(inFront()).toContain('Agents'));
        fireEvent.click(side().getByRole('button', { name: 'New GTM agent' }));
        await waitFor(() => expect(inFront()).toContain('Start'));
        expect(calls(desktop, 'chat.create')).toEqual([]);
    });

    it('has a caret of three entries, each a title and a line, and nothing else', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        expect(side().queryByRole('menu')).toBeNull();
        fireEvent.click(caret());
        expect(caret().getAttribute('aria-expanded')).toBe('true');
        const titles = entries('Create').map((each) => each.querySelector('.mt')?.textContent);
        expect(titles).toEqual(['New GTM agent', 'New chat', 'Connect an account']);
        expect(entries('Create').map((each) => each.querySelector('.ms')?.textContent)).toEqual([
            'Plan and run outreach',
            'Talk with Jason, about no campaign',
            'Link the accounts Jason works through',
        ]);
        // Escape and a click beside it close it.
        fireEvent.keyDown(window, { key: 'Escape' });
        expect(side().queryByRole('menu')).toBeNull();
    });

    it('leads the caret’s entries to Start, to a new chat, and to the Accounts screen', async () => {
        const desktop = renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        fireEvent.click(side().getByRole('button', { name: 'Agents' }));
        await waitFor(() => expect(inFront()).toContain('Agents'));
        fireEvent.click(caret());
        fireEvent.click(entries('Create')[0]!);
        await waitFor(() => expect(inFront()).toContain('Start'));
        expect(calls(desktop, 'chat.create')).toEqual([]);
        expect(side().queryByRole('menu')).toBeNull();

        fireEvent.click(caret());
        fireEvent.click(entries('Create')[1]!);
        await waitFor(() => expect(calls(desktop, 'chat.create')).toHaveLength(1));
        expect(side().queryByRole('menu')).toBeNull();

        fireEvent.click(caret());
        fireEvent.click(entries('Create')[2]!);
        await waitFor(() => expect(route.value.screen).toBe('accounts'));
        expect(inFront()).toContain('Page');
    });

    it('shows no number on Needs you while nothing waits, and the count of approvals and questions when something does', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        const needs = () => within(menu()).getByRole('button', { name: /^Needs you/ });
        await waitFor(() => expect(attention.loaded.value).toBe(true));
        // The recorded pending lists: two approvals and two questions.
        expect(attention.approvals.value).toHaveLength(2);
        await waitFor(() => expect(needs().querySelector('.count')?.textContent).toBe('4'));
        // Two approvals and one question: three.
        flush(() => {
            attention.questions.value = attention.questions.value.slice(0, 1);
        });
        await waitFor(() => expect(needs().querySelector('.count')?.textContent).toBe('3'));
        expect(needs().querySelector('.count')?.getAttribute('aria-label')).toBe('3 waiting');
        expect(within(menu()).getByRole('button', { name: 'Needs you 3 waiting' })).toBeTruthy();
        // Nothing waits: no number.
        flush(() => {
            attention.approvals.value = [];
            attention.questions.value = [];
        });
        await waitFor(() => expect(needs().querySelector('.count')).toBeNull());
    });

    it('holds Performance, Activity, Work items, Connect and System under More, and shows More active while one is in front', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        expect(more().getAttribute('aria-current')).toBeNull();
        fireEvent.click(more());
        const titles = entries('More').map((each) => each.querySelector('.mt')?.textContent);
        expect(titles).toEqual(['Performance', 'Activity', 'Work items', 'Connect', 'System']);
        for (const each of entries('More')) expect(each.querySelector('.ms')?.textContent?.length).toBeGreaterThan(0);

        fireEvent.click(entries('More')[1]!);
        await waitFor(() => expect(inFront()).toContain('Activity'));
        expect(side().queryByRole('menu')).toBeNull();
        expect(more().getAttribute('aria-current')).toBe('true');

        fireEvent.click(side().getByRole('button', { name: /^Needs you/ }));
        await waitFor(() => expect(inFront()).toContain('Needs you'));
        expect(more().getAttribute('aria-current')).toBeNull();

        fireEvent.click(more());
        fireEvent.click(entries('More')[3]!);
        await waitFor(() => expect(inFront()).toContain('Connect'));
        expect(more().getAttribute('aria-current')).toBe('true');
    });

    it('still reaches every screen it reached before: a panel in front, a page at the address its registered screen gives', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        const addressOf = (id: string) =>
            registry
                .list('screen')
                .find((each) => each.id === id)!
                .href();
        const argsOf = (button: HTMLElement) => JSON.parse(button.dataset['args']!) as unknown[];
        // The pages: each row and each entry of More carries the address the screen itself registers.
        const row = (name: string) => side().getByRole('button', { name });
        expect(argsOf(row('People'))).toEqual([addressOf('people')]);
        expect(argsOf(row('Accounts'))).toEqual([addressOf('accounts')]);
        expect(argsOf(row('Settings'))).toEqual([addressOf('settings')]);
        fireEvent.click(more());
        expect(entries('More').map((each) => argsOf(each))).toEqual([
            [addressOf('performance')],
            ['activity'],
            [addressOf('workitems')],
            ['connect'],
            [addressOf('system')],
        ]);
        fireEvent.click(more());
        // The panels of the console's own.
        fireEvent.click(side().getByRole('button', { name: 'Agents' }));
        await waitFor(() => expect(inFront()).toContain('Agents'));
        fireEvent.click(more());
        fireEvent.click(entries('More')[1]!);
        await waitFor(() => expect(inFront()).toContain('Activity'));
        fireEvent.click(more());
        fireEvent.click(entries('More')[3]!);
        await waitFor(() => expect(inFront()).toContain('Connect'));
        fireEvent.click(side().getByRole('button', { name: /^Needs you/ }));
        await waitFor(() => expect(inFront()).toContain('Needs you'));
    });
});
