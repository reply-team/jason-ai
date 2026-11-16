import { computed, effect } from '@preact/signals-react';
import { useCallback, useEffect, useMemo, useRef, type FormEvent, type KeyboardEvent, type RefObject } from 'react';
import { useApi } from '../api/context';
import type { JournalEntry } from '../api/types';
import type { DesktopChatMode, DesktopHost, DesktopRefusal } from '../platform/desktop';
import { t } from '../platform/i18n';
import { act, define } from './actions';
import { acts, readActs } from './card-acts';
import { askedAsWords, forgetProposals, lastReadingOrPlan, planView, proposalOf, proposals } from './card-plan';
import { accountsCard } from './card-accounts';
import { aiCaption, cardHead, cardReads, forgetCards } from './card-frame';
import { needsCard } from './card-needs';
import { peopleCard } from './card-people';
import { statusCard } from './card-status';
import {
    cardLayout,
    openChoice,
    optionsOf,
    placeOf,
    readCard,
    type CardKind,
    type CardOption,
    type ChoiceCard,
    type PlacedCard,
} from './cards';
import {
    afresh,
    answered,
    autoEditLine,
    choiceState,
    codexConfigLine,
    codexHosts,
    codexReachLine,
    failed,
    fromRecord,
    fullAccessLine,
    handed,
    kept,
    livePlan,
    planElsewhere,
    lost,
    modeChoices,
    moded,
    newChat,
    onHost,
    sendHeld,
    pickedFor,
    planApproved,
    planOtherHost,
    planGo,
    planLine,
    codexPlanLine,
    planModeMoved,
    replaced,
    said,
    shown,
    stopAsked,
    takenBack,
    told,
    turnOptions,
    typed,
    type Answer,
    type Chat,
    type Entry,
} from './chat-model';
import { campaigns, reload as reloadCampaigns, summary } from './campaigns';
import { chipsUnder, openingChips, type Agent, type Chip } from './chips';
import { toFeedLine, toFeedRow } from './feed';
import { html, type Html } from './html';
import { markdownView } from './markdown-view';
import { svg } from './icons';
import { chatHosts, isSignInFailure, signInSentence, toCampaignRow } from './live';
import { CampaignMoves } from '../screens/campaigns/moves';
import { Painted } from './painted';
import { AccessButton, Glyph, ModeMark, ModelButton, modeSays, useFit } from './start-menus';
import { WaitingCards } from './waiting-cards';
import { offersReply, readEnrolRoute } from './enrol-route';
import { can } from '../platform/places';
import { chatPlace, dropDialogs, replyCardFor, replyDismissed, reload as listHosts } from './connect';
import {
    changeChat,
    chatOf,
    chats,
    clock,
    conversations,
    conversationsFailure,
    feedId,
    here,
    inTab,
    messageOf,
    setup,
} from './state';
import { fullAccessStanding, standingKnown, takeBackStanding } from '../platform/full-access';

// The Live chat: the person's words go to the agent host the desktop runs, and what the agent says and does comes
// back as events. The agent's words are drawn as a fixed subset of Markdown, every word of them as text. A message sent
// while a turn runs waits in the page, and the page hands it to the desktop as the next turn when the turn ends well.
// Every chat is a conversation the desktop keeps: a campaign's one, a set-up chat about no campaign yet, or the Feed, the
// person's main chat, into which the page hands the journal's lines and which draws each as a row linking to its
// campaign. The desktop decides which campaign a turn is about, from its record; the page names the conversation, the
// host, and the model and effort chosen in the box, nothing more. A line never starts a turn.
// The menu beside the composer asks the desktop for the conversation's access mode; the desktop confirms Full access
// with the person itself, and the page shows the mode the desktop answers with.

const answerWords: Readonly<Record<Answer, string>> = {
    allow: 'Allowed',
    always: 'Allowed for this chat',
    deny: 'Not allowed',
    gone: 'This request has ended',
};

/** Whether the desktop answered a call with a refusal: its sentence, as a value, never a thrown error. */
export const isRefusal = (answer: unknown): answer is DesktopRefusal =>
    typeof answer === 'object' && answer !== null && typeof (answer as { refused?: unknown }).refused === 'string';

/** A refusal the desktop answered, thrown on as an Error whose message is the sentence alone. */
function unless<T>(answer: T | DesktopRefusal): T {
    if (isRefusal(answer)) throw new Error(answer.refused);
    return answer;
}

/**
 * The Feed, made by the desktop on the first ask: its id is kept for the sidebar. The ask also has the desktop read back
 * what was written while no page followed the journal, as each load of the page asks. A refusal or a failure leaves the
 * sidebar without it, and is said there.
 */
export async function askFeed(): Promise<void> {
    if (!can('chat.feed')) return;
    try {
        const feed = unless(await here().desktop.chat.feed([], { catchUp: true }));
        feedId.value = feed.id;
        void reloadConversations();
    } catch (error) {
        conversationsFailure.value = messageOf(error);
    }
}

/** The stream started afresh, after a resume the runtime refused: the desktop reads back what the page missed. */
export function catchUpFeed(): void {
    if (!can('chat.feed')) return;
    here()
        .desktop.chat.feed([], { catchUp: true })
        .catch(() => undefined);
}

/** Hands the desktop a line of the journal's stream when the Feed takes its kind; the desktop checks it again. */
export function handLine(line: JournalEntry): void {
    if (!can('chat.feed')) return;
    const handed = toFeedLine(line);
    if (handed === null) return;
    here()
        .desktop.chat.feed([handed])
        .catch(() => undefined);
}

/** Lists the conversations the desktop keeps again; one it could not list is said in the sidebar. */
export async function reloadConversations(): Promise<void> {
    try {
        const listed = await here().desktop.chat.list();
        conversations.value = listed;
        conversationsFailure.value = null;
    } catch (error) {
        conversationsFailure.value = messageOf(error);
    }
}

/** Holds a conversation in the console, read from the desktop once: what the page drew before comes with it. */
async function hold(id: string): Promise<string> {
    if (chats.value[id]) return id;
    const record = unless(await here().desktop.chat.open(id));
    if (!chats.value[record.id]) {
        // A conversation opened afresh reads its last card that reads again, draws the earlier ones as stubs, and its plan
        // cards hold nothing they held before: none of it belongs to these entries.
        forgetCards(record.id);
        forgetProposals(record.id);
        chats.value = { ...chats.value, [record.id]: fromRecord(record, null) };
    }
    return record.id;
}

/**
 * The conversation to show: the campaign's one, made on first use and opened with what it kept, or with no campaign
 * a new set-up chat. Resolves with its id, or with null when the desktop refused, which the sidebar says.
 */
export async function startChat(
    campaign?: { id: string; name: string },
    options: { askFirst?: boolean } = {},
): Promise<string | null> {
    try {
        // Named Ask first where the person changed it before sending: no host is named, so the desktop starts it there.
        const host = options.askFirst === true ? undefined : await newChatHost();
        const { desktop } = here();
        let id: string;
        if (campaign) {
            const made = unless(
                await (host === undefined ? desktop.chat.create(campaign.id) : desktop.chat.create(campaign.id, host)),
            );
            id = await hold(made.id);
        } else {
            const made = unless(
                await (host === undefined ? desktop.chat.create() : desktop.chat.create(undefined, host)),
            );
            // The mode the desktop made it in, Full access by the standing choice among them: its line shows at once.
            chats.value = {
                ...chats.value,
                [made.id]: { ...newChat(made.id, null), title: made.title, mode: made.mode },
            };
            id = made.id;
        }
        conversationsFailure.value = null;
        void reloadConversations();
        return id;
    } catch (error) {
        conversationsFailure.value = messageOf(error);
        return null;
    }
}

/**
 * The host a new chat's first turn runs on, named to the desktop only while the standing choice says new chats start in
 * Full access, so it can tell whether that host takes Full access; otherwise nothing is named, as before.
 */
async function newChatHost(): Promise<string | undefined> {
    await standingKnown();
    if (fullAccessStanding.value?.newChats !== true) return undefined;
    if (setup.value === null) await listHosts();
    return hostOf(newChat('', null))?.id;
}

/** The take-back in the mode menu: the desktop forgets the standing choice; the chat stays in the mode it is in. */
export function takeBackChoice(id: string): void {
    if (!can('fullAccess.takeBack')) return;
    takeBackStanding(here().desktop)
        .then((refused) => {
            if (refused !== null) changeChat(id, (chat) => told(chat, refused));
        })
        .catch((error: unknown) => changeChat(id, (chat) => told(chat, messageOf(error))));
}

/** A conversation from the sidebar's list: the one held, else the one the desktop kept. Null when it refused. */
export async function openConversation(id: string): Promise<string | null> {
    try {
        const held = await hold(id);
        conversationsFailure.value = null;
        return held;
    } catch (error) {
        conversationsFailure.value = messageOf(error);
        return null;
    }
}

/** "Start afresh": the desktop drops the hosts' sessions and keeps the conversation; a refusal is said in the chat. */
export function restartChat(id: string): void {
    if (!can('chat.restart')) return;
    here()
        .desktop.chat.restart(id)
        .then((answer) => {
            if (isRefusal(answer)) return changeChat(id, (chat) => told(chat, answer.refused));
            changeChat(id, afresh);
            // The desktop keeps the conversation changed now: the list says it first.
            void reloadConversations();
        })
        .catch((error: unknown) => changeChat(id, (chat) => told(chat, messageOf(error))));
}

/**
 * Renames the conversation to the person's name: the desktop keeps it, and nothing replaces it afterwards. The box
 * closes at once; a refusal is said in the chat in the desktop's words, and the name stays as it was.
 */
export function renameChat(id: string, title: string): void {
    changeChat(id, (chat) => ({ ...chat, renaming: null }));
    here()
        .desktop.chat.rename(id, title)
        .then((answer) => {
            if (isRefusal(answer)) return changeChat(id, (chat) => told(chat, answer.refused));
            changeChat(id, (chat) => ({ ...chat, title: answer.title }));
            void reloadConversations();
        })
        .catch((error: unknown) => changeChat(id, (chat) => told(chat, messageOf(error))));
}

/**
 * Asks the desktop for the conversation's access mode. The page changes nothing until the desktop answers: Full access
 * waits for the person's yes to the desktop's own box, and a no answers the mode as it was. A refusal is said in the
 * chat in the desktop's words.
 */
export function chooseMode(id: string, mode: DesktopChatMode): void {
    if (!can('chat.setMode')) return;
    const chat = chats.value[id];
    if (!chat) return;
    // A change to a narrower mode while a turn runs stops that turn, as Stop does: leaving Full access, leaving Auto-edit
    // for Ask first or Plan only, or a change to Plan only; Auto-edit to Full access lets it finish. The waiting message is
    // given back, and no turn follows by itself.
    const stops = breadth[mode] < breadth[chat.mode];
    if (chat.running && stops) changeChat(id, stopAsked);
    here()
        .desktop.chat.setMode(id, mode, hostOf(chat)?.id ?? '')
        .then((answer) => {
            if (isRefusal(answer)) return changeChat(id, (chat) => told(chat, answer.refused));
            changeChat(id, (chat) => moded(chat, answer.mode));
        })
        .catch((error: unknown) => changeChat(id, (chat) => told(chat, messageOf(error))));
}

/**
 * "Approve and run" on the chat's plan card: the menu's own change to Ask first (`chat.setMode`, the mode `ask_first` and
 * never another), then the go, sent as the person's message and naming Ask first, so the desktop refuses it when the
 * chat's mode moved in between. A card that is not live, approving or approved does nothing: a second press, like a
 * reload or a restart, finds no card to approve. A refusal is said in the chat in the desktop's words, and nothing is sent.
 */
export function approvePlan(id: string): void {
    if (!can('chat.setMode')) return;
    const chat = chats.value[id];
    const host = chat ? hostOf(chat) : null;
    if (!chat || !host || !livePlan(chat, host.id)) return;
    changeChat(id, (each) => ({ ...each, plan: 'approving' }));
    here()
        .desktop.chat.setMode(id, 'ask_first', host.id)
        .then((answer) => {
            if (isRefusal(answer)) return changeChat(id, (each) => told({ ...each, plan: null }, answer.refused));
            // Another choice from the menu came first: the chat is in the mode the desktop says, and the go is not sent.
            if (answer.mode !== 'ask_first')
                return changeChat(id, (each) => told(moded({ ...each, plan: null }, answer.mode), planModeMoved));
            changeChat(id, (each) => moded({ ...each, plan: 'approved' }, answer.mode));
            sendTurn(id, planGo, false, 'ask_first');
        })
        .catch((error: unknown) => changeChat(id, (each) => told({ ...each, plan: null }, messageOf(error))));
}

/** How much each mode lets a turn do without asking, narrow to wide: the desktop's own order for stopping a turn. */
const breadth: Readonly<Record<DesktopChatMode, number>> = { plan_only: 0, ask_first: 1, auto_edit: 2, full_access: 3 };

/** The most characters a conversation's name keeps, as the desktop cuts it. */
const nameLength = 60;

/** Removes the conversation, asked twice: Start is shown in its place and the list follows; a refusal is said in the chat. */
export function removeChat(id: string): void {
    if (!can('chat.remove')) return;
    changeChat(id, (chat) => ({ ...chat, removing: false }));
    here()
        .desktop.chat.remove(id)
        .then((answer) => {
            if (isRefusal(answer)) return changeChat(id, (chat) => told(chat, answer.refused));
            here().dock.dropChat(id);
            chats.value = Object.fromEntries(Object.entries(chats.value).filter(([each]) => each !== id));
            void reloadConversations();
        })
        .catch((error: unknown) => changeChat(id, (chat) => told(chat, messageOf(error))));
}

/**
 * The host a chat's next turn runs on: the one chosen, else the first found that drives the chat. A chosen host that is
 * held, below the floor or its version not read, is never traded for another: the chat's session lives on that host, and
 * a turn elsewhere would start afresh. No turn runs until it is updated and checked again, or the person chooses another.
 */
export function hostOf(chat: Chat): DesktopHost | null {
    const { drive } = chatHosts(setup.value?.hosts ?? []);
    if (heldHostOf(chat) !== null) return null;
    return drive.find((each) => each.id === chat.host) ?? drive[0] ?? null;
}

/** The host the chat chose when it is found and would drive the chat but is held; null otherwise. */
export function heldHostOf(chat: Chat): DesktopHost | null {
    if (chat.host === null) return null;
    return chatHosts(setup.value?.hosts ?? []).held.find((each) => each.id === chat.host) ?? null;
}

/**
 * Starts a turn on the host chosen now; the desktop takes the campaign from the conversation. `resent` says the words were the waiting message: a
 * turn of those the page cannot start ends with its error, and the words are given back as a failed turn gives them.
 */
export function sendTurn(id: string, text: string, resent = false, mode?: DesktopChatMode): void {
    // A chat a tab may not send to takes no message from it, whichever way the message came.
    if (inTab() && !tabMaySend(chats.value[id]?.mode ?? '')) return;
    const chat = chats.value[id];
    const words = text.trim();
    if (!chat || chat.running || !words) return;
    const host = hostOf(chat);
    if (!host) {
        // A host that was found and is held says what holds it, below the floor or a version not read, in its own mark.
        const held = (heldHostOf(chat) ?? chatHosts(setup.value?.hosts ?? []).held[0])?.belowFloor;
        if (resent)
            changeChat(id, (each) =>
                failed(said(each, words, true), held ?? 'No AI host that drives the chat was found on this machine.'),
            );
        return;
    }
    // A chat that named no host yet is on the one its turn runs on from now, so a hold on that host later holds the chat.
    // The host the turn goes to is the one whose session has its answer, a plan included (`livePlan`).
    changeChat(id, (each) => said({ ...each, host: each.host ?? host.id, turnHost: host.id }, words, resent));
    here()
        .desktop.chat.send(id, words, { ...turnOptions(onHost(chat, host.id), host), ...(mode ? { mode } : {}) })
        .then((answer) => {
            if (isRefusal(answer)) return changeChat(id, (each) => failed(each, answer.refused));
            // From its first turn the desktop keeps the conversation, and lists it.
            void reloadConversations();
        })
        .catch((error: unknown) => changeChat(id, (each) => failed(each, messageOf(error))));
}

// A turn that ended well made the waiting message the next turn: it is sent once, from here, by sendTurn. The desktop
// forgets the turn before it says the turn is done, so this send is never refused as one made while a turn runs.
effect(() => {
    const ready = Object.values(chats.value).filter((chat) => chat.outbox !== null);
    for (const chat of ready) {
        changeChat(chat.id, handed);
        sendTurn(chat.id, chat.outbox!, true);
    }
});

define('chat.suggest', ([id, text]) => {
    if (typeof id === 'string' && typeof text === 'string') sendTurn(id, text);
});

define('chat.approvePlan', ([id]) => {
    // A tab never says yes, and never reaches what only the desktop's window may.
    if (!can('chat.setMode')) return;
    if (typeof id === 'string') approvePlan(id);
});

/** "Keep planning": the card is put aside in this page, the chat stays in Plan only, and the message box takes focus. */
define('chat.keepPlanning', ([id], element) => {
    if (typeof id !== 'string') return;
    const chat = chats.value[id];
    if (!chat || !livePlan(chat, hostOf(chat)?.id ?? null)) return;
    // The box is found before the thread is drawn again, which takes the pressed button out of the page.
    const box = element.closest('.chat-panel')?.querySelector<HTMLTextAreaElement>('textarea');
    changeChat(id, (each) => ({ ...each, plan: 'kept' }));
    box?.focus();
});

define('chat.answer', ([id, request, answer]) => {
    // A tab never says yes, and never reaches what only the desktop's window may.
    if (!can('chat.answer')) return;
    if (typeof id !== 'string' || typeof request !== 'string') return;
    if (answer !== 'allow' && answer !== 'always' && answer !== 'deny') return;
    changeChat(id, (chat) => answered(chat, request, answer));
    here()
        .desktop.chat.answer(id, request, { allow: answer !== 'deny', always: answer === 'always' })
        .catch(() => changeChat(id, (chat) => lost(chat, request)));
});

define('feed.campaign', ([id]) => {
    // A tab never says yes, and never reaches what only the desktop's window may.
    if (inTab()) return;
    if (typeof id !== 'string') return;
    const name = campaigns.value.find((each) => each.id === id)?.name ?? id;
    here().dock.chat({ id, name });
});

define('feed.needs', () => {
    if (!inTab()) here().dock.show('needs');
});

/** The Open folder button of the notice that says a Full access folder is kept: the desktop opens that folder alone. */
define('chat.openFolder', ([id, name]) => {
    // A tab never says yes, and never reaches what only the desktop's window may.
    if (!can('chat.openFolder')) return;
    if (typeof id !== 'string' || typeof name !== 'string') return;
    here()
        .desktop.chat.openFolder(name)
        .then((answer) => {
            if (isRefusal(answer)) changeChat(id, (chat) => told(chat, answer.refused));
        })
        .catch((error: unknown) => changeChat(id, (chat) => told(chat, messageOf(error))));
});

define('chat.attach', ([id, campaignId]) => {
    // A tab never says yes, and never reaches what only the desktop's window may.
    if (!can('chat.attach')) return;
    if (typeof id !== 'string' || typeof campaignId !== 'string') return;
    here()
        .desktop.chat.attach(id, campaignId)
        .then((conversation) => {
            if (isRefusal(conversation)) return changeChat(id, (chat) => told(chat, conversation.refused));
            changeChat(id, (chat) => pickedFor(chat, conversation));
            void reloadConversations();
        })
        .catch((error: unknown) => changeChat(id, (chat) => told(chat, messageOf(error))));
});

/** "Sign in once by running `claude` …", with the command as code. */
const withCode = (sentence: string): Html[] =>
    sentence.split('`').map((part, at) => (at % 2 ? html`<code>${part}</code>` : html`${part}`));

/** In a browser tab a card is only shown: the desktop's own window answers it. */
const answeredInDesktop = t('chat.tab.answered');

/** A tab may send only to a chat in Ask first or Plan only, as the bridge does; any other mode, an unknown one too, is locked. */
export const tabMaySend = (mode: string): boolean => mode === 'ask_first' || mode === 'plan_only';

/** The sentence beside a locked box: it names the mode. */
export const tabLockedSentence = (mode: string): string =>
    mode === 'full_access' ? t('chat.tab.full') : mode === 'auto_edit' ? t('chat.tab.auto') : t('chat.tab.other');

function permission(chat: Chat, entry: Extract<Chat['entries'][number], { kind: 'permission' }>): Html {
    const buttons =
        entry.answer === null && inTab()
            ? html`<div class="perm-done">${answeredInDesktop}</div>`
            : entry.answer === null
              ? html`<div class="ask-a">
                    <button class="btn sm p" type="button" ${act('chat.answer', chat.id, entry.request, 'allow')}>
                        Allow
                    </button>
                    ${
                        entry.remember !== null
                            ? html`<button
                                  class="btn sm"
                                  type="button"
                                  ${act('chat.answer', chat.id, entry.request, 'always')}
                              >
                                  Allow for this chat: ${entry.remember}
                              </button>`
                            : null
                    }
                    <button class="btn sm" type="button" ${act('chat.answer', chat.id, entry.request, 'deny')}>
                        Don't allow
                    </button>
                </div>`
              : html`<div class="perm-done">${answerWords[entry.answer]}</div>`;
    return html`<div class="ask perm">
        <div class="ask-h">${svg('shield', 14)}<b>${entry.title}</b></div>
        <pre class="perm-detail">${entry.detail}</pre>
        ${buttons}
    </div>`;
}

/**
 * A Plan only turn's plan, as a card: its words drawn as text, never as markup; a line saying what Approve and run does
 * on the host the go would go to now; and, while the card is live (`livePlan`), its two buttons. A card of the past,
 * approved or put aside, keeps the words and drops the buttons; one another host made says to ask for the plan again.
 */
function planCard(
    chat: Chat,
    entry: Extract<Entry, { kind: 'text' }>,
    host: DesktopHost | null,
    words: string = entry.text,
    drawn: Html | null = null,
    drafts = false,
): Html {
    // Approve and run is offered over what a person can read: never over a plan of none.
    const last = entry === chat.entries.at(-1) && (words.trim() !== '' || drawn !== null);
    const live = last && livePlan(chat, host?.id ?? null);
    const elsewhere = last && planElsewhere(chat, host?.id ?? null);
    const approving = last && chat.plan === 'approving';
    // Only one plan is marked at a time, so the approved one is this one, whatever was said after it.
    const line =
        chat.plan === 'approved'
            ? planApproved
            : elsewhere
              ? planOtherHost
              : live || approving
                ? host !== null && codexHosts.has(host.id)
                    ? codexPlanLine
                    : planLine
                : null;
    return html`<div class="ask chat-plan">
        <div class="ask-h">${svg('journal', 14)}<b>Plan</b></div>
        ${words.trim() === '' ? null : html`<div class="said plan-text">${words}</div>`} ${drawn}
        ${line === null ? null : html`<div class="ask-w">${line}</div>`}
        ${drafts && (live || approving) ? html`<div class="ask-w plan-drafts">${planDrafts}</div>` : null}
        ${
            (live || approving) && inTab()
                ? html`<div class="perm-done">${answeredInDesktop}</div>`
                : live || approving
                  ? html`<div class="ask-a">
                        <button
                            class="btn sm p"
                            type="button"
                            ${approving ? html`disabled` : null}
                            ${act('chat.approvePlan', chat.id)}
                        >
                            ${approving ? 'Approving…' : 'Approve and run'}
                        </button>
                        <button
                            class="btn sm"
                            type="button"
                            ${approving ? html`disabled` : null}
                            ${act('chat.keepPlanning', chat.id)}
                        >
                            Keep planning
                        </button>
                    </div>`
                  : null
        }
    </div>`;
}

/** Said over Approve and run where the plan drawn in it has drafts: what it approves is the plan as written. */
const planDrafts = 'Approve and run approves this plan as written, its drafts included, open or not.';

/** A campaign a pick names: by the name the console read for it, else by its id. */
const campaignName = (id: string): string => campaigns.value.find((each) => each.id === id)?.name ?? id;

/**
 * The campaigns a set-up turn made. Only the chat's last pick is a choice, as only it is one the desktop takes, and only
 * while the chat is about no campaign and no turn runs; an earlier one is drawn as text.
 */
function pick(chat: Chat, entry: Extract<Entry, { kind: 'pick' }>, last: boolean): Html {
    const open = last && chat.campaignId === null && !chat.running && !inTab();
    return html`<div class="ask chat-pick">
        <div class="ask-h">
            ${svg('camp', 14)}<b>${open ? 'Make this the conversation of' : 'Agents this chat made'}</b>
        </div>
        ${
            open
                ? html`<div class="ask-a">
                      ${entry.campaignIds.map(
                          (id) =>
                              html`<button class="btn sm" type="button" ${act('chat.attach', chat.id, id)}>
                                  ${campaignName(id)}
                              </button>`,
                      )}
                  </div>`
                : html`<div class="perm-done">${entry.campaignIds.map(campaignName).join(', ')}</div>`
        }
    </div>`;
}

/** One line in the desktop's words for something the page alone says of the conversation. */
const noteLine = (text: string): Html => html`<div class="note chat-note">${text}</div>`;

/** A kind as the line of a card that did not hold what it needs names it. */
const kindWords: Readonly<Record<CardKind, string>> = {
    choice: 'a choice',
    people_list: 'a people list',
    agent_status: 'an agent status',
    needs: 'a needs',
    accounts: 'an accounts',
    plan: 'a plan',
};

/** The line drawn in place of a card that did not draw, in the desktop's words; never anything the block held. */
export const cardRefusedLine = (kind: CardKind | 'unknown', failure: string): string =>
    failure === 'kind_unknown'
        ? 'Jason asked for a card this app does not draw.'
        : failure === 'too_long'
          ? 'Jason’s card was too long to read, so it is not shown.'
          : failure === 'schema' && kind !== 'unknown'
            ? `Jason’s card did not hold what ${kindWords[kind]} card needs, so it is not shown.`
            : 'Jason’s card could not be read, so it is not shown.';

/** The header of each kind's card, in the desktop's words. */
const cardHeaders: Readonly<Record<CardKind, string>> = {
    choice: 'Jason asks',
    people_list: 'People',
    agent_status: 'Agent',
    needs: 'Waiting for your decision',
    accounts: 'Accounts',
    plan: 'Jason’s proposal',
};

/** The words a choice says once it no longer stands, by why; none while it stands or when another entry locked it. */
const choiceWords: Partial<Record<ReturnType<typeof choiceState>['state'], string>> = {
    own_words: 'You answered in your own words.',
    acted: 'You acted on this agent after this was asked; write your answer below if it still stands.',
    unread: 'Whether this still stands could not be read; write your answer below.',
    reopened: 'This was asked before the chat was opened again; write your answer below.',
};

/** The entries whose card that did not draw the desktop was told of, so a redraw does not tell it again. */
const toldRefused = new WeakSet<object>();

/**
 * Tells the desktop's log, once per entry, that its card did not draw: the kind and the failure's class, nothing of the
 * block. A tab has no log of the desktop's, and an earlier desktop no such call.
 */
function tellRefused(chatId: string, entry: object, kind: CardKind | 'unknown', failure: string): void {
    if (toldRefused.has(entry)) return;
    toldRefused.add(entry);
    if (!can('chat.cardRefused')) return;
    const desktop = here().desktop;
    if (typeof desktop.chat.cardRefused !== 'function') return;
    queueMicrotask(() => void desktop.chat.cardRefused(chatId, kind, failure).catch(() => undefined));
}

/**
 * A choice the AI asked for: under its frame's label, the AI's question and its options, each a button that sends its
 * label, exactly, as the person's next message, and does nothing else. Every word of it is the AI's, drawn as text. Live
 * only while `choiceState` says so; once answered the chosen option is marked, and otherwise it says why it no longer
 * stands. In a tab that may not send it is only shown, as the plan card is.
 */
function choiceView(chat: Chat, index: number, card: ChoiceCard, host: DesktopHost | null): Html {
    return asksView(chat, index, card.question, card.options, host, 'ask chat-card chat-choice');
}

/**
 * A question the AI asked on a card, a choice's or a plan's, under the AI's name: its options, each a button that sends
 * its label, live only while `choiceState` says so.
 */
function asksView(
    chat: Chat,
    index: number,
    question: string,
    options: readonly CardOption[],
    host: DesktopHost | null,
    kind: string,
): Html {
    const maySend = host !== null && (!inTab() || tabMaySend(chat.mode));
    const state = choiceState(
        chat,
        index,
        options.map((option) => option.label),
        acts.value[chat.id] ?? null,
        maySend,
    );
    const live = state.state === 'live';
    const chosen = state.state === 'chosen' ? state.label : null;
    const said =
        choiceWords[state.state] ??
        (state.state === 'idle' && inTab() && !tabMaySend(chat.mode) ? answeredInDesktop : null);
    return html`<div class="${kind}${live ? ' live' : ' done'}" role="group" aria-label="Jason asks">
        ${aiCaption(cardHeaders.choice)}
        <div class="ask-s">${question}</div>
        <div class="ui-opts">
            ${options.map((option, at) => {
                const mine = option.label === chosen;
                return html`<button
                    class="ui-opt${mine ? ' chosen' : chosen !== null ? ' faint' : ''}"
                    type="button"
                    ${live ? act('chat.choose', chat.id, index, at) : html`disabled`}
                    ${mine ? html`aria-pressed="true"` : null}
                >
                    <span class="opt-l">${option.label}</span>
                    ${option.detail === undefined ? null : html`<span class="opt-d">${option.detail}</span>`}
                    ${mine ? html`<span class="opt-mark">${svg('check', 12)}You chose this</span>` : null}
                </button>`;
            })}
        </div>
        ${said === null ? null : html`<div class="ask-w choice-said">${said}</div>`}
    </div>`;
}

/**
 * The card a turn's last block draws, right after the text entry that held it, with no speaker's label over it: a
 * choice; a data card or a plan, what it reads drawn as a stub unless it is the conversation's last card that reads
 * (`last`); the frame that says a data card is about another agent; one line in the desktop's words for a block that did
 * not read, said once to the desktop's log; nothing for a block still being written. Every kind is drawn.
 */
function cardView(
    chat: Chat,
    index: number,
    placed: PlacedCard,
    host: DesktopHost | null,
    last: number | null,
): Html | null {
    const entry = chat.entries[index]!;
    const reading = readCard(placed, {
        place: placeOf(chat),
        campaignId: chat.campaignId,
        ended: !placed.lastTurn || !chat.running,
    });
    switch (reading.state) {
        case 'pending':
            return null;
        case 'failed':
            tellRefused(chat.id, entry, reading.kind, reading.failure);
            return noteLine(cardRefusedLine(reading.kind, reading.failure));
        case 'elsewhere': {
            const head = cardHead(cardHeaders[reading.kind]);
            return html`<div class="ask chat-card" role="group" aria-label="${head.words}">
                <div class="ask-h">${head.view}</div>
                <div class="ask-w">This chat shows only its own agent.</div>
            </div>`;
        }
        case 'card':
            // A data card reads when it is the conversation's last card that reads (`last`), else stands as a stub.
            switch (reading.card.kind) {
                case 'choice':
                    return choiceView(chat, index, reading.card, host);
                case 'people_list':
                    return peopleCard(chat, index, reading.card, last);
                case 'agent_status':
                    return statusCard(chat, index, reading.card, last);
                case 'needs':
                    return needsCard(chat, index, reading.card, last);
                case 'plan': {
                    const { question, options } = reading.card;
                    const asks =
                        question === undefined || options === undefined
                            ? null
                            : asksView(chat, index, question, options, host, 'proposal-asks');
                    return planView(chat, index, reading.card, last, asks);
                }
                case 'accounts':
                    return accountsCard(chat, index, last, cardHeaders.accounts);
            }
    }
}

/**
 * What an agent's chat reads of the person's acts: its campaign and the moment its open choice was kept; null for a
 * chat of no agent, one with no open choice, and one whose choice has no moment, which draws locked without a read.
 */
function actsAsked(
    chat: Pick<Chat, 'entries' | 'campaignId' | 'main' | 'running' | 'reopened'>,
): { campaignId: string; at: string } | null {
    if (chat.campaignId === null || chat.main) return null;
    const open = openChoice(chat);
    const entry = open === null ? undefined : chat.entries[open.index];
    return entry?.kind === 'text' && entry.at !== undefined ? { campaignId: chat.campaignId, at: entry.at } : null;
}

define('chat.choose', ([id, index, option]) => {
    if (typeof id !== 'string' || typeof index !== 'number' || typeof option !== 'number') return;
    const chat = chats.value[id];
    if (!chat) return;
    // The choice is read again from the kept entries: only the chat's open one, while it is live, sends its label.
    const asked = openChoice(chat);
    if (asked === null || asked.index !== index) return;
    const labels = optionsOf(asked.card).map((each) => each.label);
    const label = labels[option];
    const host = hostOf(chat);
    const maySend = host !== null && (!inTab() || tabMaySend(chat.mode));
    if (label === undefined || choiceState(chat, index, labels, acts.value[id] ?? null, maySend).state !== 'live')
        return;
    sendTurn(id, label);
});

/** A line of the Feed, or nothing for a line the page does not know: the kind in words, how many it stands for, its campaign as a link, its age. */
function feedLine(entry: Extract<Entry, { kind: 'line' }>): Html | null {
    const names = new Map(campaigns.value.map((each) => [each.id, each.name]));
    const row = toFeedRow(entry.line, names, clock.value);
    if (row === null) return null;
    return html`<div class="feed-line" data-id="${row.id}">
        ${svg(row.icon, 14)}<b>${row.words}</b>${row.count === null ? null : html`<span class="meta">${row.count}</span>`}
        <button class="btn sm" type="button" ${act('feed.campaign', row.campaignId)}>${row.campaign}</button>
        ${row.needs ? html`<button class="btn sm" type="button" ${act('feed.needs')}>Needs you</button>` : null}
        <span class="meta">${row.age}</span>
    </div>`;
}

export function threadView(chat: Chat, host: DesktopHost | null): Html {
    if (chat.entries.length === 0 && chat.main)
        return html`<div class="hero">
            <div class="glyph">${svg('pulse', 22)}</div>
            <h2>The Feed</h2>
            <p>
                What happens in your agents comes here: a reply, an approval or a question waiting, an agent created,
                started or paused, a message or an invitation sent or scheduled, a reply sent, work that finished or
                stopped, a provider block holding work. Ask about any of it below.
            </p>
        </div>`;
    if (chat.entries.length === 0)
        return html`<div class="hero">
                <div class="glyph">${svg('chat', 22)}</div>
                <h2>${chat.campaignId ? `What should Jason do in ${chat.title}?` : 'What should Jason work on?'}</h2>
                ${chat.mode === 'ask_first' || chat.mode === 'auto_edit' || chat.mode === 'plan_only' ? html`<p>Nothing written here reaches a person without your approval.</p>` : null}
            </div>
            <div class="sugs">${chipButtons(chat, openingChips)}</div>`;
    const sentence = host && signInSentence(host);
    const lastPick = [...chat.entries].reverse().find((entry) => entry.kind === 'pick');
    // The cards the AI asked for: each text's words without its card blocks, and each turn's last block, by the index of
    // the text entry that held it.
    const layout = cardLayout(chat.entries);
    const lastReads = lastReadingOrPlan(chat, layout.cards);
    const indexOf = new Map<object, number>(chat.entries.map((entry, index) => [entry, index]));
    // The speaker is named over the agent's first words of each turn: after the person's message, or at the start. A
    // card has no speaker's label over it: a turn's words after one are named instead.
    let named = false;
    const parts = shown(chat).map((entry) => {
        if (entry.kind === 'user') named = false;
        switch (entry.kind) {
            case 'steps':
                // A trail still running is live: it is open, and draws no arrow to turn until the turn ends.
                return html`<details class="${entry.open ? 'vsteps live' : 'vsteps'}" ${entry.open ? html`open` : null}>
                    <summary>
                        ${entry.open ? 'Working…' : entry.steps.length === 1 ? '1 action' : `${entry.steps.length} actions`}
                    </summary>
                    ${entry.steps.map(
                        (step) =>
                            html`<div class="vstep">
                                <div>
                                    <div class="chat-step-text">${step.text}</div>
                                    ${step.detail !== undefined ? html`<div class="chat-step-detail">${step.detail}</div>` : null}
                                </div>
                            </div>`,
                    )}
                </details>`;
            case 'user':
                return html`<div class="m user"><div class="bub said">${entry.text}</div></div>`;
            case 'text': {
                const index = indexOf.get(entry) ?? -1;
                const cut = layout.words.get(index);
                // A plan the desktop marked is drawn by the plan's card: its turn draws no card of its own (`cardLayout`),
                // and a plan card its words end in is drawn inside it, its question as words; every other block stays.
                if (entry.plan === true && !chat.main) {
                    const marked = proposalOf(chat, index);
                    if (marked === null) return planCard(chat, entry, host);
                    const { question, options } = marked.card;
                    const asks =
                        question === undefined || options === undefined ? null : askedAsWords(question, options);
                    return planCard(
                        chat,
                        entry,
                        host,
                        marked.words,
                        planView(chat, index, marked.card, lastReads, asks),
                        (marked.card.steps ?? []).some((step) => step.body !== undefined),
                    );
                }
                const words = cut ?? entry.text;
                const placed = layout.cards.get(index);
                const card = placed === undefined ? null : cardView(chat, index, placed, host, lastReads);
                if (cut !== undefined && words.trim() === '') return card;
                const who = named ? null : html`<div class="who">Jason</div>`;
                named = true;
                return html`<div class="m ai">
                        ${who}
                        <div class="bub">${markdownView(words)}</div>
                    </div>
                    ${card}`;
            }
            case 'permission':
                return permission(chat, entry);
            case 'error':
                return html`<div class="note am chat-error">
                    <div class="said">${entry.text}</div>
                    ${sentence && isSignInFailure(entry.text) ? html`<div class="sign-in">${withCode(sentence)}</div>` : null}
                </div>`;
            case 'pick':
                return pick(chat, entry, entry === lastPick);
            case 'attached':
                return noteLine(`This chat is now the conversation of ${entry.title}.`);
            case 'fresh':
                return noteLine(
                    entry.reason === 'host_changed'
                        ? `${host?.label ?? 'This AI host'} has not seen this conversation; it was given the last messages.`
                        : "The AI's memory of this conversation was gone; it starts from the last messages.",
                );
            case 'notice':
                return entry.folder === null || inTab()
                    ? noteLine(entry.text)
                    : html`<div class="note chat-note">
                          ${entry.text}
                          <button class="btn sm" type="button" ${act('chat.openFolder', chat.id, entry.folder)}>
                              Open folder
                          </button>
                      </div>`;
            case 'afresh':
                return noteLine("Started afresh: the AI's next turn begins without its memory of this conversation.");
            case 'line':
                return feedLine(entry);
            case 'gap':
                return noteLine(
                    'More happened while Jason was closed than is shown here; Needs you and Agents hold all of it.',
                );
        }
    });
    const dropped =
        chat.dropped > 0
            ? noteLine('Earlier messages of this conversation were let go to keep it within its size.')
            : null;
    return html`${dropped}${parts}${chipsNext(chat, host)}${chat.running ? html`<div class="typing"><span class="spin"></span>Thinking…</div>` : null}`;
}

/** The agent of a chat as the console last read it, its state and its counts; none for a chat of no agent. */
function agentOf(campaignId: string | null): Agent | undefined {
    const status = campaigns.value.find((each) => each.id === campaignId)?.status;
    const row = campaignId === null ? undefined : summary.value[campaignId];
    return status === undefined || row === undefined ? undefined : { status, row };
}

/** Chips: each shows its words and sends its own as the person's next message, as a word typed would be. */
const chipButtons = (chat: Chat, chips: readonly Chip[]): Html[] =>
    chips.map(
        (chip) =>
            html`<button class="sug" type="button" ${act('chat.suggest', chat.id, chip.sent)}>${chip.shown}</button>`,
    );

/**
 * The chips under the last answer (`chipsUnder`), where a message could be sent from here now: none without a host, in a
 * tab that may not send, or while the send waits for a model's name. An agent the console did not list, an archived one
 * among them, has none.
 */
function chipsNext(chat: Chat, host: DesktopHost | null): Html | null {
    if (host === null || (inTab() && !tabMaySend(chat.mode)) || sendHeld(onHost(chat, host.id), host)) return null;
    const chips = chipsUnder(chat, agentOf(chat.campaignId));
    return chips.length === 0 ? null : html`<div class="sugs next">${chipButtons(chat, chips)}</div>`;
}

/** What the plan card shows beyond the entries and the host: whether it is live, on another host, approving or approved. */
function cardOf(chat: Chat, host: DesktopHost | null): string {
    // An approved card is no longer the last entry once the go is said, and still shows that it was approved.
    if (!chat.entries.some((entry) => entry.kind === 'text' && entry.plan === true)) return '';
    return `${livePlan(chat, host?.id ?? null)} ${planElsewhere(chat, host?.id ?? null)} ${chat.plan}`;
}

/**
 * A chat's thread, drawn again only when what it shows changed: typing in the box changes the chat, and must neither
 * draw the thread afresh, closing what the person opened in it, nor read its Markdown again.
 */
function threadOf(chatId: string) {
    let last: {
        chat: Chat;
        host: DesktopHost | null;
        named: unknown;
        now: number;
        counts: unknown;
        held: string;
        card: string;
        asked: unknown;
        read: unknown;
        proposed: unknown;
        view: Html;
    } | null = null;
    return computed(() => {
        const chat = chats.value[chatId];
        if (!chat) return html``;
        const host = hostOf(chat);
        // A pick names its campaigns by the names the console read, which may come after the pick.
        const named = campaigns.value;
        // The chips under an agent's last answer are chosen from its state, read with `named`, and its counts as the
        // console last read them; none stand while the send waits for a model's name, which the model chosen and whether
        // one is being typed decide (`sendHeld`, asked only when the thread is drawn: it counts as a draw).
        const counts = chat.campaignId === null ? undefined : summary.value[chat.campaignId];
        const held = `${chat.typing === true} ${chat.model ?? ''}`;
        // A line's age is counted from the console's clock, which moves on every half minute.
        const now = clock.value;
        // The plan card is the one part of the thread drawn from the chat's mode, its waiting words and the person's
        // answer to the card; only what the card shows of them redraws the thread, so a message queued or typed does not.
        const card = cardOf(chat, host);
        // A choice the AI asked is live by the person's acts on the agent as read. A message waits only while a turn
        // runs, after the person's message, which has locked every choice already.
        const asked = acts.value[chatId];
        // A card draws what it read, once its read is answered, and a plan card the drafts the person opened.
        const read = cardReads.value[chatId];
        const proposed = proposals.value[chatId];
        const same =
            last !== null &&
            last.now === now &&
            last.host === host &&
            last.named === named &&
            last.counts === counts &&
            last.held === held &&
            last.chat.entries === chat.entries &&
            last.chat.running === chat.running &&
            last.card === card &&
            last.asked === asked &&
            last.read === read &&
            last.proposed === proposed &&
            last.chat.mode === chat.mode &&
            last.chat.title === chat.title &&
            last.chat.campaignId === chat.campaignId;
        if (!same)
            last = { chat, host, named, now, counts, held, card, asked, read, proposed, view: threadView(chat, host) };
        return last!.view;
    });
}

/**
 * The controls in a composer's row: the access mode on the left, and on the right what the next turn runs on (host,
 * model, effort). The chat's own composer and the start screen's draw the same two menus (`start-menus.tsx`); `change`
 * and `choose` say where a choice goes. Nothing here decides what a mode offers: `modeChoices` does. A tab cannot change
 * a chat's mode, and names it instead.
 */
export function ChatControls({
    chat,
    change,
    choose,
    takeBack: takeBackChosen,
    modeMenu,
}: {
    chat: Chat;
    change: (next: (each: Chat) => Chat) => void;
    choose: (mode: DesktopChatMode) => void;
    takeBack: () => void;
    modeMenu: RefObject<HTMLButtonElement>;
}) {
    return (
        <>
            {inTab() ? (
                <span className="cpick acc mode mode-label" aria-label="Access mode">
                    <ModeMark mode={chat.mode} />
                    {modeChoices(hostOf(chat), chat.main === true).find((each) => each.mode === chat.mode)?.label ??
                        chat.mode}
                </span>
            ) : (
                <AccessButton
                    chat={chat}
                    choose={choose}
                    takeBack={takeBackChosen}
                    buttonRef={modeMenu}
                    header="Access for this chat"
                />
            )}
            <span className="grow" />
            <ModelButton chat={chat} change={change} choose={choose} header="Model for this chat" />
        </>
    );
}

/**
 * The words of the line under a composer for a mode the desktop holds: its name and what it does; none for a mode word
 * this page does not know, which a newer desktop may write.
 */
const lineOf = (mode: DesktopChatMode, label: string): string | null => {
    const said = (modeSays as Partial<Record<string, string>>)[mode];
    return said === undefined ? null : `${label} · ${said.charAt(0).toLowerCase()}${said.slice(1).replace(/\.$/, '')}`;
};

/**
 * The line under a composer: what the chat's access mode lets the AI do, the mode's name, a dot and what it does (Full
 * access and Auto-edit in their own words, as the desktop holds them), with no link, since the menu beside it changes
 * the mode; and what Codex reads and reaches where Codex runs the chat.
 */
export function ChatNotes({ chat }: { chat: Chat }) {
    const host = hostOf(chat);
    const label = modeChoices(host, chat.main === true).find((each) => each.mode === chat.mode)?.label ?? chat.mode;
    return (
        <div className="under">
            {chat.mode === 'full_access' ? (
                <span className="accline full-access" role="status" aria-label="Full access is on">
                    {fullAccessLine}
                </span>
            ) : chat.mode === 'auto_edit' ? (
                <span className="accline auto-edit" role="status" aria-label="Auto-edit is on">
                    {autoEditLine}
                </span>
            ) : (
                lineOf(chat.mode, label) !== null && <span className="accline">{lineOf(chat.mode, label)}</span>
            )}
            {host?.id === 'codex' && (
                <span className="codex-config" role="note" aria-label="What Codex reads">
                    {codexConfigLine}
                </span>
            )}
            {host !== null && codexHosts.has(host.id) && (
                <span className="codex-reach" role="note" aria-label="What a Codex command can reach">
                    {codexReachLine((here().desktop as { platform?: string }).platform)}
                </span>
            )}
        </div>
    );
}

/**
 * The agent a campaign's chat is about, in the chat's bar: its state as the runtime lists the campaign, and its one move,
 * the agent page's own control with its own confirmation. The dialog is the browser shell's, so it is drawn inside that
 * shell's stylesheet scope. Nothing is drawn until the campaign is listed.
 */
function AgentInBar({ campaignId }: { campaignId: string }) {
    const campaign = campaigns.value.find((each) => each.id === campaignId);
    // An archived campaign is over: it has no move, and its chat says nothing of a state.
    if (!campaign || campaign.status === 'archived') return null;
    const { state, tone } = toCampaignRow(campaign, undefined);
    return (
        <div className="chat-agent" role="group" aria-label="This agent">
            <span className={`pillc chat-agent-state ${tone}`}>{state}</span>
            <CampaignMoves
                campaign={campaign}
                buttonClass={(move) => (move === 'pause' ? 'btn sm chat-agent-move' : 'btn sm p chat-agent-move')}
                frame={(dialog) => <div className="harness">{dialog}</div>}
                onDone={() => void reloadCampaigns()}
            />
        </div>
    );
}

export function ChatPanel({ chatId }: { chatId: string }) {
    const thread = useMemo(() => threadOf(chatId), [chatId]);
    const view = useCallback(() => thread.value, [thread]);
    const panel = useRef<HTMLDivElement>(null);
    const removeButton = useRef<HTMLButtonElement>(null);
    const keepButton = useRef<HTMLButtonElement>(null);
    const nameBox = useRef<HTMLInputElement>(null);
    const modeMenu = useRef<HTMLButtonElement>(null);
    const messageBox = useRef<HTMLTextAreaElement>(null);
    const renameButton = useRef<HTMLButtonElement>(null);
    const wasRemoving = useRef(false);
    const wasRenaming = useRef(false);
    // The panel reads its own chat alone, so a word typed in another chat does not draw this one again.
    const record = chatOf(chatId).value;
    const removing = record?.removing ?? false;
    const renaming = record?.renaming !== null && record?.renaming !== undefined;
    // The rename box takes focus with its name selected, so typing replaces it; once it closes, by Save, Cancel or
    // Escape, focus goes back to Rename.
    useEffect(() => {
        if (renaming) {
            nameBox.current?.focus();
            nameBox.current?.select();
        } else if (wasRenaming.current) renameButton.current?.focus();
        wasRenaming.current = renaming;
    }, [renaming]);
    // Remove's question is a dialog: focus goes to keeping the conversation, the rest of the panel takes nothing while it
    // asks, and once it is answered or dismissed focus goes back to Remove.
    useEffect(() => {
        for (const part of panel.current?.querySelectorAll('.chat-scroll, .cform') ?? []) {
            if (removing) part.setAttribute('inert', '');
            else part.removeAttribute('inert');
        }
        if (removing) keepButton.current?.focus();
        else if (wasRemoving.current) removeButton.current?.focus();
        wasRemoving.current = removing;
    }, [removing]);
    const chat = record;
    // Reply is offered unless the campaign's enrolment is routed to another plugin: read when the chat is of a draft campaign
    // while Reply is not connected, and read again if the campaign or what is connected changes.
    const draftCampaignId =
        chat?.campaignId != null &&
        campaigns.value.find((each) => each.id === chat.campaignId)?.status === 'draft' &&
        setup.value &&
        !(setup.value.reply.signedIn && setup.value.reply.wired)
            ? chat.campaignId
            : null;
    useEffect(() => {
        if (draftCampaignId !== null) void readEnrolRoute(draftCampaignId);
    }, [draftCampaignId]);
    useFit(messageBox, record?.draft ?? '');
    // An agent's chat whose last choice may still be live reads, once while it is drawn, whether the person acted on the
    // agent after it was asked; the journal's stream keeps that current (`heardLine`). A chat of no agent reads nothing.
    const api = useApi();
    const entries = record?.entries;
    const campaignId = record?.campaignId ?? null;
    const main = record?.main === true;
    const running = record?.running === true;
    const reopened = record?.reopened ?? 0;
    const asked = useMemo(
        () => (entries === undefined ? null : actsAsked({ entries, campaignId, main, running, reopened })),
        [entries, campaignId, main, running, reopened],
    );
    useEffect(
        () => (asked === null ? undefined : readActs(api, chatId, asked.campaignId, asked.at)),
        [api, chatId, asked?.campaignId, asked?.at],
    );
    if (!chat) return null;
    const keep = () => changeChat(chatId, (each) => ({ ...each, removing: false }));
    // A conversation the desktop keeps is listed; one never sent to has no record to rename yet.
    const keptByDesktop = conversations.value.some((each) => each.id === chatId);
    const closeRename = () => changeChat(chatId, (each) => ({ ...each, renaming: null }));
    const { held } = chatHosts(setup.value?.hosts ?? []);
    const host = hostOf(chat);
    // The chat's own host, held: its sentence alone is said, and the menu keeps naming it until another is chosen.
    const ownHeld = heldHostOf(chat);
    const heldSaid = ownHeld ? [ownHeld] : held;
    // The model and effort shown are the ones chosen on the host the next turn runs on (`onHost`).
    const standing = host ? onHost(chat, host.id) : chat;
    // "Another model" with no name typed yet: nothing is sent until a name is, or another choice made.
    const nameMissing = host !== null && sendHeld(standing, host);
    // A chat in a mode other than Ask first and Plan only takes no message from a tab; the desktop refuses it too.
    const tabLocked = inTab() && !tabMaySend(chat.mode);
    const send = (event?: FormEvent) => {
        event?.preventDefault();
        const current = chats.value[chatId];
        if (tabLocked || !current || !host || sendHeld(onHost(current, host.id), host)) return;
        if (current.running) {
            changeChat(chatId, kept);
            return;
        }
        if (!current.draft.trim()) return;
        changeChat(chatId, (each) => typed(each, ''));
        sendTurn(chatId, current.draft);
    };
    const enter = (event: KeyboardEvent<HTMLTextAreaElement>) => {
        if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) send(event);
    };
    const stop = () => {
        changeChat(chatId, stopAsked);
        here()
            .desktop.chat.stop(chatId)
            .catch((error: unknown) => changeChat(chatId, (each) => failed(each, messageOf(error))));
    };
    return (
        <div className="chat-panel" ref={panel}>
            <div className="chat-bar">
                {chat.main ? null : chat.renaming === null ? (
                    <button
                        ref={renameButton}
                        className="btn sm"
                        type="button"
                        disabled={!keptByDesktop || chat.removing}
                        title={
                            keptByDesktop
                                ? 'Give this conversation a name of your own'
                                : 'Send a message first; then the chat can be renamed'
                        }
                        onClick={() => changeChat(chatId, (each) => ({ ...each, renaming: each.title }))}
                    >
                        Rename
                    </button>
                ) : (
                    <form
                        className="chat-rename"
                        aria-label="Rename this conversation"
                        onKeyDown={(event) => {
                            if (event.key === 'Escape') closeRename();
                        }}
                        onSubmit={(event) => {
                            event.preventDefault();
                            renameChat(chatId, chat.renaming ?? '');
                        }}
                    >
                        <input
                            ref={nameBox}
                            aria-label="Conversation name"
                            value={chat.renaming}
                            onChange={(event) =>
                                changeChat(chatId, (each) => ({
                                    ...each,
                                    // The desktop's cut: sixty characters, never a character split in two.
                                    renaming: Array.from(event.target.value).slice(0, nameLength).join(''),
                                }))
                            }
                        />
                        <button className="btn sm p" type="submit" aria-label="Save name">
                            Save
                        </button>
                        <button className="btn sm" type="button" onClick={closeRename}>
                            Cancel
                        </button>
                    </form>
                )}
                {!inTab() && (
                    <button
                        className="btn sm"
                        type="button"
                        disabled={chat.running || chat.removing}
                        title="The AI's next turn begins without its memory of this conversation; what is shown here stays"
                        onClick={() => restartChat(chatId)}
                    >
                        Start afresh
                    </button>
                )}
                {chat.main || inTab() ? null : (
                    <button
                        ref={removeButton}
                        className="btn sm"
                        type="button"
                        disabled={chat.running || chat.removing}
                        onClick={() => changeChat(chatId, (each) => ({ ...each, removing: true }))}
                    >
                        Remove
                    </button>
                )}
                {chat.campaignId !== null && !chat.main && <AgentInBar campaignId={chat.campaignId} />}
            </div>
            {chat.removing && (
                <div
                    className="note am chat-remove"
                    role="alertdialog"
                    aria-label="Remove this conversation?"
                    aria-describedby={`remove-said-${chatId}`}
                    onKeyDown={(event) => {
                        if (event.key === 'Escape') keep();
                    }}
                >
                    <div id={`remove-said-${chatId}`}>
                        Remove this conversation? Its record goes from this computer; the AI host keeps its own
                        transcript of it.
                    </div>
                    <div className="crow">
                        <button ref={keepButton} className="btn sm" type="button" onClick={keep}>
                            Keep it
                        </button>
                        <button className="btn sm" type="button" onClick={() => removeChat(chatId)}>
                            Remove it
                        </button>
                    </div>
                </div>
            )}
            <div className="chat-scroll">
                <Painted className="thread" view={view} />
                {/* An agent's approvals and questions, from its campaign's lists and never from what the AI wrote. */}
                {chat.campaignId !== null && !chat.main && <WaitingCards campaignId={chat.campaignId} />}
            </div>
            {/* Reply is asked for only where the work needs a way to send: the chat of a campaign still a draft, while
                Reply is not connected. It is an offer, so it is hidden only where the runtime says the campaign's enrolment is routed to
                another plugin, or the campaign already runs; no route and a route that cannot be read leave it, and nothing shows
                while the route is being read. */}
            {!inTab() &&
                chat.campaignId !== null &&
                campaigns.value.find((each) => each.id === chat.campaignId)?.status === 'draft' &&
                setup.value &&
                !(setup.value.reply.signedIn && setup.value.reply.wired) &&
                offersReply(chat.campaignId) &&
                !replyDismissed.value.includes(chatId) && (
                    <div className="chat-reply">
                        <Painted view={replyCardFor(chatId)} onGone={() => dropDialogs(chatPlace(chatId))} />
                        <button
                            className="btn sm"
                            type="button"
                            onClick={() => (replyDismissed.value = [...replyDismissed.value, chatId])}
                        >
                            Not now
                        </button>
                    </div>
                )}
            <form className="cform" onSubmit={send} aria-label="Message">
                <div className="composer">
                    {setup.value && !host && heldSaid.length > 0 && (
                        <div className="note am host-held">
                            {[...new Set(heldSaid.map((each) => each.belowFloor!))].map((sentence) => (
                                <div key={sentence}>
                                    {sentence
                                        .split('`')
                                        .map((part, at) => (at % 2 ? <code key={at}>{part}</code> : part))}
                                </div>
                            ))}{' '}
                            <button className="btn sm" type="button" onClick={() => void listHosts()}>
                                Check again
                            </button>
                        </div>
                    )}
                    {setup.value && !host && held.length === 0 && (
                        <div className="note am">
                            No AI host that drives the chat was found on this machine. Connect lists the hosts and
                            installs one.{' '}
                            {!inTab() && (
                                <button className="btn sm" type="button" onClick={() => here().dock.show('connect')}>
                                    Connect
                                </button>
                            )}
                        </div>
                    )}
                    {chat.waiting !== null && (
                        <div className="note chat-waiting" role="status" aria-label="Waiting message">
                            <div>
                                {chat.held
                                    ? 'Not sent. Take it back when the box is empty, or swap it with the box.'
                                    : 'Waiting to send when this turn ends'}
                            </div>
                            <div className="said">{chat.waiting}</div>
                            <div className="crow">
                                <button
                                    className="btn sm"
                                    type="button"
                                    disabled={chat.draft.trim() !== ''}
                                    title={chat.draft.trim() !== '' ? 'Empty the box to take it back' : undefined}
                                    onClick={() => changeChat(chatId, takenBack)}
                                >
                                    Take back
                                </button>
                                {chat.draft.trim() !== '' && (
                                    <button
                                        className="btn sm"
                                        type="button"
                                        onClick={() => changeChat(chatId, replaced)}
                                    >
                                        Replace
                                    </button>
                                )}
                            </div>
                        </div>
                    )}
                    {tabLocked && <div className="note am">{tabLockedSentence(chat.mode)}</div>}
                    {host?.modelsUnread && <div className="note model-unread">{host.modelsUnread}</div>}
                    <textarea
                        ref={messageBox}
                        rows={1}
                        disabled={chat.removing || tabLocked}
                        placeholder={chat.running ? 'Sent when this turn ends…' : 'Ask Jason…'}
                        aria-label="Your message"
                        value={chat.draft}
                        onChange={(event) => changeChat(chatId, (each) => typed(each, event.target.value))}
                        onKeyDown={enter}
                    />
                    <div className="crow">
                        <ChatControls
                            chat={chat}
                            change={(next) => changeChat(chatId, next)}
                            choose={(mode) => chooseMode(chatId, mode)}
                            takeBack={() => takeBackChoice(chatId)}
                            modeMenu={modeMenu}
                        />
                        {chat.running ? (
                            <>
                                <button
                                    className="btn sm"
                                    type="submit"
                                    disabled={
                                        chat.removing ||
                                        chat.stopping ||
                                        chat.waiting !== null ||
                                        chat.draft.trim() === '' ||
                                        nameMissing
                                    }
                                >
                                    Send when the turn ends
                                </button>
                                <button
                                    className="send"
                                    type="button"
                                    title="Stop"
                                    aria-label="Stop"
                                    disabled={chat.removing}
                                    onClick={stop}
                                >
                                    <Glyph name="stop" size={16} />
                                </button>
                            </>
                        ) : (
                            <button
                                className="send"
                                type="submit"
                                title="Send"
                                aria-label="Send"
                                disabled={!host || chat.removing || nameMissing || tabLocked}
                                {...(nameMissing ? { title: 'Type the model name, or choose another model' } : {})}
                            >
                                <Glyph name="up" size={16} />
                            </button>
                        )}
                    </div>
                </div>
                <ChatNotes chat={chat} />
            </form>
        </div>
    );
}
