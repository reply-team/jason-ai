import { effect, signal } from '@preact/signals-react';
import type { FormEvent, KeyboardEvent } from 'react';
import { sendTurn, startChat } from './chat';
import * as connect from './connect';
import { chatHosts } from './live';
import { Painted } from './painted';
import { here, setup } from './state';

// The start screen: the window opens on the question who the person wants to reach. What is sent there starts a set-up
// chat. With no AI that drives the chat the AI card comes first and the message waits there, to go once one is
// connected. Reply is not asked for here: its card comes up in a chat when the work needs a way to send.

/** What is typed in the start screen's box. */
export const startDraft = signal('');
/** The message sent from the start screen that waits for an AI to be connected; null when none waits. */
export const startWaiting = signal<string | null>(null);

/** Whether the setup, once read, holds an AI that can drive the chat; null while it is not read. */
const aiConnected = (): boolean | null => {
    const hosts = setup.value?.hosts;
    return hosts ? chatHosts(hosts).drive.length > 0 : null;
};

/** Makes the set-up chat, shows it and sends the message in it; a refusal gives the words back to the box. */
async function begin(text: string): Promise<void> {
    const id = await startChat();
    if (id === null) {
        startDraft.value = text;
        return;
    }
    here().dock.showChat(id);
    here().dock.close('start');
    sendTurn(id, text);
}

// The message goes as soon as an AI is there to take it, once: it is taken out of waiting before the chat is made.
effect(() => {
    const text = startWaiting.value;
    if (text === null || aiConnected() !== true) return;
    startWaiting.value = null;
    void begin(text);
});

/** What the person sent from the start screen; empty words send nothing. */
export function submitStart(text: string): void {
    const words = text.trim();
    if (!words || startWaiting.value !== null) return;
    startDraft.value = '';
    startWaiting.value = words;
}

export function StartPanel() {
    const waiting = startWaiting.value;
    const needsAi = waiting !== null && aiConnected() === false;
    const send = (event?: FormEvent) => {
        event?.preventDefault();
        submitStart(startDraft.value);
    };
    const enter = (event: KeyboardEvent<HTMLTextAreaElement>) => {
        if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) send(event);
    };
    return (
        <div className="painted start">
            <div className="panel-body">
                <div className="ph">
                    <div>
                        <h1>Who do you want to reach?</h1>
                        <p>Say who it is, and Jason sets the campaign up with you in a chat.</p>
                    </div>
                </div>
                {needsAi && <Painted className="start-ai" view={connect.aiCardView} />}
                {waiting !== null && (
                    <div className="note start-waiting" role="status" aria-label="Waiting message">
                        <div>
                            {needsAi ? 'Waiting for an AI to be connected, then this goes:' : 'Starting the chat:'}
                        </div>
                        <div className="said">{waiting}</div>
                        {needsAi && (
                            <button
                                className="btn sm"
                                type="button"
                                onClick={() => {
                                    startDraft.value = waiting;
                                    startWaiting.value = null;
                                }}
                            >
                                Take it back
                            </button>
                        )}
                    </div>
                )}
                <form className="composer" onSubmit={send} aria-label="Start">
                    <textarea
                        rows={3}
                        placeholder="For example: heads of sales at logistics companies in Poland"
                        aria-label="Who do you want to reach?"
                        value={startDraft.value}
                        disabled={waiting !== null}
                        onChange={(event) => (startDraft.value = event.target.value)}
                        onKeyDown={enter}
                    />
                    <div className="crow">
                        <button className="btn p" type="submit" disabled={waiting !== null}>
                            Start
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
