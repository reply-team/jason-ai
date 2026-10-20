import { useEffect, useRef, useState } from 'preact/hooks';
import { Unpaired } from '../../api/client';
import { useApi } from '../../api/context';
import { session } from '../../api/session';
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

/** Closes a popup when the person clicks anywhere outside it. */
function useOutsideClick(open: boolean, box: { current: HTMLElement | null }, close: () => void): void {
    useEffect(() => {
        if (!open) return;
        const outside = (event: MouseEvent) => {
            if (!box.current?.contains(event.target as Node)) close();
        };
        document.addEventListener('mousedown', outside);
        return () => document.removeEventListener('mousedown', outside);
    }, [open]);
}

/**
 * The keys of an open list: the arrows move the highlight, staying inside the list; Enter picks the highlighted entry;
 * Escape closes the list. Anything else is left to the field it was typed in.
 */
function listKeys(
    count: number,
    active: number,
    move: (to: number) => void,
    pick: (at: number) => void,
    close: () => void,
) {
    return (event: KeyboardEvent) => {
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            if (count > 0) move(Math.min(count - 1, Math.max(0, active + (event.key === 'ArrowDown' ? 1 : -1))));
        } else if (event.key === 'Enter') {
            event.preventDefault();
            if (active >= 0 && active < count) pick(active);
        } else if (event.key === 'Escape') {
            event.preventDefault();
            close();
        }
    };
}

type Option = { value: string | null; label: string };

/** A short list to pick one from, drawn by the page so it follows the page's theme. */
function Picker({
    label,
    options,
    onPick,
}: {
    label: string;
    options: Option[];
    onPick: (value: string | null) => void;
}) {
    const [open, setOpen] = useState(false);
    const [active, setActive] = useState(0);
    const box = useRef<HTMLDivElement>(null);
    const button = useRef<HTMLButtonElement>(null);
    const list = useRef<HTMLDivElement>(null);
    useOutsideClick(open, box, () => setOpen(false));
    useEffect(() => {
        if (open) list.current?.focus();
    }, [open]);

    const close = () => {
        setOpen(false);
        button.current?.focus();
    };
    const pick = (at: number) => {
        close();
        onPick(options[at]!.value);
    };

    return (
        <div class="picker" ref={box}>
            <button
                ref={button}
                type="button"
                class={`select${options[0]?.label === label ? '' : ' set'}`}
                aria-haspopup="listbox"
                aria-expanded={open}
                onClick={() => {
                    if (open) return close();
                    setActive(
                        Math.max(
                            0,
                            options.findIndex((o) => o.label === label),
                        ),
                    );
                    setOpen(true);
                }}
            >
                {label}
                <span class="caret" aria-hidden="true">
                    ▾
                </span>
            </button>
            {open && (
                <div
                    ref={list}
                    class="menu"
                    role="listbox"
                    tabIndex={-1}
                    onKeyDown={listKeys(options.length, active, setActive, pick, close)}
                >
                    {options.map((option, at) => (
                        <button
                            key={option.value ?? ''}
                            type="button"
                            role="option"
                            tabIndex={-1}
                            class={at === active ? 'active' : ''}
                            aria-selected={option.label === label}
                            onMouseEnter={() => setActive(at)}
                            onClick={() => pick(at)}
                        >
                            {option.label}
                        </button>
                    ))}
                </div>
            )}
        </div>
    );
}

type Found = { value: string; label: string; hint?: string | undefined };

/**
 * A list too long to show whole: it opens on the first few, and what the person types asks the runtime for the ones
 * that match. Picking the first entry clears the filter.
 */
function SearchPicker({
    label,
    clearLabel,
    placeholder,
    search,
    onPick,
}: {
    label: string;
    clearLabel: string;
    placeholder: string;
    search: (query: string) => Promise<Found[]>;
    onPick: (picked: Found | null) => void;
}) {
    const [open, setOpen] = useState(false);
    const [text, setText] = useState('');
    const [found, setFound] = useState<Found[] | null>(null);
    const [active, setActive] = useState(0);
    const box = useRef<HTMLDivElement>(null);
    const button = useRef<HTMLButtonElement>(null);
    const field = useRef<HTMLInputElement>(null);
    useOutsideClick(open, box, () => close(false));

    useEffect(() => {
        if (!open) return;
        field.current?.focus();
        const query = text.trim();
        let current = true;
        const ask = () =>
            void search(query).then(
                (results) => {
                    if (!current) return;
                    setFound(results);
                    // Typed, the first match is the one Enter picks; opened, the entry already chosen.
                    const chosen = results.findIndex((r) => r.label === label);
                    setActive(query ? (results.length > 0 ? 1 : 0) : chosen >= 0 ? chosen + 1 : 0);
                },
                (error: unknown) => {
                    if (error instanceof Unpaired) session.clear();
                    else if (current) setFound([]);
                },
            );
        // Opened, the first few come at once; typed, the runtime is asked once the person pauses.
        const timer = query ? setTimeout(ask, 250) : (ask(), undefined);
        return () => {
            current = false;
            if (timer) clearTimeout(timer);
        };
    }, [open, text]);

    const close = (refocus = true) => {
        setOpen(false);
        setText('');
        setFound(null);
        if (refocus) button.current?.focus();
    };
    const entries: (Found | null)[] = [null, ...(found ?? [])];
    const pick = (at: number) => {
        close();
        onPick(entries[at] ?? null);
    };

    return (
        <div class="picker" ref={box}>
            <button
                ref={button}
                type="button"
                class={`select${label === clearLabel ? '' : ' set'}`}
                aria-haspopup="listbox"
                aria-expanded={open}
                onClick={() => (open ? close() : setOpen(true))}
            >
                {label}
                <span class="caret" aria-hidden="true">
                    ▾
                </span>
            </button>
            {open && (
                <div class="menu">
                    <input
                        ref={field}
                        class="search"
                        type="search"
                        placeholder={placeholder}
                        aria-label={placeholder}
                        value={text}
                        onInput={(e) => setText((e.target as HTMLInputElement).value)}
                        onKeyDown={listKeys(entries.length, active, setActive, pick, close)}
                    />
                    <div role="listbox">
                        {entries.map((entry, at) => (
                            <button
                                key={entry?.value ?? ''}
                                type="button"
                                role="option"
                                tabIndex={-1}
                                class={at === active ? 'active' : ''}
                                aria-selected={(entry?.label ?? clearLabel) === label}
                                onMouseEnter={() => setActive(at)}
                                onClick={() => pick(at)}
                            >
                                <span class="name">{entry?.label ?? clearLabel}</span>
                                {entry?.hint && <span class="hint">{entry.hint}</span>}
                            </button>
                        ))}
                        {found?.length === 0 && <div class="none">Nothing matches</div>}
                    </div>
                </div>
            )}
        </div>
    );
}
