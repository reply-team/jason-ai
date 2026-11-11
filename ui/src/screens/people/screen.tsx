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
import { Chips } from '../../layout/chips';
import { t } from '../../platform/i18n';
import { stages, stageWord, type Stage } from './standing';

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
    const filtered = Boolean(filters.q || filters.campaign || filters.stage || filters.archived);
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
                <div className="stage-filter">
                    <Chips<Stage | ''>
                        label={t('people.stages')}
                        row="filters"
                        chips={[
                            { key: '', label: t('people.stage.all') },
                            ...stages.map((stage) => ({
                                key: stage,
                                label: stageWord(stage)?.label ?? stage,
                                ...hint(stage),
                                count: store.counts.value?.[stage] ?? null,
                            })),
                        ]}
                        on={filters.stage ?? ''}
                        onChoose={(key) => narrow(withValue(filters, 'stage', key || undefined))}
                    />
                </div>
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

/** The hover a chip carries: the runtime's own definition of the count. */
const hint = (stage: Stage) => {
    const title = stageWord(stage)?.hint;
    return title ? { title } : {};
};

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
