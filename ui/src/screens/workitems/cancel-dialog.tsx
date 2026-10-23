import { useState } from 'preact/hooks';
import { Dialog } from '../../layout/dialog';

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
    return (
        <Dialog
            title={<>Cancel {what}?</>}
            label={`Cancel ${what}`}
            onClose={onCancel}
            dismiss="Keep it"
            buttons={
                <>
                    <button class="btn danger" type="button" onClick={() => onConfirm(reason.trim() || undefined)}>
                        Cancel it
                    </button>
                </>
            }
        >
            <p>{said[ran]}</p>
            <div class="field">
                <label for="cancel-reason">Reason (optional, kept in the journal)</label>
                <textarea
                    id="cancel-reason"
                    class="textarea"
                    maxLength={2000}
                    value={reason}
                    onInput={(e) => setReason((e.target as HTMLTextAreaElement).value)}
                />
            </div>
        </Dialog>
    );
}
