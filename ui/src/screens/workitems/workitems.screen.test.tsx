import { fireEvent, screen, waitFor, within } from '@testing-library/preact';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { fixtures, type FixtureName } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
import { href, route, type Route, type Tab, type WorkItemFilters } from '../../router';

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

function fake(...counts: FixtureName[]) {
    return new FakeApi()
        .answer('campaign.list', 'campaign.list/first-page')
        .answer('contact.list', 'contact.list/first-page')
        .answer('role.list', 'role.list/all')
        .answer(
            'workitem.count',
            ...(counts.length > 0
                ? counts
                : (['workitem.count/now', 'workitem.count/planned', 'workitem.count/done'] as const)),
        );
}

/** Moves the way an address does, so a change event still on its way from an earlier move agrees with this one. */
function go(to: Route) {
    location.hash = href(to);
    route.value = to;
}

/** Opens the screen the way an address does: renderApp clears the address, and its late change event must agree with ours. */
function open(api: FakeApi, tab: Tab, filters: WorkItemFilters = {}, id?: string) {
    renderApp(api);
    const to = id ? { screen: 'workitems' as const, tab, filters, id } : { screen: 'workitems' as const, tab, filters };
    location.hash = href(to);
    route.value = to;
}

const sentLists = (api: FakeApi) =>
    api.calls.filter((c) => c.operation === 'workitem.list').map((c) => c.body as Record<string, unknown>);
const sentCounts = (api: FakeApi) =>
    api.calls.filter((c) => c.operation === 'workitem.count').map((c) => c.body as Record<string, unknown>);

describe('Work items', () => {
    it('Now lists running and waiting work by name, with three counts', async () => {
        const api = fake().answer('workitem.list', 'workitem.list/now');
        open(api, 'now');

        await waitFor(() => expect(screen.getByText('Running')).toBeTruthy());
        expect(screen.getByText('Waiting for you')).toBeTruthy();
        expect(screen.getAllByText(/Grace Hopper/).length).toBeGreaterThan(0);
        expect(screen.getAllByText('Autumn outreach').length).toBeGreaterThan(0);
        expect(screen.getByText('since 08:55')).toBeTruthy();
        await waitFor(() => expect(screen.getByRole('button', { name: /^Now\s*3$/ })).toBeTruthy());
        expect(screen.getByRole('button', { name: /^Planned\s*5$/ })).toBeTruthy();
        expect(screen.getByRole('button', { name: /^Done\s*4$/ })).toBeTruthy();

        const recorded = fixtures['workitem.list/now'].request;
        expect(sentLists(api)[0]).toMatchObject({ status: recorded.status, order: recorded.order });
        expect(sentCounts(api)).toEqual([
            fixtures['workitem.count/now'].request,
            fixtures['workitem.count/planned'].request,
            fixtures['workitem.count/done'].request,
        ]);
    });

    it('Planned groups by due day with undated work last, says when each runs, and scrolls with the cursor', async () => {
        // A browser's observer, which happy-dom lacks: the page must watch the sentinel that appears with the cursor.
        const watched: { node: Element; scrolledTo: () => void }[] = [];
        vi.stubGlobal(
            'IntersectionObserver',
            class {
                constructor(private readonly seen: (entries: { isIntersecting: boolean }[]) => void) {}
                observe(node: Element) {
                    watched.push({ node, scrolledTo: () => this.seen([{ isIntersecting: true }]) });
                }
                unobserve() {}
                disconnect() {}
            },
        );
        const api = fake().answer('workitem.list', 'workitem.list/planned', 'workitem.list/planned-next');
        open(api, 'planned');

        await waitFor(() => expect(screen.getByText('Today')).toBeTruthy());
        expect(screen.getByText(/retry after 12:15/)).toBeTruthy();
        const sentinel = screen.getByText('loading…');
        await waitFor(() => expect(watched.some((w) => w.node === sentinel)).toBe(true));
        watched.find((w) => w.node === sentinel)!.scrolledTo();
        await waitFor(() => expect(screen.getByText('No date')).toBeTruthy());
        expect(screen.getByText(/not before 16:30/)).toBeTruthy();
        const groups = [...document.querySelectorAll('.rows .group')].map((g) => g.textContent);
        expect(groups).toEqual(['Today', 'Tomorrow', 'No date']);
        expect(sentLists(api)[1]).toMatchObject({ cursor: fixtures['workitem.list/planned-next'].request.cursor });
    });

    it('Done shows outcomes newest first, and the Failures chip asks for failed only', async () => {
        const api = fake().answer('workitem.list', 'workitem.list/done', 'workitem.list/failed');
        open(api, 'done');

        await waitFor(() => expect(screen.getByText('failed: reply.refused')).toBeTruthy());
        fireEvent.click(screen.getByRole('button', { name: 'Failures' }));

        await waitFor(() => expect(sentLists(api)).toHaveLength(2));
        expect(sentLists(api)[1]).toMatchObject({ status: ['failed'], order: 'finished' });
        expect(sentLists(api)[1]).not.toHaveProperty('cursor');
        expect(route.value).toMatchObject({ screen: 'workitems', tab: 'done', filters: { failed: true } });

        // One day narrows the tab by the time it is ordered by; Now has no such time and is counted as before.
        fireEvent.input(screen.getByLabelText('Finished on'), { target: { value: '2026-10-16' } });
        await waitFor(() =>
            expect(sentLists(api).at(-1)).toMatchObject({
                order: 'finished',
                since: '2026-10-16T00:00:00.000Z',
                until: '2026-10-17T00:00:00.000Z',
            }),
        );
        expect(route.value).toMatchObject({ filters: { failed: true, on: '2026-10-16' } });
        const counted = sentCounts(api).slice(-3);
        expect(counted[0]).not.toHaveProperty('since');
        expect(counted[1]).toMatchObject({ order: 'due', since: '2026-10-16T00:00:00.000Z' });
        expect(counted[2]).toMatchObject({ order: 'finished', until: '2026-10-17T00:00:00.000Z' });

        fireEvent.click(screen.getByRole('button', { name: 'Next day' }));
        await waitFor(() => expect(route.value).toMatchObject({ filters: { on: '2026-10-17' } }));
        fireEvent.click(screen.getByRole('button', { name: 'Any day' }));
        await waitFor(() => expect(route.value).not.toMatchObject({ filters: { on: '2026-10-17' } }));
        expect(sentLists(api).at(-1)).not.toHaveProperty('since');
    });

    it('campaign, kind and role filters start from the first page, recount, and say when nothing matches', async () => {
        const api = fake(
            'workitem.count/planned',
            'workitem.count/planned',
            'workitem.count/planned',
            'workitem.count/zero',
        )
            .answer('workitem.list', 'workitem.list/planned', 'workitem.list/planned-next', 'workitem.list/empty')
            .answer('campaign.list', 'campaign.list/first-page', 'campaign.list/query');
        open(api, 'planned');
        await waitFor(() => screen.getByText('loading…'));
        fireEvent.click(screen.getByText('loading…'));
        await waitFor(() => expect(sentLists(api)).toHaveLength(2));

        const smb = fixtures['campaign.list/first-page'].response.items.find((c) => c.name === 'SMB test')!;
        fireEvent.click(screen.getByRole('button', { name: 'All campaigns' }));
        await waitFor(() => expect(api.sent('campaign.list')).toEqual({ limit: 10 }));
        const find = screen.getByPlaceholderText('Find a campaign');
        fireEvent.input(find, { target: { value: 'smb' } });
        await waitFor(() => expect(api.sent('campaign.list')).toEqual({ query: 'smb', limit: 10 }));
        // Typed, the first match is the one Enter picks.
        await screen.findByRole('option', { name: 'SMB test' });
        fireEvent.keyDown(find, { key: 'Enter' });
        await waitFor(() => expect(sentLists(api)).toHaveLength(3));
        expect(sentLists(api)[2]).toMatchObject({ campaign_id: smb.id });
        expect(sentLists(api)[2]).not.toHaveProperty('cursor');
        expect(sentCounts(api)).toHaveLength(6);

        fireEvent.click(screen.getByRole('button', { name: 'Role work' }));
        await waitFor(() => screen.getByRole('button', { name: 'Any role' }));
        // The arrows move through the roles from the one shown, and Enter picks.
        fireEvent.click(screen.getByRole('button', { name: 'Any role' }));
        const roles = screen.getByRole('listbox');
        fireEvent.keyDown(roles, { key: 'ArrowDown' });
        fireEvent.keyDown(roles, { key: 'ArrowDown' });
        fireEvent.keyDown(roles, { key: 'ArrowUp' });
        fireEvent.keyDown(roles, { key: 'Enter' });
        await waitFor(() => expect(sentLists(api).at(-1)).toMatchObject({ kind: 'ai_role', role: 'manager' }));
        await waitFor(() => expect(screen.getByText('Nothing matches these filters.')).toBeTruthy());

        fireEvent.click(screen.getByRole('button', { name: 'Provider operations' }));
        await waitFor(() => expect(sentLists(api).at(-1)).toMatchObject({ kind: 'provider_op' }));
        expect(sentLists(api).at(-1)).not.toHaveProperty('role');
        expect(screen.queryByRole('button', { name: 'Any role' })).toBeNull();
    });

    it('the person filter searches, and the address carries every filter', async () => {
        const found = fixtures['contact.list/query-email'].response.items[0]!;
        const api = fake()
            .answer('workitem.list', 'workitem.list/now')
            .answer('contact.list', 'contact.list/first-page', 'contact.list/query-email');
        open(api, 'now');
        await waitFor(() => screen.getByText('Running'));

        fireEvent.click(screen.getByRole('button', { name: 'Everybody' }));
        await waitFor(() => expect(api.sent('contact.list')).toEqual({ limit: 10 }));
        expect(await screen.findAllByRole('option')).toHaveLength(
            1 + fixtures['contact.list/first-page'].response.items.length,
        );
        // Escape closes the list and gives the focus back to the picker.
        fireEvent.keyDown(screen.getByPlaceholderText('Find a person'), { key: 'Escape' });
        await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull());
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Everybody' }));
        fireEvent.click(screen.getByRole('button', { name: 'Everybody' }));
        fireEvent.input(screen.getByPlaceholderText('Find a person'), { target: { value: 'aguilar@' } });
        await waitFor(() => expect(api.sent('contact.list')).toEqual({ query: 'aguilar@', limit: 10 }));
        fireEvent.click(await screen.findByRole('option', { name: /Tomás Aguilar/ }));

        await waitFor(() => expect(sentLists(api).at(-1)).toMatchObject({ contact_id: found.id }));
        expect(route.value).toMatchObject({ filters: { contact: found.id } });
        expect(screen.getByRole('button', { name: 'Tomás Aguilar' })).toBeTruthy();
    });

    it('an address names its person and campaign with one read each, and drops a campaign that no longer exists', async () => {
        const ada = fixtures['contact.get/one'].response;
        const gone = fixtures['campaign.get/unknown'].request.campaign_id;
        const api = fake()
            .answer('workitem.list', 'workitem.list/now')
            .answer('contact.get', 'contact.get/one')
            .answer('campaign.get', 'campaign.get/unknown');
        open(api, 'now', { contact: ada.id, campaign: gone });

        await waitFor(() => expect(api.sent('contact.get')).toEqual({ contact_id: ada.id }));
        expect(api.sent('campaign.get')).toEqual({ campaign_id: gone });
        await waitFor(() => expect(screen.getByRole('button', { name: 'Ada Lovelace' })).toBeTruthy());
        await waitFor(() => expect(route.value).not.toMatchObject({ filters: { campaign: gone } }));
        expect(route.value).toMatchObject({ filters: { contact: ada.id } });
        expect(screen.getByText('The campaign in this address no longer exists; its filter was dropped.')).toBeTruthy();
    });

    it('a campaign the address names is shown by its name', async () => {
        const smb = fixtures['campaign.get/one'].response;
        const api = fake().answer('workitem.list', 'workitem.list/now').answer('campaign.get', 'campaign.get/one');
        open(api, 'now', { campaign: smb.id });

        await waitFor(() => expect(screen.getByRole('button', { name: 'SMB test' })).toBeTruthy());
        await waitFor(() => expect(sentLists(api)[0]).toMatchObject({ campaign_id: smb.id }));
    });
});

describe('Work items: the panel', () => {
    const withoutActor = (request: Record<string, unknown>) => {
        const copy = { ...request };
        delete copy['actor'];
        return copy;
    };

    it('an open item saves its dates with the recorded request, and one that finished meanwhile reloads and says so', async () => {
        const item = fixtures['workitem.get/open'].response;
        const api = fake()
            .answer('workitem.list', 'workitem.list/planned')
            .answer('workitem.get', 'workitem.get/open')
            .answer('journal.list', 'journal.list/by-work-item')
            .answer('workitem.update', 'workitem.update/dates', 'workitem.update/terminal');
        open(api, 'planned', {}, item.id);

        await waitFor(() => screen.getByRole('button', { name: 'Save' }));
        expect(document.querySelector('.history .entry')?.textContent).toMatch(/cancelled/);
        fireEvent.input(screen.getByLabelText('Not before'), { target: { value: '2026-10-16T11:00' } });
        fireEvent.input(screen.getByLabelText('Due'), { target: { value: '2026-10-16T15:00' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => expect(api.sent('workitem.update')).toBeTruthy());
        expect(api.sent('workitem.update')).toEqual(withoutActor(fixtures['workitem.update/dates'].request));
        await waitFor(() => expect((screen.getByLabelText('Due') as HTMLInputElement).value).toBe('2026-10-16T15:00'));

        fireEvent.input(screen.getByLabelText('Priority'), { target: { value: '5' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));
        await waitFor(() => expect(screen.getByText('This item finished meanwhile.')).toBeTruthy());
        expect(api.calls.filter((c) => c.operation === 'workitem.get').length).toBeGreaterThanOrEqual(2);
    });

    it('cancels with a reason after saying what cancelling does, then reloads the list and the counts', async () => {
        const item = fixtures['workitem.get/open'].response;
        const api = fake()
            .answer('workitem.list', 'workitem.list/planned')
            .answer('workitem.get', 'workitem.get/open')
            .answer('journal.list', 'journal.list/by-work-item')
            .answer('workitem.cancel', 'workitem.cancel/reason');
        open(api, 'planned', {}, item.id);

        await waitFor(() => screen.getByRole('button', { name: 'Cancel work' }));
        const lists = sentLists(api).length;
        fireEvent.click(screen.getByRole('button', { name: 'Cancel work' }));
        const dialog = await screen.findByRole('dialog');
        expect(within(dialog).getByText('Nothing has reached the provider.')).toBeTruthy();
        fireEvent.input(within(dialog).getByLabelText(/Reason/), { target: { value: 'Not this week.' } });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel it' }));

        await waitFor(() =>
            expect(api.sent('workitem.cancel')).toEqual(withoutActor(fixtures['workitem.cancel/reason'].request)),
        );
        await waitFor(() => expect(sentLists(api).length).toBeGreaterThan(lists));
        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('a failed item shows what was recorded and nothing composed; a succeeded one says how many ran', async () => {
        const failed = fixtures['workitem.get/failed'].response;
        const api = fake()
            .answer('workitem.list', 'workitem.list/done')
            .answer('workitem.get', 'workitem.get/failed', 'workitem.get/succeeded')
            .answer('journal.list', 'journal.list/by-work-item');
        open(api, 'done', {}, failed.id);

        await waitFor(() => expect(screen.getByText('2 failed attempts, limit 3')).toBeTruthy());
        expect(screen.getAllByText('reply.refused').length).toBeGreaterThan(0);
        expect(screen.getAllByText('Invalid data: first name is required').length).toBeGreaterThan(0);
        expect(screen.getByText('permanent')).toBeTruthy();
        expect(screen.getAllByText(/reply 0\.3\.1/).length).toBe(2);
        expect(screen.queryByText(/Run again/)).toBeNull();
        expect(screen.queryByText(/^Fix/)).toBeNull();
        expect(screen.getByText('Input')).toBeTruthy();

        go({ screen: 'workitems', tab: 'done', filters: {}, id: fixtures['workitem.get/succeeded'].response.id });
        await waitFor(() => expect(screen.getByText(/1 run, none failed/)).toBeTruthy());
    });

    it('a waiting item links to its approval and is approved or rejected from the panel; an expired one takes a new due date', async () => {
        const awaiting = fixtures['workitem.get/awaiting'].response;
        const approval = fixtures['approval.list/by-work-item'].response.items[0]!;
        const api = fake()
            .answer('workitem.list', 'workitem.list/now')
            .answer(
                'workitem.get',
                'workitem.get/awaiting',
                'workitem.get/awaiting',
                'workitem.get/awaiting',
                'workitem.get/expired',
            )
            .answer('journal.list', 'journal.list/by-work-item')
            .answer('approval.list', 'approval.list/by-work-item')
            .answer('approval.approve', 'approval.approve/approved')
            .answer('approval.reject', 'approval.reject/rejected');
        open(api, 'now', {}, awaiting.id);

        const link = await screen.findByRole('link', { name: /Waiting for your approval/ });
        expect(link.getAttribute('href')).toBe(`#/attention/approvals/${approval.id}`);
        expect(api.sent('approval.list')).toEqual(fixtures['approval.list/by-work-item'].request);

        // The approval is decided right here, as on Attention: the item is read again and the lists reloaded.
        const reads = api.calls.filter((c) => c.operation === 'workitem.get').length;
        fireEvent.click(screen.getByRole('button', { name: 'Approve' }));
        await waitFor(() => expect(api.sent('approval.approve')).toEqual({ approval_id: approval.id }));
        await waitFor(() =>
            expect(api.calls.filter((c) => c.operation === 'workitem.get').length).toBeGreaterThan(reads),
        );

        fireEvent.click(await screen.findByRole('button', { name: 'Reject' }));
        const dialog = await screen.findByRole('dialog');
        fireEvent.input(within(dialog).getByLabelText(/Reason/), { target: { value: 'Not this person.' } });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Reject' }));
        await waitFor(() =>
            expect(api.sent('approval.reject')).toEqual({ approval_id: approval.id, reason: 'Not this person.' }),
        );

        go({ screen: 'workitems', tab: 'done', filters: {}, id: fixtures['workitem.get/expired'].response.id });
        await waitFor(() => expect(screen.getByText('A future due date puts this back in the queue.')).toBeTruthy());
        expect(screen.getByLabelText('Due')).toBeTruthy();
    });
});
