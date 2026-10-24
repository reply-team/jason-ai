import { useEffect, useState } from 'preact/hooks';
import { useApi } from '../../api/context';
import type { Contact, JournalEntry, WorkItemSummary } from '../../api/types';
import { More } from '../../lists/more';
import { href, type Tab } from '../../router';
import { clock, dayKey, dayLabel, when } from '../../time/format';
import { groupByDay } from '../../time/grouping';
import { plannedNote, what } from '../workitems/labels';
import { displayName, entryLabel } from './labels';
import { ThreadMessages } from './threads';
import * as store from './store';
import { Notice } from '../../layout/notice';
import { Chips } from '../../layout/chips';

type Props = { person: Contact; me: string; now: Date; zone: string };

const chips: { chip: store.Chip; label: string }[] = [
    { chip: 'all', label: 'All' },
    { chip: 'replies', label: 'Replies' },
    { chip: 'work', label: 'Work' },
    { chip: 'decisions', label: 'Decisions' },
    { chip: 'changes', label: 'Changes' },
];

/** One row below the now line: a journal line, or a membership the person's record holds. */
type Happened = {
    key: string;
    at: Date;
    mark: { kind: string; glyph: string };
    title: string;
    note: string | null;
    link: string | null;
    /** A reply's thread at the provider, which the row can open in place. */
    thread: { pluginId: string; threadId: string } | null;
};

/** Where a line about work opens: the item in Work items, or the approval in Attention. */
function linkOf(entry: JournalEntry): string | null {
    if (entry.kind.startsWith('approval_')) {
        const id = (entry.new as Record<string, unknown> | null)?.['id'];
        return typeof id === 'string' ? href({ screen: 'attention', kind: 'approvals', id }) : null;
    }
    if (!entry.work_item_id) return null;
    const finished = ['workitem_succeeded', 'workitem_failed', 'workitem_cancelled', 'workitem_expired'].includes(
        entry.kind,
    );
    return href({ screen: 'workitems', tab: finished ? 'done' : 'now', id: entry.work_item_id, filters: {} });
}

/**
 * Additions to and exclusions from campaigns, from the record: the journal names many people in one such line, so a
 * person's own record is where they are said.
 */
function memberships(person: Contact): Happened[] {
    return (person.campaigns ?? []).flatMap((c) => {
        const added: Happened = {
            key: `added-${c.campaign_id}`,
            at: new Date(c.added_at),
            mark: { kind: 'ok', glyph: '✓' },
            title: `Added to ${c.campaign_name}`,
            note: null,
            link: null,
            thread: null,
        };
        if (c.state !== 'excluded') return [added];
        return [
            added,
            {
                key: `excluded-${c.campaign_id}`,
                at: new Date(c.updated_at),
                mark: { kind: 'off', glyph: '–' },
                title: `Excluded from ${c.campaign_name}`,
                note: null,
                link: null,
                thread: null,
            },
        ];
    });
}

/**
 * The kinds a provider's event is said by. An event that reaches several of the person's campaigns is said once in each,
 * under one key, so it is one row naming them all.
 */
const saidPerCampaign = new Set(['inbound_reply_observed', 'provider_event_observed']);

/** The person's lines, each event once where its newest line stands, with the campaigns it was said in. */
function happenings(entries: JournalEntry[]): { entry: JournalEntry; campaigns: string[] }[] {
    const rows: { entry: JournalEntry; campaigns: string[] }[] = [];
    const said = new Map<string, string[]>();
    for (const entry of entries) {
        const key = saidPerCampaign.has(entry.kind) && entry.key ? `${entry.kind}/${entry.key}` : null;
        const campaigns = key && entry.campaign_name ? [entry.campaign_name] : [];
        const earlier = key ? said.get(key) : undefined;
        if (earlier) {
            earlier.push(...campaigns);
            continue;
        }
        if (key) said.set(key, campaigns);
        rows.push({ entry, campaigns });
    }
    return rows;
}

/** The thread a reply line names, as the reply's plugin calls it. */
function threadOf(entry: JournalEntry): { pluginId: string; threadId: string } | null {
    if (entry.kind !== 'inbound_reply_observed') return null;
    const said = entry.new as Record<string, unknown> | null;
    return typeof said?.['plugin'] === 'string' && typeof said['thread'] === 'string'
        ? { pluginId: said['plugin'], threadId: said['thread'] }
        : null;
}

/** Where open work opens: Planned for what has not started, Now for what is running or waiting. */
function tabOf(item: WorkItemSummary): Tab {
    return item.status === 'created' ? 'planned' : 'now';
}

export function Timeline({ person, me, now, zone }: Props) {
    const api = useApi();
    const chip = store.chip.value;
    const [expanded, setExpanded] = useState<string | null>(null);
    useEffect(() => {
        void store.loadTimeline(api, { contact_id: person.id }, 'all');
    }, [api, person.id]);

    const mine = store.timelineOf.value === store.subjectKey({ contact_id: person.id });
    const entries = mine ? store.lines.value : [];
    const lines: Happened[] = happenings(entries).map(({ entry, campaigns }) => {
        const label = entryLabel(entry, me);
        return {
            key: entry.id,
            at: new Date(entry.ts),
            mark: label.mark,
            title: label.title,
            note: [campaigns.join(', '), label.note].filter(Boolean).join(' · ') || null,
            link: linkOf(entry),
            thread: threadOf(entry),
        };
    });
    // A membership shows once the lines loaded reach its time, so it never sits above lines not yet read.
    const exhausted = store.linesCursor.value === null;
    const last = entries.at(-1);
    const oldest = last ? new Date(last.ts) : undefined;
    const placed =
        chip === 'all' || chip === 'changes'
            ? memberships(person).filter((m) => exhausted || (oldest !== undefined && m.at >= oldest))
            : [];
    const happened = [...lines, ...placed].sort((a, b) => b.at.getTime() - a.at.getTime());
    const today = dayKey(now, zone);
    const work = mine ? [...store.openWork.value].reverse() : [];

    return (
        <div class="timeline">
            <Chips
                label="Show"
                row="timeline"
                chips={chips.map(({ chip: each, label }) => ({ key: each, label }))}
                on={chip}
                onChoose={(each) => void store.loadTimeline(api, { contact_id: person.id }, each)}
            />
            {store.timelineFailure.value && <Notice danger>{store.timelineFailure.value}</Notice>}
            <div class="rows">
                {work.length > 0 && <div class="group first">Planned</div>}
                {work.map((item) => (
                    <a
                        key={item.id}
                        class="row"
                        href={href({ screen: 'workitems', tab: tabOf(item), id: item.id, filters: {} })}
                    >
                        <span class="mark plan">○</span>
                        <span class="what">
                            <span class="name">{what(item)}</span>
                            <span class="in">{item.campaign_name}</span>
                        </span>
                        <span class="meta">{plannedNote(item, now, zone, (at) => when(at, now, zone))}</span>
                    </a>
                ))}
                {store.moreOpenWork.value && (
                    <a
                        class="end"
                        href={href({ screen: 'workitems', tab: 'planned', filters: { contact: person.id } })}
                    >
                        All planned for {displayName(person).name} →
                    </a>
                )}
                <div class="now">
                    <span class="pill">now {clock(now, zone)}</span>
                    <span class="line" />
                </div>
                {store.linesLoaded.value && happened.length === 0 && <div class="empty">Nothing here yet.</div>}
                {groupByDay(happened, (h) => h.at, zone).map((group) => (
                    <div key={group.day}>
                        <div class="group">{dayLabel(group.day, today)}</div>
                        {group.items.map((h) => {
                            const open = expanded === h.key;
                            const body = (
                                <>
                                    <span class={`mark ${h.mark.kind}`}>{h.mark.glyph}</span>
                                    <span class="what">
                                        <span class="name">{h.title}</span>
                                        {h.thread && (
                                            <span class="cue">{open ? 'hide the thread ⌄' : 'show the thread ›'}</span>
                                        )}
                                        {h.note && <span class="in">{h.note}</span>}
                                    </span>
                                    <span class="meta">
                                        <time>{clock(h.at, zone)}</time>
                                    </span>
                                </>
                            );
                            if (h.thread) {
                                const thread = h.thread;
                                return (
                                    <div key={h.key}>
                                        <button
                                            type="button"
                                            class="row"
                                            aria-expanded={open}
                                            onClick={() => setExpanded(open ? null : h.key)}
                                        >
                                            {body}
                                        </button>
                                        {open && (
                                            <ThreadMessages
                                                contactId={person.id}
                                                pluginId={thread.pluginId}
                                                threadId={thread.threadId}
                                                zone={zone}
                                            />
                                        )}
                                    </div>
                                );
                            }
                            return h.link ? (
                                <a key={h.key} class="row" href={h.link}>
                                    {body}
                                </a>
                            ) : (
                                <div key={h.key} class="row">
                                    {body}
                                </div>
                            );
                        })}
                    </div>
                ))}
                <More more={store.linesCursor.value ? () => store.moreLines(api, { contact_id: person.id }) : null} />
            </div>
        </div>
    );
}
