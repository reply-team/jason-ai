import { useEffect } from 'react';
import { useApi } from '../../api/context';
import type { Campaign, CampaignActivityRow, CampaignSummaryRow } from '../../api/types';
import { campaignTabs, href, type CampaignTab, type Route } from '../../router';
import { essentials } from '../../platform/console-shell';
import { periodLabels } from '../../time/periods';
import { activityColumns, summaryColumns } from './labels';
import { useCampaignChat } from './campaign-chat';
import { ActionsRow } from '../../layout/actions-row';
import { PanelHeader } from '../../layout/panel-header';
import { Tabs } from '../../layout/tabs';
import { CampaignMoves } from './moves';
import { agentKind, asksForCreate, firstTabName, kindWord } from './kind';
import * as store from './store';

type At = Extract<Route, { screen: 'campaigns' }>;

type Props = {
    campaign: Campaign;
    at: At;
    counted: CampaignSummaryRow | CampaignActivityRow | null;
    onAct: (act: 'add') => void;
    /** A move the runtime took: Start, Pause or Resume. */
    onMoved: () => void;
    onClose: () => void;
};

const tabNames: Record<Exclude<CampaignTab, 'chat'>, string> = {
    sequence: 'Sequence', // drawn by firstTabName: Sequence where the label says Sequence, Paths otherwise
    people: 'People',
    inbox: 'Inbox',
    results: 'Results',
    settings: 'Settings',
};

/** The tabs only the demo draws, and the classic shell; the console's Live leaves them out, and their addresses open them all the same. */
const demoOnly: readonly CampaignTab[] = ['sequence'];

const number = new Intl.NumberFormat('en-US');

/** "22 people · reply campaign 4711": how many it holds, and the provider campaign it is linked to. */
export function sub(campaign: Campaign, people: number | null): string {
    const pin = campaign.external_ids.find((p) => p.kind === 'campaign');
    const linked = pin ? `${pin.plugin_id} campaign ${pin.value}` : 'no sequence linked yet';
    const many = people === null ? null : `${number.format(people)} ${people === 1 ? 'person' : 'people'}`;
    return [many, linked].filter(Boolean).join(' · ');
}

/** The campaign, laid out as People's and Work items' panels are: the header, its numbers, and the tabs. */
export function CampaignPanel({ campaign, at, counted, onAct, onMoved, onClose }: Props) {
    const to = (tab: CampaignTab): At => ({
        screen: 'campaigns',
        id: campaign.id,
        tab,
        view: at.view,
        period: at.period,
        filters: at.filters,
    });
    const api = useApi();
    const asks = asksForCreate(campaign);
    useEffect(() => {
        if (asks) void store.loadCreating(api, campaign.id);
    }, [api, campaign.id, asks]);
    const open = store.creating.value?.id === campaign.id ? store.creating.value.open : null;
    const kind = agentKind(campaign, asks ? open : false);
    const word = kindWord(kind);
    const row = counted as Record<string, unknown> | null;
    // Activity's people are the period's, not the campaign's: the line under the name says how many it holds now.
    const people = at.view === 'summary' && typeof row?.['people'] === 'number' ? row['people'] : null;
    const columns = (at.view === 'summary' ? summaryColumns.filter((c) => c.key !== 'people') : activityColumns).filter(
        (c) => c.column !== false,
    );
    const linked = at.view === 'activity' || (row?.['sequence_linked'] ?? true) !== false;
    // Inside the desktop the Chat tab opens a chat about the campaign; it is no address, so it selects nothing here.
    const chat = useCampaignChat();
    return (
        <>
            <PanelHeader
                title={campaign.name}
                sub={
                    <>
                        <span
                            className={`badge${campaign.status === 'active' ? ' success' : ' outline'}`}
                            data-status={campaign.status}
                        >
                            {campaign.status}
                        </span>{' '}
                        {word && (
                            <>
                                <span className="badge outline kind" data-kind={kind}>
                                    {word}
                                </span>{' '}
                            </>
                        )}
                        {sub(campaign, people)}
                    </>
                }
                onClose={onClose}
            />
            <ActionsRow>
                <button className="btn" type="button" onClick={() => onAct('add')}>
                    Add people
                </button>
                {/* Each move named in its class, so a sheet can tell Start and Resume from Pause. */}
                <CampaignMoves campaign={campaign} buttonClass={(move) => `btn agent-move-${move}`} onDone={onMoved} />
            </ActionsRow>
            {row && (
                <div className="stats">
                    {at.view === 'activity' && <span className="label">{periodLabels[at.period]}</span>}
                    {columns
                        .filter((c) => linked || (c.key !== 'sequence' && c.key !== 'out'))
                        .map((c) => {
                            const value = typeof row[c.key] === 'number' ? (row[c.key] as number) : 0;
                            const word = c.label.toLowerCase();
                            return value === 0 ? (
                                <span key={c.key} title={c.hint}>
                                    <b>0</b> {word}
                                </span>
                            ) : (
                                <a key={c.key} title={c.hint} href={href({ ...to('people'), filter: c.key })}>
                                    <b>{number.format(value)}</b> {word}
                                </a>
                            );
                        })}
                </div>
            )}
            <div className="campaign-tabs">
                {chat && (
                    <button
                        className="campaign-tabs-chat"
                        type="button"
                        onClick={() => chat({ id: campaign.id, name: campaign.name })}
                    >
                        Chat
                    </button>
                )}
                <Tabs
                    tabs={campaignTabs
                        .filter((each) => !essentials() || !demoOnly.includes(each))
                        .map((each) => ({
                            key: each,
                            label:
                                each === 'people' && at.view === 'activity'
                                    ? 'People Activity'
                                    : each === 'sequence'
                                      ? firstTabName(kind)
                                      : tabNames[each],
                        }))}
                    active={at.tab}
                    to={to}
                />
            </div>
        </>
    );
}
