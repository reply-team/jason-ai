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

/** Whether a focus read is going, so a focus while one is under way asks nothing more. */
let reading = false;

/** Bumped by every patch of the answer the page makes from another operation's answer, so an older read is dropped. */
let patched = 0;

/**
 * Puts the notification channels an operation answered into the page's `system.info`, as `notification.enable` and
 * `notification.disable` answer them. A focus read that began before it and ends after it is dropped.
 */
export function patchNotifications(notifications: SystemInfo['notifications']): void {
    patched += 1;
    if (info.value) info.value = { ...info.value, notifications };
}

/**
 * The window took the focus back: read `system.info` again, so what the page shows of the runtime, its notification
 * channels among it, is the runtime's answer now and not the one the window opened with. One read at a time; the read is
 * the runtime's own report, a loopback call, so no spacing is kept. A read that fails keeps the last answer, so the page
 * keeps what it shows; one a switch's answer overtook is dropped.
 */
export async function reloadOnFocus(api: Api): Promise<void> {
    if (reading) return;
    reading = true;
    const began = patched;
    try {
        const answer = await api.call<SystemInfo>('system.info');
        if (patched === began) info.value = answer;
    } catch (error) {
        if (error instanceof Unpaired) session.clear();
    } finally {
        reading = false;
    }
}
