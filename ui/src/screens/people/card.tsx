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
    /** Where each tab of this person is, on the screen the card is shown on; without it a tab is picked in place. */
    to?: ((tab: PersonTab) => Route) | undefined;
    /** The tab picked, for a card whose tabs have no address. */
    onTab?: (tab: PersonTab) => void;
    now: Date;
    zone: string;
    onAdd: () => void;
    onClose: () => void;
    /** The tabs to draw and their names; without them, Timeline, Details and Threads where a provider knows the person. */
    tabs?: readonly { key: PersonTab; label: string }[];
};

const tabNames = { timeline: 'Timeline', threads: 'Threads', details: 'Details' } as const;

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
export function PersonCard({ person, tab, to, onTab, now, zone, onAdd, onClose, tabs }: Props) {
    const { name, noFirstName } = displayName(person);
    const keys = linked(person) ? (['timeline', 'details', 'threads'] as const) : (['timeline', 'details'] as const);
    const shown = tabs ?? keys.map((key) => ({ key, label: tabNames[key] }));
    const campaigns = [...(person.campaigns ?? [])].sort(
        (a, b) => Number(a.state === 'excluded') - Number(b.state === 'excluded'),
    );
    return (
        <>
            <PanelHeader
                lead={
                    <span className="avatar" aria-hidden="true">
                        {initials(person)}
                    </span>
                }
                title={
                    <>
                        {name}
                        {noFirstName && <span className="muted"> · no first name</span>}
                    </>
                }
                sub={sub(person, now)}
                under={
                    <div className="onc">
                        {person.archived_at && <span className="badge warning">archived</span>}
                        {campaigns.map((campaign) =>
                            campaign.state === 'excluded' ? (
                                <span key={campaign.campaign_id} className="badge outline">
                                    {campaign.campaign_name} · excluded{' '}
                                    {shortDate(new Date(campaign.updated_at), zone, now)}
                                </span>
                            ) : (
                                <span key={campaign.campaign_id} className="badge accent">
                                    {campaign.campaign_name} · since {shortDate(new Date(campaign.added_at), zone, now)}
                                </span>
                            ),
                        )}
                    </div>
                }
                onClose={onClose}
            />
            <ActionsRow>
                <button className="btn" type="button" onClick={onAdd}>
                    Add to campaign
                </button>
            </ActionsRow>
            {to ? (
                <Tabs tabs={shown} active={tab} to={to} />
            ) : (
                <div className="tabs" role="tablist">
                    {shown.map((each) => (
                        <button
                            key={each.key}
                            type="button"
                            role="tab"
                            aria-selected={tab === each.key}
                            className={tab === each.key ? 'active' : ''}
                            onClick={() => onTab?.(each.key)}
                        >
                            {each.label}
                        </button>
                    ))}
                </div>
            )}
        </>
    );
}
