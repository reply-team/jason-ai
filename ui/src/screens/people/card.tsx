import type { Contact } from '../../api/types';
import { href, navigate, type PeopleFilters, type PersonTab } from '../../router';
import { place, shortDate } from '../../time/format';
import { displayName } from './labels';
import { linked } from './threads';

type Props = {
    person: Contact;
    tab: PersonTab;
    filters: PeopleFilters;
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
export function PersonCard({ person, tab, filters, now, zone, onAdd, onClose }: Props) {
    const { name, noFirstName } = displayName(person);
    const to = (next: PersonTab) => ({ screen: 'people' as const, id: person.id, tab: next, filters });
    const tabs: PersonTab[] = linked(person) ? ['timeline', 'details', 'threads'] : ['timeline', 'details'];
    const campaigns = [...(person.campaigns ?? [])].sort(
        (a, b) => Number(a.state === 'excluded') - Number(b.state === 'excluded'),
    );
    return (
        <>
            <div class="panel-header">
                <div class="heading person-head">
                    <span class="avatar" aria-hidden="true">
                        {initials(person)}
                    </span>
                    <div>
                        <h2>
                            {name}
                            {noFirstName && <span class="muted"> · no first name</span>}
                        </h2>
                        <div class="sub">{sub(person, now)}</div>
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
                                        {campaign.campaign_name} · since{' '}
                                        {shortDate(new Date(campaign.added_at), zone, now)}
                                    </span>
                                ),
                            )}
                        </div>
                    </div>
                </div>
                <button class="iconbtn close" type="button" title="Close" aria-label="Close" onClick={onClose}>
                    ×
                </button>
            </div>
            <div class="panel-actions">
                <button class="btn" type="button" onClick={onAdd}>
                    Add to campaign
                </button>
            </div>
            <div class="tabs" role="tablist">
                {tabs.map((each) => (
                    <a
                        key={each}
                        role="tab"
                        aria-selected={tab === each}
                        class={tab === each ? 'active' : ''}
                        href={href(to(each))}
                        onClick={(event) => {
                            event.preventDefault();
                            navigate(to(each));
                        }}
                    >
                        {tabNames[each]}
                    </a>
                ))}
            </div>
        </>
    );
}
