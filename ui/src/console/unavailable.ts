import { signal } from '@preact/signals-react';
import type { Api } from '../api/client';
import { routedPlugins, unavailableRouted, type ListedPlugin, type UnavailablePlugin } from './unavailable-model';

// The plugins a route names that the runtime holds back, for the mode strip: read when the console starts, again on
// every `plugins_reloaded` line, and again whenever the journal's stream opens after it was closed — a runtime that
// restarted loads its plugins before any stream that resumes could hear it — so the strip says so while the cause stands and stops by itself once the plugin is
// available again. Nothing dismisses it by hand. A read that fails says nothing: the strip is no place for a doubt.

/** What the last read found: none while nothing is unavailable, or nothing was read. */
export const unavailablePlugins = signal<readonly UnavailablePlugin[]>([]);

/** The reads in order: an answer to an earlier one, come late, never overwrites a later one's. */
let asked = 0;

/** Whether the last read answered: one that did not is made again when the stream first opens. */
let answered = false;

/** Whether the last read was answered. */
export const unavailableRead = (): boolean => answered;

/** Reads the routes and the plugins together, and keeps what a route names that is unavailable. */
export async function readUnavailable(api: Api): Promise<void> {
    const mine = ++asked;
    try {
        const [routes, plugins] = await Promise.all([
            api.call<unknown>('route.list'),
            api.call<unknown>('plugin.list'),
        ]);
        if (mine !== asked) return;
        answered = true;
        const list = (plugins as { plugins?: unknown }).plugins;
        unavailablePlugins.value = unavailableRouted(
            Array.isArray(list) ? (list as ListedPlugin[]) : [],
            routedPlugins(routes),
        );
    } catch {
        if (mine !== asked) return;
        answered = false;
        unavailablePlugins.value = [];
    }
}

/** Forgets what was read, for a console started afresh. */
export function resetUnavailable(): void {
    asked++;
    answered = false;
    unavailablePlugins.value = [];
}
