import { signal } from '@preact/signals';
import { ApiError, type Api } from '../../api/client';
import type {
    ActivityBucket,
    ActivityPeriod,
    Campaign,
    CampaignActivityByPeriodResponse,
    CampaignActivityResponse,
    CampaignActivityTotal,
    MembershipItem,
    Page,
} from '../../api/types';
import { mergePage } from '../../lists/paging';
import type { Period, Route } from '../../router';
import { periodWindow } from '../../time/periods';
import { fail } from '../people/store';

export type At = Extract<Route, { screen: 'performance' }>;
/** A campaign chosen in the filter, with what its chip says. */
export type Chosen = { id: string; name: string; status: string };

export const total = signal<CampaignActivityTotal | null>(null);
export const totalFailure = signal<string | null>(null);
/** The two series, with the moment they were read up to: the bar that holds it is the one running. */
export const series = signal<{ contacted: ActivityBucket[]; replied: ActivityBucket[]; readAt: Date } | null>(null);
export const seriesFailure = signal<string | null>(null);
/** Which period and campaigns the numbers on show belong to: no other's are shown as if they were these. */
export const shown = signal<string | null>(null);
/** True while a refresh the person asked for is under way: long enough to be seen, even when the answer is instant. */
export const refreshing = signal(false);

export const people = signal<MembershipItem[]>([]);
export const peopleCursor = signal<string | null>(null);
export const peopleFailure = signal<string | null>(null);
/** Which list the people on show belong to. */
export const peopleShown = signal<string | null>(null);

/** The names of the campaigns chosen, as the picker gave them or the runtime said; an address names only ids. */
export const campaigns = signal<Chosen[]>([]);
export const notice = signal<string | null>(null);

/** What each bar of a period is: today by the hour, a week or a month by the day, a quarter by the week. */
export function bucketOf(period: Period): ActivityPeriod {
    return period === 'today' ? 'hour' : period === '90d' ? 'week' : 'day';
}

export function numbersKey(at: At): string {
    return JSON.stringify([at.period, at.campaigns]);
}

export function listKey(at: At): string {
    return JSON.stringify([at.period, at.campaigns, at.count ?? null, at.from ?? null, at.to ?? null]);
}

/** The campaigns the reads name: none named is every campaign not archived, which the runtime takes as no list. */
function scope(at: At): Record<string, unknown> {
    return at.campaigns.length > 0 ? { campaign_ids: at.campaigns } : {};
}

let numbersRead = 0;
/** The window the numbers on show were read for: a tile's list is read for the same one, so its number counts it. */
let numbersWindow: { key: string; since: string; until: string } | null = null;

/** The period's counts and the two series, read together; a failure of one is said where it would be. */
export async function load(api: Api, at: At, now: Date, zone: string): Promise<void> {
    const mine = ++numbersRead;
    const window = periodWindow(at.period, now, zone);
    numbersWindow = { key: numbersKey(at), ...window };
    const period = bucketOf(at.period);
    const over = (count: string) =>
        api.call<CampaignActivityByPeriodResponse>('campaign.activity_by_period', {
            ...scope(at),
            count,
            period,
            ...window,
            zone,
        });
    const [counted, contacted, replied] = await Promise.allSettled([
        api.call<CampaignActivityResponse>('campaign.activity', { ...scope(at), ...window }),
        over('contacted'),
        over('replied'),
    ]);
    if (mine !== numbersRead) return;
    totalFailure.value = null;
    seriesFailure.value = null;
    if (counted.status === 'fulfilled') total.value = counted.value.total;
    else {
        total.value = null;
        fail(counted.reason, totalFailure);
    }
    if (contacted.status === 'fulfilled' && replied.status === 'fulfilled') {
        series.value = {
            contacted: contacted.value.buckets,
            replied: replied.value.buckets,
            readAt: new Date(window.until),
        };
    } else {
        series.value = null;
        fail(
            contacted.status === 'rejected' ? contacted.reason : (replied as PromiseRejectedResult).reason,
            seriesFailure,
        );
    }
    shown.value = numbersKey(at);
}

let listed = 0;
let listRequest: Record<string, unknown> | null = null;
let loadingMore = false;

/** The people of the tile chosen, over the period or the bar's own window, the latest to whom it happened first. */
export async function listPeople(api: Api, at: At, now: Date, zone: string): Promise<void> {
    if (!at.count) return;
    const mine = ++listed;
    peopleFailure.value = null;
    peopleCursor.value = null;
    const read = numbersWindow?.key === numbersKey(at) ? numbersWindow : periodWindow(at.period, now, zone);
    const window = at.from && at.to ? { since: at.from, until: at.to } : { since: read.since, until: read.until };
    const body = { ...scope(at), activity: at.count, ...window, limit: 50 };
    listRequest = body;
    try {
        const page = await api.call<Page<MembershipItem>>('campaign.list_contacts', body);
        if (mine !== listed) return;
        people.value = page.items;
        peopleCursor.value = page.next_cursor;
        peopleShown.value = listKey(at);
    } catch (error) {
        // A list that could not be read is not shown as empty: the rows on show stay, and a new list shows none.
        if (mine === listed) fail(error, peopleFailure);
    }
}

export async function morePeople(api: Api): Promise<void> {
    const after = peopleCursor.value;
    if (!after || loadingMore || !listRequest) return;
    loadingMore = true;
    const mine = listed;
    try {
        const page = await api.call<Page<MembershipItem>>('campaign.list_contacts', { ...listRequest, cursor: after });
        if (mine !== listed) return;
        people.value = mergePage(people.value, page.items, (person) => person.contact.id);
        peopleCursor.value = page.next_cursor;
    } catch (error) {
        if (mine === listed) fail(error, peopleFailure);
    } finally {
        loadingMore = false;
    }
}

/**
 * Everything on the page again, the button turning for at least half a second. A bar's people are the screen's to read
 * again, once the series read here says where the bar ends now.
 */
export async function refresh(api: Api, at: At, now: Date, zone: string): Promise<void> {
    refreshing.value = true;
    try {
        const floor = new Promise((resolve) => setTimeout(resolve, 500));
        await Promise.all([load(api, at, now, zone), at.from ? null : listPeople(api, at, now, zone), floor]);
    } finally {
        refreshing.value = false;
    }
}

/**
 * The names of the campaigns an address names; the ids the runtime no longer knows are left out, and said so. Null, and
 * nothing changed, when the address names other campaigns by the time the answers come.
 */
export async function resolveCampaigns(api: Api, ids: string[], stillAsked: () => boolean): Promise<string[] | null> {
    const known = new Map(campaigns.value.map((campaign) => [campaign.id, campaign]));
    const read = await Promise.all(
        ids.map(async (id): Promise<Chosen | null> => {
            const already = known.get(id);
            if (already) return already;
            try {
                const found = await api.call<Campaign>('campaign.get', { campaign_id: id });
                return { id, name: found.name, status: found.status };
            } catch (error) {
                if (error instanceof ApiError && error.status === 404) return null;
                fail(error, notice);
                return { id, name: id, status: 'active' };
            }
        }),
    );
    if (!stillAsked()) return null;
    const kept = read.filter((campaign): campaign is Chosen => campaign !== null);
    campaigns.value = kept;
    if (kept.length < ids.length) notice.value = 'A campaign in this address no longer exists; it was left out.';
    return kept.map((campaign) => campaign.id);
}
