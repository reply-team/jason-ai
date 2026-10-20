import type { Contact } from '../../api/types';
import { More } from '../../lists/more';
import type { LoadMore } from '../../lists/use-cursor-list';
import { navigate, type PeopleFilters, type PersonTab } from '../../router';
import { clock, dayKey, dayLabel } from '../../time/format';
import { groupByDay } from '../../time/grouping';
import { displayName, entryLabel, onCampaigns } from './labels';

type Props = {
    people: Contact[];
    filters: PeopleFilters;
    /** The tab the open person is on, which the next person opens on too. */
    tab: PersonTab;
    selected: string | null;
    more: LoadMore | null;
    me: string;
    now: Date;
    zone: string;
};

type Group = { title: string; people: Contact[] };

/** By the day something last happened to each person, newest first; the people nothing has happened to yet, last. */
function groups({ people, now, zone }: Props): Group[] {
    const today = dayKey(now, zone);
    const moved = people.filter((p) => p.last_activity);
    const still = people.filter((p) => !p.last_activity);
    return [
        ...groupByDay(moved, (p) => new Date(p.last_activity!.ts), zone).map((g) => ({
            title: dayLabel(g.day, today),
            people: g.items,
        })),
        ...(still.length > 0 ? [{ title: 'No activity yet', people: still }] : []),
    ];
}

/** "Head of Sales, Soluna · LatAm pilot": the role at the company, then the campaigns the person is on. */
function about(person: Contact): string {
    const work = [person.title, person.company].filter(Boolean).join(', ');
    const campaigns = onCampaigns(person).join(', ');
    return [work, campaigns].filter(Boolean).join(' · ');
}

export function PeopleList(props: Props) {
    const { filters, tab, selected, more, me, zone } = props;
    return (
        <div class="rows">
            {groups(props).map((group, index) => (
                <div key={group.title}>
                    <div class={`group${index === 0 ? ' first' : ''}`}>{group.title}</div>
                    {group.people.map((person) => {
                        const last = person.last_activity ? entryLabel(person.last_activity, me) : null;
                        return (
                            <a
                                key={person.id}
                                class={`row${selected === person.id ? ' selected' : ''}${person.archived_at ? ' quiet' : ''}`}
                                href="#"
                                onClick={(event) => {
                                    event.preventDefault();
                                    navigate({ screen: 'people', id: person.id, tab, filters });
                                }}
                            >
                                <span class={`mark ${last?.mark.kind ?? 'off'}`}>{last?.mark.glyph ?? '·'}</span>
                                <span class="what">
                                    <span class="name">{displayName(person).name}</span>
                                    <span class="in">{about(person)}</span>
                                </span>
                                <span class="meta">
                                    {last && <span>{last.short}</span>}
                                    {person.last_activity && (
                                        <time>{clock(new Date(person.last_activity.ts), zone)}</time>
                                    )}
                                </span>
                            </a>
                        );
                    })}
                </div>
            ))}
            <More more={more} />
        </div>
    );
}
