import type { Contact } from '../../api/types';
import type { PersonTab, Route } from '../../router';
import { place, shortDate } from '../../time/format';
import { displayName } from './labels';
import { linked } from './threads';
import { ActionsRow } from '../../layout/actions-row';
import { PanelHeader } from '../../layout/panel-header';
import { Tabs } from '../../layout/tabs';

type Props = {
    person: Contact;
    tab: PersonTab;
    /** Where each tab of this person is, on the screen the card is shown on. */
    to: (tab: PersonTab) => Route;
    now: Date;
    zone: string;
    onAdd: () => void;
    onClose: () => void;
};

const tabNames: Record<PersonTab, string> = { timeline: 'Timeline', threads: 'Threads', details: 'Details' };

function initials(person: Contact): string {
    const first = person.first_name?.trim().charAt(0) ?? '';
    const last = person.last_name?.trim().charAt(0) ?? '';
    return (first + last || displayName(person).name.charAt(0)).toUpperCase();
}

/** "Head of Sales at Soluna · Mexico City, 03:00 there": the role at the company, then where they are and their clock. */
function sub(person: Contact, now: Date): string {
    const work =
        person.title && person.company
            ? `${person.title} at ${person.company}`
            : (person.title ?? person.company ?? '');
    const where = person.time_zone ? place(person.time_zone, now) : null;
    const local = where ? `${where.city}, ${where.time} there` : '';
    return [work, local].filter(Boolean).join(' · ');
}

/** Who the person is and the campaigns they are on, what can be done, and the tabs: laid out as Work items' panel is. */
export function PersonCard({ person, tab, to, now, zone, onAdd, onClose }: Props) {
    const { name, noFirstName } = displayName(person);
    const tabs: PersonTab[] = linked(person) ? ['timeline', 'details', 'threads'] : ['timeline', 'details'];
    const campaigns = [...(person.campaigns ?? [])].sort(
        (a, b) => Number(a.state === 'excluded') - Number(b.state === 'excluded'),
    );
    return (
        <>
            <PanelHeader
                lead={
                    <span class="avatar" aria-hidden="true">
                        {initials(person)}
                    </span>
                }
                title={
                    <>
                        {name}
                        {noFirstName && <span class="muted"> · no first name</span>}
                    </>
                }
                sub={sub(person, now)}
                under={
                    <div class="onc">
                        {person.archived_at && <span class="badge warning">archived</span>}
                        {campaigns.map((campaign) =>
                            campaign.state === 'excluded' ? (
                                <span key={campaign.campaign_id} class="badge outline">
                                    {campaign.campaign_name} · excluded{' '}
                                    {shortDate(new Date(campaign.updated_at), zone, now)}
                                </span>
                            ) : (
                                <span key={campaign.campaign_id} class="badge accent">
                                    {campaign.campaign_name} · since {shortDate(new Date(campaign.added_at), zone, now)}
                                </span>
                            ),
                        )}
                    </div>
                }
                onClose={onClose}
            />
            <ActionsRow>
                <button class="btn" type="button" onClick={onAdd}>
                    Add to campaign
                </button>
            </ActionsRow>
            <Tabs tabs={tabs.map((each) => ({ key: each, label: tabNames[each] }))} active={tab} to={to} />
        </>
    );
}
