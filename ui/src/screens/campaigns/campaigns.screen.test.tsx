import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { fixtures } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
import { App } from '../../app';
import { CampaignChat } from './campaign-chat';
import { stageWord } from '../people/standing';
import { href, parseRoute, route, type Route } from '../../router';

// The recordings were made at 09:00 UTC on 16 October; the page reads "now" from the clock, in UTC here.
const recordedAt = new Date('2026-10-16T09:00:00Z');

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(recordedAt);
});
afterEach(() => {
    vi.useRealTimers();
});

const table: Route = { screen: 'campaigns', tab: 'sequence', view: 'summary', period: '7d', filters: {} };

/** Opens the screen the way an address does: renderApp clears the address, and its late change event must agree with ours. */
function open(api: FakeApi, to: Route = table) {
    renderApp(api);
    location.hash = href(to);
    route.value = to;
}

const sent = (api: FakeApi, operation: string) =>
    api.calls.filter((c) => c.operation === operation).map((c) => c.body as Record<string, unknown>);

/** The recorded request without the actor, which the client adds to every write. */
function written(recorded: object): Record<string, unknown> {
    return Object.fromEntries(Object.entries(recorded).filter(([key]) => key !== 'actor'));
}

/** A row's cells as the page shows them, the name cell first. */
async function cells(name: string): Promise<string[]> {
    const row = (await screen.findByText(name)).closest('tr')!;
    return within(row)
        .getAllByRole('cell')
        .map((cell) => cell.textContent?.trim() ?? '');
}

describe('Campaigns', () => {
    it('Summary counts each campaign’s people now, with a dash where no sequence is linked', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all');
        open(api);

        expect(await cells('Spring')).toEqual(['Spring', '22', '3', '3', '2', '2', '6', '2', '6']);
        expect(await cells('Autumn')).toEqual(['Autumndraft', '2', '0', '0', '0', '1', '1', '—', '—']);
        expect((await cells('Summer referrals'))[0]).toBe('Summer referralspaused');
        const autumn = (await screen.findByText('Autumn')).closest('tr')!;
        expect(within(autumn).getAllByTitle('No sequence linked yet')).toHaveLength(2);
        expect(sent(api, 'campaign.list')).toEqual([fixtures['campaign.list/screen'].request]);
        expect(sent(api, 'campaign.summary')).toEqual([fixtures['campaign.summary/all'].request]);
    });

    it('Activity counts the period: switching to it, and to another period, each asks for its own', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all')
            .answer('campaign.activity', 'campaign.activity/last-7-days', 'campaign.activity/today');
        open(api);
        await cells('Spring');

        fireEvent.click(screen.getByRole('button', { name: 'Activity' }));

        await waitFor(async () => expect(await cells('Spring')).toEqual(['Spring', '2', '9', '4', '2', '2']));
        expect(sent(api, 'campaign.activity')).toEqual([fixtures['campaign.activity/last-7-days'].request]);
        expect(location.hash).toBe('#/campaigns?view=activity');

        fireEvent.click(screen.getByRole('button', { name: 'Today' }));

        await waitFor(async () => expect(await cells('Spring')).toEqual(['Spring', '0', '0', '0', '0', '0']));
        expect(sent(api, 'campaign.activity')[1]).toEqual(fixtures['campaign.activity/today'].request);
        expect(location.hash).toBe('#/campaigns?view=activity&period=today');
    });

    it('Find a campaign and Archived ask the list again, and the counts for what it shows, leaving the open campaign alone', async () => {
        const api = new FakeApi()
            .answer(
                'campaign.list',
                'campaign.list/screen',
                'campaign.list/screen-query',
                'campaign.list/screen',
                'campaign.list/include-archived',
                'campaign.list/screen',
                'campaign.list/screen-query',
            )
            .answer(
                'campaign.summary',
                'campaign.summary/all',
                'campaign.summary/some',
                'campaign.summary/some',
                'campaign.summary/all',
                'campaign.summary/with-archived',
                'campaign.summary/all',
                'campaign.summary/some',
            )
            .answer('campaign.get', 'campaign.get/details');
        open(api, { ...table, id: 'cmp_1', tab: 'settings' });
        expect(await screen.findByRole('link', { name: '3 waiting' })).toBeTruthy();
        await screen.findByRole('cell', { name: /^Autumn/ });
        const find = screen.getByRole('searchbox', { name: 'Find a campaign' });

        fireEvent.input(find, { target: { value: 'spr' } });
        await waitFor(() => expect(sent(api, 'campaign.list')).toHaveLength(2), { timeout: 2000 });
        expect(sent(api, 'campaign.list')[1]).toEqual(fixtures['campaign.list/screen-query'].request);
        await waitFor(() => expect(screen.queryByRole('cell', { name: /^Autumn/ })).toBeNull());
        expect(location.hash).toBe('#/campaigns/cmp_1/settings?q=spr');
        expect(screen.getByRole('link', { name: '3 waiting' })).toBeTruthy();

        fireEvent.input(find, { target: { value: '' } });
        await waitFor(() => expect(sent(api, 'campaign.list')).toHaveLength(3), { timeout: 2000 });
        fireEvent.click(screen.getByRole('checkbox', { name: 'Archived' }));

        expect(await screen.findByRole('cell', { name: /^Winter/ })).toBeTruthy();
        expect(sent(api, 'campaign.list').at(-1)).toEqual(fixtures['campaign.list/include-archived'].request);

        // Archived turned off while a search waits for the pause: the search keeps the box as it is when it runs.
        fireEvent.input(find, { target: { value: 'spr' } });
        fireEvent.click(screen.getByRole('checkbox', { name: 'Archived' }));
        await waitFor(() => expect(sent(api, 'campaign.list')).toHaveLength(6), { timeout: 2000 });
        expect(sent(api, 'campaign.list').slice(4)).toEqual([
            fixtures['campaign.list/screen'].request,
            fixtures['campaign.list/screen-query'].request,
        ]);
        expect(location.hash).toBe('#/campaigns/cmp_1/settings?q=spr');
        // The counts read: the table's with each listing, and the open campaign's once, when it opened.
        expect(sent(api, 'campaign.summary')).toEqual([
            fixtures['campaign.summary/all'].request,
            fixtures['campaign.summary/some'].request,
            fixtures['campaign.summary/some'].request,
            fixtures['campaign.summary/all'].request,
            fixtures['campaign.summary/with-archived'].request,
            fixtures['campaign.summary/all'].request,
            fixtures['campaign.summary/some'].request,
        ]);
    });

    it('a column says on hover what it counts', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all')
            .answer('campaign.activity', 'campaign.activity/last-7-days');
        open(api);
        await cells('Spring');
        expect(screen.getByRole('columnheader', { name: 'Waiting' }).getAttribute('title')).toBe(
            'An approval or a question about their work is waiting for you.',
        );

        fireEvent.click(screen.getByRole('button', { name: 'Activity' }));

        const replied = await screen.findByRole('columnheader', { name: 'Replied' });
        expect(replied.getAttribute('title')).toBe('Their first reply in this campaign came in the period.');
    });

    it('a number opens the campaign’s people under that count, a zero opens nothing, and the rest of the row opens the campaign', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/summer')
            .answer('campaign.get', 'campaign.get/summer')
            .answer('workitem.list', 'workitem.list/summer-planned', 'workitem.list/summer-running')
            .answer('approval.list', 'approval.list/summer-pending')
            .answer('decision.list', 'decision.list/summer-pending')
            .answer('journal.list', 'journal.list/summer');
        open(api);
        const spring = (await screen.findByText('Spring')).closest('tr')!;

        const waiting = within(spring)
            .getAllByRole('link')
            .find((link) => link.textContent === '3');
        expect(waiting?.getAttribute('href')).toBe('#/campaigns/cmp_1/people?filter=waiting');
        const summer = (await screen.findByText('Summer referrals')).closest('tr')!;
        expect(
            within(summer)
                .getAllByRole('link')
                .map((link) => link.textContent),
        ).toEqual(['Summer referrals', '2', '2']);

        fireEvent.click(within(summer).getAllByText('0')[0]!);

        expect(location.hash).toBe(within(summer).getByRole('link', { name: 'Summer referrals' }).getAttribute('href'));
        await screen.findByRole('heading', { level: 2, name: 'Summer referrals' });
    });

    it('past a hundred campaigns, the table lists a hundred at a time and counts each page with it', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen-many', 'campaign.list/screen-many-next')
            .answer('campaign.summary', 'campaign.summary/many', 'campaign.summary/many-next');
        open(api);

        expect(await screen.findByText('Campaign 100')).toBeTruthy();
        expect(sent(api, 'campaign.list')).toEqual([fixtures['campaign.list/screen-many'].request]);
        expect(sent(api, 'campaign.summary')).toEqual([fixtures['campaign.summary/many'].request]);

        fireEvent.click(await screen.findByRole('button', { name: /loading/ }));

        expect(await screen.findByText('Campaign 101')).toBeTruthy();
        expect(sent(api, 'campaign.list')[1]).toEqual(fixtures['campaign.list/screen-many-next'].request);
        expect(sent(api, 'campaign.summary')[1]).toEqual(fixtures['campaign.summary/many-next'].request);
        expect(screen.getAllByRole('row')).toHaveLength(102);
    });
});

describe('The campaign panel', () => {
    const spring: Route = { ...table, id: 'cmp_1', tab: 'settings' };

    function panelApi() {
        return new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/some')
            .answer('campaign.get', 'campaign.get/details');
    }

    it('says what the campaign is, counts its people as the table does, and shows its record under Settings', async () => {
        const api = panelApi();
        open(api, spring);

        const heading = await screen.findByRole('heading', { level: 2, name: 'Spring' });
        const header = heading.closest('.panel-header') as HTMLElement;
        expect(header.textContent).toContain('active');
        expect(header.textContent).toContain('22 people');
        expect(header.textContent).toContain('reply campaign 4711');
        await waitFor(() => expect(sent(api, 'campaign.summary')).toHaveLength(2));
        expect(sent(api, 'campaign.summary')[1]).toEqual(fixtures['campaign.summary/some'].request);
        const waiting = await screen.findByRole('link', { name: '3 waiting' });
        expect(waiting.getAttribute('href')).toBe('#/campaigns/cmp_1/people?filter=waiting');

        expect(screen.getByText('offer')).toBeTruthy();
        expect(screen.getByText('A two-week pilot of the reconciliation tool.')).toBeTruthy();
        expect(screen.getByText('Provider ids')).toBeTruthy();
        expect(screen.getByText('Raw campaign record')).toBeTruthy();
        expect(screen.getByRole('tab', { name: 'Settings' }).getAttribute('aria-selected')).toBe('true');
        expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual([
            'Sequence',
            'People',
            'Inbox',
            'Results',
            'Settings',
        ]);
        // The browser hands no way to open a chat, so there is no Chat tab.
        expect(screen.queryByRole('button', { name: 'Chat' })).toBeNull();
    });

    it('says a Campaigns list with nothing in it and how a campaign starts: the sentence alone in a browser, a New chat inside the desktop', async () => {
        const api = new FakeApi().answer('campaign.list', 'campaign.list/screen-empty');
        open(api);
        await screen.findByText(
            'No campaigns yet. A campaign is set up in a chat with Jason: say who you want to reach, and the agent sets it up with you.',
        );
        expect(screen.queryByRole('button', { name: 'New chat' })).toBeNull();
        expect(sent(api, 'campaign.list')).toEqual([fixtures['campaign.list/screen-empty'].request]);
        expect(sent(api, 'campaign.summary')).toEqual([]);

        const inside = new FakeApi().answer('campaign.list', 'campaign.list/screen-empty');
        const chat = vi.fn();
        const { rerender } = renderApp(inside);
        rerender(
            <CampaignChat.Provider value={chat}>
                <App api={inside} uiApiVersion="v1" />
            </CampaignChat.Provider>,
        );
        location.hash = href(table);
        route.value = table;
        fireEvent.click(await screen.findByRole('button', { name: 'New chat' }));
        expect(chat).toHaveBeenCalledTimes(1);
        expect(chat).toHaveBeenCalledWith();
    });

    it('inside the desktop has a Chat tab, which opens a chat about the campaign and moves nowhere', async () => {
        const api = panelApi();
        const chat = vi.fn();
        const { rerender } = renderApp(api);
        rerender(
            <CampaignChat.Provider value={chat}>
                <App api={api} uiApiVersion="v1" />
            </CampaignChat.Provider>,
        );
        location.hash = href(spring);
        route.value = spring;

        fireEvent.click(await screen.findByRole('button', { name: 'Chat' }));

        expect(chat).toHaveBeenCalledTimes(1);
        expect(chat).toHaveBeenCalledWith({ id: 'cmp_1', name: 'Spring' });
        expect(location.hash).toBe('#/campaigns/cmp_1/settings');
        expect(screen.getByRole('tab', { name: 'Settings' }).getAttribute('aria-selected')).toBe('true');
    });

    it('closes back to the table', async () => {
        open(panelApi(), spring);

        fireEvent.click(await screen.findByRole('button', { name: 'Close' }));

        expect(location.hash).toBe('#/campaigns');
        await waitFor(() => expect(screen.queryByRole('heading', { level: 2 })).toBeNull());
    });

    it('drops a campaign the runtime no longer knows, and says so', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            // The People page asks for its chips' numbers too, archived people left out.
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/everyone-without-archived')
            .answer('campaign.get', 'campaign.get/unknown')
            .answer('contact.list', 'contact.list/by-activity');
        open(api, { ...table, id: fixtures['campaign.get/unknown'].request.campaign_id, tab: 'sequence' });

        expect((await screen.findByRole('status')).textContent).toContain('no longer exists');
        await waitFor(() => expect(location.hash).toBe('#/campaigns'));

        // Said once: coming back to the screen later finds it gone.
        const people: Route = { screen: 'people', tab: 'timeline', filters: {} };
        location.hash = href(people);
        route.value = people;
        await screen.findByText('Mateo Ruiz');
        location.hash = href(table);
        route.value = table;
        await screen.findByRole('cell', { name: /^Spring/ });
        await waitFor(() => expect(screen.queryByRole('status')).toBeNull());
    });

    it('in Activity, its numbers are the period’s, and none of Summary’s show while they are on their way', async () => {
        const api = panelApi().answer('campaign.activity', 'campaign.activity/last-7-days', 'campaign.activity/one');
        open(api, spring);
        expect(await screen.findByRole('link', { name: '3 waiting' })).toBeTruthy();
        // The panel's own read waits; the table's, for every campaign it lists, is answered.
        const release = api.hold(
            'campaign.activity',
            (body) => (body as { campaign_ids: string[] }).campaign_ids.length === 1,
        );

        fireEvent.click(screen.getByRole('button', { name: 'Activity' }));

        await waitFor(() => expect(screen.queryByRole('link', { name: '3 waiting' })).toBeNull());
        const panel = screen.getByRole('heading', { level: 2, name: 'Spring' }).closest('.panel') as HTMLElement;
        // No numbers under the period's label until they are the period's.
        expect(within(panel).queryByText('Last 7 days')).toBeNull();
        release();
        const added = await screen.findByRole('link', { name: '2 added' });
        expect(added.getAttribute('href')).toBe('#/campaigns/cmp_1/people?view=activity&filter=added');
        expect(within(added.closest('.panel') as HTMLElement).getByText('Last 7 days')).toBeTruthy();
        expect(sent(api, 'campaign.activity')).toEqual(
            expect.arrayContaining([
                fixtures['campaign.activity/last-7-days'].request,
                fixtures['campaign.activity/one'].request,
            ]),
        );
    });
});

describe('A campaign’s people', () => {
    const people = (filter?: string, view: 'summary' | 'activity' = 'summary'): Route => ({
        ...table,
        view,
        id: 'cmp_1',
        tab: 'people',
        ...(filter ? { filter } : {}),
    });

    /**
     * The counts in the order the screen asks for them: the People tab reads the campaign's with its list before the
     * table's are asked for, and again with each list it reads after.
     */
    function peopleApi(...lists: Parameters<FakeApi['answer']>[1][]) {
        return new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/some', 'campaign.summary/all', 'campaign.summary/some')
            .answer(
                'campaign.activity',
                'campaign.activity/one',
                'campaign.activity/last-7-days',
                'campaign.activity/one',
            )
            .answer('campaign.get', 'campaign.get/details')
            .answer('campaign.list_contacts', ...lists);
    }

    /** The row of a person in the People tab, by their name. */
    async function row(name: string): Promise<HTMLElement> {
        return (await screen.findByText(name)).closest('.row') as HTMLElement;
    }

    it.each([
        {
            why: 'an approval waits',
            count: 'waiting',
            view: 'summary',
            list: 'summary-waiting',
            chip: /^Waiting\s*3$/,
            person: 'Ada Example',
            says: 'approve: Put one person into the campaign at the provider.',
        },
        {
            why: 'a question waits',
            count: 'waiting',
            view: 'summary',
            list: 'summary-waiting',
            chip: /^Waiting\s*3$/,
            person: 'Ben Example',
            says: 'answer: Follow up on LinkedIn?',
        },
        {
            why: 'the work failed',
            count: 'failed',
            view: 'summary',
            list: 'summary-failed',
            chip: /^Failed\s*3$/,
            person: 'Cleo Example',
            says: 'It did not work.',
        },
        {
            why: 'the work expired',
            count: 'failed',
            view: 'summary',
            list: 'summary-failed',
            chip: /^Failed\s*3$/,
            person: 'Eve Example',
            says: 'expired',
        },
        {
            why: 'they replied, and when',
            count: 'replied',
            view: 'activity',
            list: 'activity-replied',
            chip: /^Replied\s*2$/,
            person: 'Rae Example',
            says: 'replied by email',
        },
        {
            why: 'they replied and were removed since',
            count: 'replied',
            view: 'activity',
            list: 'activity-replied',
            chip: /^Replied\s*2$/,
            person: 'Oli Example',
            says: 'Removed',
        },
        {
            why: 'they were taken out of the campaign',
            count: 'removed',
            view: 'summary',
            list: 'summary-removed',
            chip: /^Removed\s*1$/,
            person: 'Oli Example',
            says: 'Removed',
        },
        {
            why: 'they were taken out in the period, and when',
            count: 'removed',
            view: 'activity',
            list: 'activity-removed',
            chip: /^Removed\s*1$/,
            person: 'Oli Example',
            says: 'removed',
        },
    ] as const)(
        'a count lists exactly its people, each saying why they are in it: $why',
        async ({ count, view, list, chip, person, says }) => {
            const recorded = `campaign.list_contacts/${list}` as const;
            const api = peopleApi(recorded);
            open(api, people(count, view));

            // A row of the previous case's list stays until this case's own read goes out.
            await waitFor(() => expect(sent(api, 'campaign.list_contacts')).toEqual([fixtures[recorded].request]));
            expect((await row(person)).textContent).toContain(says);
            expect((await screen.findByRole('button', { name: chip })).getAttribute('aria-pressed')).toBe('true');
            // The campaign's numbers are read once, with its list, beside the table's: no request goes out twice.
            await screen.findByRole('cell', { name: 'Spring' });
            const counted = sent(api, view === 'summary' ? 'campaign.summary' : 'campaign.activity');
            expect(counted).toHaveLength(2);
            expect(new Set(counted.map((body) => JSON.stringify(body))).size).toBe(2);
        },
    );

    it('a removed person’s badge and the Removed chip say the one word the stage map holds, the badge in outline', async () => {
        const api = peopleApi('campaign.list_contacts/summary-removed');
        open(api, people('removed', 'summary'));
        await waitFor(() => expect(sent(api, 'campaign.list_contacts')).toHaveLength(1));
        const word = stageWord('removed')!.label;

        const badge = (await row('Oli Example')).querySelector('.meta .badge')!;
        expect(badge.textContent).toBe(word);
        expect(badge.className).toBe('badge outline');
        // The chip is the same count, named from the same map.
        expect(await screen.findByRole('button', { name: new RegExp(`^${word}\\s*1$`), pressed: true })).toBeTruthy();
    });

    it("each row says where the person stands in this campaign, from the runtime's own standing", async () => {
        const api = peopleApi('campaign.list_contacts/everyone');
        open(api, people(undefined, 'summary'));
        await waitFor(() => expect(sent(api, 'campaign.list_contacts')).toHaveLength(1));
        const stage = async (name: string) =>
            Array.from((await row(name)).querySelectorAll('.stage')).map((s) => s.textContent);
        expect(await stage('Ada Example')).toEqual(['Waiting']);
        expect(await stage('Ben Example')).toEqual(['Waiting', 'Idle']);
        expect(await stage('Pia Example')).toEqual(['Waiting', 'Failed']);
    });

    it('in Activity, the tab is People Activity, and All lists everyone something happened to in the period', async () => {
        const api = peopleApi('campaign.list_contacts/activity-people');
        open(api, people(undefined, 'activity'));

        await waitFor(() =>
            expect(sent(api, 'campaign.list_contacts')).toEqual([
                fixtures['campaign.list_contacts/activity-people'].request,
            ]),
        );
        expect(screen.getByRole('tab', { name: 'People Activity' }).getAttribute('aria-selected')).toBe('true');
        expect((await screen.findByRole('button', { name: /^All\s*14$/ })).getAttribute('aria-pressed')).toBe('true');
        expect(await row('Quinn Example')).toBeTruthy();
        // Each row says the latest thing that happened, by the count it was.
        expect((await row('Oli Example')).textContent).toContain('replied by email');
        expect((await row('Kit Example')).textContent).toContain('opted out of email');
        // New, and nothing happened to him this week.
        expect(screen.queryByText('Hal Example')).toBeNull();
    });

    it('another campaign’s name opens it on the tab and count open now', async () => {
        const autumn = fixtures['campaign.get/unlinked'].response.id;
        open(peopleApi('campaign.list_contacts/summary-failed'), people('failed'));
        await row('Cleo Example');

        const name = await screen.findByRole('link', { name: 'Autumn' });

        expect(name.getAttribute('href')).toBe(`#/campaigns/${autumn}/people?filter=failed`);
    });

    it('the search lists the people it finds, and says why the runtime refuses one', async () => {
        const api = peopleApi(
            'campaign.list_contacts/everyone',
            'campaign.list_contacts/query',
            'campaign.list_contacts/query-too-long',
        );
        open(api, people());
        await row('Ben Example');
        const search = screen.getByRole('searchbox', { name: 'Search people on this campaign' });

        fireEvent.input(search, { target: { value: 'ada' } });

        await waitFor(() => expect(screen.queryByText('Ben Example')).toBeNull(), { timeout: 2000 });
        expect(await row('Ada Example')).toBeTruthy();
        expect(sent(api, 'campaign.list_contacts')[1]).toEqual(fixtures['campaign.list_contacts/query'].request);

        fireEvent.input(search, { target: { value: 'a'.repeat(201) } });

        expect((await screen.findByRole('alert', {}, { timeout: 2000 })).textContent).toContain(
            fixtures['campaign.list_contacts/query-too-long'].response.error.message,
        );
        expect(sent(api, 'campaign.list_contacts')[2]).toEqual(
            fixtures['campaign.list_contacts/query-too-long'].request,
        );
    });

    it('pages on with the cursor it was given, for the period the list was opened with', async () => {
        const crowded = fixtures['campaign.get/many'].response;
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen-many')
            .answer(
                'campaign.activity',
                'campaign.activity/many-one',
                'campaign.activity/many',
                'campaign.activity/many-one',
            )
            .answer('campaign.get', 'campaign.get/many')
            .answer(
                'campaign.list_contacts',
                'campaign.list_contacts/activity-many',
                'campaign.list_contacts/activity-many-next',
            );
        open(api, { ...table, view: 'activity', id: crowded.id, tab: 'people', filter: 'added' });
        const panel = (await screen.findByRole('heading', { level: 2, name: crowded.name })).closest('.panel')!;
        await row('Person 50 Example');

        // The page stays open past a minute: the next page is still the period the first was read for.
        vi.setSystemTime(new Date('2026-10-16T09:20:00Z'));
        fireEvent.click(within(panel as HTMLElement).getByRole('button', { name: /loading/ }));

        expect(await row('Person 51 Example')).toBeTruthy();
        expect(sent(api, 'campaign.list_contacts')).toEqual([
            fixtures['campaign.list_contacts/activity-many'].request,
            fixtures['campaign.list_contacts/activity-many-next'].request,
        ]);
    });

    it.each([
        { outcome: 'approving it takes the person out of Waiting', answered: 'approval.approve/from-campaign-row' },
        {
            outcome: 'an approval decided elsewhere says so, and the list shows it decided',
            answered: 'approval.approve/from-campaign-row-again',
        },
    ] as const)('a waiting row opens the approval in place: $outcome', async ({ answered }) => {
        const api = peopleApi('campaign.list_contacts/summary-waiting', 'campaign.list_contacts/summary-waiting-after')
            .answer(
                'campaign.summary',
                'campaign.summary/some',
                'campaign.summary/all',
                'campaign.summary/some-after',
                'campaign.summary/all-after',
            )
            .answer('approval.get', 'approval.get/for-campaign-row')
            .answer('approval.approve', answered);
        open(api, people('waiting'));
        expect(await screen.findByRole('button', { name: /^Waiting\s*3$/ })).toBeTruthy();

        fireEvent.click(await row('Ada Example'));
        fireEvent.click(await screen.findByRole('button', { name: 'Approve' }));

        await waitFor(() => expect(sent(api, 'approval.approve')).toEqual([written(fixtures[answered].request)]));
        const said = fixtures[answered].response;
        if ('error' in said) {
            expect((await screen.findByRole('alert')).textContent).toContain(said.error.message);
            fireEvent.click(screen.getByRole('button', { name: 'Back to the campaign' }));
        }
        expect(await screen.findByRole('button', { name: /^Waiting\s*2$/ })).toBeTruthy();
        await waitFor(() => expect(screen.queryByText('Ada Example')).toBeNull());
        expect(await row('Pia Example')).toBeTruthy();
        // The table's row says the same as the panel: one count, one number on the screen.
        await waitFor(() => {
            const spring = screen.getByRole('cell', { name: 'Spring' }).closest('tr') as HTMLElement;
            expect(within(spring).getAllByRole('cell')[2]?.textContent?.trim()).toBe('2');
        });
    });

    it('a question row opens the question in place, and Back returns to the campaign', async () => {
        const api = peopleApi('campaign.list_contacts/summary-waiting').answer(
            'decision.get',
            'decision.get/for-campaign-row',
        );
        open(api, people('waiting'));
        await waitFor(() => expect(sent(api, 'campaign.list_contacts')).toHaveLength(1));

        fireEvent.click(await row('Ben Example'));
        expect(await screen.findByRole('heading', { level: 2, name: 'Follow up on LinkedIn?' })).toBeTruthy();

        fireEvent.click(screen.getByRole('button', { name: 'Back to the campaign' }));
        expect(await screen.findByRole('heading', { level: 2, name: 'Spring' })).toBeTruthy();
    });

    it('anybody not waiting for you opens in the person panel in place, from this campaign, and Close returns to the list', async () => {
        const api = peopleApi('campaign.list_contacts/summary-failed')
            .answer('contact.get', 'contact.get/imported')
            .answer('workitem.list', 'workitem.list/by-contact-open-imported')
            .answer('journal.list', 'journal.list/by-contact-empty')
            .answer('suppression.list', 'suppression.list/grace-email-clear');
        open(api, people('failed'));
        await waitFor(() => expect(sent(api, 'campaign.list_contacts')).toHaveLength(1));
        const address = location.hash;

        fireEvent.click(await row('Cleo Example'));

        const person = await waitFor(() => {
            const shown = document.querySelector<HTMLElement>('.person-panel');
            expect(shown && within(shown).queryByRole('heading', { level: 2 })).toBeTruthy();
            return shown!;
        });
        expect(sent(api, 'contact.get')).toEqual([fixtures['contact.get/imported'].request]);
        expect(location.hash).toBe(address);
        // The panel keeps its tab itself: the address stays the campaign's.
        fireEvent.click(within(person).getByRole('tab', { name: 'Details' }));
        expect(location.hash).toBe(address);

        fireEvent.click(within(person).getByRole('button', { name: 'Close' }));
        expect(document.querySelector('.person-panel')).toBeNull();
        expect(await row('Cleo Example')).toBeTruthy();
        expect(screen.getByRole('heading', { level: 2, name: 'Spring' })).toBeTruthy();
    });

    /** The People tab under Failed with Cleo opened in the person panel, and what each test goes on to read. */
    async function cleoOpen(api: FakeApi): Promise<void> {
        api.answer('contact.get', 'contact.get/imported')
            .answer('workitem.list', 'workitem.list/by-contact-open-imported')
            .answer('journal.list', 'journal.list/by-contact-empty');
        open(api, people('failed'));
        fireEvent.click(await row('Cleo Example'));
        await waitFor(() => expect(document.querySelector('.person-panel .panel-header h2')).toBeTruthy());
    }

    it('a person opened in place is let go when another count of the campaign is clicked in the table', async () => {
        const api = peopleApi('campaign.list_contacts/summary-failed', 'campaign.list_contacts/summary-waiting');
        await cleoOpen(api);

        const spring = (await screen.findByRole('cell', { name: 'Spring' })).closest('tr')!;
        const waiting = within(spring)
            .getAllByRole('link')
            .find((link) => link.getAttribute('href') === '#/campaigns/cmp_1/people?filter=waiting')!;
        fireEvent.click(waiting);

        expect(await row('Ben Example')).toBeTruthy();
        expect(document.querySelector('.person-panel')).toBeNull();
    });

    it('a person opened in place is not shown again when the campaign is left and opened again', async () => {
        const autumn = fixtures['campaign.get/unlinked'].response.id;
        const api = peopleApi(
            'campaign.list_contacts/summary-failed',
            'campaign.list_contacts/unlinked',
            'campaign.list_contacts/summary-failed',
        )
            .answer('campaign.get', 'campaign.get/details', 'campaign.get/unlinked')
            .answer(
                'campaign.summary',
                'campaign.summary/some',
                'campaign.summary/all',
                'campaign.summary/unlinked',
                'campaign.summary/some',
            );
        await cleoOpen(api);

        const away = { ...table, id: autumn, tab: 'people' as const };
        location.hash = href(away);
        route.value = away;
        await screen.findByRole('heading', { level: 2, name: 'Autumn' });
        expect(document.querySelector('.person-panel')).toBeNull();

        const back = people('failed');
        location.hash = href(back);
        route.value = back;

        await screen.findByRole('heading', { level: 2, name: 'Spring' });
        expect(await row('Cleo Example')).toBeTruthy();
        expect(document.querySelector('.person-panel')).toBeNull();
    });

    it('a number opened after the world moved shows the list as it is now', async () => {
        // The table was read when Waiting was 3; the panel asks again and the runtime now says 2.
        const api = peopleApi('campaign.list_contacts/summary-waiting-after').answer(
            'campaign.summary',
            'campaign.summary/all',
            'campaign.summary/some-after',
        );
        open(api, table);
        const spring = (await screen.findByText('Spring')).closest('tr')!;
        const three = within(spring)
            .getAllByRole('link')
            .find((link) => link.textContent === '3' && link.getAttribute('href')?.includes('waiting'))!;

        fireEvent.click(three);
        route.value = people('waiting');

        expect(await screen.findByRole('button', { name: /^Waiting\s*2$/ })).toBeTruthy();
        expect(await row('Ben Example')).toBeTruthy();
        await waitFor(() => expect(screen.getAllByText(/Example$/).filter((e) => e.closest('.row'))).toHaveLength(2));
    });

    it('an unlinked campaign’s People tab has no Sequence or Out, as its table row has none', async () => {
        const autumn = fixtures['campaign.get/unlinked'].response.id;
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer(
                'campaign.summary',
                'campaign.summary/unlinked',
                'campaign.summary/all',
                'campaign.summary/unlinked',
            )
            .answer('campaign.get', 'campaign.get/unlinked')
            .answer('campaign.list_contacts', 'campaign.list_contacts/unlinked');
        open(api, { ...table, id: autumn, tab: 'people' });

        expect(await screen.findByRole('button', { name: /^New\s*1$/ })).toBeTruthy();
        expect(screen.queryByRole('button', { name: /^Sequence/ })).toBeNull();
        expect(screen.queryByRole('button', { name: /^Out/ })).toBeNull();
    });
});

describe('A campaign’s Sequence', () => {
    function timelineApi() {
        return new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/some')
            .answer('campaign.get', 'campaign.get/details')
            .answer('workitem.list', 'workitem.list/by-campaign-planned', 'workitem.list/by-campaign-running')
            .answer('approval.list', 'approval.list/by-campaign-pending')
            .answer('decision.list', 'decision.list/by-campaign-pending')
            .answer('journal.list', 'journal.list/by-campaign', 'journal.list/by-campaign-changes');
    }

    it('puts what waits for you and what runs above now, and the campaign’s journal below it', async () => {
        const api = timelineApi();
        open(api, { ...table, id: 'cmp_1', tab: 'sequence' });

        expect(await screen.findByText('Waiting for you')).toBeTruthy();
        expect(screen.getByText('Running')).toBeTruthy();
        expect(screen.getByText('Planned')).toBeTruthy();
        // The questions about the whole campaign wait here too, though they count for nobody.
        expect(screen.getAllByText('Follow up on LinkedIn?')).toHaveLength(3);
        expect(await screen.findByText(/Holding the sends over the holiday\./)).toBeTruthy();
        // The journal's lines are the last section, under What happened, below the now line.
        const happened = screen.getByRole('region', { name: 'What happened' });
        expect(within(happened).getByText(/Holding the sends over the holiday\./)).toBeTruthy();
        expect(within(happened).queryByText('Running')).toBeNull();
        expect(happened.parentElement?.lastElementChild).toBe(happened);
        // The runtime says what is planned and what runs, each in its own read: the page splits nothing itself.
        expect(sent(api, 'workitem.list')).toEqual([
            fixtures['workitem.list/by-campaign-planned'].request,
            fixtures['workitem.list/by-campaign-running'].request,
        ]);
        expect(sent(api, 'approval.list')).toEqual([fixtures['approval.list/by-campaign-pending'].request]);
        expect(sent(api, 'decision.list')).toEqual([fixtures['decision.list/by-campaign-pending'].request]);
        expect(sent(api, 'journal.list')).toEqual([fixtures['journal.list/by-campaign'].request]);
    });

    it('opens first, and is where an address naming a tab that is gone opens', async () => {
        const api = timelineApi();
        const gone = parseRoute('#/campaigns/cmp_1/timeline');
        open(api, gone);

        expect(await screen.findByText('Waiting for you')).toBeTruthy();
        expect(screen.getByRole('tab', { name: 'Sequence' }).getAttribute('aria-selected')).toBe('true');
        expect(screen.getAllByRole('tab')[0]?.textContent).toBe('Sequence');
    });

    it('Changes lists what was done to the campaign itself', async () => {
        const api = timelineApi();
        open(api, { ...table, id: 'cmp_1', tab: 'sequence' });
        // The timeline's first read goes out once the panel has painted; a chip clicked before it would be overridden.
        await waitFor(() => expect(sent(api, 'journal.list')).toHaveLength(1));

        fireEvent.click(screen.getByRole('button', { name: 'Changes' }));

        await waitFor(() =>
            expect(sent(api, 'journal.list').at(-1)).toEqual(fixtures['journal.list/by-campaign-changes'].request),
        );
        expect(await screen.findByText(/Holding the sends over the holiday\./)).toBeTruthy();
    });

    it('a waiting approval opens in place, and Back finds the sequence under the chip it left', async () => {
        const api = timelineApi().answer('approval.get', 'approval.get/for-campaign-row');
        open(api, { ...table, id: 'cmp_1', tab: 'sequence' });
        await waitFor(() => expect(sent(api, 'journal.list')).toHaveLength(1));
        fireEvent.click(screen.getByRole('button', { name: 'Changes' }));
        await waitFor(() => expect(sent(api, 'journal.list')).toHaveLength(2));

        const waiting = (await screen.findByText('Waiting for you')).parentElement as HTMLElement;
        fireEvent.click(within(waiting).getByRole('button', { name: /Ada Example/ }));

        expect(await screen.findByRole('button', { name: 'Approve' })).toBeTruthy();
        expect(sent(api, 'approval.get')).toEqual([fixtures['approval.get/for-campaign-row'].request]);

        fireEvent.click(screen.getByRole('button', { name: 'Back to the campaign' }));
        await waitFor(() => expect(sent(api, 'journal.list')).toHaveLength(3));
        expect(sent(api, 'journal.list').at(-1)).toEqual(fixtures['journal.list/by-campaign-changes'].request);
        expect(await screen.findByText(/Holding the sends over the holiday\./)).toBeTruthy();

        // Closed and opened again, the campaign starts on All.
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));
        await waitFor(() => expect(location.hash).toBe('#/campaigns'));
        const again: Route = { ...table, id: 'cmp_1', tab: 'sequence' };
        location.hash = href(again);
        route.value = again;
        await waitFor(() => expect(sent(api, 'journal.list')).toHaveLength(4));
        expect(sent(api, 'journal.list').at(-1)).toEqual(fixtures['journal.list/by-campaign'].request);
    });

    it('left for another screen and opened again by its address, the campaign starts on All', async () => {
        const api = timelineApi();
        const sequence: Route = { ...table, id: 'cmp_1', tab: 'sequence' };
        open(api, sequence);
        await waitFor(() => expect(sent(api, 'journal.list')).toHaveLength(1));
        fireEvent.click(screen.getByRole('button', { name: 'Changes' }));
        await waitFor(() => expect(sent(api, 'journal.list')).toHaveLength(2));
        expect(await screen.findByText(/Holding the sends over the holiday\./)).toBeTruthy();

        // Another screen takes the page, and the campaign's address brings it back: the campaign opens again.
        location.hash = href({ screen: 'chat' });
        route.value = { screen: 'chat' };
        await waitFor(() => expect(screen.queryByRole('button', { name: 'Changes' })).toBeNull());
        location.hash = href(sequence);
        route.value = sequence;

        await waitFor(() => expect(sent(api, 'journal.list')).toHaveLength(3));
        expect(sent(api, 'journal.list').at(-1)).toEqual(fixtures['journal.list/by-campaign'].request);
        expect(screen.getByRole('button', { name: 'All' }).getAttribute('aria-pressed')).toBe('true');
    });
});

describe('A campaign’s Inbox', () => {
    const pilot: Route = { ...table, id: fixtures['campaign.get/inbox'].request.campaign_id, tab: 'inbox' };

    function inboxApi() {
        return new FakeApi()
            .answer('campaign.list', 'campaign.list/inbox')
            .answer('campaign.summary', 'campaign.summary/inbox-all', 'campaign.summary/inbox-one')
            .answer('campaign.get', 'campaign.get/inbox')
            .answer('journal.list', 'journal.list/by-campaign-replies');
    }

    /** Each person's section, by the name it is headed with. */
    const person = (name: string) =>
        screen.getByRole('heading', { level: 3, name }).closest('.campaign-tabs-inbox-person') as HTMLElement;

    it('lists who replied in the order Jason observed it, each with their threads asked of the plugin their line names', async () => {
        const api = inboxApi().answer('contact.threads', 'contact.threads/inbox-paula', 'contact.threads/inbox-mateo');
        open(api, pilot);

        expect(await screen.findByText(/Re: Soluna's next quarter/)).toBeTruthy();
        expect(screen.getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual([
            'Paula Mendes',
            'Mateo Ruiz',
        ]);
        expect(within(person('Mateo Ruiz')).getByText(/Happy to connect\./)).toBeTruthy();
        // Paula is linked through an account no route reaches: the threads view says so in its own words, once.
        expect(within(person('Paula Mendes')).getByText(/no route reaches the reply account/)).toBeTruthy();
        expect(within(person('Paula Mendes')).queryByRole('button', { name: 'Try again' })).toBeNull();
        // Paula's line was written at 08:50 for a move the provider made at 07:35: the reply's moment is the provider's,
        // and the line keeps its place in the order Jason observed the replies.
        expect(within(person('Paula Mendes')).getByText(/^replied 07:35/)).toBeTruthy();
        expect(within(person('Paula Mendes')).queryByText(/08:50/)).toBeNull();
        expect(within(person('Mateo Ruiz')).getByText(/^replied 08:41/)).toBeTruthy();
        expect(screen.getByText(/in the order Jason observed them/)).toBeTruthy();
        // The newest line is tied to nobody: its channel and moment, and no read for it.
        const untied = document.querySelector('.campaign-tabs-inbox-untied') as HTMLElement;
        expect(untied.textContent).toMatch(/LinkedIn/);
        expect(untied.textContent).toMatch(/no person in Jason is tied to it/);
        expect(within(untied).getByText('08:55')).toBeTruthy();
        expect(screen.getByRole('tab', { name: 'Inbox' }).getAttribute('aria-selected')).toBe('true');

        expect(sent(api, 'journal.list')).toEqual([fixtures['journal.list/by-campaign-replies'].request]);
        expect(sent(api, 'route.resolve')).toEqual([]);
        // One read per person and plugin, newest line first, each with the plugin its line names.
        expect(sent(api, 'contact.threads')).toEqual([
            fixtures['contact.threads/inbox-paula'].request,
            fixtures['contact.threads/inbox-mateo'].request,
        ]);
        const lines = fixtures['journal.list/by-campaign-replies'].response.items;
        expect(sent(api, 'contact.threads').map((body) => body['plugin_id'])).toEqual(
            lines.filter((l) => l.contact_id !== null).map((l) => (l.new as { plugin: string }).plugin),
        );
    });

    it('reads again when the person refreshes it, lines and threads both', async () => {
        const api = inboxApi().answer('contact.threads', 'contact.threads/inbox-paula', 'contact.threads/inbox-mateo');
        open(api, pilot);
        expect(await screen.findByText(/Re: Soluna's next quarter/)).toBeTruthy();

        fireEvent.click(screen.getByRole('button', { name: 'Refresh the inbox' }));
        await waitFor(() => expect(sent(api, 'contact.threads')).toHaveLength(4));
        expect(sent(api, 'journal.list')).toHaveLength(2);
        expect(await screen.findByText(/Re: Soluna's next quarter/)).toBeTruthy();
    });

    it('a campaign no reply was observed in says so, and reads nothing else', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/inbox')
            .answer('campaign.summary', 'campaign.summary/inbox-all', 'campaign.summary/inbox-quiet')
            .answer('campaign.get', 'campaign.get/inbox-quiet')
            .answer('journal.list', 'journal.list/by-campaign-replies-none');
        open(api, { ...pilot, id: fixtures['campaign.get/inbox-quiet'].request.campaign_id });

        expect(await screen.findByText('No reply has been observed in this campaign yet.')).toBeTruthy();
        expect(sent(api, 'journal.list')).toEqual([fixtures['journal.list/by-campaign-replies-none'].request]);
        expect(sent(api, 'route.resolve')).toEqual([]);
        expect(sent(api, 'contact.threads')).toEqual([]);
    });

    it('asks again from an empty Inbox, which has its Refresh as every Inbox does', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/inbox')
            .answer('campaign.summary', 'campaign.summary/inbox-all', 'campaign.summary/inbox-quiet')
            .answer('campaign.get', 'campaign.get/inbox-quiet')
            .answer('journal.list', 'journal.list/by-campaign-replies-none');
        open(api, { ...pilot, id: fixtures['campaign.get/inbox-quiet'].request.campaign_id });
        expect(await screen.findByText('No reply has been observed in this campaign yet.')).toBeTruthy();

        fireEvent.click(screen.getByRole('button', { name: 'Refresh the inbox' }));

        await waitFor(() => expect(sent(api, 'journal.list')).toHaveLength(2));
        expect(sent(api, 'journal.list')[1]).toEqual(fixtures['journal.list/by-campaign-replies-none'].request);
    });

    it('a read that failed says why, does not claim to load, and can be asked again', async () => {
        const api = inboxApi().answer('contact.threads', 'contact.threads/inbox-paula', 'contact.threads/inbox-mateo');
        const reachable = api.unreachable('journal.list');
        open(api, pilot);

        expect((await screen.findByText(/Failed to fetch/)).closest('.notice')).toBeTruthy();
        expect(screen.queryByText('loading…')).toBeNull();

        reachable();
        fireEvent.click(screen.getByRole('button', { name: 'Refresh the inbox' }));
        expect(await screen.findByText(/Re: Soluna's next quarter/)).toBeTruthy();
        expect(screen.queryByText(/Failed to fetch/)).toBeNull();
    });

    it('is read again by the toolbar’s Refresh while it is open', async () => {
        const api = inboxApi().answer('contact.threads', 'contact.threads/inbox-paula', 'contact.threads/inbox-mateo');
        open(api, pilot);
        expect(await screen.findByText(/Re: Soluna's next quarter/)).toBeTruthy();

        fireEvent.click(screen.getByTitle('Refresh'));

        await waitFor(() => expect(sent(api, 'journal.list')).toHaveLength(2));
        await waitFor(() => expect(sent(api, 'contact.threads')).toHaveLength(4));
    });
});

describe('A campaign’s Results', () => {
    /** The figures as the page shows them: each tile's name and its number. */
    async function figures(): Promise<string[]> {
        const group = await screen.findByRole('group', { name: 'The campaign’s results' });
        await waitFor(() => expect(group.getAttribute('aria-busy')).toBe('false'));
        return within(group)
            .getAllByRole('figure')
            .map((tile) => tile.getAttribute('aria-label') ?? '');
    }

    it('shows four figures as the runtime counts them: its people now, and the period’s contacted, replied and opted out', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/some')
            .answer('campaign.get', 'campaign.get/details')
            .answer('campaign.activity', 'campaign.activity/one');
        open(api, { ...table, id: 'cmp_1', tab: 'results' });

        // Each figure says what it measures over: the people now, the other three in the period, so none reads as one measure.
        expect(await figures()).toEqual([
            'People now, 22',
            'Contacted in the last 7 days, 4',
            'Replied in the last 7 days, 2',
            'Opted out in the last 7 days, 2',
        ]);
        expect(screen.getByRole('tab', { name: 'Results' }).getAttribute('aria-selected')).toBe('true');
        expect(screen.getAllByText('in the last 7 days')).toHaveLength(3);
        expect(screen.getAllByText('now')).toHaveLength(1);
        // Two reads, each the runtime's answer: nothing on the page divides one figure by another.
        expect(sent(api, 'campaign.summary')).toContainEqual(fixtures['campaign.summary/some'].request);
        expect(sent(api, 'campaign.activity')).toEqual([fixtures['campaign.activity/one'].request]);
        expect(screen.queryByText(/%/)).toBeNull();
    });

    it('a campaign nothing has happened in yet shows its zeros, not blanks', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/summer')
            .answer('campaign.get', 'campaign.get/summer')
            .answer('campaign.activity', 'campaign.activity/summer');
        open(api, { ...table, id: fixtures['campaign.get/summer'].request.campaign_id, tab: 'results' });

        expect(await figures()).toEqual([
            'People now, 2',
            'Contacted in the last 7 days, 0',
            'Replied in the last 7 days, 0',
            'Opted out in the last 7 days, 0',
        ]);
        expect(sent(api, 'campaign.activity')).toEqual([fixtures['campaign.activity/summer'].request]);
    });

    function resultsApi() {
        return new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/some')
            .answer('campaign.get', 'campaign.get/details')
            .answer('campaign.activity', 'campaign.activity/one');
    }

    it('a read that failed says why, and is neither busy nor shows a figure', async () => {
        const api = resultsApi();
        api.unreachable('campaign.activity');
        open(api, { ...table, id: 'cmp_1', tab: 'results' });

        expect((await screen.findByText(/Failed to fetch/)).closest('.notice')).toBeTruthy();
        expect(document.querySelector('[aria-busy="true"]')).toBeNull();
        expect(screen.queryAllByRole('figure')).toEqual([]);
        expect(screen.queryByText('…')).toBeNull();
    });

    it('a failure is said over its own tab only: going to Sequence leaves it behind', async () => {
        const api = resultsApi()
            .answer('workitem.list', 'workitem.list/by-campaign-planned', 'workitem.list/by-campaign-running')
            .answer('approval.list', 'approval.list/by-campaign-pending')
            .answer('decision.list', 'decision.list/by-campaign-pending')
            .answer('journal.list', 'journal.list/by-campaign');
        api.unreachable('campaign.activity');
        open(api, { ...table, id: 'cmp_1', tab: 'results' });
        expect(await screen.findByText(/Failed to fetch/)).toBeTruthy();

        fireEvent.click(screen.getByRole('tab', { name: 'Sequence' }));

        expect(await screen.findByRole('region', { name: 'What happened' })).toBeTruthy();
        expect(screen.queryByText(/Failed to fetch/)).toBeNull();
    });

    it('a late failure of one campaign’s read is never said over another campaign', async () => {
        const api = resultsApi()
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/some', 'campaign.summary/summer')
            .answer('campaign.get', 'campaign.get/details', 'campaign.get/summer');
        // Spring's period counts fail, but only once Summer is open.
        const asked = api.call.bind(api);
        let refuse = () => {};
        const late = new Promise<void>((resolve) => (refuse = resolve));
        api.call = (async (operation: string, body?: object, kind?: 'read' | 'write') => {
            if (operation !== 'campaign.activity') return asked(operation, body, kind);
            await late;
            throw new TypeError('Failed to fetch');
        }) as typeof api.call;
        open(api, { ...table, id: 'cmp_1', tab: 'results' });
        await screen.findByRole('heading', { level: 2, name: 'Spring' });

        const summer = { ...table, id: fixtures['campaign.get/summer'].request.campaign_id, tab: 'settings' as const };
        location.hash = href(summer);
        route.value = summer;
        await screen.findByRole('heading', { level: 2, name: fixtures['campaign.get/summer'].response.name });
        refuse();
        await new Promise((resolve) => setTimeout(resolve, 0));

        expect(screen.queryByText(/Failed to fetch/)).toBeNull();
    });
});

describe('What the panel does', () => {
    const details: Route = { ...table, id: 'cmp_1', tab: 'settings' };

    /** Each write is followed by the campaign's numbers and then the table's, read again in that order. */
    function actionsApi(get: 'campaign.get/details' | 'campaign.get/archived' = 'campaign.get/details', writes = 0) {
        const panel = get === 'campaign.get/archived' ? 'campaign.summary/archived' : 'campaign.summary/some';
        const again = Array.from({ length: writes }, () => [panel, 'campaign.summary/all'] as const).flat();
        return new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all', panel, ...again)
            .answer('campaign.get', get);
    }

    const actions = () => document.querySelector('.panel > .panel-actions') as HTMLElement;
    /** The Settings tab's body: the campaign's record, and none of the moves its header offers. */
    const settings = () => document.querySelector('.facts')!.closest('.panel-body') as HTMLElement;
    const moves = ['Start', 'Pause', 'Resume'];
    /** The moves a part of the panel offers, by their names. */
    const offered = (part: HTMLElement) =>
        within(part)
            .queryAllByRole('button')
            .map((each) => each.textContent?.trim())
            .filter((name) => moves.includes(name ?? ''));

    /** Chooses a person in the open Add people dialog, searching for them first when the first page does not offer them. */
    async function choose(dialog: HTMLElement, name: string, search?: string) {
        fireEvent.click(within(dialog).getByRole('button', { name: /Find a person/ }));
        if (search)
            fireEvent.input(within(dialog).getByPlaceholderText('Find a person'), { target: { value: search } });
        fireEvent.click(await within(dialog).findByRole('option', { name: new RegExp(name) }, { timeout: 2000 }));
    }

    it('Pause says what pausing does, Escape leaves it, the reason is sent, and Resume releases the work again', async () => {
        const api = actionsApi()
            .answer('campaign.pause', 'campaign.pause/with-reason')
            .answer('campaign.start', 'campaign.start/after-pause')
            .answer('campaign.get', 'campaign.get/details', 'campaign.get/unlinked')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/some', 'campaign.summary/unlinked');
        open(api, details);
        await screen.findByRole('heading', { level: 2, name: 'Spring' });

        expect(offered(actions())).toEqual(['Pause']);
        expect(offered(settings())).toEqual([]);
        fireEvent.click(within(actions()).getByRole('button', { name: 'Pause' }));
        const left = await screen.findByRole('dialog', { name: 'Pause Spring' });
        await waitFor(() => expect(left.contains(document.activeElement)).toBe(true));
        fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

        fireEvent.click(within(actions()).getByRole('button', { name: 'Pause' }));
        const dialog = await screen.findByRole('dialog', { name: 'Pause Spring' });
        expect(dialog.textContent).toContain(
            "Jason stops releasing this campaign's work. Work already running finishes. Reply's sequence keeps sending until a role pauses it there. Your reason is recorded.",
        );
        fireEvent.input(within(dialog).getByRole('textbox', { name: 'Reason' }), {
            target: { value: 'Holding the sends over the holiday.' },
        });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Pause' }));

        await waitFor(() =>
            expect(sent(api, 'campaign.pause')).toEqual([written(fixtures['campaign.pause/with-reason'].request)]),
        );
        await waitFor(() =>
            expect((document.querySelector('.panel-header') as HTMLElement).textContent).toContain('paused'),
        );

        // The header offers the one move the new status allows, beside Add people; Settings offers none.
        await waitFor(() => expect(offered(actions())).toEqual(['Resume']));
        expect(within(actions()).getByRole('button', { name: 'Add people' })).toBeTruthy();
        expect(offered(settings())).toEqual([]);
        fireEvent.click(within(actions()).getByRole('button', { name: 'Resume' }));
        const start = await screen.findByRole('dialog', { name: 'Resume Spring' });
        expect(start.textContent).toContain("Jason releases this campaign's work again.");
        // Another campaign is opened while Resume is on its way: the answer, and what is read after it, leave it open.
        const release = api.hold('campaign.start');
        fireEvent.click(within(start).getByRole('button', { name: 'Resume' }));
        await waitFor(() =>
            expect(sent(api, 'campaign.start')).toEqual([written(fixtures['campaign.start/after-pause'].request)]),
        );
        const autumn: Route = { ...details, id: 'cmp_2' };
        location.hash = href(autumn);
        route.value = autumn;
        await screen.findByRole('heading', { level: 2, name: 'Autumn' });
        const before = sent(api, 'campaign.summary').length;
        release();
        await waitFor(() => expect(sent(api, 'campaign.summary')).toHaveLength(before + 2));
        expect(sent(api, 'campaign.summary').slice(before)).toEqual(
            expect.arrayContaining([
                fixtures['campaign.summary/all'].request,
                fixtures['campaign.summary/unlinked'].request,
            ]),
        );
        expect(screen.getByRole('heading', { level: 2, name: 'Autumn' })).toBeTruthy();
    });

    it('the header’s move is offered and done on a tab other than Settings, and the tab stays', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/some', 'campaign.summary/all')
            .answer('campaign.get', 'campaign.get/details')
            .answer('campaign.list_contacts', 'campaign.list_contacts/everyone')
            .answer('campaign.pause', 'campaign.pause/with-reason');
        const people: Route = { ...details, tab: 'people' };
        open(api, people);
        await screen.findByRole('heading', { level: 2, name: 'Spring' });
        await waitFor(() => expect(sent(api, 'campaign.list_contacts')).toHaveLength(1));

        expect(offered(actions())).toEqual(['Pause']);
        fireEvent.click(within(actions()).getByRole('button', { name: 'Pause' }));
        const dialog = await screen.findByRole('dialog', { name: 'Pause Spring' });
        fireEvent.input(within(dialog).getByRole('textbox', { name: 'Reason' }), {
            target: { value: 'Holding the sends over the holiday.' },
        });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Pause' }));

        await waitFor(() =>
            expect(sent(api, 'campaign.pause')).toEqual([written(fixtures['campaign.pause/with-reason'].request)]),
        );
        await waitFor(() => expect(offered(actions())).toEqual(['Resume']));
        expect(route.value).toEqual(people);
        expect(screen.getByRole('tab', { name: /^People/ }).getAttribute('aria-selected')).toBe('true');
        // The People tab is read again under the new status, as it was asked for.
        await waitFor(() => expect(sent(api, 'campaign.list_contacts')).toHaveLength(2));
    });

    it("a draft is started from the campaign's header, saying that its work is released for the first time", async () => {
        const autumn = fixtures['campaign.get/unlinked'].response;
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/unlinked')
            .answer('campaign.get', 'campaign.get/unlinked');
        open(api, { ...details, id: autumn.id });
        await screen.findByRole('heading', { level: 2, name: autumn.name });

        expect(offered(actions())).toEqual(['Start']);
        expect(offered(settings())).toEqual([]);
        fireEvent.click(within(actions()).getByRole('button', { name: 'Start' }));
        const start = await screen.findByRole('dialog', { name: `Start ${autumn.name}` });
        expect(start.textContent).toContain("Jason starts releasing this campaign's work.");
        fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
        expect(sent(api, 'campaign.start')).toEqual([]);
    });

    it('Add people adds the people chosen and says what happened to each', async () => {
        const api = actionsApi('campaign.get/details', 1)
            .answer(
                'contact.list',
                'contact.list/campaign-picker',
                'contact.list/campaign-picker-zoe',
                'contact.list/campaign-picker',
            )
            .answer('campaign.add_contacts', 'campaign.add_contacts/several');
        open(api, details);
        await screen.findByRole('heading', { level: 2, name: 'Spring' });

        fireEvent.click(within(actions()).getByRole('button', { name: 'Add people' }));
        const dialog = await screen.findByRole('dialog', { name: 'Add people to Spring' });
        await waitFor(() => expect(dialog.contains(document.activeElement)).toBe(true));
        await choose(dialog, 'Zoe Example', 'zoe');
        await choose(dialog, 'Hal Example');
        fireEvent.input(within(dialog).getByRole('textbox', { name: 'Reason' }), {
            target: { value: 'From the partner list.' },
        });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

        expect((await within(dialog).findByRole('status')).textContent).toBe('1 added · 1 already in it');
        expect(sent(api, 'campaign.add_contacts')).toEqual([
            written(fixtures['campaign.add_contacts/several'].request),
        ]);
    });

    it('an archived campaign can be neither paused nor started, and adding to it is refused', async () => {
        const winter = fixtures['campaign.get/archived'].response;
        const api = actionsApi('campaign.get/archived')
            .answer('contact.list', 'contact.list/campaign-picker')
            .answer('campaign.add_contacts', 'campaign.add_contacts/to-archived');
        open(api, { ...details, id: winter.id });
        await screen.findByRole('heading', { level: 2, name: winter.name });

        expect(screen.getByText('Raw campaign record')).toBeTruthy();
        // The header offers no move to an archived campaign, and Settings none either.
        expect(offered(actions())).toEqual([]);
        expect(offered(settings())).toEqual([]);
        fireEvent.click(within(actions()).getByRole('button', { name: 'Add people' }));
        const dialog = await screen.findByRole('dialog', { name: `Add people to ${winter.name}` });
        await choose(dialog, 'Hal Example');
        fireEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

        expect((await within(dialog).findByRole('alert')).textContent).toContain(
            fixtures['campaign.add_contacts/to-archived'].response.error.message,
        );
        expect(sent(api, 'campaign.add_contacts')).toEqual([
            written(fixtures['campaign.add_contacts/to-archived'].request),
        ]);
    });
});
