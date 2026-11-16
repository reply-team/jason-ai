import { signal } from '@preact/signals-react';
import { ApiError, type Api } from '../api/client';
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
import { modeWords } from '../screens/send-modes/model';
import { clock } from '../time/format';
import { act, define } from './actions';
import { cardLayout, lastCardOf, placeOf, readCard, type CardOption, type PlacedCard, type PlanCard } from './cards';
import type { Chat, Entry } from './chat-model';
import { html, type Html } from './html';
import { channelWords } from './live';
import { chats, here } from './state';

// A plan the chat's AI proposes, drawn in two parts that stand visibly apart. The first is the AI's: under its frame's
// label "Jason’s proposal" and the fixed sentence that the card creates and sends nothing, its fields as labelled rows,
// its steps, each draft one press away and opened in place under "Jason’s draft", how it adapts and what the person does
// by hand. Every word of it is the AI's, drawn as its characters, never in the look of a fact or of an approval's
// message. The second is the runtime's, under "On record now": in an agent's chat the agent's status, how its messages
// go now, the people it holds and the plugin routed to send its messages; in a chat about no agent yet, that none is
// created, how messages go for every agent and the plugin the routes send them by. It is read when the card is drawn as
// the conversation's last card that reads, and again on its Refresh; an earlier one stands as a stub until "Show now".
// It never follows the journal, and nothing it read is kept in the conversation. The card creates, approves and sends
// nothing: its question's options send their words as a choice's do, and in Plan only the plan's own card decides.

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

/** What the runtime said when a read failed: its code, when it gave one, and its words. */
interface Said {
    code: string | null;
    message: string;
}

const saidOf = (error: unknown): Said =>
    error instanceof ApiError
        ? { code: error.code, message: error.message }
        : { code: null, message: error instanceof Error ? error.message : String(error) };

/** One of the reads: what it answered, or what the runtime said instead. */
type Part<T> = { ok: true; value: T } | { ok: false; said: Said };

async function part<T>(read: Promise<T>): Promise<Part<T>> {
    try {
        return { ok: true, value: await read };
    } catch (error) {
        return { ok: false, said: saidOf(error) };
    }
}

/** The plugin that would send a campaign's messages, or null when no route sends them anywhere. */
type Sender = { plugin: string; usable: boolean; problems: ErrorDetail[] } | null;

/** What part 2 read: of an agent, of a chat about no agent yet. */
type Facts =
    | { place: 'agent'; state: 'gone' }
    | { place: 'agent'; state: 'failed'; said: Said }
    | {
          place: 'agent';
          state: 'read';
          campaign: Campaign;
          people: Part<number | null>;
          sender: Part<Sender>;
      }
    | { place: 'setup'; modes: Part<SystemInfo['send_modes']>; sender: Part<string | null> };

/**
 * An agent's facts: its row first (an agent this installation does not hold is said so, and nothing else is read),
 * then, side by side, its people now and where its messages go.
 */
async function readAgent(api: Api, campaignId: string): Promise<Facts> {
    let campaign: Campaign;
    try {
        campaign = await api.call<Campaign>('campaign.get', { campaign_id: campaignId });
    } catch (error) {
        const said = saidOf(error);
        return said.code === 'campaign_not_found'
            ? { place: 'agent', state: 'gone' }
            : { place: 'agent', state: 'failed', said };
    }
    const [people, sender] = await Promise.all([
        part(
            api
                .call<CampaignSummaryResponse>('campaign.summary', {
                    campaign_ids: [campaignId],
                    exclude_archived_contacts: true,
                })
                .then((answer) => answer.campaigns[0]?.people ?? null),
        ),
        api
            .call<RouteResolution>('route.resolve', { campaign_id: campaignId, operation: 'message.send' })
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
            }),
    ]);
    return { place: 'agent', state: 'read', campaign, people, sender };
}

/**
 * The plugin the routes send a new agent's messages by: the global route of `message.send` when one is set, else the
 * global default; null for none.
 */
const globalSender = (routes: Routes): string | null =>
    routes.global.operations['message.send']?.plugin_id ?? routes.global.default?.plugin_id ?? null;

/** A chat about no agent yet: how messages go for every agent, and the plugin the routes send them by. */
async function readSetup(api: Api): Promise<Facts> {
    const [modes, sender] = await Promise.all([
        part(api.call<SystemInfo>('system.info').then((info) => info.send_modes)),
        part(api.call<Routes>('route.list', {}).then(globalSender)),
    ]);
    return { place: 'setup', modes, sender };
}

const readFacts = (chat: Pick<Chat, 'main' | 'campaignId'>): Promise<Facts> =>
    placeOf(chat) === 'agent' ? readAgent(here().api, chat.campaignId!) : readSetup(here().api);

/** One read of part 2: under way, or answered at a moment. */
type Read = { serial: number } & ({ state: 'reading' } | { state: 'read'; at: number; facts: Facts });

/** What one plan card shows of its own: its read, and which of its drafts are open, for the entry it was drawn from. */
interface Shown {
    entry: Entry;
    read: Read | null;
    open: readonly number[];
}

/** By chat id, then by the index of the text entry that holds the plan: what each plan card drawn has read and opened. */
export const proposals = signal<Readonly<Record<string, Readonly<Record<number, Shown>>>>>({});

/**
 * Whether what a card holds was drawn from this entry: the same entry, or the same words with the mark of a Plan only
 * turn's plan put on as the turn ended or taken off as the chat left Plan only. A conversation opened again draws entries
 * of its own, marked as they were kept, so it starts afresh.
 */
const sameEntry = (held: Entry, entry: Entry): boolean =>
    held === entry ||
    (held.kind === 'text' &&
        entry.kind === 'text' &&
        (held.plan === true) !== (entry.plan === true) &&
        held.text === entry.text &&
        held.at === entry.at);

function shownOf(chatId: string, index: number, entry: Entry): Shown | null {
    const shown = proposals.value[chatId]?.[index];
    return shown !== undefined && sameEntry(shown.entry, entry) ? shown : null;
}

function put(chatId: string, index: number, shown: Shown): void {
    proposals.value = { ...proposals.value, [chatId]: { ...proposals.value[chatId], [index]: shown } };
}

let serials = 0;

/** Reads part 2 now: "Reading..." until it is answered, then what it answered and when. A read under way is left to finish. */
function startRead(chat: Chat, index: number): void {
    const entry = chat.entries[index]!;
    const shown = shownOf(chat.id, index, entry);
    if (shown?.read?.state === 'reading') return;
    const serial = ++serials;
    put(chat.id, index, { entry, open: shown?.open ?? [], read: { serial, state: 'reading' } });
    void readFacts(chat).then((facts) => {
        // Only the read the card waits for lands; a conversation drawn afresh since has forgotten it.
        const now = proposals.value[chat.id]?.[index];
        if (now?.read?.serial === serial)
            put(chat.id, index, { ...now, read: { serial, state: 'read', at: Date.now(), facts } });
    });
}

/** The cards asked to read while a thread was drawn, read once the drawing is done: a drawing changes nothing. */
const asked = new Set<string>();

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
 * The index of the conversation's last card that reads, among the cards its turns draw and the plans its Plan only turns
 * end in; null when none reads. The plan is the one kind of card this window draws that reads.
 */
export function lastReading(chat: Chat, cards: ReadonlyMap<number, PlacedCard>): number | null {
    let last: number | null = null;
    for (const [index, placed] of cards) {
        const reading = readCard(placed, {
            place: placeOf(chat),
            campaignId: chat.campaignId,
            ended: !placed.lastTurn || !chat.running,
        });
        if (reading.state === 'card' && reading.card.kind === 'plan' && (last === null || index > last)) last = index;
    }
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

/** A campaign's status in a sentence chosen from the status alone: what the dispatcher's eligibility rule says of it. */
function statusSentence(status: string): string {
    switch (status) {
        case 'draft':
            return 'It is a draft: none of its work starts until it is started.';
        case 'active':
            return 'It is active.';
        case 'paused':
            return 'It is paused: none of its work starts but what stops it.';
        case 'archived':
            return 'It is archived.';
        default:
            return `Its status is ${status}.`;
    }
}

const people = (count: number): string => (count === 1 ? '1 person' : `${count} people`);

/** A refusal of the runtime's, as the waiting cards say one: its code, then its words. */
const saidView = (said: Said): Html =>
    html`<div class="note am card-said" role="alert">
        ${said.code === null ? null : html`<code>${said.code}</code> `}${said.message}
    </div>`;

/** How a send to each kind of person is decided, in the send-modes screen's words; nothing from a runtime that does not say. */
function modesView(inForce: { cold: ModeInForce; warm: ModeInForce } | undefined): Html | null {
    if (inForce === undefined) return null;
    return html`<div class="card-lab">${t('modes.settings.heading')}</div>
        <ul class="card-modes">
            <li>${t('modes.kind.cold')}: <b>${modeWords(inForce.cold.mode).name}</b></li>
            <li>${t('modes.kind.warm')}: <b>${modeWords(inForce.warm.mode).name}</b></li>
        </ul>`;
}

/** The plugin routed to send messages, and whether it could take them now, with each problem the claim would name. */
function senderView(sender: Part<Sender | string | null>, none: string): Html {
    if (!sender.ok)
        return html`<div class="card-lab">Sending</div>
            ${saidView(sender.said)}`;
    const value = sender.value;
    if (value === null)
        return html`<div class="card-lab">Sending</div>
            <div class="card-line">${none}</div>`;
    if (typeof value === 'string')
        return html`<div class="card-lab">Sending</div>
            <div class="card-line">Plugin that sends messages: <b>${value}</b></div>`;
    return html`<div class="card-lab">Sending</div>
        <div class="card-line">Plugin that sends messages: <b>${value.plugin}</b></div>
        ${
            value.usable
                ? html`<div class="card-line">It can send this agent’s messages now.</div>`
                : html`<div class="card-line">It cannot send this agent’s messages now:</div>
                      <ul class="card-problems">
                          ${value.problems.map((each) => html`<li><code>${each.code}</code> ${each.message}</li>`)}
                      </ul>`
        }`;
}

function factsView(facts: Facts): Html {
    if (facts.place === 'setup')
        return html`<div class="card-line">No agent is created yet.</div>
            ${
                facts.modes.ok
                    ? modesView(facts.modes.value)
                    : html`<div class="card-lab">${t('modes.settings.heading')}</div>
                          ${saidView(facts.modes.said)}`
            }
            ${senderView(facts.sender, 'No plugin is routed to send messages yet.')}`;
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
        ${senderView(facts.sender, 'No plugin is routed to send messages for this agent.')}`;
}

/** Part 2: "On record now", the minute it was read and Refresh in its first row; or, as a stub, "Show now" alone. */
function recordView(chatId: string, index: number, read: Read | null, stub: boolean): Html {
    const control = act('card.plan', chatId, index);
    if (stub && read === null)
        return html`<div class="card-facts stub">
            <div class="ask-h card-top">
                <span class="card-rec">On record now</span>
                <button class="btn sm card-read" type="button" ${control}>Show now</button>
            </div>
        </div>`;
    const answered = read?.state === 'read' ? read : null;
    return html`<div class="card-facts">
        <div class="ask-h card-top">
            <span class="card-rec">On record now</span>
            ${answered === null ? null : html`<span class="meta card-asof">As of ${clock(new Date(answered.at), zone)}</span>`}
            <button class="btn sm ghost card-read" type="button" ${answered === null ? html`disabled` : control}>
                Refresh
            </button>
        </div>
        ${answered === null ? html`<div class="card-reading" role="status">Reading...</div>` : factsView(answered.facts)}
    </div>`;
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
        <div class="card-who">Jason’s proposal</div>
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
                                                              <div class="card-who">Jason’s draft</div>
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
        <div class="card-who">Jason asks</div>
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
 * The plan card, drawn from the AI's block: its two parts, and under them `asks`, its question as the thread draws it.
 * Part 2 reads when the card is the conversation's last that reads (`last`) and this drawing has not read it; a thread is
 * drawn inside a computed value, so the read starts once the drawing is done. Otherwise it is a stub.
 */
export function planView(chat: Chat, index: number, card: PlanCard, last: number | null, asks: Html | null): Html {
    const entry = chat.entries[index]!;
    const shown = shownOf(chat.id, index, entry);
    const read = shown?.read ?? null;
    if (read === null && index === last) {
        const key = `${chat.id} ${index}`;
        if (!asked.has(key)) {
            asked.add(key);
            queueMicrotask(() => {
                asked.delete(key);
                const now = chats.value[chat.id];
                const held = now?.entries[index];
                if (now === undefined || held === undefined || !sameEntry(entry, held)) return;
                if ((shownOf(now.id, index, held)?.read ?? null) === null) startRead(now, index);
            });
        }
    }
    return html`<div class="ask chat-card chat-proposal" role="group" aria-label="Jason’s proposal">
        ${proposalPart(chat.id, index, card, shown?.open ?? [])} ${recordView(chat.id, index, read, index !== last)}
        ${asks}
    </div>`;
}

/** "Refresh" and "Show now": part 2 read again. It decides nothing. */
define('card.plan', ([chatId, index]) => {
    const at = planAt(chatId, index);
    if (at !== null) startRead(at.chat, index as number);
});

/** "Show the draft" and "Hide the draft": one draft opened or closed in place. It sends nothing. */
define('card.planDraft', ([chatId, index, step]) => {
    const at = planAt(chatId, index);
    if (at === null || typeof step !== 'number' || at.card.steps?.[step]?.body === undefined) return;
    const entry = at.chat.entries[index as number]!;
    const shown = shownOf(at.chat.id, index as number, entry);
    const open = shown?.open ?? [];
    put(at.chat.id, index as number, {
        entry,
        read: shown?.read ?? null,
        open: open.includes(step) ? open.filter((each) => each !== step) : [...open, step],
    });
});
