import type { ReactNode } from 'react';

/** The row under the toolbar where a screen narrows what it lists: search, pickers, choices. */
export function FiltersBar({ children }: { children: ReactNode }) {
    return <div className="filters">{children}</div>;
}
