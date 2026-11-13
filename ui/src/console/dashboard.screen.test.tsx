import { act as flushNow, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import { mode } from '../platform/mode';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { chatDesktop, journalLine, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { fixtures, type FixtureName } from '../test/fixtures';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import * as dashboard from './dashboard';
import { attention, filter } from './needs';
import { chats, lines } from './state';

// The Dashboard: opened from the sidebar row after the Feed, drawn from the reads the console already makes and two
// reads of its own over one period: the counts and the replies a role judged, by category. The counts are recorded for
// the window that ends at the moment these rows fix.

const now = new Date('2026-10-16T09:00:00Z');

type Options = {
    approvals?: 'approval.list/pending' | 'approval.list/empty';
    none?: boolean;
    /** The counts' read does not answer. */
    unread?: boolean;
    /** The recording reply.count answers with, whatever window the page asks; every campaign's by default. */
    replies?: 'reply.count/every-campaign' | 'reply.count/archived-named' | 'reply.count/until-before-since';
    /**
     * The daily trends are on, and the two 24-hour windows are answered from recordings: the last window with the first
     * of the pair, the one before with the second. Without it the desktop keeps them off, so no row asks for them.
     */
    trends?: 'rising' | 'falling' | 'level' | 'last-refused';
    /** campaign.activity answers any window with the recorded 30 days' answer: for rows that choose another period. */
    anyWindow?: boolean;
};

function renderConsole(options: Options = {}) {
    const fake = new FakeApi()
        .answer('approval.list', options.approvals ?? 'approval.list/pending')
        .answer(
            'decision.list',
            options.approvals === 'approval.list/empty' ? 'decision.list/empty' : 'decision.list/pending',
        )
        .answer('campaign.list', options.none ? 'campaign.list/screen-empty' : 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
    if (options.unread) fake.unreachable('campaign.activity');
    else fake.answer('campaign.activity', 'campaign.activity/all-30-days');
    fake.answerAnyRequest('reply.count', options.replies ?? 'reply.count/every-campaign');
    if (options.anyWindow) fake.answerAnyRequest('campaign.activity', 'campaign.activity/all-30-days');
    if (options.trends) {
        const sinceOf = (body: object) => (body as { since: string }).since;
        const pairs = {
            rising: [
                'campaign.activity/all-7-days',
                'campaign.activity/all-today',
                'reply.count/every-campaign',
                'reply.count/archived-named',
            ],
            falling: [
                'campaign.activity/all-today',
                'campaign.activity/all-7-days',
                'reply.count/archived-named',
                'reply.count/every-campaign',
            ],
            level: [
                'campaign.activity/all-7-days',
                'campaign.activity/all-7-days',
                'reply.count/every-campaign',
                'reply.count/every-campaign',
            ],
            'last-refused': [
                'campaign.activity/all-7-days',
                'campaign.activity/all-today',
                'reply.count/until-before-since',
                'reply.count/archived-named',
            ],
        } as const;
        const [lastCounts, beforeCounts, lastReplies, beforeReplies] = pairs[options.trends];
        const back = (hours: number) => new Date(now.getTime() - hours * 60 * 60 * 1000).toISOString();
        const pick = (last: FixtureName, before: FixtureName, main: FixtureName) => (body: object) =>
            sinceOf(body) === back(24) ? last : sinceOf(body) === back(48) ? before : main;
        fake.answerAnyRequest('campaign.activity', pick(lastCounts, beforeCounts, 'campaign.activity/all-30-days'));
        fake.answerAnyRequest(
            'reply.count',
            pick(lastReplies, beforeReplies, options.replies ?? 'reply.count/every-campaign'),
        );
    }
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
    const desktop = chatDesktop();
    desktop.dailyTrendsKept = options.trends ? null : false;
    render(<Console api={fake} desktop={desktop} stream={journal.stream} />);
    return { fake, journal, desktop };
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
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-16T09:00:00Z'));
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
    dashboard.replies.value = null;
    dashboard.repliesStatus.value = 'loading';
    dashboard.period.value = '30d';
    dashboard.custom.value = { from: '', to: '' };
    mode.value = 'live';
});

afterEach(() => {
    vi.useRealTimers();
    mode.value = 'live';
});

describe('the Dashboard', () => {
    it('is a sidebar row right after the Feed, and the row opens the panel', async () => {
        renderConsole();
        await waitFor(() => expect(dashboardRow()).toBeTruthy());
        const feed = within(side()).getByRole('button', { name: /^Feed/ });
        expect(feed.nextElementSibling).toBe(dashboardRow());
        expect(document.querySelector('.dash')).toBeNull();
        await open();
        expect(document.querySelector('.dash')).toBeTruthy();
    });

    it('draws six tiles in order, and the one no read gives a fact for is a dash, never a zero', async () => {
        renderConsole();
        const panel = await open();
        const labels = [...panel.querySelectorAll('.dash-tile')].map((each) => each.getAttribute('data-tile'));
        expect(labels).toEqual(['Needs you', 'Contacted', 'Replied', 'Interested', 'Meetings booked', 'Bounce rate']);
        expect(part('Bounce rate', 'b')).toBe('–');
        expect(part('Bounce rate', '.rel')).toBe('no data yet');
        // The tiles with a fact show it, and no tile reads zero.
        await waitFor(() => expect(part('Contacted', 'b')).toBe('11'));
        expect(part('Replied', 'b')).toBe('3');
        expect(
            [...dash().querySelectorAll('.dash-tile b')].map((each) => words(each)).filter((each) => each === '0'),
        ).toEqual([]);
    });

    it('shows a dash with no data yet on Needs you while what waits is not yet read', async () => {
        const { fake } = renderConsole();
        fake.hold('approval.list');
        await open();
        await settled();
        expect(attention.loaded.value).toBe(false);
        expect(part('Needs you', 'b')).toBe('–');
        expect(part('Needs you', '.rel')).toBe('no data yet');
    });

    it('shows the same on Needs you when what waits could not be read', async () => {
        const { fake } = renderConsole();
        fake.unreachable('approval.list');
        await open();
        await settled();
        expect(attention.failure.value).not.toBeNull();
        expect(part('Needs you', 'b')).toBe('–');
        expect(part('Needs you', '.rel')).toBe('no data yet');
    });

    it('shows dashes with no data yet for the period’s tiles while their read does not answer', async () => {
        renderConsole({ unread: true });
        await open();
        await settled();
        expect(part('Contacted', 'b')).toBe('–');
        expect(part('Contacted', '.rel')).toBe('no data yet');
        expect(part('Replied', 'b')).toBe('–');
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

    it('draws the period’s counts, the reply rate under replied, and a bar of contacted over people', async () => {
        const { fake } = renderConsole();
        await open();
        await waitFor(() => expect(part('Contacted', 'b')).toBe('11'));
        expect(part('Replied', 'b')).toBe('3');
        expect(part('Replied', '.rel')).toBe('27.3% reply rate');
        const asked = fake.calls.find((each) => each.operation === 'campaign.activity')!;
        expect(asked.body).toEqual({
            exclude_archived_contacts: true,
            since: '2026-09-17T00:00:00.000Z',
            until: '2026-10-16T09:00:00.000Z',
        });
        const row = dash().querySelector<HTMLElement>('.dash-agents tr[data-id="cmp_1"]')!;
        const cells = [...row.querySelectorAll('td')].map((each) => words(each));
        expect(cells.slice(3, 6)).toEqual(['11', '3', '–']);
        expect(row.querySelector('progress')?.getAttribute('value')).toBe(String(11 / 22));
        expect(row.querySelector('progress')?.getAttribute('aria-label')).toBe(
            'Contacted over the last 30 days, of the people in Spring',
        );
        expect(words(dash().querySelector('.dash-agents .hd .dash-period')!)).toBe('the last 30 days');
    });

    it('reads Interested as the conversations judged so, and says on hover what Interested and Meetings booked count', async () => {
        const { fake } = renderConsole();
        await open();
        await waitFor(() => expect(part('Interested', 'b')).toBe('1'));
        expect(tile(dash(), 'Interested').getAttribute('title')).toBe(
            'Conversations the reply-reading role judged interested.',
        );
        expect(tile(dash(), 'Meetings booked').getAttribute('title')).toBe(
            'Conversations the reply-reading role judged to have a meeting booked.',
        );
        expect(fake.sent('reply.count')).toEqual({
            since: '2026-09-17T00:00:00.000Z',
            until: '2026-10-16T09:00:00.000Z',
        });
    });

    it('holds a dash on Meetings booked where the answer has no entry of its word, and fills it where it has', async () => {
        renderConsole();
        await open();
        await waitFor(() => expect(part('Interested', 'b')).toBe('1'));
        expect(part('Meetings booked', 'b')).toBe('–');
        const total = fixtures['reply.count/every-campaign'].response.total;
        dashboard.replies.value = {
            total: {
                ...total,
                categories: [...total.categories, { category: 'meeting_booked', replies: 4, threads: 3 }],
            },
        };
        await waitFor(() => expect(part('Meetings booked', 'b')).toBe('3'));
    });

    it('holds a dash on Interested and Meetings booked where the answer has no entry of their words', async () => {
        renderConsole({ replies: 'reply.count/archived-named' });
        await open();
        await waitFor(() => expect(dash().querySelector('.dash-categories tbody tr')).toBeTruthy());
        expect(part('Interested', 'b')).toBe('–');
        expect(part('Meetings booked', 'b')).toBe('–');
    });

    it('holds a dash on both and says the replies could not be read when reply.count is refused', async () => {
        renderConsole({ replies: 'reply.count/until-before-since' });
        await open();
        await settled();
        expect(part('Interested', 'b')).toBe('–');
        expect(words(dash().querySelector('.dash-categories .bd')!)).toBe('The replies could not be read.');
    });

    it('shows no reply rate when nobody was contacted', async () => {
        renderConsole({ unread: true });
        const panel = await open();
        await settled();
        dashboard.activity.value = { total: { contacted: 0, replied: 0 }, byCampaign: {} };
        await waitFor(() => expect(part('Replied', 'b')).toBe('0'));
        expect(tile(panel, 'Replied').querySelector('.rel')).toBeNull();
    });
});

describe('the categories card', () => {
    it('lists every category of the answer in its order with conversations and replies', async () => {
        renderConsole();
        await open();
        await waitFor(() => expect(dash().querySelector('.dash-categories tbody tr')).toBeTruthy());
        const card = dash().querySelector<HTMLElement>('.dash-categories')!;
        expect([...card.querySelectorAll('thead th')].map((each) => words(each))).toEqual([
            'Category',
            'Conversations',
            'Replies',
        ]);
        expect(
            [...card.querySelectorAll('tbody tr')].map((each) => [...each.querySelectorAll('td')].map(words)),
        ).toEqual([
            ['Interested', '1', '2'],
            ['Wrong person', '1', '1'],
        ]);
        expect(words(card.querySelector('.hd .dash-period')!)).toBe('the last 30 days');
    });

    it('says it is reading while the first answer is awaited, and carries no failure sentence', async () => {
        const { fake } = renderConsole();
        const release = fake.hold('reply.count');
        await open();
        await waitFor(() => expect(words(dash().querySelector('.dash-categories .bd')!)).toBe('Reading the replies…'));
        expect(dash().querySelector('.dash-cats-failed')).toBeNull();
        release();
        await waitFor(() => expect(dash().querySelector('.dash-categories tbody tr')).toBeTruthy());
    });

    it('says nothing in the card for a custom range that asks for nothing, the hint being the only explanation', async () => {
        renderConsole({ anyWindow: true });
        await open();
        const select = dash().querySelector<HTMLSelectElement>('select[data-change="dash.period"]')!;
        select.value = 'custom';
        fireEvent.change(select);
        await waitFor(() => expect(dash().querySelector('input[aria-label="From"]')).toBeTruthy());
        const from = dash().querySelector<HTMLInputElement>('input[aria-label="From"]')!;
        from.value = '2026-10-20';
        fireEvent.change(from);
        await waitFor(() => expect(dash().querySelector('.dash-range-hint')).toBeTruthy());
        await waitFor(() => expect(words(dash().querySelector('.dash-categories .bd')!)).toBe(''));
        expect(dash().textContent).not.toContain('could not be read');
    });

    it('says no reply was judged when the answer has no category', async () => {
        renderConsole();
        await open();
        await waitFor(() => expect(dash().querySelector('.dash-categories tbody tr')).toBeTruthy());
        dashboard.replies.value = { total: { replies: 0, threads: 0, categories: [] } };
        await waitFor(() => expect(dash().querySelector('.dash-cats-none')).toBeTruthy());
        expect(words(dash().querySelector('.dash-cats-none')!)).toBe('No reply has been judged in this period.');
    });
});

describe('the period', () => {
    const choose = (value: string) => {
        const select = dash().querySelector<HTMLSelectElement>('select[data-change="dash.period"]')!;
        select.value = value;
        fireEvent.change(select);
    };
    /** The option the painted select marks as chosen. */
    const chosen = () =>
        dash().querySelector('select[data-change="dash.period"] option[selected]')?.getAttribute('value');
    const asked = (fake: FakeApi, operation: string) =>
        fake.calls.filter((each) => each.operation === operation).map((each) => each.body);

    it('offers seven days, thirty days, three months, six months, this year and Custom, opening on thirty days', async () => {
        renderConsole({ anyWindow: true });
        await open();
        const select = dash().querySelector<HTMLSelectElement>('select[data-change="dash.period"]')!;
        expect(select.getAttribute('aria-label')).toBe('Period');
        expect([...select.options].map((each) => each.textContent!.trim())).toEqual([
            '7 days',
            '30 days',
            '3 months',
            '6 months',
            'This year',
            'Custom',
        ]);
        expect(select.value).toBe('30d');
    });

    it('counts every tile and card over the chosen period, both reads asking the same window', async () => {
        const { fake } = renderConsole({ anyWindow: true });
        await open();
        await waitFor(() => expect(part('Contacted', 'b')).toBe('11'));
        choose('3m');
        const window = { since: '2026-07-16T00:00:00.000Z', until: '2026-10-16T09:00:00.000Z' };
        await waitFor(() => expect(asked(fake, 'reply.count').at(-1)).toEqual(window));
        expect(asked(fake, 'campaign.activity').at(-1)).toEqual({ exclude_archived_contacts: true, ...window });
        await waitFor(() =>
            expect(words(dash().querySelector('.dash-agents .hd .dash-period')!)).toBe('the last 3 months'),
        );
        expect(words(dash().querySelector('.dash-categories .hd .dash-period')!)).toBe('the last 3 months');
    });

    it('asks for this year from 1 January, and for seven days from six days back', async () => {
        const { fake } = renderConsole({ anyWindow: true });
        await open();
        choose('year');
        await waitFor(() =>
            expect(asked(fake, 'reply.count').at(-1)).toMatchObject({ since: '2026-01-01T00:00:00.000Z' }),
        );
        choose('7d');
        await waitFor(() =>
            expect(asked(fake, 'reply.count').at(-1)).toMatchObject({ since: '2026-10-10T00:00:00.000Z' }),
        );
    });

    it('offers From and To dates on Custom, begun on the thirty days, and reads the days the person sets', async () => {
        const { fake } = renderConsole({ anyWindow: true });
        await open();
        choose('custom');
        await waitFor(() => expect(dash().querySelector('input[aria-label="From"]')).toBeTruthy());
        expect(dash().querySelector<HTMLInputElement>('input[aria-label="From"]')!.value).toBe('2026-09-17');
        expect(dash().querySelector<HTMLInputElement>('input[aria-label="To"]')!.value).toBe('2026-10-16');
        const from = dash().querySelector<HTMLInputElement>('input[aria-label="From"]')!;
        from.value = '2026-09-01';
        fireEvent.change(from);
        const to = dash().querySelector<HTMLInputElement>('input[aria-label="To"]')!;
        to.value = '2026-09-30';
        fireEvent.change(to);
        await waitFor(() =>
            expect(asked(fake, 'reply.count').at(-1)).toEqual({
                since: '2026-09-01T00:00:00.000Z',
                until: '2026-10-01T00:00:00.000Z',
            }),
        );
        expect(words(dash().querySelector('.dash-categories .hd .dash-period')!)).toBe('the chosen dates');
    });

    it('asks for nothing and shows dashes while a custom range is backwards', async () => {
        const { fake } = renderConsole({ anyWindow: true });
        await open();
        choose('custom');
        await waitFor(() => expect(dash().querySelector('input[aria-label="From"]')).toBeTruthy());
        const calls = asked(fake, 'reply.count').length;
        const from = dash().querySelector<HTMLInputElement>('input[aria-label="From"]')!;
        from.value = '2026-10-20';
        fireEvent.change(from);
        await waitFor(() => expect(part('Contacted', 'b')).toBe('–'));
        expect(asked(fake, 'reply.count')).toHaveLength(calls);
    });

    it.each([
        ['', '2026-09-30', 'Choose both a From date and a To date.'],
        ['2026-10-20', '2026-10-10', 'Choose a From date that is not after To.'],
        ['2026-11-01', '2026-11-30', 'Choose dates that are not in the future.'],
    ])('says why a custom range from %s to %s asks for nothing', async (from, to, line) => {
        renderConsole({ anyWindow: true });
        await open();
        choose('custom');
        await waitFor(() => expect(dash().querySelector('input[aria-label="From"]')).toBeTruthy());
        expect(dash().querySelector('.dash-range-hint')).toBeNull();
        for (const [label, value] of [
            ['From', from],
            ['To', to],
        ] as const) {
            const input = dash().querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!;
            input.value = value;
            fireEvent.change(input);
            await settled();
        }
        await waitFor(() => expect(words(dash().querySelector('.dash-period-pick .dash-range-hint')!)).toBe(line));
        expect(dash().querySelector('.dash-period-pick .dash-range-hint')!.getAttribute('role')).toBe('status');
        expect(part('Contacted', 'b')).toBe('–');
    });

    it('links the range line to both date fields', async () => {
        renderConsole({ anyWindow: true });
        await open();
        choose('custom');
        await waitFor(() => expect(dash().querySelector('input[aria-label="From"]')).toBeTruthy());
        const from = dash().querySelector<HTMLInputElement>('input[aria-label="From"]')!;
        from.value = '2026-10-20';
        fireEvent.change(from);
        await waitFor(() => expect(dash().querySelector('.dash-range-hint')).toBeTruthy());
        const hint = dash().querySelector('.dash-range-hint')!;
        expect(hint.id).toBe('dash-range-hint');
        for (const label of ['From', 'To'])
            expect(dash().querySelector(`input[aria-label="${label}"]`)!.getAttribute('aria-describedby')).toBe(
                hint.id,
            );
    });

    it('has no range line for a range that asks, nor outside Custom', async () => {
        renderConsole({ anyWindow: true });
        await open();
        choose('custom');
        await waitFor(() => expect(dash().querySelector('input[aria-label="From"]')).toBeTruthy());
        expect(dash().querySelector('.dash-range-hint')).toBeNull();
        choose('7d');
        await waitFor(() => expect(dash().querySelector('input[aria-label="From"]')).toBeNull());
        expect(dash().querySelector('.dash-range-hint')).toBeNull();
    });

    it('does not remember custom dates either: Custom opens on the thirty days again', async () => {
        renderConsole({ anyWindow: true });
        await open();
        choose('custom');
        await waitFor(() => expect(dash().querySelector('input[aria-label="From"]')).toBeTruthy());
        const from = dash().querySelector<HTMLInputElement>('input[aria-label="From"]')!;
        from.value = '2026-09-01';
        fireEvent.change(from);
        await waitFor(() => expect(dashboard.custom.value.from).toBe('2026-09-01'));
        dashboard.watching(false);
        dashboard.watching(true);
        await waitFor(() => expect(chosen()).toBe('30d'));
        expect(dashboard.custom.value).toEqual({ from: '', to: '' });
        choose('custom');
        await waitFor(() =>
            expect(dash().querySelector<HTMLInputElement>('input[aria-label="From"]')!.value).toBe('2026-09-17'),
        );
        dashboard.watching(false);
    });

    it('does not remember the choice: the Dashboard opens on thirty days again', async () => {
        renderConsole({ anyWindow: true });
        await open();
        choose('6m');
        await waitFor(() => expect(chosen()).toBe('6m'));
        dashboard.watching(false);
        dashboard.watching(true);
        await waitFor(() => expect(chosen()).toBe('30d'));
        dashboard.watching(false);
    });

    it('leaves Needs you as it is now, whatever the period', async () => {
        renderConsole({ anyWindow: true });
        await open();
        await waitFor(() => expect(attention.approvals.value.length).toBeGreaterThan(0));
        const total = attention.approvals.value.length + attention.questions.value.length;
        choose('year');
        await waitFor(() => expect(part('Needs you', 'b')).toBe(String(total)));
    });
});

describe('the daily trends', () => {
    const trendIn = (label: string) => dash().querySelector<HTMLElement>(`[data-tile="${label}"] .dash-trend`);

    it('shows the last 24 hours against the 24 before them beside the four counts, with the tooltip', async () => {
        renderConsole({ trends: 'rising' });
        await open();
        await waitFor(() => expect(trendIn('Contacted')).toBeTruthy());
        await waitFor(() => expect(trendIn('Interested')).toBeTruthy());
        expect(words(trendIn('Contacted')!)).toBe('+4');
        expect(words(trendIn('Replied')!)).toBe('+2');
        expect(words(trendIn('Interested')!)).toBe('+1');
        expect(trendIn('Contacted')!.classList.contains('up')).toBe(true);
        expect(trendIn('Contacted')!.getAttribute('title')).toBe('Last 24 hours against the 24 before them.');
        // Meetings booked holds a dash here, so there is no number to put a trend beside; Needs you and Bounce rate have none.
        expect(trendIn('Meetings booked')).toBeNull();
        expect(trendIn('Needs you')).toBeNull();
        expect(trendIn('Bounce rate')).toBeNull();
    });

    it('shows a minus and red for a fall, and a neutral ±0 for none', async () => {
        renderConsole({ trends: 'falling' });
        await open();
        await waitFor(() => expect(trendIn('Contacted')).toBeTruthy());
        expect(words(trendIn('Contacted')!)).toBe('\u22124');
        expect(trendIn('Contacted')!.classList.contains('down')).toBe(true);
    });

    it('shows ±0 when both windows hold the same', async () => {
        renderConsole({ trends: 'level' });
        await open();
        await waitFor(() => expect(trendIn('Contacted')).toBeTruthy());
        expect(words(trendIn('Contacted')!)).toBe('±0');
        expect(trendIn('Contacted')!.classList.contains('flat')).toBe(true);
    });

    it('gives the reply rate no trend, and each category’s conversations one on the card', async () => {
        renderConsole({ trends: 'rising' });
        await open();
        await waitFor(() => expect(trendIn('Replied')).toBeTruthy());
        expect(trendIn('Replied')!.parentElement!.querySelector('.rel .dash-trend')).toBeNull();
        await waitFor(() => expect(dash().querySelector('.dash-categories .dash-trend')).toBeTruthy());
        const rows = [...dash().querySelectorAll('.dash-categories tbody tr')].map((each) => [
            words(each.querySelector('td')!),
            words(each.querySelectorAll('td')[1]!),
        ]);
        expect(rows).toEqual([
            ['Interested', '1 +1'],
            ['Wrong person', '1 +1'],
        ]);
    });

    it('asks the two adjacent windows from this second, whatever the period', async () => {
        const { fake } = renderConsole({ trends: 'rising' });
        await open();
        const windows = [
            { since: '2026-10-15T09:00:00.000Z', until: '2026-10-16T09:00:00.000Z' },
            { since: '2026-10-14T09:00:00.000Z', until: '2026-10-15T09:00:00.000Z' },
        ];
        const asked = (operation: string) =>
            fake.calls.filter((each) => each.operation === operation).map((each) => each.body);
        await waitFor(() => expect(asked('reply.count')).toEqual(expect.arrayContaining(windows)));
        expect(asked('campaign.activity')).toEqual(
            expect.arrayContaining(windows.map((window) => ({ exclude_archived_contacts: true, ...window }))),
        );
        const select = dash().querySelector<HTMLSelectElement>('select[data-change="dash.period"]')!;
        select.value = '3m';
        fireEvent.change(select);
        await waitFor(() =>
            expect(
                asked('reply.count').filter((each) => (each as { until: string }).until === windows[1]!.until).length,
            ).toBeGreaterThan(1),
        );
        expect(asked('reply.count').every((each) => 'since' in each)).toBe(true);
    });

    it('shows no trend for a number whose window could not be read, and the number itself stays', async () => {
        renderConsole({ trends: 'last-refused' });
        await open();
        await waitFor(() => expect(trendIn('Contacted')).toBeTruthy());
        expect(trendIn('Interested')).toBeNull();
        expect(part('Interested', 'b')).toBe('1');
    });

    it('is a switch, on, that hides the trends and tells the desktop when it is turned off', async () => {
        const { desktop } = renderConsole({ trends: 'rising' });
        await open();
        await waitFor(() => expect(trendIn('Contacted')).toBeTruthy());
        const box = dash().querySelector<HTMLInputElement>('input[data-change="dash.trends"]')!;
        expect(box.checked).toBe(true);
        expect(words(box.parentElement!)).toBe('Show daily trends');
        box.checked = false;
        fireEvent.change(box);
        await waitFor(() => expect(trendIn('Contacted')).toBeNull());
        expect(dash().querySelector('.dash-categories .dash-trend')).toBeNull();
        expect(desktop.calls.filter((each) => each.name === 'dailyTrends.set')).toEqual([
            { name: 'dailyTrends.set', args: [false] },
        ]);
        expect(dash().querySelector<HTMLInputElement>('input[data-change="dash.trends"]')!.checked).toBe(false);
    });

    it('opens off, and asks for no trend at all, when the desktop kept that the person turned them off', async () => {
        const { fake } = renderConsole();
        await open();
        await waitFor(() => expect(part('Contacted', 'b')).toBe('11'));
        expect(dash().querySelector<HTMLInputElement>('input[data-change="dash.trends"]')!.checked).toBe(false);
        expect(dash().querySelector('.dash-trend')).toBeNull();
        expect(fake.calls.filter((each) => each.operation === 'reply.count')).toHaveLength(1);
    });

    it('is on and unremembered in a browser tab whose seam has no such member', async () => {
        const { desktop } = renderConsole({ trends: 'rising' });
        delete (desktop as unknown as Record<string, unknown>)['dailyTrends'];
        dashboard.watching(false);
        dashboard.watching(true);
        await open();
        await waitFor(() => expect(trendIn('Contacted')).toBeTruthy());
        const box = dash().querySelector<HTMLInputElement>('input[data-change="dash.trends"]')!;
        expect(box.checked).toBe(true);
        box.checked = false;
        fireEvent.change(box);
        await waitFor(() => expect(trendIn('Contacted')).toBeNull());
        expect(desktop.calls.filter((each) => each.name === 'dailyTrends.set')).toEqual([]);
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

    it('says in the demo that the numbers are the sample’s', async () => {
        mode.value = 'demo';
        renderConsole();
        const panel = await open();
        const head = words(panel.querySelector('.ph')!);
        expect(head).toContain('The numbers here are the sample’s.');
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
        await waitFor(() => expect(document.querySelector('.chat-panel')).toBeTruthy());
    });

    it('shows exactly four approvals and two questions of a longer queue', async () => {
        renderConsole();
        await open();
        await waitFor(() => expect(attention.loaded.value).toBe(true));
        const approval = attention.approvals.value[0]!;
        const question = attention.questions.value[0]!;
        attention.approvals.value = Array.from({ length: 6 }, (_, at) => ({ ...approval, id: `apr_q${at}` }));
        attention.questions.value = Array.from({ length: 4 }, (_, at) => ({ ...question, id: `dec_q${at}` }));
        const ids = () =>
            [...dash().querySelectorAll('.dash-decide .dash-row')].map((each) => each.getAttribute('data-id'));
        await waitFor(() => expect(ids()).toHaveLength(6));
        expect(ids()).toEqual(['apr_q0', 'apr_q1', 'apr_q2', 'apr_q3', 'dec_q0', 'dec_q1']);
        expect(words(dash().querySelector('.dash-decide .hd .btn')!)).toBe('All 10 →');
    });

    it('draws an agent name, a question and an actor that hold markup as text', async () => {
        const { journal } = renderConsole();
        await open();
        await waitFor(() => expect(attention.loaded.value).toBe(true));
        const hostile = '<b>x</b> "q"';
        consoleCampaigns.campaigns.value = consoleCampaigns.campaigns.value.map((each) => ({
            ...each,
            name: hostile,
        }));
        attention.questions.value = attention.questions.value.map((each) => ({
            ...each,
            question: hostile,
            campaign_name: hostile,
        }));
        attention.approvals.value = attention.approvals.value.map((each) => ({ ...each, campaign_name: hostile }));
        flushNow(() => {
            journal.open();
            journal.send(
                journalLine({
                    id: 'jrn_500',
                    ts: '2026-11-11T10:00:00.000Z',
                    kind: 'campaign_started',
                    campaign_id: 'cmp_1',
                    actor: { type: 'human', id: hostile },
                }),
            );
        });
        await waitFor(() => expect(dash().querySelector('.dash-line')).toBeTruthy());
        const page = dash();
        expect(page.textContent).toContain(hostile);
        expect(words(page.querySelector('.dash-line .meta')!)).toContain(hostile);
        expect([...page.querySelectorAll('b')].some((each) => each.textContent === 'x')).toBe(false);
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
