import { useEffect, useRef, useState } from 'preact/hooks';

type Props = {
    verb: 'Reject';
    what: string;
    onConfirm: (reason: string | undefined) => void;
    onCancel: () => void;
};

/** The one dialog: a rejection, what it decides, and an optional reason the journal keeps. */
export function DecideDialog({ verb, what, onConfirm, onCancel }: Props) {
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
            <div class="dialog" role="dialog" aria-label={`${verb} ${what}`}>
                <h2>
                    {verb} {what}?
                </h2>
                <div class="field">
                    <label for="decide-reason">Reason (optional, kept in the journal)</label>
                    <textarea
                        ref={field}
                        id="decide-reason"
                        class="textarea"
                        maxLength={2000}
                        value={reason}
                        onInput={(e) => setReason((e.target as HTMLTextAreaElement).value)}
                    />
                </div>
                <div class="btn-row">
                    <button class="btn ghost" type="button" onClick={onCancel}>
                        Cancel
                    </button>
                    <button class="btn danger" type="button" onClick={() => onConfirm(reason.trim() || undefined)}>
                        {verb}
                    </button>
                </div>
            </div>
        </div>
    );
}
