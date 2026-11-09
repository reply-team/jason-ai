import { Children, type ReactNode } from 'react';

/** The row of what a panel's subject can be asked to do; a panel with nothing to offer has no row at all. */
export function ActionsRow({ children }: { children?: ReactNode }) {
    if (Children.toArray(children).length === 0) return null;
    return <div className="panel-actions">{children}</div>;
}
