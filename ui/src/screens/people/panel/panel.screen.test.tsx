import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ApiProvider } from '../../../api/context';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FakeApi, watch } from '../../../test/fake-api';
import { fixtures, type FixtureName } from '../../../test/fixtures';
import { renderApp } from '../../../test/render-app';
import { href, route, type PeopleFilters, type PersonTab } from '../../../router';
import * as store from '../store';
import { PersonPanel } from './panel';

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
const people = () =>
    new FakeApi()
        .answer('contact.list', 'contact.list/by-activity')
        .answer('campaign.summary', 'campaign.summary/everyone');

/** Every request a screen sent to an operation, in order, as it gave them. */
const asked = (api: FakeApi, operation: string) =>
    api.calls.filter((call) => call.operation === operation).map((call) => call.body);

/** A recorded request as the screen gives it: the client adds the actor to a write. */
function recorded(name: FixtureName): object {
    const rest: Record<string, unknown> = { ...(fixtures[name].request as Record<string, unknown>) };
    delete rest['actor'];
    return rest;
}

/** Details reads Jason's register for each address it shows; none of these is on it. */
const registerClear: FixtureName[] = [
    'suppression.list/person-email-clear',
    'suppression.list/person-linkedin-clear',
    'suppression.list/person-phone-clear',
    'suppression.list/grace-email-clear',
];

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
    it('has Details, Activity, Sequences, Next and Research, and Messages only for a person a provider knows', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('workitem.list', 'workitem.list/by-contact-open')
            .answer('journal.list', 'journal.list/by-contact');
        open(api, ingrid.id);
        await waitFor(() => expect(within(panel()).getByRole('heading', { name: /Ingrid Solberg/ })).toBeTruthy());
        expect(tabNames()).toEqual(['Details', 'Activity', 'Sequences', 'Next', 'Research']);
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
        expect(tabNames()).toEqual(['Details', 'Activity', 'Sequences', 'Messages', 'Next', 'Research']);
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

describe('The person panel on a screen of its own', () => {
    it('is mounted by the person and an opening campaign alone; its tabs have no address there and close is the host', async () => {
        const api = new FakeApi()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('workitem.list', 'workitem.list/by-contact-open')
            .answer('journal.list', 'journal.list/by-contact');
        watch(api);
        const onClose = vi.fn();
        location.hash = '#/campaigns/cmp_2/people';
        render(
            <ApiProvider api={api}>
                <PersonPanel contactId={ingrid.id} campaignId="cmp_2" onClose={onClose} />
            </ApiProvider>,
        );
        await shown();
        expect(asked(api, 'contact.get')).toEqual([{ contact_id: ingrid.id }]);
        expect(tabNames()).toEqual(['Details', 'Activity', 'Sequences', 'Next', 'Research']);
        expect(within(panel()).getByRole('tab', { name: 'Activity' }).getAttribute('aria-selected')).toBe('true');

        fireEvent.click(within(panel()).getByRole('tab', { name: 'Sequences' }));
        expect(await within(panel()).findByText('Nordic pilot', { selector: 'a' })).toBeTruthy();
        expect(within(panel()).getByRole('tab', { name: 'Sequences' }).getAttribute('aria-selected')).toBe('true');
        // The host's address is the host's: the panel moved nothing.
        expect(location.hash).toBe('#/campaigns/cmp_2/people');

        fireEvent.click(within(panel()).getByRole('button', { name: 'Close' }));
        expect(onClose).toHaveBeenCalledTimes(1);
    });

    it('takes the tab on show and where each tab is as one pair: both or neither', () => {
        const to = (tab: PersonTab) => ({ screen: 'people' as const, id: ingrid.id, tab, filters: {} });
        const both = <PersonPanel contactId={ingrid.id} tab="details" to={to} onClose={() => undefined} />;
        const neither = <PersonPanel contactId={ingrid.id} onClose={() => undefined} />;
        // @ts-expect-error A tab without where each tab is would draw tabs that do nothing.
        const tabAlone = <PersonPanel contactId={ingrid.id} tab="details" onClose={() => undefined} />;
        // @ts-expect-error Where each tab is without the tab on show would never show the one it went to.
        const toAlone = <PersonPanel contactId={ingrid.id} to={to} onClose={() => undefined} />;
        expect([both, neither, tabAlone, toAlone]).toHaveLength(4);
    });
});

describe('Saving the person', () => {
    const field = (name: string) => within(panel()).getByRole<HTMLInputElement>('textbox', { name });
    const type = (name: string, value: string) => fireEvent.input(field(name), { target: { value } });
    const reason = (value: string) =>
        fireEvent.input(within(panel()).getByRole('textbox', { name: 'Reason (optional, kept in the journal)' }), {
            target: { value },
        });
    const providerBox = () =>
        within(panel()).getByRole<HTMLInputElement>('checkbox', { name: /queue the name, company and title/ });

    it("saves the fields changed with the reason, and the ticked box queues the provider's contact.update with what its document takes", async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('suppression.list', ...registerClear)
            .answer('contact.update', 'contact.update/person-saved')
            .answer('workitem.create', 'workitem.create/provider-contact-update');
        open(api, ingrid.id, 'details');
        fireEvent.click(await within(await shown()).findByRole('button', { name: 'Edit' }));
        expect(field('Title').value).toBe('Head of Revenue');
        type('Title', 'VP Revenue');
        reason('Promoted in October.');
        expect(providerBox().closest('label')!.textContent).toContain('Nordic pilot');
        fireEvent.click(providerBox());
        fireEvent.click(within(panel()).getByRole('button', { name: 'Save' }));

        await waitFor(() => expect(asked(api, 'workitem.create')).toHaveLength(1));
        expect(asked(api, 'contact.update')).toEqual([recorded('contact.update/person-saved')]);
        expect(asked(api, 'workitem.create')).toEqual([recorded('workitem.create/provider-contact-update')]);
        const said = await within(panel()).findByRole('status');
        expect(said.textContent).toContain('The change is queued for the provider in Nordic pilot.');
        expect(within(said).getByRole('link', { name: 'Open the work item' }).getAttribute('href')).toBe(
            '#/workitems/planned/wi_1',
        );
        // The record on show is the one the runtime answered, with the campaigns the panel had read.
        expect(within(panel()).getByText(/VP Revenue at Fjordline/)).toBeTruthy();
        expect(within(panel()).queryByRole('button', { name: 'Save' })).toBeNull();
        expect(within(panel()).getByText(/Nordic pilot · since/)).toBeTruthy();
    });

    it('a queue the runtime refuses is said with the save standing, and the record shows what was saved', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('suppression.list', ...registerClear)
            .answer('contact.update', 'contact.update/person-saved')
            .answer('workitem.create', 'workitem.create/provider-archived');
        open(api, ingrid.id, 'details');
        fireEvent.click(await within(await shown()).findByRole('button', { name: 'Edit' }));
        type('Title', 'VP Revenue');
        reason('Promoted in October.');
        fireEvent.click(providerBox());
        fireEvent.click(within(panel()).getByRole('button', { name: 'Save' }));

        const said = await within(panel()).findByRole('status');
        expect(asked(api, 'contact.update')).toEqual([recorded('contact.update/person-saved')]);
        expect(asked(api, 'workitem.create')).toEqual([recorded('workitem.create/provider-archived')]);
        expect(said.textContent).toBe(
            "Saved in Jason. Queuing the change for the provider was refused: Campaign 'cmp_2' is archived; only journal entries can still be appended.",
        );
        expect(within(panel()).getByText(/VP Revenue at Fjordline/)).toBeTruthy();
        expect(within(panel()).queryByRole('button', { name: 'Save' })).toBeNull();
    });

    it('a field emptied is sent as null, and no channel is sent when none changed', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('suppression.list', ...registerClear)
            .answer('contact.update', 'contact.update/person-cleared');
        open(api, ingrid.id, 'details');
        fireEvent.click(await within(await shown()).findByRole('button', { name: 'Edit' }));
        type('Company', '');
        fireEvent.click(within(panel()).getByRole('button', { name: 'Save' }));

        await waitFor(() => expect(asked(api, 'contact.update')).toHaveLength(1));
        expect(asked(api, 'contact.update')).toEqual([recorded('contact.update/person-cleared')]);
        expect(asked(api, 'contact.update')[0]).not.toHaveProperty('channels');
        expect(await within(panel()).findByRole('button', { name: 'Edit' })).toBeTruthy();
    });

    it('a save the runtime refuses says why and keeps what was typed; nothing is queued', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('suppression.list', ...registerClear)
            .answer('contact.update', 'contact.update/person-refused');
        open(api, ingrid.id, 'details');
        fireEvent.click(await within(await shown()).findByRole('button', { name: 'Edit' }));
        type('Time zone', 'Europe/Atlantis');
        reason('Moved.');
        // No name, company or title changed: there is nothing for the provider, and the box is off.
        expect(providerBox().disabled).toBe(true);
        fireEvent.click(within(panel()).getByRole('button', { name: 'Save' }));

        expect((await within(panel()).findByRole('alert')).textContent).toContain(
            'Validation failed: time_zone: invalid.',
        );
        expect(asked(api, 'contact.update')).toEqual([recorded('contact.update/person-refused')]);
        expect(asked(api, 'workitem.create')).toHaveLength(0);
        expect(field('Time zone').value).toBe('Europe/Atlantis');
    });

    it('edits the channels: the ones the person had are sent as they were, with the one added', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('suppression.list', ...registerClear)
            .answer('contact.update', 'contact.update/person-channels');
        open(api, ingrid.id, 'details');
        fireEvent.click(await within(await shown()).findByRole('button', { name: 'Edit' }));
        fireEvent.click(within(panel()).getByRole('button', { name: 'Add a channel' }));
        const kinds = within(panel()).getAllByRole<HTMLSelectElement>('combobox', { name: 'Kind' });
        fireEvent.change(kinds.at(-1)!, { target: { value: 'phone' } });
        const values = within(panel()).getAllByRole<HTMLInputElement>('textbox', { name: 'Address' });
        fireEvent.input(values.at(-1)!, { target: { value: '+12125550147' } });
        reason('Gave a phone number.');
        fireEvent.click(within(panel()).getByRole('button', { name: 'Save' }));

        await waitFor(() => expect(asked(api, 'contact.update')).toHaveLength(1));
        expect(asked(api, 'contact.update')).toEqual([recorded('contact.update/person-channels')]);
        expect(await within(panel()).findByText('+12125550147')).toBeTruthy();
    });

    it('picks a primary per kind: a pick changes its own kind only, and a row moved onto a kind with one is not primary', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('suppression.list', ...registerClear, 'suppression.list/person-second-email-clear')
            .answer('contact.update', 'contact.update/person-primaries');
        open(api, ingrid.id, 'details');
        fireEvent.click(await within(await shown()).findByRole('button', { name: 'Edit' }));
        const primaries = () =>
            within(panel())
                .getAllByRole<HTMLInputElement>('radio', { name: 'Primary' })
                .map((radio) => radio.checked);
        const kinds = () => within(panel()).getAllByRole<HTMLSelectElement>('combobox', { name: 'Kind' });
        expect(kinds().map((kind) => kind.value)).toEqual(['email', 'linkedin']);
        expect(primaries()).toEqual([true, false]);

        // LinkedIn's address picked as its primary keeps the email's.
        const radio = (at: number) => within(panel()).getAllByRole('radio', { name: 'Primary' })[at]!;
        fireEvent.click(radio(1));
        expect(primaries()).toEqual([true, true]);

        // Moved onto email, which has its primary, it is not primary; moved back, it is picked again.
        fireEvent.change(kinds()[1]!, { target: { value: 'email' } });
        expect(primaries()).toEqual([true, false]);
        fireEvent.change(kinds()[1]!, { target: { value: 'linkedin' } });
        fireEvent.click(radio(1));
        expect(primaries()).toEqual([true, true]);

        // A second email picked as the primary email leaves LinkedIn's primary where it is.
        fireEvent.click(within(panel()).getByRole('button', { name: 'Add a channel' }));
        const values = within(panel()).getAllByRole<HTMLInputElement>('textbox', { name: 'Address' });
        fireEvent.input(values.at(-1)!, { target: { value: 'ingrid.solberg@fjordline.example' } });
        fireEvent.click(radio(2));
        expect(primaries()).toEqual([false, true, true]);
        fireEvent.click(within(panel()).getByRole('button', { name: 'Save' }));

        await waitFor(() => expect(asked(api, 'contact.update')).toHaveLength(1));
        expect(asked(api, 'contact.update')).toEqual([recorded('contact.update/person-primaries')]);
        expect(await within(panel()).findByText('ingrid.solberg@fjordline.example')).toBeTruthy();
    });

    it('a person in no campaign has no box for the provider, and Cancel leaves the record as it was', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/imported')
            .answer('suppression.list', ...registerClear);
        open(api, grace.id, 'details');
        fireEvent.click(await within(await shown()).findByRole('button', { name: 'Edit' }));
        expect(within(panel()).queryByRole('checkbox', { name: /queue/ })).toBeNull();
        type('Company', 'Navy');
        fireEvent.click(within(panel()).getByRole('button', { name: 'Cancel' }));
        expect(within(panel()).getByRole('button', { name: 'Edit' })).toBeTruthy();
        expect(asked(api, 'contact.update')).toHaveLength(0);
    });

    it('a refresh of the list keeps the panel and what is being typed in it', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('suppression.list', ...registerClear);
        open(api, ingrid.id, 'details');
        fireEvent.click(await within(await shown()).findByRole('button', { name: 'Edit' }));
        type('Title', 'VP Revenue');
        const lists = asked(api, 'contact.list').length;
        // A refresh an earlier test began holds the button for half a second.
        const refresh = screen.getByTitle<HTMLButtonElement>('Refresh');
        await waitFor(() => expect(refresh.disabled).toBe(false));
        fireEvent.click(refresh);
        await waitFor(() => expect(asked(api, 'contact.list').length).toBe(lists + 1));
        expect(field('Title').value).toBe('VP Revenue');
    });
});

describe('The campaign the provider change is queued in', () => {
    it('is named, and for a person in more than one it is the opening campaign until the person picks another', async () => {
        const api = new FakeApi()
            .answer('contact.get', 'contact.get/person-after-half-move')
            .answer('suppression.list', ...registerClear)
            .answer('workitem.list', 'workitem.list/by-contact-open')
            .answer('journal.list', 'journal.list/by-contact');
        watch(api);
        render(
            <ApiProvider api={api}>
                <PersonPanel contactId={ingrid.id} campaignId="cmp_3" onClose={() => undefined} />
            </ApiProvider>,
        );
        await shown();
        fireEvent.click(within(panel()).getByRole('tab', { name: 'Details' }));
        fireEvent.click(await within(panel()).findByRole('button', { name: 'Edit' }));
        fireEvent.input(within(panel()).getByRole('textbox', { name: 'Title' }), { target: { value: 'VP Revenue' } });

        const box = within(panel()).getByRole<HTMLInputElement>('checkbox', {
            name: /queue the name, company and title/,
        });
        expect(box.disabled).toBe(false);
        const where = within(panel()).getByRole<HTMLSelectElement>('combobox', {
            name: 'The campaign the change is queued in',
        });
        expect(Array.from(where.options).map((option) => option.textContent)).toEqual([
            'Nordic pilot',
            'Q4 follow-ups',
        ]);
        expect(where.value).toBe('cmp_3');
        fireEvent.change(where, { target: { value: 'cmp_2' } });
        expect(where.value).toBe('cmp_2');
    });

    it('is the campaign picked, not the opening one, that the work item carries', async () => {
        const api = new FakeApi()
            .answer('contact.get', 'contact.get/person-after-half-move')
            .answer('suppression.list', ...registerClear)
            .answer('workitem.list', 'workitem.list/by-contact-open')
            .answer('journal.list', 'journal.list/by-contact')
            .answer('contact.update', 'contact.update/person-saved')
            .answer('workitem.create', 'workitem.create/provider-contact-update');
        watch(api);
        render(
            <ApiProvider api={api}>
                <PersonPanel contactId={ingrid.id} campaignId="cmp_3" onClose={() => undefined} />
            </ApiProvider>,
        );
        fireEvent.click(within(await shown()).getByRole('tab', { name: 'Details' }));
        fireEvent.click(await within(panel()).findByRole('button', { name: 'Edit' }));
        fireEvent.input(within(panel()).getByRole('textbox', { name: 'Title' }), { target: { value: 'VP Revenue' } });
        fireEvent.input(within(panel()).getByRole('textbox', { name: 'Reason (optional, kept in the journal)' }), {
            target: { value: 'Promoted in October.' },
        });
        fireEvent.click(within(panel()).getByRole('checkbox', { name: /queue the name, company and title/ }));
        fireEvent.change(within(panel()).getByRole('combobox', { name: 'The campaign the change is queued in' }), {
            target: { value: 'cmp_2' },
        });
        fireEvent.click(within(panel()).getByRole('button', { name: 'Save' }));

        expect((await within(panel()).findByRole('status')).textContent).toContain(
            'The change is queued for the provider in Nordic pilot.',
        );
        expect(asked(api, 'workitem.create')).toEqual([recorded('workitem.create/provider-contact-update')]);
    });

    it('is a campaign whose work with the person finished when that is the one not excluded, with the ask; nothing to exclude or move', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-finished')
            .answer('suppression.list', ...registerClear)
            .answer('workitem.list', 'workitem.list/researcher-findings')
            .answer('workitem.get', 'workitem.get/researcher-finding');
        open(api, ingrid.id, 'details');
        fireEvent.click(await within(await shown()).findByRole('button', { name: 'Edit' }));
        fireEvent.input(within(panel()).getByRole('textbox', { name: 'Title' }), { target: { value: 'VP Revenue' } });
        const box = within(panel()).getByRole<HTMLInputElement>('checkbox', {
            name: /queue the name, company and title/,
        });
        expect(box.disabled).toBe(false);
        expect(box.closest('label')!.textContent).toContain('Nordic pilot');
        fireEvent.click(within(panel()).getByRole('button', { name: 'Cancel' }));

        fireEvent.click(within(panel()).getByRole('tab', { name: 'Research' }));
        await within(panel()).findByText('Fjordline opened an office in Bergen in September.');
        const ask = within(panel()).getByRole('button', { name: 'Ask the researcher' });
        expect(ask.closest('form')?.textContent).toContain('in Nordic pilot');

        fireEvent.click(within(panel()).getByRole('tab', { name: 'Sequences' }));
        const finished = (await within(panel()).findByText('Nordic pilot', { selector: 'a' })).closest('li')!;
        expect(finished.textContent).toContain('Finished');
        expect(within(panel()).queryByRole('button', { name: 'Exclude' })).toBeNull();
        expect(within(panel()).queryByRole('button', { name: 'Move' })).toBeNull();
    });
});

describe('Do not contact', () => {
    const email = fixtures['contact.get/person-panel'].response.channels[0]!.value;
    /** The row of one address in Details. */
    const row = (value: string) => within(panel()).getByText(value).closest('dd') as HTMLElement;

    it("Details reads Jason's register for each address, and says which ones Jason will not contact", async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer(
                'suppression.list',
                'suppression.list/person-email-listed',
                'suppression.list/person-linkedin-clear',
            );
        open(api, ingrid.id, 'details');
        await waitFor(() => expect(asked(api, 'suppression.list')).toHaveLength(2));
        expect(asked(api, 'suppression.list')).toEqual([
            recorded('suppression.list/person-email-listed'),
            recorded('suppression.list/person-linkedin-clear'),
        ]);
        await waitFor(() => expect(row(email).textContent).toContain('Jason will not contact this address'));
        expect(within(row(email)).getByRole('button', { name: 'Contact again' })).toBeTruthy();
        const linkedin = row(fixtures['contact.get/person-panel'].response.channels[1]!.value);
        expect(within(linkedin).getByRole('button', { name: 'Do not contact' })).toBeTruthy();
        expect(linkedin.textContent).not.toContain('Jason will not contact');
    });

    it("puts an address on Jason's register with the reason given, and says so on its row", async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('suppression.list', ...registerClear)
            .answer('suppression.add', 'suppression.add/person-email');
        open(api, ingrid.id, 'details');
        await shown();
        fireEvent.click(await waitFor(() => within(row(email)).getByRole('button', { name: 'Do not contact' })));
        fireEvent.input(within(row(email)).getByRole('textbox', { name: 'Reason (optional, kept in the journal)' }), {
            target: { value: 'Asked not to be emailed.' },
        });
        fireEvent.click(within(row(email)).getByRole('button', { name: 'Do not contact this address' }));

        await waitFor(() => expect(row(email).textContent).toContain('Jason will not contact this address'));
        expect(asked(api, 'suppression.add')).toEqual([recorded('suppression.add/person-email')]);
        expect(within(row(email)).getByRole('button', { name: 'Contact again' })).toBeTruthy();
    });

    it('takes an address off the register only with a reason, and the row offers Do not contact again', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer(
                'suppression.list',
                'suppression.list/person-email-listed',
                'suppression.list/person-linkedin-clear',
            )
            .answer('suppression.remove', 'suppression.remove/person-email');
        open(api, ingrid.id, 'details');
        fireEvent.click(await within(await shown()).findByRole('button', { name: 'Contact again' }));
        const confirm = within(row(email)).getByRole<HTMLButtonElement>('button', {
            name: 'Take this address off the register',
        });
        expect(confirm.disabled).toBe(true);
        fireEvent.input(within(row(email)).getByRole('textbox', { name: 'Reason (required, kept in the journal)' }), {
            target: { value: 'Asked to hear from us again.' },
        });
        expect(confirm.disabled).toBe(false);
        fireEvent.click(confirm);

        await waitFor(() => expect(asked(api, 'suppression.remove')).toHaveLength(1));
        expect(asked(api, 'suppression.remove')).toEqual([recorded('suppression.remove/person-email')]);
        expect(await within(row(email)).findByRole('button', { name: 'Do not contact' })).toBeTruthy();
        expect(row(email).textContent).not.toContain('Jason will not contact');
    });
});

describe('Exclude', () => {
    /** A campaign's row on Sequences. */
    const membership = (name: string) =>
        within(panel()).getByText(name, { selector: 'a' }).closest('li') as HTMLElement;

    it('excludes the person from a campaign they are in, with the reason given, and shows them excluded there', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel', 'contact.get/person-after-exclude')
            .answer('campaign.remove_contacts', 'campaign.remove_contacts/exclude-one');
        open(api, ingrid.id, 'sequences');
        await within(await shown()).findByText('Nordic pilot', { selector: 'a' });
        // A campaign the person already left offers nothing.
        expect(within(membership('Spring webinar')).queryByRole('button', { name: 'Exclude' })).toBeNull();

        fireEvent.click(within(membership('Nordic pilot')).getByRole('button', { name: 'Exclude' }));
        fireEvent.input(
            within(membership('Nordic pilot')).getByRole('textbox', { name: 'Reason (optional, kept in the journal)' }),
            { target: { value: 'Not a fit for the pilot.' } },
        );
        fireEvent.click(within(membership('Nordic pilot')).getByRole('button', { name: 'Exclude from Nordic pilot' }));

        await waitFor(() => expect(asked(api, 'contact.get')).toHaveLength(2));
        expect(asked(api, 'campaign.remove_contacts')).toEqual([recorded('campaign.remove_contacts/exclude-one')]);
        await waitFor(() => expect(membership('Nordic pilot').textContent).toContain('Excluded'));
        expect((await within(panel()).findByRole('status')).textContent).toContain('Excluded from Nordic pilot.');
        expect(within(membership('Nordic pilot')).queryByRole('button', { name: 'Exclude' })).toBeNull();
    });

    it('an Exclude the runtime refuses says so with its message, and the person stays where they are', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('campaign.remove_contacts', 'campaign.remove_contacts/exclude-archived');
        open(api, ingrid.id, 'sequences');
        await within(await shown()).findByText('Nordic pilot', { selector: 'a' });
        fireEvent.click(within(membership('Nordic pilot')).getByRole('button', { name: 'Exclude' }));
        fireEvent.input(
            within(membership('Nordic pilot')).getByRole('textbox', { name: 'Reason (optional, kept in the journal)' }),
            { target: { value: 'Not a fit for the pilot.' } },
        );
        fireEvent.click(within(membership('Nordic pilot')).getByRole('button', { name: 'Exclude from Nordic pilot' }));

        const refused = await within(membership('Nordic pilot')).findByRole('alert');
        expect(asked(api, 'campaign.remove_contacts')).toEqual([recorded('campaign.remove_contacts/exclude-archived')]);
        expect(refused.textContent).toContain(
            "Campaign 'cmp_2' is archived; only journal entries can still be appended.",
        );
        expect(membership('Nordic pilot').textContent).toContain('Enrolled');
        expect(asked(api, 'contact.get')).toHaveLength(1);
    });

    it('Cancel leaves the person where they are', async () => {
        const api = people().answer('contact.get', 'contact.get/person-panel');
        open(api, ingrid.id, 'sequences');
        await within(await shown()).findByText('Nordic pilot', { selector: 'a' });
        fireEvent.click(within(membership('Nordic pilot')).getByRole('button', { name: 'Exclude' }));
        fireEvent.click(within(membership('Nordic pilot')).getByRole('button', { name: 'Cancel' }));
        expect(within(membership('Nordic pilot')).getByRole('button', { name: 'Exclude' })).toBeTruthy();
        expect(asked(api, 'campaign.remove_contacts')).toHaveLength(0);
    });
});

describe('Move', () => {
    /** A campaign's row on Sequences. */
    const membership = (name: string) =>
        within(panel()).getByText(name, { selector: 'a' }).closest('li') as HTMLElement;

    /** Opens Move on the Nordic pilot row, picks the campaign named and gives the reason the recordings carry. */
    async function moveTo(name: string) {
        const row = membership('Nordic pilot');
        fireEvent.click(within(row).getByRole('button', { name: 'Move' }));
        fireEvent.click(within(row).getByRole('button', { name: /Choose a campaign/ }));
        fireEvent.click(await within(row).findByRole('option', { name: new RegExp(name) }));
        fireEvent.input(within(row).getByRole('textbox', { name: 'Reason (optional, kept in the journal)' }), {
            target: { value: 'Moved to the follow-ups.' },
        });
        return row;
    }

    it('adds the person to the campaign chosen, then takes them out of the one they were in', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel', 'contact.get/person-after-move')
            .answer('campaign.list', 'campaign.list/move-targets')
            .answer('campaign.add_contacts', 'campaign.add_contacts/move-in')
            .answer('campaign.remove_contacts', 'campaign.remove_contacts/move-out');
        open(api, ingrid.id, 'sequences');
        await within(await shown()).findByText('Nordic pilot', { selector: 'a' });
        // A campaign the person already left is not moved out of.
        expect(within(membership('Spring webinar')).queryByRole('button', { name: 'Move' })).toBeNull();

        // Moving into the campaign they are moving out of is nothing to do.
        let row = await moveTo('Nordic pilot');
        expect(within(row).getByRole<HTMLButtonElement>('button', { name: 'Move to Nordic pilot' }).disabled).toBe(
            true,
        );
        fireEvent.click(within(row).getByRole('button', { name: 'Cancel' }));

        row = await moveTo('Q4 follow-ups');
        fireEvent.click(within(row).getByRole('button', { name: 'Move to Q4 follow-ups' }));

        await waitFor(() => expect(asked(api, 'contact.get')).toHaveLength(2));
        expect(asked(api, 'campaign.add_contacts')).toEqual([recorded('campaign.add_contacts/move-in')]);
        expect(asked(api, 'campaign.remove_contacts')).toEqual([recorded('campaign.remove_contacts/move-out')]);
        const order = api.calls
            .map((call) => call.operation)
            .filter((op) => op.startsWith('campaign.') && op !== 'campaign.list' && op !== 'campaign.summary');
        expect(order).toEqual(['campaign.add_contacts', 'campaign.remove_contacts']);
        await waitFor(() => expect(membership('Nordic pilot').textContent).toContain('Excluded'));
        expect(membership('Q4 follow-ups').textContent).toContain('Enrolled');
        expect((await within(panel()).findByRole('status')).textContent).toContain(
            'Moved from Nordic pilot to Q4 follow-ups.',
        );
    });

    it('says a move left half done, that the person is in both campaigns, and offers the remove again', async () => {
        const api = people()
            .answer(
                'contact.get',
                'contact.get/person-panel',
                'contact.get/person-after-half-move',
                'contact.get/person-after-move',
            )
            .answer('campaign.list', 'campaign.list/move-targets')
            .answer('campaign.add_contacts', 'campaign.add_contacts/move-in')
            .answer(
                'campaign.remove_contacts',
                'campaign.remove_contacts/move-out-archived',
                'campaign.remove_contacts/move-out',
            );
        open(api, ingrid.id, 'sequences');
        await within(await shown()).findByText('Nordic pilot', { selector: 'a' });
        const row = await moveTo('Q4 follow-ups');
        fireEvent.click(within(row).getByRole('button', { name: 'Move to Q4 follow-ups' }));

        const half = await within(panel()).findByRole('alert');
        expect(half.textContent).toContain('Ingrid Solberg is now in both Nordic pilot and Q4 follow-ups.');
        expect(half.textContent).toContain('is archived');
        await waitFor(() => expect(membership('Q4 follow-ups').textContent).toContain('Enrolled'));
        expect(membership('Nordic pilot').textContent).toContain('Enrolled');
        // Nothing was taken back: the add stands.
        expect(asked(api, 'campaign.remove_contacts')).toHaveLength(1);

        fireEvent.click(within(half).getByRole('button', { name: 'Take them out of Nordic pilot again' }));
        await waitFor(() => expect(asked(api, 'contact.get')).toHaveLength(3));
        expect(asked(api, 'campaign.remove_contacts')).toEqual([
            recorded('campaign.remove_contacts/move-out'),
            recorded('campaign.remove_contacts/move-out'),
        ]);
        expect(asked(api, 'campaign.add_contacts')).toHaveLength(1);
        await waitFor(() => expect(membership('Nordic pilot').textContent).toContain('Excluded'));
        expect(within(panel()).queryByRole('alert')).toBeNull();
        expect((await within(panel()).findByRole('status')).textContent).toContain(
            'Moved from Nordic pilot to Q4 follow-ups.',
        );
    });

    it('says an add refused for the person alone, that nothing changed, and takes nobody out', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('campaign.list', 'campaign.list/move-targets')
            .answer('campaign.add_contacts', 'campaign.add_contacts/move-in-rejected');
        open(api, ingrid.id, 'sequences');
        await within(await shown()).findByText('Nordic pilot', { selector: 'a' });
        const row = await moveTo('Q4 follow-ups');
        fireEvent.click(within(row).getByRole('button', { name: 'Move to Q4 follow-ups' }));

        const refused = await within(row).findByRole('alert');
        expect(asked(api, 'campaign.add_contacts')).toEqual([recorded('campaign.add_contacts/move-in-rejected')]);
        expect(refused.textContent).toBe(
            "Adding to Q4 follow-ups was refused: Contact 'cnt_1' is archived. Nothing changed.",
        );
        expect(asked(api, 'campaign.remove_contacts')).toHaveLength(0);
        expect(asked(api, 'contact.get')).toHaveLength(1);
    });

    it('says an add the runtime refused as a failed call, that nothing changed, and takes nobody out', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-after-half-move')
            .answer('campaign.list', 'campaign.list/move-targets')
            .answer('campaign.add_contacts', 'campaign.add_contacts/archived-campaign');
        open(api, ingrid.id, 'sequences');
        await within(await shown()).findByText('Q4 follow-ups', { selector: 'a' });
        const row = membership('Q4 follow-ups');
        fireEvent.click(within(row).getByRole('button', { name: 'Move' }));
        fireEvent.click(within(row).getByRole('button', { name: /Choose a campaign/ }));
        fireEvent.click(await within(row).findByRole('option', { name: /Nordic pilot/ }));
        fireEvent.click(within(row).getByRole('button', { name: 'Move to Nordic pilot' }));

        const refused = await within(row).findByRole('alert');
        expect(asked(api, 'campaign.add_contacts')).toEqual([recorded('campaign.add_contacts/archived-campaign')]);
        expect(refused.textContent).toBe(
            "Adding to Nordic pilot was refused: Campaign 'cmp_2' is archived; only journal entries can still be appended. Nothing changed.",
        );
        expect(asked(api, 'campaign.remove_contacts')).toHaveLength(0);
        expect(asked(api, 'contact.get')).toHaveLength(1);
    });
});

describe('Research', () => {
    const findings = (api: FakeApi) =>
        api
            .answer('workitem.list', 'workitem.list/researcher-findings')
            .answer('workitem.get', 'workitem.get/researcher-finding');

    it('shows what the researcher found about the person, as text, with the campaign and the day it was found', async () => {
        const api = findings(people().answer('contact.get', 'contact.get/person-panel'));
        open(api, ingrid.id, 'research');
        const found = await within(await shown()).findByText('Fjordline opened an office in Bergen in September.');
        expect(asked(api, 'workitem.list')).toEqual([recorded('workitem.list/researcher-findings')]);
        expect(asked(api, 'workitem.get')).toEqual([recorded('workitem.get/researcher-finding')]);
        const finding = found.closest('article') as HTMLElement;
        expect(finding.textContent).toContain('Nordic pilot');
        // What a role wrote is drawn as the text it is: markup in it stays characters on the page.
        expect(finding.textContent).toContain('<b>Hiring</b> two account executives; the careers page says so.');
        expect(finding.querySelector('b')).toBeNull();
    });

    it('draws the findings that were read, with the first that could not be read said above them', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('workitem.list', 'workitem.list/researcher-findings-two')
            .answer('workitem.get', 'workitem.get/researcher-finding-second');
        api.unreachable('workitem.get', (body) => (body as { work_item_id: string }).work_item_id === 'wi_1');
        open(api, ingrid.id, 'research');
        const read = await within(await shown()).findByText('Fjordline runs ferries between Bergen and Stavanger.');
        expect(asked(api, 'workitem.list')).toEqual([recorded('workitem.list/researcher-findings-two')]);
        expect(asked(api, 'workitem.get')).toEqual([
            recorded('workitem.get/researcher-finding'),
            recorded('workitem.get/researcher-finding-second'),
        ]);
        expect(within(panel()).queryByText('Fjordline opened an office in Bergen in September.')).toBeNull();
        const failed = within(panel()).getByRole('alert');
        expect(failed.textContent).toContain('Failed to fetch');
        expect(failed.compareDocumentPosition(read) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('when no finding could be read says the failure, and not that nothing was found', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('workitem.list', 'workitem.list/researcher-findings-two');
        api.unreachable('workitem.get');
        open(api, ingrid.id, 'research');
        const failed = await within(await shown()).findByRole('alert');
        expect(failed.textContent).toContain('Failed to fetch');
        expect(asked(api, 'workitem.get')).toEqual([
            recorded('workitem.get/researcher-finding'),
            recorded('workitem.get/researcher-finding-second'),
        ]);
        expect(within(panel()).queryByText('Nothing found about this person yet.')).toBeNull();
    });

    it('asks the researcher about the person in the campaign the control names, with the question given', async () => {
        const api = findings(people().answer('contact.get', 'contact.get/person-panel')).answer(
            'workitem.create',
            'workitem.create/researcher-asked',
        );
        open(api, ingrid.id, 'research');
        await within(await shown()).findByText('Fjordline opened an office in Bergen in September.');
        const ask = within(panel()).getByRole('button', { name: 'Ask the researcher' });
        expect(ask.closest('form')?.textContent).toContain('in Nordic pilot');
        fireEvent.input(within(panel()).getByRole('textbox', { name: 'Question (optional)' }), {
            target: { value: 'Who owns the Bergen office?' },
        });
        fireEvent.click(ask);

        const said = await within(panel()).findByRole('status');
        expect(asked(api, 'workitem.create')).toEqual([recorded('workitem.create/researcher-asked')]);
        expect(said.textContent).toContain('Asked the researcher in Nordic pilot.');
        expect(within(said).getByRole('link', { name: 'Open the work item' }).getAttribute('href')).toBe(
            '#/workitems/planned/wi_1',
        );
    });

    it('an ask the runtime refuses is said with its message, and nothing is linked', async () => {
        const api = findings(people().answer('contact.get', 'contact.get/person-panel')).answer(
            'workitem.create',
            'workitem.create/researcher-archived',
        );
        open(api, ingrid.id, 'research');
        await within(await shown()).findByText('Fjordline opened an office in Bergen in September.');
        fireEvent.click(within(panel()).getByRole('button', { name: 'Ask the researcher' }));

        const refused = await within(panel()).findByRole('alert');
        expect(asked(api, 'workitem.create')).toEqual([recorded('workitem.create/researcher-archived')]);
        expect(refused.textContent).toBe(
            "Asking the researcher was refused: Campaign 'cmp_2' is archived; only journal entries can still be appended.",
        );
        expect(within(panel()).queryByRole('link', { name: 'Open the work item' })).toBeNull();
    });

    it('asks without a question, in the campaign the person picks', async () => {
        // The panel opens on Activity, which reads the person's open work and journal, then Research is chosen.
        const api = findings(new FakeApi().answer('contact.get', 'contact.get/person-after-half-move'))
            .answer('workitem.list', 'workitem.list/by-contact-open', 'workitem.list/researcher-findings')
            .answer('journal.list', 'journal.list/by-contact')
            .answer('workitem.create', 'workitem.create/researcher-asked-plain');
        watch(api);
        render(
            <ApiProvider api={api}>
                <PersonPanel contactId={ingrid.id} campaignId="cmp_3" onClose={() => undefined} />
            </ApiProvider>,
        );
        fireEvent.click(within(await shown()).getByRole('tab', { name: 'Research' }));
        await within(panel()).findByText('Fjordline opened an office in Bergen in September.');
        const where = within(panel()).getByRole<HTMLSelectElement>('combobox', {
            name: 'The campaign the researcher works in',
        });
        expect(Array.from(where.options).map((option) => option.textContent)).toEqual([
            'Nordic pilot',
            'Q4 follow-ups',
        ]);
        expect(where.value).toBe('cmp_3');
        fireEvent.change(where, { target: { value: 'cmp_2' } });
        fireEvent.click(within(panel()).getByRole('button', { name: 'Ask the researcher' }));

        expect((await within(panel()).findByRole('status')).textContent).toContain(
            'Asked the researcher in Nordic pilot.',
        );
        expect(asked(api, 'workitem.create')).toEqual([recorded('workitem.create/researcher-asked-plain')]);
    });

    it('for a person in no campaign shows what was found and offers no ask', async () => {
        const api = findings(people().answer('contact.get', 'contact.get/person-after-exclude'));
        open(api, ingrid.id, 'research');
        await within(await shown()).findByText('Fjordline opened an office in Bergen in September.');
        expect(within(panel()).queryByRole('button', { name: 'Ask the researcher' })).toBeNull();
        expect(within(panel()).queryByRole('textbox')).toBeNull();
    });
});

describe('Do not contact, for the whole person', () => {
    const toggle = () => within(panel()).getByRole<HTMLInputElement>('switch', { name: 'Do not contact' });
    const whole = () => toggle().closest('.whole') as HTMLElement;

    it('is on when every address of the person is on the register', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer(
                'suppression.list',
                'suppression.list/person-email-listed',
                'suppression.list/person-linkedin-listed',
            );
        open(api, ingrid.id, 'details');
        await waitFor(() => expect(toggle().checked).toBe(true));
        expect(whole().textContent).not.toContain('On for');
        expect(whole().textContent).toContain("Jason's own register, not the provider's list.");
    });

    it('is off when none is', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('suppression.list', ...registerClear);
        open(api, ingrid.id, 'details');
        await waitFor(() => expect(toggle().checked).toBe(false));
        expect(whole().textContent).not.toContain('On for');
    });

    it('reads as not on when only some are, and says how many', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer(
                'suppression.list',
                'suppression.list/person-email-listed',
                'suppression.list/person-linkedin-clear',
            );
        open(api, ingrid.id, 'details');
        await waitFor(() => expect(whole().textContent).toContain('On for 1 of 2 channels'));
        expect(toggle().checked).toBe(false);
    });

    it('turned on from some, puts only the addresses not on the register there, then reads the register again', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer(
                'suppression.list',
                'suppression.list/person-email-listed',
                'suppression.list/person-linkedin-clear',
                'suppression.list/person-linkedin-listed',
            )
            .answer('suppression.add', 'suppression.add/person-linkedin');
        open(api, ingrid.id, 'details');
        await waitFor(() => expect(whole().textContent).toContain('On for 1 of 2 channels'));
        fireEvent.click(toggle());

        await waitFor(() => expect(asked(api, 'suppression.list')).toHaveLength(4));
        expect(asked(api, 'suppression.add')).toEqual([recorded('suppression.add/person-linkedin')]);
        await waitFor(() => expect(toggle().checked).toBe(true));
        expect(whole().textContent).not.toContain('On for');
    });

    it('turned off, asks one reason and takes each address on the register off with it', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer(
                'suppression.list',
                'suppression.list/person-email-listed',
                'suppression.list/person-email-clear',
                'suppression.list/person-linkedin-listed',
                'suppression.list/person-linkedin-clear',
            )
            .answer('suppression.remove', 'suppression.remove/person-email', 'suppression.remove/person-linkedin');
        open(api, ingrid.id, 'details');
        await waitFor(() => expect(toggle().checked).toBe(true));
        fireEvent.click(toggle());
        const confirm = within(whole()).getByRole<HTMLButtonElement>('button', {
            name: 'Take every address off the register',
        });
        expect(confirm.disabled).toBe(true);
        expect(asked(api, 'suppression.remove')).toHaveLength(0);
        fireEvent.input(within(whole()).getByRole('textbox', { name: 'Reason (required, kept in the journal)' }), {
            target: { value: 'Asked to hear from us again.' },
        });
        fireEvent.click(confirm);

        await waitFor(() => expect(asked(api, 'suppression.list')).toHaveLength(4));
        expect(asked(api, 'suppression.remove')).toEqual([
            recorded('suppression.remove/person-email'),
            recorded('suppression.remove/person-linkedin'),
        ]);
        await waitFor(() => expect(toggle().checked).toBe(false));
        expect(whole().textContent).not.toContain('On for');
    });

    it('a call that fails ends where the register stands, said as some, with the error; what went through stands', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer(
                'suppression.list',
                'suppression.list/person-email-listed',
                'suppression.list/person-email-clear',
                'suppression.list/person-linkedin-listed',
            )
            .answer('suppression.remove', 'suppression.remove/person-email');
        open(api, ingrid.id, 'details');
        await waitFor(() => expect(toggle().checked).toBe(true));
        fireEvent.click(toggle());
        fireEvent.input(within(whole()).getByRole('textbox', { name: 'Reason (required, kept in the journal)' }), {
            target: { value: 'Asked to hear from us again.' },
        });
        // The first address is taken off; the runtime is gone before the second.
        const release = api.hold('suppression.remove');
        fireEvent.click(within(whole()).getByRole('button', { name: 'Take every address off the register' }));
        await waitFor(() => expect(asked(api, 'suppression.remove')).toHaveLength(1));
        api.unreachable('suppression.remove');
        release();

        await waitFor(() => expect(whole().textContent).toContain('On for 1 of 2 channels'));
        expect(toggle().checked).toBe(false);
        expect(asked(api, 'suppression.remove')).toHaveLength(2);
        expect(asked(api, 'suppression.remove')[0]).toEqual(recorded('suppression.remove/person-email'));
        expect(within(panel()).getByRole('alert').textContent).toContain('Failed to fetch');
    });
});

describe('An answer that comes after another person was opened', () => {
    const membership = (name: string) =>
        within(panel()).getByText(name, { selector: 'a' }).closest('li') as HTMLElement;
    /** Opens another person in the panel, the way picking them in the list does. */
    function switchTo(id: string, tab: PersonTab) {
        const to = { screen: 'people' as const, id, tab, filters: {} };
        location.hash = href(to);
        route.value = to;
    }
    /** Lets every answer released settle, and whatever it starts be sent. */
    const settled = async () => {
        for (let turn = 0; turn < 5; turn++) await new Promise((resolve) => setTimeout(resolve, 0));
    };
    /** Starts an Exclude from the Nordic pilot for Ingrid, held until the test releases it. */
    async function excludeHeld(api: FakeApi): Promise<() => void> {
        await within(await shown()).findByText('Nordic pilot', { selector: 'a' });
        fireEvent.click(within(membership('Nordic pilot')).getByRole('button', { name: 'Exclude' }));
        fireEvent.input(
            within(membership('Nordic pilot')).getByRole('textbox', { name: 'Reason (optional, kept in the journal)' }),
            { target: { value: 'Not a fit for the pilot.' } },
        );
        const release = api.hold('campaign.remove_contacts');
        fireEvent.click(within(membership('Nordic pilot')).getByRole('button', { name: 'Exclude from Nordic pilot' }));
        await waitFor(() => expect(asked(api, 'campaign.remove_contacts')).toHaveLength(1));
        return release;
    }

    it('to an Exclude leaves the person now on show where they are, and reads nobody again', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel', 'contact.get/no-first-name')
            .answer('suppression.list', 'suppression.list/aguilar-email-clear')
            .answer('campaign.remove_contacts', 'campaign.remove_contacts/exclude-one');
        open(api, ingrid.id, 'sequences');
        const release = await excludeHeld(api);
        switchTo(aguilar.id, 'details');
        await within(panel()).findByText(aguilar.channels[0]!.value);
        await waitFor(() => expect(within(panel()).getByRole('switch', { name: 'Do not contact' })).toBeTruthy());

        release();
        await settled();
        expect(asked(api, 'contact.get')).toEqual([{ contact_id: ingrid.id }, { contact_id: aguilar.id }]);
        expect(within(panel()).getByText(aguilar.channels[0]!.value)).toBeTruthy();
        expect(within(panel()).getByRole('switch', { name: 'Do not contact' })).toBeTruthy();
        expect(within(panel()).queryByRole('status')).toBeNull();
    });

    it("to Do not contact for the whole person says nothing of the earlier person's in the next one's Details", async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel', 'contact.get/no-first-name')
            .answer(
                'suppression.list',
                'suppression.list/person-email-listed',
                'suppression.list/person-linkedin-listed',
                'suppression.list/aguilar-email-clear',
            )
            .answer('suppression.remove', 'suppression.remove/person-email');
        open(api, ingrid.id, 'details');
        const toggle = () => within(panel()).getByRole<HTMLInputElement>('switch', { name: 'Do not contact' });
        await waitFor(() => expect(toggle().checked).toBe(true));
        fireEvent.click(toggle());
        fireEvent.input(within(panel()).getByRole('textbox', { name: 'Reason (required, kept in the journal)' }), {
            target: { value: 'Asked to hear from us again.' },
        });
        const release = api.hold('suppression.remove');
        fireEvent.click(within(panel()).getByRole('button', { name: 'Take every address off the register' }));
        await waitFor(() => expect(asked(api, 'suppression.remove')).toHaveLength(1));
        api.unreachable('suppression.remove');
        switchTo(aguilar.id, 'details');
        await within(panel()).findByText(aguilar.channels[0]!.value);
        await waitFor(() => expect(toggle().checked).toBe(false));

        release();
        await settled();
        expect(asked(api, 'suppression.remove')).toHaveLength(2);
        expect(within(panel()).queryByRole('alert')).toBeNull();
        expect(toggle().checked).toBe(false);
        expect(toggle().disabled).toBe(false);
        expect(
            asked(api, 'suppression.list').filter(
                (body) => 'value' in body && body.value !== aguilar.channels[0]!.value,
            ),
        ).toHaveLength(2);
    });

    it('to an act for a person whose panel was closed reads nobody again', async () => {
        const api = people()
            .answer('contact.get', 'contact.get/person-panel')
            .answer('campaign.remove_contacts', 'campaign.remove_contacts/exclude-one');
        open(api, ingrid.id, 'sequences');
        const release = await excludeHeld(api);
        fireEvent.click(within(panel()).getByRole('button', { name: 'Close' }));
        await waitFor(() => expect(document.querySelector('.person-panel')).toBeNull());

        release();
        await settled();
        expect(asked(api, 'contact.get')).toEqual([{ contact_id: ingrid.id }]);
        expect(store.person.value).toBeNull();
    });
});
