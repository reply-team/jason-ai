import { signal } from '@preact/signals-react';
import { can, earlier } from '../platform/places';
import { here, inTab } from './state';

// An agent's folder in the system's file manager, from the agent's "…" and the menu on a row of Recent: through Open in's
// own bridge (`openIn.open('files', …)`), which opens the folder the runtime recorded for the agent and never takes a path
// from the page, asked not to keep the file manager as Open in's default, so one agent's item never moves the window's
// button. The place is named as Open in names it: a product's name is the desktop's, and the page's own word
// stands in where the system has none (`open-in.tsx`).

/**
 * The file manager's name as Open in last drew it: undefined until Open in has read its places, null where this computer
 * offers no file manager.
 */
export const fileManager = signal<string | null | undefined>(undefined);

/** Why the last opening of an agent's folder was refused, in the desktop's words; null when it was not. */
export const folderRefusal = signal<string | null>(null);

/** The act's word, by the name Open in gave the file manager; null in a tab, and where none is offered or known yet. */
export function folderWord(): string | null {
    if (inTab() || !can('openIn.open')) return null;
    const name = fileManager.value;
    return typeof name === 'string' ? `Open in ${name}` : null;
}

/** Opens the agent's folder in the file manager; a refusal is said beside the sidebar until closed or the next opening. */
export async function openFolder(campaignId: string): Promise<void> {
    const openIn = earlier(here().desktop).openIn;
    if (!can('openIn.open') || openIn === undefined) return;
    folderRefusal.value = null;
    folderRefusal.value = await openIn.open('files', campaignId, { keep: false }).then(
        (answer) => (answer === null ? null : answer.refused),
        (error: unknown) => (error instanceof Error ? error.message : String(error)),
    );
}
