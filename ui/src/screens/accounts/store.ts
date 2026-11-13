import { signal } from '@preact/signals-react';
import { ApiError, Unpaired, type Api } from '../../api/client';
import { session } from '../../api/session';
import type { SystemInfo } from '../../api/types';

export const info = signal<SystemInfo | null>(null);
export const failure = signal<string | null>(null);
export const refreshing = signal(false);

/** The channels are read from `system.info`, the runtime's report of itself; nothing is written back. */
export async function load(api: Api): Promise<void> {
    failure.value = null;
    try {
        info.value = await api.call<SystemInfo>('system.info');
    } catch (error) {
        if (error instanceof Unpaired) session.clear();
        else failure.value = error instanceof ApiError ? error.message : String(error);
    }
}

/** Read again, the button turning for at least half a second. */
export async function refresh(api: Api): Promise<void> {
    refreshing.value = true;
    try {
        await Promise.all([load(api), new Promise((resolve) => setTimeout(resolve, 500))]);
    } finally {
        refreshing.value = false;
    }
}
