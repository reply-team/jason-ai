import { useEffect, useState } from 'react';
import { ApiError, Unpaired } from '../../../api/client';
import { useApi } from '../../../api/context';
import { session } from '../../../api/session';
import type { Contact, ContactCampaign } from '../../../api/types';
import { Notice } from '../../../layout/notice';
import { CampaignPicker } from '../../../pickers/campaign-picker';
import { displayName } from '../../../pickers/search';
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

type Named = { id: string; name: string };
/** A move whose add went through and whose step out of the old campaign was refused: the person is in both. */
type Half = { from: Named; to: Named; reason: string | undefined; refused: string };

function message(error: unknown): string {
    if (error instanceof Unpaired) session.clear();
    return error instanceof ApiError ? error.message : String(error);
}

/** Sequences: the campaigns the person is in, with their state in each and since when, and the acts on each. */
export function SequencesTab({ person, now, zone }: Props) {
    const [said, setSaid] = useState<string | null>(null);
    const [half, setHalf] = useState<Half | null>(null);
    useEffect(() => {
        setSaid(null);
        setHalf(null);
    }, [person.id]);
    const campaigns = memberships(person);
    const tell = (line: string) => {
        setHalf(null);
        setSaid(line);
    };
    if (campaigns.length === 0)
        return (
            <div className="panel-body">
                <div className="empty">Not in any campaign.</div>
            </div>
        );
    return (
        <div className="panel-body">
            {said && <Notice status>{said}</Notice>}
            {half && <HalfMove person={person} half={half} onHalf={setHalf} onSaid={tell} />}
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
                                <MembershipActs
                                    person={person}
                                    campaign={campaign}
                                    onSaid={tell}
                                    onHalf={(next) => {
                                        setSaid(null);
                                        setHalf(next);
                                    }}
                                />
                            )}
                        </li>
                    );
                })}
            </ul>
        </div>
    );
}

/** What remove_contacts answered for the person, in words. */
function excluded(item: Awaited<ReturnType<typeof panel.exclude>>, name: string): string {
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

type ActsProps = {
    person: Contact;
    campaign: ContactCampaign;
    onSaid: (line: string) => void;
    onHalf: (half: Half) => void;
};

/**
 * Exclude and Move for one campaign the person is a live member of. Each asks for an optional reason, kept in the
 * journal; Move also asks for the campaign the person goes to.
 */
function MembershipActs({ person, campaign, onSaid, onHalf }: ActsProps) {
    const api = useApi();
    const [asking, setAsking] = useState<'exclude' | 'move' | null>(null);
    const [target, setTarget] = useState<Named | null>(null);
    const [reason, setReason] = useState('');
    const [busy, setBusy] = useState(false);
    const [failure, setFailure] = useState<string | null>(null);
    const id = `${asking ?? 'act'}-${campaign.campaign_id}`;
    const from = { id: campaign.campaign_id, name: campaign.campaign_name };

    function ask(act: 'exclude' | 'move') {
        setAsking(act);
        setTarget(null);
        setReason('');
        setFailure(null);
    }

    async function act() {
        setBusy(true);
        setFailure(null);
        const why = reason.trim() || undefined;
        try {
            if (asking === 'exclude') {
                onSaid(excluded(await panel.exclude(api, from.id, person.id, why), from.name));
                setAsking(null);
            } else if (target) {
                const outcome = await panel.move(api, person.id, from.id, target.id, why);
                if ('notAdded' in outcome) {
                    setFailure(`Adding to ${target.name} was refused: ${outcome.notAdded}. Nothing changed.`);
                    return;
                }
                setAsking(null);
                if ('moved' in outcome) onSaid(`Moved from ${from.name} to ${target.name}.`);
                else
                    onHalf({
                        from,
                        to: target,
                        reason: why,
                        refused:
                            typeof outcome.notRemoved === 'string' ? outcome.notRemoved : message(outcome.notRemoved),
                    });
            }
        } catch (error) {
            setFailure(message(error));
        } finally {
            setBusy(false);
        }
    }

    if (!asking)
        return (
            <span className="acts">
                <button className="btn ghost" type="button" onClick={() => ask('exclude')}>
                    Exclude
                </button>
                <button className="btn ghost" type="button" onClick={() => ask('move')}>
                    Move
                </button>
            </span>
        );
    const ready = asking === 'exclude' || (target !== null && target.id !== from.id);
    return (
        <span className="ask">
            {failure && (
                <span className="notice danger" role="alert">
                    {failure}
                </span>
            )}
            {asking === 'move' && (
                <CampaignPicker
                    label={target?.name ?? 'Choose a campaign'}
                    none="Choose a campaign"
                    onPick={setTarget}
                />
            )}
            <label htmlFor={id}>Reason (optional, kept in the journal)</label>
            <input id={id} className="input" value={reason} onChange={(event) => setReason(event.target.value)} />
            <button className="btn" type="button" disabled={busy || !ready} onClick={() => void act()}>
                {asking === 'exclude' ? `Exclude from ${from.name}` : `Move to ${target?.name ?? '…'}`}
            </button>
            <button
                className="btn ghost"
                type="button"
                onClick={() => {
                    setAsking(null);
                    setFailure(null);
                }}
            >
                Cancel
            </button>
        </span>
    );
}

type HalfProps = {
    person: Contact;
    half: Half;
    onHalf: (half: Half) => void;
    onSaid: (line: string) => void;
};

/** A move left half done: what went through, what was refused, and the step out of the old campaign offered again. */
function HalfMove({ person, half, onHalf, onSaid }: HalfProps) {
    const api = useApi();
    const [busy, setBusy] = useState(false);

    async function again() {
        setBusy(true);
        try {
            const item = await panel.exclude(api, half.from.id, person.id, half.reason);
            if (item?.status === 'removed' || item?.status === 'already_excluded')
                onSaid(`Moved from ${half.from.name} to ${half.to.name}.`);
            else onHalf({ ...half, refused: item?.error?.message ?? item?.status ?? 'no answer for the person' });
        } catch (error) {
            onHalf({ ...half, refused: message(error) });
        } finally {
            setBusy(false);
        }
    }

    return (
        <Notice danger>
            Added to {half.to.name}, but taking them out of {half.from.name} was refused: {half.refused}{' '}
            {displayName(person)} is now in both {half.from.name} and {half.to.name}.{' '}
            <button className="btn" type="button" disabled={busy} onClick={() => void again()}>
                Take them out of {half.from.name} again
            </button>
        </Notice>
    );
}
