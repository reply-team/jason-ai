import { useEffect, useRef, useState } from 'preact/hooks';
import { ApiError, Unpaired } from '../../api/client';
import { useApi } from '../../api/context';
import { session } from '../../api/session';
import type { AddContactsResult, Campaign } from '../../api/types';
import { SearchPicker } from '../../pickers/pickers';
import { displayName, firstChannel, searchPeople } from '../workitems/store';
import * as store from './store';

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
    const box = useRef<HTMLDivElement>(null);
    useEffect(() => box.current?.querySelector<HTMLButtonElement>('.select')?.focus(), []);

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
        <div
            class="overlay"
            onKeyDown={(e) => {
                if (e.key === 'Escape') onClose();
            }}
        >
            <div class="dialog" role="dialog" aria-label={`Add people to ${campaign.name}`} ref={box}>
                <h2>Add people to {campaign.name}</h2>
                <div class="field">
                    <SearchPicker
                        label="Find a person"
                        clearLabel="Find a person"
                        placeholder="Find a person"
                        search={async (query) =>
                            (await searchPeople(api, query)).map((c) => ({
                                value: c.id,
                                label: displayName(c),
                                hint: [c.company, firstChannel(c)].filter((part) => part).join(' · ') || undefined,
                            }))
                        }
                        onPick={(picked) => {
                            if (picked && !people.some((p) => p.id === picked.value))
                                setPeople([...people, { id: picked.value, name: picked.label }]);
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
                        <div key={item.index} class="notice danger">
                            {item.error?.message ?? 'The campaign refused this person.'}
                        </div>
                    ))}
                {refused && (
                    <div class="notice danger" role="alert">
                        {refused}
                    </div>
                )}
                <div class="btn-row">
                    <button class="btn ghost" type="button" onClick={onClose}>
                        {said ? 'Close' : 'Cancel'}
                    </button>
                    <button
                        class="btn primary"
                        type="button"
                        disabled={people.length === 0 || busy}
                        onClick={() => void add()}
                    >
                        Add
                    </button>
                </div>
            </div>
        </div>
    );
}
