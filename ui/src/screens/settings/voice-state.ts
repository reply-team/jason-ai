import { signal } from '@preact/signals-react';

/**
 * What the runtime said of the two preferences, kept for the whole page: not read yet, neither set, one set, or
 * unreadable. The start screen reads it; a save in Settings ends it, so the note about them goes in the same session.
 */
export const voiceKnown = signal<'unread' | 'none' | 'some' | 'failed'>('unread');
