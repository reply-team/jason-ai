import { effect, signal } from '@preact/signals-react';
import { can, cannotHere, releasesPage } from '../platform/places';
import { useEffect, useRef, type FormEvent, type KeyboardEvent } from 'react';
import type { DesktopChatMode } from '../platform/desktop';
import { fullAccessStanding, takeBackStanding } from '../platform/full-access';
import { t } from '../platform/i18n';
import { mode, type Mode } from '../platform/mode';
import { ChatNotes, hostOf, isRefusal, sendTurn, startChat, tabLockedSentence, tabMaySend } from './chat';
import { moded, newChat, told, type Chat } from './chat-model';
import * as connect from './connect';
import { chatHosts } from './live';
import { Painted } from './painted';
import { AccessButton, ModelButton } from './start-menus';
import { readPreferences, noneStated } from '../screens/settings/voice-model';
import { voiceSectionId } from '../screens/settings/voice-id';
import { voiceKnown } from '../screens/settings/voice-state';
import { changeChat, chats, conversations, here, inTab, messageOf, setup, setupError } from './state';

// The start screen: the window opens on the question who the person wants to reach. What is sent there starts a set-up
// chat. With no AI that drives the chat the AI card comes first and the message waits there, to go once one is
// connected. Reply is not asked for here: its card comes up in a chat when the work needs a way to send.
//
// Start is the window's opening page and there is one of it in a window: it opens each time the window opens, also when
// campaigns exist, it cannot be closed while a message waits, and the one way back to it is to open the window again.
// New chat, in the sidebar and on the empty pages, makes a chat directly and never goes through it.

/** What is typed in the start screen's box. */
export const startDraft = signal('');
/** The message sent from the start screen that waits for an AI to be connected; null when none waits. */
export const startWaiting = signal<string | null>(null);

/**
 * The message of the chat being made, from the moment the chat is asked of the desktop until it is shown; null when none
 * is. While it is not null the box and Start take nothing, so a second message cannot start a second chat.
 */
export const startMaking = signal<string | null>(null);

/** What the controls beside the start screen's box have chosen: the host, model and effort the chat's first turn names. */
export const startPick = signal<Chat>(newChat('', null));

/** The access mode the person chose beside the box; null while none was chosen, which leaves the standing default. */
export const startMode = signal<DesktopChatMode | null>(null);

/**
 * The mode a chat made now would start in when the person chose none: Full access when the standing choice says new
 * chats start in it and the host that chat would run on can take it, Ask first otherwise.
 */
function defaultMode(): DesktopChatMode {
    // A tab never makes a chat in Full access: it could send nothing to it, so the standing choice is not its default.
    if (inTab() || fullAccessStanding.value?.newChats !== true || setup.value === null) return 'ask_first';
    return hostOf(startPick.value)?.fullAccess === null ? 'full_access' : 'ask_first';
}

/** The mode the start screen shows and the chat it makes ends up in. */
const shownMode = (): DesktopChatMode => startMode.value ?? defaultMode();

/** The choices on the start screen as a chat, so the chat's own controls draw them. */
const startChatNow = (): Chat => ({ ...startPick.value, mode: shownMode() });

/** The key under which this browser keeps that the note on the start screen was closed, in one mode. */
export const noteKey = (which: Mode): string => `jason.ui.start-note-closed.${which}`;

/** Whether the note was closed in a mode, as this browser kept it; false where it keeps nothing. */
export function noteWasClosed(which: Mode): boolean {
    try {
        return localStorage.getItem(noteKey(which)) === '1';
    } catch {
        return false;
    }
}

/** The modes in which the person closed the note: Live and the demo apart. Kept by this browser, never by the runtime. */
export const notesClosed = signal<Mode[]>((['live', 'demo'] as const).filter(noteWasClosed));

/** Closes the note for good in the mode this window is in. */
export function closeNote(): void {
    const which = mode.value;
    notesClosed.value = [...notesClosed.value.filter((each) => each !== which), which];
    try {
        localStorage.setItem(noteKey(which), '1');
    } catch {
        // A browser that keeps nothing shows the note again next time.
    }
}

/** The key under which this browser keeps that the note about name and tone was closed. */
export const voiceNoteKey = 'jason.ui.start-voice-note-closed';

/** Whether the note about name and tone was closed, as this browser kept it; false where it keeps nothing. */
export function voiceNoteWasClosed(): boolean {
    try {
        return localStorage.getItem(voiceNoteKey) === '1';
    } catch {
        return false;
    }
}

export const voiceNoteClosed = signal(voiceNoteWasClosed());

export { voiceKnown };

/** Closes the note about name and tone for good. */
export function closeVoiceNote(): void {
    voiceNoteClosed.value = true;
    try {
        localStorage.setItem(voiceNoteKey, '1');
    } catch {
        // A browser that keeps nothing shows the note again next time.
    }
}

/**
 * Whether a first message was sent: a chat exists. The desktop lists one besides the Feed, or a chat of this window
 * holds a message the person sent and is not listed yet.
 */
const messageWasSent = (): boolean =>
    conversations.value.some((each) => each.main !== true) ||
    Object.values(chats.value).some((chat) => chat.entries.some((entry) => entry.kind === 'user'));

/** Opens Settings at the section for name and tone. */
function openVoice(): void {
    here().dock.page({ screen: 'settings' });
    setTimeout(() => document.getElementById(voiceSectionId)?.scrollIntoView?.(), 0);
}

/** Whether the setup, once read, holds an AI that can drive the chat; null while it is not read. */
const aiConnected = (): boolean | null => {
    const hosts = setup.value?.hosts;
    return hosts ? chatHosts(hosts).drive.length > 0 : null;
};

/**
 * Makes the set-up chat, shows it and sends the message in it as the waiting message, so an AI that went in the
 * meantime gives the words back with its reason; a refused chat gives the words back to the box.
 */
async function begin(text: string): Promise<void> {
    startMaking.value = text;
    let id: string | null;
    // What the start screen showed decides: a chat it did not say starts in Full access never does.
    const pick = startPick.value;
    const wanted = shownMode();
    try {
        id = await startChat(undefined, { askFirst: !(wanted === 'full_access' && defaultMode() === 'full_access') });
    } finally {
        startMaking.value = null;
    }
    if (id === null) {
        startDraft.value = text;
        return;
    }
    startPick.value = newChat('', null);
    startMode.value = null;
    // A tab sends to a chat only in Ask first or Plan only: a chat made in another mode keeps the words in the box.
    if (inTab() && !tabMaySend(chats.value[id]?.mode ?? '')) {
        startDraft.value = text;
        changeChat(id, (each) => told(each, tabLockedSentence(chats.value[id]?.mode ?? '')));
        here().dock.showChat(id);
        return;
    }
    // The chat's first turn names what the controls chose; a mode the desktop did not start the chat in is asked of it.
    changeChat(id, (each) => ({
        ...each,
        host: pick.host,
        model: pick.model,
        effort: pick.effort,
        typing: pick.typing,
    }));
    if (chats.value[id]?.mode !== wanted && can('chat.setMode')) {
        try {
            const answer = await here().desktop.chat.setMode(id, wanted, hostOf(chats.value[id]!)?.id ?? '');
            if (isRefusal(answer)) changeChat(id, (each) => told(each, answer.refused));
            else changeChat(id, (each) => moded(each, answer.mode));
        } catch (error) {
            changeChat(id, (each) => told(each, messageOf(error)));
        }
    }
    here().dock.showChat(id);
    here().dock.close('start');
    sendTurn(id, text, true);
}

// The message goes as soon as an AI is there to take it, once: it is taken out of waiting before the chat is made.
effect(() => {
    const text = startWaiting.value;
    if (text === null || startMaking.value !== null || aiConnected() !== true) return;
    startWaiting.value = null;
    void begin(text);
});

/** What the person sent from the start screen; empty words send nothing, nor do words while one message is in hand. */
export function submitStart(text: string): void {
    const words = text.trim();
    if (!words || startWaiting.value !== null || startMaking.value !== null) return;
    startDraft.value = '';
    startWaiting.value = words;
}

/** The waiting message back into the box. */
export function takeBack(): void {
    const text = startWaiting.value;
    if (text === null) return;
    startDraft.value = text;
    startWaiting.value = null;
}

/**
 * The box's words: the second sentence, that a website link is enough, only where the host that will drive the chat can
 * open a page. Claude Code asks the person once and then opens it; Codex opens none, and with no host nothing can.
 */
function placeholder(): string {
    const host = hostOf(startChatNow());
    return host?.id === 'claude' ? `${t('start.placeholder')} ${t('start.placeholder.site')}` : t('start.placeholder');
}

/** The place of the chat when this place has no desktop: where the chat is, and where to get it. */
function ChatElsewhere() {
    return (
        <div className="painted start">
            <div className="panel-body">
                <div className="ph">
                    <div>
                        <h1>{t('chat.title')}</h1>
                    </div>
                </div>
                <div className="note" role="note">
                    {cannotHere()}
                </div>
                <p>{t('chat.desktop')}</p>
                <p>
                    <a href={releasesPage} target="_blank" rel="noopener noreferrer">
                        {t('chat.get')}
                    </a>
                </p>
                <p className="muted">{t('chat.agent')}</p>
            </div>
        </div>
    );
}

/** The window opens on the chat; a place that cannot make one says where the chat is instead. */
export function StartPanel() {
    return can('chat.create') ? <StartChat /> : <ChatElsewhere />;
}

function StartChat() {
    const modeMenu = useRef<HTMLButtonElement>(null);
    const waiting = startWaiting.value;
    const making = startMaking.value;
    const connected = aiConnected();
    const unread = connected === null;
    // The AI card is there as soon as the setup says no AI drives the chat, before anything is typed; with the setup
    // unread it is there for the reason it could not be read.
    const card = connected === false || (unread && setupError.value !== null);
    const box = useRef<HTMLTextAreaElement>(null);
    const held = waiting !== null || making !== null;
    // The note about name and tone waits for a first message, in Live only, and for a read that holds neither.
    const askable = mode.value === 'live' && !voiceNoteClosed.value && messageWasSent();
    useEffect(() => {
        if (!askable || voiceKnown.value !== 'unread') return;
        let current = true;
        readPreferences(here().api).then(
            (items) => {
                if (current) voiceKnown.value = noneStated(items) ? 'none' : 'some';
            },
            () => {
                if (current) voiceKnown.value = 'failed';
            },
        );
        return () => {
            current = false;
        };
    }, [askable]);
    // The box takes the focus when the window opens on Start, and again when a message is taken back into it.
    useEffect(() => {
        if (!held) box.current?.focus();
    }, [held]);
    const send = (event?: FormEvent) => {
        event?.preventDefault();
        submitStart(startDraft.value);
    };
    const enter = (event: KeyboardEvent<HTMLTextAreaElement>) => {
        if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) send(event);
    };
    const why =
        making !== null
            ? 'The chat is being made'
            : waiting !== null
              ? 'A message is waiting; take it back to change it'
              : startDraft.value.trim() === ''
                ? 'Write who you want to reach first'
                : undefined;
    return (
        <div className="painted start">
            <div className="panel-body">
                <div className="ph">
                    <div>
                        <div className="start-title">
                            <img className="start-mark" src={`${import.meta.env.BASE_URL}favicon.svg`} alt="" />
                            <h1>Who do you want to reach?</h1>
                            <span className="alpha">Alpha</span>
                        </div>
                    </div>
                </div>
                {card && <Painted className="start-ai" view={connect.aiCardView} />}
                {making !== null && (
                    <div className="note start-waiting" role="status" aria-label="Waiting message">
                        <div>Starting the chat:</div>
                        <div className="said">{making}</div>
                    </div>
                )}
                {waiting !== null && (
                    <div className="note start-waiting" role="status" aria-label="Waiting message">
                        <div>
                            {connected === false
                                ? 'Waiting for an AI to be connected. This waits here while the window is open, and goes when one is:'
                                : setupError.value !== null
                                  ? 'The setup could not be read, so this is not sent yet:'
                                  : 'Reading what is set up; this goes once an AI is there:'}
                        </div>
                        <div className="said">{waiting}</div>
                        <div className="crow">
                            <button className="btn sm" type="button" onClick={takeBack}>
                                Take back
                            </button>
                            {unread && (
                                <button className="btn sm" type="button" onClick={() => void connect.reload()}>
                                    Check again
                                </button>
                            )}
                        </div>
                    </div>
                )}
                <form className="composer" onSubmit={send} aria-label="Start">
                    <textarea
                        ref={box}
                        rows={2}
                        placeholder={placeholder()}
                        aria-label="Who do you want to reach?"
                        value={startDraft.value}
                        disabled={held}
                        onChange={(event) => (startDraft.value = event.target.value)}
                        onKeyDown={enter}
                    />
                    <div className="crow">
                        <AccessButton
                            chat={startChatNow()}
                            choose={(chosen) => (startMode.value = chosen)}
                            takeBack={() => void takeBackStanding(here().desktop).catch(() => undefined)}
                            buttonRef={modeMenu}
                        />
                        <span className="grow" />
                        <ModelButton
                            chat={startChatNow()}
                            change={(next) => (startPick.value = next(startPick.value))}
                            choose={(chosen) => (startMode.value = chosen)}
                        />
                        <button
                            className="send"
                            type="submit"
                            disabled={held}
                            title={why ?? t('start.send')}
                            aria-label={t('start.send')}
                        >
                            ↑
                        </button>
                    </div>
                    <ChatNotes chat={startChatNow()} modeMenu={modeMenu} />
                </form>
                {askable && voiceKnown.value === 'none' && (
                    <div className="start-note" role="note" aria-label={t('start.voice.name')}>
                        <span>
                            <strong>{t('start.voice.lead')}</strong> {t('start.voice.body')}
                        </span>
                        <span className="crow start-note-close">
                            <button className="btn sm" type="button" onClick={openVoice}>
                                {t('start.voice.now')}
                            </button>
                            <button className="btn sm" type="button" onClick={closeVoiceNote}>
                                {t('start.voice.later')}
                            </button>
                        </span>
                    </div>
                )}
                {!notesClosed.value.includes(mode.value) && (
                    <div className="start-note" role="note">
                        <span>
                            <strong>{t('start.note.lead')}</strong> {t('start.note.body')}
                        </span>
                        <button
                            className="btn sm start-note-close"
                            type="button"
                            title={t('start.note.close')}
                            aria-label={t('start.note.close')}
                            onClick={closeNote}
                        >
                            ×
                        </button>
                    </div>
                )}
            </div>
        </div>
    );
}
