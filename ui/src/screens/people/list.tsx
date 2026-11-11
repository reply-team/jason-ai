import type { Contact } from '../../api/types';
import { More } from '../../lists/more';
import type { LoadMore } from '../../lists/use-cursor-list';
import { navigate, type PeopleFilters, type PersonTab } from '../../router';
import { when } from '../../time/format';
import { channelName, displayName, entryLabel } from './labels';
import { leading, others, stageWords, standingOf } from './standing';

type Props = {
    people: Contact[];
    /** The filters now chosen: where a person opens from. */
    filters: PeopleFilters;
    /** The filters the rows were fetched with, which decide what each row says. */
    drawn: PeopleFilters;
    /** The tab the open person is on, which the next person opens on too. */
    tab: PersonTab;
    selected: string | null;
    more: LoadMore | null;
    me: string;
    now: Date;
    zone: string;
};

const dash = '—';

/** The kinds of channel a person can be reached on, each once, the primary ones first; the addresses are the cell's hover. */
function channelsOf(person: Contact): { names: string; values: string } {
    const sorted = [...person.channels].sort((a, b) => Number(b.primary) - Number(a.primary));
    const names = [...new Set(sorted.map((c) => channelName(c.channel)))];
    return { names: names.join(', '), values: sorted.map((c) => c.value).join('\n') };
}

/** The people, newest activity first as the runtime ordered them, one row each; the person the row names opens in the panel. */
export function PeopleList({ people, filters, drawn, tab, selected, more, me, now, zone }: Props) {
    const open = (id: string) => navigate({ screen: 'people', id, tab, filters });
    return (
        <div className="tablewrap">
            <table className="ptable">
                <thead>
                    <tr>
                        <th scope="col">Name</th>
                        <th scope="col" className="wide">
                            Company
                        </th>
                        <th scope="col" className="wide">
                            Channels
                        </th>
                        <th scope="col">Stage</th>
                        <th scope="col" className="wide">
                            Campaign
                        </th>
                        <th scope="col">Last activity</th>
                    </tr>
                </thead>
                <tbody>
                    {people.map((person) => {
                        const shown = leading(person, drawn.stage, drawn.campaign);
                        const rest = others(person, shown, drawn.campaign);
                        const words = shown ? stageWords(standingOf(shown)) : [];
                        const channels = channelsOf(person);
                        const last = person.last_activity ? entryLabel(person.last_activity, me) : null;
                        return (
                            <tr
                                key={person.id}
                                aria-current={selected === person.id ? 'true' : undefined}
                                className={`${selected === person.id ? 'sel' : ''}${person.archived_at ? ' quiet' : ''}`}
                                onClick={(event) => {
                                    if (event.ctrlKey || event.metaKey || event.shiftKey) return;
                                    event.preventDefault();
                                    open(person.id);
                                }}
                            >
                                <td className="name">
                                    <a href={`#/people/${encodeURIComponent(person.id)}`}>{displayName(person).name}</a>
                                </td>
                                <td className="wide">{person.company ?? dash}</td>
                                <td className="wide" title={channels.values || undefined}>
                                    {channels.names || dash}
                                </td>
                                <td>
                                    {words.length > 0
                                        ? words.map((word) => (
                                              <span key={word.key} className={`stage ${word.key}`} title={word.hint}>
                                                  {word.label}
                                              </span>
                                          ))
                                        : dash}
                                </td>
                                <td className="wide">
                                    {shown ? shown.campaign_name : dash}
                                    {rest.length > 0 && (
                                        <span
                                            className="also"
                                            title={`Also on: ${rest.map((c) => c.campaign_name).join(', ')}`}
                                        >
                                            +{rest.length}
                                        </span>
                                    )}
                                </td>
                                <td className="activity">
                                    {person.last_activity && last ? (
                                        <>
                                            <span>{last.short}</span>{' '}
                                            <time dateTime={person.last_activity.ts}>
                                                {when(new Date(person.last_activity.ts), now, zone)}
                                            </time>
                                        </>
                                    ) : (
                                        dash
                                    )}
                                </td>
                            </tr>
                        );
                    })}
                </tbody>
            </table>
            <More more={more} />
        </div>
    );
}
