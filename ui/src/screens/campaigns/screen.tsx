import { useEffect, useRef, useState } from 'preact/hooks';
import { useApi } from '../../api/context';
import { FiltersBar } from '../../layout/filters-bar';
import { PeriodSwitch } from '../../layout/period-switch';
import { Segmented } from '../../layout/segmented';
import { Toolbar } from '../../layout/toolbar';
import { session } from '../../api/session';
import { ApprovalPanel } from '../attention/approval-panel';
import { QuestionPanel } from '../attention/question-panel';
import * as timeline from '../people/store';
import { Search } from '../../lists/search';
import {
    href,
    navigate,
    replace,
    route,
    type CampaignFilters,
    type CampaignView,
    type Period,
    type Route,
} from '../../router';
import { AddPeopleDialog } from './add-dialog';
import { Details } from './details';
import { views } from './labels';
import { CampaignPanel } from './panel';
import { TransitionDialog } from './pause-dialog';
import { CampaignPeople } from './people';
import { CampaignTimeline } from './timeline';
import * as store from './store';
import { CampaignTable } from './table';
import { Notice } from '../../layout/notice';

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

type At = Extract<Route, { screen: 'campaigns' }>;

export function CampaignsScreen() {
    const api = useApi();
    const current = route.value;
    const at: At =
        current.screen === 'campaigns'
            ? current
            : { screen: 'campaigns', tab: 'timeline', view: 'summary', period: '7d', filters: {} };
    const table: store.Table = { view: at.view, period: at.period, filters: at.filters };
    const key = href({ screen: 'campaigns', tab: 'timeline', ...table });
    const countedUnder = store.numbersKey(table);
    const selected = at.id ?? null;

    // What the screen said on an earlier visit is not said again.
    useEffect(() => {
        store.notice.value = null;
    }, [api]);

    useEffect(() => {
        void store.load(api, table, new Date(), zone);
    }, [api, key]);

    // A campaign the address names is read with its numbers; one the runtime no longer knows is closed, and said so.
    useEffect(() => {
        if (!selected) {
            store.close();
            return;
        }
        store.notice.value = null;
        // A campaign opened starts its timeline on All; Back from what opened in place, or another tab, keeps the chip.
        timeline.chip.value = 'all';
        void store.open(api, selected, table, new Date(), zone, at.tab !== 'people').then((known) => {
            if (known) return;
            store.notice.value = 'The campaign in this address no longer exists; it was closed.';
            replace({ screen: 'campaigns', tab: 'timeline', ...table });
        });
    }, [api, selected]);

    // The panel's numbers follow the table's view and period while it stays open; a People list reads them with itself.
    const counting = useRef(countedUnder);
    useEffect(() => {
        if (counting.current === countedUnder) return;
        counting.current = countedUnder;
        if (selected && at.tab !== 'people' && store.campaign.value?.id === selected) {
            void store.count(api, selected, table, new Date(), zone);
        }
    }, [countedUnder]);

    const close = () => navigate({ screen: 'campaigns', tab: 'timeline', ...table });

    // The dialog open, with the campaign it belongs to: it never shows over another one.
    const [act, setAct] = useState<{ id: string | null; what: 'pause' | 'start' | 'add' } | null>(null);
    const acting = act && act.id === selected ? act.what : null;
    const setActing = (what: 'pause' | 'start' | 'add' | null) => setAct(what ? { id: selected, what } : null);
    /** The open campaign as it is now: its numbers, and what its tab shows. A People list reads the numbers with itself. */
    const rereadPanel = () => {
        if (!selected) return;
        if (at.tab === 'people') void store.listMembers(api, selected, table, query, new Date(), zone);
        else void store.count(api, selected, table, new Date(), zone);
        if (at.tab === 'timeline') {
            void store.loadPending(api, selected);
            void timeline.loadTimeline(api, { campaign_id: selected }, timeline.chip.value, false);
        }
    };
    // After Pause, Start or Add people: the table's row and the panel say what is true now. It runs when the write
    // answers, so it reads what is open by then, not what was open when the write began.
    const latestReread = useRef(() => {});
    latestReread.current = () => {
        void store.load(api, table, new Date(), zone);
        rereadPanel();
    };
    const reread = () => latestReread.current();

    // The People tab: its count or All, and the search typed into it, which the address does not keep.
    // Kept with the campaign it was typed for, so another campaign opens unsearched without an effect racing a keystroke.
    const [typed, setTyped] = useState<{ id: string | null; q: string }>({ id: null, q: '' });
    const search = typed.id === selected ? typed.q : '';
    const setSearch = (q: string) => setTyped({ id: selected, q });
    const query: store.PeopleQuery = {
        ...(at.filter ? { filter: at.filter } : {}),
        ...(search ? { q: search } : {}),
    };
    const listing = selected && at.tab === 'people' ? store.membersKey(selected, table, query) : null;
    useEffect(() => {
        if (selected && listing) void store.listMembers(api, selected, table, query, new Date(), zone);
    }, [api, listing]);

    /** Decides what waits on a person, then reads the table's row and the panel again: the person may have left a count. */
    const decide = async (operation: string, body: Record<string, unknown>) => {
        await store.decide(api, operation, body);
        reread();
    };

    /** The table changed is the same screen: the address follows without a history entry, and an open campaign stays open. */
    const reshape = (next: { view?: CampaignView; period?: Period; filters?: CampaignFilters }) => {
        const view = next.view ?? at.view;
        const moved: At = {
            screen: 'campaigns',
            tab: at.tab,
            view,
            period: view === 'activity' ? (next.period ?? at.period) : '7d',
            filters: next.filters ?? at.filters,
        };
        if (selected) moved.id = selected;
        // A count belongs to its view, so switching the view lets the People tab's go.
        if (selected && at.tab === 'people' && !next.view && at.filter) moved.filter = at.filter;
        if (next.view && next.view !== at.view) navigate(moved);
        else replace(moved);
    };

    // Until this table's answer is in, nothing of another table's is shown as if it were this one's.
    const ready = store.shown.value === store.keyOf(table);
    const campaigns = ready ? store.campaigns.value : [];
    const campaign = store.campaign.value;
    const waiting = store.waiting.value?.campaignId === selected ? store.waiting.value : null;
    const filtered = Boolean(at.filters.q || at.filters.archived);
    const numbers = store.countedFor(selected, table);
    const sequence =
        numbers && 'sequence_linked' in numbers ? numbers : selected ? store.summary.value[selected] : undefined;

    return (
        <div class={`main campaigns${selected ? '' : ' single'}`}>
            <div class="list">
                <Toolbar
                    title="Campaigns"
                    refresh={{
                        busy: store.refreshing.value,
                        onRefresh: () => {
                            void store.refresh(api, table, new Date(), zone);
                            rereadPanel();
                        },
                    }}
                >
                    <Segmented
                        label="Show"
                        options={(['summary', 'activity'] as const).map((view) => ({
                            value: view,
                            label: views[view],
                        }))}
                        value={at.view}
                        onChange={(view) => reshape({ view })}
                    />
                </Toolbar>
                <FiltersBar>
                    <Search
                        value={at.filters.q ?? ''}
                        label="Find a campaign"
                        placeholder="Find a campaign"
                        onSearch={(q) => reshape({ filters: withValue(at.filters, 'q', q) })}
                    />
                    {at.view === 'activity' && (
                        <PeriodSwitch value={at.period} onChange={(period) => reshape({ period })} />
                    )}
                    <label class="check">
                        <input
                            type="checkbox"
                            checked={at.filters.archived === true}
                            onChange={(e) =>
                                reshape({
                                    filters: withValue(
                                        at.filters,
                                        'archived',
                                        (e.target as HTMLInputElement).checked || undefined,
                                    ),
                                })
                            }
                        />
                        Archived
                    </label>
                </FiltersBar>
                {store.notice.value && <Notice status>{store.notice.value}</Notice>}
                {store.failure.value && <Notice danger>{store.failure.value}</Notice>}
                {store.actionFailure.value && <Notice danger>{store.actionFailure.value}</Notice>}
                {ready && campaigns.length === 0 ? (
                    <div class="empty">{filtered ? 'No campaign matches.' : 'No campaigns yet.'}</div>
                ) : (
                    <CampaignTable
                        campaigns={campaigns}
                        view={at.view}
                        period={at.period}
                        filters={at.filters}
                        summary={store.summary.value}
                        activity={store.activity.value}
                        selected={selected}
                        opens={
                            selected
                                ? { tab: at.tab, ...(at.tab === 'people' && at.filter ? { filter: at.filter } : {}) }
                                : null
                        }
                        more={store.campaignsCursor.value ? () => store.moreCampaigns(api, table) : null}
                    />
                )}
            </div>
            {selected && waiting && 'approval' in waiting && (
                <ApprovalPanel
                    approval={waiting.approval}
                    now={new Date()}
                    zone={zone}
                    busy={store.busy.value}
                    remaining=""
                    skipLabel="Back to the campaign"
                    onApprove={() => void decide('approval.approve', { approval_id: waiting.approval.id })}
                    onReject={(reason) =>
                        void decide('approval.reject', {
                            approval_id: waiting.approval.id,
                            ...(reason ? { reason } : {}),
                        })
                    }
                    onSkip={() => (store.waiting.value = null)}
                    onClose={() => (store.waiting.value = null)}
                />
            )}
            {selected && waiting && 'decision' in waiting && (
                <QuestionPanel
                    decision={waiting.decision}
                    now={new Date()}
                    busy={store.busy.value}
                    remaining=""
                    skipLabel="Back to the campaign"
                    onAnswer={(choice, reason) =>
                        void decide('decision.answer', {
                            decision_id: waiting.decision.id,
                            ...(choice.option
                                ? { answer: choice.option, option: choice.option }
                                : { answer: choice.text ?? '' }),
                            ...(reason ? { reason } : {}),
                        })
                    }
                    onSkip={() => (store.waiting.value = null)}
                    onClose={() => (store.waiting.value = null)}
                />
            )}
            {selected && !waiting && (
                <div class="panel">
                    {store.panelFailure.value && <Notice danger>{store.panelFailure.value}</Notice>}
                    {campaign && campaign.id === selected && (
                        <>
                            <CampaignPanel
                                campaign={campaign}
                                at={at}
                                counted={numbers}
                                onAct={setActing}
                                onClose={close}
                            />
                            {(acting === 'pause' || acting === 'start') && (
                                <TransitionDialog
                                    campaign={campaign}
                                    kind={acting}
                                    onDone={() => {
                                        setActing(null);
                                        reread();
                                    }}
                                    onClose={() => setActing(null)}
                                />
                            )}
                            {acting === 'add' && (
                                <AddPeopleDialog campaign={campaign} onAdded={reread} onClose={() => setActing(null)} />
                            )}
                            {at.tab === 'timeline' && (
                                <CampaignTimeline
                                    campaign={campaign}
                                    me={session.actorId.value ?? ''}
                                    now={new Date()}
                                    zone={zone}
                                    onWaiting={(what) => void store.openWaiting(api, selected!, what)}
                                />
                            )}
                            {at.tab === 'people' && (
                                <CampaignPeople
                                    at={at}
                                    counted={numbers}
                                    linked={sequence ? sequence.sequence_linked : null}
                                    people={store.membersShown.value === listing ? store.members.value : null}
                                    query={search}
                                    failure={store.membersFailure.value}
                                    me={session.actorId.value ?? ''}
                                    now={new Date()}
                                    zone={zone}
                                    more={store.membersCursor.value ? () => store.moreMembers(api) : null}
                                    onSearch={setSearch}
                                    onWaiting={(person) => {
                                        const what = person.why ? store.waitingOf(person.why) : null;
                                        if (what) void store.openWaiting(api, selected!, what);
                                    }}
                                />
                            )}
                            {at.tab === 'details' && <Details campaign={campaign} now={new Date()} zone={zone} />}
                        </>
                    )}
                </div>
            )}
        </div>
    );
}

/** The filters with one key set, or gone when it is empty: one view, one address. */
function withValue<K extends keyof CampaignFilters>(
    filters: CampaignFilters,
    key: K,
    value: CampaignFilters[K] | undefined,
): CampaignFilters {
    const next = { ...filters };
    if (value === undefined || value === '' || value === false) delete next[key];
    else next[key] = value;
    return next;
}
