import { useEffect, useState, type FormEvent } from 'react';
import { ApiError, Unpaired } from '../../../api/client';
import { useApi } from '../../../api/context';
import { session } from '../../../api/session';
import type { Contact, ContactUpdateRequest, WorkItem } from '../../../api/types';
import { ActionsRow } from '../../../layout/actions-row';
import { Notice } from '../../../layout/notice';
import { href } from '../../../router';
import { Details } from '../details';
import { channelName } from '../labels';
import * as panel from './store';

type Props = { person: Contact; campaignId: string | undefined; now: Date; zone: string };

type Field = 'first_name' | 'last_name' | 'company' | 'title' | 'time_zone';
const fields: { key: Field; label: string }[] = [
    { key: 'first_name', label: 'First name' },
    { key: 'last_name', label: 'Last name' },
    { key: 'company', label: 'Company' },
    { key: 'title', label: 'Title' },
    { key: 'time_zone', label: 'Time zone' },
];
/** The fields the provider's contact.update takes; the time zone and the channels are Jason's alone. */
const providerFields: Field[] = ['first_name', 'last_name', 'company', 'title'];
const kinds = ['email', 'phone', 'linkedin'];

type Row = { channel: string; value: string; label: string | null; primary: boolean; data: unknown };
type Draft = Record<Field, string> & { channels: Row[] };

function draftOf(person: Contact): Draft {
    return {
        first_name: person.first_name ?? '',
        last_name: person.last_name ?? '',
        company: person.company ?? '',
        title: person.title ?? '',
        time_zone: person.time_zone ?? '',
        channels: person.channels.map((each) => ({
            channel: each.channel,
            value: each.value,
            label: (each as { label?: string | null }).label ?? null,
            primary: each.primary,
            data: (each as { data?: unknown }).data ?? null,
        })),
    };
}

/** The fields the draft changed, a field emptied as null; the channels whole, when any of them changed. */
function changes(person: Contact, draft: Draft): Omit<ContactUpdateRequest, 'contact_id' | 'actor'> {
    const was = draftOf(person);
    const changed: Omit<ContactUpdateRequest, 'contact_id' | 'actor'> = {};
    for (const { key } of fields) {
        const value = draft[key].trim();
        if (value !== was[key]) changed[key] = value === '' ? null : value;
    }
    const channels = draft.channels
        .filter((row) => row.value.trim() !== '')
        .map((row) => ({ ...row, value: row.value.trim() }));
    if (JSON.stringify(channels) !== JSON.stringify(was.channels)) changed.channels = channels;
    return changed;
}

type Said = { queued: WorkItem; campaign: string } | { refused: string } | 'saved';

function message(error: unknown): string {
    if (error instanceof Unpaired) session.clear();
    return error instanceof ApiError ? error.message : String(error);
}

/** Details: the record as it stands, and the form that changes it, with a reason kept in the journal. */
export function DetailsTab({ person, campaignId, now, zone }: Props) {
    const api = useApi();
    const [draft, setDraft] = useState<Draft | null>(null);
    const [reason, setReason] = useState('');
    const [provider, setProvider] = useState(false);
    const [campaign, setCampaign] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [failure, setFailure] = useState<string | null>(null);
    const [said, setSaid] = useState<Said | null>(null);

    // Another person is another form; the same person read again keeps what is being typed.
    useEffect(() => {
        setDraft(null);
        setSaid(null);
        setFailure(null);
    }, [person.id]);

    const live = panel.liveCampaigns(person);
    const chosen = campaign ?? panel.defaultCampaign(person, campaignId);

    if (!draft)
        return (
            <>
                <ActionsRow>
                    <button
                        className="btn"
                        type="button"
                        onClick={() => {
                            setDraft(draftOf(person));
                            setReason('');
                            setProvider(false);
                            setCampaign(null);
                            setSaid(null);
                            setFailure(null);
                        }}
                    >
                        Edit
                    </button>
                </ActionsRow>
                {said && <SaidLine said={said} />}
                <Details person={person} now={now} zone={zone} />
            </>
        );

    const changed = changes(person, draft);
    const forProvider = Object.fromEntries(
        providerFields.filter((key) => key in changed).map((key) => [key, changed[key] ?? null]),
    );
    const offerable = Object.keys(forProvider).length > 0;
    const set = (patch: Partial<Draft>) => setDraft({ ...draft, ...patch });
    const setRow = (index: number, patch: Partial<Row>) =>
        set({ channels: draft.channels.map((row, at) => (at === index ? { ...row, ...patch } : row)) });

    async function submit(event: FormEvent) {
        event.preventDefault();
        if (busy || Object.keys(changed).length === 0) return;
        setBusy(true);
        setFailure(null);
        const why = reason.trim() || undefined;
        try {
            const saved = await panel.save(api, { contact_id: person.id, ...changed, ...(why ? { reason: why } : {}) });
            let line: Said = 'saved';
            if (provider && offerable && chosen) {
                const name = live.find((each) => each.campaign_id === chosen)?.campaign_name ?? chosen;
                try {
                    const queued = await panel.queueProviderUpdate(api, chosen, saved, forProvider, why);
                    line = { queued, campaign: name };
                } catch (error) {
                    line = { refused: message(error) };
                }
            }
            setDraft(null);
            setSaid(line);
        } catch (error) {
            setFailure(message(error));
        } finally {
            setBusy(false);
        }
    }

    return (
        <form className="panel-body person-edit" onSubmit={(event) => void submit(event)}>
            {failure && <Notice danger>{failure}</Notice>}
            <div className="fields">
                {fields.map(({ key, label }) => (
                    <div className="field" key={key}>
                        <label htmlFor={`person-${key}`}>{label}</label>
                        <input
                            id={`person-${key}`}
                            className="input"
                            value={draft[key]}
                            onChange={(event) => set({ [key]: event.target.value })}
                        />
                    </div>
                ))}
            </div>
            <div className="section">
                <h3>Channels</h3>
                <ul className="channels">
                    {draft.channels.map((row, index) => (
                        <li key={index}>
                            <select
                                className="select"
                                aria-label="Kind"
                                value={row.channel}
                                onChange={(event) => setRow(index, { channel: event.target.value })}
                            >
                                {[...new Set([...kinds, row.channel])].map((kind) => (
                                    <option key={kind} value={kind}>
                                        {channelName(kind)}
                                    </option>
                                ))}
                            </select>
                            <input
                                className="input"
                                aria-label="Address"
                                value={row.value}
                                onChange={(event) => setRow(index, { value: event.target.value })}
                            />
                            <label className="check">
                                <input
                                    type="radio"
                                    name={`primary-${person.id}`}
                                    checked={row.primary}
                                    onChange={() =>
                                        set({
                                            channels: draft.channels.map((each, at) => ({
                                                ...each,
                                                primary: at === index,
                                            })),
                                        })
                                    }
                                />
                                Primary
                            </label>
                            <button
                                className="btn ghost"
                                type="button"
                                onClick={() => set({ channels: draft.channels.filter((_, at) => at !== index) })}
                            >
                                Remove
                            </button>
                        </li>
                    ))}
                </ul>
                <button
                    className="btn"
                    type="button"
                    onClick={() =>
                        set({
                            channels: [
                                ...draft.channels,
                                { channel: 'email', value: '', label: null, primary: false, data: null },
                            ],
                        })
                    }
                >
                    Add a channel
                </button>
            </div>
            <div className="field">
                <label htmlFor="person-reason">Reason (optional, kept in the journal)</label>
                <textarea
                    id="person-reason"
                    className="textarea"
                    value={reason}
                    onChange={(event) => setReason(event.target.value)}
                />
            </div>
            {chosen && (
                <div className="provider">
                    <label className="check">
                        <input
                            type="checkbox"
                            checked={provider && offerable}
                            disabled={!offerable}
                            onChange={(event) => setProvider(event.target.checked)}
                        />
                        Also queue the name, company and title changes for the provider, in{' '}
                        {live.length === 1 ? live[0]!.campaign_name : null}
                    </label>
                    {live.length > 1 && (
                        <select
                            className="select"
                            aria-label="The campaign the change is queued in"
                            value={chosen}
                            onChange={(event) => setCampaign(event.target.value)}
                        >
                            {live.map((each) => (
                                <option key={each.campaign_id} value={each.campaign_id}>
                                    {each.campaign_name}
                                </option>
                            ))}
                        </select>
                    )}
                </div>
            )}
            <ActionsRow>
                <button className="btn primary" type="submit" disabled={busy || Object.keys(changed).length === 0}>
                    Save
                </button>
                <button className="btn" type="button" onClick={() => setDraft(null)}>
                    Cancel
                </button>
            </ActionsRow>
        </form>
    );
}

/** What the last save did: saved, saved and queued for the provider, or saved with the queue refused. */
function SaidLine({ said }: { said: Said }) {
    if (said === 'saved') return <Notice status>Saved.</Notice>;
    if ('refused' in said)
        return <Notice status>Saved in Jason. Queuing the change for the provider was refused: {said.refused}</Notice>;
    return (
        <Notice status>
            Saved. The change is queued for the provider in {said.campaign}.{' '}
            <a
                href={href({
                    screen: 'workitems',
                    tab: said.queued.status === 'created' ? 'planned' : 'now',
                    id: said.queued.id,
                    filters: {},
                })}
            >
                Open the work item
            </a>
        </Notice>
    );
}
