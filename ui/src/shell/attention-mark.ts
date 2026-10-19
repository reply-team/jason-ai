import { signal } from '@preact/signals';

/** Set by the Attention store when its first page of pending items is not empty; the sidebar shows a dot. */
export const pendingMark = signal(false);
