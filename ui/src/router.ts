import { signal } from '@preact/signals';

export type Route = { screen: 'attention'; kind?: 'approvals' | 'questions'; id?: string } | { screen: 'system' };

/** The fragment's path, ignoring anything after `?` (the ticket travels there and never reaches the router). */
export function parseRoute(hash: string): Route {
    const path = hash.replace(/^#/, '').split('?')[0] ?? '';
    const segments = path.split('/').filter((s) => s.length > 0);
    if (segments[0] === 'system') return { screen: 'system' };
    if (segments[0] === 'attention' && (segments[1] === 'approvals' || segments[1] === 'questions') && segments[2]) {
        const id = decode(segments[2]);
        if (id !== null) return { screen: 'attention', kind: segments[1], id };
    }
    return { screen: 'attention' };
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
    return route.kind && route.id ? `#/attention/${route.kind}/${encodeURIComponent(route.id)}` : '#/attention';
}

export const route = signal<Route>(
    typeof location === 'undefined' ? { screen: 'attention' } : parseRoute(location.hash),
);

export function navigate(to: Route): void {
    route.value = to;
    if (typeof location !== 'undefined') location.hash = href(to);
}

if (typeof addEventListener !== 'undefined') {
    addEventListener('hashchange', () => {
        route.value = parseRoute(location.hash);
    });
}
