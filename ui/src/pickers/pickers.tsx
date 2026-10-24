import type { RefObject } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import { Unpaired } from '../api/client';
import { session } from '../api/session';

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

export type Option = { value: string | null; label: string };

/** A short list to pick one from, drawn by the page so it follows the page's theme. */
export function Picker({
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

export type Found = { value: string; label: string; hint?: string | undefined };

/**
 * A list too long to show whole: it opens on the first few, and what the person types asks the runtime for the ones
 * that match. Picking the first entry clears the filter.
 */
export function SearchPicker({
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

/**
 * A long list to choose several from: the same search as SearchPicker's, each entry a box that ticks and unticks, the
 * list kept open while the person ticks; Escape or a click outside closes it.
 */
export function MultiSearchPicker({
    label,
    set,
    placeholder,
    search,
    chosen,
    max,
    onToggle,
    opener,
}: {
    label: string;
    set: boolean;
    placeholder: string;
    search: (query: string) => Promise<Found[]>;
    chosen: readonly string[];
    max: number;
    onToggle: (entry: Found) => void;
    /** The button that opens the list, for a caller that hands the keyboard back to it. */
    opener?: RefObject<HTMLButtonElement | null>;
}) {
    const [open, setOpen] = useState(false);
    const [text, setText] = useState('');
    const [found, setFound] = useState<Found[] | null>(null);
    const [active, setActive] = useState(0);
    const box = useRef<HTMLDivElement>(null);
    const own = useRef<HTMLButtonElement>(null);
    const button = opener ?? own;
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
                    setActive(0);
                },
                (error: unknown) => {
                    if (error instanceof Unpaired) session.clear();
                    else if (current) setFound([]);
                },
            );
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
    const entries = found ?? [];
    const full = (entry: Found) => !chosen.includes(entry.value) && chosen.length >= max;
    const toggle = (at: number) => {
        const entry = entries[at];
        if (!entry || full(entry)) return;
        onToggle(entry);
        field.current?.focus();
    };

    return (
        <div class="picker" ref={box}>
            <button
                ref={button}
                type="button"
                class={`select${set ? ' set' : ''}`}
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
                        onKeyDown={listKeys(entries.length, active, setActive, toggle, close)}
                    />
                    <div role="listbox" aria-multiselectable="true">
                        {entries.map((entry, at) => {
                            const on = chosen.includes(entry.value);
                            return (
                                <button
                                    key={entry.value}
                                    type="button"
                                    role="option"
                                    tabIndex={-1}
                                    class={at === active ? 'active' : ''}
                                    aria-selected={on}
                                    disabled={full(entry)}
                                    title={full(entry) ? `At most ${max} campaigns` : undefined}
                                    onMouseEnter={() => setActive(at)}
                                    onClick={() => toggle(at)}
                                >
                                    <span class="name">
                                        <span class={`box${on ? ' on' : ''}`} aria-hidden="true" />
                                        {entry.label}
                                    </span>
                                    {entry.hint && <span class="hint">{entry.hint}</span>}
                                </button>
                            );
                        })}
                        {found?.length === 0 && <div class="none">Nothing matches</div>}
                    </div>
                </div>
            )}
        </div>
    );
}
