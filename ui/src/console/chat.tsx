import { computed, effect } from '@preact/signals-react';
import { useCallback, useEffect, useMemo, useRef, type FormEvent, type KeyboardEvent } from 'react';
import type { JournalEntry } from '../api/types';
import type { DesktopChatMode, DesktopEffort, DesktopHost, DesktopRefusal } from '../platform/desktop';
import { act, define } from './actions';
import {
    afresh,
    anotherModel,
    answered,
    effortChosen,
    failed,
    fromRecord,
    fullAccessLine,
    handed,
    hostChosen,
    kept,
    lost,
    modeChoices,
    modelChosen,
    modelTyped,
    modelView,
    newChat,
    onHost,
    sendHeld,
    pickedFor,
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
import { campaigns } from './campaigns';
import { toFeedLine, toFeedRow } from './feed';
import { html, type Html } from './html';
import { markdownView } from './markdown-view';
import { svg } from './icons';
import { chatHosts, isSignInFailure, signInSentence } from './live';
import { Painted } from './painted';
import { chatPlace, dropLinks, replyCardFor, replyDismissed, reload as listHosts } from './connect';
import { changeChat, chats, clock, conversations, conversationsFailure, feedId, here, messageOf, setup } from './state';

// The Live chat: the person's words go to the agent host the desktop runs, and what the agent says and does comes
// back as events. The agent's words are drawn as a fixed subset of Markdown, every word of them as text. A message sent
// while a turn runs waits in the page, and the page hands it to the desktop as the next turn when the turn ends well.
// Every chat is a conversation the desktop keeps: a campaign's one, a set-up chat about no campaign yet, or the Feed, the
// person's main chat, into which the page hands the journal's lines and which draws each as a row linking to its
// campaign. The desktop decides which campaign a turn is about, from its record; the page names the conversation, the
// host, and the model and effort chosen in the box, nothing more. A line never starts a turn.
// The menu beside the composer asks the desktop for the conversation's access mode; the desktop confirms Full access
// with the person itself, and the page shows the mode the desktop answers with.

const suggestions = ['What is waiting for me?', 'How are my campaigns doing?', 'Help me start a new campaign.'];

/** The effort levels as the box names them. */
const effortWords: Readonly<Record<DesktopEffort, string>> = {
    low: 'Low',
    medium: 'Medium',
    high: 'High',
    xhigh: 'Extra high',
    max: 'Max',
};

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
    here()
        .desktop.chat.feed([], { catchUp: true })
        .catch(() => undefined);
}

/** Hands the desktop a line of the journal's stream when the Feed takes its kind; the desktop checks it again. */
export function handLine(line: JournalEntry): void {
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
    if (!chats.value[record.id]) chats.value = { ...chats.value, [record.id]: fromRecord(record, null) };
    return record.id;
}

/**
 * The conversation to show: the campaign's one, made on first use and opened with what it kept, or with no campaign
 * a new set-up chat. Resolves with its id, or with null when the desktop refused, which the sidebar says.
 */
export async function startChat(campaign?: { id: string; name: string }): Promise<string | null> {
    try {
        let id: string;
        if (campaign) {
            id = await hold(unless(await here().desktop.chat.create(campaign.id)).id);
        } else {
            const made = unless(await here().desktop.chat.create());
            chats.value = { ...chats.value, [made.id]: { ...newChat(made.id, null), title: made.title } };
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
    const chat = chats.value[id];
    if (!chat) return;
    // Leaving Full access while its turn runs stops that turn, and so does a change to Plan only, as Stop does: the
    // waiting message is given back, and no turn follows by itself.
    const stops =
        (chat.mode === 'full_access' && mode !== 'full_access') || (mode === 'plan_only' && chat.mode !== 'plan_only');
    if (chat.running && stops) changeChat(id, stopAsked);
    here()
        .desktop.chat.setMode(id, mode, hostOf(chat)?.id ?? '')
        .then((answer) => {
            if (isRefusal(answer)) return changeChat(id, (chat) => told(chat, answer.refused));
            changeChat(id, (chat) => ({ ...chat, mode: answer.mode }));
        })
        .catch((error: unknown) => changeChat(id, (chat) => told(chat, messageOf(error))));
}

/** The most characters a conversation's name keeps, as the desktop cuts it. */
const nameLength = 60;

/** Removes the conversation, asked twice: its tab closes and the list follows; a refusal is said in the chat. */
export function removeChat(id: string): void {
    changeChat(id, (chat) => ({ ...chat, removing: false }));
    here()
        .desktop.chat.remove(id)
        .then((answer) => {
            if (isRefusal(answer)) return changeChat(id, (chat) => told(chat, answer.refused));
            here().dock.closeChat(id);
            chats.value = Object.fromEntries(Object.entries(chats.value).filter(([each]) => each !== id));
            void reloadConversations();
        })
        .catch((error: unknown) => changeChat(id, (chat) => told(chat, messageOf(error))));
}

/** The host a chat's next turn runs on: the one chosen, else the first found that drives the chat. */
export function hostOf(chat: Chat): DesktopHost | null {
    const { drive } = chatHosts(setup.value?.hosts ?? []);
    return drive.find((each) => each.id === chat.host) ?? drive[0] ?? null;
}

/**
 * Starts a turn on the host chosen now; the desktop takes the campaign from the conversation. `resent` says the words were the waiting message: a
 * turn of those the page cannot start ends with its error, and the words are given back as a failed turn gives them.
 */
export function sendTurn(id: string, text: string, resent = false): void {
    const chat = chats.value[id];
    const words = text.trim();
    if (!chat || chat.running || !words) return;
    const host = hostOf(chat);
    if (!host) {
        // A host that was found and is held says what holds it, below the floor or a version not read, in its own mark.
        const held = chatHosts(setup.value?.hosts ?? []).held[0]?.belowFloor;
        if (resent)
            changeChat(id, (each) =>
                failed(
                    said(each, words, true),
                    held ?? 'No agent host that drives the chat was found on this machine.',
                ),
            );
        return;
    }
    changeChat(id, (each) => said(each, words, resent));
    here()
        .desktop.chat.send(id, words, turnOptions(onHost(chat, host.id), host))
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

define('chat.answer', ([id, request, answer]) => {
    if (typeof id !== 'string' || typeof request !== 'string') return;
    if (answer !== 'allow' && answer !== 'always' && answer !== 'deny') return;
    changeChat(id, (chat) => answered(chat, request, answer));
    here()
        .desktop.chat.answer(id, request, { allow: answer !== 'deny', always: answer === 'always' })
        .catch(() => changeChat(id, (chat) => lost(chat, request)));
});

define('feed.campaign', ([id]) => {
    if (typeof id !== 'string') return;
    const name = campaigns.value.find((each) => each.id === id)?.name ?? id;
    here().dock.chat({ id, name });
});

define('feed.needs', () => here().dock.show('needs'));

/** The Open folder button of the notice that says a Full access folder is kept: the desktop opens that folder alone. */
define('chat.openFolder', ([id, name]) => {
    if (typeof id !== 'string' || typeof name !== 'string') return;
    here()
        .desktop.chat.openFolder(name)
        .then((answer) => {
            if (isRefusal(answer)) changeChat(id, (chat) => told(chat, answer.refused));
        })
        .catch((error: unknown) => changeChat(id, (chat) => told(chat, messageOf(error))));
});

define('chat.attach', ([id, campaignId]) => {
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

function permission(chat: Chat, entry: Extract<Chat['entries'][number], { kind: 'permission' }>): Html {
    const buttons =
        entry.answer === null
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

/** A campaign a pick names: by the name the console read for it, else by its id. */
const campaignName = (id: string): string => campaigns.value.find((each) => each.id === id)?.name ?? id;

/**
 * The campaigns a set-up turn made. Only the chat's last pick is a choice, as only it is one the desktop takes, and only
 * while the chat is about no campaign and no turn runs; an earlier one is drawn as text.
 */
function pick(chat: Chat, entry: Extract<Entry, { kind: 'pick' }>, last: boolean): Html {
    const open = last && chat.campaignId === null && !chat.running;
    return html`<div class="ask chat-pick">
        <div class="ask-h">
            ${svg('camp', 14)}<b>${open ? 'Make this the conversation of' : 'Campaigns this chat made'}</b>
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

/** A line of the Feed: the kind in words, how many it stands for, its campaign as a link, its age. */
function feedLine(entry: Extract<Entry, { kind: 'line' }>): Html {
    const names = new Map(campaigns.value.map((each) => [each.id, each.name]));
    const row = toFeedRow(entry.line, names, clock.value);
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
                What happens in your campaigns comes here: a reply, an approval or a question waiting, a campaign
                created, started or paused, work that stopped, a provider block holding work. Ask about any of it below.
            </p>
        </div>`;
    if (chat.entries.length === 0)
        return html`<div class="hero">
                <div class="glyph">${svg('chat', 22)}</div>
                <h2>${chat.campaignId ? `What should Jason do in ${chat.title}?` : 'What should Jason work on?'}</h2>
                <p>Anything that reaches a person waits for your approval.</p>
            </div>
            <div class="sugs">
                ${suggestions.map((text) => html`<button class="sug" type="button" ${act('chat.suggest', chat.id, text)}>${text}</button>`)}
            </div>`;
    const sentence = host && signInSentence(host);
    const lastPick = [...chat.entries].reverse().find((entry) => entry.kind === 'pick');
    const parts = shown(chat).map((entry) => {
        switch (entry.kind) {
            case 'steps':
                return html`<details class="vsteps" ${entry.open ? html`open` : null}>
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
            case 'text':
                return html`<div class="m ai"><div class="bub">${markdownView(entry.text)}</div></div>`;
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
                        ? `${host?.label ?? 'This agent host'} has not seen this conversation; it was given the last messages.`
                        : "The agent's memory of this conversation was gone; it starts from the last messages.",
                );
            case 'notice':
                return entry.folder === null
                    ? noteLine(entry.text)
                    : html`<div class="note chat-note">
                          ${entry.text}
                          <button class="btn sm" type="button" ${act('chat.openFolder', chat.id, entry.folder)}>
                              Open folder
                          </button>
                      </div>`;
            case 'afresh':
                return noteLine(
                    "Started afresh: the agent's next turn begins without its memory of this conversation.",
                );
            case 'line':
                return feedLine(entry);
            case 'gap':
                return noteLine(
                    'More happened while Jason was closed than is shown here; Needs you and Campaigns hold all of it.',
                );
        }
    });
    const dropped =
        chat.dropped > 0
            ? noteLine('Earlier messages of this conversation were let go to keep it within its size.')
            : null;
    return html`${dropped}${parts}${chat.running ? html`<div class="typing"><span class="spin"></span>Thinking…</div>` : null}`;
}

/**
 * A chat's thread, drawn again only when what it shows changed: typing in the box changes the chat, and must neither
 * draw the thread afresh, closing what the person opened in it, nor read its Markdown again.
 */
function threadOf(chatId: string) {
    let last: { chat: Chat; host: DesktopHost | null; named: unknown; now: number; view: Html } | null = null;
    return computed(() => {
        const chat = chats.value[chatId];
        if (!chat) return html``;
        const host = hostOf(chat);
        // A pick names its campaigns by the names the console read, which may come after the pick.
        const named = campaigns.value;
        // A line's age is counted from the console's clock, which moves on every half minute.
        const now = clock.value;
        const same =
            last !== null &&
            last.now === now &&
            last.host === host &&
            last.named === named &&
            last.chat.entries === chat.entries &&
            last.chat.running === chat.running &&
            last.chat.title === chat.title &&
            last.chat.campaignId === chat.campaignId;
        if (!same) last = { chat, host, named, now, view: threadView(chat, host) };
        return last!.view;
    });
}

export function ChatPanel({ chatId }: { chatId: string }) {
    const thread = useMemo(() => threadOf(chatId), [chatId]);
    const view = useCallback(() => thread.value, [thread]);
    const panel = useRef<HTMLDivElement>(null);
    const removeButton = useRef<HTMLButtonElement>(null);
    const keepButton = useRef<HTMLButtonElement>(null);
    const nameBox = useRef<HTMLInputElement>(null);
    const modeMenu = useRef<HTMLSelectElement>(null);
    const renameButton = useRef<HTMLButtonElement>(null);
    const wasRemoving = useRef(false);
    const wasRenaming = useRef(false);
    const removing = chats.value[chatId]?.removing ?? false;
    const renaming = chats.value[chatId]?.renaming !== null && chats.value[chatId]?.renaming !== undefined;
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
        for (const part of panel.current?.querySelectorAll('.thread, .composer') ?? []) {
            if (removing) part.setAttribute('inert', '');
            else part.removeAttribute('inert');
        }
        if (removing) keepButton.current?.focus();
        else if (wasRemoving.current) removeButton.current?.focus();
        wasRemoving.current = removing;
    }, [removing]);
    const chat = chats.value[chatId];
    if (!chat) return null;
    const keep = () => changeChat(chatId, (each) => ({ ...each, removing: false }));
    // A conversation the desktop keeps is listed; one never sent to has no record to rename yet.
    const keptByDesktop = conversations.value.some((each) => each.id === chatId);
    const closeRename = () => changeChat(chatId, (each) => ({ ...each, renaming: null }));
    const { drive, held, other } = chatHosts(setup.value?.hosts ?? []);
    const host = hostOf(chat);
    // The model and effort shown are the ones chosen on the host the next turn runs on (`onHost`).
    const standing = host ? onHost(chat, host.id) : chat;
    const picked = host ? modelView(standing, host) : null;
    const onThisHost = (change: (each: Chat) => Chat) =>
        changeChat(chatId, (each) => (host ? change(onHost(each, host.id)) : each));
    // "Another model" with no name typed yet: nothing is sent until a name is, or another choice made.
    const nameMissing = host !== null && sendHeld(standing, host);
    const send = (event?: FormEvent) => {
        event?.preventDefault();
        const current = chats.value[chatId];
        if (!current || !host || sendHeld(onHost(current, host.id), host)) return;
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
                <button
                    className="btn sm"
                    type="button"
                    disabled={chat.running || chat.removing}
                    title="The agent's next turn begins without its memory of this conversation; what is shown here stays"
                    onClick={() => restartChat(chatId)}
                >
                    Start afresh
                </button>
                {chat.main ? null : (
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
                        Remove this conversation? Its record goes from this computer; the agent host keeps its own
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
            <Painted className="thread" view={view} />
            {/* Reply is asked for only where the work needs a way to send: the chat of a campaign still a draft, while
                Reply is not connected. A campaign on another provider, or one already running, never carries it. */}
            {chat.campaignId !== null &&
                campaigns.value.find((each) => each.id === chat.campaignId)?.status === 'draft' &&
                setup.value &&
                !(setup.value.reply.signedIn && setup.value.reply.wired) &&
                !replyDismissed.value.includes(chatId) && (
                    <div className="chat-reply">
                        <Painted view={replyCardFor(chatId)} onGone={() => dropLinks(chatPlace(chatId))} />
                        <button
                            className="btn sm"
                            type="button"
                            onClick={() => (replyDismissed.value = [...replyDismissed.value, chatId])}
                        >
                            Not now
                        </button>
                    </div>
                )}
            <form className="composer" onSubmit={send} aria-label="Message">
                {setup.value && !host && held.length > 0 && (
                    <div className="note am host-held">
                        {[...new Set(held.map((each) => each.belowFloor!))].map((sentence) => (
                            <div key={sentence}>
                                {sentence.split('`').map((part, at) => (at % 2 ? <code key={at}>{part}</code> : part))}
                            </div>
                        ))}{' '}
                        <button className="btn sm" type="button" onClick={() => void listHosts()}>
                            Check again
                        </button>
                    </div>
                )}
                {setup.value && !host && held.length === 0 && (
                    <div className="note am">
                        No agent host that drives the chat was found on this machine. Connect lists the hosts and
                        installs one.{' '}
                        <button className="btn sm" type="button" onClick={() => here().dock.show('connect')}>
                            Connect
                        </button>
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
                                <button className="btn sm" type="button" onClick={() => changeChat(chatId, replaced)}>
                                    Replace
                                </button>
                            )}
                        </div>
                    </div>
                )}
                {host?.modelsUnread && <div className="note model-unread">{host.modelsUnread}</div>}
                <textarea
                    id="box"
                    rows={2}
                    disabled={chat.removing}
                    placeholder={chat.running ? 'Sent when this turn ends…' : 'Ask Jason…'}
                    aria-label="Your message"
                    value={chat.draft}
                    onChange={(event) => changeChat(chatId, (each) => typed(each, event.target.value))}
                    onKeyDown={enter}
                />
                <div className="crow">
                    <select
                        aria-label="Agent host"
                        disabled={chat.removing}
                        value={host?.id ?? ''}
                        onChange={(event) => {
                            const chosen = event.target.value;
                            changeChat(chatId, (each) => hostChosen(each, chosen));
                            // A host Full access cannot be chosen on takes the chat back to Ask first.
                            const next = drive.find((each) => each.id === chosen);
                            if (chat.mode === 'full_access' && next !== undefined && next.fullAccess !== null)
                                chooseMode(chatId, 'ask_first');
                        }}
                    >
                        {drive.map((each) => (
                            <option key={each.id} value={each.id}>
                                {each.label}
                            </option>
                        ))}
                        {held.map((each) => (
                            <option key={each.id} value={each.id} disabled>
                                {each.label}: {each.belowFloor!.replaceAll('`', '')}
                            </option>
                        ))}
                        {other.map((each) => (
                            <option key={each.id} value={each.id} disabled>
                                {each.label}: runs campaign work, not the chat yet
                            </option>
                        ))}
                    </select>
                    {host &&
                        picked &&
                        (host.models.length > 0 ||
                            host.typedModel ||
                            picked.kept !== null ||
                            host.modelsUnread !== null) && (
                            <select
                                className="model"
                                aria-label="Model"
                                disabled={chat.removing || host.modelsUnread !== null}
                                value={picked.typing ? anotherModel : (picked.offer?.id ?? picked.kept ?? '')}
                                onChange={(event) => onThisHost((each) => modelChosen(each, host, event.target.value))}
                            >
                                <option value="">Default model</option>
                                {host.models.map((each) => (
                                    <option key={each.id} value={each.id}>
                                        {each.label}
                                    </option>
                                ))}
                                {picked.kept !== null && (
                                    <option value={picked.kept}>
                                        {picked.kept}: no longer listed, so the turn uses {host.label}'s own model with
                                        no effort
                                    </option>
                                )}
                                {host.typedModel && <option value={anotherModel}>Another model…</option>}
                            </select>
                        )}
                    {host && picked?.typing && (
                        <input
                            className="model-name"
                            type="text"
                            aria-label="Model name"
                            placeholder="Full model name"
                            spellCheck={false}
                            disabled={chat.removing}
                            value={standing.model ?? ''}
                            onChange={(event) => onThisHost((each) => modelTyped(each, event.target.value))}
                        />
                    )}
                    {picked?.offer && picked.offer.efforts.length > 0 && (
                        <select
                            className="effort"
                            aria-label="Effort"
                            disabled={chat.removing}
                            value={
                                standing.effort !== null && picked.offer.efforts.includes(standing.effort)
                                    ? standing.effort
                                    : ''
                            }
                            onChange={(event) =>
                                onThisHost((each) => effortChosen(each, event.target.value as DesktopEffort | ''))
                            }
                        >
                            <option value="">Default effort</option>
                            {picked.offer.efforts.map((each) => (
                                <option key={each} value={each}>
                                    {effortWords[each]}
                                </option>
                            ))}
                        </select>
                    )}
                    <select
                        ref={modeMenu}
                        className="mode"
                        aria-label="Access mode"
                        disabled={chat.removing}
                        value={chat.mode}
                        onChange={(event) => chooseMode(chatId, event.target.value as DesktopChatMode)}
                    >
                        {modeChoices(host).map((each) => (
                            <option key={each.mode} value={each.mode} disabled={each.disabled !== null}>
                                {each.disabled === null
                                    ? each.label
                                    : `${each.label}: ${each.disabled.replaceAll('`', '')}`}
                            </option>
                        ))}
                    </select>
                    <span className="grow" />
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
                                ■
                            </button>
                        </>
                    ) : (
                        <button
                            className="send"
                            type="submit"
                            title="Send"
                            aria-label="Send"
                            disabled={!host || chat.removing || nameMissing}
                            {...(nameMissing ? { title: 'Type the model name, or choose another model' } : {})}
                        >
                            ↑
                        </button>
                    )}
                </div>
                {chat.mode === 'full_access' && (
                    <div className="note am full-access" role="status" aria-label="Full access is on">
                        {fullAccessLine}{' '}
                        <button className="btn sm" type="button" onClick={() => modeMenu.current?.focus()}>
                            Change
                        </button>
                    </div>
                )}
            </form>
        </div>
    );
}
