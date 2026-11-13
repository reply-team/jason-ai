import { signal } from '@preact/signals-react';
import type { CampaignSummary, CampaignSummaryResponse, CampaignSummaryRow, Page } from '../api/types';
import { parseRoute } from '../router';
import * as store from '../screens/campaigns/store';
import { act, define } from './actions';
import { html, type Html } from './html';
import { toCampaignRow } from './live';
import { here } from './state';

// The campaigns, with the counts the runtime keeps for each, read the way the browser's Campaigns screen reads its
// summary table, so both say the same numbers, but held by the panel itself: the page panel can show that screen at a
// table of its own beside this one, and neither replaces the other's list.

/** The campaigns the panel lists, in the order the runtime listed them. */
export const campaigns = signal<CampaignSummary[]>([]);
/** The runtime's counts for each, by campaign id. */
export const summary = signal<Record<string, CampaignSummaryRow>>({});
/** Whether the list has been read at least once. */
export const read = signal(false);
export const failure = signal<string | null>(null);
let generation = 0;

/** The first page of campaigns that are not archived, then one read of their counts. A late answer is dropped. */
export async function reload(): Promise<void> {
    const mine = ++generation;
    const api = here().api;
    failure.value = null;
    try {
        const page = await api.call<Page<CampaignSummary>>('campaign.list', store.listBody({}));
        if (mine !== generation) return;
        const ids = page.items.map((each) => each.id);
        const counts = ids.length
            ? await api.call<CampaignSummaryResponse>('campaign.summary', {
                  campaign_ids: ids,
                  ...store.archivedLeftOut,
              })
            : { campaigns: [] };
        if (mine !== generation) return;
        summary.value = Object.fromEntries(counts.campaigns.map((row) => [row.campaign_id, row]));
        campaigns.value = page.items;
        read.value = true;
    } catch (error) {
        if (mine === generation) store.fail(error, failure);
    }
}

define('campaigns.start', () => here().dock.chat());

define('campaigns.open', ([id]) => {
    if (typeof id === 'string') here().dock.page(parseRoute(`#/campaigns/${encodeURIComponent(id)}`));
});

define('campaigns.chat', ([id]) => {
    const campaign = campaigns.value.find((each) => each.id === id);
    if (campaign) here().dock.chat({ id: campaign.id, name: campaign.name });
});

export function campaignsView(): Html {
    const shownFailure = failure.value;
    const rows = campaigns.value.map((campaign) => toCampaignRow(campaign, summary.value[campaign.id]));
    return html`<div class="panel-body">
        <div class="ph">
            <div>
                <h1>Agents</h1>
                <p>Every agent that is not archived, with the people in it.</p>
            </div>
        </div>
        ${shownFailure ? html`<div class="note am" role="alert">${shownFailure}</div>` : null}
        ${
            !read.value && !shownFailure
                ? html`<div class="empty">Reading the agents…</div>`
                : rows.length === 0
                  ? html`<div class="empty empty-start">
                        <p>
                            No agents yet. An agent is set up in a chat with Jason: say who you want to reach, and the
                            AI sets it up with you.
                        </p>
                        <button class="btn sm p" type="button" ${act('campaigns.start')}>New chat</button>
                    </div>`
                  : html`<table class="t">
                        <thead>
                            <tr>
                                <th>Agent</th>
                                <th>State</th>
                                <th>People</th>
                                <th>Waiting</th>
                                <th>Failed</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            ${rows.map(
                                (row) =>
                                    html`<tr>
                                        <td><b>${row.name}</b></td>
                                        <td><span class="pillc ${row.tone}">${row.state}</span></td>
                                        <td>${row.people}</td>
                                        <td>${row.waiting}</td>
                                        <td>${row.failed}</td>
                                        <td class="acts">
                                            <button class="btn sm" type="button" ${act('campaigns.open', row.id)}>
                                                Open
                                            </button>
                                            <button class="btn sm" type="button" ${act('campaigns.chat', row.id)}>
                                                Chat
                                            </button>
                                        </td>
                                    </tr>`,
                            )}
                        </tbody>
                    </table>`
        }
    </div>`;
}
