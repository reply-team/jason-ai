import { parseRoute } from '../router';
import * as store from '../screens/campaigns/store';
import { act, define } from './actions';
import { html, type Html } from './html';
import { toCampaignRow } from './live';
import { here } from './state';

// The campaigns, with the counts the runtime keeps for each: read through the browser's Campaigns store, in its
// summary view, so both say the same numbers.

const table: store.Table = { view: 'summary', period: '30d', filters: {} };
const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

export function reload(): Promise<void> {
    return store.load(here().api, table, new Date(), zone);
}

define('campaigns.open', ([id]) => {
    if (typeof id === 'string') here().dock.page(parseRoute(`#/campaigns/${encodeURIComponent(id)}`));
});

define('campaigns.chat', ([id]) => {
    const campaign = store.campaigns.value.find((each) => each.id === id);
    if (campaign) here().dock.chat({ id: campaign.id, name: campaign.name });
});

export function campaignsView(): Html {
    const failure = store.failure.value;
    const rows = store.campaigns.value.map((campaign) => toCampaignRow(campaign, store.summary.value[campaign.id]));
    return html`<div class="panel-body">
        <div class="ph">
            <div>
                <h1>Campaigns</h1>
                <p>Every campaign that is not archived, with the people in it.</p>
            </div>
        </div>
        ${failure ? html`<div class="note am" role="alert">${failure}</div>` : null}
        ${
            store.shown.value === null && !failure
                ? html`<div class="empty">Reading the campaigns…</div>`
                : rows.length === 0
                  ? html`<div class="empty">No campaigns yet. Ask the chat to start one.</div>`
                  : html`<table class="t">
                        <thead>
                            <tr>
                                <th>Campaign</th>
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
