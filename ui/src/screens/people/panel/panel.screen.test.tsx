import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FakeApi } from '../../../test/fake-api';
import { fixtures } from '../../../test/fixtures';
import { renderApp } from '../../../test/render-app';
import { href, route, type PeopleFilters, type PersonTab } from '../../../router';
import * as store from '../store';

// The recordings were made at 09:00 UTC on 16 October; the page reads "now" from the clock.
const recordedAt = new Date('2026-10-16T09:00:00Z');

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(recordedAt);
    // The person a test before left open is not this test's, even where the recordings gave both the same id.
    store.close();
});
afterEach(() => {
    vi.useRealTimers();
});

const ingrid = fixtures['contact.get/person-panel'].response;
const mateo = fixtures['contact.get/with-campaigns'].response;
const grace = fixtures['contact.get/imported'].response;
const aguilar = fixtures['contact.get/no-first-name'].response;

/** Opens People with one person beside the list, the way an address does. */
function open(api: FakeApi, id: string, tab: PersonTab = 'timeline', filters: PeopleFilters = {}) {
    renderApp(api);
    const to = { screen: 'people' as const, id, tab, filters };
    location.hash = href(to);
    route.value = to;
}

/** The list beside the panel, which every test here reads. */
const people = () => new FakeApi().answer('contact.list', 'contact.list/by-activity');

/** Every request a screen sent to an operation, in order, as it gave them. */
const asked = (api: FakeApi, operation: string) =>
    api.calls.filter((call) => call.operation === operation).map((call) => call.body);

const panel = () => document.querySelector<HTMLElement>('.person-panel')!;
/** The panel once it shows a person. */
async function shown(): Promise<HTMLElement> {
    await waitFor(() => expect(document.querySelector('.person-panel .panel-header h2')).toBeTruthy());
    return panel();
}
const tabNames = () =>
    within(panel())
        .getAllByRole('tab')
        .map((tab) => tab.textContent);

describe('The person panel', () => {
    it('has Details, Activity, Sequences and Next, and Messages only for a person a provider knows', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('workitem.list', 'workitem.list/by-contact-open')
            .answer('journal.list', 'journal.list/by-contact');
        open(api, ingrid.id);
        await waitFor(() => expect(within(panel()).getByRole('heading', { name: /Ingrid Solberg/ })).toBeTruthy());
        expect(tabNames()).toEqual(['Details', 'Activity', 'Sequences', 'Next']);
        expect(within(panel()).getByRole('tab', { name: 'Activity' }).getAttribute('aria-selected')).toBe('true');

        // Each tab is an address of its own.
        fireEvent.click(within(panel()).getByRole('tab', { name: 'Sequences' }));
        expect(route.value).toMatchObject({ screen: 'people', id: ingrid.id, tab: 'sequences' });
        expect(location.hash).toBe(`#/people/${ingrid.id}/sequences`);
    });

    it('names Messages for a person linked at a provider, and reads the threads there', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/with-campaigns')
            .answer('contact.threads', 'contact.threads/two-threads');
        open(api, mateo.id, 'threads');
        await waitFor(() => expect(within(panel()).getByRole('heading', { name: /Mateo Ruiz/ })).toBeTruthy());
        expect(tabNames()).toEqual(['Details', 'Activity', 'Sequences', 'Messages', 'Next']);
        expect(within(panel()).getByRole('tab', { name: 'Messages' }).getAttribute('aria-selected')).toBe('true');
        await waitFor(() => expect(asked(api, 'contact.threads')).toHaveLength(1));
    });

    it('Activity is the journal by contact, with the work that is open above the now line', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('workitem.list', 'workitem.list/by-contact-open')
            .answer('journal.list', 'journal.list/by-contact');
        open(api, ingrid.id);
        await waitFor(() => expect(asked(api, 'journal.list')).toHaveLength(1));
        expect(asked(api, 'journal.list')[0]).toMatchObject({ contact_id: ingrid.id, limit: 50 });
        await waitFor(() => expect(panel().querySelector('.now')).toBeTruthy());
    });

    it('Sequences lists the campaigns the person is in, the ones they are in first, each with its state and when', async () => {
        const api = people().answer('contact.get', 'contact.get/person-panel');
        open(api, ingrid.id, 'sequences');
        const nordic = await within(await shown()).findByText('Nordic pilot');
        const rows = within(panel())
            .getAllByRole('listitem')
            .map((row) => row.textContent);
        expect(rows).toHaveLength(2);
        expect(rows[0]).toContain('Nordic pilot');
        expect(rows[0]).toContain('Enrolled');
        expect(rows[0]).toContain('added 2 Oct');
        expect(rows[1]).toContain('Spring webinar');
        expect(rows[1]).toContain('Excluded');
        expect(rows[1]).toContain('changed 21 Sep');
        // The campaign opens on Campaigns.
        expect(nordic.closest('a')!.getAttribute('href')).toBe('#/campaigns/cmp_2');
    });

    it('Sequences says so when the person is in no campaign', async () => {
        const api = people().answer('contact.get', 'contact.get/imported');
        open(api, grace.id, 'sequences');
        expect(await within(await shown()).findByText('Not in any campaign.')).toBeTruthy();
    });

    it("Next lists the person's open work by kind and state as the runtime answers it, each opening in Work items", async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('workitem.list', 'workitem.list/by-contact-open');
        open(api, ingrid.id, 'next');
        await waitFor(() => expect(asked(api, 'workitem.list')).toHaveLength(1));
        expect(asked(api, 'workitem.list')[0]).toEqual({
            contact_id: ingrid.id,
            status: ['created', 'awaiting_approval', 'scheduled', 'processing'],
            order: 'due',
            limit: 50,
        });
        const item = fixtures['workitem.list/by-contact-open'].response.items[0]!;
        const row = (await within(await shown()).findByText('responder')).closest('a')!;
        expect(row.textContent).toContain('Planned');
        expect(row.textContent).toContain(item.campaign_name);
        expect(row.textContent).toContain('not before 11:30');
        expect(row.getAttribute('href')).toBe(`#/workitems/planned/${item.id}`);
        // Nothing of ours says what Jason will do next: only what the runtime answered is drawn.
        expect(panel().textContent).not.toMatch(/will /);
    });

    it('Next says so when nothing is open', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/no-first-name')
            .answer('workitem.list', 'workitem.list/by-contact-open-none');
        open(api, aguilar.id, 'next');
        expect(await within(await shown()).findByText('No open work for this person.')).toBeTruthy();
    });

    it('a refresh of the list never closes the open panel, even when the person is no longer listed', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('workitem.list', 'workitem.list/by-contact-open');
        open(api, ingrid.id, 'next');
        await within(await shown()).findByText('responder');
        const lists = asked(api, 'contact.list').length;
        fireEvent.click(screen.getByTitle('Refresh'));
        await waitFor(() => expect(asked(api, 'contact.list').length).toBe(lists + 1));
        expect(within(panel()).getByRole('heading', { name: /Ingrid Solberg/ })).toBeTruthy();
        expect(route.value).toMatchObject({ id: ingrid.id, tab: 'next' });
    });
});
