import { fireEvent, screen, waitFor, within } from '@testing-library/preact';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { fixtures, type FixtureName } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
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

/** Opens the screen the way an address does: renderApp clears the address, and its late change event must agree with ours. */
function open(api: FakeApi, filters: PeopleFilters = {}, id?: string, tab: PersonTab = 'timeline') {
    renderApp(api);
    const to = id ? { screen: 'people' as const, id, tab, filters } : { screen: 'people' as const, tab, filters };
    location.hash = href(to);
    route.value = to;
}

const sentLists = (api: FakeApi) =>
    api.calls.filter((c) => c.operation === 'contact.list').map((c) => c.body as Record<string, unknown>);

const mateo = fixtures['contact.get/with-campaigns'].response;

/** What a person's page reads besides their record: their open work and their journal under All. */
function timeline(
    api: FakeApi,
    journal: FixtureName = 'journal.list/by-contact',
    work: FixtureName = 'workitem.list/by-contact-open',
) {
    return api.answer('workitem.list', work).answer('journal.list', journal);
}

const rowTitles = () =>
    Array.from(document.querySelectorAll('.panel .rows .row .name')).map((n) => n.textContent?.trim());

describe('People', () => {
    it('lists people by what last happened to them, by day, the one with no first name by last name, nobody-yet last', async () => {
        const api = new FakeApi().answer('contact.list', 'contact.list/by-activity', 'contact.list/by-activity-next');
        open(api);

        await waitFor(() => expect(screen.getByText('Mateo Ruiz')).toBeTruthy());
        expect(sentLists(api)[0]).toEqual({ order: 'activity', limit: 50 });
        const today = screen.getByText('Today');
        expect(today.className).toContain('group');
        const mateoRow = screen.getByText('Mateo Ruiz').closest('a')!;
        expect(within(mateoRow).getByText('Head of Sales, Soluna · LatAm pilot')).toBeTruthy();
        expect(within(mateoRow).getByText(/replied by email/)).toBeTruthy();
        expect(within(mateoRow).getByText('08:41')).toBeTruthy();
        const aguilarRow = screen.getByText('Aguilar').closest('a')!;
        expect(within(aguilarRow).getByText(/enrollment failed/)).toBeTruthy();
        const paulaRow = screen.getByText('Paula Mendes').closest('a')!;
        expect(within(paulaRow).getByText('researcher started')).toBeTruthy();
        expect(screen.getByText('6 October')).toBeTruthy();

        fireEvent.click(screen.getByRole('button', { name: /loading/ }));
        await waitFor(() => expect(screen.getByText('Grace Hopper')).toBeTruthy());
        expect(sentLists(api)[1]).toEqual({
            order: 'activity',
            limit: 50,
            cursor: fixtures['contact.list/by-activity'].response.next_cursor,
        });
        const groups = Array.from(document.querySelectorAll('.rows .group')).map((g) => g.textContent);
        expect(groups.at(-1)).toBe('No activity yet');
    });

    it('search, campaign and archived start from the first page and live in the address', async () => {
        const api = new FakeApi()
            .answer(
                'contact.list',
                'contact.list/by-activity',
                'contact.list/by-activity-query',
                'contact.list/by-campaign',
                'contact.list/query-none',
            )
            .answer('campaign.list', 'campaign.list/for-picker');
        open(api);
        await waitFor(() => expect(screen.getByText('Mateo Ruiz')).toBeTruthy());

        fireEvent.input(screen.getByRole('searchbox', { name: 'Search people' }), { target: { value: 'mat' } });
        await waitFor(() => expect(sentLists(api)).toHaveLength(2));
        expect(sentLists(api)[1]).toEqual({ order: 'activity', limit: 50, query: 'mat' });
        expect(route.value).toMatchObject({ screen: 'people', filters: { q: 'mat' } });

        fireEvent.click(screen.getByRole('button', { name: /All campaigns/ }));
        const latAm = await screen.findByRole('option', { name: /LatAm pilot/ });
        fireEvent.click(latAm);
        await waitFor(() => expect(sentLists(api)).toHaveLength(3));
        const campaignId = fixtures['campaign.list/for-picker'].response.items.find(
            (c) => c.name === 'LatAm pilot',
        )!.id;
        expect(sentLists(api)[2]).toEqual({ order: 'activity', limit: 50, query: 'mat', campaign_id: campaignId });

        fireEvent.click(screen.getByRole('checkbox', { name: 'Archived' }));
        await waitFor(() => expect(sentLists(api)).toHaveLength(4));
        expect(sentLists(api)[3]).toMatchObject({ include_archived: true, campaign_id: campaignId, query: 'mat' });
        expect(route.value).toMatchObject({ filters: { q: 'mat', campaign: campaignId, archived: true } });
        await waitFor(() => expect(screen.getByText('Nobody matches.')).toBeTruthy());
    });

    it('a row opens the person, keeping the list and its filters; a filter changed or another row keeps the tab', async () => {
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity')
            .answer('contact.get', 'contact.get/with-campaigns');
        open(api, { q: 'mat' });
        await waitFor(() => expect(screen.getByText('Mateo Ruiz')).toBeTruthy());

        fireEvent.click(screen.getByText('Mateo Ruiz').closest('a')!);

        expect(route.value).toEqual({ screen: 'people', id: mateo.id, tab: 'timeline', filters: { q: 'mat' } });
        await waitFor(() => expect(screen.getByText('Mateo Ruiz').closest('a')!.className).toContain('selected'));

        route.value = { screen: 'people', id: mateo.id, tab: 'details', filters: { q: 'mat' } };
        fireEvent.input(screen.getByRole('searchbox', { name: 'Search people' }), { target: { value: 'mateo' } });
        await waitFor(() => expect(route.value).toMatchObject({ filters: { q: 'mateo' } }));
        expect(route.value).toEqual({ screen: 'people', id: mateo.id, tab: 'details', filters: { q: 'mateo' } });

        const aguilar = fixtures['contact.list/by-activity'].response.items.find((c) => c.last_name === 'Aguilar')!;
        fireEvent.click(screen.getByText('Aguilar').closest('a')!);
        expect(route.value).toEqual({ screen: 'people', id: aguilar.id, tab: 'details', filters: { q: 'mateo' } });
    });
    it("a person's card shows where they are, their local time and their campaigns, active first, excluded outlined; it closes as Work items' panel does", async () => {
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity')
            .answer('contact.get', 'contact.get/with-campaigns');
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
            'workitem.list/empty',
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
            'journal.list/by-contact-empty',
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
        await waitFor(() => expect(screen.getByText('Nothing here yet.')).toBeTruthy());
    });

    it('a row about work opens it in Work items, a failure says what was recorded, and nothing planned shows no Planned', async () => {
        const aguilar = fixtures['contact.get/no-first-name'].response;
        const api = timeline(
            new FakeApi()
                .answer('contact.list', 'contact.list/by-activity')
                .answer('contact.get', 'contact.get/no-first-name'),
            'journal.list/by-contact-failed',
            'workitem.list/empty',
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
    it('Details shows the record in one grid, and there is no Threads tab without a provider id', async () => {
        const aguilar = fixtures['contact.get/no-first-name'].response;
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity')
            .answer('contact.get', 'contact.get/no-first-name');
        open(api, {}, aguilar.id, 'details');
        await waitFor(() => expect(api.sent('contact.get')).toEqual({ contact_id: aguilar.id }));

        const details = (await screen.findByText('aguilar@andina.example')).closest('.panel') as HTMLElement;
        expect(within(details).getByText('primary')).toBeTruthy();
        expect(within(details).getByText('Email')).toBeTruthy();
        expect(Array.from(details.querySelectorAll('dl')).map((dl) => dl.className)).toEqual(['facts', 'facts']);
        expect(screen.queryByRole('tab', { name: 'Threads' })).toBeNull();
        expect(within(details).getByText('Andina Tech')).toBeTruthy();
        expect(within(details.querySelectorAll('.facts')[1] as HTMLElement).getByText(/America\/Lima/)).toBeTruthy();
        expect(within(details).queryByText(/Threads at/)).toBeNull();
        expect(api.calls.some((c) => c.operation === 'contact.threads')).toBe(false);
    });

    it('the Threads tab reads the threads when shown, a thread only when opened, more from where a page ended, cleaned', async () => {
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity')
            .answer('contact.get', 'contact.get/with-campaigns')
            .answer('contact.threads', 'contact.threads/has-more', 'contact.threads/two-threads')
            .answer('contact.thread', 'contact.thread/has-more', 'contact.thread/messages');
        open(api, {}, mateo.id, 'details');
        const tab = await screen.findByRole('tab', { name: 'Threads' });
        expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual(['Timeline', 'Details', 'Threads']);
        expect(api.calls.some((c) => c.operation === 'contact.threads')).toBe(false);

        fireEvent.click(tab);
        expect(route.value).toMatchObject({ id: mateo.id, tab: 'threads' });
        await waitFor(() =>
            expect(api.sent('contact.threads')).toEqual({ contact_id: mateo.id, plugin_id: 'reply', limit: 100 }),
        );
        fireEvent.click(await screen.findByRole('button', { name: 'Load more' }));
        await waitFor(() =>
            expect(api.sent('contact.threads')).toEqual({
                contact_id: mateo.id,
                plugin_id: 'reply',
                limit: 100,
                offset: 2,
            }),
        );
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
        await waitFor(() =>
            expect(api.sent('contact.thread')).toEqual({
                contact_id: mateo.id,
                plugin_id: 'reply',
                thread_id: 't_501',
                limit: 20,
                offset: 3,
            }),
        );
    });

    it('a failed read shows in place, with Try again only when trying again can help', async () => {
        const api = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity')
            .answer('contact.get', 'contact.get/with-campaigns')
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

        fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
        const refused = await screen.findByRole('alert');
        expect(refused.textContent).toBe(
            "These threads can't be read: no route reaches the reply account this person was linked through.",
        );
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
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
});
