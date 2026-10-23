import { useApi } from '../api/context';
import { SearchPicker } from './pickers';
import { searchCampaigns } from './search';

/**
 * A campaign chosen by searching for it. A campaign that is not active says so beside its name, so a quiet one is
 * told apart from a running one.
 */
export function CampaignPicker({
    label,
    none,
    onPick,
}: {
    label: string;
    none: string;
    onPick: (picked: { id: string; name: string } | null) => void;
}) {
    const api = useApi();
    return (
        <SearchPicker
            label={label}
            clearLabel={none}
            placeholder="Find a campaign"
            search={async (query) =>
                (await searchCampaigns(api, query)).map((c) => ({
                    value: c.id,
                    label: c.name,
                    hint: c.status === 'active' ? undefined : c.status,
                }))
            }
            onPick={(picked) => onPick(picked ? { id: picked.value, name: picked.label } : null)}
        />
    );
}
