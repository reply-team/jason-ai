import { signal } from '@preact/signals';
import { ApiError, Unpaired, type Api } from '../../api/client';
import { session } from '../../api/session';
import type { AddContactsResult, CampaignSummary, Contact, JournalEntry, Page, WorkItemSummary } from '../../api/types';
import { mergePage } from '../../lists/paging';
import type { PeopleFilters } from '../../router';

export const people = signal<Contact[]>([]);
export const cursor = signal<string | null>(null);
export const loaded = signal(false);
export const failure = signal<string | null>(null);
/** True while a refresh the person asked for is under way: long enough to be seen, even when the answer is instant. */
export const refreshing = signal(false);
/** The campaign the filter names, with the name to show for it. */
export const campaign = signal<{ id: string; name: string } | null>(null);

const pageSize = 50;
let generation = 0;
let loadingMore = false;

export function listBody(filters: PeopleFilters): Record<string, unknown> {
    const body: Record<string, unknown> = { order: 'activity', limit: pageSize };
    if (filters.q) body['query'] = filters.q;
    if (filters.campaign) body['campaign_id'] = filters.campaign;
    if (filters.archived) body['include_archived'] = true;
    return body;
}

export function fail(error: unknown, into = failure): void {
    if (error instanceof Unpaired) {
        session.clear();
        return;
    }
    into.value = error instanceof ApiError ? error.message : String(error);
}

/** The first page for these filters; an answer to filters the person has already left is dropped. */
export async function load(api: Api, filters: PeopleFilters): Promise<void> {
    const mine = ++generation;
    failure.value = null;
    cursor.value = null;
    try {
        const page = await api.call<Page<Contact>>('contact.list', listBody(filters));
        if (mine !== generation) return;
        people.value = page.items;
        cursor.value = page.next_cursor;
        loaded.value = true;
    } catch (error) {
        if (mine === generation) fail(error);
    }
}

export async function more(api: Api, filters: PeopleFilters): Promise<void> {
    const after = cursor.value;
    if (!after || loadingMore) return;
    loadingMore = true;
    const mine = generation;
    try {
        const page = await api.call<Page<Contact>>('contact.list', { ...listBody(filters), cursor: after });
        if (mine !== generation) return;
        people.value = mergePage(people.value, page.items, (p) => p.id);
        cursor.value = page.next_cursor;
    } catch (error) {
        if (mine === generation) fail(error);
    } finally {
        loadingMore = false;
    }
}

/** The list again from its first page, the button turning for at least half a second. */
export async function refresh(api: Api, filters: PeopleFilters): Promise<void> {
    refreshing.value = true;
    const floor = new Promise((resolve) => setTimeout(resolve, 500));
    await Promise.all([load(api, filters), floor]);
    refreshing.value = false;
}

/** The campaigns the picker offers: the first few, or the ones whose name holds what was typed. Archived ones are not offered. */
export async function searchCampaigns(api: Api, query: string): Promise<CampaignSummary[]> {
    const page = await api.call<Page<CampaignSummary>>('campaign.list', query ? { query, limit: 10 } : { limit: 10 });
    return page.items;
}

/** The name of the campaign an address names; false when the runtime no longer knows it. */
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

/** The person open on the right, as contact.get answers: the record, the campaigns they are on, the latest line. */
export const person = signal<Contact | null>(null);
export const personFailure = signal<string | null>(null);
let opened = 0;

export async function open(api: Api, id: string): Promise<void> {
    const mine = ++opened;
    personFailure.value = null;
    if (person.value?.id !== id) person.value = null;
    try {
        const found = await api.call<Contact>('contact.get', { contact_id: id });
        if (mine === opened) person.value = found;
    } catch (error) {
        if (mine === opened) fail(error, personFailure);
    }
}

export function close(): void {
    ++opened;
    person.value = null;
    personFailure.value = null;
}

/** Adds the open person to a campaign as the paired person, and reads them again so the card shows the campaign. */
export async function add(api: Api, id: string, campaignId: string, reason?: string): Promise<AddContactsResult> {
    const body: Record<string, unknown> = { campaign_id: campaignId, contacts: [{ contact_id: id }] };
    if (reason) body['reason'] = reason;
    const result = await api.call<AddContactsResult>('campaign.add_contacts', body, 'write');
    void open(api, id);
    return result;
}

export type Chip = 'all' | 'replies' | 'work' | 'decisions' | 'changes';

/** The journal kinds each chip asks for; All is every one of them, and nothing the journal keeps for itself. */
export const chipKinds: Record<Exclude<Chip, 'all'>, string[]> = {
    replies: ['inbound_reply_observed', 'provider_event_observed'],
    work: [
        'workitem_succeeded',
        'workitem_failed',
        'workitem_cancelled',
        'workitem_expired',
        'external_effect_reported',
    ],
    decisions: ['approval_requested', 'approval_approved', 'approval_rejected', 'decision_raised', 'decision_answered'],
    changes: ['contact_created', 'contact_updated', 'contact_archived'],
};

export function kindsOf(chip: Chip): string[] {
    return chip === 'all' ? Object.values(chipKinds).flat() : chipKinds[chip];
}

export const openStatuses = ['created', 'awaiting_approval', 'scheduled', 'processing'];

export const chip = signal<Chip>('all');
/** The person's open work, soonest due first as the runtime orders it. */
export const openWork = signal<WorkItemSummary[]>([]);
export const moreOpenWork = signal(false);
export const lines = signal<JournalEntry[]>([]);
export const linesCursor = signal<string | null>(null);
export const linesLoaded = signal(false);
export const timelineFailure = signal<string | null>(null);
let timelineGeneration = 0;
let loadingLines = false;

/** The open work and the first page of the chip's lines, together; an answer for another person or chip is dropped. */
/** The person the timeline's rows belong to: another person's rows are never shown, even for the moment a read takes. */
export const timelineOf = signal<string | null>(null);

export async function loadTimeline(api: Api, id: string, which: Chip): Promise<void> {
    const mine = ++timelineGeneration;
    if (timelineOf.value !== id) {
        timelineOf.value = id;
        openWork.value = [];
        lines.value = [];
    }
    chip.value = which;
    timelineFailure.value = null;
    linesLoaded.value = false;
    linesCursor.value = null;
    try {
        const [work, page] = await Promise.all([
            api.call<Page<WorkItemSummary>>('workitem.list', {
                contact_id: id,
                status: openStatuses,
                order: 'due',
                limit: 50,
            }),
            api.call<Page<JournalEntry>>('journal.list', { contact_id: id, kinds: kindsOf(which), limit: 50 }),
        ]);
        if (mine !== timelineGeneration) return;
        openWork.value = work.items;
        moreOpenWork.value = work.next_cursor !== null;
        lines.value = page.items;
        linesCursor.value = page.next_cursor;
        linesLoaded.value = true;
    } catch (error) {
        if (mine === timelineGeneration) fail(error, timelineFailure);
    }
}

export async function moreLines(api: Api, id: string): Promise<void> {
    const after = linesCursor.value;
    if (!after || loadingLines) return;
    loadingLines = true;
    const mine = timelineGeneration;
    try {
        const page = await api.call<Page<JournalEntry>>('journal.list', {
            contact_id: id,
            kinds: kindsOf(chip.value),
            limit: 50,
            cursor: after,
        });
        if (mine !== timelineGeneration) return;
        lines.value = mergePage(lines.value, page.items, (e) => e.id);
        linesCursor.value = page.next_cursor;
    } catch (error) {
        if (mine === timelineGeneration) fail(error, timelineFailure);
    } finally {
        loadingLines = false;
    }
}
