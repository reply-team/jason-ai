import { computed, effect } from '@preact/signals-react';
import { useCallback, useMemo, type FormEvent, type KeyboardEvent } from 'react';
import type { DesktopHost } from '../platform/desktop';
import { act, define } from './actions';
import {
    answered,
    failed,
    handed,
    kept,
    lost,
    newChat,
    replaced,
    said,
    shown,
    stopAsked,
    takenBack,
    typed,
    type Answer,
    type Chat,
} from './chat-model';
import { html, type Html } from './html';
import { markdownView } from './markdown-view';
import { svg } from './icons';
import { chatHosts, isSignInFailure, signInSentence } from './live';
import { Painted } from './painted';
import { changeChat, chats, here, messageOf, setup } from './state';

// The Live chat: the person's words go to the agent host the desktop runs, and what the agent says and does comes
// back as events. The agent's words are drawn as a fixed subset of Markdown, every word of them as text. A message sent
// while a turn runs waits in the page, and the page hands it to the desktop as the next turn when the turn ends well.

const suggestions = ['What is waiting for me?', 'How are my campaigns doing?', 'Help me start a new campaign.'];

const answerWords: Readonly<Record<Answer, string>> = {
    allow: 'Allowed',
    always: 'Allowed for this chat',
    deny: 'Not allowed',
    gone: 'This request has ended',
};

let made = 0;

/** Opens a chat and returns its id: an id the page makes up and keeps for every turn of the chat. */
export function openChat(campaign?: { id: string; name: string }): string {
    const id = `chat-${Date.now().toString(36)}-${++made}`;
    chats.value = { ...chats.value, [id]: newChat(id, null, campaign) };
    return id;
}

/** The host a chat's next turn runs on: the one chosen, else the first found that drives the chat. */
export function hostOf(chat: Chat): DesktopHost | null {
    const { drive } = chatHosts(setup.value?.hosts ?? []);
    return drive.find((each) => each.id === chat.host) ?? drive[0] ?? null;
}

/**
 * Starts a turn on the host chosen now, with the chat's campaign. `resent` says the words were the waiting message: a
 * turn of those the page cannot start ends with its error, and the words are given back as a failed turn gives them.
 */
export function sendTurn(id: string, text: string, resent = false): void {
    const chat = chats.value[id];
    const words = text.trim();
    if (!chat || chat.running || !words) return;
    const host = hostOf(chat);
    if (!host) {
        if (resent)
            changeChat(id, (each) =>
                failed(said(each, words, true), 'No agent host that drives the chat was found on this machine.'),
            );
        return;
    }
    changeChat(id, (each) => said(each, words, resent));
    const options = { host: host.id, ...(chat.campaignId ? { campaignId: chat.campaignId } : {}) };
    here()
        .desktop.chat.send(id, words, options)
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
                    ${entry.steps.map((step) => html`<div class="vstep">${step}</div>`)}
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
        }
    });
    return html`${parts}${chat.running ? html`<div class="typing"><span class="spin"></span>Thinking…</div>` : null}`;
}

/**
 * A chat's thread, drawn again only when what it shows changed: typing in the box changes the chat, and must neither
 * draw the thread afresh, closing what the person opened in it, nor read its Markdown again.
 */
function threadOf(chatId: string) {
    let last: { chat: Chat; host: DesktopHost | null; view: Html } | null = null;
    return computed(() => {
        const chat = chats.value[chatId];
        if (!chat) return html``;
        const host = hostOf(chat);
        const same =
            last !== null &&
            last.host === host &&
            last.chat.entries === chat.entries &&
            last.chat.running === chat.running &&
            last.chat.title === chat.title &&
            last.chat.campaignId === chat.campaignId;
        if (!same) last = { chat, host, view: threadView(chat, host) };
        return last!.view;
    });
}

export function ChatPanel({ chatId }: { chatId: string }) {
    const thread = useMemo(() => threadOf(chatId), [chatId]);
    const view = useCallback(() => thread.value, [thread]);
    const chat = chats.value[chatId];
    if (!chat) return null;
    const { drive, other } = chatHosts(setup.value?.hosts ?? []);
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
            <Painted className="thread" view={view} />
            <form className="composer" onSubmit={send} aria-label="Message">
                {setup.value && !host && (
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
