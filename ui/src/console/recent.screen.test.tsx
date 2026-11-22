import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { currentDesktop, placeKind, setPlace } from '../platform/places';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import { nothingWaitsInChats } from '../test/waiting';
import { agentFacts } from './agent-facts';
import * as consoleCampaigns from './campaigns';
import { reloadConversations } from './chat';
import { Console } from './console';
import { attention } from './needs';
import { resetRecent } from './recent';
import { chats, clock, conversations } from './state';

// The sidebar's two sections, AGENTS and CHATS: their headers and pluses, AGENTS' filter, the rows with their dot, number
// and age, each row's three dots, CHATS' paging and the empty texts. Read against the recorded exchanges of the runtime's own tests: Spring (cmp_1) is active
// with two approvals and two questions waiting, Autumn (cmp_2) is a draft and Summer referrals (cmp_3) is paused.

const minutes = (count: number) => new Date(Date.now() - count * 60_000).toISOString();

/** The runtime the console was drawn over last, and what it was asked of one agent alone. */
let answered: FakeApi;
const aloneOf = (operation: string, id: string) =>
    answered.calls.filter((each) => {
        const body = each.body as { campaign_id?: string; campaign_ids?: string[] };
        return (
            each.operation === operation &&
            (body.campaign_id === id || (body.campaign_ids?.length === 1 && body.campaign_ids[0] === id))
        );
    }).length;

async function renderConsole(
    seed: (desktop: StubDesktop) => void = () => undefined,
    agents:
        | 'campaign.list/screen'
        | 'campaign.list/screen-empty'
        | 'campaign.list/screen-many'
        | 'campaign.list/move-targets' = 'campaign.list/screen',
): Promise<StubDesktop> {
    const desktop = stubDesktop();
    seed(desktop);
    const fake = nothingWaitsInChats(new FakeApi())
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
        .answerAnyRequest('campaign.list', agents)
        .answerAnyRequest(
            'campaign.summary',
            agents === 'campaign.list/screen-many' ? 'campaign.summary/many' : 'campaign.summary/all',
        );
    answered = fake;
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
const agentsList = () => side().getByRole('group', { name: 'Agents' });
const chatsList = () => side().getByRole('group', { name: 'Chats' });
const rowsOf = (list: HTMLElement) => Array.from(list.querySelectorAll<HTMLElement>('.recent-row'));
const rows = () => [...rowsOf(agentsList()), ...rowsOf(chatsList())];
const titlesOf = (list: HTMLElement) => rowsOf(list).map((each) => each.querySelector('.rt')?.textContent);
const titles = () => titlesOf(agentsList());
const row = (name: string) =>
    within(document.querySelector<HTMLElement>('.side .sscroll')!).getByRole('button', { name });
const filterButton = () => side().getByRole('button', { name: 'Filter' });
const headerOf = (name: string) =>
    Array.from(document.querySelectorAll<HTMLElement>('.side .recent')).find(
        (each) => each.querySelector('.rl')?.textContent === name,
    )!;
const choose = (name: string) => {
    fireEvent.click(filterButton());
    fireEvent.click(within(side().getByRole('menu', { name: 'Show' })).getByRole('menuitemradio', { name }));
};

beforeEach(() => {
    resetRecent();
    clock.value = Date.now();
    chats.value = {};
    // What an agent's header read in an earlier row is no part of this one.
    agentFacts.value = {};
    conversations.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

describe('the two sections', () => {
    it('heads AGENTS with a filter and a plus that makes an agent, then CHATS with a plus that makes a chat, and no search box', async () => {
        await renderConsole(seedAgents);
        const agents = headerOf('Agents');
        const chats = headerOf('Chats');
        expect(
            within(agents)
                .getAllByRole('button')
                .map((each) => each.getAttribute('aria-label')),
        ).toEqual(['Filter', 'New agent']);
        expect(
            within(chats)
                .getAllByRole('button')
                .map((each) => each.getAttribute('aria-label')),
        ).toEqual(['New chat']);
        expect(filterButton().getAttribute('data-active')).toBe('false');
        // AGENTS stands above CHATS, and neither has a box of its own: Search finds agents and chats.
        expect(agents.compareDocumentPosition(chats) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        expect(side().queryByRole('textbox')).toBeNull();
        expect(side().queryByText('Recent')).toBeNull();
    });

    it('names an agent’s row by its campaign’s name, not by a title its conversation carries', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        // Spring's conversation is titled Q4 push: the row says Spring, as the agent's header and the Agents page do.
        expect(conversations.value.find((each) => each.id === 'conv_s')?.title).toBe('Q4 push');
        expect(titles()[0]).toBe('Spring');
        expect(side().queryByRole('button', { name: 'Q4 push' })).toBeNull();
    });

    it('lists each agent in AGENTS and each chat about no agent in CHATS, a conversation in exactly one', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        expect(titlesOf(agentsList())).toEqual(['Spring', 'Autumn', 'Summer referrals']);
        expect(titlesOf(chatsList())).toEqual(['Help me start']);
    });

    it('keeps a row for an agent with no conversation, and opening it makes its conversation as the Agents page does', async () => {
        const desktop = await renderConsole((desktop) => {
            desktop.keep({ id: 'conv_n', campaignId: null, title: 'Help me start', updatedAt: minutes(0) });
        });
        await waitFor(() => expect(titles()).toEqual(['Spring', 'Autumn', 'Summer referrals']));
        expect(row('Autumn').querySelector('.t')?.textContent).toBe('');
        fireEvent.click(row('Autumn'));
        await waitFor(() => expect(route.value).toMatchObject({ screen: 'campaigns', id: 'cmp_2', tab: 'chat' }));
        await waitFor(() => expect(row('Autumn').getAttribute('aria-current')).toBe('true'));
        expect(desktop.calls.some((each) => each.name === 'chat.open')).toBe(false);
    });

    it('opens the Agents page from All agents, under the rows of AGENTS', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        const all = within(agentsList()).getByRole('button', { name: 'All agents' });
        expect(all.compareDocumentPosition(row('Summer referrals')) & Node.DOCUMENT_POSITION_PRECEDING).toBeTruthy();
        fireEvent.click(all);
        await waitFor(() => expect(screen.getByRole('heading', { name: 'Agents' })).toBeTruthy());
    });

    it('shows a three-dot button on each row that opens the row’s menu, named for the row', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        for (const name of ['Autumn', 'Help me start']) {
            const dots = side().getByRole('button', { name: `More for ${name}` });
            expect(dots.getAttribute('aria-haspopup')).toBe('menu');
            expect(dots.closest('.rrow')?.contains(row(name))).toBe(true);
            fireEvent.click(dots);
            const menu = await screen.findByRole('menu', { name });
            expect(within(menu).getByRole('menuitem', { name: 'Open' })).toBeTruthy();
            fireEvent.keyDown(menu, { key: 'Escape' });
            await waitFor(() => expect(screen.queryByRole('menu', { name })).toBeNull());
        }
    });

    it('gives the focus back to the three dots after Escape, Tab or an act that leaves the row in place', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        const dots = () => side().getByRole('button', { name: 'More for Spring' });
        for (const leave of ['Escape', 'Tab'] as const) {
            fireEvent.click(dots());
            const menu = await screen.findByRole('menu', { name: 'Spring' });
            fireEvent.keyDown(menu, { key: leave });
            await waitFor(() => expect(screen.queryByRole('menu', { name: 'Spring' })).toBeNull());
            expect(document.activeElement).toBe(dots());
        }
        // Pause opens its dialog over the area; closing it without pausing leaves the row where it was.
        fireEvent.click(dots());
        const menu = await screen.findByRole('menu', { name: 'Spring' });
        fireEvent.click(within(menu).getByRole('menuitem', { name: 'Pause' }));
        const dialog = await screen.findByRole('dialog');
        fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
        expect(document.activeElement).toBe(dots());
    });

    it('gives the focus back to the row after a menu a right-click or the keys opened', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        fireEvent.contextMenu(row('Spring'));
        fireEvent.keyDown(await screen.findByRole('menu', { name: 'Spring' }), { key: 'Escape' });
        await waitFor(() => expect(screen.queryByRole('menu', { name: 'Spring' })).toBeNull());
        expect(document.activeElement).toBe(row('Spring'));
        row('Spring').focus();
        fireEvent.keyDown(row('Spring'), { key: 'F10', shiftKey: true });
        fireEvent.keyDown(await screen.findByRole('menu', { name: 'Spring' }), { key: 'Tab' });
        await waitFor(() => expect(screen.queryByRole('menu', { name: 'Spring' })).toBeNull());
        expect(document.activeElement).toBe(row('Spring'));
    });

    it('closes the menu its three dots opened by a second press on them', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        const dots = () => side().getByRole('button', { name: 'More for Spring' });
        fireEvent.click(dots());
        await screen.findByRole('menu', { name: 'Spring' });
        fireEvent.mouseDown(dots());
        fireEvent.click(dots());
        await waitFor(() => expect(screen.queryByRole('menu', { name: 'Spring' })).toBeNull());
        expect(dots().getAttribute('aria-expanded')).toBe('false');
    });
});

describe('the order and the first ten', () => {
    it('puts an agent made outside the console, with no conversation yet, at the top of AGENTS', async () => {
        // Spring webinar (cmp_1) and Nordic pilot (cmp_2) have conversations older than either agent's last change; Q4
        // follow-ups (cmp_3), changed last and made by no click of the console, has none.
        await renderConsole((desktop) => {
            desktop.keep({ id: 'conv_1', campaignId: 'cmp_1', title: 'Spring', updatedAt: '2026-09-01T00:00:00.000Z' });
            desktop.keep({ id: 'conv_2', campaignId: 'cmp_2', title: 'Nordic', updatedAt: '2026-09-02T00:00:00.000Z' });
        }, 'campaign.list/move-targets');
        await waitFor(() => expect(titles()).toEqual(['Q4 follow-ups', 'Nordic pilot', 'Spring webinar']));
    });

    it('puts the agent active most recently first, and moves an agent up when its chat moves', async () => {
        const desktop = await renderConsole(seedAgents);
        await waitFor(() => expect(titles()).toEqual(['Spring', 'Autumn', 'Summer referrals']));
        desktop.keep({ id: 'conv_r', campaignId: 'cmp_3', title: 'Summer referrals', updatedAt: minutes(0.1) });
        await flush(() => reloadConversations());
        await waitFor(() => expect(titles()).toEqual(['Summer referrals', 'Spring', 'Autumn']));
    });

    it('shows ten agents under All, and a filter keeps every agent it names, past the tenth too', async () => {
        await renderConsole((desktop) => {
            // Eleven agents with a chat, the newest first; Campaign 001, which has the approvals and questions, has none,
            // so under All it stands past the tenth row.
            for (let at = 2; at <= 12; at += 1)
                desktop.keep({
                    id: `conv_c${at}`,
                    campaignId: `cmp_${at}`,
                    title: `Chat ${at}`,
                    updatedAt: minutes(at),
                });
        }, 'campaign.list/screen-many');
        await waitFor(() => expect(rowsOf(agentsList())).toHaveLength(10));
        expect(titles()).not.toContain('Campaign 001');
        expect(within(agentsList()).getByRole('button', { name: 'All agents' })).toBeTruthy();
        choose('Needs you');
        expect(titles()).toEqual(['Campaign 001']);
        choose('Live');
        expect(rowsOf(agentsList()).length).toBeGreaterThan(10);
        expect(titles()).toContain('Campaign 001');
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

    it('shows under each entry the agents it names, and leaves CHATS as it is', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        expect(titles()).toEqual(['Spring', 'Autumn', 'Summer referrals']);
        choose('Needs you');
        expect(titles()).toEqual(['Spring']);
        choose('Live');
        expect(titles()).toEqual(['Spring']);
        choose('Paused');
        expect(titles()).toEqual(['Summer referrals']);
        choose('Draft');
        expect(titles()).toEqual(['Autumn']);
        expect(titlesOf(chatsList())).toEqual(['Help me start']);
        choose('All');
        expect(titles()).toEqual(['Spring', 'Autumn', 'Summer referrals']);
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
    it('holds, for an agent, a dot, the title, a number only above zero, and the age; for a chat, its title and age', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        const order = (name: string) => Array.from(row(name).children).map((each) => each.className.split(' ')[0]);
        expect(order('Spring')).toEqual(['rdot', 'rt', 'count', 't']);
        expect(order('Help me start')).toEqual(['rt', 't']);
        expect(row('Spring').querySelector('.t')?.textContent).toBe('5 min');
        expect(row('Autumn').querySelector('.t')?.textContent).toBe('2 h');
        expect(row('Summer referrals').querySelector('.t')?.textContent).toBe('3 d');
        expect(row('Help me start').querySelector('.t')?.textContent).toBe('now');
        // Spring has two approvals and two questions; nobody else has any.
        expect(row('Spring').querySelector('.count')?.textContent).toBe('4');
        expect(row('Autumn').querySelector('.count')).toBeNull();
        expect(row('Summer referrals').querySelector('.count')).toBeNull();
    });

    it('counts approvals plus questions of its own agent, and drops the number when nothing waits', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(row('Spring').querySelector('.count')?.textContent).toBe('4'));
        flush(() => {
            attention.questions.value = attention.questions.value.slice(0, 1);
        });
        await waitFor(() => expect(row('Spring').querySelector('.count')?.textContent).toBe('3'));
        flush(() => {
            attention.approvals.value = [];
            attention.questions.value = [];
        });
        await waitFor(() => expect(row('Spring').querySelector('.count')).toBeNull());
    });

    it('draws amber while something waits, else the agent’s state', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        const dot = (name: string) => row(name).querySelector('.rdot')!.className.replace('rdot ', '');
        expect(dot('Spring')).toBe('waiting');
        expect(dot('Autumn')).toBe('draft');
        expect(dot('Summer referrals')).toBe('paused');
        flush(() => {
            attention.approvals.value = [];
            attention.questions.value = [];
        });
        await waitFor(() => expect(dot('Spring')).toBe('active'));
    });

    it('pulses the dot of an agent whose chat runs a turn now, and no other', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        const pulsing = () =>
            rows()
                .filter((each) => each.querySelector('.rdot.running'))
                .map((each) => each.querySelector('.rt')?.textContent);
        expect(pulsing()).toEqual([]);
        flush(() => {
            conversations.value = conversations.value.map((each) =>
                each.id === 'conv_a' ? { ...each, running: true } : each,
            );
        });
        await waitFor(() => expect(pulsing()).toEqual(['Autumn']));
    });

    it('gives the dot of an agent that is not held no title', async () => {
        await renderConsole((desktop) => {
            seedAgents(desktop);
            desktop.keep({ id: 'conv_g', campaignId: 'cmp_gone', title: 'Archived one', updatedAt: minutes(10.5) });
        });
        await waitFor(() => expect(rows()).toHaveLength(5));
        const ring = row('Archived one').querySelector('.rdot');
        expect(ring?.className).toContain('none');
        expect(ring?.hasAttribute('title')).toBe(false);
        expect(row('Help me start').querySelector('.rdot')).toBeNull();
    });

    it('leaves out the chat of an archived agent once that agent is read, and its own address still opens it', async () => {
        const desktop = await renderConsole((desktop) => {
            seedAgents(desktop);
            // Winter (cmp_4) is archived: the list of agents leaves it out, so its agent is read on its own.
            desktop.keep({ id: 'conv_w', campaignId: 'cmp_4', title: 'Winter', updatedAt: minutes(1.5) });
        });
        await waitFor(() => expect(aloneOf('campaign.get', 'cmp_4')).toBe(1));
        await waitFor(() => expect(rows()).toHaveLength(4));
        expect(titles()).not.toContain('Winter');
        expect(titlesOf(chatsList())).not.toContain('Winter');
        flush(() => {
            location.hash = '#/campaigns/cmp_4/chat';
        });
        await waitFor(() =>
            expect(desktop.calls.some((each) => each.name === 'chat.open' && each.args[0] === 'conv_w')).toBe(true),
        );
    });

    it('reads an agent the list of agents does not hold by its state alone, once, and on a change only the new one', async () => {
        const desktop = await renderConsole((desktop) => {
            seedAgents(desktop);
            desktop.keep({ id: 'conv_w', campaignId: 'cmp_4', title: 'Winter', updatedAt: minutes(1.5) });
            desktop.keep({ id: 'conv_g', campaignId: 'cmp_gone', title: 'Archived one', updatedAt: minutes(10.5) });
        });
        await waitFor(() =>
            expect([aloneOf('campaign.get', 'cmp_4'), aloneOf('campaign.get', 'cmp_gone')]).toEqual([1, 1]),
        );
        await waitFor(() => expect(titles()).not.toContain('Winter'));
        // An agent the runtime does not know keeps its conversation, as one whose agent is not held.
        expect(titles()).toContain('Archived one');
        // A conversation of one more agent the list does not hold: that agent alone is read.
        desktop.keep({ id: 'conv_p', campaignId: 'cmp_5', title: 'Winter plan', updatedAt: minutes(0.5) });
        await flush(() => reloadConversations());
        await waitFor(() => expect(aloneOf('campaign.get', 'cmp_5')).toBe(1));
        expect([aloneOf('campaign.get', 'cmp_4'), aloneOf('campaign.get', 'cmp_gone')]).toEqual([1, 1]);
        // Its state and nothing else: neither its people nor its work.
        for (const id of ['cmp_4', 'cmp_gone', 'cmp_5'])
            expect([aloneOf('campaign.summary', id), aloneOf('workitem.list', id)], id).toEqual([0, 0]);
    });

    it('opens its chat when clicked, as before', async () => {
        const desktop = await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        fireEvent.click(row('Autumn'));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.open')).toBe(true));
        await waitFor(() => expect(row('Autumn').getAttribute('aria-current')).toBe('true'));
        fireEvent.click(row('Help me start'));
        await waitFor(() => expect(row('Help me start').getAttribute('aria-current')).toBe('true'));
        expect(row('Autumn').getAttribute('aria-current')).toBeNull();
    });
});

describe('paging', () => {
    const seedMany = (desktop: StubDesktop) => {
        for (let at = 0; at < 25; at += 1)
            desktop.keep({ id: `conv_m${String(at).padStart(2, '0')}`, title: `Chat ${at}`, updatedAt: minutes(at) });
    };

    it('shows ten chats, the newest first, then adds ten with each Load more until all are shown', async () => {
        await renderConsole(seedMany);
        await waitFor(() => expect(rowsOf(chatsList())).toHaveLength(10));
        expect(titlesOf(chatsList())[0]).toBe('Chat 0');
        const more = () => within(chatsList()).queryByRole('button', { name: 'Load more' });
        fireEvent.click(more()!);
        expect(rowsOf(chatsList())).toHaveLength(20);
        fireEvent.click(more()!);
        expect(rowsOf(chatsList())).toHaveLength(25);
        expect(more()).toBeNull();
        expect(within(agentsList()).queryByRole('button', { name: 'Load more' })).toBeNull();
    });

    it('keeps the chats Load more uncovered when the filter of AGENTS changes', async () => {
        await renderConsole(seedMany);
        await waitFor(() => expect(rowsOf(chatsList())).toHaveLength(10));
        fireEvent.click(within(chatsList()).getByRole('button', { name: 'Load more' }));
        expect(rowsOf(chatsList())).toHaveLength(20);
        choose('Live');
        choose('All');
        expect(rowsOf(chatsList())).toHaveLength(20);
    });
});

describe('the empty texts', () => {
    it('says in one line that there is no agent and no chat yet, the plus being the way to each', async () => {
        await renderConsole(() => undefined, 'campaign.list/screen-empty');
        await waitFor(() => expect(side().getByText('No chats yet: the plus starts one.')).toBeTruthy());
        await waitFor(() => expect(side().getByText('No agents yet: the plus makes one.')).toBeTruthy());
        expect(side().queryByText('No agents match.')).toBeNull();
        // The line stands inside the section, which has no All agents while it holds none.
        expect(within(agentsList()).getByText('No agents yet: the plus makes one.')).toBeTruthy();
        expect(within(chatsList()).getByText('No chats yet: the plus starts one.')).toBeTruthy();
        expect(side().queryByRole('button', { name: 'All agents' })).toBeNull();
    });

    it('names no plus that cannot be pressed', async () => {
        await renderConsole(() => undefined, 'campaign.list/screen-empty');
        await waitFor(() => expect(within(chatsList()).getByText('No chats yet: the plus starts one.')).toBeTruthy());
        // The same sections, drawn where the page has no desktop: neither plus can be pressed.
        const kind = placeKind();
        const desktop = currentDesktop();
        try {
            setPlace('none');
            flush(() => {
                clock.value += 1;
            });
            await waitFor(() => expect(within(agentsList()).getByText('No agents yet.')).toBeTruthy());
            await waitFor(() => expect(within(chatsList()).getByText('No chats yet.')).toBeTruthy());
            expect(side().queryByText(/the plus/)).toBeNull();
        } finally {
            setPlace(kind, desktop);
        }
    });

    it('says no chat is there only once the desktop has listed its conversations', async () => {
        let list: () => void = () => undefined;
        await renderConsole((desktop) => {
            const listed = desktop.chat.list.bind(desktop.chat);
            desktop.chat.list = () =>
                new Promise((done) => {
                    list = () => done(listed());
                });
        }, 'campaign.list/screen-empty');
        await waitFor(() => expect(within(agentsList()).getByText('No agents yet: the plus makes one.')).toBeTruthy());
        expect(side().queryByText('No chats yet: the plus starts one.')).toBeNull();
        flush(() => list());
        await waitFor(() => expect(within(chatsList()).getByText('No chats yet: the plus starts one.')).toBeTruthy());
    });

    it('says No agents match. when the filter hides every agent', async () => {
        await renderConsole(seedAgents);
        await waitFor(() => expect(rows()).toHaveLength(4));
        expect(side().queryByText('No agents match.')).toBeNull();
        flush(() => {
            attention.approvals.value = [];
            attention.questions.value = [];
        });
        choose('Needs you');
        expect(side().getByText('No agents match.')).toBeTruthy();
        expect(side().queryByText('No agents yet: the plus makes one.')).toBeNull();
    });
});
