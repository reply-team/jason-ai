import { signal } from '@preact/signals-react';
import { Unpaired, type Api } from '../api/client';
import { session } from '../api/session';
import type { SystemInfo } from '../api/types';

export const info = signal<SystemInfo | null>(null);

export async function load(api: Api): Promise<void> {
    try {
        info.value = await api.call<SystemInfo>('system.info');
    } catch (error) {
        if (error instanceof Unpaired) session.clear();
        else info.value = null;
    }
}

/** Whether a read is going, so a focus while one is under way asks nothing more. */
let reading = false;

/**
 * The window took the focus back: read `system.info` again, so what the page shows of the runtime, its notification
 * channels among it, is the runtime's answer now and not the one the window opened with. One read at a time; the read is
 * the runtime's own report, a loopback call, so no spacing is kept.
 */
export async function reloadOnFocus(api: Api): Promise<void> {
    if (reading) return;
    reading = true;
    try {
        await load(api);
    } finally {
        reading = false;
    }
}
