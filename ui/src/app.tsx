import { useEffect, useMemo } from 'preact/hooks';
import { signal } from '@preact/signals';
import { HttpApi, type Api } from './api/client';
import { ApiProvider } from './api/context';
import { session } from './api/session';
import { API_VERSION } from './api/version';
import { route } from './router';
import { PairAgain } from './shell/pair-again';
import { Sidebar } from './shell/sidebar';
import * as shell from './shell/shell';
import { AttentionScreen } from './screens/attention/screen';
import { SystemScreen } from './screens/system/screen';
import { WorkItemsScreen } from './screens/workitems/screen';

const ready = signal(false);

export function App({ api, uiApiVersion = API_VERSION }: { api?: Api; uiApiVersion?: string }) {
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
            <div class="app">
                <Sidebar />
                <div class="content">
                    {runtimeApi && runtimeApi !== uiApiVersion && (
                        <div class="notice danger">
                            This UI was built for API {uiApiVersion}; the runtime speaks {runtimeApi}. Reinstall the UI
                            from a release that matches.
                        </div>
                    )}
                    {route.value.screen === 'system' ? (
                        <SystemScreen />
                    ) : route.value.screen === 'workitems' ? (
                        <WorkItemsScreen />
                    ) : (
                        <AttentionScreen />
                    )}
                </div>
            </div>
        </ApiProvider>
    );
}
