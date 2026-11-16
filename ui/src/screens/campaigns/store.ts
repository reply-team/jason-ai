import { signal } from '@preact/signals-react';
import { ApiError, Unpaired, type Api } from '../../api/client';
import { session } from '../../api/session';
import {
    interestedWord,
    AddContactsResult,
    Approval,
    ApprovalSummary,
    Campaign,
    ActivityBucket,
    CampaignActivityByPeriodResponse,
    CampaignActivityResponse,
    CampaignActivityRow,
    CampaignSummary,
    CampaignSummaryResponse,
    CampaignSummaryRow,
    Decision,
    DecisionSummary,
    JournalEntry,
    MembershipItem,
    MembershipWhy,
    Page,
    ReplyCountResponse,
    WorkItemSummary,
} from '../../api/types';
import { mergePage } from '../../lists/paging';
import { inboxEntriesOf, inboxLines, type InboxEntry } from './inbox-lines';
import type { CampaignFilters, CampaignView, Period } from '../../router';
import { bucketOf, periodWindow } from '../../time/periods';
import { createBody, unfinishedCreate } from './kind';

/**
 * What every read of a campaign's people or counts on the campaign screens adds, the Results and the Performance page
 * included: archived people left out, as the People page leaves them out while Archived is not ticked, so a number and
 * the list it opens are of the same people.
 */
export const archivedLeftOut = { exclude_archived_contacts: true } as const;

/** What the table shows: the view, its period, and the campaigns it lists. */
export type Table = { view: CampaignView; period: Period; filters: CampaignFilters };

export const campaigns = signal<CampaignSummary[]>([]);
/** Each listed campaign's counts, by its id: the summary's or the period's, whichever the table shows. */
export const summary = signal<Record<string, CampaignSummaryRow>>({});
export const activity = signal<Record<string, CampaignActivityRow>>({});
/** Which table the campaigns and counts above belong to: a page shows none of another table's numbers. */
export const shown = signal<string | null>(null);
export const failure = signal<string | null>(null);
/** True while a refresh the person asked for is under way: long enough to be seen, even when the answer is instant. */
export const refreshing = signal(false);

// A page of campaigns is the most one read of their counts takes, so each page is counted with itself.
const pageSize = 100;
let generation = 0;
let loadingCampaigns = false;
export const campaignsCursor = signal<string | null>(null);
/** The period the table's activity was read for: its next page is counted for the same one. */
let tableWindow: { since: string; until: string } | null = null;

export function keyOf(table: Table): string {
    return JSON.stringify([
        table.view,
        table.view === 'activity' ? table.period : null,
        table.filters.q ?? null,
        table.filters.archived === true,
    ]);
}

/** What the open campaign's numbers and lists depend on: the view and, in Activity, its period; never the table's search. */
export function numbersKey(table: Table): string {
    return JSON.stringify([table.view, table.view === 'activity' ? table.period : null]);
}

export function listBody(filters: CampaignFilters): Record<string, unknown> {
    const body: Record<string, unknown> = { limit: pageSize };
    if (filters.q) body['query'] = filters.q;
    if (filters.archived) body['include_archived'] = true;
    return body;
}

/** What a failed read says to a person, or nothing when the pairing is gone and the page asks for it again. */
function failureText(error: unknown): string | null {
    if (error instanceof Unpaired) {
        session.clear();
        return null;
    }
    return error instanceof ApiError ? error.message : String(error);
}

export function fail(error: unknown, into = failure): void {
    const text = failureText(error);
    if (text !== null) into.value = text;
}

/** The view's counts for one page of campaigns, in one read: by campaign id. */
async function countPage(
    api: Api,
    table: Table,
    ids: string[],
): Promise<Record<string, CampaignSummaryRow> | Record<string, CampaignActivityRow>> {
    if (ids.length === 0) return {};
    const counts =
        table.view === 'summary'
            ? await api.call<CampaignSummaryResponse>('campaign.summary', { campaign_ids: ids, ...archivedLeftOut })
            : await api.call<CampaignActivityResponse>('campaign.activity', {
                  campaign_ids: ids,
                  ...tableWindow,
                  ...archivedLeftOut,
              });
    return Object.fromEntries(counts.campaigns.map((row) => [row.campaign_id, row])) as
        Record<string, CampaignSummaryRow> | Record<string, CampaignActivityRow>;
}

/**
 * The first page of campaigns the filters list, then one read of the view's counts for them, in the order listed.
 * Nothing on the page counts: every number is the runtime's. An answer to a table the person has already left is dropped.
 */
export async function load(api: Api, table: Table, now: Date, zone: string): Promise<void> {
    const mine = ++generation;
    failure.value = null;
    tableWindow = table.view === 'activity' ? periodWindow(table.period, now, zone) : null;
    try {
        const page = await api.call<Page<CampaignSummary>>('campaign.list', listBody(table.filters));
        if (mine !== generation) return;
        const counts = await countPage(
            api,
            table,
            page.items.map((c) => c.id),
        );
        if (mine !== generation) return;
        if (table.view === 'summary') summary.value = counts as Record<string, CampaignSummaryRow>;
        else activity.value = counts as Record<string, CampaignActivityRow>;
        campaigns.value = page.items;
        campaignsCursor.value = page.next_cursor;
        shown.value = keyOf(table);
    } catch (error) {
        if (mine === generation) fail(error);
    }
}

/** The next page of campaigns, counted with itself for the period the first page was. */
export async function moreCampaigns(api: Api, table: Table): Promise<void> {
    const after = campaignsCursor.value;
    if (!after || loadingCampaigns) return;
    loadingCampaigns = true;
    const mine = generation;
    try {
        const page = await api.call<Page<CampaignSummary>>('campaign.list', {
            ...listBody(table.filters),
            cursor: after,
        });
        if (mine !== generation) return;
        const counts = await countPage(
            api,
            table,
            page.items.map((c) => c.id),
        );
        if (mine !== generation) return;
        if (table.view === 'summary')
            summary.value = { ...summary.value, ...(counts as Record<string, CampaignSummaryRow>) };
        else activity.value = { ...activity.value, ...(counts as Record<string, CampaignActivityRow>) };
        campaigns.value = mergePage(campaigns.value, page.items, (c) => c.id);
        campaignsCursor.value = page.next_cursor;
    } catch (error) {
        if (mine === generation) fail(error);
    } finally {
        loadingCampaigns = false;
    }
}

/** The table again, the button turning for at least half a second. */
export async function refresh(api: Api, table: Table, now: Date, zone: string): Promise<void> {
    refreshing.value = true;
    const floor = new Promise((resolve) => setTimeout(resolve, 500));
    await Promise.all([load(api, table, now, zone), floor]);
    refreshing.value = false;
}

/** A campaign the address named and the runtime no longer knows: said once, above the table. */
export const notice = signal<string | null>(null);

/** The campaign open on the right, as campaign.get answers. */
export const campaign = signal<Campaign | null>(null);
/**
 * The open campaign's numbers, with the campaign and the table they were counted for: the panel shows them only under
 * that view and period, never another's.
 */
export const counted = signal<{ id: string; key: string; row: CampaignSummaryRow | CampaignActivityRow } | null>(null);
export const panelFailure = signal<string | null>(null);
/** A decision about what waits on a person that the runtime refused, said where Attention says it. */
export const actionFailure = signal<string | null>(null);
let opened = 0;
let counting = 0;

/**
 * Opens a campaign: its record, then its numbers, unless the tab it opens on lists people, which reads them with its
 * list. False when the runtime does not know it.
 */
export async function open(
    api: Api,
    id: string,
    table: Table,
    now: Date,
    zone: string,
    withNumbers = true,
): Promise<boolean> {
    const mine = ++opened;
    panelFailure.value = null;
    actionFailure.value = null;
    // Opening a campaign, coming back to it included, shows the campaign: what a row opened earlier is left behind.
    waiting.value = null;
    if (campaign.value?.id !== id) {
        campaign.value = null;
        counted.value = null;
    }
    try {
        const found = await api.call<Campaign>('campaign.get', { campaign_id: id });
        if (mine !== opened) return true;
        campaign.value = found;
    } catch (error) {
        if (error instanceof ApiError && error.status === 404) return false;
        if (mine === opened) fail(error, panelFailure);
        return true;
    }
    if (withNumbers) await count(api, id, table, now, zone);
    return true;
}

/**
 * The open campaign's numbers in the table's view, asked for again rather than taken from the table: the person
 * clicked a number some time ago, and the panel and the list it opens say what is true now. Read again with every list
 * the People tab reads, for the same period, so a count and its list are answers to one question.
 */
export async function count(
    api: Api,
    id: string,
    table: Table,
    now: Date,
    zone: string,
    window = periodWindow(table.period, now, zone),
): Promise<void> {
    const mine = ++counting;
    try {
        const row =
            table.view === 'summary'
                ? (
                      await api.call<CampaignSummaryResponse>('campaign.summary', {
                          campaign_ids: [id],
                          ...archivedLeftOut,
                      })
                  ).campaigns[0]
                : (
                      await api.call<CampaignActivityResponse>('campaign.activity', {
                          campaign_ids: [id],
                          ...window,
                          ...archivedLeftOut,
                      })
                  ).campaigns[0];
        if (mine === counting && row) counted.value = { id, key: numbersKey(table), row };
    } catch (error) {
        if (mine === counting) fail(error, panelFailure);
    }
}

/** The numbers of this campaign under this table, or none while they are on their way. */
export function countedFor(id: string | null, table: Table): CampaignSummaryRow | CampaignActivityRow | null {
    const found = counted.value;
    return found && found.id === id && found.key === numbersKey(table) ? found.row : null;
}

export function close(): void {
    ++opened;
    waiting.value = null;
    campaign.value = null;
    counted.value = null;
    results.value = null;
    inbox.value = null;
    tabFailure.value = null;
    panelFailure.value = null;
}

/**
 * A Results or an Inbox read that failed, with the campaign and the tab that asked: it is said over that campaign and
 * that tab only, and leaving the tab lets it go (`leaveTab`).
 */
export const tabFailure = signal<{ id: string; tab: 'results' | 'inbox'; text: string } | null>(null);

/** What failed for this campaign's tab, or nothing. */
export function failedOver(id: string | null, tab: string): string | null {
    const found = tabFailure.value;
    return found && found.id === id && found.tab === tab ? found.text : null;
}

/** The person left a tab: what failed there is not said any more. */
export function leaveTab(): void {
    tabFailure.value = null;
}

function failOver(id: string, tab: 'results' | 'inbox', error: unknown): void {
    const text = failureText(error);
    if (text !== null) tabFailure.value = { id, tab, text };
}

function forget(tab: 'results' | 'inbox'): void {
    if (tabFailure.value?.tab === tab) tabFailure.value = null;
}

/**
 * One read of the Results tab that may fail by itself: still on its way, its value, or that it failed, with the
 * runtime's own reason when the runtime refused it (null when nobody answered).
 */
export type Read<T> = { state: 'pending' } | { state: 'ok'; value: T } | { state: 'failed'; reason: string | null };

/** The newest finished review of the campaign: when, or null while none has finished. */
export type Review = string | null;

/**
 * The Results tab's answers, with the campaign and the period they were read for: the campaign's people now, from
 * campaign.summary, and the period's counts, from campaign.activity; those two or nothing, written the moment both land.
 * Beside them, each read that may fail or be slow alone, filled in as it lands: the conversations judged interested
 * (reply.count), the two series (campaign.activity_by_period) and the last review (workitem.list). The tab shows them
 * as they came; it divides nothing.
 */
export const results = signal<{
    id: string;
    period: Period;
    now: CampaignSummaryRow;
    counts: CampaignActivityRow;
    interested: Read<number>;
    series: Read<{ contacted: ActivityBucket[]; replied: ActivityBucket[]; readAt: Date }>;
    review: Read<Review>;
} | null>(null);
let resulting = 0;

/** What an optional read came to: the runtime's own words when it refused, nothing when nobody answered. */
function failedRead(error: unknown): { state: 'failed'; reason: string | null } {
    // A request nobody is paired for ends the session, as every read of this page does.
    failureText(error);
    return { state: 'failed', reason: error instanceof ApiError ? error.message : null };
}

/**
 * Reads the open campaign's Results again: the counts, or why not, said over this campaign's Results only, and the
 * optional reads, each filled in where it lands. The review and the people now do not depend on the period; a period
 * change reads them again with the rest, which costs one cheap request and keeps one rule for the tab. An answer of an
 * older load is never written.
 */
export async function loadResults(api: Api, id: string, period: Period, now: Date, zone: string): Promise<void> {
    const mine = ++resulting;
    forget('results');
    const window = periodWindow(period, now, zone);
    const over = (count: string) =>
        api.call<CampaignActivityByPeriodResponse>('campaign.activity_by_period', {
            campaign_ids: [id],
            count,
            period: bucketOf(period),
            ...window,
            zone,
            ...archivedLeftOut,
        });
    let core: { now: CampaignSummaryRow; counts: CampaignActivityRow } | null = null;
    // A refresh of what is drawn keeps it until the new answer lands: the optional reads start from the last ones of the
    // same campaign and period, and are written over one by one. Another campaign or period starts from none.
    const drawn = results.value && results.value.id === id && results.value.period === period ? results.value : null;
    let interested: Read<number> = drawn?.interested ?? { state: 'pending' };
    let series: Read<{ contacted: ActivityBucket[]; replied: ActivityBucket[]; readAt: Date }> = drawn?.series ?? {
        state: 'pending',
    };
    let review: Read<Review> = drawn?.review ?? { state: 'pending' };
    const publish = () => {
        if (mine === resulting && core) results.value = { id, period, ...core, interested, series, review };
    };
    const settle = <T>(read: Promise<T>, take: (value: T) => void, fail: (error: unknown) => void) =>
        read.then(
            (value) => {
                if (mine !== resulting) return;
                take(value);
                publish();
            },
            (error: unknown) => {
                if (mine !== resulting) return;
                fail(error);
                publish();
            },
        );

    // The optional reads are started with the counts and never hold them back.
    const judged = settle(
        api.call<ReplyCountResponse>('reply.count', { campaign_ids: [id], ...window }),
        (value) => {
            // An absent word is a count of zero in a window that was read; the Dashboard draws a dash for it, which on
            // one campaign's tab would say "unknown" of something the runtime has just answered.
            interested = {
                state: 'ok',
                value: value.total.categories.find((each) => each.category === interestedWord)?.threads ?? 0,
            };
        },
        (error) => (interested = failedRead(error)),
    );
    const charted = settle(
        Promise.all([over('contacted'), over('replied')]),
        ([contacted, replied]) => {
            series = {
                state: 'ok',
                value: { contacted: contacted.buckets, replied: replied.buckets, readAt: new Date(window.until) },
            };
        },
        (error) => (series = failedRead(error)),
    );
    const reviewed = settle(
        // The latest review the manager role finished here: one request, newest finished first, a limit of one.
        api.call<Page<WorkItemSummary>>('workitem.list', {
            campaign_id: id,
            role: 'manager',
            status: ['succeeded'],
            order: 'finished',
            limit: 1,
        }),
        (value) => (review = { state: 'ok', value: value.items[0]?.finished_at ?? null }),
        (error) => (review = failedRead(error)),
    );

    try {
        const [summed, active] = await Promise.all([
            api.call<CampaignSummaryResponse>('campaign.summary', { campaign_ids: [id], ...archivedLeftOut }),
            api.call<CampaignActivityResponse>('campaign.activity', {
                campaign_ids: [id],
                ...window,
                ...archivedLeftOut,
            }),
        ]);
        const [row, counts] = [summed.campaigns[0], active.campaigns[0]];
        if (mine === resulting && row && counts) {
            core = { now: row, counts };
            publish();
        }
    } catch (error) {
        if (mine === resulting) failOver(id, 'results', error);
    }
    // The optional reads settle by themselves; waiting here keeps a caller's `await` to the whole tab.
    await Promise.all([judged, charted, reviewed]);
}

/** The Results of this campaign for this period, or none while they are on their way. */
export function resultsFor(id: string | null, period: Period) {
    const found = results.value;
    return found && found.id === id && found.period === period ? found : null;
}

/**
 * The Inbox tab: what the campaign's latest `inbound_reply_observed` lines say, as entries (`inboxEntriesOf`). Each
 * person's threads are asked of the plugin their line names, never of a plugin of the tab's own.
 */
export const inbox = signal<{ id: string; read: number; entries: InboxEntry[]; left: number } | null>(null);
let inboxing = 0;

/**
 * The person left the Inbox: what it showed goes, and so does every read still on its way, so the tab opened again
 * shows only what a read made since says.
 */
export function leaveInbox(): void {
    ++inboxing;
    inbox.value = null;
}

/**
 * Reads the Inbox's lines, when the tab opens and when the person asks again; nothing reads it in the background. Each
 * read is numbered: only the latest is shown, and its number makes the threads read anew.
 */
export async function loadInbox(api: Api, id: string): Promise<void> {
    const mine = ++inboxing;
    forget('inbox');
    try {
        const lines = await api.call<Page<JournalEntry>>('journal.list', {
            campaign_id: id,
            kinds: ['inbound_reply_observed'],
            limit: inboxLines,
        });
        const { entries, left } = inboxEntriesOf(lines.items);
        if (mine === inboxing) inbox.value = { id, read: mine, entries, left };
    } catch (error) {
        if (mine === inboxing) failOver(id, 'inbox', error);
    }
}

/** What the People tab lists: one count of the view, or All, its everyone; and what the search found. */
export type PeopleQuery = { filter?: string; q?: string };

export const members = signal<MembershipItem[]>([]);
export const membersCursor = signal<string | null>(null);
export const membersFailure = signal<string | null>(null);
/** Which list the members above belong to, as campaign id and query: no other list's people are shown for this one's. */
export const membersShown = signal<string | null>(null);
/** The first page's request, the period's instants included: the next pages ask the same, with the cursor. */
let membersRequest: Record<string, unknown> | null = null;

const memberPage = 50;
let listed = 0;
let loadingMore = false;

export function membersKey(id: string, table: Table, query: PeopleQuery): string {
    return JSON.stringify([id, numbersKey(table), query.filter ?? null, query.q ?? null]);
}

/**
 * The list the runtime keeps for the count: the count's own filter, the period with an activity, and the search. All is
 * everyone in Summary, and in Activity everyone something happened to in the period.
 */
export function membersBody(
    id: string,
    table: Table,
    query: PeopleQuery,
    window: { since: string; until: string },
): Record<string, unknown> {
    const body: Record<string, unknown> = { campaign_id: id, limit: memberPage, ...archivedLeftOut };
    if (table.view === 'summary') {
        if (query.filter) body['summary'] = query.filter;
    } else Object.assign(body, { activity: query.filter ?? 'people', ...window });
    if (query.q) body['query'] = query.q;
    return body;
}

export async function listMembers(
    api: Api,
    id: string,
    table: Table,
    query: PeopleQuery,
    now: Date,
    zone: string,
): Promise<void> {
    const mine = ++listed;
    membersFailure.value = null;
    membersCursor.value = null;
    const window = periodWindow(table.period, now, zone);
    const body = membersBody(id, table, query, window);
    membersRequest = body;
    void count(api, id, table, now, zone, window);
    try {
        const page = await api.call<Page<MembershipItem>>('campaign.list_contacts', body);
        if (mine !== listed) return;
        members.value = page.items;
        membersCursor.value = page.next_cursor;
        membersShown.value = membersKey(id, table, query);
    } catch (error) {
        if (mine === listed) fail(error, membersFailure);
    }
}

export async function moreMembers(api: Api): Promise<void> {
    const after = membersCursor.value;
    if (!after || loadingMore || !membersRequest) return;
    loadingMore = true;
    const mine = listed;
    try {
        const body = { ...membersRequest, cursor: after };
        const page = await api.call<Page<MembershipItem>>('campaign.list_contacts', body);
        if (mine !== listed) return;
        members.value = mergePage(members.value, page.items, (m) => m.contact.id);
        membersCursor.value = page.next_cursor;
    } catch (error) {
        if (mine === listed) fail(error, membersFailure);
    } finally {
        loadingMore = false;
    }
}

/**
 * The approval or question a waiting person's row opened, shown in place of the campaign until it is decided or left —
 * and only over the campaign it was opened in.
 */
export const waiting = signal<({ campaignId: string } & ({ approval: Approval } | { decision: Decision })) | null>(
    null,
);
export const busy = signal(false);

/** The approval or question a person's why names, or one the Timeline lists. */
export async function openWaiting(
    api: Api,
    campaignId: string,
    what: { approval_id: string } | { decision_id: string },
): Promise<void> {
    try {
        waiting.value =
            'approval_id' in what
                ? { campaignId, approval: await api.call<Approval>('approval.get', what) }
                : { campaignId, decision: await api.call<Decision>('decision.get', what) };
    } catch (error) {
        fail(error, actionFailure);
    }
}

export function waitingOf(why: MembershipWhy): { approval_id: string } | { decision_id: string } | null {
    if (why.kind === 'approval' && why.approval_id) return { approval_id: why.approval_id };
    if (why.kind === 'question' && why.decision_id) return { decision_id: why.decision_id };
    return null;
}

/** What waits for a person in the campaign, the questions about the campaign as a whole included: the Timeline's middle. */
export const pendingApprovals = signal<ApprovalSummary[]>([]);
export const pendingQuestions = signal<DecisionSummary[]>([]);
export const approvalsCursor = signal<string | null>(null);
export const questionsCursor = signal<string | null>(null);
export const pendingOf = signal<string | null>(null);
/** The campaign's planned work and what is running, each as the runtime lists it, soonest first. */
export const planned = signal<WorkItemSummary[]>([]);
export const running = signal<WorkItemSummary[]>([]);
export const plannedCursor = signal<string | null>(null);
export const runningCursor = signal<string | null>(null);
let pendingGeneration = 0;

const approvalsBody = (id: string) => ({ status: 'pending', campaign_id: id, limit: 50 });
const questionsBody = (id: string) => ({ status: 'pending', campaign_id: id, limit: 50 });
const plannedBody = (id: string) => ({ campaign_id: id, status: ['created'], order: 'due', limit: 50 });
const runningBody = (id: string) => ({ campaign_id: id, status: ['scheduled', 'processing'], order: 'due', limit: 50 });

export async function loadPending(api: Api, id: string): Promise<void> {
    const mine = ++pendingGeneration;
    try {
        const [approvals, questions, plan, run] = await Promise.all([
            api.call<Page<ApprovalSummary>>('approval.list', approvalsBody(id)),
            api.call<Page<DecisionSummary>>('decision.list', questionsBody(id)),
            api.call<Page<WorkItemSummary>>('workitem.list', plannedBody(id)),
            api.call<Page<WorkItemSummary>>('workitem.list', runningBody(id)),
        ]);
        if (mine !== pendingGeneration) return;
        pendingApprovals.value = approvals.items;
        pendingQuestions.value = questions.items;
        approvalsCursor.value = approvals.next_cursor;
        questionsCursor.value = questions.next_cursor;
        planned.value = plan.items;
        running.value = run.items;
        plannedCursor.value = plan.next_cursor;
        runningCursor.value = run.next_cursor;
        pendingOf.value = id;
    } catch (error) {
        if (mine === pendingGeneration) fail(error, panelFailure);
    }
}

export type Section = 'approvals' | 'questions' | 'planned' | 'running';

/** The next page of one of the Timeline's sections above the now line. */
export async function morePending(api: Api, id: string, section: Section): Promise<void> {
    const mine = pendingGeneration;
    try {
        if (section === 'approvals' && approvalsCursor.value) {
            const page = await api.call<Page<ApprovalSummary>>('approval.list', {
                ...approvalsBody(id),
                cursor: approvalsCursor.value,
            });
            if (mine !== pendingGeneration) return;
            pendingApprovals.value = mergePage(pendingApprovals.value, page.items, (a) => a.id);
            approvalsCursor.value = page.next_cursor;
        } else if (section === 'questions' && questionsCursor.value) {
            const page = await api.call<Page<DecisionSummary>>('decision.list', {
                ...questionsBody(id),
                cursor: questionsCursor.value,
            });
            if (mine !== pendingGeneration) return;
            pendingQuestions.value = mergePage(pendingQuestions.value, page.items, (d) => d.id);
            questionsCursor.value = page.next_cursor;
        } else if (section === 'planned' && plannedCursor.value) {
            const page = await api.call<Page<WorkItemSummary>>('workitem.list', {
                ...plannedBody(id),
                cursor: plannedCursor.value,
            });
            if (mine !== pendingGeneration) return;
            planned.value = mergePage(planned.value, page.items, (w) => w.id);
            plannedCursor.value = page.next_cursor;
        } else if (section === 'running' && runningCursor.value) {
            const page = await api.call<Page<WorkItemSummary>>('workitem.list', {
                ...runningBody(id),
                cursor: runningCursor.value,
            });
            if (mine !== pendingGeneration) return;
            running.value = mergePage(running.value, page.items, (w) => w.id);
            runningCursor.value = page.next_cursor;
        }
    } catch (error) {
        if (mine === pendingGeneration) fail(error, panelFailure);
    }
}

/** Decides what waits, as Attention does, then shows the campaign again; the caller reads its numbers and list anew. */
export async function decide(api: Api, operation: string, body: Record<string, unknown>): Promise<void> {
    busy.value = true;
    actionFailure.value = null;
    try {
        await api.call(operation, body, 'write');
        waiting.value = null;
    } catch (error) {
        fail(error, actionFailure);
    } finally {
        busy.value = false;
    }
}

/**
 * Pauses or starts the open campaign as the paired person; the panel shows the campaign the runtime answers with, while
 * it is still the one open.
 */
export async function transition(
    api: Api,
    operation: 'campaign.pause' | 'campaign.start',
    id: string,
    reason?: string,
): Promise<void> {
    const body: Record<string, unknown> = { campaign_id: id };
    if (reason) body['reason'] = reason;
    const answered = await api.call<Campaign>(operation, body, 'write');
    if (campaign.value?.id === id) campaign.value = answered;
}

/**
 * Archives a campaign as the paired person, with the reason they typed: the runtime's answer is the campaign as it is now,
 * archived, also when another window archived it first.
 */
export async function archive(api: Api, id: string, reason?: string): Promise<Campaign> {
    const body: Record<string, unknown> = { campaign_id: id };
    if (reason) body['reason'] = reason;
    const answered = await api.call<Campaign>('campaign.archive', body, 'write');
    if (campaign.value?.id === id) campaign.value = answered;
    return answered;
}

/** Adds people to the open campaign as the paired person: the runtime says, for each, whether it added them. */
export function addPeople(api: Api, id: string, people: string[], reason?: string): Promise<AddContactsResult> {
    const body: Record<string, unknown> = { campaign_id: id, contacts: people.map((contact_id) => ({ contact_id })) };
    if (reason) body['reason'] = reason;
    return api.call<AddContactsResult>('campaign.add_contacts', body, 'write');
}

/** Whether the open campaign has a `campaign.create` unfinished, for the campaign it was asked of; none until it is answered. */
export const creating = signal<{ id: string; open: boolean } | null>(null);
let creatingGeneration = 0;

/**
 * The one read behind the agent's kind: a page of the campaign's unfinished provider operations, asked only of a started
 * agent with no pin. A failed read says nothing: the kind then stays unsaid, and no failure is shown for it.
 */
export async function loadCreating(api: Api, id: string): Promise<void> {
    const mine = ++creatingGeneration;
    try {
        const page = await api.call<Page<WorkItemSummary>>('workitem.list', createBody(id));
        if (mine === creatingGeneration) creating.value = { id, open: unfinishedCreate(page.items) };
    } catch {
        if (mine === creatingGeneration) creating.value = null;
    }
}
