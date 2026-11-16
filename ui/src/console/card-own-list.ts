import { signal } from '@preact/signals-react';
import type { AddContactsResult, Campaign } from '../api/types';
import { repeated } from '../screens/attention/store';
import { act, define } from './actions';
import {
    aiCaption,
    campaignOf,
    cardAt,
    cardHead,
    drawn,
    frame,
    heldOf,
    type Held,
    readFor,
    type ReadFor,
    type Said,
    saidOf,
    saidView,
    settleRead,
    startRead,
} from './card-frame';
import { cardRows, statusSentence } from './card-model';
import type { OwnListCard } from './cards';
import { locksChoice, type Chat } from './chat-model';
import { html, type Html } from './html';
import { addressesIn, fieldWord, listRequest, mappingLine, rowOutcome } from './own-list';
import { here } from './state';

// The list card the chat's AI writes, in an agent's own chat, from a list the person gave it: the AI's reading of the
// list, every row it will send drawn with how each column was read; the page's own comparison of the rows' addresses with
// what the person wrote in this chat; and, under "On record now", the agent as the runtime holds it, with one button,
// Add to this agent. A press is the person's own act: one `campaign.add_contacts` of every row, matched by the card's
// key, so a second press adds nobody twice. The answer is drawn row by row in the runtime's words. Nothing of a press is
// kept: a page opened again offers the button again, and a press then says each row is already in the agent.

/** What the card read of its agent: the agent, that this installation does not hold it, or what the runtime said. */
type ListRead = { state: 'gone' } | { state: 'failed'; said: Said } | { state: 'read'; campaign: Campaign };

async function readAgent(campaignId: string): Promise<ListRead> {
    try {
        return {
            state: 'read',
            campaign: await here().api.call<Campaign>('campaign.get', { campaign_id: campaignId }),
        };
    } catch (error) {
        const said = saidOf(error);
        return said.code === 'campaign_not_found' ? { state: 'gone' } : { state: 'failed', said };
    }
}

/** A press of a card, for what the card is now: under way, answered, or refused whole. */
type Press = ReadFor &
    ({ state: 'adding' } | { state: 'answered'; answer: AddContactsResult } | { state: 'refused'; said: Said });

/** One chat's lists in this drawing of the page, never kept: by entry index, each one's press, and those shown whole. */
export interface ListsHeld {
    presses: Readonly<Record<number, Press>>;
    opened: readonly number[];
}

/** By chat id, what its lists hold; the thread redraws when its chat's changes. */
export const lists = signal<Readonly<Record<string, ListsHeld>>>({});

const listsOf = (chatId: string): ListsHeld => lists.value[chatId] ?? { presses: {}, opened: [] };

function change(chatId: string, next: (held: ListsHeld) => ListsHeld): void {
    lists.value = { ...lists.value, [chatId]: next(listsOf(chatId)) };
}

function pressOf(chat: Chat, index: number): Press | null {
    const press = listsOf(chat.id).presses[index];
    const now = readFor(chat, index);
    return press !== undefined && press.text === now.text && press.scope === now.scope ? press : null;
}

function put(chatId: string, index: number, press: Press): void {
    change(chatId, (held) => ({ ...held, presses: { ...held.presses, [index]: press } }));
}

/**
 * Whether a list's button may be pressed: its turn has ended and nothing of the person's or the AI's stands after it,
 * so a list the AI corrected later, or one the person answered in words, is never the one added.
 */
function pressable(chat: Chat, index: number): boolean {
    let last = chat.entries.length - 1;
    while (last >= 0 && !locksChoice(chat.entries[last]!.kind)) last--;
    return !chat.running && last === index;
}

/** The agent the card read, when it read one that takes people. */
function agentOf(held: Held<ListRead> | null): Campaign | null {
    return held?.state === 'read' && held.facts.state === 'read' && held.facts.campaign.status !== 'archived'
        ? held.facts.campaign
        : null;
}

/** "Add to this agent": every row, once, and then the agent read again; every act of the card held until both answer. */
define('card.own_list.add', ([chatId, index], _element, event) => {
    if (repeated(event) || typeof index !== 'number') return;
    const at = cardAt(chatId, index, 'own_list');
    if (at === null) return;
    const { chat, now, card } = at;
    const campaignId = campaignOf(card, chat);
    const press = pressOf(chat, index);
    if (campaignId === null || !pressable(chat, index) || press?.state === 'adding' || press?.state === 'answered')
        return;
    if (agentOf(heldOf<ListRead>(chat.id, index, now)) === null) return;
    void add(chat.id, index, now, card, campaignId);
});

async function add(chatId: string, index: number, now: ReadFor, card: OwnListCard, campaignId: string): Promise<void> {
    put(chatId, index, { ...now, state: 'adding' });
    let outcome: Press;
    try {
        const answer = await here().api.call<AddContactsResult>(
            'campaign.add_contacts',
            listRequest(card, campaignId),
            'write',
        );
        outcome = { ...now, state: 'answered', answer };
    } catch (error) {
        outcome = { ...now, state: 'refused', said: saidOf(error) };
    }
    // What the agent is now, read after the press: the card stands on it, and the press is held until it answers.
    settleRead(chatId, index, now, await readAgent(campaignId));
    put(chatId, index, outcome);
}

define('card.own_list', ([chatId, index]) => {
    const at = cardAt(chatId, index, 'own_list');
    const campaignId = at === null ? null : campaignOf(at.card, at.chat);
    if (at === null || campaignId === null) return;
    startRead(at.chat.id, index as number, at.now, () => readAgent(campaignId));
});

/** "Show more": every row of the list, in place. It decides nothing. */
define('card.own_list.more', ([chatId, index]) => {
    if (typeof chatId !== 'string' || typeof index !== 'number') return;
    change(chatId, (held) => ({ ...held, opened: [...held.opened, index] }));
});

const rowsWord = (count: number): string => (count === 1 ? '1 row' : `${count} rows`);

/**
 * The comparison of the card's addresses with what the person wrote in this chat, in one line, and every row whose
 * address is not there, shown whatever else is folded; where none is there, one sentence, since the rows may have been
 * read from a file the page cannot see.
 */
function compared(card: OwnListCard, found: ReturnType<typeof addressesIn>, table: (rows: number[]) => Html): Html {
    if (found.total === 0)
        return html`<div class="ask-w card-compare">
            This list has no email address for Jason to compare with what you wrote in this chat.
        </div>`;
    if (found.found === 0)
        return html`<div class="ask-w card-compare">
            None of these addresses is in what you wrote in this chat. If the AI read these rows from a file, Jason
            cannot compare them with it.
        </div>`;
    const line = `${found.found} of ${found.total} ${found.total === 1 ? 'address is' : 'addresses are'} in what you wrote in this chat`;
    return html`<div class="card-compare">
        <div class="card-count-line">${line}</div>
        ${
            found.missing.length === 0
                ? null
                : html`<div class="ask-w">Not in what you wrote here:</div>
                      ${table(found.missing)}`
        }
    </div>`;
}

/** One sentence above the button: what the runtime's status and send mode for these people say adding does now. */
function runningSentence(campaign: Campaign): string {
    if (campaign.status !== 'active') return statusSentence(campaign.status);
    const cold = campaign.send_modes_in_force?.cold.mode;
    switch (cold) {
        case undefined:
            return 'This agent is running.';
        case 'automatic':
            return 'This agent is running, and its messages to people who have not replied yet go automatically: one it writes to someone added here can be sent without asking you, once Jason’s own review of it passes.';
        case 'every_message':
            return 'This agent is running, and every message it writes to people who have not replied yet waits for you to approve it.';
        default:
            return `This agent is running, and its send mode for people who have not replied yet is ${cold}.`;
    }
}

/**
 * The agent as the runtime holds it, what adding does there, and the button or what the press did: the runtime's answer
 * counted and, row by row, in `rows`.
 */
function facts(
    chat: Chat,
    index: number,
    card: OwnListCard,
    campaignId: string,
    read: ListRead,
    rows: Html | null,
): Html {
    if (read.state === 'gone') return html`<div class="ask-w">This agent is not in this installation.</div>`;
    if (read.state === 'failed') return saidView(read.said);
    const campaign = read.campaign;
    const press = pressOf(chat, index);
    const archived = campaign.status === 'archived';
    const key = card.match_by === 'email' ? 'email address' : 'LinkedIn profile';
    const people = html`<button
        class="btn sm"
        type="button"
        ${act('card.page', `#/campaigns/${encodeURIComponent(campaignId)}/people`)}
    >
        People
    </button>`;
    const said =
        press?.state === 'refused'
            ? saidView(press.said)
            : press?.state === 'answered'
              ? html`<div class="card-count-line" role="status">
                        Added ${press.answer.summary.added} · Already in this agent
                        ${press.answer.summary.already_member} · Not added ${press.answer.summary.rejected}
                    </div>
                    ${rows}`
              : null;
    const live = pressable(chat, index);
    const button =
        archived || press?.state === 'answered'
            ? null
            : live
              ? html`<button
                    class="btn sm p"
                    type="button"
                    ${press?.state === 'adding' ? html`disabled` : act('card.own_list.add', chat.id, index)}
                >
                    Add to this agent
                </button>`
              : chat.running
                ? null
                : html`<span class="ask-w">Only the conversation’s last list is added from the chat.</span>`;
    return html`${
            archived
                ? html`<div class="ask-w card-archived">This agent is archived, so nobody can be added to it.</div>`
                : html`<ul class="card-notes">
                          <li>
                              Someone Jason already holds with this ${key} is added as they are; this list does not
                              change their record.
                          </li>
                          <li>Anyone taken out of this agent before is not added back.</li>
                          <li>Nothing is sent on a channel where the person is on the do-not-contact list.</li>
                      </ul>
                      <div class="card-status">${runningSentence(campaign)}</div>`
        }
        ${said}
        ${press?.state === 'adding' ? html`<div class="card-reading" role="status">Adding the list...</div>` : null}
        <div class="ask-a card-nav">${button} ${press?.state === 'answered' ? people : null}</div>`;
}

/** The list card, its agent read when it is drawn as the conversation's last card that reads, and on its Refresh. */
export function ownListCard(chat: Chat, index: number, card: OwnListCard, last: number | null): Html {
    // The schema holds a list to an agent's own chat, so its campaign is always that chat's.
    const campaignId = campaignOf(card, chat)!;
    const { held, stub } = drawn(chat, index, last, () => readAgent(campaignId));
    const press = pressOf(chat, index);
    const answer = press?.state === 'answered' ? press.answer : null;
    const outcome = (row: number) => rowOutcome(answer?.items.find((item) => item.index === row));
    const found = addressesIn(
        card,
        chat.entries.flatMap((entry) => (entry.kind === 'user' ? [entry.text] : [])),
    );
    const all = listsOf(chat.id).opened.includes(index);
    const more = html`<button class="lk card-more" type="button" ${act('card.own_list.more', chat.id, index)}>
        Show more
    </button>`;
    const table = (rows: readonly number[]): Html =>
        html`<div class="card-scroll">
            <table class="card-tbl card-list-rows">
                <thead>
                    <tr>
                        <th>#</th>
                        ${card.columns.map((column) => html`<th>${fieldWord(column.field)}</th>`)}
                    </tr>
                </thead>
                <tbody>
                    ${rows.map(
                        (row) =>
                            html`<tr>
                                <td class="mono">${row + 1}</td>
                                ${card.rows[row]!.map((value) => html`<td>${value}</td>`)}
                            </tr>`,
                    )}
                </tbody>
            </table>
        </div>`;
    const every = card.rows.map((_, row) => row);
    const shown = all ? every : every.filter((row) => row < cardRows);
    // The runtime's answer, row by row, by the row's number and the value it was matched by: a row it did not add is
    // shown however the list folds.
    const matched = card.columns.findIndex((column) => column.field === card.match_by);
    const told = all || answer === null ? every : every.filter((row) => row < cardRows || !outcome(row).added);
    const answered =
        answer === null
            ? null
            : html`<div class="card-scroll card-answer">
                      <table class="card-tbl">
                          <thead>
                              <tr>
                                  <th>#</th>
                                  <th>${fieldWord(card.match_by)}</th>
                                  <th>What happened</th>
                              </tr>
                          </thead>
                          <tbody>
                              ${told.map(
                                  (row) =>
                                      html`<tr>
                                          <td class="mono">${row + 1}</td>
                                          <td>${card.rows[row]![matched]}</td>
                                          <td class="card-outcome">${outcome(row).words}</td>
                                      </tr>`,
                              )}
                          </tbody>
                      </table>
                  </div>
                  ${told.length < every.length ? more : null}`;
    const before = html`<div class="card-ai card-list">
            ${aiCaption('The list as Jason read it')}
            <div class="card-map">${mappingLine(card)}</div>
            <div class="card-count-line">${rowsWord(card.rows.length)}</div>
            ${table(shown)} ${shown.length < every.length ? more : null}
        </div>
        ${compared(card, found, table)}`;
    return frame<ListRead>({
        chatId: chat.id,
        index,
        head: cardHead('Your list'),
        action: 'card.own_list',
        held,
        stub,
        kept: true,
        kind: 'chat-own-list',
        before,
        facts: (read) => facts(chat, index, card, campaignId, read, answered),
    });
}
