import { useEffect, useState } from 'preact/hooks';
import { useApi } from '../../api/context';
import { FiltersBar } from '../../layout/filters-bar';
import { Toolbar } from '../../layout/toolbar';
import { session } from '../../api/session';
import { Search } from '../../lists/search';
import { CampaignPicker } from '../../pickers/campaign-picker';
import { href, navigate, replace, route, type PeopleFilters } from '../../router';
import { AddDialog } from './add-dialog';
import { PersonCard } from './card';
import { Details } from './details';
import { PeopleList } from './list';
import { linked, ThreadsTab } from './threads';
import { Timeline } from './timeline';
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

    const [adding, setAdding] = useState(false);
    useEffect(() => {
        setAdding(false);
        if (selected) void store.open(api, selected);
        else store.close();
    }, [api, selected]);

    const person = store.person.value;
    // An address on Threads for a person linked nowhere shows their timeline: the tab is not there to show.
    const tab = asked === 'threads' && person && !linked(person) ? 'timeline' : asked;
    const now = new Date();
    const people = store.people.value;
    const filtered = Boolean(filters.q || filters.campaign || filters.archived);
    const campaignLabel = filters.campaign
        ? store.campaign.value?.id === filters.campaign
            ? store.campaign.value.name
            : '…'
        : 'All campaigns';

    return (
        <div class={`main${selected ? '' : ' single'}`}>
            <div class="list">
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
                    <label class="check">
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
                    <div class="empty">{filtered ? 'Nobody matches.' : 'Nobody yet.'}</div>
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
                <div class="panel">
                    {store.personFailure.value && <Notice danger>{store.personFailure.value}</Notice>}
                    {person && person.id === selected && (
                        <>
                            <PersonCard
                                person={person}
                                tab={tab}
                                to={(next) => ({ screen: 'people', id: person.id, tab: next, filters })}
                                now={now}
                                zone={zone}
                                onAdd={() => setAdding(true)}
                                onClose={close}
                            />
                            {tab === 'timeline' && (
                                <Timeline person={person} me={session.actorId.value ?? ''} now={now} zone={zone} />
                            )}
                            {tab === 'threads' && <ThreadsTab person={person} zone={zone} />}
                            {tab === 'details' && <Details person={person} now={now} zone={zone} />}
                            {adding && <AddDialog person={person} onClose={() => setAdding(false)} />}
                        </>
                    )}
                </div>
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
