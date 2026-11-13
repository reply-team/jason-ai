import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { mode } from '../platform/mode';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { chatDesktop, journalLine, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import * as dashboard from './dashboard';
import { attention, filter } from './needs';
import { chats, lines } from './state';

// The Dashboard: opened from the sidebar row after the Feed, drawn from the reads the console already makes and one
// read of the thirty days' counts. That read has no recording yet, so these rows leave it unreachable: its tiles are
// dashes, and the panel's drawing of counts is held by setting what a read would hold.

type Options = { approvals?: 'approval.list/pending' | 'approval.list/empty'; none?: boolean };

function renderConsole(options: Options = {}) {
    const fake = new FakeApi()
        .answer('approval.list', options.approvals ?? 'approval.list/pending')
        .answer(
            'decision.list',
            options.approvals === 'approval.list/empty' ? 'decision.list/empty' : 'decision.list/pending',
        )
        .answer('campaign.list', options.none ? 'campaign.list/screen-empty' : 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
    fake.unreachable('campaign.activity');
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
    const journal = stubStream();
    render(<Console api={fake} desktop={chatDesktop()} stream={journal.stream} />);
    return { fake, journal };
}

const side = () => document.querySelector<HTMLElement>('.side')!;
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
const dashboardRow = () => within(side()).getByRole('button', { name: 'Dashboard' });

/** Opens the panel from the sidebar and waits for its agents to be read. */
async function open(): Promise<HTMLElement> {
    await waitFor(() => expect(dashboardRow()).toBeTruthy());
    fireEvent.click(dashboardRow());
    await waitFor(() => expect(document.querySelector('.dash')).toBeTruthy());
    await waitFor(() => expect(document.querySelector('.dash .dash-tiles, .dash .dash-empty')).toBeTruthy());
    return document.querySelector<HTMLElement>('.dash')!;
}

// The panel is painted afresh when a signal changes, so every query goes to the page as it is now.
const dash = () => document.querySelector<HTMLElement>('.dash')!;
const tile = (_panel: HTMLElement, label: string) => dash().querySelector<HTMLElement>(`[data-tile="${label}"]`)!;
const part = (label: string, selector: string) => words(tile(dash(), label).querySelector(selector)!);
const settled = () => new Promise((resolve) => setTimeout(resolve, 30));

beforeEach(() => {
    chats.value = {};
    lines.value = [];
    attention.loaded.value = false;
    attention.detail.value = null;
    attention.approvals.value = [];
    attention.questions.value = [];
    filter.value = 'all';
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    dashboard.activity.value = null;
    mode.value = 'live';
});

afterEach(() => {
    mode.value = 'live';
});

describe('the Dashboard', () => {
    it('is a sidebar row right after the Feed, and the row opens the panel', async () => {
        renderConsole();
        await waitFor(() => expect(dashboardRow()).toBeTruthy());
        const feed = within(side()).getByRole('navigation', { name: 'Feed' });
        const here = within(side()).getByRole('navigation', { name: 'Dashboard' });
        expect(feed.nextElementSibling).toBe(here);
        expect(document.querySelector('.dash')).toBeNull();
        await open();
        expect(document.querySelector('.dash')).toBeTruthy();
    });

    it('draws six tiles in order, and the three the runtime holds no fact for are dashes, never zeros', async () => {
        renderConsole();
        const panel = await open();
        const labels = [...panel.querySelectorAll('.dash-tile')].map((each) => each.getAttribute('data-tile'));
        expect(labels).toEqual([
            'Needs you',
            'Contacted · 30 days',
            'Replied · 30 days',
            'Interested',
            'Meetings booked',
            'Bounce rate',
        ]);
        for (const label of ['Interested', 'Meetings booked', 'Bounce rate']) {
            expect(part(label, 'b')).toBe('–');
            expect(part(label, '.rel')).toBe('no data yet');
        }
        // The read of the thirty days did not answer: its tiles are dashes as well, and no tile reads zero.
        expect(part('Contacted · 30 days', 'b')).toBe('–');
        expect(part('Replied · 30 days', 'b')).toBe('–');
        expect(
            [...panel.querySelectorAll('.dash-tile b')].map((each) => words(each)).filter((each) => each === '0'),
        ).toEqual([]);
    });

    it('counts what waits in the first tile with its subline', async () => {
        renderConsole();
        await open();
        await waitFor(() => expect(attention.approvals.value.length).toBeGreaterThan(0));
        const total = attention.approvals.value.length + attention.questions.value.length;
        await waitFor(() => expect(part('Needs you', 'b')).toBe(String(total)));
        expect(part('Needs you', '.rel')).toBe(
            `${attention.approvals.value.length} approvals · ${attention.questions.value.length} questions`,
        );
    });

    it('draws the thirty days’ counts, the reply rate under replied, and a bar of contacted over people', async () => {
        renderConsole();
        await open();
        await settled();
        dashboard.activity.value = {
            total: { contacted: 40, replied: 5 },
            byCampaign: { cmp_1: { contacted: 11, replied: 2 } },
        };
        await waitFor(() => expect(part('Contacted · 30 days', 'b')).toBe('40'));
        expect(part('Replied · 30 days', 'b')).toBe('5');
        expect(part('Replied · 30 days', '.rel')).toBe('12.5% reply rate');
        const row = dash().querySelector<HTMLElement>('.dash-agents tr[data-id="cmp_1"]')!;
        const cells = [...row.querySelectorAll('td')].map((each) => words(each));
        expect(cells.slice(3, 6)).toEqual(['11', '2', '–']);
        expect(row.querySelector('progress')?.getAttribute('value')).toBe(String(11 / 22));
    });

    it('shows no reply rate when nobody was contacted', async () => {
        renderConsole();
        const panel = await open();
        await settled();
        dashboard.activity.value = { total: { contacted: 0, replied: 0 }, byCampaign: {} };
        await waitFor(() => expect(part('Replied · 30 days', 'b')).toBe('0'));
        expect(tile(panel, 'Replied · 30 days').querySelector('.rel')).toBeNull();
    });
});

describe('the Dashboard header', () => {
    it('greets by the hour with no name and no comma, and says its live line', async () => {
        renderConsole();
        const panel = await open();
        const heading = panel.querySelector('h1')!;
        expect(['Good morning', 'Good afternoon', 'Good evening']).toContain(words(heading));
        expect(words(panel.querySelector('.ph')!)).toContain('Here is where your real work stands.');
    });

    it('says the demo’s own line in the demo', async () => {
        mode.value = 'demo';
        renderConsole();
        const panel = await open();
        const head = words(panel.querySelector('.ph')!);
        expect(head).toContain('Jason has been working while you were away.');
        expect(head).not.toContain('real work');
    });

    it('offers Review N waiting only while something waits, and it opens Needs you', async () => {
        renderConsole();
        const panel = await open();
        const total = () => attention.approvals.value.length + attention.questions.value.length;
        await waitFor(() => expect(total()).toBeGreaterThan(0));
        const review = await waitFor(() => within(panel).getByRole('button', { name: `Review ${total()} waiting` }));
        expect(within(panel).getByRole('button', { name: 'New GTM agent' })).toBeTruthy();
        fireEvent.click(review);
        await waitFor(() => expect(screen.getByRole('heading', { name: 'Needs you', level: 1 })).toBeTruthy());
    });

    it('has no Review button when nothing waits', async () => {
        renderConsole({ approvals: 'approval.list/empty' });
        const panel = await open();
        await waitFor(() => expect(attention.loaded.value).toBe(true));
        expect(within(panel).queryByRole('button', { name: /^Review/ })).toBeNull();
        expect(within(panel).getByRole('button', { name: 'New GTM agent' })).toBeTruthy();
    });
});

describe('the cards', () => {
    it('lists the first waiting approvals and questions, with All N linking to Needs you', async () => {
        renderConsole();
        await open();
        const card = () => dash().querySelector<HTMLElement>('.dash-decide')!;
        await waitFor(() => expect(card().querySelectorAll('.dash-row').length).toBeGreaterThan(0));
        const total = attention.approvals.value.length + attention.questions.value.length;
        expect(card().querySelectorAll('.dash-row').length).toBeLessThanOrEqual(6);
        expect(words(card().querySelector('.hd .btn')!)).toBe(`All ${total} →`);
        const first = card().querySelector<HTMLElement>('.dash-row')!;
        expect(words(first)).toContain(attention.approvals.value[0]!.campaign_name);
        fireEvent.click(card().querySelector('.hd .btn')!);
        await waitFor(() => expect(screen.getByRole('heading', { name: 'Needs you', level: 1 })).toBeTruthy());
    });

    it('cuts a question at ninety characters and names its agent', async () => {
        renderConsole();
        await open();
        await waitFor(() => expect(attention.questions.value.length).toBeGreaterThan(0));
        attention.questions.value = attention.questions.value.map((each) => ({ ...each, question: 'q'.repeat(120) }));
        const card = () => dash().querySelector<HTMLElement>('.dash-decide')!;
        await waitFor(() => expect(card().textContent).toContain(`${'q'.repeat(89)}…`));
        expect(card().textContent).not.toContain('q'.repeat(90));
        expect(card().textContent).toContain(`Question · ${attention.questions.value[0]!.campaign_name}`);
    });

    it('says nothing waits when nothing does', async () => {
        renderConsole({ approvals: 'approval.list/empty' });
        await open();
        await waitFor(() => expect(attention.loaded.value).toBe(true));
        const card = () => dash().querySelector<HTMLElement>('.dash-decide')!;
        await waitFor(() => expect(words(card().querySelector('.bd')!)).toBe('Nothing waits for you.'));
        expect(card().querySelector('.hd .btn')).toBeNull();
    });

    it('lists only the agents that are running, with a dash for meetings, and All agents opens Agents', async () => {
        renderConsole();
        await open();
        const card = dash().querySelector<HTMLElement>('.dash-agents')!;
        const rows = [...card.querySelectorAll('tbody tr')];
        expect(rows.map((each) => words(each.querySelector('b')!))).toEqual(['Spring']);
        expect([...card.querySelectorAll('thead th')].map((each) => words(each))).toEqual([
            'Agent',
            'Status',
            'People',
            'Contacted',
            'Replied',
            'Meetings',
            'Progress',
        ]);
        fireEvent.click(within(card).getByRole('button', { name: 'All agents →' }));
        await waitFor(() => expect(screen.getByRole('heading', { name: 'Agents', level: 1 })).toBeTruthy());
    });

    it('says none is running when no agent is', async () => {
        renderConsole();
        await open();
        consoleCampaigns.campaigns.value = consoleCampaigns.campaigns.value.filter((each) => each.status !== 'active');
        await waitFor(() => expect(dash().querySelector('.dash-no-agents')).toBeTruthy());
        expect(words(dash().querySelector('.dash-no-agents')!)).toBe(
            'None of your agents is running yet. An agent starts when you tell it to in its chat, or with Start at the top of its page.',
        );
    });

    it('draws the latest lines of the Feed, six at most, the one naming an agent opening it', async () => {
        const { journal } = renderConsole();
        await open();
        const { act } = await import('@testing-library/react');
        act(() => {
            journal.open();
            for (let at = 1; at <= 8; at += 1)
                journal.send(
                    journalLine({
                        id: `jrn_${400 + at}`,
                        ts: '2026-11-11T10:00:00.000Z',
                        kind: 'campaign_started',
                        campaign_id: 'cmp_1',
                        campaign_name: 'Spring',
                    }),
                );
        });
        const card = () => dash().querySelector<HTMLElement>('.dash-recent')!;
        await waitFor(() => expect(card().querySelectorAll('.dash-line')).toHaveLength(6));
        const line = card().querySelector<HTMLElement>('.dash-line')!;
        expect(words(line.querySelector('b')!)).toBe('Agent started');
        expect(words(line.querySelectorAll('div')[1]!)).toMatch(/ · /);
        fireEvent.click(within(line).getByRole('button', { name: 'Spring' }));
        await waitFor(() => expect(document.querySelector('.chat-panel, .composer, [data-chat]')).not.toBeUndefined());
    });
});

describe('the empty Dashboard', () => {
    it('shows the heading, one card and New GTM agent when there is no agent at all', async () => {
        renderConsole({ none: true });
        const panel = await open();
        expect(words(panel.querySelector('h1')!)).toBe('Dashboard');
        expect(panel.querySelector('.dash-tiles')).toBeNull();
        const card = panel.querySelector<HTMLElement>('.dash-empty')!;
        expect(words(card.querySelector('h3')!)).toBe('Nothing to show yet');
        expect(words(card.querySelector('p')!)).toBe(
            'Once an agent is working, the people it reaches and the replies it gets show up here.',
        );
        expect(within(card).getByRole('button', { name: 'New GTM agent' })).toBeTruthy();
        expect(panel.textContent).not.toMatch(/meeting/i);
    });
});
