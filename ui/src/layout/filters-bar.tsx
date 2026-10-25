import type { ComponentChildren } from 'preact';

/** The row under the toolbar where a screen narrows what it lists: search, pickers, choices. */
export function FiltersBar({ children }: { children: ComponentChildren }) {
    return <div class="filters">{children}</div>;
}
