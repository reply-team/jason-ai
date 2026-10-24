import { useRef } from 'preact/hooks';
import { useApi } from '../api/context';
import { campaignIdsLimit } from '../api/types';
import { MultiSearchPicker } from './pickers';
import { searchCampaigns } from './search';

export type ChosenCampaign = { id: string; name: string; status: string };

/**
 * Up to as many campaigns as one read may name, none meaning all: the button, then a chip for each one chosen with its
 * status when it is not active and a way to drop it, then Clear.
 */
export function CampaignsPicker({
    chosen,
    onChange,
    max = campaignIdsLimit,
}: {
    chosen: readonly ChosenCampaign[];
    onChange: (next: ChosenCampaign[]) => void;
    max?: number;
}) {
    const api = useApi();
    // Dropping a chip or clearing them all takes away the button that had the keyboard; the picker gets it back.
    const opener = useRef<HTMLButtonElement>(null);
    const change = (next: ChosenCampaign[]) => {
        onChange(next);
        opener.current?.focus();
    };
    return (
        <>
            <MultiSearchPicker
                opener={opener}
                label={chosen.length > 0 ? 'Campaigns' : 'All campaigns'}
                set={chosen.length > 0}
                placeholder="Find a campaign"
                max={max}
                chosen={chosen.map((campaign) => campaign.id)}
                search={async (query) =>
                    (await searchCampaigns(api, query, true)).map((campaign) => ({
                        value: campaign.id,
                        label: campaign.name,
                        hint: campaign.status === 'active' ? undefined : campaign.status,
                    }))
                }
                onToggle={(entry) =>
                    onChange(
                        chosen.some((campaign) => campaign.id === entry.value)
                            ? chosen.filter((campaign) => campaign.id !== entry.value)
                            : [...chosen, { id: entry.value, name: entry.label, status: entry.hint ?? 'active' }],
                    )
                }
            />
            {chosen.map((campaign) => (
                <span key={campaign.id} class="chip">
                    {campaign.name}
                    {campaign.status !== 'active' && <span class="hint"> {campaign.status}</span>}
                    <button
                        type="button"
                        aria-label={`Drop ${campaign.name}`}
                        title={`Drop ${campaign.name}`}
                        onClick={() => change(chosen.filter((other) => other.id !== campaign.id))}
                    >
                        ×
                    </button>
                </span>
            ))}
            {chosen.length > 0 && (
                <button type="button" class="btn ghost" onClick={() => change([])}>
                    Clear
                </button>
            )}
        </>
    );
}
