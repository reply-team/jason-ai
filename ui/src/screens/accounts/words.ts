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

const silenced: Readonly<Record<string, string>> = {
    disabled_for_application: 'Windows did not show it: Jason is switched off in its notification settings.',
    disabled_for_user: 'Windows did not show it: notifications are switched off for every app.',
    disabled_by_group_policy: 'Windows did not show it: a group policy switches notifications off.',
    disabled_by_manifest: 'Windows did not show it: the application’s manifest switches them off.',
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
        case 'silenced':
            return (channel.reason && silenced[channel.reason]) || `Not shown${when}.`;
        case 'not_attempted':
            return (channel.reason && notAttempted[channel.reason]) || `Not attempted${when}.`;
        default:
            return `${channel.outcome}${when}.`;
    }
}

/** The pill's tone for an outcome: red for a failure, amber for a channel that could not reach anybody. */
export function outcomeTone(channel: NotificationChannel): 'success' | 'danger' | '' {
    if (!channel.on) return '';
    if (channel.outcome === 'delivered') return 'success';
    if (channel.outcome === 'failed') return 'danger';
    return '';
}
