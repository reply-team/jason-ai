import { useEffect, useRef, useState } from 'preact/hooks';

/** The search box: what is typed reaches the list once the person pauses, and only from two characters on. */
export function Search({
    value,
    label,
    placeholder,
    onSearch,
}: {
    value: string;
    label: string;
    placeholder: string;
    onSearch: (q: string) => void;
}) {
    const [text, setText] = useState(value);
    // The address changed from outside (Back, a link): the box shows it. Not on mounting, which would erase what was
    // typed before the first effects ran.
    const mounted = useRef(false);
    useEffect(() => {
        if (mounted.current) setText(value);
        mounted.current = true;
    }, [value]);
    // The search runs after a pause, so it calls the latest onSearch: a view or a filter changed meanwhile stays changed.
    const latest = useRef(onSearch);
    latest.current = onSearch;
    useEffect(() => {
        const query = text.trim();
        if (query === value || (query.length > 0 && query.length < 2)) return;
        const timer = setTimeout(() => latest.current(query), 250);
        return () => clearTimeout(timer);
    }, [text]);
    return (
        <input
            class="search"
            type="search"
            aria-label={label}
            placeholder={placeholder}
            value={text}
            onInput={(e) => setText((e.target as HTMLInputElement).value)}
        />
    );
}
