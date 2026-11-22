// A plugin a route names that the runtime holds back: the facts the mode strip says while the cause stands. Pure, over
// what `route.list` and `plugin.list` answer; the console reads them (`unavailable.ts`).

/** A plugin as `plugin.list` lists it, read loosely: its id, its manifest's name, its state and its problems. */
export interface ListedPlugin {
    id: string;
    name?: string | null;
    status: string;
    problems?: readonly { code: string; message: string }[];
}

/** One unavailable plugin a route names: how the Accounts card names it, and the runtime's own problem text. */
export interface UnavailablePlugin {
    id: string;
    label: string;
    problem: string;
}

/** The plugin as the Accounts card names it: its manifest's name and its id, or its id alone where it gives none. */
export const pluginLabel = (id: string, name: string | null | undefined): string =>
    typeof name === 'string' && name !== '' ? `${name} (${id})` : id;

/** The plugin ids one level of routes names: a default, the operations' own routes, and their channels' routes. */
function namedIn(level: unknown, into: Set<string>): void {
    if (typeof level !== 'object' || level === null) return;
    const {
        default: fallback,
        operations,
        channels,
    } = level as {
        default?: unknown;
        operations?: unknown;
        channels?: unknown;
    };
    const take = (target: unknown) => {
        const id = (target as { plugin_id?: unknown } | null)?.plugin_id;
        if (typeof id === 'string' && id !== '') into.add(id);
    };
    take(fallback);
    if (typeof operations === 'object' && operations !== null) Object.values(operations).forEach(take);
    if (typeof channels === 'object' && channels !== null)
        for (const byChannel of Object.values(channels))
            if (typeof byChannel === 'object' && byChannel !== null) Object.values(byChannel).forEach(take);
}

/**
 * Every plugin a route names, as `route.list` answers: the global default, operation and channel routes, and every
 * campaign's own. A plugin no route names is not among them.
 */
export function routedPlugins(routes: unknown): Set<string> {
    const named = new Set<string>();
    if (typeof routes !== 'object' || routes === null) return named;
    const { global, campaigns } = routes as { global?: unknown; campaigns?: unknown };
    namedIn(global, named);
    if (Array.isArray(campaigns)) for (const campaign of campaigns) namedIn(campaign, named);
    return named;
}

/** The plugins a route names that the runtime lists `unavailable`, in its order, each with its problems' words. */
export function unavailableRouted(plugins: readonly ListedPlugin[], routed: ReadonlySet<string>): UnavailablePlugin[] {
    return plugins
        .filter((plugin) => plugin.status === 'unavailable' && routed.has(plugin.id))
        .map((plugin) => ({
            id: plugin.id,
            label: pluginLabel(plugin.id, plugin.name),
            problem: (plugin.problems ?? []).map((each) => each.message).join(' '),
        }));
}
