import { useState } from 'react';
import type { Decision } from '../../api/types';
import { relative } from '../../time/format';
import { ActionsRow } from '../../layout/actions-row';
import { CopyPromptButton } from './copy-prompt-button';
import { SessionLink } from './session-link';
import { PanelHeader } from '../../layout/panel-header';

type Props = {
    decision: Decision;
    now: Date;
    busy: boolean;
    remaining: string;
    /** What the button that leaves this one says: the next waiting thing on Attention, the campaign elsewhere. */
    skipLabel?: string;
    onAnswer: (choice: { option?: string; text?: string }, reason?: string) => void;
    onSkip: () => void;
    onClose: () => void;
};

export function QuestionPanel({
    decision,
    now,
    busy,
    remaining,
    skipLabel = 'Skip to next',
    onAnswer,
    onSkip,
    onClose,
}: Props) {
    const [option, setOption] = useState<string | null>(null);
    const [text, setText] = useState('');
    const [reason, setReason] = useState('');
    const pending = decision.status === 'pending';
    const canAnswer = pending && !busy && (option !== null || text.trim().length > 0);
    return (
        <div className="panel">
            <PanelHeader
                title={decision.question}
                sub={
                    <>
                        {decision.campaign_name} · asked {relative(new Date(decision.raised_at), now)}
                    </>
                }
                onClose={onClose}
            />
            <div className="panel-body">
                {decision.options && decision.options.length > 0 && (
                    <div className="section">
                        <h3>Options</h3>
                        {pending ? (
                            <div className="options" role="radiogroup">
                                {decision.options.map((o) => (
                                    <button
                                        key={o.label}
                                        type="button"
                                        role="radio"
                                        aria-checked={option === o.label}
                                        className={`option${option === o.label ? ' chosen' : ''}`}
                                        onClick={() => setOption(o.label)}
                                    >
                                        <span className="radio" />
                                        <span>
                                            <div className="label">{o.label}</div>
                                            {o.detail && <div className="detail">{o.detail}</div>}
                                        </span>
                                    </button>
                                ))}
                            </div>
                        ) : (
                            <div className="options">
                                {decision.options.map((o) => (
                                    <div
                                        key={o.label}
                                        className={`option still${o.label === decision.chosen_option ? ' chosen' : ''}`}
                                    >
                                        <span className="radio" />
                                        <span>
                                            <div className="label">{o.label}</div>
                                            {o.detail && <div className="detail">{o.detail}</div>}
                                        </span>
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>
                )}
                {pending && (
                    <div className="field">
                        <label htmlFor="free-answer">
                            {decision.options?.length ? 'Or write an answer' : 'Your answer'}
                        </label>
                        <textarea
                            id="free-answer"
                            className="textarea"
                            value={text}
                            onChange={(e) => {
                                setText((e.target as HTMLTextAreaElement).value);
                                setOption(null);
                            }}
                        />
                    </div>
                )}
                {pending && (
                    <div className="field">
                        <label htmlFor="answer-reason">Why (optional, kept in the journal)</label>
                        <input
                            id="answer-reason"
                            className="input"
                            value={reason}
                            onChange={(e) => setReason((e.target as HTMLInputElement).value)}
                        />
                    </div>
                )}
                <ActionsRow>
                    {pending && (
                        <>
                            <button
                                className="btn primary"
                                type="button"
                                disabled={!canAnswer}
                                onClick={() =>
                                    onAnswer(option ? { option } : { text: text.trim() }, reason.trim() || undefined)
                                }
                            >
                                Answer
                            </button>
                            <CopyPromptButton
                                kind="question"
                                itemId={decision.id}
                                campaignId={decision.campaign_id}
                                workItemId={decision.work_item_id}
                            />
                            <SessionLink kind="question" itemId={decision.id} />
                        </>
                    )}
                </ActionsRow>
                {decision.references && decision.references.length > 0 && (
                    <div className="section">
                        <h3>Read before deciding</h3>
                        <div className="refs">
                            {decision.references.map((r) => (
                                <span key={r.id} className="mono">
                                    {r.kind} {r.id}
                                </span>
                            ))}
                        </div>
                    </div>
                )}
                {!pending && (
                    <dl className="facts">
                        <dt>Answered</dt>
                        <dd>
                            {decision.chosen_option ?? decision.answer} by {decision.answered_by?.id}
                        </dd>
                    </dl>
                )}
            </div>
            {pending && (
                <div className="panel-footer">
                    <span className="next">{remaining}</span>
                    <span className="spacer" />
                    <button className="btn ghost" type="button" onClick={onSkip}>
                        {skipLabel}
                    </button>
                </div>
            )}
        </div>
    );
}
