import { useEffect, useLayoutEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { Unpaired } from '../api/client';
import { useApi } from '../api/context';
import type { ApprovalSummary, DecisionSummary, WorkItem } from '../api/types';
import { MessageEditor } from '../screens/attention/message-editor';
import { SenderLine } from '../screens/attention/sender-line';
import { repeated } from '../screens/attention/store';
import { editable, readItem, sentence } from '../screens/attention/message-edit';
import { Doubts } from '../screens/send-modes/doubts';
import { attention } from './needs';
import { senderReads, type SenderReads } from './senders';
import { clock, here } from './state';
import {
    approvalGate,
    approvedSentence,
    readingSentence,
    toApprovalCard,
    toQuestionCard,
    unreadSentence,
} from './waiting-model';
import {
    answerWaiting,
    approveWaiting,
    approveWhole,
    drawWaiting,
    editedWaiting,
    moreWaiting,
    rejectWaiting,
    waitingOf,
    type Waiting,
} from './waiting';

// An agent's chat, under its conversation: the campaign's approvals and questions that wait on a person, as cards.
// Approve, Edit, Reject and an answer act through Needs you's holder; Details opens the item in Needs you.

function ApprovalCardView({ waiting, row, senders }: { waiting: Waiting; row: ApprovalSummary; senders: SenderReads }) {
    const api = useApi();
    const whole = waiting.approvals.value[row.id];
    const gate = approvalGate(whole, waiting.unread.value[row.id]);
    const card = toApprovalCard(row, whole, clock.value);
    // An act of this chat's, on any card, holds every card's acts from its press until the lists read after it have
    // answered: a second click never lands on the card that moves up into the decided one's place.
    const busy = waiting.store.busy.value || waiting.store.acting.value > 0;
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
    // A message's words are clamped to four lines; one that runs past them offers to open them in place, and stays open
    // for this card. Measured in the page, and again as the card's width changes; a body that fits changes nothing. The
    // clamp holds no act: the gate below is the whole read's alone.
    const bodyBox = useRef<HTMLDivElement>(null);
    const [overflows, setOverflows] = useState(false);
    const [opened, setOpened] = useState<string | null>(null);
    const open = card.body !== null && opened === card.body;
    useLayoutEffect(() => {
        const box = bodyBox.current;
        if (box === null || open) return;
        const measure = () => setOverflows(box.scrollHeight > box.clientHeight + 1);
        measure();
        if (typeof ResizeObserver === 'undefined') return;
        const resized = new ResizeObserver(measure);
        resized.observe(box);
        return () => resized.disconnect();
    }, [card.body, open]);
    const unopened = card.body !== null && overflows && !open;
    // Every approval, whatever it does, is decided only once it has been read whole (`approvalGate`, which Needs you's
    // rows follow too): its doubts, its words or what will happen, and the person it reaches all come from that read.
    const held = busy || checking || !gate.live;

    // A message is checked against its work item at the press, and one whose text moved since the approval was asked is
    // not approved, since the runtime asks again with the new text.
    async function approve() {
        setProblem(null);
        const act =
            whole === undefined
                ? undefined
                : { workItemId: whole.work_item_id, operation: whole.operation, what: card.what, to: card.to };
        const why = await approveWhole(api, whole, () => approveWaiting(api, waiting, row.id, act), setChecking);
        if (why !== null) setProblem(why);
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
                <span className="meta">
                    {[card.channel, card.from !== null && `from ${card.from}`, card.age].filter(Boolean).join(' · ')}
                </span>
            </div>
            {/* The sending name the provider holds now for the mailbox named above: a fact read as the card is drawn, which
                decides nothing. */}
            <SenderLine approval={whole} reads={senders} />
            <div className="ask-w">{card.why}</div>
            {/* Who doubted a send and why, below the sentence that says so and above the words, as the panel draws it. */}
            {whole !== undefined && <Doubts preview={whole.preview} />}
            {card.subject !== null && <div className="ask-s">{card.subject}</div>}
            {card.body !== null && (
                <div ref={bodyBox} className={open ? 'ask-b open said waiting-body' : 'ask-b said waiting-body'}>
                    {card.body}
                </div>
            )}
            {unopened && (
                <div className="waiting-more">
                    <button className="btn sm ghost" type="button" onClick={() => setOpened(card.body)}>
                        Show the whole message
                    </button>
                </div>
            )}
            {card.intent !== null && <div className="waiting-intent">{card.intent}</div>}
            {/* An approval is approved only once it is here whole: until then, the card says why it cannot be. */}
            {!gate.live && (
                <div className="note am" role="status">
                    {'reading' in gate ? (
                        readingSentence('chat')
                    ) : (
                        <>
                            {unreadSentence('chat', gate.unread)}{' '}
                            {gate.unread.code !== null && <code>{gate.unread.code}</code>} {gate.unread.message}
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
                        <button
                            className="btn sm no"
                            type="submit"
                            disabled={busy}
                            onClick={(event) => {
                                if (repeated(event.nativeEvent)) event.preventDefault();
                            }}
                        >
                            Reject
                        </button>
                    </div>
                </form>
            ) : (
                <div className="ask-a">
                    <button
                        className="btn sm ok"
                        type="button"
                        disabled={held}
                        onClick={(event) => {
                            // The second click of a double click is no second Approve.
                            if (!repeated(event.nativeEvent)) void approve();
                        }}
                    >
                        Approve
                    </button>
                    {whole !== undefined && editable(whole) && (
                        <button className="btn sm" type="button" disabled={held} onClick={() => void edit()}>
                            Edit
                        </button>
                    )}
                    <button
                        ref={rejectButton}
                        className="btn sm no"
                        type="button"
                        disabled={busy || checking}
                        onClick={() => {
                            setReason('');
                            setRejecting(true);
                        }}
                    >
                        Reject
                    </button>
                    <button className="btn sm ghost lk" type="button" onClick={details}>
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
    const busy = waiting.store.busy.value || waiting.store.acting.value > 0;
    const [text, setText] = useState('');
    const [reason, setReason] = useState('');
    // An answer of one's own and the reason sit behind one more pill, "Something else…", as the console's option card
    // draws them, and open in place below the options; a question with no options takes no other answer, so they stand
    // open. An option pressed while they are open carries the reason typed, and one pressed while they are shut, none.
    const [other, setOther] = useState(false);
    const own = card.options.length === 0 || other;
    const ownBox = useRef<HTMLInputElement>(null);
    const wasOther = useRef(false);
    useEffect(() => {
        if (other && !wasOther.current) ownBox.current?.focus();
        wasOther.current = other;
    }, [other]);
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
                <div className="ui-opts">
                    {card.options.map((option) => (
                        <button
                            key={option.label}
                            className="ui-opt"
                            type="button"
                            title={option.detail ?? undefined}
                            disabled={busy}
                            onClick={(event) => {
                                if (repeated(event.nativeEvent)) return;
                                void answerWaiting(
                                    api,
                                    waiting,
                                    row.id,
                                    { option: option.label },
                                    own ? optional(reason) : undefined,
                                );
                            }}
                        >
                            {option.label}
                        </button>
                    ))}
                    <button
                        className={other ? 'ui-opt on' : 'ui-opt'}
                        type="button"
                        aria-expanded={other}
                        onClick={() => setOther(!other)}
                    >
                        Something else…
                    </button>
                </div>
            )}
            {own && (
                <form
                    className={card.options.length > 0 ? 'ask-a ui-other' : 'ask-a'}
                    onSubmit={answer}
                    aria-label="Answer in your own words"
                >
                    <input
                        ref={ownBox}
                        className="inp"
                        aria-label="Your answer"
                        placeholder="Your answer"
                        value={text}
                        onChange={(event) => setText(event.target.value)}
                    />
                    <button
                        className="btn sm p"
                        type="submit"
                        disabled={busy || !text.trim()}
                        onClick={(event) => {
                            if (repeated(event.nativeEvent)) event.preventDefault();
                        }}
                    >
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
            )}
        </div>
    );
}

/** The cards of what waits in the chat's campaign, read from that campaign's lists while the chat is drawn. */
export function WaitingCards({ campaignId }: { campaignId: string }) {
    const api = useApi();
    const waiting = useMemo(() => waitingOf(campaignId), [campaignId]);
    useEffect(() => drawWaiting(api, campaignId), [api, campaignId]);
    // The accounts the cards' sender lines read, one read per card, kept while this chat is drawn.
    const senders = useMemo(() => senderReads(), [campaignId]);
    const store = waiting.store;
    const approvals = store.approvals.value;
    const questions = store.questions.value;
    const refused = waiting.refused.value;
    const reasked = waiting.reasked.value;
    const approved = waiting.approved.value;
    // What the runtime refused, with its code: an act's refusal stays until the next act; a read that failed, until the
    // next read.
    const failure = store.failure.value;
    const said = refused ?? (failure === null ? null : { code: store.failureCode.value, message: failure });
    const more = store.approvalsCursor.value !== null || store.questionsCursor.value !== null;
    if (
        approvals.length === 0 &&
        questions.length === 0 &&
        reasked.length === 0 &&
        approved.length === 0 &&
        said === null
    )
        return null;
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
                <ApprovalCardView key={row.id} waiting={waiting} row={row} senders={senders} />
            ))}
            {approved.map((each) => (
                <div key={each.approvalId} className="note chat-note approved-line" role="status">
                    {approvedSentence(each)}
                </div>
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
