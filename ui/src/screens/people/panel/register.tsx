import { useState } from 'react';
import { ApiError, Unpaired } from '../../../api/client';
import { useApi } from '../../../api/context';
import { session } from '../../../api/session';
import type { Channel, Contact } from '../../../api/types';
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

/**
 * Do not contact for the whole person, read from the register's rows and holding nothing of its own: on when every
 * address is on the register, off when none is, and when some are, not on, with how many. Turning it on puts the
 * others there; turning it off asks one reason and takes each one off with it.
 */
export function WholePersonSwitch({ person }: { person: Contact }) {
    const api = useApi();
    const [asking, setAsking] = useState(false);
    const [reason, setReason] = useState('');
    const [busy, setBusy] = useState(false);

    const read = panel.register.value?.contactId === person.id ? panel.register.value.entries : {};
    const keys = person.channels.map((c) => panel.addressKey(c.channel, c.value));
    if (keys.length === 0 || !keys.every((key) => key in read)) return null;
    const listed = keys.filter((key) => read[key] !== null).length;
    const all = listed === keys.length;
    const id = `whole-${person.id}`;

    async function set(on: boolean, why?: string) {
        setBusy(true);
        try {
            await panel.setWholePerson(api, person, on, why);
            setAsking(false);
            setReason('');
        } finally {
            setBusy(false);
        }
    }

    return (
        <div className="whole">
            <label className="check">
                <input
                    type="checkbox"
                    role="switch"
                    checked={all}
                    aria-checked={all}
                    disabled={busy || asking}
                    onChange={() => (all ? setAsking(true) : void set(true))}
                />
                Do not contact
            </label>
            {listed > 0 && !all && (
                <span className="muted">
                    On for {listed} of {keys.length} channels
                </span>
            )}
            <span className="muted">Jason&apos;s own register, not the provider&apos;s list.</span>
            {asking && (
                <span className="ask">
                    <label htmlFor={id}>Reason (required, kept in the journal)</label>
                    <input
                        id={id}
                        className="input"
                        value={reason}
                        onChange={(event) => setReason(event.target.value)}
                    />
                    <button
                        className="btn"
                        type="button"
                        disabled={busy || reason.trim() === ''}
                        onClick={() => void set(false, reason.trim())}
                    >
                        Take every address off the register
                    </button>
                    <button className="btn ghost" type="button" onClick={() => setAsking(false)}>
                        Cancel
                    </button>
                </span>
            )}
        </div>
    );
}
