import { useApi } from '../api/context';
import { SearchPicker } from './pickers';
import { displayName, firstChannel, searchPeople } from './search';

/** A person chosen by searching for them by anything they are known by; each is shown with company and channel. */
export function PersonPicker({
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
            placeholder="Find a person"
            search={async (query) =>
                (await searchPeople(api, query)).map((c) => ({
                    value: c.id,
                    label: displayName(c),
                    hint: [c.company, firstChannel(c)].filter((part) => part).join(' · ') || undefined,
                }))
            }
            onPick={(picked) => onPick(picked ? { id: picked.value, name: picked.label } : null)}
        />
    );
}
