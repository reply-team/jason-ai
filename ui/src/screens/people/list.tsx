import type { Contact } from '../../api/types';
import { More } from '../../lists/more';
import type { LoadMore } from '../../lists/use-cursor-list';
import { t } from '../../platform/i18n';
import { navigate, type PeopleFilters, type PersonTab } from '../../router';
import { when } from '../../time/format';
import { channelName, displayName, entryLabel } from './labels';
import { leading, others, stageWords } from './standing';

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

const dash = '—';

/** The kinds of channel a person can be reached on, each once, the primary ones first; the addresses are the cell's hover. */
function channelsOf(person: Contact): { names: string; values: string } {
    const sorted = [...person.channels].sort((a, b) => Number(b.primary) - Number(a.primary));
    const names = [...new Set(sorted.map((c) => channelName(c.channel)))];
    return { names: names.join(', '), values: sorted.map((c) => c.value).join('\n') };
}

/** The people, newest activity first as the runtime ordered them, one row each; the person the row names opens in the panel. */
export function PeopleList({ people, filters, tab, selected, more, me, now, zone }: Props) {
    const open = (id: string) => navigate({ screen: 'people', id, tab, filters });
    return (
        <div className="tablewrap">
            <table className="ptable">
                <thead>
                    <tr>
                        <th scope="col">{t('people.col.name')}</th>
                        <th scope="col" className="wide">
                            {t('people.col.company')}
                        </th>
                        <th scope="col" className="wide">
                            {t('people.col.channels')}
                        </th>
                        <th scope="col">{t('people.col.stage')}</th>
                        <th scope="col" className="wide">
                            {t('people.col.campaign')}
                        </th>
                        <th scope="col">{t('people.col.activity')}</th>
                    </tr>
                </thead>
                <tbody>
                    {people.map((person) => {
                        const shown = leading(person, filters.stage, filters.campaign);
                        const rest = others(person, shown, filters.campaign);
                        const words = shown ? stageWords(shown.standing) : [];
                        const channels = channelsOf(person);
                        const last = person.last_activity ? entryLabel(person.last_activity, me) : null;
                        return (
                            <tr
                                key={person.id}
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
                                            title={t('people.also', {
                                                names: rest.map((c) => c.campaign_name).join(', '),
                                            })}
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
