import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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
import { resetRecent } from './recent';
import { chats, clock, conversations } from './state';

// "Recent" in the sidebar: its two buttons, the search, the filter, the rows with their dot, number and age, the
// paging and the empty texts. Read against the recorded exchanges of the runtime's own tests: Spring (cmp_1) is active
// with two approvals and two questions waiting, Autumn (cmp_2) is a draft and Summer referrals (cmp_3) is paused.

const minutes = (count: number) => new Date(Date.now() - count * 60_000).toISOString();

async function renderConsole(seed: (desktop: StubDesktop) => void = () => undefined): Promise<StubDesktop> {
    const desktop = stubDesktop();
    seed(desktop);
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
    await screen.findByRole('heading', { name: 'Who do you want to reach?' });
    await waitFor(() => expect(attention.loaded.value).toBe(true));
    await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
    return desktop;
}

/** The three agents' chats, a renamed one among them, and a set-up chat with no agent. */
const seedAgents = (desktop: StubDesktop) => {
    desktop.keep({ id: 'conv_s', campaignId: 'cmp_1', title: 'Q4 push', updatedAt: minutes(5.5) });
    desktop.keep({ id: 'conv_a', campaignId: 'cmp_2', title: 'Autumn', updatedAt: minutes(125.5) });
    desktop.keep({
        id: 'conv_r',
        campaignId: 'cmp_3',
        title: 'Summer referrals',
        updatedAt: minutes(60 * 24 * 3 + 0.5),
    });
    desktop.keep({ id: 'conv_n', campaignId: null, title: 'Help me start', updatedAt: minutes(0) });
};

const side = () => within(document.querySelector<HTMLElement>('.side')!);
const list = () => side().getByRole('navigation', { name: 'Conversations' });
const rows = () => Array.from(list().querySelectorAll<HTMLElement>('.recent-row'));
const titles = () => rows().map((each) => each.querySelector('.rt')?.textContent);
const row = (name: string) => within(list()).getByRole('button', { name });
const search = () => side().getByRole('button', { name: 'Search agents' });
const filterButton = () => side().getByRole('button', { name: 'Filter' });
const box = () => side().getByRole('textbox', { name: 'Search agents' });
const choose = (name: string) => {
    fireEvent.click(filterButton());
    fireEvent.click(within(side().getByRole('menu', { name: 'Show' })).getByRole('menuitemradio', { name }));
};

beforeEach(() => {
    resetRecent();
    clock.value = Date.now();
    chats.value = {};
    conversations.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

describe('the Recent header', () => {
    it('carries a magnifier titled Search agents and a filter titled Filter on its right', async () => {
        await renderConsole(seedAgents);
        const header = side().getByText('Recent').closest('.recent')!;
        const buttons = within(header as HTMLElement).getAllByRole('button');
        expect(buttons.map((each) => each.getAttribute('title'))).toEqual(['Search agents', 'Filter']);
        expect(filterButton().getAttribute('data-active')).toBe('false');
    });
});

describe('the search', () => {
    it('opens a box with the placeholder Search agents and filters rows by title and by the agent’s name', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        expect(side().queryByRole('textbox', { name: 'Search agents' })).toBeNull();
        fireEvent.click(search());
        expect(box().getAttribute('placeholder')).toBe('Search agents');
        fireEvent.input(box(), { target: { value: 'help' } });
        expect(titles()).toEqual(['Help me start']);
        // The chat is called Q4 push; its agent is Spring.
        fireEvent.input(box(), { target: { value: 'SPRING' } });
        expect(titles()).toEqual(['Q4 push']);
        fireEvent.input(box(), { target: { value: 'q4' } });
        expect(titles()).toEqual(['Q4 push']);
    });

    it('is closed and cleared by Escape, and the rows all come back', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        fireEvent.click(search());
        fireEvent.input(box(), { target: { value: 'autumn' } });
        expect(titles()).toEqual(['Autumn']);
        fireEvent.keyDown(box(), { key: 'Escape' });
        expect(side().queryByRole('textbox', { name: 'Search agents' })).toBeNull();
        expect(rows()).toHaveLength(4);
        fireEvent.click(search());
        expect(box().getAttribute('value') ?? (box() as HTMLInputElement).value).toBe('');
    });

    it('is closed by the magnifier too, clearing the text', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        fireEvent.click(search());
        fireEvent.input(box(), { target: { value: 'autumn' } });
        fireEvent.click(search());
        expect(rows()).toHaveLength(4);
    });
});

describe('the filter', () => {
    it('opens a menu headed Show with All, Needs you, Live, Paused and Draft, and a check on the active one', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        fireEvent.click(filterButton());
        const menu = side().getByRole('menu', { name: 'Show' });
        expect(within(menu).getByText('Show')).toBeTruthy();
        const entries = within(menu).getAllByRole('menuitemradio');
        expect(entries.map((each) => each.textContent?.trim())).toEqual([
            'All',
            'Needs you',
            'Live',
            'Paused',
            'Draft',
        ]);
        expect(entries.map((each) => each.getAttribute('aria-checked'))).toEqual([
            'true',
            'false',
            'false',
            'false',
            'false',
        ]);
        expect(entries[0]!.querySelector('svg')).not.toBeNull();
        expect(entries[1]!.querySelector('svg')).toBeNull();
    });

    it('marks the button active when the entry is not All, and moves the check', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        choose('Paused');
        expect(side().queryByRole('menu', { name: 'Show' })).toBeNull();
        expect(filterButton().getAttribute('data-active')).toBe('true');
        fireEvent.click(filterButton());
        const checked = within(side().getByRole('menu', { name: 'Show' }))
            .getAllByRole('menuitemradio')
            .filter((each) => each.getAttribute('aria-checked') === 'true');
        expect(checked.map((each) => each.textContent?.trim())).toEqual(['Paused']);
        fireEvent.click(within(side().getByRole('menu', { name: 'Show' })).getByRole('menuitemradio', { name: 'All' }));
        expect(filterButton().getAttribute('data-active')).toBe('false');
    });

    it('shows under each entry the rows it names, and a chat with no agent under All alone', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        expect(titles()).toEqual(['Help me start', 'Q4 push', 'Autumn', 'Summer referrals']);
        choose('Needs you');
        expect(titles()).toEqual(['Q4 push']);
        choose('Live');
        expect(titles()).toEqual(['Q4 push']);
        choose('Paused');
        expect(titles()).toEqual(['Summer referrals']);
        choose('Draft');
        expect(titles()).toEqual(['Autumn']);
        choose('All');
        expect(titles()).toContain('Help me start');
    });

    it('closes the menu with Escape', async () => {
        await renderConsole(seedAgents);
        fireEvent.click(filterButton());
        expect(side().getByRole('menu', { name: 'Show' })).toBeTruthy();
        fireEvent.keyDown(window, { key: 'Escape' });
        expect(side().queryByRole('menu', { name: 'Show' })).toBeNull();
    });
});

describe('a row', () => {
    it('holds a dot, the title, a number only above zero, and the age', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        const order = Array.from(row('Q4 push').children).map((each) => each.className.split(' ')[0]);
        expect(order).toEqual(['rdot', 'rt', 'count', 't']);
        expect(row('Q4 push').querySelector('.t')?.textContent).toBe('5 min');
        expect(row('Autumn').querySelector('.t')?.textContent).toBe('2 h');
        expect(row('Summer referrals').querySelector('.t')?.textContent).toBe('3 d');
        expect(row('Help me start').querySelector('.t')?.textContent).toBe('now');
        // Spring has two approvals and two questions; nobody else has any.
        expect(row('Q4 push').querySelector('.count')?.textContent).toBe('4');
        expect(row('Autumn').querySelector('.count')).toBeNull();
        expect(row('Summer referrals').querySelector('.count')).toBeNull();
        expect(row('Help me start').querySelector('.count')).toBeNull();
    });

    it('counts approvals plus questions of its own agent, and drops the number when nothing waits', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(row('Q4 push').querySelector('.count')?.textContent).toBe('4'));
        flush(() => {
            attention.questions.value = attention.questions.value.slice(0, 1);
        });
        await waitFor(() => expect(row('Q4 push').querySelector('.count')?.textContent).toBe('3'));
        flush(() => {
            attention.approvals.value = [];
            attention.questions.value = [];
        });
        await waitFor(() => expect(row('Q4 push').querySelector('.count')).toBeNull());
    });

    it('draws amber while something waits, else the agent’s state, and a ring for a chat with no agent', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        const dot = (name: string) => row(name).querySelector('.rdot')!.className.replace('rdot ', '');
        expect(dot('Q4 push')).toBe('waiting');
        expect(dot('Autumn')).toBe('draft');
        expect(dot('Summer referrals')).toBe('paused');
        expect(dot('Help me start')).toBe('none');
        flush(() => {
            attention.approvals.value = [];
            attention.questions.value = [];
        });
        await waitFor(() => expect(dot('Q4 push')).toBe('active'));
    });

    it('opens its chat when clicked, as before', async () => {
        const desktop = await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        fireEvent.click(row('Autumn'));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.open')).toBe(true));
        await waitFor(() => expect(row('Autumn').getAttribute('aria-current')).toBe('true'));
    });
});

describe('paging', () => {
    const seedMany = (desktop: StubDesktop) => {
        for (let at = 0; at < 25; at += 1)
            desktop.keep({ id: `conv_m${String(at).padStart(2, '0')}`, title: `Chat ${at}`, updatedAt: minutes(at) });
    };

    it('shows ten rows, then adds ten with each Load more until all are shown', async () => {
        await renderConsole(seedMany);
        await waitFor(() => expect(rows()).toHaveLength(10));
        const more = () => within(list()).queryByRole('button', { name: 'Load more' });
        fireEvent.click(more()!);
        expect(rows()).toHaveLength(20);
        fireEvent.click(more()!);
        expect(rows()).toHaveLength(25);
        expect(more()).toBeNull();
    });

    it('shows every match while a search text or a filter is active, with no Load more', async () => {
        await renderConsole(seedMany);
        await waitFor(() => expect(rows()).toHaveLength(10));
        fireEvent.click(search());
        fireEvent.input(box(), { target: { value: 'chat' } });
        expect(rows()).toHaveLength(25);
        expect(within(list()).queryByRole('button', { name: 'Load more' })).toBeNull();
        fireEvent.keyDown(box(), { key: 'Escape' });
        expect(rows()).toHaveLength(10);
    });
});

describe('the empty texts', () => {
    it('says No chats yet. when the list holds nothing at all', async () => {
        await renderConsole();
        await waitFor(() => expect(side().getByText('No chats yet.')).toBeTruthy());
        expect(side().queryByText('No agents match.')).toBeNull();
    });

    it('says No agents match. when a search or a filter hides everything', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        expect(side().queryByText('No agents match.')).toBeNull();
        fireEvent.click(search());
        fireEvent.input(box(), { target: { value: 'zzz' } });
        expect(side().getByText('No agents match.')).toBeTruthy();
        expect(side().queryByText('No chats yet.')).toBeNull();
        fireEvent.keyDown(box(), { key: 'Escape' });
        flush(() => {
            attention.approvals.value = [];
            attention.questions.value = [];
        });
        choose('Needs you');
        expect(side().getByText('No agents match.')).toBeTruthy();
    });
});
