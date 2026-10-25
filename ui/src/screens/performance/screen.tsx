import { useEffect, useState } from 'preact/hooks';
import type { ActivityBucket } from '../../api/types';
import { useApi } from '../../api/context';
import { session } from '../../api/session';
import { FiltersBar } from '../../layout/filters-bar';
import { Notice } from '../../layout/notice';
import { PeriodSwitch } from '../../layout/period-switch';
import { Toolbar } from '../../layout/toolbar';
import { CampaignsPicker } from '../../pickers/campaigns-picker';
import { navigate, replace, route, type ActivityCount, type Period } from '../../router';
import { AddDialog } from '../people/add-dialog';
import { PersonCard } from '../people/card';
import { Details } from '../people/details';
import * as person from '../people/store';
import { linked, ThreadsTab } from '../people/threads';
import { Timeline } from '../people/timeline';
import { Charts } from './charts';
import { Listing } from './list';
import * as store from './store';
import { Tiles } from './tiles';

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
const periods: readonly Period[] = ['today', '7d', '30d', '90d'];
const fresh: store.At = { screen: 'performance', tab: 'timeline', period: '7d', campaigns: [] };

/** The page's address with these changes, a bar's window dropped unless it is given again: it belongs to its period. */
function moved(at: store.At, next: Partial<store.At>): store.At {
    const to: store.At = {
        screen: 'performance',
        tab: at.tab,
        period: next.period ?? at.period,
        campaigns: next.campaigns ?? at.campaigns,
    };
    if (at.id) to.id = at.id;
    const count = 'count' in next ? next.count : at.count;
    if (count) to.count = count;
    if (count && next.from && next.to) Object.assign(to, { from: next.from, to: next.to });
    return to;
}

export function PerformanceScreen() {
    const api = useApi();
    const current = route.value;
    const at = current.screen === 'performance' ? current : fresh;
    const known = at.campaigns.every((id) => store.campaigns.value.some((campaign) => campaign.id === id));
    const numbers = store.numbersKey(at);
    const listing = store.listKey(at);
    const ready = store.shown.value === numbers;
    const series = ready ? store.series.value : null;
    const charted = at.count === 'contacted' || at.count === 'replied' ? at.count : null;
    const bucket =
        at.from && at.to && charted
            ? (series?.[charted].find((b) => b.start === at.from && b.end === at.to) ?? null)
            : null;

    // An address names campaigns by id: their names come first, and an id the runtime no longer knows is left out.
    useEffect(() => {
        if (known) return;
        const asked = at.campaigns.join(' ');
        const stillAsked = () => route.value.screen === 'performance' && route.value.campaigns.join(' ') === asked;
        void store.resolveCampaigns(api, at.campaigns, stillAsked).then((kept) => {
            if (kept && kept.length < at.campaigns.length) replace(moved(at, { campaigns: kept }));
        });
    }, [api, at.campaigns.join(' ')]);

    useEffect(() => {
        if (known) void store.load(api, at, new Date(), zone);
    }, [api, numbers, known]);

    // A bar is a bucket of the series on show. Read since the address was made, the running one ends later; out of the
    // period, it is no bar at all, and the address goes back to the period, its number and its list together.
    useEffect(() => {
        if (!series || !at.from || bucket) return;
        const later = charted ? series[charted].find((b) => b.start === at.from) : undefined;
        replace(moved(at, later ? { from: later.start, to: later.end } : {}));
    }, [series, at.from, at.to]);

    // A bar's people wait for its bucket, and are read again with each series read after: they are that bucket's. Without
    // a series to say, the address's window is read as it is.
    const barRead = at.from ? (series ?? (ready ? 'unread' : null)) : null;
    useEffect(() => {
        if (!known || !at.count) return;
        if (at.from && !bucket && !(ready && !series)) return;
        void store.listPeople(api, at, new Date(), zone);
    }, [api, listing, known, barRead]);

    // The person open beside the list is People's own panel: the same card, tabs and dialog, on this page's address.
    const [adding, setAdding] = useState(false);
    useEffect(() => {
        setAdding(false);
        if (at.id) void person.open(api, at.id);
        else person.close();
    }, [api, at.id]);
    const open = person.person.value;
    // An address on Threads for a person linked nowhere shows their timeline: the tab is not there to show.
    const tab = at.tab === 'threads' && open && !linked(open) ? 'timeline' : at.tab;
    const me = session.actorId.value ?? '';
    const close = () => {
        const back: store.At = { ...at, tab: 'timeline' };
        delete back.id;
        navigate(back);
    };

    const now = new Date();
    const total = ready ? store.total.value : null;
    const period = store.bucketOf(at.period);
    const chosen = store.campaigns.value.filter((campaign) => at.campaigns.includes(campaign.id));

    return (
        <div class={`main${at.id ? '' : ' single'}`}>
            <div class="list">
                <Toolbar
                    title="Performance"
                    refresh={{
                        busy: store.refreshing.value,
                        onRefresh: () => void store.refresh(api, at, new Date(), zone),
                    }}
                >
                    <PeriodSwitch
                        periods={periods}
                        value={at.period}
                        onChange={(next) => replace(moved(at, { period: next }))}
                    />
                </Toolbar>
                <FiltersBar>
                    <CampaignsPicker
                        chosen={chosen}
                        onChange={(next) => {
                            store.notice.value = null;
                            store.campaigns.value = next;
                            replace(moved(at, { campaigns: next.map((campaign) => campaign.id) }));
                        }}
                    />
                </FiltersBar>
                {store.notice.value && <Notice status>{store.notice.value}</Notice>}
                <div class="perf">
                    {store.seriesFailure.value && <Notice danger>{store.seriesFailure.value}</Notice>}
                    {(series || !ready) && (
                        <Charts
                            series={series}
                            period={period}
                            zone={zone}
                            now={series?.readAt ?? now}
                            chosen={{ count: at.count, from: at.from, to: at.to }}
                            onBar={(count: ActivityCount, b: ActivityBucket) =>
                                replace(moved(at, { count, from: b.start, to: b.end }))
                            }
                        />
                    )}
                    {store.totalFailure.value && <Notice danger>{store.totalFailure.value}</Notice>}
                    {(total || !ready) && (
                        <Tiles total={total} chosen={at.count} onChoose={(count) => replace(moved(at, { count }))} />
                    )}
                    {at.count && (
                        <Listing
                            at={at}
                            count={at.count}
                            counted={total ? total[at.count] : null}
                            bucket={bucket}
                            period={period}
                            people={store.peopleShown.value === listing ? store.people.value : []}
                            loaded={store.peopleShown.value === listing}
                            more={store.peopleCursor.value ? () => store.morePeople(api) : null}
                            zone={zone}
                            now={now}
                            onOpen={(id) => navigate({ ...at, id })}
                            onBack={() => replace(moved(at, {}))}
                        />
                    )}
                    {store.peopleFailure.value && <Notice danger>{store.peopleFailure.value}</Notice>}
                </div>
            </div>
            {at.id && (
                <div class="panel">
                    {person.personFailure.value && <Notice danger>{person.personFailure.value}</Notice>}
                    {open && open.id === at.id && (
                        <>
                            <PersonCard
                                person={open}
                                tab={tab}
                                to={(next) => ({ ...at, id: open.id, tab: next })}
                                now={now}
                                zone={zone}
                                onAdd={() => setAdding(true)}
                                onClose={close}
                            />
                            {tab === 'timeline' && <Timeline person={open} me={me} now={now} zone={zone} />}
                            {tab === 'threads' && <ThreadsTab person={open} zone={zone} />}
                            {tab === 'details' && <Details person={open} now={now} zone={zone} />}
                            {adding && <AddDialog person={open} onClose={() => setAdding(false)} />}
                        </>
                    )}
                </div>
            )}
        </div>
    );
}
