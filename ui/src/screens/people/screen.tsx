import { useEffect } from 'react';
import { useApi } from '../../api/context';
import { FiltersBar } from '../../layout/filters-bar';
import { Toolbar } from '../../layout/toolbar';
import { session } from '../../api/session';
import { Search } from '../../lists/search';
import { CampaignPicker } from '../../pickers/campaign-picker';
import { href, navigate, replace, route, type PeopleFilters } from '../../router';
import { PeopleList } from './list';
import { PersonPanel } from './panel/panel';
import * as store from './store';
import { Notice } from '../../layout/notice';

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

export function PeopleScreen() {
    const api = useApi();
    const current = route.value;
    const filters: PeopleFilters = current.screen === 'people' ? current.filters : {};
    const selected = current.screen === 'people' ? (current.id ?? null) : null;
    const asked = current.screen === 'people' ? current.tab : 'timeline';
    const key = href({ screen: 'people', tab: 'timeline', filters });

    useEffect(() => {
        void store.load(api, filters);
    }, [api, key]);

    // A campaign the address names is read once for its name, and dropped if the runtime no longer knows it.
    useEffect(() => {
        const id = filters.campaign;
        if (!id) return;
        void store.resolveCampaign(api, id).then((known) => {
            if (known) return;
            const rest = { ...filters };
            delete rest.campaign;
            narrow(rest);
        });
    }, [api, filters.campaign]);

    /** A filter changed is the same list, narrowed: the address follows without a history entry, and the person stays open on their tab. */
    const narrow = (next: PeopleFilters) =>
        replace(
            selected
                ? { screen: 'people', id: selected, tab: asked, filters: next }
                : { screen: 'people', tab: 'timeline', filters: next },
        );
    const close = () => navigate({ screen: 'people', tab: 'timeline', filters });

    const people = store.people.value;
    const filtered = Boolean(filters.q || filters.campaign || filters.archived);
    const campaignLabel = filters.campaign
        ? store.campaign.value?.id === filters.campaign
            ? store.campaign.value.name
            : '…'
        : 'All campaigns';

    return (
        <div className={`main${selected ? '' : ' single'}`}>
            <div className="list">
                <Toolbar
                    title="People"
                    refresh={{ busy: store.refreshing.value, onRefresh: () => void store.refresh(api, filters) }}
                />
                <FiltersBar>
                    <Search
                        value={filters.q ?? ''}
                        label="Search people"
                        placeholder="Search name, company, email, phone, LinkedIn…"
                        onSearch={(q) => narrow(withValue(filters, 'q', q))}
                    />
                    <CampaignPicker
                        label={campaignLabel}
                        none="All campaigns"
                        onPick={(picked) => {
                            if (picked) store.campaign.value = picked;
                            narrow(withValue(filters, 'campaign', picked?.id));
                        }}
                    />
                    <label className="check">
                        <input
                            type="checkbox"
                            checked={filters.archived === true}
                            onChange={(e) =>
                                narrow(
                                    withValue(filters, 'archived', (e.target as HTMLInputElement).checked || undefined),
                                )
                            }
                        />
                        Archived
                    </label>
                </FiltersBar>
                {store.failure.value && <Notice danger>{store.failure.value}</Notice>}
                {store.loaded.value && people.length === 0 ? (
                    <div className="empty">{filtered ? 'Nobody matches.' : 'Nobody yet.'}</div>
                ) : (
                    <PeopleList
                        people={people}
                        filters={filters}
                        tab={asked}
                        selected={selected}
                        more={store.cursor.value ? () => store.more(api, filters) : null}
                        me={session.actorId.value ?? ''}
                        now={new Date()}
                        zone={zone}
                    />
                )}
            </div>
            {selected && (
                // An address on a tab this person's panel does not have, Messages for a person linked nowhere, shows their activity.
                <PersonPanel
                    contactId={selected}
                    campaignId={filters.campaign}
                    tab={asked}
                    to={(next) => ({ screen: 'people', id: selected, tab: next, filters })}
                    onClose={close}
                />
            )}
        </div>
    );
}

/** The filters with one key set, or gone when it is empty: one view, one address. */
function withValue<K extends keyof PeopleFilters>(
    filters: PeopleFilters,
    key: K,
    value: PeopleFilters[K] | undefined,
): PeopleFilters {
    const next = { ...filters };
    if (value === undefined || value === '' || value === false) delete next[key];
    else next[key] = value;
    return next;
}
