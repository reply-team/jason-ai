import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { fixtures, type FixtureName } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
import { App } from '../../app';
import { CampaignChat } from '../campaigns/campaign-chat';
import { href, route, type PeopleFilters, type PersonTab } from '../../router';

// The recordings were made at 09:00 UTC on 16 October; the page reads "now" from the clock.
const recordedAt = new Date('2026-10-16T09:00:00Z');

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(recordedAt);
});
afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
});

/**
 * Opens the screen the way an address does: renderApp clears the address, and its late change event must agree with ours.
 * Beside a list with no search the page asks for its chips' numbers: archived people left out until Archived is ticked.
 */
function open(api: FakeApi, filters: PeopleFilters = {}, id?: string, tab: PersonTab = 'timeline') {
    if (!api.offers('campaign.summary')) {
        api.answer('campaign.summary', 'campaign.summary/everyone-without-archived', 'campaign.summary/everyone');
    }
    renderApp(api);
    const to = id ? { screen: 'people' as const, id, tab, filters } : { screen: 'people' as const, tab, filters };
    location.hash = href(to);
    route.value = to;
}

const sentLists = (api: FakeApi) =>
    api.calls.filter((c) => c.operation === 'contact.list').map((c) => c.body as Record<string, unknown>);

const mateo = fixtures['contact.get/with-campaigns'].response;
/** Details reads Jason's register for each address it shows; none of these is on it. */
const registerClear: FixtureName[] = [
    'suppression.list/mateo-email-clear',
    'suppression.list/mateo-linkedin-clear',
    'suppression.list/mateo-phone-clear',
    'suppression.list/aguilar-email-clear',
];

/** What a person's page reads besides their record: their open work and their journal under All. */
function timeline(
    api: FakeApi,
    journal: FixtureName = 'journal.list/by-contact',
    work: FixtureName = 'workitem.list/by-contact-open',
) {
    return api.answer('workitem.list', work).answer('journal.list', journal);
}

/** The row of the people table that names this person, and its cells' text. */
const rowOf = (name: string) =>
    Array.from(document.querySelectorAll<HTMLElement>('.ptable tbody tr')).find(
        (tr) => tr.querySelector('td.name')?.textContent === name,
    )!;
const cells = (row: HTMLElement) => Array.from(row.querySelectorAll('td')).map((td) => td.textContent);

/**
 * Presses Refresh once it can be pressed. The list's state outlives a row, and a refresh an earlier row started keeps the
 * button turning, and pressed in vain, for its half second.
 */
async function pressRefresh() {
    const button = screen.getByTitle<HTMLButtonElement>('Refresh');
    await waitFor(() => expect(button.disabled).toBe(false));
    fireEvent.click(button);
}

/** The timeline's reply rows, once the person's own lines have come: lines an earlier row read for them show until then. */
async function replyRows(count: number) {
    return waitFor(() => {
        const rows = screen.getAllByRole('button', { name: /show the thread/ });
        expect(rows).toHaveLength(count);
        return rows;
    });
}

const rowTitles = () =>
    Array.from(document.querySelectorAll('.panel .rows .row .name')).map((n) => n.textContent?.trim());

describe('People', () => {
    it('says a People list with nothing in it and how people come in: the sentence alone in a browser, a New chat inside the desktop', async () => {
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity-empty')
            .answer('campaign.summary', 'campaign.summary/everyone-without-archived-empty');
        open(api);
        await screen.findByText(
            'Nobody yet. People come in when a campaign finds or imports them: start with a campaign, set up in a chat with Jason.',
        );
        expect(screen.queryByRole('button', { name: 'New chat' })).toBeNull();
        expect(sentLists(api)).toEqual([fixtures['contact.list/by-activity-empty'].request]);

        const inside = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity-empty')
            .answer('campaign.summary', 'campaign.summary/everyone-without-archived-empty');
        const chat = vi.fn();
        const { rerender } = renderApp(inside);
        rerender(
            <CampaignChat.Provider value={chat}>
                <App api={inside} uiApiVersion="v1" />
            </CampaignChat.Provider>,
        );
        location.hash = href({ screen: 'people', tab: 'timeline', filters: {} });
        route.value = { screen: 'people', tab: 'timeline', filters: {} };
        fireEvent.click(await screen.findByRole('button', { name: 'New chat' }));
        expect(chat).toHaveBeenCalledTimes(1);
        expect(chat).toHaveBeenCalledWith();
    });

    it('lists people in a table, one row each, newest activity first as the runtime ordered them', async () => {
        const api = new FakeApi().answer('contact.list', 'contact.list/by-activity-several');
        open(api);

        await waitFor(() => expect(screen.getByText('Ada Example')).toBeTruthy());
        expect(sentLists(api)[0]).toEqual({ order: 'activity', limit: 200 });
        expect(Array.from(document.querySelectorAll('.ptable thead th')).map((th) => th.textContent)).toEqual([
            'Name',
            'Company',
            'Channels',
            'Stage',
            'Campaign',
            'Last activity',
        ]);
        const names = Array.from(document.querySelectorAll('.ptable td.name')).map((td) => td.textContent);
        expect(names).toHaveLength(24);
        expect(names.slice(0, 3)).toEqual(['Ada Example', 'Cleo Example', 'Ivy Example']);

        // Stage, then the campaign it is read in, then the latest line with its moment.
        expect(cells(rowOf('Ada Example'))).toEqual([
            'Ada Example',
            'Ada Labs',
            'Email',
            'Waiting',
            'Spring+2',
            'enrollment waiting for you 15 Oct, 16:00',
        ]);
        expect(rowOf('Ada Example').querySelector('.also')!.getAttribute('title')).toBe(
            'Also on: Autumn, Summer referrals',
        );
        expect(rowOf('Ada Example').querySelector('.stage')!.getAttribute('title')).toBe(
            'An approval or a question about their work is waiting for you.',
        );
        // A person in two counts of one campaign shows both; an excluded membership stands in Removed alone.
        expect(cells(rowOf('Ben Example'))[3]).toBe('WaitingIdle');
        expect(cells(rowOf('Oli Example'))[3]).toBe('Removed');
        // A person in no campaign, and one nothing has happened to, show no stage, no campaign and no moment.
        expect(cells(rowOf('Zoe Example')).slice(3)).toEqual(['—', '—', '—']);
    });

    it('a list that fails to load keeps the rows of the same list, and drops the rows of a search the person has left', async () => {
        const api = new FakeApi().answer('contact.list', 'contact.list/by-activity', 'contact.list/query-too-long');
        open(api);
        await waitFor(() => expect(screen.getByText('Mateo Ruiz')).toBeTruthy());

        // The runtime does not answer a refresh: what the person was reading stays, under the failure.
        const reachable = api.unreachable('contact.list');
        await pressRefresh();
        expect((await screen.findByRole('alert')).textContent).toContain('Failed to fetch');
        expect(screen.getByText('Mateo Ruiz')).toBeTruthy();
        reachable();

        // A search the runtime refuses: the rows belong to the list before it, and go.
        fireEvent.input(screen.getByRole('searchbox', { name: 'Search people' }), {
            target: { value: 'a'.repeat(201) },
        });
        await waitFor(() =>
            expect(screen.getByRole('alert').textContent).toBe(
                fixtures['contact.list/query-too-long'].response.error.message,
            ),
        );
        expect(screen.queryByText('Mateo Ruiz')).toBeNull();
    });

    it('the list reads on from where a page ended', async () => {
        const api = new FakeApi().answer('contact.list', 'contact.list/many', 'contact.list/many-next');
        open(api);

        await waitFor(() => expect(document.querySelectorAll('.ptable tbody tr')).toHaveLength(200));
        fireEvent.click(screen.getByRole('button', { name: /loading/ }));
        await waitFor(() => expect(screen.getByText('Extra 150 Example')).toBeTruthy());
        // The page and the one past it are one list: every person once, in the order the runtime gave.
        const names = Array.from(document.querySelectorAll('.ptable td.name')).map((td) => td.textContent);
        expect(names).toHaveLength(201);
        expect(new Set(names).size).toBe(201);
        expect(names.at(-1)).toBe('Extra 150 Example');
        expect(sentLists(api)[1]).toEqual(fixtures['contact.list/many-next'].request);
    });

    it('search, campaign and archived start from the first page and live in the address', async () => {
        const api = new FakeApi()
            .answer(
                'contact.list',
                'contact.list/by-activity',
                'contact.list/by-activity-query',
                'contact.list/by-campaign-query',
                'contact.list/by-campaign-query-archived',
                'contact.list/by-campaign-query-none',
            )
            .answer('campaign.list', 'campaign.list/for-picker');
        open(api);
        await waitFor(() => expect(screen.getByText('Mateo Ruiz')).toBeTruthy());

        fireEvent.input(screen.getByRole('searchbox', { name: 'Search people' }), { target: { value: 'mat' } });
        await waitFor(() => expect(sentLists(api)).toHaveLength(2));
        expect(sentLists(api)[1]).toEqual({ order: 'activity', limit: 200, query: 'mat' });
        expect(route.value).toMatchObject({ screen: 'people', filters: { q: 'mat' } });

        fireEvent.click(screen.getByRole('button', { name: /All campaigns/ }));
        const latAm = await screen.findByRole('option', { name: /LatAm pilot/ });
        // A campaign that is not active says so beside its name, as in every campaign picker.
        expect(screen.getByRole('option', { name: /Re-engage Q2 no-replies/ }).textContent).toContain('paused');
        expect(latAm.textContent).not.toContain('active');
        fireEvent.click(latAm);
        await waitFor(() => expect(sentLists(api)).toHaveLength(3));
        const campaignId = fixtures['campaign.list/for-picker'].response.items.find(
            (c) => c.name === 'LatAm pilot',
        )!.id;
        expect(sentLists(api)[2]).toEqual({ order: 'activity', limit: 200, query: 'mat', campaign_id: campaignId });

        fireEvent.click(screen.getByRole('checkbox', { name: 'Archived' }));
        await waitFor(() => expect(sentLists(api)).toHaveLength(4));
        expect(sentLists(api)[3]).toMatchObject({ include_archived: true, campaign_id: campaignId, query: 'mat' });
        expect(route.value).toMatchObject({ filters: { q: 'mat', campaign: campaignId, archived: true } });

        fireEvent.input(screen.getByRole('searchbox', { name: 'Search people' }), { target: { value: 'zz' } });
        await waitFor(() => expect(screen.getByText('Nobody matches.')).toBeTruthy());
    });

    it('a row opens the person, keeping the list and its filters; a filter changed or another row keeps the tab', async () => {
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity-query', 'contact.list/by-activity-query-aguilar')
                .answer('contact.get', 'contact.get/with-campaigns', 'contact.get/no-first-name')
                .answer('suppression.list', ...registerClear),
        );
        open(api, { q: 'mat' });
        await waitFor(() => expect(screen.getByText('Mateo Ruiz')).toBeTruthy());

        fireEvent.click(rowOf('Mateo Ruiz'));

        expect(route.value).toEqual({ screen: 'people', id: mateo.id, tab: 'timeline', filters: { q: 'mat' } });
        await waitFor(() => expect(rowOf('Mateo Ruiz').className).toContain('sel'));
        expect(rowOf('Mateo Ruiz').getAttribute('aria-current')).toBe('true');

        const details = { screen: 'people' as const, id: mateo.id, tab: 'details' as const, filters: { q: 'mat' } };
        location.hash = href(details);
        route.value = details;
        fireEvent.input(screen.getByRole('searchbox', { name: 'Search people' }), { target: { value: 'aguilar' } });
        await waitFor(() => expect(route.value).toMatchObject({ filters: { q: 'aguilar' } }));
        expect(route.value).toEqual({ screen: 'people', id: mateo.id, tab: 'details', filters: { q: 'aguilar' } });

        const aguilar = fixtures['contact.list/by-activity-query-aguilar'].response.items[0]!;
        fireEvent.click(await waitFor(() => rowOf('Aguilar') ?? Promise.reject(new Error('no row'))));
        expect(route.value).toEqual({ screen: 'people', id: aguilar.id, tab: 'details', filters: { q: 'aguilar' } });
    });
    it("a person's card shows where they are, their local time and their campaigns, active first, excluded outlined; it closes as Work items' panel does", async () => {
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity')
                .answer('contact.get', 'contact.get/with-campaigns'),
        );
        open(api, {}, mateo.id);

        const card = await screen.findByRole('heading', { level: 2, name: 'Mateo Ruiz' });
        const header = card.closest('.panel-header') as HTMLElement;
        expect(within(header).getByText('Head of Sales at Soluna · Mexico City, 03:00 there')).toBeTruthy();
        expect(within(header).getByText('LatAm pilot · since 3 Oct').className).toContain('accent');
        expect(within(header).getByText('Re-engage Q2 no-replies · excluded 1 Oct').className).toContain('outline');
        expect(Array.from(header.querySelectorAll('.onc .badge')).map((b) => b.textContent)).toEqual([
            'LatAm pilot · since 3 Oct',
            'Re-engage Q2 no-replies · excluded 1 Oct',
        ]);
        await waitFor(() => expect(api.sent('contact.get')).toEqual({ contact_id: mateo.id }));
        const actions = document.querySelector('.panel > .panel-actions') as HTMLElement;
        expect(within(actions).getByRole('button', { name: 'Add to campaign' })).toBeTruthy();

        fireEvent.click(within(header).getByRole('button', { name: 'Close' }));
        expect(route.value).toEqual({ screen: 'people', tab: 'timeline', filters: {} });
    });

    it('the person with no first name: the header says so, and nothing is made up', async () => {
        const aguilar = fixtures['contact.get/no-first-name'].response;
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity')
                .answer('contact.get', 'contact.get/no-first-name'),
            'journal.list/by-contact-failed',
            'workitem.list/by-contact-open-none',
        );
        open(api, {}, aguilar.id);

        const card = await screen.findByRole('heading', { level: 2, name: /Aguilar/ });
        await waitFor(() => expect(api.sent('journal.list')).toBeTruthy());
        expect(card.textContent).toBe('Aguilar · no first name');
        expect(document.querySelector('.panel .notice')).toBeNull();
    });

    it('adds the person to a campaign and closes; says in the dialog when it re-enrols, already has them, or is refused; leaves with the person', async () => {
        const forPicker = fixtures['campaign.list/for-picker'].response.items;
        const latAm = forPicker.find((c) => c.name === 'LatAm pilot')!;
        const reEngage = forPicker.find((c) => c.name === 'Re-engage Q2 no-replies')!;
        const api = timeline(new FakeApi())
            .answer('contact.list', 'contact.list/by-activity')
            .answer('contact.get', 'contact.get/with-campaigns')
            .answer('campaign.list', 'campaign.list/for-picker')
            .answer(
                'campaign.add_contacts',
                'campaign.add_contacts/re-enrol-excluded',
                'campaign.add_contacts/already-member',
                'campaign.add_contacts/archived-campaign',
            );
        open(api, {}, mateo.id);
        await screen.findByRole('heading', { level: 2, name: 'Mateo Ruiz' });

        fireEvent.click(screen.getByRole('button', { name: 'Add to campaign' }));
        const dialog = await screen.findByRole('dialog', { name: 'Add to campaign' });
        fireEvent.click(within(dialog).getByRole('button', { name: /Choose a campaign/ }));
        fireEvent.click(await within(dialog).findByRole('option', { name: /Re-engage Q2 no-replies/ }));
        expect(within(dialog).getByText(/adding re-enrols them/)).toBeTruthy();
        fireEvent.input(within(dialog).getByRole('textbox', { name: 'Reason' }), {
            target: { value: 'Back in scope.' },
        });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

        await waitFor(() => expect(api.sent('campaign.add_contacts')).toBeTruthy());
        expect(api.sent('campaign.add_contacts')).toEqual({
            campaign_id: reEngage.id,
            contacts: [{ contact_id: mateo.id }],
            reason: 'Back in scope.',
        });
        await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Add to campaign' })).toBeNull());
        expect(document.querySelector('.panel .notice')).toBeNull();
        expect(api.calls.filter((c) => c.operation === 'contact.get')).toHaveLength(2);

        fireEvent.click(screen.getByRole('button', { name: 'Add to campaign' }));
        const again = await screen.findByRole('dialog', { name: 'Add to campaign' });
        fireEvent.click(within(again).getByRole('button', { name: /Choose a campaign/ }));
        fireEvent.click(await within(again).findByRole('option', { name: /LatAm pilot/ }));
        fireEvent.click(within(again).getByRole('button', { name: 'Add' }));
        expect(await within(again).findByText('LatAm pilot already has them.')).toBeTruthy();
        fireEvent.click(within(again).getByRole('button', { name: 'Add' }));
        expect(await within(again).findByText(/is archived/)).toBeTruthy();
        expect(api.sent('campaign.add_contacts')).toMatchObject({ campaign_id: latAm.id });

        fireEvent.click(screen.getByRole('button', { name: 'Close' }));
        await waitFor(() => expect(screen.queryByRole('heading', { level: 2, name: 'Mateo Ruiz' })).toBeNull());
        const back = { screen: 'people' as const, id: mateo.id, tab: 'timeline' as const, filters: {} };
        location.hash = href(back);
        route.value = back;
        await screen.findByRole('heading', { level: 2, name: 'Mateo Ruiz' });
        expect(screen.queryByRole('dialog', { name: 'Add to campaign' })).toBeNull();
    });
    it('Escape in the campaign list closes only the list, and a second Escape closes the dialog', async () => {
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity')
                .answer('contact.get', 'contact.get/with-campaigns')
                .answer('campaign.list', 'campaign.list/for-picker'),
        );
        open(api, {}, mateo.id);
        // As the tests above: the page's own reads have gone out, so its first effects have run.
        await waitFor(() => expect(api.sent('journal.list')).toBeTruthy());
        fireEvent.click(screen.getByRole('button', { name: 'Add to campaign' }));
        const dialog = await screen.findByRole('dialog', { name: 'Add to campaign' });
        const choose = within(dialog).getByRole('button', { name: /Choose a campaign/ });
        fireEvent.click(choose);
        await within(dialog).findByRole('option', { name: /LatAm pilot/ });

        fireEvent.keyDown(within(dialog).getByPlaceholderText('Find a campaign'), { key: 'Escape' });
        await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull());
        expect(screen.getByRole('dialog', { name: 'Add to campaign' })).toBe(dialog);
        expect(document.activeElement).toBe(choose);

        fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
        await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Add to campaign' })).toBeNull());
    });

    it('open work sits above the now line, farthest first', async () => {
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity')
                .answer('contact.get', 'contact.get/with-campaigns'),
        );
        open(api, {}, mateo.id);

        await waitFor(() => expect(api.sent('workitem.list')).toBeTruthy());
        expect(api.sent('workitem.list')).toEqual({
            contact_id: mateo.id,
            status: ['created', 'awaiting_approval', 'scheduled', 'processing'],
            order: 'due',
            limit: 50,
        });
        const planned = await screen.findByText('responder');
        expect(planned.closest('a')!.textContent).toContain('not before 11:30');
        const now = screen.getByText('now 09:00');
        expect(planned.compareDocumentPosition(now) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('below the now line, what happened, newest first, by day, with membership events in their place', async () => {
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity')
                .answer('contact.get', 'contact.get/with-campaigns'),
        );
        open(api, {}, mateo.id);

        await waitFor(() => expect(api.sent('journal.list')).toBeTruthy());
        await screen.findByText('Replied by email');
        const sent = api.sent('journal.list') as Record<string, unknown>;
        expect(sent).toMatchObject({ contact_id: mateo.id, limit: 50 });
        expect(sent['kinds']).toEqual(
            expect.arrayContaining([
                'inbound_reply_observed',
                'workitem_failed',
                'approval_approved',
                'contact_updated',
            ]),
        );
        expect(rowTitles().filter((t) => t !== 'responder')).toEqual([
            'Replied by email',
            'Enrolled in LatAm pilot',
            'You approved the enrollment',
            'Personalizer finished its work',
            'Researcher updated title and company',
            'Added to LatAm pilot',
            'Excluded from Re-engage Q2 no-replies',
            'Created by Researcher',
            'Added to Re-engage Q2 no-replies',
        ]);
    });

    it('each chip asks for its kinds; membership events show under All and Changes only', async () => {
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity')
                .answer('contact.get', 'contact.get/with-campaigns'),
        );
        api.answer(
            'journal.list',
            'journal.list/by-contact',
            'journal.list/by-contact-replies',
            'journal.list/by-contact-changes',
            'journal.list/by-contact-work',
        );
        open(api, {}, mateo.id);
        await waitFor(() => expect(api.sent('journal.list')).toBeTruthy());
        await screen.findByText('Replied by email');

        fireEvent.click(screen.getByRole('button', { name: 'Replies' }));
        await waitFor(() =>
            expect((api.sent('journal.list') as Record<string, unknown>)['kinds']).toEqual([
                'inbound_reply_observed',
                'provider_event_observed',
            ]),
        );
        await waitFor(() => expect(rowTitles().filter((t) => t !== 'responder')).toEqual(['Replied by email']));

        fireEvent.click(screen.getByRole('button', { name: 'Changes' }));
        await waitFor(() =>
            expect((api.sent('journal.list') as Record<string, unknown>)['kinds']).toEqual([
                'contact_created',
                'contact_updated',
                'contact_archived',
            ]),
        );
        await waitFor(() => expect(rowTitles()).toContain('Added to LatAm pilot'));

        fireEvent.click(screen.getByRole('button', { name: 'Work' }));
        await waitFor(() => expect(rowTitles()).not.toContain('Added to LatAm pilot'));
        expect(rowTitles().filter((t) => t !== 'responder')).toEqual([
            'Enrolled in LatAm pilot',
            'Personalizer finished its work',
        ]);
    });

    it('a row about work opens it in Work items, a failure says what was recorded, and nothing planned shows no Planned', async () => {
        const aguilar = fixtures['contact.get/no-first-name'].response;
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity')
                .answer('contact.get', 'contact.get/no-first-name'),
            'journal.list/by-contact-failed',
            'workitem.list/by-contact-open-none',
        );
        open(api, {}, aguilar.id);

        const failed = (await screen.findByText('Enrollment failed')).closest('a')!;
        expect(failed.textContent).toContain('The route forbids deriving a first name.');
        const line = fixtures['journal.list/by-contact-failed'].response.items.find(
            (e) => e.kind === 'workitem_failed',
        )!;
        expect(failed.getAttribute('href')).toBe(`#/workitems/done/${line.work_item_id}`);
        expect(screen.queryByText('Planned')).toBeNull();
        expect(screen.queryByText('Nothing planned.')).toBeNull();
    });
    it('Details shows the record in one grid, and there is no Messages tab without a provider id', async () => {
        const aguilar = fixtures['contact.get/no-first-name'].response;
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity')
            .answer('contact.get', 'contact.get/no-first-name')
            .answer('suppression.list', ...registerClear);
        open(api, {}, aguilar.id, 'details');
        await waitFor(() => expect(api.sent('contact.get')).toEqual({ contact_id: aguilar.id }));

        const details = (await screen.findByText('aguilar@andina.example')).closest('.panel') as HTMLElement;
        expect(within(details).getByText('primary')).toBeTruthy();
        expect(within(details).getByText('Email')).toBeTruthy();
        expect(Array.from(details.querySelectorAll('dl')).map((dl) => dl.className)).toEqual(['facts', 'facts']);
        expect(screen.queryByRole('tab', { name: 'Messages' })).toBeNull();
        expect(within(details).getByText('Andina Tech')).toBeTruthy();
        expect(within(details.querySelectorAll('.facts')[1] as HTMLElement).getByText(/America\/Lima/)).toBeTruthy();
        expect(within(details).queryByText(/Threads at/)).toBeNull();
        expect(api.calls.some((c) => c.operation === 'contact.threads')).toBe(false);
    });

    it('the Messages tab reads the threads when shown, a thread only when opened, more from where a page ended, cleaned', async () => {
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity')
            .answer('contact.get', 'contact.get/with-campaigns')
            .answer('contact.threads', 'contact.threads/has-more', 'contact.threads/next')
            .answer('contact.thread', 'contact.thread/has-more', 'contact.thread/next')
            .answer('suppression.list', ...registerClear);
        open(api, {}, mateo.id, 'details');
        const tab = await screen.findByRole('tab', { name: 'Messages' });
        expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual([
            'Details',
            'Activity',
            'Sequences',
            'Messages',
            'Next',
            'Research',
        ]);
        expect(api.calls.some((c) => c.operation === 'contact.threads')).toBe(false);
        // A click that asks for a new browser tab is left to the browser.
        expect(fireEvent.click(tab, { ctrlKey: true })).toBe(true);
        expect(fireEvent.click(tab, { metaKey: true })).toBe(true);

        fireEvent.click(tab);
        expect(route.value).toMatchObject({ id: mateo.id, tab: 'threads' });
        await waitFor(() =>
            expect(api.sent('contact.threads')).toEqual({ contact_id: mateo.id, plugin_id: 'reply', limit: 100 }),
        );
        fireEvent.click(await screen.findByRole('button', { name: 'Load more' }));
        // The next page brings an older thread, and repeats one the page already shows.
        expect(await screen.findByText(/Pricing for the pilot/)).toBeTruthy();
        expect(screen.getAllByText(/Happy to connect\./)).toHaveLength(1);
        const linkedIn = screen.getByText(/Happy to connect\./).closest('button')!;
        expect(within(linkedIn).getByText('LinkedIn')).toBeTruthy();
        expect(within(linkedIn).getByText('3 Oct, 18:02')).toBeTruthy();
        expect(linkedIn.querySelector('.s')!.textContent!.startsWith('·')).toBe(false);

        fireEvent.click(screen.getByText(/Re: Soluna's next quarter/));
        await waitFor(() =>
            expect(api.sent('contact.thread')).toEqual({
                contact_id: mateo.id,
                plugin_id: 'reply',
                thread_id: 't_501',
                limit: 20,
            }),
        );
        const bodies = await screen.findAllByTestId('message-body');
        expect(bodies).toHaveLength(3);
        const html = bodies.map((b) => b.innerHTML).join('\n');
        expect(html).toContain('<blockquote>Worth 20 minutes next week?</blockquote>');
        expect(html).toContain(
            '<a href="https://soluna.example/team" target="_blank" rel="noopener noreferrer">Our team</a>',
        );
        for (const gone of [
            '<img',
            '<script',
            '<style',
            'style=',
            'onclick',
            '<iframe',
            '<form',
            '<input',
            'javascript:',
            '<a>',
        ]) {
            expect(html).not.toContain(gone);
        }
        expect(bodies[2]!.innerHTML).toBe(
            'Thursday works.<br>Ten in the morning, Mexico City time &amp; no later?<br><br>On Thu, Mateo &lt;mateo@soluna.example&gt; wrote: &lt;3',
        );

        fireEvent.click(screen.getAllByRole('button', { name: 'Load more' }).at(-1)!);
        await waitFor(() => expect(screen.getAllByTestId('message-body')).toHaveLength(4));
        expect(screen.getAllByTestId('message-body').at(-1)!.textContent).toBe('Ten works. See you Thursday.');
    });

    it('a failed read shows in place, with Try again only when trying again can help', async () => {
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity')
            .answer('contact.get', 'contact.get/with-campaigns', 'contact.get/unrouted')
            .answer(
                'contact.threads',
                'contact.threads/provider-failed',
                'contact.threads/two-threads',
                'contact.threads/no-route',
            );
        open(api, {}, mateo.id, 'threads');

        const failed = await screen.findByRole('alert');
        expect(failed.textContent).toContain(fixtures['contact.threads/provider-failed'].response.error.message);
        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
        expect(await screen.findByText(/Happy to connect\./)).toBeTruthy();

        // Paula is known to the provider in an account no route reaches: trying again cannot help her.
        const paula = {
            screen: 'people' as const,
            id: fixtures['contact.get/unrouted'].response.id,
            tab: 'threads' as const,
            filters: {},
        };
        location.hash = href(paula);
        route.value = paula;
        const refused = await screen.findByRole('alert');
        expect(refused.textContent).toBe(
            "These threads can't be read: no route reaches the reply account this person was linked through.",
        );
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    });

    it("a refresh's answer that arrives after a later read is dropped", async () => {
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity')
            .answer('contact.get', 'contact.get/with-campaigns')
            .answer('contact.threads', 'contact.threads/has-more', 'contact.threads/next');
        open(api, {}, mateo.id, 'threads');
        const more = await screen.findByRole('button', { name: 'Load more' });

        const release = api.hold('contact.threads', (body) => !('offset' in body));
        fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
        fireEvent.click(more);
        await waitFor(() => expect(screen.queryByRole('button', { name: 'Load more' })).toBeNull());

        // The refresh began first and answers last: what it read is older than what is shown.
        await act(async () => {
            release();
            await new Promise((resolve) => setTimeout(resolve, 0));
        });
        expect(screen.queryByRole('button', { name: 'Load more' })).toBeNull();
    });

    it('a threads answer that arrives after another person was opened is dropped', async () => {
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity')
            .answer('contact.get', 'contact.get/with-campaigns', 'contact.get/unrouted')
            .answer('contact.threads', 'contact.threads/two-threads', 'contact.threads/no-route');
        const release = api.hold('contact.threads', (body) => (body as { contact_id: string }).contact_id === mateo.id);
        open(api, {}, mateo.id, 'threads');
        // A held read shows nothing the page could be watched for; the test waits until it has been sent.
        await waitFor(() => expect(api.sent('contact.threads')).toMatchObject({ contact_id: mateo.id }));

        // Paula is linked at the same provider, so her Threads section takes the place of Mateo's.
        const paula = fixtures['contact.get/unrouted'].response;
        const to = { screen: 'people' as const, id: paula.id, tab: 'threads' as const, filters: {} };
        location.hash = href(to);
        route.value = to;
        await screen.findByRole('heading', { level: 2, name: /Paula/ });
        expect((await screen.findByRole('alert')).textContent).toContain('no route reaches');

        // Mateo's answer arrives now; the page has moved on, and it is dropped.
        await act(async () => {
            release();
            await new Promise((resolve) => setTimeout(resolve, 0));
        });
        expect(screen.queryByText(/Happy to connect\./)).toBeNull();
        expect(screen.getByRole('alert').textContent).toContain('no route reaches');
    });

    it('two reply rows of one thread each show the thread when opened, one after the other', async () => {
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity')
                .answer('contact.get', 'contact.get/with-campaigns'),
            'journal.list/by-contact-replied-twice',
        ).answer('contact.thread', 'contact.thread/messages');
        open(api, {}, mateo.id);
        const [newer, older] = await replyRows(2);

        // The older row's read is still on its way when the newer row takes its place.
        const release = api.hold('contact.thread');
        fireEvent.click(older!);
        await screen.findByText('loading…');
        fireEvent.click(newer!);
        await act(async () => {
            release();
            await new Promise((resolve) => setTimeout(resolve, 0));
        });
        expect(await screen.findAllByTestId('message-body')).toHaveLength(3);
        expect(screen.queryByText('loading…')).toBeNull();
    });

    it('a reply said in two campaigns is one row that names both', async () => {
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity')
                .answer('contact.get', 'contact.get/with-campaigns'),
            'journal.list/by-contact-replied-twice',
        );
        open(api, {}, mateo.id);

        const rows = await replyRows(2);
        expect(rows.map((row) => row.querySelector('.in')?.textContent)).toEqual([
            'LatAm pilot, Re-engage Q2 no-replies',
            'LatAm pilot · mentions a meeting',
        ]);
    });

    it("the timeline's reply row opens its thread in place", async () => {
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity')
                .answer('contact.get', 'contact.get/with-campaigns'),
        ).answer('contact.thread', 'contact.thread/messages');
        open(api, {}, mateo.id);
        await waitFor(() => expect(api.sent('journal.list')).toBeTruthy());

        const toggle = await screen.findByRole('button', { name: /show the thread/ });
        expect(toggle.classList.contains('row')).toBe(true);
        expect(within(toggle).getByText('Replied by email')).toBeTruthy();
        expect(within(toggle.querySelector('.what') as HTMLElement).getByText('show the thread ›')).toBeTruthy();
        fireEvent.click(toggle);
        expect(toggle.getAttribute('aria-expanded')).toBe('true');

        await waitFor(() =>
            expect(api.sent('contact.thread')).toEqual({
                contact_id: mateo.id,
                plugin_id: 'reply',
                thread_id: 't_501',
                limit: 20,
            }),
        );
        expect((await screen.findAllByTestId('message-body')).length).toBe(3);
    });

    it("a thread the provider says is not this person's shows the refusal, and the threads stay", async () => {
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity')
            .answer('contact.get', 'contact.get/with-campaigns')
            .answer('contact.threads', 'contact.threads/two-threads')
            .answer('contact.thread', 'contact.thread/not-this-contact');
        open(api, {}, mateo.id, 'threads');

        fireEvent.click(await screen.findByText(/Re: Soluna's next quarter/));
        const refused = await screen.findByRole('alert');
        expect(refused.textContent).toBe(fixtures['contact.thread/not-this-contact'].response.error.message);
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
        expect(screen.getByText(/Happy to connect\./)).toBeTruthy();
    });

    it('an address on Messages for a person linked nowhere shows their activity', async () => {
        const grace = fixtures['contact.get/imported'].response;
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity')
                .answer('contact.get', 'contact.get/imported'),
            'journal.list/by-contact-empty',
            'workitem.list/by-contact-open-imported',
        );
        open(api, {}, grace.id, 'threads');

        await screen.findByRole('heading', { level: 2, name: 'Grace Hopper' });
        expect(screen.getByRole('tab', { name: 'Activity' }).getAttribute('aria-selected')).toBe('true');
        expect(await screen.findByText('Nothing here yet.')).toBeTruthy();
    });

    it('a campaign filter the runtime no longer knows is dropped from the address, and the list is everyone', async () => {
        const gone = fixtures['campaign.get/gone'].request.campaign_id;
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/gone-campaign', 'contact.list/by-activity')
            .answer('campaign.get', 'campaign.get/gone');
        // The gone campaign's numbers never come: the address has left it before they could.
        api.hold('campaign.summary', (body) => (body as { campaign_ids?: string[] }).campaign_ids?.[0] === gone);
        open(api, { campaign: gone });

        await waitFor(() => expect(route.value).toEqual({ screen: 'people', tab: 'timeline', filters: {} }));
        await waitFor(() => expect(screen.getByText('Mateo Ruiz')).toBeTruthy());
        expect(screen.queryByRole('alert')).toBeNull();
    });

    it('a person with no time zone has no place and no clock on their card', async () => {
        const grace = fixtures['contact.get/imported'].response;
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity')
                .answer('contact.get', 'contact.get/imported'),
            'journal.list/by-contact-empty',
            'workitem.list/by-contact-open-imported',
        );
        open(api, {}, grace.id);

        const card = await screen.findByRole('heading', { level: 2, name: 'Grace Hopper' });
        const header = card.closest('.panel-header') as HTMLElement;
        expect(header.querySelector('.sub')!.textContent).toBe('Rear Admiral at Navy');
    });
});

describe('People: the stage chips', () => {
    const chipLabels = () =>
        Array.from(document.querySelectorAll('.stage-filter .chip')).map((c) =>
            c.textContent?.replace(/\s+/g, ' ').trim(),
        );
    const plain = ['All', 'Waiting', 'Failed', 'Working', 'New', 'Idle', 'Sequence', 'Out'];
    const chip = (name: RegExp) =>
        within(document.querySelector('.stage-filter') as HTMLElement).getByRole('button', { name });

    it('ask for one count when chosen, and All asks for everyone; the row is read in the campaign that holds it', async () => {
        const api = new FakeApi().answer(
            'contact.list',
            'contact.list/by-activity-several',
            'contact.list/by-activity-summary-waiting',
            'contact.list/by-activity-several',
        );
        open(api);
        await waitFor(() => expect(screen.getByText('Ada Example')).toBeTruthy());
        expect(screen.getByRole('group', { name: 'Stage' })).toBeTruthy();
        expect(chip(/^Waiting/).getAttribute('title')).toBe(
            'An approval or a question about their work is waiting for you.',
        );

        fireEvent.click(chip(/^Waiting/));
        await waitFor(() => expect(sentLists(api)).toHaveLength(2));
        expect(sentLists(api)[1]).toEqual(fixtures['contact.list/by-activity-summary-waiting'].request);
        expect(route.value).toMatchObject({ screen: 'people', filters: { stage: 'waiting' } });
        await waitFor(() => expect(document.querySelectorAll('.ptable tbody tr')).toHaveLength(3));
        expect(chip(/^Waiting/).getAttribute('aria-pressed')).toBe('true');
        expect(cells(rowOf('Pia Example'))[3]).toBe('WaitingFailed');
        expect(rowOf('Pia Example').getAttribute('aria-current')).toBeNull();

        fireEvent.click(chip(/^All/));
        await waitFor(() => expect(sentLists(api)).toHaveLength(3));
        expect(sentLists(api)[2]).toEqual({ order: 'activity', limit: 200 });
    });

    it('under a chip the campaign shown is the first one that holds the count', async () => {
        const api = new FakeApi().answer('contact.list', 'contact.list/by-activity-summary-waiting');
        open(api, { stage: 'waiting' });
        await waitFor(() => expect(screen.getByText('Ada Example')).toBeTruthy());
        expect(cells(rowOf('Ada Example')).slice(3, 5)).toEqual(['Waiting', 'Spring+2']);
    });

    const withoutArchived = ['All', 'Waiting 3', 'Failed 3', 'Working 2', 'New 3', 'Idle 7', 'Sequence 2', 'Out 6'];
    const withArchived = ['All', 'Waiting 3', 'Failed 3', 'Working 2', 'New 4', 'Idle 7', 'Sequence 2', 'Out 6'];
    const summaries = (api: FakeApi) => api.calls.filter((c) => c.operation === 'campaign.summary').map((c) => c.body);

    it('in the default view a chip shows the number of people it lists, archived people left out as the list leaves them', async () => {
        const api = new FakeApi().answer('contact.list', 'contact.list/by-activity-summary-new-without-archived');
        open(api, { stage: 'new' });
        await waitFor(() => expect(chipLabels()).toEqual(withoutArchived));
        expect(api.sent('campaign.summary')).toEqual(fixtures['campaign.summary/everyone-without-archived'].request);
        expect(sentLists(api)[0]).toEqual(fixtures['contact.list/by-activity-summary-new-without-archived'].request);
        // The number on New and the rows drawn under New are the same people.
        await waitFor(() => expect(document.querySelectorAll('.ptable tbody tr')).toHaveLength(3));
        const rows = document.querySelectorAll('.ptable tbody tr').length;
        expect(chip(/^New/).textContent?.replace(/\s+/g, ' ').trim()).toBe(`New ${rows}`);
    });

    it("narrowed to a campaign in the default view, the chips show that campaign's own numbers, archived people left out", async () => {
        const spring = fixtures['campaign.summary/some-without-archived'].request.campaign_ids[0]!;
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-campaign-without-archived')
            .answer('campaign.get', 'campaign.get/details')
            .answer('campaign.summary', 'campaign.summary/some-without-archived');
        open(api, { campaign: spring });
        await waitFor(() =>
            expect(chipLabels()).toEqual([
                'All',
                'Waiting 3',
                'Failed 3',
                'Working 2',
                'New 1',
                'Idle 6',
                'Sequence 2',
                'Out 6',
            ]),
        );
        expect(api.sent('campaign.summary')).toEqual(fixtures['campaign.summary/some-without-archived'].request);
        // Everyone the list shows is one of the campaign's people, and every one of them is shown.
        const people = fixtures['campaign.summary/some-without-archived'].response.campaigns[0]!.people;
        await waitFor(() => expect(document.querySelectorAll('.ptable tbody tr')).toHaveLength(people));
    });

    it('beside a search no chip shows a number, and none is asked for, because a count knows no search', async () => {
        const api = new FakeApi().answer('contact.list', 'contact.list/by-activity-query');
        open(api, { q: 'mat' });
        await waitFor(() => expect(screen.getByText('Mateo Ruiz')).toBeTruthy());
        expect(chipLabels()).toEqual(plain);
        expect(summaries(api)).toEqual([]);
    });

    it('ticking Archived asks for the numbers again and shows the ones that count archived people; unticking asks again', async () => {
        const api = new FakeApi().answer('contact.list', 'contact.list/by-activity', 'contact.list/archived');
        open(api);
        await waitFor(() => expect(chipLabels()).toEqual(withoutArchived));

        fireEvent.click(screen.getByRole('checkbox', { name: 'Archived' }));
        await waitFor(() => expect(chipLabels()).toEqual(withArchived));
        expect(summaries(api)).toEqual([
            fixtures['campaign.summary/everyone-without-archived'].request,
            fixtures['campaign.summary/everyone'].request,
        ]);

        fireEvent.click(screen.getByRole('checkbox', { name: 'Archived' }));
        await waitFor(() => expect(chipLabels()).toEqual(withoutArchived));
        expect(summaries(api)).toHaveLength(3);
    });

    it('numbers asked before Archived was ticked that arrive after it are dropped', async () => {
        const api = new FakeApi().answer('contact.list', 'contact.list/by-activity', 'contact.list/archived');
        const release = api.hold(
            'campaign.summary',
            (body) => (body as { exclude_archived_contacts?: boolean }).exclude_archived_contacts === true,
        );
        open(api);
        await waitFor(() => expect(summaries(api)).toHaveLength(1));
        // The numbers go as soon as the box changes, before anything answers.
        fireEvent.click(screen.getByRole('checkbox', { name: 'Archived' }));
        await waitFor(() => expect(chipLabels()).toEqual(withArchived));

        release();
        await new Promise((resolve) => setTimeout(resolve, 20));
        expect(chipLabels()).toEqual(withArchived);
    });

    it('a count that fails to read leaves the list and says so', async () => {
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/archived')
            .answer('campaign.summary', 'campaign.summary/everyone');
        open(api, { archived: true });
        await waitFor(() => expect(chipLabels()).toContain('Waiting 3'));
        const reachable = api.unreachable('campaign.summary');
        await pressRefresh();
        expect((await screen.findByRole('alert')).textContent).toContain('Failed to fetch');
        expect(chipLabels()).toEqual(plain);
        expect(document.querySelectorAll('.ptable tbody tr').length).toBeGreaterThan(0);
        reachable();
    });

    it("read a campaign's rows in that campaign when the list is narrowed to it", async () => {
        const unlinked = fixtures['campaign.get/unlinked'].request.campaign_id;
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-campaign')
            .answer('campaign.get', 'campaign.get/unlinked');
        // This list's own numbers are not what the test reads: they stay on their way.
        api.hold('campaign.summary');
        open(api, { campaign: unlinked });
        await waitFor(() => expect(screen.getByText('Mateo Ruiz')).toBeTruthy());
        // Mateo is in two campaigns; the list is of the one, so the row says where they stand there.
        expect(cells(rowOf('Mateo Ruiz')).slice(3, 5)).toEqual(['WorkingOut', 'LatAm pilot+1']);
    });

    it("show a campaign's own numbers when Archived is ticked and the list is narrowed to it, and ask again on a refresh", async () => {
        const unlinked = fixtures['campaign.get/unlinked'].request.campaign_id;
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-campaign-archived')
            .answer('campaign.get', 'campaign.get/unlinked')
            .answer('campaign.summary', 'campaign.summary/unlinked');
        open(api, { campaign: unlinked, archived: true });
        await waitFor(() => expect(chipLabels()).toContain('New 1'));
        expect(api.sent('campaign.summary')).toEqual({ campaign_ids: [unlinked] });
        expect(chipLabels()).toContain('Idle 1');
        expect(chipLabels()).toContain('Waiting 0');

        await pressRefresh();
        await waitFor(() => expect(api.calls.filter((c) => c.operation === 'campaign.summary')).toHaveLength(2));
        await waitFor(() => expect(chipLabels()).toContain('New 1'));
    });

    it('keep drawing the rows with the filters they were fetched with while the next list is on its way', async () => {
        const api = new FakeApi().answer(
            'contact.list',
            'contact.list/by-activity-several',
            'contact.list/by-activity-summary-waiting',
            'contact.list/by-activity-several',
        );
        open(api);
        await waitFor(() => expect(screen.getByText('Cleo Example')).toBeTruthy());
        const release = api.hold('contact.list', (body) => (body as { summary?: string }).summary === 'waiting');

        fireEvent.click(chip(/^Waiting/));
        await waitFor(() => expect(sentLists(api)).toHaveLength(2));
        // Cleo stands in Failed: with the new chip's rule her row would read as nothing at all.
        expect(cells(rowOf('Cleo Example'))[3]).toBe('Failed');
        expect(cells(rowOf('Cleo Example'))[4]).toBe('Spring');

        // The person changes their mind before the answer comes: the late answer is dropped.
        fireEvent.click(chip(/^All/));
        await waitFor(() => expect(sentLists(api)).toHaveLength(3));
        await waitFor(() => expect(route.value).toMatchObject({ filters: {} }));
        release();
        await new Promise((resolve) => setTimeout(resolve, 20));
        expect(document.querySelectorAll('.ptable tbody tr')).toHaveLength(24);
    });
});
