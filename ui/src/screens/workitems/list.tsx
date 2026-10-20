import type { LoadMore } from '../../lists/use-cursor-list';
import type { WorkItemSummary } from '../../api/types';
import { More } from '../../lists/more';
import { navigate, type Tab, type WorkItemFilters } from '../../router';
import { clock, dayKey, dayLabel } from '../../time/format';
import { groupByDay } from '../../time/grouping';
import { doneNote, mark, plannedNote, what } from './labels';

type Props = {
    tab: Tab;
    filters: WorkItemFilters;
    items: WorkItemSummary[];
    selected: string | null;
    more: LoadMore | null;
    now: Date;
    zone: string;
};

type Group = {
    title: string;
    items: WorkItemSummary[];
    note: (item: WorkItemSummary) => string;
    time?: (item: WorkItemSummary) => string;
};

/** Now in two blocks, Planned by due day soonest first with undated work last, Done by finish day newest first. */
function groups({ tab, items, now, zone }: Props): Group[] {
    if (tab === 'now') {
        const blocks: Group[] = [
            {
                title: 'Running',
                items: items.filter((i) => i.status !== 'awaiting_approval'),
                note: (i) => (i.running_since ? `since ${clock(new Date(i.running_since), zone)}` : 'claimed'),
            },
            {
                title: 'Waiting for you',
                items: items.filter((i) => i.status === 'awaiting_approval'),
                note: () => 'awaiting approval ›',
            },
        ];
        return blocks.filter((g) => g.items.length > 0);
    }

    const today = dayKey(now, zone);
    if (tab === 'planned') {
        const note = (i: WorkItemSummary) => plannedNote(i, now, zone);
        const dated = items.filter((i) => i.due_at);
        const undated = items.filter((i) => !i.due_at);
        return [
            ...groupByDay(dated, (i) => new Date(i.due_at!), zone, false).map((g) => ({
                title: dayLabel(g.day, today),
                items: g.items,
                note,
            })),
            ...(undated.length > 0 ? [{ title: 'No date', items: undated, note }] : []),
        ];
    }

    return groupByDay(items, (i) => new Date(i.finished_at ?? i.updated_at), zone).map((g) => ({
        title: dayLabel(g.day, today),
        items: g.items,
        note: doneNote,
        time: (i: WorkItemSummary) => clock(new Date(i.finished_at ?? i.updated_at), zone),
    }));
}

export function WorkItemList(props: Props) {
    const { tab, filters, selected, more } = props;
    return (
        <div class="rows">
            {groups(props).map((group, index) => (
                <div key={group.title}>
                    <div class={`group${index === 0 ? ' first' : ''}`}>{group.title}</div>
                    {group.items.map((item) => {
                        const m = mark(item.status);
                        return (
                            <a
                                key={item.id}
                                class={`row${selected === item.id ? ' selected' : ''}`}
                                href="#"
                                onClick={(event) => {
                                    event.preventDefault();
                                    navigate({ screen: 'workitems', tab, id: item.id, filters });
                                }}
                            >
                                <span class={`mark ${m.kind}`}>{m.glyph}</span>
                                <span class="what">
                                    <span class="name">
                                        {what(item)}
                                        {item.contact_name ? ` · ${item.contact_name}` : ''}
                                    </span>
                                    <span class="in">{item.campaign_name}</span>
                                </span>
                                <span class="meta">
                                    <span>{group.note(item)}</span>
                                    {group.time && <time>{group.time(item)}</time>}
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
