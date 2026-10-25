import type { ActivityBucket, ActivityPeriod, MembershipItem } from '../../api/types';
import type { LoadMore } from '../../lists/use-cursor-list';
import { More } from '../../lists/more';
import { href, type ActivityCount } from '../../router';
import { bucketPhrase } from '../../time/buckets';
import { clock, dayKey, dayLabel, number } from '../../time/format';
import { groupByDay } from '../../time/grouping';
import { happened } from '../campaigns/why';
import { displayName } from '../people/labels';
import { byTheDay, counts, inThePeriod } from './labels';
import type { At } from './store';

/**
 * The people of the tile or the bar chosen, the latest to whom it happened first, grouped by that day; each row names
 * the campaigns that count them and what happened, and opens the person beside the list.
 */
export function Listing({
    at,
    count,
    counted,
    bucket,
    period,
    people,
    loaded,
    more,
    zone,
    now,
    onOpen,
    onBack,
}: {
    at: At;
    count: ActivityCount;
    counted: number | null;
    bucket: ActivityBucket | null;
    period: ActivityPeriod;
    people: MembershipItem[];
    loaded: boolean;
    more: LoadMore | null;
    zone: string;
    now: Date;
    onOpen: (id: string) => void;
    onBack: () => void;
}) {
    const label = counts.find((c) => c.key === count)!.label;
    const n = bucket ? bucket.value : counted;
    const when = bucket ? bucketPhrase(bucket, period, zone, now) : inThePeriod[at.period];
    const today = dayKey(now, zone);
    const dated = people.filter((person) => person.why?.at);
    return (
        <div class="listing">
            <div class="head">
                <h2>{label}</h2>
                {n !== null && (
                    <span class="hint">
                        {number.format(n)} {n === 1 ? 'person' : 'people'} {when}, {byTheDay[count]}
                    </span>
                )}
                {bucket && (
                    <button
                        type="button"
                        class="iconbtn"
                        aria-label="Back to the whole period"
                        title="Back to the whole period"
                        onClick={onBack}
                    >
                        ×
                    </button>
                )}
            </div>
            {loaded && people.length === 0 && <div class="empty">Nobody in this count for the period.</div>}
            <div class="rows">
                {groupByDay(dated, (person) => new Date(person.why!.at!), zone).map((group, index) => (
                    <div key={group.day}>
                        <div class={`group${index === 0 ? ' first' : ''}`}>{dayLabel(group.day, today)}</div>
                        {group.items.map((person) => {
                            const contact = person.contact;
                            const names = person.counted.map(
                                (c) =>
                                    contact.campaigns?.find((m) => m.campaign_id === c.campaign_id)?.campaign_name ??
                                    c.campaign_id,
                            );
                            const what = person.why ? happened(person.why, count) : null;
                            const replied = count === 'replied' || person.why?.text === 'replied';
                            return (
                                <a
                                    key={contact.id}
                                    class={`row${at.id === contact.id ? ' selected' : ''}`}
                                    href={href({ ...at, id: contact.id })}
                                    onClick={(event) => {
                                        event.preventDefault();
                                        onOpen(contact.id);
                                    }}
                                >
                                    <span class={`mark ${replied ? 'reply' : 'off'}`}>{replied ? '↩' : '·'}</span>
                                    <span class="what">
                                        <span class="name">{displayName(contact).name}</span>
                                        <span class="in">{[names.join(', '), what].filter(Boolean).join(' · ')}</span>
                                    </span>
                                    <span class="meta">
                                        <time>{clock(new Date(person.why!.at!), zone)}</time>
                                    </span>
                                </a>
                            );
                        })}
                    </div>
                ))}
                <More more={more} />
            </div>
        </div>
    );
}
