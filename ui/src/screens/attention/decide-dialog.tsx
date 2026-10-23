import { useState } from 'preact/hooks';
import { Dialog } from '../../layout/dialog';

type Props = {
    verb: 'Reject';
    what: string;
    onConfirm: (reason: string | undefined) => void;
    onCancel: () => void;
};

/** The one dialog: a rejection, what it decides, and an optional reason the journal keeps. */
export function DecideDialog({ verb, what, onConfirm, onCancel }: Props) {
    const [reason, setReason] = useState('');
    return (
        <Dialog
            title={
                <>
                    {verb} {what}?
                </>
            }
            label={`${verb} ${what}`}
            onClose={onCancel}
            buttons={
                <>
                    <button class="btn danger" type="button" onClick={() => onConfirm(reason.trim() || undefined)}>
                        {verb}
                    </button>
                </>
            }
        >
            <div class="field">
                <label for="decide-reason">Reason (optional, kept in the journal)</label>
                <textarea
                    id="decide-reason"
                    class="textarea"
                    maxLength={2000}
                    value={reason}
                    onInput={(e) => setReason((e.target as HTMLTextAreaElement).value)}
                />
            </div>
        </Dialog>
    );
}
