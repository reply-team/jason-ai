import { useState } from 'preact/hooks';
import { ApiError, Unpaired } from '../../api/client';
import { useApi } from '../../api/context';
import { session } from '../../api/session';
import type { AddContactsResult, Campaign } from '../../api/types';
import { PersonPicker } from '../../pickers/person-picker';
import * as store from './store';
import { Dialog } from '../../layout/dialog';
import { Notice } from '../../layout/notice';

type Props = { campaign: Campaign; onAdded: () => void; onClose: () => void };

/** "2 added · 1 already in it · 1 refused": what the runtime did with each person asked for. */
function outcome(result: AddContactsResult): string {
    const { added, already_member: already, rejected } = result.summary;
    return [
        `${added} added`,
        already > 0 ? `${already} already in it` : null,
        rejected > 0 ? `${rejected} refused` : null,
    ]
        .filter(Boolean)
        .join(' · ');
}

/** Finding people and adding them to the campaign, with an optional reason the journal keeps. */
export function AddPeopleDialog({ campaign, onAdded, onClose }: Props) {
    const api = useApi();
    const [people, setPeople] = useState<{ id: string; name: string }[]>([]);
    const [reason, setReason] = useState('');
    const [busy, setBusy] = useState(false);
    const [refused, setRefused] = useState<string | null>(null);
    const [said, setSaid] = useState<AddContactsResult | null>(null);

    const add = async () => {
        setBusy(true);
        setRefused(null);
        setSaid(null);
        try {
            const result = await store.addPeople(
                api,
                campaign.id,
                people.map((p) => p.id),
                reason.trim() || undefined,
            );
            setSaid(result);
            setPeople([]);
            onAdded();
        } catch (error) {
            if (error instanceof Unpaired) session.clear();
            else setRefused(error instanceof ApiError ? error.message : String(error));
        } finally {
            setBusy(false);
        }
    };

    return (
        <Dialog
            title={<>Add people to {campaign.name}</>}
            label={`Add people to ${campaign.name}`}
            onClose={onClose}
            dismiss={said ? 'Close' : 'Cancel'}
            buttons={
                <>
                    <button
                        class="btn primary"
                        type="button"
                        disabled={people.length === 0 || busy}
                        onClick={() => void add()}
                    >
                        Add
                    </button>
                </>
            }
        >
            <div class="field">
                <PersonPicker
                    label="Find a person"
                    none="Find a person"
                    onPick={(picked) => {
                        if (picked && !people.some((p) => p.id === picked.id)) setPeople([...people, picked]);
                    }}
                />
                {people.length > 0 && (
                    <div class="picked">
                        {people.map((person) => (
                            <span key={person.id} class="badge outline">
                                {person.name}{' '}
                                <button
                                    class="iconbtn"
                                    type="button"
                                    aria-label={`Leave ${person.name} out`}
                                    onClick={() => setPeople(people.filter((p) => p.id !== person.id))}
                                >
                                    ×
                                </button>
                            </span>
                        ))}
                    </div>
                )}
            </div>
            <div class="field">
                <label for="add-people-reason">Reason (optional, kept in the journal)</label>
                <textarea
                    id="add-people-reason"
                    class="textarea"
                    aria-label="Reason"
                    maxLength={2000}
                    value={reason}
                    onInput={(e) => setReason((e.target as HTMLTextAreaElement).value)}
                />
            </div>
            {said && (
                <p class="hint" role="status">
                    {outcome(said)}
                </p>
            )}
            {said?.items
                .filter((item) => item.status === 'rejected')
                .map((item) => (
                    <Notice key={item.index} danger alert={false}>
                        {item.error?.message ?? 'The campaign refused this person.'}
                    </Notice>
                ))}
            {refused && <Notice danger>{refused}</Notice>}
        </Dialog>
    );
}
