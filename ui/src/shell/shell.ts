import { signal } from '@preact/signals';
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
