import type { JournalEntry } from '../../api/types';

/** How many of the campaign's latest reply lines the Inbox reads. */
export const inboxLines = 50;

/** How many people's threads the Inbox asks for at most: one read per person and plugin, the newest lines first. */
export const inboxThreadReads = 10;

/**
 * What the Inbox draws for a line: a person, with the plugin the reply was observed through (none when the line names
 * none, and then nothing is read), or a reply the provider tied to nobody Jason knows, drawn as its channel and moment.
 * The moment is the provider's, when the thread last moved; a line that carries none has the moment Jason wrote it.
 */
export type InboxEntry =
    | { kind: 'person'; contactId: string; name: string | null; pluginId: string | null; at: string }
    | { kind: 'untied'; channel: string | null; at: string };

function field(line: JournalEntry, name: string): string | null {
    const value = line.new && typeof line.new === 'object' ? (line.new as Record<string, unknown>)[name] : undefined;
    return typeof value === 'string' && value ? value : null;
}

/** When the thread moved, as the provider said; when Jason wrote the line, for a line that does not say. */
function momentOf(line: JournalEntry): string {
    return field(line, 'last_activity_at') ?? line.ts;
}

/**
 * The entries of the lines in the order the runtime listed them, the order Jason observed the replies in: each person
 * and plugin once, at most `inboxThreadReads` of them, and every line tied to nobody. `left` counts the people and
 * plugins among these lines beyond that bound.
 */
export function inboxEntriesOf(lines: JournalEntry[]): { entries: InboxEntry[]; left: number } {
    const entries: InboxEntry[] = [];
    const seen = new Set<string>();
    let people = 0;
    let left = 0;
    for (const line of lines) {
        if (!line.contact_id) {
            entries.push({ kind: 'untied', channel: field(line, 'channel'), at: momentOf(line) });
            continue;
        }
        const pluginId = field(line, 'plugin');
        const pair = `${line.contact_id}\n${pluginId ?? ''}`;
        if (seen.has(pair)) continue;
        seen.add(pair);
        if (people === inboxThreadReads) {
            left += 1;
            continue;
        }
        people += 1;
        entries.push({
            kind: 'person',
            contactId: line.contact_id,
            name: line.contact_name,
            pluginId,
            at: momentOf(line),
        });
    }
    return { entries, left };
}
