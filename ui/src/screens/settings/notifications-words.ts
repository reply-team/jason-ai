import type { NotificationChannel } from '../../api/types';
import { relative } from '../../time/format';

/** A channel's name for a person: the toast, or the plugin's id as the runtime reports it. */
export const channelName = (channel: NotificationChannel): string =>
    channel.kind === 'toast' ? 'Desktop toast' : channel.channel;

const notAttempted: Readonly<Record<string, string>> = {
    off: 'Switched off.',
    no_desktop: 'This machine has no desktop to show it on.',
    not_declared: 'The plugin does not declare notifications.',
    unavailable: 'The plugin is unavailable.',
    binding_invalid: 'The channel’s binding is not valid.',
};

/**
 * What Windows answered when the last toast was sent, which is the only time the runtime asks it: said as what happened
 * then, never as the switch now, since the person may have changed it since.
 */
const silenced: Readonly<Record<string, string>> = {
    disabled_for_application: 'Windows answered that Jason was switched off in its notification settings.',
    disabled_for_user: 'Windows answered that notifications were switched off for every app.',
    disabled_by_group_policy: 'Windows answered that a group policy switched notifications off.',
    disabled_by_manifest: 'Windows answered that the application’s manifest switched them off.',
};

/** What a channel did with the last notification, in words, from the runtime's outcome, reason, code and class. */
export function outcomeWords(channel: NotificationChannel, now: Date): string {
    if (channel.outcome === null) {
        return channel.presence === 'no_desktop' ? notAttempted['no_desktop']! : 'Nothing has been sent yet.';
    }
    const when = channel.at ? ` ${relative(new Date(channel.at), now)}` : '';
    switch (channel.outcome) {
        case 'delivered':
            return `Delivered${when}.`;
        case 'failed': {
            const why = [channel.code, channel.class].filter((each) => each).join(', ');
            return `Failed${when}${why ? ` (${why})` : ''}.`;
        }
        case 'silenced': {
            const answered = channel.reason ? silenced[channel.reason] : undefined;
            return answered
                ? `Not shown${when}. At that notification, ${answered.charAt(0).toLowerCase()}${answered.slice(1)} Windows is asked again at the next notification.`
                : `Not shown${when}.`;
        }
        case 'not_attempted':
            return (channel.reason && notAttempted[channel.reason]) || `Not attempted${when}.`;
        default:
            return `${channel.outcome}${when}.`;
    }
}

const notReady: Readonly<Record<string, string>> = {
    unavailable: 'the plugin is unavailable',
    not_declared: 'the plugin does not declare notifications',
    binding_invalid: 'its binding is not valid',
};

/**
 * What a plugin channel is missing, from what the runtime reads of it now: for one that is off, why its switch cannot turn
 * it on; for one that is on, why it cannot deliver. Null for the toast, for a channel that could deliver, and from a
 * runtime that does not say.
 */
export function notReadyWords(channel: NotificationChannel): string | null {
    if (channel.kind === 'toast' || !channel.readiness || channel.readiness === 'ready') return null;
    const why = notReady[channel.readiness] ?? channel.readiness;
    const lead = channel.on ? 'Cannot deliver' : 'Cannot be turned on';
    return `${lead}: ${why}${channel.readiness_detail ? ` (${channel.readiness_detail})` : ''}.`;
}

/** The pill's tone for an outcome: green for a delivery, red for a failure, none for the rest. */
export function outcomeTone(channel: NotificationChannel): 'success' | 'danger' | '' {
    if (!channel.on) return '';
    if (channel.outcome === 'delivered') return 'success';
    if (channel.outcome === 'failed') return 'danger';
    return '';
}
