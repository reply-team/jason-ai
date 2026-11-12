import { useEffect } from 'react';
import { useApi } from '../../api/context';
import { Painted } from '../../console/painted';
import { Notice } from '../../layout/notice';
import { Toolbar } from '../../layout/toolbar';
import * as store from './store';
import { useProviderAccounts } from './provider-card';
import { channelName, outcomeTone, outcomeWords } from './words';
import { relative } from '../../time/format';

export function AccountsScreen() {
    const api = useApi();
    const provider = useProviderAccounts();
    useEffect(() => {
        void store.load(api);
    }, [api]);
    const now = new Date();
    const notifications = store.info.value?.notifications ?? null;
    return (
        <div className="main single">
            <div className="list" style={{ borderRight: 0 }}>
                <Toolbar
                    title="Accounts"
                    refresh={{ busy: store.refreshing.value, onRefresh: () => void store.refresh(api) }}
                />
                {store.failure.value && <Notice danger>{store.failure.value}</Notice>}
                <div className="sys">
                    <section className="card wide">
                        <h2>Provider</h2>
                        {provider ? (
                            <Painted className="painted" view={provider.view} onGone={provider.gone} />
                        ) : (
                            <p className="empty">
                                Connecting, reconnecting, pausing and removing a provider’s accounts are done in Jason
                                Desktop; a browser cannot do them.
                            </p>
                        )}
                    </section>

                    <section className="card wide">
                        <h2>
                            Notifications{' '}
                            {notifications && (
                                <span className={`badge ${notifications.enabled ? 'success' : ''}`}>
                                    {notifications.enabled ? 'on' : 'off'}
                                </span>
                            )}
                        </h2>
                        {notifications === null ? (
                            <p className="empty">
                                {store.info.value
                                    ? 'This runtime does not report its notification channels.'
                                    : 'Reading the notification channels…'}
                            </p>
                        ) : notifications.channels.length === 0 ? (
                            <p className="empty">No channel is set up, so Jason tells you nothing outside this page.</p>
                        ) : (
                            <table className="tbl">
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
                                            <td>{outcomeWords(channel, now)}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        )}
                        {notifications?.last_at && (
                            <p className="hint">
                                The last notification was sent {relative(new Date(notifications.last_at), now)}.
                            </p>
                        )}
                        <p className="hint">
                            These are changed under <code>Notifications:*</code> in the installation’s settings file, or
                            by asking your AI, whose operating-the-installation skill covers them; this page only shows
                            them.
                        </p>
                    </section>
                </div>
            </div>
        </div>
    );
}
