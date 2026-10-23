import { useEffect } from 'preact/hooks';
import { useApi } from '../../api/context';
import { Segmented } from '../../layout/segmented';
import { Toolbar } from '../../layout/toolbar';
import { href, navigate, replace, route, type Tab } from '../../router';
import { Filters } from './filters';
import { WorkItemList } from './list';
import { WorkItemPanel } from './panel';
import * as store from './store';
import { Notice } from '../../layout/notice';

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
const titles: Record<Tab, string> = { now: 'Now', planned: 'Planned', done: 'Done' };
const empty: Record<Tab, string> = {
    now: 'Nothing is running or waiting.',
    planned: 'Nothing is planned.',
    done: 'Nothing has finished yet.',
};

export function WorkItemsScreen() {
    const api = useApi();
    const current = route.value;
    const view: store.View =
        current.screen === 'workitems' ? { tab: current.tab, filters: current.filters } : { tab: 'now', filters: {} };
    const selected = current.screen === 'workitems' ? (current.id ?? null) : null;
    const key = href({ screen: 'workitems', ...view });

    useEffect(() => {
        store.notice.value = null;
        void store.loadSources(api);
    }, [api]);

    useEffect(() => {
        void store.load(api, view);
    }, [api, key]);

    // A campaign the address names is read once for its name, and dropped if the runtime no longer knows it.
    useEffect(() => {
        const id = view.filters.campaign;
        if (!id) return;
        void store.resolveCampaign(api, id).then((known) => {
            if (known) return;
            const rest = { ...view.filters };
            delete rest.campaign;
            store.notice.value = 'The campaign in this address no longer exists; its filter was dropped.';
            replace({ screen: 'workitems', tab: view.tab, filters: rest });
        });
    }, [api, view.filters.campaign]);

    // A person the address names is read once for their name, and dropped if the runtime no longer knows them.
    useEffect(() => {
        const id = view.filters.contact;
        if (!id) return;
        void store.resolvePerson(api, id).then((known) => {
            if (known) return;
            const rest = { ...view.filters };
            delete rest.contact;
            store.notice.value = 'The person in this address is not known any more; their filter was dropped.';
            replace({ screen: 'workitems', tab: view.tab, filters: rest });
        });
    }, [api, view.filters.contact]);

    useEffect(() => {
        if (selected) void store.openItem(api, selected);
        else store.closeItem();
    }, [api, selected]);

    const close = () => navigate({ screen: 'workitems', ...view });
    const detail = store.detail.value;
    const now = new Date();
    // The failures chip narrows Done only; carried in the address onto another tab, it narrows nothing there.
    const filtered = Object.keys(view.filters).some(
        (k) => (k !== 'failed' || view.tab === 'done') && (k !== 'on' || view.tab !== 'now'),
    );
    const items = store.items.value;

    return (
        <div class={`main${selected ? '' : ' single'}`}>
            <div class="list">
                <Toolbar
                    title="Work items"
                    refresh={{ busy: store.refreshing.value, onRefresh: () => void store.refresh(api, view) }}
                >
                    <Segmented
                        label="Show"
                        options={store.tabs.map((tab) => ({
                            value: tab,
                            label: titles[tab],
                            count: store.counts.value[tab],
                        }))}
                        value={view.tab}
                        onChange={(tab) => navigate({ screen: 'workitems', tab, filters: view.filters })}
                    />
                </Toolbar>
                <Filters tab={view.tab} filters={view.filters} />
                {store.notice.value && <Notice>{store.notice.value}</Notice>}
                {store.failure.value && <Notice danger>{store.failure.value}</Notice>}
                {store.loaded.value && items.length === 0 ? (
                    <div class="empty">{filtered ? 'Nothing matches these filters.' : empty[view.tab]}</div>
                ) : (
                    <WorkItemList
                        tab={view.tab}
                        filters={view.filters}
                        items={items}
                        selected={selected}
                        more={store.cursor.value ? () => store.more(api, view) : null}
                        now={now}
                        zone={zone}
                    />
                )}
            </div>
            {selected && detail?.id === selected && (
                <WorkItemPanel
                    item={detail}
                    history={store.history.value}
                    approvalId={store.approvalId.value}
                    notice={store.panelNotice.value}
                    busy={store.busy.value}
                    now={now}
                    zone={zone}
                    moreHistory={store.historyCursor.value ? () => store.moreHistory(api, selected) : null}
                    onSave={(patch) => void store.save(api, view, selected, patch)}
                    onCancel={(reason) => void store.cancel(api, view, selected, reason)}
                    onApprove={(approval) => void store.approve(api, view, selected, approval)}
                    onReject={(approval, reason) => void store.reject(api, view, selected, approval, reason)}
                    onClose={close}
                />
            )}
        </div>
    );
}
