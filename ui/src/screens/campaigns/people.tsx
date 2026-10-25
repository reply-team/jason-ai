import type { CampaignActivityRow, CampaignSummaryRow, MembershipItem } from '../../api/types';
import { More } from '../../lists/more';
import { Search } from '../../lists/search';
import { replace, type Route } from '../../router';
import { when } from '../../time/format';
import { displayName, entryLabel } from '../people/labels';
import { activityColumns, atTheProvider, everyone, summaryColumns } from './labels';
import { happened } from './why';
import { Notice } from '../../layout/notice';
import { Chips } from '../../layout/chips';

type At = Extract<Route, { screen: 'campaigns' }>;

type Props = {
    at: At;
    counted: CampaignSummaryRow | CampaignActivityRow | null;
    /** Whether a provider's sequence is linked, as the runtime says; Sequence and Out count nothing until one is. */
    linked: boolean | null;
    people: MembershipItem[] | null;
    query: string;
    failure: string | null;
    me: string;
    now: Date;
    zone: string;
    more: (() => Promise<void>) | null;
    onSearch: (q: string) => void;
    onWaiting: (person: MembershipItem) => void;
};

/** Why a person is in the count they were listed under, in the few words a row has room for. */
function why(person: MembershipItem, filter: string | undefined, me: string, now: Date, zone: string): string | null {
    const reason = person.why;
    const at = reason?.at ? when(new Date(reason.at), now, zone) : null;
    if (!reason) return person.contact.last_activity ? entryLabel(person.contact.last_activity, me).short : null;
    switch (reason.kind) {
        case 'approval':
            return `approve: ${reason.text ?? 'an operation'}`;
        case 'question':
            return `answer: ${reason.text ?? 'a question'}`;
        case 'failure':
            return [reason.text ?? reason.code, at].filter(Boolean).join(' · ');
        default:
            return [happened(reason, filter), at].filter(Boolean).join(' ');
    }
}

/** "Head of Sales, Soluna · ada@soluna.example": the role at the company, then one way to reach them. */
function about(person: MembershipItem): string {
    const contact = person.contact;
    const work = [contact.title, contact.company].filter(Boolean).join(', ');
    const channel = contact.channels.find((c) => c.primary) ?? contact.channels[0];
    return [work, channel?.value].filter(Boolean).join(' · ');
}

/**
 * The campaign's people under one count of the view, or All of them, found by a search: each with why the count
 * counts them. A person waiting for you opens what waits in place; anybody else opens on People.
 */
export function CampaignPeople({
    at,
    counted,
    linked,
    people,
    query,
    failure,
    me,
    now,
    zone,
    more,
    onSearch,
    onWaiting,
}: Props) {
    const columns =
        at.view === 'summary'
            ? summaryColumns.filter((c) => c.key !== 'people' && (linked !== false || !atTheProvider.includes(c.key)))
            : activityColumns;
    const row = counted as Record<string, unknown> | null;
    const all = typeof row?.['people'] === 'number' ? row['people'] : null;
    const go = (next: { filter?: string }) => {
        const to: At = {
            screen: 'campaigns',
            ...(at.id ? { id: at.id } : {}),
            tab: 'people',
            view: at.view,
            period: at.period,
            filters: at.filters,
        };
        replace({ ...to, ...next });
    };
    const chosen = at.filter ?? '';
    return (
        <div class="panel-body people-tab">
            <Search
                value={query}
                label="Search people on this campaign"
                placeholder="Search people on this campaign"
                onSearch={onSearch}
            />
            <Chips
                label="Show"
                row="filters"
                chips={[
                    { key: '', label: 'All', title: everyone[at.view], count: all },
                    ...columns.map((column) => ({
                        key: column.key,
                        label: column.label,
                        title: column.hint,
                        count: typeof row?.[column.key] === 'number' ? (row[column.key] as number) : null,
                    })),
                ]}
                on={chosen}
                onChoose={(key) => go(key ? { filter: key } : {})}
            />
            {failure && <Notice danger>{failure}</Notice>}
            {people !== null && people.length === 0 && <div class="empty">Nobody here.</div>}
            <div class="rows">
                {(people ?? []).map((person) => {
                    const opens = person.why?.kind === 'approval' || person.why?.kind === 'question';
                    const line = why(person, at.filter, me, now, zone);
                    return (
                        <a
                            key={person.contact.id}
                            class="row"
                            href={`#/people/${encodeURIComponent(person.contact.id)}`}
                            onClick={(event) => {
                                if (!opens) return;
                                event.preventDefault();
                                onWaiting(person);
                            }}
                        >
                            <span class={`mark ${opens ? 'wait' : person.why?.kind === 'failure' ? 'fail' : 'off'}`}>
                                {opens ? '!' : person.why?.kind === 'failure' ? '×' : '·'}
                            </span>
                            <span class="what">
                                <span class="name">{displayName(person.contact).name}</span>
                                <span class="in" title={line ?? undefined}>
                                    {line ?? about(person)}
                                </span>
                            </span>
                            {person.state === 'excluded' && (
                                <span class="meta">
                                    <span class="badge outline">Removed</span>
                                </span>
                            )}
                        </a>
                    );
                })}
                <More more={more} />
            </div>
        </div>
    );
}
