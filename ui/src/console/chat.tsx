import { computed, effect } from '@preact/signals-react';
import { useCallback, useMemo, type FormEvent, type KeyboardEvent } from 'react';
import type { DesktopConversation, DesktopHost } from '../platform/desktop';
import { act, define } from './actions';
import {
    afresh,
    answered,
    failed,
    fromRecord,
    handed,
    kept,
    lost,
    newChat,
    pickedFor,
    replaced,
    said,
    shown,
    stopAsked,
    takenBack,
    told,
    typed,
    type Answer,
    type Chat,
    type Entry,
} from './chat-model';
import { campaigns } from './campaigns';
import { html, type Html } from './html';
import { markdownView } from './markdown-view';
import { svg } from './icons';
import { chatHosts, isSignInFailure, signInSentence } from './live';
import { Painted } from './painted';
import { reload as listHosts } from './connect';
import { changeChat, chats, conversations, conversationsFailure, here, messageOf, setup } from './state';

// The Live chat: the person's words go to the agent host the desktop runs, and what the agent says and does comes
// back as events. The agent's words are drawn as a fixed subset of Markdown, every word of them as text. A message sent
// while a turn runs waits in the page, and the page hands it to the desktop as the next turn when the turn ends well.
// Every chat is a conversation the desktop keeps: a campaign's one, or a set-up chat about no campaign yet. The desktop
// decides which campaign a turn is about, from its record; the page names the conversation and the host, nothing more.

const suggestions = ['What is waiting for me?', 'How are my campaigns doing?', 'Help me start a new campaign.'];

const answerWords: Readonly<Record<Answer, string>> = {
    allow: 'Allowed',
    always: 'Allowed for this chat',
    deny: 'Not allowed',
    gone: 'This request has ended',
};

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
    const record = await here().desktop.chat.open(id);
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
            id = await hold((await here().desktop.chat.create(campaign.id)).id);
        } else {
            const made: DesktopConversation = await here().desktop.chat.create();
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
        .then(() => changeChat(id, afresh))
        .catch((error: unknown) => changeChat(id, (chat) => told(chat, messageOf(error))));
}

/** Removes the conversation, asked twice: its tab closes and the list follows; a refusal is said in the chat. */
export function removeChat(id: string): void {
    changeChat(id, (chat) => ({ ...chat, removing: false }));
    here()
        .desktop.chat.remove(id)
        .then(() => {
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
        .desktop.chat.send(id, words, { host: host.id })
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

define('chat.attach', ([id, campaignId]) => {
    if (typeof id !== 'string' || typeof campaignId !== 'string') return;
    here()
        .desktop.chat.attach(id, campaignId)
        .then((conversation) => {
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

/** The campaigns a set-up turn made: while the chat is about none and no turn runs, each is a choice. */
function pick(chat: Chat, entry: Extract<Entry, { kind: 'pick' }>): Html {
    const open = chat.campaignId === null && !chat.running;
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

export function threadView(chat: Chat, host: DesktopHost | null): Html {
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
                return pick(chat, entry);
            case 'attached':
                return noteLine(`This chat is now the conversation of ${entry.title}.`);
            case 'fresh':
                return noteLine(
                    entry.reason === 'host_changed'
                        ? `${host?.label ?? 'This agent host'} has not seen this conversation; it was given the last messages.`
                        : "The agent's memory of this conversation was gone; it starts from the last messages.",
                );
            case 'afresh':
                return noteLine(
                    "Started afresh: the agent's next turn begins without its memory of this conversation.",
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
    let last: { chat: Chat; host: DesktopHost | null; named: unknown; view: Html } | null = null;
    return computed(() => {
        const chat = chats.value[chatId];
        if (!chat) return html``;
        const host = hostOf(chat);
        // A pick names its campaigns by the names the console read, which may come after the pick.
        const named = campaigns.value;
        const same =
            last !== null &&
            last.host === host &&
            last.named === named &&
            last.chat.entries === chat.entries &&
            last.chat.running === chat.running &&
            last.chat.title === chat.title &&
            last.chat.campaignId === chat.campaignId;
        if (!same) last = { chat, host, named, view: threadView(chat, host) };
        return last!.view;
    });
}

export function ChatPanel({ chatId }: { chatId: string }) {
    const thread = useMemo(() => threadOf(chatId), [chatId]);
    const view = useCallback(() => thread.value, [thread]);
    const chat = chats.value[chatId];
    if (!chat) return null;
    const { drive, held, other } = chatHosts(setup.value?.hosts ?? []);
    const host = hostOf(chat);
    const send = (event?: FormEvent) => {
        event?.preventDefault();
        const current = chats.value[chatId];
        if (!current || !host) return;
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
        <div className="chat-panel">
            <div className="chat-bar">
                <button
                    className="btn sm"
                    type="button"
                    disabled={chat.running}
                    title="The agent's next turn begins without its memory of this conversation; what is shown here stays"
                    onClick={() => restartChat(chatId)}
                >
                    Start afresh
                </button>
                <button
                    className="btn sm"
                    type="button"
                    disabled={chat.running}
                    onClick={() => changeChat(chatId, (each) => ({ ...each, removing: true }))}
                >
                    Remove
                </button>
            </div>
            {chat.removing && (
                <div className="note am chat-remove" role="alertdialog" aria-label="Remove this conversation?">
                    <div>
                        Remove this conversation? Its record goes from this computer; the agent host keeps its own
                        transcript of it.
                    </div>
                    <div className="crow">
                        <button className="btn sm" type="button" onClick={() => removeChat(chatId)}>
                            Remove it
                        </button>
                        <button
                            className="btn sm"
                            type="button"
                            onClick={() => changeChat(chatId, (each) => ({ ...each, removing: false }))}
                        >
                            Keep it
                        </button>
                    </div>
                </div>
            )}
            <Painted className="thread" view={view} />
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
                <textarea
                    id="box"
                    rows={2}
                    placeholder={chat.running ? 'Sent when this turn ends…' : 'Ask Jason…'}
                    aria-label="Your message"
                    value={chat.draft}
                    onChange={(event) => changeChat(chatId, (each) => typed(each, event.target.value))}
                    onKeyDown={enter}
                />
                <div className="crow">
                    <select
                        aria-label="Agent host"
                        value={host?.id ?? ''}
                        onChange={(event) => changeChat(chatId, (each) => ({ ...each, host: event.target.value }))}
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
                    <span className="grow" />
                    {chat.running ? (
                        <>
                            <button
                                className="btn sm"
                                type="submit"
                                disabled={chat.stopping || chat.waiting !== null || chat.draft.trim() === ''}
                            >
                                Send when the turn ends
                            </button>
                            <button className="send" type="button" title="Stop" aria-label="Stop" onClick={stop}>
                                ■
                            </button>
                        </>
                    ) : (
                        <button className="send" type="submit" title="Send" aria-label="Send" disabled={!host}>
                            ↑
                        </button>
                    )}
                </div>
            </form>
        </div>
    );
}
