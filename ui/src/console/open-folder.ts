import { signal } from '@preact/signals-react';
import { t } from '../platform/i18n';
import { can, earlier } from '../platform/places';
import { here, inTab } from './state';

// An agent's folder in the system's file manager, from the agent's "…" and the menu on a row of Recent: through Open in's
// own bridge (`openIn.open('files', …)`), which opens the folder the runtime recorded for the agent and never takes a path
// from the page, asked not to keep the file manager as Open in's default, so one agent's item never moves the window's
// button. The place is named as Open in names it: a product's name is the desktop's, and the page's own word
// stands in where the system has none (`open-in.tsx`).

/**
 * The file manager's name, as the desktop names it among Open in's places: a fact of this computer, not of a folder, so it
 * is read once by a read of its own (`readFileManager`) and never taken from what Open in read last. Undefined until that
 * read answers, and where it was refused; null where this computer has no file manager.
 */
export const fileManager = signal<string | null | undefined>(undefined);

/**
 * Reads the file manager's name once, as the console is drawn; the function it returns forgets the answer still to come.
 * In a tab, and where the desktop has no Open in, nothing is read.
 */
export function readFileManager(): () => void {
    fileManager.value = undefined;
    const openIn = earlier(here().desktop).openIn;
    if (inTab() || !can('openIn.targets') || openIn === undefined) return () => undefined;
    let current = true;
    openIn.targets().then(
        (answer) => {
            if (!current || 'refused' in answer) return;
            const files = answer.places.find((each) => each.id === 'files');
            fileManager.value = files === undefined ? null : files.label !== '' ? files.label : t('openIn.place.files');
        },
        () => undefined,
    );
    return () => {
        current = false;
    };
}

/** Why the last opening of an agent's folder was refused, in the desktop's words; null when it was not. */
export const folderRefusal = signal<string | null>(null);

/**
 * The act's word, by the desktop's name for the file manager, and the page's own word for it until the desktop has said
 * one, so the item is there from the first draw; null in a tab, and where this computer has no file manager.
 */
export function folderWord(): string | null {
    if (inTab() || !can('openIn.open')) return null;
    const name = fileManager.value;
    return name === null ? null : `Open in ${name ?? t('openIn.place.files')}`;
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
