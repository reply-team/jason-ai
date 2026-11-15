import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { Unpaired } from '../api/client';
import { useApi } from '../api/context';
import type { ApprovalSummary, DecisionSummary, WorkItem } from '../api/types';
import { MessageEditor } from '../screens/attention/message-editor';
import {
    STALE_SENTENCE,
    checkMessage,
    editable,
    readItem,
    sentence,
    uncheckedSentence,
} from '../screens/attention/message-edit';
import { Doubts } from '../screens/send-modes/doubts';
import { attention } from './needs';
import { clock, here } from './state';
import { toApprovalCard, toQuestionCard } from './waiting-model';
import {
    answerWaiting,
    approveWaiting,
    drawWaiting,
    editedWaiting,
    moreWaiting,
    rejectWaiting,
    waitingOf,
    type Waiting,
} from './waiting';

// An agent's chat, under its conversation: the campaign's approvals and questions that wait on a person, as cards.
// Approve, Edit, Reject and an answer act through Needs you's holder; Details opens the item in Needs you.

const readingSentence = 'This approval is being read; it cannot be approved until it is shown here whole.';
const unreadSentence =
    'This approval could not be read, so it cannot be approved here; Details opens it in Needs you. The runtime said:';

function ApprovalCardView({ waiting, row }: { waiting: Waiting; row: ApprovalSummary }) {
    const api = useApi();
    const whole = waiting.approvals.value[row.id];
    const unreadWhy = waiting.unread.value[row.id];
    const card = toApprovalCard(row, whole, clock.value);
    const busy = waiting.store.busy.value;
    const [rejecting, setRejecting] = useState(false);
    const [reason, setReason] = useState('');
    const [editing, setEditing] = useState<WorkItem | null>(null);
    const [checking, setChecking] = useState(false);
    const [problem, setProblem] = useState<string | null>(null);
    const reasonBox = useRef<HTMLInputElement>(null);
    const rejectButton = useRef<HTMLButtonElement>(null);
    const wasRejecting = useRef(false);
    // The question takes focus as it opens, and gives it back to Reject once it closes without rejecting.
    useEffect(() => {
        if (rejecting) reasonBox.current?.focus();
        else if (wasRejecting.current) rejectButton.current?.focus();
        wasRejecting.current = rejecting;
    }, [rejecting]);
    // Every approval, whatever it does, is decided only once it has been read whole: its doubts, its words or what will
    // happen, and the person it reaches all come from that read. The rule is the card's, never a list of kinds.
    const unread = whole === undefined;
    const held = busy || checking || unread;

    // The browser's approval panel's check, at the press: the work item is read again, and a message whose text moved
    // since the approval was asked is not approved, since the runtime asks again with the new text.
    async function approve() {
        setProblem(null);
        if (whole === undefined) return;
        if (card.message) {
            setChecking(true);
            const checked = await checkMessage(api, whole).finally(() => setChecking(false));
            if (checked === null) return;
            if ('unchecked' in checked) return setProblem(uncheckedSentence(checked.unchecked));
            if (checked.stale) return setProblem(STALE_SENTENCE);
        }
        await approveWaiting(api, waiting, row.id);
    }

    async function edit() {
        if (whole === undefined) return;
        setProblem(null);
        setChecking(true);
        try {
            setEditing(await readItem(api, whole.work_item_id));
        } catch (error) {
            if (!(error instanceof Unpaired)) setProblem(sentence(error));
        } finally {
            setChecking(false);
        }
    }

    function details() {
        here().dock.show('needs');
        void attention.open(api, 'approvals', row.id);
    }

    const reject = (event: FormEvent) => {
        event.preventDefault();
        setRejecting(false);
        void rejectWaiting(api, waiting, row.id, reason.trim() || undefined);
    };

    return (
        <div className="ask waiting-card" data-id={row.id} role="group" aria-label={card.title}>
            <div className="ask-h">
                <span className="b amber">Waiting for you</span> <b>{card.what}</b>{' '}
                {card.to !== null && <span className="waiting-to">to {card.to}</span>}{' '}
                <span className="meta">{[card.channel, card.age].filter(Boolean).join(' · ')}</span>
            </div>
            {card.from !== null && <div className="meta waiting-from">From {card.from}</div>}
            <div className="ask-w">{card.why}</div>
            {/* Who doubted a send and why, below the sentence that says so and above the words, as the panel draws it. */}
            {whole !== undefined && <Doubts preview={whole.preview} />}
            {card.subject !== null && <div className="ask-s">{card.subject}</div>}
            {card.body !== null && <div className="said waiting-body">{card.body}</div>}
            {card.intent !== null && <div className="waiting-intent">{card.intent}</div>}
            {/* An approval is approved only once it is here whole: until then, the card says why it cannot be. */}
            {unread && (
                <div className="note am" role="status">
                    {unreadWhy === undefined ? (
                        readingSentence
                    ) : (
                        <>
                            {unreadSentence} {unreadWhy.code !== null && <code>{unreadWhy.code}</code>}{' '}
                            {unreadWhy.message}
                        </>
                    )}
                </div>
            )}
            {problem !== null && (
                <div className="note am" role="status">
                    {problem}
                </div>
            )}
            {editing !== null && whole !== undefined ? (
                <div className="waiting-edit">
                    <MessageEditor
                        approval={whole}
                        item={editing}
                        busy={busy}
                        startOpen
                        onMoved={setEditing}
                        onSaved={() => {
                            setEditing(null);
                            editedWaiting(api, waiting, whole);
                        }}
                        onClose={() => setEditing(null)}
                    />
                </div>
            ) : rejecting ? (
                <form
                    className="note am waiting-reject"
                    role="alertdialog"
                    aria-label={`Reject ${card.title}?`}
                    onSubmit={reject}
                    onKeyDown={(event) => {
                        if (event.key === 'Escape') setRejecting(false);
                    }}
                >
                    <div>Reject {card.title}?</div>
                    <input
                        ref={reasonBox}
                        className="inp"
                        aria-label="Reason (optional, kept in the journal)"
                        placeholder="Reason (optional, kept in the journal)"
                        maxLength={2000}
                        value={reason}
                        onChange={(event) => setReason(event.target.value)}
                    />
                    <div className="crow">
                        <button className="btn sm" type="button" onClick={() => setRejecting(false)}>
                            Cancel
                        </button>
                        <button className="btn sm am" type="submit" disabled={busy}>
                            Reject
                        </button>
                    </div>
                </form>
            ) : (
                <div className="ask-a">
                    <button className="btn sm p" type="button" disabled={held} onClick={() => void approve()}>
                        Approve
                    </button>
                    {whole !== undefined && editable(whole) && (
                        <button className="btn sm" type="button" disabled={held} onClick={() => void edit()}>
                            Edit
                        </button>
                    )}
                    <button
                        ref={rejectButton}
                        className="btn sm"
                        type="button"
                        disabled={busy || checking}
                        onClick={() => {
                            setReason('');
                            setRejecting(true);
                        }}
                    >
                        Reject
                    </button>
                    <span className="grow" />
                    <button className="btn sm" type="button" onClick={details}>
                        Details
                    </button>
                </div>
            )}
        </div>
    );
}

function QuestionCardView({ waiting, row }: { waiting: Waiting; row: DecisionSummary }) {
    const api = useApi();
    const card = toQuestionCard(row, waiting.questions.value[row.id], clock.value);
    const busy = waiting.store.busy.value;
    const [text, setText] = useState('');
    const [reason, setReason] = useState('');
    const optional = (value: string) => value.trim() || undefined;
    const answer = (event: FormEvent) => {
        event.preventDefault();
        if (text.trim()) void answerWaiting(api, waiting, row.id, { text: text.trim() }, optional(reason));
    };
    return (
        <div className="ask q waiting-card" data-id={row.id} role="group" aria-label={`Question: ${card.question}`}>
            <div className="ask-h">
                <span className="b violet">Question</span> <span className="meta">{card.age}</span>
            </div>
            <div className="ask-s">{card.question}</div>
            {card.options.length > 0 && (
                <div className="ask-a">
                    {card.options.map((option) => (
                        <button
                            key={option.label}
                            className="btn sm"
                            type="button"
                            title={option.detail ?? undefined}
                            disabled={busy}
                            onClick={() =>
                                void answerWaiting(api, waiting, row.id, { option: option.label }, optional(reason))
                            }
                        >
                            {option.label}
                        </button>
                    ))}
                </div>
            )}
            <form className="ask-a" onSubmit={answer} aria-label="Answer in your own words">
                <input
                    className="inp"
                    aria-label="Your answer"
                    placeholder="Your answer"
                    value={text}
                    onChange={(event) => setText(event.target.value)}
                />
                <button className="btn sm p" type="submit" disabled={busy || !text.trim()}>
                    Answer
                </button>
                <input
                    className="inp"
                    aria-label="Reason (optional)"
                    placeholder="Reason (optional)"
                    value={reason}
                    onChange={(event) => setReason(event.target.value)}
                />
            </form>
        </div>
    );
}

/** The cards of what waits in the chat's campaign, read from that campaign's lists while the chat is drawn. */
export function WaitingCards({ campaignId }: { campaignId: string }) {
    const api = useApi();
    const waiting = useMemo(() => waitingOf(campaignId), [campaignId]);
    useEffect(() => drawWaiting(api, campaignId), [api, campaignId]);
    const store = waiting.store;
    const approvals = store.approvals.value;
    const questions = store.questions.value;
    const refused = waiting.refused.value;
    const reasked = waiting.reasked.value;
    // What the runtime refused, with its code: an act's refusal stays until the next act; a read that failed, until the
    // next read.
    const failure = store.failure.value;
    const said = refused ?? (failure === null ? null : { code: store.failureCode.value, message: failure });
    const more = store.approvalsCursor.value !== null || store.questionsCursor.value !== null;
    if (approvals.length === 0 && questions.length === 0 && reasked.length === 0 && said === null) return null;
    return (
        <section className="chat-waiting-for" aria-label="Waiting for you">
            <div className="lab">Waiting for you</div>
            {said !== null && (
                <div className="note am" role="alert">
                    {said.code !== null && <code>{said.code}</code>} {said.message}
                </div>
            )}
            {questions.map((row) => (
                <QuestionCardView key={row.id} waiting={waiting} row={row} />
            ))}
            {approvals.map((row) => (
                <ApprovalCardView key={row.id} waiting={waiting} row={row} />
            ))}
            {reasked.length > 0 && (
                <div className="note chat-note" role="status">
                    Jason asks again with the new text when it next takes this work.
                </div>
            )}
            {more && (
                <button className="btn sm" type="button" onClick={() => void moreWaiting(api, waiting)}>
                    Show more
                </button>
            )}
        </section>
    );
}
