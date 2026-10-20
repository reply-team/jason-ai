import { signal } from '@preact/signals';
import { ApiError, Unpaired, type Api } from '../../api/client';
import { session } from '../../api/session';
import type {
    ApprovalSummary,
    CampaignSummary,
    Contact,
    JournalEntry,
    Page,
    Role,
    WorkItem,
    WorkItemCount,
    WorkItemSummary,
} from '../../api/types';
import { mergePage } from '../../lists/paging';
import type { Tab, WorkItemFilters } from '../../router';

export type View = { tab: Tab; filters: WorkItemFilters };

export const tabs: readonly Tab[] = ['now', 'planned', 'done'];

export const items = signal<WorkItemSummary[]>([]);
export const cursor = signal<string | null>(null);
export const counts = signal<Record<Tab, number | null>>({ now: null, planned: null, done: null });
export const loaded = signal(false);
export const failure = signal<string | null>(null);
/** A line about the address itself: a filter it named that could not be kept. */
export const notice = signal<string | null>(null);
/** True while a refresh the person asked for is under way: long enough to be seen, even when the answer is instant. */
export const refreshing = signal(false);
export const roles = signal<Role[]>([]);
/** The campaign the filter names, with the name to show for it. */
export const campaign = signal<{ id: string; name: string } | null>(null);
/** The person the filter names, with the name to show for them. */
export const person = signal<{ id: string; name: string } | null>(null);

const pageSize = 50;
let generation = 0;
let shownView = '';
let loadingMore = false;

/** The statuses each tab lists; Done narrowed to failures by its chip. */
export function statusesOf(tab: Tab, failedOnly = false): string[] {
    if (tab === 'now') return ['processing', 'scheduled', 'awaiting_approval'];
    if (tab === 'planned') return ['created'];
    return failedOnly ? ['failed'] : ['succeeded', 'failed', 'cancelled', 'expired'];
}

/** What is running in the order it came, what is planned soonest first, what finished newest first. */
export function orderOf(tab: Tab): 'created' | 'due' | 'finished' {
    return tab === 'now' ? 'created' : tab === 'planned' ? 'due' : 'finished';
}

/** The start of a day on the person's own clock, as the moment the runtime takes. */
function dayStart(day: string, after = 0): string {
    const at = new Date(`${day}T00:00:00`);
    at.setDate(at.getDate() + after);
    return at.toISOString();
}

function filterBody(filters: WorkItemFilters, tab: Tab): Record<string, unknown> {
    const body: Record<string, unknown> = {};
    // The day narrows the time a tab orders by: when work is due on Planned, when it finished on Done. Now has none.
    if (tab !== 'now' && filters.on) {
        body['since'] = dayStart(filters.on);
        body['until'] = dayStart(filters.on, 1);
    }
    if (filters.campaign) body['campaign_id'] = filters.campaign;
    if (filters.contact) body['contact_id'] = filters.contact;
    if (filters.kind) body['kind'] = filters.kind;
    if (filters.role && filters.kind !== 'provider_op') body['role'] = filters.role;
    return body;
}

function countBody(tab: Tab, filters: WorkItemFilters): object {
    return {
        ...filterBody(filters, tab),
        status: statusesOf(tab, tab === 'done' && filters.failed),
        order: orderOf(tab),
    };
}

function listBody(view: View): Record<string, unknown> {
    return { ...countBody(view.tab, view.filters), limit: pageSize };
}

function fail(error: unknown): void {
    if (error instanceof Unpaired) {
        session.clear();
        return;
    }
    failure.value = error instanceof ApiError ? error.message : String(error);
}

/** The tab's first page and the three counts, together; an answer to a view the person has already left is dropped. */
export async function load(api: Api, view: View): Promise<void> {
    const mine = ++generation;
    failure.value = null;
    // The old cursor belongs to the old answer: until the new first page arrives there is nothing to continue. And a
    // new view's numbers are unknown until counted, so they are absent rather than the old view's.
    cursor.value = null;
    const key = JSON.stringify(view);
    if (key !== shownView) {
        shownView = key;
        counts.value = { now: null, planned: null, done: null };
        // Another view's rows, regrouped by this view's rules, would read as this view's for a moment.
        items.value = [];
        loaded.value = false;
    }
    try {
        const [page, ...totals] = await Promise.all([
            api.call<Page<WorkItemSummary>>('workitem.list', listBody(view)),
            ...tabs.map((tab) => api.call<WorkItemCount>('workitem.count', countBody(tab, view.filters))),
        ]);
        if (mine !== generation) return;
        items.value = page.items;
        cursor.value = page.next_cursor;
        counts.value = { now: totals[0]!.total, planned: totals[1]!.total, done: totals[2]!.total };
        loaded.value = true;
    } catch (error) {
        if (mine === generation) fail(error);
    }
}

export async function more(api: Api, view: View): Promise<void> {
    const after = cursor.value;
    if (!after || loadingMore) return;
    loadingMore = true;
    const mine = generation;
    try {
        const page = await api.call<Page<WorkItemSummary>>('workitem.list', { ...listBody(view), cursor: after });
        if (mine !== generation) return;
        items.value = mergePage(items.value, page.items, (i) => i.id);
        cursor.value = page.next_cursor;
    } catch (error) {
        if (mine === generation) fail(error);
    } finally {
        loadingMore = false;
    }
}

/** A refresh the person asked for, shown for at least a moment so the button is seen to do something. */
export async function refresh(api: Api, view: View): Promise<void> {
    refreshing.value = true;
    try {
        await Promise.all([load(api, view), new Promise((resolve) => setTimeout(resolve, 500))]);
    } finally {
        refreshing.value = false;
    }
}

/** The roles the role filter offers; campaigns and people are searched as the person types. */
export async function loadSources(api: Api): Promise<void> {
    try {
        roles.value = (await api.call<Page<Role>>('role.list', {})).items;
    } catch (error) {
        fail(error);
    }
}

/** The name for a campaign an address names; false when the runtime no longer knows it. */
export async function resolveCampaign(api: Api, id: string): Promise<boolean> {
    if (campaign.value?.id === id) return true;
    try {
        const found = await api.call<CampaignSummary>('campaign.get', { campaign_id: id });
        campaign.value = { id, name: found.name };
        return true;
    } catch (error) {
        if (error instanceof ApiError && error.status === 404) return false;
        fail(error);
        return true;
    }
}

/** The first ten campaigns, or the first ten whose name holds the text. */
export async function searchCampaigns(api: Api, query: string): Promise<CampaignSummary[]> {
    const page = await api.call<Page<CampaignSummary>>('campaign.list', query ? { query, limit: 10 } : { limit: 10 });
    return page.items;
}

export function displayName(contact: Contact): string {
    const name = [contact.first_name, contact.last_name].filter((part) => part).join(' ');
    return name || firstChannel(contact) || contact.id;
}

/** The value a person is most recognised by: their email where they have one, else whatever channel comes first. */
export function firstChannel(contact: Contact): string | undefined {
    return (contact.channels.find((c) => c.channel === 'email') ?? contact.channels[0])?.value;
}

/** The name for a person an address names; false when the runtime no longer knows them. */
export async function resolvePerson(api: Api, id: string): Promise<boolean> {
    if (person.value?.id === id) return true;
    try {
        const contact = await api.call<Contact>('contact.get', { contact_id: id });
        person.value = { id, name: displayName(contact) };
        return true;
    } catch (error) {
        if (error instanceof ApiError && error.status === 404) return false;
        fail(error);
        return true;
    }
}

/** The first ten people, or the first ten found by anything they are known by. */
export async function searchPeople(api: Api, query: string): Promise<Contact[]> {
    const page = await api.call<Page<Contact>>('contact.list', query ? { query, limit: 10 } : { limit: 10 });
    return page.items;
}

// ---- the panel ---------------------------------------------------------------------------------------------------

export const detail = signal<WorkItem | null>(null);
export const history = signal<JournalEntry[]>([]);
export const historyCursor = signal<string | null>(null);
/** The approval a waiting item waits for, so the panel can link to it. */
export const approvalId = signal<string | null>(null);
/** A line about the open item itself: it changed under the person while they looked at it. */
export const panelNotice = signal<string | null>(null);
export const busy = signal(false);

let opened = 0;

/** The item, its history and, while it waits for a person, the approval it waits for. */
export async function openItem(api: Api, id: string): Promise<void> {
    const mine = ++opened;
    panelNotice.value = null;
    try {
        const [item, entries] = await Promise.all([
            api.call<WorkItem>('workitem.get', { work_item_id: id }),
            api.call<Page<JournalEntry>>('journal.list', { work_item_id: id }),
        ]);
        if (mine !== opened) return;
        detail.value = item;
        history.value = entries.items;
        historyCursor.value = entries.next_cursor;
        approvalId.value = null;
        if (item.status === 'awaiting_approval') {
            const waiting = await api.call<Page<ApprovalSummary>>('approval.list', {
                work_item_id: id,
                statuses: ['pending'],
            });
            if (mine === opened) approvalId.value = waiting.items[0]?.id ?? null;
        }
    } catch (error) {
        if (mine === opened) {
            detail.value = null;
            fail(error);
        }
    }
}

export function closeItem(): void {
    opened++;
    detail.value = null;
    history.value = [];
    historyCursor.value = null;
    approvalId.value = null;
    panelNotice.value = null;
}

/** The history again, after a change the person made: its own lines are the newest. */
async function reloadHistory(api: Api, id: string): Promise<void> {
    const mine = opened;
    try {
        const page = await api.call<Page<JournalEntry>>('journal.list', { work_item_id: id });
        if (mine !== opened) return;
        history.value = page.items;
        historyCursor.value = page.next_cursor;
    } catch (error) {
        fail(error);
    }
}

export async function moreHistory(api: Api, id: string): Promise<void> {
    const after = historyCursor.value;
    if (!after) return;
    try {
        const page = await api.call<Page<JournalEntry>>('journal.list', { work_item_id: id, cursor: after });
        history.value = mergePage(history.value, page.items, (e) => e.id);
        historyCursor.value = page.next_cursor;
    } catch (error) {
        fail(error);
    }
}

export type Patch = { priority?: number; not_before?: string | null; due_at?: string | null };

/**
 * A change, then the list and the counts again; an item that finished while the person looked is read again and
 * said so, because the runtime refuses to change finished work.
 */
async function change(api: Api, view: View, id: string, call: () => Promise<WorkItem>): Promise<boolean> {
    busy.value = true;
    panelNotice.value = null;
    try {
        detail.value = await call();
        void reloadHistory(api, id);
        return true;
    } catch (error) {
        if (error instanceof ApiError && error.code === 'workitem_terminal') {
            await openItem(api, id);
            panelNotice.value = 'This item finished meanwhile.';
        } else {
            fail(error);
        }
        return false;
    } finally {
        busy.value = false;
        void load(api, view);
    }
}

/** The approval a waiting item waits for, decided from its panel as on Attention; the item is read again afterwards. */
async function decide(api: Api, view: View, id: string, call: () => Promise<unknown>): Promise<void> {
    busy.value = true;
    try {
        await call();
    } catch (error) {
        fail(error);
    } finally {
        busy.value = false;
        await openItem(api, id);
        void load(api, view);
    }
}

export function approve(api: Api, view: View, id: string, approval: string): Promise<void> {
    return decide(api, view, id, () => api.call('approval.approve', { approval_id: approval }, 'write'));
}

export function reject(api: Api, view: View, id: string, approval: string, reason?: string): Promise<void> {
    const body = reason ? { approval_id: approval, reason } : { approval_id: approval };
    return decide(api, view, id, () => api.call('approval.reject', body, 'write'));
}

export function save(api: Api, view: View, id: string, patch: Patch): Promise<boolean> {
    return change(api, view, id, () => api.call<WorkItem>('workitem.update', { work_item_id: id, ...patch }, 'write'));
}

export function cancel(api: Api, view: View, id: string, reason?: string): Promise<boolean> {
    const body = reason ? { work_item_id: id, reason } : { work_item_id: id };
    return change(api, view, id, () => api.call<WorkItem>('workitem.cancel', body, 'write'));
}
