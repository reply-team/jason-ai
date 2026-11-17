import { Painted } from '../../console/painted';
import { Toolbar } from '../../layout/toolbar';
import { useProviderAccounts } from './provider-card';

export function AccountsScreen() {
    const provider = useProviderAccounts();
    return (
        <div className="main single">
            <div className="list" style={{ borderRight: 0 }}>
                <Toolbar title="Accounts" />
                <div className="sys">
                    {provider ? (
                        <section className="wide provider">
                            <Painted className="painted" view={provider.view} onGone={provider.gone} />
                        </section>
                    ) : (
                        <section className="card wide">
                            <h2>Provider</h2>
                            <p className="empty">
                                Connecting, reconnecting, pausing and removing a provider’s accounts are done in Jason
                                Desktop; a browser cannot do them.
                            </p>
                        </section>
                    )}
                </div>
            </div>
        </div>
    );
}
