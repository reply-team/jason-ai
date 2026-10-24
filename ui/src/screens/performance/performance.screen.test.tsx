import { act, fireEvent, screen, waitFor, within } from '@testing-library/preact';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
import { href, route, type Route } from '../../router';

// The recordings were made at 09:00 UTC on 16 October; the page reads "now" from the clock, in UTC here.
beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-16T09:00:00Z'));
});
afterEach(() => {
    vi.useRealTimers();
});

const page: Route = { screen: 'performance', tab: 'timeline', period: '7d', campaigns: [] };

/** Goes to an address the way the browser does when one is opened. */
function go(to: Route) {
    location.hash = href(to);
    route.value = to;
}

/** Opens the screen the way an address does: renderApp clears the address, and its late change event must agree with ours. */
function open(api: FakeApi, to: Route = page) {
    renderApp(api);
    go(to);
}

/** The week across every campaign: its counts and the two series the page draws. */
function week(api: FakeApi): FakeApi {
    return api
        .answer('campaign.activity', 'campaign.activity/all-7-days')
        .answer(
            'campaign.activity_by_period',
            'campaign.activity_by_period/contacted-7-days',
            'campaign.activity_by_period/replied-7-days',
        );
}

const chart = (name: RegExp) => screen.findByRole('group', { name });
const bars = async (name: RegExp) => within(await chart(name)).getAllByRole('button');
/** A person's row in the list under the tiles, not their name in the panel beside it. */
async function rowOf(name: string): Promise<HTMLElement> {
    await waitFor(() => expect(document.querySelector('.listing')).toBeTruthy());
    return (await within(document.querySelector('.listing') as HTMLElement).findByText(name)).closest('a')!;
}

describe('Performance', () => {
    it('opens on the period’s numbers and the two series, with no list', async () => {
        const api = week(new FakeApi());
        const release = api.hold('campaign.activity_by_period');
        open(api);
        // The series come back a moment after the read: the bar running then is still the current one.
        await waitFor(() => expect(api.sent('campaign.activity_by_period')).toBeTruthy());
        vi.setSystemTime(new Date('2026-10-16T09:00:05Z'));
        release();

        for (const [label, n] of [
            ['People', 14],
            ['Added', 2],
            ['Worked on', 9],
            ['Contacted', 4],
            ['Replied', 2],
            ['Opted out', 2],
            ['Removed', 1],
        ] as const) {
            expect((await screen.findByRole('button', { name: `${label}, ${n}` })).getAttribute('aria-pressed')).toBe(
                'false',
            );
        }
        expect(screen.getByRole('button', { name: 'Replied, 2' }).textContent).toContain(
            '50.0% of the period’s contacted',
        );
        // Contacted: Ivy and Tess on the 12th, Uma on the 13th, Vic on the 14th; the last day is today.
        const contacted = await bars(/^Contacted per day/);
        expect(contacted.map((bar) => bar.getAttribute('aria-label'))).toEqual([
            'Sat 10 Oct, 0',
            'Sun 11 Oct, 0',
            'Mon 12 Oct, 2',
            'Tue 13 Oct, 1',
            'Wed 14 Oct, 1',
            'Thu 15 Oct, 0',
            'Fri 16 Oct, 0',
        ]);
        expect(contacted.at(-1)!.textContent).toContain('today');
        expect(await bars(/^Replied per day/)).toHaveLength(7);
        expect(screen.queryByText(/people in the last 7 days/)).toBeNull();
        const periods = within(screen.getByRole('group', { name: 'Period' })).getAllByRole('button');
        expect(periods.map((p) => [p.textContent, p.getAttribute('aria-pressed')])).toEqual([
            ['Today', 'false'],
            ['Last 7 days', 'true'],
            ['Last 30 days', 'false'],
            ['Last 90 days', 'false'],
        ]);
    });

    it('a tile opens its people under the tiles, by the day they were counted, and the address keeps it', async () => {
        const api = week(new FakeApi()).answer('campaign.list_contacts', 'campaign.list_contacts/activity-replied-all');
        open(api);
        await waitFor(() => expect(api.sent('campaign.activity')).toBeTruthy());
        // Two hours on, the list is read for the window its tile's number was read for.
        vi.setSystemTime(new Date('2026-10-16T11:00:00Z'));

        fireEvent.click(await screen.findByRole('button', { name: 'Replied, 2' }));

        expect(await screen.findByText('2 people in the last 7 days, by the day they replied')).toBeTruthy();
        const oli = await rowOf('Oli Example');
        expect(oli.textContent).toContain('Spring · replied by email');
        expect(oli.textContent).toContain('08:00');
        const groups = Array.from(document.querySelectorAll('.listing .group')).map((g) => g.textContent);
        expect(groups).toEqual(['Wednesday 14 October', 'Monday 12 October']);
        expect(await rowOf('Rae Example')).toBeTruthy();
        expect(screen.getByRole('button', { name: 'Replied, 2' }).getAttribute('aria-pressed')).toBe('true');
        expect(location.hash).toBe('#/performance?count=replied');
        expect(await bars(/^Contacted per day/)).toHaveLength(7);
    });

    it('another tile switches the list, and a list once open stays open', async () => {
        const api = week(new FakeApi()).answer(
            'campaign.list_contacts',
            'campaign.list_contacts/activity-replied-all',
            'campaign.list_contacts/activity-people-all',
        );
        open(api);
        fireEvent.click(await screen.findByRole('button', { name: 'Replied, 2' }));
        await screen.findByText('2 people in the last 7 days, by the day they replied');

        fireEvent.click(screen.getByRole('button', { name: 'People, 14' }));

        expect(
            await screen.findByText('14 people in the last 7 days, by the day something happened to them'),
        ).toBeTruthy();
        expect((await rowOf('Uma Example')).textContent).toContain('conversation.reply');
        expect(screen.getByRole('button', { name: 'Replied, 2' }).getAttribute('aria-pressed')).toBe('false');
        fireEvent.click(screen.getByRole('button', { name: 'People, 14' }));
        expect(screen.getByText('14 people in the last 7 days, by the day something happened to them')).toBeTruthy();
        expect(location.hash).toBe('#/performance?count=people');
    });

    it('a bar lists its own day’s people as the series on show cuts it, and the way back is the period', async () => {
        const api = week(new FakeApi()).answer(
            'campaign.list_contacts',
            'campaign.list_contacts/activity-replied-day',
            'campaign.list_contacts/activity-replied-today',
            'campaign.list_contacts/activity-replied-all',
        );
        open(api);

        const monday = (await bars(/^Replied per day/)).find(
            (bar) => bar.getAttribute('aria-label') === 'Mon 12 Oct, 1',
        )!;
        fireEvent.click(monday);

        expect(await screen.findByText('1 person on Mon 12 Oct, by the day they replied')).toBeTruthy();
        expect(await rowOf('Rae Example')).toBeTruthy();
        expect(screen.queryByText('Oli Example')).toBeNull();
        expect(monday.getAttribute('aria-pressed')).toBe('true');
        expect(location.hash).toBe(
            '#/performance?count=replied&from=2026-10-12T00%3A00%3A00.000Z&to=2026-10-13T00%3A00%3A00.000Z',
        );

        fireEvent.click(screen.getByRole('button', { name: 'Back to the whole period' }));

        expect(await screen.findByText('2 people in the last 7 days, by the day they replied')).toBeTruthy();
        expect(await rowOf('Oli Example')).toBeTruthy();
        expect(location.hash).toBe('#/performance?count=replied');

        // An address made at 08:00 names today's bar as it ended then; the series read at 09:00 ends it at 09:00, and the
        // address, the number and the list follow it.
        go({ ...page, count: 'replied', from: '2026-10-16T00:00:00.000Z', to: '2026-10-16T08:00:00.000Z' });
        expect(await screen.findByText('0 people on Fri 16 Oct, by the day they replied')).toBeTruthy();
        expect(await screen.findByText('Nobody in this count for the period.')).toBeTruthy();
        expect(location.hash).toBe(
            '#/performance?count=replied&from=2026-10-16T00%3A00%3A00.000Z&to=2026-10-16T09%3A00%3A00.000Z',
        );

        // A bar the period no longer holds is no bar: the page goes back to the period, its number and its list together.
        go({ ...page, count: 'replied', from: '2026-10-01T00:00:00.000Z', to: '2026-10-02T00:00:00.000Z' });
        await waitFor(() => expect(location.hash).toBe('#/performance?count=replied'));
        expect(await screen.findByText('2 people in the last 7 days, by the day they replied')).toBeTruthy();
        expect(await rowOf('Oli Example')).toBeTruthy();
        expect(screen.queryByRole('button', { name: 'Back to the whole period' })).toBeNull();
    });

    it('Today reads every number again, by the hour', async () => {
        const api = week(new FakeApi())
            .answer('campaign.activity', 'campaign.activity/all-7-days', 'campaign.activity/all-today')
            .answer(
                'campaign.activity_by_period',
                'campaign.activity_by_period/contacted-7-days',
                'campaign.activity_by_period/replied-7-days',
                'campaign.activity_by_period/contacted-today',
                'campaign.activity_by_period/replied-today',
            );
        open(api);
        await screen.findByRole('button', { name: 'Replied, 2' });
        const release = api.hold('campaign.activity', (body) =>
            (body as { since: string }).since.startsWith('2026-10-16'),
        );

        fireEvent.click(within(screen.getByRole('group', { name: 'Period' })).getByRole('button', { name: 'Today' }));

        // While today's numbers are on their way, the tiles keep their place without the week's numbers in them.
        const tiles = await screen.findByRole('group', { name: 'The period’s counts' });
        expect(tiles.getAttribute('aria-busy')).toBe('true');
        expect(screen.queryByRole('button', { name: 'Replied, 2' })).toBeNull();
        release();
        expect(await screen.findByRole('button', { name: 'Replied, 0' })).toBeTruthy();
        // From midnight to nine: eight hours and the one running now.
        expect(await bars(/^Contacted per hour/)).toHaveLength(9);
        expect(location.hash).toBe('#/performance?period=today');
    });

    it('a period with nothing in it says so in the charts and in the list', async () => {
        const api = new FakeApi()
            .answer('campaign.activity', 'campaign.activity/all-today')
            .answer(
                'campaign.activity_by_period',
                'campaign.activity_by_period/contacted-today',
                'campaign.activity_by_period/replied-today',
            )
            .answer('campaign.list_contacts', 'campaign.list_contacts/activity-replied-today');
        open(api, { ...page, period: 'today', count: 'replied' });

        expect(await screen.findByRole('button', { name: 'People, 0' })).toBeTruthy();
        expect(within(await chart(/^Contacted per hour/)).getByText('Nobody yet')).toBeTruthy();
        expect(within(await chart(/^Replied per hour/)).getByText('Nobody yet')).toBeTruthy();
        expect(await screen.findByText('Nobody in this count for the period.')).toBeTruthy();
    });

    it('the campaign filter narrows every number, rides in the address and shows each campaign as a chip', async () => {
        const api = new FakeApi()
            .answer(
                'campaign.activity',
                'campaign.activity/all-7-days',
                'campaign.activity/one',
                'campaign.activity/two',
            )
            .answer(
                'campaign.activity_by_period',
                'campaign.activity_by_period/contacted-7-days',
                'campaign.activity_by_period/replied-7-days',
                'campaign.activity_by_period/contacted-one',
                'campaign.activity_by_period/replied-one',
                'campaign.activity_by_period/contacted-two',
                'campaign.activity_by_period/replied-two',
            )
            .answer('campaign.list', 'campaign.list/campaigns-for-picker');
        open(api);
        await screen.findByRole('button', { name: 'Replied, 2' });

        fireEvent.click(screen.getByRole('button', { name: /^All campaigns/ }));
        // An archived campaign counts as any other, so it is offered too, saying it is archived.
        expect((await screen.findByRole('option', { name: /^Winter/ })).textContent).toContain('archived');
        fireEvent.click(await screen.findByRole('option', { name: /^Spring/ }));
        await waitFor(() => expect(location.hash).toBe('#/performance?campaign=cmp_1'));
        fireEvent.click(await screen.findByRole('option', { name: /^Autumn/ }));
        await waitFor(() => expect(location.hash).toBe('#/performance?campaign=cmp_1&campaign=cmp_2'));
        expect(screen.getByRole('option', { name: /^Autumn/ }).getAttribute('aria-selected')).toBe('true');
        fireEvent.keyDown(screen.getByRole('searchbox'), { key: 'Escape' });

        await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull());
        const chips = Array.from(document.querySelectorAll('.filters .chip')).map((chip) => chip.textContent);
        expect(chips).toEqual(['Spring×', 'Autumn draft×']);
        expect(screen.getByRole('button', { name: /^Campaigns/ })).toBeTruthy();
        expect(await screen.findByRole('button', { name: 'Replied, 2' })).toBeTruthy();

        const clear = screen.getByRole('button', { name: 'Clear' });
        clear.focus();
        fireEvent.click(clear);

        await waitFor(() => expect(location.hash).toBe('#/performance'));
        // Clear is gone with the chips, so the keyboard goes back to the picker.
        expect(document.activeElement).toBe(screen.getByRole('button', { name: /^All campaigns/ }));
        expect(document.querySelectorAll('.filters .chip')).toHaveLength(0);
    });

    it('a campaign the runtime no longer knows is left out of the address, and the page says so', async () => {
        const api = week(new FakeApi())
            .answer('campaign.get', 'campaign.get/gone-here', 'campaign.get/details')
            .answer('campaign.activity', 'campaign.activity/all-7-days', 'campaign.activity/one')
            .answer(
                'campaign.activity_by_period',
                'campaign.activity_by_period/contacted-7-days',
                'campaign.activity_by_period/replied-7-days',
                'campaign.activity_by_period/contacted-one',
                'campaign.activity_by_period/replied-one',
            );
        open(api, { ...page, campaigns: ['cmp_5', 'cmp_1'] });

        expect(await screen.findByText('A campaign in this address no longer exists; it was left out.')).toBeTruthy();
        await waitFor(() => expect(location.hash).toBe('#/performance?campaign=cmp_1'));
        expect(Array.from(document.querySelectorAll('.filters .chip')).map((chip) => chip.textContent)).toEqual([
            'Spring×',
        ]);
        expect(await screen.findByRole('button', { name: 'Replied, 2' })).toBeTruthy();

        // An address the person leaves before its campaigns are known stays left: the answer rewrites nothing.
        let asked = false;
        const release = api.hold('campaign.get', () => (asked = true));
        go({ ...page, campaigns: ['cmp_5', 'cmp_1'] });
        await waitFor(() => expect(asked).toBe(true));
        go(page);
        release();
        await new Promise((resolve) => setTimeout(resolve, 50));
        expect(location.hash).toBe('#/performance');
    });

    it('a read that fails says so where its numbers would be, and the rest still draws', async () => {
        const api = week(new FakeApi());
        api.unreachable('campaign.activity');
        api.unreachable('campaign.list_contacts');
        open(api, { ...page, count: 'replied' });

        await waitFor(() => expect(screen.getAllByRole('alert')).toHaveLength(2));
        expect(await bars(/^Contacted per day/)).toHaveLength(7);
        expect(screen.queryByRole('button', { name: /^Replied, / })).toBeNull();
        // A list that could not be read is not an empty one.
        expect(screen.queryByText('Nobody in this count for the period.')).toBeNull();
    });

    it('a row opens the person beside the list, and the panel’s tabs and its close stay on Performance', async () => {
        const api = week(new FakeApi())
            .answer('campaign.list_contacts', 'campaign.list_contacts/activity-replied-all')
            .answer('contact.get', 'contact.get/oli')
            .answer('workitem.list', 'workitem.list/oli-open')
            .answer('journal.list', 'journal.list/oli');
        open(api, { ...page, count: 'replied' });

        fireEvent.click(await rowOf('Oli Example'));

        expect(await screen.findByRole('heading', { name: /^Oli Example/ })).toBeTruthy();
        expect(location.hash).toBe('#/performance/cnt_6?count=replied');
        expect((await rowOf('Oli Example')).className).toContain('selected');
        expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual(['Timeline', 'Details']);
        await waitFor(() => expect(document.querySelector('.panel .timeline .rows .row')).toBeTruthy());

        fireEvent.click(screen.getByRole('tab', { name: 'Details' }));
        await waitFor(() => expect(location.hash).toBe('#/performance/cnt_6/details?count=replied'));

        fireEvent.click(screen.getByRole('button', { name: 'Close' }));
        await waitFor(() => expect(location.hash).toBe('#/performance?count=replied'));
        expect(screen.queryByRole('heading', { name: /^Oli Example/ })).toBeNull();
        expect(await rowOf('Rae Example')).toBeTruthy();
    });

    it('a person linked nowhere, addressed on Threads, is shown on their timeline', async () => {
        const api = week(new FakeApi())
            .answer('campaign.list_contacts', 'campaign.list_contacts/activity-replied-all')
            .answer('contact.get', 'contact.get/oli')
            .answer('workitem.list', 'workitem.list/oli-open')
            .answer('journal.list', 'journal.list/oli');
        open(api, { ...page, count: 'replied', id: 'cnt_6', tab: 'threads' });

        expect(await screen.findByRole('heading', { name: /^Oli Example/ })).toBeTruthy();
        expect(screen.getByRole('tab', { name: 'Timeline' }).getAttribute('aria-selected')).toBe('true');
        await waitFor(() => expect(document.querySelector('.panel .timeline .rows .row')).toBeTruthy());
    });

    it('the refresh button is held until the numbers are read again, however long that takes', async () => {
        const api = week(new FakeApi());
        open(api);
        await screen.findByRole('button', { name: 'Replied, 2' });
        const refresh = screen.getByTitle('Refresh') as HTMLButtonElement;
        // Timers are faked too: the button turns for at least half a second, and this read takes longer.
        vi.useFakeTimers({ toFake: ['Date', 'setTimeout'] });
        vi.setSystemTime(new Date('2026-10-16T09:00:00Z'));
        const release = api.hold('campaign.activity');
        await act(async () => {
            fireEvent.click(refresh);
            await vi.advanceTimersByTimeAsync(600);
        });
        expect(refresh.disabled).toBe(true);

        await act(async () => {
            release();
            await vi.advanceTimersByTimeAsync(0);
        });
        expect(refresh.disabled).toBe(false);
    });
});
