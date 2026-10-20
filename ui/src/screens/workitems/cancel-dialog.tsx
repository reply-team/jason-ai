import { useEffect, useRef, useState } from 'preact/hooks';

/** Where the work stands: an attempt running now, none ever made, or earlier attempts that ended. */
export type Ran = 'running' | 'never' | 'before';

const said: Record<Ran, string> = {
    running: 'The running attempt is stopped.',
    never: 'Nothing has reached the provider.',
    before: 'No further attempt is made; what earlier attempts did stays done.',
};

type Props = {
    what: string;
    ran: Ran;
    onConfirm: (reason: string | undefined) => void;
    onCancel: () => void;
};

/** Cancelling work: what it does and does not do, and an optional reason the journal keeps. */
export function CancelDialog({ what, ran, onConfirm, onCancel }: Props) {
    const [reason, setReason] = useState('');
    const field = useRef<HTMLTextAreaElement>(null);
    useEffect(() => field.current?.focus(), []);
    return (
        <div
            class="overlay"
            onKeyDown={(e) => {
                if (e.key === 'Escape') onCancel();
            }}
        >
            <div class="dialog" role="dialog" aria-label={`Cancel ${what}`}>
                <h2>Cancel {what}?</h2>
                <p>{said[ran]}</p>
                <div class="field">
                    <label for="cancel-reason">Reason (optional, kept in the journal)</label>
                    <textarea
                        ref={field}
                        id="cancel-reason"
                        class="textarea"
                        maxLength={2000}
                        value={reason}
                        onInput={(e) => setReason((e.target as HTMLTextAreaElement).value)}
                    />
                </div>
                <div class="btn-row">
                    <button class="btn ghost" type="button" onClick={onCancel}>
                        Keep it
                    </button>
                    <button class="btn danger" type="button" onClick={() => onConfirm(reason.trim() || undefined)}>
                        Cancel it
                    </button>
                </div>
            </div>
        </div>
    );
}
