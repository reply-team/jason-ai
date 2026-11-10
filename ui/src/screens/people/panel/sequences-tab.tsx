import type { Contact, ContactCampaign } from '../../../api/types';
import { href } from '../../../router';
import { shortDate } from '../../../time/format';

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
    const campaigns = memberships(person);
    if (campaigns.length === 0)
        return (
            <div className="panel-body">
                <div className="empty">Not in any campaign.</div>
            </div>
        );
    return (
        <div className="panel-body">
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
                        </li>
                    );
                })}
            </ul>
        </div>
    );
}
