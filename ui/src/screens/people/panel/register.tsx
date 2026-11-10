import { useState } from 'react';
import { ApiError, Unpaired } from '../../../api/client';
import { useApi } from '../../../api/context';
import { session } from '../../../api/session';
import type { Channel } from '../../../api/types';
import * as panel from './store';

type Props = { contactId: string; channel: Channel };

/**
 * Do not contact for one address, as Jason's own register holds it: whether Jason will contact the address, and the
 * act that changes it. Putting it on takes an optional reason; taking it off requires one, as the runtime does.
 */
export function RegisterControl({ contactId, channel }: Props) {
    const api = useApi();
    const [asking, setAsking] = useState(false);
    const [reason, setReason] = useState('');
    const [busy, setBusy] = useState(false);
    const [failure, setFailure] = useState<string | null>(null);

    const read = panel.register.value?.contactId === contactId ? panel.register.value.entries : {};
    const key = panel.addressKey(channel.channel, channel.value);
    if (!(key in read)) return null;
    const listed = read[key] !== null;
    const id = `register-${channel.channel}-${channel.value}`.replace(/[^A-Za-z0-9_-]/g, '-');

    async function act() {
        setBusy(true);
        setFailure(null);
        const why = reason.trim();
        try {
            if (listed) await panel.contactAgain(api, contactId, channel.channel, channel.value, why);
            else await panel.doNotContact(api, contactId, channel.channel, channel.value, why || undefined);
            setAsking(false);
            setReason('');
        } catch (error) {
            if (error instanceof Unpaired) session.clear();
            setFailure(error instanceof ApiError ? error.message : String(error));
        } finally {
            setBusy(false);
        }
    }

    return (
        <span className="register">
            {listed && <span className="badge warning">Jason will not contact this address</span>}
            {!asking && (
                <button className="btn ghost" type="button" onClick={() => setAsking(true)}>
                    {listed ? 'Contact again' : 'Do not contact'}
                </button>
            )}
            {asking && (
                <span className="ask">
                    {failure && (
                        <span className="notice danger" role="alert">
                            {failure}
                        </span>
                    )}
                    <label htmlFor={id}>
                        {listed ? 'Reason (required, kept in the journal)' : 'Reason (optional, kept in the journal)'}
                    </label>
                    <input
                        id={id}
                        className="input"
                        value={reason}
                        onChange={(event) => setReason(event.target.value)}
                    />
                    <button
                        className="btn"
                        type="button"
                        disabled={busy || (listed && reason.trim() === '')}
                        onClick={() => void act()}
                    >
                        {listed ? 'Take this address off the register' : 'Do not contact this address'}
                    </button>
                    <button
                        className="btn ghost"
                        type="button"
                        onClick={() => {
                            setAsking(false);
                            setFailure(null);
                        }}
                    >
                        Cancel
                    </button>
                </span>
            )}
        </span>
    );
}
