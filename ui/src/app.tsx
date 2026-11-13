import { useEffect, useMemo } from 'react';
import { signal } from '@preact/signals-react';
import { HttpApi, type Api } from './api/client';
import { ApiProvider } from './api/context';
import { session } from './api/session';
import { API_VERSION } from './api/version';
import { t } from './platform/i18n';
import { registry } from './platform/slots';
import { route } from './router';
import { ModeStrip } from './shell/mode-strip';
import { PairAgain } from './shell/pair-again';
import { Sidebar } from './shell/sidebar';
import * as shell from './shell/shell';

const ready = signal(false);

/** The screen the address names, among the screens registered; Attention when it names none of them. */
export function CurrentPage() {
    const screens = registry.list('screen');
    const Page = (
        screens.find((each) => each.id === route.value.screen) ?? screens.find((each) => each.id === 'attention')
    )?.component;
    return Page ? <Page /> : null;
}

/**
 * The harness: paired, its screens and the version notice. `chromeless` leaves its sidebar out, where a shell around it
 * has a navigation of its own.
 */
export function App({
    api,
    uiApiVersion = API_VERSION,
    chromeless = false,
}: {
    api?: Api;
    uiApiVersion?: string;
    chromeless?: boolean;
}) {
    const client = useMemo(() => api ?? new HttpApi(session), [api]);
    useEffect(() => {
        ready.value = false;
        void session.bootstrap().finally(() => {
            ready.value = true;
        });
    }, []);
    useEffect(() => {
        if (ready.value && session.paired.value) void shell.load(client);
    }, [ready.value, session.paired.value, client]);

    if (!ready.value) return null;
    if (!session.paired.value) return <PairAgain />;
    const runtimeApi = shell.info.value?.api_version;
    return (
        <ApiProvider api={client}>
            {/* In Demo the strip heads every screen; a shell around a chromeless harness draws its own. */}
            {!chromeless && <ModeStrip />}
            <div className={chromeless ? 'app chromeless' : 'app'}>
                {!chromeless && <Sidebar />}
                <div className="content">
                    {runtimeApi && runtimeApi !== uiApiVersion && (
                        <div className="notice danger">
                            {t('shell.version', { ui: uiApiVersion, runtime: runtimeApi })}
                        </div>
                    )}
                    <CurrentPage />
                </div>
            </div>
        </ApiProvider>
    );
}
