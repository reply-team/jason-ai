import type { Api } from '../api/client';
import type {
    Campaign,
    CampaignActivityResponse,
    CampaignActivityRow,
    CampaignSummaryResponse,
    CampaignSummaryRow,
} from '../api/types';
import { t } from '../platform/i18n';
import { periodWindow } from '../time/periods';
import { campaignOf, cardAt, drawn, frame, saidOf, saidView, startRead, type Said } from './card-frame';
import { modesInForce, monthCounts, nowCounts, statusSentence, type Count } from './card-model';
import type { AgentStatusCard } from './cards';
import { act, define } from './actions';
import type { Chat } from './chat-model';
import { html, type Html } from './html';
import { here } from './state';

// An agent's status, as the chat's AI asks for it by the agent's id alone: its status in a sentence chosen from the
// status, the counts of now, the last thirty days over the Results tab's own window (so the card and the tab say the
// same numbers), and how its sends are decided now. Every number, the status and the modes are the runtime's; nothing
// the runtime does not count is shown. Its controls open a page or show what waits.

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

/** One of the card's reads: what it answered, or what the runtime said instead. */
type Part<T> = { ok: true; row: T } | { ok: false; said: Said };

type StatusRead =
    | { state: 'gone' }
    | { state: 'failed'; said: Said }
    | { state: 'read'; campaign: Campaign; now: Part<CampaignSummaryRow>; month: Part<CampaignActivityRow> };

async function part<T>(read: Promise<{ campaigns: T[] }>): Promise<Part<T>> {
    try {
        const row = (await read).campaigns[0];
        return row === undefined
            ? { ok: false, said: { code: null, message: 'The runtime counted nothing.' } }
            : { ok: true, row };
    } catch (error) {
        return { ok: false, said: saidOf(error) };
    }
}

/**
 * The card's reads: the agent's row first (an agent this installation does not hold is said so and nothing else is
 * read), then, side by side, its counts of now and of the last thirty days; a count that fails says why in its place.
 */
async function readStatus(api: Api, campaignId: string): Promise<StatusRead> {
    let campaign: Campaign;
    try {
        campaign = await api.call<Campaign>('campaign.get', { campaign_id: campaignId });
    } catch (error) {
        const said = saidOf(error);
        return said.code === 'campaign_not_found' ? { state: 'gone' } : { state: 'failed', said };
    }
    const scope = { campaign_ids: [campaignId], exclude_archived_contacts: true };
    const [now, month] = await Promise.all([
        part(api.call<CampaignSummaryResponse>('campaign.summary', scope)),
        part(
            api.call<CampaignActivityResponse>('campaign.activity', {
                ...scope,
                ...periodWindow('30d', new Date(), zone),
            }),
        ),
    ]);
    return { state: 'read', campaign, now, month };
}

const counts = (label: string, shown: Count[]): Html =>
    html`<div class="card-lab">${label}</div>
        <div class="card-counts">
            ${shown.map(
                (count) =>
                    html`<div class="card-count" data-count="${count.key}">
                        <b>${count.value}</b><span>${count.label}</span>
                    </div>`,
            )}
        </div>`;

function statusFacts(chat: Chat, campaignId: string, read: StatusRead): Html {
    if (read.state === 'gone') return html`<div class="ask-w">This agent is not in this installation.</div>`;
    if (read.state === 'failed') return saidView(read.said);
    const id = encodeURIComponent(campaignId);
    const modes = modesInForce(read.campaign.send_modes_in_force);
    const waiting = read.now.ok && read.now.row.waiting > 0;
    const page = (tab: string, label: string) =>
        html`<button class="btn sm" type="button" ${act('card.page', `#/campaigns/${id}/${tab}`)}>${label}</button>`;
    return html`<div class="card-status">${statusSentence(read.campaign.status)}</div>
        ${
            read.now.ok
                ? counts('Now', nowCounts(read.now.row))
                : html`<div class="card-lab">Now</div>
                      ${saidView(read.now.said)}`
        }
        ${
            read.month.ok
                ? counts('Last 30 days', monthCounts(read.month.row))
                : html`<div class="card-lab">Last 30 days</div>
                      ${saidView(read.month.said)}`
        }
        ${
            modes.length === 0
                ? null
                : html`<div class="card-lab">${t('modes.settings.heading')}</div>
                      <ul class="card-modes">
                          ${modes.map((each) => html`<li>${each.kind}: <b>${each.mode}</b></li>`)}
                      </ul>`
        }
        <div class="ask-a card-nav">
            ${page('people', 'People')} ${page('inbox', 'Inbox')} ${page('results', 'Results')}
            ${page('settings', 'Settings')}
            ${waiting ? html`<button class="btn sm" type="button" ${act('card.waits', chat.id, campaignId)}>Show what waits</button>` : null}
        </div>`;
}

/** The status card, read when it is drawn as the conversation's last card that reads, and on its Refresh. */
export function statusCard(chat: Chat, index: number, card: AgentStatusCard, last: number | null): Html {
    // The schema holds a status card outside an agent's chat to the campaign it names, so there is always one.
    const campaignId = campaignOf(card, chat)!;
    const { held, stub } = drawn(chat, index, last, () => readStatus(here().api, campaignId));
    const read = held?.state === 'read' ? held.facts : null;
    return frame<StatusRead>({
        chatId: chat.id,
        index,
        header: read?.state === 'read' ? `Agent: ${read.campaign.name}` : 'Agent',
        action: 'card.status',
        held,
        stub,
        facts: (facts) => statusFacts(chat, campaignId, facts),
    });
}

define('card.status', ([chatId, index]) => {
    const at = cardAt(chatId, index, 'agent_status');
    const campaignId = at === null ? null : campaignOf(at.card, at.chat);
    if (at === null || campaignId === null) return;
    startRead(at.chat.id, index as number, at.entry, () => readStatus(here().api, campaignId));
});
