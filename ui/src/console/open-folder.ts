import { signal } from '@preact/signals-react';
import { t } from '../platform/i18n';
import { mode } from '../platform/mode';
import { can, earlier } from '../platform/places';
import type { SessionHost } from './conversation-items';
import { here, inTab } from './state';

// An agent's folder in the system's file manager, from the agent's "…" and the menu on a row of the sidebar: through Open in's
// own bridge (`openIn.open('files', …)`), which opens the folder the runtime recorded for the agent and never takes a path
// from the page, asked not to keep the file manager as Open in's default, so one agent's item never moves the window's
// button. The place is named as Open in names it: a product's name is the desktop's, and the page's own word
// stands in where the system has none (`open-in.tsx`).
//
// Beside it, an agent host in a terminal at the agent's folder: the same bridge with the host's own place, `claude` or
// `codex` (`openIn.open(host, …, { keep: false })`), which the desktop opens with the bundled `jason session open`, and
// which leaves the window's Open in where it was too. Only a host Open in found here is offered, read by the same read.

/**
 * The file manager's name, as the desktop names it among Open in's places: a fact of this computer, not of a folder, so it
 * is read once by a read of its own (`readFileManager`) and never taken from what Open in read last. Undefined until that
 * read answers, and where it was refused; null where this computer has no file manager.
 */
export const fileManager = signal<string | null | undefined>(undefined);

/**
 * The agent hosts Open in found on this computer, Claude Code before Codex, by the desktop's names for them: read by the
 * same read as the file manager's name. Empty until that read answers, where it was refused, and where none is found.
 */
export const sessionHosts = signal<readonly SessionHost[]>([]);

/**
 * Reads the file manager's name once, as the console is drawn; the function it returns forgets the answer still to come.
 * In a tab, and where the desktop has no Open in, nothing is read.
 */
export function readFileManager(): () => void {
    fileManager.value = undefined;
    sessionHosts.value = [];
    const openIn = earlier(here().desktop).openIn;
    if (inTab() || !can('openIn.targets') || openIn === undefined) return () => undefined;
    let current = true;
    openIn.targets().then(
        (answer) => {
            if (!current || 'refused' in answer) return;
            const files = answer.places.find((each) => each.id === 'files');
            fileManager.value = files === undefined ? null : files.label !== '' ? files.label : t('openIn.place.files');
            sessionHosts.value = answer.places.flatMap((each): SessionHost[] =>
                (each.id === 'claude' || each.id === 'codex') && each.label !== ''
                    ? [{ id: each.id, label: each.label }]
                    : [],
            );
        },
        () => undefined,
    );
    return () => {
        current = false;
    };
}

/** Why the last opening of an agent's folder, or of a host there, was refused, in the desktop's words; null when it was not. */
export const folderRefusal = signal<string | null>(null);

/**
 * The act's word, by the desktop's name for the file manager, and the page's own word for it until the desktop has said
 * one, so the item is there from the first draw; null in a tab, and where this computer has no file manager.
 */
export function folderWord(): string | null {
    if (inTab() || !can('openIn.open')) return null;
    const name = fileManager.value;
    return name === null ? null : t('agent.menu.folder', { place: name ?? t('openIn.place.files') });
}

/** The agent hosts the place offers a session of: none in a tab, in the demo, or where the desktop has no Open in. */
export function hostsOffered(): readonly SessionHost[] {
    if (inTab() || mode.value === 'demo' || !can('openIn.open')) return [];
    return sessionHosts.value;
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

/**
 * Opens a new session of the host in a terminal at the agent's folder, leaving the window's Open in where it was. Nothing
 * is said once the terminal opens; a refusal is said beside the sidebar, as the folder's is.
 */
export async function openSession(campaignId: string, host: SessionHost['id']): Promise<void> {
    const openIn = earlier(here().desktop).openIn;
    if (!can('openIn.open') || openIn === undefined) return;
    folderRefusal.value = null;
    folderRefusal.value = await openIn.open(host, campaignId, { keep: false }).then(
        (answer) => (answer === null ? null : answer.refused),
        (error: unknown) => (error instanceof Error ? error.message : String(error)),
    );
}
