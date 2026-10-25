import { useEffect } from 'preact/hooks';
import { useApi } from '../../api/context';
import type { Campaign, JournalEntry, WorkItemSummary } from '../../api/types';
import { More } from '../../lists/more';
import { href } from '../../router';
import { clock, dayKey, dayLabel, relative, when } from '../../time/format';
import { groupByDay } from '../../time/grouping';
import { title } from '../attention/labels';
import { entryLabel } from '../people/labels';
import * as timeline from '../people/store';
import { plannedNote, what } from '../workitems/labels';
import * as store from './store';
import { Notice } from '../../layout/notice';
import { Chips } from '../../layout/chips';

type Props = {
    campaign: Campaign;
    me: string;
    now: Date;
    zone: string;
    onWaiting: (what: { approval_id: string } | { decision_id: string }) => void;
};

const chips: { chip: timeline.Chip; label: string }[] = [
    { chip: 'all', label: 'All' },
    { chip: 'replies', label: 'Replies' },
    { chip: 'work', label: 'Work' },
    { chip: 'decisions', label: 'Decisions' },
    { chip: 'changes', label: 'Changes' },
];

/** Where a line opens: the approval on Attention, or the work item in Work items. */
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

/** A line about one person says who, since a campaign's journal is everyone's. */
function lineTitle(entry: JournalEntry, me: string): string {
    const said = entryLabel(entry, me).title;
    return entry.contact_name ? `${entry.contact_name} · ${said}` : said;
}

function WorkRow({ item, now, zone }: { item: WorkItemSummary; now: Date; zone: string }) {
    return (
        <a
            class="row"
            href={href({
                screen: 'workitems',
                tab: item.status === 'created' ? 'planned' : 'now',
                id: item.id,
                filters: {},
            })}
        >
            <span class={`mark ${item.status === 'created' ? 'plan' : 'run'}`}>
                {item.status === 'created' ? '○' : '◐'}
            </span>
            <span class="what">
                <span class="name">{what(item)}</span>
                {item.contact_name && <span class="in">{item.contact_name}</span>}
            </span>
            <span class="meta">{plannedNote(item, now, zone, (at) => when(at, now, zone))}</span>
        </a>
    );
}

/**
 * The campaign over time: what is planned, what waits for you and what is running above the now line, and below it
 * what its journal says, by day, narrowed by chips as a person's timeline is.
 */
export function CampaignTimeline({ campaign, me, now, zone, onWaiting }: Props) {
    const api = useApi();
    const subject = { campaign_id: campaign.id };
    const chip = timeline.chip.value;
    useEffect(() => {
        // Back from what opened in place, or another tab, finds the chip it left; the screen sets All when a campaign opens.
        const same = timeline.timelineOf.value === timeline.subjectKey(subject);
        void timeline.loadTimeline(api, subject, same ? timeline.chip.value : 'all', false);
        void store.loadPending(api, campaign.id);
    }, [api, campaign.id]);

    const mine = timeline.timelineOf.value === timeline.subjectKey(subject);
    const pending = store.pendingOf.value === campaign.id;
    const planned = pending ? store.planned.value : [];
    const running = pending ? store.running.value : [];
    const approvals = pending ? store.pendingApprovals.value : [];
    const questions = pending ? store.pendingQuestions.value : [];
    const more = (section: store.Section, cursor: string | null) =>
        pending && cursor ? () => store.morePending(api, campaign.id, section) : null;
    const lines = mine ? timeline.lines.value : [];
    const today = dayKey(now, zone);

    return (
        <div class="timeline">
            <Chips
                label="Show"
                row="timeline"
                chips={chips.map(({ chip: each, label }) => ({ key: each, label }))}
                on={chip}
                onChoose={(each) => void timeline.loadTimeline(api, subject, each, false)}
            />
            {timeline.timelineFailure.value && <Notice danger>{timeline.timelineFailure.value}</Notice>}
            <div class="rows">
                {planned.length > 0 && (
                    <div>
                        <div class="group first">Planned</div>
                        <More more={more('planned', store.plannedCursor.value)} />
                        {[...planned].reverse().map((item) => (
                            <WorkRow key={item.id} item={item} now={now} zone={zone} />
                        ))}
                    </div>
                )}
                {approvals.length + questions.length > 0 && (
                    <div>
                        <div class="group">Waiting for you</div>
                        {approvals.map((approval) => (
                            <button
                                key={approval.id}
                                type="button"
                                class="row"
                                onClick={() => onWaiting({ approval_id: approval.id })}
                            >
                                <span class="mark wait">!</span>
                                <span class="what">
                                    <span class="name">{title(approval.operation, approval.contact_name)}</span>
                                    <span class="in">
                                        waiting since {relative(new Date(approval.requested_at), now)}
                                    </span>
                                </span>
                            </button>
                        ))}
                        {questions.map((question) => (
                            <button
                                key={question.id}
                                type="button"
                                class="row"
                                onClick={() => onWaiting({ decision_id: question.id })}
                            >
                                <span class="mark ask">?</span>
                                <span class="what">
                                    <span class="name">{question.question}</span>
                                    <span class="in">
                                        {question.contact_name ?? 'about the campaign'} · asked{' '}
                                        {relative(new Date(question.raised_at), now)}
                                    </span>
                                </span>
                            </button>
                        ))}
                        <More more={more('approvals', store.approvalsCursor.value)} />
                        <More more={more('questions', store.questionsCursor.value)} />
                    </div>
                )}
                {running.length > 0 && (
                    <div>
                        <div class="group">Running</div>
                        {running.map((item) => (
                            <WorkRow key={item.id} item={item} now={now} zone={zone} />
                        ))}
                        <More more={more('running', store.runningCursor.value)} />
                    </div>
                )}
                <div class="now">
                    <span class="pill">now {clock(now, zone)}</span>
                    <span class="line" />
                </div>
                {timeline.linesLoaded.value && mine && lines.length === 0 && <div class="empty">Nothing here yet.</div>}
                {groupByDay(lines, (entry) => new Date(entry.ts), zone).map((group) => (
                    <div key={group.day}>
                        <div class="group">{dayLabel(group.day, today)}</div>
                        {group.items.map((entry) => {
                            const label = entryLabel(entry, me);
                            const link = linkOf(entry);
                            const body = (
                                <>
                                    <span class={`mark ${label.mark.kind}`}>{label.mark.glyph}</span>
                                    <span class="what">
                                        <span class="name">{lineTitle(entry, me)}</span>
                                        {label.note && <span class="in">{label.note}</span>}
                                    </span>
                                    <span class="meta">
                                        <time>{clock(new Date(entry.ts), zone)}</time>
                                    </span>
                                </>
                            );
                            return link ? (
                                <a key={entry.id} class="row" href={link}>
                                    {body}
                                </a>
                            ) : (
                                <div key={entry.id} class="row">
                                    {body}
                                </div>
                            );
                        })}
                    </div>
                ))}
                <More more={timeline.linesCursor.value ? () => timeline.moreLines(api, subject) : null} />
            </div>
        </div>
    );
}
