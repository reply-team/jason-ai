import { effect, signal } from '@preact/signals-react';
import { useEffect, useRef, type FormEvent, type KeyboardEvent } from 'react';
import { sendTurn, startChat } from './chat';
import * as connect from './connect';
import { chatHosts } from './live';
import { Painted } from './painted';
import { here, setup, setupError } from './state';

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
    try {
        id = await startChat();
    } finally {
        startMaking.value = null;
    }
    if (id === null) {
        startDraft.value = text;
        return;
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

export function StartPanel() {
    const waiting = startWaiting.value;
    const making = startMaking.value;
    const connected = aiConnected();
    const unread = connected === null;
    // The AI card is there as soon as the setup says no AI drives the chat, before anything is typed; with the setup
    // unread it is there for the reason it could not be read.
    const card = connected === false || (unread && setupError.value !== null);
    const box = useRef<HTMLTextAreaElement>(null);
    const held = waiting !== null || making !== null;
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
                        <h1>Who do you want to reach?</h1>
                        <p>Say who it is, and Jason sets the campaign up with you in a chat.</p>
                        <p className="start-optional">
                            This interface is optional: Jason is driven from a terminal and an agent, and this page, the
                            CLI and the API do the same things.
                        </p>
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
                        rows={3}
                        placeholder="For example: heads of sales at logistics companies in Poland"
                        aria-label="Who do you want to reach?"
                        value={startDraft.value}
                        disabled={held}
                        onChange={(event) => (startDraft.value = event.target.value)}
                        onKeyDown={enter}
                    />
                    <div className="crow">
                        <button className="btn p" type="submit" disabled={held} title={why}>
                            Start
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
