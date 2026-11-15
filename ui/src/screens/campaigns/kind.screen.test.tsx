import { screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { fixtures } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
import { firstCampaignTab, href, route, type Route } from '../../router';

afterEach(() => {
    firstCampaignTab.value = 'sequence';
});

const table: Extract<Route, { screen: 'campaigns' }> = {
    screen: 'campaigns',
    tab: 'settings',
    view: 'summary',
    period: '7d',
    filters: {},
};

/** The agent's page, opened by its address, with the lists and counts of its table answered as the table's tests do. */
function agent(
    get:
        | 'campaign.get/details'
        | 'campaign.get/started'
        | 'campaign.get/inbox-quiet'
        | 'campaign.get/making'
        | 'campaign.get/unlinked'
        | 'campaign.get/summer',
) {
    const id = fixtures[get].request.campaign_id;
    const api = new FakeApi()
        .answer('campaign.list', 'campaign.list/screen')
        .answerAnyRequest('campaign.summary', 'campaign.summary/all')
        .answer('campaign.get', get)
        .answerAnyRequest(
            'workitem.list',
            get === 'campaign.get/making' ? 'workitem.list/creating-open' : 'workitem.list/creating-none',
        );
    const to = { ...table, id };
    renderApp(api);
    location.hash = href(to);
    route.value = to;
    return api;
}

const header = async (name: string) =>
    (await screen.findByRole('heading', { level: 2, name })).closest('.panel-header') as HTMLElement;
const sent = (api: FakeApi) => api.calls.filter((c) => c.operation === 'workitem.list').map((c) => c.body);
const firstTab = () => screen.getAllByRole('tab')[0]!.textContent;
const kind = (head: HTMLElement) => head.querySelector('.kind')?.textContent ?? null;

describe('the kind of an agent in its header', () => {
    it('says Sequence beside the state where a provider campaign is pinned, asks nothing more, and names the first tab Sequence', async () => {
        const api = agent('campaign.get/details');
        const head = await header('Spring');

        expect(kind(head)).toBe('Sequence');
        expect(head.textContent).toContain('reply campaign 4711');
        expect(firstTab()).toBe('Sequence');
        expect(sent(api)).toEqual([]);
    });

    it('says Adaptive for a started agent with no pin and no campaign.create unfinished, after one read of one page, and names the first tab Paths', async () => {
        const api = agent('campaign.get/started');
        const head = await header('Running');

        await waitFor(() => expect(kind(head)).toBe('Adaptive'));
        expect(firstTab()).toBe('Paths');
        expect(sent(api)).toEqual([fixtures['workitem.list/creating-none'].request]);
    });

    it('says nothing while a campaign.create of the agent is unfinished: a provider campaign is being made', async () => {
        const api = agent('campaign.get/making');
        const head = await header('Making');

        await waitFor(() => expect(sent(api)).toEqual([fixtures['workitem.list/creating-open'].request]));
        await waitFor(() => expect(firstTab()).toBe('Paths'));
        expect(kind(head)).toBeNull();
    });

    it('says nothing for a draft with no pin, and does not read for it', async () => {
        const api = agent('campaign.get/unlinked');
        const head = await header('Autumn');

        expect(kind(head)).toBeNull();
        expect(firstTab()).toBe('Paths');
        expect(sent(api)).toEqual([]);
    });

    it('says nothing when the one read fails, and shows no failure for it', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answerAnyRequest('campaign.summary', 'campaign.summary/all')
            .answer('campaign.get', 'campaign.get/started');
        api.unreachable('workitem.list');
        const to = { ...table, id: fixtures['campaign.get/started'].request.campaign_id };
        renderApp(api);
        location.hash = href(to);
        route.value = to;
        const head = await header('Running');

        await waitFor(() => expect(sent(api)).toHaveLength(1));
        expect(kind(head)).toBeNull();
        expect(screen.queryByText(/Failed to fetch/)).toBeNull();
    });

    it('says Adaptive for a paused agent with no pin, as for an active one', async () => {
        const api = agent('campaign.get/inbox-quiet');
        const head = await header('Re-engage Q2 no-replies');

        await waitFor(() => expect(kind(head)).toBe('Adaptive'));
        expect(firstTab()).toBe('Paths');
        expect(sent(api)).toHaveLength(1);
    });

    it('does not carry the first agent answer to the next: after Running reads none, Making says nothing while its own read is open', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answerAnyRequest('campaign.summary', 'campaign.summary/all')
            .answer('campaign.get', 'campaign.get/started', 'campaign.get/making')
            .answerAnyRequest('workitem.list', (body) =>
                (body as { campaign_id?: string }).campaign_id === fixtures['campaign.get/making'].request.campaign_id
                    ? 'workitem.list/creating-open'
                    : 'workitem.list/creating-none',
            );
        const first = { ...table, id: fixtures['campaign.get/started'].request.campaign_id };
        renderApp(api);
        location.hash = href(first);
        route.value = first;
        const running = await header('Running');
        await waitFor(() => expect(kind(running)).toBe('Adaptive'));

        const release = api.hold('workitem.list');
        const second = { ...table, id: fixtures['campaign.get/making'].request.campaign_id };
        location.hash = href(second);
        route.value = second;
        const making = await header('Making');
        expect(kind(making)).toBeNull();
        release();

        await waitFor(() => expect(sent(api)).toHaveLength(2));
        expect(kind(making)).toBeNull();
    });

    it('says Adaptive when a full first page holds no campaign.create: the page is the read, and nothing is asked of a second', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/screen')
            .answerAnyRequest('campaign.summary', 'campaign.summary/all')
            .answer('campaign.get', 'campaign.get/started')
            .answerAnyRequest('workitem.list', 'workitem.list/planned-many');
        const to = { ...table, id: fixtures['campaign.get/started'].request.campaign_id };
        renderApp(api);
        location.hash = href(to);
        route.value = to;
        const head = await header('Running');

        await waitFor(() => expect(kind(head)).toBe('Adaptive'));
        expect(fixtures['workitem.list/planned-many'].response.items).toHaveLength(50);
        expect(sent(api)).toHaveLength(1);
    });
});
