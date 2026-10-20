import { useApi } from '../../api/context';
import { Picker, SearchPicker } from '../../pickers/pickers';
import { replace, type Tab, type WorkItemFilters } from '../../router';
import * as store from './store';

type Props = { tab: Tab; filters: WorkItemFilters };

/** The filters every tab shares; a change narrows the same view, so it replaces the address rather than adding to history. */
export function Filters({ tab, filters }: Props) {
    const api = useApi();
    const set = (next: Loose) => replace({ screen: 'workitems', tab, filters: without(next) });
    const kind = filters.kind;
    const campaign = store.campaign.value?.id === filters.campaign ? store.campaign.value : null;
    const person = store.person.value?.id === filters.contact ? store.person.value : null;

    return (
        <div class="filters">
            <SearchPicker
                label={filters.campaign ? (campaign?.name ?? '…') : 'All campaigns'}
                clearLabel="All campaigns"
                placeholder="Find a campaign"
                search={async (query) =>
                    (await store.searchCampaigns(api, query)).map((c) => ({
                        value: c.id,
                        label: c.name,
                        hint: c.status === 'active' ? undefined : c.status,
                    }))
                }
                onPick={(picked) => {
                    if (picked) store.campaign.value = { id: picked.value, name: picked.label };
                    set({ ...filters, campaign: picked?.value });
                }}
            />
            <SearchPicker
                label={filters.contact ? (person?.name ?? '…') : 'Everybody'}
                clearLabel="Everybody"
                placeholder="Find a person"
                search={async (query) =>
                    (await store.searchPeople(api, query)).map((c) => ({
                        value: c.id,
                        label: store.displayName(c),
                        hint: [c.company, store.firstChannel(c)].filter((part) => part).join(' · ') || undefined,
                    }))
                }
                onPick={(picked) => {
                    if (picked) store.person.value = { id: picked.value, name: picked.label };
                    set({ ...filters, contact: picked?.value });
                }}
            />
            <div class="segmented" role="group" aria-label="Kind">
                {(
                    [
                        [undefined, 'All'],
                        ['ai_role', 'Role work'],
                        ['provider_op', 'Provider operations'],
                    ] as const
                ).map(([value, label]) => (
                    <button
                        key={label}
                        type="button"
                        class={kind === value ? 'on' : ''}
                        aria-pressed={kind === value}
                        onClick={() => {
                            if (kind === value) return;
                            set({ ...filters, kind: value, role: value === 'provider_op' ? undefined : filters.role });
                        }}
                    >
                        {label}
                    </button>
                ))}
            </div>
            {kind !== 'provider_op' && (
                <Picker
                    label={filters.role ?? 'Any role'}
                    options={[
                        { value: null, label: 'Any role' },
                        ...store.roles.value.map((r) => ({ value: r.name, label: r.name })),
                    ]}
                    onPick={(value) => set({ ...filters, role: value ?? undefined })}
                />
            )}
            {tab !== 'now' && (
                <Day
                    label={tab === 'planned' ? 'Due on' : 'Finished on'}
                    value={filters.on}
                    onChange={(on) => set({ ...filters, on })}
                />
            )}
            {tab === 'done' && (
                <button
                    type="button"
                    class={`chipbtn${filters.failed ? ' on' : ''}`}
                    aria-pressed={!!filters.failed}
                    onClick={() => set({ ...filters, failed: !filters.failed || undefined })}
                >
                    Failures
                </button>
            )}
        </div>
    );
}

/** A day as the date field and the address write it, on the person's own calendar. */
function dayOf(at: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}`;
}

/** One day, stepped a day at a time; the arrows start from today when no day is set. */
function Day({
    label,
    value,
    onChange,
}: {
    label: string;
    value: string | undefined;
    onChange: (day: string | undefined) => void;
}) {
    const step = (days: number) => {
        const at = value ? new Date(`${value}T00:00:00`) : new Date();
        at.setDate(at.getDate() + (value ? days : 0));
        onChange(dayOf(at));
    };
    return (
        <div class="day">
            <span class="label">{label}</span>
            <button class="iconbtn" type="button" aria-label="Previous day" onClick={() => step(-1)}>
                ‹
            </button>
            <input
                class="input"
                type="date"
                aria-label={label}
                value={value ?? ''}
                onInput={(e) => onChange((e.target as HTMLInputElement).value || undefined)}
            />
            <button class="iconbtn" type="button" aria-label="Next day" onClick={() => step(1)}>
                ›
            </button>
            {value && (
                <button
                    class="iconbtn"
                    type="button"
                    aria-label="Any day"
                    title="Any day"
                    onClick={() => onChange(undefined)}
                >
                    ×
                </button>
            )}
        </div>
    );
}

/** Filters as a change writes them: a key set to nothing is a filter taken off. */
type Loose = { [K in keyof WorkItemFilters]?: WorkItemFilters[K] | undefined };

/** The filters with every empty key gone, so one view has one address. */
function without(filters: Loose): WorkItemFilters {
    return Object.fromEntries(
        Object.entries(filters).filter(([, v]) => v !== undefined && v !== null && v !== ''),
    ) as WorkItemFilters;
}
