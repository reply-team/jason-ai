import type { CampaignActivityRow, CampaignSummary, CampaignSummaryRow } from '../../api/types';
import { More } from '../../lists/more';
import type { LoadMore } from '../../lists/use-cursor-list';
import { href, navigate, type CampaignFilters, type CampaignTab, type CampaignView, type Period } from '../../router';
import { activityColumns, atTheProvider, summaryColumns, tone, unlinked } from './labels';

const number = new Intl.NumberFormat('en-US');

type Props = {
    campaigns: CampaignSummary[];
    view: CampaignView;
    period: Period;
    filters: CampaignFilters;
    summary: Record<string, CampaignSummaryRow>;
    activity: Record<string, CampaignActivityRow>;
    selected: string | null;
    /** The open campaign's tab, and the count its People tab shows: another campaign opens on them. */
    opens: { tab: CampaignTab; filter?: string } | null;
    more: LoadMore | null;
};

/**
 * One row per campaign, one column per count of the view; a number opens exactly the people it counts, and anywhere else
 * on the row opens the campaign on the tab open now.
 */
export function CampaignTable({ campaigns, view, period, filters, summary, activity, selected, opens, more }: Props) {
    const columns = (view === 'summary' ? summaryColumns : activityColumns).filter((column) => column.column !== false);
    return (
        <div class="tablewrap">
            <table class="grid">
                <thead>
                    <tr>
                        <th>Campaign</th>
                        {columns.map((column) => (
                            <th
                                key={column.key}
                                title={column.hint}
                                class={column.key === atTheProvider[0] ? 'side' : ''}
                            >
                                {column.label}
                            </th>
                        ))}
                    </tr>
                </thead>
                <tbody>
                    {campaigns.map((campaign) => {
                        const counts: Record<string, unknown> | undefined =
                            view === 'summary' ? summary[campaign.id] : activity[campaign.id];
                        const linked = view === 'activity' || summary[campaign.id]?.sequence_linked !== false;
                        const at = { screen: 'campaigns' as const, id: campaign.id, view, period, filters };
                        const opened = { ...at, ...(opens ?? { tab: 'timeline' as const }) };
                        return (
                            <tr
                                key={campaign.id}
                                class={campaign.id === selected ? 'sel' : ''}
                                onClick={(event) => {
                                    if (!(event.target as HTMLElement).closest('a')) navigate(opened);
                                }}
                            >
                                <td>
                                    <a class="name" href={href(opened)}>
                                        {campaign.name}
                                    </a>
                                    {campaign.status !== 'active' && (
                                        <span class="badge outline">{campaign.status}</span>
                                    )}
                                </td>
                                {columns.map((column) => {
                                    const side = column.key === atTheProvider[0] ? 'side' : '';
                                    if (!linked && atTheProvider.includes(column.key)) {
                                        return (
                                            <td key={column.key} class={side}>
                                                <span class="dash" title={unlinked}>
                                                    —
                                                </span>
                                            </td>
                                        );
                                    }
                                    const found = counts?.[column.key];
                                    const value = typeof found === 'number' ? found : 0;
                                    // People is every member, not a count of the summary's, so it opens the People tab unfiltered.
                                    const filter = column.key === 'people' ? {} : { filter: column.key };
                                    return (
                                        <td key={column.key} class={side}>
                                            {value === 0 ? (
                                                <span class="zero">0</span>
                                            ) : (
                                                <a
                                                    class={`n ${tone[column.key] ?? ''}`}
                                                    href={href({ ...at, tab: 'people', ...filter })}
                                                >
                                                    {number.format(value)}
                                                </a>
                                            )}
                                        </td>
                                    );
                                })}
                            </tr>
                        );
                    })}
                </tbody>
            </table>
            <More more={more} />
        </div>
    );
}
