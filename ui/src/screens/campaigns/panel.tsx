import type { Campaign, CampaignActivityRow, CampaignSummaryRow } from '../../api/types';
import { href, type CampaignTab, type Route } from '../../router';
import { periodLabels } from '../../time/periods';
import { activityColumns, summaryColumns } from './labels';
import { ActionsRow } from '../../layout/actions-row';
import { PanelHeader } from '../../layout/panel-header';
import { Tabs } from '../../layout/tabs';

type At = Extract<Route, { screen: 'campaigns' }>;

type Props = {
    campaign: Campaign;
    at: At;
    counted: CampaignSummaryRow | CampaignActivityRow | null;
    onAct: (act: 'pause' | 'start' | 'add') => void;
    onClose: () => void;
};

const tabNames: Record<CampaignTab, string> = { timeline: 'Timeline', people: 'People', details: 'Details' };
const number = new Intl.NumberFormat('en-US');

/** "active · 22 people · reply campaign 4711": where it stands, how many it holds, and the provider campaign it is linked to. */
function sub(campaign: Campaign, people: number | null): string {
    const pin = campaign.external_ids.find((p) => p.kind === 'campaign');
    const linked = pin ? `${pin.plugin_id} campaign ${pin.value}` : 'no sequence linked yet';
    const many = people === null ? null : `${number.format(people)} ${people === 1 ? 'person' : 'people'}`;
    return [many, linked].filter(Boolean).join(' · ');
}

/** The campaign, laid out as People's and Work items' panels are: the header, its numbers, and the tabs. */
export function CampaignPanel({ campaign, at, counted, onAct, onClose }: Props) {
    const to = (tab: CampaignTab): At => ({
        screen: 'campaigns',
        id: campaign.id,
        tab,
        view: at.view,
        period: at.period,
        filters: at.filters,
    });
    const row = counted as Record<string, unknown> | null;
    // Activity's people are the period's, not the campaign's: the line under the name says how many it holds now.
    const people = at.view === 'summary' && typeof row?.['people'] === 'number' ? row['people'] : null;
    const columns = (at.view === 'summary' ? summaryColumns.filter((c) => c.key !== 'people') : activityColumns).filter(
        (c) => c.column !== false,
    );
    const linked = at.view === 'activity' || (row?.['sequence_linked'] ?? true) !== false;
    return (
        <>
            <PanelHeader
                title={campaign.name}
                sub={
                    <>
                        <span class={`badge${campaign.status === 'active' ? ' success' : ' outline'}`}>
                            {campaign.status}
                        </span>{' '}
                        {sub(campaign, people)}
                    </>
                }
                onClose={onClose}
            />
            <ActionsRow>
                {/* A draft is taken live by the planner or the command line; an archived campaign is over. */}
                {campaign.status === 'active' && (
                    <button class="btn" type="button" onClick={() => onAct('pause')}>
                        Pause
                    </button>
                )}
                {campaign.status === 'paused' && (
                    <button class="btn" type="button" onClick={() => onAct('start')}>
                        Start
                    </button>
                )}
                <button class="btn" type="button" onClick={() => onAct('add')}>
                    Add people
                </button>
            </ActionsRow>
            {row && (
                <div class="stats">
                    {at.view === 'activity' && <span class="label">{periodLabels[at.period]}</span>}
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
            <Tabs
                tabs={(['timeline', 'people', 'details'] as const).map((each) => ({
                    key: each,
                    label: each === 'people' && at.view === 'activity' ? 'People Activity' : tabNames[each],
                }))}
                active={at.tab}
                to={to}
            />
        </>
    );
}
