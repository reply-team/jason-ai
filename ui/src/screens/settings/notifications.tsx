import { ScrollBox } from '../../layout/scroll-box';
import { t } from '../../platform/i18n';
import { channelName, outcomeTone, outcomeWords } from './notifications-words';
import { useProviderAccounts } from '../accounts/provider-card';
import { relative } from '../../time/format';
import * as shell from '../../shell/shell';

/**
 * "Notifications" in Settings: read only, what `system.info` (the answer the page already holds) says of the channels: whether notifications are on, and for
 * each channel whether it is on and what it did with the last notification. Nothing is written and nothing is kept.
 */
export function NotificationsSection() {
    const provider = useProviderAccounts();
    const now = new Date();
    const notifications = shell.info.value?.notifications ?? null;
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
                            {notifications.channels.map((channel) => (
                                <tr key={channel.channel}>
                                    <td>
                                        <strong>{channelName(channel)}</strong>
                                    </td>
                                    <td>
                                        <span className={`badge ${outcomeTone(channel)}`}>
                                            {channel.on ? 'on' : 'off'}
                                        </span>
                                    </td>
                                    <td className={channel.outcome === 'failed' ? 'failed' : undefined}>
                                        {outcomeWords(channel, now)}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </ScrollBox>
            )}
            {notifications?.last_at && (
                <p className="hint">The last notification was sent {relative(new Date(notifications.last_at), now)}.</p>
            )}
            <p className="hint">
                This is how Jason tells you that an approval or a question is waiting for you. To turn a channel on or
                off, ask your AI
                {provider && '; a provider’s channel can also be connected or disconnected on the Connect panel'}.
            </p>
        </section>
    );
}
