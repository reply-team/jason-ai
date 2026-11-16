import { signal } from '@preact/signals-react';
import type { Api } from '../api/client';
import type {
    Campaign,
    CampaignSummaryResponse,
    ErrorDetail,
    ModeInForce,
    RouteResolution,
    Routes,
    SystemInfo,
} from '../api/types';
import { t } from '../platform/i18n';
import { act, define } from './actions';
import {
    aiCaption,
    cardHead,
    drawn,
    frame,
    lastReadingCard,
    readFor,
    saidOf,
    saidView,
    startRead,
    type Said,
} from './card-frame';
import { modesInForce, people, statusSentence } from './card-model';
import {
    cardLayout,
    lastCardOf,
    placeOf,
    planChannels,
    readCard,
    type CardOption,
    type PlacedCard,
    type PlanCard,
    type PlanChannel,
} from './cards';
import type { Chat, Entry } from './chat-model';
import { html, type Html } from './html';
import { channelWords } from './live';
import { chats, here } from './state';

// A plan the chat's AI proposes, in the frame every card that reads shares (`card-frame.ts`): under its header
// "Jason’s proposal", the minute its facts were read and Refresh, two parts that stand visibly apart. The first is the
// AI's, in a dashed frame of its own: the fixed sentence that the card creates and sends nothing, its fields as labelled
// rows, its steps, each draft one press away and opened in place under "Jason’s draft", how it adapts and what the
// person does by hand. Every word of it is the AI's, drawn as its characters, never in the look of a fact or of an
// approval's message. The second is the runtime's, under "On record now": in an agent's chat the agent's status, how its
// messages go now, the people it holds and the plugin a send on each of the plan's channels goes to; in a chat about no
// agent yet, that none is created, how messages go for every agent and the plugin the routes send each channel by. It is
// read as every card that reads is: when the card is drawn as the conversation's last card that reads, and again on its
// Refresh; an earlier one stands as a stub until "Show now", its first part and its question still drawn whole. It never
// follows the journal, and nothing it read is kept in the conversation. The card creates, approves and sends nothing:
// its question's options send their words as a choice's do, and in Plan only the plan's own card decides.

/** One of the reads: what it answered, or what the runtime said instead. */
type Part<T> = { ok: true; value: T } | { ok: false; said: Said };

async function part<T>(read: Promise<T>): Promise<Part<T>> {
    try {
        return { ok: true, value: await read };
    } catch (error) {
        return { ok: false, said: saidOf(error) };
    }
}

/** The plugin that would send a campaign's messages on a channel, or null when no route sends them anywhere. */
type Sender = { plugin: string; usable: boolean; problems: ErrorDetail[] } | null;

/** Where the messages of one channel go: for an agent, the route a send on that channel resolves to; for none, a plugin. */
type Sending<T> = { channel: PlanChannel; sender: Part<T> };

/** What part 2 read: of an agent, of a chat about no agent yet. */
type Facts =
    | { place: 'agent'; state: 'gone' }
    | { place: 'agent'; state: 'failed'; said: Said }
    | {
          place: 'agent';
          state: 'read';
          campaign: Campaign;
          people: Part<number | null>;
          sending: Sending<Sender>[];
      }
    | { place: 'setup'; modes: Part<SystemInfo['send_modes']>; sending: Sending<string | null>[] };

/** The channels a plan's sends would go on: the ones it names, or every one a send takes when it names none. */
const channelsOf = (card: PlanCard): readonly PlanChannel[] => card.channels ?? planChannels;

/** Where a send on `channel` goes for the campaign: a channel route of its own counts, as the claim reads it. */
const resolveSender = (api: Api, campaignId: string, channel: PlanChannel): Promise<Part<Sender>> =>
    api
        .call<RouteResolution>('route.resolve', { campaign_id: campaignId, operation: 'message.send', channel })
        .then((resolved): Part<Sender> => ({
            ok: true,
            value:
                resolved.plugin_id === null
                    ? null
                    : { plugin: resolved.plugin_id, usable: resolved.usable, problems: resolved.problems },
        }))
        .catch((error: unknown): Part<Sender> => {
            const said = saidOf(error);
            return said.code === 'no_route' ? { ok: true, value: null } : { ok: false, said };
        });

/**
 * An agent's facts: its row first (an agent this installation does not hold is said so, and nothing else is read),
 * then, side by side, its people now and where its messages go on each channel the plan names.
 */
async function readAgent(api: Api, campaignId: string, channels: readonly PlanChannel[]): Promise<Facts> {
    let campaign: Campaign;
    try {
        campaign = await api.call<Campaign>('campaign.get', { campaign_id: campaignId });
    } catch (error) {
        const said = saidOf(error);
        return said.code === 'campaign_not_found'
            ? { place: 'agent', state: 'gone' }
            : { place: 'agent', state: 'failed', said };
    }
    const [count, ...sent] = await Promise.all([
        part(
            api
                .call<CampaignSummaryResponse>('campaign.summary', {
                    campaign_ids: [campaignId],
                    exclude_archived_contacts: true,
                })
                .then((answer) => answer.campaigns[0]?.people ?? null),
        ),
        ...channels.map((channel) => resolveSender(api, campaignId, channel)),
    ]);
    const sending = channels.map((channel, at) => ({ channel, sender: sent[at]! }));
    return { place: 'agent', state: 'read', campaign, people: count, sending };
}

/**
 * The plugin the routes send a new agent's messages on a channel by, in the order the claim reads the global routes: the
 * channel's own route, then the route of `message.send`, then the default; null for none.
 */
const globalSender = (routes: Routes, channel: PlanChannel): string | null =>
    routes.global.channels['message.send']?.[channel]?.plugin_id ??
    routes.global.operations['message.send']?.plugin_id ??
    routes.global.default?.plugin_id ??
    null;

/** A chat about no agent yet: how messages go for every agent, and the plugin the routes send each channel's by. */
async function readSetup(api: Api, channels: readonly PlanChannel[]): Promise<Facts> {
    const [modes, routes] = await Promise.all([
        part(api.call<SystemInfo>('system.info').then((info) => info.send_modes)),
        part(api.call<Routes>('route.list', {})),
    ]);
    const sending = channels.map((channel): Sending<string | null> => ({
        channel,
        sender: routes.ok ? { ok: true, value: globalSender(routes.value, channel) } : routes,
    }));
    return { place: 'setup', modes, sending };
}

/** Part 2's read, for the chat's place when the read starts. */
const readFacts = (chat: Pick<Chat, 'main' | 'campaignId'>, card: PlanCard): Promise<Facts> =>
    placeOf(chat) === 'agent'
        ? readAgent(here().api, chat.campaignId!, channelsOf(card))
        : readSetup(here().api, channelsOf(card));

/** Which of a plan card's drafts are open, for the entry it was drawn from. */
interface Opened {
    entry: Entry;
    open: readonly number[];
}

/** By chat id, then by the index of the text entry that holds the plan: the drafts the person opened on each plan card. */
export const proposals = signal<Readonly<Record<string, Readonly<Record<number, Opened>>>>>({});

/**
 * Whether the drafts a card holds open were opened on this entry: the same kind, words and moment, whatever the mark of a
 * Plan only turn's plan says, which is put on as the turn ends and taken off as the chat leaves Plan only or its next turn
 * ends. A conversation opened again from the desktop's record forgets them (`forgetProposals`).
 */
const sameEntry = (held: Entry, entry: Entry): boolean =>
    held === entry ||
    (held.kind === 'text' && entry.kind === 'text' && held.text === entry.text && held.at === entry.at);

/** Forgets the drafts a chat's plan cards hold open: it is opened again from the desktop's record. */
export function forgetProposals(chatId: string): void {
    if (proposals.value[chatId] === undefined) return;
    proposals.value = Object.fromEntries(Object.entries(proposals.value).filter(([id]) => id !== chatId));
}

function openOf(chatId: string, index: number, entry: Entry): readonly number[] {
    const opened = proposals.value[chatId]?.[index];
    return opened !== undefined && sameEntry(opened.entry, entry) ? opened.open : [];
}

/**
 * The plan a Plan only turn's marked words end in, as the plan's own card draws it: their last card block when it reads
 * as a plan, taken out of the words, every other block left in them as written; null when that block is no plan that
 * reads, which then stays in the words too.
 */
export function proposalOf(chat: Chat, index: number): { words: string; card: PlanCard } | null {
    const entry = chat.entries[index];
    if (entry?.kind !== 'text' || entry.plan !== true || chat.main) return null;
    const last = lastCardOf(entry.text);
    if (last === null) return null;
    const reading = readCard(last.block, { place: placeOf(chat), campaignId: chat.campaignId, ended: true });
    return reading.state === 'card' && reading.card.kind === 'plan'
        ? { words: last.words.trim(), card: reading.card }
        : null;
}

/**
 * The index of the conversation's last card that reads, among the cards its turns draw (`lastReadingCard`, the plan's
 * among them) and the plans its Plan only turns end in; null when none reads. One card reads at a time, whatever its kind.
 */
export function lastReadingOrPlan(chat: Chat, cards: ReadonlyMap<number, PlacedCard>): number | null {
    let last = lastReadingCard(chat, cards);
    chat.entries.forEach((_, index) => {
        if ((last === null || index > last) && proposalOf(chat, index) !== null) last = index;
    });
    return last;
}

/** The plan card a chat's entry holds now, read as the thread reads it; null for none. */
function planAt(chatId: unknown, index: unknown): { chat: Chat; card: PlanCard } | null {
    if (typeof chatId !== 'string' || typeof index !== 'number') return null;
    const chat = chats.value[chatId];
    if (chat === undefined) return null;
    const marked = proposalOf(chat, index);
    if (marked !== null) return { chat, card: marked.card };
    const placed = cardLayout(chat.entries).cards.get(index);
    if (placed === undefined) return null;
    const reading = readCard(placed, {
        place: placeOf(chat),
        campaignId: chat.campaignId,
        ended: !placed.lastTurn || !chat.running,
    });
    return reading.state === 'card' && reading.card.kind === 'plan' ? { chat, card: reading.card } : null;
}

/** How a send to each kind of person is decided, in the send-modes screen's words; nothing from a runtime that does not say. */
function modesView(inForce: { cold: ModeInForce; warm: ModeInForce } | undefined): Html | null {
    const modes = modesInForce(inForce);
    return modes.length === 0
        ? null
        : html`<div class="card-lab">${t('modes.settings.heading')}</div>
              <ul class="card-modes">
                  ${modes.map((each) => html`<li>${each.kind}: <b>${each.mode}</b></li>`)}
              </ul>`;
}

/** What a channel's sends are, as a line names the plugin that sends them. */
const sendWords: Readonly<Record<PlanChannel, string>> = { email: 'email', linkedin: 'LinkedIn messages' };

/**
 * Where each channel's messages go, one line for each: the plugin routed to send them, and for an agent whether it could
 * send them now, with each problem the claim would name; or that no plugin is routed to send them.
 */
function sendingView(sending: readonly Sending<Sender | string | null>[], whose: string): Html {
    const said = new Set<Said>();
    return html`<div class="card-lab">Sending</div>
        ${sending.map(({ channel, sender }) => {
            const what = sendWords[channel];
            if (!sender.ok) {
                // One failed read of the routes serves every channel: it is said once for the card.
                if (said.has(sender.said)) return null;
                said.add(sender.said);
                return saidView(sender.said);
            }
            const value = sender.value;
            if (value === null) return html`<div class="card-line">No plugin is routed to send ${what} ${whose}.</div>`;
            if (typeof value === 'string')
                return html`<div class="card-line">Plugin that sends ${what}: <b>${value}</b></div>`;
            return html`<div class="card-line">Plugin that sends ${what}: <b>${value.plugin}</b></div>
                ${
                    value.usable
                        ? html`<div class="card-line card-usable">It can send this agent’s ${what} now.</div>`
                        : html`<div class="card-line card-usable">It cannot send this agent’s ${what} now:</div>
                              <ul class="card-problems">
                                  ${value.problems.map((each) => html`<li><code>${each.code}</code> ${each.message}</li>`)}
                              </ul>`
                }`;
        })}`;
}

/** Part 2: what is on record now, in the runtime's facts and the desktop's sentences. */
function factsView(facts: Facts): Html {
    if (facts.place === 'setup')
        return html`<div class="card-line">No agent is created yet.</div>
            ${
                facts.modes.ok
                    ? modesView(facts.modes.value)
                    : html`<div class="card-lab">${t('modes.settings.heading')}</div>
                          ${saidView(facts.modes.said)}`
            }
            ${sendingView(facts.sending, 'yet')}`;
    if (facts.state === 'gone') return html`<div class="card-line">This agent is not in this installation.</div>`;
    if (facts.state === 'failed') return saidView(facts.said);
    const count = facts.people;
    return html`<div class="card-status">${statusSentence(facts.campaign.status)}</div>
        ${modesView(facts.campaign.send_modes_in_force)}
        <div class="card-lab">People</div>
        ${
            !count.ok
                ? saidView(count.said)
                : html`<div class="card-line">
                      ${count.value === null ? 'The runtime counted nothing.' : `${people(count.value)} in this agent`}
                  </div>`
        }
        ${sendingView(facts.sending, 'for this agent')}`;
}

/** A row of the AI's own words under the desktop's label for it; nothing when the AI gave none. */
const row = (label: string, value: string | undefined): Html | null =>
    value === undefined
        ? null
        : html`<div class="proposal-row">
              <dt>${label}</dt>
              <dd>${value}</dd>
          </div>`;

/** A list of the AI's lines under the desktop's heading for it; nothing for none. */
const lines = (heading: string, values: readonly string[] | undefined): Html | null =>
    values === undefined || values.length === 0
        ? null
        : html`<div class="proposal-lab">${heading}</div>
              <ul class="proposal-lines">
                  ${values.map((value) => html`<li>${value}</li>`)}
              </ul>`;

/** Part 1: the AI's proposal, every word of it its own, each draft one press from the screen and opened in place. */
function proposalPart(chatId: string, index: number, card: PlanCard, open: readonly number[]): Html {
    const steps = card.steps ?? [];
    return html`<div class="card-ai">
        <div class="proposal-none">This card creates and sends nothing.</div>
        <div class="proposal-title">${card.title}</div>
        <dl class="proposal-rows">
            ${row('Who', card.who)} ${row('What we offer', card.offer)} ${row('Written as', card.sender)}
            ${row('Goal', card.goal)} ${row('Channels', card.channels?.map(channelWords).join(', '))}
        </dl>
        ${
            steps.length === 0
                ? null
                : html`<div class="proposal-lab">Proposed steps</div>
                      <ol class="proposal-steps">
                          ${steps.map((step, at) => {
                              const opened = open.includes(at);
                              const head = [step.when, channelWords(step.channel)].filter(
                                  (each): each is string => each !== undefined,
                              );
                              return html`<li>
                                  <div class="proposal-step">
                                      <span>${head.join(' · ')}</span>
                                      ${step.subject === undefined ? null : html`<span class="proposal-subject">${step.subject}</span>`}
                                  </div>
                                  ${
                                      step.body === undefined
                                          ? null
                                          : html`<button
                                                    class="btn sm ghost proposal-toggle"
                                                    type="button"
                                                    aria-expanded="${opened ? 'true' : 'false'}"
                                                    ${act('card.planDraft', chatId, index, at)}
                                                >
                                                    ${opened ? 'Hide the draft' : 'Show the draft'}
                                                </button>
                                                ${
                                                    opened
                                                        ? html`<div class="proposal-draft">
                                                              ${aiCaption('Jason’s draft')}
                                                              <div class="proposal-body">${step.body}</div>
                                                          </div>`
                                                        : null
                                                }`
                                  }
                              </li>`;
                          })}
                      </ol>`
        }
        ${lines('How it adapts', card.rules)} ${lines('You do by hand', card.manual)}
    </div>`;
}

/**
 * A plan's question in Plan only, where the plan's own card decides: the AI's question and its answers, whole, as its
 * words, with no button to press.
 */
export function askedAsWords(question: string, options: readonly CardOption[]): Html {
    return html`<div class="proposal-asks" role="group" aria-label="Jason asks">
        ${aiCaption('Jason asks')}
        <div class="ask-s">${question}</div>
        <ul class="proposal-answers">
            ${options.map(
                (option) =>
                    html`<li>
                        <span class="opt-l">${option.label}</span>
                        ${option.detail === undefined ? null : html`<span class="opt-d">${option.detail}</span>`}
                    </li>`,
            )}
        </ul>
        <div class="ask-w">In Plan only these are not buttons; write your answer below.</div>
    </div>`;
}

/**
 * The plan card, drawn from the AI's block in the shared frame: its header, the minute part 2 was read and Refresh; part
 * 1; part 2 under "On record now"; and under them `asks`, its question as the thread draws it. Part 2 reads when the
 * card is the conversation's last that reads (`last`) and has not been read for what it is now; otherwise its frame is a
 * stub, with part 1 and the question still drawn whole.
 */
export function planView(chat: Chat, index: number, card: PlanCard, last: number | null, asks: Html | null): Html {
    const { held, stub } = drawn(chat, index, last, () => readFacts(chat, card));
    return frame<Facts>({
        chatId: chat.id,
        index,
        head: cardHead('Jason’s proposal'),
        action: 'card.plan',
        held,
        stub,
        kept: true,
        kind: 'chat-proposal',
        before: proposalPart(chat.id, index, card, openOf(chat.id, index, chat.entries[index]!)),
        after: asks,
        facts: (facts) => factsView(facts),
    });
}

/** "Refresh" and "Show now": part 2 read again. It decides nothing. */
define('card.plan', ([chatId, index]) => {
    const at = planAt(chatId, index);
    if (at === null) return;
    const chat = at.chat;
    startRead(chat.id, index as number, readFor(chat, index as number), () => readFacts(chat, at.card));
});

/** "Show the draft" and "Hide the draft": one draft opened or closed in place. It sends nothing. */
define('card.planDraft', ([chatId, index, step]) => {
    const at = planAt(chatId, index);
    if (at === null || typeof step !== 'number' || at.card.steps?.[step]?.body === undefined) return;
    const entry = at.chat.entries[index as number]!;
    const open = openOf(at.chat.id, index as number, entry);
    proposals.value = {
        ...proposals.value,
        [at.chat.id]: {
            ...proposals.value[at.chat.id],
            [index as number]: {
                entry,
                open: open.includes(step) ? open.filter((each) => each !== step) : [...open, step],
            },
        },
    };
});
