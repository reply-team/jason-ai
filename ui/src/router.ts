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

const day = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;

export type Route =
    | { screen: 'attention'; kind?: 'approvals' | 'questions'; id?: string }
    | { screen: 'system' }
    | { screen: 'workitems'; tab: Tab; id?: string; filters: WorkItemFilters };

const tabs: readonly Tab[] = ['now', 'planned', 'done'];

/** The fragment's path, ignoring anything after `?` (the ticket travels there and never reaches the router). */
export function parseRoute(hash: string): Route {
    const [path = '', query = ''] = hash.replace(/^#/, '').split('?');
    const segments = path.split('/').filter((s) => s.length > 0);
    if (segments[0] === 'system') return { screen: 'system' };
    if (segments[0] === 'workitems') return workItems(segments.slice(1), query);
    if (segments[0] === 'attention' && (segments[1] === 'approvals' || segments[1] === 'questions') && segments[2]) {
        const id = decode(segments[2]);
        if (id !== null) return { screen: 'attention', kind: segments[1], id };
    }
    return { screen: 'attention' };
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
    return route.kind && route.id ? `#/attention/${route.kind}/${encodeURIComponent(route.id)}` : '#/attention';
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
