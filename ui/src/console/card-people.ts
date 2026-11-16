import type { Api } from '../api/client';
import type { Campaign, CampaignSummaryResponse, Contact, MembershipItem, Page } from '../api/types';
import { campaignOf, cardAt, drawn, frame, metaLine, saidOf, saidView, startRead, type Said } from './card-frame';
import { peopleEmpty, peopleFooter, personRow } from './card-model';
import type { PeopleListCard } from './cards';
import { act, define } from './actions';
import type { Chat } from './chat-model';
import { html, type Html } from './html';
import { here } from './state';

// The people list the chat's AI asks for: up to ten people as the runtime lists them, of one agent or of everyone, by
// the AI's search and a count's standing when it names them. The search is the AI's query, shown as such above the
// facts; every row and count under "On record now" is the runtime's answer. Its only controls open a page.

const LIMIT = 10;

/** What the card read: the people, whether more stand behind them, the count its footer says, or why it could not. */
type PeopleRead =
    | { state: 'gone' }
    | { state: 'failed'; said: Said }
    | { state: 'read'; archived: boolean; people: Contact[]; more: boolean; count: number | null };

/**
 * The card's reads: with a campaign, its row first (an agent this installation does not hold is said so and nothing
 * else is read), then a page of its members and, for the footer's count when nothing is searched, its counts; without
 * one, a page of everyone. The same calls the People screen and the campaign's People tab make.
 */
async function readPeople(api: Api, card: PeopleListCard, campaignId: string | null): Promise<PeopleRead> {
    const query = card.search === undefined ? {} : { query: card.search };
    try {
        if (campaignId === null) {
            const page = await api.call<Page<Contact>>('contact.list', { ...query, limit: LIMIT });
            return { state: 'read', archived: false, people: page.items, more: page.next_cursor !== null, count: null };
        }
        let campaign: Campaign;
        try {
            campaign = await api.call<Campaign>('campaign.get', { campaign_id: campaignId });
        } catch (error) {
            const said = saidOf(error);
            return said.code === 'campaign_not_found' ? { state: 'gone' } : { state: 'failed', said };
        }
        const [page, summed] = await Promise.all([
            api.call<Page<MembershipItem>>('campaign.list_contacts', {
                campaign_id: campaignId,
                ...query,
                ...(card.standing === undefined ? {} : { summary: card.standing }),
                limit: LIMIT,
                exclude_archived_contacts: true,
            }),
            card.search === undefined
                ? api.call<CampaignSummaryResponse>('campaign.summary', {
                      campaign_ids: [campaignId],
                      exclude_archived_contacts: true,
                  })
                : Promise.resolve(null),
        ]);
        const row = summed?.campaigns.find((each) => each.campaign_id === campaignId);
        return {
            state: 'read',
            archived: campaign.status === 'archived',
            people: page.items.map((item) => item.contact),
            more: page.next_cursor !== null,
            count: row === undefined ? null : row[card.standing ?? 'people'],
        };
    } catch (error) {
        return { state: 'failed', said: saidOf(error) };
    }
}

/** The address of the People page or tab the footer opens: the agent's tab under its count, else the People page. */
function morePlace(card: PeopleListCard, campaignId: string | null): string {
    const id = campaignId === null ? null : encodeURIComponent(campaignId);
    if (id !== null && card.search === undefined)
        return `#/campaigns/${id}/people${card.standing === undefined ? '' : `?filter=${card.standing}`}`;
    const parts = [
        ...(card.search === undefined ? [] : [`q=${encodeURIComponent(card.search)}`]),
        ...(id === null ? [] : [`campaign=${id}`]),
    ];
    return `#/people${parts.length === 0 ? '' : `?${parts.join('&')}`}`;
}

function facts(card: PeopleListCard, campaignId: string | null, read: PeopleRead): Html {
    if (read.state === 'gone') return html`<div class="ask-w">This agent is not in this installation.</div>`;
    if (read.state === 'failed') return saidView(read.said);
    const scope = { campaign: campaignId !== null, search: card.search !== undefined, standing: card.standing };
    const footer = peopleFooter({ ...scope, count: read.count, more: read.more });
    const rows = read.people.map((contact) => personRow(contact, campaignId ?? undefined));
    return html`${read.archived ? html`<div class="ask-w card-archived">This agent is archived.</div>` : null}
    ${
        rows.length === 0
            ? html`<div class="ask-w card-empty">${peopleEmpty(scope)}</div>`
            : html`<ul class="card-rows" aria-label="People">
                  ${rows.map(
                      (row) =>
                          html`<li>
                              <button
                                  class="card-row"
                                  type="button"
                                  ${act('card.page', `#/people/${encodeURIComponent(row.id)}`)}
                              >
                                  <span class="card-name">${row.name}</span>
                                  ${metaLine([row.company, row.title, row.channel])}
                                  ${row.standing.map((word) => html`<span class="stage">${word}</span>`)}
                              </button>
                          </li>`,
                  )}
              </ul>`
    }
    ${
        footer === null
            ? null
            : html`<button class="lk card-more" type="button" ${act('card.page', morePlace(card, campaignId))}>
                  ${footer}
              </button>`
    }`;
}

/** The people list's card, read when it is drawn as the conversation's last card that reads, and on its Refresh. */
export function peopleCard(chat: Chat, index: number, card: PeopleListCard, last: number | null): Html {
    const campaignId = campaignOf(card, chat);
    const { held, stub } = drawn(chat, index, last, () => readPeople(here().api, card, campaignId));
    // The AI's search, as its words and outside the facts: what the runtime was asked, never a fact of its own.
    const before =
        card.search === undefined
            ? null
            : html`<div class="card-ai">
                  <div class="card-query">Jason’s query: “${card.search}”</div>
                  <div class="ask-w card-match">A text match in names, companies, titles, addresses and fields.</div>
              </div>`;
    return frame<PeopleRead>({
        chatId: chat.id,
        index,
        header: 'People',
        action: 'card.people',
        held,
        stub,
        before,
        facts: (read) => facts(card, campaignId, read),
    });
}

define('card.people', ([chatId, index]) => {
    const at = cardAt(chatId, index, 'people_list');
    if (at === null) return;
    const campaignId = campaignOf(at.card, at.chat);
    startRead(at.chat.id, index as number, at.entry, () => readPeople(here().api, at.card, campaignId));
});
