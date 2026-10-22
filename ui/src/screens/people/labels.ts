import type { Contact, JournalEntry } from '../../api/types';
import { operationLabel } from '../attention/labels';
import { mark } from '../workitems/labels';

/** What a row calls a person: first and last name; the last alone when the record has no first; else their first channel. */
export function displayName(contact: Contact): { name: string; noFirstName: boolean } {
    const first = contact.first_name?.trim() ?? '';
    const last = contact.last_name?.trim() ?? '';
    if (first) return { name: last ? `${first} ${last}` : first, noFirstName: false };
    if (last) return { name: last, noFirstName: true };
    return { name: contact.channels[0]?.value ?? contact.id, noFirstName: true };
}

const channelNames: Record<string, string> = { email: 'Email', linkedin: 'LinkedIn', phone: 'Phone' };

/** A channel as a person calls it: `linkedin` is LinkedIn; one Jason has no word for keeps its own name, capitalised. */
export function channelName(channel: string): string {
    return channelNames[channel.toLowerCase()] ?? capital(channel);
}

/** The campaigns a person is on, by name: excluded ones are not "on" any more. */
export function onCampaigns(contact: Contact): string[] {
    return (contact.campaigns ?? []).filter((c) => c.state !== 'excluded').map((c) => c.campaign_name);
}

export type EntryLabel = {
    /** The row's mark: its class and its glyph, as the Work items rows have them. */
    mark: { kind: string; glyph: string };
    /** The line a timeline row reads, starting with who or what. */
    title: string;
    /** The few words a list row ends with. */
    short: string;
    /** What the line says beside it, when it says more: a failure's message, a reason, a reply's flags. */
    note: string | null;
};

function capital(text: string): string {
    return text.charAt(0).toUpperCase() + text.slice(1);
}

/** Who acted: you, a person by name, a role by its name, or Jason. */
function actorName(entry: JournalEntry, me: string): string {
    const { type, id } = entry.actor;
    if (type === 'human') return id === me ? 'You' : (id ?? 'A person');
    if (type === 'attempt') return entry.actor_role ? capital(entry.actor_role) : 'A role';
    if (type === 'role') return id ? capital(id) : 'A role';
    return 'Jason';
}

/** The work a line is about, as a noun: "the enrollment", or the role's work. */
function workNoun(entry: JournalEntry): string {
    if (entry.work_item_operation === 'campaign.enroll') return 'enrollment';
    if (entry.work_item_operation) return operationLabel(entry.work_item_operation).toLowerCase();
    return entry.work_item_role ? `${entry.work_item_role}'s work` : 'work';
}

function channelWords(channel: unknown): string {
    if (channel === 'email') return 'by email';
    if (channel === 'linkedin') return 'on LinkedIn';
    return typeof channel === 'string' && channel ? `on ${channel}` : '';
}

function field(value: unknown, name: string): unknown {
    return value && typeof value === 'object' ? (value as Record<string, unknown>)[name] : undefined;
}

function list(words: string[]): string {
    if (words.length <= 1) return words.join('');
    return `${words.slice(0, -1).join(', ')} and ${words.at(-1)}`;
}

/**
 * One journal line in words: who did what, in the line's own terms and nothing more. The list row and the timeline row
 * read the same line, so they say the same thing.
 */
export function entryLabel(entry: JournalEntry, me: string): EntryLabel {
    const who = actorName(entry, me);
    const whom = who === 'You' || who === 'A person' ? who.toLowerCase() : who;
    const role = entry.work_item_role ? capital(entry.work_item_role) : null;
    // The work a line is about, as a title leads with it and as a list row's few words end with it: those are all lower
    // case, the role's name included, as the runtime spells it.
    const work = role ?? capital(workNoun(entry));
    const workShort = entry.work_item_role ?? workNoun(entry);
    const plugin = entry.key?.split('/')[1] ?? 'the provider';
    switch (entry.kind) {
        case 'inbound_reply_observed': {
            const how = channelWords(field(entry.new, 'channel'));
            const meeting = field(entry.new, 'has_meeting_intent') === true ? 'mentions a meeting' : null;
            return {
                mark: { kind: 'reply', glyph: '↩' },
                title: `Replied ${how}`.trim(),
                short: `replied ${how}`.trim(),
                note: meeting,
            };
        }
        case 'provider_event_observed': {
            const what = String(field(entry.new, 'kind') ?? 'event');
            const words: Record<string, string> = {
                reply_detected: 'replied',
                invitation_accepted: 'accepted the invitation',
                bounce: 'bounced',
            };
            const said = words[what] ?? what.replaceAll('_', ' ');
            return { mark: { kind: 'reply', glyph: '↩' }, title: capital(said), short: said, note: null };
        }
        case 'workitem_succeeded':
            if (entry.work_item_operation === 'campaign.enroll') {
                const into = entry.campaign_name ? ` in ${entry.campaign_name}` : '';
                return { mark: { kind: 'ok', glyph: '✓' }, title: `Enrolled${into}`, short: 'enrolled', note: null };
            }
            if (entry.work_item_operation === 'optout.record') {
                return { mark: { kind: 'off', glyph: '–' }, title: 'Opted out', short: 'opted out', note: null };
            }
            return role
                ? {
                      mark: { kind: 'ok', glyph: '✓' },
                      title: `${role} finished its work`,
                      short: `${workShort} finished`,
                      note: null,
                  }
                : {
                      mark: { kind: 'ok', glyph: '✓' },
                      title: `${capital(workNoun(entry))} done`,
                      short: `${workNoun(entry)} done`,
                      note: null,
                  };
        case 'workitem_failed': {
            const message = field(entry.new, 'message');
            const what = role ?? capital(workNoun(entry));
            return {
                mark: { kind: 'fail', glyph: '✗' },
                title: `${what} failed`,
                short: `${workShort} failed`,
                note: typeof message === 'string' ? message : null,
            };
        }
        case 'workitem_cancelled':
            return {
                mark: { kind: 'off', glyph: '–' },
                title: `${capital(workNoun(entry))} cancelled`,
                short: `${workNoun(entry)} cancelled`,
                note: entry.reason,
            };
        case 'workitem_expired':
            return {
                mark: { kind: 'off', glyph: '◌' },
                title: `${capital(workNoun(entry))} expired`,
                short: `${workNoun(entry)} expired`,
                note: null,
            };
        case 'external_effect_reported':
            return {
                mark: { kind: 'ok', glyph: '✓' },
                title: `${who} reported what was done by hand`,
                short: 'reported',
                note: entry.reason,
            };
        case 'approval_requested':
            return {
                mark: mark('awaiting_approval'),
                title: `The ${workNoun(entry)} asked for your approval`,
                short: `${workNoun(entry)} waiting for you`,
                note: null,
            };
        case 'approval_superseded':
        case 'approval_cancelled':
            return {
                mark: mark('cancelled'),
                title: `The approval for the ${workNoun(entry)} was withdrawn`,
                short: 'approval withdrawn',
                note: entry.reason,
            };
        case 'decision_cancelled':
            return {
                mark: mark('cancelled'),
                title: 'A question was withdrawn',
                short: 'question withdrawn',
                note: null,
            };
        case 'workitem_created':
            return {
                mark: mark('created'),
                title: `${work} planned`,
                short: `${workShort} planned`,
                note: entry.reason,
            };
        case 'workitem_scheduled':
            return { mark: mark('scheduled'), title: `${work} starting`, short: `${workShort} starting`, note: null };
        case 'workitem_processing':
            return { mark: mark('processing'), title: `${work} started`, short: `${workShort} started`, note: null };
        case 'workitem_awaiting_approval':
            return {
                mark: mark('awaiting_approval'),
                title: `${work} waits for your approval`,
                short: `${workShort} waiting for you`,
                note: null,
            };
        case 'workitem_released':
            return {
                mark: mark('created'),
                title: `${work} will be tried again`,
                short: `${workShort} to try again`,
                note: entry.reason,
            };
        case 'workitem_reopened':
            return {
                mark: mark('created'),
                title: `${work} reopened`,
                short: `${workShort} reopened`,
                note: entry.reason,
            };
        case 'workitem_updated':
        case 'workitem_context_updated':
            return {
                mark: mark('created'),
                title: `${who} changed the ${workNoun(entry)}`,
                short: `${workNoun(entry)} changed`,
                note: entry.reason,
            };
        case 'external_id_pinned':
            return { mark: mark('succeeded'), title: `Linked at ${plugin}`, short: `linked at ${plugin}`, note: null };
        case 'external_id_diverged':
            return {
                mark: mark('cancelled'),
                title: `${capital(plugin)} answered with another id`,
                short: `${plugin} answered another id`,
                note: null,
            };
        case 'approval_approved':
            return {
                mark: { kind: 'ok', glyph: '✓' },
                title: `${who} approved the ${workNoun(entry)}`,
                short: `${workNoun(entry)} approved`,
                note: entry.reason,
            };
        case 'approval_rejected':
            return {
                mark: { kind: 'off', glyph: '–' },
                title: `${who} rejected the ${workNoun(entry)}`,
                short: `${workNoun(entry)} rejected`,
                note: entry.reason,
            };
        case 'decision_raised':
            return {
                mark: { kind: 'ask', glyph: '?' },
                title: `${role ?? who} asked a question`,
                short: 'a question for you',
                note: null,
            };
        case 'decision_answered':
            return {
                mark: { kind: 'ok', glyph: '✓' },
                title: `${who} answered a question`,
                short: 'question answered',
                note: null,
            };
        case 'contact_created':
            return {
                mark: { kind: 'ok', glyph: '✓' },
                title: `Created by ${whom}`,
                short: `created by ${whom}`,
                note: entry.reason,
            };
        case 'contact_updated': {
            const fields = Array.isArray(entry.new) ? entry.new.map(String).map((f) => f.replaceAll('_', ' ')) : [];
            const what = fields.length > 0 ? list(fields) : 'the record';
            return {
                mark: { kind: 'ok', glyph: '✓' },
                title: `${who} updated ${what}`,
                short: `${what} updated`,
                note: entry.reason,
            };
        }
        case 'contact_archived':
            return { mark: { kind: 'off', glyph: '–' }, title: 'Archived', short: 'archived', note: entry.reason };
        case 'campaign_paused':
            return {
                mark: { kind: 'off', glyph: '❚❚' },
                title: `${who} paused it`,
                short: 'paused',
                note: entry.reason,
            };
        case 'campaign_started':
            return {
                mark: { kind: 'ok', glyph: '▶' },
                title: `${who} started it`,
                short: 'started',
                note: entry.reason,
            };
        case 'campaign_updated':
            return {
                mark: { kind: 'off', glyph: '·' },
                title: `${who} changed it`,
                short: 'changed',
                note: entry.reason,
            };
        case 'context_updated':
            return {
                mark: { kind: 'off', glyph: '·' },
                title: `${who} changed its context`,
                short: 'context changed',
                note: entry.reason,
            };
        case 'contacts_added':
        case 'contacts_removed': {
            const many = Number(field(entry.new, entry.kind === 'contacts_added' ? 'added' : 'removed') ?? 0);
            const people = `${many} ${many === 1 ? 'person' : 'people'}`;
            const done = entry.kind === 'contacts_added' ? 'added' : 'removed';
            return {
                mark: {
                    kind: entry.kind === 'contacts_added' ? 'ok' : 'off',
                    glyph: entry.kind === 'contacts_added' ? '+' : '–',
                },
                title: `${who} ${done} ${people}`,
                short: `${people} ${done}`,
                note: entry.reason,
            };
        }
        default: {
            const said = entry.kind.replaceAll('_', ' ');
            return { mark: { kind: 'off', glyph: '·' }, title: capital(said), short: said, note: null };
        }
    }
}
