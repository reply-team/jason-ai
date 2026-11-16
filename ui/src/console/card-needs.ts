import type { Api } from '../api/client';
import type { ApprovalSummary, Campaign, DecisionSummary, Page } from '../api/types';
import { clock } from '../time/format';
import { campaignOf, cardAt, drawn, frame, metaLine, saidOf, saidView, startRead, type Said } from './card-frame';
import { needsLine } from './card-model';
import { placeOf, type NeedsCard } from './cards';
import { act, define } from './actions';
import type { Chat } from './chat-model';
import { html, type Html } from './html';
import { attention } from './needs';
import { here } from './state';
import { toApprovalCard, toQuestionCard } from './waiting-model';

// What waits for the person's decision, as the chat's AI asks for it: a pointer, never a place to decide. It counts the
// approvals and the questions waiting (items, never people) in one agent or in every one, as Needs you's own lists say
// them when the card is read; in the agent's own chat it points at the waiting cards below the conversation, and
// anywhere else it lists each item with an Open that shows it in Needs you. It approves, rejects and answers nothing.

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

type NeedsRead =
    | { state: 'gone' }
    | { state: 'failed'; said: Said }
    | { state: 'read'; archived: boolean; approvals: Page<ApprovalSummary>; questions: Page<DecisionSummary> };

/**
 * The card's reads, Needs you's own requests: for one agent, its row first (an agent this installation does not hold is
 * said so and nothing else is read), then its pending approvals and questions; for every agent, both lists as Needs
 * you asks for them. The card holds its own read, as of its time; it does not follow the journal.
 */
async function readNeeds(api: Api, campaignId: string | null): Promise<NeedsRead> {
    let archived = false;
    if (campaignId !== null) {
        try {
            archived = (await api.call<Campaign>('campaign.get', { campaign_id: campaignId })).status === 'archived';
        } catch (error) {
            const said = saidOf(error);
            return said.code === 'campaign_not_found' ? { state: 'gone' } : { state: 'failed', said };
        }
    }
    const body = campaignId === null ? {} : { status: 'pending', campaign_id: campaignId, limit: 50 };
    try {
        const [approvals, questions] = await Promise.all([
            api.call<Page<ApprovalSummary>>('approval.list', body),
            api.call<Page<DecisionSummary>>('decision.list', body),
        ]);
        return { state: 'read', archived, approvals, questions };
    } catch (error) {
        return { state: 'failed', said: saidOf(error) };
    }
}

const open = (kind: 'approvals' | 'questions', id: string): Html =>
    html`<button class="btn sm card-open" type="button" ${act('card.open', kind, id)}>Open</button>`;

function needsFacts(chat: Chat, campaignId: string | null, read: NeedsRead, at: number): Html {
    if (read.state === 'gone') return html`<div class="ask-w">This agent is not in this installation.</div>`;
    if (read.state === 'failed') return saidView(read.said);
    const { approvals, questions } = read;
    const archived = read.archived ? html`<div class="ask-w card-archived">This agent is archived.</div>` : null;
    if (approvals.items.length === 0 && questions.items.length === 0)
        return html`${archived}
            <div class="ask-w card-empty">Nothing is waiting for your decision.</div>`;
    const line = needsLine(
        { count: approvals.items.length, more: approvals.next_cursor !== null },
        { count: questions.items.length, more: questions.next_cursor !== null },
    );
    // In the agent's own chat its waiting cards are below the conversation, read by their own holder: the card points
    // at them, as of its read, and the cards may say more or fewer if something moved since.
    if (placeOf(chat) === 'agent' && campaignId === chat.campaignId)
        return html`${archived}
            <div class="card-count-line">${line}</div>
            <div class="ask-w">As of ${clock(new Date(at), zone)}, they were below this conversation.</div>
            <div class="ask-a">
                <button class="btn sm" type="button" ${act('card.waits', chat.id, campaignId)}>Show them</button>
            </div>`;
    return html`${archived}
        <div class="card-count-line">${line}</div>
        <ul class="card-rows card-needs" aria-label="Waiting for your decision">
            ${questions.items.map((row) => {
                const card = toQuestionCard(row, undefined, at);
                return html`<li class="card-item">
                    <span class="card-name">Question</span>
                    <span class="card-question">${card.question}</span>
                    ${metaLine([row.contact_name, row.campaign_name, card.age])} ${open('questions', row.id)}
                </li>`;
            })}
            ${approvals.items.map((row) => {
                const card = toApprovalCard(row, undefined, at);
                return html`<li class="card-item">
                    <span class="card-name">${card.what}</span>
                    ${metaLine([card.to, row.campaign_name, card.age])} ${open('approvals', row.id)}
                </li>`;
            })}
        </ul>`;
}

/** The needs card, read when it is drawn as the conversation's last card that reads, and on its Refresh. */
export function needsCard(chat: Chat, index: number, card: NeedsCard, last: number | null): Html {
    const campaignId = campaignOf(card, chat);
    const { held, stub } = drawn(chat, index, last, () => readNeeds(here().api, campaignId));
    return frame<NeedsRead>({
        chatId: chat.id,
        index,
        header: 'Waiting for your decision',
        action: 'card.needs',
        held,
        stub,
        facts: (read, at) => needsFacts(chat, campaignId, read, at),
    });
}

define('card.needs', ([chatId, index]) => {
    const at = cardAt(chatId, index, 'needs');
    if (at === null) return;
    const campaignId = campaignOf(at.card, at.chat);
    startRead(at.chat.id, index as number, at.entry, () => readNeeds(here().api, campaignId));
});

/** Open: the item shown in Needs you, where it is decided under its own gate. Navigation only. */
define('card.open', ([kind, id]) => {
    if ((kind !== 'approvals' && kind !== 'questions') || typeof id !== 'string') return;
    here().dock.show('needs');
    void attention.open(here().api, kind, id);
});
