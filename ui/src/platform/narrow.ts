import { useSyncExternalStore } from 'react';

const query = '(max-width: 520px)';

const subscribe = (notify: () => void): (() => void) => {
    if (typeof matchMedia !== 'function') return () => undefined;
    const list = matchMedia(query);
    list.addEventListener('change', notify);
    return () => list.removeEventListener('change', notify);
};

const now = (): boolean => typeof matchMedia === 'function' && matchMedia(query).matches;

/** Whether the page is as narrow as a phone's: where a long line of words has no room to be read whole. */
export const useNarrow = (): boolean => useSyncExternalStore(subscribe, now, () => false);

/** Whether the page is at most this many pixels wide right now (a paint reads it; it is not a subscription). */
export const narrowerThan = (px: number): boolean =>
    typeof matchMedia === 'function' && matchMedia(`(max-width: ${px}px)`).matches;
