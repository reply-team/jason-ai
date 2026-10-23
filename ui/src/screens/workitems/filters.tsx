import { FiltersBar } from '../../layout/filters-bar';
import { Segmented } from '../../layout/segmented';
import { Picker } from '../../pickers/pickers';
import { CampaignPicker } from '../../pickers/campaign-picker';
import { PersonPicker } from '../../pickers/person-picker';
import { replace, type Tab, type WorkItemFilters } from '../../router';
import * as store from './store';

type Props = { tab: Tab; filters: WorkItemFilters };

/** The filters every tab shares; a change narrows the same view, so it replaces the address rather than adding to history. */
export function Filters({ tab, filters }: Props) {
    const set = (next: Loose) => replace({ screen: 'workitems', tab, filters: without(next) });
    const kind = filters.kind;
    const campaign = store.campaign.value?.id === filters.campaign ? store.campaign.value : null;
    const person = store.person.value?.id === filters.contact ? store.person.value : null;

    return (
        <FiltersBar>
            <CampaignPicker
                label={filters.campaign ? (campaign?.name ?? '…') : 'All campaigns'}
                none="All campaigns"
                onPick={(picked) => {
                    if (picked) store.campaign.value = picked;
                    set({ ...filters, campaign: picked?.id });
                }}
            />
            <PersonPicker
                label={filters.contact ? (person?.name ?? '…') : 'Everybody'}
                none="Everybody"
                onPick={(picked) => {
                    if (picked) store.person.value = picked;
                    set({ ...filters, contact: picked?.id });
                }}
            />
            <Segmented<WorkItemFilters['kind']>
                label="Kind"
                options={[
                    { value: undefined, label: 'All' },
                    { value: 'ai_role', label: 'Role work' },
                    { value: 'provider_op', label: 'Provider operations' },
                ]}
                value={kind}
                onChange={(value) =>
                    set({ ...filters, kind: value, role: value === 'provider_op' ? undefined : filters.role })
                }
            />
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
        </FiltersBar>
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
