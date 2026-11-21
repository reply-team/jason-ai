import { useState } from 'react';
import { ApiError, Unpaired } from '../../api/client';
import { useApi } from '../../api/context';
import type { NotificationChannel, NotificationsInfo } from '../../api/types';
import { ScrollBox } from '../../layout/scroll-box';
import { t } from '../../platform/i18n';
import { mode } from '../../platform/mode';
import { channelName, notReadyWords, outcomeTone, outcomeWords } from './notifications-words';
import { useProviderAccounts } from '../accounts/provider-card';
import { relative } from '../../time/format';
import * as shell from '../../shell/shell';

/**
 * The sentence the runtime refuses a channel's switch with while every notification is held off as a whole
 * (`notifications_off`): the one line of the settings file to change. The page says it before anybody presses a switch.
 */
export const allOffWords =
    'Every notification is off: Notifications:Enabled is false in the settings file, and no channel is turned on while it is. Take that line out of the settings file to turn notifications back on as a whole.';

/**
 * "Notifications" in Settings: what `system.info` (the answer the page already holds) says of the channels — whether
 * notifications are on, and for each channel whether it is on and what it did with the last notification — and a switch
 * per channel, which asks the runtime to turn that one channel on or off (`notification.enable`, `notification.disable`)
 * and shows the channels as its answer says them. There is no switch for every channel at once, and none for what the
 * operating system itself holds off: that stays words. A switch is made in the paired person's name, as every write of the
 * page is. A switch is inert in the Demo, while every notification is off, and on a plugin channel that is off and could
 * not deliver; a plugin channel that could not deliver says what is missing, whether it is off or on, and one that is on
 * can still be turned off. A refused switch shows the runtime's sentence and stays where it was.
 */
export function NotificationsSection() {
    const api = useApi();
    const provider = useProviderAccounts();
    const [busy, setBusy] = useState<string | null>(null);
    const [refused, setRefused] = useState<{ channel: string; text: string } | null>(null);
    const now = new Date();
    const notifications = shell.info.value?.notifications ?? null;
    const demo = mode.value === 'demo';

    async function flip(channel: NotificationChannel) {
        setBusy(channel.channel);
        setRefused(null);
        try {
            const answer = await api.call<NotificationsInfo>(
                channel.on ? 'notification.disable' : 'notification.enable',
                { channel: channel.channel },
                'write',
            );
            shell.patchNotifications(answer);
        } catch (error) {
            if (!(error instanceof Unpaired))
                setRefused({
                    channel: channel.channel,
                    text: error instanceof ApiError ? error.message : String(error),
                });
        } finally {
            setBusy(null);
        }
    }

    return (
        <section data-section="notifications">
            <h2>
                {t('settings.notifications')}{' '}
                {notifications && (
                    <span className={`badge ${notifications.enabled ? 'success' : ''}`}>
                        {notifications.enabled ? 'on' : 'off'}
                    </span>
                )}
            </h2>
            {notifications === null ? (
                <p className="empty">
                    {shell.info.value
                        ? 'This runtime does not report its notification channels.'
                        : 'Reading the notification channels…'}
                </p>
            ) : notifications.channels.length === 0 ? (
                <p className="empty">No channel is set up, so Jason sends you no notifications.</p>
            ) : (
                <ScrollBox label="Notification channels">
                    <table className="tbl notif-channels">
                        <thead>
                            <tr>
                                <th>Channel</th>
                                <th>State</th>
                                <th>Last notification</th>
                            </tr>
                        </thead>
                        <tbody>
                            {notifications.channels.map((channel) => {
                                const missing = notReadyWords(channel);
                                const inert =
                                    demo ||
                                    busy !== null ||
                                    !notifications.enabled ||
                                    (!channel.on && missing !== null);
                                return (
                                    <tr key={channel.channel}>
                                        <td>
                                            <strong>{channelName(channel)}</strong>
                                        </td>
                                        <td>
                                            <button
                                                type="button"
                                                role="switch"
                                                aria-checked={channel.on}
                                                aria-label={channelName(channel)}
                                                className={`badge switch ${outcomeTone(channel)}`}
                                                disabled={inert}
                                                onClick={() => void flip(channel)}
                                            >
                                                {channel.on ? 'on' : 'off'}
                                            </button>
                                            {missing && <span className="missing"> {missing}</span>}
                                        </td>
                                        <td className={channel.outcome === 'failed' ? 'failed' : undefined}>
                                            {outcomeWords(channel, now)}
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </ScrollBox>
            )}
            {refused && (
                <p className="hint failed" role="alert">
                    {refused.text}
                </p>
            )}
            {notifications && !notifications.enabled && <p className="hint">{allOffWords}</p>}
            {demo && notifications && <p className="hint">This is the Demo: its switches change nothing.</p>}
            {notifications?.last_at && (
                <p className="hint">The last notification was sent {relative(new Date(notifications.last_at), now)}.</p>
            )}
            <p className="hint">
                This is how Jason tells you that an approval or a question is waiting for you. Turn one channel on or
                off with its switch, or with jason notification enable or disable
                {provider && '; a provider’s channel is connected or disconnected on the Connect panel'}. A switch is
                made in your name. Every notification at once is switched in the settings file.
            </p>
        </section>
    );
}
