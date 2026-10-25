import { signal } from '@preact/signals';
import { ApiError, Unpaired, type Api } from '../../api/client';
import { session } from '../../api/session';
import type { Plugin, Routes, SystemInfo, UiTokens } from '../../api/types';

export const info = signal<SystemInfo | null>(null);
export const plugins = signal<Plugin[]>([]);
export const routes = signal<Routes | null>(null);
export const tokens = signal<UiTokens | null>(null);
export const failure = signal<string | null>(null);
export const refreshing = signal(false);

function fail(error: unknown): void {
    if (error instanceof Unpaired) session.clear();
    else failure.value = error instanceof ApiError ? error.message : String(error);
}

/** plugin.list answers a page of plugins under `plugins`; the exact shape is the runtime's and only the list is read. */
function pluginsOf(answer: unknown): Plugin[] {
    const list = (answer as { plugins?: unknown }).plugins;
    return Array.isArray(list) ? (list as Plugin[]) : [];
}

export async function load(api: Api): Promise<void> {
    failure.value = null;
    try {
        const [i, p, r, t] = await Promise.all([
            api.call<SystemInfo>('system.info'),
            api.call<unknown>('plugin.list'),
            api.call<Routes>('route.list'),
            api.call<UiTokens>('ui.tokens'),
        ]);
        info.value = i;
        plugins.value = pluginsOf(p);
        routes.value = r;
        tokens.value = t;
    } catch (error) {
        fail(error);
    }
}

/** Everything read again, the button turning for at least half a second. */
export async function refresh(api: Api): Promise<void> {
    refreshing.value = true;
    try {
        await Promise.all([load(api), new Promise((resolve) => setTimeout(resolve, 500))]);
    } finally {
        refreshing.value = false;
    }
}

/** Every paired browser but this one, one revoke each; then the list as the runtime now has it. */
export async function revokeOthers(api: Api, mine: string | null): Promise<void> {
    try {
        for (const token of (tokens.value?.items ?? []).filter((t) => t.id !== mine)) {
            await api.call('ui.revoke', { token_id: token.id });
        }
        tokens.value = await api.call<UiTokens>('ui.tokens');
    } catch (error) {
        fail(error);
    }
}

export async function revoke(api: Api, tokenId: string): Promise<void> {
    try {
        await api.call('ui.revoke', { token_id: tokenId });
        tokens.value = await api.call<UiTokens>('ui.tokens');
    } catch (error) {
        fail(error);
    }
}
