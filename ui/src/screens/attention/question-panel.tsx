import { useState } from 'preact/hooks';
import type { Decision } from '../../api/types';
import { relative } from '../../time/format';
import { ActionsRow } from '../../layout/actions-row';
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
        <div class="panel">
            <PanelHeader
                title={decision.question}
                sub={
                    <>
                        {decision.campaign_name} · asked {relative(new Date(decision.raised_at), now)}
                    </>
                }
                onClose={onClose}
            />
            <div class="panel-body">
                {decision.options && decision.options.length > 0 && (
                    <div class="section">
                        <h3>Options</h3>
                        {pending ? (
                            <div class="options" role="radiogroup">
                                {decision.options.map((o) => (
                                    <button
                                        key={o.label}
                                        type="button"
                                        role="radio"
                                        aria-checked={option === o.label}
                                        class={`option${option === o.label ? ' chosen' : ''}`}
                                        onClick={() => setOption(o.label)}
                                    >
                                        <span class="radio" />
                                        <span>
                                            <div class="label">{o.label}</div>
                                            {o.detail && <div class="detail">{o.detail}</div>}
                                        </span>
                                    </button>
                                ))}
                            </div>
                        ) : (
                            <div class="options">
                                {decision.options.map((o) => (
                                    <div
                                        key={o.label}
                                        class={`option still${o.label === decision.chosen_option ? ' chosen' : ''}`}
                                    >
                                        <span class="radio" />
                                        <span>
                                            <div class="label">{o.label}</div>
                                            {o.detail && <div class="detail">{o.detail}</div>}
                                        </span>
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>
                )}
                {pending && (
                    <div class="field">
                        <label for="free-answer">
                            {decision.options?.length ? 'Or write an answer' : 'Your answer'}
                        </label>
                        <textarea
                            id="free-answer"
                            class="textarea"
                            value={text}
                            onInput={(e) => {
                                setText((e.target as HTMLTextAreaElement).value);
                                setOption(null);
                            }}
                        />
                    </div>
                )}
                {pending && (
                    <div class="field">
                        <label for="answer-reason">Why (optional, kept in the journal)</label>
                        <input
                            id="answer-reason"
                            class="input"
                            value={reason}
                            onInput={(e) => setReason((e.target as HTMLInputElement).value)}
                        />
                    </div>
                )}
                <ActionsRow>
                    {pending && (
                        <button
                            class="btn primary"
                            type="button"
                            disabled={!canAnswer}
                            onClick={() =>
                                onAnswer(option ? { option } : { text: text.trim() }, reason.trim() || undefined)
                            }
                        >
                            Answer
                        </button>
                    )}
                </ActionsRow>
                {decision.references && decision.references.length > 0 && (
                    <div class="section">
                        <h3>Read before deciding</h3>
                        <div class="refs">
                            {decision.references.map((r) => (
                                <span key={r.id} class="mono">
                                    {r.kind} {r.id}
                                </span>
                            ))}
                        </div>
                    </div>
                )}
                {!pending && (
                    <dl class="facts">
                        <dt>Answered</dt>
                        <dd>
                            {decision.chosen_option ?? decision.answer} by {decision.answered_by?.id}
                        </dd>
                    </dl>
                )}
            </div>
            {pending && (
                <div class="panel-footer">
                    <span class="next">{remaining}</span>
                    <span class="spacer" />
                    <button class="btn ghost" type="button" onClick={onSkip}>
                        {skipLabel}
                    </button>
                </div>
            )}
        </div>
    );
}
