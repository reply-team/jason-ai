import { useState } from 'react';
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
                    <button className="btn danger" type="button" onClick={() => onConfirm(reason.trim() || undefined)}>
                        {verb}
                    </button>
                </>
            }
        >
            <div className="field">
                <label htmlFor="decide-reason">Reason (optional, kept in the journal)</label>
                <textarea
                    id="decide-reason"
                    className="textarea"
                    maxLength={2000}
                    value={reason}
                    onChange={(e) => setReason((e.target as HTMLTextAreaElement).value)}
                />
            </div>
        </Dialog>
    );
}
