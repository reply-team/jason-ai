import { signal } from '@preact/signals';

export type Tab = 'now' | 'planned' | 'done';
export type WorkItemFilters = {
    campaign?: string;
    contact?: string;
    kind?: 'ai_role' | 'provider_op';
    role?: string;
    /** One day, as YYYY-MM-DD, on the time the tab orders by: due on Planned, finished on Done. */
    on?: string;
    failed?: boolean;
};

export type PersonTab = 'timeline' | 'threads' | 'details';
export type PeopleFilters = {
    /** The search typed into the list: a name, a company, a title or part of a channel value. */
    q?: string;
    campaign?: string;
    archived?: boolean;
};

const day = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;

export type Route =
    | { screen: 'attention'; kind?: 'approvals' | 'questions'; id?: string; decided?: true }
    | { screen: 'system' }
    | { screen: 'workitems'; tab: Tab; id?: string; filters: WorkItemFilters }
    | { screen: 'people'; id?: string; tab: PersonTab; filters: PeopleFilters };

const tabs: readonly Tab[] = ['now', 'planned', 'done'];

/** The fragment's path, ignoring anything after `?` (the ticket travels there and never reaches the router). */
export function parseRoute(hash: string): Route {
    const [path = '', query = ''] = hash.replace(/^#/, '').split('?');
    const segments = path.split('/').filter((s) => s.length > 0);
    if (segments[0] === 'system') return { screen: 'system' };
    if (segments[0] === 'workitems') return workItems(segments.slice(1), query);
    if (segments[0] === 'people') return people(segments.slice(1), query);
    const decided = new URLSearchParams(query).get('decided') === '1' ? { decided: true as const } : {};
    if (segments[0] === 'attention' && (segments[1] === 'approvals' || segments[1] === 'questions') && segments[2]) {
        const id = decode(segments[2]);
        if (id !== null) return { screen: 'attention', kind: segments[1], id, ...decided };
    }
    return { screen: 'attention', ...decided };
}

function workItems(segments: string[], query: string): Route {
    const tab = tabs.find((t) => t === segments[0]) ?? 'now';
    const id = tab === segments[0] && segments[1] ? decode(segments[1]) : null;
    const params = new URLSearchParams(query);
    const filters: WorkItemFilters = {};
    const campaign = params.get('campaign');
    if (campaign) filters.campaign = campaign;
    const contact = params.get('contact');
    if (contact) filters.contact = contact;
    const kind = params.get('kind');
    if (kind === 'ai_role' || kind === 'provider_op') filters.kind = kind;
    const role = params.get('role');
    if (role) filters.role = role;
    const on = params.get('on');
    if (on && day.test(on)) filters.on = on;
    if (params.get('failed') === '1') filters.failed = true;
    return id !== null ? { screen: 'workitems', tab, id, filters } : { screen: 'workitems', tab, filters };
}

function people(segments: string[], query: string): Route {
    const id = segments[0] ? decode(segments[0]) : null;
    const params = new URLSearchParams(query);
    const filters: PeopleFilters = {};
    const q = params.get('q');
    if (q) filters.q = q;
    const campaign = params.get('campaign');
    if (campaign) filters.campaign = campaign;
    if (params.get('archived') === '1') filters.archived = true;
    if (id === null) return { screen: 'people', tab: 'timeline', filters };
    const tab = segments[1] === 'details' || segments[1] === 'threads' ? segments[1] : 'timeline';
    return { screen: 'people', id, tab, filters };
}

/** The People filters in a fixed key order, as the Work items ones are. */
function peopleQuery(filters: PeopleFilters): string {
    const parts: string[] = [];
    if (filters.q) parts.push(`q=${encodeURIComponent(filters.q)}`);
    if (filters.campaign) parts.push(`campaign=${encodeURIComponent(filters.campaign)}`);
    if (filters.archived) parts.push('archived=1');
    return parts.length > 0 ? `?${parts.join('&')}` : '';
}

/** The filters in a fixed key order, so one view has one address. */
function filterQuery(filters: WorkItemFilters): string {
    const parts: string[] = [];
    if (filters.campaign) parts.push(`campaign=${encodeURIComponent(filters.campaign)}`);
    if (filters.contact) parts.push(`contact=${encodeURIComponent(filters.contact)}`);
    if (filters.kind) parts.push(`kind=${filters.kind}`);
    if (filters.role) parts.push(`role=${encodeURIComponent(filters.role)}`);
    if (filters.on) parts.push(`on=${filters.on}`);
    if (filters.failed) parts.push('failed=1');
    return parts.length > 0 ? `?${parts.join('&')}` : '';
}

/** A malformed percent-escape is no id, not a page that never renders. */
function decode(segment: string): string | null {
    try {
        return decodeURIComponent(segment);
    } catch {
        return null;
    }
}

export function href(route: Route): string {
    if (route.screen === 'system') return '#/system';
    if (route.screen === 'workitems') {
        const tab = route.tab !== 'now' || route.id ? `/${route.tab}` : '';
        const id = route.id ? `/${encodeURIComponent(route.id)}` : '';
        return `#/workitems${tab}${id}${filterQuery(route.filters)}`;
    }
    if (route.screen === 'people') {
        const id = route.id ? `/${encodeURIComponent(route.id)}` : '';
        const tab = route.id && route.tab !== 'timeline' ? `/${route.tab}` : '';
        return `#/people${id}${tab}${peopleQuery(route.filters)}`;
    }
    const decided = route.decided ? '?decided=1' : '';
    return route.kind && route.id
        ? `#/attention/${route.kind}/${encodeURIComponent(route.id)}${decided}`
        : `#/attention${decided}`;
}

export const route = signal<Route>(
    typeof location === 'undefined' ? { screen: 'attention' } : parseRoute(location.hash),
);

export function navigate(to: Route): void {
    route.value = to;
    if (typeof location !== 'undefined') location.hash = href(to);
}

/** Moves without a history entry: a filter changed is the same view, narrowed, not a place Back should return to. */
export function replace(to: Route): void {
    route.value = to;
    if (typeof history !== 'undefined') history.replaceState(null, '', href(to));
}

if (typeof addEventListener !== 'undefined') {
    addEventListener('hashchange', () => {
        route.value = parseRoute(location.hash);
    });
}
