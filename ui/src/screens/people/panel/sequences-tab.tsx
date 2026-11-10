import { useEffect, useState } from 'react';
import { ApiError, Unpaired } from '../../../api/client';
import { useApi } from '../../../api/context';
import { session } from '../../../api/session';
import type { Contact, ContactCampaign } from '../../../api/types';
import { Notice } from '../../../layout/notice';
import { href } from '../../../router';
import { shortDate } from '../../../time/format';
import * as panel from './store';

type Props = { person: Contact; now: Date; zone: string };

const stateWords: Record<string, string> = {
    enrolled: 'Enrolled',
    paused: 'Paused',
    finished: 'Finished',
    excluded: 'Excluded',
};

/** The campaigns the person is in first, then the ones they left, each as contact.get answered it. */
export function memberships(person: Contact): ContactCampaign[] {
    return [...(person.campaigns ?? [])].sort(
        (a, b) => Number(a.state === 'excluded') - Number(b.state === 'excluded'),
    );
}

/** Sequences: the campaigns the person is in, with their state in each and since when. */
export function SequencesTab({ person, now, zone }: Props) {
    const [said, setSaid] = useState<string | null>(null);
    useEffect(() => setSaid(null), [person.id]);
    const campaigns = memberships(person);
    if (campaigns.length === 0)
        return (
            <div className="panel-body">
                <div className="empty">Not in any campaign.</div>
            </div>
        );
    return (
        <div className="panel-body">
            {said && <Notice status>{said}</Notice>}
            <ul className="sequences">
                {campaigns.map((campaign) => {
                    const date =
                        campaign.state === 'enrolled'
                            ? `added ${shortDate(new Date(campaign.added_at), zone, now)}`
                            : `changed ${shortDate(new Date(campaign.updated_at), zone, now)}`;
                    return (
                        <li key={campaign.campaign_id}>
                            <a
                                className="name"
                                href={href({
                                    screen: 'campaigns',
                                    id: campaign.campaign_id,
                                    tab: 'timeline',
                                    view: 'summary',
                                    period: '7d',
                                    filters: {},
                                })}
                            >
                                {campaign.campaign_name}
                            </a>
                            <span className={`badge ${campaign.state === 'enrolled' ? 'accent' : 'outline'}`}>
                                {stateWords[campaign.state] ?? campaign.state}
                            </span>
                            <span className="muted">{date}</span>
                            {(campaign.state === 'enrolled' || campaign.state === 'paused') && (
                                <ExcludeControl person={person} campaign={campaign} onSaid={setSaid} />
                            )}
                        </li>
                    );
                })}
            </ul>
        </div>
    );
}

type ExcludeProps = { person: Contact; campaign: ContactCampaign; onSaid: (line: string) => void };

/** What remove_contacts answered for the person, in words. */
function outcome(item: Awaited<ReturnType<typeof panel.exclude>>, name: string): string {
    switch (item?.status) {
        case 'removed':
            return `Excluded from ${name}.`;
        case 'already_excluded':
            return `Already excluded from ${name}.`;
        case 'not_member':
            return `Not in ${name}; nothing to exclude.`;
        default:
            return `Excluding from ${name} was refused${item?.error ? `: ${item.error.message}` : '.'}`;
    }
}

/** Exclude for one campaign the person is a live member of: an optional reason, kept in the journal, then the act. */
function ExcludeControl({ person, campaign, onSaid }: ExcludeProps) {
    const api = useApi();
    const [asking, setAsking] = useState(false);
    const [reason, setReason] = useState('');
    const [busy, setBusy] = useState(false);
    const [failure, setFailure] = useState<string | null>(null);
    const id = `exclude-${campaign.campaign_id}`;

    async function act() {
        setBusy(true);
        setFailure(null);
        try {
            const item = await panel.exclude(api, campaign.campaign_id, person.id, reason.trim() || undefined);
            setAsking(false);
            setReason('');
            onSaid(outcome(item, campaign.campaign_name));
        } catch (error) {
            if (error instanceof Unpaired) session.clear();
            setFailure(error instanceof ApiError ? error.message : String(error));
        } finally {
            setBusy(false);
        }
    }

    if (!asking)
        return (
            <button className="btn ghost" type="button" onClick={() => setAsking(true)}>
                Exclude
            </button>
        );
    return (
        <span className="ask">
            {failure && (
                <span className="notice danger" role="alert">
                    {failure}
                </span>
            )}
            <label htmlFor={id}>Reason (optional, kept in the journal)</label>
            <input id={id} className="input" value={reason} onChange={(event) => setReason(event.target.value)} />
            <button className="btn" type="button" disabled={busy} onClick={() => void act()}>
                Exclude from {campaign.campaign_name}
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
    );
}
