import { useEffect, useRef, useState } from 'preact/hooks';
import { useApi } from '../../api/context';
import { session } from '../../api/session';
import { SearchPicker } from '../../pickers/pickers';
import { href, navigate, replace, route, type PeopleFilters } from '../../router';
import { AddDialog } from './add-dialog';
import { PersonCard } from './card';
import { Details } from './details';
import { PeopleList } from './list';
import { linked, ThreadsTab } from './threads';
import { Timeline } from './timeline';
import * as store from './store';

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
                <div class="toolbar">
                    <h1>People</h1>
                    <span class="spacer" />
                    <button
                        class={`iconbtn${store.refreshing.value ? ' spin' : ''}`}
                        type="button"
                        title="Refresh"
                        disabled={store.refreshing.value}
                        onClick={() => void store.refresh(api, filters)}
                    >
                        <span class="glyph">⟳</span>
                    </button>
                </div>
                <div class="filters">
                    <Search value={filters.q ?? ''} onSearch={(q) => narrow(withValue(filters, 'q', q))} />
                    <SearchPicker
                        label={campaignLabel}
                        clearLabel="All campaigns"
                        placeholder="Find a campaign"
                        search={async (query) =>
                            (await store.searchCampaigns(api, query)).map((c) => ({ value: c.id, label: c.name }))
                        }
                        onPick={(picked) => {
                            if (picked) store.campaign.value = { id: picked.value, name: picked.label };
                            narrow(withValue(filters, 'campaign', picked?.value));
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
                </div>
                {store.failure.value && (
                    <div class="notice danger" role="alert">
                        {store.failure.value}
                    </div>
                )}
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
                    {store.personFailure.value && (
                        <div class="notice danger" role="alert">
                            {store.personFailure.value}
                        </div>
                    )}
                    {person && person.id === selected && (
                        <>
                            <PersonCard
                                person={person}
                                tab={tab}
                                filters={filters}
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

/** The search box: what is typed reaches the list once the person pauses, and only from two characters on. */
function Search({ value, onSearch }: { value: string; onSearch: (q: string) => void }) {
    const [text, setText] = useState(value);
    // The address changed from outside (Back, a link): the box shows it. Not on mounting, which would erase what was
    // typed before the first effects ran.
    const mounted = useRef(false);
    useEffect(() => {
        if (mounted.current) setText(value);
        mounted.current = true;
    }, [value]);
    useEffect(() => {
        const query = text.trim();
        if (query === value || (query.length > 0 && query.length < 2)) return;
        const timer = setTimeout(() => onSearch(query), 250);
        return () => clearTimeout(timer);
    }, [text]);
    return (
        <input
            class="search"
            type="search"
            aria-label="Search people"
            placeholder="Search name, company, email, phone, LinkedIn…"
            value={text}
            onInput={(e) => setText((e.target as HTMLInputElement).value)}
        />
    );
}
